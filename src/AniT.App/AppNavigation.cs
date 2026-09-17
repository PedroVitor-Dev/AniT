using System.Windows;
using System.Windows.Controls;

namespace AniT.App;

/// <summary>
/// Keeps the dashboard as the application hub and allows only one maximized
/// destination window at a time. Destination windows do not create another
/// taskbar entry and have their duplicated navigation sidebar collapsed.
/// </summary>
internal static class AppNavigation
{
    private static bool isNavigating;
    private static Window? destinationWindow;

    public static void Home(Window current)
    {
        var dashboard = FindDashboard(current);
        if (destinationWindow is { IsLoaded: true })
            destinationWindow.Close();
        else if (current is not DashboardWindow)
            current.Close();

        if (dashboard is not null)
        {
            if (dashboard.WindowState == WindowState.Minimized)
                dashboard.WindowState = WindowState.Maximized;
            dashboard.Activate();
        }
    }

    public static void Explore(Window current) => OpenDestination(current, static () => new ExploreWindow());
    public static void Calendar(Window current) => OpenDestination(current, static () => new CalendarWindow());
    public static void History(Window current) => OpenDestination(current, static () => new HistoryWindow());
    public static void Profile(Window current) => OpenDestination(current, static () => new ProfileWindow());
    public static void Achievements(Window current) => OpenDestination(current, static () => new AchievementsWindow());

    public static void OpenLibrary(Window current) => OpenDestination(current, static () => new LibraryWindow());

    private static void OpenDestination<TWindow>(Window current, Func<TWindow> createWindow)
        where TWindow : Window
    {
        if (isNavigating) return;

        if (destinationWindow is TWindow { IsLoaded: true } existing)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Maximized;

            existing.Activate();
            return;
        }

        isNavigating = true;
        try
        {
            var dashboard = FindDashboard(current) ?? Application.Current.MainWindow as DashboardWindow;

            // Replacing the previous destination also closes any dialog owned by
            // it, preventing a chain of Explore > Calendar > History windows.
            if (destinationWindow is { IsLoaded: true })
                destinationWindow.Close();

            var next = createWindow();
            next.Owner = dashboard;
            next.ShowInTaskbar = false;
            next.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            next.WindowState = WindowState.Maximized;
            CollapseNavigationSidebar(next);

            destinationWindow = next;
            next.Closed += (_, _) =>
            {
                if (ReferenceEquals(destinationWindow, next))
                    destinationWindow = null;
            };
            next.Show();
            next.Activate();
        }
        finally
        {
            isNavigating = false;
        }
    }

    private static DashboardWindow? FindDashboard(Window window)
    {
        Window? candidate = window;
        while (candidate is not null)
        {
            if (candidate is DashboardWindow dashboard)
                return dashboard;
            candidate = candidate.Owner;
        }

        return null;
    }

    private static void CollapseNavigationSidebar(Window window)
    {
        void Collapse()
        {
            if (window.FindName("SidebarColumn") is ColumnDefinition sidebar)
                sidebar.Width = new GridLength(0);
        }

        window.Loaded += (_, _) => Collapse();
        window.SizeChanged += (_, _) => Collapse();
        Collapse();
    }
}
