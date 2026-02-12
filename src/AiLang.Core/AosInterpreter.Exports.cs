namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private AosValue EvalExport(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
        {
            return CreateRuntimeErr("RUN027", "Export requires identifier name attribute.", node.Id, node.Span);
        }

        if (node.Children.Count != 0)
        {
            return CreateRuntimeErr("RUN028", "Export must not have children.", node.Id, node.Span);
        }

        if (runtime.ExportScopes.Count == 0)
        {
            return AosValue.Void;
        }

        var name = nameAttr.AsString();
        if (!env.TryGetValue(name, out var value))
        {
            return CreateRuntimeErr("RUN029", $"Export name not found: {name}", node.Id, node.Span);
        }

        runtime.ExportScopes.Peek()[name] = value;
        return AosValue.Void;
    }
}
