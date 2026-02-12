namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static void AddEvalTraceStep(AosRuntime runtime, AosNode node)
    {
        if (!runtime.TraceEnabled)
        {
            return;
        }

        runtime.TraceSteps.Add(new AosNode(
            "Step",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["kind"] = new AosAttrValue(AosAttrKind.String, node.Kind),
                ["nodeId"] = new AosAttrValue(AosAttrKind.String, node.Id)
            },
            new List<AosNode>(),
            node.Span));
    }
}
