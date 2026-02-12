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
                var key = eq >= 0 ? part[..eq] : part;
                var value = eq >= 0 && eq + 1 < part.Length ? part[(eq + 1)..] : string.Empty;
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
}
