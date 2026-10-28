using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace McLauncher.Core;

/// <summary>Uses the public Modrinth v2 and CurseForge v1 APIs without scraping or mirror URLs.</summary>
public sealed class ContentCatalogClient(HttpClient httpClient)
{
    private const string ModrinthApi = "https://api.modrinth.com/v2/";
    private const string CurseForgeApi = "https://api.curseforge.com/v1/";

    public async Task<ContentPage<ContentProject>> SearchAsync(
        ContentSearch search, string? curseForgeApiKey, CancellationToken cancellationToken)
    {
        ValidatePage(search.Offset, search.PageSize);
        var keyword = ModSearchNames.ResolveQuery(search.Kind, search.Query);
        if (search.Platform == ContentPlatform.Modrinth)
        {
            var facets = new List<string[]> { new[] { "project_type:" + ModrinthType(search.Kind) } };
            if (!string.IsNullOrWhiteSpace(search.GameVersion)) facets.Add(["versions:" + search.GameVersion.Trim()]);
            if (!string.IsNullOrWhiteSpace(search.Loader)) facets.Add(["categories:" + search.Loader]);
            var url = ModrinthApi + "search?" + Query(
                ("query", keyword), ("facets", JsonSerializer.Serialize(facets)),
                ("index", keyword.Length == 0 ? "downloads" : "relevance"),
                ("offset", Number(search.Offset)), ("limit", Number(search.PageSize)));
            using var json = await GetAsync(url, null, cancellationToken);
            var root = json.RootElement;
            var items = root.GetProperty("hits").EnumerateArray().Select(item => new ContentProject(
                search.Platform, search.Kind, Text(item, "project_id"), Text(item, "title"),
                Text(item, "description"), Text(item, "author"), item.GetProperty("downloads").GetInt64(),
                $"https://modrinth.com/{ModrinthType(search.Kind)}/{Uri.EscapeDataString(Text(item, "slug"))}")).ToArray();
            return new(items, search.Offset, root.GetProperty("total_hits").GetInt64());
        }
        else
        {
            var url = CurseForgeApi + "mods/search?" + Query(
                ("gameId", "432"), ("classId", CurseForgeClass(search.Kind)), ("searchFilter", keyword),
                ("gameVersion", search.GameVersion.Trim()), ("modLoaderType", CurseForgeLoader(search.Loader)),
                ("sortField", keyword.Length == 0 ? "6" : ""), ("sortOrder", keyword.Length == 0 ? "desc" : ""),
                ("index", Number(search.Offset)), ("pageSize", Number(search.PageSize)));
            using var json = await GetAsync(url, curseForgeApiKey, cancellationToken);
            var root = json.RootElement;
            var items = root.GetProperty("data").EnumerateArray().Select(item => new ContentProject(
                search.Platform, search.Kind, item.GetProperty("id").ToString(), Text(item, "name"),
                Text(item, "summary"), string.Join(", ", item.GetProperty("authors").EnumerateArray().Select(x => Text(x, "name"))),
                item.GetProperty("downloadCount").GetInt64(), Text(item.GetProperty("links"), "websiteUrl"))).ToArray();
            return new(items, search.Offset, root.GetProperty("pagination").GetProperty("totalCount").GetInt64());
        }
    }

    public async Task<ContentPage<ContentFile>> GetFilesAsync(
        ContentProject project, string gameVersion, string loader, int offset, int pageSize,
        string? curseForgeApiKey, CancellationToken cancellationToken)
    {
        ValidatePage(offset, pageSize);
        var id = Uri.EscapeDataString(project.Id);
        if (project.Platform == ContentPlatform.Modrinth)
        {
            var url = ModrinthApi + $"project/{id}/version?" + Query(
                ("game_versions", string.IsNullOrWhiteSpace(gameVersion) ? "" : JsonSerializer.Serialize(new[] { gameVersion.Trim() })),
                ("loaders", string.IsNullOrWhiteSpace(loader) ? "" : JsonSerializer.Serialize(new[] { loader })));
            using var json = await GetAsync(url, null, cancellationToken);
            // Modrinth's version endpoint returns all versions and has no server-side pagination.
            var versions = json.RootElement.EnumerateArray();
            var total = versions.Sum(version => version.GetProperty("files").GetArrayLength());
            var items = versions.OrderByDescending(version => Date(version, "date_published"))
                .SelectMany(version => version.GetProperty("files").EnumerateArray()
                .OrderByDescending(file => file.TryGetProperty("primary", out var primary) && primary.GetBoolean())
                .Select(file => (Version: version, File: file)))
                .Skip(offset).Take(pageSize)
                .Select(item => new ContentFile(
                    Text(item.Version, "id"), Text(item.Version, "name"), Text(item.File, "filename"), Text(item.File, "url"),
                    item.File.GetProperty("size").GetInt64(), Join(item.Version, "game_versions"), Join(item.Version, "loaders"),
                    Text(item.Version, "version_type"), Date(item.Version, "date_published"),
                    project.WebsiteUrl.TrimEnd('/') + "/version/" + Uri.EscapeDataString(Text(item.Version, "id")),
                    Text(item.File.GetProperty("hashes"), "sha512"), Text(item.File.GetProperty("hashes"), "sha1")))
                .ToArray();
            return new(items, offset, total);
        }
        else
        {
            var url = CurseForgeApi + $"mods/{id}/files?" + Query(
                ("gameVersion", gameVersion.Trim()), ("modLoaderType", CurseForgeLoader(loader)),
                ("index", Number(offset)), ("pageSize", Number(pageSize)));
            using var json = await GetAsync(url, curseForgeApiKey, cancellationToken);
            var root = json.RootElement;
            var items = root.GetProperty("data").EnumerateArray()
                .Select(file => new ContentFile(
                    file.GetProperty("id").ToString(), Text(file, "displayName"), Text(file, "fileName"),
                    file.TryGetProperty("isAvailable", out var available) && !available.GetBoolean() ? null : Text(file, "downloadUrl"),
                    file.GetProperty("fileLength").GetInt64(), Join(file, "gameVersions"),
                    loader, file.GetProperty("releaseType").GetInt32() switch { 1 => "release", 2 => "beta", _ => "alpha" },
                    Date(file, "fileDate"), project.WebsiteUrl.TrimEnd('/') + "/files/" + file.GetProperty("id"),
                    Sha1: file.GetProperty("hashes").EnumerateArray()
                        .Where(hash => hash.GetProperty("algo").GetInt32() == 1)
                        .Select(hash => Text(hash, "value")).FirstOrDefault())).ToArray();
            return new(items, offset, root.GetProperty("pagination").GetProperty("totalCount").GetInt64());
        }
    }

    private async Task<JsonDocument> GetAsync(string url, string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("McLauncher/1.0 (https://github.com/baiji559/McLauncher)");
        if (url.StartsWith(CurseForgeApi, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("CurseForge 需要官方 API Key，请在下载页填写，或设置 CURSEFORGE_API_KEY 环境变量。");
            request.Headers.Add("x-api-key", apiKey.Trim());
        }
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "平台拒绝访问，请检查 API Key 或访问权限。",
                HttpStatusCode.TooManyRequests => "平台请求过于频繁，请稍后再试。",
                HttpStatusCode.NotFound => "该项目或文件已被移除。",
                _ => $"平台请求失败（HTTP {(int)response.StatusCode}）。"
            };
            throw new HttpRequestException(message);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string Query(params (string Key, string Value)[] fields) => string.Join("&", fields
        .Where(x => !string.IsNullOrWhiteSpace(x.Value))
        .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));

    private static string ModrinthType(ContentKind kind) => kind switch
    {
        ContentKind.Mod => "mod", ContentKind.Modpack => "modpack", ContentKind.Shader => "shader", _ => "resourcepack"
    };

    private static string CurseForgeClass(ContentKind kind) => kind switch
    {
        ContentKind.Mod => "6", ContentKind.Modpack => "4471", ContentKind.Shader => "6552", _ => "12"
    };

    private static string CurseForgeLoader(string loader) => loader switch
    {
        "forge" => "1", "fabric" => "4", "quilt" => "5", "neoforge" => "6", _ => ""
    };

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTimeOffset Date(JsonElement item, string name) => DateTimeOffset.Parse(Text(item, name), CultureInfo.InvariantCulture);
    private static string Join(JsonElement item, string name) => string.Join(", ", item.GetProperty(name).EnumerateArray().Select(x => x.GetString()));
    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static void ValidatePage(int offset, int pageSize)
    {
        if (offset < 0 || pageSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(offset));
    }
}
