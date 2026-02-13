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
            "  run <path.aos> [args...]",
            "    Execute source file (VM default).",
            "    Example: airun run examples/hello.aos",
            "  serve <path.aos> [--port <n>] [--tls-cert <path>] [--tls-key <path>] [--vm=bytecode|ast]",
            "    Start HTTP lifecycle app.",
            "    Example: airun serve examples/golden/http/health_app.aos --port 8080",
            "  bench [--iterations <n>] [--human]",
            "    Run deterministic benchmark suite.",
            "    Example: airun bench --iterations 20 --human",
            "  --version | version",
            "    Print build/runtime metadata.",
            "    Example: airun --version",
            "",
            "Global Flags:",
            "  --trace",
            "  --vm=bytecode|ast");
    }

    public static string BuildUnknownCommand(string command)
    {
        return $"Unknown command: {command}. See airun --help.";
    }
}
