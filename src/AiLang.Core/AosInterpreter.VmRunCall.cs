using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private bool TryEvaluateVmRunCall(
        string target,
        AosNode node,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;
        if (target != "sys.vm_run")
        {
            return false;
        }

        if (!runtime.Permissions.Contains("sys"))
        {
            return true;
        }
        if (node.Children.Count != 3)
        {
            return true;
        }

        var bytecodeValue = EvalNode(node.Children[0], runtime, env);
        var entryValue = EvalNode(node.Children[1], runtime, env);
        var argsValue = EvalNode(node.Children[2], runtime, env);
        if (bytecodeValue.Kind != AosValueKind.Node || entryValue.Kind != AosValueKind.String || argsValue.Kind != AosValueKind.Node)
        {
            return true;
        }

        try
        {
            var bytecodeNode = bytecodeValue.AsNode();
            var entryName = entryValue.AsString();
            var vm = VmProgramLoader.Load(bytecodeNode, BytecodeAdapter.Instance);
            var args = BuildVmArgs(vm, entryName, argsValue.AsNode());
            result = VmEngine.Run<AosNode, AosValue>(
                vm,
                entryName,
                args,
                new VmExecutionAdapter(runtime));
            return true;
        }
        catch (VmRuntimeException ex)
        {
            result = AosValue.FromNode(CreateErrNode("vm_err", ex.Code, ex.Message, ex.NodeId, node.Span));
            return true;
        }
    }
}
