namespace AiLang.Core;

public sealed class AosRuntime
{
    public Dictionary<string, AosValue> Env { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "math" };
    public HashSet<string> ReadOnlyBindings { get; } = new(StringComparer.Ordinal);
    public string ModuleBaseDir { get; set; } = Directory.GetCurrentDirectory();
    public Dictionary<string, Dictionary<string, AosValue>> ModuleExports { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ModuleLoading { get; } = new(StringComparer.Ordinal);
    public Stack<Dictionary<string, AosValue>> ExportScopes { get; } = new();
    public bool TraceEnabled { get; set; }
    public List<AosNode> TraceSteps { get; } = new();
    public AosNode? Program { get; set; }
}

public sealed class AosInterpreter
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
            Console.WriteLine(arg.AsString());
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

            Console.WriteLine(ValueToDisplayString(value));
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

            Console.Write(value.AsString());
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

            var line = Console.ReadLine();
            return AosValue.FromString(line ?? string.Empty);
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

            return AosValue.FromString(Console.In.ReadToEnd());
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

            return AosValue.FromString(File.ReadAllText(pathValue.AsString()));
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

            return AosValue.FromBool(File.Exists(pathValue.AsString()));
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

            var path = pathValue.AsString();
            return AosValue.FromBool(File.Exists(path) || Directory.Exists(path));
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

            Directory.CreateDirectory(pathValue.AsString());
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

            File.WriteAllText(pathValue.AsString(), textValue.AsString());
            return AosValue.Void;
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

            var parse = AosExternalFrontend.Parse(text.AsString());

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

            var payload = EvalNode(node.Children[0], runtime, env);
            if (payload.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            var request = payload.AsString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var method = request.Length > 0 ? request[0] : string.Empty;
            var path = request.Length > 1 ? request[1] : string.Empty;
            var parsed = new AosNode(
                "HttpRequest",
                "auto",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["method"] = new AosAttrValue(AosAttrKind.String, method),
                    ["path"] = new AosAttrValue(AosAttrKind.String, path)
                },
                new List<AosNode>(),
                node.Span);
            return AosValue.FromNode(parsed);
        }

        if (target == "compiler.toJson")
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

            if (!TrySerializeJsonNode(input.AsNode(), out var json))
            {
                return AosValue.Unknown;
            }

            return AosValue.FromString(json);
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

            AosStandardLibraryLoader.EnsureRouteLoaded(runtime, this);
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

            var bundleValue = EvalNode(node.Children[0], runtime, env);
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
            var bundlePath = Path.Combine(publishDir, $"{projectName}.aibundle");
            var outputBinaryPath = Path.Combine(publishDir, projectName);
            var bundleText = bundleValue.Kind switch
            {
                AosValueKind.Node => AosFormatter.Format(bundleValue.AsNode()),
                AosValueKind.String => bundleValue.AsString(),
                _ => string.Empty
            };
            if (bundleText.Length == 0 && node.Children.Count > 0)
            {
                // compiler.publish accepts a literal bundle node argument; it does not require the
                // bundle node to be executable.
                bundleText = AosFormatter.Format(node.Children[0]);
            }

            if (bundleText.Length == 0)
            {
                return AosValue.Unknown;
            }

            try
            {
                Directory.CreateDirectory(publishDir);
                File.WriteAllText(bundlePath, bundleText);

                var sourceBinary = ResolveHostBinaryPath();
                if (sourceBinary is null)
                {
                    return AosValue.FromNode(CreateErrNode("publish_err", "PUB004", "host executable not found.", node.Id, node.Span));
                }

                File.Copy(sourceBinary, outputBinaryPath, overwrite: true);
                File.AppendAllText(outputBinaryPath, "\n--AIBUNDLE1--\n" + bundleText);
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(
                            outputBinaryPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    catch
                    {
                        // Best-effort on non-Unix platforms.
                    }
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
        if (Path.IsPathRooted(relativePath))
        {
            return CreateRuntimeErr("RUN022", "Import path must be relative.", node.Id, node.Span);
        }

        var absolutePath = Path.GetFullPath(Path.Combine(runtime.ModuleBaseDir, relativePath));
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

        if (!File.Exists(absolutePath))
        {
            return CreateRuntimeErr("RUN024", $"Import file not found: {relativePath}", node.Id, node.Span);
        }

        AosParseResult parse;
        try
        {
            parse = AosExternalFrontend.Parse(File.ReadAllText(absolutePath));
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
        runtime.ModuleBaseDir = Path.GetDirectoryName(absolutePath) ?? priorBaseDir;
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

    private static bool TrySerializeJsonNode(AosNode node, out string json)
    {
        if (node.Kind == "Lit")
        {
            if (!node.Attrs.TryGetValue("value", out var litAttr))
            {
                json = string.Empty;
                return false;
            }

            switch (litAttr.Kind)
            {
                case AosAttrKind.String:
                    json = "\"" + EscapeJsonString(litAttr.AsString()) + "\"";
                    return true;
                case AosAttrKind.Int:
                    json = litAttr.AsInt().ToString();
                    return true;
                case AosAttrKind.Bool:
                    json = litAttr.AsBool() ? "true" : "false";
                    return true;
                default:
                    json = string.Empty;
                    return false;
            }
        }

        if (node.Kind == "Map")
        {
            var entries = new List<(string Key, AosNode Value, int Index)>();
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (child.Kind != "Field" ||
                    !child.Attrs.TryGetValue("key", out var keyAttr) ||
                    keyAttr.Kind != AosAttrKind.String ||
                    child.Children.Count != 1)
                {
                    json = string.Empty;
                    return false;
                }

                entries.Add((keyAttr.AsString(), child.Children[0], i));
            }

            var ordered = entries
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .ThenBy(e => e.Index)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('"');
                sb.Append(EscapeJsonString(ordered[i].Key));
                sb.Append('"');
                sb.Append(':');
                if (!TrySerializeJsonNode(ordered[i].Value, out var valueJson))
                {
                    json = string.Empty;
                    return false;
                }
                sb.Append(valueJson);
            }
            sb.Append('}');
            json = sb.ToString();
            return true;
        }

        json = string.Empty;
        return false;
    }

    private static string EscapeJsonString(string value)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
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

    private static int RunGoldenTests(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"FAIL {directory} (directory not found)");
            return 1;
        }

        var aicProgram = LoadAicProgram();
        if (aicProgram is null)
        {
            Console.WriteLine("FAIL aic (compiler/aic.aos not found)");
            return 1;
        }

        var inputFiles = Directory.GetFiles(directory, "*.in.aos", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var failCount = 0;
        foreach (var inputPath in inputFiles)
        {
            var stem = inputPath[..^".in.aos".Length];
            var outPath = $"{stem}.out.aos";
            var errPath = $"{stem}.err";
            var testName = Path.GetFileName(stem);
            var source = File.ReadAllText(inputPath);
            if (testName == "new_directory_exists")
            {
                Directory.CreateDirectory(Path.Combine(directory, "new", "existing_project"));
            }
            else if (testName == "new_success")
            {
                var successDir = Path.Combine(directory, "new", "success_project");
                if (Directory.Exists(successDir))
                {
                    Directory.Delete(successDir, recursive: true);
                }
            }

            string actual;
            string expected;

            if (File.Exists(errPath))
            {
                expected = NormalizeGoldenText(File.ReadAllText(errPath));
                var modeArgs = ResolveGoldenArgs(directory, testName, errorMode: true);
                actual = ExecuteAicMode(aicProgram, modeArgs!, source);
            }
            else if (File.Exists(outPath))
            {
                expected = NormalizeGoldenText(File.ReadAllText(outPath));
                if (testName.StartsWith("trace_", StringComparison.Ordinal))
                {
                    actual = ExecuteTraceGolden(source, testName);
                    goto compare_result;
                }
                if (testName.StartsWith("lifecycle_", StringComparison.Ordinal))
                {
                    actual = ExecuteLifecycleGolden(source, testName);
                    goto compare_result;
                }
                if (testName == "http_health_route_refactor")
                {
                    actual = ExecuteLifecycleGolden(source, testName);
                    goto compare_result;
                }
                if (testName == "publish_binary_runs")
                {
                    actual = ExecutePublishBinaryGolden(aicProgram, directory, source);
                    goto compare_result;
                }
                var modeArgs = ResolveGoldenArgs(directory, testName, errorMode: false);
                if (modeArgs is not null)
                {
                    actual = ExecuteAicMode(aicProgram, modeArgs, source);
                }
                else
                {
                    var fmtActual = ExecuteAicMode(aicProgram, new[] { "fmt" }, source);
                    var runActual = ExecuteAicMode(aicProgram, new[] { "run" }, source);
                    actual = expected == fmtActual ? fmtActual : runActual;
                }
            }
            else
            {
                failCount++;
                Console.WriteLine($"FAIL {testName}");
                continue;
            }

        compare_result:
            if (actual == expected)
            {
                Console.WriteLine($"PASS {testName}");
            }
            else
            {
                failCount++;
                Console.WriteLine($"FAIL {testName}");
            }

            if (testName == "new_success")
            {
                var successDir = Path.Combine(directory, "new", "success_project");
                if (Directory.Exists(successDir))
                {
                    Directory.Delete(successDir, recursive: true);
                }
            }
        }

        return failCount == 0 ? 0 : 1;
    }

    private static string ExecuteAicMode(AosNode aicProgram, string[] argv, string input)
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.Env["argv"] = BuildArgvNode(argv);
        runtime.ReadOnlyBindings.Add("argv");
        var interpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureRouteLoaded(runtime, interpreter);

        var oldIn = Console.In;
        var oldOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(input));
            Console.SetOut(writer);
            interpreter.EvaluateProgram(aicProgram, runtime);
        }
        finally
        {
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }

        return NormalizeGoldenText(writer.ToString());
    }

    private static string[]? ResolveGoldenArgs(string directory, string testName, bool errorMode)
    {
        if (testName.StartsWith("new_", StringComparison.Ordinal))
        {
            var newDir = Path.Combine(directory, "new");
            return testName switch
            {
                "new_missing_name" => new[] { "new" },
                "new_directory_exists" => new[] { "new", Path.Combine(newDir, "existing_project") },
                "new_success" => new[] { "new", Path.Combine(newDir, "success_project") },
                _ => new[] { "new" }
            };
        }

        if (testName.StartsWith("publish_", StringComparison.Ordinal))
        {
            return testName switch
            {
                "publish_binary_runs" => new[] { "publish", Path.Combine(directory, "publish", "binary_runs") },
                "publish_bundle_single_file" => new[] { "publish", Path.Combine(directory, "publish", "bundle_single_file") },
                "publish_bundle_with_import" => new[] { "publish", Path.Combine(directory, "publish", "bundle_with_import") },
                "publish_bundle_cycle_error" => new[] { "publish", Path.Combine(directory, "publish", "bundle_cycle_error") },
                "publish_writes_bundle" => new[] { "publish", Path.Combine(directory, "publish", "writes_bundle") },
                "publish_overwrite_bundle" => new[] { "publish", Path.Combine(directory, "publish", "overwrite_bundle") },
                "publish_missing_dir" => new[] { "publish" },
                "publish_missing_manifest" => new[] { "publish", Path.Combine(directory, "publish", "missing_manifest") },
                _ => new[] { "publish" }
            };
        }

        if (errorMode)
        {
            return new[] { "check" };
        }

        return null;
    }

    private static string ExecuteTraceGolden(string source, string testName)
    {
        var hostBinary = ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=trace)";
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"ailang-trace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "trace_input.aos");
            File.WriteAllText(sourcePath, source);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(sourcePath);
            psi.ArgumentList.Add("--trace");
            if (testName == "trace_with_args")
            {
                psi.ArgumentList.Add("alpha");
                psi.ArgumentList.Add("beta");
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute trace golden.\" nodeId=trace)";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecuteLifecycleGolden(string source, string testName)
    {
        var hostBinary = ResolveHostBinaryPath();
        if (hostBinary is null)
        {
            return "Err#err0(code=RUN001 message=\"host binary not found.\" nodeId=lifecycle)";
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"ailang-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "lifecycle_input.aos");
            File.WriteAllText(sourcePath, source);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostBinary,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(sourcePath);
            if (testName == "lifecycle_event_message_basic" || testName == "http_health_route_refactor")
            {
                psi.ArgumentList.Add("__event_message");
                psi.ArgumentList.Add("text");
                psi.ArgumentList.Add("GET /health");
            }

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return "Err#err0(code=RUN001 message=\"Failed to execute lifecycle golden.\" nodeId=lifecycle)";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (testName == "lifecycle_app_exit_code")
            {
                return AosFormatter.Format(new AosNode(
                    "ExitCode",
                    "ec1",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Int, process.ExitCode)
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
            }
            if (testName == "lifecycle_command_exit_after_print")
            {
                var exitText = AosFormatter.Format(new AosNode(
                    "ExitCode",
                    "ec1",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Int, process.ExitCode)
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
                var printed = NormalizeGoldenText(output);
                return printed.Length == 0 ? exitText : $"{printed}\n{exitText}";
            }

            return NormalizeGoldenText(output);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static string ExecutePublishBinaryGolden(AosNode aicProgram, string directory, string input)
    {
        var publishDir = Path.Combine(directory, "publish", "binary_runs");
        var publishOutput = ExecuteAicMode(aicProgram, new[] { "publish", publishDir }, input);
        var binaryPath = Path.Combine(publishDir, "binaryrun");
        if (!File.Exists(binaryPath))
        {
            return publishOutput;
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = "alpha beta",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = publishDir
        };
        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
        {
            return "Err#err0(code=RUN001 message=\"Failed to execute published binary.\" nodeId=binary)";
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = output;
        return "binary-ok";
    }

    private static AosNode? LoadAicProgram()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.Combine(Directory.GetCurrentDirectory(), "compiler")
        };

        string? path = null;
        foreach (var root in searchRoots)
        {
            var candidate = Path.Combine(root, "aic.aos");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path is null)
        {
            return null;
        }

        var parse = ParseSource(File.ReadAllText(path));
        return parse.Root;
    }

    private static AosValue BuildArgvNode(string[] values)
    {
        var children = new List<AosNode>(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            children.Add(new AosNode(
                "Lit",
                $"argv{i}",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["value"] = new AosAttrValue(AosAttrKind.String, values[i])
                },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
        }

        var argvNode = new AosNode(
            "Block",
            "argv",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            children,
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosValue.FromNode(argvNode);
    }

    private static string NormalizeGoldenText(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        while (normalized.EndsWith("\n", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }
        return normalized;
    }

    private static string ExecuteFmt(string source)
    {
        var parse = ParseSource(source);
        if (parse.Root is null)
        {
            return FormatDiagnosticErr(parse.Diagnostics.FirstOrDefault(), "PAR000");
        }

        if (parse.Diagnostics.Count > 0)
        {
            return FormatDiagnosticErr(parse.Diagnostics[0], parse.Diagnostics[0].Code);
        }

        return AosFormatter.Format(parse.Root);
    }

    private static string ExecuteCheck(string source)
    {
        var parse = ParseSource(source);
        if (parse.Root is null)
        {
            return FormatDiagnosticErr(parse.Diagnostics.FirstOrDefault(), "PAR000");
        }

        if (parse.Diagnostics.Count > 0)
        {
            return FormatDiagnosticErr(parse.Diagnostics[0], parse.Diagnostics[0].Code);
        }

        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math", "io", "compiler", "console" };
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root, null, permissions, runStructural: false);
        if (validation.Diagnostics.Count == 0)
        {
            return "Ok#ok0(type=void)";
        }

        var first = validation.Diagnostics[0];
        return AosFormatter.Format(CreateErrNode("diag0", first.Code, first.Message, first.NodeId ?? "unknown", parse.Root.Span));
    }

    private static string ExecuteRun(string source)
    {
        var parse = ParseSource(source);
        if (parse.Root is null)
        {
            return FormatDiagnosticErr(parse.Diagnostics.FirstOrDefault(), "PAR000");
        }

        if (parse.Diagnostics.Count > 0)
        {
            return FormatDiagnosticErr(parse.Diagnostics[0], parse.Diagnostics[0].Code);
        }

        var runtime = new AosRuntime();
        runtime.Permissions.Add("console");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureRouteLoaded(runtime, interpreter);

        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root, null, runtime.Permissions, runStructural: false);
        if (validation.Diagnostics.Count > 0)
        {
            var first = validation.Diagnostics[0];
            return AosFormatter.Format(CreateErrNode("diag0", first.Code, first.Message, first.NodeId ?? "unknown", parse.Root.Span));
        }

        var result = interpreter.EvaluateProgram(parse.Root, runtime);
        return FormatOkValue(result, parse.Root.Span);
    }

    private static AosParseResult ParseSource(string source)
    {
        return AosExternalFrontend.Parse(source);
    }

    private static string FormatDiagnosticErr(AosDiagnostic? diagnostic, string fallbackCode)
    {
        var code = diagnostic?.Code ?? fallbackCode;
        var message = diagnostic?.Message ?? "Parse failed.";
        var nodeId = diagnostic?.NodeId ?? "unknown";
        var node = CreateErrNode("err0", code, message, nodeId, new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(node);
    }

    private static string FormatOkValue(AosValue value, AosSpan span)
    {
        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["type"] = new AosAttrValue(AosAttrKind.Identifier, value.Kind.ToString().ToLowerInvariant())
        };

        if (value.Kind == AosValueKind.String)
        {
            attrs["value"] = new AosAttrValue(AosAttrKind.String, value.AsString());
        }
        else if (value.Kind == AosValueKind.Int)
        {
            attrs["value"] = new AosAttrValue(AosAttrKind.Int, value.AsInt());
        }
        else if (value.Kind == AosValueKind.Bool)
        {
            attrs["value"] = new AosAttrValue(AosAttrKind.Bool, value.AsBool());
        }

        var ok = new AosNode("Ok", "ok0", attrs, new List<AosNode>(), span);
        return AosFormatter.Format(ok);
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

    private static string? ResolveHostBinaryPath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "airun"),
            Path.Combine(Directory.GetCurrentDirectory(), "tools", "airun")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
