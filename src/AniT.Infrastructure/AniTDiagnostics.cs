using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace AniT.Infrastructure;

public static class AniTDiagnostics
{
    private const long MaximumLogBytes = 2L * 1024 * 1024;
    private const int RetainedLogFiles = 5;
    private static readonly object Sync = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Logs");

    public static void Write(string category, string message, Exception? exception = null)
    {
        var settings = AniTSystemSettingsStore.Load();
        if (!settings.DiagnosticLoggingEnabled && !settings.DeveloperMode) return;
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var current = Path.Combine(LogDirectory, "anit.log");
                RotateIfNeeded(current);
                File.AppendAllText(current,
                    $"{DateTimeOffset.Now:O} [{category}] {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private static void RotateIfNeeded(string current)
    {
        if (!File.Exists(current) || new FileInfo(current).Length < MaximumLogBytes) return;
        for (var index = RetainedLogFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? current : Path.Combine(LogDirectory, $"anit.{index - 1}.log");
            var destination = Path.Combine(LogDirectory, $"anit.{index}.log");
            if (File.Exists(source)) File.Move(source, destination, true);
        }
    }
}

public sealed class AniTOnlineRequestHandler : DelegatingHandler
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> Limiters = new();
    private readonly Func<AniTSystemSettings> settingsProvider;

    public AniTOnlineRequestHandler(HttpMessageHandler? innerHandler = null, Func<AniTSystemSettings>? settingsProvider = null)
        : base(innerHandler ?? AniTNetworkSecurity.CreateRestrictedHandler()) => this.settingsProvider = settingsProvider ?? AniTSystemSettingsStore.Load;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = settingsProvider();
        if (settings.OfflineMode)
        {
            AniTDiagnostics.Write("ONLINE", $"Bloqueado pelo modo offline: {request.Method} {request.RequestUri?.Host}");
            throw new HttpRequestException("O AniT está em modo totalmente offline.");
        }

        var limiter = Limiters.GetOrAdd(settings.OnlineConnectionLimit, value => new SemaphoreSlim(value, value));
        await limiter.WaitAsync(cancellationToken);
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.OnlineSourceTimeoutSeconds));
        try
        {
            var response = await base.SendAsync(request, timeout.Token);
            AniTDiagnostics.Write("ONLINE", $"{request.Method} {request.RequestUri?.Host} → {(int)response.StatusCode} em {timer.ElapsedMilliseconds} ms");
            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            AniTDiagnostics.Write("ONLINE", $"Timeout após {settings.OnlineSourceTimeoutSeconds}s: {request.RequestUri?.Host}", exception);
            throw new HttpRequestException($"A fonte online excedeu o limite de {settings.OnlineSourceTimeoutSeconds} segundos.", exception);
        }
        catch (Exception exception)
        {
            AniTDiagnostics.Write("ONLINE", $"Falha em {request.RequestUri?.Host}", exception);
            throw;
        }
        finally
        {
            limiter.Release();
        }
    }
}

public static class AniTNetworkSecurity
{
    public static bool IsPotentiallyPublicUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return !IPAddress.TryParse(uri.Host, out var address) || IsPublicAddress(address);
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && !address.IsIPv6SiteLocal
                && (bytes[0] & 0xFE) != 0xFC;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var octets = address.GetAddressBytes();
        return octets[0] != 0
            && octets[0] != 10
            && octets[0] != 127
            && !(octets[0] == 100 && octets[1] is >= 64 and <= 127)
            && !(octets[0] == 169 && octets[1] == 254)
            && !(octets[0] == 172 && octets[1] is >= 16 and <= 31)
            && !(octets[0] == 192 && octets[1] == 168)
            && !(octets[0] == 198 && octets[1] is 18 or 19)
            && octets[0] < 224;
    }

    internal static HttpMessageHandler CreateRestrictedHandler() => new SocketsHttpHandler
    {
        ConnectCallback = ConnectPublicAsync
    };

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var candidates = addresses.Where(IsPublicAddress).ToArray();
        if (candidates.Length == 0)
            throw new HttpRequestException("A fonte online tentou acessar um endereço de rede local ou reservado.");

        Exception? lastError = null;
        foreach (var address in candidates)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastError = exception;
                socket.Dispose();
            }
        }

        throw new HttpRequestException("Não foi possível conectar à fonte online.", lastError);
    }
}
