namespace AiLang.Core;

public sealed class AosReplSession
{
    private readonly AosRuntime _runtime = new();
    private readonly AosValidator _validator = new();
    private readonly AosInterpreter _interpreter = new();
    private readonly AosPatchApplier _patchApplier = new();
    private int _counter;

    public string ExecuteLine(string line)
    {
        var parse = Parse(line);
        if (parse.Root is null)
        {
            return FormatErr(parse.Diagnostics.FirstOrDefault(), "PARSE");
        }

        var cmd = parse.Root;
        if (cmd.Kind != "Cmd")
        {
            return FormatErr(new AosDiagnostic("REPL001", "Expected Cmd node.", cmd.Id, cmd.Span), "REPL");
        }

        if (!cmd.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
        {
            return FormatErr(new AosDiagnostic("REPL002", "Cmd requires name.", cmd.Id, cmd.Span), "REPL");
        }

        var name = nameAttr.AsString();
        return name switch
        {
            "help" => HandleHelp(),
            "setPerms" => HandleSetPerms(cmd),
            "load" => HandleLoad(cmd),
            "eval" => HandleEval(cmd),
            "applyPatch" => HandlePatch(cmd),
            _ => FormatErr(new AosDiagnostic("REPL003", $"Unknown command '{name}'.", cmd.Id, cmd.Span), "REPL")
        };
    }

    private string HandleHelp()
    {
        var commands = new[] { "help", "setPerms", "load", "eval", "applyPatch" };
        var children = new List<AosNode>();
        foreach (var command in commands)
        {
            children.Add(new AosNode(
                "Cmd",
                NextId("cmd"),
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["name"] = new AosAttrValue(AosAttrKind.Identifier, command)
                },
                new List<AosNode>(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
        }

        return FormatOk(AosValue.Void, children);
    }

    private string HandleSetPerms(AosNode cmd)
    {
        if (!cmd.Attrs.TryGetValue("allow", out var allowAttr) || allowAttr.Kind != AosAttrKind.Identifier)
        {
            return FormatErr(new AosDiagnostic("REPL010", "setPerms requires allow attribute.", cmd.Id, cmd.Span), "REPL");
        }

        var perms = allowAttr.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _runtime.Permissions.Clear();
        foreach (var perm in perms)
        {
            _runtime.Permissions.Add(perm);
        }
        return FormatOk(AosValue.Void);
    }

    private string HandleLoad(AosNode cmd)
    {
        if (cmd.Children.Count != 1)
        {
            return FormatErr(new AosDiagnostic("REPL020", "load requires one Program child.", cmd.Id, cmd.Span), "REPL");
        }

        var program = cmd.Children[0];
        if (program.Kind != "Program")
        {
            return FormatErr(new AosDiagnostic("REPL021", "load child must be Program.", cmd.Id, cmd.Span), "REPL");
        }

        var envTypes = RuntimeTypes();
        var validation = _validator.Validate(program, envTypes, _runtime.Permissions);
        if (validation.Diagnostics.Count > 0)
        {
            return FormatErr(validation.Diagnostics[0], "VAL");
        }

        _runtime.Program = program;
        _interpreter.EvaluateProgram(program, _runtime);
        return FormatOk(AosValue.Void);
    }

    private string HandleEval(AosNode cmd)
    {
        if (cmd.Children.Count != 1)
        {
            return FormatErr(new AosDiagnostic("REPL030", "eval requires one expression child.", cmd.Id, cmd.Span), "REPL");
        }

        var expr = cmd.Children[0];
        var envTypes = RuntimeTypes();
        var validation = _validator.Validate(expr, envTypes, _runtime.Permissions);
        if (validation.Diagnostics.Count > 0)
        {
            return FormatErr(validation.Diagnostics[0], "VAL");
        }

        var value = _interpreter.EvaluateExpression(expr, _runtime);
        return FormatOk(value);
    }

    private string HandlePatch(AosNode cmd)
    {
        if (_runtime.Program is null)
        {
            return FormatErr(new AosDiagnostic("REPL040", "No program loaded.", cmd.Id, cmd.Span), "REPL");
        }

        if (cmd.Children.Count == 0)
        {
            return FormatErr(new AosDiagnostic("REPL041", "applyPatch requires Op children.", cmd.Id, cmd.Span), "REPL");
        }

        foreach (var child in cmd.Children)
        {
            if (child.Kind != "Op")
            {
                return FormatErr(new AosDiagnostic("REPL042", "applyPatch children must be Op.", child.Id, child.Span), "REPL");
            }
        }

        var result = _patchApplier.Apply(_runtime.Program, cmd.Children);
        if (result.Diagnostics.Count > 0)
        {
            return FormatErr(result.Diagnostics[0], "PAT");
        }

        _runtime.Program = result.Root;
        return FormatOk(AosValue.Void);
    }

    private Dictionary<string, AosValueKind> RuntimeTypes()
    {
        var types = new Dictionary<string, AosValueKind>(StringComparer.Ordinal);
        foreach (var entry in _runtime.Env)
        {
            types[entry.Key] = entry.Value.Kind;
        }
        return types;
    }

    private AosParseResult Parse(string line)
    {
        return AosParsing.Parse(line);
    }

    private string FormatOk(AosValue value)
    {
        return FormatOk(value, new List<AosNode>());
    }

    private string FormatOk(AosValue value, List<AosNode> children)
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

        var node = new AosNode("Ok", NextId("ok"), attrs, children, new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(node);
    }

    private string FormatErr(AosDiagnostic? diagnostic, string fallbackCode)
    {
        var code = diagnostic?.Code ?? fallbackCode;
        var message = diagnostic?.Message ?? "Unknown error";
        var nodeId = diagnostic?.NodeId ?? "unknown";

        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
        {
            ["code"] = new AosAttrValue(AosAttrKind.Identifier, code),
            ["message"] = new AosAttrValue(AosAttrKind.String, message),
            ["nodeId"] = new AosAttrValue(AosAttrKind.Identifier, nodeId)
        };

        var node = new AosNode("Err", NextId("err"), attrs, new List<AosNode>(), new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        return AosFormatter.Format(node);
    }

    private string NextId(string prefix)
    {
        _counter++;
        return $"{prefix}{_counter}";
    }
}
