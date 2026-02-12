using AiVM.Core;

namespace AiLang.Core;

public sealed class AosRuntime
{
    public Dictionary<string, AosValue> Env { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "math" };
    public HashSet<string> ReadOnlyBindings { get; } = new(StringComparer.Ordinal);
    public string ModuleBaseDir { get; set; } = HostFileSystem.GetFullPath(".");
    public Dictionary<string, Dictionary<string, AosValue>> ModuleExports { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ModuleLoading { get; } = new(StringComparer.Ordinal);
    public Stack<Dictionary<string, AosValue>> ExportScopes { get; } = new();
    public bool TraceEnabled { get; set; }
    public List<AosNode> TraceSteps { get; } = new();
    public AosNode? Program { get; set; }
    public VmNetworkState Network { get; } = new();
}

public sealed partial class AosInterpreter
{
    private bool _strictUnknown;
    private int _evalDepth;
    private const int MaxEvalDepth = 4096;

    public AosValue EvaluateProgram(AosNode program, AosRuntime runtime)
    {
        _strictUnknown = false;
        return Evaluate(program, runtime, runtime.Env);
    }

    public AosValue EvaluateExpression(AosNode expr, AosRuntime runtime)
    {
        _strictUnknown = false;
        return Evaluate(expr, runtime, runtime.Env);
    }

    public AosValue EvaluateExpressionStrict(AosNode expr, AosRuntime runtime)
    {
        _strictUnknown = true;
        return Evaluate(expr, runtime, runtime.Env);
    }

    public AosValue RunBytecode(AosNode bytecode, string entryName, AosNode argsNode, AosRuntime runtime)
    {
        try
        {
            var vm = VmProgramLoader.Load(bytecode, BytecodeAdapter.Instance);
            var args = BuildVmArgs(vm, entryName, argsNode);
            return VmEngine.Run<AosNode, AosValue>(
                vm,
                entryName,
                args,
                new VmExecutionAdapter(this, runtime));
        }
        catch (VmRuntimeException ex)
        {
            return AosValue.FromNode(CreateErrNode(
                "vm_err",
                ex.Code,
                ex.Message,
                ex.NodeId,
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
        }
    }

    private AosValue Evaluate(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        try
        {
            return EvalNode(node, runtime, env);
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
    }

    private AosValue EvalNode(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (runtime.TraceEnabled)
        {
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

        _evalDepth++;
        if (_evalDepth > MaxEvalDepth)
        {
            throw new InvalidOperationException($"Evaluation depth exceeded at {node.Kind}#{node.Id}.");
        }

        try
        {
            var value = EvalCore(node, runtime, env);

            if (_strictUnknown && value.Kind == AosValueKind.Unknown)
            {
                throw new InvalidOperationException($"Unknown value from node {node.Kind}#{node.Id}.");
            }

            return value;
        }
        finally
        {
            _evalDepth--;
        }
    }

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
