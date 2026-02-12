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
    public Dictionary<int, System.Net.Sockets.TcpListener> NetListeners { get; } = new();
    public Dictionary<int, System.Net.Sockets.TcpClient> NetConnections { get; } = new();
    public Dictionary<int, System.Security.Cryptography.X509Certificates.X509Certificate2> NetTlsCertificates { get; } = new();
    public Dictionary<int, System.Net.Security.SslStream> NetTlsStreams { get; } = new();
    public int NextNetHandle { get; set; } = 1;
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
            var vm = VmProgram.Load(bytecode);
            return VmRunner.Run(this, runtime, vm, entryName, argsNode);
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

        if (target == "sys.net_listen")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var portValue = EvalNode(node.Children[0], runtime, env);
            if (portValue.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }

            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, portValue.AsInt());
            listener.Start();
            var handle = runtime.NextNetHandle++;
            runtime.NetListeners[handle] = listener;
            return AosValue.FromInt(handle);
        }

        if (target == "sys.net_listen_tls")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 3)
            {
                return AosValue.Unknown;
            }

            var portValue = EvalNode(node.Children[0], runtime, env);
            var certPathValue = EvalNode(node.Children[1], runtime, env);
            var keyPathValue = EvalNode(node.Children[2], runtime, env);
            if (portValue.Kind != AosValueKind.Int || certPathValue.Kind != AosValueKind.String || keyPathValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            var certPath = certPathValue.AsString();
            var keyPath = keyPathValue.AsString();
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificate;
            try
            {
                certificate = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(certPath, keyPath);
            }
            catch
            {
                return AosValue.FromInt(-1);
            }

            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, portValue.AsInt());
            listener.Start();
            var handle = runtime.NextNetHandle++;
            runtime.NetListeners[handle] = listener;
            runtime.NetTlsCertificates[handle] = certificate;
            return AosValue.FromInt(handle);
        }

        if (target == "sys.net_accept")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var handleValue = EvalNode(node.Children[0], runtime, env);
            if (handleValue.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }

            if (!runtime.NetListeners.TryGetValue(handleValue.AsInt(), out var listener))
            {
                return AosValue.FromInt(-1);
            }

            var client = listener.AcceptTcpClient();
            var connHandle = runtime.NextNetHandle++;
            runtime.NetConnections[connHandle] = client;
            if (runtime.NetTlsCertificates.TryGetValue(handleValue.AsInt(), out var cert))
            {
                var tlsStream = new System.Net.Security.SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                try
                {
                    tlsStream.AuthenticateAsServer(
                        cert,
                        clientCertificateRequired: false,
                        enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                        checkCertificateRevocation: false);
                }
                catch
                {
                    tlsStream.Dispose();
                    try { client.Close(); } catch { }
                    runtime.NetConnections.Remove(connHandle);
                    return AosValue.FromInt(-1);
                }

                runtime.NetTlsStreams[connHandle] = tlsStream;
            }
            return AosValue.FromInt(connHandle);
        }

        if (target == "sys.net_readHeaders")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var handleValue = EvalNode(node.Children[0], runtime, env);
            if (handleValue.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }

            if (!runtime.NetConnections.TryGetValue(handleValue.AsInt(), out var client))
            {
                return AosValue.FromString(string.Empty);
            }

            Stream stream = runtime.NetTlsStreams.TryGetValue(handleValue.AsInt(), out var tlsStream)
                ? tlsStream
                : client.GetStream();
            var bytes = new List<byte>(1024);
            var endSeen = 0;
            var pattern = new byte[] { 13, 10, 13, 10 };
            while (bytes.Count < 65536)
            {
                var next = stream.ReadByte();
                if (next < 0)
                {
                    break;
                }

                var b = (byte)next;
                bytes.Add(b);
                if (b == pattern[endSeen])
                {
                    endSeen++;
                    if (endSeen == 4)
                    {
                        break;
                    }
                }
                else
                {
                    endSeen = b == pattern[0] ? 1 : 0;
                }
            }

            var headerText = System.Text.Encoding.ASCII.GetString(bytes.ToArray());
            var contentLength = ParseContentLength(headerText);
            if (contentLength > 0)
            {
                var bodyBuffer = new byte[contentLength];
                var readTotal = 0;
                while (readTotal < contentLength)
                {
                    var read = stream.Read(bodyBuffer, readTotal, contentLength - readTotal);
                    if (read <= 0)
                    {
                        break;
                    }
                    readTotal += read;
                }

                if (readTotal > 0)
                {
                    headerText += System.Text.Encoding.UTF8.GetString(bodyBuffer, 0, readTotal);
                }
            }

            return AosValue.FromString(headerText);
        }

        if (target == "sys.net_write")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 2)
            {
                return AosValue.Unknown;
            }

            var handleValue = EvalNode(node.Children[0], runtime, env);
            var textValue = EvalNode(node.Children[1], runtime, env);
            if (handleValue.Kind != AosValueKind.Int || textValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            if (!runtime.NetConnections.TryGetValue(handleValue.AsInt(), out var client))
            {
                return AosValue.Unknown;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(textValue.AsString());
            Stream stream = runtime.NetTlsStreams.TryGetValue(handleValue.AsInt(), out var tlsStream)
                ? tlsStream
                : client.GetStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return AosValue.Void;
        }

        if (target == "sys.net_close")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var handleValue = EvalNode(node.Children[0], runtime, env);
            if (handleValue.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }

            var handle = handleValue.AsInt();
            if (runtime.NetConnections.TryGetValue(handle, out var conn))
            {
                if (runtime.NetTlsStreams.TryGetValue(handle, out var tlsStream))
                {
                    try { tlsStream.Dispose(); } catch { }
                    runtime.NetTlsStreams.Remove(handle);
                }
                try { conn.Close(); } catch { }
                runtime.NetConnections.Remove(handle);
                return AosValue.Void;
            }

            if (runtime.NetListeners.TryGetValue(handle, out var listener))
            {
                try { listener.Stop(); } catch { }
                runtime.NetListeners.Remove(handle);
                runtime.NetTlsCertificates.Remove(handle);
                return AosValue.Void;
            }

            return AosValue.Void;
        }

        if (target == "sys.stdout_writeLine")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var textValue = EvalNode(node.Children[0], runtime, env);
            if (textValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            Console.WriteLine(textValue.AsString());
            return AosValue.Void;
        }

        if (target == "sys.proc_exit")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var codeValue = EvalNode(node.Children[0], runtime, env);
            if (codeValue.Kind != AosValueKind.Int)
            {
                return AosValue.Unknown;
            }

            throw new AosProcessExitException(codeValue.AsInt());
        }

        if (target == "sys.fs_readFile")
        {
            if (!runtime.Permissions.Contains("sys"))
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

        if (target == "sys.fs_fileExists")
        {
            if (!runtime.Permissions.Contains("sys"))
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

        if (target == "sys.str_utf8ByteCount")
        {
            if (!runtime.Permissions.Contains("sys"))
            {
                return AosValue.Unknown;
            }
            if (node.Children.Count != 1)
            {
                return AosValue.Unknown;
            }

            var textValue = EvalNode(node.Children[0], runtime, env);
            if (textValue.Kind != AosValueKind.String)
            {
                return AosValue.Unknown;
            }

            return AosValue.FromInt(System.Text.Encoding.UTF8.GetByteCount(textValue.AsString()));
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
                var vm = VmProgram.Load(bytecodeValue.AsNode());
                return VmRunner.Run(this, runtime, vm, entryValue.AsString(), argsValue.AsNode());
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
            var bundlePath = Path.Combine(publishDir, $"{projectName}.aibundle");
            var outputBinaryPath = Path.Combine(publishDir, projectName);
            var libraryPath = Path.Combine(publishDir, $"{projectName}.ailib");

            var manifestPath = Path.Combine(publishDir, "project.aiproj");
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
                            Directory.CreateDirectory(publishDir);
                            File.WriteAllText(libraryPath, AosFormatter.Format(canonicalProgram));
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
            var entryPath = Path.GetFullPath(Path.Combine(publishDir, entryFile));
            if (!File.Exists(entryPath))
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
                Directory.CreateDirectory(publishDir);
                File.WriteAllText(bundlePath, bytecodeText);

                var sourceBinary = ResolveHostBinaryPath();
                if (sourceBinary is null)
                {
                    return AosValue.FromNode(CreateErrNode("publish_err", "PUB004", "host executable not found.", node.Id, node.Span));
                }

                File.Copy(sourceBinary, outputBinaryPath, overwrite: true);
                File.AppendAllText(outputBinaryPath, "\n--AIBUNDLE1:BYTECODE--\n" + bytecodeText);
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
            if (Path.IsPathRooted(relativePath))
            {
                throw new VmRuntimeException("VM001", "Import path must be relative.", child.Id);
            }

            var fullPath = Path.GetFullPath(Path.Combine(moduleBaseDir, relativePath));
            if (!loading.Add(fullPath))
            {
                throw new VmRuntimeException("VM001", "Circular import detected.", child.Id);
            }
            if (!File.Exists(fullPath))
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

            var importedDir = Path.GetDirectoryName(fullPath) ?? moduleBaseDir;
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

    private static int ParseContentLength(string raw)
    {
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = trimmed[..colon].Trim();
            if (!string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed[(colon + 1)..].Trim();
            if (int.TryParse(value, out var parsed) && parsed > 0)
            {
                return parsed;
            }
            return 0;
        }

        return 0;
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

        if (!File.Exists(manifestPath))
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

            if (Path.IsPathRooted(includePath))
            {
                errNode = CreateErrNode("publish_err", "PUB012", "Include path must be relative.", includeNode.Id, includeNode.Span);
                return false;
            }

            var includeDir = Path.GetFullPath(Path.Combine(publishDir, includePath));
            var includeManifestPath = Path.Combine(includeDir, $"{includeName}.ailib");
            if (!File.Exists(includeManifestPath))
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

    private sealed class VmRuntimeException : Exception
    {
        public VmRuntimeException(string code, string message, string nodeId)
            : base(message)
        {
            Code = code;
            NodeId = nodeId;
        }

        public string Code { get; }
        public string NodeId { get; }
    }

    private sealed class VmInstruction
    {
        public required string Op { get; init; }
        public int A { get; init; }
        public int B { get; init; }
        public string S { get; init; } = string.Empty;
    }

    private sealed class VmFunction
    {
        public required string Name { get; init; }
        public required List<string> Params { get; init; }
        public required List<string> Locals { get; init; }
        public required List<VmInstruction> Instructions { get; init; }
    }

    private sealed class VmProgram
    {
        public required List<AosValue> Constants { get; init; }
        public required List<VmFunction> Functions { get; init; }
        public required Dictionary<string, int> FunctionIndexByName { get; init; }

        public static VmProgram Load(AosNode node)
        {
            if (node.Kind != "Bytecode")
            {
                throw new VmRuntimeException("VM001", "Expected Bytecode node.", node.Id);
            }

            if (!node.Attrs.TryGetValue("magic", out var magicAttr) || magicAttr.Kind != AosAttrKind.String || magicAttr.AsString() != "AIBC")
            {
                throw new VmRuntimeException("VM001", "Unsupported bytecode magic.", node.Id);
            }
            if (!node.Attrs.TryGetValue("format", out var formatAttr) || formatAttr.Kind != AosAttrKind.String || formatAttr.AsString() != "AiBC1")
            {
                throw new VmRuntimeException("VM001", "Unsupported bytecode format.", node.Id);
            }
            if (!node.Attrs.TryGetValue("version", out var versionAttr) || versionAttr.Kind != AosAttrKind.Int || versionAttr.AsInt() != 1)
            {
                throw new VmRuntimeException("VM001", "Unsupported bytecode version.", node.Id);
            }
            if (!node.Attrs.TryGetValue("flags", out var flagsAttr) || flagsAttr.Kind != AosAttrKind.Int)
            {
                throw new VmRuntimeException("VM001", "Invalid bytecode flags.", node.Id);
            }

            var constants = new List<AosValue>();
            var functions = new List<VmFunction>();
            foreach (var child in node.Children)
            {
                if (child.Kind == "Const")
                {
                    if (!child.Attrs.TryGetValue("kind", out var kindAttr) || kindAttr.Kind != AosAttrKind.Identifier)
                    {
                        throw new VmRuntimeException("VM001", "Invalid Const node.", child.Id);
                    }

                    var kind = kindAttr.AsString();
                    if (!child.Attrs.TryGetValue("value", out var valueAttr))
                    {
                        throw new VmRuntimeException("VM001", "Const missing value.", child.Id);
                    }

                    constants.Add(kind switch
                    {
                        "string" when valueAttr.Kind == AosAttrKind.String => AosValue.FromString(valueAttr.AsString()),
                        "int" when valueAttr.Kind == AosAttrKind.Int => AosValue.FromInt(valueAttr.AsInt()),
                        "bool" when valueAttr.Kind == AosAttrKind.Bool => AosValue.FromBool(valueAttr.AsBool()),
                        "node" when valueAttr.Kind == AosAttrKind.String => AosValue.FromNode(DecodeNodeConstant(valueAttr.AsString(), child.Id)),
                        "null" => AosValue.Unknown,
                        _ => throw new VmRuntimeException("VM001", "Unsupported constant kind.", child.Id)
                    });
                    continue;
                }

                if (child.Kind == "Func")
                {
                    if (!child.Attrs.TryGetValue("name", out var nameAttr) || nameAttr.Kind != AosAttrKind.Identifier)
                    {
                        throw new VmRuntimeException("VM001", "Func missing name.", child.Id);
                    }

                    var name = nameAttr.AsString();
                    var paramText = child.Attrs.TryGetValue("params", out var paramsAttr) && paramsAttr.Kind == AosAttrKind.String
                        ? paramsAttr.AsString()
                        : string.Empty;
                    var localText = child.Attrs.TryGetValue("locals", out var localsAttr) && localsAttr.Kind == AosAttrKind.String
                        ? localsAttr.AsString()
                        : string.Empty;
                    var parameters = SplitCsv(paramText);
                    var locals = SplitCsv(localText);
                    var instructions = new List<VmInstruction>(child.Children.Count);
                    foreach (var instNode in child.Children)
                    {
                        if (instNode.Kind != "Inst")
                        {
                            throw new VmRuntimeException("VM001", "Func contains non-instruction child.", instNode.Id);
                        }
                        if (!instNode.Attrs.TryGetValue("op", out var opAttr) || opAttr.Kind != AosAttrKind.Identifier)
                        {
                            throw new VmRuntimeException("VM001", "Instruction missing op.", instNode.Id);
                        }

                        instructions.Add(new VmInstruction
                        {
                            Op = opAttr.AsString(),
                            A = instNode.Attrs.TryGetValue("a", out var aAttr) && aAttr.Kind == AosAttrKind.Int ? aAttr.AsInt() : 0,
                            B = instNode.Attrs.TryGetValue("b", out var bAttr) && bAttr.Kind == AosAttrKind.Int ? bAttr.AsInt() : 0,
                            S = instNode.Attrs.TryGetValue("s", out var sAttr) && sAttr.Kind == AosAttrKind.String ? sAttr.AsString() : string.Empty
                        });
                    }

                    functions.Add(new VmFunction
                    {
                        Name = name,
                        Params = parameters,
                        Locals = locals,
                        Instructions = instructions
                    });
                    continue;
                }

                throw new VmRuntimeException("VM001", "Unsupported Bytecode section.", child.Id);
            }

            var functionIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < functions.Count; i++)
            {
                functionIndexByName[functions[i].Name] = i;
            }

            return new VmProgram
            {
                Constants = constants,
                Functions = functions,
                FunctionIndexByName = functionIndexByName
            };
        }

        private static List<string> SplitCsv(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new List<string>();
            }

            return text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }

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
                    foreach (var child in node.Children)
                    {
                        CompileStatement(context, state, child);
                    }
                    state.Emit("CONST", context.AddConstant(AosValue.Unknown));
                    return;
                default:
                    throw new VmRuntimeException("VM001", $"Unsupported construct in bytecode mode: {node.Kind}.", node.Id);
            }
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

    private static class VmRunner
    {
        public static AosValue Run(AosInterpreter interpreter, AosRuntime runtime, VmProgram vm, string entryName, AosNode argsNode)
        {
            if (!vm.FunctionIndexByName.TryGetValue(entryName, out var entryIndex))
            {
                throw new VmRuntimeException("VM001", $"Entry function not found: {entryName}.", entryName);
            }

            var args = new List<AosValue>();
            var entry = vm.Functions[entryIndex];
            if (entry.Params.Count == 1 && string.Equals(entry.Params[0], "argv", StringComparison.Ordinal))
            {
                args.Add(AosValue.FromNode(argsNode));
            }
            else
            {
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
                        continue;
                    }
                    args.Add(AosValue.FromNode(child));
                }
            }

            return ExecuteFunction(interpreter, runtime, vm, entryIndex, args);
        }

        private static AosValue ExecuteFunction(AosInterpreter interpreter, AosRuntime runtime, VmProgram vm, int functionIndex, List<AosValue> args)
        {
            if (functionIndex < 0 || functionIndex >= vm.Functions.Count)
            {
                throw new VmRuntimeException("VM001", "Invalid function index.", "vm");
            }
            var function = vm.Functions[functionIndex];
            if (args.Count != function.Params.Count)
            {
                throw new VmRuntimeException("VM001", $"Arity mismatch for {function.Name}.", function.Name);
            }

            var locals = new AosValue[function.Locals.Count];
            for (var i = 0; i < locals.Length; i++)
            {
                locals[i] = AosValue.Unknown;
            }
            for (var i = 0; i < function.Params.Count; i++)
            {
                locals[i] = args[i];
            }

            var stack = new List<AosValue>();
            var pc = 0;
            while (pc < function.Instructions.Count)
            {
                var inst = function.Instructions[pc];
                switch (inst.Op)
                {
                    case "CONST":
                        if (inst.A < 0 || inst.A >= vm.Constants.Count)
                        {
                            throw new VmRuntimeException("VM001", "Invalid CONST index.", function.Name);
                        }
                        stack.Add(vm.Constants[inst.A]);
                        pc++;
                        break;
                    case "LOAD_LOCAL":
                        if (inst.A < 0 || inst.A >= locals.Length)
                        {
                            throw new VmRuntimeException("VM001", "Invalid local slot.", function.Name);
                        }
                        stack.Add(locals[inst.A]);
                        pc++;
                        break;
                    case "STORE_LOCAL":
                        if (inst.A < 0 || inst.A >= locals.Length)
                        {
                            throw new VmRuntimeException("VM001", "Invalid local slot.", function.Name);
                        }
                        locals[inst.A] = Pop(stack, function.Name);
                        pc++;
                        break;
                    case "POP":
                        _ = Pop(stack, function.Name);
                        pc++;
                        break;
                    case "EQ":
                    {
                        var right = Pop(stack, function.Name);
                        var left = Pop(stack, function.Name);
                        var equals = left.Kind == right.Kind && left.Kind switch
                        {
                            AosValueKind.String => left.AsString() == right.AsString(),
                            AosValueKind.Int => left.AsInt() == right.AsInt(),
                            AosValueKind.Bool => left.AsBool() == right.AsBool(),
                            AosValueKind.Unknown => true,
                            _ => false
                        };
                        stack.Add(AosValue.FromBool(equals));
                        pc++;
                        break;
                    }
                    case "ADD_INT":
                    {
                        var right = Pop(stack, function.Name);
                        var left = Pop(stack, function.Name);
                        if (left.Kind != AosValueKind.Int || right.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "ADD_INT requires int operands.", function.Name);
                        }
                        stack.Add(AosValue.FromInt(left.AsInt() + right.AsInt()));
                        pc++;
                        break;
                    }
                    case "STR_CONCAT":
                    {
                        var right = Pop(stack, function.Name);
                        var left = Pop(stack, function.Name);
                        if (left.Kind != AosValueKind.String || right.Kind != AosValueKind.String)
                        {
                            throw new VmRuntimeException("VM001", "STR_CONCAT requires string operands.", function.Name);
                        }
                        stack.Add(AosValue.FromString(left.AsString() + right.AsString()));
                        pc++;
                        break;
                    }
                    case "TO_STRING":
                    {
                        var value = Pop(stack, function.Name);
                        stack.Add(AosValue.FromString(ValueToDisplayString(value)));
                        pc++;
                        break;
                    }
                    case "STR_ESCAPE":
                    {
                        var value = Pop(stack, function.Name);
                        if (value.Kind != AosValueKind.String)
                        {
                            throw new VmRuntimeException("VM001", "STR_ESCAPE requires string operand.", function.Name);
                        }
                        stack.Add(AosValue.FromString(EscapeString(value.AsString())));
                        pc++;
                        break;
                    }
                    case "NODE_KIND":
                    {
                        var value = Pop(stack, function.Name);
                        if (value.Kind != AosValueKind.Node)
                        {
                            throw new VmRuntimeException("VM001", "NODE_KIND requires node operand.", function.Name);
                        }
                        stack.Add(AosValue.FromString(value.AsNode().Kind));
                        pc++;
                        break;
                    }
                    case "NODE_ID":
                    {
                        var value = Pop(stack, function.Name);
                        if (value.Kind != AosValueKind.Node)
                        {
                            throw new VmRuntimeException("VM001", "NODE_ID requires node operand.", function.Name);
                        }
                        stack.Add(AosValue.FromString(value.AsNode().Id));
                        pc++;
                        break;
                    }
                    case "ATTR_COUNT":
                    {
                        var value = Pop(stack, function.Name);
                        if (value.Kind != AosValueKind.Node)
                        {
                            throw new VmRuntimeException("VM001", "ATTR_COUNT requires node operand.", function.Name);
                        }
                        stack.Add(AosValue.FromInt(value.AsNode().Attrs.Count));
                        pc++;
                        break;
                    }
                    case "ATTR_KEY":
                    {
                        var indexValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "ATTR_KEY requires (node,int).", function.Name);
                        }
                        var attrs = nodeValue.AsNode().Attrs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                        var index = indexValue.AsInt();
                        stack.Add(AosValue.FromString(index >= 0 && index < attrs.Count ? attrs[index].Key : string.Empty));
                        pc++;
                        break;
                    }
                    case "ATTR_VALUE_KIND":
                    {
                        var indexValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "ATTR_VALUE_KIND requires (node,int).", function.Name);
                        }
                        var attrs = nodeValue.AsNode().Attrs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                        var index = indexValue.AsInt();
                        var kind = index >= 0 && index < attrs.Count ? attrs[index].Value.Kind.ToString().ToLowerInvariant() : string.Empty;
                        stack.Add(AosValue.FromString(kind));
                        pc++;
                        break;
                    }
                    case "ATTR_VALUE_STRING":
                    {
                        var indexValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "ATTR_VALUE_STRING requires (node,int).", function.Name);
                        }
                        var attrs = nodeValue.AsNode().Attrs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                        var index = indexValue.AsInt();
                        if (index < 0 || index >= attrs.Count)
                        {
                            stack.Add(AosValue.FromString(string.Empty));
                        }
                        else
                        {
                            var attr = attrs[index].Value;
                            stack.Add(attr.Kind == AosAttrKind.String || attr.Kind == AosAttrKind.Identifier
                                ? AosValue.FromString(attr.AsString())
                                : AosValue.FromString(string.Empty));
                        }
                        pc++;
                        break;
                    }
                    case "ATTR_VALUE_INT":
                    {
                        var indexValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "ATTR_VALUE_INT requires (node,int).", function.Name);
                        }
                        var attrs = nodeValue.AsNode().Attrs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                        var index = indexValue.AsInt();
                        if (index < 0 || index >= attrs.Count || attrs[index].Value.Kind != AosAttrKind.Int)
                        {
                            stack.Add(AosValue.FromInt(0));
                        }
                        else
                        {
                            stack.Add(AosValue.FromInt(attrs[index].Value.AsInt()));
                        }
                        pc++;
                        break;
                    }
                    case "ATTR_VALUE_BOOL":
                    {
                        var indexValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "ATTR_VALUE_BOOL requires (node,int).", function.Name);
                        }
                        var attrs = nodeValue.AsNode().Attrs.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                        var index = indexValue.AsInt();
                        if (index < 0 || index >= attrs.Count || attrs[index].Value.Kind != AosAttrKind.Bool)
                        {
                            stack.Add(AosValue.FromBool(false));
                        }
                        else
                        {
                            stack.Add(AosValue.FromBool(attrs[index].Value.AsBool()));
                        }
                        pc++;
                        break;
                    }
                    case "CHILD_COUNT":
                    {
                        var value = Pop(stack, function.Name);
                        if (value.Kind != AosValueKind.Node)
                        {
                            throw new VmRuntimeException("VM001", "CHILD_COUNT requires node operand.", function.Name);
                        }
                        stack.Add(AosValue.FromInt(value.AsNode().Children.Count));
                        pc++;
                        break;
                    }
                    case "CHILD_AT":
                    {
                        var indexValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || indexValue.Kind != AosValueKind.Int)
                        {
                            throw new VmRuntimeException("VM001", "CHILD_AT requires (node,int).", function.Name);
                        }
                        var children = nodeValue.AsNode().Children;
                        var index = indexValue.AsInt();
                        if (index < 0 || index >= children.Count)
                        {
                            throw new VmRuntimeException("VM001", "CHILD_AT index out of range.", function.Name);
                        }
                        stack.Add(AosValue.FromNode(children[index]));
                        pc++;
                        break;
                    }
                    case "MAKE_BLOCK":
                    {
                        var idValue = Pop(stack, function.Name);
                        if (idValue.Kind != AosValueKind.String)
                        {
                            throw new VmRuntimeException("VM001", "MAKE_BLOCK requires string id.", function.Name);
                        }
                        stack.Add(AosValue.FromNode(new AosNode(
                            "Block",
                            idValue.AsString(),
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                            new List<AosNode>(),
                            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))));
                        pc++;
                        break;
                    }
                    case "APPEND_CHILD":
                    {
                        var childValue = Pop(stack, function.Name);
                        var nodeValue = Pop(stack, function.Name);
                        if (nodeValue.Kind != AosValueKind.Node || childValue.Kind != AosValueKind.Node)
                        {
                            throw new VmRuntimeException("VM001", "APPEND_CHILD requires (node,node).", function.Name);
                        }
                        var baseNode = nodeValue.AsNode();
                        var children = new List<AosNode>(baseNode.Children) { childValue.AsNode() };
                        stack.Add(AosValue.FromNode(new AosNode(
                            baseNode.Kind,
                            baseNode.Id,
                            new Dictionary<string, AosAttrValue>(baseNode.Attrs, StringComparer.Ordinal),
                            children,
                            baseNode.Span)));
                        pc++;
                        break;
                    }
                    case "MAKE_ERR":
                    {
                        var nodeIdValue = Pop(stack, function.Name);
                        var messageValue = Pop(stack, function.Name);
                        var codeValue = Pop(stack, function.Name);
                        var idValue = Pop(stack, function.Name);
                        if (idValue.Kind != AosValueKind.String ||
                            codeValue.Kind != AosValueKind.String ||
                            messageValue.Kind != AosValueKind.String ||
                            nodeIdValue.Kind != AosValueKind.String)
                        {
                            throw new VmRuntimeException("VM001", "MAKE_ERR requires (string,string,string,string).", function.Name);
                        }
                        stack.Add(AosValue.FromNode(CreateErrNode(
                            idValue.AsString(),
                            codeValue.AsString(),
                            messageValue.AsString(),
                            nodeIdValue.AsString(),
                            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))));
                        pc++;
                        break;
                    }
                    case "MAKE_LIT_STRING":
                    {
                        var textValue = Pop(stack, function.Name);
                        var idValue = Pop(stack, function.Name);
                        if (idValue.Kind != AosValueKind.String || textValue.Kind != AosValueKind.String)
                        {
                            throw new VmRuntimeException("VM001", "MAKE_LIT_STRING requires (string,string).", function.Name);
                        }
                        stack.Add(AosValue.FromNode(new AosNode(
                            "Lit",
                            idValue.AsString(),
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                            {
                                ["value"] = new AosAttrValue(AosAttrKind.String, textValue.AsString())
                            },
                            new List<AosNode>(),
                            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)))));
                        pc++;
                        break;
                    }
                    case "MAKE_NODE":
                    {
                        if (inst.A < 0 || inst.A >= vm.Constants.Count)
                        {
                            throw new VmRuntimeException("VM001", "Invalid MAKE_NODE template index.", function.Name);
                        }
                        var templateValue = vm.Constants[inst.A];
                        if (templateValue.Kind != AosValueKind.Node)
                        {
                            throw new VmRuntimeException("VM001", "MAKE_NODE template must be node constant.", function.Name);
                        }
                        var argsForNode = PopArgs(stack, inst.B, function.Name);
                        var template = templateValue.AsNode();
                        var children = new List<AosNode>(argsForNode.Count);
                        foreach (var arg in argsForNode)
                        {
                            children.Add(ToRuntimeNode(arg));
                        }
                        stack.Add(AosValue.FromNode(new AosNode(
                            template.Kind,
                            template.Id,
                            new Dictionary<string, AosAttrValue>(template.Attrs, StringComparer.Ordinal),
                            children,
                            template.Span)));
                        pc++;
                        break;
                    }
                    case "JUMP":
                        pc = inst.A;
                        break;
                    case "JUMP_IF_FALSE":
                    {
                        var cond = Pop(stack, function.Name);
                        if (cond.Kind != AosValueKind.Bool)
                        {
                            throw new VmRuntimeException("VM001", "JUMP_IF_FALSE requires bool.", function.Name);
                        }
                        pc = cond.AsBool() ? pc + 1 : inst.A;
                        break;
                    }
                    case "CALL":
                    {
                        var argc = inst.B;
                        var callArgs = PopArgs(stack, argc, function.Name);
                        var result = ExecuteFunction(interpreter, runtime, vm, inst.A, callArgs);
                        stack.Add(result);
                        pc++;
                        break;
                    }
                    case "CALL_SYS":
                    {
                        var argc = inst.A;
                        var callArgs = PopArgs(stack, argc, function.Name);
                        var result = ExecuteCall(interpreter, runtime, inst.S, callArgs);
                        stack.Add(result);
                        pc++;
                        break;
                    }
                    case "RETURN":
                        return stack.Count == 0 ? AosValue.Void : Pop(stack, function.Name);
                    default:
                        throw new VmRuntimeException("VM001", $"Unsupported opcode: {inst.Op}.", function.Name);
                }
            }

            return AosValue.Void;
        }

        private static AosValue ExecuteCall(AosInterpreter interpreter, AosRuntime runtime, string target, List<AosValue> args)
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
            var env = new Dictionary<string, AosValue>(runtime.Env, StringComparer.Ordinal);
            var value = interpreter.EvalCall(callNode, runtime, env);
            if (value.Kind == AosValueKind.Unknown)
            {
                throw new VmRuntimeException("VM001", $"Unsupported call target in bytecode mode: {target}.", target);
            }
            return value;
        }

        private static AosValue Pop(List<AosValue> stack, string nodeId)
        {
            if (stack.Count == 0)
            {
                throw new VmRuntimeException("VM001", "Stack underflow.", nodeId);
            }
            var index = stack.Count - 1;
            var value = stack[index];
            stack.RemoveAt(index);
            return value;
        }

        private static List<AosValue> PopArgs(List<AosValue> stack, int argc, string nodeId)
        {
            if (argc < 0 || stack.Count < argc)
            {
                throw new VmRuntimeException("VM001", "Invalid call argument count.", nodeId);
            }

            var args = new List<AosValue>(argc);
            for (var i = 0; i < argc; i++)
            {
                args.Add(Pop(stack, nodeId));
            }
            args.Reverse();
            return args;
        }
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
