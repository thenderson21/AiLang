namespace AiLang.Core;

public static class AosRuntimeNodes
{
    private static readonly AosSpan ZeroSpan = new(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0));

    public static AosNode BuildArgvNode(string[] values)
    {
        var children = new List<AosNode>(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            children.Add(new AosNode(
                "Lit",
                $"argv{i}",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["value"] = new AosAttrValue(AosAttrKind.String, values[i])
                },
                new List<AosNode>(),
                ZeroSpan));
        }

        return new AosNode(
            "Block",
            "argv",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            children,
            ZeroSpan);
    }
}
