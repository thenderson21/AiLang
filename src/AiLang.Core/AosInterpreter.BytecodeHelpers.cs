using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static string VmConstantKey(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.String => $"s:{value.AsString()}",
            AosValueKind.Int => $"i:{value.AsInt()}",
            AosValueKind.Bool => value.AsBool() ? "b:true" : "b:false",
            AosValueKind.Node => $"o:{EncodeNodeConstant(value.AsNode())}",
            AosValueKind.Unknown => "n:null",
            _ => throw new InvalidOperationException("Unsupported constant value.")
        };
    }

    private static string EncodeNodeConstant(AosNode node)
    {
        var wrapper = new AosNode(
            "Program",
            "bc_const_program",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            new List<AosNode> { node },
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(wrapper);
    }

    private static AosNode DecodeNodeConstant(string text, string nodeId)
    {
        var parse = AosParsing.Parse(text);
        if (parse.Root is null || parse.Diagnostics.Count > 0 || parse.Root.Kind != "Program" || parse.Root.Children.Count != 1)
        {
            throw new VmRuntimeException("VM001", "Invalid node constant encoding.", nodeId);
        }

        return parse.Root.Children[0];
    }

    private static AosNode BuildVmInstruction(int index, string op, int? a, int? b, string? s)
    {
        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["op"] = new AosAttrValue(AosAttrKind.Identifier, op)
        };
        if (a.HasValue)
        {
            attrs["a"] = new AosAttrValue(AosAttrKind.Int, a.Value);
        }
        if (b.HasValue)
        {
            attrs["b"] = new AosAttrValue(AosAttrKind.Int, b.Value);
        }
        if (!string.IsNullOrEmpty(s))
        {
            attrs["s"] = new AosAttrValue(AosAttrKind.String, s!);
        }
        return new AosNode(
            "Inst",
            $"i{index}",
            attrs,
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    }
}
