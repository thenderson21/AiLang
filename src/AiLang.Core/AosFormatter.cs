namespace AiLang.Core;

public static class AosFormatter
{
    private static readonly Lazy<AosNode> FormatterProgram = new(LoadFormatterProgram);

    public static string Format(AosNode node)
    {
        var program = FormatterProgram.Value;
        var runtime = new AosRuntime();
        runtime.Permissions.Clear();

        var validator = new AosValidator();
        var validation = validator.Validate(program, null, runtime.Permissions, runStructural: false);
        if (validation.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"Formatter validation failed: {validation.Diagnostics[0].Message}");
        }

        var interpreter = new AosInterpreter();
        interpreter.EvaluateProgram(program, runtime);

        runtime.Env["__input"] = AosValue.FromNode(node);
        var callNode = new AosNode(
            "Call",
            "format_call",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["target"] = new AosAttrValue(AosAttrKind.Identifier, "format")
            },
            new List<AosNode>
            {
                new(
                    "Var",
                    "format_input",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__input")
                    },
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))
            },
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));

        var result = interpreter.EvaluateExpression(callNode, runtime);
        if (result.Kind != AosValueKind.String)
        {
            throw new InvalidOperationException("Formatter did not return a string.");
        }
        return result.AsString();
    }

    private static AosNode LoadFormatterProgram()
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
            var candidate = Path.Combine(root, "format.aos");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path is null)
        {
            throw new FileNotFoundException("format.aos not found.");
        }

        var source = File.ReadAllText(path);
        var parse = AosParsing.Parse(source);

        if (parse.Root is null)
        {
            throw new InvalidOperationException("Failed to parse format.aos.");
        }

        if (parse.Root.Kind != "Program")
        {
            throw new InvalidOperationException("format.aos must contain a Program node.");
        }

        if (parse.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException($"format.aos parse error: {parse.Diagnostics[0].Message}");
        }

        return parse.Root;
    }
}
