using AiVM.Core;

namespace AiLang.Core;

public sealed class AosRuntime
{
    public Dictionary<string, AosValue> Env { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "math" };
    public HashSet<string> ReadOnlyBindings { get; } = new(StringComparer.Ordinal);
    public string ModuleBaseDir { get; set; } = HostFileSystem.GetFullPath(".");
    public Dictionary<string, Dictionary<string, AosValue>> ModuleExports { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ModuleLoading { get; } = new(StringComparer.Ordinal);
    public Stack<Dictionary<string, AosValue>> ExportScopes { get; } = new();
    public bool TraceEnabled { get; set; }
    public List<AosNode> TraceSteps { get; } = new();
    public AosNode? Program { get; set; }
    public VmNetworkState Network { get; } = new();
}

public sealed partial class AosInterpreter
{
    private bool _strictUnknown;
    private int _evalDepth;
    private const int MaxEvalDepth = 4096;

    public AosValue EvaluateProgram(AosNode program, AosRuntime runtime)
    {
        _strictUnknown = false;
        return Evaluate(program, runtime, runtime.Env);
    }

    public AosValue EvaluateExpression(AosNode expr, AosRuntime runtime)
    {
        _strictUnknown = false;
        return Evaluate(expr, runtime, runtime.Env);
    }

    public AosValue EvaluateExpressionStrict(AosNode expr, AosRuntime runtime)
    {
        _strictUnknown = true;
        return Evaluate(expr, runtime, runtime.Env);
    }

    public AosValue RunBytecode(AosNode bytecode, string entryName, AosNode argsNode, AosRuntime runtime)
    {
        try
        {
            var vm = VmProgramLoader.Load(bytecode, BytecodeAdapter.Instance);
            var args = BuildVmArgs(vm, entryName, argsNode);
            return VmEngine.Run<AosNode, AosValue>(
                vm,
                entryName,
                args,
                new VmExecutionAdapter(this, runtime));
        }
        catch (VmRuntimeException ex)
        {
            return AosValue.FromNode(CreateErrNode(
                "vm_err",
                ex.Code,
                ex.Message,
                ex.NodeId,
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
        }
    }

    private AosValue Evaluate(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        try
        {
            return EvalNode(node, runtime, env);
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
    }

    private AosValue EvalNode(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (runtime.TraceEnabled)
        {
            runtime.TraceSteps.Add(new AosNode(
                "Step",
                "auto",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["kind"] = new AosAttrValue(AosAttrKind.String, node.Kind),
                    ["nodeId"] = new AosAttrValue(AosAttrKind.String, node.Id)
                },
                new List<AosNode>(),
                node.Span));
        }

        _evalDepth++;
        if (_evalDepth > MaxEvalDepth)
        {
            throw new InvalidOperationException($"Evaluation depth exceeded at {node.Kind}#{node.Id}.");
        }

        try
        {
            var value = EvalCore(node, runtime, env);

            if (_strictUnknown && value.Kind == AosValueKind.Unknown)
            {
                throw new InvalidOperationException($"Unknown value from node {node.Kind}#{node.Id}.");
            }

            return value;
        }
        finally
        {
            _evalDepth--;
        }
    }

    private AosValue EvalCore(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        switch (node.Kind)
        {
            case "Program":
                AosValue last = AosValue.Void;
                foreach (var child in node.Children)
                {
                    last = EvalNode(child, runtime, env);
                    if (IsErrValue(last))
                    {
                        return last;
                    }
                }
                return last;
            case "Let":
                if (!node.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
                {
                    return AosValue.Unknown;
                }
                var name = nameAttr.AsString();
                if (runtime.ReadOnlyBindings.Contains(name))
                {
                    throw new InvalidOperationException($"Cannot assign read-only binding '{name}'.");
                }
                if (node.Children.Count != 1)
                {
                    return AosValue.Unknown;
                }
                var value = EvalNode(node.Children[0], runtime, env);
                env[name] = value;
                return AosValue.Void;
            case "Var":
                if (!node.Attrs.TryGetValue("name", out var varAttr) || varAttr.Kind != AosAttrKind.Identifier)
                {
                    return AosValue.Unknown;
                }
                return env.TryGetValue(varAttr.AsString(), out var bound) ? bound : AosValue.Unknown;
            case "Lit":
                if (!node.Attrs.TryGetValue("value", out var litAttr))
                {
                    return AosValue.Unknown;
                }
                return litAttr.Kind switch
                {
                    AosAttrKind.String => AosValue.FromString(litAttr.AsString()),
                    AosAttrKind.Int => AosValue.FromInt(litAttr.AsInt()),
                    AosAttrKind.Bool => AosValue.FromBool(litAttr.AsBool()),
                    _ => AosValue.Unknown
                };
            case "Call":
                return EvalCall(node, runtime, env);
            case "Import":
                return EvalImport(node, runtime, env);
            case "Export":
                return EvalExport(node, runtime, env);
            case "Fn":
                return EvalFunction(node, runtime, env);
            case "Eq":
                return EvalEq(node, runtime, env);
            case "Add":
                return EvalAdd(node, runtime, env);
            case "ToString":
                return EvalToString(node, runtime, env);
            case "StrConcat":
                return EvalStrConcat(node, runtime, env);
            case "StrEscape":
                return EvalStrEscape(node, runtime, env);
            case "MakeBlock":
                return EvalMakeBlock(node, runtime, env);
            case "AppendChild":
                return EvalAppendChild(node, runtime, env);
            case "MakeErr":
                return EvalMakeErr(node, runtime, env);
            case "MakeLitString":
                return EvalMakeLitString(node, runtime, env);
            case "Event":
            case "Command":
            case "HttpRequest":
            case "Route":
            case "Match":
            case "Map":
            case "Field":
            case "Bytecode":
                return AosValue.FromNode(node);
            case "NodeKind":
                return EvalNodeKind(node, runtime, env);
            case "NodeId":
                return EvalNodeId(node, runtime, env);
            case "AttrCount":
                return EvalAttrCount(node, runtime, env);
            case "AttrKey":
                return EvalAttrKey(node, runtime, env);
            case "AttrValueKind":
                return EvalAttrValueKind(node, runtime, env);
            case "AttrValueString":
                return EvalAttrValueString(node, runtime, env);
            case "AttrValueInt":
                return EvalAttrValueInt(node, runtime, env);
            case "AttrValueBool":
                return EvalAttrValueBool(node, runtime, env);
            case "ChildCount":
                return EvalChildCount(node, runtime, env);
            case "ChildAt":
                return EvalChildAt(node, runtime, env);
            case "If":
                if (node.Children.Count < 2)
                {
                    return AosValue.Unknown;
                }
                var cond = EvalNode(node.Children[0], runtime, env);
                var condValue = cond.Kind == AosValueKind.Bool && cond.AsBool();
                if (cond.Kind != AosValueKind.Bool)
                {
                    return AosValue.Unknown;
                }
                if (condValue)
                {
                    return EvalNode(node.Children[1], runtime, env);
                }
                if (node.Children.Count >= 3)
                {
                    return EvalNode(node.Children[2], runtime, env);
                }
                return AosValue.Void;
            case "Block":
                AosValue result = AosValue.Void;
                foreach (var child in node.Children)
                {
                    result = EvalNode(child, runtime, env);
                    if (IsErrValue(result))
                    {
                        return result;
                    }
                }
                return result;
            case "Return":
                if (node.Children.Count == 1)
                {
                    throw new ReturnSignal(EvalNode(node.Children[0], runtime, env));
                }
                throw new ReturnSignal(AosValue.Void);
            default:
                return AosValue.Unknown;
        }
    }

    private AosValue EvalCall(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("target", out var targetAttr) || targetAttr.Kind != AosAttrKind.Identifier)
        {
            return AosValue.Unknown;
        }

        var target = targetAttr.AsString();
        if (target == "math.add")
        {
            if (!runtime.Permissions.Contains("math"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 2)
            {
                return AosValue.Unknown;
            }
            var left = EvalNode(node.Children[0], runtime, env);
            var right = EvalNode(node.Children[1], runtime, env);
            if (left.Kind != AosValueKind.Int || right.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }
            return AosValue.FromInt(left.AsInt() + right.AsInt());
        }

        if (target == "console.print")
        {
            if (!runtime.Permissions.Contains("console"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }
            var arg = EvalNode(node.Children[0], runtime, env);
            if (arg.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }
            VmSyscalls.ConsolePrintLine(arg.AsString());
            return AosValue.Void;
        }

        if (target == "io.print")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var value = EvalNode(node.Children[0], runtime, env);
            if (value.Kind == AosValueKind.Unknown)
            {
                return AosValue.Unknown;
            }

            VmSyscalls.IoPrint(ValueToDisplayString(value));
            return AosValue.Void;
        }

        if (target == "io.write")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var value = EvalNode(node.Children[0], runtime, env);
            if (value.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            VmSyscalls.IoWrite(value.AsString());
            return AosValue.Void;
        }

        if (target == "io.readLine")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 0)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromString(VmSyscalls.IoReadLine());
        }

        if (target == "io.readAllStdin")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 0)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromString(VmSyscalls.IoReadAllStdin());
        }

        if (target == "io.readFile")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var pathValue = EvalNode(node.Children[0], runtime, env);
            if (pathValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromString(VmSyscalls.IoReadFile(pathValue.AsString()));
        }

        if (target == "io.fileExists")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var pathValue = EvalNode(node.Children[0], runtime, env);
            if (pathValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromBool(VmSyscalls.IoFileExists(pathValue.AsString()));
        }

        if (target == "io.pathExists")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var pathValue = EvalNode(node.Children[0], runtime, env);
            if (pathValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromBool(VmSyscalls.IoPathExists(pathValue.AsString()));
        }

        if (target == "io.makeDir")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var pathValue = EvalNode(node.Children[0], runtime, env);
            if (pathValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            VmSyscalls.IoMakeDir(pathValue.AsString());
            return AosValue.Void;
        }

        if (target == "io.writeFile")
        {
            if (!runtime.Permissions.Contains("io"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 2)
            {
                return AosValue.Unknown;
            }

            var pathValue = EvalNode(node.Children[0], runtime, env);
            var textValue = EvalNode(node.Children[1], runtime, env);
            if (pathValue.Kind != AosValueKind.String || textValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            VmSyscalls.IoWriteFile(pathValue.AsString(), textValue.AsString());
            return AosValue.Void;
        }

        if (TryEvaluateSysCall(target, node, runtime, env, out var sysValue))
        {
            return sysValue;
        }

        if (target == "sys.vm_run")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 3)
            {
                return AosValue.Unknown;
            }

            var bytecodeValue = EvalNode(node.Children[0], runtime, env);
            var entryValue = EvalNode(node.Children[1], runtime, env);
            var argsValue = EvalNode(node.Children[2], runtime, env);
            if (bytecodeValue.Kind != AosValueKind.Node || entryValue.Kind != AosValueKind.String || argsValue.Kind != AosValueKind.Node)
            {
                return AosValue.Unknown;
            }

            try
            {
                var bytecodeNode = bytecodeValue.AsNode();
                var entryName = entryValue.AsString();
                var vm = VmProgramLoader.Load(bytecodeNode, BytecodeAdapter.Instance);
                var args = BuildVmArgs(vm, entryName, argsValue.AsNode());
                return VmEngine.Run<AosNode, AosValue>(
                    vm,
                    entryName,
                    args,
                    new VmExecutionAdapter(this, runtime));
            }
            catch (VmRuntimeException ex)
            {
                return AosValue.FromNode(CreateErrNode("vm_err", ex.Code, ex.Message, ex.NodeId, node.Span));
            }
        }

        if (target == "compiler.parse")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            if (text.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            var parse = AosParsing.Parse(text.AsString());

            if (parse.Root is not null && parse.Diagnostics.Count == 0 && parse.Root.Kind == "Program")
            {
                return AosValue.FromNode(parse.Root);
            }

            var diagnostic = parse.Diagnostics.FirstOrDefault();
            if (diagnostic is null && parse.Root is not null && parse.Root.Kind != "Program")
            {
                diagnostic = new AosDiagnostic("PAR001", "Expected Program root.", parse.Root.Id, parse.Root.Span);
            }
            diagnostic ??= new AosDiagnostic("PAR000", "Parse failed.", "unknown", null);
            return AosValue.FromNode(CreateErrNode("parse_err", diagnostic.Code, diagnostic.Message, diagnostic.NodeId ?? "unknown", node.Span));
        }

        if (target == "compiler.emitBytecode")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return AosValue.Unknown;
            }

            try
            {
                var resolved = ResolveImportsForBytecode(
                    input.AsNode(),
                    runtime.ModuleBaseDir,
                    new HashSet<string>(StringComparer.Ordinal));
                return AosValue.FromNode(BytecodeCompiler.Compile(resolved, allowImportNodes: true));
            }
            catch (VmRuntimeException ex)
            {
                return AosValue.FromNode(CreateErrNode("vm_emit_err", ex.Code, ex.Message, ex.NodeId, node.Span));
            }
        }

        if (target == "compiler.strCompare")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 2)
            {
                return AosValue.Unknown;
            }

            var left = EvalNode(node.Children[0], runtime, env);
            var right = EvalNode(node.Children[1], runtime, env);
            if (left.Kind != AosValueKind.String || right.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            var cmp = string.CompareOrdinal(left.AsString(), right.AsString());
            if (cmp < 0)
            {
                return AosValue.FromInt(-1);
            }
            if (cmp > 0)
            {
                return AosValue.FromInt(1);
            }
            return AosValue.FromInt(0);
        }

        if (target == "compiler.strToken")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 2)
            {
                return AosValue.Unknown;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            var index = EvalNode(node.Children[1], runtime, env);
            if (text.Kind != AosValueKind.String || index.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }

            var tokens = text.AsString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var i = index.AsInt();
            if (i < 0 || i >= tokens.Length)
            {
                return AosValue.FromString(string.Empty);
            }

            return AosValue.FromString(tokens[i]);
        }

        if (target == "compiler.parseHttpRequest")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            if (text.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromNode(ParseHttpRequestNode(text.AsString(), node.Span));
        }

        if (target == "compiler.format")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromString(AosFormatter.Format(input.AsNode()));
        }

        if (target == "compiler.validate")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return AosValue.Unknown;
            }

            var structural = new AosStructuralValidator();
            var diagnostics = structural.Validate(input.AsNode());
            return AosValue.FromNode(CreateDiagnosticsNode(diagnostics, node.Span));
        }

        if (target == "compiler.validateHost")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return AosValue.Unknown;
            }

            var validator = new AosValidator();
            var diagnostics = validator.Validate(input.AsNode(), null, runtime.Permissions, runStructural: false).Diagnostics;
            return AosValue.FromNode(CreateDiagnosticsNode(diagnostics, node.Span));
        }

        if (target == "compiler.test")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var dirValue = EvalNode(node.Children[0], runtime, env);
            if (dirValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromInt(RunGoldenTests(dirValue.AsString()));
        }

        if (target == "compiler.run")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return AosValue.Unknown;
            }

            AosStandardLibraryLoader.EnsureLoaded(runtime, this);
            var runEnv = new Dictionary<string, AosValue>(StringComparer.Ordinal);
            var value = Evaluate(input.AsNode(), runtime, runEnv);
            if (IsErrValue(value))
            {
                return value;
            }

            return AosValue.FromNode(ToRuntimeNode(value));
        }

        if (target == "compiler.publish")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 2)
            {
                return AosValue.Unknown;
            }

            var projectNameValue = EvalNode(node.Children[1], runtime, env);
            if (projectNameValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            if (!runtime.Env.TryGetValue("argv", out var argvValue) || argvValue.Kind != AosValueKind.Node)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB001", "publish directory argument not found.", node.Id, node.Span));
            }

            var argvNode = argvValue.AsNode();
            if (argvNode.Children.Count < 2)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB001", "publish directory argument not found.", node.Id, node.Span));
            }

            var dirNode = argvNode.Children[1];
            if (!dirNode.Attrs.TryGetValue("value", out var dirAttr) || dirAttr.Kind != AosAttrKind.String)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB002", "invalid publish directory argument.", node.Id, node.Span));
            }

            var publishDir = dirAttr.AsString();
            var projectName = projectNameValue.AsString();
            var bundlePath = HostFileSystem.Combine(publishDir, $"{projectName}.aibundle");
            var outputBinaryPath = HostFileSystem.Combine(publishDir, projectName);
            var libraryPath = HostFileSystem.Combine(publishDir, $"{projectName}.ailib");

            var manifestPath = HostFileSystem.Combine(publishDir, "project.aiproj");
            if (TryLoadProjectNode(manifestPath, out var projectNode, out var manifestErr) && projectNode is not null)
            {
                if (ValidateProjectIncludesForPublish(publishDir, projectNode, out var includeErr))
                {
                    if (TryGetStringProjectAttr(projectNode, "entryExport", out var entryExportValue) &&
                        string.Equals(entryExportValue, "library", StringComparison.Ordinal))
                    {
                        if (!TryGetStringProjectAttr(projectNode, "version", out var libraryVersion) || string.IsNullOrWhiteSpace(libraryVersion))
                        {
                            return AosValue.FromNode(CreateErrNode("publish_err", "PUB014", "Library project requires non-empty version.", projectNode.Id, node.Span));
                        }

                        var canonicalProgram = new AosNode(
                            "Program",
                            "libp1",
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                            new List<AosNode>
                            {
                                new AosNode(
                                    "Project",
                                    "proj1",
                                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                                    {
                                        ["name"] = new AosAttrValue(AosAttrKind.String, projectName),
                                        ["entryFile"] = new AosAttrValue(AosAttrKind.String, TryGetStringProjectAttr(projectNode, "entryFile", out var ef) ? ef : string.Empty),
                                        ["entryExport"] = new AosAttrValue(AosAttrKind.String, "library"),
                                        ["version"] = new AosAttrValue(AosAttrKind.String, libraryVersion)
                                    },
                                    new List<AosNode>(),
                                    node.Span)
                            },
                            node.Span);
                        try
                        {
                            HostFileSystem.EnsureDirectory(publishDir);
                            HostFileSystem.WriteAllText(libraryPath, AosFormatter.Format(canonicalProgram));
                            return AosValue.FromInt(0);
                        }
                        catch (Exception ex)
                        {
                            return AosValue.FromNode(CreateErrNode("publish_err", "PUB003", ex.Message, node.Id, node.Span));
                        }
                    }
                }
                else
                {
                    return AosValue.FromNode(includeErr!);
                }
            }
            else if (manifestErr is not null)
            {
                return AosValue.FromNode(manifestErr);
            }

            var rawBundleNode = node.Children.Count > 0 ? node.Children[0] : null;
            if (rawBundleNode is null || rawBundleNode.Kind != "Bundle")
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB005", "publish requires Bundle node.", node.Id, node.Span));
            }

            if (!rawBundleNode.Attrs.TryGetValue("entryFile", out var entryFileAttr) || entryFileAttr.Kind != AosAttrKind.String)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB006", "Bundle missing entryFile.", node.Id, node.Span));
            }

            var entryFile = entryFileAttr.AsString();
            var entryPath = HostFileSystem.GetFullPath(HostFileSystem.Combine(publishDir, entryFile));
            if (!HostFileSystem.FileExists(entryPath))
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB007", $"Entry file not found: {entryFile}", node.Id, node.Span));
            }

            AosNode? entryProgram;
            try
            {
                var parsed = AosParsing.ParseFile(entryPath);
                if (parsed.Root is null || parsed.Diagnostics.Count > 0 || parsed.Root.Kind != "Program")
                {
                    var diagnostic = parsed.Diagnostics.FirstOrDefault();
                    return AosValue.FromNode(CreateErrNode(
                        "publish_err",
                        diagnostic?.Code ?? "PAR000",
                        diagnostic?.Message ?? "Failed to parse entry file.",
                        diagnostic?.NodeId ?? node.Id,
                        node.Span));
                }
                entryProgram = parsed.Root;
            }
            catch (Exception ex)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB008", ex.Message, node.Id, node.Span));
            }

            AosNode bytecodeNode;
            try
            {
                bytecodeNode = BytecodeCompiler.Compile(entryProgram!, allowImportNodes: true);
            }
            catch (VmRuntimeException vmEx)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", vmEx.Code, vmEx.Message, vmEx.NodeId, node.Span));
            }

            var bytecodeText = AosFormatter.Format(bytecodeNode);

            try
            {
                HostFileSystem.EnsureDirectory(publishDir);
                HostFileSystem.WriteAllText(bundlePath, bytecodeText);

                var sourceBinary = HostExecutableLocator.ResolveHostBinaryPath();
                if (sourceBinary is null)
                {
                    return AosValue.FromNode(CreateErrNode("publish_err", "PUB004", "host executable not found.", node.Id, node.Span));
                }

                if (!BundlePublisher.TryWriteEmbeddedBytecodeExecutable(sourceBinary, outputBinaryPath, bytecodeText, out var bundleWriteError))
                {
                    return AosValue.FromNode(CreateErrNode("publish_err", "PUB003", bundleWriteError, node.Id, node.Span));
                }

                return AosValue.FromInt(0);
            }
            catch (Exception ex)
            {
                return AosValue.FromNode(CreateErrNode("publish_err", "PUB003", ex.Message, node.Id, node.Span));
            }
        }

        if (!env.TryGetValue(target, out var functionValue))
        {
            runtime.Env.TryGetValue(target, out functionValue);
        }

        if (functionValue is not null && functionValue.Kind == AosValueKind.Function)
        {
            var args = node.Children.Select(child => EvalNode(child, runtime, env)).ToList();
            return EvalFunctionCall(functionValue.AsFunction(), args, runtime);
        }

        return AosValue.Unknown;
    }

    private AosValue EvalImport(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("path", out var pathAttr) || pathAttr.Kind != AosAttrKind.String)
        {
            return CreateRuntimeErr("RUN020", "Import requires string path attribute.", node.Id, node.Span);
        }

        if (node.Children.Count != 0)
        {
            return CreateRuntimeErr("RUN021", "Import must not have children.", node.Id, node.Span);
        }

        var relativePath = pathAttr.AsString();
        if (HostFileSystem.IsPathRooted(relativePath))
        {
            return CreateRuntimeErr("RUN022", "Import path must be relative.", node.Id, node.Span);
        }

        var absolutePath = HostFileSystem.GetFullPath(HostFileSystem.Combine(runtime.ModuleBaseDir, relativePath));
        if (runtime.ModuleExports.TryGetValue(absolutePath, out var cachedExports))
        {
            foreach (var exportEntry in cachedExports)
            {
                env[exportEntry.Key] = exportEntry.Value;
            }
            return AosValue.Void;
        }

        if (runtime.ModuleLoading.Contains(absolutePath))
        {
            return CreateRuntimeErr("RUN023", "Circular import detected.", node.Id, node.Span);
        }

        if (!HostFileSystem.FileExists(absolutePath))
        {
            return CreateRuntimeErr("RUN024", $"Import file not found: {relativePath}", node.Id, node.Span);
        }

        AosParseResult parse;
        try
        {
            parse = AosParsing.ParseFile(absolutePath);
        }
        catch (Exception ex)
        {
            return CreateRuntimeErr("RUN025", $"Failed to read import: {ex.Message}", node.Id, node.Span);
        }

        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var diag = parse.Diagnostics.FirstOrDefault();
            return CreateRuntimeErr(diag?.Code ?? "PAR000", diag?.Message ?? "Parse failed.", node.Id, node.Span);
        }

        if (parse.Root.Kind != "Program")
        {
            return CreateRuntimeErr("RUN026", "Imported root must be Program.", node.Id, node.Span);
        }

        var structural = new AosStructuralValidator();
        var validation = structural.Validate(parse.Root);
        if (validation.Count > 0)
        {
            var first = validation[0];
            return CreateRuntimeErr(first.Code, first.Message, first.NodeId ?? node.Id, node.Span);
        }

        var priorBaseDir = runtime.ModuleBaseDir;
        runtime.ModuleBaseDir = HostFileSystem.GetDirectoryName(absolutePath) ?? priorBaseDir;
        runtime.ModuleLoading.Add(absolutePath);
        runtime.ExportScopes.Push(new Dictionary<string, AosValue>(StringComparer.Ordinal));
        try
        {
            var moduleEnv = new Dictionary<string, AosValue>(StringComparer.Ordinal);
            var result = Evaluate(parse.Root, runtime, moduleEnv);
            if (IsErrValue(result))
            {
                return result;
            }

            var exports = runtime.ExportScopes.Peek();
            runtime.ModuleExports[absolutePath] = new Dictionary<string, AosValue>(exports, StringComparer.Ordinal);
            foreach (var exportEntry in exports)
            {
                env[exportEntry.Key] = exportEntry.Value;
            }

            return AosValue.Void;
        }
        finally
        {
            runtime.ExportScopes.Pop();
            runtime.ModuleLoading.Remove(absolutePath);
            runtime.ModuleBaseDir = priorBaseDir;
        }
    }

    private static AosNode ResolveImportsForBytecode(AosNode program, string moduleBaseDir, HashSet<string> loading)
    {
        if (program.Kind != "Program")
        {
            throw new VmRuntimeException("VM001", "compiler.emitBytecode expects Program node.", program.Id);
        }

        var outputChildren = new List<AosNode>();
        var seenLets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in program.Children)
        {
            if (child.Kind != "Import")
            {
                outputChildren.Add(child);
                if (child.Kind == "Let" &&
                    child.Attrs.TryGetValue("name", out var letNameAttr) &&
                    letNameAttr.Kind == AosAttrKind.Identifier)
                {
                    seenLets.Add(letNameAttr.AsString());
                }
                continue;
            }

            if (!child.Attrs.TryGetValue("path", out var pathAttr) || pathAttr.Kind != AosAttrKind.String)
            {
                throw new VmRuntimeException("VM001", "Import requires string path attribute.", child.Id);
            }

            var relativePath = pathAttr.AsString();
            if (HostFileSystem.IsPathRooted(relativePath))
            {
                throw new VmRuntimeException("VM001", "Import path must be relative.", child.Id);
            }

            var fullPath = HostFileSystem.GetFullPath(HostFileSystem.Combine(moduleBaseDir, relativePath));
            if (!loading.Add(fullPath))
            {
                throw new VmRuntimeException("VM001", "Circular import detected.", child.Id);
            }
            if (!HostFileSystem.FileExists(fullPath))
            {
                loading.Remove(fullPath);
                throw new VmRuntimeException("VM001", $"Import file not found: {relativePath}", child.Id);
            }

            AosParseResult parse;
            try
            {
                parse = AosParsing.ParseFile(fullPath);
            }
            catch (Exception ex)
            {
                loading.Remove(fullPath);
                throw new VmRuntimeException("VM001", $"Failed to read import: {ex.Message}", child.Id);
            }

            if (parse.Root is null || parse.Diagnostics.Count > 0 || parse.Root.Kind != "Program")
            {
                loading.Remove(fullPath);
                var diag = parse.Diagnostics.FirstOrDefault();
                throw new VmRuntimeException("VM001", diag?.Message ?? "Imported root must be Program.", child.Id);
            }

            var importedDir = HostFileSystem.GetDirectoryName(fullPath) ?? moduleBaseDir;
            var flattenedImport = ResolveImportsForBytecode(parse.Root, importedDir, loading);
            loading.Remove(fullPath);
            foreach (var letNode in ExtractExportedLets(flattenedImport))
            {
                if (letNode.Attrs.TryGetValue("name", out var nameAttr) &&
                    nameAttr.Kind == AosAttrKind.Identifier &&
                    seenLets.Add(nameAttr.AsString()))
                {
                    outputChildren.Add(letNode);
                }
            }
        }

        return new AosNode(
            "Program",
            program.Id,
            new Dictionary<string, AosAttrValue>(program.Attrs, StringComparer.Ordinal),
            outputChildren,
            program.Span);
    }

    private static List<AosNode> ExtractExportedLets(AosNode program)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in program.Children)
        {
            if (child.Kind == "Export" &&
                child.Attrs.TryGetValue("name", out var exportName) &&
                exportName.Kind == AosAttrKind.Identifier)
            {
                names.Add(exportName.AsString());
            }
        }

        var lets = new List<AosNode>();
        foreach (var child in program.Children)
        {
            if (child.Kind == "Let" &&
                child.Attrs.TryGetValue("name", out var letName) &&
                letName.Kind == AosAttrKind.Identifier &&
                names.Contains(letName.AsString()))
            {
                lets.Add(child);
            }
        }
        return lets;
    }

    private AosValue EvalExport(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
        {
            return CreateRuntimeErr("RUN027", "Export requires identifier name attribute.", node.Id, node.Span);
        }

        if (node.Children.Count != 0)
        {
            return CreateRuntimeErr("RUN028", "Export must not have children.", node.Id, node.Span);
        }

        if (runtime.ExportScopes.Count == 0)
        {
            return AosValue.Void;
        }

        var name = nameAttr.AsString();
        if (!env.TryGetValue(name, out var value))
        {
            return CreateRuntimeErr("RUN029", $"Export name not found: {name}", node.Id, node.Span);
        }

        runtime.ExportScopes.Peek()[name] = value;
        return AosValue.Void;
    }

    private AosValue EvalFunction(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("params", out var paramsAttr) || paramsAttr.Kind != AosAttrKind.Identifier)
        {
            return AosValue.Unknown;
        }

        if (node.Children.Count != 1 || node.Children[0].Kind != "Block")
        {
            return AosValue.Unknown;
        }

        var parameters = paramsAttr.AsString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var captured = new Dictionary<string, AosValue>(env, StringComparer.Ordinal);
        var function = new AosFunction(parameters, node.Children[0], captured);
        return AosValue.FromFunction(function);
    }

    private AosValue EvalFunctionCall(AosFunction function, List<AosValue> args, AosRuntime runtime)
    {
        if (function.Parameters.Count != args.Count)
        {
            return AosValue.Unknown;
        }

        var localEnv = new Dictionary<string, AosValue>(runtime.Env, StringComparer.Ordinal);
        foreach (var entry in function.CapturedEnv)
        {
            localEnv[entry.Key] = entry.Value;
        }
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            localEnv[function.Parameters[i]] = args[i];
        }

        try
        {
            return EvalNode(function.Body, runtime, localEnv);
        }
        catch (ReturnSignal signal)
        {
            return signal.Value;
        }
    }

    private AosValue EvalEq(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 2)
        {
            return AosValue.Unknown;
        }
        var left = EvalNode(node.Children[0], runtime, env);
        var right = EvalNode(node.Children[1], runtime, env);
        if (left.Kind != right.Kind)
        {
            return AosValue.FromBool(false);
        }
        return left.Kind switch
        {
            AosValueKind.String => AosValue.FromBool(left.AsString() == right.AsString()),
            AosValueKind.Int => AosValue.FromBool(left.AsInt() == right.AsInt()),
            AosValueKind.Bool => AosValue.FromBool(left.AsBool() == right.AsBool()),
            _ => AosValue.FromBool(false)
        };
    }

    private AosValue EvalAdd(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 2)
        {
            return AosValue.Unknown;
        }
        var left = EvalNode(node.Children[0], runtime, env);
        var right = EvalNode(node.Children[1], runtime, env);
        if (left.Kind != AosValueKind.Int || right.Kind != AosValueKind.Int)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromInt(left.AsInt() + right.AsInt());
    }

    private AosValue EvalToString(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var value = EvalNode(node.Children[0], runtime, env);
        return value.Kind switch
        {
            AosValueKind.Int => AosValue.FromString(value.AsInt().ToString()),
            AosValueKind.Bool => AosValue.FromString(value.AsBool() ? "true" : "false"),
            _ => AosValue.Unknown
        };
    }

    private AosValue EvalStrConcat(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 2)
        {
            return AosValue.Unknown;
        }
        var left = EvalNode(node.Children[0], runtime, env);
        var right = EvalNode(node.Children[1], runtime, env);
        if (left.Kind != AosValueKind.String || right.Kind != AosValueKind.String)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromString(left.AsString() + right.AsString());
    }

    private AosValue EvalStrEscape(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var value = EvalNode(node.Children[0], runtime, env);
        if (value.Kind != AosValueKind.String)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromString(EscapeString(value.AsString()));
    }

    private AosValue EvalMakeBlock(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var idValue = EvalNode(node.Children[0], runtime, env);
        if (idValue.Kind != AosValueKind.String)
        {
            return AosValue.Unknown;
        }
        var result = new AosNode("Block", idValue.AsString(), new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), new List<AosNode>(), node.Span);
        return AosValue.FromNode(result);
    }

    private AosValue EvalAppendChild(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 2)
        {
            return AosValue.Unknown;
        }
        var parentValue = EvalNode(node.Children[0], runtime, env);
        var childValue = EvalNode(node.Children[1], runtime, env);
        if (parentValue.Kind != AosValueKind.Node || childValue.Kind != AosValueKind.Node)
        {
            return AosValue.Unknown;
        }
        var parent = parentValue.AsNode();
        var child = childValue.AsNode();
        var newAttrs = new Dictionary<string, AosAttrValue>(parent.Attrs, StringComparer.Ordinal);
        var newChildren = new List<AosNode>(parent.Children) { child };
        var result = new AosNode(parent.Kind, parent.Id, newAttrs, newChildren, parent.Span);
        return AosValue.FromNode(result);
    }

    private AosValue EvalMakeErr(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 4)
        {
            return AosValue.Unknown;
        }
        var idValue = EvalNode(node.Children[0], runtime, env);
        var codeValue = EvalNode(node.Children[1], runtime, env);
        var messageValue = EvalNode(node.Children[2], runtime, env);
        var nodeIdValue = EvalNode(node.Children[3], runtime, env);
        if (idValue.Kind != AosValueKind.String || codeValue.Kind != AosValueKind.String || messageValue.Kind != AosValueKind.String || nodeIdValue.Kind != AosValueKind.String)
        {
            return AosValue.Unknown;
        }
        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["code"] = new AosAttrValue(AosAttrKind.Identifier, codeValue.AsString()),
            ["message"] = new AosAttrValue(AosAttrKind.String, messageValue.AsString()),
            ["nodeId"] = new AosAttrValue(AosAttrKind.Identifier, nodeIdValue.AsString())
        };
        var result = new AosNode("Err", idValue.AsString(), attrs, new List<AosNode>(), node.Span);
        return AosValue.FromNode(result);
    }

    private AosValue EvalMakeLitString(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 2)
        {
            return AosValue.Unknown;
        }
        var idValue = EvalNode(node.Children[0], runtime, env);
        var strValue = EvalNode(node.Children[1], runtime, env);
        if (idValue.Kind != AosValueKind.String || strValue.Kind != AosValueKind.String)
        {
            return AosValue.Unknown;
        }
        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["value"] = new AosAttrValue(AosAttrKind.String, strValue.AsString())
        };
        var result = new AosNode("Lit", idValue.AsString(), attrs, new List<AosNode>(), node.Span);
        return AosValue.FromNode(result);
    }

    private AosValue EvalNodeKind(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var target = EvalNode(node.Children[0], runtime, env);
        if (target.Kind != AosValueKind.Node)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromString(target.AsNode().Kind);
    }

    private AosValue EvalNodeId(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var target = EvalNode(node.Children[0], runtime, env);
        if (target.Kind != AosValueKind.Node)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromString(target.AsNode().Id);
    }

    private AosValue EvalAttrCount(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var target = EvalNode(node.Children[0], runtime, env);
        if (target.Kind != AosValueKind.Node)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromInt(target.AsNode().Attrs.Count);
    }

    private AosValue EvalAttrKey(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        var (targetNode, index) = ResolveNodeIndex(node, runtime, env);
        if (targetNode is null)
        {
            return AosValue.Unknown;
        }
        var keys = targetNode.Attrs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (index < 0 || index >= keys.Count)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromString(keys[index]);
    }

    private AosValue EvalAttrValueKind(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        var (targetNode, index) = ResolveNodeIndex(node, runtime, env);
        if (targetNode is null)
        {
            return AosValue.Unknown;
        }
        var entry = GetAttrEntry(targetNode, index);
        if (entry is null)
        {
            return AosValue.Unknown;
        }
        var attr = entry.Value.Value;
        return AosValue.FromString(attr.Kind.ToString().ToLowerInvariant());
    }

    private AosValue EvalAttrValueString(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        var (targetNode, index) = ResolveNodeIndex(node, runtime, env);
        if (targetNode is null)
        {
            return AosValue.Unknown;
        }
        var entry = GetAttrEntry(targetNode, index);
        if (entry is null)
        {
            return AosValue.Unknown;
        }
        var attr = entry.Value.Value;
        return attr.Kind switch
        {
            AosAttrKind.String => AosValue.FromString(attr.AsString()),
            AosAttrKind.Identifier => AosValue.FromString(attr.AsString()),
            _ => AosValue.Unknown
        };
    }

    private AosValue EvalAttrValueInt(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        var (targetNode, index) = ResolveNodeIndex(node, runtime, env);
        if (targetNode is null)
        {
            return AosValue.Unknown;
        }
        var entry = GetAttrEntry(targetNode, index);
        if (entry is null || entry.Value.Value.Kind != AosAttrKind.Int)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromInt(entry.Value.Value.AsInt());
    }

    private AosValue EvalAttrValueBool(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        var (targetNode, index) = ResolveNodeIndex(node, runtime, env);
        if (targetNode is null)
        {
            return AosValue.Unknown;
        }
        var entry = GetAttrEntry(targetNode, index);
        if (entry is null || entry.Value.Value.Kind != AosAttrKind.Bool)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromBool(entry.Value.Value.AsBool());
    }

    private AosValue EvalChildCount(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 1)
        {
            return AosValue.Unknown;
        }
        var target = EvalNode(node.Children[0], runtime, env);
        if (target.Kind != AosValueKind.Node)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromInt(target.AsNode().Children.Count);
    }

    private AosValue EvalChildAt(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        var (targetNode, index) = ResolveNodeIndex(node, runtime, env);
        if (targetNode is null)
        {
            return AosValue.Unknown;
        }
        if (index < 0 || index >= targetNode.Children.Count)
        {
            return AosValue.Unknown;
        }
        return AosValue.FromNode(targetNode.Children[index]);
    }

    private (AosNode? node, int index) ResolveNodeIndex(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (node.Children.Count != 2)
        {
            return (null, -1);
        }
        var target = EvalNode(node.Children[0], runtime, env);
        var indexValue = EvalNode(node.Children[1], runtime, env);
        if (target.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
        {
            return (null, -1);
        }
        return (target.AsNode(), indexValue.AsInt());
    }

    private static KeyValuePair<string, AosAttrValue>? GetAttrEntry(AosNode node, int index)
    {
        var entries = node.Attrs.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
        if (index < 0 || index >= entries.Count)
        {
            return null;
        }
        return entries[index];
    }

    private static string EscapeString(string value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string ValueToDisplayString(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.String => value.AsString(),
            AosValueKind.Int => value.AsInt().ToString(),
            AosValueKind.Bool => value.AsBool() ? "true" : "false",
            AosValueKind.Void => "void",
            AosValueKind.Node => $"{value.AsNode().Kind}#{value.AsNode().Id}",
            AosValueKind.Function => "function",
            _ => "unknown"
        };
    }

    private static AosNode CreateErrNode(string id, string code, string message, string nodeId, AosSpan span)
    {
        return new AosNode(
            "Err",
            id,
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["code"] = new AosAttrValue(AosAttrKind.Identifier, code),
                ["message"] = new AosAttrValue(AosAttrKind.String, message),
                ["nodeId"] = new AosAttrValue(AosAttrKind.Identifier, nodeId)
            },
            new List<AosNode>(),
            span);
    }

    private static AosNode ParseHttpRequestNode(string raw, AosSpan span)
    {
        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        var splitIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        var head = splitIndex >= 0 ? normalized[..splitIndex] : normalized;
        var body = splitIndex >= 0 ? normalized[(splitIndex + 2)..] : string.Empty;

        var lines = head.Split('\n');
        var requestLine = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = requestParts.Length > 0 ? requestParts[0] : string.Empty;
        var rawPath = requestParts.Length > 1 ? requestParts[1] : string.Empty;

        var query = string.Empty;
        var path = rawPath;
        var queryStart = rawPath.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            path = rawPath[..queryStart];
            query = queryStart + 1 < rawPath.Length ? rawPath[(queryStart + 1)..] : string.Empty;
        }

        var headerMapChildren = new List<AosNode>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headerMapChildren.Add(new AosNode(
                "Field",
                $"http_header_{i}",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["key"] = new AosAttrValue(AosAttrKind.String, key)
                },
                new List<AosNode>
                {
                    new AosNode(
                        "Lit",
                        $"http_header_val_{i}",
                        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                        {
                            ["value"] = new AosAttrValue(AosAttrKind.String, value)
                        },
                        new List<AosNode>(),
                        span)
                },
                span));
        }

        var queryMapChildren = new List<AosNode>();
        if (!string.IsNullOrEmpty(query))
        {
            var queryParts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < queryParts.Length; i++)
            {
                var part = queryParts[i];
                var eq = part.IndexOf('=');
                var key = eq >= 0 ? part[..eq] : part;
                var value = eq >= 0 && eq + 1 < part.Length ? part[(eq + 1)..] : string.Empty;
                queryMapChildren.Add(new AosNode(
                    "Field",
                    $"http_query_{i}",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["key"] = new AosAttrValue(AosAttrKind.String, key)
                    },
                    new List<AosNode>
                    {
                        new AosNode(
                            "Lit",
                            $"http_query_val_{i}",
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                            {
                                ["value"] = new AosAttrValue(AosAttrKind.String, value)
                            },
                            new List<AosNode>(),
                            span)
                    },
                    span));
            }
        }

        return new AosNode(
            "HttpRequest",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["method"] = new AosAttrValue(AosAttrKind.String, method),
                ["path"] = new AosAttrValue(AosAttrKind.String, path)
            },
            new List<AosNode>
            {
                new AosNode("Map", "http_headers", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), headerMapChildren, span),
                new AosNode("Map", "http_query", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), queryMapChildren, span)
            },
            span);
    }

    private static bool TryLoadProjectNode(string manifestPath, out AosNode? projectNode, out AosNode? errNode)
    {
        projectNode = null;
        errNode = null;

        if (!HostFileSystem.FileExists(manifestPath))
        {
            return false;
        }

        AosParseResult parse;
        try
        {
            parse = AosParsing.ParseFile(manifestPath);
        }
        catch (Exception ex)
        {
            errNode = CreateErrNode(
                "publish_err",
                "PUB009",
                $"Failed to read project manifest: {ex.Message}",
                "project",
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
            return false;
        }

        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var first = parse.Diagnostics.FirstOrDefault();
            errNode = CreateErrNode(
                "publish_err",
                first?.Code ?? "PAR000",
                first?.Message ?? "Failed to parse project manifest.",
                first?.NodeId ?? "project",
                parse.Root?.Span ?? new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
            return false;
        }

        if (parse.Root.Kind != "Program" || parse.Root.Children.Count != 1 || parse.Root.Children[0].Kind != "Project")
        {
            errNode = CreateErrNode(
                "publish_err",
                "PUB010",
                "project.aiproj must contain Program with one Project child.",
                parse.Root.Id,
                parse.Root.Span);
            return false;
        }

        projectNode = parse.Root.Children[0];
        return true;
    }

    private static bool TryGetStringProjectAttr(AosNode node, string key, out string value)
    {
        value = string.Empty;
        if (!node.Attrs.TryGetValue(key, out var attr) || attr.Kind != AosAttrKind.String)
        {
            return false;
        }

        value = attr.AsString();
        return true;
    }

    private static bool ValidateProjectIncludesForPublish(string publishDir, AosNode projectNode, out AosNode? errNode)
    {
        errNode = null;
        foreach (var includeNode in projectNode.Children)
        {
            if (includeNode.Kind != "Include")
            {
                continue;
            }

            if (!TryGetStringProjectAttr(includeNode, "name", out var includeName) ||
                !TryGetStringProjectAttr(includeNode, "path", out var includePath) ||
                !TryGetStringProjectAttr(includeNode, "version", out var includeVersion))
            {
                errNode = CreateErrNode("publish_err", "PUB011", "Include requires name, path, and version.", includeNode.Id, includeNode.Span);
                return false;
            }

            if (HostFileSystem.IsPathRooted(includePath))
            {
                errNode = CreateErrNode("publish_err", "PUB012", "Include path must be relative.", includeNode.Id, includeNode.Span);
                return false;
            }

            var includeDir = HostFileSystem.GetFullPath(HostFileSystem.Combine(publishDir, includePath));
            var includeManifestPath = HostFileSystem.Combine(includeDir, $"{includeName}.ailib");
            if (!HostFileSystem.FileExists(includeManifestPath))
            {
                errNode = CreateErrNode("publish_err", "PUB015", $"Included library not found: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!TryLoadProjectNode(includeManifestPath, out var includeProjectNode, out var includeParseErr))
            {
                errNode = includeParseErr ?? CreateErrNode("publish_err", "PUB016", $"Invalid included library manifest: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }
            if (includeProjectNode is null)
            {
                errNode = CreateErrNode("publish_err", "PUB016", $"Invalid included library manifest: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!TryGetStringProjectAttr(includeProjectNode, "name", out var actualName) ||
                !string.Equals(actualName, includeName, StringComparison.Ordinal))
            {
                errNode = CreateErrNode("publish_err", "PUB017", $"Included library name mismatch for {includeName}.", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!TryGetStringProjectAttr(includeProjectNode, "version", out var actualVersion))
            {
                errNode = CreateErrNode("publish_err", "PUB018", $"Included library missing version: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!string.Equals(actualVersion, includeVersion, StringComparison.Ordinal))
            {
                errNode = CreateErrNode(
                    "publish_err",
                    "PUB019",
                    $"Included library version mismatch for {includeName}: expected {includeVersion}, got {actualVersion}.",
                    includeNode.Id,
                    includeNode.Span);
                return false;
            }
        }

        return true;
    }

    private static AosNode CreateDiagnosticsNode(List<AosDiagnostic> diagnostics, AosSpan span)
    {
        var children = new List<AosNode>(diagnostics.Count);
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var diagnostic = diagnostics[i];
            children.Add(CreateErrNode(
                $"diag{i}",
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.NodeId ?? "unknown",
                span));
        }

        return new AosNode(
            "Block",
            "diagnostics",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            children,
            span);
    }

    private static string VmConstantKey(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.String => $"s:{value.AsString()}",
            AosValueKind.Int => $"i:{value.AsInt()}",
            AosValueKind.Bool => value.AsBool() ? "b:true" : "b:false",
            AosValueKind.Node => $"o:{EncodeNodeConstant(value.AsNode())}",
            AosValueKind.Unknown => "n:null",
            _ => throw new InvalidOperationException("Unsupported constant value.")
        };
    }

    private static string EncodeNodeConstant(AosNode node)
    {
        var wrapper = new AosNode(
            "Program",
            "bc_const_program",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            new List<AosNode> { node },
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(wrapper);
    }

    private static AosNode DecodeNodeConstant(string text, string nodeId)
    {
        var parse = AosParsing.Parse(text);
        if (parse.Root is null || parse.Diagnostics.Count > 0 || parse.Root.Kind != "Program" || parse.Root.Children.Count != 1)
        {
            throw new VmRuntimeException("VM001", "Invalid node constant encoding.", nodeId);
        }

        return parse.Root.Children[0];
    }

    private static AosNode BuildVmInstruction(int index, string op, int? a, int? b, string? s)
    {
        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["op"] = new AosAttrValue(AosAttrKind.Identifier, op)
        };
        if (a.HasValue)
        {
            attrs["a"] = new AosAttrValue(AosAttrKind.Int, a.Value);
        }
        if (b.HasValue)
        {
            attrs["b"] = new AosAttrValue(AosAttrKind.Int, b.Value);
        }
        if (!string.IsNullOrEmpty(s))
        {
            attrs["s"] = new AosAttrValue(AosAttrKind.String, s!);
        }
        return new AosNode(
            "Inst",
            $"i{index}",
            attrs,
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    }

    private bool TryEvaluateSysCall(
        string target,
        AosNode callNode,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;
        if (target == "sys.vm_run" || !SyscallContracts.IsSysTarget(target))
        {
            return false;
        }

        if (!runtime.Permissions.Contains("sys"))
        {
            return true;
        }

        var args = new List<SysValue>(callNode.Children.Count);
        for (var i = 0; i < callNode.Children.Count; i++)
        {
            args.Add(ToSysValue(EvalNode(callNode.Children[i], runtime, env)));
        }

        if (!VmSyscallDispatcher.TryInvoke(target, args, runtime.Network, out var sysResult))
        {
            return false;
        }

        result = FromSysValue(sysResult);
        return true;
    }

    private static SysValue ToSysValue(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.String => SysValue.String(value.AsString()),
            AosValueKind.Int => SysValue.Int(value.AsInt()),
            AosValueKind.Bool => SysValue.Bool(value.AsBool()),
            AosValueKind.Void => SysValue.Void(),
            _ => SysValue.Unknown()
        };
    }

    private static AosValue FromSysValue(SysValue value)
    {
        return value.Kind switch
        {
            VmValueKind.String => AosValue.FromString(value.StringValue),
            VmValueKind.Int => AosValue.FromInt(value.IntValue),
            VmValueKind.Bool => AosValue.FromBool(value.BoolValue),
            VmValueKind.Void => AosValue.Void,
            _ => AosValue.Unknown
        };
    }




    private sealed class ReturnSignal : Exception
    {
        public ReturnSignal(AosValue value)
        {
            Value = value;
        }

        public AosValue Value { get; }
    }

    private static bool IsErrValue(AosValue value)
    {
        return value.Kind == AosValueKind.Node && value.Data is AosNode node && node.Kind == "Err";
    }

    private static AosNode ToRuntimeNode(AosValue value)
    {
        return value.Kind switch
        {
            AosValueKind.Node => value.AsNode(),
            AosValueKind.String => new AosNode(
                "Lit",
                "runtime_string",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal) { ["value"] = new AosAttrValue(AosAttrKind.String, value.AsString()) },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            AosValueKind.Int => new AosNode(
                "Lit",
                "runtime_int",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal) { ["value"] = new AosAttrValue(AosAttrKind.Int, value.AsInt()) },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            AosValueKind.Bool => new AosNode(
                "Lit",
                "runtime_bool",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal) { ["value"] = new AosAttrValue(AosAttrKind.Bool, value.AsBool()) },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            AosValueKind.Void => new AosNode(
                "Block",
                "void",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))),
            _ => CreateErrNode(
                "runtime_err",
                "RUN030",
                "Unsupported runtime value.",
                "runtime",
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
        };
    }

    private static AosValue CreateRuntimeErr(string code, string message, string nodeId, AosSpan span)
    {
        return AosValue.FromNode(CreateErrNode("runtime_err", code, message, nodeId, span));
    }

}
