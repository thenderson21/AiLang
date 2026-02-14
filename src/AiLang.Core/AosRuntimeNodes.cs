namespace AiLang.Core;

public static class AosRuntimeNodes
{
    private static readonly AosSpan ZeroSpan = new(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0));

    public static AosNode BuildArgvNode(string[] values)
    {
        return BuildStringListNode("argv", "argv", values);
    }

    public static AosNode BuildStringListNode(string rootId, string childIdPrefix, string[] values)
    {
        var children = new List<AosNode>(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            children.Add(new AosNode(
                "Lit",
                $"{childIdPrefix}{i}",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["value"] = new AosAttrValue(AosAttrKind.String, values[i])
                },
                new List<AosNode>(),
                ZeroSpan));
        }

        return new AosNode(
            "Block",
            rootId,
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            children,
            ZeroSpan);
    }

    public static AosNode BuildFsStatNode(string type, int size, int mtimeUnixMs)
    {
        return new AosNode(
            "Stat",
            "stat",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["type"] = new AosAttrValue(AosAttrKind.String, type),
                ["size"] = new AosAttrValue(AosAttrKind.Int, size),
                ["mtime"] = new AosAttrValue(AosAttrKind.Int, mtimeUnixMs)
            },
            new List<AosNode>(),
            ZeroSpan);
    }
}
