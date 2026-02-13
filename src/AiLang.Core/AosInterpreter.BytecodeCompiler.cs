using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private sealed class VmCompileFunction
    {
        public required string Name { get; init; }
        public required List<string> Parameters { get; init; }
        public required List<string> Locals { get; init; }
        public required List<AosNode> Instructions { get; init; }
    }

    private sealed class VmCompileContext
    {
        private readonly Dictionary<string, int> _constantIndex = new(StringComparer.Ordinal);
        private readonly List<AosValue> _constants = new();
        private readonly Dictionary<string, AosNode> _functions = new(StringComparer.Ordinal);
        private readonly List<string> _functionOrder = new();
        private int _instructionId;

        public VmCompileContext(AosNode program, bool allowImportNodes)
        {
            Program = program;
            AllowImportNodes = allowImportNodes;
        }

        public AosNode Program { get; }
        public bool AllowImportNodes { get; }

        public int AddConstant(AosValue value)
        {
            var key = VmConstantKey(value);
            if (_constantIndex.TryGetValue(key, out var existing))
            {
                return existing;
            }
            var next = _constants.Count;
            _constants.Add(value);
            _constantIndex[key] = next;
            return next;
        }

        public IReadOnlyList<AosValue> Constants => _constants;

        public void DiscoverFunctions()
        {
            foreach (var child in Program.Children)
            {
                if (child.Kind != "Let")
                {
                    continue;
                }
                if (!child.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
                {
                    continue;
                }
                if (child.Children.Count != 1 || child.Children[0].Kind != "Fn")
                {
                    continue;
                }
                var name = nameAttr.AsString();
                _functions[name] = child.Children[0];
            }

            _functionOrder.Clear();
            _functionOrder.Add("main");
            foreach (var name in _functions.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                _functionOrder.Add(name);
            }
        }

        public IReadOnlyList<string> FunctionOrder => _functionOrder;

        public AosNode? GetFunctionNode(string name)
        {
            return _functions.TryGetValue(name, out var fn) ? fn : null;
        }

        public AosNode BuildInstruction(string op, int? a = null, int? b = null, string? s = null)
        {
            return BuildVmInstruction(_instructionId++, op, a, b, s);
        }
    }

    private sealed class VmFunctionCompileState
    {
        private readonly VmCompileContext _context;
        private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
        private readonly List<string> _locals = new();

        public VmFunctionCompileState(VmCompileContext context, string functionName, List<string> parameters)
        {
            _context = context;
            Name = functionName;
            Parameters = parameters;
            Instructions = new List<AosNode>();
            for (var i = 0; i < parameters.Count; i++)
            {
                _slots[parameters[i]] = i;
                _locals.Add(parameters[i]);
            }
        }

        public string Name { get; }
        public List<string> Parameters { get; }
        public List<AosNode> Instructions { get; }
        public IReadOnlyList<string> LocalNames => _locals;

        public int EnsureSlot(string name)
        {
            if (_slots.TryGetValue(name, out var slot))
            {
                return slot;
            }
            slot = _locals.Count;
            _slots[name] = slot;
            _locals.Add(name);
            return slot;
        }

        public bool TryGetSlot(string name, out int slot)
        {
            return _slots.TryGetValue(name, out slot);
        }

        public void Emit(string op, int? a = null, int? b = null, string? s = null)
        {
            Instructions.Add(_context.BuildInstruction(op, a, b, s));
        }
    }

    private static class BytecodeCompiler
    {
        public static AosNode Compile(AosNode program, bool allowImportNodes = false)
        {
            if (program.Kind != "Program")
            {
                throw new VmRuntimeException("VM001", "compiler.emitBytecode expects Program node.", program.Id);
            }

            var context = new VmCompileContext(program, allowImportNodes);
            context.DiscoverFunctions();
            var compiled = new List<VmCompileFunction>();

            foreach (var functionName in context.FunctionOrder)
            {
                if (functionName == "main")
                {
                    compiled.Add(CompileMain(context));
                    continue;
                }

                var fnNode = context.GetFunctionNode(functionName);
                if (fnNode is null)
                {
                    throw new VmRuntimeException("VM001", $"Function not found: {functionName}.", program.Id);
                }
                compiled.Add(CompileFunction(context, functionName, fnNode));
            }

            var children = new List<AosNode>();
            for (var i = 0; i < context.Constants.Count; i++)
            {
                var constant = context.Constants[i];
                var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal);
                if (constant.Kind == AosValueKind.String)
                {
                    attrs["kind"] = new AosAttrValue(AosAttrKind.Identifier, "string");
                    attrs["value"] = new AosAttrValue(AosAttrKind.String, constant.AsString());
                }
                else if (constant.Kind == AosValueKind.Int)
                {
                    attrs["kind"] = new AosAttrValue(AosAttrKind.Identifier, "int");
                    attrs["value"] = new AosAttrValue(AosAttrKind.Int, constant.AsInt());
                }
                else if (constant.Kind == AosValueKind.Bool)
                {
                    attrs["kind"] = new AosAttrValue(AosAttrKind.Identifier, "bool");
                    attrs["value"] = new AosAttrValue(AosAttrKind.Bool, constant.AsBool());
                }
                else if (constant.Kind == AosValueKind.Node)
                {
                    attrs["kind"] = new AosAttrValue(AosAttrKind.Identifier, "node");
                    attrs["value"] = new AosAttrValue(AosAttrKind.String, EncodeNodeConstant(constant.AsNode()));
                }
                else
                {
                    attrs["kind"] = new AosAttrValue(AosAttrKind.Identifier, "null");
                    attrs["value"] = new AosAttrValue(AosAttrKind.Identifier, "null");
                }

                children.Add(new AosNode(
                    "Const",
                    $"k{i}",
                    attrs,
                    new List<AosNode>(),
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
            }

            foreach (var function in compiled)
            {
                children.Add(new AosNode(
                    "Func",
                    $"f_{function.Name}",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, function.Name),
                        ["params"] = new AosAttrValue(AosAttrKind.String, string.Join(",", function.Parameters)),
                        ["locals"] = new AosAttrValue(AosAttrKind.String, string.Join(",", function.Locals))
                    },
                    function.Instructions,
                    new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
            }

            return new AosNode(
                "Bytecode",
                "bc1",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["magic"] = new AosAttrValue(AosAttrKind.String, "AIBC"),
                    ["format"] = new AosAttrValue(AosAttrKind.String, "AiBC1"),
                    ["version"] = new AosAttrValue(AosAttrKind.Int, 1),
                    ["flags"] = new AosAttrValue(AosAttrKind.Int, 0)
                },
                children,
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        }

        private static VmCompileFunction CompileMain(VmCompileContext context)
        {
            var state = new VmFunctionCompileState(context, "main", new List<string> { "argv" });
            foreach (var child in context.Program.Children)
            {
                if (child.Kind == "Let" &&
                    child.Attrs.TryGetValue("name", out var letNameAttr) &&
                    letNameAttr.Kind == AosAttrKind.Identifier &&
                    child.Children.Count == 1 &&
                    child.Children[0].Kind == "Fn")
                {
                    continue;
                }

                CompileStatement(context, state, child);
            }

            state.Emit("RETURN");
            return new VmCompileFunction
            {
                Name = "main",
                Parameters = state.Parameters,
                Locals = state.LocalNames.ToList(),
                Instructions = state.Instructions
            };
        }

        private static VmCompileFunction CompileFunction(VmCompileContext context, string name, AosNode fnNode)
        {
            if (!fnNode.Attrs.TryGetValue("params", out var paramsAttr) || paramsAttr.Kind != AosAttrKind.Identifier)
            {
                throw new VmRuntimeException("VM001", "Fn missing params.", fnNode.Id);
            }
            if (fnNode.Children.Count != 1 || fnNode.Children[0].Kind != "Block")
            {
                throw new VmRuntimeException("VM001", "Fn body must be Block.", fnNode.Id);
            }

            var parameters = paramsAttr.AsString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var state = new VmFunctionCompileState(context, name, parameters);
            CompileBlock(context, state, fnNode.Children[0], isRoot: true);
            state.Emit("CONST", context.AddConstant(AosValue.Unknown));
            state.Emit("RETURN");

            return new VmCompileFunction
            {
                Name = name,
                Parameters = state.Parameters,
                Locals = state.LocalNames.ToList(),
                Instructions = state.Instructions
            };
        }

        private static void CompileBlock(VmCompileContext context, VmFunctionCompileState state, AosNode block, bool isRoot)
        {
            if (block.Kind != "Block")
            {
                throw new VmRuntimeException("VM001", "Expected Block node.", block.Id);
            }
            foreach (var child in block.Children)
            {
                CompileStatement(context, state, child);
            }
            if (!isRoot)
            {
                state.Emit("CONST", context.AddConstant(AosValue.Unknown));
            }
        }

        private static void CompileStatement(VmCompileContext context, VmFunctionCompileState state, AosNode node)
        {
            if (node.Kind == "Export")
            {
                return;
            }

            if (node.Kind == "Import")
            {
                if (context.AllowImportNodes)
                {
                    return;
                }
                throw new VmRuntimeException("VM001", "Unsupported construct in bytecode mode: Import.", node.Id);
            }

            if (node.Kind == "Let")
            {
                if (!node.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
                {
                    throw new VmRuntimeException("VM001", "Let requires name.", node.Id);
                }
                if (node.Children.Count != 1)
                {
                    throw new VmRuntimeException("VM001", "Let requires exactly one child.", node.Id);
                }
                if (node.Children[0].Kind == "Fn")
                {
                    // top-level function declarations are handled separately.
                    return;
                }

                CompileExpression(context, state, node.Children[0]);
                var slot = state.EnsureSlot(nameAttr.AsString());
                state.Emit("STORE_LOCAL", slot);
                return;
            }

            if (node.Kind == "Return")
            {
                if (node.Children.Count == 0)
                {
                    state.Emit("CONST", context.AddConstant(AosValue.Unknown));
                }
                else if (node.Children.Count == 1)
                {
                    CompileExpression(context, state, node.Children[0]);
                }
                else
                {
                    throw new VmRuntimeException("VM001", "Return supports at most one child.", node.Id);
                }
                state.Emit("RETURN");
                return;
            }

            if (node.Kind == "If")
            {
                CompileIfStatement(context, state, node);
                return;
            }

            CompileExpression(context, state, node);
            state.Emit("POP");
        }

        private static void CompileIfStatement(VmCompileContext context, VmFunctionCompileState state, AosNode node)
        {
            if (node.Children.Count < 2 || node.Children.Count > 3)
            {
                throw new VmRuntimeException("VM001", "If requires 2 or 3 children.", node.Id);
            }

            CompileExpression(context, state, node.Children[0]);
            var jumpIfFalseIndex = state.Instructions.Count;
            state.Emit("JUMP_IF_FALSE", 0);
            CompileStatementOrBlock(context, state, node.Children[1]);
            var jumpEndIndex = state.Instructions.Count;
            state.Emit("JUMP", 0);
            var elseStart = state.Instructions.Count;
            if (node.Children.Count == 3)
            {
                CompileStatementOrBlock(context, state, node.Children[2]);
            }
            var end = state.Instructions.Count;
            PatchJump(state.Instructions[jumpIfFalseIndex], elseStart);
            PatchJump(state.Instructions[jumpEndIndex], end);
        }

        private static void CompileStatementOrBlock(VmCompileContext context, VmFunctionCompileState state, AosNode node)
        {
            if (node.Kind == "Block")
            {
                foreach (var child in node.Children)
                {
                    CompileStatement(context, state, child);
                }
                return;
            }
            CompileStatement(context, state, node);
        }

        private static void CompileExpression(VmCompileContext context, VmFunctionCompileState state, AosNode node)
        {
            switch (node.Kind)
            {
                case "Var":
                {
                    if (!node.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
                    {
                        throw new VmRuntimeException("VM001", "Var requires name.", node.Id);
                    }
                    if (!state.TryGetSlot(nameAttr.AsString(), out var slot))
                    {
                        throw new VmRuntimeException("VM001", $"Unsupported variable in bytecode mode: {nameAttr.AsString()}.", node.Id);
                    }
                    state.Emit("LOAD_LOCAL", slot);
                    return;
                }
                case "Lit":
                {
                    if (!node.Attrs.TryGetValue("value", out var litAttr))
                    {
                        throw new VmRuntimeException("VM001", "Lit missing value.", node.Id);
                    }
                    var constIndex = litAttr.Kind switch
                    {
                        AosAttrKind.String => context.AddConstant(AosValue.FromString(litAttr.AsString())),
                        AosAttrKind.Int => context.AddConstant(AosValue.FromInt(litAttr.AsInt())),
                        AosAttrKind.Bool => context.AddConstant(AosValue.FromBool(litAttr.AsBool())),
                        AosAttrKind.Identifier when litAttr.AsString() == "null" => context.AddConstant(AosValue.Unknown),
                        _ => throw new VmRuntimeException("VM001", "Unsupported literal in bytecode mode.", node.Id)
                    };
                    state.Emit("CONST", constIndex);
                    return;
                }
                case "Eq":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "Eq expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("EQ");
                    return;
                }
                case "Add":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "Add expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("ADD_INT");
                    return;
                }
                case "StrConcat":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "StrConcat expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("STR_CONCAT");
                    return;
                }
                case "ToString":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "ToString expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("TO_STRING");
                    return;
                }
                case "StrEscape":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "StrEscape expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("STR_ESCAPE");
                    return;
                }
                case "NodeKind":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "NodeKind expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("NODE_KIND");
                    return;
                }
                case "NodeId":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "NodeId expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("NODE_ID");
                    return;
                }
                case "AttrCount":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "AttrCount expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("ATTR_COUNT");
                    return;
                }
                case "AttrKey":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "AttrKey expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("ATTR_KEY");
                    return;
                }
                case "AttrValueKind":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "AttrValueKind expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("ATTR_VALUE_KIND");
                    return;
                }
                case "AttrValueString":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "AttrValueString expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("ATTR_VALUE_STRING");
                    return;
                }
                case "AttrValueInt":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "AttrValueInt expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("ATTR_VALUE_INT");
                    return;
                }
                case "AttrValueBool":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "AttrValueBool expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("ATTR_VALUE_BOOL");
                    return;
                }
                case "ChildCount":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "ChildCount expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("CHILD_COUNT");
                    return;
                }
                case "ChildAt":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "ChildAt expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("CHILD_AT");
                    return;
                }
                case "MakeBlock":
                {
                    if (node.Children.Count != 1)
                    {
                        throw new VmRuntimeException("VM001", "MakeBlock expects 1 child.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    state.Emit("MAKE_BLOCK");
                    return;
                }
                case "AppendChild":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "AppendChild expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("APPEND_CHILD");
                    return;
                }
                case "MakeErr":
                {
                    if (node.Children.Count != 4)
                    {
                        throw new VmRuntimeException("VM001", "MakeErr expects 4 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    CompileExpression(context, state, node.Children[2]);
                    CompileExpression(context, state, node.Children[3]);
                    state.Emit("MAKE_ERR");
                    return;
                }
                case "MakeLitString":
                {
                    if (node.Children.Count != 2)
                    {
                        throw new VmRuntimeException("VM001", "MakeLitString expects 2 children.", node.Id);
                    }
                    CompileExpression(context, state, node.Children[0]);
                    CompileExpression(context, state, node.Children[1]);
                    state.Emit("MAKE_LIT_STRING");
                    return;
                }
                case "Map":
                case "Field":
                case "Route":
                case "Match":
                case "Command":
                case "Event":
                case "HttpRequest":
                case "Import":
                case "Export":
                {
                    CompileNodeLiteral(context, state, node);
                    return;
                }
                case "Call":
                {
                    if (!node.Attrs.TryGetValue("target", out var targetAttr) || targetAttr.Kind != AosAttrKind.Identifier)
                    {
                        throw new VmRuntimeException("VM001", "Call target missing.", node.Id);
                    }
                    foreach (var child in node.Children)
                    {
                        CompileExpression(context, state, child);
                    }
                    var target = targetAttr.AsString();
                    var fnIndex = -1;
                    for (var i = 0; i < context.FunctionOrder.Count; i++)
                    {
                        if (string.Equals(context.FunctionOrder[i], target, StringComparison.Ordinal))
                        {
                            fnIndex = i;
                            break;
                        }
                    }
                    if (fnIndex >= 0)
                    {
                        state.Emit("CALL", fnIndex, node.Children.Count);
                    }
                    else
                    {
                        state.Emit("CALL_SYS", node.Children.Count, null, target);
                    }
                    return;
                }
                case "If":
                    CompileIfExpression(context, state, node);
                    return;
                case "Block":
                    if (node.Children.Count == 1 && !IsStatementOnlyNode(node.Children[0]))
                    {
                        CompileExpression(context, state, node.Children[0]);
                    }
                    else
                    {
                        foreach (var child in node.Children)
                        {
                            CompileStatement(context, state, child);
                        }
                        state.Emit("CONST", context.AddConstant(AosValue.Unknown));
                    }
                    return;
                default:
                    throw new VmRuntimeException("VM001", $"Unsupported construct in bytecode mode: {node.Kind}.", node.Id);
            }
        }

        private static bool IsStatementOnlyNode(AosNode node)
        {
            return node.Kind is "Let" or "Return" or "Import" or "Export";
        }

        private static void CompileIfExpression(VmCompileContext context, VmFunctionCompileState state, AosNode node)
        {
            if (node.Children.Count < 2 || node.Children.Count > 3)
            {
                throw new VmRuntimeException("VM001", "If requires 2 or 3 children.", node.Id);
            }

            CompileExpression(context, state, node.Children[0]);
            var jumpIfFalseIndex = state.Instructions.Count;
            state.Emit("JUMP_IF_FALSE", 0);
            CompileExpression(context, state, node.Children[1]);
            var jumpEndIndex = state.Instructions.Count;
            state.Emit("JUMP", 0);
            var elseStart = state.Instructions.Count;
            if (node.Children.Count == 3)
            {
                CompileExpression(context, state, node.Children[2]);
            }
            else
            {
                state.Emit("CONST", context.AddConstant(AosValue.Unknown));
            }
            var end = state.Instructions.Count;
            PatchJump(state.Instructions[jumpIfFalseIndex], elseStart);
            PatchJump(state.Instructions[jumpEndIndex], end);
        }

        private static void PatchJump(AosNode instruction, int target)
        {
            instruction.Attrs["a"] = new AosAttrValue(AosAttrKind.Int, target);
        }

        private static void CompileNodeLiteral(VmCompileContext context, VmFunctionCompileState state, AosNode node)
        {
            foreach (var child in node.Children)
            {
                CompileExpression(context, state, child);
            }

            var template = new AosNode(
                node.Kind,
                node.Id,
                new Dictionary<string, AosAttrValue>(node.Attrs, StringComparer.Ordinal),
                new List<AosNode>(),
                node.Span);
            var constIndex = context.AddConstant(AosValue.FromNode(template));
            state.Emit("MAKE_NODE", constIndex, node.Children.Count);
        }
    }


}
