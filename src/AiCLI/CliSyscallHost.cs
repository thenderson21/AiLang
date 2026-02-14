using AiVM.Core;

namespace AiCLI;

internal sealed class CliSyscallHost : DefaultSyscallHost
{
    public override string[] ProcessArgv()
    {
        return Environment.GetCommandLineArgs();
    }
}
