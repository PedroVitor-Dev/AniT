using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace AniT.App;

public partial class SetupShelfWindow : Window
{
    private string? savedFolderPath;
    private string? pendingFolderPath;
    private Guid? configuredRootId;

    public SetupShelfWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 620, 470);
        LoadConfiguredShelf();
    }

    private void LoadConfiguredShelf()
    {
        try
        {
            using var context = App.OpenFreshDatabase();
            var configuredRoot = context.LibraryRoots
                .AsNoTracking()
                .Where(root => root.IsEnabled)
                .OrderBy(root => root.DisplayName)
                .Select(root => new { root.Id, root.Path })
                .FirstOrDefault();

            configuredRoot ??= context.LibraryRoots
                .AsNoTracking()
                .OrderBy(root => root.DisplayName)
                .Select(root => new { root.Id, root.Path })
                .FirstOrDefault();

            if (configuredRoot is null)
            {
                ShowEmptyState();
                return;
            }

            configuredRootId = configuredRoot.Id;
            savedFolderPath = NormalizePath(configuredRoot.Path);
            pendingFolderPath = savedFolderPath;
            ShowLockedState();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not load the configured shelf: {exception}");
            StatusText.Text = "Não foi possível ler a configuração da estante.";
            ShowEmptyState(keepStatus: true);
        }
    }

    private void ShowEmptyState(bool keepStatus = false)
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
        if (!keepStatus) StatusText.Text = "Escolha uma pasta para fixá-la como a estante do AniT.";
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
        StatusText.Text = Directory.Exists(savedFolderPath)
            ? "Diretório fixado. Clique em Editar para escolher outro."
            : "O diretório fixado não está disponível. Clique em Editar para substituí-lo.";
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        ShelfTitleText.Text = "Editar diretório da estante";
        LockBadge.Visibility = Visibility.Collapsed;
        EditButton.Visibility = Visibility.Collapsed;
        ChooseFolderButton.Visibility = Visibility.Visible;
        ChooseFolderButton.Content = "Escolher outra pasta";
        FinishButton.Visibility = Visibility.Visible;
        FinishButton.IsEnabled = false;
        CancelEditButton.Visibility = Visibility.Visible;
        StatusText.Text = "Escolha a nova pasta. A alteração só será aplicada depois de Salvar.";
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
        StatusText.Text = "Nova pasta selecionada. Clique em Salvar para fixar a alteração.";
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (pendingFolderPath is null) return;
        var selectedPath = NormalizePath(pendingFolderPath);
        if (!Directory.Exists(selectedPath))
        {
            StatusText.Text = "Essa pasta não está disponível.";
            return;
        }

        FinishButton.IsEnabled = false;
        ChooseFolderButton.IsEnabled = false;
        CancelEditButton.IsEnabled = false;
        StatusText.Text = "Salvando e organizando episódios…";

        try
        {
            await using var context = App.OpenFreshDatabase();
            var roots = await context.LibraryRoots.ToListAsync();
            var root = configuredRootId is Guid id ? roots.FirstOrDefault(item => item.Id == id) : null;
            root ??= roots.FirstOrDefault(item => PathsMatch(item.Path, selectedPath));

            if (root is null)
            {
                root = new global::AniT.Core.LibraryRoot
                {
                    Path = selectedPath,
                    DisplayName = GetDisplayName(selectedPath)
                };
                context.LibraryRoots.Add(root);
            }

            root.Path = selectedPath;
            root.DisplayName = GetDisplayName(selectedPath);
            root.IsEnabled = true;
            root.IncludeSubfolders = true;
            root.LastUnavailableAt = null;

            // There is only one active shelf. Previous roots are retained as inactive
            // records so changing the folder never destroys the user's metadata.
            foreach (var previousRoot in roots.Where(item => item.Id != root.Id))
            {
                previousRoot.IsEnabled = false;
            }

            await context.SaveChangesAsync();
            var result = await new global::AniT.Infrastructure.LibraryScanner(context).ScanAsync(root);
            await App.Achievements.RecordAsync(new global::AniT.Core.Achievements.AchievementEvent(
                global::AniT.Core.Achievements.AchievementEventType.LibraryChanged));

            App.Database.ChangeTracker.Clear();
            configuredRootId = root.Id;
            savedFolderPath = selectedPath;
            pendingFolderPath = selectedPath;
            StatusText.Text = $"Estante salva · {result.EpisodesAdded} episódio(s) novo(s).";
            DialogResult = true;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"AniT could not save the shelf: {exception}");
            StatusText.Text = "Não foi possível salvar a estante. Tente novamente.";
            FinishButton.IsEnabled = true;
        }
        finally
        {
            ChooseFolderButton.IsEnabled = true;
            CancelEditButton.IsEnabled = true;
        }
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string GetDisplayName(string path)
    {
        var displayName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(displayName) ? "Minha estante" : displayName;
    }
}
