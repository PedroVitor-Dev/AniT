using System.Windows;
using System.Windows.Input;

namespace AniT.App;

public partial class ProfilePinWindow : Window
{
    private readonly Guid profileId;

    public ProfilePinWindow(ProfileSettings profile)
    {
        InitializeComponent();
        profileId = profile.ProfileId;
        TitleText.Text = $"PIN de {profile.DisplayName}";
        Loaded += (_, _) => PinBox.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ProfileSettingsStore.VerifyPin(profileId, PinBox.Password))
        {
            DialogResult = true;
            return;
        }
        PinBox.Clear();
        ErrorText.Text = "PIN incorreto. Tente novamente.";
        PinBox.Focus();
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e) => ErrorText.Text = string.Empty;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm();
        else if (e.Key == Key.Escape) DialogResult = false;
    }
}
