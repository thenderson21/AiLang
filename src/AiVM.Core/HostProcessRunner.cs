using System.Diagnostics;

namespace AiVM.Core;

public static class HostProcessRunner
{
    public sealed record ProcessResult(int ExitCode, byte[] Stdout, string Stderr);

    public static ProcessResult? RunWithStdIn(string fileName, string arguments, string stdin)
    {
        fileName = ResolveExecutablePath(fileName, null);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        process.StandardInput.Write(stdin);
        process.StandardInput.Close();

        var stderrTask = process.StandardError.ReadToEndAsync();
        using var stdout = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(stdout);
        process.WaitForExit();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return new ProcessResult(process.ExitCode, stdout.ToArray(), stderr);
    }

    public static ProcessResult? Run(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        string? stdin = null)
    {
        fileName = ResolveExecutablePath(fileName, workingDirectory);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var stderrTask = process.StandardError.ReadToEndAsync();
        using var stdout = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(stdout);
        process.WaitForExit();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return new ProcessResult(process.ExitCode, stdout.ToArray(), stderr);
    }

    private static string ResolveExecutablePath(string fileName, string? workingDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fileName;
        }

        if (string.IsNullOrWhiteSpace(fileName) || Path.HasExtension(fileName))
        {
            return fileName;
        }

        if (Path.IsPathRooted(fileName))
        {
            if (File.Exists(fileName))
            {
                return fileName;
            }

            var rootedExe = fileName + ".exe";
            return File.Exists(rootedExe) ? rootedExe : fileName;
        }

        var cwdCandidate = Path.GetFullPath(fileName);
        if (File.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        var cwdExe = cwdCandidate + ".exe";
        if (File.Exists(cwdExe))
        {
            return cwdExe;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var wdCandidate = Path.GetFullPath(Path.Combine(workingDirectory, fileName));
            if (File.Exists(wdCandidate))
            {
                return wdCandidate;
            }

            var wdExe = wdCandidate + ".exe";
            if (File.Exists(wdExe))
            {
                return wdExe;
            }
        }

        var withExe = fileName + ".exe";
        return File.Exists(withExe) ? withExe : fileName;
    }
}
