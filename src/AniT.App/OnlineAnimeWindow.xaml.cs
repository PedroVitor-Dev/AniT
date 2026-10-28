using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AniT.Infrastructure;
using Microsoft.Win32;

namespace AniT.App;

public partial class OnlineAnimeWindow : Window
{
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    OnlineAnimeService? service;
    CancellationTokenSource? adding;
    bool refreshing;
    bool playing;

    public OnlineAnimeWindow() : this(false) { }

    internal OnlineAnimeWindow(bool preview)
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1040, 780);
        timer.Tick += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { timer.Stop(); lifetime.Cancel(); adding?.Cancel(); };
        var root = preview ? null : App.Database.LibraryRoots.FirstOrDefault(r => r.IsEnabled);
        FolderBox.Text = root?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    async void Consent_Click(object sender, RoutedEventArgs e)
    {
        if (ConsentCheck.IsChecked != true) return;
        ConsentPanel.Visibility = Visibility.Collapsed;
        Workspace.Visibility = Visibility.Visible;
        try { service = App.Online; timer.Start(); await RefreshAsync(); }
        catch (Exception ex) {
            ConsentPanel.Visibility = Visibility.Visible; Workspace.Visibility = Visibility.Collapsed;
            StatusText.Text = "Não foi possível iniciar os downloads: " + ex.Message;
        }
    }

    void PickTorrent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Torrent (*.torrent)|*.torrent", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) SourceBox.Text = dialog.FileName;
    }
    void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Onde salvar os episódios", InitialDirectory = FolderBox.Text };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }
    async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (adding is not null || service is null) return;
        var source = SourceBox.Text; var folder = FolderBox.Text; var title = TitleBox.Text;
        if (string.IsNullOrWhiteSpace(source)) { StatusText.Text = "Cole um magnet link ou escolha um arquivo .torrent."; SourceBox.Focus(); return; }
        adding = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        AddForm.IsEnabled = false; BusyBar.Visibility = CancelButton.Visibility = Visibility.Visible;
        StatusText.Text = "Lendo o torrent e buscando a lista de episódios… Isso pode levar até 2 minutos para magnet links.";
        try {
            await Task.Run(() => service.AddAsync(source, folder, title, true, adding.Token));
            SourceBox.Clear(); TitleBox.Clear(); StatusText.Text = "Adicionado à biblioteca. Escolha um episódio para assistir durante o download.";
            await RefreshAsync();
        } catch (OperationCanceledException) { StatusText.Text = "Preparação cancelada ou sem resposta dos participantes. Nenhum episódio foi adicionado."; }
        catch (Exception ex) { StatusText.Text = "Não foi possível adicionar: " + ex.Message; }
        finally { AddForm.IsEnabled = true; BusyBar.Visibility = CancelButton.Visibility = Visibility.Collapsed; adding.Dispose(); adding = null; }
    }
    void Cancel_Click(object sender, RoutedEventArgs e) => adding?.Cancel();
    async Task RefreshAsync()
    {
        if (refreshing || service is null || lifetime.IsCancellationRequested) return;
        refreshing = true;
        try {
            var snapshots = await Task.Run(() => service.SnapshotAsync(lifetime.Token));
            if (lifetime.IsCancellationRequested) return;
            // Avoid resetting focused controls unless something displayed has changed.
            var rows = snapshots.Select(d => new OnlineDownloadRow(d)).ToArray();
            var signature = string.Join("|", rows.Select(r => r.Signature));
            if (!Equals(DownloadsList.Tag, signature)) { DownloadsList.ItemsSource = rows; DownloadsList.Tag = signature; }
            EmptyText.Visibility = snapshots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (service.LastError is { } error) StatusText.Text = error;
        } catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = "Falha ao atualizar downloads: " + ex.Message; }
        finally { refreshing = false; }
    }
    async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (service is null || sender is not Button { DataContext: OnlineDownloadRow row }) return;
        try { await Task.Run(() => service.SetPausedAsync(row.Id, !row.Paused, lifetime.Token)); await RefreshAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (playing || sender is not Button { Tag: Guid id }) return;
        playing = true;
        try { await App.PlayEpisodeAsync(id); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { playing = false; }
    }
    void Back_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class OnlineDownloadRow(OnlineTransfer transfer)
{
    public string Id => transfer.Id;
    public string Name => transfer.Name;
    public bool Paused => transfer.Paused;
    public Visibility PauseVisibility => transfer.Episodes.Count > 0 && transfer.Episodes.All(e => !e.IsCloud) ? Visibility.Collapsed : Visibility.Visible;
    public string PauseLabel => Paused ? "Retomar download" : "Pausar download";
    public double Progress => transfer.Progress;
    public string Status => $"{transfer.State} · {transfer.Progress:0.0}% · {transfer.DownloadSpeed / 1024d:0} KB/s · {transfer.Peers} conexões";
    public OnlineEpisodeRow[] Episodes { get; } = transfer.Episodes.Select(e => new OnlineEpisodeRow(e)).ToArray();
    public string Signature => Id + Status + string.Join(";", Episodes.Select(e => e.Status));
}
public sealed class OnlineEpisodeRow(OnlineEpisode episode)
{
    public Guid EpisodeId => episode.EpisodeId;
    public string Title => episode.Title;
    public string FileName => episode.FileName;
    public double Progress => episode.Progress;
    public string Status => episode.IsCloud ? $"{episode.Progress:0.0}% baixado · {episode.Size / (1024d * 1024):0} MB" : "Download concluído · disponível no seu PC";
    public string PlayLabel => episode.IsCloud ? "Assistir em streaming" : "Assistir";
    public Visibility CloudVisibility => episode.IsCloud ? Visibility.Visible : Visibility.Collapsed;
}
