using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;
using AniT.Theater;

internal static class TheaterFloatingChecks
{
    public static void Run()
    {
        var identity = new MediaIdentity(1024, new string('A', 64));
        var host = TheaterRoom.Host(identity, IPAddress.Loopback, displayName: "Host");
        var guests = new List<TheaterRoom>();
        var windows = new List<Window>();
        try
        {
            host.BeginPreparationAsync().GetAwaiter().GetResult();
            for (var i = 0; i < 4; i++) guests.Add(TheaterRoom.JoinAsync(host.Invite!.Encode(), identity, displayName: "Convidado com nome longo " + new string('x', 30)).GetAwaiter().GetResult());
            Wait(() => guests.All(g => g.Clock.IsReady && g.Participants.Count == 5));
            host.SetReadyAsync(true).GetAwaiter().GetResult(); foreach (var g in guests) g.SetReadyAsync(true).GetAwaiter().GetResult(); Wait(() => host.BothReady);
            var guest = guests[0];
            var hostPreparation = new TheaterPreparationWindow(() => host.ApprovePlaybackAsync(0));
            var guestPreparation = new TheaterPreparationWindow(() => guest.ConfirmParticipationAsync());
            var hostVoice = Voice(host); var guestVoice = Voice(guest);
            windows.AddRange(new Window[] { hostPreparation, guestPreparation, hostVoice, guestVoice });
            foreach (var window in windows) { window.ShowInTaskbar = false; window.ShowActivated = false; window.Opacity = 0; window.Show(); }
            void Refresh()
            {
                hostPreparation.Refresh(host, false, host.ApprovedStartAtMs); guestPreparation.Refresh(guest, false, guest.LatestState?.HostTimeMs ?? 0);
                hostVoice.Refresh(host, false); guestVoice.Refresh(guest, false);
            }
            Refresh();
            Require(!Find<Button>(hostPreparation, "ConfirmButton").IsEnabled, "Host cannot approve before everyone confirms");
            Require(Find<Button>(guestPreparation, "ConfirmButton").IsEnabled, "Guest can confirm over the video");
            Click(guestPreparation, "ConfirmButton"); Wait(() => guest.GuestConfirmed); Refresh();
            Require(guest.Participants.Any(p => p.Confirmed), "Guest sees its own confirmed state in the participant list");
            Require(Find<Button>(guestPreparation, "ConfirmButton").Visibility == Visibility.Collapsed, "Confirmed guest only waits for host");
            foreach (var member in guests.Skip(1)) member.ConfirmParticipationAsync().GetAwaiter().GetResult(); Wait(() => host.CanApprovePlayback); Refresh();
            Require(Find<Button>(hostPreparation, "ConfirmButton").IsEnabled, "Only prepared host can approve all four guests");
            Click(hostPreparation, "ConfirmButton"); Wait(() => guest.PlaybackAuthorized); Refresh();
            Require(Find<TextBlock>(hostPreparation, "Countdown").Text.Contains("Começa"), "Host countdown appears over the player");
            Require(Find<Button>(guestVoice, "MuteButton").Visibility == Visibility.Collapsed, "Microphone stays off before joining voice");
            Click(guestVoice, "JoinButton"); Wait(() => guest.VoiceEnabled); Refresh();
            Require(guest.VoiceMuted && Find<Button>(guestVoice, "MuteButton").IsVisible, "Joining voice stays muted and reveals controls");
            Click(guestVoice, "DeafenButton"); Wait(() => guest.VoiceDeafened); Refresh();
            Require(Find<Button>(guestVoice, "MuteButton").IsEnabled == false, "Deafening also protects microphone activation");
            var cases = 0;
            foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light })
            foreach (var scale in new[] { 100, 125 })
            foreach (var text in new[] { 100, 140 })
            foreach (var size in new[] { (340d, 320d), (460d, 510d), (660d, 680d) })
            {
                AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode = theme, InterfaceScalePercent = scale, TextScalePercent = text }); Refresh();
                foreach (var window in windows)
                {
                    window.Width = size.Item1; window.Height = size.Item2; window.UpdateLayout(); Pump();
                    var scroll = Find<ScrollViewer>(window, "ContentScroll"); var footer = Find<FrameworkElement>(window, "Footer");
                    Require(scroll.ViewportWidth > 150 && scroll.ViewportHeight > 20, $"Floating viewport {window.GetType().Name} {theme} {scale}/{text} {size}: {scroll.ViewportWidth}x{scroll.ViewportHeight}");
                    Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "No horizontal overflow in floating windows");
                    var root = (FrameworkElement)window.Content; var bounds = footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
                    Require(bounds.Bottom <= root.ActualHeight + 1, "Footer stays reachable with large text");
                    scroll.ScrollToBottom(); window.UpdateLayout(); Require(scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ExtentHeight - 1, "All participants remain reachable"); scroll.ScrollToTop(); window.UpdateLayout(); Pump();
                    if (Environment.GetEnvironmentVariable("ANIT_THEATER_CAPTURE_ROOT") is { Length: > 0 } output && scale == 100 && text == 100 && size.Item1 == 460)
                    {
                        Directory.CreateDirectory(output);
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, window.GetType().Name + "-" + windows.IndexOf(window) + "-" + theme + ".png")); encoder.Save(file);
                    }
                    cases++;
                }
            }
            Console.WriteLine($"PASS: {cases} floating preparation/voice layout cases, four guests, role-specific confirmation, host authorization/countdown and opt-in voice controls; no microphone device opened.");
        }
        finally { foreach (var window in windows) window.Close(); foreach (var guest in guests) guest.DisposeAsync().AsTask().GetAwaiter().GetResult(); host.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static TheaterVoiceWindow Voice(TheaterRoom room) => new(async () => { if (room.VoiceEnabled) await room.StopVoiceAsync(); else await room.StartVoiceAsync(); }, () => room.SetVoiceStateAsync(!room.VoiceMuted, room.VoiceDeafened), () => room.SetVoiceStateAsync(true, !room.VoiceDeafened), id => room.SetMicrophoneBlockedAsync(id, true));
    private static T Find<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Click(Window window, string name) => Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Wait(Func<bool> ready) { var deadline = DateTime.UtcNow.AddSeconds(8); while (!ready()) { if (DateTime.UtcNow >= deadline) throw new TimeoutException("Floating fixture timed out"); Pump(); Thread.Sleep(10); } }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
