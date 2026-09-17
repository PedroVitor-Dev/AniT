#if DEBUG
using System.Windows;
using System.Windows.Controls;
using AniT.Core.Achievements;
using Microsoft.EntityFrameworkCore;

namespace AniT.App;

internal sealed class AchievementDebugWindow : Window
{
    private readonly ComboBox selector = new();
    private readonly TextBox amount = new() { Text = "1", Width = 80 };
    private readonly TextBlock status = new() { Foreground = System.Windows.Media.Brushes.LightSkyBlue, Margin = new Thickness(0, 12, 0, 0) };

    public AchievementDebugWindow()
    {
        Title = "Achievement Debug · AniT";
        Width = 620;
        Height = 390;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 21, 43));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        selector.ItemsSource = AchievementCatalog.All;
        selector.DisplayMemberPath = nameof(AchievementDefinition.Name);
        selector.SelectedIndex = 0;
        selector.Margin = new Thickness(0, 10, 0, 16);
        selector.Padding = new Thickness(10);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Achievement Debug", Foreground = System.Windows.Media.Brushes.White, FontSize = 26, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Disponível somente em builds DEBUG.", Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 3, 0, 0) });
        panel.Children.Add(selector);
        panel.Children.Add(Row(
            Button("Unlock", async () => await UnlockAsync()),
            amount,
            Button("Add progress", async () => await AddProgressAsync()),
            Button("Show popup", ShowPopupAsync)));
        panel.Children.Add(Row(
            Button("Reset achievement", async () => await ResetAsync()),
            Button("Reset all", ResetAllAsync),
            Button("Recalculate", async () => { await App.Achievements.RecalculateAsync(); status.Text = "Recalculado."; })));
        panel.Children.Add(status);
        Content = panel;
    }

    private AchievementDefinition Selected => (AchievementDefinition)selector.SelectedItem;

    private async Task UnlockAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var state = await context.UserAchievements.FindAsync(Selected.Id) ?? new UserAchievement { AchievementId = Selected.Id };
        if (context.Entry(state).State == EntityState.Detached) context.UserAchievements.Add(state);
        state.CurrentValue = Selected.TargetValue;
        state.IsUnlocked = true;
        state.UnlockedAt ??= DateTimeOffset.UtcNow;
        state.PopupShown = false;
        if (!await context.AchievementHistory.AnyAsync(item => item.AchievementId == Selected.Id))
            context.AchievementHistory.Add(new AchievementHistoryEntry { AchievementId = Selected.Id, UnlockedAt = state.UnlockedAt.Value, Points = AchievementPoints.For(Selected.Rarity) });
        await context.SaveChangesAsync();
        AchievementNotificationQueue.Enqueue([new AchievementUnlock(Selected, state.UnlockedAt.Value, AchievementPoints.For(Selected.Rarity))]);
        status.Text = $"#{Selected.Id:000} desbloqueada.";
    }

    private async Task AddProgressAsync()
    {
        if (!long.TryParse(amount.Text, out var delta)) return;
        await using var context = App.OpenFreshDatabase();
        var state = await context.UserAchievements.FindAsync(Selected.Id) ?? new UserAchievement { AchievementId = Selected.Id };
        if (context.Entry(state).State == EntityState.Detached) context.UserAchievements.Add(state);
        state.CurrentValue = Math.Max(0, state.CurrentValue + delta);
        await context.SaveChangesAsync();
        status.Text = $"Progresso: {state.CurrentValue} / {Selected.TargetValue}.";
    }

    private Task ShowPopupAsync()
    {
        AchievementNotificationQueue.Enqueue([new AchievementUnlock(Selected, DateTimeOffset.UtcNow, AchievementPoints.For(Selected.Rarity))]);
        status.Text = "Popup enfileirado.";
        return Task.CompletedTask;
    }

    private async Task ResetAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var state = await context.UserAchievements.FindAsync(Selected.Id);
        if (state is not null) context.UserAchievements.Remove(state);
        var history = await context.AchievementHistory.SingleOrDefaultAsync(item => item.AchievementId == Selected.Id);
        if (history is not null) context.AchievementHistory.Remove(history);
        await context.SaveChangesAsync();
        status.Text = $"#{Selected.Id:000} resetada.";
    }

    private async Task ResetAllAsync()
    {
        await using var context = App.OpenFreshDatabase();
        context.UserAchievements.RemoveRange(context.UserAchievements);
        context.AchievementHistory.RemoveRange(context.AchievementHistory);
        context.AchievementMetrics.RemoveRange(context.AchievementMetrics);
        await context.SaveChangesAsync();
        status.Text = "Todas as conquistas e métricas foram resetadas.";
    }

    private static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 8, 12, 8) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
}
#endif
