using System.Runtime.InteropServices;

namespace AiLang.Core;

internal static class AivmCBridge
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeResult
    {
        public int Ok;
        public int Loaded;
        public int Status;
        public int Error;
        public uint LoadStatus;
        public nuint LoadErrorOffset;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate NativeResult ExecuteAibc1Delegate(IntPtr bytes, nuint byteCount);

    internal static void TryProbeFromEnvironment()
    {
        try
        {
            var probe = Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_PROBE");
            if (!string.Equals(probe, "1", StringComparison.Ordinal))
            {
                return;
            }

            var overridePath = Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_LIB");
            _ = TryResolveExecuteAibc1(overridePath, out _, out _, out _);
        }
        catch
        {
            // Probing must never affect deterministic CLI behavior.
        }
    }

    internal static bool TryResolveExecuteAibc1(
        string? libraryPath,
        out nint libraryHandle,
        out ExecuteAibc1Delegate? executeAibc1,
        out string error)
    {
        libraryHandle = 0;
        executeAibc1 = null;
        error = string.Empty;

        if (!TryLoadLibrary(libraryPath, out libraryHandle, out error))
        {
            return false;
        }

        if (!NativeLibrary.TryGetExport(libraryHandle, "aivm_c_execute_aibc1", out var symbol))
        {
            error = "aivm_c_execute_aibc1 export was not found.";
            NativeLibrary.Free(libraryHandle);
            libraryHandle = 0;
            return false;
        }

        executeAibc1 = Marshal.GetDelegateForFunctionPointer<ExecuteAibc1Delegate>(symbol);
        return true;
    }

    private static bool TryLoadLibrary(string? libraryPath, out nint libraryHandle, out string error)
    {
        if (!string.IsNullOrWhiteSpace(libraryPath))
        {
            if (NativeLibrary.TryLoad(libraryPath, out libraryHandle))
            {
                error = string.Empty;
                return true;
            }

            error = $"Failed to load AIVM C bridge library path: {libraryPath}";
            return false;
        }

        foreach (var candidate in GetLibraryCandidates())
        {
            if (NativeLibrary.TryLoad(candidate, out libraryHandle))
            {
                error = string.Empty;
                return true;
            }
        }

        libraryHandle = 0;
        error = "Unable to load default AIVM C bridge library candidate.";
        return false;
    }

    private static IEnumerable<string> GetLibraryCandidates()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return "aivm_core_shared.dll";
            yield return "aivm_core.dll";
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "libaivm_core_shared.dylib";
            yield return "libaivm_core.dylib";
            yield break;
        }

        yield return "libaivm_core_shared.so";
        yield return "libaivm_core.so";
    }
}
