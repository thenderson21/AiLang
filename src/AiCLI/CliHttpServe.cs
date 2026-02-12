internal static class CliHttpServe
{
    public static bool TryParseServeOptions(string[] args, out int port, out string tlsCertPath, out string tlsKeyPath, out string[] appArgs)
    {
        port = 8080;
        tlsCertPath = string.Empty;
        tlsKeyPath = string.Empty;
        var collected = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--port", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var parsedPort) || parsedPort < 0 || parsedPort > 65535)
                {
                    appArgs = Array.Empty<string>();
                    return false;
                }

                port = parsedPort;
                i++;
                continue;
            }
            if (string.Equals(args[i], "--tls-cert", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    appArgs = Array.Empty<string>();
                    return false;
                }

                tlsCertPath = args[i + 1];
                i++;
                continue;
            }
            if (string.Equals(args[i], "--tls-key", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    appArgs = Array.Empty<string>();
                    return false;
                }

                tlsKeyPath = args[i + 1];
                i++;
                continue;
            }

            collected.Add(args[i]);
        }

        appArgs = collected.ToArray();
        return true;
    }
}
