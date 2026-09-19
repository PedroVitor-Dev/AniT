using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AniT.App;

public partial class ProfileEditWindow : Window
{
    private readonly DateTimeOffset memberSince;
    private readonly string fallbackAvatarPath;
    private string? selectedAvatarPath;
    private string? selectedBannerPath;

    public ProfileSettings? SavedSettings { get; private set; }

    public ProfileEditWindow(ProfileSettings settings, string fallbackAvatarPath)
    {
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(Height, MaxHeight);
        memberSince = settings.MemberSince;
        this.fallbackAvatarPath = fallbackAvatarPath;
        selectedAvatarPath = settings.AvatarPath;
        selectedBannerPath = settings.BannerPath;

        NameTextBox.Text = settings.DisplayName;
        BioTextBox.Text = settings.Bio;
        AvatarSizeSlider.Value = settings.AvatarSize;
        RefreshAvatarPreview();
        RefreshBannerPreview();

        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void ChooseAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher foto de perfil",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;
        selectedAvatarPath = dialog.FileName;
        RefreshAvatarPreview();
    }

    private void ClearAvatar_Click(object sender, RoutedEventArgs e)
    {
        selectedAvatarPath = null;
        RefreshAvatarPreview();
    }

    private void ChooseBanner_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileBannerPickerWindow { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedBannerPath)) return;
        selectedBannerPath = dialog.SelectedBannerPath;
        RefreshBannerPreview();
    }

    private void ClearBanner_Click(object sender, RoutedEventArgs e)
    {
        selectedBannerPath = null;
        RefreshBannerPreview();
    }

    private void AvatarSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AvatarSizeText is not null) AvatarSizeText.Text = $"{Math.Round(e.NewValue):0} px";
        if (AvatarPreview is not null)
        {
            AvatarPreview.Width = e.NewValue;
            AvatarPreview.Height = e.NewValue;
        }
    }

    private void RefreshAvatarPreview()
    {
        var path = !string.IsNullOrWhiteSpace(selectedAvatarPath) && File.Exists(selectedAvatarPath)
            ? selectedAvatarPath
            : fallbackAvatarPath;

        AvatarFileText.Text = selectedAvatarPath is null
            ? "Avatar automático"
            : "Foto personalizada selecionada";

        try
        {
            var uri = File.Exists(path)
                ? new Uri(path, UriKind.Absolute)
                : new Uri("pack://application:,,,/Assets/Profile/1.png", UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = uri;
            image.EndInit();
            image.Freeze();
            AvatarPreviewBrush.ImageSource = image;
        }
        catch
        {
            AvatarPreviewBrush.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Assets/Profile/1.png", UriKind.Absolute));
        }
    }

    private void RefreshBannerPreview()
    {
        BannerStatusText.Text = selectedBannerPath is null
            ? "Capa padrão do AniT"
            : "Arte em alta qualidade guardada no cache";

        try
        {
            BannerPreview.Source = LoadImage(
                selectedBannerPath,
                "pack://application:,,,/Assets/History/2.png");
        }
        catch
        {
            BannerPreview.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/History/2.png", UriKind.Absolute));
        }
    }

    private static BitmapImage LoadImage(string? path, string fallbackPackUri)
    {
        var uri = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? new Uri(path, UriKind.Absolute)
            : new Uri(fallbackPackUri, UriKind.Absolute);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = uri;
        image.EndInit();
        image.Freeze();
        return image;
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

        SavedSettings = new ProfileSettings(
            name,
            BioTextBox.Text.Trim(),
            memberSince,
            selectedAvatarPath,
            Math.Round(AvatarSizeSlider.Value),
            selectedBannerPath);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
