namespace McLauncher.Core;

public enum ContentPlatform { Modrinth, CurseForge }
public enum ContentKind { Mod, Modpack, Shader, ResourcePack }

public sealed record ContentSearch(
    ContentPlatform Platform, ContentKind Kind, string Query = "", string GameVersion = "",
    string Loader = "", int Offset = 0, int PageSize = 20);

public sealed record ContentProject(
    ContentPlatform Platform, ContentKind Kind, string Id, string Title, string Description,
    string Author, long Downloads, string WebsiteUrl)
{
    public string DisplayTitle => Kind == ContentKind.Mod && ModSearchNames.GetChineseName(Title) is { } chinese
        ? $"{chinese} · {Title}" : Title;
    public string Subtitle => $"{Author} · {Downloads:N0} 次下载";
}

public sealed record ContentPage<T>(IReadOnlyList<T> Items, int Offset, int PageSize, long Total)
{
    public bool HasNext => Offset + Items.Count < Total && Items.Count > 0;
}

public sealed record ContentFile(
    string Id, string Name, string FileName, string? DownloadUrl, long Size,
    string GameVersions, string Loaders, string ReleaseType, DateTimeOffset PublishedAt,
    string WebsiteUrl, string? Sha512 = null, string? Sha1 = null)
{
    public string DisplayName => $"{Name} · {ReleaseType}";
    public string Details => $"{GameVersions} · {Loaders} · {Size / 1048576d:0.##} MB · {PublishedAt:yyyy-MM-dd}";
}

public sealed record ContentDownloadProgress(long Received, long? Total)
{
    public double Percentage => Total is > 0 ? Math.Clamp(Received * 100d / Total.Value, 0, 100) : 0;
}
