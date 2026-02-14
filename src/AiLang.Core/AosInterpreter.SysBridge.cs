using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private bool TryEvaluateSysCall(
        string target,
        AosNode callNode,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;
        if (target == "sys.vm_run" || !SyscallContracts.IsSysTarget(target))
        {
            return false;
        }

        if (target == "sys.process_argv")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return true;
            }
            if (callNode.Children.Count != 0)
            {
                return true;
            }

            result = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(VmSyscalls.ProcessArgv()));
            return true;
        }

        if (target == "sys.fs_stat")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return true;
            }
            if (callNode.Children.Count != 1)
            {
                return true;
            }

            var pathValue = EvalNode(callNode.Children[0], runtime, env);
            if (pathValue.Kind != AosValueKind.String)
            {
                return true;
            }

            var stat = VmSyscalls.FsStat(pathValue.AsString());
            result = AosValue.FromNode(AosRuntimeNodes.BuildFsStatNode(stat.Type, stat.Size, stat.MtimeUnixMs));
            return true;
        }

        if (!runtime.Permissions.Contains("sys"))
        {
            return true;
        }

        if (!VmSyscallDispatcher.SupportsTarget(target))
        {
            return false;
        }

        if (VmSyscallDispatcher.TryGetExpectedArity(target, out var sysArity) &&
            callNode.Children.Count != sysArity)
        {
            return true;
        }

        var args = new List<SysValue>(callNode.Children.Count);
        for (var i = 0; i < callNode.Children.Count; i++)
        {
            args.Add(ToSysValue(EvalNode(callNode.Children[i], runtime, env)));
        }

        if (!VmSyscallDispatcher.TryInvoke(target, args, runtime.Network, out var sysResult))
        {
            return false;
        }

        result = FromSysValue(sysResult);
        return true;
    }

    private bool TryEvaluateCapabilityCall(
        string target,
        AosNode callNode,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;
        var requiredPermission = target switch
        {
            "console.print" => "console",
            "io.print" or "io.write" or "io.readLine" or "io.readAllStdin" or "io.readFile" or "io.fileExists" or "io.pathExists" or "io.makeDir" or "io.writeFile" => "io",
            _ => null
        };
        if (requiredPermission is null)
        {
            return false;
        }
        if (!runtime.Permissions.Contains(requiredPermission))
        {
            return true;
        }

        if (!VmCapabilityDispatcher.SupportsTarget(target))
        {
            return false;
        }

        if (VmCapabilityDispatcher.TryGetExpectedArity(target, out var capabilityArity) &&
            callNode.Children.Count != capabilityArity)
        {
            return true;
        }

        var args = new List<SysValue>(callNode.Children.Count);
        for (var i = 0; i < callNode.Children.Count; i++)
        {
            var value = EvalNode(callNode.Children[i], runtime, env);
            if (target == "io.print")
            {
                if (value.Kind == AosValueKind.Unknown)
                {
                    return true;
                }
                args.Add(SysValue.String(ValueToDisplayString(value)));
            }
            else
            {
                args.Add(ToSysValue(value));
            }
        }

        if (!VmCapabilityDispatcher.TryInvoke(target, args, out var capResult))
        {
            return false;
        }

        result = FromSysValue(capResult);
        return true;
    }

    private static SysValue ToSysValue(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.String => SysValue.String(value.AsString()),
            AosValueKind.Int => SysValue.Int(value.AsInt()),
            AosValueKind.Bool => SysValue.Bool(value.AsBool()),
            AosValueKind.Void => SysValue.Void(),
            _ => SysValue.Unknown()
        };
    }

    private static AosValue FromSysValue(SysValue value)
    {
        return value.Kind switch
        {
            VmValueKind.String => AosValue.FromString(value.StringValue),
            VmValueKind.Int => AosValue.FromInt(value.IntValue),
            VmValueKind.Bool => AosValue.FromBool(value.BoolValue),
            VmValueKind.Void => AosValue.Void,
            _ => AosValue.Unknown
        };
    }
}
