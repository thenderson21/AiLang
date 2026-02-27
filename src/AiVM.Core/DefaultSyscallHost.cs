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
using System.Threading.Tasks;

namespace AiVM.Core;

public partial class DefaultSyscallHost : ISyscallHost
{
    private const int NetConnectTimeoutMs = 8000;
    private const int NetTlsHandshakeTimeoutMs = 8000;
    private static readonly HashSet<string> DebugChannels = new(StringComparer.Ordinal) { "event", "ui", "net", "worker", "scene" };
    private static readonly HashSet<string> DebugDrawOps = new(StringComparer.Ordinal) { "rect", "ellipse", "path", "text", "line", "transform", "filter", "image" };
    private static readonly HashSet<string> DebugAsyncPhases = new(StringComparer.Ordinal) { "start", "poll", "done", "fail", "cancel" };
    private static readonly string[] EmptyArgv = Array.Empty<string>();
    private static readonly Stopwatch MonotonicStopwatch = Stopwatch.StartNew();
    private int _nextUiHandle = 1;
    private string _debugMode = "off";
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
            if (!TryConnectWithTimeout(client, host, port, NetConnectTimeoutMs))
            {
                try { client.Close(); } catch { }
                return -1;
            }
            var connectionHandle = state.NextNetHandle++;
            state.NetConnections[connectionHandle] = client;
            return connectionHandle;
        }
        catch
        {
            return -1;
        }
    }

    public virtual int NetTcpConnectTls(VmNetworkState state, string host, int port)
    {
        try
        {
            var client = new TcpClient();
            if (!TryConnectWithTimeout(client, host, port, NetConnectTimeoutMs))
            {
                try { client.Close(); } catch { }
                return -1;
            }
            var connectionHandle = state.NextNetHandle++;
            state.NetConnections[connectionHandle] = client;

            var tlsStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            if (!TryAuthenticateTlsWithTimeout(tlsStream, host, NetTlsHandshakeTimeoutMs))
            {
                try { tlsStream.Dispose(); } catch { }
                try { client.Close(); } catch { }
                state.NetConnections.Remove(connectionHandle);
                return -1;
            }

            state.NetTlsStreams[connectionHandle] = tlsStream;
            return connectionHandle;
        }
        catch
        {
            return -1;
        }
    }

    public virtual int NetTcpConnectStart(VmNetworkState state, string host, int port)
    {
        return StartNetConnectAsyncOperation(state, host, port, useTls: false);
    }

    public virtual int NetTcpConnectTlsStart(VmNetworkState state, string host, int port)
    {
        return StartNetConnectAsyncOperation(state, host, port, useTls: true);
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

    public virtual int NetTcpReadStart(VmNetworkState state, int connectionHandle, int maxBytes)
    {
        return StartNetAsyncOperation(state, () =>
        {
            var text = NetTcpRead(state, connectionHandle, maxBytes);
            return (0, text, string.Empty);
        });
    }

    public virtual int NetTcpWriteStart(VmNetworkState state, int connectionHandle, string data)
    {
        return StartNetAsyncOperation(state, () =>
        {
            var written = NetTcpWrite(state, connectionHandle, data);
            return written >= 0
                ? (written, string.Empty, string.Empty)
                : (-1, string.Empty, "write_failed");
        });
    }

    public virtual int NetAsyncPoll(VmNetworkState state, int operationHandle)
    {
        lock (state.NetAsyncLock)
        {
            if (!state.NetAsyncOperations.TryGetValue(operationHandle, out var op))
            {
                return -3;
            }

            if (op.Status == 1 && !op.Finalized)
            {
                FinalizeCompletedNetOperation(state, op);
            }
            else if (op.Status == -2 && !op.Finalized)
            {
                DisposePendingNetResources(op);
                op.Finalized = true;
            }

            return op.Status;
        }
    }

    public virtual int NetAsyncAwait(VmNetworkState state, int operationHandle)
    {
        VmNetAsyncOperation? op;
        lock (state.NetAsyncLock)
        {
            if (!state.NetAsyncOperations.TryGetValue(operationHandle, out op))
            {
                return -3;
            }
        }

        op.Task.Wait();
        lock (state.NetAsyncLock)
        {
            if (op.Status == 1 && !op.Finalized)
            {
                FinalizeCompletedNetOperation(state, op);
            }
            else if (op.Status == -2 && !op.Finalized)
            {
                DisposePendingNetResources(op);
                op.Finalized = true;
            }

            return op.Status;
        }
    }

    public virtual bool NetAsyncCancel(VmNetworkState state, int operationHandle)
    {
        lock (state.NetAsyncLock)
        {
            if (!state.NetAsyncOperations.TryGetValue(operationHandle, out var op))
            {
                return false;
            }

            if (op.Status != 0)
            {
                return false;
            }

            DisposePendingNetResources(op);
            op.Finalized = true;
            op.Status = -2;
            op.Error = "cancelled";
            return true;
        }
    }

    public virtual int NetAsyncResultInt(VmNetworkState state, int operationHandle)
    {
        lock (state.NetAsyncLock)
        {
            return state.NetAsyncOperations.TryGetValue(operationHandle, out var op)
                ? op.IntResult
                : 0;
        }
    }

    public virtual string NetAsyncResultString(VmNetworkState state, int operationHandle)
    {
        lock (state.NetAsyncLock)
        {
            return state.NetAsyncOperations.TryGetValue(operationHandle, out var op)
                ? op.StringResult
                : string.Empty;
        }
    }

    public virtual string NetAsyncError(VmNetworkState state, int operationHandle)
    {
        lock (state.NetAsyncLock)
        {
            return state.NetAsyncOperations.TryGetValue(operationHandle, out var op)
                ? op.Error
                : "unknown_operation";
        }
    }

    public virtual void DebugEmit(string channel, string payload)
    {
        if (!DebugChannels.Contains(channel))
        {
            throw new VmRuntimeException("DBG001", $"Unsupported debug channel '{channel}'.", "sys.debug_emit");
        }

        Console.WriteLine($"[debug.{channel}] {payload}");
    }

    public virtual string DebugMode()
    {
        return _debugMode;
    }

    public void SetDebugMode(string mode)
    {
        _debugMode = NormalizeDebugMode(mode);
    }

    public virtual void DebugCaptureFrameBegin(int frameId, int width, int height)
    {
        DebugEmit("scene", $"frame.begin id={frameId} size={width}x{height}");
    }

    public virtual void DebugCaptureFrameEnd(int frameId)
    {
        DebugEmit("scene", $"frame.end id={frameId}");
    }

    public virtual void DebugCaptureDraw(string op, string args)
    {
        if (!DebugDrawOps.Contains(op))
        {
            throw new VmRuntimeException("DBG002", $"Unsupported draw op '{op}'.", "sys.debug_captureDraw");
        }

        DebugEmit("scene", $"draw {op} {args}");
    }

    public virtual void DebugCaptureInput(string eventPayload)
    {
        DebugEmit("event", $"input {NormalizeControlChars(eventPayload)}");
    }

    public virtual void DebugCaptureState(string key, string valuePayload)
    {
        DebugEmit("ui", $"state {key}={valuePayload}");
    }

    public virtual int DebugReplayLoad(VmNetworkState state, string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            lock (state.DebugLock)
            {
                var handle = state.NextDebugReplayHandle++;
                state.DebugReplays[handle] = new VmDebugReplayState { Lines = lines };
                return handle;
            }
        }
        catch
        {
            return -1;
        }
    }

    public virtual string DebugReplayNext(VmNetworkState state, int handle)
    {
        lock (state.DebugLock)
        {
            if (!state.DebugReplays.TryGetValue(handle, out var replay))
            {
                return string.Empty;
            }

            if (replay.Index >= replay.Lines.Length)
            {
                return string.Empty;
            }

            var line = replay.Lines[replay.Index];
            replay.Index++;
            return line;
        }
    }

    public virtual void DebugAssert(bool cond, string code, string message)
    {
        if (cond)
        {
            return;
        }

        throw new VmRuntimeException(code, message, "sys.debug_assert");
    }

    public virtual bool DebugArtifactWrite(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public virtual void DebugTraceAsync(int opId, string phase, string detail)
    {
        if (!DebugAsyncPhases.Contains(phase))
        {
            throw new VmRuntimeException("DBG003", $"Unsupported async phase '{phase}'.", "sys.debug_traceAsync");
        }

        DebugEmit("worker", $"async op={opId} phase={phase} detail={detail}");
    }

    public virtual int WorkerStart(VmNetworkState state, string taskName, string payload)
    {
        int handle;
        VmWorkerOperation op;
        lock (state.WorkerLock)
        {
            handle = state.NextWorkerHandle++;
            op = new VmWorkerOperation { Task = Task.CompletedTask };
            state.WorkerOperations[handle] = op;
        }

        op.Task = Task.Run(() =>
        {
            string result;
            string error;
            switch (taskName)
            {
                case "echo":
                    result = payload;
                    error = string.Empty;
                    break;
                case "upper":
                    result = payload.ToUpperInvariant();
                    error = string.Empty;
                    break;
                default:
                    result = string.Empty;
                    error = "unknown_worker_task";
                    break;
            }

            lock (state.WorkerLock)
            {
                if (!state.WorkerOperations.TryGetValue(handle, out var current) || current.Status == -2)
                {
                    return;
                }

                current.Result = result;
                current.Error = error;
                current.Status = string.IsNullOrEmpty(error) ? 1 : -1;
            }
        });

        return handle;
    }

    public virtual int WorkerPoll(VmNetworkState state, int workerHandle)
    {
        lock (state.WorkerLock)
        {
            return state.WorkerOperations.TryGetValue(workerHandle, out var op)
                ? op.Status
                : -3;
        }
    }

    public virtual string WorkerResult(VmNetworkState state, int workerHandle)
    {
        lock (state.WorkerLock)
        {
            return state.WorkerOperations.TryGetValue(workerHandle, out var op)
                ? op.Result
                : string.Empty;
        }
    }

    public virtual string WorkerError(VmNetworkState state, int workerHandle)
    {
        lock (state.WorkerLock)
        {
            return state.WorkerOperations.TryGetValue(workerHandle, out var op)
                ? op.Error
                : "unknown_worker";
        }
    }

    public virtual bool WorkerCancel(VmNetworkState state, int workerHandle)
    {
        lock (state.WorkerLock)
        {
            if (!state.WorkerOperations.TryGetValue(workerHandle, out var op))
            {
                return false;
            }

            if (op.Status != 0)
            {
                return false;
            }

            op.Status = -2;
            op.Error = "cancelled";
            return true;
        }
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

    public virtual void UiWaitFrame(int windowHandle)
    {
        if (!_openWindows.Contains(windowHandle))
        {
            return;
        }

        Thread.Sleep(16);
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

    private static int StartNetAsyncOperation(VmNetworkState state, Func<(int intResult, string stringResult, string error)> run)
    {
        int handle;
        VmNetAsyncOperation op;
        lock (state.NetAsyncLock)
        {
            handle = state.NextNetAsyncHandle++;
            op = new VmNetAsyncOperation { Task = Task.CompletedTask };
            state.NetAsyncOperations[handle] = op;
        }

        op.Task = Task.Run(() =>
        {
            int intResult;
            string stringResult;
            string error;
            try
            {
                (intResult, stringResult, error) = run();
            }
            catch
            {
                intResult = -1;
                stringResult = string.Empty;
                error = "async_operation_failed";
            }

            lock (state.NetAsyncLock)
            {
                if (!state.NetAsyncOperations.TryGetValue(handle, out var current) || current.Status == -2)
                {
                    return;
                }

                current.IntResult = intResult;
                current.StringResult = stringResult;
                current.Error = error;
                current.Status = string.IsNullOrEmpty(error) ? 1 : -1;
            }
        });

        return handle;
    }

    private static int StartNetConnectAsyncOperation(VmNetworkState state, string host, int port, bool useTls)
    {
        int opHandle;
        int pendingConnectionHandle;
        VmNetAsyncOperation op;
        lock (state.NetAsyncLock)
        {
            opHandle = state.NextNetAsyncHandle++;
            pendingConnectionHandle = state.NextNetHandle++;
            op = new VmNetAsyncOperation
            {
                Task = Task.CompletedTask,
                Kind = useTls ? VmNetAsyncOperationKind.TcpConnectTls : VmNetAsyncOperationKind.TcpConnect,
                PendingConnectionHandle = pendingConnectionHandle
            };
            state.NetAsyncOperations[opHandle] = op;
        }

        op.Task = Task.Run(() =>
        {
            TcpClient? client = null;
            SslStream? tlsStream = null;
            var error = useTls ? "connect_tls_failed" : "connect_failed";
            try
            {
                client = new TcpClient();
                if (TryConnectWithTimeout(client, host, port, NetConnectTimeoutMs))
                {
                    if (useTls)
                    {
                        tlsStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                        if (!TryAuthenticateTlsWithTimeout(tlsStream, host, NetTlsHandshakeTimeoutMs))
                        {
                            DisposePendingNetResources(client, tlsStream);
                            client = null;
                            tlsStream = null;
                        }
                        else
                        {
                            error = string.Empty;
                        }
                    }
                    else
                    {
                        error = string.Empty;
                    }
                }
                else
                {
                    DisposePendingNetResources(client, tlsStream);
                    client = null;
                    tlsStream = null;
                }
            }
            catch
            {
                DisposePendingNetResources(client, tlsStream);
                client = null;
                tlsStream = null;
            }

            lock (state.NetAsyncLock)
            {
                if (!state.NetAsyncOperations.TryGetValue(opHandle, out var current))
                {
                    DisposePendingNetResources(client, tlsStream);
                    return;
                }

                if (current.Status == -2)
                {
                    DisposePendingNetResources(client, tlsStream);
                    return;
                }

                if (string.IsNullOrEmpty(error) && client is not null)
                {
                    current.PendingTcpClient = client;
                    current.PendingTlsStream = tlsStream;
                    current.Error = string.Empty;
                    current.Status = 1;
                    return;
                }

                current.PendingTcpClient = null;
                current.PendingTlsStream = null;
                current.IntResult = -1;
                current.Error = error;
                current.Status = -1;
            }
        });

        return opHandle;
    }

    private static void FinalizeCompletedNetOperation(VmNetworkState state, VmNetAsyncOperation op)
    {
        if (op.Kind != VmNetAsyncOperationKind.TcpConnect &&
            op.Kind != VmNetAsyncOperationKind.TcpConnectTls)
        {
            op.Finalized = true;
            return;
        }

        if (op.PendingConnectionHandle < 0 || op.PendingTcpClient is null)
        {
            DisposePendingNetResources(op);
            op.IntResult = -1;
            op.Error = op.Kind == VmNetAsyncOperationKind.TcpConnectTls ? "connect_tls_failed" : "connect_failed";
            op.Status = -1;
            op.Finalized = true;
            return;
        }

        state.NetConnections[op.PendingConnectionHandle] = op.PendingTcpClient;
        if (op.Kind == VmNetAsyncOperationKind.TcpConnectTls)
        {
            if (op.PendingTlsStream is null)
            {
                try { op.PendingTcpClient.Close(); } catch { }
                state.NetConnections.Remove(op.PendingConnectionHandle);
                op.IntResult = -1;
                op.Error = "connect_tls_failed";
                op.Status = -1;
                op.Finalized = true;
                op.PendingTcpClient = null;
                return;
            }

            state.NetTlsStreams[op.PendingConnectionHandle] = op.PendingTlsStream;
        }

        op.IntResult = op.PendingConnectionHandle;
        op.PendingTcpClient = null;
        op.PendingTlsStream = null;
        op.Finalized = true;
    }

    private static void DisposePendingNetResources(VmNetAsyncOperation op)
    {
        DisposePendingNetResources(op.PendingTcpClient, op.PendingTlsStream);
        op.PendingTcpClient = null;
        op.PendingTlsStream = null;
    }

    private static void DisposePendingNetResources(TcpClient? client, SslStream? tlsStream)
    {
        if (tlsStream is not null)
        {
            try { tlsStream.Dispose(); } catch { }
        }

        if (client is not null)
        {
            try { client.Close(); } catch { }
        }
    }

    private static bool TryConnectWithTimeout(TcpClient client, string host, int port, int timeoutMs)
    {
        try
        {
            var connectTask = client.ConnectAsync(host, port);
            return WaitForTaskSuccess(connectTask, timeoutMs);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryAuthenticateTlsWithTimeout(SslStream stream, string host, int timeoutMs)
    {
        try
        {
            var authTask = stream.AuthenticateAsClientAsync(host);
            return WaitForTaskSuccess(authTask, timeoutMs);
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForTaskSuccess(Task task, int timeoutMs)
    {
        try
        {
            if (!task.Wait(timeoutMs))
            {
                return false;
            }

            return !task.IsFaulted && !task.IsCanceled;
        }
        catch
        {
            return false;
        }
    }

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

    private static string NormalizeDebugMode(string mode)
    {
        return mode switch
        {
            "live" or "snapshot" or "replay" or "scene" => mode,
            _ => "off"
        };
    }

    private static string NormalizeControlChars(string value)
    {
        return value
            .Replace("\r", "<enter>", StringComparison.Ordinal)
            .Replace("\n", "<enter>", StringComparison.Ordinal)
            .Replace("\t", "<tab>", StringComparison.Ordinal)
            .Replace("\b", "<backspace>", StringComparison.Ordinal);
    }

}
