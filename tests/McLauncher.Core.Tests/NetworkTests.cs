using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using McLauncher.Core;

internal static class NetworkTests
{
    public static void TlsRecovery() => TlsRecoveryAsync().GetAwaiter().GetResult();
    public static void RetryLimits() => RetryLimitsAsync().GetAwaiter().GetResult();
    public static void StatusAndCancellation() => StatusAndCancellationAsync().GetAwaiter().GetResult();

    private static async Task TlsRecoveryAsync()
    {
        var messages = new List<string>();
        using var primary = new Stub((_, _) => throw SslFailure());
        using var compatible = new Stub((request, _) =>
        {
            Require(request.Version == HttpVersion.Version11, "兼容连接必须使用 HTTP 1.1。");
            Require(request.Headers.GetValues("x-api-key").Single() == "session-secret", "重试需保留认证。");
            Require(request.RequestUri!.Query == "?query=AppleSkin", "重试需保留查询参数。");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var http = new HttpClient(new ContentRetryHandler(primary, compatible, messages.Add, TimeSpan.Zero));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.curseforge.com/search?query=AppleSkin");
        request.Headers.Add("x-api-key", "session-secret");
        using var response = await http.SendAsync(request);
        Require(response.StatusCode == HttpStatusCode.OK && primary.Calls == 1 && compatible.Calls == 1, "TLS 失败后应切换兼容连接并恢复。");
        Require(messages.Any(message => message.Contains("TLS 1.2", StringComparison.Ordinal)), "日志需说明兼容重试。");
        Require(messages.All(message => !message.Contains("session-secret", StringComparison.Ordinal)), "日志不能泄露 Key。");
    }

    private static async Task RetryLimitsAsync()
    {
        using var primary = new Stub((_, _) => throw SslFailure());
        using var compatible = new Stub((_, _) => throw SslFailure());
        using var http = new HttpClient(new ContentRetryHandler(primary, compatible, retryDelay: TimeSpan.Zero));
        try
        {
            using var response = await http.GetAsync("https://api.modrinth.com/v2/search");
            throw new Exception("持续 TLS 故障应报错。");
        }
        catch (HttpRequestException exception)
        {
            Require(primary.Calls == 1 && compatible.Calls == 2, "最多尝试三次。");
            Require(exception.Message.Contains("api.modrinth.com", StringComparison.Ordinal)
                && exception.Message.Contains("unexpected EOF", StringComparison.Ordinal)
                && exception.Message.Contains("系统代理/直连", StringComparison.Ordinal), "错误需包含域名、底层原因及操作建议。");
        }

        using var transient = new Stub((_, call) => call == 1
            ? throw new HttpRequestException(HttpRequestError.ConnectionError, "reset")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var unusedFallback = new Stub((_, _) => throw new Exception("普通连接错误不应强制切换 TLS。"));
        using var transientHttp = new HttpClient(new ContentRetryHandler(transient, unusedFallback, retryDelay: TimeSpan.Zero));
        using var restored = await transientHttp.GetAsync("https://api.modrinth.com/v2/search");
        Require(restored.IsSuccessStatusCode && transient.Calls == 2 && unusedFallback.Calls == 0, "普通临时错误应可重试恢复。");
    }

    private static async Task StatusAndCancellationAsync()
    {
        var failedBody = new TrackedContent();
        using var statuses = new Stub((_, call) => Task.FromResult(new HttpResponseMessage(
            call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = call == 1 ? failedBody : new StringContent("ok") }));
        using var unusedFallback = new Stub((_, _) => throw new Exception("不应切换 TLS。"));
        using (var http = new HttpClient(new ContentRetryHandler(statuses, unusedFallback, retryDelay: TimeSpan.Zero)))
        {
            using var response = await http.GetAsync("https://api.modrinth.com/v2/search");
            Require(response.IsSuccessStatusCode && statuses.Calls == 2 && failedBody.WasDisposed, "临时 HTTP 错误应释放响应并重试。");
        }
        foreach (var code in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests })
        {
            using var handler = new Stub((_, _) =>
            {
                var response = new HttpResponseMessage(code);
                if (code == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                return Task.FromResult(response);
            });
            using var fallback = new Stub((_, _) => throw new Exception("不应重试。"));
            using var http = new HttpClient(new ContentRetryHandler(handler, fallback, retryDelay: TimeSpan.Zero));
            using var response = await http.GetAsync("https://api.curseforge.com/v1/mods/search");
            Require(response.StatusCode == code && handler.Calls == 1, "权限错误、404 和较长 Retry-After 不应重复请求。");
        }
        using var cancellation = new CancellationTokenSource();
        using var failure = new Stub((_, _) => throw SslFailure());
        using var cancelledFallback = new Stub((_, _) => throw new Exception("取消后不应重试。"));
        using var cancelHttp = new HttpClient(new ContentRetryHandler(failure, cancelledFallback, _ => cancellation.Cancel()));
        try
        {
            using var response = await cancelHttp.GetAsync("https://api.modrinth.com/v2/search", cancellation.Token);
            throw new Exception("取消后应结束请求。");
        }
        catch (OperationCanceledException) { Require(failure.Calls == 1 && cancelledFallback.Calls == 0, "取消必须立即停止后续重试。"); }
    }

    public static async Task LiveModesAsync()
    {
        var succeeded = false;
        foreach (var useProxy in new[] { true, false })
        {
            var mode = useProxy ? "系统代理" : "直连";
            using var http = ContentNetwork.CreateHttpClient(useProxy, Console.WriteLine);
            try
            {
                var page = await new ContentCatalogClient(http).SearchAsync(new(ContentPlatform.Modrinth, ContentKind.Mod, "苹果皮"), null, default);
                Require(page.Items.Any(project => project.Title == "AppleSkin"), "没有找到 AppleSkin。");
                succeeded = true;
                Console.WriteLine($"PASS {mode} Modrinth 搜索与 TLS 校验");
            }
            catch (Exception exception) { Console.WriteLine($"INFO {mode} 当前不可用：{exception.Message}"); }
        }
        Require(succeeded, "两种网络模式均未连通。");
    }

    private static HttpRequestException SslFailure() => new(HttpRequestError.SecureConnectionError,
        "The SSL connection could not be established, see inner exception.", new AuthenticationException("TLS handshake failed", new IOException("unexpected EOF")));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Stub(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, ++Calls);
    }
    private sealed class TrackedContent() : StringContent("temporary failure")
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
