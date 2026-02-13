using AiVM.Core;

namespace AiLang.Core;

public static class AosCliExecutionEngine
{
    public static int RunRepl(Func<string?> readLine, Action<string> writeLine)
    {
        var session = new AosReplSession();
        string? line;
        while ((line = readLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var output = session.ExecuteLine(line);
            writeLine(output);
        }

        return 0;
    }

    public static int RunSource(string path, string[] argv, bool traceEnabled, string vmMode, Action<string> writeLine)
    {
        try
        {
            var evaluateProgram = !string.Equals(vmMode, "bytecode", StringComparison.Ordinal);
            if (!TryLoadProgramForExecution(path, traceEnabled, argv, evaluateProgram, vmMode, out var program, out var runtime, out var errCode, out var errMessage, out var errNodeId))
            {
                writeLine(FormatErr("err1", errCode, errMessage, errNodeId));
                return errCode.StartsWith("PAR", StringComparison.Ordinal) || errCode.StartsWith("VAL", StringComparison.Ordinal) || errCode == "RUN002" ? 2 : 3;
            }

            var traceSnapshot = traceEnabled ? new List<AosNode>(runtime!.TraceSteps) : null;
            var result = ExecuteRuntimeStart(runtime!, BuildKernelRunArgs());
            var suppressOutput = (runtime!.Env.TryGetValue("__runtime_suppress_output", out var suppressValue) &&
                                  suppressValue.Kind == AosValueKind.Bool &&
                                  suppressValue.AsBool()) ||
                                 (program is not null &&
                                  HasNamedExport(program, "init") &&
                                  HasNamedExport(program, "update"));

            if (IsErrNode(result, out var errNode))
            {
                writeLine(AosFormatter.Format(errNode!));
                return 3;
            }
            if (result.Kind == AosValueKind.Unknown)
            {
                writeLine(FormatErr("err1", "RUN001", "Runtime returned unknown result.", "runtime.start"));
                return 3;
            }

            if (traceEnabled)
            {
                writeLine(FormatTrace("trace1", traceSnapshot ?? runtime!.TraceSteps));
            }
            else if (!suppressOutput)
            {
                writeLine(FormatOk("ok1", result));
            }

            if (suppressOutput)
            {
                return 0;
            }

            return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
        }
        catch (AosProcessExitException exit)
        {
            return exit.Code;
        }
        catch (Exception ex)
        {
            writeLine(FormatErr("err1", "RUN001", ex.Message, "unknown"));
            return 3;
        }
    }

    public static int RunServe(string path, string[] argv, int port, string tlsCertPath, string tlsKeyPath, bool traceEnabled, string vmMode, Action<string> writeLine)
    {
        try
        {
            if (!TryLoadProgramForExecution(path, traceEnabled, argv, evaluateProgram: true, vmMode, out _, out var runtime, out var errCode, out var errMessage, out var errNodeId))
            {
                writeLine(FormatErr("err1", errCode, errMessage, errNodeId));
                return errCode.StartsWith("PAR", StringComparison.Ordinal) || errCode.StartsWith("VAL", StringComparison.Ordinal) || errCode == "RUN002" ? 2 : 3;
            }

            runtime!.Permissions.Add("sys");
            var result = ExecuteRuntimeStart(runtime, BuildKernelServeArgs(port, tlsCertPath, tlsKeyPath));
            if (IsErrNode(result, out var errNode))
            {
                writeLine(AosFormatter.Format(errNode!));
                return 3;
            }
            if (result.Kind == AosValueKind.Unknown)
            {
                writeLine(FormatErr("err1", "RUN001", "Runtime returned unknown result.", "runtime.start"));
                return 3;
            }

            if (traceEnabled)
            {
                writeLine(FormatTrace("trace1", runtime.TraceSteps));
            }

            return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
        }
        catch (AosProcessExitException exit)
        {
            return exit.Code;
        }
        catch (Exception ex)
        {
            writeLine(FormatErr("err1", "RUN001", ex.Message, "unknown"));
            return 3;
        }
    }

    public static int RunEmbeddedBundle(string bundleText, string[] cliArgs, bool traceEnabled, string vmMode, Action<string> writeLine)
    {
        try
        {
            if (string.Equals(vmMode, "bytecode", StringComparison.Ordinal))
            {
                writeLine(FormatErr("err1", "VM001", "Embedded AST bundle requires --vm=ast.", "bundle"));
                return 3;
            }

            var parse = Parse(bundleText);
            if (parse.Root is null || parse.Diagnostics.Count > 0)
            {
                var diagnostic = parse.Diagnostics.FirstOrDefault() ?? new AosDiagnostic("BND001", "Embedded bundle parse failed.", "bundle", null);
                writeLine(FormatErr("err1", diagnostic.Code, diagnostic.Message, diagnostic.NodeId ?? "bundle"));
                return 3;
            }

            if (parse.Root.Kind != "Bundle")
            {
                writeLine(FormatErr("err1", "BND002", "Embedded payload is not a Bundle node.", parse.Root.Id));
                return 3;
            }

            if (!TryGetBundleAttr(parse.Root, "entryFile", out var entryFile) ||
                !TryGetBundleAttr(parse.Root, "entryExport", out var entryExport))
            {
                writeLine(FormatErr("err1", "BND003", "Bundle is missing required attributes.", parse.Root.Id));
                return 3;
            }

            var runtime = new AosRuntime();
            runtime.Permissions.Add("console");
            runtime.Permissions.Add("io");
            runtime.Permissions.Add("compiler");
            runtime.TraceEnabled = traceEnabled;
            runtime.ModuleBaseDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory();
            runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(cliArgs));
            runtime.ReadOnlyBindings.Add("argv");
            runtime.Env["__vm_mode"] = AosValue.FromString(vmMode);
            runtime.ReadOnlyBindings.Add("__vm_mode");
            runtime.Env["__entryArgs"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(cliArgs));
            runtime.ReadOnlyBindings.Add("__entryArgs");
            var bootstrapInterpreter = new AosInterpreter();
            runtime.TraceEnabled = false;
            AosStandardLibraryLoader.EnsureLoaded(runtime, bootstrapInterpreter);
            runtime.TraceEnabled = traceEnabled;

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
                writeLine(AosFormatter.Format(exportErr!));
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
                writeLine(AosFormatter.Format(errNode!));
                return 3;
            }
            if (result.Kind == AosValueKind.Unknown)
            {
                writeLine(FormatErr("err1", "RUN001", "Runtime returned unknown result.", "runtime.start"));
                return 3;
            }

            if (traceEnabled)
            {
                writeLine(FormatTrace("trace1", runtime.TraceSteps));
            }

            return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
        }
        catch (Exception ex)
        {
            writeLine(FormatErr("err1", "BND004", ex.Message, "bundle"));
            return 3;
        }
    }

    public static int RunEmbeddedBytecode(string bytecodeText, string[] cliArgs, bool traceEnabled, Action<string> writeLine)
    {
        try
        {
            var parse = Parse(bytecodeText);
            if (parse.Root is null || parse.Diagnostics.Count > 0)
            {
                var diagnostic = parse.Diagnostics.FirstOrDefault() ?? new AosDiagnostic("BND001", "Embedded bytecode parse failed.", "bundle", null);
                writeLine(FormatErr("err1", diagnostic.Code, diagnostic.Message, diagnostic.NodeId ?? "bundle"));
                return 3;
            }

            if (parse.Root.Kind != "Bytecode")
            {
                writeLine(FormatErr("err1", "BND005", "Embedded payload is not Bytecode.", parse.Root.Id));
                return 3;
            }

            var runtime = new AosRuntime();
            runtime.Permissions.Add("console");
            runtime.Permissions.Add("io");
            runtime.Permissions.Add("compiler");
            runtime.Permissions.Add("sys");
            runtime.TraceEnabled = traceEnabled;
            runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(cliArgs));
            runtime.ReadOnlyBindings.Add("argv");

            var interpreter = new AosInterpreter();
            var result = interpreter.RunBytecode(parse.Root, "main", AosRuntimeNodes.BuildArgvNode(cliArgs), runtime);
            if (IsErrNode(result, out var errNode))
            {
                writeLine(AosFormatter.Format(errNode!));
                return 3;
            }
            if (result.Kind == AosValueKind.Unknown)
            {
                writeLine(FormatErr("err1", "RUN001", "Runtime returned unknown result.", "runtime.start"));
                return 3;
            }

            if (traceEnabled)
            {
                writeLine(FormatTrace("trace1", runtime.TraceSteps));
            }

            return result.Kind == AosValueKind.Int ? result.AsInt() : 0;
        }
        catch (Exception ex)
        {
            writeLine(FormatErr("err1", "BND006", ex.Message, "bundle"));
            return 3;
        }
    }

    public static int RunBench(string[] args, Action<string> writeLine)
    {
        var iterations = 20;
        var human = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--iterations", StringComparison.Ordinal) && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed) && parsed > 0)
            {
                iterations = parsed;
                i++;
                continue;
            }
            if (string.Equals(args[i], "--human", StringComparison.Ordinal))
            {
                human = true;
            }
        }

        var cases = new[]
        {
            ("loop", "examples/bench/loop_compute.aos"),
            ("str_concat", "examples/bench/str_concat.aos"),
            ("map_create", "examples/bench/map_create.aos"),
            ("http_handler", "examples/bench/http_handler.aos"),
            ("lifecycle", "examples/bench/lifecycle_run.aos")
        };

        var caseNodes = new List<AosNode>(cases.Length);
        foreach (var (name, relativePath) in cases)
        {
            caseNodes.Add(RunBenchCase(name, relativePath, iterations));
        }

        var report = new AosNode(
            "Benchmark",
            "bench1",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["iterations"] = new AosAttrValue(AosAttrKind.Int, iterations),
                ["cases"] = new AosAttrValue(AosAttrKind.Int, caseNodes.Count)
            },
            caseNodes,
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

        if (human)
        {
            writeLine("name         status         astTicks   vmTicks    inst  speedup");
            foreach (var node in caseNodes)
            {
                var name = node.Attrs["name"].AsString();
                var status = node.Attrs["status"].AsString();
                var astTicks = node.Attrs["astTicks"].AsInt();
                var vmTicks = node.Attrs["vmTicks"].AsInt();
                var inst = node.Attrs["instructionCount"].AsInt();
                var speedup = vmTicks == 0 ? "n/a" : $"{(double)astTicks / vmTicks:0.00}x";
                writeLine($"{name,-12} {status,-13} {astTicks,9} {vmTicks,9} {inst,5} {speedup,7}");
            }
        }
        else
        {
            writeLine(AosFormatter.Format(report));
        }

        return 0;
    }

    private static AosNode? LoadRuntimeKernel()
    {
        var path = AosCompilerAssets.TryFind("runtime.aos");
        if (path is null)
        {
            return null;
        }

        var parse = Parse(HostFileSystem.ReadAllText(path));
        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            return null;
        }

        return parse.Root.Kind == "Program" ? parse.Root : null;
    }

    private static AosNode BuildKernelServeArgs(int port, string tlsCertPath, string tlsKeyPath)
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
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            new(
                "Lit",
                "karg2",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["value"] = new AosAttrValue(AosAttrKind.String, tlsCertPath)
                },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            new(
                "Lit",
                "karg3",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["value"] = new AosAttrValue(AosAttrKind.String, tlsKeyPath)
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

    private static AosNode BuildKernelRunArgs()
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

    private static AosValue ExecuteRuntimeStart(AosRuntime runtime, AosNode kernelArgs)
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

    private static bool TryLoadProgramForExecution(
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

        if (!TryResolveExecutionSource(
                path,
                out var sourcePath,
                out var moduleBaseDir,
                out var entryExportOverride,
                out var resolvedFromManifest,
                out errCode,
                out errMessage,
                out errNodeId))
        {
            return false;
        }

        var source = File.ReadAllText(sourcePath);
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

        var executionProgram = resolvedFromManifest
            ? BuildManifestExecutionProgram(parse.Root, entryExportOverride)
            : parse.Root;

        runtime = new AosRuntime();
        runtime.Permissions.Add("console");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.ModuleBaseDir = moduleBaseDir;
        runtime.TraceEnabled = false;
        runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(argv));
        runtime.ReadOnlyBindings.Add("argv");
        if (!string.IsNullOrEmpty(entryExportOverride))
        {
            runtime.Env["__entryExport"] = AosValue.FromString(entryExportOverride);
            runtime.ReadOnlyBindings.Add("__entryExport");
        }
        runtime.Env["__vm_mode"] = AosValue.FromString(vmMode);
        runtime.ReadOnlyBindings.Add("__vm_mode");
        runtime.Env["__program"] = AosValue.FromNode(executionProgram);
        runtime.ReadOnlyBindings.Add("__program");
        var bootstrapInterpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(runtime, bootstrapInterpreter);
        runtime.TraceEnabled = traceEnabled;

        var envTypes = new Dictionary<string, AosValueKind>(StringComparer.Ordinal)
        {
            ["argv"] = AosValueKind.Node
        };

        var validator = new AosValidator();
        var validation = validator.Validate(executionProgram, envTypes, runtime.Permissions, runStructural: false);
        if (validation.Diagnostics.Count > 0)
        {
            var diagnostic = validation.Diagnostics[0];
            errCode = diagnostic.Code;
            errMessage = diagnostic.Message;
            errNodeId = diagnostic.NodeId ?? "unknown";
            return false;
        }

        var shouldEvaluateProgram = evaluateProgram ||
                                    (string.Equals(vmMode, "bytecode", StringComparison.Ordinal) &&
                                     HasNamedExport(executionProgram, "init") &&
                                     HasNamedExport(executionProgram, "update"));

        if (shouldEvaluateProgram)
        {
            var initResult = bootstrapInterpreter.EvaluateProgram(executionProgram, runtime);
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

        program = executionProgram;
        return true;
    }

    private static bool TryResolveExecutionSource(
        string path,
        out string sourcePath,
        out string moduleBaseDir,
        out string entryExportOverride,
        out bool resolvedFromManifest,
        out string errCode,
        out string errMessage,
        out string errNodeId)
    {
        sourcePath = path;
        moduleBaseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        entryExportOverride = string.Empty;
        resolvedFromManifest = false;
        errCode = string.Empty;
        errMessage = string.Empty;
        errNodeId = "unknown";

        if (!path.EndsWith(".aiproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!HostFileSystem.FileExists(path))
        {
            errCode = "RUN002";
            errMessage = "project.aiproj not found.";
            errNodeId = "project";
            return false;
        }

        AosParseResult parse;
        try
        {
            parse = AosParsing.ParseFile(path);
        }
        catch (Exception ex)
        {
            errCode = "RUN002";
            errMessage = $"Failed to read project manifest: {ex.Message}";
            errNodeId = "project";
            return false;
        }

        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var diagnostic = parse.Diagnostics.FirstOrDefault() ?? new AosDiagnostic("PAR000", "Parse failed.", "project", null);
            errCode = diagnostic.Code;
            errMessage = diagnostic.Message;
            errNodeId = diagnostic.NodeId ?? "project";
            return false;
        }

        if (!string.Equals(parse.Root.Kind, "Program", StringComparison.Ordinal))
        {
            errCode = "RUN002";
            errMessage = "project.aiproj must contain Program root.";
            errNodeId = parse.Root.Id;
            return false;
        }

        var projectNodes = parse.Root.Children.Where(node => string.Equals(node.Kind, "Project", StringComparison.Ordinal)).ToList();
        if (projectNodes.Count != 1)
        {
            errCode = "RUN002";
            errMessage = "project.aiproj must contain Program with one Project child.";
            errNodeId = parse.Root.Id;
            return false;
        }

        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root, null, new HashSet<string>(StringComparer.Ordinal), runStructural: false);
        if (validation.Diagnostics.Count > 0)
        {
            var diagnostic = validation.Diagnostics[0];
            errCode = diagnostic.Code;
            errMessage = diagnostic.Message;
            errNodeId = diagnostic.NodeId ?? "project";
            return false;
        }

        var project = projectNodes[0];
        if (!project.Attrs.TryGetValue("entryFile", out var entryFileAttr) || entryFileAttr.Kind != AosAttrKind.String)
        {
            errCode = "RUN002";
            errMessage = "Project entryFile must be string.";
            errNodeId = project.Id;
            return false;
        }
        if (!project.Attrs.TryGetValue("entryExport", out var entryExportAttr) || entryExportAttr.Kind != AosAttrKind.String)
        {
            errCode = "RUN002";
            errMessage = "Project entryExport must be string.";
            errNodeId = project.Id;
            return false;
        }

        var entryFile = entryFileAttr.AsString();
        moduleBaseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        sourcePath = Path.GetFullPath(Path.Combine(moduleBaseDir, entryFile));
        if (!HostFileSystem.FileExists(sourcePath))
        {
            errCode = "RUN002";
            errMessage = $"Entry file not found: {entryFile}";
            errNodeId = project.Id;
            return false;
        }

        entryExportOverride = entryExportAttr.AsString();
        resolvedFromManifest = true;
        return true;
    }

    private static AosNode BuildManifestExecutionProgram(AosNode sourceProgram, string entryExport)
    {
        var callId = NextSyntheticId(sourceProgram, "run_manifest_entry_call");
        var argsId = NextSyntheticId(sourceProgram, "run_manifest_entry_args");
        var children = new List<AosNode>(sourceProgram.Children.Count + 1);
        children.AddRange(sourceProgram.Children);
        children.Add(new AosNode(
            "Call",
            callId,
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["target"] = new AosAttrValue(AosAttrKind.Identifier, entryExport)
            },
            new List<AosNode>
            {
                new AosNode(
                    "Var",
                    argsId,
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, "argv")
                    },
                    new List<AosNode>(),
                    sourceProgram.Span)
            },
            sourceProgram.Span));

        return new AosNode(
            "Program",
            sourceProgram.Id,
            sourceProgram.Attrs,
            children,
            sourceProgram.Span);
    }

    private static string NextSyntheticId(AosNode program, string baseId)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        CollectIds(program, used);
        if (!used.Contains(baseId))
        {
            return baseId;
        }

        var suffix = 1;
        while (used.Contains($"{baseId}_{suffix}"))
        {
            suffix++;
        }

        return $"{baseId}_{suffix}";
    }

    private static void CollectIds(AosNode node, HashSet<string> used)
    {
        used.Add(node.Id);
        foreach (var child in node.Children)
        {
            CollectIds(child, used);
        }
    }

    private static bool HasNamedExport(AosNode program, string exportName)
    {
        foreach (var child in program.Children)
        {
            if (!string.Equals(child.Kind, "Export", StringComparison.Ordinal))
            {
                continue;
            }

            if (!child.Attrs.TryGetValue("name", out var nameAttr))
            {
                continue;
            }

            if ((nameAttr.Kind == AosAttrKind.String || nameAttr.Kind == AosAttrKind.Identifier) &&
                string.Equals(nameAttr.AsString(), exportName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static AosParseResult Parse(string source)
    {
        return AosParsing.Parse(source);
    }

    private static bool TryGetBundleAttr(AosNode bundle, string key, out string value)
    {
        value = string.Empty;
        if (!bundle.Attrs.TryGetValue(key, out var attr) || attr.Kind != AosAttrKind.String)
        {
            return false;
        }
        value = attr.AsString();
        return !string.IsNullOrEmpty(value);
    }

    private static bool IsErrNode(AosValue value, out AosNode? errNode)
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

    private static string FormatOk(string id, AosValue value)
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

    private static string FormatTrace(string id, List<AosNode> steps)
    {
        var trace = new AosNode(
            "Trace",
            id,
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            new List<AosNode>(steps),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(trace);
    }

    private static string FormatErr(string id, string code, string message, string nodeId)
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

    private static AosNode RunBenchCase(string name, string relativePath, int iterations)
    {
        var fullPath = ResolveRepoPath(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return BuildBenchCaseNode(name, "missing", 0, 0, 0);
        }

        var source = File.ReadAllText(fullPath);
        var parse = Parse(source);
        if (parse.Root is null || parse.Diagnostics.Count > 0 || parse.Root.Kind != "Program")
        {
            return BuildBenchCaseNode(name, "parse_err", 0, 0, 0);
        }

        var program = parse.Root;
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math", "console", "io", "compiler", "sys" };
        var validator = new AosValidator();
        var validation = validator.Validate(program, null, permissions, runStructural: false);
        if (validation.Diagnostics.Count > 0)
        {
            return BuildBenchCaseNode(name, "val_err", 0, 0, 0);
        }

        var astTicks = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var runtime = NewBenchRuntime(fullPath);
            var interpreter = new AosInterpreter();
            AosStandardLibraryLoader.EnsureLoaded(runtime, interpreter);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var value = interpreter.EvaluateProgram(program, runtime);
            sw.Stop();
            astTicks += sw.ElapsedTicks;
            if (IsErrNode(value, out _))
            {
                return BuildBenchCaseNode(name, "ast_err", astTicks, 0, 0);
            }
        }

        var compileRuntime = NewBenchRuntime(fullPath);
        var compileInterpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(compileRuntime, compileInterpreter);
        compileRuntime.Env["__bench_program"] = AosValue.FromNode(program);
        var emitNode = new AosNode(
            "Call",
            "bench_emit",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["target"] = new AosAttrValue(AosAttrKind.Identifier, "compiler.emitBytecode")
            },
            new List<AosNode>
            {
                new(
                    "Var",
                    "bench_prog_ref",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__bench_program")
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
            },
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

        var emitValue = compileInterpreter.EvaluateExpression(emitNode, compileRuntime);
        if (emitValue.Kind != AosValueKind.Node || IsErrNode(emitValue, out _))
        {
            return BuildBenchCaseNode(name, "vm_unsupported", astTicks, 0, 0);
        }

        var bytecode = emitValue.AsNode();
        var instCount = CountInstructions(bytecode);
        var vmTicks = 0L;
        for (var i = 0; i < iterations; i++)
        {
            var runtime = NewBenchRuntime(fullPath);
            var interpreter = new AosInterpreter();
            AosStandardLibraryLoader.EnsureLoaded(runtime, interpreter);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = interpreter.RunBytecode(bytecode, "main", AosRuntimeNodes.BuildArgvNode(Array.Empty<string>()), runtime);
            sw.Stop();
            vmTicks += sw.ElapsedTicks;
            if (IsErrNode(result, out _))
            {
                return BuildBenchCaseNode(name, "vm_err", astTicks, vmTicks, instCount);
            }
        }

        return BuildBenchCaseNode(name, "ok", astTicks, vmTicks, instCount);
    }

    private static AosRuntime NewBenchRuntime(string sourcePath)
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("console");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.ModuleBaseDir = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(Array.Empty<string>()));
        runtime.ReadOnlyBindings.Add("argv");
        return runtime;
    }

    private static AosNode BuildBenchCaseNode(string name, string status, long astTicks, long vmTicks, int instCount)
    {
        var astTicksInt = astTicks > int.MaxValue ? int.MaxValue : (int)astTicks;
        var vmTicksInt = vmTicks > int.MaxValue ? int.MaxValue : (int)vmTicks;
        return new AosNode(
            "Case",
            $"case_{name}",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["name"] = new AosAttrValue(AosAttrKind.String, name),
                ["status"] = new AosAttrValue(AosAttrKind.Identifier, status),
                ["astTicks"] = new AosAttrValue(AosAttrKind.Int, astTicksInt),
                ["vmTicks"] = new AosAttrValue(AosAttrKind.Int, vmTicksInt),
                ["instructionCount"] = new AosAttrValue(AosAttrKind.Int, instCount)
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    }

    private static int CountInstructions(AosNode bytecode)
    {
        var count = 0;
        foreach (var child in bytecode.Children)
        {
            if (child.Kind == "Func")
            {
                count += child.Children.Count(inst => inst.Kind == "Inst");
            }
        }
        return count;
    }

    private static string? ResolveRepoPath(string relativePath)
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), relativePath),
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(Directory.GetCurrentDirectory(), "..", relativePath)
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }
}
