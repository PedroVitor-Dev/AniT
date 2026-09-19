using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AniT.App;

/// <summary>
/// Keeps one primary application page visible. A branded loading surface covers
/// module changes until the destination has completed its first render. The
/// Library is intentionally a reusable secondary window.
/// </summary>
internal static class AppNavigation
{
    private const int MinimumLoadingMilliseconds = 620;
    private static readonly Stack<Window> detailHistory = new();
    private static bool isNavigating;
    private static bool loadingInUse;
    private static LibraryWindow? libraryWindow;
    private static NavigationLoadingWindow? loadingWindow;

    public static void Home(Window current) => OpenRoot(current, static () => new DashboardWindow());
    public static void Explore(Window current) => OpenRoot(current, static () => new ExploreWindow());
    public static void Explore(Window current, string genre) =>
        OpenRoot(current, static () => new ExploreWindow(), window => window.SelectGenre(genre));
    public static void Calendar(Window current) => OpenRoot(current, static () => new CalendarWindow());
    public static void History(Window current) => OpenRoot(current, static () => new HistoryWindow());
    public static void Profile(Window current) => OpenRoot(current, static () => new ProfileWindow());
    public static void Achievements(Window current) => OpenRoot(current, static () => new AchievementsWindow());
    public static void Settings(Window current) => OpenRoot(current, static () => new SystemSettingsWindow());

    internal static async void PrewarmLoadingSurface(Window source)
    {
        if (loadingWindow is { IsLoaded: true } || source.IsLoaded is false) return;

        try
        {
            var loading = new NavigationLoadingWindow(source)
            {
                Owner = source,
                Topmost = false,
                Left = -32000,
                Top = -32000
            };
            loadingWindow = loading;
            loading.Show();
            await loading.Dispatcher.InvokeAsync(loading.UpdateLayout, DispatcherPriority.Loaded);
            await WaitForCompositionFramesAsync(2);

            if (!loadingInUse)
            {
                loading.Hide();
                loading.Topmost = true;
                loading.PrepareFor(source);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Não foi possível pré-aquecer a máscara de navegação: {exception}");
            loadingWindow = null;
        }
    }

    public static void OpenLibrary(Window current)
    {
        if (libraryWindow is { IsLoaded: true } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var library = new LibraryWindow
        {
            Owner = null,
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowState = WindowState.Normal
        };
        library.Closed += (_, _) =>
        {
            if (ReferenceEquals(libraryWindow, library)) libraryWindow = null;
        };
        libraryWindow = library;
        library.Show();
        library.Activate();
    }

    public static async void OpenAnimeDetails(Window requestedCurrent, Guid animeId, Guid? episodeId = null)
    {
        if (isNavigating) return;
        var current = ResolvePrimaryWindow(requestedCurrent);
        if (current is AnimeDetailsWindow)
        {
            OpenRoot(current, () => new AnimeDetailsWindow(animeId, episodeId));
            return;
        }

        isNavigating = true;
        var timer = Stopwatch.StartNew();
        var loading = ShowLoading(current);
        await RevealLoadingSurfaceAsync(loading);
        var details = new AnimeDetailsWindow(animeId, episodeId);
        PrepareAsPrimary(details, current);
        details.ShowInTaskbar = false;
        details.IsHitTestVisible = false;
        details.Closed += (_, _) => RestorePreviousAfterDetailClosed();

        detailHistory.Push(current);
        try
        {
            var destinationReady = WaitForContentRenderedAsync(details);
            ShowAsPrimary(details);
            RevealWhenReady(current, details, loading, timer, destinationReady, () =>
            {
                current.Hide();
                current.ShowInTaskbar = false;
                details.ShowInTaskbar = true;
            }, () =>
            {
                if (detailHistory.TryPeek(out var hidden) && ReferenceEquals(hidden, current)) detailHistory.Pop();
            });
        }
        catch
        {
            if (detailHistory.TryPeek(out var hidden) && ReferenceEquals(hidden, current)) detailHistory.Pop();
            CloseLoading(loading);
            RestoreFailedNavigation(current, details);
            isNavigating = false;
            throw;
        }
    }

    public static async void Back(Window current)
    {
        if (isNavigating) return;
        if (detailHistory.Count == 0)
        {
            Home(current);
            return;
        }

        isNavigating = true;
        var timer = Stopwatch.StartNew();
        var loading = ShowLoading(current);
        await RevealLoadingSurfaceAsync(loading);
        var previous = detailHistory.Pop();
        previous.Opacity = 0;
        previous.ShowInTaskbar = false;
        previous.IsHitTestVisible = false;
        Application.Current.MainWindow = previous;
        RestorePrimaryState(previous, current);
        previous.Show();

        var destinationReady = WaitForRedisplayedWindowAsync(previous);
        RevealWhenReady(current, previous, loading, timer, destinationReady, () =>
        {
            previous.ShowInTaskbar = true;
            current.Close();
        }, () => detailHistory.Push(previous));
    }

    private static async void OpenRoot<TWindow>(Window requestedCurrent, Func<TWindow> createWindow, Action<TWindow>? configure = null)
        where TWindow : Window
    {
        if (isNavigating) return;
        var current = ResolvePrimaryWindow(requestedCurrent);
        if (current is TWindow samePage)
        {
            configure?.Invoke(samePage);
            if (samePage.WindowState == WindowState.Minimized) samePage.WindowState = WindowState.Maximized;
            samePage.Activate();
            return;
        }

        isNavigating = true;
        var timer = Stopwatch.StartNew();
        var loading = ShowLoading(current);
        await RevealLoadingSurfaceAsync(loading);
        var next = createWindow();
        PrepareAsPrimary(next, current);
        configure?.Invoke(next);
        next.ShowInTaskbar = false;
        next.IsHitTestVisible = false;

        try
        {
            var destinationReady = WaitForContentRenderedAsync(next);
            ShowAsPrimary(next);
            RevealWhenReady(current, next, loading, timer, destinationReady, () =>
            {
                next.ShowInTaskbar = true;
                current.Close();
                CloseDetailHistory();
            });
        }
        catch
        {
            CloseLoading(loading);
            RestoreFailedNavigation(current, next);
            isNavigating = false;
            throw;
        }
    }

    private static NavigationLoadingWindow? ShowLoading(Window current)
    {
        try
        {
            loadingInUse = true;
            var loading = loadingWindow is { IsLoaded: true } existing
                ? existing
                : new NavigationLoadingWindow(current);
            loadingWindow = loading;
            loading.Owner = null;
            loading.Topmost = true;
            loading.PrepareFor(current);
            loading.Opacity = 1;
            loading.Show();
            return loading;
        }
        catch (Exception exception)
        {
            // The transition is decorative: a loader failure must never block navigation.
            Debug.WriteLine($"Falha ao exibir a tela de carregamento: {exception}");
            loadingInUse = false;
            return null;
        }
    }

    private static async Task RevealLoadingSurfaceAsync(NavigationLoadingWindow? loading)
    {
        if (loading is null) return;

        try
        {
            await loading.Dispatcher.InvokeAsync(loading.UpdateLayout, DispatcherPriority.Loaded);
            await WaitForCompositionFramesAsync(1);
            loading.Opacity = 1;
            await WaitForCompositionFramesAsync(1);
        }
        catch (Exception exception)
        {
            // The current page remains visible if the decorative surface cannot render.
            Debug.WriteLine($"Falha ao preparar a tela de carregamento: {exception}");
            CloseLoading(loading);
        }
    }

    private static async void RevealWhenReady(
        Window current,
        Window next,
        NavigationLoadingWindow? loading,
        Stopwatch timer,
        Task destinationReady,
        Action completed,
        Action? failed = null)
    {
        try
        {
            // This task belongs to the destination window itself. Global rendering
            // ticks can come exclusively from the animated loader and are not proof
            // that the page underneath has painted its first frame.
            await Task.WhenAny(destinationReady, Task.Delay(TimeSpan.FromSeconds(3)));
            await next.Dispatcher.InvokeAsync(next.UpdateLayout, DispatcherPriority.Loaded);
            await WaitForCompositionFramesAsync(2);
            var remaining = MinimumLoadingMilliseconds - (int)timer.ElapsedMilliseconds;
            if (remaining > 0) await Task.Delay(remaining);
            if (!next.IsLoaded) throw new InvalidOperationException("A página de destino foi fechada antes de terminar o carregamento.");

            // Reveal the fully laid-out page while the opaque loader still covers it,
            // then wait for DWM/WPF to compose real frames before removing the mask.
            next.Opacity = 1;
            await next.Dispatcher.InvokeAsync(next.UpdateLayout, DispatcherPriority.Render);
            await WaitForCompositionFramesAsync(2);

            // Closing the previous HWND may itself make DWM recompose the desktop.
            // Keep the fully opaque mask above both windows during that operation.
            completed();
            next.IsHitTestVisible = true;
            next.Activate();
            await next.Dispatcher.InvokeAsync(next.UpdateLayout, DispatcherPriority.ContextIdle);
            await Task.Delay(80);
            await WaitForCompositionFramesAsync(2);
            FlushDesktopComposition();
            CloseLoading(loading);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Falha na transição de navegação: {exception}");
            failed?.Invoke();
            CloseLoading(loading);
            RestoreFailedNavigation(current, next);
        }
        finally
        {
            isNavigating = false;
        }
    }

    private static void CloseLoading(NavigationLoadingWindow? loading)
    {
        if (loading?.IsLoaded != true) return;

        loading.Hide();
        loadingInUse = false;
        if (Application.Current.MainWindow is { IsLoaded: true } owner
            && !ReferenceEquals(owner, loading))
        {
            loading.Owner = owner;
        }
    }

    private static async Task WaitForCompositionFramesAsync(int frameCount)
    {
        for (var frame = 0; frame < frameCount; frame++)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler? onRendering = null;
            onRendering = (_, _) =>
            {
                CompositionTarget.Rendering -= onRendering;
                completion.TrySetResult();
            };
            CompositionTarget.Rendering += onRendering;
            await completion.Task;
        }
    }

    private static Task WaitForContentRenderedAsync(Window window)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? onContentRendered = null;
        onContentRendered = (_, _) =>
        {
            window.ContentRendered -= onContentRendered;
            completion.TrySetResult();
        };
        window.ContentRendered += onContentRendered;
        return completion.Task;
    }

    private static async Task WaitForRedisplayedWindowAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Loaded);
        await WaitForCompositionFramesAsync(2);
    }

    private static void FlushDesktopComposition()
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(6)) _ = DwmFlush();
        }
        catch (Exception exception)
        {
            // DWM synchronization is an extra visual guarantee, never a navigation requirement.
            Debug.WriteLine($"Não foi possível sincronizar a composição da transição: {exception}");
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmFlush();

    private static Window ResolvePrimaryWindow(Window requested)
    {
        if (requested is LibraryWindow
            && Application.Current.MainWindow is { IsLoaded: true } primary
            && !ReferenceEquals(primary, requested))
        {
            return primary;
        }

        return requested;
    }

    private static void PrepareAsPrimary(Window next, Window current)
    {
        next.Owner = null;
        next.Opacity = 0;
        next.WindowStartupLocation = WindowStartupLocation.Manual;

        var bounds = current.WindowState == WindowState.Normal
            ? new Rect(current.Left, current.Top, current.Width, current.Height)
            : current.RestoreBounds;
        if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
        {
            next.Left = bounds.Left;
            next.Top = bounds.Top;
            next.Width = bounds.Width;
            next.Height = bounds.Height;
        }

        next.WindowState = current.WindowState == WindowState.Minimized ? WindowState.Maximized : current.WindowState;
    }

    private static void ShowAsPrimary(Window window)
    {
        Application.Current.MainWindow = window;
        window.Show();
    }

    private static void RestoreFailedNavigation(Window current, Window next)
    {
        if (next.IsLoaded) next.Close();
        current.Opacity = 1;
        current.IsHitTestVisible = true;
        current.ShowInTaskbar = true;
        if (!current.IsVisible) current.Show();
        Application.Current.MainWindow = current;
        current.Activate();
    }

    private static void RestorePrimaryState(Window previous, Window current)
    {
        previous.WindowState = current.WindowState == WindowState.Minimized ? WindowState.Maximized : current.WindowState;
    }

    private static void RestorePreviousAfterDetailClosed()
    {
        if (isNavigating
            || detailHistory.Count == 0
            || Application.Current.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        var previous = detailHistory.Pop();
        previous.IsHitTestVisible = true;
        previous.ShowInTaskbar = true;
        Application.Current.MainWindow = previous;
        previous.Show();
        previous.Activate();
    }

    private static void CloseDetailHistory()
    {
        while (detailHistory.TryPop(out var hidden)) hidden.Close();
    }
}
