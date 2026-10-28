using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AniT.Infrastructure;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;

namespace AniT.App;

public partial class OnlineAnimeWindow : Window
{
    readonly CancellationTokenSource lifetime = new();
    readonly bool preview;
    bool importing;
    public OnlineAnimeWindow() : this(false) { }
    internal OnlineAnimeWindow(bool preview)
    {
        this.preview = preview; InitializeComponent(); ResponsiveWindow.FitToWorkArea(this, 820, 640);
        Closed += (_, _) => lifetime.Cancel();
    }
    void Consent_Click(object sender, RoutedEventArgs e)
    {
        if (ConsentCheck.IsChecked != true) return;
        ConsentPanel.Visibility = Visibility.Collapsed; Workspace.Visibility = Visibility.Visible;
    }
    void SourceMode_Changed(object sender, RoutedEventArgs e)
    {
        if (InputPanel is null) return;
        InputPanel.Visibility = Visibility.Visible; SourceBox.Clear();
        var magnet = MagnetChoice.IsChecked == true;
        PickTorrentButton.Visibility = magnet ? Visibility.Collapsed : Visibility.Visible;
        SourceLabel.Text = magnet ? "Magnet link" : "Ou cole a URL do arquivo .torrent";
        StatusText.Text = ""; SourceBox.Focus();
    }
    async void PickTorrent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Torrent (*.torrent)|*.torrent", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ImportAsync(dialog.FileName);
    }
    async void Add_Click(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(source)) { StatusText.Text = "Informe a fonte para continuar."; SourceBox.Focus(); return; }
        if (MagnetChoice.IsChecked == true && !source.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) { StatusText.Text = "Cole um magnet link válido."; return; }
        if (TorrentChoice.IsChecked == true && (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) { StatusText.Text = "Cole uma URL HTTP/HTTPS de .torrent ou selecione um arquivo."; return; }
        await ImportAsync(source);
    }
    async Task ImportAsync(string source)
    {
        if (importing || preview) return;
        importing = true;
        try
        {
            string? folder;
            using (var db = App.OpenFreshDatabase()) folder = db.LibraryRoots.AsNoTracking().Where(root => root.IsEnabled).OrderBy(root => root.AddedAt).Select(root => root.Path).AsEnumerable().FirstOrDefault(Directory.Exists);
            if (folder is null)
            {
                var picker = new OpenFolderDialog { Title = "Escolha a pasta padrão dos seus animes" };
                if (picker.ShowDialog(this) != true) return;
                folder = picker.FolderName;
                using var db = App.OpenFreshDatabase(); db.LibraryRoots.Add(new AniT.Core.LibraryRoot { Path = folder, DisplayName = "Minha biblioteca" }); await db.SaveChangesAsync(lifetime.Token);
            }
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var popup = new OnlineImportProgressWindow(cancel) { Owner = this };
            Workspace.IsEnabled = false; popup.Show();
            var progress = new Progress<OnlineImportProgress>(popup.Report);
            try
            {
                var result = await App.ImportOnlineAnimeAsync(source, folder, progress, cancel.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                popup.Complete(result.Title, result.Episodes, result.ArtworkPending);
                await Task.Delay(AniTSystemSettingsStore.Load().ReduceMotion ? 600 : 1100, lifetime.Token);
                popup.Close();
                var primary = Application.Current.MainWindow;
                Close();
                if (primary is DashboardWindow dashboard) await dashboard.RefreshAsync();
                if (primary is { IsLoaded: true } && primary != this) AppNavigation.Home(primary);
            }
            catch (OperationCanceledException) { StatusText.Text = "Importação cancelada."; }
            catch (Exception ex) { StatusText.Text = "Não foi possível importar: " + ex.Message; }
            finally { popup.Close(); if (IsLoaded) Workspace.IsEnabled = true; }
        }
        catch (Exception ex) { StatusText.Text = "Não foi possível preparar a importação: " + ex.Message; }
        finally { importing = false; }
    }
    void Back_Click(object sender, RoutedEventArgs e) => Close();
}
