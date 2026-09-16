using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace AniT.App;

public partial class MediaVersionsWindow : Window
{
    private readonly Guid episodeId;
    public ObservableCollection<MediaVersionItem> Versions { get; } = [];

    public MediaVersionsWindow(Guid episodeId)
    {
        this.episodeId = episodeId;
        InitializeComponent();
        ResponsiveWindow.FitToWorkArea(this, 760, 520);
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        await using var context = App.OpenFreshDatabase();
        var episode = await context.Episodes.AsNoTracking().Include(item => item.Season)!.ThenInclude(season => season!.Anime)
            .Include(item => item.MediaFiles).ThenInclude(file => file.LibraryRoot)
            .SingleOrDefaultAsync(item => item.Id == episodeId);
        if (episode is null) { Close(); return; }
        TitleText.Text = $"{episode.Season?.Anime?.Title} · Episódio {episode.Number:00}";
        Versions.Clear();
        foreach (var file in episode.MediaFiles.OrderByDescending(file => file.IsPreferred).ThenByDescending(file => file.Resolution))
        {
            var fullPath = file.LibraryRoot is null ? file.RelativePath : Path.Combine(file.LibraryRoot.Path, file.RelativePath);
            var available = file.Availability == global::AniT.Core.MediaFileAvailability.Available && File.Exists(fullPath);
            var labels = new[] { file.Resolution, file.Language, file.ReleaseGroup }.Where(value => !string.IsNullOrWhiteSpace(value));
            Versions.Add(new MediaVersionItem(file.Id, string.Join(" • ", labels.DefaultIfEmpty(Path.GetExtension(file.FileName).TrimStart('.').ToUpperInvariant())), fullPath, file.IsPreferred, available, file.DuplicateOfMediaFileId is not null));
        }
    }

    private async void Prefer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid id }) return;
        await using var context = App.OpenFreshDatabase();
        await new global::AniT.Infrastructure.LibraryReviewService(context).SetPreferredAsync(id);
        StatusText.Text = "✓ Versão preferida atualizada";
        await LoadAsync();
    }

    private async void Correct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Guid id }) return;
        var correction = new CorrectAssociationWindow(id) { Owner = this };
        if (correction.ShowDialog() is true)
        {
            StatusText.Text = "✓ Associação corrigida";
            await LoadAsync();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record MediaVersionItem(Guid Id, string Label, string Path, bool IsPreferred, bool IsAvailable, bool IsDuplicate)
{
    public string Status => IsDuplicate ? "Duplicata física" : !IsAvailable ? "Indisponível" : IsPreferred ? "● Preferida" : "Disponível";
    public string StatusColor => !IsAvailable ? "#F0A56B" : IsPreferred ? "#64DBB2" : "#82A7CD";
    public string ActionLabel => IsPreferred ? "Preferida" : "Usar como padrão";
    public bool CanPrefer => IsAvailable && !IsPreferred && !IsDuplicate;
}
