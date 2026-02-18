using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private bool TryEvaluateCompilerCall(
        string target,
        AosNode node,
        AosRuntime runtime,
        Dictionary<string, AosValue> env,
        out AosValue result)
    {
        result = AosValue.Unknown;

        if (target == "std.json.parse")
        {
            if (!runtime.Permissions.Contains("compiler"))
            {
                return true;
            }

            if (node.Children.Count != 1)
            {
                return true;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            if (text.Kind != AosValueKind.String)
            {
                return true;
            }

            if (!TryParseJsonBodyNode(text.AsString(), node.Span, out var parsed, out var error))
            {
                result = AosValue.FromNode(CreateErrNode("parse_json_err", "PARJSON001", error, node.Id, node.Span));
                return true;
            }

            result = AosValue.FromNode(parsed);
            return true;
        }

        if (!target.StartsWith("compiler.", StringComparison.Ordinal))
        {
            return false;
        }

        if (target == "compiler.publish")
        {
            result = EvalCompilerPublish(node, runtime, env);
            return true;
        }

        if (!runtime.Permissions.Contains("compiler"))
        {
            return true;
        }

        if (target == "compiler.parse")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            if (text.Kind != AosValueKind.String)
            {
                return true;
            }

            var parse = AosParsing.Parse(text.AsString());

            if (parse.Root is not null && parse.Diagnostics.Count == 0 && parse.Root.Kind == "Program")
            {
                result = AosValue.FromNode(parse.Root);
                return true;
            }

            var diagnostic = parse.Diagnostics.FirstOrDefault();
            if (diagnostic is null && parse.Root is not null && parse.Root.Kind != "Program")
            {
                diagnostic = new AosDiagnostic("PAR001", "Expected Program root.", parse.Root.Id, parse.Root.Span);
            }
            diagnostic ??= new AosDiagnostic("PAR000", "Parse failed.", "unknown", null);
            result = AosValue.FromNode(CreateErrNode("parse_err", diagnostic.Code, diagnostic.Message, diagnostic.NodeId ?? "unknown", node.Span));
            return true;
        }

        if (target == "compiler.emitBytecode")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return true;
            }

            try
            {
                var resolved = ResolveImportsForBytecode(
                    input.AsNode(),
                    runtime.ModuleBaseDir,
                    new HashSet<string>(StringComparer.Ordinal));
                result = AosValue.FromNode(BytecodeCompiler.Compile(resolved, allowImportNodes: true));
                return true;
            }
            catch (VmRuntimeException ex)
            {
                result = AosValue.FromNode(CreateErrNode("vm_emit_err", ex.Code, ex.Message, ex.NodeId, node.Span));
                return true;
            }
        }

        if (target == "compiler.strCompare")
        {
            if (node.Children.Count != 2)
            {
                return true;
            }

            var left = EvalNode(node.Children[0], runtime, env);
            var right = EvalNode(node.Children[1], runtime, env);
            if (left.Kind != AosValueKind.String || right.Kind != AosValueKind.String)
            {
                return true;
            }

            var cmp = string.CompareOrdinal(left.AsString(), right.AsString());
            result = cmp < 0 ? AosValue.FromInt(-1) : cmp > 0 ? AosValue.FromInt(1) : AosValue.FromInt(0);
            return true;
        }

        if (target == "compiler.strToken")
        {
            if (node.Children.Count != 2)
            {
                return true;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            var index = EvalNode(node.Children[1], runtime, env);
            if (text.Kind != AosValueKind.String || index.Kind != AosValueKind.Int)
            {
                return true;
            }

            var tokens = text.AsString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var i = index.AsInt();
            result = i < 0 || i >= tokens.Length ? AosValue.FromString(string.Empty) : AosValue.FromString(tokens[i]);
            return true;
        }

        if (target == "compiler.parseHttpRequest")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            if (text.Kind != AosValueKind.String)
            {
                return true;
            }

            result = AosValue.FromNode(ParseHttpRequestNode(text.AsString(), node.Span));
            return true;
        }

        if (target == "compiler.parseHttpBodyForm")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var text = EvalNode(node.Children[0], runtime, env);
            if (text.Kind != AosValueKind.String)
            {
                return true;
            }

            result = AosValue.FromNode(ParseFormBodyNode(text.AsString(), node.Span));
            return true;
        }

        if (target == "compiler.format")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return true;
            }

            result = AosValue.FromString(AosFormatter.Format(input.AsNode()));
            return true;
        }

        if (target == "compiler.formatIds")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return true;
            }

            var normalized = AosNodeIdCanonicalizer.NormalizeIds(input.AsNode());
            result = AosValue.FromString(AosFormatter.Format(normalized));
            return true;
        }

        if (target == "compiler.validate")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return true;
            }

            var structural = new AosStructuralValidator();
            var diagnostics = structural.Validate(input.AsNode());
            result = AosValue.FromNode(CreateDiagnosticsNode(diagnostics, node.Span));
            return true;
        }

        if (target == "compiler.validateHost")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return true;
            }

            var validator = new AosValidator();
            var diagnostics = validator.Validate(input.AsNode(), null, runtime.Permissions, runStructural: false).Diagnostics;
            result = AosValue.FromNode(CreateDiagnosticsNode(diagnostics, node.Span));
            return true;
        }

        if (target == "compiler.test")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var dirValue = EvalNode(node.Children[0], runtime, env);
            if (dirValue.Kind != AosValueKind.String)
            {
                return true;
            }

            result = AosValue.FromInt(RunGoldenTests(dirValue.AsString()));
            return true;
        }

        if (target == "compiler.run")
        {
            if (node.Children.Count != 1)
            {
                return true;
            }

            var input = EvalNode(node.Children[0], runtime, env);
            if (input.Kind != AosValueKind.Node)
            {
                return true;
            }

            AosStandardLibraryLoader.EnsureLoaded(runtime, this);
            var runEnv = new Dictionary<string, AosValue>(StringComparer.Ordinal);
            var value = Evaluate(input.AsNode(), runtime, runEnv);
            if (IsErrValue(value))
            {
                result = value;
                return true;
            }

            result = AosValue.FromNode(ToRuntimeNode(value));
            return true;
        }

        return false;
    }
}
