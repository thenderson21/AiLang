using AiLang.Core;

internal static class CliAdapters
{
    public static IEventSource CreateEventSource(AosValue argValue)
    {
        if (argValue.Kind == AosValueKind.Node)
        {
            var argvNode = argValue.AsNode();
            if (TryGetArgString(argvNode, 0, out var marker) &&
                string.Equals(marker, "__event_message", StringComparison.Ordinal) &&
                TryGetArgString(argvNode, 1, out var type) &&
                TryGetArgString(argvNode, 2, out var payload))
            {
                return new MessageOnceEventSource(type, payload);
            }
        }

        return new StartOnlyEventSource();
    }

    private static bool TryGetArgString(AosNode argvNode, int index, out string value)
    {
        value = string.Empty;
        if (index < 0 || index >= argvNode.Children.Count)
        {
            return false;
        }

        var child = argvNode.Children[index];
        if (!string.Equals(child.Kind, "Lit", StringComparison.Ordinal))
        {
            return false;
        }

        if (!child.Attrs.TryGetValue("value", out var attr) || attr.Kind != AosAttrKind.String)
        {
            return false;
        }

        value = attr.AsString();
        return true;
    }
}

internal interface IEventSource
{
    AosNode? NextEvent();
}

internal sealed class StartOnlyEventSource : IEventSource
{
    private bool _dispatched;

    public AosNode? NextEvent()
    {
        if (_dispatched)
        {
            return null;
        }

        _dispatched = true;
        return new AosNode(
            "Event",
            "Start",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    }
}

internal sealed class MessageOnceEventSource : IEventSource
{
    private readonly string _type;
    private readonly string _payload;
    private bool _dispatched;

    public MessageOnceEventSource(string type, string payload)
    {
        _type = type;
        _payload = payload;
    }

    public AosNode? NextEvent()
    {
        if (_dispatched)
        {
            return null;
        }

        _dispatched = true;
        return new AosNode(
            "Event",
            "Message",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["type"] = new AosAttrValue(AosAttrKind.String, _type),
                ["payload"] = new AosAttrValue(AosAttrKind.String, _payload)
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    }
}

internal sealed class FutureWebEventSource : IEventSource
{
    public AosNode? NextEvent() => null;
}

internal interface ICommandExecutor
{
    int? Execute(AosNode command);
}

internal sealed class CliCommandExecutor : ICommandExecutor
{
    public int? Execute(AosNode command)
    {
        if (command.Kind != "Command")
        {
            return null;
        }

        if (command.Id == "Exit" &&
            command.Attrs.TryGetValue("code", out var codeAttr) &&
            codeAttr.Kind == AosAttrKind.Int)
        {
            return codeAttr.AsInt();
        }

        if (command.Id == "Print" &&
            command.Attrs.TryGetValue("text", out var textAttr) &&
            textAttr.Kind == AosAttrKind.String)
        {
            Console.WriteLine(textAttr.AsString());
            return null;
        }

        if (command.Id == "Emit" &&
            command.Attrs.TryGetValue("type", out var emitTypeAttr) &&
            emitTypeAttr.Kind == AosAttrKind.String &&
            command.Attrs.TryGetValue("payload", out var emitPayloadAttr) &&
            emitPayloadAttr.Kind == AosAttrKind.String)
        {
            if (string.Equals(emitTypeAttr.AsString(), "stdout", StringComparison.Ordinal))
            {
                Console.WriteLine(emitPayloadAttr.AsString());
            }
        }

        return null;
    }
}

internal sealed class FutureWebCommandExecutor : ICommandExecutor
{
    public int? Execute(AosNode command) => null;
}

internal sealed class ServeCommandExecutor : ICommandExecutor
{
    public string? HttpResponsePayload { get; private set; }
    public bool ExitRequested { get; private set; }

    public void Reset()
    {
        HttpResponsePayload = null;
        ExitRequested = false;
    }

    public int? Execute(AosNode command)
    {
        if (command.Kind != "Command")
        {
            return null;
        }

        if (command.Id == "Exit" &&
            command.Attrs.TryGetValue("code", out var codeAttr) &&
            codeAttr.Kind == AosAttrKind.Int)
        {
            ExitRequested = true;
            return codeAttr.AsInt();
        }

        if (command.Id == "Print" &&
            command.Attrs.TryGetValue("text", out var textAttr) &&
            textAttr.Kind == AosAttrKind.String)
        {
            Console.WriteLine(textAttr.AsString());
            return null;
        }

        if (command.Id == "Emit" &&
            command.Attrs.TryGetValue("type", out var emitTypeAttr) &&
            emitTypeAttr.Kind == AosAttrKind.String &&
            command.Attrs.TryGetValue("payload", out var emitPayloadAttr) &&
            emitPayloadAttr.Kind == AosAttrKind.String)
        {
            var emitType = emitTypeAttr.AsString();
            if (string.Equals(emitType, "stdout", StringComparison.Ordinal))
            {
                Console.WriteLine(emitPayloadAttr.AsString());
            }
            else if (string.Equals(emitType, "http.response", StringComparison.Ordinal))
            {
                HttpResponsePayload = emitPayloadAttr.AsString();
            }
        }

        return null;
    }
}
