using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
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
                new VmExecutionAdapter(runtime));
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
}
