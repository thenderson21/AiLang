namespace AiVM.Core;

public sealed class VmNetworkState
{
    public Dictionary<int, System.Net.Sockets.TcpListener> NetListeners { get; } = new();
    public Dictionary<int, System.Net.Sockets.TcpClient> NetConnections { get; } = new();
    public Dictionary<int, System.Net.Sockets.UdpClient> NetUdpSockets { get; } = new();
    public Dictionary<int, System.Security.Cryptography.X509Certificates.X509Certificate2> NetTlsCertificates { get; } = new();
    public Dictionary<int, System.Net.Security.SslStream> NetTlsStreams { get; } = new();
    public Dictionary<int, VmNetAsyncOperation> NetAsyncOperations { get; } = new();
    public object NetAsyncLock { get; } = new();
    public int NextNetHandle { get; set; } = 1;
    public int NextNetAsyncHandle { get; set; } = 1;
}

public sealed class VmNetAsyncOperation
{
    public required System.Threading.Tasks.Task Task { get; set; }
    public int Status { get; set; } = 0;
    public int IntResult { get; set; }
    public string StringResult { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
