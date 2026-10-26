using System.Windows;

namespace AniT.Presentation;

/// <summary>Inherited boundary: legacy appearance traversal must not mutate owned components.</summary>
public static class AppearanceScope
{
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.RegisterAttached(
        "ReduceMotion", typeof(bool), typeof(AppearanceScope), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetReduceMotion(DependencyObject element) => (bool)element.GetValue(ReduceMotionProperty);
    public static void SetReduceMotion(DependencyObject element, bool value) => element.SetValue(ReduceMotionProperty, value);
    public static readonly DependencyProperty IsManagedProperty = DependencyProperty.RegisterAttached(
        "IsManaged", typeof(bool), typeof(AppearanceScope), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsManaged(DependencyObject element) => (bool)element.GetValue(IsManagedProperty);
    public static void SetIsManaged(DependencyObject element, bool value) => element.SetValue(IsManagedProperty, value);
}
