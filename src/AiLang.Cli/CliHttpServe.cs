using AiLang.Core;
using System.Net.Sockets;
using System.Text;

internal static class CliHttpServe
{
    public static bool TryParseServeOptions(string[] args, out int port, out string[] appArgs)
    {
        port = 8080;
        var collected = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--port", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out var parsedPort) || parsedPort < 0 || parsedPort > 65535)
                {
                    appArgs = Array.Empty<string>();
                    return false;
                }

                port = parsedPort;
                i++;
                continue;
            }

            collected.Add(args[i]);
        }

        appArgs = collected.ToArray();
        return true;
    }

    public static AosNode CreateHttpRequestEvent(string method, string requestPath)
    {
        return new AosNode(
            "Event",
            "Message",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["type"] = new AosAttrValue(AosAttrKind.String, "http.request"),
                ["payload"] = new AosAttrValue(AosAttrKind.String, $"{method} {requestPath}")
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
    }

    public static void AppendEventDispatchTrace(AosRuntime runtime, AosNode eventNode)
    {
        runtime.TraceSteps.Add(new AosNode(
            "Step",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["kind"] = new AosAttrValue(AosAttrKind.String, "EventDispatch"),
                ["nodeId"] = new AosAttrValue(AosAttrKind.String, eventNode.Id)
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
    }

    public static void AppendCommandExecuteTrace(AosRuntime runtime, AosNode command)
    {
        runtime.TraceSteps.Add(new AosNode(
            "Step",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["kind"] = new AosAttrValue(AosAttrKind.String, "CommandExecute"),
                ["nodeId"] = new AosAttrValue(AosAttrKind.String, command.Id)
            },
            new List<AosNode>(),
            new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0))));
    }

    public static bool TryReadHttpRequestLine(NetworkStream stream, out string method, out string path)
    {
        method = string.Empty;
        path = string.Empty;

        var bytes = new List<byte>(1024);
        var endSeen = 0;
        var pattern = new byte[] { 13, 10, 13, 10 };
        while (bytes.Count < 16384)
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

        var text = Encoding.ASCII.GetString(bytes.ToArray());
        var firstLineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
        var firstLine = firstLineEnd >= 0 ? text[..firstLineEnd] : text;
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        method = parts[0];
        path = parts[1];
        return true;
    }

    public static void WriteHttpResponse(NetworkStream stream, string? payload)
    {
        var body = payload ?? string.Empty;
        string response;
        if (payload is null)
        {
            response = "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n";
        }
        else
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            response =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/plain; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "\r\n" +
                body;
        }

        var output = Encoding.UTF8.GetBytes(response);
        stream.Write(output, 0, output.Length);
    }
}
