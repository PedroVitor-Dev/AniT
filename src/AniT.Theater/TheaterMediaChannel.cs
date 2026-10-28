using System.Net.Security;
using System.Net.Sockets;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace AniT.Theater;

public sealed partial class TheaterRoom
{
    private TheaterInvite? hostInvite;
    private readonly SemaphoreSlim mediaConnectGate = new(1, 1);

    private async Task EnsureMediaChannelAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await mediaConnectGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var control = peer ?? throw new IOException("Host desconectado.");
            if (control.Media is not null) return;
            var invite = hostInvite ?? throw new InvalidOperationException("Convite de streaming indisponível.");
            Peer? mediaLink = null;
            try
            {
                mediaLink = await ConnectTlsPeerAsync(invite, timeout.Token).ConfigureAwait(false);
                await mediaLink.Channel.SendAsync(new("media-hello", Token: control.MediaToken), timeout.Token).ConfigureAwait(false);
                var welcome = await mediaLink.Channel.ReadAsync(timeout.Token).ConfigureAwait(false);
                if (welcome.Type != "media-welcome") throw new InvalidDataException("Canal de vídeo recusado. Entre novamente na sala.");
                timeout.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(peer, control) || !control.TryAttachMedia(mediaLink)) throw new IOException("A sala foi encerrada durante a conexão do vídeo.");
                control.MediaBackground = RunMediaPeerAsync(control, mediaLink);
            }
            catch
            {
                mediaLink?.Dispose(); control.Dispose();
                throw;
            }
        }
        finally { mediaConnectGate.Release(); }
    }

    private static async Task<Peer> ConnectTlsPeerAsync(TheaterInvite invite, CancellationToken cancellationToken)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            await client.ConnectAsync(invite.Address, invite.Port, cancellationToken).ConfigureAwait(false);
            var expected = Convert.FromHexString(invite.CertificateHash);
            tls = new SslStream(client.GetStream(), false, (_, cert, _, _) => cert is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), expected));
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "AniT Movie Theater", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, cancellationToken).ConfigureAwait(false);
            return new Peer(client, new PacketChannel(tls));
        }
        catch { tls?.Dispose(); client.Dispose(); throw; }
    }

    private async Task AcceptMediaChannelAsync(Peer mediaLink, string? token, CancellationToken cancellationToken)
    {
        Peer? control = null;
        await admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidate = ActiveGuests.FirstOrDefault(p => p.AcceptsMediaToken(token));
            if (Mode == TheaterMode.DirectStream && candidate is not null && candidate.AcceptsMediaToken(token) && candidate.TryAttachMedia(mediaLink)) control = candidate;
        }
        finally { admissionGate.Release(); }
        if (control is null)
        {
            await mediaLink.Channel.SendAsync(new("reject", Error: "media-channel"), cancellationToken).ConfigureAwait(false);
            return;
        }
        try
        {
            await mediaLink.Channel.SendAsync(new("media-welcome"), cancellationToken).ConfigureAwait(false);
            await RunMediaPeerAsync(control, mediaLink).ConfigureAwait(false);
        }
        finally { control.Dispose(); }
    }

    private async Task RunMediaPeerAsync(Peer control, Peer mediaLink)
    {
        using var active = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var requests = Channel.CreateBounded<RoomPacket>(4);
        var serving = IsHost ? ServeStreamAsync(mediaLink, requests.Reader, active.Token) : Task.CompletedTask;
        try
        {
            while (!active.IsCancellationRequested)
            {
                // An idle/paused video connection is valid; the independent control heartbeat detects departure.
                var packet = await mediaLink.Channel.ReadAsync(active.Token).ConfigureAwait(false);
                if (control.Removed) continue;
                switch (packet.Type)
                {
                    case "stream-request" when IsHost:
                        if (packet.Request <= 0 || packet.Offset < 0 || packet.Offset >= media.Length || packet.Count is < 1 or > StreamBlockSize || packet.Count > media.Length - packet.Offset || !requests.Writer.TryWrite(packet)) throw new InvalidDataException("Pedido de streaming inválido.");
                        break;
                    case "stream-chunk" when !IsHost:
                        ReceiveStreamChunk(packet);
                        break;
                    case "stream-end" when !IsHost:
                        if (!streamBlocks.TryGetValue(packet.Request, out var block) || block.Received != block.Bytes.Length || !block.Complete.TrySetResult(block.Bytes)) throw new InvalidDataException("Trecho incompleto ou não solicitado.");
                        break;
                    default: throw new InvalidDataException("Mensagem incompatível com o canal de vídeo.");
                }
            }
        }
        catch (Exception) when (active.IsCancellationRequested) { }
        catch (Exception) { if (!control.Removed) Notice?.Invoke("O canal de vídeo foi interrompido. Entre novamente na sala."); }
        finally
        {
            active.Cancel(); mediaLink.Dispose(); control.Dispose(); requests.Writer.TryComplete();
            foreach (var pending in streamBlocks.Values) pending.Complete.TrySetException(new IOException("Canal de vídeo desconectado."));
            try { await serving.ConfigureAwait(false); } catch (Exception) { }
        }
    }
}
