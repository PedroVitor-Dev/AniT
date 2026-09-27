using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

public partial class ShelfSettingsControl : UserControl
{
    private string? savedFolderPath;
    private string? pendingFolderPath;
    private Guid? configuredRootId;
    private bool loaded;
    private readonly ObservableCollection<MonitoredFolderItem> monitoredFolders = [];

    public event EventHandler? ShelfSaved;

    public ShelfSettingsControl()
    {
        InitializeComponent();
        MonitoredFoldersList.ItemsSource = monitoredFolders;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        LoadConfiguredShelf();
    }

    public void Reload() => LoadConfiguredShelf();

    private void LoadConfiguredShelf()
    {
        try
        {
            using var context = App.OpenFreshDatabase();
            var roots = context.LibraryRoots.AsNoTracking()
                .OrderByDescending(root => root.IsEnabled)
                .ThenBy(root => root.DisplayName)
                .Select(root => new MonitoredFolderItem(root.Id, root.DisplayName, root.Path, root.IsEnabled, root.IncludeSubfolders))
                .ToArray();
            monitoredFolders.Clear();
            foreach (var root in roots) monitoredFolders.Add(root);
            NoFoldersText.Visibility = roots.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var configuredRoot = roots.FirstOrDefault();

            if (configuredRoot is null)
            {
                ShowEmptyState();
                return;
            }

            configuredRootId = configuredRoot.Id;
            savedFolderPath = NormalizePath(configuredRoot.Path);
            pendingFolderPath = savedFolderPath;
            IncludeSubfoldersBox.IsChecked = configuredRoot.IncludeSubfolders;
            ShowLockedState();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not load the shelf settings: {exception}");
            ShowEmptyState("Não foi possível ler a configuração da estante.");
        }
    }

    private void ShowEmptyState(string? status = null)
    {
        configuredRootId = null;
        savedFolderPath = null;
        pendingFolderPath = null;
        ShelfTitleText.Text = "Prepare sua estante";
        FolderText.Text = "Nenhuma pasta selecionada";
        FolderText.ToolTip = null;
        LockBadge.Visibility = Visibility.Collapsed;
        EditButton.Visibility = Visibility.Collapsed;
        ChooseFolderButton.Visibility = Visibility.Visible;
        ChooseFolderButton.Content = "Escolher pasta";
        FinishButton.Visibility = Visibility.Visible;
        FinishButton.IsEnabled = false;
        CancelEditButton.Visibility = Visibility.Collapsed;
        ScanNowButton.Visibility = Visibility.Collapsed;
        ReviewLinksButton.Visibility = Visibility.Collapsed;
        StatusText.Text = status ?? "Escolha uma pasta para criar sua estante local.";
    }

    private void ShowLockedState()
    {
        if (savedFolderPath is null)
        {
            ShowEmptyState();
            return;
        }

        pendingFolderPath = savedFolderPath;
        ShelfTitleText.Text = "Sua estante está configurada";
        FolderText.Text = savedFolderPath;
        FolderText.ToolTip = savedFolderPath;
        LockBadge.Visibility = Visibility.Visible;
        EditButton.Visibility = Visibility.Visible;
        ChooseFolderButton.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Collapsed;
        CancelEditButton.Visibility = Visibility.Collapsed;
        ScanNowButton.Visibility = Visibility.Visible;
        ReviewLinksButton.Visibility = Visibility.Collapsed;
        StatusText.Text = Directory.Exists(savedFolderPath)
            ? "Diretório disponível. O AniT monitora esta pasta ao iniciar."
            : "O diretório não está disponível. Troque a pasta ou conecte novamente a unidade.";
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        ShelfTitleText.Text = "Trocar diretório da estante";
        LockBadge.Visibility = Visibility.Collapsed;
        EditButton.Visibility = Visibility.Collapsed;
        ChooseFolderButton.Visibility = Visibility.Visible;
        ChooseFolderButton.Content = "Escolher outra pasta";
        FinishButton.Visibility = Visibility.Visible;
        FinishButton.IsEnabled = false;
        CancelEditButton.Visibility = Visibility.Visible;
        ScanNowButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "A pasta atual permanece ativa até você salvar a nova escolha.";
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        pendingFolderPath = savedFolderPath;
        ShowLockedState();
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var initialPath = pendingFolderPath ?? savedFolderPath;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolha a pasta principal da estante",
            InitialDirectory = initialPath is not null && Directory.Exists(initialPath) ? initialPath : string.Empty
        };
        if (dialog.ShowDialog() is not true) return;

        pendingFolderPath = NormalizePath(dialog.FolderName);
        FolderText.Text = pendingFolderPath;
        FolderText.ToolTip = pendingFolderPath;
        FinishButton.IsEnabled = true;
        StatusText.Text = "Nova pasta selecionada. Salve para aplicar e iniciar a leitura.";
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (pendingFolderPath is null) return;
        var isBackupRelink = App.HasPendingLibraryRelink;
        var selectedPath = NormalizePath(pendingFolderPath);
        if (!Directory.Exists(selectedPath))
        {
            StatusText.Text = "Essa pasta não está disponível.";
            return;
        }

        SetBusy(true, "Salvando e analisando os episódios…");
        try
        {
            await using var context = App.OpenFreshDatabase();
            var roots = await context.LibraryRoots.ToListAsync();
            var root = configuredRootId is Guid id ? roots.FirstOrDefault(item => item.Id == id) : null;
            root ??= roots.FirstOrDefault(item => PathsMatch(item.Path, selectedPath));
            if (root is null)
            {
                root = new global::AniT.Core.LibraryRoot { Path = selectedPath, DisplayName = GetDisplayName(selectedPath) };
                context.LibraryRoots.Add(root);
            }

            root.Path = selectedPath;
            root.DisplayName = GetDisplayName(selectedPath);
            root.IsEnabled = true;
            root.IncludeSubfolders = IncludeSubfoldersBox.IsChecked == true;
            root.LastUnavailableAt = null;
            await context.SaveChangesAsync();
            var result = await new global::AniT.Infrastructure.LibraryScanner(
                context,
                settings: global::AniT.Infrastructure.AniTSystemSettingsStore.Load()).ScanAsync(root, preferExistingCatalog: isBackupRelink);
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.LibraryChanged));

            App.Database.ChangeTracker.Clear();
            configuredRootId = root.Id;
            savedFolderPath = selectedPath;
            pendingFolderPath = selectedPath;
            App.CompleteLibraryRelink();
            ShowLockedState();
            ReviewLinksButton.Visibility = result.NeedsReview > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = result.NeedsReview > 0
                ? $"Estante reconectada · {result.FilesMatched} arquivo(s) ligados automaticamente e {result.NeedsReview} aguardando sua confirmação."
                : $"Estante reconectada · {result.FilesMatched} arquivo(s) ligados ao histórico e {result.EpisodesAdded} episódio(s) novo(s).";
            ShelfSaved?.Invoke(this, EventArgs.Empty);
            LoadConfiguredShelf();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not save the shelf settings: {exception}");
            StatusText.Text = "Não foi possível salvar a estante. Tente novamente.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ScanNow_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Reexaminando todas as pastas monitoradas…");
        try
        {
            Guid[] rootIds;
            await using (var lookupContext = App.OpenFreshDatabase())
            {
                if (configuredRootId is Guid primaryId)
                {
                    var primary = await lookupContext.LibraryRoots.FirstOrDefaultAsync(item => item.Id == primaryId);
                    if (primary is not null) primary.IncludeSubfolders = IncludeSubfoldersBox.IsChecked == true;
                    await lookupContext.SaveChangesAsync();
                }
                rootIds = await lookupContext.LibraryRoots.AsNoTracking()
                    .Where(root => root.IsEnabled)
                    .Select(root => root.Id)
                    .ToArrayAsync();
            }

            var files = 0;
            var episodes = 0;
            var reviews = 0;
            foreach (var rootId in rootIds)
            {
                await using var context = App.OpenFreshDatabase();
                var root = await context.LibraryRoots.FirstOrDefaultAsync(item => item.Id == rootId);
                if (root is null) continue;
                var result = await new global::AniT.Infrastructure.LibraryScanner(
                    context,
                    settings: global::AniT.Infrastructure.AniTSystemSettingsStore.Load()).ScanAsync(root);
                files += result.FilesFound;
                episodes += result.EpisodesAdded;
                reviews += result.NeedsReview;
            }
            App.Database.ChangeTracker.Clear();
            ReviewLinksButton.Visibility = reviews > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = reviews > 0
                ? $"Leitura concluída · {files} arquivo(s) e {reviews} precisam de confirmação manual."
                : $"Leitura concluída · {files} arquivo(s), {episodes} episódio(s) novo(s).";
            ShelfSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not rescan the shelf: {exception}");
            StatusText.Text = "A leitura não pôde ser concluída.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        EditButton.IsEnabled = !busy;
        ChooseFolderButton.IsEnabled = !busy;
        FinishButton.IsEnabled = !busy && pendingFolderPath is not null && !PathsMatch(pendingFolderPath, savedFolderPath ?? string.Empty);
        CancelEditButton.IsEnabled = !busy;
        ScanNowButton.IsEnabled = !busy;
        ReviewLinksButton.IsEnabled = !busy;
        IncludeSubfoldersBox.IsEnabled = !busy;
        if (message is not null) StatusText.Text = message;
    }

    private static bool PathsMatch(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string GetDisplayName(string path) => string.IsNullOrWhiteSpace(Path.GetFileName(path)) ? "Minha estante" : Path.GetFileName(path);

    private void ReviewLinks_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner) AppNavigation.OpenLibraryReview(owner);
    }

    private async void AddMonitoredFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Adicionar pasta monitorada" };
        if (dialog.ShowDialog() is not true) return;
        var selectedPath = NormalizePath(dialog.FolderName);
        SetBusy(true, "Adicionando e analisando a nova pasta…");
        try
        {
            await using var context = App.OpenFreshDatabase();
            var root = await context.LibraryRoots.FirstOrDefaultAsync(item => item.Path == selectedPath);
            if (root is null)
            {
                root = new global::AniT.Core.LibraryRoot
                {
                    Path = selectedPath,
                    DisplayName = GetDisplayName(selectedPath),
                    IsEnabled = true,
                    IncludeSubfolders = true
                };
                context.LibraryRoots.Add(root);
            }
            else
            {
                root.IsEnabled = true;
                root.LastUnavailableAt = null;
            }
            await context.SaveChangesAsync();
            var result = await new global::AniT.Infrastructure.LibraryScanner(
                context,
                settings: global::AniT.Infrastructure.AniTSystemSettingsStore.Load()).ScanAsync(root, preferExistingCatalog: App.HasPendingLibraryRelink);
            App.CompleteLibraryRelink();
            StatusText.Text = $"Pasta adicionada · {result.FilesFound} arquivo(s) encontrados e {result.NeedsReview} para revisar.";
            LoadConfiguredShelf();
            ShelfSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not add a monitored folder: {exception}");
            StatusText.Text = "Não foi possível adicionar essa pasta.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RootEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id } checkBox) return;
        await UpdateRootAsync(id, root => root.IsEnabled = checkBox.IsChecked == true);
    }

    private async void RootSubfolders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id } checkBox) return;
        await UpdateRootAsync(id, root => root.IncludeSubfolders = checkBox.IsChecked == true);
    }

    private async Task UpdateRootAsync(Guid rootId, Action<global::AniT.Core.LibraryRoot> update)
    {
        try
        {
            await using var context = App.OpenFreshDatabase();
            var root = await context.LibraryRoots.FirstOrDefaultAsync(item => item.Id == rootId);
            if (root is null) return;
            update(root);
            await context.SaveChangesAsync();
            LoadConfiguredShelf();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not update a monitored folder: {exception}");
            StatusText.Text = "Não foi possível atualizar essa pasta.";
            LoadConfiguredShelf();
        }
    }

    private async void RemoveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var item = monitoredFolders.FirstOrDefault(folder => folder.Id == id);
        if (item is null || MessageBox.Show(
                $"Parar de monitorar “{item.DisplayName}”? Os vídeos não serão apagados e seu histórico será preservado.",
                "Remover pasta monitorada",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await using var context = App.OpenFreshDatabase();
            await context.LibraryReviewItems.Where(review => review.LibraryRootId == id).ExecuteDeleteAsync();
            await context.MediaFiles.Where(file => file.LibraryRootId == id).ExecuteDeleteAsync();
            await context.LibraryRoots.Where(root => root.Id == id).ExecuteDeleteAsync();
            if (configuredRootId == id) configuredRootId = null;
            App.Database.ChangeTracker.Clear();
            LoadConfiguredShelf();
            StatusText.Text = "Pasta removida do monitoramento. Histórico e progresso foram preservados.";
            ShelfSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not remove a monitored folder: {exception}");
            StatusText.Text = "Não foi possível remover essa pasta.";
        }
    }
}

public sealed record MonitoredFolderItem(Guid Id, string DisplayName, string Path, bool IsEnabled, bool IncludeSubfolders);
