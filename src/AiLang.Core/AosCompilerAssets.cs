using AiVM.Core;

namespace AiLang.Core;

public static class AosCompilerAssets
{
    public static string[] SearchRoots()
    {
        return new[]
        {
            HostFileSystem.Combine(HostEnvironment.BaseDirectory, "src", "compiler"),
            HostFileSystem.Combine(HostEnvironment.BaseDirectory, "compiler"),
            HostEnvironment.BaseDirectory,
            HostFileSystem.Combine(HostFileSystem.GetCurrentDirectory(), "src", "compiler"),
            HostFileSystem.Combine(HostFileSystem.GetCurrentDirectory(), "compiler"),
            HostFileSystem.GetCurrentDirectory()
        };
    }

    public static string? TryFind(string fileName)
    {
        foreach (var root in SearchRoots())
        {
            var candidate = HostFileSystem.Combine(root, fileName);
            if (HostFileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static AosNode LoadRequiredProgram(string fileName)
    {
        var path = TryFind(fileName);
        if (path is null)
        {
            throw new FileNotFoundException($"{fileName} not found.");
        }

        var parse = AosParsing.ParseFile(path);
        if (parse.Root is null)
        {
            throw new InvalidOperationException($"Failed to parse {fileName}.");
        }

        if (parse.Root.Kind != "Program")
        {
            throw new InvalidOperationException($"{fileName} must contain a Program node.");
        }

        if (parse.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"{fileName} parse error: {parse.Diagnostics[0].Message}");
        }

        return parse.Root;
    }
}
