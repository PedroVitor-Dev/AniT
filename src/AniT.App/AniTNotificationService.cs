using AniT.Infrastructure;
using System.Collections.Concurrent;
using System.Drawing;
using System.Text.Json;

namespace AniT.App;

internal static class AniTNotificationService
{
    private static readonly ConcurrentQueue<NotificationEnvelope> Queue = new();
    private static readonly object Sync = new();
    private static bool processing;
    private static System.Windows.Forms.NotifyIcon? windowsIcon;
    private static Icon? ownedIcon;

    public static Task<bool> NotifyAsync(AniTNotificationKind kind, string title, string message)
    {
        var settings = AniTSystemSettingsStore.Load();
        if (!NotificationPreferences.CanDeliver(settings, kind, DateTimeOffset.Now)) return Task.FromResult(false);
        Enqueue(new NotificationEnvelope(title, message, settings.NotificationDeliveryMode));
        return Task.FromResult(true);
    }

    public static Task ShowTestAsync(NotificationDeliveryMode mode)
    {
        Enqueue(new NotificationEnvelope("Notificações prontas", "Este é um aviso de teste do AniT.", mode));
        return Task.CompletedTask;
    }

    public static void NotifyAchievementWindows(string title, string message)
    {
        var settings = AniTSystemSettingsStore.Load();
        if (settings.NotificationDeliveryMode != NotificationDeliveryMode.WindowsAndInApp ||
            !NotificationPreferences.CanDeliver(settings, AniTNotificationKind.AchievementUnlocked, DateTimeOffset.Now)) return;
        _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ShowWindows(title, message));
    }

    public static void Shutdown()
    {
        windowsIcon?.Dispose();
        windowsIcon = null;
        ownedIcon?.Dispose();
        ownedIcon = null;
    }

    private static void Enqueue(NotificationEnvelope envelope)
    {
        Queue.Enqueue(envelope);
        lock (Sync)
        {
            if (processing) return;
            processing = true;
        }
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(ProcessAsync);
    }

    private static async Task ProcessAsync()
    {
        try
        {
            while (Queue.TryDequeue(out var item))
            {
                if (item.Mode == NotificationDeliveryMode.WindowsAndInApp) ShowWindows(item.Title, item.Message);
                var toast = new AniTNotificationToastWindow(item.Title, item.Message);
                await toast.ShowToastAsync();
                await Task.Delay(180);
            }
        }
        finally
        {
            lock (Sync)
            {
                processing = false;
                if (!Queue.IsEmpty)
                {
                    processing = true;
                    _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(ProcessAsync);
                }
            }
        }
    }

    private static void ShowWindows(string title, string message)
    {
        try
        {
            if (windowsIcon is null)
            {
                var processPath = Environment.ProcessPath;
                ownedIcon = !string.IsNullOrWhiteSpace(processPath) ? Icon.ExtractAssociatedIcon(processPath) : null;
                windowsIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = ownedIcon ?? SystemIcons.Information,
                    Text = "AniT",
                    Visible = true
                };
            }
            windowsIcon.BalloonTipTitle = Limit(title, 63);
            windowsIcon.BalloonTipText = Limit(message, 255);
            windowsIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            windowsIcon.ShowBalloonTip(5000);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT Windows notification failed: {exception}");
        }
    }

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..(maximum - 1)] + "…";
    private sealed record NotificationEnvelope(string Title, string Message, NotificationDeliveryMode Mode);
}

internal sealed record NotificationRuntimeState(DateTimeOffset? LastWeeklySummaryAt = null);

internal static class NotificationRuntimeStateStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data", "notification-state.json");

    public static NotificationRuntimeState Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<NotificationRuntimeState>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }

    public static void Save(NotificationRuntimeState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
