using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Globalization;
using System.ComponentModel;
using System.Threading;

namespace AiVM.Core;

public partial class DefaultSyscallHost : ISyscallHost
{
    private static readonly string[] EmptyArgv = Array.Empty<string>();
    private static readonly Stopwatch MonotonicStopwatch = Stopwatch.StartNew();
    private int _nextUiHandle = 1;
    private readonly HashSet<int> _openWindows = new();
    private readonly LinuxX11UiBackend? _linuxUi = OperatingSystem.IsLinux() ? new LinuxX11UiBackend() : null;
    private readonly WindowsWin32UiBackend? _windowsUi = OperatingSystem.IsWindows() ? new WindowsWin32UiBackend() : null;
    private readonly MacOsScriptUiBackend? _macUi = OperatingSystem.IsMacOS() ? new MacOsScriptUiBackend() : null;

    public virtual string[] ProcessArgv() => EmptyArgv;

    public virtual int TimeNowUnixMs() => unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public virtual int TimeMonotonicMs() => unchecked((int)MonotonicStopwatch.ElapsedMilliseconds);

    public virtual void TimeSleepMs(int ms) => Thread.Sleep(ms);

    public virtual void ConsoleWriteErrLine(string text) => Console.Error.WriteLine(text);

    public virtual void ConsoleWrite(string text) => Console.Write(text);

    public virtual string ProcessCwd() => Directory.GetCurrentDirectory();

    public virtual string ProcessEnvGet(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;

    public virtual void ConsolePrintLine(string text) => Console.WriteLine(text);

    public virtual void IoPrint(string text) => Console.WriteLine(text);

    public virtual void IoWrite(string text) => Console.Write(text);

    public virtual string IoReadLine() => Console.ReadLine() ?? string.Empty;

    public virtual string IoReadAllStdin() => Console.In.ReadToEnd();

    public virtual string IoReadFile(string path) => File.ReadAllText(path);

    public virtual bool IoFileExists(string path) => File.Exists(path);

    public virtual bool IoPathExists(string path) => File.Exists(path) || Directory.Exists(path);

    public virtual void IoMakeDir(string path) => Directory.CreateDirectory(path);

    public virtual void IoWriteFile(string path, string text) => File.WriteAllText(path, text);

    public virtual string FsReadFile(string path) => File.ReadAllText(path);

    public virtual bool FsFileExists(string path) => File.Exists(path);

    public virtual string[] FsReadDir(string path)
    {
        if (!Directory.Exists(path))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetFileSystemEntries(path)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToArray();
    }

    public virtual VmFsStat FsStat(string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            return new VmFsStat(
                "file",
                unchecked((int)fileInfo.Length),
                unchecked((int)new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds()));
        }

        if (Directory.Exists(path))
        {
            var directoryInfo = new DirectoryInfo(path);
            return new VmFsStat(
                "dir",
                0,
                unchecked((int)new DateTimeOffset(directoryInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds()));
        }

        return new VmFsStat("missing", 0, 0);
    }

    public virtual bool FsPathExists(string path) => File.Exists(path) || Directory.Exists(path);

    public virtual void FsWriteFile(string path, string text) => File.WriteAllText(path, text);

    public virtual void FsMakeDir(string path) => Directory.CreateDirectory(path);

    public virtual int StrUtf8ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    public virtual string CryptoBase64Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    public virtual string CryptoBase64Decode(string text)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(text));
        }
        catch
        {
            return string.Empty;
        }
    }

    public virtual string CryptoSha1(string text)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public virtual string CryptoSha256(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public virtual string CryptoHmacSha256(string key, string text)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public virtual string CryptoRandomBytes(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        var bytes = new byte[count];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public virtual string Platform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return "unknown";
    }

    public virtual string Architecture()
    {
        return RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
    }

    public virtual string OsVersion()
    {
        return RuntimeInformation.OSDescription;
    }

    public virtual string Runtime()
    {
        return "airun-dotnet";
    }

    public virtual int NetListen(VmNetworkState state, int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var handle = state.NextNetHandle++;
        state.NetListeners[handle] = listener;
        return handle;
    }

    public virtual int NetListenTls(VmNetworkState state, int port, string certPath, string keyPath)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        }
        catch
        {
            return -1;
        }

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var handle = state.NextNetHandle++;
        state.NetListeners[handle] = listener;
        state.NetTlsCertificates[handle] = certificate;
        return handle;
    }

    public virtual int NetAccept(VmNetworkState state, int listenerHandle)
    {
        if (!state.NetListeners.TryGetValue(listenerHandle, out var listener))
        {
            return -1;
        }

        var client = listener.AcceptTcpClient();
        var connHandle = state.NextNetHandle++;
        state.NetConnections[connHandle] = client;
        if (state.NetTlsCertificates.TryGetValue(listenerHandle, out var cert))
        {
            var tlsStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                tlsStream.AuthenticateAsServer(
                    cert,
                    clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.Tls12 | SslProtocols.Tls13,
                    checkCertificateRevocation: false);
            }
            catch
            {
                tlsStream.Dispose();
                try { client.Close(); } catch { }
                state.NetConnections.Remove(connHandle);
                return -1;
            }

            state.NetTlsStreams[connHandle] = tlsStream;
        }

        return connHandle;
    }

    public virtual int NetTcpListen(VmNetworkState state, string host, int port)
    {
        var listener = new TcpListener(ResolveListenAddress(host), port);
        listener.Start();
        var handle = state.NextNetHandle++;
        state.NetListeners[handle] = listener;
        return handle;
    }

    public virtual int NetTcpListenTls(VmNetworkState state, string host, int port, string certPath, string keyPath)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        }
        catch
        {
            return -1;
        }

        var listener = new TcpListener(ResolveListenAddress(host), port);
        listener.Start();
        var handle = state.NextNetHandle++;
        state.NetListeners[handle] = listener;
        state.NetTlsCertificates[handle] = certificate;
        return handle;
    }

    public virtual int NetTcpAccept(VmNetworkState state, int listenerHandle)
    {
        return NetAccept(state, listenerHandle);
    }

    public virtual int NetTcpConnect(VmNetworkState state, string host, int port)
    {
        try
        {
            var client = new TcpClient();
            client.Connect(host, port);
            var connectionHandle = state.NextNetHandle++;
            state.NetConnections[connectionHandle] = client;
            return connectionHandle;
        }
        catch
        {
            return -1;
        }
    }

    public virtual string NetTcpRead(VmNetworkState state, int connectionHandle, int maxBytes)
    {
        if (maxBytes <= 0 ||
            !state.NetConnections.TryGetValue(connectionHandle, out var client))
        {
            return string.Empty;
        }

        var stream = GetConnectionStream(state, client, connectionHandle);
        var buffer = new byte[maxBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    public virtual int NetTcpWrite(VmNetworkState state, int connectionHandle, string data)
    {
        if (!state.NetConnections.TryGetValue(connectionHandle, out var client))
        {
            return -1;
        }

        var stream = GetConnectionStream(state, client, connectionHandle);
        var payload = Encoding.UTF8.GetBytes(data);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
        return payload.Length;
    }

    public virtual int NetUdpBind(VmNetworkState state, string host, int port)
    {
        var endpoint = new IPEndPoint(ResolveListenAddress(host), port);
        var socket = new UdpClient(endpoint);
        var handle = state.NextNetHandle++;
        state.NetUdpSockets[handle] = socket;
        return handle;
    }

    public virtual VmUdpPacket NetUdpRecv(VmNetworkState state, int handle, int maxBytes)
    {
        if (maxBytes <= 0 || !state.NetUdpSockets.TryGetValue(handle, out var socket))
        {
            return new VmUdpPacket(string.Empty, 0, string.Empty);
        }

        var remote = new IPEndPoint(IPAddress.Any, 0);
        var payload = socket.Receive(ref remote);
        if (payload.Length > maxBytes)
        {
            Array.Resize(ref payload, maxBytes);
        }

        return new VmUdpPacket(remote.Address.ToString(), remote.Port, Encoding.UTF8.GetString(payload));
    }

    public virtual int NetUdpSend(VmNetworkState state, int handle, string host, int port, string data)
    {
        if (!state.NetUdpSockets.TryGetValue(handle, out var socket))
        {
            return -1;
        }

        var payload = Encoding.UTF8.GetBytes(data);
        var sent = socket.Send(payload, payload.Length, host, port);
        return sent;
    }

    public virtual int UiCreateWindow(string title, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(title) || width <= 0 || height <= 0)
        {
            return -1;
        }

        var handle = _nextUiHandle++;
        if (_linuxUi is not null && _linuxUi.TryCreateWindow(handle, title, width, height))
        {
            _openWindows.Add(handle);
            return handle;
        }

        if (_windowsUi is not null && _windowsUi.TryCreateWindow(handle, title, width, height))
        {
            _openWindows.Add(handle);
            return handle;
        }

        if (_macUi is not null && _macUi.TryCreateWindow(handle, title, width, height))
        {
            _openWindows.Add(handle);
            return handle;
        }

        return -1;
    }

    public virtual void UiBeginFrame(int windowHandle)
    {
        if (_linuxUi is not null && _linuxUi.TryBeginFrame(windowHandle))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryBeginFrame(windowHandle))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryBeginFrame(windowHandle))
        {
            return;
        }
    }

    public virtual void UiDrawRect(int windowHandle, int x, int y, int width, int height, string color)
    {
        if (_linuxUi is not null && _linuxUi.TryDrawRect(windowHandle, x, y, width, height, color))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryDrawRect(windowHandle, x, y, width, height, color))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryDrawRect(windowHandle, x, y, width, height, color))
        {
            return;
        }
    }

    public virtual void UiDrawText(int windowHandle, int x, int y, string text, string color, int size)
    {
        if (_linuxUi is not null && _linuxUi.TryDrawText(windowHandle, x, y, text, color, size))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryDrawText(windowHandle, x, y, text, color, size))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryDrawText(windowHandle, x, y, text, color, size))
        {
            return;
        }
    }

    public virtual void UiDrawLine(int windowHandle, int x1, int y1, int x2, int y2, string color, int strokeWidth)
    {
        if (_linuxUi is not null && _linuxUi.TryDrawLine(windowHandle, x1, y1, x2, y2, color, strokeWidth))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryDrawLine(windowHandle, x1, y1, x2, y2, color, strokeWidth))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryDrawLine(windowHandle, x1, y1, x2, y2, color, strokeWidth))
        {
            return;
        }
    }

    public virtual void UiDrawEllipse(int windowHandle, int x, int y, int width, int height, string color)
    {
        if (_linuxUi is not null && _linuxUi.TryDrawEllipse(windowHandle, x, y, width, height, color))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryDrawEllipse(windowHandle, x, y, width, height, color))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryDrawEllipse(windowHandle, x, y, width, height, color))
        {
            return;
        }
    }

    public virtual void UiDrawPath(int windowHandle, string path, string color, int strokeWidth)
    {
        var points = UiDrawCommand.ParsePathPoints(path);
        if (points.Count < 2)
        {
            return;
        }

        for (var i = 1; i < points.Count; i++)
        {
            UiDrawLine(windowHandle, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, color, strokeWidth);
        }
    }

    public virtual void UiDrawImage(int windowHandle, int x, int y, int width, int height, string rgbaBase64)
    {
        if (_linuxUi is not null && _linuxUi.TryDrawImage(windowHandle, x, y, width, height, rgbaBase64))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryDrawImage(windowHandle, x, y, width, height, rgbaBase64))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryDrawImage(windowHandle, x, y, width, height, rgbaBase64))
        {
            return;
        }
    }

    public virtual void UiEndFrame(int windowHandle)
    {
        _ = _openWindows.Contains(windowHandle);
    }

    public virtual VmUiEvent UiPollEvent(int windowHandle)
    {
        if (_linuxUi is not null)
        {
            return _linuxUi.PollEvent(windowHandle);
        }

        if (_windowsUi is not null)
        {
            return _windowsUi.PollEvent(windowHandle);
        }

        if (_macUi is not null)
        {
            return _macUi.PollEvent(windowHandle);
        }

        return new VmUiEvent("closed", string.Empty, -1, -1, string.Empty, string.Empty, string.Empty, false);
    }

    public virtual VmUiWindowSize UiGetWindowSize(int windowHandle)
    {
        if (_linuxUi is not null && _linuxUi.TryGetWindowSize(windowHandle, out var linuxWidth, out var linuxHeight))
        {
            return new VmUiWindowSize(linuxWidth, linuxHeight);
        }

        if (_windowsUi is not null && _windowsUi.TryGetWindowSize(windowHandle, out var windowsWidth, out var windowsHeight))
        {
            return new VmUiWindowSize(windowsWidth, windowsHeight);
        }

        if (_macUi is not null && _macUi.TryGetWindowSize(windowHandle, out var macWidth, out var macHeight))
        {
            return new VmUiWindowSize(macWidth, macHeight);
        }

        return new VmUiWindowSize(-1, -1);
    }

    public virtual void UiPresent(int windowHandle)
    {
        if (_linuxUi is not null && _linuxUi.TryPresent(windowHandle))
        {
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryPresent(windowHandle))
        {
            return;
        }

        if (_macUi is not null && _macUi.TryPresent(windowHandle))
        {
            return;
        }
    }

    public virtual void UiCloseWindow(int windowHandle)
    {
        if (_linuxUi is not null && _linuxUi.TryCloseWindow(windowHandle))
        {
            _openWindows.Remove(windowHandle);
            return;
        }

        if (_windowsUi is not null && _windowsUi.TryCloseWindow(windowHandle))
        {
            _openWindows.Remove(windowHandle);
            return;
        }

        if (_macUi is not null && _macUi.TryCloseWindow(windowHandle))
        {
            _openWindows.Remove(windowHandle);
            return;
        }
    }

    public virtual string NetReadHeaders(VmNetworkState state, int connectionHandle)
    {
        if (!state.NetConnections.TryGetValue(connectionHandle, out var client))
        {
            return string.Empty;
        }

        var stream = GetConnectionStream(state, client, connectionHandle);
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

        var headerText = Encoding.ASCII.GetString(bytes.ToArray());
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
                headerText += Encoding.UTF8.GetString(bodyBuffer, 0, readTotal);
            }
        }

        return headerText;
    }

    public virtual bool NetWrite(VmNetworkState state, int connectionHandle, string text)
    {
        if (!state.NetConnections.TryGetValue(connectionHandle, out var client))
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var stream = GetConnectionStream(state, client, connectionHandle);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        return true;
    }

    public virtual void NetClose(VmNetworkState state, int handle)
    {
        if (state.NetConnections.TryGetValue(handle, out var conn))
        {
            if (state.NetTlsStreams.TryGetValue(handle, out var tlsStream))
            {
                try { tlsStream.Dispose(); } catch { }
                state.NetTlsStreams.Remove(handle);
            }

            try { conn.Close(); } catch { }
            state.NetConnections.Remove(handle);
            return;
        }

        if (state.NetListeners.TryGetValue(handle, out var listener))
        {
            try { listener.Stop(); } catch { }
            state.NetListeners.Remove(handle);
            state.NetTlsCertificates.Remove(handle);
            return;
        }

        if (state.NetUdpSockets.TryGetValue(handle, out var socket))
        {
            try { socket.Close(); } catch { }
            state.NetUdpSockets.Remove(handle);
        }
    }

    public virtual void StdoutWriteLine(string text) => Console.WriteLine(text);

    public virtual void ProcessExit(int code) => throw new AosProcessExitException(code);

    private static int ParseContentLength(string raw)
    {
        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, out var parsed) && parsed > 0)
                {
                    return parsed;
                }

                return 0;
            }
        }

        return 0;
    }

    private static Stream GetConnectionStream(VmNetworkState state, TcpClient client, int connectionHandle)
    {
        return state.NetTlsStreams.TryGetValue(connectionHandle, out var tlsStream)
            ? tlsStream
            : client.GetStream();
    }

    private static IPAddress ResolveListenAddress(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal) || string.Equals(host, "*", StringComparison.Ordinal))
        {
            return IPAddress.Any;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        return IPAddress.Loopback;
    }

}
