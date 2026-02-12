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
        AddEvalTraceStep(runtime, node);

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
