using AiVM.Core;
using System.Runtime.InteropServices;
using System.Text;

namespace AiLang.Core;

internal static class AivmCBridge
{
    private const int NativeValueTypeVoid = 0;
    private const int NativeValueTypeInt = 1;
    private const int NativeValueTypeBool = 2;
    private const int NativeValueTypeString = 3;
    private const int NativeValueTypeUnknown = 5;

    private static readonly Dictionary<string, int> OpcodeMap = new(StringComparer.Ordinal)
    {
        ["NOP"] = 0,
        ["HALT"] = 1,
        ["STUB"] = 2,
        ["PUSH_INT"] = 3,
        ["POP"] = 4,
        ["STORE_LOCAL"] = 5,
        ["LOAD_LOCAL"] = 6,
        ["ADD_INT"] = 7,
        ["JUMP"] = 8,
        ["JUMP_IF_FALSE"] = 9,
        ["PUSH_BOOL"] = 10,
        ["EQ_INT"] = 13,
        ["EQ"] = 14,
        ["CONST"] = 15,
        ["STR_CONCAT"] = 16,
        ["TO_STRING"] = 17,
        ["STR_ESCAPE"] = 18,
        ["RETURN"] = 19,
        ["STR_SUBSTRING"] = 20,
        ["STR_REMOVE"] = 21,
        ["CALL_SYS"] = 22,
        ["ASYNC_CALL_SYS"] = 24,
        ["AWAIT"] = 25,
        ["PAR_BEGIN"] = 26,
        ["PAR_FORK"] = 27,
        ["PAR_JOIN"] = 28,
        ["PAR_CANCEL"] = 29,
        ["STR_UTF8_BYTE_COUNT"] = 30,
        ["NODE_KIND"] = 31,
        ["NODE_ID"] = 32,
        ["ATTR_COUNT"] = 33,
        ["ATTR_KEY"] = 34,
        ["ATTR_VALUE_KIND"] = 35,
        ["ATTR_VALUE_STRING"] = 36,
        ["ATTR_VALUE_INT"] = 37,
        ["ATTR_VALUE_BOOL"] = 38,
        ["CHILD_COUNT"] = 39,
        ["CHILD_AT"] = 40,
        ["MAKE_BLOCK"] = 41,
        ["APPEND_CHILD"] = 42,
        ["MAKE_ERR"] = 43,
        ["MAKE_LIT_STRING"] = 44,
        ["MAKE_LIT_INT"] = 45,
        ["RET"] = 12
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInstruction
    {
        public int Opcode;
        public long OperandInt;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeValue
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public long IntValue;
        [FieldOffset(8)] public int BoolValue;
        [FieldOffset(8)] public IntPtr StringValue;
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

    private sealed class BridgeBytecodeAdapter : IVmBytecodeAdapter<AosNode, AosValue>
    {
        public string GetNodeKind(AosNode node) => node.Kind;
        public string GetNodeId(AosNode node) => node.Id;
        public IEnumerable<AosNode> GetChildren(AosNode node) => node.Children;

        public VmAttr GetAttr(AosNode node, string key)
        {
            if (!node.Attrs.TryGetValue(key, out var attr))
            {
                return VmAttr.Missing();
            }

            return attr.Kind switch
            {
                AosAttrKind.Identifier => VmAttr.Identifier(attr.AsString()),
                AosAttrKind.String => VmAttr.String(attr.AsString()),
                AosAttrKind.Int => VmAttr.Int(attr.AsInt()),
                AosAttrKind.Bool => VmAttr.Bool(attr.AsBool()),
                _ => VmAttr.Missing()
            };
        }

        public AosValue FromString(string value) => AosValue.FromString(value);
        public AosValue FromInt(int value) => AosValue.FromInt(value);
        public AosValue FromBool(bool value) => AosValue.FromBool(value);
        public AosValue FromNull() => AosValue.Unknown;
        public AosValue FromEncodedNodeConstant(string encodedNode, string nodeId)
        {
            throw new VmRuntimeException("VM001", "Node constants are not supported by C bridge execute path.", nodeId);
        }
    }

    internal static bool IsExecutionEnabledFromEnvironment()
    {
        var execute = Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_EXECUTE");
        return string.Equals(execute, "1", StringComparison.Ordinal);
    }

    internal static bool TryExecuteEmbeddedBytecode(AosNode bytecodeRoot, out int exitCode, out string failureMessage)
    {
        exitCode = 1;
        failureMessage = string.Empty;

        if (!TryLowerMainFunction(bytecodeRoot, out var instructions, out var constants, out failureMessage))
        {
            return false;
        }

        if (!TryGetExpectedAbiVersionForExecute(out var expectedAbi, out failureMessage))
        {
            return false;
        }

        if (!TryResolveApi(
                Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_LIB"),
                out var libraryHandle,
                out _,
                out var executeInstructionsWithConstants,
                out var abiVersion,
                out var loadError))
        {
            failureMessage = loadError;
            return false;
        }

        try
        {
            if (abiVersion != expectedAbi)
            {
                failureMessage = $"Bridge ABI mismatch: expected {expectedAbi}, got {abiVersion}.";
                return false;
            }

            var nativeInstructions = instructions
                .Select(inst => new NativeInstruction { Opcode = inst.opcode, OperandInt = inst.operand })
                .ToArray();

            var nativeConstants = new NativeValue[constants.Count];
            var allocatedStrings = new List<IntPtr>();
            for (var i = 0; i < constants.Count; i++)
            {
                if (!TryEncodeNativeConstant(constants[i], out nativeConstants[i], allocatedStrings, out failureMessage))
                {
                    foreach (var ptr in allocatedStrings)
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                    return false;
                }
            }

            var instructionHandle = GCHandle.Alloc(nativeInstructions, GCHandleType.Pinned);
            GCHandle constantHandle = default;
            try
            {
                IntPtr constantsPtr = IntPtr.Zero;
                if (nativeConstants.Length > 0)
                {
                    constantHandle = GCHandle.Alloc(nativeConstants, GCHandleType.Pinned);
                    constantsPtr = constantHandle.AddrOfPinnedObject();
                }

                var result = executeInstructionsWithConstants!(
                    instructionHandle.AddrOfPinnedObject(),
                    (nuint)nativeInstructions.Length,
                    constantsPtr,
                    (nuint)nativeConstants.Length);
                if (result.Ok != 1 || result.Status != 2)
                {
                    failureMessage = $"Native execute failed (ok={result.Ok} status={result.Status} error={result.Error}).";
                    return false;
                }

                exitCode = result.HasExitCode == 1 ? result.ExitCode : 0;
                return true;
            }
            finally
            {
                if (constantHandle.IsAllocated)
                {
                    constantHandle.Free();
                }
                instructionHandle.Free();
                foreach (var ptr in allocatedStrings)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }
        finally
        {
            NativeLibrary.Free(libraryHandle);
        }
    }

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

            var expected = GetExpectedAbiVersion();

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

    private static uint GetExpectedAbiVersion()
    {
        var expected = 1U;
        var expectedRaw = Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_ABI");
        if (!string.IsNullOrWhiteSpace(expectedRaw) && uint.TryParse(expectedRaw, out var parsed))
        {
            expected = parsed;
        }

        return expected;
    }

    private static bool TryGetExpectedAbiVersionForExecute(out uint expected, out string error)
    {
        expected = 1U;
        error = string.Empty;

        var expectedRaw = Environment.GetEnvironmentVariable("AIVM_C_BRIDGE_ABI");
        if (string.IsNullOrWhiteSpace(expectedRaw))
        {
            return true;
        }

        if (!uint.TryParse(expectedRaw, out expected))
        {
            error = $"Invalid AIVM_C_BRIDGE_ABI value: '{expectedRaw}'.";
            return false;
        }

        return true;
    }

    private static bool TryLowerMainFunction(
        AosNode bytecodeRoot,
        out List<(int opcode, long operand)> instructions,
        out List<AosValue> constants,
        out string error)
    {
        instructions = new List<(int opcode, long operand)>();
        constants = new List<AosValue>();
        error = string.Empty;

        VmProgram<AosValue> program;
        try
        {
            program = VmProgramLoader.Load(bytecodeRoot, new BridgeBytecodeAdapter());
        }
        catch (VmRuntimeException ex)
        {
            error = $"{ex.Code}: {ex.Message} (nodeId={ex.NodeId})";
            return false;
        }

        if (!program.FunctionIndexByName.TryGetValue("main", out var mainIndex))
        {
            error = "Entry function 'main' was not found.";
            return false;
        }

        var main = program.Functions[mainIndex];
        if (main.Params.Count != 0)
        {
            error = $"Entry function 'main' with params is not yet supported by C bridge execute path (count={main.Params.Count}).";
            return false;
        }
        var indexMap = new int[main.Instructions.Count + 1];
        var nextIndex = 0;
        for (var i = 0; i < main.Instructions.Count; i++)
        {
            indexMap[i] = nextIndex;
            nextIndex += string.Equals(main.Instructions[i].Op, "CALL_SYS", StringComparison.Ordinal) ? 2 : 1;
        }
        indexMap[main.Instructions.Count] = nextIndex;

        constants.AddRange(program.Constants);
        for (var i = 0; i < main.Instructions.Count; i++)
        {
            var inst = main.Instructions[i];
            if (string.Equals(inst.Op, "CALL", StringComparison.Ordinal) ||
                string.Equals(inst.Op, "ASYNC_CALL", StringComparison.Ordinal) ||
                string.Equals(inst.Op, "CALL_SYS", StringComparison.Ordinal) ||
                string.Equals(inst.Op, "ASYNC_CALL_SYS", StringComparison.Ordinal) ||
                string.Equals(inst.Op, "MAKE_NODE", StringComparison.Ordinal))
            {
                error = $"Opcode '{inst.Op}' is not yet supported by C bridge execute path.";
                return false;
            }

            if (!OpcodeMap.TryGetValue(inst.Op, out var opcode))
            {
                error = $"Opcode '{inst.Op}' is not mapped for native C bridge execute path.";
                return false;
            }

            long operand = inst.A;
            if (string.Equals(inst.Op, "JUMP", StringComparison.Ordinal) ||
                string.Equals(inst.Op, "JUMP_IF_FALSE", StringComparison.Ordinal))
            {
                if (inst.A < 0 || inst.A > main.Instructions.Count)
                {
                    error = $"Jump target out of range: {inst.A}.";
                    return false;
                }
                operand = indexMap[inst.A];
            }

            instructions.Add((opcode, operand));
        }

        return true;
    }

    private static bool TryEncodeNativeConstant(
        AosValue value,
        out NativeValue native,
        List<IntPtr> allocatedStrings,
        out string error)
    {
        native = default;
        error = string.Empty;

        switch (value.Kind)
        {
            case AosValueKind.Void:
                native.Type = NativeValueTypeVoid;
                return true;
            case AosValueKind.Unknown:
                native.Type = NativeValueTypeUnknown;
                return true;
            case AosValueKind.Int:
                native.Type = NativeValueTypeInt;
                native.IntValue = value.AsInt();
                return true;
            case AosValueKind.Bool:
                native.Type = NativeValueTypeBool;
                native.BoolValue = value.AsBool() ? 1 : 0;
                return true;
            case AosValueKind.String:
            {
                native.Type = NativeValueTypeString;
                var text = value.AsString() ?? string.Empty;
                var bytes = Encoding.UTF8.GetBytes(text);
                var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                Marshal.WriteByte(ptr, bytes.Length, 0);
                native.StringValue = ptr;
                allocatedStrings.Add(ptr);
                return true;
            }
            default:
                error = $"Unsupported constant kind for C bridge execute path: {value.Kind}.";
                return false;
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
