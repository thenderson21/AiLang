using System.Runtime.InteropServices;

namespace AiLang.Core;

internal static class AivmCBridge
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInstruction
    {
        public int Opcode;
        public long OperandInt;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeResult
    {
        public int Ok;
        public int Loaded;
        public int HasExitCode;
        public int ExitCode;
        public int Status;
        public int Error;
        public uint LoadStatus;
        public nuint LoadErrorOffset;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate NativeResult ExecuteAibc1Delegate(IntPtr bytes, nuint byteCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate NativeResult ExecuteInstructionsWithConstantsDelegate(
        IntPtr instructions,
        nuint instructionCount,
        IntPtr constants,
        nuint constantCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint AbiVersionDelegate();

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
            if (!TryResolveApi(overridePath, out var libraryHandle, out _, out var executeInstructionsWithConstants, out var abiVersion, out _))
            {
                return;
            }

            var expected = 1U;
            var expectedRaw = Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_ABI");
            if (!string.IsNullOrWhiteSpace(expectedRaw) && uint.TryParse(expectedRaw, out var parsed))
            {
                expected = parsed;
            }

            if (abiVersion != expected)
            {
                NativeLibrary.Free(libraryHandle);
                return;
            }

            if (!TryRunSmokeExecute(executeInstructionsWithConstants!))
            {
                NativeLibrary.Free(libraryHandle);
                return;
            }

            NativeLibrary.Free(libraryHandle);
        }
        catch
        {
            // Probing must never affect deterministic CLI behavior.
        }
    }

    internal static bool TryResolveApi(
        string? libraryPath,
        out nint libraryHandle,
        out ExecuteAibc1Delegate? executeAibc1,
        out ExecuteInstructionsWithConstantsDelegate? executeInstructionsWithConstants,
        out uint abiVersion,
        out string error)
    {
        libraryHandle = 0;
        executeAibc1 = null;
        executeInstructionsWithConstants = null;
        abiVersion = 0U;
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
        if (!NativeLibrary.TryGetExport(libraryHandle, "aivm_c_execute_instructions_with_constants", out symbol))
        {
            error = "aivm_c_execute_instructions_with_constants export was not found.";
            NativeLibrary.Free(libraryHandle);
            libraryHandle = 0;
            executeAibc1 = null;
            return false;
        }

        executeInstructionsWithConstants = Marshal.GetDelegateForFunctionPointer<ExecuteInstructionsWithConstantsDelegate>(symbol);
        if (!NativeLibrary.TryGetExport(libraryHandle, "aivm_c_abi_version", out symbol))
        {
            error = "aivm_c_abi_version export was not found.";
            NativeLibrary.Free(libraryHandle);
            libraryHandle = 0;
            executeAibc1 = null;
            executeInstructionsWithConstants = null;
            return false;
        }

        abiVersion = Marshal.GetDelegateForFunctionPointer<AbiVersionDelegate>(symbol)();
        return true;
    }

    private static bool TryRunSmokeExecute(ExecuteInstructionsWithConstantsDelegate executeInstructionsWithConstants)
    {
        var instructions = new[] { new NativeInstruction { Opcode = 1, OperandInt = 0 } };
        var handle = GCHandle.Alloc(instructions, GCHandleType.Pinned);
        try
        {
            var result = executeInstructionsWithConstants(handle.AddrOfPinnedObject(), (nuint)instructions.Length, IntPtr.Zero, 0U);
            return result.Ok == 1 &&
                   result.Status == 2 &&
                   result.Error == 0;
        }
        finally
        {
            handle.Free();
        }
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
