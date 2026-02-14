using System.Text;

namespace AiLang.Core;

public static class AosNodeIdCanonicalizer
{
    public static AosNode AssignMissingIds(AosNode root)
    {
        var state = new CanonicalizeState(normalizeAll: false);
        return RewriteNode(root, "root", 0, state);
    }

    public static AosNode NormalizeIds(AosNode root)
    {
        var state = new CanonicalizeState(normalizeAll: true);
        return RewriteNode(root, "root", 0, state);
    }

    private static AosNode RewriteNode(AosNode node, string parentKey, int siblingOrdinal, CanonicalizeState state)
    {
        var stableKey = BuildStableKey(parentKey, node.Kind, node.Attrs, siblingOrdinal);
        var resolvedId = ResolveId(node, stableKey, state);

        var childCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var rewrittenChildren = new List<AosNode>(node.Children.Count);
        foreach (var child in node.Children)
        {
            var childBaseKey = BuildStableKey(stableKey, child.Kind, child.Attrs, 0);
            var ordinal = childCounts.TryGetValue(childBaseKey, out var seen) ? seen : 0;
            childCounts[childBaseKey] = ordinal + 1;
            rewrittenChildren.Add(RewriteNode(child, stableKey, ordinal, state));
        }

        return new AosNode(
            node.Kind,
            resolvedId,
            new Dictionary<string, AosAttrValue>(node.Attrs, StringComparer.Ordinal),
            rewrittenChildren,
            node.Span);
    }

    private static string ResolveId(AosNode node, string stableKey, CanonicalizeState state)
    {
        if (!state.NormalizeAll && !string.IsNullOrEmpty(node.Id))
        {
            state.UsedIds.Add(node.Id);
            return node.Id;
        }

        var prefix = BuildKindPrefix(node.Kind);
        var digest = ComputeDigest(stableKey);
        var candidate = $"{prefix}_{digest}";
        var suffix = 1;
        while (!state.UsedIds.Add(candidate))
        {
            suffix++;
            candidate = $"{prefix}_{digest}_{suffix}";
        }

        return candidate;
    }

    private static string BuildStableKey(string parentKey, string kind, Dictionary<string, AosAttrValue> attrs, int siblingOrdinal)
    {
        var sb = new StringBuilder();
        sb.Append(parentKey);
        sb.Append('|');
        sb.Append(kind);
        sb.Append('|');
        AppendAttrs(sb, attrs);
        sb.Append("|o:");
        sb.Append(siblingOrdinal);
        return sb.ToString();
    }

    private static void AppendAttrs(StringBuilder sb, Dictionary<string, AosAttrValue> attrs)
    {
        var keys = attrs.Keys.OrderBy(static k => k, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var attr = attrs[key];
            sb.Append(key.Length);
            sb.Append(':');
            sb.Append(key);
            sb.Append('=');
            sb.Append((int)attr.Kind);
            sb.Append(':');
            var value = attr.Kind switch
            {
                AosAttrKind.String => attr.AsString(),
                AosAttrKind.Int => attr.AsInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
                AosAttrKind.Bool => attr.AsBool() ? "true" : "false",
                _ => attr.AsString()
            };
            sb.Append(value.Length);
            sb.Append(':');
            sb.Append(value);
            sb.Append(';');
        }
    }

    private static string BuildKindPrefix(string kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return "n";
        }

        var sb = new StringBuilder(kind.Length);
        foreach (var ch in kind)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (ch == '_' || ch == '-')
            {
                sb.Append('_');
            }
        }

        if (sb.Length == 0)
        {
            return "n";
        }

        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, 'n');
        }

        return sb.ToString();
    }

    private static string ComputeDigest(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ulong hash = 14695981039346656037UL;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        var hex = hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        return hex[..12];
    }

    private sealed class CanonicalizeState
    {
        public CanonicalizeState(bool normalizeAll)
        {
            NormalizeAll = normalizeAll;
        }

        public bool NormalizeAll { get; }
        public HashSet<string> UsedIds { get; } = new(StringComparer.Ordinal);
    }
}
