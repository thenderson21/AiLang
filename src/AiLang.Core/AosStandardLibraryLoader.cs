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
        if (runtime.Env.ContainsKey("compiler.toJson"))
        {
            return;
        }

        var result = interpreter.EvaluateProgram(JsonProgram.Value, runtime);
        if (result.Kind == AosValueKind.Node && result.AsNode().Kind == "Err")
        {
            throw new InvalidOperationException("json.aos evaluation failed.");
        }

        if (!runtime.Env.ContainsKey("compiler.toJson"))
        {
            throw new InvalidOperationException("json.aos did not define compiler.toJson.");
        }
    }

    private static AosNode LoadRouteProgram()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.Combine(AppContext.BaseDirectory, "src", "compiler"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "compiler"),
            Path.Combine(AppContext.BaseDirectory, "compiler"),
            Path.Combine(Directory.GetCurrentDirectory(), "compiler")
        };

        string? path = null;
        foreach (var root in searchRoots)
        {
            var candidate = Path.Combine(root, "route.aos");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path is null)
        {
            throw new FileNotFoundException("route.aos not found.");
        }

        var parse = AosParsing.ParseFile(path);
        if (parse.Root is null)
        {
            throw new InvalidOperationException("Failed to parse route.aos.");
        }

        if (parse.Root.Kind != "Program")
        {
            throw new InvalidOperationException("route.aos must contain a Program node.");
        }

        if (parse.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"route.aos parse error: {parse.Diagnostics[0].Message}");
        }

        return parse.Root;
    }

    private static AosNode LoadJsonProgram()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.Combine(AppContext.BaseDirectory, "src", "compiler"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "compiler"),
            Path.Combine(AppContext.BaseDirectory, "compiler"),
            Path.Combine(Directory.GetCurrentDirectory(), "compiler")
        };

        string? path = null;
        foreach (var root in searchRoots)
        {
            var candidate = Path.Combine(root, "json.aos");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path is null)
        {
            throw new FileNotFoundException("json.aos not found.");
        }

        var parse = AosParsing.ParseFile(path);
        if (parse.Root is null)
        {
            throw new InvalidOperationException("Failed to parse json.aos.");
        }

        if (parse.Root.Kind != "Program")
        {
            throw new InvalidOperationException("json.aos must contain a Program node.");
        }

        if (parse.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"json.aos parse error: {parse.Diagnostics[0].Message}");
        }

        return parse.Root;
    }

}
