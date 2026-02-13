using System.Text;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static AosNode ParseHttpRequestNode(string raw, AosSpan span)
    {
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        var splitIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        var head = splitIndex >= 0 ? normalized[..splitIndex] : normalized;

        var lines = head.Split('\n');
        var requestLine = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = requestParts.Length > 0 ? requestParts[0] : string.Empty;
        var rawPath = requestParts.Length > 1 ? requestParts[1] : string.Empty;

        var query = string.Empty;
        var path = rawPath;
        var queryStart = rawPath.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            path = rawPath[..queryStart];
            query = queryStart + 1 < rawPath.Length ? rawPath[(queryStart + 1)..] : string.Empty;
        }

        var headerMapChildren = new List<AosNode>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headerMapChildren.Add(new AosNode(
                "Field",
                $"http_header_{i}",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["key"] = new AosAttrValue(AosAttrKind.String, key)
                },
                new List<AosNode>
                {
                    new AosNode(
                        "Lit",
                        $"http_header_val_{i}",
                        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                        {
                            ["value"] = new AosAttrValue(AosAttrKind.String, value)
                        },
                        new List<AosNode>(),
                        span)
                },
                span));
        }

        var queryMapChildren = new List<AosNode>();
        if (!string.IsNullOrEmpty(query))
        {
            var queryParts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < queryParts.Length; i++)
            {
                var part = queryParts[i];
                var eq = part.IndexOf('=');
                var key = UrlDecodeComponent(eq >= 0 ? part[..eq] : part);
                var value = UrlDecodeComponent(eq >= 0 && eq + 1 < part.Length ? part[(eq + 1)..] : string.Empty);
                queryMapChildren.Add(new AosNode(
                    "Field",
                    $"http_query_{i}",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["key"] = new AosAttrValue(AosAttrKind.String, key)
                    },
                    new List<AosNode>
                    {
                        new AosNode(
                            "Lit",
                            $"http_query_val_{i}",
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                            {
                                ["value"] = new AosAttrValue(AosAttrKind.String, value)
                            },
                            new List<AosNode>(),
                            span)
                    },
                    span));
            }
        }

        return new AosNode(
            "HttpRequest",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["method"] = new AosAttrValue(AosAttrKind.String, method),
                ["path"] = new AosAttrValue(AosAttrKind.String, path)
            },
            new List<AosNode>
            {
                new AosNode("Map", "http_headers", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), headerMapChildren, span),
                new AosNode("Map", "http_query", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), queryMapChildren, span)
            },
            span);
    }

    private static string UrlDecodeComponent(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '+')
            {
                bytes.Add((byte)' ');
                continue;
            }

            if (ch == '%' && i + 2 < value.Length && TryParseHexByte(value[i + 1], value[i + 2], out var decoded))
            {
                bytes.Add(decoded);
                i += 2;
                continue;
            }

            var scalarLength = 1;
            if (char.IsHighSurrogate(ch) &&
                i + 1 < value.Length &&
                char.IsLowSurrogate(value[i + 1]))
            {
                scalarLength = 2;
            }

            var charByteCount = Encoding.UTF8.GetByteCount(value.AsSpan(i, scalarLength));
            if (charByteCount == 1)
            {
                bytes.Add((byte)ch);
                i += scalarLength - 1;
                continue;
            }

            var encoded = new byte[charByteCount];
            Encoding.UTF8.GetBytes(value.AsSpan(i, scalarLength), encoded);
            bytes.AddRange(encoded);
            i += scalarLength - 1;
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool TryParseHexByte(char hi, char lo, out byte value)
    {
        value = 0;
        if (!TryParseHexNibble(hi, out var hiNibble) || !TryParseHexNibble(lo, out var loNibble))
        {
            return false;
        }

        value = (byte)((hiNibble << 4) | loNibble);
        return true;
    }

    private static bool TryParseHexNibble(char ch, out int value)
    {
        if (ch is >= '0' and <= '9')
        {
            value = ch - '0';
            return true;
        }

        if (ch is >= 'A' and <= 'F')
        {
            value = ch - 'A' + 10;
            return true;
        }

        if (ch is >= 'a' and <= 'f')
        {
            value = ch - 'a' + 10;
            return true;
        }

        value = 0;
        return false;
    }
}
