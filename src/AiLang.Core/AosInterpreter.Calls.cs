namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private AosValue EvalCall(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("target", out var targetAttr) || targetAttr.Kind != AosAttrKind.Identifier)
        {
            return AosValue.Unknown;
        }

        var target = targetAttr.AsString();
        if (TryEvaluateMathAddCall(target, node, runtime, env, out var mathValue))
        {
            return mathValue;
        }

        if (TryEvaluateCapabilityCall(target, node, runtime, env, out var capabilityValue))
        {
            return capabilityValue;
        }

        if (TryEvaluateSysCall(target, node, runtime, env, out var sysValue))
        {
            return sysValue;
        }

        if (TryEvaluateVmRunCall(target, node, runtime, env, out var vmRunValue))
        {
            return vmRunValue;
        }

        if (TryEvaluateCompilerCall(target, node, runtime, env, out var compilerValue))
        {
            return compilerValue;
        }

        if (TryEvaluateUserFunctionCall(target, node, runtime, env, out var functionValue))
        {
            return functionValue;
        }

        return AosValue.Unknown;
    }
}
