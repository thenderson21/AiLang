using AiVM.Core;

namespace AiLang.Core;

public sealed class AosRuntime
{
    public Dictionary<string, AosValue> Env { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "math" };
    public HashSet<string> ReadOnlyBindings { get; } = new(StringComparer.Ordinal);
    public string ModuleBaseDir { get; set; } = HostFileSystem.GetFullPath(".");
    public Dictionary<string, Dictionary<string, AosValue>> ModuleExports { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ModuleLoading { get; } = new(StringComparer.Ordinal);
    public Stack<Dictionary<string, AosValue>> ExportScopes { get; } = new();
    public bool TraceEnabled { get; set; }
    public List<AosNode> TraceSteps { get; } = new();
    public AosNode? Program { get; set; }
    public VmNetworkState Network { get; } = new();
}
