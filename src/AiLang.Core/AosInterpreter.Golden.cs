using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static int RunGoldenTests(string directory)
    {
        if (!HostFileSystem.DirectoryExists(directory))
        {
            HostConsole.WriteLine($"FAIL {directory} (directory not found)");
            return 1;
        }

        var aicProgram = LoadAicProgram();
        if (aicProgram is null)
        {
            HostConsole.WriteLine("FAIL aic (src/compiler/aic.aos not found)");
            return 1;
        }

        var inputFiles = HostFileSystem.GetFiles(directory, "*.in.aos", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var failCount = 0;
        foreach (var inputPath in inputFiles)
        {
            var stem = inputPath[..^".in.aos".Length];
            var outPath = $"{stem}.out.aos";
            var errPath = $"{stem}.err";
            var testName = HostFileSystem.GetFileName(stem);
            var source = HostFileSystem.ReadAllText(inputPath);
            if (testName == "new_directory_exists")
            {
                HostFileSystem.EnsureDirectory(HostFileSystem.Combine(directory, "new", "existing_project"));
            }
            else if (testName == "new_success" ||
                     testName == "new_cli_success" ||
                     testName == "new_http_success" ||
                     testName == "new_gui_success" ||
                     testName == "new_lib_success")
            {
                var successDir = testName switch
                {
                    "new_success" => HostFileSystem.Combine(directory, "new", "success_project"),
                    "new_cli_success" => HostFileSystem.Combine(directory, "new", "success_cli_project"),
                    "new_http_success" => HostFileSystem.Combine(directory, "new", "success_http_project"),
                    "new_gui_success" => HostFileSystem.Combine(directory, "new", "success_gui_project"),
                    _ => HostFileSystem.Combine(directory, "new", "success_lib_project")
                };
                if (HostFileSystem.DirectoryExists(successDir))
                {
                    HostFileSystem.DeleteDirectory(successDir, recursive: true);
                }
            }

            string actual;
            string expected;

            if (HostFileSystem.FileExists(errPath))
            {
                expected = NormalizeGoldenText(HostFileSystem.ReadAllText(errPath));
                if (testName.StartsWith("vm_", StringComparison.Ordinal))
                {
                    actual = ExecuteVmGolden(source, testName);
                    goto compare_result;
                }
                var modeArgs = ResolveGoldenArgs(directory, testName, errorMode: true);
                actual = ExecuteAicMode(aicProgram, modeArgs!, source);
            }
            else if (HostFileSystem.FileExists(outPath))
            {
                expected = NormalizeGoldenText(HostFileSystem.ReadAllText(outPath));
                if (testName.StartsWith("trace_", StringComparison.Ordinal))
                {
                    actual = ExecuteTraceGolden(source, testName);
                    goto compare_result;
                }
                if (testName.StartsWith("lifecycle_", StringComparison.Ordinal))
                {
                    actual = ExecuteLifecycleGolden(source, testName);
                    goto compare_result;
                }
                if (testName.StartsWith("vm_", StringComparison.Ordinal))
                {
                    actual = ExecuteVmGolden(source, testName);
                    goto compare_result;
                }
                if (testName == "http_health_route_refactor")
                {
                    actual = ExecuteLifecycleGolden(source, testName);
                    goto compare_result;
                }
                if (testName == "publish_binary_runs")
                {
                    actual = ExecutePublishBinaryGolden(aicProgram, directory, source);
                    goto compare_result;
                }
                var modeArgs = ResolveGoldenArgs(directory, testName, errorMode: false);
                if (modeArgs is not null)
                {
                    actual = ExecuteAicMode(aicProgram, modeArgs, source);
                }
                else
                {
                    var fmtActual = ExecuteAicMode(aicProgram, new[] { "fmt" }, source);
                    var runActual = ExecuteAicMode(aicProgram, new[] { "run" }, source);
                    actual = expected == fmtActual ? fmtActual : runActual;
                }
            }
            else
            {
                failCount++;
                HostConsole.WriteLine($"FAIL {testName}");
                continue;
            }

        compare_result:
            if (actual == expected)
            {
                HostConsole.WriteLine($"PASS {testName}");
            }
            else
            {
                failCount++;
                HostConsole.WriteLine($"FAIL {testName}");
            }

            if (testName == "new_success" ||
                testName == "new_cli_success" ||
                testName == "new_http_success" ||
                testName == "new_gui_success" ||
                testName == "new_lib_success")
            {
                var successDir = testName switch
                {
                    "new_success" => HostFileSystem.Combine(directory, "new", "success_project"),
                    "new_cli_success" => HostFileSystem.Combine(directory, "new", "success_cli_project"),
                    "new_http_success" => HostFileSystem.Combine(directory, "new", "success_http_project"),
                    "new_gui_success" => HostFileSystem.Combine(directory, "new", "success_gui_project"),
                    _ => HostFileSystem.Combine(directory, "new", "success_lib_project")
                };
                if (HostFileSystem.DirectoryExists(successDir))
                {
                    HostFileSystem.DeleteDirectory(successDir, recursive: true);
                }
            }
        }

        return failCount == 0 ? 0 : 1;
    }

    private static string ExecuteAicMode(AosNode aicProgram, string[] argv, string input)
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(argv));
        runtime.ReadOnlyBindings.Add("argv");
        var interpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(runtime, interpreter);

        var oldIn = HostConsole.In;
        var oldOut = HostConsole.Out;
        var writer = new StringWriter();
        try
        {
            HostConsole.In = new StringReader(input);
            HostConsole.Out = writer;
            interpreter.EvaluateProgram(aicProgram, runtime);
        }
        finally
        {
            HostConsole.In = oldIn;
            HostConsole.Out = oldOut;
        }

        return NormalizeGoldenText(writer.ToString());
    }

    private static string[]? ResolveGoldenArgs(string directory, string testName, bool errorMode)
    {
        if (testName.StartsWith("new_", StringComparison.Ordinal))
        {
            var newDir = HostFileSystem.Combine(directory, "new");
            return testName switch
            {
                "new_missing_name" => new[] { "new" },
                "new_directory_exists" => new[] { "new", HostFileSystem.Combine(newDir, "existing_project") },
                "new_success" => new[] { "new", HostFileSystem.Combine(newDir, "success_project") },
                "new_cli_success" => new[] { "new", HostFileSystem.Combine(newDir, "success_cli_project"), "cli" },
                "new_http_success" => new[] { "new", HostFileSystem.Combine(newDir, "success_http_project"), "http" },
                "new_gui_success" => new[] { "new", HostFileSystem.Combine(newDir, "success_gui_project"), "gui" },
                "new_lib_success" => new[] { "new", HostFileSystem.Combine(newDir, "success_lib_project"), "lib" },
                "new_unknown_template" => new[] { "new", HostFileSystem.Combine(newDir, "success_unknown_project"), "unknown" },
                _ => new[] { "new" }
            };
        }

        if (testName.StartsWith("publish_", StringComparison.Ordinal))
        {
            return testName switch
            {
                "publish_binary_runs" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "binary_runs") },
                "publish_bundle_single_file" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "bundle_single_file") },
                "publish_bundle_with_import" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "bundle_with_import") },
                "publish_bundle_cycle_error" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "bundle_cycle_error") },
                "publish_writes_bundle" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "writes_bundle") },
                "publish_overwrite_bundle" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "overwrite_bundle") },
                "publish_include_success" => new[] { "publish", HostFileSystem.Combine(directory, "publishcases", "include_success") },
                "publish_include_missing_library" => new[] { "publish", HostFileSystem.Combine(directory, "publishcases", "include_missing_library") },
                "publish_include_version_mismatch" => new[] { "publish", HostFileSystem.Combine(directory, "publishcases", "include_version_mismatch") },
                "publish_missing_dir" => new[] { "publish" },
                "publish_missing_manifest" => new[] { "publish", HostFileSystem.Combine(directory, "publish", "missing_manifest") },
                _ => new[] { "publish" }
            };
        }

        if (errorMode)
        {
            return new[] { "check" };
        }

        return null;
    }

    private static string ExecuteTraceGolden(string source, string testName)
    {
        var hostBinary = HostExecutableLocator.ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=trace)";
        }

        var tempDir = HostFileSystem.Combine(HostFileSystem.GetTempPath(), $"ailang-trace-{Guid.NewGuid():N}");
        HostFileSystem.EnsureDirectory(tempDir);
        try
        {
            var sourcePath = HostFileSystem.Combine(tempDir, "trace_input.aos");
            HostFileSystem.WriteAllText(sourcePath, source);

            var args = new List<string> { "run", sourcePath, "--trace", "--vm=ast" };
            if (testName == "trace_with_args")
            {
                args.Add("alpha");
                args.Add("beta");
            }

            var result = HostProcessRunner.Run(hostBinary, args, HostFileSystem.GetCurrentDirectory());
            if (result is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute trace golden.\" nodeId=trace)";
            }

            var output = System.Text.Encoding.UTF8.GetString(result.Stdout);
            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                HostFileSystem.DeleteDirectory(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecuteLifecycleGolden(string source, string testName)
    {
        var hostBinary = HostExecutableLocator.ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=lifecycle)";
        }

        var tempDir = HostFileSystem.Combine(HostFileSystem.GetTempPath(), $"ailang-lifecycle-{Guid.NewGuid():N}");
        HostFileSystem.EnsureDirectory(tempDir);
        try
        {
            var sourcePath = HostFileSystem.Combine(tempDir, "lifecycle_input.aos");
            HostFileSystem.WriteAllText(sourcePath, source);

            var args = new List<string> { "run", sourcePath, "--vm=ast" };
            if (testName == "lifecycle_event_message_basic" || testName == "http_health_route_refactor")
            {
                args.Add("__event_message");
                args.Add("text");
                args.Add("GET /health");
            }

            var result = HostProcessRunner.Run(hostBinary, args, HostFileSystem.GetCurrentDirectory());
            if (result is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute lifecycle golden.\" nodeId=lifecycle)";
            }

            var output = System.Text.Encoding.UTF8.GetString(result.Stdout);
            if (testName == "lifecycle_app_exit_code")
            {
                return AosFormatter.Format(new AosNode(
                    "ExitCode",
                    "ec1",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Int, result.ExitCode)
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
            }
            if (testName == "lifecycle_command_exit_after_print")
            {
                var exitText = AosFormatter.Format(new AosNode(
                    "ExitCode",
                    "ec1",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Int, result.ExitCode)
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
                var printed = NormalizeGoldenText(output);
                return printed.Length == 0 ? exitText : $"{printed}\n{exitText}";
            }

            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                HostFileSystem.DeleteDirectory(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecuteVmGolden(string source, string testName)
    {
        var hostBinary = HostExecutableLocator.ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=vm)";
        }

        var tempDir = HostFileSystem.Combine(HostFileSystem.GetTempPath(), $"ailang-vm-{Guid.NewGuid():N}");
        HostFileSystem.EnsureDirectory(tempDir);
        try
        {
            var sourcePath = HostFileSystem.Combine(tempDir, "vm_input.aos");
            HostFileSystem.WriteAllText(sourcePath, source);
            if (testName == "vm_import_support")
            {
                HostFileSystem.WriteAllText(HostFileSystem.Combine(tempDir, "mod.aos"),
                    "Program#m1 { Export#e1(name=hello) Let#l1(name=hello) { Fn#f1(params=name) { Block#b1 { Return#r1 { Lit#s1(value=\"hello\") } } } } }");
            }

            var args = new List<string> { "run", sourcePath };
            if (testName != "vm_default_is_canonical" && testName != "vm_unsupported_construct")
            {
                args.Add("--vm=ast");
            }
            if (testName == "vm_health_handler")
            {
                args.Add("__event_message");
                args.Add("text");
                args.Add("GET /health");
            }

            var stdin = testName == "vm_echo" ? "vm-echo\n" : null;
            var result = HostProcessRunner.Run(hostBinary, args, HostFileSystem.GetCurrentDirectory(), stdin);
            if (result is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute vm golden.\" nodeId=vm)";
            }

            var output = System.Text.Encoding.UTF8.GetString(result.Stdout);
            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                HostFileSystem.DeleteDirectory(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecutePublishBinaryGolden(AosNode aicProgram, string directory, string input)
    {
        var publishDir = HostFileSystem.Combine(directory, "publish", "binary_runs");
        var publishOutput = ExecuteAicMode(aicProgram, new[] { "publish", publishDir }, input);
        var binaryPath = HostFileSystem.Combine(publishDir, "binaryrun");
        if (!HostFileSystem.FileExists(binaryPath))
        {
            return publishOutput;
        }

        var result = HostProcessRunner.Run(binaryPath, new[] { "alpha", "beta" }, publishDir);
        if (result is null)
        {
            return "Err#err0(code=RUN001 message=\"Failed to execute published binary.\" nodeId=binary)";
        }
        return "binary-ok";
    }

    private static AosNode? LoadAicProgram()
    {
        var path = AosCompilerAssets.TryFind("aic.aos");
        if (path is null)
        {
            return null;
        }

        var parse = ParseSource(HostFileSystem.ReadAllText(path));
        return parse.Root;
    }

    private static string NormalizeGoldenText(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        while (normalized.EndsWith("\n", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }
        return normalized;
    }

    private static string ExecuteFmt(string source)
    {
        var parse = ParseSource(source);
        if (parse.Root is null)
        {
            return FormatDiagnosticErr(parse.Diagnostics.FirstOrDefault(), "PAR000");
        }

        if (parse.Diagnostics.Count > 0)
        {
            return FormatDiagnosticErr(parse.Diagnostics[0], parse.Diagnostics[0].Code);
        }

        return AosFormatter.Format(parse.Root);
    }

    private static string ExecuteCheck(string source)
    {
        var parse = ParseSource(source);
        if (parse.Root is null)
        {
            return FormatDiagnosticErr(parse.Diagnostics.FirstOrDefault(), "PAR000");
        }

        if (parse.Diagnostics.Count > 0)
        {
            return FormatDiagnosticErr(parse.Diagnostics[0], parse.Diagnostics[0].Code);
        }

        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math", "io", "compiler", "console" };
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root, null, permissions, runStructural: false);
        if (validation.Diagnostics.Count == 0)
        {
            return "Ok#ok0(type=void)";
        }

        var first = validation.Diagnostics[0];
        return AosFormatter.Format(CreateErrNode("diag0", first.Code, first.Message, first.NodeId ?? "unknown", parse.Root.Span));
    }

    private static string ExecuteRun(string source)
    {
        var parse = ParseSource(source);
        if (parse.Root is null)
        {
            return FormatDiagnosticErr(parse.Diagnostics.FirstOrDefault(), "PAR000");
        }

        if (parse.Diagnostics.Count > 0)
        {
            return FormatDiagnosticErr(parse.Diagnostics[0], parse.Diagnostics[0].Code);
        }

        var runtime = new AosRuntime();
        runtime.Permissions.Add("console");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(runtime, interpreter);

        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root, null, runtime.Permissions, runStructural: false);
        if (validation.Diagnostics.Count > 0)
        {
            var first = validation.Diagnostics[0];
            return AosFormatter.Format(CreateErrNode("diag0", first.Code, first.Message, first.NodeId ?? "unknown", parse.Root.Span));
        }

        var result = interpreter.EvaluateProgram(parse.Root, runtime);
        return FormatOkValue(result, parse.Root.Span);
    }

    private static AosParseResult ParseSource(string source)
    {
        return AosParsing.Parse(source);
    }

    private static string FormatDiagnosticErr(AosDiagnostic? diagnostic, string fallbackCode)
    {
        var code = diagnostic?.Code ?? fallbackCode;
        var message = diagnostic?.Message ?? "Parse failed.";
        var nodeId = diagnostic?.NodeId ?? "unknown";
        var node = CreateErrNode("err0", code, message, nodeId, new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(node);
    }

    private static string FormatOkValue(AosValue value, AosSpan span)
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

        var ok = new AosNode("Ok", "ok0", attrs, new List<AosNode>(), span);
        return AosFormatter.Format(ok);
    }

}
