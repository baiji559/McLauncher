using System.Text.Json;

namespace McLauncher.Core;

public sealed record ModSearchName(string English, string Chinese, string[] Aliases);

/// <summary>Small, independently maintained name index; downloads still come from the selected platform.</summary>
public static class ModSearchNames
{
    private static readonly ModSearchName[] Names = Load();
    private static readonly Dictionary<string, ModSearchName> ExactNames = BuildIndex();

    public static ModSearchName? Find(string query)
    {
        var normalized = Normalize(query);
        if (normalized.Length == 0) return null;
        if (ExactNames.TryGetValue(normalized, out var exact)) return exact;
        // Accept a common pasted suffix without changing arbitrary English search terms.
        foreach (var suffix in new[] { "模组", "模組", "mod" })
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal)
                && ExactNames.TryGetValue(normalized[..^suffix.Length], out var suffixed)) return suffixed;
        }
        // Short Chinese names such as 锂/钠/玉 must match exactly; ambiguous partial names stay unchanged.
        if (normalized.Length < 2 || !ContainsChinese(normalized)) return null;
        var matches = Names.Where(name => new[] { name.Chinese }.Concat(name.Aliases)
            .Any(alias => Normalize(alias).Contains(normalized, StringComparison.Ordinal))).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string ResolveQuery(ContentKind kind, string query) =>
        kind == ContentKind.Mod ? Find(query)?.English ?? query.Trim() : query.Trim();

    public static string? GetChineseName(string title)
    {
        // Display names require an exact match; partial matching is only for user-entered queries.
        return ExactNames.TryGetValue(Normalize(title), out var name) ? name.Chinese : null;
    }

    public static bool ContainsChinese(string query) => query.Any(ch => ch is >= '\u3400' and <= '\u9fff');

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static ModSearchName[] Load()
    {
        using var stream = typeof(ModSearchNames).Assembly.GetManifestResourceStream("McLauncher.Core.mod-search-names.json")
            ?? throw new InvalidOperationException("缺少模组中文名称索引。");
        return JsonSerializer.Deserialize<ModSearchName[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("模组中文名称索引无效。");
    }

    private static Dictionary<string, ModSearchName> BuildIndex()
    {
        var index = new Dictionary<string, ModSearchName>(StringComparer.Ordinal);
        foreach (var name in Names)
        foreach (var alias in new[] { name.English, name.Chinese, name.Chinese + name.English, name.English + name.Chinese }.Concat(name.Aliases))
            index.Add(Normalize(alias), name);
        return index;
    }
}
