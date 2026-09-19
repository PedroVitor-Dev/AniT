using System.Windows;

namespace AniT.App;

public partial class AchievementSettingsWindow : Window
{
    public AchievementSettingsWindow()
    {
        InitializeComponent();
        var settings = AchievementSettingsStore.Load();
        NotificationsCheck.IsChecked = settings.ShowNotifications;
        SoundCheck.IsChecked = settings.PlaySound;
        VolumeSlider.Value = settings.Volume * 100;
        PositionCombo.SelectedIndex = (int)settings.Position;
        ReduceAnimationsCheck.IsChecked = settings.ReduceAnimations;
    }

    private async void Recalculate_Click(object sender, RoutedEventArgs e)
    {
        var progress = await App.Achievements.RecalculateAsync();
        StatusText.Text = $"✓ {progress.Count(item => item.IsUnlocked)} de {progress.Count} conquistas desbloqueadas.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AchievementSettingsStore.Save(new AchievementSettings(
            NotificationsCheck.IsChecked == true,
            SoundCheck.IsChecked == true,
            VolumeSlider.Value / 100d,
            (AchievementToastPosition)Math.Max(0, PositionCombo.SelectedIndex),
            ReduceAnimationsCheck.IsChecked == true));
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
