using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AniT.Core;
using AniT.Player;
using AniT.Theater;
using Microsoft.Win32;

namespace AniT.App;

public partial class MovieTheaterWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim playerGate = new(1, 1);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? operation;
    private TheaterRoom? room;
    private MpcHcPlayer? player;
    private MovieTheaterViewerWindow? viewer;
    private bool closingViewer;
    internal MovieTheaterViewerWindow? ActiveViewer => viewer;
    internal MpcHcPlayer? ActivePlayer => player;
    internal Exception? LastPlaybackError { get; private set; }
    private MediaIdentity? media;
    private string? selectedPath;
    private DateTime selectedModified;
    private bool prepared;
    private bool busy;
    private bool closed;
    private bool ended;
    private long scheduledStart;
    private long lastGuestSeekAt;
    private int trackCounter;
    private bool modeChosen;
    private bool? hostRole;
    private TheaterMode selectedMode;
    private Uri? streamingAddress;
    private FrameworkElement? visibleSection;

    public MovieTheaterWindow() : this(null) { }

    internal MovieTheaterWindow(TheaterRoom? initialRoom)
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1080, 840);
        HostAddress.Text = SuggestedAddress();
        timer.Tick += async (_, _) => await TickAsync();
        if (initialRoom is not null)
        {
            room = initialRoom; modeChosen = true; hostRole = initialRoom.IsHost; selectedMode = initialRoom.Mode;
            MediaLabel.Text = "Episódio de demonstração";
            BindRoom();
            RoomInvite.Text = room.Invite?.Encode() ?? string.Empty;
        }
        SizeChanged += (_, _) => UpdateCinemaLayout();
        TheaterScroller.SizeChanged += (_, _) => UpdateCinemaLayout();
        TheaterScroller.ScrollChanged += (_, args) => { if (args.ViewportWidthChange != 0) UpdateCinemaLayout(); };
        Loaded += (_, _) => UpdateCinemaLayout();
        JoinInvite.TextChanged += (_, _) => UpdateControls();
        UseLocalCopyCheck.Checked += (_, _) => UpdateControls();
        UseLocalCopyCheck.Unchecked += (_, _) => UpdateControls();
        UpdateControls();
    }

    private void UpdateCinemaLayout()
    {
        var width = TheaterScroller.ViewportWidth;
        if (!double.IsFinite(width) || width <= 0) width = Math.Max(280, TheaterRoot.ActualWidth - 24);
        width = Math.Min(1040, width);
        var columns = width >= 740;
        var cardWidth = columns ? (width - 16) / 2 : Math.Max(240, width - 4);
        StreamingModeOption.Width = SyncModeOption.Width = cardWidth;
        StreamingModeOption.Margin = columns ? new Thickness(0, 0, 16, 16) : new Thickness(0, 0, 0, 16);
        HostRoleOption.Width = GuestRoleOption.Width = cardWidth;
        HostRoleOption.Margin = StreamingModeOption.Margin;
        HeroArt.Visibility = width >= 650 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimateCinemaSection(FrameworkElement section)
    {
        if (ReferenceEquals(visibleSection, section)) return;
        visibleSection = section;
        var duration = TryFindResource("AniT.Motion.Normal") is Duration configured ? configured : new Duration(TimeSpan.Zero);
        section.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.8, 1, duration) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
    }

    private void ChooseMode_Click(object sender, RoutedEventArgs e)
    {
        if (modeChosen)
        {
            if (HostRoleOption.IsChecked != true && GuestRoleOption.IsChecked != true) { TheaterStatus.Text = "Escolha Host ou Convidado para continuar."; return; }
            SelectRole(HostRoleOption.IsChecked == true); return;
        }
        if (StreamingModeOption.IsChecked != true && SyncModeOption.IsChecked != true) { TheaterStatus.Text = "Escolha um modo para continuar."; return; }
        SelectMode(StreamingModeOption.IsChecked == true ? TheaterMode.DirectStream : TheaterMode.PeerSync);
    }
    internal void SelectMode(TheaterMode mode)
    {
        if (room is not null || busy) return;
        selectedMode = mode; modeChosen = true; hostRole = null;
        HostRoleOption.IsChecked = GuestRoleOption.IsChecked = false;
        StreamingModeOption.IsChecked = mode == TheaterMode.DirectStream; SyncModeOption.IsChecked = mode == TheaterMode.PeerSync;
        TheaterStatus.Text = "Agora escolha se você vai criar a sala ou entrar com um convite.";
        UpdateControls();
    }
    internal void SelectRole(bool host)
    {
        if (!modeChosen || room is not null || busy) return;
        hostRole = host; media = null; selectedPath = null;
        MediaLabel.Text = "Nenhum vídeo selecionado";
        ShareEpisodeCheck.IsChecked = false; UseLocalCopyCheck.IsChecked = false;
        TheaterStatus.Text = host ? "Selecione o episódio para criar sua sala." : "Cole o convite recebido para entrar na sala.";
        UpdateControls();
    }
    private void ChangeMode_Click(object sender, RoutedEventArgs e) { if (room is null && !busy) { modeChosen = false; hostRole = null; ShareEpisodeCheck.IsChecked = false; UpdateControls(); } }
    private void BackStep_Click(object sender, RoutedEventArgs e)
    {
        if (room is not null || busy) return;
        if (hostRole.HasValue) { hostRole = null; ShareEpisodeCheck.IsChecked = false; TheaterStatus.Text = "Escolha Host ou Convidado para continuar."; UpdateControls(); }
        else ChangeMode_Click(sender, e);
    }

    private static string SuggestedAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))?.Address.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private async void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        if (busy || room is not null) return;
        var dialog = new OpenFileDialog { Title = "Selecione sua cópia do episódio", Filter = "Vídeos|*.mkv;*.mp4;*.avi;*.webm;*.mov;*.m4v;*.ts;*.m2ts;*.wmv", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await SelectMediaAsync(dialog.FileName);
    }

    internal TheaterRoom? ActiveRoom => room;

    internal async Task SelectMediaAsync(string path)
    {
        if (busy || room is not null) return;
        await RunAsync(async token =>
        {
            media = null; selectedPath = null;
            MediaLabel.Text = Path.GetFileName(path);
            MediaDetail.Text = "Verificando o arquivo completo…";
            TheaterStatus.Text = "Verificando o vídeo. Você pode cancelar esta operação.";
            var identity = await MediaIdentity.FromFileAsync(path, token);
            token.ThrowIfCancellationRequested();
            media = identity; selectedPath = Path.GetFullPath(path);
            selectedModified = File.GetLastWriteTimeUtc(selectedPath);
            MediaDetail.Text = $"Verificado · {identity.Length / 1048576d:0.0} MB · compare a mesma versão do arquivo ntodos os computadores.";
            TheaterStatus.Text = hostRole == true ? "Arquivo verificado. Confira a autorização e crie sua sala." : "Arquivo verificado. Cole o convite recebido e entre na sala.";
        });
        if (media is null && !closed) MediaDetail.Text = "Verificação não concluída. Selecione o vídeo novamente.";
    }

    private async void CreateRoom_Click(object sender, RoutedEventArgs e) => await RunAsync(token =>
    {
        if (hostRole != true) throw new InvalidOperationException("Escolha Host antes de criar uma sala.");
        EnsureMedia();
        if (!IPAddress.TryParse(HostAddress.Text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork || address.Equals(IPAddress.Any)) throw new InvalidOperationException("Informe o IPv4 deste computador na rede usada pelo participante.");
        if (!int.TryParse(HostPort.Text, out var port) || port is < 1024 or > 65535) throw new InvalidOperationException("Informe uma porta TCP entre 1024 e 65535.");
        if (!modeChosen) throw new InvalidOperationException("Escolha o modo da sala primeiro.");
        if (selectedMode == TheaterMode.DirectStream && ShareEpisodeCheck.IsChecked != true) throw new InvalidOperationException("Autorize a transmissão do episódio antes de criar a sala de streaming.");
        room = TheaterRoom.Host(media!, address, port, sharedFilePath: ShareEpisodeCheck.IsChecked == true ? selectedPath : null, mode: selectedMode, displayName: ProfileSettingsStore.Load().DisplayName);
        BindRoom();
        RoomInvite.Text = room.Invite!.Encode();
        TheaterStatus.Text = "Sala criada. Envie o convite e clique em Abrir episódio. O Windows pode pedir acesso à rede para o AniT.";
        return Task.CompletedTask;
    });

    private async void JoinRoom_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        if (hostRole != false) throw new InvalidOperationException("Escolha Convidado antes de entrar na sala.");
        var localMedia = selectedMode == TheaterMode.PeerSync && UseLocalCopyCheck.IsChecked == true ? media : null;
        if (UseLocalCopyCheck.IsChecked == true && selectedMode == TheaterMode.PeerSync) EnsureMedia();
        TheaterStatus.Text = "Conectando ao host e conferindo o episódio…";
        var connected = await TheaterRoom.JoinAsync(JoinInvite.Text, localMedia, token, displayName: ProfileSettingsStore.Load().DisplayName);
        if (closed || token.IsCancellationRequested) { await connected.DisposeAsync(); token.ThrowIfCancellationRequested(); return; }
        room = connected; selectedMode = connected.Mode; modeChosen = true; hostRole = false;
        if (!connected.HasMedia) { media = null; selectedPath = null; MediaLabel.Text = connected.OfferedName; MediaDetail.Text = connected.Mode == TheaterMode.DirectStream ? "Streaming por trechos do PC do host. Não precisa baixar o episódio completo." : "Baixe o episódio compartilhado pelo host na preparação."; }
        BindRoom();
        TheaterStatus.Text = connected.Mode == TheaterMode.DirectStream ? "Conectado. Clique em Abrir transmissão para receber os primeiros trechos e abrir sua tela de vídeo." : connected.HasMedia ? "Conectado. O arquivo é idêntico ao do host. Clique em Abrir episódio." : "Conectado. Escolha onde salvar e baixe o episódio antes de abri-lo.";
    });

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (busy || room is not { IsHost: false, HasMedia: false, SharingAvailable: true } active) return;
        var dialog = new SaveFileDialog { Title = "Salvar episódio em um novo arquivo", FileName = active.OfferedName, Filter = "Vídeo|*" + Path.GetExtension(active.OfferedName), AddExtension = true, OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return;
        if (room?.Mode == TheaterMode.PeerSync)
        {
            await ScheduleP2pDownloadAsync(dialog.FileName);
        }
        else await DownloadMediaAsync(dialog.FileName);
    }

    internal async Task DownloadMediaAsync(string destination) => await RunAsync(async token =>
    {
        var active = room ?? throw new InvalidOperationException("Entre na sala primeiro.");
        TheaterStatus.Text = "Baixando o episódio. Você poderá abri-lo quando a verificação terminar.";
        var path = await active.DownloadAsync(destination, token);
        if (closed) return;
        media = active.Episode; selectedPath = path; selectedModified = File.GetLastWriteTimeUtc(path);
        MediaLabel.Text = Path.GetFileName(path);
        MediaDetail.Text = $"Download verificado · {media.Length / 1048576d:0.0} MB · arquivo OK ntodos os computadores.";
        TheaterStatus.Text = "Download verificado. O AniT vai abrir o vídeo pausado; depois confirme sua presença.";
    });

    private void BindRoom()
    {
        automaticOpenAttempted = false; pendingDownloadPath = null; lastInviteConnection = null;
        room!.Changed += RoomChanged;
        room.Notice += RoomNotice;
        PositionLabel.Text = "00:00 / 00:00";
        SyncLabel.Text = "Aguardando abertura do vídeo";
        timer.Start();
        UpdateControls();
    }

    private void RoomChanged()
    {
        if (closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (closed) return;
            if (room is { IsHost: false, IsConnected: false, ExitReason: TheaterExitReason.RemovedByHost })
            {
                await ReturnAfterRemovalAsync(); return;
            }
            if (room is { IsHost: false, IsConnected: false })
            {
                try { player?.SetPlaybackPermission(false); }
                catch (Exception) { /* Closing still releases an unresponsive player. */ }
                operation?.Cancel();
                Close();
                return;
            }
            if (room is { } active && (!active.BothReady || !active.PlaybackAuthorized || active.StreamHold)) LockPlayerForBuffer();
            UpdateControls();
        }));
    }

    private void RoomNotice(string message)
    {
        if (closed || Dispatcher.HasShutdownStarted) return;
        var origin = room;
        Dispatcher.BeginInvoke(new Action(() => { if (!closed && ReferenceEquals(room, origin)) TheaterStatus.Text = message; }));
    }

    private void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(RoomInvite.Text); TheaterStatus.Text = "Convite copiado. Compartilhe-o com o participante."; }
        catch { TheaterStatus.Text = "Não foi possível copiar. Selecione o convite e copie manualmente."; }
    }

    private async void PreparePlayer_Click(object sender, RoutedEventArgs e) => await RunAsync(OpenSessionVideoAsync);

    private async Task OpenSessionVideoAsync(CancellationToken token)
    {
        var active = room ?? throw new InvalidOperationException("Crie uma sala ou entre primeiro.");
        if (active.Mode == TheaterMode.PeerSync && !active.PreparationStarted)
        {
            if (!active.IsHost) throw new InvalidOperationException("Aguarde o host iniciar a preparação.");
            EnsureMedia(); await active.BeginPreparationAsync(token);
        }
        if (active.IsHost || active.Mode == TheaterMode.PeerSync) EnsureMedia();
        else { TheaterStatus.Text = "Recebendo os primeiros trechos da transmissão…"; streamingAddress = await active.StartStreamingAsync(token); }
        await playerGate.WaitAsync(token);
        try
        {
            if (!prepared)
            {
                TheaterStatus.Text = "Abrindo o vídeo no MPC-HC, pausado…";
                var path = Path.Combine(AppContext.BaseDirectory, "Player", "MPC-HC", "mpc-hc64.exe");
                if (!File.Exists(path)) throw new FileNotFoundException("O MPC-HC integrado não foi encontrado. Confira a instalação do AniT.");
                player?.Dispose();
                lastGuestPosition = 0; LastPlaybackError = null;
                player = new MpcHcPlayer(path, independent: true, viewerOnly: !active.IsHost);
                player.SetPlaybackPermission(false);
                player.PlaybackEnded += (_, _) => ended = true;
                player.PlaybackClosed += (_, _) => { prepared = false; _ = UnreadyAsync(); };
                Action<IntPtr> attachViewer = handle =>
                {
                    playerWindowHandle = handle;
                    if (active.IsHost) return;
                    CloseViewer();
                    viewer = new MovieTheaterViewerWindow(handle, new System.Windows.Interop.WindowInteropHelper(this).Handle);
                    viewer.Closed += async (_, _) =>
                    {
                        if (closingViewer || closed) return;
                        prepared = false;
                        operation?.Cancel();
                        await UnreadyAsync();
                        await playerGate.WaitAsync();
                        try { player?.Dispose(); player = null; viewer = null; }
                        finally { playerGate.Release(); }
                    };
                    viewer.ShowPlayer();
                };
                if (!active.IsHost && active.Mode == TheaterMode.DirectStream) await player.PrepareStreamAsync(streamingAddress!, token, attachViewer);
                else await player.PrepareLocalFileAsync(selectedPath!, token, attachViewer);
                viewer?.RefreshBounds();
                prepared = true; ended = false;
            }
            await player!.PauseAsync(token);
            if (!active.IsHost && active.Mode == TheaterMode.DirectStream && player.Duration is { } duration) active.ConfigureStreamingMedia(duration);
            await active.SetReadyAsync(true, token);
            TheaterStatus.Text = active.Mode == TheaterMode.PeerSync ? active.IsHost ? "Seu vídeo está pausado. Aguarde a confirmação do convidado para autorizar o início." : "Vídeo aberto e pausado. Confirme sua presença para o host autorizar." : active.IsHost ? "Episódio aberto e pausado. Quando o convidado também estiver pronto, use Iniciar juntos." : "Você está pronto. Aguarde o host iniciar.";
        }
        catch
        {
            prepared = false;
            CloseViewer();
            player?.Dispose(); player = null;
            throw;
        }
        finally { playerGate.Release(); }
    }

    private async Task UnreadyAsync()
    {
        var active = room;
        try { if (active is not null) await active.SetReadyAsync(false, lifetime.Token); }
        catch (Exception) { }
        if (!closed) { TheaterStatus.Text = "A janela de vídeo foi fechada. Abra o episódio ou a transmissão novamente para continuar."; UpdateControls(); }
    }

    private async void Play_Click(object sender, RoutedEventArgs e) => await PlayTogetherAsync();

    private Task PlayTogetherAsync() => HostCommandAsync(async token =>
    {
        player!.SetPlaybackPermission(false);
        await player.PauseAsync(token);
        player.SetSpeed(1);
        var position = ended ? TimeSpan.Zero : await player.GetPositionAsync(token);
        if (ended) await player.SeekAsync(position, token);
        ended = false;
        if (room!.Mode == TheaterMode.PeerSync) scheduledStart = await room.ApprovePlaybackAsync(position.TotalSeconds, token, player.AudioTrack, player.SubtitleTrack);
        else { scheduledStart = ClockEstimator.NowMs + 350; await room.ResumeStreamingAsync(position.TotalSeconds, scheduledStart, token, player.AudioTrack, player.SubtitleTrack); }
        TheaterStatus.Text = room.Mode == TheaterMode.PeerSync ? "Host autorizou. A sessão começa em 5 segundos…" : "Iniciando transmissão sincronizada…";
        UpdateControls();
    });

    private async void Pause_Click(object sender, RoutedEventArgs e) => await HostCommandAsync(async token =>
    {
        scheduledStart = 0;
        await player!.PauseAsync(token); player.SetSpeed(1);
        await room!.PublishAsync((await player.GetPositionAsync(token)).TotalSeconds, false, token, audioTrack: player.AudioTrack, subtitleTrack: player.SubtitleTrack);
        TheaterStatus.Text = "Reprodução pausada para todos os participantes.";
    });

    private async void Seek_Click(object sender, RoutedEventArgs e) => await HostCommandAsync(async token =>
    {
        if (!TimeSpan.TryParseExact(SeekPosition.Text.Trim(), new[] { @"h\:mm\:ss", @"hh\:mm\:ss", @"m\:ss", @"mm\:ss" }, CultureInfo.InvariantCulture, out var position)
            || position < TimeSpan.Zero || player!.Duration is { } duration && position > duration)
            throw new InvalidOperationException("Informe uma posição válida dentro do episódio, como 00:12:30.");
        scheduledStart = 0; ended = false;
        var playing = await player!.GetStateAsync(token) == PlayerState.Playing;
        await player.SeekAsync(position, token);
        await room!.PublishAsync(position.TotalSeconds, playing, token, audioTrack: player.AudioTrack, subtitleTrack: player.SubtitleTrack);
        TheaterStatus.Text = "Posição enviada ao participante.";
    });

    private async Task HostCommandAsync(Func<CancellationToken, Task> command) => await RunAsync(async token =>
    {
        if (room is not { IsHost: true, BothReady: true } || !prepared) throw new InvalidOperationException("Todos os participantes precisam abrir o vídeo antes de iniciar.");
        await playerGate.WaitAsync(token);
        try { await command(token); }
        finally { playerGate.Release(); }
    });

    private async Task TickAsync()
    {
        UpdateHostManager();
        await SampleGuestBufferAsync();
        UpdatePlaybackActions();
        UpdateP2pPreparationView();
        UpdateStreamBufferView();
        await AdvanceP2pPreparationAsync();
        if (closed || busy || player is null || !prepared || room is null || !await playerGate.WaitAsync(0)) return;
        try
        {
            var active = room;
            if (!active.BothReady || !active.PlaybackAuthorized)
            {
                scheduledStart = 0;
                player.SetPlaybackPermission(false);
                player.SetSpeed(1);
                SyncLabel.Text = active.Mode == TheaterMode.PeerSync && active.BothReady ? "Vídeos pausados · aguardando confirmação e aval do host." : "Reprodução pausada · aguardando todos os participantes.";
                return;
            }
            var now = ClockEstimator.NowMs;
            if (active.IsHost && active.StreamHold) { await HoldStreamForGuestAsync(active); return; }
            if (active.IsHost && scheduledStart > 0)
            {
                if (now < scheduledStart) { SyncLabel.Text = $"Começa em {Math.Ceiling((scheduledStart - now) / 1000d):0}…"; return; }
                player.SetPlaybackPermission(true);
                player.Resume(); scheduledStart = 0;
            }
            if (active.IsHost && scheduledStart == 0) player.SetPlaybackPermission(true);
            viewer?.RefreshBounds();
            if (player.CurrentFilePath is { } actual && !(active.Mode == TheaterMode.DirectStream && !active.IsHost ? Uri.TryCreate(actual, UriKind.Absolute, out var actualUri) && actualUri == streamingAddress : string.Equals(Path.GetFullPath(actual), selectedPath, StringComparison.OrdinalIgnoreCase)))
            {
                prepared = false;
                await player.PauseAsync(lifetime.Token);
                await active.SetReadyAsync(false, lifetime.Token);
                TheaterStatus.Text = "O episódio aberto no MPC-HC mudou. Saia da sala e selecione o novo episódio para verificar as cópias dos participantes.";
                UpdateControls();
                return;
            }
            var position = await player.GetPositionAsync(lifetime.Token);
            if (closed || !prepared || player is null) return;
            if (!active.IsHost) lastGuestPosition = position.TotalSeconds;
            var playing = await player.GetStateAsync(lifetime.Token) == PlayerState.Playing;
            PositionLabel.Text = $"{Format(position)} / {Format(player.Duration ?? TimeSpan.Zero)}";
            if (active.IsHost)
            {
                if (active.StreamHold) { await HoldStreamForGuestAsync(active); return; }
                player.SetSpeed(1);
                if (++trackCounter % 10 == 0) player.RefreshTracks();
                if (ended) { await player.PauseAsync(lifetime.Token); playing = false; }
                // Small timeline packets follow every player sample; binary media frames stay bounded.
                await active.PublishAsync(position.TotalSeconds, playing, lifetime.Token, audioTrack: player.AudioTrack, subtitleTrack: player.SubtitleTrack);
                SyncLabel.Text = playing ? "Host · reproduzindo" : "Host · pausado";
            }
            else if (active.LatestState is { } state && active.Clock.IsReady)
            {
                var hostNow = ClockEstimator.NowMs + active.Clock.OffsetMs;
                if (hostNow - state.HostTimeMs > 5000) { player.SetPlaybackPermission(false); await player.PauseAsync(lifetime.Token); SyncLabel.Text = "Aguardando atualização do host…"; return; }
                var correction = TimelineSync.Correct(state, position.TotalSeconds, hostNow, streaming: active.Mode == TheaterMode.DirectStream);
                var shouldPlay = state.Playing && !active.StreamHold && hostNow >= state.HostTimeMs;
                player.SetPlaybackPermission(shouldPlay);
                player.SetTracks(state.AudioTrack, state.SubtitleTrack);
                if (!shouldPlay && playing) await player.PauseAsync(lifetime.Token);
                if (!active.StreamHold && correction.Seek && (active.Mode != TheaterMode.DirectStream || !state.Playing || now - lastGuestSeekAt >= 1500 || Math.Abs(correction.TargetSeconds - position.TotalSeconds) > 3))
                {
                    await player.SeekAsync(TimeSpan.FromSeconds(correction.TargetSeconds), lifetime.Token);
                    lastGuestSeekAt = now;
                }
                player.SetSpeed(shouldPlay ? correction.Speed : 1);
                if (shouldPlay && !playing) player.Resume();
                SyncLabel.Text = active.StreamHold ? active.GuestBuffering ? "Sessão pausada · recebendo dados do vídeo." : "Buffer pronto · aguardando o host retomar." : hostNow < state.HostTimeMs ? "Aguardando início sincronizado…" : $"Seguindo o host · diferença estimada {Math.Abs(correction.TargetSeconds - position.TotalSeconds) * 1000:0} ms";
            }
            else player.SetPlaybackPermission(false);
        }
        catch (OperationCanceledException) when (closed) { }
        catch (InvalidOperationException) when (!closed && room is { IsHost: true, Mode: TheaterMode.DirectStream, StreamHold: true })
        {
            // A buffer report may arrive between sampling the host and publishing its playing state.
            scheduledStart = 0; LockPlayerForBuffer();
        }
        catch (Exception error) when (!closed && (error is OperationCanceledException or TimeoutException) && room is { IsHost: false, Mode: TheaterMode.DirectStream, StreamHold: true })
        {
            LockPlayerForBuffer();
            SyncLabel.Text = "Sessão pausada · o player aguarda os dados do vídeo.";
        }
        catch (Exception error)
        {
            LastPlaybackError = error;
            prepared = false; scheduledStart = 0;
            try { if (room is not null) await room.SetReadyAsync(false); } catch (Exception) { }
            CloseViewer();
            try { player?.Dispose(); } catch (Exception) { }
            player = null;
            if (!closed) { TheaterStatus.Text = "A reprodução foi interrompida. Confira a conexão e abra o vídeo novamente."; UpdateControls(); }
        }
        finally { playerGate.Release(); }
    }

    private async void LeaveRoom_Click(object sender, RoutedEventArgs e) => await RunAsync(async _ => await LeaveRoomAsync());

    private async Task LeaveRoomAsync()
    {
        CloseFloatingWindows();
        timer.Stop(); scheduledStart = 0; streamingAddress = null; pendingDownloadPath = null; automaticOpenAttempted = false;
        voiceDevice?.Dispose(); voiceDevice = null;
        var previous = room; room = null;
        if (previous is not null)
        {
            previous.Changed -= RoomChanged; previous.Notice -= RoomNotice;
            await previous.DisposeAsync();
        }
        await playerGate.WaitAsync();
        try { prepared = false; CloseViewer(); player?.Dispose(); player = null; }
        finally { playerGate.Release(); }
        RoomInvite.Clear();
        TheaterStatus.Text = previous?.IsHost == true ? "Sala encerrada. Crie outra para gerar um novo convite." : "Você saiu da sala. Entre pelo convite do host para reconectar.";
    }

    private void EnsureMedia()
    {
        if (media is null || selectedPath is null) throw new InvalidOperationException("Selecione e verifique o vídeo primeiro.");
        var info = new FileInfo(selectedPath);
        if (!info.Exists || info.Length != media.Length || info.LastWriteTimeUtc != selectedModified) throw new IOException("O vídeo foi alterado ou não está disponível. Saia da sala e selecione-o novamente.");
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || closed || returningAfterRemoval) return;
        busy = true;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = pending; UpdateControls();
        try { await action(pending.Token); }
        catch (OperationCanceledException) { if (!closed) TheaterStatus.Text = pending.IsCancellationRequested ? "Operação cancelada." : "A conexão não respondeu a tempo. Confira o endereço, a porta e o acesso à rede."; }
        catch (Exception error) { if (!closed) TheaterStatus.Text = error is SocketException ? "Não foi possível conectar. Confira o endereço, a porta e o acesso do AniT à rede." : error is TimeoutException ? "O vídeo não abriu ou a conexão não respondeu a tempo. Tente novamente." : error.Message; preparationPopup?.ShowError(TheaterStatus.Text); voicePopup?.ShowError(TheaterStatus.Text); }
        finally { operation = null; busy = false; if (!closed) UpdateControls(); }
    }

    private void UpdateControls()
    {
        TheaterRoot.IsEnabled = !returningAfterRemoval;
        var active = room;
        UpdateHostManager();
        ModePanel.Visibility = !modeChosen && active is null ? Visibility.Visible : Visibility.Collapsed;
        ModeSummaryPanel.Visibility = modeChosen || active is not null ? Visibility.Visible : Visibility.Collapsed;
        RolePanel.Visibility = modeChosen && !hostRole.HasValue && active is null ? Visibility.Visible : Visibility.Collapsed;
        SetupPanel.Visibility = modeChosen && hostRole.HasValue && active is null ? Visibility.Visible : Visibility.Collapsed;
        ChangeModeButton.Visibility = active is null ? Visibility.Visible : Visibility.Collapsed;
        ChangeModeButton.IsEnabled = !busy;
        BackStepButton.Visibility = modeChosen && active is null ? Visibility.Visible : Visibility.Collapsed;
        BackStepButton.IsEnabled = !busy;
        ChooseModeButton.Visibility = (!modeChosen || !hostRole.HasValue) && active is null ? Visibility.Visible : Visibility.Collapsed;
        CinemaHero.MinHeight = modeChosen ? 112 : 184;
        CinemaHero.Padding = modeChosen ? new Thickness(24, 16, 24, 16) : new Thickness(28, 24, 28, 24);
        HeroArt.Width = HeroArt.Height = modeChosen ? 92 : 144;
        HeroDescription.Visibility = HeroBadges.Visibility = modeChosen ? Visibility.Collapsed : Visibility.Visible;
        UpdateCinemaLayout();
        AnimateCinemaSection(active is not null ? SessionPanel : !modeChosen ? ModePanel : hostRole.HasValue ? SetupPanel : RolePanel);
        FlowStep.Text = active is not null ? "SALA DE PREPARAÇÃO" : !hostRole.HasValue ? "ETAPA 2 DE 3 · SEU PAPEL" : hostRole == true ? "ETAPA 3 DE 3 · CRIAR SALA" : "ETAPA 3 DE 3 · ENTRAR NA SALA";
        ModeSummary.Text = (active?.Mode ?? selectedMode) == TheaterMode.DirectStream ? "Streaming direto do seu PC" : "Sincronização P2P";
        JoinModeNote.Text = selectedMode == TheaterMode.DirectStream ? "Entre pelo convite sem selecionar um vídeo. O modo definido pelo host aparece ao conectar; depois, clique em Abrir transmissão." : "Você pode entrar sem selecionar um vídeo e baixar a cópia autorizada pelo host durante a preparação.";
        if (media is null && active is null) MediaDetail.Text = selectedMode == TheaterMode.DirectStream ? "Só o host seleciona o vídeo. O convidado entra por convite e abre a transmissão, sem baixar o arquivo completo." : "O host seleciona o vídeo. O convidado usa sua própria cópia ou baixa na preparação, se o host autorizar.";
        CreateModeNote.Text = selectedMode == TheaterMode.DirectStream ? "O host mantém o vídeo original aberto e transmite os trechos solicitados durante a reprodução. Sem conversão; o upload precisa acompanhar a qualidade do arquivo." : "Ambos usam o mesmo arquivo completo. O envio abaixo é opcional e acontece antes de assistir.";
        ShareConsentText.Text = selectedMode == TheaterMode.DirectStream ? "Autorizo transmitir os trechos deste episódio ao participante que receber o convite." : "Autorizo enviar uma cópia completa deste episódio ao participante que receber o convite.";
        var hostSetup = modeChosen && hostRole == true && active is null;
        var guestSetup = modeChosen && hostRole == false && active is null;
        HostSetupPanel.Visibility = hostSetup ? Visibility.Visible : Visibility.Collapsed;
        GuestSetupPanel.Visibility = guestSetup ? Visibility.Visible : Visibility.Collapsed;
        UseLocalCopyCheck.Visibility = guestSetup && selectedMode == TheaterMode.PeerSync ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = hostSetup || guestSetup && selectedMode == TheaterMode.PeerSync && UseLocalCopyCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MediaSectionTitle.Text = hostSetup ? "1. Escolha o episódio" : "Selecione sua cópia do episódio";
        SelectFileButton.IsEnabled = !busy && active is null;
        CreateRoomButton.IsEnabled = !busy && media is not null && hostSetup;
        JoinRoomButton.IsEnabled = !busy && guestSetup && !string.IsNullOrWhiteSpace(JoinInvite.Text) && (selectedMode != TheaterMode.PeerSync || UseLocalCopyCheck.IsChecked != true || media is not null);
        ShareEpisodeCheck.IsEnabled = !busy && active is null;
        ConnectionPanel.Visibility = active is null && hostRole.HasValue ? Visibility.Visible : Visibility.Collapsed;
        SessionPanel.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        CancelOperationButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        HostAddress.IsEnabled = HostPort.IsEnabled = JoinInvite.IsEnabled = UseLocalCopyCheck.IsEnabled = !busy;
        P2pPreparationPanel.Visibility = active?.Mode == TheaterMode.PeerSync ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPresenceButton.Visibility = Visibility.Collapsed;
        PreparePlayerButton.Visibility = active is null ? Visibility.Collapsed : Visibility.Visible;
        PlayButton.Visibility = active?.IsHost == true ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        PlayButton.Content = "_Iniciar juntos";
        DownloadButton.Content = "_Baixar episódio";
        if (active is null) return;
        TransferGuestHint.Visibility = active.IsHost ? Visibility.Collapsed : Visibility.Visible;
        TransferPanel.Visibility = active.SharingAvailable && active.Mode == TheaterMode.PeerSync ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.Visibility = active.Mode == TheaterMode.PeerSync && !active.IsHost && !active.HasMedia && active.SharingAvailable && active.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = !busy && active.IsConnected && active.SharingAvailable;
        TransferProgress.Value = active.Episode.Length > 0 ? Math.Clamp(100d * active.TransferredBytes / active.Episode.Length, 0, 100) : 0;
        var progress = $"{active.TransferredBytes / 1048576d:0.0} / {active.Episode.Length / 1048576d:0.0} MB";
        TransferLabel.Text = active.IsHost
            ? active.RemoteHasMedia ? "Participante: arquivo OK · cópia completa verificada" : active.TransferredBytes == active.Episode.Length ? "Envio concluído · aguardando verificação do participante…" : active.TransferredBytes > 0 ? $"Enviando episódio · {progress}" : $"Compartilhamento autorizado · {active.OfferedName} · {active.Episode.Length / 1048576d:0.0} MB"
            : active.HasMedia ? "Arquivo OK · cópia completa verificada" : active.TransferredBytes == active.Episode.Length ? "Download recebido · verificando o arquivo completo…" : active.IsDownloading ? $"Baixando episódio · {progress}" : $"Disponível para baixar · {active.OfferedName} · {active.Episode.Length / 1048576d:0.0} MB";
        RoomTitle.Text = active.IsHost ? "Sua sala · host" : "Sala do host · participante";
        HostInvitePanel.Visibility = active.IsHost ? Visibility.Visible : Visibility.Collapsed;
        ConnectionLabel.Text = active.IsConnected ? active.IsHost ? active.RemoteHasMedia ? "Participante conectado · arquivo OK" : "Participante conectado · aguardando download e verificação" : active.Clock.IsReady ? $"Conexão direta · ida e volta estimada {active.Clock.RoundTripMs:0} ms" : "Medindo conexão…" : active.IsHost ? "Aguardando participante" : "Desconectado · saia para entrar novamente";
        if (active.Mode == TheaterMode.DirectStream) ConnectionLabel.Text = !active.IsConnected ? active.IsHost ? "Aguardando participante para streaming" : "Streaming desconectado · saia para entrar novamente" : $"Streaming direto · {active.StreamBytesReceived / 1048576d:0.0} MB transferidos sob demanda";
        LocalReadyLabel.Text = active.LocalReady ? "Você: vídeo aberto" : !active.IsHost && active.Mode == TheaterMode.PeerSync && !active.HasMedia ? "Você: falta baixar o episódio" : "Você: falta abrir o vídeo";
        var remoteName = active.IsHost ? "Convidado" : "Host";
        RemoteReadyLabel.Text = !active.IsConnected ? $"{remoteName}: aguardando conexão" : active.RemoteReady ? $"{remoteName}: vídeo aberto" : active.Mode == TheaterMode.PeerSync && active.IsHost && !active.RemoteHasMedia ? "Convidado: aguardando arquivo OK" : $"{remoteName}: falta abrir o vídeo";
        PreparePlayerButton.IsEnabled = !busy && (active.HasMedia && selectedPath is not null || !active.IsHost && active.Mode == TheaterMode.DirectStream) && !active.LocalReady && (active.IsHost || active.IsConnected && active.Clock.IsReady);
        PreparePlayerButton.Content = active.LocalReady ? "Vídeo aberto" : prepared ? "_Estou pronto" : !active.IsHost && active.Mode == TheaterMode.DirectStream ? "_Abrir transmissão" : !active.IsHost && UseLocalCopyCheck.IsChecked != true ? "_Abrir episódio baixado" : "_Abrir episódio";
        ReadyActionHint.Text = active.LocalReady
            ? active.IsHost ? "Vídeo aberto. Você controla a reprodução da sala." : "Vídeo aberto. Aguarde os comandos do host."
            : !active.IsHost && active.Mode == TheaterMode.PeerSync && !active.HasMedia ? "Primeiro baixe o episódio e aguarde a verificação. Depois, abra o vídeo para entrar na sessão."
            : active.IsHost ? "Abre o episódio no MPC-HC, pausado. Quando o convidado também estiver pronto, use Iniciar juntos."
            : active.Mode == TheaterMode.DirectStream ? "Recebe os primeiros trechos e abre sua tela de vídeo, pausada. Depois, aguarde o host iniciar."
            : "Abre sua cópia do episódio, pausada. Depois, aguarde o host iniciar.";
        LeaveRoomButton.IsEnabled = !busy;
        CopyInviteButton.IsEnabled = !busy;
        PlayButton.IsEnabled = PauseButton.IsEnabled = SeekButton.IsEnabled = !busy && active.IsHost && active.BothReady && prepared;
        UpdatePlaybackActions();
        PreparePlayerButton.Visibility = !active.LocalReady && (active.IsHost || active.IsConnected && active.Clock.IsReady) ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.Visibility = active.IsHost && active.BothReady && prepared && scheduledStart == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateP2pPreparationView();
        UpdateStreamBufferView();
        RoleNote.Text = active.IsHost ? "Você controla a sala e o MPC-HC. Use áudio e legendas embutidos; as faixas selecionadas serão seguidas pelo participante. Para trocar de episódio, crie outra sala." : "O host controla a reprodução. Sua janela exibe apenas o vídeo, sem barras, menus ou atalhos de reprodução. F11 alterna tela cheia.";
    }

    private void UpdatePlaybackActions()
    {
        var active = room;
        var canControl = active is { IsHost: true, BothReady: true, PlaybackAuthorized: true } && prepared && scheduledStart == 0;
        SeekPanel.Visibility = PlaybackButtonsPanel.Visibility = canControl ? Visibility.Visible : Visibility.Collapsed;
        if (active?.Mode == TheaterMode.DirectStream)
            PlayButton.Visibility = active.IsHost && active.BothReady && prepared && scheduledStart == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void Return_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is { } main && !ReferenceEquals(main, this))
        {
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }
        Close();
    }

    private static string Format(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");

    private void CloseViewer()
    {
        ClosePreparationPopup();
        if (voicePopup is { } floatingVoice && IsLoaded) new System.Windows.Interop.WindowInteropHelper(floatingVoice).Owner = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        playerWindowHandle = IntPtr.Zero;
        closingViewer = true;
        try { viewer?.Close(); viewer = null; }
        finally { closingViewer = false; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CloseViewer();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true; CloseFloatingWindows(); timer.Stop(); lifetime.Cancel();
        voiceDevice?.Dispose(); voiceDevice = null;
        if (room is not null) { room.Changed -= RoomChanged; room.Notice -= RoomNotice; _ = room.DisposeAsync(); }
        CloseViewer();
        try { player?.Dispose(); } catch (Exception) { }
        player = null;
        base.OnClosed(e);
    }
}
