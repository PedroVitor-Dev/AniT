using System.Windows;

namespace AniT.App;

internal static class ResponsiveWindow
{
    private const double ScreenMargin = 28;

    public static void FitToWorkArea(Window window, double preferredWidth, double preferredHeight)
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(480, workArea.Width - ScreenMargin);
        var availableHeight = Math.Max(360, workArea.Height - ScreenMargin);

        window.MinWidth = Math.Min(window.MinWidth, availableWidth);
        window.MinHeight = Math.Min(window.MinHeight, availableHeight);
        // Limit only the initial size. Capping MaxWidth/MaxHeight here leaves a
        // visible ScreenMargin strip even after the user maximizes the window.
        window.MaxWidth = double.PositiveInfinity;
        window.MaxHeight = double.PositiveInfinity;
        window.Width = Math.Min(preferredWidth, availableWidth);
        window.Height = Math.Min(preferredHeight, availableHeight);
    }
}
