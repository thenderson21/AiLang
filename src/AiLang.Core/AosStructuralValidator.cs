namespace AiLang.Core;

public sealed class AosStructuralValidator
{
    private static readonly Lazy<AosNode> ValidatorProgram = new(LoadValidatorProgram);

    public List<AosDiagnostic> Validate(AosNode root)
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Clear();

        var interpreter = new AosInterpreter();
        interpreter.EvaluateProgram(ValidatorProgram.Value, runtime);

        runtime.Env["__input"] = AosValue.FromNode(root);
        var callNode = new AosNode(
            "Call",
            "validate_call",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["target"] = new AosAttrValue(AosAttrKind.Identifier, "validate")
            },
            new List<AosNode>
            {
                new(
                    "Var",
                    "validate_input",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__input")
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
            },
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

        var result = interpreter.EvaluateExpression(callNode, runtime);
        if (result.Kind != AosValueKind.Node)
        {
            try
            {
                var strict = new AosInterpreter();
                strict.EvaluateExpressionStrict(callNode, runtime);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"validate.aos did not return a node list (got {result.Kind}). {ex.Message}", ex);
            }
            throw new InvalidOperationException($"validate.aos did not return a node list (got {result.Kind}).");
        }

        var errorsNode = result.AsNode();
        var diagnostics = new List<AosDiagnostic>();
        foreach (var child in errorsNode.Children)
        {
            if (child.Kind != "Err")
            {
                continue;
            }
            var code = child.Attrs.TryGetValue("code", out var codeAttr) && codeAttr.Kind == AosAttrKind.Identifier ? codeAttr.AsString() : "VAL";
            var message = child.Attrs.TryGetValue("message", out var msgAttr) && msgAttr.Kind == AosAttrKind.String ? msgAttr.AsString() : "Validation error";
            var nodeId = child.Attrs.TryGetValue("nodeId", out var idAttr) && idAttr.Kind == AosAttrKind.Identifier ? idAttr.AsString() : null;
            diagnostics.Add(new AosDiagnostic(code, message, nodeId, null));
        }
        return diagnostics;
    }

    private static AosNode LoadValidatorProgram()
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "compiler"),
            Path.Combine(Directory.GetCurrentDirectory(), "compiler")
        };

        string? path = null;
        foreach (var root in searchRoots)
        {
            var candidate = Path.Combine(root, "validate.aos");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path is null)
        {
            throw new FileNotFoundException("validate.aos not found.");
        }

        var source = File.ReadAllText(path);
        var parse = AosParsing.Parse(source);

        if (parse.Root is null)
        {
            throw new InvalidOperationException("Failed to parse validate.aos.");
        }

        if (parse.Root.Kind != "Program")
        {
            throw new InvalidOperationException("validate.aos must contain a Program node.");
        }

        if (parse.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"validate.aos parse error: {parse.Diagnostics[0].Message}");
        }

        return parse.Root;
    }
}
