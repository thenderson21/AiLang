using AiLang.Core;
using AiCLI;
using AiVM.Core;

Environment.ExitCode = RunCli(args);
return;

static int RunCli(string[] args)
{
    VmSyscalls.Host = new CliSyscallHost();

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
    if (!InAosDevMode() && string.Equals(vmMode, "ast", StringComparison.Ordinal))
    {
        Console.WriteLine("Err#err0(code=DEV001 message=\"AST mode is disabled in production build.\" nodeId=vmMode)");
        return 1;
    }

    if (EmbeddedBundleLoader.TryLoadFromCurrentProcess(out var embedded))
    {
        if (!InAosDevMode() && !embedded.IsBytecode)
        {
            Console.WriteLine("Err#err0(code=DEV002 message=\"Production runtime only supports embedded bytecode payloads.\" nodeId=bundle)");
            return 1;
        }

        return embedded.IsBytecode
            ? AosCliExecutionEngine.RunEmbeddedBytecode(embedded.Text, filteredArgs, traceEnabled, Console.WriteLine)
            : AosCliExecutionEngine.RunEmbeddedBundle(embedded.Text, filteredArgs, traceEnabled, vmMode, Console.WriteLine);
    }

    if (filteredArgs.Length == 0)
    {
        PrintUsage();
        return 1;
    }

    switch (filteredArgs[0])
    {
        case "repl":
            if (!InAosDevMode())
            {
                Console.WriteLine("Err#err0(code=DEV003 message=\"REPL is unavailable in production build.\" nodeId=repl)");
                return 1;
            }
            return AosCliExecutionEngine.RunRepl(Console.ReadLine, Console.WriteLine);
        case "run":
            if (filteredArgs.Length < 2)
            {
                PrintUsage();
                return 1;
            }
            if (!InAosDevMode())
            {
                Console.WriteLine("Err#err0(code=DEV004 message=\"Source run is unavailable in production build.\" nodeId=run)");
                return 1;
            }
            return AosCliExecutionEngine.RunSource(filteredArgs[1], filteredArgs.Skip(2).ToArray(), traceEnabled, vmMode, Console.WriteLine);
        case "serve":
            if (filteredArgs.Length < 2)
            {
                PrintUsage();
                return 1;
            }
            if (!InAosDevMode())
            {
                Console.WriteLine("Err#err0(code=DEV005 message=\"Source serve is unavailable in production build.\" nodeId=serve)");
                return 1;
            }
            if (!CliHttpServe.TryParseServeOptions(filteredArgs.Skip(2).ToArray(), out var port, out var tlsCertPath, out var tlsKeyPath, out var appArgs))
            {
                PrintUsage();
                return 1;
            }
            return AosCliExecutionEngine.RunServe(filteredArgs[1], appArgs, port, tlsCertPath, tlsKeyPath, traceEnabled, vmMode, Console.WriteLine);
        case "bench":
            if (!InAosDevMode())
            {
                Console.WriteLine("Err#err0(code=DEV006 message=\"Bench is unavailable in production build.\" nodeId=bench)");
                return 1;
            }
            return AosCliExecutionEngine.RunBench(filteredArgs.Skip(1).ToArray(), Console.WriteLine);
        default:
            PrintUsage();
            return 1;
    }
}

static void PrintUsage()
{
    if (InAosDevMode())
    {
        Console.WriteLine("Usage: airun repl | airun run <path.aos> | airun serve <path.aos> [--port <n>] [--tls-cert <path>] [--tls-key <path>] [--vm=bytecode|ast] | airun bench [--iterations <n>] [--human]");
        return;
    }

    Console.WriteLine("Usage: airun <embedded-binary> [args...]");
}

static bool InAosDevMode()
{
#if AOS_DEV_MODE
    return true;
#else
    return false;
#endif
}
