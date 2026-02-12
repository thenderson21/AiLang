namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static AosNode CreateErrNode(string id, string code, string message, string nodeId, AosSpan span)
    {
        return new AosNode(
            "Err",
            id,
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["code"] = new AosAttrValue(AosAttrKind.Identifier, code),
                ["message"] = new AosAttrValue(AosAttrKind.String, message),
                ["nodeId"] = new AosAttrValue(AosAttrKind.Identifier, nodeId)
            },
            new List<AosNode>(),
            span);
    }

    private static AosNode CreateDiagnosticsNode(List<AosDiagnostic> diagnostics, AosSpan span)
    {
        var children = new List<AosNode>(diagnostics.Count);
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            children.Add(CreateErrNode(
                $"diag{i}",
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.NodeId ?? "unknown",
                span));
        }

        return new AosNode(
            "Block",
            "diagnostics",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            children,
            span);
    }

    private static bool IsErrValue(AosValue value)
    {
        return value.Kind == AosValueKind.Node && value.Data is AosNode node && node.Kind == "Err";
    }

    private static AosNode ToRuntimeNode(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.Node => value.AsNode(),
            AosValueKind.String => new AosNode(
                "Lit",
                "runtime_string",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal) { ["value"] = new AosAttrValue(AosAttrKind.String, value.AsString()) },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            AosValueKind.Int => new AosNode(
                "Lit",
                "runtime_int",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal) { ["value"] = new AosAttrValue(AosAttrKind.Int, value.AsInt()) },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            AosValueKind.Bool => new AosNode(
                "Lit",
                "runtime_bool",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal) { ["value"] = new AosAttrValue(AosAttrKind.Bool, value.AsBool()) },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            AosValueKind.Void => new AosNode(
                "Block",
                "void",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            _ => CreateErrNode(
                "runtime_err",
                "RUN030",
                "Unsupported runtime value.",
                "runtime",
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
        };
    }

    private static AosValue CreateRuntimeErr(string code, string message, string nodeId, AosSpan span)
    {
        return AosValue.FromNode(CreateErrNode("runtime_err", code, message, nodeId, span));
    }
}
