namespace AiCLI;

public static class CliHelpText
{
    public static string Build(bool devMode)
    {
        if (!devMode)
        {
            return string.Join('\n',
                "Usage: airun --version | airun <embedded-binary> [args...]",
                "Try: airun --version");
        }

        return string.Join('\n',
            "Usage: airun <command> [options]",
            "",
            "Commands:",
            "  repl",
            "    Start interactive REPL.",
            "    Example: airun repl",
            "  run [app|project-dir] [-- app-args...]",
            "    Execute app/project (VM default). If omitted, uses ./project.aiproj.",
            "    Example (explicit, no --): airun run examples/hello.aos arg1 arg2",
            "    Example (implicit cwd): airun run -- --flag value",
            "  serve <path.aos> [--port <n>] [--tls-cert <path>] [--tls-key <path>] [--vm=bytecode|ast]",
            "    Start HTTP lifecycle app.",
            "    Example: airun serve examples/golden/http/health_app.aos --port 8080",
            "  bench [--iterations <n>] [--human]",
            "    Run deterministic benchmark suite.",
            "    Example: airun bench --iterations 20 --human",
            "  debug [wrapper-flags] [app|project-dir] [-- app-args...]",
            "    Run app with deterministic debug artifact capture.",
            "    Example (explicit, no --): airun debug --events examples/debug/events/minimal.events.toml examples/debug/apps/debug_minimal.aos arg1",
            "    Example (implicit cwd): airun debug -- --flag value",
            "  debug scenario <fixture.toml> [--name <scenario>]",
            "    Run named scenario from fixture.",
            "  --version | version",
            "    Print build/runtime metadata.",
            "    Example: airun --version",
            "",
            "Global Flags:",
            "  --trace",
            "  --vm=bytecode|ast",
            "  --debug-mode=off|live|snapshot|replay|scene",
            "Debug Options:",
            "  --events <fixture.toml>",
            "  --out <dir>",
            "  --compare <golden.out>",
            "  --name <scenario>",
            "Legacy Separator:",
            "  |  (deprecated; use --)");
    }

    public static string BuildUnknownCommand(string command)
    {
        return $"Unknown command: {command}. See airun --help.";
    }
}
