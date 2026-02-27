using System.Globalization;
using System.Text;

namespace AiCLI;

internal enum CliTomlKind
{
    String,
    Int,
    Bool,
    StringArray
}

internal readonly struct CliTomlValue
{
    public CliTomlKind Kind { get; }
    public string StringValue { get; }
    public int IntValue { get; }
    public bool BoolValue { get; }
    public IReadOnlyList<string> StringArrayValue { get; }

    private CliTomlValue(CliTomlKind kind, string stringValue, int intValue, bool boolValue, IReadOnlyList<string> stringArrayValue)
    {
        Kind = kind;
        StringValue = stringValue;
        IntValue = intValue;
        BoolValue = boolValue;
        StringArrayValue = stringArrayValue;
    }

    public static CliTomlValue FromString(string value) => new(CliTomlKind.String, value, 0, false, Array.Empty<string>());
    public static CliTomlValue FromInt(int value) => new(CliTomlKind.Int, string.Empty, value, false, Array.Empty<string>());
    public static CliTomlValue FromBool(bool value) => new(CliTomlKind.Bool, string.Empty, 0, value, Array.Empty<string>());
    public static CliTomlValue FromStringArray(IReadOnlyList<string> values) => new(CliTomlKind.StringArray, string.Empty, 0, false, values);
}

internal static class CliToml
{
    public static List<Dictionary<string, CliTomlValue>> ParseArrayOfTables(string path, string tableName)
    {
        var lines = File.ReadAllLines(path);
        var rows = new List<Dictionary<string, CliTomlValue>>();
        Dictionary<string, CliTomlValue>? current = null;

        foreach (var raw in lines)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            {
                var section = line[2..^2].Trim();
                if (string.Equals(section, tableName, StringComparison.Ordinal))
                {
                    current = new Dictionary<string, CliTomlValue>(StringComparer.Ordinal);
                    rows.Add(current);
                }
                else
                {
                    current = null;
                }
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var valueText = line[(eq + 1)..].Trim();
            current[key] = ParseValue(valueText);
        }

        return rows;
    }

    public static string GetString(Dictionary<string, CliTomlValue> row, string key, string fallback)
    {
        if (!row.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Kind == CliTomlKind.String ? value.StringValue : fallback;
    }

    public static int GetInt(Dictionary<string, CliTomlValue> row, string key, int fallback)
    {
        if (!row.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Kind == CliTomlKind.Int ? value.IntValue : fallback;
    }

    public static bool GetBool(Dictionary<string, CliTomlValue> row, string key, bool fallback)
    {
        if (!row.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value.Kind == CliTomlKind.Bool ? value.BoolValue : fallback;
    }

    public static List<string> GetStringArray(Dictionary<string, CliTomlValue> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value.Kind != CliTomlKind.StringArray)
        {
            return new List<string>();
        }

        return new List<string>(value.StringArrayValue);
    }

    public static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    public static string RenderStringArray(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[]";
        }

        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(Quote(values[i]));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static CliTomlValue ParseValue(string text)
    {
        if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2)
        {
            return CliTomlValue.FromString(Unescape(text[1..^1]));
        }

        if (string.Equals(text, "true", StringComparison.Ordinal))
        {
            return CliTomlValue.FromBool(true);
        }

        if (string.Equals(text, "false", StringComparison.Ordinal))
        {
            return CliTomlValue.FromBool(false);
        }

        if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
        {
            var inner = text[1..^1].Trim();
            if (inner.Length == 0)
            {
                return CliTomlValue.FromStringArray(Array.Empty<string>());
            }

            var parts = SplitArray(inner);
            var values = new List<string>(parts.Count);
            foreach (var part in parts)
            {
                var token = part.Trim();
                if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
                {
                    values.Add(Unescape(token[1..^1]));
                }
            }
            return CliTomlValue.FromStringArray(values);
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
        {
            return CliTomlValue.FromInt(parsedInt);
        }

        return CliTomlValue.FromString(text);
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
                continue;
            }

            if (ch == '#' && !inString)
            {
                return line[..i];
            }
        }
        return line;
    }

    private static List<string> SplitArray(string input)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inString = false;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch == '"' && (i == 0 || input[i - 1] != '\\'))
            {
                inString = !inString;
                sb.Append(ch);
                continue;
            }

            if (ch == ',' && !inString)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        result.Add(sb.ToString());
        return result;
    }

    private static string Unescape(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\\' || i + 1 >= text.Length)
            {
                sb.Append(ch);
                continue;
            }

            i++;
            var next = text[i];
            switch (next)
            {
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case '"':
                    sb.Append('"');
                    break;
                case '\\':
                    sb.Append('\\');
                    break;
                default:
                    sb.Append('\\');
                    sb.Append(next);
                    break;
            }
        }

        return sb.ToString();
    }
}
