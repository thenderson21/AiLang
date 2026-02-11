using AiLang.Core;
using System.Net;
using System.Net.Sockets;
using System.Text;

Environment.ExitCode = RunCli(args);
return;

static int RunCli(string[] args)
{
    var traceEnabled = args.Contains("--trace", StringComparer.Ordinal);
    var filteredArgs = args.Where(a => !string.Equals(a, "--trace", StringComparison.Ordinal)).ToArray();

    if (TryLoadEmbeddedBundle(out var embeddedBundleText))
    {
        return RunEmbeddedBundle(embeddedBundleText!, filteredArgs, traceEnabled);
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
            return RunSource(filteredArgs[1], filteredArgs.Skip(2).ToArray(), traceEnabled);
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

            return RunServe(filteredArgs[1], appArgs, port, traceEnabled);
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

static int RunSource(string path, string[] argv, bool traceEnabled)
{
    try
    {
        if (!TryLoadProgramForExecution(path, traceEnabled, argv, evaluateProgram: false, out var parseRoot, out var runtime, out var errCode, out var errMessage, out var errNodeId))
        {
            Console.WriteLine(FormatErr("err1", errCode, errMessage, errNodeId));
            return errCode.StartsWith("PAR", StringComparison.Ordinal) || errCode.StartsWith("VAL", StringComparison.Ordinal) || errCode == "RUN002" ? 2 : 3;
        }

        var interpreter = new AosInterpreter();
        var execution = ExecuteCliProgram(interpreter, runtime!, parseRoot!);
        var result = execution.Value;
        if (traceEnabled)
        {
            Console.WriteLine(FormatTrace("trace1", runtime!.TraceSteps));
        }
        else if (!execution.SuppressOutput)
        {
            Console.WriteLine(FormatOk("ok1", result));
        }
        return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine(FormatErr("err1", "RUN001", ex.Message, "unknown"));
        return 3;
    }
}

static int RunServe(string path, string[] argv, int port, bool traceEnabled)
{
    try
    {
        if (!TryLoadProgramForExecution(path, traceEnabled, argv, evaluateProgram: true, out var parseRoot, out var runtime, out var errCode, out var errMessage, out var errNodeId))
        {
            Console.WriteLine(FormatErr("err1", errCode, errMessage, errNodeId));
            return errCode.StartsWith("PAR", StringComparison.Ordinal) || errCode.StartsWith("VAL", StringComparison.Ordinal) || errCode == "RUN002" ? 2 : 3;
        }

        if (!HasExport(parseRoot!, "init") || !HasExport(parseRoot!, "update") ||
            !HasFunctionBinding(runtime!, "init") || !HasFunctionBinding(runtime!, "update"))
        {
            Console.WriteLine(FormatErr("err0", "HTTP001", "App must export init and update.", "app"));
            return 1;
        }

        var interpreter = new AosInterpreter();
        var state = InvokeNamedFunction(interpreter, runtime, "init", runtime.Env["argv"], traceName: "init");
        if (IsErrNode(state, out var initErr))
        {
            Console.WriteLine(AosFormatter.Format(initErr!));
            return 3;
        }

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var commandExecutor = new ServeCommandExecutor();
        var exitCode = 0;

        while (true)
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();

            if (!CliHttpServe.TryReadHttpRequestLine(stream, out var method, out var requestPath))
            {
                CliHttpServe.WriteHttpResponse(stream, null);
                continue;
            }

            if (traceEnabled)
            {
                runtime.TraceSteps.Clear();
            }

            var eventNode = CliHttpServe.CreateHttpRequestEvent(method, requestPath);

            if (traceEnabled)
            {
                CliHttpServe.AppendEventDispatchTrace(runtime, eventNode);
            }

            var next = RuntimeDispatch(interpreter, runtime, AosValue.FromNode(eventNode), state);
            if (IsErrNode(next, out var updateErr))
            {
                Console.WriteLine(AosFormatter.Format(updateErr!));
                listener.Stop();
                return 3;
            }

            commandExecutor.Reset();
            if (TryUnpackLifecycleBlock(next, runtime, out var nextState, out var commands))
            {
                state = nextState;
                foreach (var command in commands)
                {
                    if (traceEnabled)
                    {
                        CliHttpServe.AppendCommandExecuteTrace(runtime, command);
                    }

                    var commandExitCode = commandExecutor.Execute(command);
                    if (commandExitCode is not null)
                    {
                        exitCode = commandExitCode.Value;
                        break;
                    }
                }
            }
            else if (TryGetExitCode(next, out var immediateExit))
            {
                exitCode = immediateExit;
            }
            else
            {
                state = next;
            }

            CliHttpServe.WriteHttpResponse(stream, commandExecutor.HttpResponsePayload);
            if (traceEnabled)
            {
                Console.WriteLine(FormatTrace("trace1", runtime.TraceSteps));
            }

            if (exitCode != 0 || commandExecutor.ExitRequested)
            {
                listener.Stop();
                return exitCode;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(FormatErr("err1", "RUN001", ex.Message, "unknown"));
        return 3;
    }
}


static bool TryLoadProgramForExecution(
    string path,
    bool traceEnabled,
    string[] argv,
    bool evaluateProgram,
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
    runtime.TraceEnabled = traceEnabled;
    runtime.Env["argv"] = AosValue.FromNode(BuildArgvNode(argv));
    runtime.ReadOnlyBindings.Add("argv");
    var bootstrapInterpreter = new AosInterpreter();
    AosStandardLibraryLoader.EnsureRouteLoaded(runtime, bootstrapInterpreter);

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
    }

    program = parse.Root;
    return true;
}

static AosParseResult Parse(string source)
{
    return AosExternalFrontend.Parse(source);
}

static int RunEmbeddedBundle(string bundleText, string[] cliArgs, bool traceEnabled)
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
        runtime.Env["__entryArgs"] = AosValue.FromNode(BuildArgvNode(cliArgs));
        runtime.ReadOnlyBindings.Add("__entryArgs");
        var bootstrapInterpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureRouteLoaded(runtime, bootstrapInterpreter);

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

        var execution = ExecuteModuleEntry(bootstrapInterpreter, runtime, entryExport, exportValue, "__entryArgs");
        var result = execution.Value;

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

static bool TryLoadEmbeddedBundle(out string? bundleText)
{
    bundleText = null;
    var processPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
    {
        return false;
    }

    var marker = Encoding.UTF8.GetBytes("\n--AIBUNDLE1--\n");
    var bytes = File.ReadAllBytes(processPath);
    var markerIndex = LastIndexOf(bytes, marker);
    if (markerIndex < 0)
    {
        return false;
    }

    var start = markerIndex + marker.Length;
    if (start >= bytes.Length)
    {
        return false;
    }

    bundleText = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
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

static (AosValue Value, bool SuppressOutput) ExecuteCliProgram(AosInterpreter interpreter, AosRuntime runtime, AosNode program)
{
    var programResult = interpreter.EvaluateProgram(program, runtime);
    if (IsErrNode(programResult, out _))
    {
        return (programResult, false);
    }

    if (HasExport(program, "init") && HasExport(program, "update") &&
        HasFunctionBinding(runtime, "init") && HasFunctionBinding(runtime, "update"))
    {
        var lifecycle = ExecuteLifecycle(interpreter, runtime, "argv");
        return (lifecycle, true);
    }

    if (HasExport(program, "start") && HasFunctionBinding(runtime, "start"))
    {
        var startResult = InvokeNamedFunction(interpreter, runtime, "start", runtime.Env["argv"], traceName: null);
        return (startResult, false);
    }

    return (programResult, false);
}

static (AosValue Value, bool SuppressOutput) ExecuteModuleEntry(
    AosInterpreter interpreter,
    AosRuntime runtime,
    string entryExport,
    AosValue exportValue,
    string argBindingName)
{
    if (HasFunctionBinding(runtime, "init") && HasFunctionBinding(runtime, "update"))
    {
        var lifecycle = ExecuteLifecycle(interpreter, runtime, argBindingName);
        return (lifecycle, true);
    }

    if (HasFunctionBinding(runtime, entryExport))
    {
        var args = runtime.Env.TryGetValue(argBindingName, out var argValue) ? argValue : AosValue.FromNode(BuildArgvNode(Array.Empty<string>()));
        var callResult = InvokeNamedFunction(interpreter, runtime, entryExport, args, traceName: null);
        return (callResult, false);
    }

    return (exportValue, false);
}

static AosValue ExecuteLifecycle(AosInterpreter interpreter, AosRuntime runtime, string argBindingName)
{
    if (!runtime.Env.TryGetValue(argBindingName, out var argValue))
    {
        argValue = AosValue.FromNode(BuildArgvNode(Array.Empty<string>()));
    }

    var state = InvokeNamedFunction(interpreter, runtime, "init", argValue, traceName: "init");
    if (IsErrNode(state, out _))
    {
        return state;
    }

    var eventSource = CliAdapters.CreateEventSource(argValue);
    ICommandExecutor commandExecutor = new CliCommandExecutor();

    while (true)
    {
        var nextEvent = eventSource.NextEvent();
        if (nextEvent is null)
        {
            break;
        }

        if (runtime.TraceEnabled)
        {
            runtime.TraceSteps.Add(new AosNode(
                "Step",
                "auto",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["kind"] = new AosAttrValue(AosAttrKind.String, "EventDispatch"),
                    ["nodeId"] = new AosAttrValue(AosAttrKind.String, nextEvent.Id)
                },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
        }

        var next = RuntimeDispatch(interpreter, runtime, AosValue.FromNode(nextEvent), state);
        if (IsErrNode(next, out _))
        {
            return next;
        }

        if (TryUnpackLifecycleBlock(next, runtime, out var nextState, out var commands))
        {
            state = nextState;
            foreach (var command in commands)
            {
                if (runtime.TraceEnabled)
                {
                    runtime.TraceSteps.Add(new AosNode(
                        "Step",
                        "auto",
                        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                        {
                            ["kind"] = new AosAttrValue(AosAttrKind.String, "CommandExecute"),
                            ["nodeId"] = new AosAttrValue(AosAttrKind.String, command.Id)
                        },
                        new List<AosNode>(),
                        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
                }

                var commandExitCode = commandExecutor.Execute(command);
                if (commandExitCode is not null)
                {
                    return AosValue.FromInt(commandExitCode.Value);
                }
            }

            break;
        }

        if (TryGetExitCode(next, out var exitCode))
        {
            return AosValue.FromInt(exitCode);
        }

        state = next;
        break;
    }
    return AosValue.Void;
}

static AosValue RuntimeDispatch(AosInterpreter interpreter, AosRuntime runtime, AosValue @event, AosValue state)
{
    return InvokeNamedFunction(interpreter, runtime, "update", state, @event, traceName: "update");
}

static AosValue InvokeNamedFunction(
    AosInterpreter interpreter,
    AosRuntime runtime,
    string functionName,
    AosValue arg0,
    AosValue? arg1 = null,
    string? traceName = null)
{
    var bindings = new List<(string Name, AosValue Value)> { ("__entry_arg0", arg0) };
    if (arg1 is not null)
    {
        bindings.Add(("__entry_arg1", arg1));
    }

    var previous = new Dictionary<string, AosValue>(StringComparer.Ordinal);
    foreach (var (name, value) in bindings)
    {
        if (runtime.Env.TryGetValue(name, out var prior))
        {
            previous[name] = prior;
        }
        runtime.Env[name] = value;
    }

    var callChildren = new List<AosNode>
    {
        new(
            "Var",
            "entry_arg0",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__entry_arg0")
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
    };
    if (arg1 is not null)
    {
        callChildren.Add(new AosNode(
            "Var",
            "entry_arg1",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__entry_arg1")
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
    }

    var call = new AosNode(
        "Call",
        $"entry_call_{functionName}",
        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["target"] = new AosAttrValue(AosAttrKind.Identifier, functionName)
        },
        callChildren,
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

    AosValue result;
    try
    {
        result = interpreter.EvaluateExpression(call, runtime);
    }
    finally
    {
        foreach (var (name, _) in bindings)
        {
            if (previous.TryGetValue(name, out var prior))
            {
                runtime.Env[name] = prior;
            }
            else
            {
                runtime.Env.Remove(name);
            }
        }
    }

    if (!string.IsNullOrEmpty(traceName) && runtime.TraceEnabled)
    {
        AppendLifecycleTraceStep(runtime, traceName!, result);
    }
    return result;
}

static void AppendLifecycleTraceStep(AosRuntime runtime, string functionName, AosValue value)
{
    var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
    {
        ["kind"] = new AosAttrValue(AosAttrKind.String, "LifecycleCall"),
        ["nodeId"] = new AosAttrValue(AosAttrKind.String, functionName),
        ["returnKind"] = new AosAttrValue(AosAttrKind.String, value.Kind.ToString().ToLowerInvariant())
    };
    if (value.Kind == AosValueKind.String)
    {
        attrs["returnValue"] = new AosAttrValue(AosAttrKind.String, value.AsString());
    }
    else if (value.Kind == AosValueKind.Int)
    {
        attrs["returnValue"] = new AosAttrValue(AosAttrKind.Int, value.AsInt());
    }
    else if (value.Kind == AosValueKind.Bool)
    {
        attrs["returnValue"] = new AosAttrValue(AosAttrKind.Bool, value.AsBool());
    }
    else if (value.Kind == AosValueKind.Node)
    {
        attrs["returnNode"] = new AosAttrValue(AosAttrKind.String, $"{value.AsNode().Kind}#{value.AsNode().Id}");
    }

    runtime.TraceSteps.Add(new AosNode(
        "Step",
        "auto",
        attrs,
        new List<AosNode>(),
        new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
}

static bool TryGetExitCode(AosValue value, out int exitCode)
{
    exitCode = 0;
    if (value.Kind != AosValueKind.Node)
    {
        return false;
    }
    var node = value.AsNode();
    if (node.Kind != "Command" || node.Id != "Exit")
    {
        return false;
    }
    if (!node.Attrs.TryGetValue("code", out var codeAttr) || codeAttr.Kind != AosAttrKind.Int)
    {
        return false;
    }
    exitCode = codeAttr.AsInt();
    return true;
}

static bool TryUnpackLifecycleBlock(AosValue value, AosRuntime runtime, out AosValue state, out List<AosNode> commands)
{
    state = AosValue.Void;
    commands = new List<AosNode>();
    if (value.Kind != AosValueKind.Node)
    {
        return false;
    }

    var node = value.AsNode();
    if (node.Kind != "Block" || node.Children.Count == 0)
    {
        return false;
    }

    if (runtime.TraceEnabled)
    {
        runtime.TraceSteps.Add(new AosNode(
            "Step",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["kind"] = new AosAttrValue(AosAttrKind.String, node.Kind),
                ["nodeId"] = new AosAttrValue(AosAttrKind.String, node.Id)
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
    }

    state = NodeToValue(node.Children[0]);
    for (var i = 1; i < node.Children.Count; i++)
    {
        if (node.Children[i].Kind == "Command")
        {
            commands.Add(node.Children[i]);
        }
    }

    return true;
}

static AosValue NodeToValue(AosNode node)
{
    if (node.Kind == "Lit" && node.Attrs.TryGetValue("value", out var lit))
    {
        return lit.Kind switch
        {
            AosAttrKind.String => AosValue.FromString(lit.AsString()),
            AosAttrKind.Int => AosValue.FromInt(lit.AsInt()),
            AosAttrKind.Bool => AosValue.FromBool(lit.AsBool()),
            _ => AosValue.FromNode(node)
        };
    }

    return AosValue.FromNode(node);
}

static bool HasFunctionBinding(AosRuntime runtime, string name)
    => runtime.Env.TryGetValue(name, out var value) && value.Kind == AosValueKind.Function;

static bool HasExport(AosNode program, string exportName)
{
    foreach (var child in program.Children)
    {
        if (child.Kind != "Export")
        {
            continue;
        }
        if (child.Attrs.TryGetValue("name", out var nameAttr) &&
            nameAttr.Kind == AosAttrKind.Identifier &&
            string.Equals(nameAttr.AsString(), exportName, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
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
    Console.WriteLine("Usage: airun repl | airun run <path.aos> | airun serve <path.aos> [--port <n>]");
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
