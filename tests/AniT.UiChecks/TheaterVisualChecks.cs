using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;
using AniT.Theater;
using AniT.Testing;
using System.Net.Http;
using System.Net.Http.Headers;

internal static class TheaterVisualChecks
{
    public static MovieTheaterWindow Preview(bool host = false)
    {
        if (!host) return new MovieTheaterWindow();
        var room = TheaterRoom.Host(new MediaIdentity(1024, new string('A', 64)), IPAddress.Loopback);
        return new MovieTheaterWindow(room);
    }

    public static void Run()
    {
        var count = 0;
        foreach (var scene in new[] { "empty", "sync-role", "stream-role", "sync-host-setup", "stream-host-setup", "sync-guest-setup", "stream-guest-setup", "sync-guest-file", "host", "guest", "host-share", "guest-download", "host-stream", "guest-stream", "host-released", "guest-confirmation", "host-approval", "guest-countdown", "host-stream-buffering", "guest-stream-buffering", "host-stream-recovered", "guest-stream-recovered", "host-five", "guest-five", "host-voice", "guest-voice" })
        {
            TheaterRoom? server = null, guest = null;
            var extraGuests = new List<TheaterRoom>();
            TheaterTestRelay? relay = null;
            HttpClient? http = null;
            HttpRequestMessage? range = null;
            Task<HttpResponseMessage>? pendingRead = null;
            var bufferScene = scene.EndsWith("-buffering", StringComparison.Ordinal) || scene.EndsWith("-recovered", StringComparison.Ordinal);
            var source = Path.Combine(Path.GetTempPath(), "anit-transfer-layout-" + Guid.NewGuid().ToString("N") + ".mkv");
            var isRole = scene.EndsWith("-role", StringComparison.Ordinal);
            var isSetup = scene.EndsWith("-setup", StringComparison.Ordinal) || scene == "sync-guest-file";
            var hasRoom = scene != "empty" && !isRole && !isSetup;
            var isHostSetup = isSetup && scene.Contains("-host-", StringComparison.Ordinal);
            var mode = scene.Contains("stream", StringComparison.Ordinal) ? TheaterMode.DirectStream : TheaterMode.PeerSync;
            var isHost = scene.StartsWith("host", StringComparison.Ordinal);
            try
            {
                var identity = new MediaIdentity(1024, new string('A', 64));
                var sharing = bufferScene || scene is "host-share" or "guest-download" or "host-stream" or "guest-stream";
                if (sharing) { File.WriteAllBytes(source, new byte[bufferScene ? 4 * 1024 * 1024 : 1024]); identity = MediaIdentity.FromFileAsync(source).GetAwaiter().GetResult(); }
                if (hasRoom) server = TheaterRoom.Host(identity, IPAddress.Loopback, sharedFilePath: sharing ? source : null, mode: mode);
                if (bufferScene)
                {
                    relay = new TheaterTestRelay(server!.Invite!.Port);
                    guest = TheaterRoom.JoinAsync((server.Invite with { Port = relay.Port }).Encode()).GetAwaiter().GetResult();
                    Wait(() => guest.Clock.IsReady);
                    var endpoint = guest.StartStreamingAsync().GetAwaiter().GetResult();
                    server.SetReadyAsync(true).GetAwaiter().GetResult(); guest.SetReadyAsync(true).GetAwaiter().GetResult(); Wait(() => server.BothReady);
                    relay.PauseMedia(); http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
                    range = new HttpRequestMessage(HttpMethod.Get, endpoint); range.Headers.Range = new RangeHeaderValue(1024 * 1024, 1024 * 1024 + 99);
                    pendingRead = http.SendAsync(range);
                    relay.MediaBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    Wait(() => { guest.ObserveStreamingPlaybackAsync(10, true).GetAwaiter().GetResult(); return guest.StreamHold && server.StreamHold; });
                    server.PublishAsync(10, false).GetAwaiter().GetResult();
                    if (scene.EndsWith("-recovered", StringComparison.Ordinal))
                    {
                        relay.ResumeMedia(); pendingRead.GetAwaiter().GetResult().Dispose(); pendingRead = null;
                        Wait(() => { guest.ObserveStreamingPlaybackAsync(10, false).GetAwaiter().GetResult(); return !guest.GuestBuffering && !server.GuestBuffering; });
                    }
                }
                if (!bufferScene && hasRoom && scene.StartsWith("guest", StringComparison.Ordinal)) guest = TheaterRoom.JoinAsync(server!.Invite!.Encode(), sharing ? null : identity).GetAwaiter().GetResult();
                if (scene is "host-released" or "guest-confirmation" or "host-approval" or "guest-countdown")
                {
                    server!.BeginPreparationAsync().GetAwaiter().GetResult();
                    guest ??= TheaterRoom.JoinAsync(server.Invite!.Encode(), identity).GetAwaiter().GetResult();
                    if (scene != "host-released")
                    {
                        Wait(() => guest.Clock.IsReady);
                        server.SetReadyAsync(true).GetAwaiter().GetResult(); guest.SetReadyAsync(true).GetAwaiter().GetResult(); Wait(() => server.BothReady);
                        if (scene is "host-approval" or "guest-countdown") { guest.ConfirmParticipationAsync().GetAwaiter().GetResult(); Wait(() => server.CanApprovePlayback); }
                        if (scene == "guest-countdown") { server.ApprovePlaybackAsync(0).GetAwaiter().GetResult(); Wait(() => guest.PlaybackAuthorized); }
                    }
                }
                if (scene.EndsWith("-five", StringComparison.Ordinal) || scene.EndsWith("-voice", StringComparison.Ordinal))
                {
                    while (server!.Participants.Count < 5)
                    {
                        var member = TheaterRoom.JoinAsync(server.Invite!.Encode(), identity, displayName: "Convidado com nome comprido " + new string('x', 30)).GetAwaiter().GetResult();
                        extraGuests.Add(member); Wait(() => server.Participants.Count == extraGuests.Count + (guest is null ? 1 : 2));
                    }
                    Wait(() => (guest ?? extraGuests[0]).Participants.Count == 5);
                    if (scene.EndsWith("-voice", StringComparison.Ordinal))
                    {
                        server.StartVoiceAsync().GetAwaiter().GetResult(); server.SetVoiceStateAsync(false, false).GetAwaiter().GetResult();
                        var talker = guest ?? extraGuests[0]; talker.StartVoiceAsync().GetAwaiter().GetResult(); talker.SetVoiceStateAsync(false, false).GetAwaiter().GetResult();
                        Wait(() => server.Participants.Count(p => p.VoiceConnected) == 2);
                        var samples = Enumerable.Range(0, TheaterRoom.VoiceFrameSamples).Select(i => (short)(10000 * Math.Sin(i * Math.PI / 30))).ToArray();
                        server.SubmitVoice(samples); talker.SubmitVoice(samples);
                    }
                }
                var window = hasRoom ? new MovieTheaterWindow(isHost ? server : guest) : new MovieTheaterWindow();
                if (isRole || isSetup) window.SelectMode(mode);
                if (isSetup) window.SelectRole(isHostSetup);
                if (scene == "sync-guest-file") ((CheckBox)window.FindName("UseLocalCopyCheck")).IsChecked = true;
                window.ShowActivated = false; window.ShowInTaskbar = false; window.Opacity = 0;
                window.Show(); Pump();
                foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light })
                    foreach (var scale in new[] { 100, 125 })
                        foreach (var text in new[] { 100, 140 })
                            foreach (var size in new[] { (580d, 420d), (1060d, 700d), (1280d, 800d), (1920d, 1080d) })
                            {
                                window.Width = size.Item1; window.Height = size.Item2;
                                AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode = theme, InterfaceScalePercent = scale, TextScalePercent = text, ReduceMotion = text == 140, ArtworkStretchMode = ArtworkStretchMode.Manual });
                                window.UpdateLayout(); Pump();
                                var scroll = (ScrollViewer)window.FindName("TheaterScroller");
                                Require(scroll.ViewportWidth > 250 && scroll.ViewportHeight > 20, $"Usable theater viewport {scene} {theme} {scale} {text} {size}: {scroll.ViewportWidth}x{scroll.ViewportHeight}");
                                Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"Theater horizontal overflow {scene} {theme} {scale} {text} {size}");
                                var root = (FrameworkElement)window.FindName("TheaterRoot");
                                var status = (FrameworkElement)window.FindName("StatusScroller");
                                var bounds = status.TransformToAncestor(root).TransformBounds(new Rect(status.RenderSize));
                                Require(bounds.Bottom <= root.ActualHeight + 1, "Status reachable outside scroller");
                                Require(!((Button)window.FindName("PlayButton")).IsEnabled, "No playback before real file/player preparation");
                                Require(((FrameworkElement)window.FindName("HostManagerPanel")).Visibility == (hasRoom && isHost ? Visibility.Visible : Visibility.Collapsed), "Host manager only appears for the host inside a room");
                                Require(((FrameworkElement)window.FindName("VoicePanel")).Visibility == (hasRoom ? Visibility.Visible : Visibility.Collapsed), "Voice requires a room");
                                Require(((Button)window.FindName("OpenVoicePopupButton")).IsVisible == hasRoom, "Dedicated voice window can be opened from the room");
                                Require(((FrameworkElement)window.FindName("SessionPanel")).Visibility == (!hasRoom ? Visibility.Collapsed : Visibility.Visible), "Session matches room state");
                                Require(((FrameworkElement)window.FindName("HostInvitePanel")).Visibility == (isHost ? Visibility.Visible : Visibility.Collapsed), "Only host shows shareable invite");
                                Require(((FrameworkElement)window.FindName("SeekPanel")).Visibility == Visibility.Collapsed || !hasRoom, "Seek stays hidden until a real player is ready and authorized");
                                Require(((FrameworkElement)window.FindName("TransferPanel")).Visibility == (sharing && mode == TheaterMode.PeerSync ? Visibility.Visible : Visibility.Collapsed), "Transfer only appears when host approved");
                                Require(scene != "guest-download" || ((Button)window.FindName("DownloadButton")).IsEnabled, "Guest without media can download in preparation");
                                Require(((FrameworkElement)window.FindName("ModePanel")).Visibility == (scene == "empty" ? Visibility.Visible : Visibility.Collapsed), "Mode choice comes first");
                                Require(((FrameworkElement)window.FindName("SetupPanel")).Visibility == (isSetup ? Visibility.Visible : Visibility.Collapsed), "Preparation follows mode choice");
                                Require(((FrameworkElement)window.FindName("RolePanel")).Visibility == (isRole ? Visibility.Visible : Visibility.Collapsed), "Role choice follows mode and precedes configuration");
                                Require(((FrameworkElement)window.FindName("HostSetupPanel")).Visibility == (isHostSetup ? Visibility.Visible : Visibility.Collapsed), "Host fields only shown in host setup");
                                Require(((FrameworkElement)window.FindName("GuestSetupPanel")).Visibility == (isSetup && !isHostSetup ? Visibility.Visible : Visibility.Collapsed), "Guest has only join fields");
                                Require(((FrameworkElement)window.FindName("MediaPanel")).Visibility == (isHostSetup || scene == "sync-guest-file" ? Visibility.Visible : Visibility.Collapsed), "Guests only select a local file by opting in on P2P");
                                Require(((FrameworkElement)window.FindName("UseLocalCopyCheck")).Visibility == (isSetup && !isHostSetup && mode == TheaterMode.PeerSync ? Visibility.Visible : Visibility.Collapsed), "Local copy option only applies to P2P guests");
                                Require(((FrameworkElement)window.FindName("StreamBufferPanel")).Visibility == (bufferScene ? Visibility.Visible : Visibility.Collapsed), "Buffer guidance appears only during a streaming hold");
                                Require(((FrameworkElement)window.FindName("P2pPreparationPanel")).Visibility == (hasRoom && mode == TheaterMode.PeerSync ? Visibility.Visible : Visibility.Collapsed), "P2P shows guided locks and progress without changing streaming");
                                Require(!hasRoom || isHost || ((Button)window.FindName("PlayButton")).Visibility == Visibility.Collapsed, "Guest never receives host approval button");
                                var continueButton = (Button)window.FindName("ChooseModeButton");
                                Require(continueButton.Visibility == (scene == "empty" || isRole ? Visibility.Visible : Visibility.Collapsed), "Continue appears only during mode or role choice");
                                if (scene == "empty")
                                {
                                    var actionBounds = continueButton.TransformToAncestor(root).TransformBounds(new Rect(continueButton.RenderSize));
                                    Require(actionBounds.Bottom <= root.ActualHeight + 1, "Mode action always reachable outside scrolling");
                                    var first = (RadioButton)window.FindName("StreamingModeOption"); var second = (RadioButton)window.FindName("SyncModeOption");
                                    var firstBounds = first.TransformToAncestor(root).TransformBounds(new Rect(first.RenderSize)); var secondBounds = second.TransformToAncestor(root).TransformBounds(new Rect(second.RenderSize));
                                    Require(scroll.ViewportWidth < 740 ? secondBounds.Top >= firstBounds.Bottom - 1 : Math.Abs(firstBounds.Top - secondBounds.Top) < 1, $"Mode cards stack/alignment {scene} {theme} {scale}/{text} {size} viewport {scroll.ViewportWidth} first {firstBounds} second {secondBounds}");
                                }
                                var art = (Image)window.FindName("HeroArt");
                                Require(art.Source is not null && art.Stretch == System.Windows.Media.Stretch.Uniform, "Cinema art stays loaded and uncropped with manual cover focus");
                                Require(StableWindowRendering.GetEnabled(window), "Transparent cinema assets use stable window rendering");
                                var motion = (Duration)window.FindResource("AniT.Motion.Normal");
                                Require(text != 140 || motion.TimeSpan.TotalMilliseconds <= 3, "Cinema transitions respect reduced motion");
                                var button = (Button)window.FindName("SelectFileButton");
                                Require(button.MinHeight >= 44 && button.FocusVisualStyle is not null, "Native action hit target and focus");
                                scroll.ScrollToBottom(); window.UpdateLayout();
                                Require(scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 1, "End of theater content reachable");
                                if (Environment.GetEnvironmentVariable("ANIT_THEATER_CAPTURE_ROOT") is { Length: > 0 } captureRoot && scene.EndsWith("-voice", StringComparison.Ordinal) && scale == 100 && text == 100 && size.Item1 == 1060)
                                {
                                    scroll.ScrollToTop(); window.UpdateLayout();
                                    var talker = guest ?? extraGuests[0];
                                    var samples = Enumerable.Range(0, TheaterRoom.VoiceFrameSamples).Select(i => (short)(10000 * Math.Sin(i * Math.PI / 30))).ToArray();
                                    server!.SubmitVoice(samples); talker.SubmitVoice(samples);
                                    Pump(); System.Threading.Thread.Sleep(110); Pump();
                                    var focus = (FrameworkElement)window.FindName(isHost ? "HostManagerPanel" : "GuestVoicePolicyPanel");
                                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + focus.TranslatePoint(new Point(0, 0), scroll).Y); window.UpdateLayout(); Pump();
                                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                                    bitmap.Render((System.Windows.Media.Visual)window.Content);
                                    var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                                    Require(pixels.Any(p => p != 0), "Captured theater content must be visible");
                                    Directory.CreateDirectory(captureRoot);
                                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                                    using var output = File.Create(Path.Combine(captureRoot, scene + "-" + theme + ".png")); png.Save(output);
                                }
                                scroll.ScrollToTop(); count++;
                            }
                window.Close(); Pump();
            }
            finally
            {
                relay?.ResumeMedia();
                if (pendingRead is not null) { try { pendingRead.GetAwaiter().GetResult().Dispose(); } catch (Exception) { } }
                range?.Dispose(); http?.Dispose(); foreach (var member in extraGuests) member.DisposeAsync().AsTask().GetAwaiter().GetResult(); guest?.DisposeAsync().AsTask().GetAwaiter().GetResult(); server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                relay?.DisposeAsync().AsTask().GetAwaiter().GetResult(); if (File.Exists(source)) File.Delete(source);
            }
        }
        var flow = new MovieTheaterWindow { Opacity = 0, ShowInTaskbar = false, ShowActivated = false }; flow.Show(); Pump();
        void Click(string name) { ((Button)flow.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        ((RadioButton)flow.FindName("StreamingModeOption")).IsChecked = true; Click("ChooseModeButton");
        Require(((FrameworkElement)flow.FindName("RolePanel")).IsVisible && !((FrameworkElement)flow.FindName("SetupPanel")).IsVisible, "Mode button opens role choice, no configuration exposed");
        ((RadioButton)flow.FindName("GuestRoleOption")).IsChecked = true; Click("ChooseModeButton");
        Require(((FrameworkElement)flow.FindName("GuestSetupPanel")).IsVisible && !((FrameworkElement)flow.FindName("MediaPanel")).IsVisible && !((FrameworkElement)flow.FindName("HostSetupPanel")).IsVisible, "Streaming guest sees invitation only");
        Require(!((Button)flow.FindName("JoinRoomButton")).IsEnabled, "Guest needs an invitation before joining");
        ((TextBox)flow.FindName("JoinInvite")).Text = "invite for validation"; Require(((Button)flow.FindName("JoinRoomButton")).IsEnabled, "Pasting invitation enables guest action");
        Click("BackStepButton"); ((RadioButton)flow.FindName("HostRoleOption")).IsChecked = true; Click("ChooseModeButton");
        Require(((FrameworkElement)flow.FindName("HostSetupPanel")).IsVisible && !((FrameworkElement)flow.FindName("GuestSetupPanel")).IsVisible, "Back navigation permits switching role with separate fields");
        Click("BackStepButton"); Click("BackStepButton"); Require(((FrameworkElement)flow.FindName("ModePanel")).IsVisible, "Back returns through role choice to mode");
        flow.Close(); Pump();
        var origin = new Window { Width = 100, Height = 100, Opacity = 0, ShowInTaskbar = false, ShowActivated = false }; origin.Show(); Application.Current.MainWindow = origin; Pump();
        TheaterNavigation.OpenCommand.Execute(null, origin); Pump();
        Require(Application.Current.Windows.OfType<MovieTheaterWindow>().Count() == 1, "Theater navigation opens one window");
        TheaterNavigation.OpenCommand.Execute(null, origin); Pump();
        Require(Application.Current.Windows.OfType<MovieTheaterWindow>().Count() == 1, "Repeated navigation reuses room window");
        var openTheater = Application.Current.Windows.OfType<MovieTheaterWindow>().Single();
        ((Button)openTheater.FindName("ReturnButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Require(Application.Current.Windows.OfType<MovieTheaterWindow>().Count() == 0 && origin.IsVisible, "Return to AniT closes Movie Theater and keeps primary window open");
        origin.Close(); Pump();
        Console.WriteLine($"PASS: {count} Movie Theater mode selection/P2P/streaming host/guest theme/scale/text/viewport cases; bounded layout, role controls, readiness and scrolling.");
    }
    private static void Wait(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!predicate()) { if (DateTime.UtcNow >= until) throw new TimeoutException("Room fixture preparation timed out"); System.Threading.Thread.Sleep(10); }
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
