using AniT.Core;
using AniT.Infrastructure;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AniT.App;

internal static class GlobalSearchController
{
    private static readonly GlobalSearchService SearchService = new(App.OpenFreshDatabase);
    private static readonly ConditionalWeakTable<TextBox, SearchState> States = new();

    public static void Attach(Window owner, TextBox textBox, TextBlock hint, FrameworkElement placementTarget)
    {
        if (States.TryGetValue(textBox, out _)) return;

        var view = new GlobalSearchResultsView();
        var popup = new Popup
        {
            AllowsTransparency = true,
            Placement = PlacementMode.Bottom,
            PlacementTarget = placementTarget,
            VerticalOffset = 8,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
            Child = view
        };
        var state = new SearchState(owner, textBox, hint, placementTarget, popup, view);
        States.Add(textBox, state);

        void UpdateWidth() => view.Width = Math.Max(420, placementTarget.ActualWidth);
        placementTarget.SizeChanged += (_, _) => UpdateWidth();
        owner.Loaded += (_, _) => UpdateWidth();
        owner.Deactivated += (_, _) => popup.IsOpen = false;
        owner.Closed += (_, _) => state.Dispose();
        textBox.TextChanged += state.TextChanged;
        textBox.PreviewKeyDown += state.PreviewKeyDown;
        view.ResultInvoked += state.ResultInvoked;
    }

    private sealed class SearchState(
        Window owner,
        TextBox textBox,
        TextBlock hint,
        FrameworkElement placementTarget,
        Popup popup,
        GlobalSearchResultsView view) : IDisposable
    {
        private CancellationTokenSource? searchCancellation;
        private int requestVersion;

        public async void TextChanged(object sender, TextChangedEventArgs e)
        {
            hint.Visibility = string.IsNullOrEmpty(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            searchCancellation = null;

            var query = textBox.Text.Trim();
            if (global::AniT.Core.AnimeTitleNormalizer.Normalize(query).Length < 2)
            {
                popup.IsOpen = false;
                return;
            }

            var version = ++requestVersion;
            var cancellation = new CancellationTokenSource();
            searchCancellation = cancellation;
            view.ShowLoading();
            view.Width = Math.Max(420, placementTarget.ActualWidth);
            popup.IsOpen = true;

            try
            {
                await Task.Delay(180, cancellation.Token);
                var results = await SearchService.SearchAsync(query, cancellationToken: cancellation.Token);
                if (version != requestVersion || cancellation.IsCancellationRequested) return;
                view.SetResults(results);
            }
            catch (OperationCanceledException)
            {
                // A newer query replaced this one.
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"AniT global search failed: {exception}");
                if (version == requestVersion) view.ShowError();
            }
        }

        public void PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && popup.IsOpen)
            {
                popup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (!popup.IsOpen) return;
            if (e.Key == Key.Down)
            {
                view.MoveSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                view.MoveSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                view.InvokeSelected();
                e.Handled = true;
            }
        }

        public void ResultInvoked(object? sender, GlobalSearchResult result)
        {
            popup.IsOpen = false;
            switch (result.Kind)
            {
                case GlobalSearchResultKind.Anime when result.AnimeId is Guid animeId:
                    AppNavigation.OpenAnimeDetails(owner, animeId);
                    break;
                case GlobalSearchResultKind.Episode when result.AnimeId is Guid episodeAnimeId:
                    AppNavigation.OpenAnimeDetails(owner, episodeAnimeId, result.EpisodeId);
                    break;
                case GlobalSearchResultKind.Genre when !string.IsNullOrWhiteSpace(result.GenreName):
                    AppNavigation.Explore(owner, result.GenreName);
                    break;
            }
        }

        public void Dispose()
        {
            searchCancellation?.Cancel();
            searchCancellation?.Dispose();
            popup.IsOpen = false;
            textBox.TextChanged -= TextChanged;
            textBox.PreviewKeyDown -= PreviewKeyDown;
            view.ResultInvoked -= ResultInvoked;
        }
    }
}
