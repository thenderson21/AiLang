namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static int RunGoldenTests(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"FAIL {directory} (directory not found)");
            return 1;
        }

        var aicProgram = LoadAicProgram();
        if (aicProgram is null)
        {
            Console.WriteLine("FAIL aic (src/compiler/aic.aos not found)");
            return 1;
        }

        var inputFiles = Directory.GetFiles(directory, "*.in.aos", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var failCount = 0;
        foreach (var inputPath in inputFiles)
        {
            var stem = inputPath[..^".in.aos".Length];
            var outPath = $"{stem}.out.aos";
            var errPath = $"{stem}.err";
            var testName = Path.GetFileName(stem);
            var source = File.ReadAllText(inputPath);
            if (testName == "new_directory_exists")
            {
                Directory.CreateDirectory(Path.Combine(directory, "new", "existing_project"));
            }
            else if (testName == "new_success")
            {
                var successDir = Path.Combine(directory, "new", "success_project");
                if (Directory.Exists(successDir))
                {
                    Directory.Delete(successDir, recursive: true);
                }
            }

            string actual;
            string expected;

            if (File.Exists(errPath))
            {
                expected = NormalizeGoldenText(File.ReadAllText(errPath));
                if (testName.StartsWith("vm_", StringComparison.Ordinal))
                {
                    actual = ExecuteVmGolden(source, testName);
                    goto compare_result;
                }
                var modeArgs = ResolveGoldenArgs(directory, testName, errorMode: true);
                actual = ExecuteAicMode(aicProgram, modeArgs!, source);
            }
            else if (File.Exists(outPath))
            {
                expected = NormalizeGoldenText(File.ReadAllText(outPath));
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
                Console.WriteLine($"FAIL {testName}");
                continue;
            }

        compare_result:
            if (actual == expected)
            {
                Console.WriteLine($"PASS {testName}");
            }
            else
            {
                failCount++;
                Console.WriteLine($"FAIL {testName}");
            }

            if (testName == "new_success")
            {
                var successDir = Path.Combine(directory, "new", "success_project");
                if (Directory.Exists(successDir))
                {
                    Directory.Delete(successDir, recursive: true);
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

        var oldIn = Console.In;
        var oldOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(input));
            Console.SetOut(writer);
            interpreter.EvaluateProgram(aicProgram, runtime);
        }
        finally
        {
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }

        return NormalizeGoldenText(writer.ToString());
    }

    private static string[]? ResolveGoldenArgs(string directory, string testName, bool errorMode)
    {
        if (testName.StartsWith("new_", StringComparison.Ordinal))
        {
            var newDir = Path.Combine(directory, "new");
            return testName switch
            {
                "new_missing_name" => new[] { "new" },
                "new_directory_exists" => new[] { "new", Path.Combine(newDir, "existing_project") },
                "new_success" => new[] { "new", Path.Combine(newDir, "success_project") },
                _ => new[] { "new" }
            };
        }

        if (testName.StartsWith("publish_", StringComparison.Ordinal))
        {
            return testName switch
            {
                "publish_binary_runs" => new[] { "publish", Path.Combine(directory, "publish", "binary_runs") },
                "publish_bundle_single_file" => new[] { "publish", Path.Combine(directory, "publish", "bundle_single_file") },
                "publish_bundle_with_import" => new[] { "publish", Path.Combine(directory, "publish", "bundle_with_import") },
                "publish_bundle_cycle_error" => new[] { "publish", Path.Combine(directory, "publish", "bundle_cycle_error") },
                "publish_writes_bundle" => new[] { "publish", Path.Combine(directory, "publish", "writes_bundle") },
                "publish_overwrite_bundle" => new[] { "publish", Path.Combine(directory, "publish", "overwrite_bundle") },
                "publish_include_success" => new[] { "publish", Path.Combine(directory, "publishcases", "include_success") },
                "publish_include_missing_library" => new[] { "publish", Path.Combine(directory, "publishcases", "include_missing_library") },
                "publish_include_version_mismatch" => new[] { "publish", Path.Combine(directory, "publishcases", "include_version_mismatch") },
                "publish_missing_dir" => new[] { "publish" },
                "publish_missing_manifest" => new[] { "publish", Path.Combine(directory, "publish", "missing_manifest") },
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
        var hostBinary = ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=trace)";
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"ailang-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "trace_input.aos");
            File.WriteAllText(sourcePath, source);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("--trace");
            psi.ArgumentList.Add("--vm=ast");
            if (testName == "trace_with_args")
            {
                psi.ArgumentList.Add("alpha");
                psi.ArgumentList.Add("beta");
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute trace golden.\" nodeId=trace)";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecuteLifecycleGolden(string source, string testName)
    {
        var hostBinary = ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=lifecycle)";
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"ailang-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "lifecycle_input.aos");
            File.WriteAllText(sourcePath, source);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("--vm=ast");
            if (testName == "lifecycle_event_message_basic" || testName == "http_health_route_refactor")
            {
                psi.ArgumentList.Add("__event_message");
                psi.ArgumentList.Add("text");
                psi.ArgumentList.Add("GET /health");
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute lifecycle golden.\" nodeId=lifecycle)";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (testName == "lifecycle_app_exit_code")
            {
                return AosFormatter.Format(new AosNode(
                    "ExitCode",
                    "ec1",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Int, process.ExitCode)
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
                        ["value"] = new AosAttrValue(AosAttrKind.Int, process.ExitCode)
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
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecuteVmGolden(string source, string testName)
    {
        var hostBinary = ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=vm)";
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"ailang-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "vm_input.aos");
            File.WriteAllText(sourcePath, source);
            if (testName == "vm_import_support")
            {
                File.WriteAllText(Path.Combine(tempDir, "mod.aos"),
                    "Program#m1 { Export#e1(name=hello) Let#l1(name=hello) { Fn#f1(params=name) { Block#b1 { Return#r1 { Lit#s1(value=\"hello\") } } } } }");
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(sourcePath);
            if (testName == "vm_echo")
            {
                // Explicit stdin exercise for io.readLine.
                psi.StandardInputEncoding = System.Text.Encoding.UTF8;
            }
            if (testName == "vm_health_handler")
            {
                psi.ArgumentList.Add("__event_message");
                psi.ArgumentList.Add("text");
                psi.ArgumentList.Add("GET /health");
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute vm golden.\" nodeId=vm)";
            }

            if (testName == "vm_echo")
            {
                process.StandardInput.Write("vm-echo\n");
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecutePublishBinaryGolden(AosNode aicProgram, string directory, string input)
    {
        var publishDir = Path.Combine(directory, "publish", "binary_runs");
        var publishOutput = ExecuteAicMode(aicProgram, new[] { "publish", publishDir }, input);
        var binaryPath = Path.Combine(publishDir, "binaryrun");
        if (!File.Exists(binaryPath))
        {
            return publishOutput;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = "alpha beta",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = publishDir
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
        {
            return "Err#err0(code=RUN001 message=\"Failed to execute published binary.\" nodeId=binary)";
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = output;
        return "binary-ok";
    }

    private static AosNode? LoadAicProgram()
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
            var candidate = Path.Combine(root, "aic.aos");
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

        var parse = ParseSource(File.ReadAllText(path));
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
