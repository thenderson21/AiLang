using System.Reflection;

namespace AiCLI;

public static class CliVersionInfo
{
    public const int AibcVersion = 1;

    public static string BuildLine(bool devMode)
    {
        var assembly = typeof(CliVersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var version = "0.0.0";
        var commit = "unknown";

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex >= 0)
            {
                version = informational[..plusIndex];
                var metadata = informational[(plusIndex + 1)..];
                if (!string.IsNullOrWhiteSpace(metadata))
                {
                    commit = metadata.Length > 12 ? metadata[..12] : metadata;
                }
            }
            else
            {
                version = informational;
            }
        }
        else
        {
            version = assembly.GetName().Version?.ToString() ?? version;
        }

        return $"airun version={version} aibc={AibcVersion} mode={(devMode ? "dev" : "prod")} commit={commit}";
    }
}
