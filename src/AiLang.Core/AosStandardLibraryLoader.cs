namespace AiLang.Core;

public static class AosStandardLibraryLoader
{
    private static readonly Lazy<AosNode> RouteProgram = new(LoadRouteProgram);
    private static readonly Lazy<AosNode> JsonProgram = new(LoadJsonProgram);

    public static void EnsureLoaded(AosRuntime runtime, AosInterpreter interpreter)
    {
        if (runtime.Env.ContainsKey("compiler.route"))
        {
            EnsureJsonLoaded(runtime, interpreter);
            return;
        }

        var result = interpreter.EvaluateProgram(RouteProgram.Value, runtime);
        if (result.Kind == AosValueKind.Node && result.AsNode().Kind == "Err")
        {
            throw new InvalidOperationException("route.aos evaluation failed.");
        }

        if (!runtime.Env.ContainsKey("compiler.route"))
        {
            throw new InvalidOperationException("route.aos did not define compiler.route.");
        }

        EnsureJsonLoaded(runtime, interpreter);
    }

    private static void EnsureJsonLoaded(AosRuntime runtime, AosInterpreter interpreter)
    {
        if (runtime.Env.ContainsKey("std.json.stringify"))
        {
            return;
        }

        var result = interpreter.EvaluateProgram(JsonProgram.Value, runtime);
        if (result.Kind == AosValueKind.Node && result.AsNode().Kind == "Err")
        {
            throw new InvalidOperationException("json.aos evaluation failed.");
        }

        if (!runtime.Env.ContainsKey("std.json.stringify"))
        {
            throw new InvalidOperationException("json.aos did not define std.json.stringify.");
        }
    }

    private static AosNode LoadRouteProgram()
    {
        return AosCompilerAssets.LoadRequiredProgram("route.aos");
    }

    private static AosNode LoadJsonProgram()
    {
        return AosCompilerAssets.LoadRequiredProgram("json.aos");
    }

}
