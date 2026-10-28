using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using AniT.Theater;

namespace AniT.App;

public partial class MovieTheaterWindow
{
    private TheaterPreparationWindow? preparationPopup;
    private TheaterVoiceWindow? voicePopup;
    private IntPtr playerWindowHandle;
    private bool closingFloating, preparationDismissed, voiceDismissed;
    private string preparationStage = "";
    internal TheaterPreparationWindow? ActivePreparationPopup => preparationPopup;
    internal TheaterVoiceWindow? ActiveVoicePopup => voicePopup;
    internal IntPtr FloatingPlayerOwner => viewer?.WindowHandle is { } handle && handle != IntPtr.Zero ? handle : playerWindowHandle;

    private void OpenVoicePopup_Click(object sender, RoutedEventArgs e)
    {
        voiceDismissed = false; ShowVoicePopup(); voicePopup?.Activate();
    }
    private void OpenPreparationPopup_Click(object sender, RoutedEventArgs e)
    {
        preparationDismissed = false; UpdateFloatingWindows(); preparationPopup?.Activate();
    }
    private void UpdateFloatingWindows()
    {
        if (closed || closingFloating) return;
        if (room is not { } active) { CloseFloatingWindows(); return; }
        if (prepared && !voiceDismissed) ShowVoicePopup();
        if (voicePopup is { } voice) { AttachFloatingOwner(voice); voice.Refresh(active, busy); }
        var deadline = active.IsHost ? scheduledStart : active.LatestState is { Playing: true } state ? state.HostTimeMs : 0;
        var counting = active.PlaybackAuthorized && deadline > ClockEstimator.NowMs + (active.IsHost ? 0 : active.Clock.OffsetMs);
        if (!prepared || active.Mode != TheaterMode.PeerSync || active.PlaybackAuthorized && !counting) { ClosePreparationPopup(); preparationStage = ""; preparationDismissed = false; return; }
        var stage = counting ? "countdown" : active.IsHost ? active.CanApprovePlayback ? "approve" : "waiting" : active.GuestConfirmed ? "confirmed" : "confirm";
        if (stage != preparationStage) { preparationStage = stage; preparationDismissed = false; }
        if (preparationPopup is null && !preparationDismissed)
        {
            var bound = active;
            var popup = new TheaterPreparationWindow(async () =>
            {
                if (!ReferenceEquals(room, bound) || closed) return;
                if (bound.IsHost) await PlayTogetherAsync(); else await ConfirmPresenceAsync();
            });
            preparationPopup = popup; AttachFloatingOwner(popup);
            popup.Closed += (_, _) => { if (ReferenceEquals(preparationPopup, popup)) { preparationPopup = null; preparationDismissed = true; } };
            popup.Refresh(active, busy, deadline); popup.Show();
        }
        if (preparationPopup is { } preparation) { AttachFloatingOwner(preparation); preparation.Refresh(active, busy, deadline); }
    }
    private void ShowVoicePopup()
    {
        if (closed || closingFloating || room is not { } bound || voicePopup is not null) return;
        Task IfCurrent(Func<Task> action) => ReferenceEquals(room, bound) && !closed ? action() : Task.CompletedTask;
        var popup = new TheaterVoiceWindow(() => IfCurrent(ToggleVoiceAsync), () => IfCurrent(ToggleMicrophoneAsync), () => IfCurrent(ToggleVoiceAudioAsync), id => IfCurrent(() => ToggleParticipantMicrophoneAsync(id)));
        voicePopup = popup; AttachFloatingOwner(popup);
        popup.Closed += async (_, _) =>
        {
            if (!ReferenceEquals(voicePopup, popup)) return;
            voicePopup = null; voiceDismissed = true;
            if (closingFloating || closed || !ReferenceEquals(room, bound)) return;
            voiceDevice?.Dispose(); voiceDevice = null;
            try { await bound.StopVoiceAsync(); } catch (Exception) { }
            if (!closed) UpdateControls();
        };
        popup.Refresh(bound, busy); popup.Show(); PositionVoicePopup(popup);
    }
    private void AttachFloatingOwner(Window popup)
    {
        var owner = FloatingPlayerOwner;
        if (owner == IntPtr.Zero || !IsWindow(owner)) owner = new WindowInteropHelper(this).EnsureHandle();
        var helper = new WindowInteropHelper(popup);
        if (helper.Owner != owner) helper.Owner = owner;
    }
    private void PositionVoicePopup(Window popup)
    {
        var handle = new WindowInteropHelper(popup).Handle;
        var owner = FloatingPlayerOwner;
        var area = System.Windows.Forms.Screen.FromHandle(owner == IntPtr.Zero ? new WindowInteropHelper(this).Handle : owner).WorkingArea;
        if (GetWindowRect(handle, out var bounds)) SetWindowPos(handle, IntPtr.Zero, Math.Max(area.Left, area.Right - (bounds.Right - bounds.Left) - 20), Math.Max(area.Top, area.Bottom - (bounds.Bottom - bounds.Top) - 20), 0, 0, 0x15);
    }
    private void ClosePreparationPopup()
    {
        var popup = preparationPopup; preparationPopup = null; popup?.Close();
    }
    private void CloseFloatingWindows()
    {
        closingFloating = true;
        try { ClosePreparationPopup(); var voice = voicePopup; voicePopup = null; voice?.Close(); }
        finally { closingFloating = false; voiceDismissed = preparationDismissed = false; preparationStage = ""; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FloatingBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out FloatingBounds bounds);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
