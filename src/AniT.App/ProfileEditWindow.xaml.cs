using System.Windows;

namespace AniT.App;

public partial class ProfileEditWindow : Window
{
    private readonly DateTimeOffset memberSince;
    public ProfileSettings? SavedSettings { get; private set; }

    public ProfileEditWindow(ProfileSettings settings)
    {
        InitializeComponent();
        memberSince = settings.MemberSince;
        NameTextBox.Text = settings.DisplayName;
        BioTextBox.Text = settings.Bio;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ValidationText.Text = "Digite um nome para o seu perfil.";
            NameTextBox.Focus();
            return;
        }

        SavedSettings = new ProfileSettings(name, BioTextBox.Text.Trim(), memberSince);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
