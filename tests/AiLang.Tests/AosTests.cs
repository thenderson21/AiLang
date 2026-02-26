using AiLang.Core;
using AiCLI;
using AiVM.Core;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace AiLang.Tests;

public class AosTests
{
    private sealed class RecordingSyscallHost : DefaultSyscallHost
    {
        public string? LastFsWritePath { get; private set; }
        public string? LastFsWriteText { get; private set; }
        public string? LastFsMakeDirPath { get; private set; }
        public string? LastConsoleErrLine { get; private set; }
        public string? LastConsoleWrite { get; private set; }
        public string? LastConsoleLine { get; private set; }
        public string? LastStdoutLine { get; private set; }
        public int IoPrintCount { get; private set; }
        public string ProcessCwdResult { get; set; } = string.Empty;
        public string PlatformResult { get; set; } = "test-os";
        public string ArchitectureResult { get; set; } = "test-arch";
        public string OsVersionResult { get; set; } = "test-version";
        public string RuntimeResult { get; set; } = "test-runtime";
        public string IoReadLineResult { get; set; } = string.Empty;
        public string IoReadAllStdinResult { get; set; } = string.Empty;
        public bool FsPathExistsResult { get; set; }
        public string[] ProcessArgvResult { get; set; } = Array.Empty<string>();
        public string ProcessEnvGetResult { get; set; } = string.Empty;
        public string[] FsReadDirResult { get; set; } = Array.Empty<string>();
        public string? LastFsReadDirPath { get; private set; }
        public VmFsStat FsStatResult { get; set; } = new("missing", 0, 0);
        public string? LastFsStatPath { get; private set; }
        public int TimeNowUnixMsResult { get; set; }
        public int TimeMonotonicMsResult { get; set; }
        public int LastSleepMs { get; private set; } = -1;
        public int StrUtf8ByteCountResult { get; set; } = 777;
        public string? LastNetTcpListenHost { get; private set; }
        public int LastNetTcpListenPort { get; private set; } = -1;
        public string? LastNetTcpConnectHost { get; private set; }
        public int LastNetTcpConnectPort { get; private set; } = -1;
        public int NetTcpConnectResult { get; set; } = -1;
        public string? LastNetTcpConnectTlsHost { get; private set; }
        public int LastNetTcpConnectTlsPort { get; private set; } = -1;
        public int NetTcpConnectTlsResult { get; set; } = -1;
        public string? LastNetTcpConnectStartHost { get; private set; }
        public int LastNetTcpConnectStartPort { get; private set; } = -1;
        public int NetTcpConnectStartResult { get; set; } = -1;
        public string? LastNetTcpConnectTlsStartHost { get; private set; }
        public int LastNetTcpConnectTlsStartPort { get; private set; } = -1;
        public int NetTcpConnectTlsStartResult { get; set; } = -1;
        public int LastNetTcpReadStartHandle { get; private set; } = -1;
        public int LastNetTcpReadStartMaxBytes { get; private set; } = -1;
        public int NetTcpReadStartResult { get; set; } = -1;
        public int LastNetTcpWriteStartHandle { get; private set; } = -1;
        public string? LastNetTcpWriteStartData { get; private set; }
        public int NetTcpWriteStartResult { get; set; } = -1;
        public int LastNetAsyncPollHandle { get; private set; } = -1;
        public int NetAsyncPollResult { get; set; } = 0;
        public int LastNetAsyncAwaitHandle { get; private set; } = -1;
        public int NetAsyncAwaitResult { get; set; } = 1;
        public int LastNetAsyncCancelHandle { get; private set; } = -1;
        public bool NetAsyncCancelResult { get; set; }
        public int LastNetAsyncResultIntHandle { get; private set; } = -1;
        public int NetAsyncResultIntResult { get; set; }
        public int LastNetAsyncResultStringHandle { get; private set; } = -1;
        public string NetAsyncResultStringResult { get; set; } = string.Empty;
        public int LastNetAsyncErrorHandle { get; private set; } = -1;
        public string NetAsyncErrorResult { get; set; } = string.Empty;
        public string? LastWorkerTaskName { get; private set; }
        public string? LastWorkerPayload { get; private set; }
        public int WorkerStartResult { get; set; } = -1;
        public int LastWorkerPollHandle { get; private set; } = -1;
        public int WorkerPollResult { get; set; } = 0;
        public int LastWorkerResultHandle { get; private set; } = -1;
        public string WorkerResultValue { get; set; } = string.Empty;
        public int LastWorkerErrorHandle { get; private set; } = -1;
        public string WorkerErrorValue { get; set; } = string.Empty;
        public int LastWorkerCancelHandle { get; private set; } = -1;
        public bool WorkerCancelResult { get; set; }
        public string? LastDebugEmitChannel { get; private set; }
        public string? LastDebugEmitPayload { get; private set; }
        public string DebugModeValue { get; set; } = "off";
        public int LastDebugCaptureFrameBeginId { get; private set; } = -1;
        public int LastDebugCaptureFrameBeginWidth { get; private set; } = -1;
        public int LastDebugCaptureFrameBeginHeight { get; private set; } = -1;
        public int LastDebugCaptureFrameEndId { get; private set; } = -1;
        public string? LastDebugCaptureDrawOp { get; private set; }
        public string? LastDebugCaptureDrawArgs { get; private set; }
        public string? LastDebugCaptureInputPayload { get; private set; }
        public string? LastDebugCaptureStateKey { get; private set; }
        public string? LastDebugCaptureStatePayload { get; private set; }
        public string? LastDebugReplayLoadPath { get; private set; }
        public int DebugReplayLoadResult { get; set; } = -1;
        public int LastDebugReplayNextHandle { get; private set; } = -1;
        public string DebugReplayNextValue { get; set; } = string.Empty;
        public bool LastDebugAssertCondition { get; private set; }
        public string? LastDebugAssertCode { get; private set; }
        public string? LastDebugAssertMessage { get; private set; }
        public string? LastDebugArtifactWritePath { get; private set; }
        public string? LastDebugArtifactWriteText { get; private set; }
        public bool DebugArtifactWriteResult { get; set; }
        public int LastDebugTraceAsyncOpId { get; private set; } = -1;
        public string? LastDebugTraceAsyncPhase { get; private set; }
        public string? LastDebugTraceAsyncDetail { get; private set; }
        public string? LastNetTcpReadPayload { get; set; }
        public int LastNetTcpReadHandle { get; private set; } = -1;
        public int LastNetTcpReadMaxBytes { get; private set; } = -1;
        public int LastNetTcpWriteHandle { get; private set; } = -1;
        public string? LastNetTcpWriteData { get; private set; }
        public int NetTcpWriteResult { get; set; } = -1;
        public int LastNetCloseHandle { get; private set; } = -1;
        public string? LastCryptoBase64EncodeInput { get; private set; }
        public string CryptoBase64EncodeResult { get; set; } = string.Empty;
        public string? LastCryptoBase64DecodeInput { get; private set; }
        public string CryptoBase64DecodeResult { get; set; } = string.Empty;
        public string? LastCryptoSha1Input { get; private set; }
        public string CryptoSha1Result { get; set; } = string.Empty;
        public string? LastCryptoSha256Input { get; private set; }
        public string CryptoSha256Result { get; set; } = string.Empty;
        public string? LastCryptoHmacSha256Key { get; private set; }
        public string? LastCryptoHmacSha256Text { get; private set; }
        public string CryptoHmacSha256Result { get; set; } = string.Empty;
        public int LastCryptoRandomBytesCount { get; private set; } = -1;
        public string CryptoRandomBytesResult { get; set; } = string.Empty;
        public string? LastNetUdpBindHost { get; private set; }
        public int LastNetUdpBindPort { get; private set; } = -1;
        public int NetUdpBindResult { get; set; } = -1;
        public int LastNetUdpRecvHandle { get; private set; } = -1;
        public int LastNetUdpRecvMaxBytes { get; private set; } = -1;
        public VmUdpPacket NetUdpRecvResult { get; set; }
        public int LastNetUdpSendHandle { get; private set; } = -1;
        public string? LastNetUdpSendHost { get; private set; }
        public int LastNetUdpSendPort { get; private set; } = -1;
        public string? LastNetUdpSendData { get; private set; }
        public int NetUdpSendResult { get; set; } = -1;
        public string? LastUiCreateTitle { get; private set; }
        public int LastUiCreateWidth { get; private set; } = -1;
        public int LastUiCreateHeight { get; private set; } = -1;
        public int UiCreateWindowResult { get; set; } = -1;
        public int LastUiPollHandle { get; private set; } = -1;
        public VmUiEvent UiPollEventResult { get; set; }
        public int LastUiWaitFrameHandle { get; private set; } = -1;
        public int LastUiGetWindowSizeHandle { get; private set; } = -1;
        public VmUiWindowSize UiGetWindowSizeResult { get; set; }
        public int LastUiLineHandle { get; private set; } = -1;
        public int LastUiLineX1 { get; private set; } = -1;
        public int LastUiLineY1 { get; private set; } = -1;
        public int LastUiLineX2 { get; private set; } = -1;
        public int LastUiLineY2 { get; private set; } = -1;
        public string? LastUiLineColor { get; private set; }
        public int LastUiLineStrokeWidth { get; private set; } = -1;
        public int LastUiEllipseHandle { get; private set; } = -1;
        public int LastUiEllipseX { get; private set; } = -1;
        public int LastUiEllipseY { get; private set; } = -1;
        public int LastUiEllipseWidth { get; private set; } = -1;
        public int LastUiEllipseHeight { get; private set; } = -1;
        public string? LastUiEllipseColor { get; private set; }
        public int LastUiPathHandle { get; private set; } = -1;
        public string? LastUiPathValue { get; private set; }
        public string? LastUiPathColor { get; private set; }
        public int LastUiPathStrokeWidth { get; private set; } = -1;
        public int LastUiImageHandle { get; private set; } = -1;
        public int LastUiImageX { get; private set; } = -1;
        public int LastUiImageY { get; private set; } = -1;
        public int LastUiImageWidth { get; private set; } = -1;
        public int LastUiImageHeight { get; private set; } = -1;
        public string? LastUiImageRgbaBase64 { get; private set; }

        public override void ConsoleWrite(string text)
        {
            LastConsoleWrite = text;
        }

        public override void ConsoleWriteErrLine(string text)
        {
            LastConsoleErrLine = text;
        }

        public override void ConsolePrintLine(string text)
        {
            LastConsoleLine = text;
        }

        public override void StdoutWriteLine(string text)
        {
            LastStdoutLine = text;
        }

        public override void IoPrint(string text)
        {
            IoPrintCount++;
        }

        public override string IoReadLine()
        {
            return IoReadLineResult;
        }

        public override string IoReadAllStdin()
        {
            return IoReadAllStdinResult;
        }

        public override void FsMakeDir(string path)
        {
            LastFsMakeDirPath = path;
        }

        public override bool FsPathExists(string path)
        {
            return FsPathExistsResult;
        }

        public override string[] FsReadDir(string path)
        {
            LastFsReadDirPath = path;
            return FsReadDirResult;
        }

        public override VmFsStat FsStat(string path)
        {
            LastFsStatPath = path;
            return FsStatResult;
        }

        public override void FsWriteFile(string path, string text)
        {
            LastFsWritePath = path;
            LastFsWriteText = text;
        }

        public override string[] ProcessArgv()
        {
            return ProcessArgvResult;
        }

        public override string ProcessEnvGet(string name)
        {
            return ProcessEnvGetResult;
        }

        public override int TimeNowUnixMs()
        {
            return TimeNowUnixMsResult;
        }

        public override int TimeMonotonicMs()
        {
            return TimeMonotonicMsResult;
        }

        public override void TimeSleepMs(int ms)
        {
            LastSleepMs = ms;
        }

        public override string ProcessCwd()
        {
            return ProcessCwdResult;
        }

        public override int StrUtf8ByteCount(string text)
        {
            return StrUtf8ByteCountResult;
        }

        public override string Platform()
        {
            return PlatformResult;
        }

        public override string Architecture()
        {
            return ArchitectureResult;
        }

        public override string OsVersion()
        {
            return OsVersionResult;
        }

        public override string Runtime()
        {
            return RuntimeResult;
        }

        public override int NetTcpListen(VmNetworkState state, string host, int port)
        {
            LastNetTcpListenHost = host;
            LastNetTcpListenPort = port;
            return 77;
        }

        public override int NetTcpConnect(VmNetworkState state, string host, int port)
        {
            LastNetTcpConnectHost = host;
            LastNetTcpConnectPort = port;
            return NetTcpConnectResult;
        }

        public override int NetTcpConnectTls(VmNetworkState state, string host, int port)
        {
            LastNetTcpConnectTlsHost = host;
            LastNetTcpConnectTlsPort = port;
            return NetTcpConnectTlsResult;
        }

        public override int NetTcpConnectStart(VmNetworkState state, string host, int port)
        {
            LastNetTcpConnectStartHost = host;
            LastNetTcpConnectStartPort = port;
            return NetTcpConnectStartResult;
        }

        public override int NetTcpConnectTlsStart(VmNetworkState state, string host, int port)
        {
            LastNetTcpConnectTlsStartHost = host;
            LastNetTcpConnectTlsStartPort = port;
            return NetTcpConnectTlsStartResult;
        }

        public override int NetTcpReadStart(VmNetworkState state, int connectionHandle, int maxBytes)
        {
            LastNetTcpReadStartHandle = connectionHandle;
            LastNetTcpReadStartMaxBytes = maxBytes;
            return NetTcpReadStartResult;
        }

        public override int NetTcpWriteStart(VmNetworkState state, int connectionHandle, string data)
        {
            LastNetTcpWriteStartHandle = connectionHandle;
            LastNetTcpWriteStartData = data;
            return NetTcpWriteStartResult;
        }

        public override int NetAsyncPoll(VmNetworkState state, int operationHandle)
        {
            LastNetAsyncPollHandle = operationHandle;
            return NetAsyncPollResult;
        }

        public override int NetAsyncAwait(VmNetworkState state, int operationHandle)
        {
            LastNetAsyncAwaitHandle = operationHandle;
            return NetAsyncAwaitResult;
        }

        public override bool NetAsyncCancel(VmNetworkState state, int operationHandle)
        {
            LastNetAsyncCancelHandle = operationHandle;
            return NetAsyncCancelResult;
        }

        public override int NetAsyncResultInt(VmNetworkState state, int operationHandle)
        {
            LastNetAsyncResultIntHandle = operationHandle;
            return NetAsyncResultIntResult;
        }

        public override string NetAsyncResultString(VmNetworkState state, int operationHandle)
        {
            LastNetAsyncResultStringHandle = operationHandle;
            return NetAsyncResultStringResult;
        }

        public override string NetAsyncError(VmNetworkState state, int operationHandle)
        {
            LastNetAsyncErrorHandle = operationHandle;
            return NetAsyncErrorResult;
        }

        public override int WorkerStart(VmNetworkState state, string taskName, string payload)
        {
            LastWorkerTaskName = taskName;
            LastWorkerPayload = payload;
            return WorkerStartResult;
        }

        public override int WorkerPoll(VmNetworkState state, int workerHandle)
        {
            LastWorkerPollHandle = workerHandle;
            return WorkerPollResult;
        }

        public override string WorkerResult(VmNetworkState state, int workerHandle)
        {
            LastWorkerResultHandle = workerHandle;
            return WorkerResultValue;
        }

        public override string WorkerError(VmNetworkState state, int workerHandle)
        {
            LastWorkerErrorHandle = workerHandle;
            return WorkerErrorValue;
        }

        public override bool WorkerCancel(VmNetworkState state, int workerHandle)
        {
            LastWorkerCancelHandle = workerHandle;
            return WorkerCancelResult;
        }

        public override void DebugEmit(string channel, string payload)
        {
            LastDebugEmitChannel = channel;
            LastDebugEmitPayload = payload;
        }

        public override string DebugMode()
        {
            return DebugModeValue;
        }

        public override void DebugCaptureFrameBegin(int frameId, int width, int height)
        {
            LastDebugCaptureFrameBeginId = frameId;
            LastDebugCaptureFrameBeginWidth = width;
            LastDebugCaptureFrameBeginHeight = height;
        }

        public override void DebugCaptureFrameEnd(int frameId)
        {
            LastDebugCaptureFrameEndId = frameId;
        }

        public override void DebugCaptureDraw(string op, string args)
        {
            LastDebugCaptureDrawOp = op;
            LastDebugCaptureDrawArgs = args;
        }

        public override void DebugCaptureInput(string eventPayload)
        {
            LastDebugCaptureInputPayload = eventPayload;
        }

        public override void DebugCaptureState(string key, string valuePayload)
        {
            LastDebugCaptureStateKey = key;
            LastDebugCaptureStatePayload = valuePayload;
        }

        public override int DebugReplayLoad(VmNetworkState state, string path)
        {
            LastDebugReplayLoadPath = path;
            return DebugReplayLoadResult;
        }

        public override string DebugReplayNext(VmNetworkState state, int handle)
        {
            LastDebugReplayNextHandle = handle;
            return DebugReplayNextValue;
        }

        public override void DebugAssert(bool cond, string code, string message)
        {
            LastDebugAssertCondition = cond;
            LastDebugAssertCode = code;
            LastDebugAssertMessage = message;
            if (!cond)
            {
                throw new VmRuntimeException(code, message, "sys.debug_assert");
            }
        }

        public override bool DebugArtifactWrite(string path, string text)
        {
            LastDebugArtifactWritePath = path;
            LastDebugArtifactWriteText = text;
            return DebugArtifactWriteResult;
        }

        public override void DebugTraceAsync(int opId, string phase, string detail)
        {
            LastDebugTraceAsyncOpId = opId;
            LastDebugTraceAsyncPhase = phase;
            LastDebugTraceAsyncDetail = detail;
        }

        public override string NetTcpRead(VmNetworkState state, int connectionHandle, int maxBytes)
        {
            LastNetTcpReadHandle = connectionHandle;
            LastNetTcpReadMaxBytes = maxBytes;
            return LastNetTcpReadPayload ?? string.Empty;
        }

        public override int NetTcpWrite(VmNetworkState state, int connectionHandle, string data)
        {
            LastNetTcpWriteHandle = connectionHandle;
            LastNetTcpWriteData = data;
            return NetTcpWriteResult;
        }

        public override void NetClose(VmNetworkState state, int handle)
        {
            LastNetCloseHandle = handle;
        }

        public override string CryptoBase64Encode(string text)
        {
            LastCryptoBase64EncodeInput = text;
            return CryptoBase64EncodeResult;
        }

        public override string CryptoBase64Decode(string text)
        {
            LastCryptoBase64DecodeInput = text;
            return CryptoBase64DecodeResult;
        }

        public override string CryptoSha1(string text)
        {
            LastCryptoSha1Input = text;
            return CryptoSha1Result;
        }

        public override string CryptoSha256(string text)
        {
            LastCryptoSha256Input = text;
            return CryptoSha256Result;
        }

        public override string CryptoHmacSha256(string key, string text)
        {
            LastCryptoHmacSha256Key = key;
            LastCryptoHmacSha256Text = text;
            return CryptoHmacSha256Result;
        }

        public override string CryptoRandomBytes(int count)
        {
            LastCryptoRandomBytesCount = count;
            return CryptoRandomBytesResult;
        }

        public override int NetUdpBind(VmNetworkState state, string host, int port)
        {
            LastNetUdpBindHost = host;
            LastNetUdpBindPort = port;
            return NetUdpBindResult;
        }

        public override VmUdpPacket NetUdpRecv(VmNetworkState state, int handle, int maxBytes)
        {
            LastNetUdpRecvHandle = handle;
            LastNetUdpRecvMaxBytes = maxBytes;
            return NetUdpRecvResult;
        }

        public override int NetUdpSend(VmNetworkState state, int handle, string host, int port, string data)
        {
            LastNetUdpSendHandle = handle;
            LastNetUdpSendHost = host;
            LastNetUdpSendPort = port;
            LastNetUdpSendData = data;
            return NetUdpSendResult;
        }

        public override int UiCreateWindow(string title, int width, int height)
        {
            LastUiCreateTitle = title;
            LastUiCreateWidth = width;
            LastUiCreateHeight = height;
            return UiCreateWindowResult;
        }

        public override VmUiEvent UiPollEvent(int windowHandle)
        {
            LastUiPollHandle = windowHandle;
            return UiPollEventResult;
        }

        public override void UiWaitFrame(int windowHandle)
        {
            LastUiWaitFrameHandle = windowHandle;
        }

        public override VmUiWindowSize UiGetWindowSize(int windowHandle)
        {
            LastUiGetWindowSizeHandle = windowHandle;
            return UiGetWindowSizeResult;
        }

        public override void UiDrawLine(int windowHandle, int x1, int y1, int x2, int y2, string color, int strokeWidth)
        {
            LastUiLineHandle = windowHandle;
            LastUiLineX1 = x1;
            LastUiLineY1 = y1;
            LastUiLineX2 = x2;
            LastUiLineY2 = y2;
            LastUiLineColor = color;
            LastUiLineStrokeWidth = strokeWidth;
        }

        public override void UiDrawEllipse(int windowHandle, int x, int y, int width, int height, string color)
        {
            LastUiEllipseHandle = windowHandle;
            LastUiEllipseX = x;
            LastUiEllipseY = y;
            LastUiEllipseWidth = width;
            LastUiEllipseHeight = height;
            LastUiEllipseColor = color;
        }

        public override void UiDrawPath(int windowHandle, string path, string color, int strokeWidth)
        {
            LastUiPathHandle = windowHandle;
            LastUiPathValue = path;
            LastUiPathColor = color;
            LastUiPathStrokeWidth = strokeWidth;
        }

        public override void UiDrawImage(int windowHandle, int x, int y, int width, int height, string rgbaBase64)
        {
            LastUiImageHandle = windowHandle;
            LastUiImageX = x;
            LastUiImageY = y;
            LastUiImageWidth = width;
            LastUiImageHeight = height;
            LastUiImageRgbaBase64 = rgbaBase64;
        }
    }

    [Test]
    public void CompilerAssets_CanFindBundledCompilerFiles()
    {
        var path = AosCompilerAssets.TryFind("runtime.aos");
        Assert.That(path, Is.Not.Null);
        Assert.That(path!.EndsWith("runtime.aos", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void CompilerAssets_LoadRequiredProgram_ReturnsProgramRoot()
    {
        var program = AosCompilerAssets.LoadRequiredProgram("format.aos");
        Assert.That(program.Kind, Is.EqualTo("Program"));
        Assert.That(program.Children.Count, Is.GreaterThan(0));
    }

    [Test]
    public void TokenizerParser_ParsesBasicProgram()
    {
        var source = "Program#p1 { // root\n Let#l1(name=answer) { Lit#v1(value=1) } }";
        var parse = Parse(source);
        Assert.That(parse.Root, Is.Not.Null);
        Assert.That(parse.Diagnostics, Is.Empty);

        var program = parse.Root!;
        Assert.That(program.Kind, Is.EqualTo("Program"));
        Assert.That(program.Children.Count, Is.EqualTo(1));

        var letNode = program.Children[0];
        Assert.That(letNode.Kind, Is.EqualTo("Let"));
        Assert.That(letNode.Attrs["name"].AsString(), Is.EqualTo("answer"));
        Assert.That(letNode.Children.Count, Is.EqualTo(1));
        Assert.That(letNode.Children[0].Kind, Is.EqualTo("Lit"));
    }

    [Test]
    public void Formatter_RoundTrips()
    {
        var source = "Call#c1(target=console.print) { Lit#s1(value=\"hi\\nthere\") }";
        var parse1 = Parse(source);
        Assert.That(parse1.Diagnostics, Is.Empty);
        var formatted = AosFormatter.Format(parse1.Root!);
        var parse2 = Parse(formatted);
        Assert.That(parse2.Diagnostics, Is.Empty);
        var formatted2 = AosFormatter.Format(parse2.Root!);
        Assert.That(formatted2, Is.EqualTo(formatted));
    }

    [Test]
    public void Formatter_ProducesCanonicalOutput()
    {
        var source = "Call#c1(target=console.print) { Lit#s1(value=\"hi\") }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);
        var formatted = AosFormatter.Format(parse.Root!);
        Assert.That(formatted, Is.EqualTo("Call#c1(target=console.print) { Lit#s1(value=\"hi\") }"));
    }

    [Test]
    public void Formatter_SortsAttributesByKey()
    {
        var source = "Program#p1(z=1 a=2) { }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);
        var formatted = AosFormatter.Format(parse.Root!);
        Assert.That(formatted, Is.EqualTo("Program#p1(a=2 z=1)"));
    }

    [Test]
    public void Validator_DeniesConsolePermission()
    {
        var source = "Call#c1(target=console.print) { Lit#s1(value=\"hi\") }";
        var parse = Parse(source);
        var validator = new AosValidator();
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math" };
        var result = validator.Validate(parse.Root!, null, permissions);
        Assert.That(result.Diagnostics.Any(d => d.Code == "VAL040"), Is.True);
    }

    [Test]
    public void Validator_SyscallGroupPermission_UsesMappedGroup()
    {
        var source = "Call#c1(target=sys.time_nowUnixMs)";
        var parse = Parse(source);
        var validator = new AosValidator();

        var denied = validator.Validate(parse.Root!, null, new HashSet<string>(StringComparer.Ordinal) { "math" });
        Assert.That(denied.Diagnostics.Any(d => d.Code == "VAL040" && d.Message.Contains("'time'", StringComparison.Ordinal)), Is.True);

        var allowedByGroup = validator.Validate(parse.Root!, null, new HashSet<string>(StringComparer.Ordinal) { "time" });
        Assert.That(allowedByGroup.Diagnostics.Any(d => d.Code == "VAL040"), Is.False);

        var allowedByLegacy = validator.Validate(parse.Root!, null, new HashSet<string>(StringComparer.Ordinal) { "sys" });
        Assert.That(allowedByLegacy.Diagnostics.Any(d => d.Code == "VAL040"), Is.False);
    }

    [Test]
    public void Validator_ReportsMissingAttribute()
    {
        var source = "Let#l1 { Lit#v1(value=1) }";
        var parse = Parse(source);
        var validator = new AosValidator();
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math" };
        var result = validator.Validate(parse.Root!, null, permissions);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == "VAL002");
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Message, Is.EqualTo("Missing attribute 'name'."));
        Assert.That(diagnostic.NodeId, Is.EqualTo("l1"));
    }

    [Test]
    public void Validator_ReportsDuplicateIds()
    {
        var source = "Program#p1 { Lit#dup(value=1) Lit#dup(value=2) }";
        var parse = Parse(source);
        var validator = new AosValidator();
        var permissions = new HashSet<string>(StringComparer.Ordinal) { "math" };
        var result = validator.Validate(parse.Root!, null, permissions);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == "VAL001");
        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic!.Message, Is.EqualTo("Duplicate node id 'dup'."));
        Assert.That(diagnostic.NodeId, Is.EqualTo("dup"));
    }

    [Test]
    public void PatchOps_ReplaceAndInsert()
    {
        var program = Parse("Program#p1 { Let#l1(name=x) { Lit#v1(value=1) } }").Root!;
        var replaceOp = Parse("Op#o1(kind=replaceNode id=v1) { Lit#v2(value=2) }").Root!;
        var insertOp = Parse("Op#o2(kind=insertChild parentId=p1 slot=declarations index=1) { Let#l2(name=y) { Lit#v3(value=3) } }").Root!;

        var applier = new AosPatchApplier();
        var result = applier.Apply(program, new[] { replaceOp, insertOp });

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Root!.Children.Count, Is.EqualTo(2));
        var updatedLit = result.Root.Children[0].Children[0];
        Assert.That(updatedLit.Attrs["value"].AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void Repl_PersistsStateAcrossCommands()
    {
        var session = new AosReplSession();
        var load = "Cmd#c1(name=load) { Program#p1 { Let#l1(name=foo) { Lit#v1(value=5) } } }";
        var eval = "Cmd#c2(name=eval) { Var#v2(name=foo) }";

        var loadResult = session.ExecuteLine(load);
        Assert.That(loadResult.Contains("Ok#"), Is.True);

        var evalResult = session.ExecuteLine(eval);
        Assert.That(evalResult.Contains("type=int"), Is.True);
        Assert.That(evalResult.Contains("value=5"), Is.True);
    }

    [Test]
    public void Repl_HelpReturnsStructuredCommands()
    {
        var session = new AosReplSession();
        var help = "Cmd#c1(name=help)";
        var result = session.ExecuteLine(help);

        var parse = Parse(result);
        Assert.That(parse.Diagnostics, Is.Empty);
        Assert.That(parse.Root, Is.Not.Null);

        var ok = parse.Root!;
        Assert.That(ok.Kind, Is.EqualTo("Ok"));
        Assert.That(ok.Attrs["type"].AsString(), Is.EqualTo("void"));
        Assert.That(ok.Children.Count, Is.EqualTo(5));
        Assert.That(ok.Children.All(child => child.Kind == "Cmd"), Is.True);
        var names = ok.Children.Select(child => child.Attrs["name"].AsString()).ToList();
        var expected = new[] { "help", "setPerms", "load", "eval", "applyPatch" };
        Assert.That(names.Count, Is.EqualTo(expected.Length));
        Assert.That(expected.All(name => names.Contains(name)), Is.True);
    }

    [Test]
    public void CompilerBuiltins_ParseAndValidateWork()
    {
        var source = "Program#p1 { Call#c1(target=compiler.validate) { Call#c2(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Lit#l1(value=1) }\") } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, runtime.Permissions, runStructural: false);
        Assert.That(validation.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var diags = value.AsNode();
        Assert.That(diags.Kind, Is.EqualTo("Block"));
        Assert.That(diags.Children.Count, Is.EqualTo(0));
    }

    [Test]
    public void CompilerEmitBytecode_IncludesHeaderFields()
    {
        var source = "Program#p1 { Call#c1(target=compiler.emitBytecode) { Call#c2(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Lit#l1(value=1) }\") } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var bytecode = value.AsNode();
        Assert.That(bytecode.Kind, Is.EqualTo("Bytecode"));
        Assert.That(bytecode.Attrs["magic"].AsString(), Is.EqualTo("AIBC"));
        Assert.That(bytecode.Attrs["format"].AsString(), Is.EqualTo("AiBC1"));
        Assert.That(bytecode.Attrs["version"].AsInt(), Is.EqualTo(1));
        Assert.That(bytecode.Attrs["flags"].AsInt(), Is.EqualTo(0));
    }

    [Test]
    public void CompilerEmitBytecode_EncodesSyscallIdForCallSys()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Call#c1(target=sys.time_nowUnixMs) } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);
        var interpreter = new AosInterpreter();
        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var value = interpreter.EvaluateExpression(emitCall, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var bytecode = value.AsNode();
        var functionNode = bytecode.Children.FirstOrDefault(child =>
            child.Kind == "Func" &&
            child.Attrs.TryGetValue("name", out var nameAttr) &&
            nameAttr.Kind == AosAttrKind.Identifier &&
            nameAttr.AsString() == "start");
        Assert.That(functionNode, Is.Not.Null);
        var callSysInst = functionNode!.Children.FirstOrDefault(child =>
            child.Kind == "Inst" &&
            child.Attrs.TryGetValue("op", out var opAttr) &&
            opAttr.AsString() == "CALL_SYS");
        Assert.That(callSysInst, Is.Not.Null);
        Assert.That(callSysInst!.Attrs.TryGetValue("b", out var syscallIdAttr), Is.True);
        Assert.That(syscallIdAttr.Kind, Is.EqualTo(AosAttrKind.Int));
        Assert.That(syscallIdAttr.AsInt(), Is.GreaterThan(0));
    }

    [Test]
    public void VmRunBytecode_CallSys_PrefersSyscallIdOverStringTarget()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value=\"abc\") } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));
        var bytecode = bytecodeValue.AsNode();

        var startFuncIndex = bytecode.Children.FindIndex(child =>
            child.Kind == "Func" &&
            child.Attrs.TryGetValue("name", out var nameAttr) &&
            nameAttr.Kind == AosAttrKind.Identifier &&
            nameAttr.AsString() == "start");
        Assert.That(startFuncIndex, Is.GreaterThanOrEqualTo(0));

        var startFunc = bytecode.Children[startFuncIndex];
        var callSysIndex = startFunc.Children.FindIndex(child =>
            child.Kind == "Inst" &&
            child.Attrs.TryGetValue("op", out var opAttr) &&
            opAttr.AsString() == "CALL_SYS");
        Assert.That(callSysIndex, Is.GreaterThanOrEqualTo(0));

        var callSysInst = startFunc.Children[callSysIndex];
        Assert.That(callSysInst.Attrs.TryGetValue("b", out var idAttr), Is.True);
        Assert.That(idAttr.Kind, Is.EqualTo(AosAttrKind.Int));
        Assert.That(idAttr.AsInt(), Is.GreaterThan(0));

        var mutatedAttrs = new Dictionary<string, AosAttrValue>(callSysInst.Attrs, StringComparer.Ordinal)
        {
            ["s"] = new AosAttrValue(AosAttrKind.String, "sys.unknown")
        };
        startFunc.Children[callSysIndex] = new AosNode(callSysInst.Kind, callSysInst.Id, mutatedAttrs, callSysInst.Children, callSysInst.Span);

        bytecode.Children[startFuncIndex] = startFunc;

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecode, "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Int));
        Assert.That(result.AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void VmRunBytecode_AsyncCallSys_PrefersSyscallIdOverStringTarget()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Par#p2 { Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value=\"abc\") } Call#c2(target=sys.str_utf8ByteCount) { Lit#s2(value=\"de\") } } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));
        var bytecode = bytecodeValue.AsNode();

        var startFuncIndex = bytecode.Children.FindIndex(child =>
            child.Kind == "Func" &&
            child.Attrs.TryGetValue("name", out var nameAttr) &&
            nameAttr.Kind == AosAttrKind.Identifier &&
            nameAttr.AsString() == "start");
        Assert.That(startFuncIndex, Is.GreaterThanOrEqualTo(0));

        var startFunc = bytecode.Children[startFuncIndex];
        var asyncCallSysIndex = startFunc.Children.FindIndex(child =>
            child.Kind == "Inst" &&
            child.Attrs.TryGetValue("op", out var opAttr) &&
            opAttr.AsString() == "ASYNC_CALL_SYS");
        Assert.That(asyncCallSysIndex, Is.GreaterThanOrEqualTo(0));

        var asyncCallSysInst = startFunc.Children[asyncCallSysIndex];
        Assert.That(asyncCallSysInst.Attrs.TryGetValue("b", out var idAttr), Is.True);
        Assert.That(idAttr.Kind, Is.EqualTo(AosAttrKind.Int));
        Assert.That(idAttr.AsInt(), Is.GreaterThan(0));

        var mutatedAttrs = new Dictionary<string, AosAttrValue>(asyncCallSysInst.Attrs, StringComparer.Ordinal)
        {
            ["s"] = new AosAttrValue(AosAttrKind.String, "sys.unknown")
        };
        startFunc.Children[asyncCallSysIndex] = new AosNode(asyncCallSysInst.Kind, asyncCallSysInst.Id, mutatedAttrs, asyncCallSysInst.Children, asyncCallSysInst.Span);

        bytecode.Children[startFuncIndex] = startFunc;

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecode, "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
        var block = result.AsNode();
        Assert.That(block.Kind, Is.EqualTo("Block"));
        Assert.That(block.Children.Count, Is.EqualTo(2));
        Assert.That(block.Children[0].Attrs["value"].AsInt(), Is.EqualTo(3));
        Assert.That(block.Children[1].Attrs["value"].AsInt(), Is.EqualTo(2));
    }

    [Test]
    public void SyscallDispatch_Utf8Count_ReturnsInt()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value=\"abc\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
        Assert.That(value.AsInt(), Is.EqualTo(3));
    }

    [Test]
    public void SyscallDispatch_StrSubstring_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.str_substring) { Lit#s1(value=\"a😀bc\") Lit#i1(value=1) Lit#i2(value=2) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(value.AsString(), Is.EqualTo("😀b"));
    }

    [Test]
    public void SyscallDispatch_StrRemove_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.str_remove) { Lit#s1(value=\"a😀bc\") Lit#i1(value=1) Lit#i2(value=2) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(value.AsString(), Is.EqualTo("ac"));
    }

    [Test]
    public void VmSyscalls_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;

            var count = VmSyscalls.StrUtf8ByteCount("abc");
            VmSyscalls.StdoutWriteLine("hello");

            Assert.That(count, Is.EqualTo(777));
            Assert.That(host.LastStdoutLine, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_ConsoleWrite_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.ConsoleWrite("hello");
            Assert.That(host.LastConsoleWrite, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ConsoleWrite_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.console_write) { Lit#s1(value=\"hello\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastConsoleWrite, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_ConsoleWriteErrLine_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.ConsoleWriteErrLine("err-line");
            Assert.That(host.LastConsoleErrLine, Is.EqualTo("err-line"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ConsoleWriteErrLine_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.console_writeErrLine) { Lit#s1(value=\"err-line\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastConsoleErrLine, Is.EqualTo("err-line"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_IoReadLine_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { IoReadLineResult = "typed" };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.IoReadLine(), Is.EqualTo("typed"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ConsoleReadLine_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.console_readLine) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { IoReadLineResult = "typed-line" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("typed-line"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_IoReadAllStdin_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { IoReadAllStdinResult = "all-input" };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.IoReadAllStdin(), Is.EqualTo("all-input"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ConsoleReadAllStdin_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.console_readAllStdin) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { IoReadAllStdinResult = "all-stdin" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("all-stdin"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_ProcessArgv_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { ProcessArgvResult = new[] { "aic", "run", "sample.aos" } };
        try
        {
            VmSyscalls.Host = host;
            var argv = VmSyscalls.ProcessArgv();
            Assert.That(argv, Is.EqualTo(new[] { "aic", "run", "sample.aos" }));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ProcessArgv_ReturnsArgvNode()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.process_argv) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { ProcessArgvResult = new[] { "aic", "run", "sample.aos" } };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var argv = value.AsNode();
            Assert.That(argv.Kind, Is.EqualTo("Block"));
            Assert.That(argv.Children.Count, Is.EqualTo(3));
            Assert.That(argv.Children[0].Attrs["value"].AsString(), Is.EqualTo("aic"));
            Assert.That(argv.Children[1].Attrs["value"].AsString(), Is.EqualTo("run"));
            Assert.That(argv.Children[2].Attrs["value"].AsString(), Is.EqualTo("sample.aos"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_ProcessEnvGet_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { ProcessEnvGetResult = "v1" };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.ProcessEnvGet("NAME"), Is.EqualTo("v1"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ProcessEnvGet_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.process_envGet) { Lit#s1(value=\"NAME\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { ProcessEnvGetResult = "v1" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("v1"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_TimeSleepMs_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.TimeSleepMs(15);
            Assert.That(host.LastSleepMs, Is.EqualTo(15));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_TimeNowUnixMs_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { TimeNowUnixMsResult = 123456789 };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.TimeNowUnixMs(), Is.EqualTo(123456789));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_TimeMonotonicMs_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { TimeMonotonicMsResult = 1234 };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.TimeMonotonicMs(), Is.EqualTo(1234));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_TimeNowUnixMs_ReturnsInt()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.time_nowUnixMs) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { TimeNowUnixMsResult = 4242 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(4242));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_TimeNowUnixMs_AllowsGroupPermissionWithoutSys()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.time_nowUnixMs) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { TimeNowUnixMsResult = 9191 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("time");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(9191));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_TimeMonotonicMs_ReturnsInt()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.time_monotonicMs) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { TimeMonotonicMsResult = 5678 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(5678));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_UiWaitFrame_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.UiWaitFrame(22);
            Assert.That(host.LastUiWaitFrameHandle, Is.EqualTo(22));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_TimeSleepMs_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.time_sleepMs) { Lit#ms1(value=15) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastSleepMs, Is.EqualTo(15));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_FsMakeDir_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.FsMakeDir("new-dir");
            Assert.That(host.LastFsMakeDirPath, Is.EqualTo("new-dir"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_FsMakeDir_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.fs_makeDir) { Lit#s1(value=\"new-dir\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastFsMakeDirPath, Is.EqualTo("new-dir"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_FsPathExists_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsPathExistsResult = true };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.FsPathExists("x"), Is.True);
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_FsReadDir_UsesConfiguredHost_AndSortsEntries()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsReadDirResult = new[] { "zeta", "alpha", "beta" } };
        try
        {
            VmSyscalls.Host = host;
            var entries = VmSyscalls.FsReadDir("x");
            Assert.That(host.LastFsReadDirPath, Is.EqualTo("x"));
            Assert.That(entries, Is.EqualTo(new[] { "alpha", "beta", "zeta" }));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_FsStat_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsStatResult = new VmFsStat("file", 12, 99) };
        try
        {
            VmSyscalls.Host = host;
            var stat = VmSyscalls.FsStat("x");
            Assert.That(host.LastFsStatPath, Is.EqualTo("x"));
            Assert.That(stat.Type, Is.EqualTo("file"));
            Assert.That(stat.Size, Is.EqualTo(12));
            Assert.That(stat.MtimeUnixMs, Is.EqualTo(99));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_FsReadDir_ReturnsNode()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.fs_readDir) { Lit#s1(value=\"x\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsReadDirResult = new[] { "zeta", "alpha", "beta" } };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var entries = value.AsNode();
            Assert.That(entries.Kind, Is.EqualTo("Block"));
            Assert.That(entries.Children.Count, Is.EqualTo(3));
            Assert.That(entries.Children[0].Attrs["value"].AsString(), Is.EqualTo("alpha"));
            Assert.That(entries.Children[1].Attrs["value"].AsString(), Is.EqualTo("beta"));
            Assert.That(entries.Children[2].Attrs["value"].AsString(), Is.EqualTo("zeta"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_FsStat_ReturnsNode()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.fs_stat) { Lit#s1(value=\"x\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsStatResult = new VmFsStat("file", 12, 99) };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var stat = value.AsNode();
            Assert.That(stat.Kind, Is.EqualTo("Stat"));
            Assert.That(stat.Attrs["type"].AsString(), Is.EqualTo("file"));
            Assert.That(stat.Attrs["size"].AsInt(), Is.EqualTo(12));
            Assert.That(stat.Attrs["mtime"].AsInt(), Is.EqualTo(99));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallRegistry_ResolvesTcpAliases()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.net_tcpListen", out var listenId), Is.True);
        Assert.That(listenId, Is.EqualTo(SyscallId.NetTcpListen));
        Assert.That(SyscallRegistry.TryResolve("sys.net_tcpConnect", out var connectId), Is.True);
        Assert.That(connectId, Is.EqualTo(SyscallId.NetTcpConnect));
        Assert.That(SyscallRegistry.TryResolve("sys.net_tcpConnectTls", out var connectTlsId), Is.True);
        Assert.That(connectTlsId, Is.EqualTo(SyscallId.NetTcpConnectTls));
        Assert.That(SyscallRegistry.TryResolve("sys.net_tcpAccept", out var acceptId), Is.True);
        Assert.That(acceptId, Is.EqualTo(SyscallId.NetTcpAccept));
        Assert.That(SyscallRegistry.TryResolve("sys.net_tcpRead", out var readId), Is.True);
        Assert.That(readId, Is.EqualTo(SyscallId.NetTcpRead));
        Assert.That(SyscallRegistry.TryResolve("sys.net_tcpWrite", out var writeId), Is.True);
        Assert.That(writeId, Is.EqualTo(SyscallId.NetTcpWrite));
    }

    [Test]
    public void SyscallRegistry_ResolvesUiShapeAliases()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.ui_drawLine", out var lineId), Is.True);
        Assert.That(lineId, Is.EqualTo(SyscallId.UiDrawLine));
        Assert.That(SyscallRegistry.TryResolve("sys.ui_drawEllipse", out var ellipseId), Is.True);
        Assert.That(ellipseId, Is.EqualTo(SyscallId.UiDrawEllipse));
        Assert.That(SyscallRegistry.TryResolve("sys.ui_drawPath", out var pathId), Is.True);
        Assert.That(pathId, Is.EqualTo(SyscallId.UiDrawPath));
        Assert.That(SyscallRegistry.TryResolve("sys.ui_drawImage", out var imageId), Is.True);
        Assert.That(imageId, Is.EqualTo(SyscallId.UiDrawImage));
        Assert.That(SyscallRegistry.TryResolve("sys.ui_waitFrame", out var waitFrameId), Is.True);
        Assert.That(waitFrameId, Is.EqualTo(SyscallId.UiWaitFrame));
    }

    [Test]
    public void SyscallRegistry_ResolvesWorkerAliases()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.worker_start", out var workerStartId), Is.True);
        Assert.That(workerStartId, Is.EqualTo(SyscallId.WorkerStart));
        Assert.That(SyscallRegistry.TryResolve("sys.worker_poll", out var workerPollId), Is.True);
        Assert.That(workerPollId, Is.EqualTo(SyscallId.WorkerPoll));
        Assert.That(SyscallRegistry.TryResolve("sys.worker_result", out var workerResultId), Is.True);
        Assert.That(workerResultId, Is.EqualTo(SyscallId.WorkerResult));
        Assert.That(SyscallRegistry.TryResolve("sys.worker_error", out var workerErrorId), Is.True);
        Assert.That(workerErrorId, Is.EqualTo(SyscallId.WorkerError));
        Assert.That(SyscallRegistry.TryResolve("sys.worker_cancel", out var workerCancelId), Is.True);
        Assert.That(workerCancelId, Is.EqualTo(SyscallId.WorkerCancel));
    }

    [Test]
    public void SyscallRegistry_ResolvesDebugAliases()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.debug_emit", out var debugEmitId), Is.True);
        Assert.That(debugEmitId, Is.EqualTo(SyscallId.DebugEmit));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_mode", out var debugModeId), Is.True);
        Assert.That(debugModeId, Is.EqualTo(SyscallId.DebugMode));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_captureFrameBegin", out var debugFrameBeginId), Is.True);
        Assert.That(debugFrameBeginId, Is.EqualTo(SyscallId.DebugCaptureFrameBegin));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_captureFrameEnd", out var debugFrameEndId), Is.True);
        Assert.That(debugFrameEndId, Is.EqualTo(SyscallId.DebugCaptureFrameEnd));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_captureDraw", out var debugDrawId), Is.True);
        Assert.That(debugDrawId, Is.EqualTo(SyscallId.DebugCaptureDraw));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_captureInput", out var debugInputId), Is.True);
        Assert.That(debugInputId, Is.EqualTo(SyscallId.DebugCaptureInput));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_captureState", out var debugStateId), Is.True);
        Assert.That(debugStateId, Is.EqualTo(SyscallId.DebugCaptureState));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_replayLoad", out var debugReplayLoadId), Is.True);
        Assert.That(debugReplayLoadId, Is.EqualTo(SyscallId.DebugReplayLoad));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_replayNext", out var debugReplayNextId), Is.True);
        Assert.That(debugReplayNextId, Is.EqualTo(SyscallId.DebugReplayNext));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_assert", out var debugAssertId), Is.True);
        Assert.That(debugAssertId, Is.EqualTo(SyscallId.DebugAssert));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_artifactWrite", out var debugArtifactWriteId), Is.True);
        Assert.That(debugArtifactWriteId, Is.EqualTo(SyscallId.DebugArtifactWrite));
        Assert.That(SyscallRegistry.TryResolve("sys.debug_traceAsync", out var debugTraceAsyncId), Is.True);
        Assert.That(debugTraceAsyncId, Is.EqualTo(SyscallId.DebugTraceAsync));
    }

    [Test]
    public void SyscallDispatch_NetTcpListen_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.net_tcpListen) { Lit#h1(value=\"127.0.0.1\") Lit#p1(value=4040) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(77));
            Assert.That(host.LastNetTcpListenHost, Is.EqualTo("127.0.0.1"));
            Assert.That(host.LastNetTcpListenPort, Is.EqualTo(4040));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_NetTcpConnect_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.net_tcpConnect) { Lit#h1(value=\"example.com\") Lit#p1(value=80) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { NetTcpConnectResult = 91 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(91));
            Assert.That(host.LastNetTcpConnectHost, Is.EqualTo("example.com"));
            Assert.That(host.LastNetTcpConnectPort, Is.EqualTo(80));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_NetTcpConnectTls_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.net_tcpConnectTls) { Lit#h1(value=\"example.com\") Lit#p1(value=443) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { NetTcpConnectTlsResult = 92 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(92));
            Assert.That(host.LastNetTcpConnectTlsHost, Is.EqualTo("example.com"));
            Assert.That(host.LastNetTcpConnectTlsPort, Is.EqualTo(443));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_NetTcpReadAndWrite_CallHost()
    {
        var parse = Parse("Program#p1 { Let#l1(name=r) { Call#c1(target=sys.net_tcpRead) { Lit#h1(value=10) Lit#m1(value=32) } } Call#c2(target=sys.net_tcpWrite) { Lit#h2(value=10) Lit#d1(value=\"hello\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            LastNetTcpReadPayload = "payload",
            NetTcpWriteResult = 5
        };

        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(5));
            Assert.That(host.LastNetTcpReadHandle, Is.EqualTo(10));
            Assert.That(host.LastNetTcpReadMaxBytes, Is.EqualTo(32));
            Assert.That(host.LastNetTcpWriteHandle, Is.EqualTo(10));
            Assert.That(host.LastNetTcpWriteData, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_NetUdpBindAndSend_CallHost()
    {
        var parse = Parse("Program#p1 { Let#l1(name=h) { Call#c1(target=sys.net_udpBind) { Lit#h1(value=\"127.0.0.1\") Lit#p1(value=9090) } } Call#c2(target=sys.net_udpSend) { Var#v1(name=h) Lit#h2(value=\"127.0.0.1\") Lit#p2(value=9999) Lit#d1(value=\"ping\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { NetUdpBindResult = 22, NetUdpSendResult = 4 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("net");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(4));
            Assert.That(host.LastNetUdpBindHost, Is.EqualTo("127.0.0.1"));
            Assert.That(host.LastNetUdpBindPort, Is.EqualTo(9090));
            Assert.That(host.LastNetUdpSendHandle, Is.EqualTo(22));
            Assert.That(host.LastNetUdpSendHost, Is.EqualTo("127.0.0.1"));
            Assert.That(host.LastNetUdpSendPort, Is.EqualTo(9999));
            Assert.That(host.LastNetUdpSendData, Is.EqualTo("ping"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_NetUdpRecv_ReturnsNode()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.net_udpRecv) { Lit#h1(value=22) Lit#m1(value=64) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { NetUdpRecvResult = new VmUdpPacket("127.0.0.1", 4444, "pong") };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("net");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var packet = value.AsNode();
            Assert.That(packet.Kind, Is.EqualTo("UdpPacket"));
            Assert.That(packet.Attrs["host"].AsString(), Is.EqualTo("127.0.0.1"));
            Assert.That(packet.Attrs["port"].AsInt(), Is.EqualTo(4444));
            Assert.That(packet.Attrs["data"].AsString(), Is.EqualTo("pong"));
            Assert.That(host.LastNetUdpRecvHandle, Is.EqualTo(22));
            Assert.That(host.LastNetUdpRecvMaxBytes, Is.EqualTo(64));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiCreateWindow_ReturnsHandle()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_createWindow) { Lit#t1(value=\"demo\") Lit#w1(value=800) Lit#h1(value=600) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { UiCreateWindowResult = 9 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(9));
            Assert.That(host.LastUiCreateTitle, Is.EqualTo("demo"));
            Assert.That(host.LastUiCreateWidth, Is.EqualTo(800));
            Assert.That(host.LastUiCreateHeight, Is.EqualTo(600));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiPollEvent_ReturnsNode()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_pollEvent) { Lit#h1(value=9) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            UiPollEventResult = new VmUiEvent("click", "button_1", 10, 20, string.Empty, string.Empty, string.Empty, false)
        };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var uiEvent = value.AsNode();
            Assert.That(uiEvent.Kind, Is.EqualTo("UiEvent"));
            Assert.That(uiEvent.Attrs["type"].AsString(), Is.EqualTo("click"));
            Assert.That(uiEvent.Attrs["targetId"].AsString(), Is.EqualTo("button_1"));
            Assert.That(uiEvent.Attrs["x"].AsInt(), Is.EqualTo(10));
            Assert.That(uiEvent.Attrs["y"].AsInt(), Is.EqualTo(20));
            Assert.That(uiEvent.Attrs["key"].AsString(), Is.EqualTo(string.Empty));
            Assert.That(uiEvent.Attrs["text"].AsString(), Is.EqualTo(string.Empty));
            Assert.That(uiEvent.Attrs["modifiers"].AsString(), Is.EqualTo(string.Empty));
            Assert.That(uiEvent.Attrs["repeat"].AsBool(), Is.False);
            Assert.That(host.LastUiPollHandle, Is.EqualTo(9));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiPollEvent_KeyPayload_IsCanonicalized()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_pollEvent) { Lit#h1(value=9) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            UiPollEventResult = new VmUiEvent("key", "input_1", 123, 456, "ArrowLeft", "A", "shift,ctrl,shift,invalid", true)
        };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var uiEvent = value.AsNode();
            Assert.That(uiEvent.Attrs["type"].AsString(), Is.EqualTo("key"));
            Assert.That(uiEvent.Attrs["targetId"].AsString(), Is.EqualTo("input_1"));
            Assert.That(uiEvent.Attrs["x"].AsInt(), Is.EqualTo(-1));
            Assert.That(uiEvent.Attrs["y"].AsInt(), Is.EqualTo(-1));
            Assert.That(uiEvent.Attrs["key"].AsString(), Is.EqualTo("left"));
            Assert.That(uiEvent.Attrs["text"].AsString(), Is.EqualTo("A"));
            Assert.That(uiEvent.Attrs["modifiers"].AsString(), Is.EqualTo("ctrl,shift"));
            Assert.That(uiEvent.Attrs["repeat"].AsBool(), Is.True);
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiPollEvent_DerivesKeyFromPrintableText_WhenMissing()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_pollEvent) { Lit#h1(value=9) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            UiPollEventResult = new VmUiEvent("key", string.Empty, -1, -1, string.Empty, "`", string.Empty, false)
        };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var uiEvent = value.AsNode();
            Assert.That(uiEvent.Attrs["key"].AsString(), Is.EqualTo("`"));
            Assert.That(uiEvent.Attrs["text"].AsString(), Is.EqualTo("`"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiGetWindowSize_ReturnsNode()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_getWindowSize) { Lit#h1(value=9) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            UiGetWindowSizeResult = new VmUiWindowSize(1024, 768)
        };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
            var size = value.AsNode();
            Assert.That(size.Kind, Is.EqualTo("UiWindowSize"));
            Assert.That(size.Attrs["width"].AsInt(), Is.EqualTo(1024));
            Assert.That(size.Attrs["height"].AsInt(), Is.EqualTo(768));
            Assert.That(host.LastUiGetWindowSizeHandle, Is.EqualTo(9));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiWaitFrame_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_waitFrame) { Lit#h1(value=9) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastUiWaitFrameHandle, Is.EqualTo(9));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiDrawLine_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_drawLine) { Lit#h1(value=9) Lit#x1(value=10) Lit#y1(value=20) Lit#x2(value=30) Lit#y2(value=40) Lit#c1(value=\"#ff0000\") Lit#s1(value=3) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastUiLineHandle, Is.EqualTo(9));
            Assert.That(host.LastUiLineX1, Is.EqualTo(10));
            Assert.That(host.LastUiLineY1, Is.EqualTo(20));
            Assert.That(host.LastUiLineX2, Is.EqualTo(30));
            Assert.That(host.LastUiLineY2, Is.EqualTo(40));
            Assert.That(host.LastUiLineColor, Is.EqualTo("#ff0000"));
            Assert.That(host.LastUiLineStrokeWidth, Is.EqualTo(3));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiDrawEllipse_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_drawEllipse) { Lit#h1(value=9) Lit#x1(value=11) Lit#y1(value=22) Lit#w1(value=33) Lit#h2(value=44) Lit#c1(value=\"blue\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastUiEllipseHandle, Is.EqualTo(9));
            Assert.That(host.LastUiEllipseX, Is.EqualTo(11));
            Assert.That(host.LastUiEllipseY, Is.EqualTo(22));
            Assert.That(host.LastUiEllipseWidth, Is.EqualTo(33));
            Assert.That(host.LastUiEllipseHeight, Is.EqualTo(44));
            Assert.That(host.LastUiEllipseColor, Is.EqualTo("blue"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiDrawPath_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_drawPath) { Lit#h1(value=9) Lit#p1(value=\"10,20;30,40\") Lit#c1(value=\"#00ff00\") Lit#s1(value=2) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastUiPathHandle, Is.EqualTo(9));
            Assert.That(host.LastUiPathValue, Is.EqualTo("10,20;30,40"));
            Assert.That(host.LastUiPathColor, Is.EqualTo("#00ff00"));
            Assert.That(host.LastUiPathStrokeWidth, Is.EqualTo(2));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_UiDrawImage_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.ui_drawImage) { Lit#h1(value=9) Lit#x1(value=12) Lit#y1(value=34) Lit#w1(value=2) Lit#h2(value=1) Lit#d1(value=\"AQIDBAUGBwg=\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("ui");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastUiImageHandle, Is.EqualTo(9));
            Assert.That(host.LastUiImageX, Is.EqualTo(12));
            Assert.That(host.LastUiImageY, Is.EqualTo(34));
            Assert.That(host.LastUiImageWidth, Is.EqualTo(2));
            Assert.That(host.LastUiImageHeight, Is.EqualTo(1));
            Assert.That(host.LastUiImageRgbaBase64, Is.EqualTo("AQIDBAUGBwg="));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_CryptoBase64Encode_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.crypto_base64Encode) { Lit#s1(value=\"hello\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { CryptoBase64EncodeResult = "aGVsbG8=" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("crypto");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("aGVsbG8="));
            Assert.That(host.LastCryptoBase64EncodeInput, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_CryptoBase64Decode_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.crypto_base64Decode) { Lit#s1(value=\"aGVsbG8=\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { CryptoBase64DecodeResult = "hello" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("crypto");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("hello"));
            Assert.That(host.LastCryptoBase64DecodeInput, Is.EqualTo("aGVsbG8="));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_CryptoSha1_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.crypto_sha1) { Lit#s1(value=\"hello\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { CryptoSha1Result = "sha1hex" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("crypto");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("sha1hex"));
            Assert.That(host.LastCryptoSha1Input, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_CryptoSha256_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.crypto_sha256) { Lit#s1(value=\"hello\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { CryptoSha256Result = "sha256hex" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("crypto");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("sha256hex"));
            Assert.That(host.LastCryptoSha256Input, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_CryptoHmacSha256_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.crypto_hmacSha256) { Lit#k1(value=\"secret\") Lit#s1(value=\"hello\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { CryptoHmacSha256Result = "hmachex" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("crypto");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("hmachex"));
            Assert.That(host.LastCryptoHmacSha256Key, Is.EqualTo("secret"));
            Assert.That(host.LastCryptoHmacSha256Text, Is.EqualTo("hello"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_CryptoRandomBytes_CallsHost()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.crypto_randomBytes) { Lit#n1(value=8) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { CryptoRandomBytesResult = "AQIDBAUGBwg=" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("crypto");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("AQIDBAUGBwg="));
            Assert.That(host.LastCryptoRandomBytesCount, Is.EqualTo(8));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_FsReadDir_IsWired()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.fs_readDir", out var syscallId), Is.True);
        Assert.That(VmSyscallDispatcher.TryGetExpectedArity(syscallId, out var arity), Is.True);
        Assert.That(arity, Is.EqualTo(1));

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsReadDirResult = new[] { "x" } };
        try
        {
            VmSyscalls.Host = host;
            var invoked = VmSyscallDispatcher.TryInvoke(
                syscallId,
                new[] { SysValue.String("path") }.AsSpan(),
                new VmNetworkState(),
                out var result);
            Assert.That(invoked, Is.True);
            Assert.That(host.LastFsReadDirPath, Is.EqualTo("path"));
            Assert.That(result.Kind, Is.EqualTo(VmValueKind.Unknown));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_FsStat_IsWired()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.fs_stat", out var syscallId), Is.True);
        Assert.That(VmSyscallDispatcher.TryGetExpectedArity(syscallId, out var arity), Is.True);
        Assert.That(arity, Is.EqualTo(1));

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsStatResult = new VmFsStat("file", 12, 99) };
        try
        {
            VmSyscalls.Host = host;
            var invoked = VmSyscallDispatcher.TryInvoke(
                syscallId,
                new[] { SysValue.String("path") }.AsSpan(),
                new VmNetworkState(),
                out var result);
            Assert.That(invoked, Is.True);
            Assert.That(host.LastFsStatPath, Is.EqualTo("path"));
            Assert.That(result.Kind, Is.EqualTo(VmValueKind.Unknown));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_NetUdpRecv_IsWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            NetUdpRecvResult = new VmUdpPacket("127.0.0.1", 9000, "data")
        };
        try
        {
            VmSyscalls.Host = host;
            var invoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.NetUdpRecv,
                new[] { SysValue.Int(12), SysValue.Int(64) }.AsSpan(),
                new VmNetworkState(),
                out var result);
            Assert.That(invoked, Is.True);
            Assert.That(result.Kind, Is.EqualTo(VmValueKind.Unknown));
            Assert.That(host.LastNetUdpRecvHandle, Is.EqualTo(12));
            Assert.That(host.LastNetUdpRecvMaxBytes, Is.EqualTo(64));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_UiPollEvent_IsWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            UiPollEventResult = new VmUiEvent("none", string.Empty, 0, 0, string.Empty, string.Empty, string.Empty, false)
        };
        try
        {
            VmSyscalls.Host = host;
            var invoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiPollEvent,
                new[] { SysValue.Int(9) }.AsSpan(),
                new VmNetworkState(),
                out var result);
            Assert.That(invoked, Is.True);
            Assert.That(result.Kind, Is.EqualTo(VmValueKind.Unknown));
            Assert.That(host.LastUiPollHandle, Is.EqualTo(9));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_UiWaitFrame_IsWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var invoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiWaitFrame,
                new[] { SysValue.Int(9) }.AsSpan(),
                new VmNetworkState(),
                out var result);
            Assert.That(invoked, Is.True);
            Assert.That(result.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastUiWaitFrameHandle, Is.EqualTo(9));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_WorkerSyscalls_AreWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            WorkerStartResult = 41,
            WorkerPollResult = 1,
            WorkerResultValue = "ok",
            WorkerErrorValue = string.Empty,
            WorkerCancelResult = true
        };
        try
        {
            VmSyscalls.Host = host;
            var state = new VmNetworkState();

            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.WorkerStart, out var startArity), Is.True);
            Assert.That(startArity, Is.EqualTo(2));
            var started = VmSyscallDispatcher.TryInvoke(
                SyscallId.WorkerStart,
                new[] { SysValue.String("echo"), SysValue.String("payload") }.AsSpan(),
                state,
                out var startResult);
            Assert.That(started, Is.True);
            Assert.That(startResult.Kind, Is.EqualTo(VmValueKind.Int));
            Assert.That(startResult.IntValue, Is.EqualTo(41));
            Assert.That(host.LastWorkerTaskName, Is.EqualTo("echo"));
            Assert.That(host.LastWorkerPayload, Is.EqualTo("payload"));

            var polled = VmSyscallDispatcher.TryInvoke(
                SyscallId.WorkerPoll,
                new[] { SysValue.Int(41) }.AsSpan(),
                state,
                out var pollResult);
            Assert.That(polled, Is.True);
            Assert.That(pollResult.Kind, Is.EqualTo(VmValueKind.Int));
            Assert.That(pollResult.IntValue, Is.EqualTo(1));
            Assert.That(host.LastWorkerPollHandle, Is.EqualTo(41));

            var fetchedResult = VmSyscallDispatcher.TryInvoke(
                SyscallId.WorkerResult,
                new[] { SysValue.Int(41) }.AsSpan(),
                state,
                out var workerResult);
            Assert.That(fetchedResult, Is.True);
            Assert.That(workerResult.Kind, Is.EqualTo(VmValueKind.String));
            Assert.That(workerResult.StringValue, Is.EqualTo("ok"));
            Assert.That(host.LastWorkerResultHandle, Is.EqualTo(41));

            var fetchedError = VmSyscallDispatcher.TryInvoke(
                SyscallId.WorkerError,
                new[] { SysValue.Int(41) }.AsSpan(),
                state,
                out var workerError);
            Assert.That(fetchedError, Is.True);
            Assert.That(workerError.Kind, Is.EqualTo(VmValueKind.String));
            Assert.That(workerError.StringValue, Is.EqualTo(string.Empty));
            Assert.That(host.LastWorkerErrorHandle, Is.EqualTo(41));

            var cancelled = VmSyscallDispatcher.TryInvoke(
                SyscallId.WorkerCancel,
                new[] { SysValue.Int(41) }.AsSpan(),
                state,
                out var cancelResult);
            Assert.That(cancelled, Is.True);
            Assert.That(cancelResult.Kind, Is.EqualTo(VmValueKind.Bool));
            Assert.That(cancelResult.BoolValue, Is.True);
            Assert.That(host.LastWorkerCancelHandle, Is.EqualTo(41));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_DebugSyscalls_AreWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            DebugModeValue = "live",
            DebugReplayLoadResult = 73,
            DebugReplayNextValue = "event-1",
            DebugArtifactWriteResult = true
        };
        try
        {
            VmSyscalls.Host = host;
            var state = new VmNetworkState();

            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugEmit, out var emitArity), Is.True);
            Assert.That(emitArity, Is.EqualTo(2));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugMode, out var modeArity), Is.True);
            Assert.That(modeArity, Is.EqualTo(0));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugCaptureFrameBegin, out var frameBeginArity), Is.True);
            Assert.That(frameBeginArity, Is.EqualTo(3));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugCaptureFrameEnd, out var frameEndArity), Is.True);
            Assert.That(frameEndArity, Is.EqualTo(1));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugCaptureDraw, out var drawArity), Is.True);
            Assert.That(drawArity, Is.EqualTo(2));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugCaptureInput, out var inputArity), Is.True);
            Assert.That(inputArity, Is.EqualTo(1));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugCaptureState, out var stateArity), Is.True);
            Assert.That(stateArity, Is.EqualTo(2));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugReplayLoad, out var replayLoadArity), Is.True);
            Assert.That(replayLoadArity, Is.EqualTo(1));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugReplayNext, out var replayNextArity), Is.True);
            Assert.That(replayNextArity, Is.EqualTo(1));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugAssert, out var assertArity), Is.True);
            Assert.That(assertArity, Is.EqualTo(3));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugArtifactWrite, out var artifactArity), Is.True);
            Assert.That(artifactArity, Is.EqualTo(2));
            Assert.That(VmSyscallDispatcher.TryGetExpectedArity(SyscallId.DebugTraceAsync, out var traceArity), Is.True);
            Assert.That(traceArity, Is.EqualTo(3));

            var emitted = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugEmit,
                new[] { SysValue.String("event"), SysValue.String("payload") }.AsSpan(),
                state,
                out var emitResult);
            Assert.That(emitted, Is.True);
            Assert.That(emitResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugEmitChannel, Is.EqualTo("event"));
            Assert.That(host.LastDebugEmitPayload, Is.EqualTo("payload"));

            var mode = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugMode,
                ReadOnlySpan<SysValue>.Empty,
                state,
                out var modeResult);
            Assert.That(mode, Is.True);
            Assert.That(modeResult.Kind, Is.EqualTo(VmValueKind.String));
            Assert.That(modeResult.StringValue, Is.EqualTo("live"));

            var frameBegin = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugCaptureFrameBegin,
                new[] { SysValue.Int(7), SysValue.Int(640), SysValue.Int(480) }.AsSpan(),
                state,
                out var frameBeginResult);
            Assert.That(frameBegin, Is.True);
            Assert.That(frameBeginResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugCaptureFrameBeginId, Is.EqualTo(7));
            Assert.That(host.LastDebugCaptureFrameBeginWidth, Is.EqualTo(640));
            Assert.That(host.LastDebugCaptureFrameBeginHeight, Is.EqualTo(480));

            var frameEnd = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugCaptureFrameEnd,
                new[] { SysValue.Int(7) }.AsSpan(),
                state,
                out var frameEndResult);
            Assert.That(frameEnd, Is.True);
            Assert.That(frameEndResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugCaptureFrameEndId, Is.EqualTo(7));

            var draw = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugCaptureDraw,
                new[] { SysValue.String("rect"), SysValue.String("x=1,y=2") }.AsSpan(),
                state,
                out var drawResult);
            Assert.That(draw, Is.True);
            Assert.That(drawResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugCaptureDrawOp, Is.EqualTo("rect"));
            Assert.That(host.LastDebugCaptureDrawArgs, Is.EqualTo("x=1,y=2"));

            var input = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugCaptureInput,
                new[] { SysValue.String("key:<enter>") }.AsSpan(),
                state,
                out var inputResult);
            Assert.That(input, Is.True);
            Assert.That(inputResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugCaptureInputPayload, Is.EqualTo("key:<enter>"));

            var captureState = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugCaptureState,
                new[] { SysValue.String("focus"), SysValue.String("nameField") }.AsSpan(),
                state,
                out var captureStateResult);
            Assert.That(captureState, Is.True);
            Assert.That(captureStateResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugCaptureStateKey, Is.EqualTo("focus"));
            Assert.That(host.LastDebugCaptureStatePayload, Is.EqualTo("nameField"));

            var replayLoaded = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugReplayLoad,
                new[] { SysValue.String("/tmp/replay.log") }.AsSpan(),
                state,
                out var replayLoadResult);
            Assert.That(replayLoaded, Is.True);
            Assert.That(replayLoadResult.Kind, Is.EqualTo(VmValueKind.Int));
            Assert.That(replayLoadResult.IntValue, Is.EqualTo(73));
            Assert.That(host.LastDebugReplayLoadPath, Is.EqualTo("/tmp/replay.log"));

            var replayNext = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugReplayNext,
                new[] { SysValue.Int(73) }.AsSpan(),
                state,
                out var replayNextResult);
            Assert.That(replayNext, Is.True);
            Assert.That(replayNextResult.Kind, Is.EqualTo(VmValueKind.String));
            Assert.That(replayNextResult.StringValue, Is.EqualTo("event-1"));
            Assert.That(host.LastDebugReplayNextHandle, Is.EqualTo(73));

            var asserted = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugAssert,
                new[] { SysValue.Bool(true), SysValue.String("DBGX"), SysValue.String("ok") }.AsSpan(),
                state,
                out var assertResult);
            Assert.That(asserted, Is.True);
            Assert.That(assertResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugAssertCondition, Is.True);
            Assert.That(host.LastDebugAssertCode, Is.EqualTo("DBGX"));
            Assert.That(host.LastDebugAssertMessage, Is.EqualTo("ok"));

            var artifactWritten = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugArtifactWrite,
                new[] { SysValue.String("/tmp/out.scene"), SysValue.String("frame=1") }.AsSpan(),
                state,
                out var artifactResult);
            Assert.That(artifactWritten, Is.True);
            Assert.That(artifactResult.Kind, Is.EqualTo(VmValueKind.Bool));
            Assert.That(artifactResult.BoolValue, Is.True);
            Assert.That(host.LastDebugArtifactWritePath, Is.EqualTo("/tmp/out.scene"));
            Assert.That(host.LastDebugArtifactWriteText, Is.EqualTo("frame=1"));

            var traced = VmSyscallDispatcher.TryInvoke(
                SyscallId.DebugTraceAsync,
                new[] { SysValue.Int(99), SysValue.String("done"), SysValue.String("ok") }.AsSpan(),
                state,
                out var traceResult);
            Assert.That(traced, Is.True);
            Assert.That(traceResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastDebugTraceAsyncOpId, Is.EqualTo(99));
            Assert.That(host.LastDebugTraceAsyncPhase, Is.EqualTo("done"));
            Assert.That(host.LastDebugTraceAsyncDetail, Is.EqualTo("ok"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_UiGetWindowSize_IsWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            UiGetWindowSizeResult = new VmUiWindowSize(640, 480)
        };
        try
        {
            VmSyscalls.Host = host;
            var invoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiGetWindowSize,
                new[] { SysValue.Int(9) }.AsSpan(),
                new VmNetworkState(),
                out var result);
            Assert.That(invoked, Is.True);
            Assert.That(result.Kind, Is.EqualTo(VmValueKind.Unknown));
            Assert.That(host.LastUiGetWindowSizeHandle, Is.EqualTo(9));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_UiShapeSyscalls_AreWired()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;

            var lineInvoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiDrawLine,
                new[] { SysValue.Int(9), SysValue.Int(10), SysValue.Int(20), SysValue.Int(30), SysValue.Int(40), SysValue.String("red"), SysValue.Int(2) }.AsSpan(),
                new VmNetworkState(),
                out var lineResult);
            Assert.That(lineInvoked, Is.True);
            Assert.That(lineResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastUiLineHandle, Is.EqualTo(9));
            Assert.That(host.LastUiLineStrokeWidth, Is.EqualTo(2));

            var ellipseInvoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiDrawEllipse,
                new[] { SysValue.Int(7), SysValue.Int(1), SysValue.Int(2), SysValue.Int(3), SysValue.Int(4), SysValue.String("#fff") }.AsSpan(),
                new VmNetworkState(),
                out var ellipseResult);
            Assert.That(ellipseInvoked, Is.True);
            Assert.That(ellipseResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastUiEllipseHandle, Is.EqualTo(7));
            Assert.That(host.LastUiEllipseWidth, Is.EqualTo(3));

            var pathInvoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiDrawPath,
                new[] { SysValue.Int(5), SysValue.String("1,1;2,2"), SysValue.String("green"), SysValue.Int(4) }.AsSpan(),
                new VmNetworkState(),
                out var pathResult);
            Assert.That(pathInvoked, Is.True);
            Assert.That(pathResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastUiPathHandle, Is.EqualTo(5));
            Assert.That(host.LastUiPathValue, Is.EqualTo("1,1;2,2"));
            Assert.That(host.LastUiPathStrokeWidth, Is.EqualTo(4));

            var imageInvoked = VmSyscallDispatcher.TryInvoke(
                SyscallId.UiDrawImage,
                new[] { SysValue.Int(3), SysValue.Int(8), SysValue.Int(9), SysValue.Int(1), SysValue.Int(1), SysValue.String("AQIDBA==") }.AsSpan(),
                new VmNetworkState(),
                out var imageResult);
            Assert.That(imageInvoked, Is.True);
            Assert.That(imageResult.Kind, Is.EqualTo(VmValueKind.Void));
            Assert.That(host.LastUiImageHandle, Is.EqualTo(3));
            Assert.That(host.LastUiImageWidth, Is.EqualTo(1));
            Assert.That(host.LastUiImageRgbaBase64, Is.EqualTo("AQIDBA=="));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscallDispatcher_StrSubstring_IsWired()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.str_substring", out var syscallId), Is.True);
        Assert.That(VmSyscallDispatcher.TryGetExpectedArity(syscallId, out var arity), Is.True);
        Assert.That(arity, Is.EqualTo(3));

        var invoked = VmSyscallDispatcher.TryInvoke(
            syscallId,
            new[] { SysValue.String("a😀bc"), SysValue.Int(1), SysValue.Int(2) }.AsSpan(),
            new VmNetworkState(),
            out var result);
        Assert.That(invoked, Is.True);
        Assert.That(result.Kind, Is.EqualTo(VmValueKind.String));
        Assert.That(result.StringValue, Is.EqualTo("😀b"));
    }

    [Test]
    public void VmSyscallDispatcher_StrRemove_IsWired()
    {
        Assert.That(SyscallRegistry.TryResolve("sys.str_remove", out var syscallId), Is.True);
        Assert.That(VmSyscallDispatcher.TryGetExpectedArity(syscallId, out var arity), Is.True);
        Assert.That(arity, Is.EqualTo(3));

        var invoked = VmSyscallDispatcher.TryInvoke(
            syscallId,
            new[] { SysValue.String("a😀bc"), SysValue.Int(1), SysValue.Int(2) }.AsSpan(),
            new VmNetworkState(),
            out var result);
        Assert.That(invoked, Is.True);
        Assert.That(result.Kind, Is.EqualTo(VmValueKind.String));
        Assert.That(result.StringValue, Is.EqualTo("ac"));
    }

    [Test]
    public void SyscallDispatch_FsPathExists_ReturnsBool()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.fs_pathExists) { Lit#s1(value=\"x\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { FsPathExistsResult = true };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Bool));
            Assert.That(value.AsBool(), Is.True);
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_FsWriteFile_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.FsWriteFile("file.txt", "content");
            Assert.That(host.LastFsWritePath, Is.EqualTo("file.txt"));
            Assert.That(host.LastFsWriteText, Is.EqualTo("content"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_FsWriteFile_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.fs_writeFile) { Lit#s1(value=\"file.txt\") Lit#s2(value=\"content\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastFsWritePath, Is.EqualTo("file.txt"));
            Assert.That(host.LastFsWriteText, Is.EqualTo("content"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_ProcessCwd_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { ProcessCwdResult = "/tmp/cwd" };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.ProcessCwd(), Is.EqualTo("/tmp/cwd"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ProcessCwd_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.process_cwd) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { ProcessCwdResult = "/tmp/cwd" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("/tmp/cwd"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_ConsolePrintLine_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            VmSyscalls.ConsolePrintLine("line");
            Assert.That(host.LastConsoleLine, Is.EqualTo("line"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_ConsoleWriteLine_ReturnsVoid()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.console_writeLine) { Lit#s1(value=\"line\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Void));
            Assert.That(host.LastConsoleLine, Is.EqualTo("line"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_NetTcpConnect_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { NetTcpConnectResult = 44 };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var parse = Parse("Program#p1 { Call#c1(target=sys.net_tcpConnect) { Lit#h1(value=\"example.com\") Lit#p1(value=80) } }");
            Assert.That(parse.Diagnostics, Is.Empty);

            var value = interpreter.EvaluateProgram(parse.Root!, runtime);
            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(value.AsInt(), Is.EqualTo(44));
            Assert.That(host.LastNetTcpConnectHost, Is.EqualTo("example.com"));
            Assert.That(host.LastNetTcpConnectPort, Is.EqualTo(80));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_Platform_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { PlatformResult = "host-platform" };
        try
        {
            VmSyscalls.Host = host;
            var result = VmSyscalls.Platform();
            Assert.That(result, Is.EqualTo("host-platform"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_Platform_ReturnsString()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.platform) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost { PlatformResult = "vm-platform" };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);

            Assert.That(value.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(value.AsString(), Is.EqualTo("vm-platform"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void VmSyscalls_SystemInfo_UsesConfiguredHost()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            ArchitectureResult = "arm64",
            OsVersionResult = "test-os-version",
            RuntimeResult = "airun-test"
        };
        try
        {
            VmSyscalls.Host = host;
            Assert.That(VmSyscalls.Architecture(), Is.EqualTo("arm64"));
            Assert.That(VmSyscalls.OsVersion(), Is.EqualTo("test-os-version"));
            Assert.That(VmSyscalls.Runtime(), Is.EqualTo("airun-test"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void SyscallDispatch_SystemInfo_ReturnsStrings()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            ArchitectureResult = "x64",
            OsVersionResult = "os-v",
            RuntimeResult = "runtime-v"
        };
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            var interpreter = new AosInterpreter();

            var arch = Parse("Program#p1 { Call#c1(target=sys.arch) }");
            var osVersion = Parse("Program#p1 { Call#c2(target=sys.os_version) }");
            var runtimeName = Parse("Program#p1 { Call#c3(target=sys.runtime) }");
            Assert.That(arch.Diagnostics, Is.Empty);
            Assert.That(osVersion.Diagnostics, Is.Empty);
            Assert.That(runtimeName.Diagnostics, Is.Empty);

            var archValue = interpreter.EvaluateProgram(arch.Root!, runtime);
            var osVersionValue = interpreter.EvaluateProgram(osVersion.Root!, runtime);
            var runtimeValue = interpreter.EvaluateProgram(runtimeName.Root!, runtime);

            Assert.That(archValue.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(osVersionValue.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(runtimeValue.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(archValue.AsString(), Is.EqualTo("x64"));
            Assert.That(osVersionValue.AsString(), Is.EqualTo("os-v"));
            Assert.That(runtimeValue.AsString(), Is.EqualTo("runtime-v"));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void CompilerParseHttpRequest_DecodesQueryValues()
    {
        var parse = Parse("Program#p1 { Call#c1(target=compiler.parseHttpRequest) { Lit#s1(value=\"GET /weather?city=Fort%20Worth&name=Ada+Lovelace&mark=%E2%9C%93&metro=S%C3%A3o+Paulo&emoji=😀 HTTP/1.1\\r\\n\\r\\n\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var req = value.AsNode();
        Assert.That(req.Kind, Is.EqualTo("HttpRequest"));
        var query = req.Children[1];
        Assert.That(query.Kind, Is.EqualTo("Map"));
        Assert.That(query.Children[0].Attrs["key"].AsString(), Is.EqualTo("city"));
        Assert.That(query.Children[0].Children[0].Attrs["value"].AsString(), Is.EqualTo("Fort Worth"));
        Assert.That(query.Children[1].Attrs["key"].AsString(), Is.EqualTo("name"));
        Assert.That(query.Children[1].Children[0].Attrs["value"].AsString(), Is.EqualTo("Ada Lovelace"));
        Assert.That(query.Children[2].Attrs["key"].AsString(), Is.EqualTo("mark"));
        Assert.That(query.Children[2].Children[0].Attrs["value"].AsString(), Is.EqualTo("✓"));
        Assert.That(query.Children[3].Attrs["key"].AsString(), Is.EqualTo("metro"));
        Assert.That(query.Children[3].Children[0].Attrs["value"].AsString(), Is.EqualTo("São Paulo"));
        Assert.That(query.Children[4].Attrs["key"].AsString(), Is.EqualTo("emoji"));
        Assert.That(query.Children[4].Children[0].Attrs["value"].AsString(), Is.EqualTo("😀"));
        Assert.That(req.Children[2].Kind, Is.EqualTo("Lit"));
        Assert.That(req.Children[2].Attrs["value"].AsString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void CompilerParseHttpRequest_IncludesBody()
    {
        var parse = Parse("Program#p1 { Call#c1(target=compiler.parseHttpRequest) { Lit#s1(value=\"POST /submit HTTP/1.1\\r\\nContent-Type: application/json\\r\\n\\r\\n{\\\"name\\\":\\\"Ada\\\"}\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var req = value.AsNode();
        Assert.That(req.Kind, Is.EqualTo("HttpRequest"));
        Assert.That(req.Children.Count, Is.EqualTo(3));
        Assert.That(req.Children[2].Kind, Is.EqualTo("Lit"));
        Assert.That(req.Children[2].Attrs["value"].AsString(), Is.EqualTo("{\"name\":\"Ada\"}"));
    }

    [Test]
    public void CompilerParseHttpBodyForm_ParsesDeterministically()
    {
        var parse = Parse("Program#p1 { Call#c1(target=compiler.parseHttpBodyForm) { Lit#s1(value=\"city=Fort%20Worth&name=Ada+Lovelace\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var map = value.AsNode();
        Assert.That(map.Kind, Is.EqualTo("Map"));
        Assert.That(map.Children.Count, Is.EqualTo(2));
        Assert.That(map.Children[0].Attrs["key"].AsString(), Is.EqualTo("city"));
        Assert.That(map.Children[0].Children[0].Attrs["value"].AsString(), Is.EqualTo("Fort Worth"));
        Assert.That(map.Children[1].Attrs["key"].AsString(), Is.EqualTo("name"));
        Assert.That(map.Children[1].Children[0].Attrs["value"].AsString(), Is.EqualTo("Ada Lovelace"));
    }

    [Test]
    public void StdJsonParse_ReturnsMapForValidJson_AndErrForInvalid()
    {
        var validParse = Parse("Program#p1 { Call#c1(target=std.json.parse) { Lit#s1(value=\"{\\\"city\\\":\\\"Fort Worth\\\",\\\"ok\\\":true,\\\"count\\\":7}\") } }");
        Assert.That(validParse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var validValue = interpreter.EvaluateProgram(validParse.Root!, runtime);
        Assert.That(validValue.Kind, Is.EqualTo(AosValueKind.Node));
        var map = validValue.AsNode();
        Assert.That(map.Kind, Is.EqualTo("Map"));
        Assert.That(map.Children.Count, Is.EqualTo(3));

        var invalidParse = Parse("Program#p2 { Call#c2(target=std.json.parse) { Lit#s2(value=\"{\\\"city\\\":}\") } }");
        Assert.That(invalidParse.Diagnostics, Is.Empty);
        var invalidValue = interpreter.EvaluateProgram(invalidParse.Root!, runtime);
        Assert.That(invalidValue.Kind, Is.EqualTo(AosValueKind.Node));
        var err = invalidValue.AsNode();
        Assert.That(err.Kind, Is.EqualTo("Err"));
        Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("PARJSON001"));
    }

    [Test]
    public void StdHttpHelpers_QueryAndHeaderGet_Work()
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var interpreter = new AosInterpreter();
        var stdHttpProgram = Parse(File.ReadAllText(FindRepoFile("src/std/http.aos")));
        Assert.That(stdHttpProgram.Diagnostics, Is.Empty);
        var stdHttpResult = interpreter.EvaluateProgram(stdHttpProgram.Root!, runtime);
        Assert.That(stdHttpResult.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

        var queryParse = Parse("Program#p1 { Call#c1(target=httpQueryGet) { Call#c2(target=compiler.parseHttpRequest) { Lit#s1(value=\"GET /weather?city=Fort%20Worth HTTP/1.1\\r\\nAccept: text/csv\\r\\n\\r\\n\") } Lit#s2(value=\"city\") Lit#s3(value=\"missing\") } }");
        Assert.That(queryParse.Diagnostics, Is.Empty);
        var queryValue = interpreter.EvaluateProgram(queryParse.Root!, runtime);
        Assert.That(queryValue.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(queryValue.AsString(), Is.EqualTo("Fort Worth"));

        var headerParse = Parse("Program#p1 { Call#c1(target=httpHeaderGet) { Call#c2(target=compiler.parseHttpRequest) { Lit#s1(value=\"GET /weather HTTP/1.1\\r\\nAccept: text/csv\\r\\n\\r\\n\") } Lit#s2(value=\"Accept\") Lit#s3(value=\"application/json\") } }");
        Assert.That(headerParse.Diagnostics, Is.Empty);
        var headerValue = interpreter.EvaluateProgram(headerParse.Root!, runtime);
        Assert.That(headerValue.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(headerValue.AsString(), Is.EqualTo("text/csv"));
    }

    [Test]
    public void StdHttpHelpers_ResponseAndEmitRaw_AreDeterministic()
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var stdHttpProgram = Parse(File.ReadAllText(FindRepoFile("src/std/http.aos")));
        Assert.That(stdHttpProgram.Diagnostics, Is.Empty);
        var stdHttpResult = interpreter.EvaluateProgram(stdHttpProgram.Root!, runtime);
        Assert.That(stdHttpResult.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

        var responseParse = Parse("Program#p1 { Call#c1(target=httpResponse) { Lit#i1(value=200) Lit#s1(value=\"OK\") Lit#s2(value=\"text/plain\") Lit#s3(value=\"hi\") } }");
        Assert.That(responseParse.Diagnostics, Is.Empty);
        var responseValue = interpreter.EvaluateProgram(responseParse.Root!, runtime);
        Assert.That(responseValue.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(responseValue.AsString(), Is.EqualTo("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\nhi"));

        var emitParse = Parse("Program#p1 { Call#c1(target=httpEmitRaw) { MakeBlock#b1 { Lit#s1(value=\"state\") } Lit#s2(value=\"HTTP/1.1 200 OK\\r\\nContent-Length: 0\\r\\n\\r\\n\") } }");
        Assert.That(emitParse.Diagnostics, Is.Empty);
        var emitValue = interpreter.EvaluateProgram(emitParse.Root!, runtime);
        Assert.That(emitValue.Kind, Is.EqualTo(AosValueKind.Node));
        var next = emitValue.AsNode();
        Assert.That(next.Kind, Is.EqualTo("Block"));
        Assert.That(next.Children.Count, Is.EqualTo(2));
        Assert.That(next.Children[1].Kind, Is.EqualTo("Command"));
        Assert.That(next.Children[1].Attrs["type"].AsString(), Is.EqualTo("http.raw"));
    }

    [Test]
    public void StdHttpHelpers_HttpRequestStart_UsesTcpStartSyscalls()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            NetTcpConnectStartResult = 101,
            StrUtf8ByteCountResult = 3
        };

        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            runtime.Permissions.Add("compiler");
            var interpreter = new AosInterpreter();
            var stdHttpProgram = Parse(File.ReadAllText(FindRepoFile("src/std/http.aos")));
            Assert.That(stdHttpProgram.Diagnostics, Is.Empty);
            var stdHttpResult = interpreter.EvaluateProgram(stdHttpProgram.Root!, runtime);
            Assert.That(stdHttpResult.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

            var requestParse = Parse("Program#p1 { Call#c1(target=httpRequest) { Lit#m1(value=\"POST\") Lit#h1(value=\"example.com\") Lit#p1(value=80) Lit#pa1(value=\"/submit\") Lit#hd1(value=\"Accept: text/plain\") Lit#b1(value=\"abc\") Lit#mx1(value=4096) } }");
            Assert.That(requestParse.Diagnostics, Is.Empty);
            var stateValue = interpreter.EvaluateProgram(requestParse.Root!, runtime);
            Assert.That(stateValue.Kind, Is.EqualTo(AosValueKind.Node));
            var stateNode = stateValue.AsNode();
            Assert.That(stateNode.Kind, Is.EqualTo("Block"));
            Assert.That(host.LastNetTcpConnectStartHost, Is.EqualTo("example.com"));
            Assert.That(host.LastNetTcpConnectStartPort, Is.EqualTo(80));
            Assert.That(host.LastNetTcpWriteStartHandle, Is.EqualTo(-1));
            Assert.That(host.LastNetTcpReadStartHandle, Is.EqualTo(-1));
            Assert.That(host.LastNetCloseHandle, Is.EqualTo(-1));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void StdHttpHelpers_HttpRequestAwait_UsesTlsConnect_ForPort443()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            NetTcpConnectResult = 77,
            NetTcpConnectTlsStartResult = 21,
            NetTcpWriteStartResult = 84,
            NetTcpReadStartResult = 85,
            NetAsyncPollResult = 1,
            NetAsyncResultIntResult = 22,
            NetAsyncResultStringResult = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok",
            StrUtf8ByteCountResult = 0
        };

        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            runtime.Permissions.Add("compiler");
            var interpreter = new AosInterpreter();
            var stdHttpProgram = Parse(File.ReadAllText(FindRepoFile("src/std/http.aos")));
            Assert.That(stdHttpProgram.Diagnostics, Is.Empty);
            var stdHttpResult = interpreter.EvaluateProgram(stdHttpProgram.Root!, runtime);
            Assert.That(stdHttpResult.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

            var requestParse = Parse("Program#p1 { Call#c1(target=httpRequestAwait) { Call#c2(target=httpRequest) { Lit#m1(value=\"GET\") Lit#h1(value=\"example.com\") Lit#p1(value=443) Lit#pa1(value=\"/\") Lit#hd1(value=\"\") Lit#b1(value=\"\") Lit#mx1(value=4096) } } }");
            Assert.That(requestParse.Diagnostics, Is.Empty);
            var responseValue = interpreter.EvaluateProgram(requestParse.Root!, runtime);
            Assert.That(responseValue.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(responseValue.AsString(), Is.EqualTo("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok"));
            Assert.That(host.LastNetTcpConnectTlsStartHost, Is.EqualTo("example.com"));
            Assert.That(host.LastNetTcpConnectTlsStartPort, Is.EqualTo(443));
            Assert.That(host.LastNetTcpConnectHost, Is.Null);
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void StdNetHelpers_UdpWrappers_CallSyscalls()
    {
        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost
        {
            NetUdpBindResult = 31,
            NetUdpSendResult = 4,
            NetUdpRecvResult = new VmUdpPacket("127.0.0.1", 9001, "pong")
        };

        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("net");
            var interpreter = new AosInterpreter();
            var stdNetProgram = Parse(File.ReadAllText(FindRepoFile("src/std/net.aos")));
            Assert.That(stdNetProgram.Diagnostics, Is.Empty);
            var stdNetResult = interpreter.EvaluateProgram(stdNetProgram.Root!, runtime);
            Assert.That(stdNetResult.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

            var callParse = Parse("Program#p2 { Let#l2(name=h) { Call#c20(target=udpBind) { Lit#s20(value=\"127.0.0.1\") Lit#i20(value=9000) } } Call#c21(target=udpSend) { Var#v20(name=h) Lit#s21(value=\"127.0.0.1\") Lit#i21(value=9001) Lit#d20(value=\"ping\") } Call#c22(target=udpRecv) { Var#v21(name=h) Lit#i22(value=64) } }");
            Assert.That(callParse.Diagnostics, Is.Empty);
            var packetValue = interpreter.EvaluateProgram(callParse.Root!, runtime);
            Assert.That(packetValue.Kind, Is.EqualTo(AosValueKind.Node));
            var packet = packetValue.AsNode();
            Assert.That(packet.Kind, Is.EqualTo("UdpPacket"));
            Assert.That(packet.Attrs["host"].AsString(), Is.EqualTo("127.0.0.1"));
            Assert.That(packet.Attrs["port"].AsInt(), Is.EqualTo(9001));
            Assert.That(packet.Attrs["data"].AsString(), Is.EqualTo("pong"));

            Assert.That(host.LastNetUdpBindHost, Is.EqualTo("127.0.0.1"));
            Assert.That(host.LastNetUdpBindPort, Is.EqualTo(9000));
            Assert.That(host.LastNetUdpSendHandle, Is.EqualTo(31));
            Assert.That(host.LastNetUdpSendHost, Is.EqualTo("127.0.0.1"));
            Assert.That(host.LastNetUdpSendPort, Is.EqualTo(9001));
            Assert.That(host.LastNetUdpSendData, Is.EqualTo("ping"));
            Assert.That(host.LastNetUdpRecvHandle, Is.EqualTo(31));
            Assert.That(host.LastNetUdpRecvMaxBytes, Is.EqualTo(64));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void StdUiInput_KeySemantics_AreDeterministic()
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var stdUiProgram = Parse(File.ReadAllText(FindRepoFile("src/std/ui_input.aos")));
        Assert.That(stdUiProgram.Diagnostics, Is.Empty);
        var stdUiResult = interpreter.EvaluateProgram(stdUiProgram.Root!, runtime);
        Assert.That(stdUiResult.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

        var cases = new[]
        {
            (Key: "a", Text: "a", AllowEnter: false, AllowTab: false, StartText: "hi", StartCursor: 2, ExpectedText: "hia", ExpectedCursor: 3, ExpectedSubmit: false, ExpectedToken: "insert"),
            (Key: "a", Text: "A", AllowEnter: false, AllowTab: false, StartText: "hi", StartCursor: 2, ExpectedText: "hiA", ExpectedCursor: 3, ExpectedSubmit: false, ExpectedToken: "insert"),
            (Key: "1", Text: "1", AllowEnter: false, AllowTab: false, StartText: "hi", StartCursor: 2, ExpectedText: "hi1", ExpectedCursor: 3, ExpectedSubmit: false, ExpectedToken: "insert"),
            (Key: "space", Text: "", AllowEnter: false, AllowTab: false, StartText: "hi", StartCursor: 2, ExpectedText: "hi ", ExpectedCursor: 3, ExpectedSubmit: false, ExpectedToken: "insert"),
            (Key: "`", Text: "`", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 1, ExpectedText: "a`b", ExpectedCursor: 2, ExpectedSubmit: false, ExpectedToken: "insert"),
            (Key: "tab", Text: "\t", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 1, ExpectedText: "ab", ExpectedCursor: 1, ExpectedSubmit: false, ExpectedToken: "noop"),
            (Key: "enter", Text: "\r", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 2, ExpectedText: "ab", ExpectedCursor: 2, ExpectedSubmit: true, ExpectedToken: "submit"),
            (Key: "enter", Text: "\r", AllowEnter: true, AllowTab: false, StartText: "ab", StartCursor: 2, ExpectedText: "ab\n", ExpectedCursor: 3, ExpectedSubmit: false, ExpectedToken: "insert"),
            (Key: "backspace", Text: "", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 2, ExpectedText: "a", ExpectedCursor: 1, ExpectedSubmit: false, ExpectedToken: "backspace"),
            (Key: "delete", Text: "", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 0, ExpectedText: "b", ExpectedCursor: 0, ExpectedSubmit: false, ExpectedToken: "delete"),
            (Key: "left", Text: "", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 1, ExpectedText: "ab", ExpectedCursor: 0, ExpectedSubmit: false, ExpectedToken: "move"),
            (Key: "right", Text: "", AllowEnter: false, AllowTab: false, StartText: "ab", StartCursor: 1, ExpectedText: "ab", ExpectedCursor: 2, ExpectedSubmit: false, ExpectedToken: "move")
        };

        foreach (var testCase in cases)
        {
            var callSuffix =
                $"Lit#t1(value=\"{EscapeAosString(testCase.StartText)}\") " +
                $"Lit#i1(value={testCase.StartCursor}) " +
                $"Lit#k1(value=\"{EscapeAosString(testCase.Key)}\") " +
                $"Lit#e1(value=\"{EscapeAosString(testCase.Text)}\") " +
                $"Lit#ae1(value={(testCase.AllowEnter ? "true" : "false")}) " +
                $"Lit#at1(value={(testCase.AllowTab ? "true" : "false")})";

            var textParse = Parse($"Program#p1 {{ Call#c1(target=editNextText) {{ {callSuffix} }} }}");
            Assert.That(textParse.Diagnostics, Is.Empty);
            var textValue = interpreter.EvaluateProgram(textParse.Root!, runtime);
            Assert.That(textValue.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(textValue.AsString(), Is.EqualTo(testCase.ExpectedText), $"Unexpected text for key {testCase.Key}.");

            var cursorParse = Parse($"Program#p2 {{ Call#c2(target=editNextCursor) {{ {callSuffix} }} }}");
            Assert.That(cursorParse.Diagnostics, Is.Empty);
            var cursorValue = interpreter.EvaluateProgram(cursorParse.Root!, runtime);
            Assert.That(cursorValue.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(cursorValue.AsInt(), Is.EqualTo(testCase.ExpectedCursor), $"Unexpected cursor for key {testCase.Key}.");

            var submitParse = Parse(
                $"Program#p3 {{ Call#c3(target=editShouldSubmit) {{ " +
                $"Lit#k2(value=\"{EscapeAosString(testCase.Key)}\") " +
                $"Lit#ae2(value={(testCase.AllowEnter ? "true" : "false")}) }} }}");
            Assert.That(submitParse.Diagnostics, Is.Empty);
            var submitValue = interpreter.EvaluateProgram(submitParse.Root!, runtime);
            Assert.That(submitValue.Kind, Is.EqualTo(AosValueKind.Bool));
            Assert.That(submitValue.AsBool(), Is.EqualTo(testCase.ExpectedSubmit), $"Unexpected submit flag for key {testCase.Key}.");

            var tokenParse = Parse(
                $"Program#p4 {{ Call#c4(target=editDebugToken) {{ " +
                $"Lit#k3(value=\"{EscapeAosString(testCase.Key)}\") " +
                $"Lit#e2(value=\"{EscapeAosString(testCase.Text)}\") " +
                $"Lit#ae3(value={(testCase.AllowEnter ? "true" : "false")}) " +
                $"Lit#at2(value={(testCase.AllowTab ? "true" : "false")}) }} }}");
            Assert.That(tokenParse.Diagnostics, Is.Empty);
            var tokenValue = interpreter.EvaluateProgram(tokenParse.Root!, runtime);
            Assert.That(tokenValue.Kind, Is.EqualTo(AosValueKind.String));
            Assert.That(tokenValue.AsString(), Is.EqualTo(testCase.ExpectedToken), $"Unexpected debug token for key {testCase.Key}.");
        }
    }

    [Test]
    public void StdUiInput_FocusRouting_IsDeterministic()
    {
        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var stdUiProgram = Parse(File.ReadAllText(FindRepoFile("src/std/ui_input.aos")));
        Assert.That(stdUiProgram.Diagnostics, Is.Empty);
        _ = interpreter.EvaluateProgram(stdUiProgram.Root!, runtime);

        var focusedParse = Parse("Program#p1 { Call#c1(target=shouldHandleKeyEvent) { Lit#t1(value=\"key\") Lit#f1(value=\"input_1\") Lit#tg1(value=\"\") } }");
        Assert.That(focusedParse.Diagnostics, Is.Empty);
        var focused = interpreter.EvaluateProgram(focusedParse.Root!, runtime);
        Assert.That(focused.Kind, Is.EqualTo(AosValueKind.Bool));
        Assert.That(focused.AsBool(), Is.True);

        var unfocusedParse = Parse("Program#p2 { Call#c2(target=shouldHandleKeyEvent) { Lit#t2(value=\"key\") Lit#f2(value=\"\") Lit#tg2(value=\"\") } }");
        Assert.That(unfocusedParse.Diagnostics, Is.Empty);
        var unfocused = interpreter.EvaluateProgram(unfocusedParse.Root!, runtime);
        Assert.That(unfocused.Kind, Is.EqualTo(AosValueKind.Bool));
        Assert.That(unfocused.AsBool(), Is.False);

        var focusSetParse = Parse("Program#p3 { Call#c3(target=nextFocusOnClick) { Lit#t3(value=\"click\") Lit#tg3(value=\"input_2\") Lit#f3(value=\"input_1\") } }");
        Assert.That(focusSetParse.Diagnostics, Is.Empty);
        var nextFocused = interpreter.EvaluateProgram(focusSetParse.Root!, runtime);
        Assert.That(nextFocused.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(nextFocused.AsString(), Is.EqualTo("input_2"));

        var focusClearParse = Parse("Program#p4 { Call#c4(target=nextFocusOnClick) { Lit#t4(value=\"click\") Lit#tg4(value=\"\") Lit#f4(value=\"input_2\") } }");
        Assert.That(focusClearParse.Diagnostics, Is.Empty);
        var cleared = interpreter.EvaluateProgram(focusClearParse.Root!, runtime);
        Assert.That(cleared.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(cleared.AsString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void UnknownSysTarget_DoesNotEvaluateArguments()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.unknown) { Call#c2(target=io.print) { Lit#s1(value=\"side-effect\") } } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("sys");
            runtime.Permissions.Add("io");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);

            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Unknown));
            Assert.That(host.IoPrintCount, Is.EqualTo(0));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void InvalidCapabilityArity_DoesNotEvaluateArguments()
    {
        var parse = Parse("Program#p1 { Call#c1(target=io.print) { Call#c2(target=io.print) { Lit#s1(value=\"nested\") } Lit#s2(value=\"extra\") } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var previous = VmSyscalls.Host;
        var host = new RecordingSyscallHost();
        try
        {
            VmSyscalls.Host = host;
            var runtime = new AosRuntime();
            runtime.Permissions.Add("io");
            var interpreter = new AosInterpreter();
            var value = interpreter.EvaluateProgram(parse.Root!, runtime);

            Assert.That(value.Kind, Is.EqualTo(AosValueKind.Unknown));
            Assert.That(host.IoPrintCount, Is.EqualTo(0));
        }
        finally
        {
            VmSyscalls.Host = previous;
        }
    }

    [Test]
    public void Validator_ParRequiresAtLeastTwoChildren()
    {
        var parse = Parse("Program#p1 { Par#par1 { Lit#v1(value=1) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, new HashSet<string>(StringComparer.Ordinal), runStructural: false);
        Assert.That(validation.Diagnostics.Any(d => d.Code == "VAL168"), Is.True);
    }

    [Test]
    public void Validator_ParComputeOnlyRejectsSyscalls()
    {
        var parse = Parse("Program#p1 { Par#par1 { Call#c1(target=sys.net_tcpConnect) { Lit#h1(value=\"example.com\") Lit#p1(value=80) } Lit#v1(value=1) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var permissions = new HashSet<string>(StringComparer.Ordinal) { "sys" };
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, permissions, runStructural: false);
        Assert.That(validation.Diagnostics.Any(d => d.Code == "VAL169"), Is.True);
    }

    [Test]
    public void Validator_AwaitRequiresNodeTypedChild()
    {
        var parse = Parse("Program#p1 { Await#a1 { Lit#v1(value=1) } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, new HashSet<string>(StringComparer.Ordinal), runStructural: false);
        Assert.That(validation.Diagnostics.Any(d => d.Code == "VAL167"), Is.True);
    }

    [Test]
    public void Validator_UpdateContext_RejectsBlockingCalls()
    {
        var parse = Parse("Program#p1 { Let#l1(name=update) { Fn#f1(params=state,event) { Block#b1 { Call#c1(target=sys.net_asyncAwait) { Lit#i1(value=1) } Return#r1 { Var#v1(name=state) } } } } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var permissions = new HashSet<string>(StringComparer.Ordinal) { "sys" };
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, permissions, runStructural: false);
        Assert.That(validation.Diagnostics.Any(d => d.Code == "VAL340"), Is.True);
    }

    [Test]
    public void Validator_UpdateContext_AllowsNonBlockingPoll()
    {
        var parse = Parse("Program#p1 { Let#l1(name=update) { Fn#f1(params=state,event) { Block#b1 { Call#c1(target=sys.net_asyncPoll) { Lit#i1(value=1) } Return#r1 { Var#v1(name=state) } } } } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var permissions = new HashSet<string>(StringComparer.Ordinal) { "sys" };
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, permissions, runStructural: false);
        Assert.That(validation.Diagnostics.Any(d => d.Code == "VAL340"), Is.False);
    }

    [Test]
    public void Validator_NonUpdateContext_DoesNotEmitBlockingDiagnostic()
    {
        var parse = Parse("Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Call#c1(target=sys.net_asyncAwait) { Lit#i1(value=1) } } } } } }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var permissions = new HashSet<string>(StringComparer.Ordinal) { "sys" };
        var validator = new AosValidator();
        var validation = validator.Validate(parse.Root!, null, permissions, runStructural: false);
        Assert.That(validation.Diagnostics.Any(d => d.Code == "VAL340"), Is.False);
    }

    [Test]
    public void SyscallDispatch_InvalidArgs_ReturnsUnknown()
    {
        var parse = Parse("Program#p1 { Call#c1(target=sys.net_close) }");
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");
        var interpreter = new AosInterpreter();
        var value = interpreter.EvaluateProgram(parse.Root!, runtime);

        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Unknown));
    }

    [Test]
    public void VmRunBytecode_RejectsUnsupportedVersion()
    {
        var bytecode = Parse("Bytecode#bc1(magic=\"AIBC\" format=\"AiBC1\" version=2 flags=0)").Root!;
        var runtime = new AosRuntime();
        var interpreter = new AosInterpreter();
        var args = Parse("Block#argv").Root!;

        var value = interpreter.RunBytecode(bytecode, "main", args, runtime);
        Assert.That(value.Kind, Is.EqualTo(AosValueKind.Node));
        var err = value.AsNode();
        Assert.That(err.Kind, Is.EqualTo("Err"));
        Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("VM001"));
        Assert.That(err.Attrs["message"].AsString(), Is.EqualTo("Unsupported bytecode version."));
    }

    [Test]
    public void AstRuntime_UpdateContext_RejectsBlockingCall_Transitively()
    {
        var source = "Program#p1 { Let#l1(name=waitNet) { Fn#f1(params=op) { Block#b1 { Return#r1 { Call#c1(target=sys.net_asyncAwait) { Var#v1(name=op) } } } } } Let#l2(name=update) { Fn#f2(params=state,event) { Block#b2 { Call#c2(target=waitNet) { Lit#i1(value=1) } Return#r2 { Var#v2(name=state) } } } } Call#c3(target=update) { Lit#i2(value=0) Lit#i3(value=0) } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("sys");

        var result = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
        var err = result.AsNode();
        Assert.That(err.Kind, Is.EqualTo("Err"));
        Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("RUN031"));
        Assert.That(err.Attrs["message"].AsString(), Does.Contain("Blocking call 'sys.net_asyncAwait'"));
    }

    [Test]
    public void VmRunBytecode_UpdateContext_RejectsBlockingCall_Transitively()
    {
        var source = "Program#p1 { Let#l1(name=waitNet) { Fn#f1(params=op) { Block#b1 { Return#r1 { Call#c1(target=sys.net_asyncAwait) { Var#v1(name=op) } } } } } Let#l2(name=update) { Fn#f2(params=state,event) { Block#b2 { Call#c2(target=waitNet) { Lit#i1(value=1) } Return#r2 { Var#v2(name=state) } } } } Let#l3(name=start) { Fn#f3(params=argv) { Block#b3 { Return#r3 { Call#c3(target=update) { Lit#i2(value=0) Lit#i3(value=0) } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
        var err = result.AsNode();
        Assert.That(err.Kind, Is.EqualTo("Err"));
        Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("RUN031"));
        Assert.That(err.Attrs["message"].AsString(), Does.Contain("Blocking call 'sys.net_asyncAwait'"));
    }

    [Test]
    public void VmRunBytecode_EmitsInstructionTrace_WhenEnabled()
    {
        var source = "Program#p1 { Call#c1(target=compiler.emitBytecode) { Call#c2(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Lit#l1(value=1) }\") } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime { TraceEnabled = true };
        runtime.Permissions.Add("compiler");
        var bytecodeValue = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        runtime.TraceSteps.Clear();
        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "main", args, runtime);
        Assert.That(result.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

        var vmSteps = runtime.TraceSteps
            .Where(step => step.Kind == "Step" &&
                           step.Attrs.TryGetValue("kind", out var kindAttr) &&
                           kindAttr.Kind == AosAttrKind.String &&
                           kindAttr.AsString() == "VmInstruction")
            .ToList();

        Assert.That(vmSteps.Count, Is.GreaterThan(0));
        Assert.That(vmSteps.Any(step =>
            step.Attrs.TryGetValue("op", out var opAttr) &&
            opAttr.Kind == AosAttrKind.String &&
            opAttr.AsString() == "RETURN"), Is.True);
    }

    [Test]
    public void VmRunBytecode_IfBlockExpression_PreservesBranchValue()
    {
        var source = "Program#p1 { Let#l1(name=buildUrl) { Fn#f1(params=_) { Block#b0 { Let#l2(name=city) { If#if1 { Eq#eq1 { Lit#i1(value=1) Lit#i2(value=1) } Block#b1 { Lit#s1(value=\"Fort Worth\") } Block#b2 { Lit#s2(value=\"Else\") } } } Return#r1 { StrConcat#sc1 { Lit#s3(value=\"https://wttr.in/\") Var#v1(name=city) } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        var program = parse.Root!;
        runtime.Env["__program"] = AosValue.FromNode(program);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv { Lit#a1(value=0) }").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "buildUrl", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.String));
        Assert.That(result.AsString(), Is.EqualTo("https://wttr.in/Fort Worth"));
    }

    [Test]
    public void VmRunBytecode_AsyncCallAndAwait_ReturnsResolvedValue()
    {
        var source = "Program#p1 { Let#l1(name=worker) { Fn#f1(params=x async=true) { Block#b1 { Return#r1 { Add#a1 { Var#v1(name=x) Lit#i1(value=1) } } } } } Let#l2(name=start) { Fn#f2(params=argv) { Block#b2 { Let#l3(name=t) { Call#c1(target=worker) { Lit#i2(value=41) } } Return#r2 { Await#aw1 { Var#v2(name=t) } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Int));
        Assert.That(result.AsInt(), Is.EqualTo(42));
    }

    [Test]
    public void VmRunBytecode_ParJoin_PreservesDeclarationOrder()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Par#par1 { Lit#s1(value=\"first\") Lit#s2(value=\"second\") } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
        var block = result.AsNode();
        Assert.That(block.Kind, Is.EqualTo("Block"));
        Assert.That(block.Children.Count, Is.EqualTo(2));
        Assert.That(block.Children[0].Kind, Is.EqualTo("Lit"));
        Assert.That(block.Children[0].Attrs["value"].AsString(), Is.EqualTo("first"));
        Assert.That(block.Children[1].Kind, Is.EqualTo("Lit"));
        Assert.That(block.Children[1].Attrs["value"].AsString(), Is.EqualTo("second"));
    }

    [Test]
    public void VmRunBytecode_ParAsyncSyscalls_MergesInDeclarationOrder()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Par#par1 { Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value=\"abc\") } Call#c2(target=sys.str_utf8ByteCount) { Lit#s2(value=\"hello\") } } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
        var block = result.AsNode();
        Assert.That(block.Kind, Is.EqualTo("Block"));
        Assert.That(block.Children.Count, Is.EqualTo(2));
        Assert.That(block.Children[0].Kind, Is.EqualTo("Lit"));
        Assert.That(block.Children[0].Attrs["value"].AsInt(), Is.EqualTo(3));
        Assert.That(block.Children[1].Kind, Is.EqualTo("Lit"));
        Assert.That(block.Children[1].Attrs["value"].AsInt(), Is.EqualTo(5));
    }

    [Test]
    public void VmRunBytecode_ParAsyncSyscalls_UnsupportedTarget_FailsDeterministically()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Par#par1 { Call#c1(target=sys.unknown) { Lit#s1(value=\"x\") } Call#c2(target=sys.str_utf8ByteCount) { Lit#s2(value=\"ok\") } } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv").Root!;
        var result = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);
        Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
        var err = result.AsNode();
        Assert.That(err.Kind, Is.EqualTo("Err"));
        Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("VM001"));
        Assert.That(err.Attrs["message"].AsString(), Is.EqualTo("Unsupported call target in bytecode mode: sys.unknown."));
    }

    [Test]
    public void VmRunBytecode_ParAsyncSyscalls_OutputIsDeterministicAcrossRuns()
    {
        var source = "Program#p1 { Let#l1(name=start) { Fn#f1(params=argv) { Block#b1 { Return#r1 { Par#par1 { Call#c1(target=sys.str_utf8ByteCount) { Lit#s1(value=\"abc\") } Call#c2(target=sys.str_utf8ByteCount) { Lit#s2(value=\"abcd\") } } } } } } }";
        var parse = Parse(source);
        Assert.That(parse.Diagnostics, Is.Empty);

        var interpreter = new AosInterpreter();
        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Env["__program"] = AosValue.FromNode(parse.Root!);

        var emitCall = Parse("Call#c1(target=compiler.emitBytecode) { Var#v1(name=__program) }").Root!;
        var bytecodeValue = interpreter.EvaluateExpression(emitCall, runtime);
        Assert.That(bytecodeValue.Kind, Is.EqualTo(AosValueKind.Node));

        var args = Parse("Block#argv").Root!;
        var result1 = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);
        var result2 = interpreter.RunBytecode(bytecodeValue.AsNode(), "start", args, runtime);

        Assert.That(result1.Kind, Is.EqualTo(AosValueKind.Node));
        Assert.That(result2.Kind, Is.EqualTo(AosValueKind.Node));
        Assert.That(AosFormatter.Format(result1.AsNode()), Is.EqualTo(AosFormatter.Format(result2.AsNode())));
    }

    [Test]
    public void Evaluator_ImportsAndMergesExplicitExports()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-ok-");
        try
        {
            var depPath = Path.Combine(tempDir.FullName, "dep.aos");
            File.WriteAllText(depPath, "Program#dp { Let#dl(name=x) { Lit#dv(value=7) } Export#de(name=x) }");

            var main = Parse("Program#mp { Import#mi(path=\"dep.aos\") Var#mv(name=x) }").Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(main, runtime);

            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Int));
            Assert.That(result.AsInt(), Is.EqualTo(7));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Validator_ImportPredeclare_DoesNotExposePrivateFunctions()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-private-");
        try
        {
            var depPath = Path.Combine(tempDir.FullName, "dep.aos");
            File.WriteAllText(depPath, "Program#dp { Let#d1(name=privateFn) { Fn#d2(params=_) { Block#d3 { Return#d4 { Lit#d5(value=1) } } } } Let#d6(name=publicFn) { Fn#d7(params=_) { Block#d8 { Return#d9 { Lit#d10(value=2) } } } } Export#d11(name=publicFn) }");

            var main = Parse("Program#mp { Import#mi(path=\"dep.aos\") Call#mc(target=privateFn) { Lit#mv(value=0) } }").Root!;
            var validator = new AosValidator();
            var permissions = new HashSet<string>(StringComparer.Ordinal) { "compiler", "sys", "io", "console" };
            var diagnostics = validator.Validate(main, null, permissions, runStructural: false, moduleBaseDir: tempDir.FullName).Diagnostics;

            Assert.That(diagnostics.Any(d => d.Code == "VAL036"), Is.True);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Validator_ImportPredeclare_RespectsExportOrder()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-order-");
        try
        {
            var depPath = Path.Combine(tempDir.FullName, "dep.aos");
            File.WriteAllText(depPath, "Program#dp { Export#de(name=lateFn) Let#dl(name=lateFn) { Fn#df(params=_) { Block#db { Return#dr { Lit#dv(value=1) } } } } }");

            var main = Parse("Program#mp { Import#mi(path=\"dep.aos\") Call#mc(target=lateFn) { Lit#mv(value=0) } }").Root!;
            var validator = new AosValidator();
            var permissions = new HashSet<string>(StringComparer.Ordinal) { "compiler", "sys", "io", "console" };
            var diagnostics = validator.Validate(main, null, permissions, runStructural: false, moduleBaseDir: tempDir.FullName).Diagnostics;

            Assert.That(diagnostics.Any(d => d.Code == "VAL036"), Is.True);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Evaluator_ReportsCircularImport()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-cycle-");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "a.aos"), "Program#a { Import#ia(path=\"b.aos\") Let#al(name=x) { Lit#av(value=1) } Export#ae(name=x) }");
            File.WriteAllText(Path.Combine(tempDir.FullName, "b.aos"), "Program#b { Import#ib(path=\"a.aos\") Let#bl(name=y) { Lit#bv(value=2) } Export#be(name=y) }");

            var main = Parse("Program#mp { Import#mi(path=\"a.aos\") Var#mv(name=x) }").Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(main, runtime);

            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
            var err = result.AsNode();
            Assert.That(err.Kind, Is.EqualTo("Err"));
            Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("RUN023"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void Evaluator_ReportsMissingImportFile()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-import-missing-");
        try
        {
            var main = Parse("Program#mp { Import#mi(path=\"missing.aos\") }").Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(main, runtime);

            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
            var err = result.AsNode();
            Assert.That(err.Kind, Is.EqualTo("Err"));
            Assert.That(err.Attrs["code"].AsString(), Is.EqualTo("RUN024"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void CompilerRun_ReturnsErrNode_ForMissingImport()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-compiler-run-missing-");
        try
        {
            var source = "Program#p1 { Let#l1(name=parsed) { Call#c1(target=compiler.parse) { Lit#s1(value=\"Program#p2 { Import#i1(path=\\\"missing.aos\\\") }\") } } Call#c2(target=compiler.run) { Var#v1(name=parsed) } }";
            var root = Parse(source).Root!;
            var runtime = new AosRuntime { ModuleBaseDir = tempDir.FullName };
            runtime.Permissions.Add("compiler");

            var interpreter = new AosInterpreter();
            var result = interpreter.EvaluateProgram(root, runtime);
            Assert.That(result.Kind, Is.EqualTo(AosValueKind.Node));
            Assert.That(result.AsNode().Kind, Is.EqualTo("Err"));
            Assert.That(result.AsNode().Attrs["code"].AsString(), Is.EqualTo("RUN024"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void WeatherApi_ProgramEvaluation_DoesNotThrow()
    {
        var appPath = FindRepoFile("samples/weather-api/src/app.aos");
        var parse = AosParsing.ParseFile(appPath);
        Assert.That(parse.Root, Is.Not.Null);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("console");
        runtime.ModuleBaseDir = Path.GetDirectoryName(appPath)!;

        var interpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(runtime, interpreter);

        var result = interpreter.EvaluateProgram(parse.Root!, runtime);
        Assert.That(result.Kind, Is.Not.EqualTo(AosValueKind.Unknown));
    }

    [Test]
    public void WeatherApi_RuntimeStart_DoesNotThrow()
    {
        var appPath = FindRepoFile("samples/weather-api/src/app.aos");
        var parse = AosParsing.ParseFile(appPath);
        Assert.That(parse.Root, Is.Not.Null);
        Assert.That(parse.Diagnostics, Is.Empty);

        var runtime = new AosRuntime();
        runtime.Permissions.Add("compiler");
        runtime.Permissions.Add("sys");
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("console");
        runtime.ModuleBaseDir = Path.GetDirectoryName(appPath)!;
        runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(Array.Empty<string>()));
        runtime.ReadOnlyBindings.Add("argv");
        runtime.Env["__entryExport"] = AosValue.FromString("start");
        runtime.ReadOnlyBindings.Add("__entryExport");
        runtime.Env["__vm_mode"] = AosValue.FromString("bytecode");
        runtime.ReadOnlyBindings.Add("__vm_mode");

        var executionProgram = new AosNode(
            "Program",
            parse.Root!.Id,
            parse.Root.Attrs,
            parse.Root.Children.Concat(new[]
            {
                new AosNode(
                    "Call",
                    "run_manifest_entry_call_test",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["target"] = new AosAttrValue(AosAttrKind.Identifier, "start")
                    },
                    new List<AosNode>
                    {
                        new AosNode(
                            "Var",
                            "run_manifest_entry_args_test",
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                            {
                                ["name"] = new AosAttrValue(AosAttrKind.Identifier, "argv")
                            },
                            new List<AosNode>(),
                            parse.Root.Span)
                    },
                    parse.Root.Span)
            }).ToList(),
            parse.Root.Span);

        runtime.Env["__program"] = AosValue.FromNode(executionProgram);
        runtime.ReadOnlyBindings.Add("__program");

        var interpreter = new AosInterpreter();
        AosStandardLibraryLoader.EnsureLoaded(runtime, interpreter);
        var init = interpreter.EvaluateProgram(executionProgram, runtime);
        Assert.That(init.Kind, Is.Not.EqualTo(AosValueKind.Unknown));
        runtime.Env["__program_result"] = init;
        runtime.ReadOnlyBindings.Add("__program_result");

        var runtimeKernel = AosCompilerAssets.LoadRequiredProgram("runtime.aos");
        var kernelInit = interpreter.EvaluateProgram(runtimeKernel, runtime);
        Assert.That(kernelInit.Kind, Is.Not.EqualTo(AosValueKind.Unknown));

        runtime.Env["__kernel_args"] = AosValue.FromNode(
            new AosNode(
                "Block",
                "kargv_run_test",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                new List<AosNode>
                {
                    new AosNode(
                        "Lit",
                        "karg_run0_test",
                        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                        {
                            ["value"] = new AosAttrValue(AosAttrKind.String, "run")
                        },
                        new List<AosNode>(),
                        parse.Root.Span)
                },
                parse.Root.Span));

        var call = new AosNode(
            "Call",
            "kernel_call_test",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["target"] = new AosAttrValue(AosAttrKind.Identifier, "runtime.start")
            },
            new List<AosNode>
            {
                new AosNode(
                    "Var",
                    "kernel_args_test",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["name"] = new AosAttrValue(AosAttrKind.Identifier, "__kernel_args")
                    },
                    new List<AosNode>(),
                    parse.Root.Span)
            },
            parse.Root.Span);

        var result = interpreter.EvaluateExpression(call, runtime);
        Assert.That(result.Kind, Is.Not.EqualTo(AosValueKind.Unknown));
    }

    [Test]
    public void Aic_Smoke_FmtCheckRun()
    {
        var aicPath = FindRepoFile("src/compiler/aic.aos");
        var fmtInput = File.ReadAllText(FindRepoFile("examples/aic_fmt_input.aos"));
        var checkInput = File.ReadAllText(FindRepoFile("examples/aic_check_bad.aos"));
        var runInput = File.ReadAllText(FindRepoFile("examples/aic_run_input.aos"));

        var fmtOutput = ExecuteAic(aicPath, "fmt", fmtInput);
        Assert.That(fmtOutput, Is.EqualTo("Program#program_cef1fc82ba90 { Let#let_3ad75de6b0e8(name=x) { Lit#lit_168514cb3fef(value=1) } }"));

        var fmtIdsOutput = ExecuteAic(aicPath, "fmt", fmtInput, "--ids");
        Assert.That(fmtIdsOutput, Is.EqualTo("Program#program_cef1fc82ba90 { Let#let_3ad75de6b0e8(name=x) { Lit#lit_168514cb3fef(value=1) } }"));

        var checkOutput = ExecuteAic(aicPath, "check", checkInput);
        Assert.That(checkOutput.Contains("Err#diag0(code=VAL002"), Is.True);

        var runOutput = ExecuteAic(aicPath, "run", runInput);
        Assert.That(runOutput, Is.EqualTo("Ok#ok0(type=string value=\"hello from main\")"));
    }

    [Test]
    public void Aic_Run_ImportDiagnostics()
    {
        var aicPath = FindRepoFile("src/compiler/aic.aos");
        var cycleInput = File.ReadAllText(FindRepoFile("examples/golden/run_import_cycle.in.aos"));
        var missingInput = File.ReadAllText(FindRepoFile("examples/golden/run_import_missing.in.aos"));

        var cycleOutput = ExecuteAic(aicPath, "run", cycleInput);
        var missingOutput = ExecuteAic(aicPath, "run", missingInput);

        Assert.That(cycleOutput, Is.EqualTo("Err#runtime_err(code=RUN023 message=\"Circular import detected.\" nodeId=mb2)"));
        Assert.That(missingOutput, Is.EqualTo("Err#runtime_err(code=RUN024 message=\"Import file not found: examples/golden/modules/does_not_exist.aos\" nodeId=rm2)"));
    }

    [Test]
    public void RunSource_ProjectManifest_LoadsEntryFile()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-run-aiproj-");
        try
        {
            var srcDir = Path.Combine(tempDir.FullName, "src");
            Directory.CreateDirectory(srcDir);
            var manifestPath = Path.Combine(tempDir.FullName, "project.aiproj");
            var sourcePath = Path.Combine(srcDir, "main.aos");

            File.WriteAllText(manifestPath, "Program#p1 { Project#proj1(name=\"demo\" entryFile=\"src/main.aos\" entryExport=\"start\") }");
            File.WriteAllText(sourcePath, "Program#p2 { Export#e1(name=start) Let#l1(name=start) { Fn#f1(params=args) { Block#b1 { Call#c1(target=sys.proc_exit) { Lit#i1(value=7) } Return#r1 { Lit#s1(value=\"manifest-ok\") } } } } }");

            var lines = new List<string>();
            var exitCode = AosCliExecutionEngine.RunSource(manifestPath, Array.Empty<string>(), traceEnabled: false, vmMode: "bytecode", lines.Add);

            Assert.That(exitCode, Is.EqualTo(7));
            Assert.That(lines.Count, Is.EqualTo(0));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void RunSource_ProjectManifest_ResolvesImportsRelativeToEntryFile()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-run-aiproj-imports-");
        try
        {
            var srcDir = Path.Combine(tempDir.FullName, "src");
            var libDir = Path.Combine(srcDir, "lib");
            Directory.CreateDirectory(libDir);
            var manifestPath = Path.Combine(tempDir.FullName, "project.aiproj");
            var sourcePath = Path.Combine(srcDir, "main.aos");
            var depPath = Path.Combine(libDir, "dep.aos");

            File.WriteAllText(manifestPath, "Program#p1 { Project#proj1(name=\"demo\" entryFile=\"src/main.aos\" entryExport=\"start\") }");
            File.WriteAllText(depPath, "Program#dp1 { Let#dl1(name=codeFromDep) { Fn#df1(params=_) { Block#db1 { Return#dr1 { Lit#di1(value=9) } } } } Export#de1(name=codeFromDep) }");
            File.WriteAllText(sourcePath, "Program#mp1 { Import#mi1(path=\"lib/dep.aos\") Let#ml1(name=start) { Fn#mf1(params=args) { Block#mb1 { Let#ml2(name=code) { Call#mc1(target=codeFromDep) { Lit#mi2(value=0) } } Call#mc2(target=sys.proc_exit) { Var#mv1(name=code) } Return#mr1 { Lit#mi3(value=0) } } } } Export#me1(name=start) }");

            var lines = new List<string>();
            var exitCode = AosCliExecutionEngine.RunSource(manifestPath, Array.Empty<string>(), traceEnabled: false, vmMode: "bytecode", lines.Add);

            Assert.That(exitCode, Is.EqualTo(9));
            Assert.That(lines.Count, Is.EqualTo(0));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void RunSource_ProjectManifest_MissingField_ReturnsValidationError()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-run-aiproj-bad-");
        try
        {
            var srcDir = Path.Combine(tempDir.FullName, "src");
            Directory.CreateDirectory(srcDir);
            var manifestPath = Path.Combine(tempDir.FullName, "project.aiproj");
            var sourcePath = Path.Combine(srcDir, "main.aos");

            File.WriteAllText(manifestPath, "Program#p1 { Project#proj1(name=\"demo\" entryFile=\"src/main.aos\") }");
            File.WriteAllText(sourcePath, "Program#p2 { Lit#i1(value=1) }");

            var lines = new List<string>();
            var exitCode = AosCliExecutionEngine.RunSource(manifestPath, Array.Empty<string>(), traceEnabled: false, vmMode: "bytecode", lines.Add);

            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(lines.Count, Is.EqualTo(1));
            Assert.That(lines[0], Is.EqualTo("Err#err1(code=VAL002 message=\"Missing attribute 'entryExport'.\" nodeId=proj1)"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void RunSource_ProjectDirectory_LoadsProjectManifest()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-run-dir-");
        try
        {
            var srcDir = Path.Combine(tempDir.FullName, "src");
            Directory.CreateDirectory(srcDir);
            var manifestPath = Path.Combine(tempDir.FullName, "project.aiproj");
            var sourcePath = Path.Combine(srcDir, "main.aos");

            File.WriteAllText(manifestPath, "Program#p1 { Project#proj1(name=\"demo\" entryFile=\"src/main.aos\" entryExport=\"start\") }");
            File.WriteAllText(sourcePath, "Program#p2 { Export#e1(name=start) Let#l1(name=start) { Fn#f1(params=args) { Block#b1 { Call#c1(target=sys.proc_exit) { Lit#i1(value=4) } Return#r1 { Lit#s1(value=\"dir-ok\") } } } } }");

            var lines = new List<string>();
            var exitCode = AosCliExecutionEngine.RunSource(tempDir.FullName, Array.Empty<string>(), traceEnabled: false, vmMode: "bytecode", lines.Add);

            Assert.That(exitCode, Is.EqualTo(4));
            Assert.That(lines.Count, Is.EqualTo(0));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void RunSource_ProjectDirectory_MissingManifest_ReturnsRunError()
    {
        var tempDir = Directory.CreateTempSubdirectory("ailang-run-dir-missing-");
        try
        {
            var lines = new List<string>();
            var exitCode = AosCliExecutionEngine.RunSource(tempDir.FullName, Array.Empty<string>(), traceEnabled: false, vmMode: "bytecode", lines.Add);

            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(lines.Count, Is.EqualTo(1));
            Assert.That(lines[0], Is.EqualTo("Err#err1(code=RUN002 message=\"project.aiproj not found in directory.\" nodeId=project)"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public void Serve_HealthEndpoint_ReturnsOk()
    {
        var appPath = FindRepoFile("examples/golden/http/health_app.aos");
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var port = FindFreePort();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        try
        {
            var response = SendHttpRequest(port, "GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n");
            Assert.That(response.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal), Is.True);
            Assert.That(response.EndsWith("{\"status\":\"ok\"}", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
    }

    [Test]
    public void Serve_HealthEndpoint_ReturnsOk_OverHttps()
    {
        var appPath = FindRepoFile("examples/golden/http/health_app.aos");
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var port = FindFreePort();
        var certDir = Directory.CreateTempSubdirectory("ailang-https-");
        var certPath = Path.Combine(certDir.FullName, "cert.pem");
        var keyPath = Path.Combine(certDir.FullName, "key.pem");
        WriteSelfSignedPem(certPath, keyPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--tls-cert");
        psi.ArgumentList.Add(certPath);
        psi.ArgumentList.Add("--tls-key");
        psi.ArgumentList.Add(keyPath);

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        try
        {
            var response = SendHttpsRequest(port, "GET /health HTTP/1.1\r\nHost: localhost\r\n\r\n");
            Assert.That(response.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal), Is.True);
            Assert.That(response.EndsWith("{\"status\":\"ok\"}", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            certDir.Delete(recursive: true);
        }
    }

    [Test]
    public void Serve_InvalidTlsMaterial_ReturnsRuntimeError()
    {
        var appPath = FindRepoFile("examples/golden/http/health_app.aos");
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var port = FindFreePort();
        var missingCert = Path.Combine(Path.GetTempPath(), $"ailang-missing-cert-{Guid.NewGuid():N}.pem");
        var missingKey = Path.Combine(Path.GetTempPath(), $"ailang-missing-key-{Guid.NewGuid():N}.pem");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(appPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--tls-cert");
        psi.ArgumentList.Add(missingCert);
        psi.ArgumentList.Add("--tls-key");
        psi.ArgumentList.Add(missingKey);

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);

        var exited = process!.WaitForExit(15000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("serve did not exit for invalid TLS material.");
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        Assert.That(process.ExitCode, Is.EqualTo(3), process.StandardError.ReadToEnd());
        Assert.That(
            output,
            Is.EqualTo("Err#runtime_err(code=TLS003 message=\"Failed to load TLS certificate or key.\" nodeId=serve)"));
    }

    [Test]
    public void Bench_Command_ReturnsStructuredReport()
    {
        var repoRoot = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "src", "AiCLI"));
        psi.ArgumentList.Add("bench");
        psi.ArgumentList.Add("--iterations");
        psi.ArgumentList.Add("1");

        using var process = Process.Start(psi);
        Assert.That(process, Is.Not.Null);
        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit(15000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("bench did not exit within timeout.");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        var output = stdoutTask.Result.Trim();
        var error = stderrTask.Result;
        Assert.That(process.ExitCode, Is.EqualTo(0), error);
        var parsed = Parse(output);
        Assert.That(parsed.Diagnostics, Is.Empty);
        Assert.That(parsed.Root, Is.Not.Null);
        Assert.That(parsed.Root!.Kind, Is.EqualTo("Benchmark"));
        Assert.That(parsed.Root.Children.Count, Is.EqualTo(5));
    }

    [Test]
    public void Cli_Version_PrintsDeterministicMetadataLine()
    {
        var devLine = CliVersionInfo.BuildLine(devMode: true);
        var prodLine = CliVersionInfo.BuildLine(devMode: false);

        Assert.That(devLine.StartsWith("airun version=", StringComparison.Ordinal), Is.True);
        Assert.That(devLine.Contains(" aibc=1 ", StringComparison.Ordinal), Is.True);
        Assert.That(devLine.Contains(" mode=dev ", StringComparison.Ordinal), Is.True);
        Assert.That(devLine.Contains(" commit=", StringComparison.Ordinal), Is.True);

        Assert.That(prodLine.StartsWith("airun version=", StringComparison.Ordinal), Is.True);
        Assert.That(prodLine.Contains(" aibc=1 ", StringComparison.Ordinal), Is.True);
        Assert.That(prodLine.Contains(" mode=prod ", StringComparison.Ordinal), Is.True);
        Assert.That(prodLine.Contains(" commit=", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void Cli_HelpText_ContainsCommandSectionsAndExamples()
    {
        var help = CliHelpText.Build(devMode: true);

        Assert.That(help.Contains("Commands:", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("run [app|project-dir] [-- app-args...]", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("Example (explicit, no --): airun run examples/hello.aos arg1 arg2", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("Example (implicit cwd): airun run -- --flag value", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("debug [wrapper-flags] [app|project-dir] [-- app-args...]", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("|  (deprecated; use --)", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("serve <path.aos>", StringComparison.Ordinal), Is.True);
        Assert.That(help.Contains("--vm=bytecode|ast", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void CliInvocationParsing_ExplicitPath_WithTrailingArgs_NoSeparator()
    {
        var ok = CliInvocationParsing.TryResolveTargetAndArgs(
            new[] { "samples/cli-fetch/src/app.aos", "one", "two" },
            Directory.GetCurrentDirectory(),
            out var invocation,
            out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(invocation.TargetPath, Is.EqualTo("samples/cli-fetch/src/app.aos"));
        Assert.That(invocation.AppArgs, Is.EqualTo(new[] { "one", "two" }));
        Assert.That(invocation.UsedImplicitProject, Is.False);
    }

    [Test]
    public void CliInvocationParsing_ExplicitPath_WithDashDash_PassesThroughArgs()
    {
        var ok = CliInvocationParsing.TryResolveTargetAndArgs(
            new[] { "samples/cli-fetch/src/app.aos", "--", "--flag", "value" },
            Directory.GetCurrentDirectory(),
            out var invocation,
            out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(invocation.TargetPath, Is.EqualTo("samples/cli-fetch/src/app.aos"));
        Assert.That(invocation.AppArgs, Is.EqualTo(new[] { "--flag", "value" }));
    }

    [Test]
    public void CliInvocationParsing_ImplicitCwdProject_WithArgs()
    {
        var cwd = Directory.CreateTempSubdirectory("ailang-cli-implicit-");
        try
        {
            File.WriteAllText(Path.Combine(cwd.FullName, "project.aiproj"), "Program#p1 { Project#proj1(name=\"x\" entryFile=\"src/main.aos\" entryExport=\"main\") }");
            var ok = CliInvocationParsing.TryResolveTargetAndArgs(
                new[] { "--", "--alpha", "1" },
                cwd.FullName,
                out var invocation,
                out var error);

            Assert.That(ok, Is.True, error);
            Assert.That(invocation.TargetPath, Is.EqualTo(cwd.FullName));
            Assert.That(invocation.UsedImplicitProject, Is.True);
            Assert.That(invocation.AppArgs, Is.EqualTo(new[] { "--alpha", "1" }));
        }
        finally
        {
            cwd.Delete(true);
        }
    }

    [Test]
    public void CliInvocationParsing_MissingPathAndCwdProject_ReturnsClearError()
    {
        var cwd = Directory.CreateTempSubdirectory("ailang-cli-missing-");
        try
        {
            var ok = CliInvocationParsing.TryResolveTargetAndArgs(
                Array.Empty<string>(),
                cwd.FullName,
                out _,
                out var error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.EqualTo("missing app path (or run from a folder containing project.aiproj)"));
        }
        finally
        {
            cwd.Delete(true);
        }
    }

    [Test]
    public void CliInvocationParsing_LegacySeparatorStillWorks()
    {
        var ok = CliInvocationParsing.TryResolveTargetAndArgs(
            new[] { "samples/cli-fetch/src/app.aos", "|", "--legacy", "1" },
            Directory.GetCurrentDirectory(),
            out var invocation,
            out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(invocation.AppArgs, Is.EqualTo(new[] { "--legacy", "1" }));
        Assert.That(invocation.UsedLegacySeparator, Is.True);
    }

    [Test]
    public void CliInvocationParsing_UnknownWrapperOption_FailsFast()
    {
        var ok = CliInvocationParsing.TryResolveTargetAndArgs(
            new[] { "--unknown" },
            Directory.GetCurrentDirectory(),
            out _,
            out var error);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo("unknown option: --unknown"));
    }

    [Test]
    public void Cli_HelpText_UnknownCommand_ReferencesHelp()
    {
        var message = CliHelpText.BuildUnknownCommand("oops");
        Assert.That(message, Is.EqualTo("Unknown command: oops. See airun --help."));
    }

    [Test]
    public void CliHttpServe_TryParseServeOptions_ParsesPortTlsAndAppArgs()
    {
        var method = typeof(CliHelpText).Assembly
            .GetType("CliHttpServe", throwOnError: true)!
            .GetMethod("TryParseServeOptions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var invokeArgs = new object[] { new[] { "--port", "9443", "--tls-cert", "cert.pem", "--tls-key", "key.pem", "--flag", "value" }, 0, string.Empty, string.Empty, Array.Empty<string>() };
        var ok = (bool)method.Invoke(null, invokeArgs)!;
        var port = (int)invokeArgs[1];
        var tlsCertPath = (string)invokeArgs[2];
        var tlsKeyPath = (string)invokeArgs[3];
        var appArgs = (string[])invokeArgs[4];

        Assert.That(ok, Is.True);
        Assert.That(port, Is.EqualTo(9443));
        Assert.That(tlsCertPath, Is.EqualTo("cert.pem"));
        Assert.That(tlsKeyPath, Is.EqualTo("key.pem"));
        Assert.That(appArgs, Is.EqualTo(new[] { "--flag", "value" }));
    }

    [Test]
    public void CliHttpServe_TryParseServeOptions_InvalidPort_ReturnsFalse()
    {
        var method = typeof(CliHelpText).Assembly
            .GetType("CliHttpServe", throwOnError: true)!
            .GetMethod("TryParseServeOptions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var invokeArgs = new object[] { new[] { "--port", "70000" }, 0, string.Empty, string.Empty, Array.Empty<string>() };
        var ok = (bool)method.Invoke(null, invokeArgs)!;
        var appArgs = (string[])invokeArgs[4];

        Assert.That(ok, Is.False);
        Assert.That(appArgs, Is.Empty);
    }

    [Test]
    public void Parsing_AssignsDeterministicIds_WhenSourceOmitsIds()
    {
        var parse = AosParsing.Parse("Program { Let(name=x) { Lit(value=1) } }");
        Assert.That(parse.Diagnostics, Is.Empty);
        Assert.That(parse.Root, Is.Not.Null);
        Assert.That(parse.Root!.Id, Is.EqualTo("program_cef1fc82ba90"));
        Assert.That(parse.Root.Children[0].Id, Is.EqualTo("let_3ad75de6b0e8"));
        Assert.That(parse.Root.Children[0].Children[0].Id, Is.EqualTo("lit_168514cb3fef"));
    }

    [Test]
    public void DefaultSyscallHost_NetTcpConnectStart_FinalizesConnectionOnPoll()
    {
        var host = new DefaultSyscallHost();
        var state = new VmNetworkState();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = Task.Run(() =>
            {
                using var accepted = listener.AcceptTcpClient();
                Thread.Sleep(100);
            });

            var operationHandle = host.NetTcpConnectStart(state, "127.0.0.1", port);
            Assert.That(operationHandle, Is.GreaterThan(0));
            Assert.That(state.NetConnections.Count, Is.EqualTo(0));

            var status = 0;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                status = host.NetAsyncPoll(state, operationHandle);
                if (status != 0)
                {
                    break;
                }

                Thread.Sleep(10);
            }

            Assert.That(status, Is.EqualTo(1));
            var connectionHandle = host.NetAsyncResultInt(state, operationHandle);
            Assert.That(connectionHandle, Is.GreaterThan(0));
            Assert.That(state.NetConnections.ContainsKey(connectionHandle), Is.True);
            host.NetClose(state, connectionHandle);
            _ = acceptTask.Wait(2000);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static AosParseResult Parse(string source)
    {
        var tokenizer = new AosTokenizer(source);
        var tokens = tokenizer.Tokenize();
        var parser = new AosParser(tokens);
        var result = parser.ParseSingle();
        result.Diagnostics.AddRange(tokenizer.Diagnostics);
        return result;
    }

    private static string ExecuteAic(string aicPath, string mode, string stdin, params string[] extraArgs)
    {
        var aicProgram = Parse(File.ReadAllText(aicPath)).Root!;
        var runtime = new AosRuntime();
        runtime.Permissions.Add("io");
        runtime.Permissions.Add("compiler");
        runtime.ModuleBaseDir = Path.GetDirectoryName(FindRepoFile("AiLang.slnx"))!;
        var argv = new List<string> { mode };
        argv.AddRange(extraArgs);
        runtime.Env["argv"] = AosValue.FromNode(AosRuntimeNodes.BuildArgvNode(argv.ToArray()));
        runtime.ReadOnlyBindings.Add("argv");

        var validator = new AosValidator();
        var envTypes = new Dictionary<string, AosValueKind>(StringComparer.Ordinal)
        {
            ["argv"] = AosValueKind.Node
        };
        var validation = validator.Validate(aicProgram, envTypes, runtime.Permissions, runStructural: false);
        Assert.That(validation.Diagnostics, Is.Empty);

        var oldIn = Console.In;
        var oldOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetIn(new StringReader(stdin));
            Console.SetOut(writer);
            var interpreter = new AosInterpreter();
            interpreter.EvaluateProgram(aicProgram, runtime);
        }
        finally
        {
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }

        return writer.ToString();
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}");
    }

    private static string EscapeAosString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string SendHttpRequest(int port, string request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                using var stream = client.GetStream();
                var bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException("Failed to connect to serve endpoint.", last);
    }

    private static string SendHttpsRequest(int port, string request)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                using var stream = client.GetStream();
                using var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
                ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                });

                var bytes = Encoding.ASCII.GetBytes(request);
                ssl.Write(bytes, 0, bytes.Length);
                ssl.Flush();
                using var reader = new StreamReader(ssl, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException("Failed to connect to HTTPS serve endpoint.", last);
    }

    private static void WriteSelfSignedPem(string certPath, string keyPath)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllText(certPath, cert.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
    }
}
