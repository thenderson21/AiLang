namespace AiLang.Core;

public sealed partial class AosInterpreter
{
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
}
