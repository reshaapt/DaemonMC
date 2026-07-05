using System.Collections.Concurrent;

namespace DaemonMC.Telemetry;

internal static class PacketTracePropertyResolver
{
    private static readonly ConcurrentDictionary<string, string[]> SourceLines = new();

    public static string FromWriteArgument(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return string.Empty;

        return Clean(argument);
    }

    public static string FromSourceLine(string filePath, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(filePath) || lineNumber <= 0 || !File.Exists(filePath))
            return string.Empty;

        string[] lines = SourceLines.GetOrAdd(filePath, File.ReadAllLines);
        if (lineNumber > lines.Length)
            return string.Empty;

        return Parse(lines[lineNumber - 1]);
    }

    private static string Parse(string line)
    {
        line = StripComment(line).Trim();

        int equalsIndex = line.IndexOf('=');
        if (equalsIndex > 0 && !line.Contains("==", StringComparison.Ordinal))
            return Clean(line[..equalsIndex]);

        int openParen = line.IndexOf('(');
        int closeParen = line.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen)
            return Clean(line[(openParen + 1)..closeParen]);

        return string.Empty;
    }

    private static string StripComment(string value)
    {
        int commentIndex = value.IndexOf("//", StringComparison.Ordinal);
        return commentIndex >= 0 ? value[..commentIndex] : value;
    }

    private static string Clean(string value)
    {
        value = StripComment(value).Trim().TrimEnd(',', ';');

        int commaIndex = value.IndexOf(',');
        if (commaIndex >= 0)
            value = value[..commaIndex].Trim();

        if (value.StartsWith("() =>", StringComparison.Ordinal))
            value = value[5..].Trim();

        return value;
    }
}
