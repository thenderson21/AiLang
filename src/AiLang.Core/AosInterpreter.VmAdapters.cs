using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private sealed class BytecodeAdapter : IVmBytecodeAdapter<AosNode, AosValue>
    {
        public static readonly BytecodeAdapter Instance = new();

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
        public AosValue FromEncodedNodeConstant(string encodedNode, string nodeId) =>
            AosValue.FromNode(DecodeNodeConstant(encodedNode, nodeId));
    }

    private static List<AosValue> BuildVmArgs(VmProgram<AosValue> vm, string entryName, AosNode argsNode)
    {
        if (!vm.FunctionIndexByName.TryGetValue(entryName, out var entryIndex))
        {
            throw new VmRuntimeException("VM001", $"Entry function not found: {entryName}.", entryName);
        }
        var entry = vm.Functions[entryIndex];

        var args = new List<AosValue>();
        if (entry.Params.Count == 1 && string.Equals(entry.Params[0], "argv", StringComparison.Ordinal))
        {
            args.Add(AosValue.FromNode(argsNode));
            return args;
        }

        foreach (var child in argsNode.Children)
        {
            if (child.Kind == "Lit" && child.Attrs.TryGetValue("value", out var valueAttr))
            {
                args.Add(valueAttr.Kind switch
                {
                    AosAttrKind.String => AosValue.FromString(valueAttr.AsString()),
                    AosAttrKind.Int => AosValue.FromInt(valueAttr.AsInt()),
                    AosAttrKind.Bool => AosValue.FromBool(valueAttr.AsBool()),
                    AosAttrKind.Identifier when valueAttr.AsString() == "null" => AosValue.Unknown,
                    _ => AosValue.Unknown
                });
            }
            else
            {
                args.Add(AosValue.FromNode(child));
            }
        }

        return args;
    }

    private sealed class VmExecutionAdapter : IVmExecutionAdapter<AosValue, AosNode>
    {
        private readonly AosInterpreter _interpreter;
        private readonly AosRuntime _runtime;

        public VmExecutionAdapter(AosInterpreter interpreter, AosRuntime runtime)
        {
            _interpreter = interpreter;
            _runtime = runtime;
        }

        public AosValue VoidValue => AosValue.Void;
        public AosValue UnknownValue => AosValue.Unknown;
        public bool IsUnknown(AosValue value) => value.Kind == AosValueKind.Unknown;
        public AosValue FromBool(bool value) => AosValue.FromBool(value);
        public AosValue FromInt(int value) => AosValue.FromInt(value);
        public AosValue FromString(string value) => AosValue.FromString(value);
        public AosValue FromNode(AosNode node) => AosValue.FromNode(node);

        public bool TryGetBool(AosValue value, out bool result)
        {
            if (value.Kind == AosValueKind.Bool)
            {
                result = value.AsBool();
                return true;
            }
            result = false;
            return false;
        }

        public bool TryGetInt(AosValue value, out int result)
        {
            if (value.Kind == AosValueKind.Int)
            {
                result = value.AsInt();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetString(AosValue value, out string? result)
        {
            if (value.Kind == AosValueKind.String)
            {
                result = value.AsString();
                return true;
            }
            result = null;
            return false;
        }

        public bool TryGetNode(AosValue value, out AosNode? node)
        {
            if (value.Kind == AosValueKind.Node)
            {
                node = value.AsNode();
                return true;
            }
            node = null;
            return false;
        }

        public bool ValueEquals(AosValue left, AosValue right)
        {
            return left.Kind == right.Kind && left.Kind switch
            {
                AosValueKind.String => left.AsString() == right.AsString(),
                AosValueKind.Int => left.AsInt() == right.AsInt(),
                AosValueKind.Bool => left.AsBool() == right.AsBool(),
                AosValueKind.Unknown => true,
                _ => false
            };
        }

        public string ValueToDisplayString(AosValue value) => AosInterpreter.ValueToDisplayString(value);
        public string EscapeString(string value) => AosInterpreter.EscapeString(value);
        public string NodeKind(AosNode node) => node.Kind;
        public string NodeId(AosNode node) => node.Id;

        public IReadOnlyList<KeyValuePair<string, VmAttr>> OrderedAttrs(AosNode node)
        {
            return node.Attrs
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new KeyValuePair<string, VmAttr>(kv.Key, kv.Value.Kind switch
                {
                    AosAttrKind.Identifier => VmAttr.Identifier(kv.Value.AsString()),
                    AosAttrKind.String => VmAttr.String(kv.Value.AsString()),
                    AosAttrKind.Int => VmAttr.Int(kv.Value.AsInt()),
                    AosAttrKind.Bool => VmAttr.Bool(kv.Value.AsBool()),
                    _ => VmAttr.Missing()
                }))
                .ToList();
        }

        public IReadOnlyList<AosNode> NodeChildren(AosNode node) => node.Children;

        public AosNode CreateNode(string kind, string id, IReadOnlyDictionary<string, VmAttr> attrs, IReadOnlyList<AosNode> children)
        {
            var converted = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal);
            foreach (var kv in attrs)
            {
                converted[kv.Key] = kv.Value.Kind switch
                {
                    VmAttrKind.Identifier => new AosAttrValue(AosAttrKind.Identifier, kv.Value.StringValue),
                    VmAttrKind.String => new AosAttrValue(AosAttrKind.String, kv.Value.StringValue),
                    VmAttrKind.Int => new AosAttrValue(AosAttrKind.Int, kv.Value.IntValue),
                    VmAttrKind.Bool => new AosAttrValue(AosAttrKind.Bool, kv.Value.BoolValue),
                    _ => new AosAttrValue(AosAttrKind.Identifier, string.Empty)
                };
            }

            return new AosNode(
                kind,
                id,
                converted,
                children.ToList(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        }

        public AosNode ValueToNode(AosValue value) => ToRuntimeNode(value);

        public AosValue ExecuteCall(string target, IReadOnlyList<AosValue> args)
        {
            var callNode = new AosNode(
                "Call",
                "vm_call",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["target"] = new AosAttrValue(AosAttrKind.Identifier, target)
                },
                args.Select(ToRuntimeNode).ToList(),
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
            var env = new Dictionary<string, AosValue>(_runtime.Env, StringComparer.Ordinal);
            return _interpreter.EvalCall(callNode, _runtime, env);
        }
    }
}
