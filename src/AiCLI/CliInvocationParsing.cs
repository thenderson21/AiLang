namespace AiCLI;

public sealed record CliAppInvocation(
    string TargetPath,
    string[] AppArgs,
    bool UsedImplicitProject,
    bool UsedLegacySeparator);

public static class CliInvocationParsing
{
    public static bool TryResolveTargetAndArgs(
        string[] tokens,
        string cwd,
        out CliAppInvocation invocation,
        out string error)
    {
        var appArgs = new List<string>();
        var target = string.Empty;
        var targetFound = false;
        var passthrough = false;
        var usedLegacySeparator = false;

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!passthrough && token == "--")
            {
                passthrough = true;
                continue;
            }

            if (!passthrough && token == "|")
            {
                passthrough = true;
                usedLegacySeparator = true;
                continue;
            }

            if (!targetFound)
            {
                if (passthrough)
                {
                    appArgs.Add(token);
                    continue;
                }

                if (!passthrough && token.StartsWith("--", StringComparison.Ordinal))
                {
                    invocation = new CliAppInvocation(string.Empty, Array.Empty<string>(), false, usedLegacySeparator);
                    error = $"unknown option: {token}";
                    return false;
                }

                target = token;
                targetFound = true;
                continue;
            }

            appArgs.Add(token);
        }

        if (!targetFound)
        {
            var manifestPath = Path.Combine(cwd, "project.aiproj");
            if (!File.Exists(manifestPath))
            {
                invocation = new CliAppInvocation(string.Empty, Array.Empty<string>(), false, usedLegacySeparator);
                error = "missing app path (or run from a folder containing project.aiproj)";
                return false;
            }

            invocation = new CliAppInvocation(cwd, appArgs.ToArray(), true, usedLegacySeparator);
            error = string.Empty;
            return true;
        }

        invocation = new CliAppInvocation(target, appArgs.ToArray(), false, usedLegacySeparator);
        error = string.Empty;
        return true;
    }
}
