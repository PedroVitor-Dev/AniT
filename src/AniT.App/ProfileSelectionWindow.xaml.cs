using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AniT.App;

public partial class ProfileSelectionWindow : Window
{
    private readonly ObservableCollection<ProfileEntryItem> profiles = [];
    private readonly DispatcherTimer lockoutTimer;
    private ProfileEntryItem? pendingProfile;
    private int failedAttempts;
    private int lockoutSeconds;

    public Guid SelectedProfileId { get; private set; }

    public ProfileSelectionWindow()
    {
        InitializeComponent();
        lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        lockoutTimer.Tick += LockoutTimer_Tick;
        ProfilesList.ItemsSource = profiles;
        foreach (var profile in ProfileSettingsStore.GetProfiles())
        {
            var avatar = !string.IsNullOrWhiteSpace(profile.AvatarPath) && File.Exists(profile.AvatarPath)
                ? profile.AvatarPath
                : "Assets/Profile/1.png";
            profiles.Add(new ProfileEntryItem(
                profile.ProfileId,
                profile.DisplayName,
                avatar,
                ProfileSettingsStore.HasPin(profile)));
        }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProfileEntryItem profile }) return;
        if (!profile.HasPin)
        {
            Accept(profile.ProfileId);
            return;
        }

        pendingProfile = profile;
        PinAvatarBrush.ImageSource = ComfortableImageSource.Load(profile.AvatarPath, 320);
        PinTitle.Text = $"PIN de {profile.DisplayName}";
        PinBox.Clear();
        PinError.Text = string.Empty;
        ConfirmPinButton.IsEnabled = true;
        PinOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => PinBox.Focus(), DispatcherPriority.Input);
    }

    private void ConfirmPin_Click(object sender, RoutedEventArgs e) => TryConfirmPin();

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        TryConfirmPin();
        e.Handled = true;
    }

    private void TryConfirmPin()
    {
        if (pendingProfile is null || lockoutSeconds > 0) return;
        if (ProfileSettingsStore.VerifyPin(pendingProfile.ProfileId, PinBox.Password))
        {
            failedAttempts = 0;
            Accept(pendingProfile.ProfileId);
            return;
        }

        failedAttempts++;
        PinBox.Clear();
        if (failedAttempts >= 5)
        {
            failedAttempts = 0;
            lockoutSeconds = 30;
            ConfirmPinButton.IsEnabled = false;
            PinError.Text = "Muitas tentativas. Aguarde 30 segundos.";
            lockoutTimer.Start();
        }
        else
        {
            PinError.Text = $"PIN incorreto. Restam {5 - failedAttempts} tentativa(s).";
        }
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (lockoutSeconds == 0) PinError.Text = string.Empty;
    }

    private void LockoutTimer_Tick(object? sender, EventArgs e)
    {
        lockoutSeconds--;
        if (lockoutSeconds > 0)
        {
            PinError.Text = $"Muitas tentativas. Aguarde {lockoutSeconds} segundos.";
            return;
        }
        lockoutTimer.Stop();
        ConfirmPinButton.IsEnabled = true;
        PinError.Text = "Você já pode tentar novamente.";
        PinBox.Focus();
    }

    private void CancelPin_Click(object sender, RoutedEventArgs e) => ClosePinOverlay();

    private void ClosePinOverlay()
    {
        pendingProfile = null;
        PinBox.Clear();
        PinOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (PinOverlay.Visibility == Visibility.Visible) ClosePinOverlay();
        else Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Accept(Guid profileId)
    {
        lockoutTimer.Stop();
        SelectedProfileId = profileId;
        DialogResult = true;
    }
}

public sealed record ProfileEntryItem(Guid ProfileId, string DisplayName, string AvatarPath, bool HasPin)
{
    public Visibility LockVisibility => HasPin ? Visibility.Visible : Visibility.Collapsed;
    public string AccessLabel => HasPin ? "Protegido por PIN" : "Entrar no perfil";
}
