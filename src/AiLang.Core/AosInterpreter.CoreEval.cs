namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private AosValue EvalCore(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        switch (node.Kind)
        {
            case "Program":
                AosValue last = AosValue.Void;
                foreach (var child in node.Children)
                {
                    last = EvalNode(child, runtime, env);
                    if (IsErrValue(last))
                    {
                        return last;
                    }
                }
                return last;
            case "Let":
                if (!node.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
                {
                    return AosValue.Unknown;
                }
                var name = nameAttr.AsString();
                if (runtime.ReadOnlyBindings.Contains(name))
                {
                    throw new InvalidOperationException($"Cannot assign read-only binding '{name}'.");
                }
                if (node.Children.Count != 1)
                {
                    return AosValue.Unknown;
                }
                var value = EvalNode(node.Children[0], runtime, env);
                env[name] = value;
                return AosValue.Void;
            case "Var":
                if (!node.Attrs.TryGetValue("name", out var varAttr) || varAttr.Kind != AosAttrKind.Identifier)
                {
                    return AosValue.Unknown;
                }
                return env.TryGetValue(varAttr.AsString(), out var bound) ? bound : AosValue.Unknown;
            case "Lit":
                if (!node.Attrs.TryGetValue("value", out var litAttr))
                {
                    return AosValue.Unknown;
                }
                return litAttr.Kind switch
                {
                    AosAttrKind.String => AosValue.FromString(litAttr.AsString()),
                    AosAttrKind.Int => AosValue.FromInt(litAttr.AsInt()),
                    AosAttrKind.Bool => AosValue.FromBool(litAttr.AsBool()),
                    _ => AosValue.Unknown
                };
            case "Call":
                return EvalCall(node, runtime, env);
            case "Import":
                return EvalImport(node, runtime, env);
            case "Export":
                return EvalExport(node, runtime, env);
            case "Fn":
                return EvalFunction(node, runtime, env);
            case "Eq":
                return EvalEq(node, runtime, env);
            case "Add":
                return EvalAdd(node, runtime, env);
            case "ToString":
                return EvalToString(node, runtime, env);
            case "StrConcat":
                return EvalStrConcat(node, runtime, env);
            case "StrEscape":
                return EvalStrEscape(node, runtime, env);
            case "MakeBlock":
                return EvalMakeBlock(node, runtime, env);
            case "AppendChild":
                return EvalAppendChild(node, runtime, env);
            case "MakeErr":
                return EvalMakeErr(node, runtime, env);
            case "MakeLitString":
                return EvalMakeLitString(node, runtime, env);
            case "MakeLitInt":
                return EvalMakeLitInt(node, runtime, env);
            case "Event":
            case "Command":
            case "HttpRequest":
            case "Route":
            case "Match":
            case "Map":
            case "Field":
            case "Bytecode":
                return AosValue.FromNode(node);
            case "NodeKind":
                return EvalNodeKind(node, runtime, env);
            case "NodeId":
                return EvalNodeId(node, runtime, env);
            case "AttrCount":
                return EvalAttrCount(node, runtime, env);
            case "AttrKey":
                return EvalAttrKey(node, runtime, env);
            case "AttrValueKind":
                return EvalAttrValueKind(node, runtime, env);
            case "AttrValueString":
                return EvalAttrValueString(node, runtime, env);
            case "AttrValueInt":
                return EvalAttrValueInt(node, runtime, env);
            case "AttrValueBool":
                return EvalAttrValueBool(node, runtime, env);
            case "ChildCount":
                return EvalChildCount(node, runtime, env);
            case "ChildAt":
                return EvalChildAt(node, runtime, env);
            case "If":
                if (node.Children.Count < 2)
                {
                    return AosValue.Unknown;
                }
                var cond = EvalNode(node.Children[0], runtime, env);
                var condValue = cond.Kind == AosValueKind.Bool && cond.AsBool();
                if (cond.Kind != AosValueKind.Bool)
                {
                    return AosValue.Unknown;
                }
                if (condValue)
                {
                    return EvalNode(node.Children[1], runtime, env);
                }
                if (node.Children.Count >= 3)
                {
                    return EvalNode(node.Children[2], runtime, env);
                }
                return AosValue.Void;
            case "Block":
                AosValue result = AosValue.Void;
                foreach (var child in node.Children)
                {
                    result = EvalNode(child, runtime, env);
                    if (IsErrValue(result))
                    {
                        return result;
                    }
                }
                return result;
            case "Return":
                if (node.Children.Count == 1)
                {
                    throw new ReturnSignal(EvalNode(node.Children[0], runtime, env));
                }
                throw new ReturnSignal(AosValue.Void);
            default:
                return AosValue.Unknown;
        }
    }
}
