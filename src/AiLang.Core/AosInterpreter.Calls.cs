namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static readonly HashSet<string> BlockingCallTargets = new(StringComparer.Ordinal)
    {
        "sys.net_asyncAwait",
        "sys.net_accept",
        "sys.net_readHeaders",
        "sys.net_tcpAccept",
        "sys.net_tcpConnect",
        "sys.net_tcpConnectTls",
        "sys.net_tcpRead",
        "sys.net_tcpWrite",
        "sys.net_udpRecv",
        "sys.time_sleepMs",
        "sys.fs_readFile",
        "sys.fs_readDir",
        "sys.fs_stat",
        "sys.console_readLine",
        "sys.console_readAllStdin",
        "io.readLine",
        "io.readAllStdin",
        "io.readFile",
        "httpRequestAwait"
    };

    private AosValue EvalCall(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("target", out var targetAttr) || targetAttr.Kind != AosAttrKind.Identifier)
        {
            return AosValue.Unknown;
        }

        var target = targetAttr.AsString();
        if (runtime.CallStack.Contains("update") && BlockingCallTargets.Contains(target))
        {
            return CreateRuntimeErr("RUN031", $"Blocking call '{target}' is not allowed during update.", node.Id, node.Span);
        }

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
