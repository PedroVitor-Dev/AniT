using System.Windows;
using System.Windows.Interop;

namespace AniT.App;

/// <summary>Per-window workaround for GPU artifacts on alpha surfaces and rounded panel edges.</summary>
public static class StableWindowRendering
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(StableWindowRendering), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Window window) return;
        window.SourceInitialized -= Window_SourceInitialized;
        if ((bool)args.NewValue) window.SourceInitialized += Window_SourceInitialized;
        Apply(window);
    }

    private static void Window_SourceInitialized(object? sender, EventArgs args)
    { if (sender is Window window) Apply(window); }

    private static void Apply(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source && source.CompositionTarget is not null)
            source.CompositionTarget.RenderMode = GetEnabled(window) ? RenderMode.SoftwareOnly : RenderMode.Default;
    }
}
