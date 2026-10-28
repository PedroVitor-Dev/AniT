using System.IO;
using System.Runtime.InteropServices;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Net.Sockets;
using AniT.App;
using AniT.Core;
using AniT.Player;
using AniT.Theater;
using AniT.Testing;

internal static class TheaterPlayerChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCopyData { public IntPtr Command; public int Length; public IntPtr Payload; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr sender, ref NativeCopyData data, uint flags, uint timeout, out IntPtr result);

    public static int Run(Application app)
    {
        var result = 1;
        var window = new Window { Width = 100, Height = 100, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Loaded += async (_, _) =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "anit-theater-player-" + Guid.NewGuid().ToString("N"));
            MpcHcPlayer? first = null, second = null;
            TheaterRoom? host = null, guest = null;
            MovieTheaterWindow? hostWindow = null, guestWindow = null;
            TheaterTestRelay? relay = null;
            try
            {
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "synthetic-silence.wav"); WriteWave(path);
                var exe = Path.Combine(AppContext.BaseDirectory, "Player", "MPC-HC", "mpc-hc64.exe");
                first = new MpcHcPlayer(exe, true); second = new MpcHcPlayer(exe, true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var token = timeout.Token;
                IntPtr firstHandle = IntPtr.Zero;
                await Task.WhenAll(first.PrepareLocalFileAsync(path, token, handle => firstHandle = handle), second.PrepareLocalFileAsync(path, token));
                Require(await first.GetStateAsync(token) == PlayerState.Paused && await second.GetStateAsync(token) == PlayerState.Paused, "Two dedicated players start paused");
                var concurrentPositions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => first.GetPositionAsync(token)));
                Require(concurrentPositions.All(value => value.TotalSeconds < .1), "Concurrent MPC position callers do not cancel or overwrite one another");
                first.SetSpeed(1); first.SetSpeed(1.02); first.SetSpeed(1);
                var pausedPosition = await first.GetPositionAsync(token); await Task.Delay(500, token);
                Require(await first.GetStateAsync(token) == PlayerState.Paused && Math.Abs(((await first.GetPositionAsync(token)) - pausedPosition).TotalSeconds) < .1, "Setting playback speed while paused must not start MPC-HC");
                first.SetPlaybackPermission(false); first.Resume();
                // Bypass the wrapper to reproduce native/late graph playback notifications.
                var nativePlay = new NativeCopyData { Command = new IntPtr(0xA0000004L) };
                SendMessageTimeout(firstHandle, 0x004A, IntPtr.Zero, ref nativePlay, 2, 1500, out _);
                await Task.Delay(500, token);
                Require(await first.GetStateAsync(token) == PlayerState.Paused, "Playback permission blocks wrapper and native resume outside synchronization timer");
                first.SetPlaybackPermission(true);
                var identity = await MediaIdentity.FromFileAsync(path, token);
                host = TheaterRoom.Host(identity, IPAddress.Loopback); guest = await TheaterRoom.JoinAsync(host.Invite!.Encode(), identity, token);
                while (!guest.Clock.IsReady) await Task.Delay(20, token);
                await host.BeginPreparationAsync(token); await Until(() => guest.PreparationStarted, token);
                await host.SetReadyAsync(true, token); await guest.SetReadyAsync(true, token);
                while (!host.BothReady) await Task.Delay(20, token);
                await guest.ConfirmParticipationAsync(token); await Until(() => host.CanApprovePlayback, token); await host.ApprovePlaybackAsync(0, token);
                first.Resume(); await Task.Delay(400, token);
                var source = await first.GetPositionAsync(token); Require(source.TotalSeconds > .1, "Host actually advances");
                await host.PublishAsync(source.TotalSeconds, true, token);
                await Until(() => guest.LatestState?.PositionSeconds == source.TotalSeconds, token);
                var correction = TimelineSync.Correct(guest.LatestState!, 0, ClockEstimator.NowMs + guest.Clock.OffsetMs);
                await second.SeekAsync(TimeSpan.FromSeconds(correction.TargetSeconds), token); second.Resume();
                await Task.Delay(400, token);
                var hostPosition = await first.GetPositionAsync(token); var guestPosition = await second.GetPositionAsync(token);
                var difference = Math.Abs((hostPosition - guestPosition).TotalMilliseconds);
                Require(difference < 600, $"Local playback convergence: {difference:0}ms");
                await first.PauseAsync(token); await first.SeekAsync(TimeSpan.FromSeconds(5), token);
                await host.PublishAsync(5, false, token);
                while (guest.LatestState?.PositionSeconds != 5) await Task.Delay(10, token);
                await second.PauseAsync(token); await second.SeekAsync(TimeSpan.FromSeconds(guest.LatestState!.PositionSeconds), token);
                Require(Math.Abs((await second.GetPositionAsync(token)).TotalSeconds - 5) < .25, "Guest applies paused seek");
                second.SetSpeed(1.02); second.SetSpeed(1);
                Console.WriteLine($"PASS: two real MPC-HC instances; synthetic silent media, prepare/pause/play/seek/rate, TLS room and loopback convergence {difference:0}ms (single-PC diagnostic, not Internet/DPI validation).");
                await guest.DisposeAsync(); await host.DisposeAsync(); guest = null; host = null;
                second.Dispose(); first.Dispose(); second = null; first = null;
                foreach (var mode in new[] { TheaterMode.PeerSync, TheaterMode.DirectStream })
                {
                    hostWindow = new MovieTheaterWindow { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                    guestWindow = new MovieTheaterWindow { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                    hostWindow.Show(); guestWindow.Show(); hostWindow.SelectMode(mode); guestWindow.SelectMode(mode); hostWindow.SelectRole(true); guestWindow.SelectRole(false);
                    var videoPath = Path.Combine(folder, "synthetic-video.avi"); SyntheticTheaterMedia.WriteAvi(videoPath, mode == TheaterMode.DirectStream ? 600 : 120);
                    await hostWindow.SelectMediaAsync(videoPath);
                    Find<CheckBox>(hostWindow, "ShareEpisodeCheck").IsChecked = true;
                    using (var available = new TcpListener(IPAddress.Loopback, 0))
                    {
                        available.Start(); Find<TextBox>(hostWindow, "HostPort").Text = ((IPEndPoint)available.LocalEndpoint).Port.ToString(); available.Stop();
                    }
                    Find<TextBox>(hostWindow, "HostAddress").Text = "127.0.0.1";
                    Click(hostWindow, "CreateRoomButton"); await Until(() => hostWindow.ActiveRoom is not null, token);
                    var invitation = hostWindow.ActiveRoom!.Invite!;
                    if (mode == TheaterMode.DirectStream) { relay = new TheaterTestRelay(invitation.Port); relay.ConfigureDelays(15, 0, 3, mediaVariationMilliseconds: 0); invitation = invitation with { Port = relay.Port }; }
                    Find<TextBox>(guestWindow, "JoinInvite").Text = invitation.Encode();
                    Click(guestWindow, "JoinRoomButton"); await Until(() => guestWindow.ActiveRoom?.Clock.IsReady == true, token);
                    if (mode == TheaterMode.PeerSync)
                    {
                        Require(guestWindow.ActiveRoom is { HasMedia: false, SharingAvailable: true }, "Guest enters preparation without owning the episode");
                        Require(Find<Button>(guestWindow, "ConfirmPresenceButton").Visibility == Visibility.Collapsed && Find<Button>(guestWindow, "PreparePlayerButton").Visibility == Visibility.Collapsed, "Guest sees no confirmation or manual opening before preparation");
                        Require(!Find<Button>(guestWindow, "PreparePlayerButton").IsEnabled && !Find<Button>(hostWindow, "PlayButton").IsEnabled, "Download gates player and shared start");
                        await guestWindow.ScheduleP2pDownloadAsync(Path.Combine(folder, "guest-downloaded.avi"));
                        Require(!guestWindow.ActiveRoom!.HasMedia && !guestWindow.ActiveRoom.PreparationStarted, "Guest queues destination but cannot download before host release");
                        Click(hostWindow, "PreparePlayerButton");
                        await Until(() => hostWindow.ActiveRoom?.BothReady == true, token);
                        Require(guestWindow.ActiveRoom.HasMedia && File.Exists(Path.Combine(folder, "guest-downloaded.avi")), "Host release automatically downloads, verifies and opens guest video");
                        Require(!Find<Button>(hostWindow, "PlayButton").IsEnabled && !hostWindow.ActiveRoom!.PlaybackAuthorized, "Verified file and open videos still require guest confirmation and host approval");
                        hostWindow.ActivePlayer!.Resume(); await Task.Delay(700, token);
                        Require(await hostWindow.ActivePlayer.GetStateAsync(token) == PlayerState.Paused && guestWindow.ActiveRoom.LatestState is null, "Native host play cannot bypass final approval");
                        guestWindow.ActivePlayer!.Resume();
                        await AssertPausedTogether(hostWindow, guestWindow, token);
                        Require(Find<Button>(guestWindow, "ConfirmPresenceButton").Visibility == Visibility.Visible && Find<Button>(guestWindow, "PreparePlayerButton").Visibility == Visibility.Collapsed, "Prepared guest sees confirmation without redundant opening action");
                        await Until(() => guestWindow.ActivePreparationPopup is { IsVisible: true } && hostWindow.ActivePreparationPopup is { IsVisible: true }, token);
                        Require(new System.Windows.Interop.WindowInteropHelper(guestWindow.ActivePreparationPopup!).Owner == guestWindow.FloatingPlayerOwner, "Guest confirmation belongs to the native video viewer");
                        Require(new System.Windows.Interop.WindowInteropHelper(hostWindow.ActivePreparationPopup!).Owner == hostWindow.FloatingPlayerOwner, "Host approval belongs to the native MPC window");
                        Require(!guestWindow.ActivePreparationPopup!.Topmost && !hostWindow.ActivePreparationPopup!.Topmost, "Preparation is above its own video, not globally topmost");
                        Click(guestWindow.ActivePreparationPopup!, "ConfirmButton");
                        await Until(() => hostWindow.ActiveRoom!.CanApprovePlayback, token);
                        await AssertPausedTogether(hostWindow, guestWindow, token);
                    }
                    if (mode == TheaterMode.DirectStream) { Click(hostWindow, "PreparePlayerButton"); Click(guestWindow, "PreparePlayerButton"); }
                    while (!Find<Button>(hostWindow, "PlayButton").IsEnabled)
                    {
                        if (hostWindow.ActiveRoom?.IsConnected != true || guestWindow.ActiveRoom?.IsConnected != true)
                            throw new InvalidOperationException("Preparation lost the room: " + Find<TextBlock>(guestWindow, "TheaterStatus").Text);
                        if (mode == TheaterMode.DirectStream && Find<Button>(guestWindow, "PreparePlayerButton").IsEnabled) throw new InvalidOperationException("Guest preparation failed: " + Find<TextBlock>(guestWindow, "TheaterStatus").Text);
                        await Task.Delay(20, token);
                    }
                    if (mode == TheaterMode.DirectStream)
                    {
                        Require(!guestWindow.ActiveRoom!.HasMedia, "Streaming has no complete local episode");
                        Require(guestWindow.ActiveRoom.StreamBytesReceived < new FileInfo(videoPath).Length, $"Streaming starts before full episode transfer ({guestWindow.ActiveRoom.StreamBytesReceived} bytes of {new FileInfo(videoPath).Length})");
                        Console.WriteLine($"PASS: streaming graph prepared with {guestWindow.ActiveRoom.StreamBytesReceived} of {new FileInfo(videoPath).Length} original bytes; no guest episode file.");
                    }
                    Require(Find<FrameworkElement>(hostWindow, "HostManagerPanel").Visibility == Visibility.Visible && Find<FrameworkElement>(guestWindow, "HostManagerPanel").Visibility == Visibility.Collapsed, "Only the host sees participant management");
                    var participantId = hostWindow.ActiveRoom!.Participants.Single(p => !p.IsHost).Id;
                    await Until(() => FindParticipantAction(hostWindow, participantId, "Bloquear microfone") is not null, token);
                    FindParticipantAction(hostWindow, participantId, "Bloquear microfone")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => guestWindow.ActiveRoom!.MicrophoneBlocked, token);
                    await Until(() => FindParticipantAction(hostWindow, participantId, "Liberar microfone") is { IsEnabled: true }, token);
                    FindParticipantAction(hostWindow, participantId, "Liberar microfone")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => !guestWindow.ActiveRoom!.MicrophoneBlocked && Find<Button>(hostWindow, "PlayButton").IsEnabled, token);
                    await guestWindow.ActiveRoom!.StartVoiceAsync(token); // Transport only: no microphone/device capture in this test.
                    guestWindow.ActiveVoicePopup!.Close();
                    await Until(() => !guestWindow.ActiveRoom!.VoiceEnabled && guestWindow.ActiveVoicePopup is null, token);
                    Require(guestWindow.ActivePlayer is not null && guestWindow.ActiveViewer is not null, "Closing voice leaves video open");
                    Click(guestWindow, "OpenVoicePopupButton"); await Until(() => guestWindow.ActiveVoicePopup is { IsVisible: true }, token);
                    Require(guestWindow.ActiveViewer is { IsLockedSurface: true }, "Guest video surface disables native input and menus");
                    Require(((FrameworkElement)guestWindow.FindName("PlaybackButtonsPanel")).Visibility == Visibility.Collapsed, "Guest has no play/pause buttons");
                    guestWindow.ActiveViewer!.ToggleFullscreen(); guestWindow.ActiveViewer.ToggleFullscreen();
                    Require(guestWindow.ActiveViewer.IsLockedSurface, "Fullscreen preserves viewer input lock");
                    Require(hostWindow.ActiveVoicePopup is { IsVisible: true } && guestWindow.ActiveVoicePopup is { IsVisible: true }, "Both roles have independent voice controls over the video");
                    Require(new System.Windows.Interop.WindowInteropHelper(guestWindow.ActiveVoicePopup!).Owner == guestWindow.FloatingPlayerOwner, "Voice remains owned by the video after fullscreen changes");
                    if (mode == TheaterMode.PeerSync) Click(hostWindow.ActivePreparationPopup!, "ConfirmButton"); else Click(hostWindow, "PlayButton");
                    await Until(() => guestWindow.ActiveRoom?.LatestState is { Playing: true }, token);
                    Require(guestWindow.ActiveRoom!.LatestState!.HostTimeMs > ClockEstimator.NowMs + (mode == TheaterMode.PeerSync ? 3500 : 0), "UI schedules shared countdown, five seconds after P2P host approval");
                    if (mode == TheaterMode.PeerSync) await AssertPausedTogether(hostWindow, guestWindow, token);
                    await Until(() => !Find<TextBlock>(hostWindow, "PositionLabel").Text.StartsWith("00:00 /", StringComparison.Ordinal) && !Find<TextBlock>(guestWindow, "PositionLabel").Text.StartsWith("00:00 /", StringComparison.Ordinal), token);
                    if (mode == TheaterMode.PeerSync) await Until(() => hostWindow.ActivePreparationPopup is null && guestWindow.ActivePreparationPopup is null, token);
                    Require(Find<FrameworkElement>(hostWindow, "SeekPanel").Visibility == Visibility.Visible, "Host receives seek controls after shared playback starts");
                    var positions = await Task.WhenAll(hostWindow.ActivePlayer!.GetPositionAsync(token), guestWindow.ActivePlayer!.GetPositionAsync(token));
                    var gapMs = Math.Abs((positions[0] - positions[1]).TotalMilliseconds);
                    Require(gapMs < 750, $"{mode} real-player loopback alignment stays below 750 ms ({gapMs:0} ms)");
                    Console.WriteLine($"PASS: {mode} real-player loopback timeline difference {gapMs:0} ms (single PC, synthetic video).");
                    if (mode == TheaterMode.DirectStream)
                    {
                        relay!.PauseMedia();
                        await Until(() => hostWindow.ActiveRoom!.StreamHold && guestWindow.ActiveRoom!.StreamHold, token);
                        await Until(() => guestWindow.ActiveRoom!.LatestState is { BufferHold: true, Playing: false }, token);
                        Require(Find<FrameworkElement>(hostWindow, "StreamBufferPanel").Visibility == Visibility.Visible && Find<FrameworkElement>(guestWindow, "StreamBufferPanel").Visibility == Visibility.Visible, "Buffer wait is visible on both real windows");
                        Require(Find<Button>(hostWindow, "PlayButton").Visibility == Visibility.Collapsed, "Resume stays hidden while the guest is buffering");
                        await AssertPausedTogether(hostWindow, guestWindow, token);
                        hostWindow.ActivePlayer!.Resume(); await Task.Delay(250, token);
                        Require(await hostWindow.ActivePlayer.GetStateAsync(token) == PlayerState.Paused, "Native play cannot bypass buffer hold");
                        relay.ResumeMedia();
                        await Until(() => !hostWindow.ActiveRoom!.GuestBuffering && !guestWindow.ActiveRoom!.GuestBuffering, token);
                        await Until(() => Find<Button>(hostWindow, "PlayButton").IsEnabled, token);
                        await AssertPausedTogether(hostWindow, guestWindow, token);
                        Require(hostWindow.ActiveRoom!.StreamHold && guestWindow.ActiveRoom!.StreamHold, "Recovered data does not autoplay either real player");
                        Click(hostWindow, "PlayButton");
                        await Until(() => !guestWindow.ActiveRoom!.StreamHold && guestWindow.ActivePlayer!.GetStateAsync(token).Result == PlayerState.Playing && hostWindow.ActivePlayer!.GetStateAsync(token).Result == PlayerState.Playing, token);
                        Console.WriteLine("PASS: real streaming players under delayed/jittered control and interrupted media pause together on blocked media, retain the room, recover data without autoplay and resume only after host approval.");
                    }
                    // First reproduce the user's native MPC pause, leaving room timers active.
                    await hostWindow.ActivePlayer!.PauseAsync(token);
                    await Until(() => guestWindow.ActiveRoom?.LatestState is { Playing: false }, token);
                    await AssertPausedTogether(hostWindow, guestWindow, token);
                    hostWindow.ActivePlayer.Resume();
                    await Until(() => guestWindow.ActiveRoom?.LatestState is { Playing: true }, token);
                    await Until(() => guestWindow.ActivePlayer!.GetStateAsync(token).Result == PlayerState.Playing, token);
                    Click(hostWindow, "PauseButton"); await Until(() => guestWindow.ActiveRoom?.LatestState is { Playing: false }, token);
                    await AssertPausedTogether(hostWindow, guestWindow, token);
                    Find<TextBox>(hostWindow, "SeekPosition").Text = "00:00:05"; Click(hostWindow, "SeekButton");
                    await Until(() => Find<TextBlock>(guestWindow, "PositionLabel").Text.StartsWith("00:05 /", StringComparison.Ordinal), token);
                    hostWindow.ActivePlayer.Resume();
                    await Until(() => guestWindow.ActivePlayer!.GetStateAsync(token).Result == PlayerState.Playing, token);
                    if (mode == TheaterMode.PeerSync)
                    {
                        var previousInvite = hostWindow.ActiveRoom!.Invite!.Encode();
                        await Until(() => FindParticipantAction(hostWindow, participantId, "Expulsar") is { IsEnabled: true }, token);
                        FindParticipantAction(hostWindow, participantId, "Expulsar")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Until(() => guestWindow.ActiveRoom is null && guestWindow.ActivePlayer is null && guestWindow.ActiveViewer is null, token);
                        Require(guestWindow.ActivePreparationPopup is null && guestWindow.ActiveVoicePopup is null, "Removal closes all floating windows");
                        Require(guestWindow.IsVisible && Find<TextBlock>(guestWindow, "TheaterStatus").Text.Contains("removido", StringComparison.OrdinalIgnoreCase), "Removed guest sees why the room closed and can request another invitation");
                        Require(File.Exists(Path.Combine(folder, "guest-downloaded.avi")), "Removal keeps the completed guest download");
                        Require(previousInvite != hostWindow.ActiveRoom!.Invite!.Encode(), "Host manager rotates the invitation after removal");
                        Find<CheckBox>(guestWindow, "UseLocalCopyCheck").IsChecked = true;
                        Find<TextBox>(guestWindow, "JoinInvite").Text = hostWindow.ActiveRoom.Invite.Encode();
                        Click(guestWindow, "JoinRoomButton");
                        await Until(() => guestWindow.ActiveRoom?.IsConnected == true, token);
                        Console.WriteLine("PASS: host manager changes microphone permissions, removes the real guest viewer, keeps the downloaded episode and accepts a fresh invitation.");
                    }
                    var hostVoicePopup = hostWindow.ActiveVoicePopup; var guestVoicePopup = guestWindow.ActiveVoicePopup;
                    hostWindow.Close();
                    await Until(() => !guestWindow.IsVisible && guestWindow.ActivePlayer is null && guestWindow.ActiveViewer is null, token);
                    if (mode == TheaterMode.PeerSync) Require(File.Exists(Path.Combine(folder, "guest-downloaded.avi")) && (await MediaIdentity.FromFileAsync(Path.Combine(folder, "guest-downloaded.avi"), token)) == (await MediaIdentity.FromFileAsync(videoPath, token)), "Closing host preserves the verified guest episode");
                    Require(hostWindow.ActiveVoicePopup is null && guestWindow.ActiveVoicePopup is null && hostVoicePopup?.IsVisible != true && guestVoicePopup?.IsVisible != true, "Room exit closes voice panels");
                    hostWindow = guestWindow = null;
                    if (relay is not null) { await relay.DisposeAsync(); relay = null; }
                    Console.WriteLine($"PASS: actual Movie Theater {mode} windows; preparation, host controls, countdown, timers, pause/seek, locked viewer and disconnect with synthetic video.");
                }
                result = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); if (hostWindow is not null) Console.Error.WriteLine("Host UI: " + Find<TextBlock>(hostWindow, "TheaterStatus").Text + " / " + Find<TextBlock>(hostWindow, "SyncLabel").Text); if (guestWindow is not null) Console.Error.WriteLine("Guest UI: " + Find<TextBlock>(guestWindow, "TheaterStatus").Text + " / " + Find<TextBlock>(guestWindow, "SyncLabel").Text); }
            finally
            {
                if (hostWindow?.IsVisible == true) hostWindow.Close(); if (guestWindow?.IsVisible == true) guestWindow.Close();
                if (guest is not null) await guest.DisposeAsync(); if (host is not null) await host.DisposeAsync();
                if (relay is not null) await relay.DisposeAsync();
                second?.Dispose(); first?.Dispose();
                try { if (Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, true); } catch (IOException) { }
                app.Shutdown();
            }
        };
        app.Run(window); return result;
    }
    public static int Preview(Application app)
    {
        var folder = Path.Combine(Path.GetTempPath(), "anit-theater-preview-" + Guid.NewGuid().ToString("N"));
        var owner = new Window { Title = "Prévia · Movie Theater", Width = 580, Height = 420, ShowInTaskbar = true };
        MpcHcPlayer? player = null;
        owner.Loaded += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(folder); var path = Path.Combine(folder, "synthetic-video.avi"); SyntheticTheaterMedia.WriteAvi(path);
                player = new MpcHcPlayer(Path.Combine(AppContext.BaseDirectory, "Player", "MPC-HC", "mpc-hc64.exe"), true, true);
                await player.PrepareLocalFileAsync(path, default, handle =>
                {
                    var view = new MovieTheaterViewerWindow(handle, new System.Windows.Interop.WindowInteropHelper(owner).Handle);
                    view.Closed += async (_, _) =>
                    {
                        player?.Dispose(); await Task.Delay(500);
                        try { if (Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(folder, true); } catch (IOException) { }
                        app.Shutdown();
                    };
                    view.ShowPlayer();
                });
                player.Resume();
            }
            catch (Exception error) { Console.Error.WriteLine(error); player?.Dispose(); app.Shutdown(); }
        };
        app.Run(owner); return 0;
    }

    private static async Task AssertPausedTogether(MovieTheaterWindow host, MovieTheaterWindow guest, CancellationToken token)
    {
        await Until(() => guest.ActivePlayer is null || guest.ActivePlayer.GetStateAsync(token).Result == PlayerState.Paused, token);
        Require(host.ActivePlayer is not null && guest.ActivePlayer is not null, $"Both players stay available. Host: {host.LastPlaybackError}; guest: {guest.LastPlaybackError}");
        var hostPosition = await host.ActivePlayer!.GetPositionAsync(token);
        var guestPosition = await guest.ActivePlayer!.GetPositionAsync(token);
        // Several room ticks must not undo native or room-button pauses, on either PC.
        await Task.Delay(1000, token);
        Require(host.ActivePlayer is not null && guest.ActivePlayer is not null, $"Players stay available during pause. Host: {host.LastPlaybackError}; guest: {guest.LastPlaybackError}");
        Require(await host.ActivePlayer!.GetStateAsync(token) == PlayerState.Paused && await guest.ActivePlayer!.GetStateAsync(token) == PlayerState.Paused, "Host and guest remain paused across room timers");
        Require(Math.Abs(((await host.ActivePlayer.GetPositionAsync(token)) - hostPosition).TotalSeconds) < .1 && Math.Abs(((await guest.ActivePlayer!.GetPositionAsync(token)) - guestPosition).TotalSeconds) < .1, "Paused host and guest positions do not advance");
    }

    private static Button? FindParticipantAction(MovieTheaterWindow window, Guid participant, string content)
    {
        window.UpdateLayout();
        Button? Search(DependencyObject root)
        {
            if (root is Button { Tag: Guid id } button && id == participant && Equals(button.Content, content)) return button;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                if (Search(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
        return Search(Find<ItemsControl>(window, "HostParticipantList"));
    }

    private static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Click(Window window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> predicate, CancellationToken token)
    {
        while (!predicate()) await Task.Delay(20, token);
    }
    private static void WriteWave(string path)
    {
        const int samples = 48000 * 12; const int bytes = samples * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(bytes + 36); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
