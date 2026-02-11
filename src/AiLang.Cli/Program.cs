using AiLang.Core;
using System.Text;

Environment.ExitCode = RunCli(args);
return;

static int RunCli(string[] args)
{
    var traceEnabled = args.Contains("--trace", StringComparer.Ordinal);
    string vmMode = "bytecode";
    var filtered = new List<string>();
    foreach (var arg in args)
    {
        if (string.Equals(arg, "--trace", StringComparison.Ordinal))
        {
            continue;
        }
        if (arg.StartsWith("--vm=", StringComparison.Ordinal))
        {
            vmMode = arg["--vm=".Length..];
            continue;
        }

        filtered.Add(arg);
    }

    var filteredArgs = filtered.ToArray();

    if (TryLoadEmbeddedPayload(out var embeddedPayload, out var embeddedBytecodePayload))
    {
        return embeddedBytecodePayload
            ? RunEmbeddedBytecode(embeddedPayload!, filteredArgs, traceEnabled)
            : RunEmbeddedBundle(embeddedPayload!, filteredArgs, traceEnabled, vmMode);
    }

    if (filteredArgs.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    switch (filteredArgs[0])
    {
        case "repl":
            return RunRepl();
        case "run":
            if (filteredArgs.Length < 2)
            {
                PrintUsage();
                return 1;
            }
            return RunSource(filteredArgs[1], filteredArgs.Skip(2).ToArray(), traceEnabled, vmMode);
        case "serve":
            if (filteredArgs.Length < 2)
            {
                PrintUsage();
                return 1;
            }
            if (!CliHttpServe.TryParseServeOptions(filteredArgs.Skip(2).ToArray(), out var port, out var appArgs))
            {
                PrintUsage();
                return 1;
            }

            return RunServe(filteredArgs[1], appArgs, port, traceEnabled, vmMode);
        default:
            PrintUsage();
            return 1;
    }
}

static int RunRepl()
{
    var session = new AosReplSession();
    string? line;
    while ((line = Console.ReadLine()) is not null)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var output = session.ExecuteLine(line);
        Console.WriteLine(output);
    }

    return 0;
}

static int RunSource(string path, string[] argv, bool traceEnabled, string vmMode)
{
    try
    {
        var evaluateProgram = !string.Equals(vmMode, "bytecode", StringComparison.Ordinal);
        if (!TryLoadProgramForExecution(path, traceEnabled, argv, evaluateProgram, vmMode, out _, out var runtime, out var errCode, out var errMessage, out var errNodeId))
        {
            Console.WriteLine(FormatErr("err1", errCode, errMessage, errNodeId));
            return errCode.StartsWith("PAR", StringComparison.Ordinal) || errCode.StartsWith("VAL", StringComparison.Ordinal) || errCode == "RUN002" ? 2 : 3;
        }

        var traceSnapshot = traceEnabled ? new List<AosNode>(runtime!.TraceSteps) : null;
        var result = ExecuteRuntimeStart(runtime!, BuildKernelRunArgs());
        var suppressOutput = runtime!.Env.TryGetValue("__runtime_suppress_output", out var suppressValue) &&
                             suppressValue.Kind == AosValueKind.Bool &&
                             suppressValue.AsBool();
        if (!suppressOutput && TryGetProgramNode(runtime, out var programNode))
        {
            suppressOutput = IsLifecycleProgram(programNode!);
        }

        if (IsErrNode(result, out var errNode))
        {
            Console.WriteLine(AosFormatter.Format(errNode!));
            return 3;
        }

        if (traceEnabled)
        {
            Console.WriteLine(FormatTrace("trace1", traceSnapshot ?? runtime!.TraceSteps));
        }
        else if (!suppressOutput)
        {
            Console.WriteLine(FormatOk("ok1", result));
        }
        return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
    }
    catch (AosProcessExitException exit)
    {
        return exit.Code;
    }
    catch (Exception ex)
    {
        Console.WriteLine(FormatErr("err1", "RUN001", ex.Message, "unknown"));
        return 3;
    }
}

static int RunServe(string path, string[] argv, int port, bool traceEnabled, string vmMode)
{
    try
    {
        if (!TryLoadProgramForExecution(path, traceEnabled, argv, evaluateProgram: true, vmMode, out _, out var runtime, out var errCode, out var errMessage, out var errNodeId))
        {
            Console.WriteLine(FormatErr("err1", errCode, errMessage, errNodeId));
            return errCode.StartsWith("PAR", StringComparison.Ordinal) || errCode.StartsWith("VAL", StringComparison.Ordinal) || errCode == "RUN002" ? 2 : 3;
        }

        runtime!.Permissions.Add("sys");
        var result = ExecuteRuntimeStart(runtime, BuildKernelServeArgs(port));
        if (IsErrNode(result, out var errNode))
        {
            Console.WriteLine(AosFormatter.Format(errNode!));
            return 3;
        }

        if (traceEnabled)
        {
            Console.WriteLine(FormatTrace("trace1", runtime.TraceSteps));
        }
        return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
    }
    catch (AosProcessExitException exit)
    {
        return exit.Code;
    }
    catch (Exception ex)
    {
        Console.WriteLine(FormatErr("err1", "RUN001", ex.Message, "unknown"));
        return 3;
    }
}

static AosNode? LoadRuntimeKernel()
{
    var searchRoots = new[]
    {
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory(),
        Path.Combine(Directory.GetCurrentDirectory(), "src", "compiler"),
        Path.Combine(Directory.GetCurrentDirectory(), "compiler")
    };

    string? path = null;
    foreach (var root in searchRoots)
    {
        var candidate = Path.Combine(root, "runtime.aos");
        if (File.Exists(candidate))
        {
            path = candidate;
            break;
        }
    }

    if (path is null)
    {
        return null;
    }

    var parse = Parse(File.ReadAllText(path));
    if (parse.Root is null || parse.Diagnostics.Count > 0)
    {
        return null;
    }

    return parse.Root.Kind == "Program" ? parse.Root : null;
}

static AosNode BuildKernelServeArgs(int port)
{
    var children = new List<AosNode>
    {
        new(
            "Lit",
            "karg0",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["value"] = new AosAttrValue(AosAttrKind.String, "serve")
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
        new(
            "Lit",
            "karg1",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["value"] = new AosAttrValue(AosAttrKind.Int, port)
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
    };

    return new AosNode(
        "Block",
        "kargv",
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
        children,
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
}

static AosNode BuildKernelRunArgs()
{
    return new AosNode(
        "Block",
        "kargv_run",
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
        new List<AosNode>
        {
            new(
                "Lit",
                "karg_run0",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["value"] = new AosAttrValue(AosAttrKind.String, "run")
                },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
        },
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
}

static AosValue ExecuteRuntimeStart(AosRuntime runtime, AosNode kernelArgs)
{
    var runtimeKernel = LoadRuntimeKernel();
    if (runtimeKernel is null)
    {
        throw new InvalidOperationException("runtime.aos not found.");
    }

    var interpreter = new AosInterpreter();
    runtime.Permissions.Add("sys");
    var kernelInit = interpreter.EvaluateProgram(runtimeKernel, runtime);
    if (IsErrNode(kernelInit, out var kernelErr))
    {
        return AosValue.FromNode(kernelErr!);
    }

    runtime.Env["__kernel_args"] = AosValue.FromNode(kernelArgs);
    var call = new AosNode(
        "Call",
        "kernel_call",
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["target"] = new AosAttrValue(AosAttrKind.Identifier, "runtime.start")
        },
        new List<AosNode>
        {
            new(
                "Var",
                "kernel_args",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__kernel_args")
                },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
        },
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

    return interpreter.EvaluateExpression(call, runtime);
}


static bool TryLoadProgramForExecution(
    string path,
    bool traceEnabled,
    string[] argv,
    bool evaluateProgram,
    string vmMode,
    out AosNode? program,
    out AosRuntime? runtime,
    out string errCode,
    out string errMessage,
    out string errNodeId)
{
    program = null;
    runtime = null;
    errCode = string.Empty;
    errMessage = string.Empty;
    errNodeId = "unknown";

    var source = File.ReadAllText(path);
    var parse = Parse(source);
    if (parse.Root is null || parse.Diagnostics.Count > 0)
    {
        var diagnostic = parse.Diagnostics.FirstOrDefault() ?? new AosDiagnostic("PAR000", "Parse failed.", "unknown", null);
        errCode = diagnostic.Code;
        errMessage = diagnostic.Message;
        errNodeId = diagnostic.NodeId ?? "unknown";
        return false;
    }

    if (parse.Root.Kind != "Program")
    {
        errCode = "RUN002";
        errMessage = "run expects Program root.";
        errNodeId = parse.Root.Id;
        return false;
    }

    runtime = new AosRuntime();
    runtime.Permissions.Add("console");
    runtime.Permissions.Add("io");
    runtime.Permissions.Add("compiler");
    runtime.ModuleBaseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
    runtime.TraceEnabled = false;
    runtime.Env["argv"] = AosValue.FromNode(BuildArgvNode(argv));
    runtime.ReadOnlyBindings.Add("argv");
    runtime.Env["__vm_mode"] = AosValue.FromString(vmMode);
    runtime.ReadOnlyBindings.Add("__vm_mode");
    runtime.Env["__program"] = AosValue.FromNode(parse.Root);
    runtime.ReadOnlyBindings.Add("__program");
    var bootstrapInterpreter = new AosInterpreter();
    AosStandardLibraryLoader.EnsureLoaded(runtime, bootstrapInterpreter);
    runtime.TraceEnabled = traceEnabled;

    var envTypes = new Dictionary<string, AosValueKind>(StringComparer.Ordinal)
    {
        ["argv"] = AosValueKind.Node
    };

    var validator = new AosValidator();
    var validation = validator.Validate(parse.Root, envTypes, runtime.Permissions, runStructural: false);
    if (validation.Diagnostics.Count > 0)
    {
        var diagnostic = validation.Diagnostics[0];
        errCode = diagnostic.Code;
        errMessage = diagnostic.Message;
        errNodeId = diagnostic.NodeId ?? "unknown";
        return false;
    }

    if (evaluateProgram)
    {
        var initResult = bootstrapInterpreter.EvaluateProgram(parse.Root, runtime);
        if (IsErrNode(initResult, out var errNode))
        {
            errCode = errNode!.Attrs.TryGetValue("code", out var codeAttr) && codeAttr.Kind == AosAttrKind.Identifier ? codeAttr.AsString() : "RUN001";
            errMessage = errNode.Attrs.TryGetValue("message", out var messageAttr) && messageAttr.Kind == AosAttrKind.String ? messageAttr.AsString() : "Runtime error.";
            errNodeId = errNode.Attrs.TryGetValue("nodeId", out var nodeIdAttr) && nodeIdAttr.Kind == AosAttrKind.Identifier ? nodeIdAttr.AsString() : "unknown";
            return false;
        }
        runtime.Env["__program_result"] = initResult;
        runtime.ReadOnlyBindings.Add("__program_result");
    }

    program = parse.Root;
    return true;
}

static AosParseResult Parse(string source)
{
    return AosExternalFrontend.Parse(source);
}

static int RunEmbeddedBundle(string bundleText, string[] cliArgs, bool traceEnabled, string vmMode)
{
    try
    {
        var parse = Parse(bundleText);
        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var diagnostic = parse.Diagnostics.FirstOrDefault() ?? new AosDiagnostic("BND001", "Embedded bundle parse failed.", "bundle", null);
            Console.WriteLine(FormatErr("err1", diagnostic.Code, diagnostic.Message, diagnostic.NodeId ?? "bundle"));
            return 3;
        }

        if (parse.Root.Kind != "Bundle")
        {
            Console.WriteLine(FormatErr("err1", "BND002", "Embedded payload is not a Bundle node.", parse.Root.Id));
            return 3;
        }

        if (!TryGetBundleAttr(parse.Root, "entryFile", out var entryFile) ||
            !TryGetBundleAttr(parse.Root, "entryExport", out var entryExport))
        {
            Console.WriteLine(FormatErr("err1", "BND003", "Bundle is missing required attributes.", parse.Root.Id));
            return 3;
        }

        var runtime = new AosRuntime();
        runtime.Permissions.Add("console");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.TraceEnabled = traceEnabled;
        runtime.ModuleBaseDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
        runtime.Env["argv"] = AosValue.FromNode(BuildArgvNode(cliArgs));
        runtime.ReadOnlyBindings.Add("argv");
        var bundleVmMode = string.Equals(vmMode, "bytecode", StringComparison.Ordinal) ? "ast" : vmMode;
        runtime.Env["__vm_mode"] = AosValue.FromString(bundleVmMode);
        runtime.ReadOnlyBindings.Add("__vm_mode");
        runtime.Env["__entryArgs"] = AosValue.FromNode(BuildArgvNode(cliArgs));
        runtime.ReadOnlyBindings.Add("__entryArgs");
        var bootstrapInterpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(runtime, bootstrapInterpreter);

        var driverProgram = new AosNode(
            "Program",
            "embedded_program",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            new List<AosNode>
            {
                new(
                    "Import",
                    "embedded_import",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["path"] = new AosAttrValue(AosAttrKind.String, entryFile)
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
                new(
                    "Var",
                    "embedded_export",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, entryExport)
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
            },
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

        var exportValue = bootstrapInterpreter.EvaluateProgram(driverProgram, runtime);
        if (IsErrNode(exportValue, out var exportErr))
        {
            Console.WriteLine(AosFormatter.Format(exportErr!));
            return 3;
        }

        runtime.Env["__program"] = AosValue.FromNode(driverProgram);
        runtime.ReadOnlyBindings.Add("__program");
        runtime.Env["__program_result"] = exportValue;
        runtime.ReadOnlyBindings.Add("__program_result");
        runtime.Env["__entryExport"] = AosValue.FromString(entryExport);
        runtime.ReadOnlyBindings.Add("__entryExport");

        var result = ExecuteRuntimeStart(runtime, BuildKernelRunArgs());

        if (IsErrNode(result, out var errNode))
        {
            Console.WriteLine(AosFormatter.Format(errNode!));
            return 3;
        }

        if (traceEnabled)
        {
            Console.WriteLine(FormatTrace("trace1", runtime.TraceSteps));
        }

        return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine(FormatErr("err1", "BND004", ex.Message, "bundle"));
        return 3;
    }
}

static int RunEmbeddedBytecode(string bytecodeText, string[] cliArgs, bool traceEnabled)
{
    try
    {
        var parse = Parse(bytecodeText);
        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var diagnostic = parse.Diagnostics.FirstOrDefault() ?? new AosDiagnostic("BND001", "Embedded bytecode parse failed.", "bundle", null);
            Console.WriteLine(FormatErr("err1", diagnostic.Code, diagnostic.Message, diagnostic.NodeId ?? "bundle"));
            return 3;
        }

        if (parse.Root.Kind != "Bytecode")
        {
            Console.WriteLine(FormatErr("err1", "BND005", "Embedded payload is not Bytecode.", parse.Root.Id));
            return 3;
        }

        var runtime = new AosRuntime();
        runtime.Permissions.Add("console");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.TraceEnabled = traceEnabled;
        runtime.Env["argv"] = AosValue.FromNode(BuildArgvNode(cliArgs));
        runtime.ReadOnlyBindings.Add("argv");

        var interpreter = new AosInterpreter();
        var result = interpreter.RunBytecode(parse.Root, "main", BuildArgvNode(cliArgs), runtime);
        if (IsErrNode(result, out var errNode))
        {
            Console.WriteLine(AosFormatter.Format(errNode!));
            return 3;
        }

        if (traceEnabled)
        {
            Console.WriteLine(FormatTrace("trace1", runtime.TraceSteps));
        }

        return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine(FormatErr("err1", "BND006", ex.Message, "bundle"));
        return 3;
    }
}

static bool TryLoadEmbeddedPayload(out string? payloadText, out bool isBytecodePayload)
{
    payloadText = null;
    isBytecodePayload = false;
    var processPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
    {
        return false;
    }

    var markerAst = Encoding.UTF8.GetBytes("\n--AIBUNDLE1--\n");
    var markerBytecode = Encoding.UTF8.GetBytes("\n--AIBUNDLE1:BYTECODE--\n");
    var bytes = File.ReadAllBytes(processPath);
    var astIndex = LastIndexOf(bytes, markerAst);
    var bytecodeIndex = LastIndexOf(bytes, markerBytecode);
    if (astIndex < 0 && bytecodeIndex < 0)
    {
        return false;
    }

    var marker = astIndex >= bytecodeIndex ? markerAst : markerBytecode;
    var markerIndex = astIndex >= bytecodeIndex ? astIndex : bytecodeIndex;
    isBytecodePayload = astIndex < bytecodeIndex;
    var start = markerIndex + marker.Length;
    if (start >= bytes.Length)
    {
        return false;
    }

    payloadText = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    return true;
}

static int LastIndexOf(byte[] haystack, byte[] needle)
{
    for (var i = haystack.Length - needle.Length; i >= 0; i--)
    {
        var match = true;
        for (var j = 0; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j])
            {
                match = false;
                break;
            }
        }

        if (match)
        {
            return i;
        }
    }

    return -1;
}


static bool TryGetBundleAttr(AosNode bundle, string key, out string value)
{
    value = string.Empty;
    if (!bundle.Attrs.TryGetValue(key, out var attr) || attr.Kind != AosAttrKind.String)
    {
        return false;
    }
    value = attr.AsString();
    return !string.IsNullOrEmpty(value);
}

static bool IsErrNode(AosValue value, out AosNode? errNode)
{
    errNode = null;
    if (value.Kind != AosValueKind.Node)
    {
        return false;
    }
    var node = value.AsNode();
    if (node.Kind != "Err")
    {
        return false;
    }
    errNode = node;
    return true;
}

static bool TryGetProgramNode(AosRuntime runtime, out AosNode? programNode)
{
    programNode = null;
    if (!runtime.Env.TryGetValue("__program", out var programValue) || programValue.Kind != AosValueKind.Node)
    {
        return false;
    }
    programNode = programValue.AsNode();
    return true;
}

static bool IsLifecycleProgram(AosNode program)
{
    var hasInit = false;
    var hasUpdate = false;
    foreach (var child in program.Children)
    {
        if (child.Kind != "Export")
        {
            continue;
        }
        if (!child.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
        {
            continue;
        }

        var name = nameAttr.AsString();
        if (string.Equals(name, "init", StringComparison.Ordinal))
        {
            hasInit = true;
        }
        else if (string.Equals(name, "update", StringComparison.Ordinal))
        {
            hasUpdate = true;
        }
    }

    return hasInit && hasUpdate;
}

static string FormatOk(string id, AosValue value)
{
    var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
    {
        ["type"] = new AosAttrValue(AosAttrKind.Identifier, value.Kind.ToString().ToLowerInvariant())
    };

    if (value.Kind == AosValueKind.String)
    {
        attrs["value"] = new AosAttrValue(AosAttrKind.String, value.AsString());
    }
    else if (value.Kind == AosValueKind.Int)
    {
        attrs["value"] = new AosAttrValue(AosAttrKind.Int, value.AsInt());
    }
    else if (value.Kind == AosValueKind.Bool)
    {
        attrs["value"] = new AosAttrValue(AosAttrKind.Bool, value.AsBool());
    }

    var node = new AosNode(
        "Ok",
        id,
        attrs,
        new List<AosNode>(),
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    return AosFormatter.Format(node);
}

static string FormatTrace(string id, List<AosNode> steps)
{
    var trace = new AosNode(
        "Trace",
        id,
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
        new List<AosNode>(steps),
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    return AosFormatter.Format(trace);
}

static string FormatErr(string id, string code, string message, string nodeId)
{
    var node = new AosNode(
        "Err",
        id,
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["code"] = new AosAttrValue(AosAttrKind.Identifier, code),
            ["message"] = new AosAttrValue(AosAttrKind.String, message),
            ["nodeId"] = new AosAttrValue(AosAttrKind.Identifier, nodeId)
        },
        new List<AosNode>(),
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    return AosFormatter.Format(node);
}

static void PrintUsage()
{
    Console.WriteLine("Usage: airun repl | airun run <path.aos> | airun serve <path.aos> [--port <n>] [--vm=bytecode|ast]");
}

static AosNode BuildArgvNode(string[] values)
{
    var children = new List<AosNode>(values.Length);
    for (var i = 0; i < values.Length; i++)
    {
        children.Add(new AosNode(
            "Lit",
            $"argv{i}",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["value"] = new AosAttrValue(AosAttrKind.String, values[i])
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
    }

    return new AosNode(
        "Block",
        "argv",
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
        children,
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
}
