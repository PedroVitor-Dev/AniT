using AniT.Infrastructure;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AniT.App;

public partial class ProfileBannerPickerWindow : Window
{
    private readonly ProfileBannerProvider provider;
    private readonly AniTSystemSettings systemSettings;
    private readonly ObservableCollection<ProfileBannerCandidate> results = [];
    private CancellationTokenSource? searchCancellation;
    private bool isBusy;

    public string? SelectedBannerPath { get; private set; }

    public ProfileBannerPickerWindow()
    {
        InitializeComponent();
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AniT",
            "Cache",
            "ProfileBanners");
        provider = new ProfileBannerProvider(cacheDirectory);
        systemSettings = AniTSystemSettingsStore.Load();
        foreach (var source in systemSettings.ImageSources.Where(item => item.IsEnabled && item.UseForProfileBanners))
        {
            SourceComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"{source.Name} · personalizada",
                Tag = $"custom:{source.Id:N}"
            });
        }
        ResultsList.ItemsSource = results;
        SearchBox.Text = "anime";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        await SearchAsync();
        e.Handled = true;
    }

    private async Task SearchAsync()
    {
        if (isBusy) return;
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();

        SetBusy(true, "Buscando artes em alta qualidade...");
        MessagePanel.Visibility = Visibility.Collapsed;
        try
        {
            var (source, customSourceId) = SelectedSource();
            var result = await provider.SearchAsync(
                SearchBox.Text,
                source,
                systemSettings.ImageSources,
                customSourceId,
                searchCancellation.Token);
            results.Clear();
            foreach (var item in result.Items) results.Add(item);

            ResultsScroller.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = results.Count > 0
                ? $"{results.Count} artes encontradas. Selecione uma para baixar e guardar no cache."
                : result.Message ?? "Nenhuma arte encontrada.";

            if (result.NetworkUnavailable)
            {
                ShowMessage("A internet tirou uma pausa", result.Message ?? "O Baki-Pi guardou sua capa atual. Tente novamente quando a conexão voltar.", true);
            }
            else if (results.Count == 0)
            {
                ShowMessage("Nenhuma arte por aqui", result.Message ?? "Tente outro anime, personagem ou tema.", true);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SelectBanner_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || sender is not Button { DataContext: ProfileBannerCandidate candidate }) return;
        SetBusy(true, "Guardando a arte original no cache...");
        try
        {
            SelectedBannerPath = await provider.CacheSelectedAsync(candidate);
            DialogResult = true;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            ResultsScroller.Visibility = Visibility.Collapsed;
            ShowMessage(
                "Baki-Pi não conseguiu baixar essa arte",
                $"Sua capa atual continua segura. Verifique a conexão e tente novamente.\n\n{exception.Message}",
                true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private (ProfileBannerSource Source, Guid? CustomSourceId) SelectedSource()
    {
        var tag = (SourceComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (tag?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true
            && Guid.TryParseExact(tag[7..], "N", out var customSourceId))
        {
            return (ProfileBannerSource.All, customSourceId);
        }

        return (Enum.TryParse<ProfileBannerSource>(tag, out var source) ? source : ProfileBannerSource.All, null);
    }

    private void ShowMessage(string title, string message, bool canRetry)
    {
        MessageTitle.Text = title;
        MessageText.Text = message;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        isBusy = busy;
        SearchButton.IsEnabled = !busy;
        SourceComboBox.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        LoadingPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(message)) LoadingText.Text = message;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        base.OnClosed(e);
    }
}
