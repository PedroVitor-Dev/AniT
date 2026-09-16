using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace AniT.App;

public partial class OrganizationPreviewWindow : Window
{
    private global::AniT.Core.FileOrganizationPlan? plan;
    public ObservableCollection<OrganizationPreviewItem> PreviewItems { get; } = [];

    public OrganizationPreviewWindow()
    {
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 1040, 720);
        DataContext = this;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => EmptyPreview.Visibility = Visibility.Visible;

    private async void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Destino da biblioteca organizada" };
        if (dialog.ShowDialog() is not true) return;
        DestinationText.Text = dialog.FolderName;
        StatusText.Text = "Gerando prévia…";
        await using var context = App.OpenFreshDatabase();
        var ids = await context.MediaFiles
            .Where(file => file.Availability == global::AniT.Core.MediaFileAvailability.Available && file.DuplicateOfMediaFileId == null)
            .Select(file => file.Id)
            .ToListAsync();
        plan = await new global::AniT.Infrastructure.LibraryOrganizer(context).BuildPlanAsync(ids, dialog.FolderName);
        PreviewItems.Clear();
        foreach (var operation in plan.Operations)
            PreviewItems.Add(new OrganizationPreviewItem(operation));
        EmptyPreview.Visibility = PreviewItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExecuteButton.IsEnabled = PreviewItems.Any(item => item.CanExecute);
        StatusText.Text = $"{PreviewItems.Count} operação(ões) · {PreviewItems.Count(item => !item.CanExecute)} conflito(s)";
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (plan is null) return;
        var selected = PreviewItems.Where(item => item.IsSelected && item.CanExecute).Select(item => item.MediaFileId).ToList();
        if (selected.Count == 0) { StatusText.Text = "Selecione ao menos uma operação válida."; return; }
        if (MessageBox.Show($"Mover/renomear {selected.Count} arquivo(s) e seus sidecars agora?", "Confirmar organização", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        ExecuteButton.IsEnabled = false;
        await using var context = App.OpenFreshDatabase();
        var result = await new global::AniT.Infrastructure.LibraryOrganizer(context).ExecuteAsync(plan, selected);
        if (result.Errors.Count > 0)
        {
            StatusText.Text = $"{result.Completed} concluído(s) · {result.Errors.Count} erro(s). {result.Errors[0]}";
            ExecuteButton.IsEnabled = true;
            return;
        }
        MessageBox.Show($"{result.Completed} arquivo(s) organizado(s) com segurança.", "AniT", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class OrganizationPreviewItem : INotifyPropertyChanged
{
    private bool isSelected;
    public OrganizationPreviewItem(global::AniT.Core.FileOrganizationOperation operation)
    {
        MediaFileId = operation.MediaFileId;
        SourcePath = operation.SourcePath;
        DestinationPath = operation.DestinationPath;
        CanExecute = !operation.HasConflict;
        isSelected = CanExecute;
        Detail = operation.HasConflict
            ? operation.ValidationMessage ?? "Conflito"
            : operation.Sidecars.Count > 0 ? $"Inclui {operation.Sidecars.Count} legenda(s) sidecar" : "Pronto para organizar";
    }
    public Guid MediaFileId { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public bool CanExecute { get; }
    public string Detail { get; }
    public bool IsSelected { get => isSelected; set { if (isSelected == value) return; isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
