namespace AiVM.Core;

public static class VmSyscalls
{
    public static ISyscallHost Host { get; set; } = new DefaultSyscallHost();

    public static string[] ProcessArgv()
    {
        return Host.ProcessArgv();
    }

    public static string ProcessEnvGet(string name)
    {
        return Host.ProcessEnvGet(name);
    }

    public static int TimeNowUnixMs()
    {
        return Host.TimeNowUnixMs();
    }

    public static int TimeMonotonicMs()
    {
        return Host.TimeMonotonicMs();
    }

    public static void TimeSleepMs(int ms)
    {
        Host.TimeSleepMs(ms);
    }

    public static void ConsoleWriteErrLine(string text)
    {
        Host.ConsoleWriteErrLine(text);
    }

    public static void ConsoleWrite(string text)
    {
        Host.ConsoleWrite(text);
    }

    public static string ProcessCwd()
    {
        return Host.ProcessCwd();
    }

    public static void ConsolePrintLine(string text)
    {
        Host.ConsolePrintLine(text);
    }

    public static void IoPrint(string text)
    {
        Host.IoPrint(text);
    }

    public static void IoWrite(string text)
    {
        Host.IoWrite(text);
    }

    public static string IoReadLine()
    {
        return Host.IoReadLine();
    }

    public static string IoReadAllStdin()
    {
        return Host.IoReadAllStdin();
    }

    public static string IoReadFile(string path)
    {
        return Host.IoReadFile(path);
    }

    public static bool IoFileExists(string path)
    {
        return Host.IoFileExists(path);
    }

    public static bool IoPathExists(string path)
    {
        return Host.IoPathExists(path);
    }

    public static void IoMakeDir(string path)
    {
        Host.IoMakeDir(path);
    }

    public static void IoWriteFile(string path, string text)
    {
        Host.IoWriteFile(path, text);
    }

    public static string FsReadFile(string path)
    {
        return Host.FsReadFile(path);
    }

    public static bool FsFileExists(string path)
    {
        return Host.FsFileExists(path);
    }

    public static string[] FsReadDir(string path)
    {
        var entries = Host.FsReadDir(path);
        Array.Sort(entries, StringComparer.Ordinal);
        return entries;
    }

    public static VmFsStat FsStat(string path)
    {
        return Host.FsStat(path);
    }

    public static bool FsPathExists(string path)
    {
        return Host.FsPathExists(path);
    }

    public static void FsWriteFile(string path, string text)
    {
        Host.FsWriteFile(path, text);
    }

    public static void FsMakeDir(string path)
    {
        Host.FsMakeDir(path);
    }

    public static int StrUtf8ByteCount(string text)
    {
        return Host.StrUtf8ByteCount(text);
    }

    public static string StrSubstring(string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return string.Empty;
        }

        var offsets = BuildRuneOffsets(text);
        var runeCount = offsets.Length - 1;
        var clampedStart = Math.Max(0, Math.Min(start, runeCount));
        var clampedEnd = Math.Max(clampedStart, Math.Min(runeCount, clampedStart + length));
        var startOffset = offsets[clampedStart];
        var endOffset = offsets[clampedEnd];
        return text.Substring(startOffset, endOffset - startOffset);
    }

    public static string StrRemove(string text, int start, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return text ?? string.Empty;
        }

        var offsets = BuildRuneOffsets(text);
        var runeCount = offsets.Length - 1;
        var clampedStart = Math.Max(0, Math.Min(start, runeCount));
        var clampedEnd = Math.Max(clampedStart, Math.Min(runeCount, clampedStart + length));
        if (clampedStart == clampedEnd)
        {
            return text;
        }

        var startOffset = offsets[clampedStart];
        var endOffset = offsets[clampedEnd];
        return string.Concat(text.AsSpan(0, startOffset), text.AsSpan(endOffset));
    }

    public static string Platform()
    {
        return Host.Platform();
    }

    public static string Architecture()
    {
        return Host.Architecture();
    }

    public static string OsVersion()
    {
        return Host.OsVersion();
    }

    public static string Runtime()
    {
        return Host.Runtime();
    }

    public static int NetListen(VmNetworkState state, int port)
    {
        return Host.NetListen(state, port);
    }

    public static int NetListenTls(VmNetworkState state, int port, string certPath, string keyPath)
    {
        return Host.NetListenTls(state, port, certPath, keyPath);
    }

    public static int NetAccept(VmNetworkState state, int listenerHandle)
    {
        return Host.NetAccept(state, listenerHandle);
    }

    public static string NetReadHeaders(VmNetworkState state, int connectionHandle)
    {
        return Host.NetReadHeaders(state, connectionHandle);
    }

    public static bool NetWrite(VmNetworkState state, int connectionHandle, string text)
    {
        return Host.NetWrite(state, connectionHandle, text);
    }

    public static void NetClose(VmNetworkState state, int handle)
    {
        Host.NetClose(state, handle);
    }

    public static int NetTcpListen(VmNetworkState state, string host, int port)
    {
        return Host.NetTcpListen(state, host, port);
    }

    public static int NetTcpListenTls(VmNetworkState state, string host, int port, string certPath, string keyPath)
    {
        return Host.NetTcpListenTls(state, host, port, certPath, keyPath);
    }

    public static int NetTcpConnect(VmNetworkState state, string host, int port)
    {
        return Host.NetTcpConnect(state, host, port);
    }

    public static int NetTcpAccept(VmNetworkState state, int listenerHandle)
    {
        return Host.NetTcpAccept(state, listenerHandle);
    }

    public static string NetTcpRead(VmNetworkState state, int connectionHandle, int maxBytes)
    {
        return Host.NetTcpRead(state, connectionHandle, maxBytes);
    }

    public static int NetTcpWrite(VmNetworkState state, int connectionHandle, string data)
    {
        return Host.NetTcpWrite(state, connectionHandle, data);
    }

    public static int NetUdpBind(VmNetworkState state, string host, int port)
    {
        return Host.NetUdpBind(state, host, port);
    }

    public static VmUdpPacket NetUdpRecv(VmNetworkState state, int handle, int maxBytes)
    {
        return Host.NetUdpRecv(state, handle, maxBytes);
    }

    public static int NetUdpSend(VmNetworkState state, int handle, string host, int port, string data)
    {
        return Host.NetUdpSend(state, handle, host, port, data);
    }

    public static int UiCreateWindow(string title, int width, int height)
    {
        return Host.UiCreateWindow(title, width, height);
    }

    public static void UiBeginFrame(int windowHandle)
    {
        Host.UiBeginFrame(windowHandle);
    }

    public static void UiDrawRect(int windowHandle, int x, int y, int width, int height, string color)
    {
        Host.UiDrawRect(windowHandle, x, y, width, height, color);
    }

    public static void UiDrawText(int windowHandle, int x, int y, string text, string color, int size)
    {
        Host.UiDrawText(windowHandle, x, y, text, color, size);
    }

    public static void UiDrawLine(int windowHandle, int x1, int y1, int x2, int y2, string color, int strokeWidth)
    {
        Host.UiDrawLine(windowHandle, x1, y1, x2, y2, color, strokeWidth);
    }

    public static void UiDrawEllipse(int windowHandle, int x, int y, int width, int height, string color)
    {
        Host.UiDrawEllipse(windowHandle, x, y, width, height, color);
    }

    public static void UiDrawPath(int windowHandle, string path, string color, int strokeWidth)
    {
        Host.UiDrawPath(windowHandle, path, color, strokeWidth);
    }

    public static void UiDrawImage(int windowHandle, int x, int y, int width, int height, string rgbaBase64)
    {
        Host.UiDrawImage(windowHandle, x, y, width, height, rgbaBase64);
    }

    public static void UiEndFrame(int windowHandle)
    {
        Host.UiEndFrame(windowHandle);
    }

    public static VmUiEvent UiPollEvent(int windowHandle)
    {
        return CanonicalizeUiEvent(Host.UiPollEvent(windowHandle));
    }

    public static VmUiWindowSize UiGetWindowSize(int windowHandle)
    {
        var size = Host.UiGetWindowSize(windowHandle);
        return new VmUiWindowSize(size.Width < 0 ? -1 : size.Width, size.Height < 0 ? -1 : size.Height);
    }

    public static void UiPresent(int windowHandle)
    {
        Host.UiPresent(windowHandle);
    }

    public static void UiCloseWindow(int windowHandle)
    {
        Host.UiCloseWindow(windowHandle);
    }

    public static string CryptoBase64Encode(string text)
    {
        return Host.CryptoBase64Encode(text);
    }

    public static string CryptoBase64Decode(string text)
    {
        return Host.CryptoBase64Decode(text);
    }

    public static string CryptoSha1(string text)
    {
        return Host.CryptoSha1(text);
    }

    public static string CryptoSha256(string text)
    {
        return Host.CryptoSha256(text);
    }

    public static string CryptoHmacSha256(string key, string text)
    {
        return Host.CryptoHmacSha256(key, text);
    }

    public static string CryptoRandomBytes(int count)
    {
        return Host.CryptoRandomBytes(count);
    }

    public static void StdoutWriteLine(string text)
    {
        Host.StdoutWriteLine(text);
    }

    public static void ProcessExit(int code)
    {
        Host.ProcessExit(code);
    }

    private static VmUiEvent CanonicalizeUiEvent(VmUiEvent value)
    {
        var type = value.Type switch
        {
            "none" => "none",
            "closed" => "closed",
            "click" => "click",
            "key" => "key",
            _ => "none"
        };

        var targetId = value.TargetId ?? string.Empty;
        var key = CanonicalizeKeyToken(value.Key ?? string.Empty, value.Text ?? string.Empty);
        var text = value.Text ?? string.Empty;
        var modifiers = CanonicalizeModifiers(value.Modifiers);
        var repeat = value.Repeat;
        var x = value.X;
        var y = value.Y;

        if (!string.Equals(type, "click", StringComparison.Ordinal))
        {
            x = -1;
            y = -1;
        }

        if (!string.Equals(type, "key", StringComparison.Ordinal))
        {
            key = string.Empty;
            text = string.Empty;
            modifiers = string.Empty;
            repeat = false;
        }

        if (string.Equals(type, "none", StringComparison.Ordinal) ||
            string.Equals(type, "closed", StringComparison.Ordinal))
        {
            targetId = string.Empty;
        }

        return new VmUiEvent(type, targetId, x, y, key, text, modifiers, repeat);
    }

    private static string CanonicalizeKeyToken(string key, string text)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.ToLowerInvariant() switch
            {
                "return" => "enter",
                "esc" => "escape",
                "arrowleft" => "left",
                "arrowright" => "right",
                "arrowup" => "up",
                "arrowdown" => "down",
                _ => key.ToLowerInvariant()
            };
        }

        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length != 1)
        {
            return string.Empty;
        }

        var ch = text[0];
        if (char.IsControl(ch))
        {
            return string.Empty;
        }

        if (ch == ' ')
        {
            return "space";
        }

        return char.ToLowerInvariant(ch).ToString();
    }

    private static int[] BuildRuneOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            index += rune.Utf16SequenceLength;
            offsets.Add(index);
        }

        return offsets.ToArray();
    }

    private static string CanonicalizeModifiers(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in parsed)
        {
            if (token is "alt" or "ctrl" or "meta" or "shift")
            {
                seen.Add(token);
            }
        }

        if (seen.Count == 0)
        {
            return string.Empty;
        }

        var ordered = seen.OrderBy(static m => m, StringComparer.Ordinal);
        return string.Join(',', ordered);
    }
}
