using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McLauncher.Core;

internal static class ContentTests
{
    public static void CatalogRequests() => CatalogRequestsAsync().GetAwaiter().GetResult();
    public static void ChineseSearch() => ChineseSearchAsync().GetAwaiter().GetResult();
    public static void FileLists() => FileListsAsync().GetAwaiter().GetResult();
    public static void DownloadIntegrity() => DownloadIntegrityAsync().GetAwaiter().GetResult();
    public static void DownloadSafety() => DownloadSafetyAsync().GetAwaiter().GetResult();

    private static async Task ChineseSearchAsync()
    {
        var cases = new[] { ("苹果皮", "AppleSkin"), ("锂", "Lithium"), ("实体渲染机制优化", "Entity Culling"),
            ("蘋果皮", "AppleSkin"), ("實體渲染機制優化", "Entity Culling"), ("JEI", "Just Enough Items"),
            (" 苹果皮 模组 ", "AppleSkin"), ("实体渲染机制", "Entity Culling"), ("锂电池", "锂电池"),
            ("没有收录的模组", "没有收录的模组"), ("fabric-language-kotlin", "fabric-language-kotlin") };
        foreach (var (input, expected) in cases)
        {
            Equal(expected, ModSearchNames.ResolveQuery(ContentKind.Mod, input));
            foreach (var platform in Enum.GetValues<ContentPlatform>())
            {
                using var handler = new StubHandler(request =>
                {
                    var query = ParseQuery(request.RequestUri!);
                    Equal(expected, query[platform == ContentPlatform.Modrinth ? "query" : "searchFilter"]);
                    if (platform == ContentPlatform.Modrinth)
                    {
                        Equal("relevance", query["index"]);
                        Equal("20", query["offset"]);
                        using var facets = JsonDocument.Parse(query["facets"]);
                        Equal("versions:1.20.1", facets.RootElement[1][0].GetString());
                        Equal("categories:fabric", facets.RootElement[2][0].GetString());
                        return JsonResponse("""{"hits":[],"total_hits":0}""");
                    }
                    Equal(false, query.ContainsKey("sortField"));
                    Equal("1.20.1", query["gameVersion"]);
                    Equal("4", query["modLoaderType"]);
                    Equal("20", query["index"]);
                    return JsonResponse("""{"data":[],"pagination":{"totalCount":0}}""");
                });
                using var http = new HttpClient(handler);
                await new ContentCatalogClient(http).SearchAsync(new(platform, ContentKind.Mod, input, "1.20.1", "fabric", 20), "secret", default);
            }
        }
        Equal("苹果皮", ModSearchNames.ResolveQuery(ContentKind.ResourcePack, "苹果皮"));
        Equal("实体渲染机制优化", ModSearchNames.GetChineseName("EntityCulling"));
        Equal("苹果皮", ModSearchNames.GetChineseName("AppleSkin"));
        Equal("锂", ModSearchNames.GetChineseName("Lithium"));
        Equal<string?>(null, ModSearchNames.GetChineseName("AppleSkin Extra"));
        Equal("锂 · Lithium", new ContentProject(ContentPlatform.Modrinth, ContentKind.Mod, "test", "Lithium", "", "", 0, "").DisplayTitle);

        using var emptyHandler = new StubHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            if (request.RequestUri!.Host == "api.modrinth.com")
            {
                Equal("downloads", query["index"]);
                return JsonResponse("""{"hits":[],"total_hits":0}""");
            }
            Equal("6", query["sortField"]);
            return JsonResponse("""{"data":[],"pagination":{"totalCount":0}}""");
        });
        using var emptyHttp = new HttpClient(emptyHandler);
        foreach (var platform in Enum.GetValues<ContentPlatform>())
            await new ContentCatalogClient(emptyHttp).SearchAsync(new(platform, ContentKind.Mod), "secret", default);
    }

    public static async Task LiveChineseSearchAsync()
    {
        using var http = ContentNetwork.CreateHttpClient(log: Console.WriteLine);
        var client = new ContentCatalogClient(http);
        foreach (var (keyword, title) in new[] { ("苹果皮", "AppleSkin"), ("锂", "Lithium"), ("实体渲染机制优化", "Entity Culling") })
        {
            var page = await client.SearchAsync(new(ContentPlatform.Modrinth, ContentKind.Mod, keyword), null, default);
            if (!page.Items.Take(3).Any(project => project.Title.Equals(title, StringComparison.OrdinalIgnoreCase)))
                throw new Exception($"中文搜索未在前三项找到 {title}：{keyword}");
            var filtered = await client.SearchAsync(new(ContentPlatform.Modrinth, ContentKind.Mod, keyword, "1.20.1", "fabric"), null, default);
            var project = filtered.Items.FirstOrDefault(project => project.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"中文搜索与版本/加载器筛选未找到 {title}");
            var files = await client.GetFilesAsync(project, "1.20.1", "fabric", 0, 20, null, default);
            if (files.Items.Count == 0) throw new Exception($"{title} 未返回兼容的可下载文件。");
            Console.WriteLine($"PASS 中文在线搜索 {keyword} → {title}，含 1.20.1 / Fabric 文件列表");
        }
    }

    private static async Task CatalogRequestsAsync()
    {
        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            var type = kind switch { ContentKind.Mod => "mod", ContentKind.Modpack => "modpack", ContentKind.Shader => "shader", _ => "resourcepack" };
            using var handler = new StubHandler(request =>
            {
                var query = ParseQuery(request.RequestUri!);
                Equal("sodium & 中文", query["query"]);
                Equal("20", query["offset"]);
                Equal("20", query["limit"]);
                using var facets = JsonDocument.Parse(query["facets"]);
                Equal("project_type:" + type, facets.RootElement[0][0].GetString());
                Equal("versions:1.20.1", facets.RootElement[1][0].GetString());
                Equal("categories:fabric", facets.RootElement[2][0].GetString());
                if (request.Headers.Contains("x-api-key")) throw new Exception("CurseForge Key 不应发送给 Modrinth。");
                if (!request.Headers.UserAgent.Any()) throw new Exception("缺少 User-Agent。");
                return JsonResponse("""
                    {"hits":[{"project_id":"abc","slug":"test","title":"测试","description":"说明","author":"Alice","downloads":123}],"total_hits":42}
                    """);
            });
            using var http = new HttpClient(handler);
            var page = await new ContentCatalogClient(http).SearchAsync(new(ContentPlatform.Modrinth, kind, "sodium & 中文", "1.20.1", "fabric", 20), "secret", default);
            Equal("测试", page.Items[0].Title);
            Equal($"https://modrinth.com/{type}/test", page.Items[0].WebsiteUrl);
            Equal(true, page.HasNext);
        }

        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            using var handler = new StubHandler(request =>
            {
                var query = ParseQuery(request.RequestUri!);
                Equal(kind switch { ContentKind.Mod => "6", ContentKind.Modpack => "4471", ContentKind.Shader => "6552", _ => "12" }, query["classId"]);
                Equal("432", query["gameId"]);
                Equal("6", query["modLoaderType"]);
                Equal("1.21.1", query["gameVersion"]);
                Equal("secret", request.Headers.GetValues("x-api-key").Single());
                return JsonResponse("""
                    {"data":[{"id":17,"name":"CF Test","summary":"说明","authors":[{"name":"Bob"}],"downloadCount":7,"links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/test"}}],"pagination":{"totalCount":1}}
                    """);
            });
            using var http = new HttpClient(handler);
            var client = new ContentCatalogClient(http);
            await ThrowsAsync<InvalidOperationException>(() => client.SearchAsync(new(ContentPlatform.CurseForge, kind), null, default));
            Equal(0, handler.Calls);
            var page = await client.SearchAsync(new(ContentPlatform.CurseForge, kind, "", "1.21.1", "neoforge"), "secret", default);
            Equal("17", page.Items[0].Id);
            Equal("Bob", page.Items[0].Author);
            Equal(false, page.HasNext);
        }
        using var rateHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var rateHttp = new HttpClient(rateHandler);
        await ThrowsAsync<HttpRequestException>(() => new ContentCatalogClient(rateHttp).SearchAsync(new(ContentPlatform.Modrinth, ContentKind.Mod), null, default));
    }

    private static async Task FileListsAsync()
    {
        using var handler = new StubHandler(request =>
        {
            var query = ParseQuery(request.RequestUri!);
            Equal("[\"1.20.1\"]", query["game_versions"]);
            Equal("[\"fabric\"]", query["loaders"]);
            return JsonResponse("""
                [
                  {"id":"old","name":"Old","version_type":"release","date_published":"2024-01-01T00:00:00Z","game_versions":["1.20.1"],"loaders":["fabric"],"files":[{"filename":"old.jar","url":"https://cdn.modrinth.com/old.jar","size":3,"primary":true,"hashes":{"sha512":"abc","sha1":"def"}}]},
                  {"id":"new","name":"New","version_type":"beta","date_published":"2025-01-01T00:00:00Z","game_versions":["1.20.1"],"loaders":["fabric"],"files":[{"filename":"extra.jar","url":"https://cdn.modrinth.com/extra.jar","size":3,"primary":false,"hashes":{"sha512":"abc"}},{"filename":"main.jar","url":"https://cdn.modrinth.com/main.jar","size":3,"primary":true,"hashes":{"sha512":"abc"}}]}
                ]
                """);
        });
        using var http = new HttpClient(handler);
        var client = new ContentCatalogClient(http);
        var project = new ContentProject(ContentPlatform.Modrinth, ContentKind.Mod, "test", "Test", "", "", 0, "https://modrinth.com/mod/test");
        var first = await client.GetFilesAsync(project, "1.20.1", "fabric", 0, 2, null, default);
        Equal(3L, first.Total);
        Equal("main.jar", first.Items[0].FileName);
        Equal("extra.jar", first.Items[1].FileName);
        Equal(true, first.HasNext);
        var second = await client.GetFilesAsync(project, "1.20.1", "fabric", 2, 2, null, default);
        Equal("def", second.Items[0].Sha1);
        Equal(false, second.HasNext);

        using var cfHandler = new StubHandler(request =>
        {
            Equal("20", ParseQuery(request.RequestUri!)["index"]);
            Equal("4", ParseQuery(request.RequestUri!)["modLoaderType"]);
            return JsonResponse("""
                {"data":[{"id":99,"displayName":"Blocked","fileName":"blocked.jar","downloadUrl":null,"fileLength":3,"gameVersions":["1.20.1","Fabric"],"releaseType":1,"fileDate":"2025-01-01T00:00:00Z","isAvailable":true,"hashes":[{"algo":1,"value":"def"}]}],"pagination":{"totalCount":21}}
                """);
        });
        using var cfHttp = new HttpClient(cfHandler);
        var cfPage = await new ContentCatalogClient(cfHttp).GetFilesAsync(project with { Platform = ContentPlatform.CurseForge, Id = "17", WebsiteUrl = "https://www.curseforge.com/minecraft/mc-mods/test" }, "1.20.1", "fabric", 20, 20, "secret", default);
        Equal(true, string.IsNullOrWhiteSpace(cfPage.Items[0].DownloadUrl));
        Equal("https://www.curseforge.com/minecraft/mc-mods/test/files/99", cfPage.Items[0].WebsiteUrl);
        Equal("def", cfPage.Items[0].Sha1);
        Equal(false, cfPage.HasNext);
    }

    private static async Task DownloadIntegrityAsync()
    {
        var root = CreateTestDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("Minecraft content test");
            using var handler = new StubHandler(request =>
            {
                if (request.Headers.Contains("x-api-key")) throw new Exception("API Key 不应发送到下载 CDN。");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            });
            using var http = new HttpClient(handler);
            var downloader = new ContentDownloader(http);
            var file = TestFile("content.jar", bytes.Length) with { Sha512 = Convert.ToHexString(SHA512.HashData(bytes)), Sha1 = Convert.ToHexString(SHA1.HashData(bytes)) };
            var path = await downloader.DownloadAsync(file, root.FullName, null, default);
            Equal(Convert.ToHexString(bytes), Convert.ToHexString(await File.ReadAllBytesAsync(path)));
            await ThrowsAsync<IOException>(() => downloader.DownloadAsync(file, root.FullName, null, default));
            Equal(1, handler.Calls);
            await ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(file with { FileName = "bad.jar", Sha1 = "00" }, root.FullName, null, default));
            Equal(false, File.Exists(Path.Combine(root.FullName, "bad.jar")));
            await ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(file with { FileName = "truncated.jar", Size = bytes.Length + 1 }, root.FullName, null, default));
            Equal(false, File.Exists(Path.Combine(root.FullName, "truncated.jar")));
            Equal(0, Directory.GetFiles(root.FullName, "*.part").Length);
        }
        finally { root.Delete(true); }
    }

    private static async Task DownloadSafetyAsync()
    {
        var root = CreateTestDirectory();
        try
        {
            using var cancel = new CancellationTokenSource();
            using var handler = new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new CancelStream(cancel)) });
            using var http = new HttpClient(handler);
            var downloader = new ContentDownloader(http);
            foreach (var name in new[] { "../outside.jar", "..\\outside.jar", "C:\\outside.jar", "mod.jar:evil", "CON.jar", "mod.jar.", "mod.jar " })
                await ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(TestFile(name, 4), root.FullName, null, default));
            await ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(TestFile("x.jar", 4) with { DownloadUrl = null }, root.FullName, null, default));
            await ThrowsAsync<InvalidOperationException>(() => downloader.DownloadAsync(TestFile("x.jar", 4) with { DownloadUrl = "http://example.com/x.jar" }, root.FullName, null, default));
            Equal(0, handler.Calls);
            await ThrowsAsync<OperationCanceledException>(() => downloader.DownloadAsync(TestFile("cancel.jar", 4), root.FullName, null, cancel.Token));
            Equal(0, Directory.GetFiles(root.FullName).Length);
            foreach (var (kind, folder) in new[] { (ContentKind.Mod, "mods"), (ContentKind.Modpack, "modpacks"), (ContentKind.Shader, "shaderpacks"), (ContentKind.ResourcePack, "resourcepacks") })
                Equal(Path.Combine(root.FullName, folder), ContentDownloader.GetDestinationDirectory(root.FullName, kind));
        }
        finally { root.Delete(true); }
    }

    public static async Task LiveModrinthAsync()
    {
        using var http = ContentNetwork.CreateHttpClient(log: Console.WriteLine);
        var client = new ContentCatalogClient(http);
        ContentFile? download = null;
        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            var page = await client.SearchAsync(new(ContentPlatform.Modrinth, kind), null, default);
            if (page.Items.Count == 0) throw new Exception("在线搜索未返回资源：" + kind);
            var files = await client.GetFilesAsync(page.Items[0], "", "", 0, 20, null, default);
            if (files.Items.Count == 0) throw new Exception("在线版本列表为空：" + kind);
            if (kind == ContentKind.Mod) download = files.Items.First();
            Console.WriteLine($"PASS Modrinth 在线 {kind} 搜索与版本列表");
        }
        var root = CreateTestDirectory();
        try
        {
            if (download is null || download.Size > 20 * 1024 * 1024) throw new Exception("在线下载测试文件不可用或过大。");
            await new ContentDownloader(http).DownloadAsync(download, root.FullName, null, default);
            Console.WriteLine("PASS Modrinth CDN 实际下载与哈希校验");
        }
        finally { root.Delete(true); }
    }

    private static DirectoryInfo CreateTestDirectory() => Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, "artifacts", "tests", Guid.NewGuid().ToString("N")));
    private static ContentFile TestFile(string name, long size) => new("v1", "Test", name, "https://cdn.modrinth.com/test.jar", size, "1.20.1", "fabric", "release", DateTimeOffset.UtcNow, "https://modrinth.com/mod/test/version/v1");
    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static Dictionary<string, string> ParseQuery(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"预期 {expected}，实际 {actual}。");
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("预期异常：" + typeof(T).Name);
    }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
    private sealed class CancelStream(CancellationTokenSource cancel) : MemoryStream(new byte[] { 1, 2, 3, 4 })
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) { cancel.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            _read = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }
    }
}
