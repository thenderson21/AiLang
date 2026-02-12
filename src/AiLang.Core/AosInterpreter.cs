namespace AiLang.Core;

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

}
