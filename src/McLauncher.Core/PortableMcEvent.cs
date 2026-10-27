namespace McLauncher.Core;

public sealed record PortableMcEvent(string Kind, IReadOnlyList<string> Values, string RawLine)
{
    public bool IsError => Kind.StartsWith("error_", StringComparison.Ordinal);

    public static PortableMcEvent Parse(string line)
    {
        var fields = line.Split('\t');
        return new PortableMcEvent(fields[0], fields[1..], line);
    }

    public static string DecodeText(string value) => value.Replace("\\n", "\n").Replace("\\t", "\t");
}

