using AiLang.Core;
using System.Globalization;

namespace AiCLI;

internal static class CliDebugCommand
{
    private sealed class DebugRunOptions
    {
        public string AppPath { get; set; } = string.Empty;
        public string VmMode { get; set; } = "bytecode";
        public string DebugMode { get; set; } = "live";
        public string OutDir { get; set; } = string.Empty;
        public string EventsPath { get; set; } = string.Empty;
        public string ComparePath { get; set; } = string.Empty;
        public string ScenarioName { get; set; } = string.Empty;
        public string FixturePath { get; set; } = string.Empty;
        public List<string> AppArgs { get; } = new();
    }

    public static int Run(string[] args, CliSyscallHost host, string defaultVmMode, Action<string> writeLine)
    {
        if (!TryParse(args, defaultVmMode, out var options, out var parseError))
        {
            writeLine(parseError);
            return 1;
        }

        if (!Path.IsPathRooted(options.AppPath))
        {
            options.AppPath = Path.GetFullPath(options.AppPath);
        }

        if (!string.IsNullOrEmpty(options.EventsPath))
        {
            options.EventsPath = ResolveRelative(options.EventsPath, options.FixturePath, options.AppPath);
            host.LoadReplayEvents(options.EventsPath);
            options.DebugMode = "replay";
        }

        if (!string.IsNullOrEmpty(options.ComparePath))
        {
            options.ComparePath = ResolveRelative(options.ComparePath, options.FixturePath, options.AppPath);
        }

        if (string.IsNullOrEmpty(options.OutDir))
        {
            var seed = string.Join("|", new[]
            {
                options.AppPath,
                options.VmMode,
                options.DebugMode,
                options.EventsPath,
                options.ComparePath,
                string.Join(";", options.AppArgs)
            });
            options.OutDir = Path.Combine(".artifacts", "debug", $"run-{AosDebugRecorder.ComputeRunId(seed)}");
        }

        if (!Path.IsPathRooted(options.OutDir))
        {
            options.OutDir = Path.GetFullPath(options.OutDir);
        }

        Directory.CreateDirectory(options.OutDir);
        host.SetDebugMode(options.DebugMode);

        var runSeed = $"{options.AppPath}|{options.VmMode}|{options.DebugMode}|{string.Join(";", options.AppArgs)}";
        var recorder = new AosDebugRecorder(AosDebugRecorder.ComputeRunId(runSeed));
        var outputLines = new List<string>();
        var exitCode = 0;

        try
        {
            AosCliExecutionEngine.ActiveDebugRecorder = recorder;
            exitCode = AosCliExecutionEngine.RunSource(
                options.AppPath,
                options.AppArgs.ToArray(),
                traceEnabled: false,
                vmMode: options.VmMode,
                line =>
                {
                    outputLines.Add(line);
                    writeLine(line);
                });
        }
        catch (Exception ex)
        {
            recorder.RecordDiagnostic("DBG_RUN", ex.Message, "debug");
            exitCode = 3;
        }
        finally
        {
            AosCliExecutionEngine.ActiveDebugRecorder = null;
        }

        var compareOk = true;
        if (!string.IsNullOrEmpty(options.ComparePath))
        {
            var actual = string.Join("\n", outputLines).TrimEnd('\r', '\n');
            var expected = File.Exists(options.ComparePath)
                ? File.ReadAllText(options.ComparePath).TrimEnd('\r', '\n')
                : string.Empty;
            compareOk = string.Equals(actual, expected, StringComparison.Ordinal);
        }

        WriteArtifacts(options, outputLines, recorder, exitCode, compareOk);
        if (!compareOk)
        {
            return 4;
        }

        return exitCode;
    }

    private static void WriteArtifacts(DebugRunOptions options, List<string> outputLines, AosDebugRecorder recorder, int exitCode, bool compareOk)
    {
        DeleteIfExists(Path.Combine(options.OutDir, "config.aos"));
        DeleteIfExists(Path.Combine(options.OutDir, "vm_trace.aos"));
        DeleteIfExists(Path.Combine(options.OutDir, "state_snapshots.aos"));
        DeleteIfExists(Path.Combine(options.OutDir, "syscalls.aos"));
        DeleteIfExists(Path.Combine(options.OutDir, "events.aos"));
        DeleteIfExists(Path.Combine(options.OutDir, "diagnostics.aos"));

        File.WriteAllText(Path.Combine(options.OutDir, "config.toml"), RenderConfigToml(options, exitCode, compareOk));
        File.WriteAllText(Path.Combine(options.OutDir, "stdout.txt"), string.Join("\n", outputLines));
        File.WriteAllText(Path.Combine(options.OutDir, "vm_trace.toml"), RenderSimpleTables(recorder.VmTrace, "step"));
        File.WriteAllText(Path.Combine(options.OutDir, "state_snapshots.toml"), RenderSnapshotsToml(recorder.StateSnapshots));
        File.WriteAllText(Path.Combine(options.OutDir, "syscalls.toml"), RenderSyscallsToml(recorder.Syscalls));
        File.WriteAllText(Path.Combine(options.OutDir, "events.toml"), RenderSimpleTables(recorder.LifecycleEvents, "event"));
        File.WriteAllText(Path.Combine(options.OutDir, "diagnostics.toml"), RenderSimpleTables(recorder.Diagnostics, "diagnostic"));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool TryParse(string[] args, string defaultVmMode, out DebugRunOptions options, out string parseError)
    {
        options = new DebugRunOptions { VmMode = defaultVmMode };
        parseError = string.Empty;
        var vmProvided = false;
        var debugModeProvided = false;

        var start = 0;
        if (args.Length > 0 && string.Equals(args[0], "scenario", StringComparison.Ordinal))
        {
            if (args.Length < 2)
            {
                parseError = "Usage: airun debug scenario <fixture.toml> [--name <scenario>]";
                return false;
            }

            options.FixturePath = Path.GetFullPath(args[1]);
            start = 2;
        }

        var passthrough = false;
        var targetTokens = new List<string>();
        for (var i = start; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                passthrough = true;
                targetTokens.Add(arg);
                continue;
            }

            if (!passthrough && arg.StartsWith("--vm=", StringComparison.Ordinal))
            {
                options.VmMode = arg["--vm=".Length..];
                vmProvided = true;
                continue;
            }

            if (!passthrough && arg.StartsWith("--debug-mode=", StringComparison.Ordinal))
            {
                options.DebugMode = arg["--debug-mode=".Length..];
                debugModeProvided = true;
                continue;
            }

            if (!passthrough && arg == "--name" && i + 1 < args.Length)
            {
                options.ScenarioName = args[++i];
                continue;
            }

            if (!passthrough && arg == "--events" && i + 1 < args.Length)
            {
                options.EventsPath = args[++i];
                continue;
            }

            if (!passthrough && arg == "--out" && i + 1 < args.Length)
            {
                options.OutDir = args[++i];
                continue;
            }

            if (!passthrough && arg == "--compare" && i + 1 < args.Length)
            {
                options.ComparePath = args[++i];
                continue;
            }

            if (!passthrough && string.IsNullOrEmpty(options.FixturePath) && arg.StartsWith("--", StringComparison.Ordinal))
            {
                parseError = $"unknown option: {arg}";
                return false;
            }

            targetTokens.Add(arg);
        }

        if (!string.IsNullOrEmpty(options.FixturePath))
        {
            if (!TryLoadScenario(options.FixturePath, options.ScenarioName, out var scenario, out parseError))
            {
                return false;
            }

            options.AppPath = scenario.AppPath;
            if (!vmProvided)
            {
                options.VmMode = scenario.VmMode;
            }
            if (!debugModeProvided)
            {
                options.DebugMode = scenario.DebugMode;
            }
            if (string.IsNullOrEmpty(options.EventsPath))
            {
                options.EventsPath = scenario.EventsPath;
            }
            if (string.IsNullOrEmpty(options.ComparePath))
            {
                options.ComparePath = scenario.ComparePath;
            }
            if (string.IsNullOrEmpty(options.OutDir))
            {
                options.OutDir = scenario.OutDir;
            }
            if (options.AppArgs.Count == 0)
            {
                options.AppArgs.AddRange(scenario.Args);
            }
        }
        else
        {
            if (!CliInvocationParsing.TryResolveTargetAndArgs(
                    targetTokens.ToArray(),
                    Directory.GetCurrentDirectory(),
                    out var invocation,
                    out parseError))
            {
                return false;
            }

            options.AppPath = invocation.TargetPath;
            options.AppArgs.AddRange(invocation.AppArgs);
        }

        return true;
    }

    private static string ResolveRelative(string value, string fixturePath, string appPath)
    {
        if (string.IsNullOrEmpty(value) || Path.IsPathRooted(value))
        {
            return value;
        }

        var baseDir = !string.IsNullOrEmpty(fixturePath)
            ? Path.GetDirectoryName(fixturePath) ?? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(appPath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, value));
    }

    private sealed class DebugScenario
    {
        public string AppPath { get; init; } = string.Empty;
        public string VmMode { get; init; } = "bytecode";
        public string DebugMode { get; init; } = "live";
        public string EventsPath { get; init; } = string.Empty;
        public string ComparePath { get; init; } = string.Empty;
        public string OutDir { get; init; } = string.Empty;
        public List<string> Args { get; init; } = new();
    }

    private static bool TryLoadScenario(string fixturePath, string scenarioName, out DebugScenario scenario, out string error)
    {
        scenario = new DebugScenario();
        error = string.Empty;
        var rows = CliToml.ParseArrayOfTables(fixturePath, "scenario");
        Dictionary<string, CliTomlValue>? selected = null;
        foreach (var row in rows)
        {
            var name = CliToml.GetString(row, "name", string.Empty);
            if (string.IsNullOrEmpty(scenarioName) || string.Equals(name, scenarioName, StringComparison.Ordinal))
            {
                selected = row;
                break;
            }
        }

        if (selected is null)
        {
            error = string.IsNullOrEmpty(scenarioName)
                ? "No scenario rows in fixture."
                : $"Scenario '{scenarioName}' not found.";
            return false;
        }

        scenario = new DebugScenario
        {
            AppPath = ResolveRelative(CliToml.GetString(selected, "app_path", string.Empty), fixturePath, fixturePath),
            VmMode = CliToml.GetString(selected, "vm", "bytecode"),
            DebugMode = CliToml.GetString(selected, "debug_mode", "live"),
            EventsPath = CliToml.GetString(selected, "events_path", string.Empty),
            ComparePath = CliToml.GetString(selected, "compare_path", string.Empty),
            OutDir = CliToml.GetString(selected, "out_dir", string.Empty),
            Args = CliToml.GetStringArray(selected, "args")
        };
        return true;
    }

    private static string RenderConfigToml(DebugRunOptions options, int exitCode, bool compareOk)
    {
        return string.Join('\n', new[]
        {
            $"app_path = {CliToml.Quote(options.AppPath)}",
            $"vm_mode = {CliToml.Quote(options.VmMode)}",
            $"debug_mode = {CliToml.Quote(options.DebugMode)}",
            $"events_path = {CliToml.Quote(options.EventsPath)}",
            $"compare_path = {CliToml.Quote(options.ComparePath)}",
            $"scenario_name = {CliToml.Quote(options.ScenarioName)}",
            $"exit_code = {exitCode}",
            $"compare_ok = {(compareOk ? "true" : "false")}",
            string.Empty
        });
    }

    private static string RenderSimpleTables(IReadOnlyList<AosNode> nodes, string table)
    {
        var lines = new List<string>();
        foreach (var node in nodes)
        {
            lines.Add($"[[{table}]]");
            foreach (var kv in node.Attrs.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                lines.Add(RenderAttrLine(kv.Key, kv.Value));
            }
            lines.Add(string.Empty);
        }
        return string.Join('\n', lines);
    }

    private static string RenderSyscallsToml(IReadOnlyList<AosNode> nodes)
    {
        var lines = new List<string>();
        foreach (var node in nodes)
        {
            lines.Add("[[syscall]]");
            foreach (var kv in node.Attrs.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                lines.Add(RenderAttrLine(kv.Key, kv.Value));
            }

            var args = node.Children
                .Where(c => string.Equals(c.Kind, "Arg", StringComparison.Ordinal) && c.Attrs.TryGetValue("value", out _))
                .Select(c => c.Attrs["value"].AsString())
                .ToList();
            lines.Add($"args = {CliToml.RenderStringArray(args)}");
            lines.Add(string.Empty);
        }
        return string.Join('\n', lines);
    }

    private static string RenderSnapshotsToml(IReadOnlyList<AosNode> nodes)
    {
        var lines = new List<string>();
        foreach (var node in nodes)
        {
            lines.Add("[[snapshot]]");
            foreach (var kv in node.Attrs.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                lines.Add(RenderAttrLine(kv.Key, kv.Value));
            }

            foreach (var child in node.Children)
            {
                var values = child.Children
                    .Where(c => c.Attrs.TryGetValue("value", out _))
                    .Select(c => c.Attrs["value"].AsString())
                    .ToList();
                lines.Add($"{ToSnakeCase(child.Kind)} = {CliToml.RenderStringArray(values)}");
            }
            lines.Add(string.Empty);
        }

        return string.Join('\n', lines);
    }

    private static string RenderAttrLine(string key, AosAttrValue value)
    {
        var rendered = value.Kind switch
        {
            AosAttrKind.String or AosAttrKind.Identifier => CliToml.Quote(value.AsString()),
            AosAttrKind.Int => value.AsInt().ToString(CultureInfo.InvariantCulture),
            AosAttrKind.Bool => value.AsBool() ? "true" : "false",
            _ => CliToml.Quote(value.AsString())
        };
        return $"{ToSnakeCase(key)} = {rendered}";
    }

    private static string ToSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var chars = new List<char>(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsUpper(ch))
            {
                if (i > 0)
                {
                    chars.Add('_');
                }
                chars.Add(char.ToLowerInvariant(ch));
            }
            else
            {
                chars.Add(ch);
            }
        }
        return new string(chars.ToArray());
    }
}
