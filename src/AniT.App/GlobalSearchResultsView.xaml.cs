using AniT.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AniT.App;

public partial class GlobalSearchResultsView : UserControl
{
    public ObservableCollection<GlobalSearchResult> Results { get; } = [];
    public event EventHandler<GlobalSearchResult>? ResultInvoked;

    public GlobalSearchResultsView()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void ShowLoading()
    {
        ResultCountText.Text = string.Empty;
        StatusGlyph.Text = "✦";
        StatusText.Text = "Buscando na sua biblioteca…";
        StatusPanel.Visibility = Visibility.Visible;
        ResultsList.Visibility = Visibility.Collapsed;
    }

    public void ShowError()
    {
        ResultCountText.Text = string.Empty;
        StatusGlyph.Text = "!";
        StatusText.Text = "Não foi possível concluir a busca.";
        StatusPanel.Visibility = Visibility.Visible;
        ResultsList.Visibility = Visibility.Collapsed;
    }

    public void SetResults(IEnumerable<GlobalSearchResult> results)
    {
        Results.Clear();
        foreach (var result in results) Results.Add(result);

        ResultCountText.Text = Results.Count == 0 ? string.Empty : $"{Results.Count} encontrado{(Results.Count == 1 ? string.Empty : "s")}";
        StatusGlyph.Text = "⌕";
        StatusText.Text = "Nenhum anime, episódio ou gênero encontrado.";
        StatusPanel.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = Results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResultsList.SelectedIndex = Results.Count == 0 ? -1 : 0;
    }

    public void MoveSelection(int direction)
    {
        if (Results.Count == 0) return;
        var current = Math.Max(0, ResultsList.SelectedIndex);
        ResultsList.SelectedIndex = Math.Clamp(current + direction, 0, Results.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    public bool InvokeSelected()
    {
        if (ResultsList.SelectedItem is not GlobalSearchResult result) return false;
        ResultInvoked?.Invoke(this, result);
        return true;
    }

    private void ResultsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ResultsList, e.OriginalSource as DependencyObject) is ListBoxItem item
            && item.DataContext is GlobalSearchResult result)
        {
            ResultInvoked?.Invoke(this, result);
        }
    }
}
