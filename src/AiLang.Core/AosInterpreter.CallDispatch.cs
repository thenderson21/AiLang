namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private bool TryEvaluateMathAddCall(
        string target,
        AosNode node,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;
        if (target != "math.add")
        {
            return false;
        }

        if (!runtime.Permissions.Contains("math"))
        {
            return true;
        }
        if (node.Children.Count != 2)
        {
            return true;
        }

        var left = EvalNode(node.Children[0], runtime, env);
        var right = EvalNode(node.Children[1], runtime, env);
        if (left.Kind != AosValueKind.Int || right.Kind != AosValueKind.Int)
        {
            return true;
        }

        result = AosValue.FromInt(left.AsInt() + right.AsInt());
        return true;
    }

    private bool TryEvaluateUserFunctionCall(
        string target,
        AosNode node,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;
        if (!env.TryGetValue(target, out var functionValue))
        {
            runtime.Env.TryGetValue(target, out functionValue);
        }

        if (functionValue is null || functionValue.Kind != AosValueKind.Function)
        {
            return false;
        }

        var args = node.Children.Select(child => EvalNode(child, runtime, env)).ToList();
        runtime.CallStack.Push(target);
        try
        {
            result = EvalFunctionCall(functionValue.AsFunction(), args, runtime);
        }
        finally
        {
            runtime.CallStack.Pop();
        }
        return true;
    }
}
