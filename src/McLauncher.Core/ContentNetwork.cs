using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;

namespace McLauncher.Core;

public static class ContentNetwork
{
    public static HttpClient CreateHttpClient(bool useSystemProxy = true, Action<string>? log = null)
    {
        var primary = CreateHandler(useSystemProxy, SslProtocols.None);
        var compatible = CreateHandler(useSystemProxy, SslProtocols.Tls12);
        return new HttpClient(new ContentRetryHandler(primary, compatible, log))
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    private static SocketsHttpHandler CreateHandler(bool useProxy, SslProtocols protocols) => new()
    {
        UseProxy = useProxy,
        ConnectTimeout = TimeSpan.FromSeconds(12),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        SslOptions = new SslClientAuthenticationOptions { EnabledSslProtocols = protocols }
        // Certificate validation remains the platform default in both modes.
    };
}

/// <summary>Retries only bodyless GET requests; certificate validation is never disabled.</summary>
public sealed class ContentRetryHandler : HttpMessageHandler
{
    private readonly HttpMessageInvoker _primary;
    private readonly HttpMessageInvoker _compatible;
    private readonly Action<string>? _log;
    private readonly TimeSpan _retryDelay;
    private const int MaxAttempts = 3;

    public ContentRetryHandler(HttpMessageHandler primary, HttpMessageHandler compatible,
        Action<string>? log = null, TimeSpan? retryDelay = null)
    {
        _primary = new(primary);
        _compatible = new(compatible);
        _log = log;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.Content is not null)
            return await _primary.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var useCompatible = false;
        var host = request.RequestUri?.Host ?? "平台";
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var copy = CopyRequest(request, useCompatible);
            TimeSpan delay;
            try
            {
                var response = await (useCompatible ? _compatible : _primary).SendAsync(copy, cancellationToken).ConfigureAwait(false);
                if (attempt == MaxAttempts || !IsTransientStatus(response.StatusCode)) return response;

                var retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
                // Respect a long Retry-After without blocking the UI operation for an extended period.
                if (retryAfter > TimeSpan.FromSeconds(5)) return response;
                delay = retryAfter is { } wait && wait > TimeSpan.Zero ? wait : _retryDelay * attempt;
                _log?.Invoke($"{host} 返回 HTTP {(int)response.StatusCode}，准备重试（{attempt + 1}/{MaxAttempts}）");
                response.Dispose();
            }
            catch (HttpRequestException exception) when (IsTransientConnection(exception))
            {
                if (attempt == MaxAttempts)
                {
                    var reason = exception.GetBaseException().Message;
                    throw new HttpRequestException(exception.HttpRequestError,
                        $"连接 {host} 失败，已尝试 {MaxAttempts} 次。请在下载页“网络设置”切换系统代理/直连，或检查代理是否正常。具体原因：{reason}",
                        exception, exception.StatusCode);
                }
                useCompatible |= exception.HttpRequestError == HttpRequestError.SecureConnectionError
                    || HasAuthenticationFailure(exception);
                _log?.Invoke($"{host} 连接失败：{exception.GetBaseException().Message}；"
                    + (useCompatible ? "改用 TLS 1.2 / HTTP 1.1 兼容连接，" : "")
                    + $"准备重试（{attempt + 1}/{MaxAttempts}）");
                delay = _retryDelay * attempt;
            }
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransientConnection(HttpRequestException exception) =>
        exception.HttpRequestError is HttpRequestError.SecureConnectionError or HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError or HttpRequestError.ProxyTunnelError or HttpRequestError.ResponseEnded
        || (exception.StatusCode is null && exception.InnerException is IOException or AuthenticationException);

    private static bool HasAuthenticationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is AuthenticationException) return true;
        return false;
    }

    private static bool IsTransientStatus(HttpStatusCode code) => code is HttpStatusCode.RequestTimeout
        or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
        or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static HttpRequestMessage CopyRequest(HttpRequestMessage request, bool compatible)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = compatible ? HttpVersion.Version11 : request.Version,
            VersionPolicy = compatible ? HttpVersionPolicy.RequestVersionOrLower : request.VersionPolicy
        };
        foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _primary.Dispose(); _compatible.Dispose(); }
        base.Dispose(disposing);
    }
}
