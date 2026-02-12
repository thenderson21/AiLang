namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private AosValue EvalFunction(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("params", out var paramsAttr) || paramsAttr.Kind != AosAttrKind.Identifier)
        {
            return AosValue.Unknown;
        }

        if (node.Children.Count != 1 || node.Children[0].Kind != "Block")
        {
            return AosValue.Unknown;
        }

        var parameters = paramsAttr.AsString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var captured = new Dictionary<string, AosValue>(env, StringComparer.Ordinal);
        var function = new AosFunction(parameters, node.Children[0], captured);
        return AosValue.FromFunction(function);
    }

    private AosValue EvalFunctionCall(AosFunction function, List<AosValue> args, AosRuntime runtime)
    {
        if (function.Parameters.Count != args.Count)
        {
            return AosValue.Unknown;
        }

        var localEnv = new Dictionary<string, AosValue>(runtime.Env, StringComparer.Ordinal);
        foreach (var entry in function.CapturedEnv)
        {
            localEnv[entry.Key] = entry.Value;
        }
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            localEnv[function.Parameters[i]] = args[i];
        }

        try
        {
            return EvalNode(function.Body, runtime, localEnv);
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
    }

    private sealed class ReturnSignal : Exception
    {
        public ReturnSignal(AosValue value)
        {
            Value = value;
        }

        public AosValue Value { get; }
    }
}
