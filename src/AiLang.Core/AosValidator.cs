namespace AiLang.Core;

public sealed class AosValidationResult
{
    public AosValidationResult(List<AosDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public List<AosDiagnostic> Diagnostics { get; }
}

public sealed class AosValidator
{
    private readonly List<AosDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly AosStructuralValidator _structuralValidator = new();

    public AosValidationResult Validate(AosNode root, Dictionary<string, AosValueKind>? envTypes, HashSet<string> permissions)
        => Validate(root, envTypes, permissions, runStructural: true);

    public AosValidationResult Validate(AosNode root, Dictionary<string, AosValueKind>? envTypes, HashSet<string> permissions, bool runStructural)
    {
        _diagnostics.Clear();
        _ids.Clear();

        if (runStructural)
        {
            var structural = _structuralValidator.Validate(root);
            if (structural.Count > 0)
            {
                _diagnostics.AddRange(structural);
                return new AosValidationResult(_diagnostics.ToList());
            }
        }

        var env = envTypes is null
            ? new Dictionary<string, AosValueKind>(StringComparer.Ordinal)
            : new Dictionary<string, AosValueKind>(envTypes, StringComparer.Ordinal);
        PredeclareFunctions(root, env);
        ValidateNode(root, env, permissions);
        return new AosValidationResult(_diagnostics.ToList());
    }

    private AosValueKind ValidateNode(AosNode node, Dictionary<string, AosValueKind> env, HashSet<string> permissions)
    {
        if (!_ids.Add(node.Id))
        {
            _diagnostics.Add(new AosDiagnostic("VAL001", $"Duplicate node id '{node.Id}'.", node.Id, node.Span));
        }

        switch (node.Kind)
        {
            case "Program":
                foreach (var child in node.Children)
                {
                    ValidateNode(child, env, permissions);
                }
                return AosValueKind.Void;
            case "Let":
                RequireAttr(node, "name");
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1 && node.Children[0].Kind == "Fn" &&
                    node.Attrs.TryGetValue("name", out var fnNameAttr) && fnNameAttr.Kind == AosAttrKind.Identifier)
                {
                    env[fnNameAttr.AsString()] = AosValueKind.Function;
                }

                var letType = node.Children.Count == 1 ? ValidateNode(node.Children[0], env, permissions) : AosValueKind.Unknown;
                if (node.Attrs.TryGetValue("name", out var nameAttr) && nameAttr.Kind == AosAttrKind.Identifier)
                {
                    env[nameAttr.AsString()] = letType;
                }
                return AosValueKind.Void;
            case "Var":
                RequireAttr(node, "name");
                RequireChildren(node, 0, 0);
                if (node.Attrs.TryGetValue("name", out var varNameAttr) && varNameAttr.Kind == AosAttrKind.Identifier)
                {
                    if (env.TryGetValue(varNameAttr.AsString(), out var varType))
                    {
                        return varType;
                    }
                    _diagnostics.Add(new AosDiagnostic("VAL010", $"Unknown variable '{varNameAttr.AsString()}'.", node.Id, node.Span));
                }
                return AosValueKind.Unknown;
            case "Lit":
                RequireAttr(node, "value");
                RequireChildren(node, 0, 0);
                if (node.Attrs.TryGetValue("value", out var litVal))
                {
                    return litVal.Kind switch
                    {
                        AosAttrKind.String => AosValueKind.String,
                        AosAttrKind.Int => AosValueKind.Int,
                        AosAttrKind.Bool => AosValueKind.Bool,
                        _ => AosValueKind.Unknown
                    };
                }
                return AosValueKind.Unknown;
            case "Call":
                RequireAttr(node, "target");
                var target = node.Attrs.TryGetValue("target", out var targetVal) ? targetVal.AsString() : string.Empty;
                var argTypes = node.Children.Select(child => ValidateNode(child, env, permissions)).ToList();
                return ValidateCall(node, target, argTypes, env, permissions);
            case "Import":
                RequireAttr(node, "path");
                RequireChildren(node, 0, 0);
                if (node.Attrs.TryGetValue("path", out var pathAttr) && pathAttr.Kind != AosAttrKind.String)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL089", "Import path must be string.", node.Id, node.Span));
                }
                return AosValueKind.Void;
            case "Export":
                RequireAttr(node, "name");
                RequireChildren(node, 0, 0);
                if (node.Attrs.TryGetValue("name", out var exportNameAttr) && exportNameAttr.Kind == AosAttrKind.Identifier)
                {
                    if (!env.ContainsKey(exportNameAttr.AsString()))
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL010", $"Unknown variable '{exportNameAttr.AsString()}'.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Void;
            case "Project":
                RequireAttr(node, "name");
                RequireAttr(node, "entryFile");
                RequireAttr(node, "entryExport");
                RequireChildren(node, 0, int.MaxValue);
                if (node.Attrs.TryGetValue("entryFile", out var entryFileAttr))
                {
                    if (entryFileAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL092", "Project entryFile must be string.", node.Id, node.Span));
                    }
                    else
                    {
                        var entryFile = entryFileAttr.AsString();
                        if (Path.IsPathRooted(entryFile))
                        {
                            _diagnostics.Add(new AosDiagnostic("VAL093", "Project entryFile must be relative path.", node.Id, node.Span));
                        }
                    }
                }
                if (node.Attrs.TryGetValue("entryExport", out var entryExportAttr))
                {
                    if (entryExportAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL094", "Project entryExport must be string.", node.Id, node.Span));
                    }
                    else if (string.IsNullOrEmpty(entryExportAttr.AsString()))
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL095", "Project entryExport must be non-empty.", node.Id, node.Span));
                    }
                }
                foreach (var child in node.Children)
                {
                    if (child.Kind != "Include")
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL150", "Project children must be Include nodes.", child.Id, child.Span));
                        continue;
                    }
                    ValidateNode(child, env, permissions);
                }
                return AosValueKind.Void;
            case "Include":
                RequireAttr(node, "name");
                RequireAttr(node, "path");
                RequireAttr(node, "version");
                RequireChildren(node, 0, 0);
                if (node.Attrs.TryGetValue("name", out var includeNameAttr))
                {
                    if (includeNameAttr.Kind != AosAttrKind.String || string.IsNullOrEmpty(includeNameAttr.AsString()))
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL151", "Include name must be non-empty string.", node.Id, node.Span));
                    }
                }
                if (node.Attrs.TryGetValue("version", out var versionAttr))
                {
                    if (versionAttr.Kind != AosAttrKind.String || string.IsNullOrEmpty(versionAttr.AsString()))
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL152", "Include version must be non-empty string.", node.Id, node.Span));
                    }
                }
                if (node.Attrs.TryGetValue("path", out var includePathAttr))
                {
                    if (includePathAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL153", "Include path must be string.", node.Id, node.Span));
                    }
                    else if (Path.IsPathRooted(includePathAttr.AsString()))
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL154", "Include path must be relative path.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Void;
            case "Fn":
                RequireAttr(node, "params");
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1 && node.Children[0].Kind != "Block")
                {
                    _diagnostics.Add(new AosDiagnostic("VAL050", "Fn body must be Block.", node.Id, node.Span));
                }
                if (node.Children.Count == 1)
                {
                    var fnEnv = new Dictionary<string, AosValueKind>(env, StringComparer.Ordinal);
                    if (node.Attrs.TryGetValue("params", out var paramsAttr) && paramsAttr.Kind == AosAttrKind.Identifier)
                    {
                        var names = paramsAttr.AsString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var name in names)
                        {
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                fnEnv[name] = AosValueKind.Unknown;
                            }
                        }
                    }
                    ValidateNode(node.Children[0], fnEnv, permissions);
                }
                return AosValueKind.Function;
            case "Eq":
                RequireChildren(node, 2, 2);
                if (node.Children.Count == 2)
                {
                    var leftType = ValidateNode(node.Children[0], env, permissions);
                    var rightType = ValidateNode(node.Children[1], env, permissions);
                    if (leftType != rightType && leftType != AosValueKind.Unknown && rightType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL051", "Eq operands must have same type.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Bool;
            case "Add":
                RequireChildren(node, 2, 2);
                if (node.Children.Count == 2)
                {
                    var leftType = ValidateNode(node.Children[0], env, permissions);
                    var rightType = ValidateNode(node.Children[1], env, permissions);
                    if (leftType != AosValueKind.Int && leftType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL052", "Add left operand must be int.", node.Id, node.Span));
                    }
                    if (rightType != AosValueKind.Int && rightType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL053", "Add right operand must be int.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Int;
            case "ToString":
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1)
                {
                    var valueType = ValidateNode(node.Children[0], env, permissions);
                    if (valueType != AosValueKind.Int && valueType != AosValueKind.Bool && valueType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL054", "ToString expects int or bool.", node.Id, node.Span));
                    }
                }
                return AosValueKind.String;
            case "StrConcat":
                RequireChildren(node, 2, 2);
                if (node.Children.Count == 2)
                {
                    var leftType = ValidateNode(node.Children[0], env, permissions);
                    var rightType = ValidateNode(node.Children[1], env, permissions);
                    if (leftType != AosValueKind.String && leftType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL055", "StrConcat left operand must be string.", node.Id, node.Span));
                    }
                    if (rightType != AosValueKind.String && rightType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL056", "StrConcat right operand must be string.", node.Id, node.Span));
                    }
                }
                return AosValueKind.String;
            case "StrEscape":
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1)
                {
                    var valueType = ValidateNode(node.Children[0], env, permissions);
                    if (valueType != AosValueKind.String && valueType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL057", "StrEscape expects string.", node.Id, node.Span));
                    }
                }
                return AosValueKind.String;
            case "MakeBlock":
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1)
                {
                    var idType = ValidateNode(node.Children[0], env, permissions);
                    if (idType != AosValueKind.String && idType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL058", "MakeBlock expects string id.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Node;
            case "AppendChild":
                RequireChildren(node, 2, 2);
                if (node.Children.Count == 2)
                {
                    var parentType = ValidateNode(node.Children[0], env, permissions);
                    var childType = ValidateNode(node.Children[1], env, permissions);
                    if (parentType != AosValueKind.Node && parentType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL059", "AppendChild expects node parent.", node.Id, node.Span));
                    }
                    if (childType != AosValueKind.Node && childType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL060", "AppendChild expects node child.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Node;
            case "MakeErr":
                RequireChildren(node, 4, 4);
                ValidateChildrenAsStrings(node, env, permissions, "VAL061");
                return AosValueKind.Node;
            case "MakeLitString":
                RequireChildren(node, 2, 2);
                ValidateChildrenAsStrings(node, env, permissions, "VAL062");
                return AosValueKind.Node;
            case "Event":
                RequireChildren(node, 0, 0);
                if (node.Id == "Message")
                {
                    RequireAttr(node, "type");
                    RequireAttr(node, "payload");
                    if (node.Attrs.TryGetValue("type", out var typeAttr) && typeAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL100", "Event#Message type must be string.", node.Id, node.Span));
                    }
                    if (node.Attrs.TryGetValue("payload", out var payloadAttr) && payloadAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL101", "Event#Message payload must be string.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Node;
            case "HttpRequest":
                RequireChildren(node, 0, 0);
                RequireAttr(node, "method");
                RequireAttr(node, "path");
                if (node.Attrs.TryGetValue("method", out var methodAttr) && methodAttr.Kind != AosAttrKind.String)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL102", "HttpRequest method must be string.", node.Id, node.Span));
                }
                if (node.Attrs.TryGetValue("path", out var httpPathAttr) && httpPathAttr.Kind != AosAttrKind.String)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL103", "HttpRequest path must be string.", node.Id, node.Span));
                }
                return AosValueKind.Node;
            case "Route":
                RequireChildren(node, 0, 0);
                RequireAttr(node, "path");
                RequireAttr(node, "handler");
                if (node.Attrs.TryGetValue("path", out var routePathAttr) && routePathAttr.Kind != AosAttrKind.String)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL106", "Route path must be string.", node.Id, node.Span));
                }
                if (node.Attrs.TryGetValue("handler", out var routeHandlerAttr) && routeHandlerAttr.Kind != AosAttrKind.String)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL107", "Route handler must be string.", node.Id, node.Span));
                }
                return AosValueKind.Node;
            case "Map":
                foreach (var child in node.Children)
                {
                    var childType = ValidateNode(child, env, permissions);
                    if (child.Kind != "Field")
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL112", "Map children must be Field nodes.", child.Id, child.Span));
                    }
                    else if (childType != AosValueKind.Node && childType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL113", "Field must evaluate to node.", child.Id, child.Span));
                    }
                }
                return AosValueKind.Node;
            case "Field":
                RequireChildren(node, 1, 1);
                RequireAttr(node, "key");
                if (node.Attrs.TryGetValue("key", out var fieldKeyAttr) && fieldKeyAttr.Kind != AosAttrKind.String)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL114", "Field key must be string.", node.Id, node.Span));
                }
                if (node.Children.Count == 1)
                {
                    ValidateNode(node.Children[0], env, permissions);
                }
                return AosValueKind.Node;
            case "Match":
                RequireChildren(node, 0, 0);
                RequireAttr(node, "handler");
                if (node.Attrs.TryGetValue("handler", out var matchHandlerAttr))
                {
                    if (matchHandlerAttr.Kind == AosAttrKind.String)
                    {
                        return AosValueKind.Node;
                    }
                    if (matchHandlerAttr.Kind == AosAttrKind.Identifier &&
                        string.Equals(matchHandlerAttr.AsString(), "null", StringComparison.Ordinal))
                    {
                        return AosValueKind.Node;
                    }
                    _diagnostics.Add(new AosDiagnostic("VAL108", "Match handler must be string or null.", node.Id, node.Span));
                }
                return AosValueKind.Node;
            case "Command":
                RequireChildren(node, 0, 0);
                if (node.Id == "Exit")
                {
                    RequireAttr(node, "code");
                    if (node.Attrs.TryGetValue("code", out var codeAttr) && codeAttr.Kind != AosAttrKind.Int)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL096", "Command#Exit code must be int.", node.Id, node.Span));
                    }
                }
                else if (node.Id == "Print")
                {
                    RequireAttr(node, "text");
                    if (node.Attrs.TryGetValue("text", out var textAttr) && textAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL097", "Command#Print text must be string.", node.Id, node.Span));
                    }
                }
                else if (node.Id == "Emit")
                {
                    RequireAttr(node, "type");
                    RequireAttr(node, "payload");
                    if (node.Attrs.TryGetValue("type", out var typeAttr) && typeAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL098", "Command#Emit type must be string.", node.Id, node.Span));
                    }
                    if (node.Attrs.TryGetValue("payload", out var payloadAttr) && payloadAttr.Kind != AosAttrKind.String)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL099", "Command#Emit payload must be string.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Node;
            case "NodeKind":
            case "NodeId":
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1)
                {
                    var nodeType = ValidateNode(node.Children[0], env, permissions);
                    if (nodeType != AosValueKind.Node && nodeType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL060", $"{node.Kind} expects node.", node.Id, node.Span));
                    }
                }
                return AosValueKind.String;
            case "AttrCount":
            case "ChildCount":
                RequireChildren(node, 1, 1);
                if (node.Children.Count == 1)
                {
                    var nodeType = ValidateNode(node.Children[0], env, permissions);
                    if (nodeType != AosValueKind.Node && nodeType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL061", $"{node.Kind} expects node.", node.Id, node.Span));
                    }
                }
                return AosValueKind.Int;
            case "AttrKey":
            case "AttrValueKind":
            case "AttrValueString":
                RequireChildren(node, 2, 2);
                ValidateNodeChildPair(node, env, permissions, "VAL062");
                return AosValueKind.String;
            case "AttrValueInt":
                RequireChildren(node, 2, 2);
                ValidateNodeChildPair(node, env, permissions, "VAL063");
                return AosValueKind.Int;
            case "AttrValueBool":
                RequireChildren(node, 2, 2);
                ValidateNodeChildPair(node, env, permissions, "VAL064");
                return AosValueKind.Bool;
            case "ChildAt":
                RequireChildren(node, 2, 2);
                ValidateNodeChildPair(node, env, permissions, "VAL065");
                return AosValueKind.Node;
            case "If":
                RequireChildren(node, 2, 3);
                if (node.Children.Count >= 1)
                {
                    var condType = ValidateNode(node.Children[0], env, permissions);
                    if (condType != AosValueKind.Bool && condType != AosValueKind.Unknown)
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL020", "If condition must be bool.", node.Id, node.Span));
                    }
                }
                if (node.Children.Count >= 2)
                {
                    if (node.Children[1].Kind != "Block")
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL021", "If then-branch must be Block.", node.Id, node.Span));
                    }
                }
                if (node.Children.Count == 3)
                {
                    if (node.Children[2].Kind != "Block")
                    {
                        _diagnostics.Add(new AosDiagnostic("VAL022", "If else-branch must be Block.", node.Id, node.Span));
                    }
                }

                var thenType = node.Children.Count >= 2 ? ValidateNode(node.Children[1], new Dictionary<string, AosValueKind>(env), permissions) : AosValueKind.Void;
                var elseType = node.Children.Count == 3 ? ValidateNode(node.Children[2], new Dictionary<string, AosValueKind>(env), permissions) : AosValueKind.Void;

                if (node.Children.Count == 2)
                {
                    return AosValueKind.Void;
                }

                if (thenType == elseType)
                {
                    return thenType;
                }

                if (thenType == AosValueKind.Unknown || elseType == AosValueKind.Unknown)
                {
                    return AosValueKind.Unknown;
                }

                _diagnostics.Add(new AosDiagnostic("VAL023", "If branches must return the same type.", node.Id, node.Span));
                return AosValueKind.Unknown;
            case "Block":
                var blockType = AosValueKind.Void;
                foreach (var child in node.Children)
                {
                    blockType = ValidateNode(child, env, permissions);
                }
                return blockType;
            case "Return":
                RequireChildren(node, 0, 1);
                if (node.Children.Count == 1)
                {
                    return ValidateNode(node.Children[0], env, permissions);
                }
                return AosValueKind.Void;
            default:
                _diagnostics.Add(new AosDiagnostic("VAL999", $"Unknown node kind '{node.Kind}'.", node.Id, node.Span));
                return AosValueKind.Unknown;
        }
    }

    private AosValueKind ValidateCall(AosNode node, string target, List<AosValueKind> argTypes, Dictionary<string, AosValueKind> env, HashSet<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            _diagnostics.Add(new AosDiagnostic("VAL030", "Call target is required.", node.Id, node.Span));
            return AosValueKind.Unknown;
        }

        if (target == "math.add")
        {
            RequirePermission(node, "math", permissions);
            if (argTypes.Count != 2)
            {
                _diagnostics.Add(new AosDiagnostic("VAL031", "math.add expects 2 arguments.", node.Id, node.Span));
            }
            else
            {
                if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL032", "math.add arg 1 must be int.", node.Id, node.Span));
                }
                if (argTypes[1] != AosValueKind.Int && argTypes[1] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL033", "math.add arg 2 must be int.", node.Id, node.Span));
                }
            }
            return AosValueKind.Int;
        }


        if (target == "console.print")
        {
            RequirePermission(node, "console", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL034", "console.print expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL035", "console.print arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Void;
        }

        if (target == "io.print")
        {
            RequirePermission(node, "io", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL070", "io.print expects 1 argument.", node.Id, node.Span));
            }
            return AosValueKind.Void;
        }

        if (target == "io.write")
        {
            RequirePermission(node, "io", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL072", "io.write expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL073", "io.write arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Void;
        }

        if (target == "io.readLine")
        {
            RequirePermission(node, "io", permissions);
            if (argTypes.Count != 0)
            {
                _diagnostics.Add(new AosDiagnostic("VAL071", "io.readLine expects 0 arguments.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "io.readAllStdin")
        {
            RequirePermission(node, "io", permissions);
            if (argTypes.Count != 0)
            {
                _diagnostics.Add(new AosDiagnostic("VAL074", "io.readAllStdin expects 0 arguments.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "io.readFile")
        {
            RequirePermission(node, "io", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL081", "io.readFile expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL082", "io.readFile arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "io.fileExists")
        {
            RequirePermission(node, "io", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL083", "io.fileExists expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL084", "io.fileExists arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Bool;
        }

        if (target == "compiler.parse")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL075", "compiler.parse expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL076", "compiler.parse arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Node;
        }

        if (target == "sys.net_listen")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL123", "sys.net_listen expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL124", "sys.net_listen arg must be int.", node.Id, node.Span));
            }
            return AosValueKind.Int;
        }

        if (target == "sys.net_accept")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL125", "sys.net_accept expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL126", "sys.net_accept arg must be int.", node.Id, node.Span));
            }
            return AosValueKind.Int;
        }

        if (target == "sys.net_readHeaders")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL127", "sys.net_readHeaders expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL128", "sys.net_readHeaders arg must be int.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "sys.net_write")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 2)
            {
                _diagnostics.Add(new AosDiagnostic("VAL129", "sys.net_write expects 2 arguments.", node.Id, node.Span));
            }
            else
            {
                if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL130", "sys.net_write arg 1 must be int.", node.Id, node.Span));
                }
                if (argTypes[1] != AosValueKind.String && argTypes[1] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL131", "sys.net_write arg 2 must be string.", node.Id, node.Span));
                }
            }
            return AosValueKind.Void;
        }

        if (target == "sys.net_close")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL132", "sys.net_close expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL133", "sys.net_close arg must be int.", node.Id, node.Span));
            }
            return AosValueKind.Void;
        }

        if (target == "sys.stdout_writeLine")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL134", "sys.stdout_writeLine expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL135", "sys.stdout_writeLine arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Void;
        }

        if (target == "sys.proc_exit")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL136", "sys.proc_exit expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Int && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL137", "sys.proc_exit arg must be int.", node.Id, node.Span));
            }
            return AosValueKind.Void;
        }

        if (target == "sys.fs_readFile")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL138", "sys.fs_readFile expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL139", "sys.fs_readFile arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "sys.fs_fileExists")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL140", "sys.fs_fileExists expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL141", "sys.fs_fileExists arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Bool;
        }

        if (target == "sys.str_utf8ByteCount")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL142", "sys.str_utf8ByteCount expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL143", "sys.str_utf8ByteCount arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Int;
        }

        if (target == "sys.vm_run")
        {
            RequirePermission(node, "sys", permissions);
            if (argTypes.Count != 3)
            {
                _diagnostics.Add(new AosDiagnostic("VAL144", "sys.vm_run expects 3 arguments.", node.Id, node.Span));
            }
            else
            {
                if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL145", "sys.vm_run arg 1 must be node.", node.Id, node.Span));
                }
                if (argTypes[1] != AosValueKind.String && argTypes[1] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL146", "sys.vm_run arg 2 must be string.", node.Id, node.Span));
                }
                if (argTypes[2] != AosValueKind.Node && argTypes[2] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL147", "sys.vm_run arg 3 must be node.", node.Id, node.Span));
                }
            }
            return AosValueKind.Unknown;
        }

        if (target == "compiler.format")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL077", "compiler.format expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL078", "compiler.format arg must be node.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "compiler.parseHttpRequest")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL104", "compiler.parseHttpRequest expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL105", "compiler.parseHttpRequest arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Node;
        }

        if (target == "compiler.route")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 2)
            {
                _diagnostics.Add(new AosDiagnostic("VAL109", "compiler.route expects 2 arguments.", node.Id, node.Span));
            }
            else
            {
                if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL110", "compiler.route arg 1 must be string.", node.Id, node.Span));
                }
                if (argTypes[1] != AosValueKind.Node && argTypes[1] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL111", "compiler.route arg 2 must be node.", node.Id, node.Span));
                }
            }
            return AosValueKind.Node;
        }

        if (target == "compiler.toJson")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL115", "compiler.toJson expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL116", "compiler.toJson arg must be node.", node.Id, node.Span));
            }
            return AosValueKind.String;
        }

        if (target == "compiler.emitBytecode")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL148", "compiler.emitBytecode expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL149", "compiler.emitBytecode arg must be node.", node.Id, node.Span));
            }
            return AosValueKind.Node;
        }

        if (target == "compiler.strCompare")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 2)
            {
                _diagnostics.Add(new AosDiagnostic("VAL117", "compiler.strCompare expects 2 arguments.", node.Id, node.Span));
            }
            else
            {
                if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL118", "compiler.strCompare arg 1 must be string.", node.Id, node.Span));
                }
                if (argTypes[1] != AosValueKind.String && argTypes[1] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL119", "compiler.strCompare arg 2 must be string.", node.Id, node.Span));
                }
            }
            return AosValueKind.Int;
        }

        if (target == "compiler.strToken")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 2)
            {
                _diagnostics.Add(new AosDiagnostic("VAL120", "compiler.strToken expects 2 arguments.", node.Id, node.Span));
            }
            else
            {
                if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL121", "compiler.strToken arg 1 must be string.", node.Id, node.Span));
                }
                if (argTypes[1] != AosValueKind.Int && argTypes[1] != AosValueKind.Unknown)
                {
                    _diagnostics.Add(new AosDiagnostic("VAL122", "compiler.strToken arg 2 must be int.", node.Id, node.Span));
                }
            }
            return AosValueKind.String;
        }

        if (target == "compiler.validate")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL079", "compiler.validate expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL080", "compiler.validate arg must be node.", node.Id, node.Span));
            }
            return AosValueKind.Node;
        }

        if (target == "compiler.validateHost")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL087", "compiler.validateHost expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL088", "compiler.validateHost arg must be node.", node.Id, node.Span));
            }
            return AosValueKind.Node;
        }

        if (target == "compiler.test")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL085", "compiler.test expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.String && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL086", "compiler.test arg must be string.", node.Id, node.Span));
            }
            return AosValueKind.Int;
        }

        if (target == "compiler.run")
        {
            RequirePermission(node, "compiler", permissions);
            if (argTypes.Count != 1)
            {
                _diagnostics.Add(new AosDiagnostic("VAL090", "compiler.run expects 1 argument.", node.Id, node.Span));
            }
            else if (argTypes[0] != AosValueKind.Node && argTypes[0] != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic("VAL091", "compiler.run arg must be node.", node.Id, node.Span));
            }
            return AosValueKind.Node;
        }

        if (!target.Contains('.') && env.TryGetValue(target, out var fnType) && fnType == AosValueKind.Function)
        {
            return AosValueKind.Unknown;
        }

        _diagnostics.Add(new AosDiagnostic("VAL036", $"Unknown call target '{target}'.", node.Id, node.Span));
        return AosValueKind.Unknown;
    }

    private void RequireAttr(AosNode node, string name)
    {
        if (!node.Attrs.ContainsKey(name))
        {
            _diagnostics.Add(new AosDiagnostic("VAL002", $"Missing attribute '{name}'.", node.Id, node.Span));
        }
    }

    private void RequireChildren(AosNode node, int min, int max)
    {
        if (node.Children.Count < min || node.Children.Count > max)
        {
            _diagnostics.Add(new AosDiagnostic("VAL003", $"{node.Kind} expects {min}-{max} children.", node.Id, node.Span));
        }
    }

    private void RequirePermission(AosNode node, string permission, HashSet<string> permissions)
    {
        if (!permissions.Contains(permission))
        {
            _diagnostics.Add(new AosDiagnostic("VAL040", $"Permission '{permission}' denied.", node.Id, node.Span));
        }
    }

    private void ValidateNodeChildPair(AosNode node, Dictionary<string, AosValueKind> env, HashSet<string> permissions, string code)
    {
        if (node.Children.Count != 2)
        {
            return;
        }
        var targetType = ValidateNode(node.Children[0], env, permissions);
        var indexType = ValidateNode(node.Children[1], env, permissions);
        if (targetType != AosValueKind.Node && targetType != AosValueKind.Unknown)
        {
            _diagnostics.Add(new AosDiagnostic(code, $"{node.Kind} expects node as first child.", node.Id, node.Span));
        }
        if (indexType != AosValueKind.Int && indexType != AosValueKind.Unknown)
        {
            _diagnostics.Add(new AosDiagnostic(code, $"{node.Kind} expects int index as second child.", node.Id, node.Span));
        }
    }

    private void ValidateChildrenAsStrings(AosNode node, Dictionary<string, AosValueKind> env, HashSet<string> permissions, string code)
    {
        foreach (var child in node.Children)
        {
            var valueType = ValidateNode(child, env, permissions);
            if (valueType != AosValueKind.String && valueType != AosValueKind.Unknown)
            {
                _diagnostics.Add(new AosDiagnostic(code, $"{node.Kind} expects string arguments.", node.Id, node.Span));
                return;
            }
        }
    }

    private void PredeclareFunctions(AosNode node, Dictionary<string, AosValueKind> env)
    {
        if (node.Kind == "Let" && node.Children.Count == 1 && node.Children[0].Kind == "Fn")
        {
            if (node.Attrs.TryGetValue("name", out var nameAttr) && nameAttr.Kind == AosAttrKind.Identifier)
            {
                env[nameAttr.AsString()] = AosValueKind.Function;
            }
        }

        foreach (var child in node.Children)
        {
            PredeclareFunctions(child, env);
        }
    }
}
