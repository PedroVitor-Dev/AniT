using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AniT.App;

/// <summary>Clips panel content to the inner edge without clipping the border's own stroke.</summary>
internal static class RoundedPanelClip
{
    private static readonly DependencyProperty IsManagedProperty = DependencyProperty.RegisterAttached(
        "IsManaged", typeof(bool), typeof(RoundedPanelClip), new PropertyMetadata(false));

    public static bool IsManaged(Border border) => (bool)border.GetValue(IsManagedProperty);

    public static void Update(Border border)
    {
        border.SetValue(IsManagedProperty, true);
        border.Clip = null;
        border.ClipToBounds = false;
        if (border.Child is not UIElement child || border.ActualWidth <= 0 || border.ActualHeight <= 0) return;

        var stroke = border.BorderThickness;
        var origin = border.TranslatePoint(new Point(stroke.Left, stroke.Top), child);
        var bounds = new Rect(origin, new Size(
            Math.Max(0, border.ActualWidth - stroke.Left - stroke.Right),
            Math.Max(0, border.ActualHeight - stroke.Top - stroke.Bottom)));
        // Home panels have uniform radii. Their inner radius follows the configured corner size.
        var radius = Math.Max(0, border.CornerRadius.TopLeft - Math.Max(stroke.Left, stroke.Top));
        if (child.Clip is RectangleGeometry current && current.Rect == bounds
            && current.RadiusX == radius && current.RadiusY == radius) return;
        child.Clip = new RectangleGeometry(bounds, radius, radius);
    }
}
