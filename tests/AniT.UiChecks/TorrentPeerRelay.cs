using System.IO;
using System.Net;
using System.Net.Sockets;

/// <summary>Throttles raw peer traffic, including the first burst, independently of the torrent engine.</summary>
internal sealed class TorrentPeerRelay : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource lifetime = new();
    readonly List<Task> clients = [];
    readonly int upstreamPort;
    readonly Task accepting;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int BytesPerSecond;
    public TorrentPeerRelay(int upstreamPort, int bytesPerSecond)
    {
        this.upstreamPort=upstreamPort; BytesPerSecond=bytesPerSecond; listener.Start(); accepting=AcceptAsync();
    }
    async Task AcceptAsync()
    {
        try {
            while(!lifetime.IsCancellationRequested) {
                var client=await listener.AcceptTcpClientAsync(lifetime.Token);
                clients.Add(ForwardAsync(client));
            }
        } catch(OperationCanceledException) { }
          catch(SocketException) when(lifetime.IsCancellationRequested) { }
    }
    async Task ForwardAsync(TcpClient client)
    {
        using(client)using(var upstream=new TcpClient())using(var stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
            try {
                await upstream.ConnectAsync(IPAddress.Loopback,upstreamPort,stop.Token);
                var upload=client.GetStream().CopyToAsync(upstream.GetStream(),stop.Token);
                var download=CopyLimitedAsync(upstream.GetStream(),client.GetStream(),stop.Token);
                await Task.WhenAny(upload,download); stop.Cancel();
                try {await Task.WhenAll(upload,download);}catch(OperationCanceledException) { }
            } catch(Exception ex) when(ex is IOException or SocketException or OperationCanceledException) { }
        }
    }
    async Task CopyLimitedAsync(Stream source,Stream destination,CancellationToken token)
    {
        var buffer=new byte[16*1024];int count;
        while((count=await source.ReadAsync(buffer,token))>0) {
            var rate=Volatile.Read(ref BytesPerSecond);
            if(rate>0)await Task.Delay(TimeSpan.FromSeconds(count/(double)rate),token);
            await destination.WriteAsync(buffer.AsMemory(0,count),token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();listener.Stop();await accepting;
        await Task.WhenAll(clients);lifetime.Dispose();
    }
}
