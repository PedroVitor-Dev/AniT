using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;
using AniT.Presentation;
using AniT.Presentation.Pilot;

internal static class DesignSystemChecks
{
    public static void Run(Application app)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        // Real legacy handlers, injected settings: no user store or App.OnStartup.
        AppearanceManager.Initialize(app, AniTSystemSettings.Default);
        var window = new FluentPilotWindow { Width = 1120, Height = 840 };
        window.Show(); Pump();
        var root = (FrameworkElement)window.Content;
        var text = (TextBlock)window.FindName("NameError");
        var panel = (Border)window.FindName("FormPanel");
        var toggle = (Wpf.Ui.Controls.ToggleSwitch)window.FindName("MotionSwitch");
        Require(AppearanceScope.GetIsManaged(text), "Appearance boundary inherits into descendants");
        var applications = 0;
        foreach (var theme in new[] { AppearanceThemeMode.Dark, AppearanceThemeMode.Light, AppearanceThemeMode.Automatic })
        foreach (var scale in new[] { 80, 100, 125 })
        foreach (var textScale in new[] { 85, 100, 140 })
        foreach (var radius in new[] { 0, 16, 32 })
        foreach (var motion in new[] { false, true })
        {
            var settings = AniTSystemSettings.Default with { ThemeMode = theme, InterfaceScalePercent = scale, TextScalePercent = textScale, CornerRadius = radius, ReduceMotion = motion };
            AppearanceManager.Apply(settings);
            AppearanceManager.ApplyToWindow(window); // Reapplication must not compound or replace expressions.
            window.UpdateLayout();
            Require(root.LayoutTransform is ScaleTransform t && t.ScaleX == scale / 100d && t.ScaleY == scale / 100d, "Single interface scale");
            Require(Math.Abs(text.FontSize - 12d * textScale / 100) < .001, "Single text scale");
            Require(panel.CornerRadius.TopLeft == radius, "Managed radius remains authoritative");
            Require(ReferenceEquals(toggle.Template, window.FindResource("AniT.StaticToggle")) == motion, "Motion preference reaches component style");
            Require(text.ReadLocalValue(TextBlock.FontSizeProperty) == DependencyProperty.UnsetValue, "Dynamic typography resource preserved");
            applications++;
        }
        foreach (var theme in new[] { VisualTheme.Dark, VisualTheme.Light })
        foreach (var accent in new[] { "#77B5FF", "#42C8FF", "#000000", "#FFFFFF", "#FF0000", "#008000", "#FFFF00", "#777777" })
        {
            window.ApplyAppearance(new(theme, accent)); window.UpdateLayout();
            foreach (var foreground in new[] { "Text", "Muted", "Error", "Success", "Warning" })
            foreach (var background in new[] { "Background", "Surface", "Raised", "Sidebar" })
                Require(CinematicTheme.Contrast(Color(window, foreground), Color(window, background)) >= 4.5, $"Text contrast {theme}/{foreground}/{background}");
            foreach (var state in new[] { "Accent", "AccentHover", "AccentPressed" })
                Require(CinematicTheme.Contrast(Color(window, state), Color(window, "OnAccent")) >= 4.5, $"Custom accent text contrast: {state}");
            Require(CinematicTheme.Contrast(Color(window, "Border"), Color(window, "Surface")) >= 3, "Control boundary contrast");
        }
        window.ApplyAppearance(new()); window.UpdateLayout(); Pump();
        var opener = (Control)window.FindName("DialogButton");
        opener.BringIntoView(); window.Activate(); opener.Focus(); Pump();
        var task = window.OpenDialogAsync(); Pump();
        var dialog = window.CurrentDialog!;
        Require(dialog is not null && dialog.IsEnabled, "Modal is enabled");
        var primary = (Control)dialog!.Template.FindName("PrimaryButton", dialog);
        var close = (Control)dialog.Template.FindName("CloseButton", dialog);
        Require(ReferenceEquals(FocusManager.GetFocusedElement(dialog), primary), "Initial logical modal focus");
        SendKey(window, Key.Tab); Pump();
        Require(ReferenceEquals(FocusManager.GetFocusedElement(dialog), close), "Tab moves within modal");
        SendKey(window, Key.Tab); Pump();
        Require(ReferenceEquals(FocusManager.GetFocusedElement(dialog), primary), "Tab cycles within modal");
        SendKey(window, Key.Escape); Complete(task);
        Require(window.CurrentDialog is null && opener.IsEnabled, "Escape closes and restores background");
        task = window.OpenDialogAsync(); Pump(); SendKey(window, Key.Enter); Complete(task);
        Require(task.Result == Wpf.Ui.Controls.ContentDialogResult.Primary, "Enter accepts default action");
        window.Close();
        Require(app.TryFindResource("AniT.Surface") is null, "Design tokens stay window scoped");
        Console.WriteLine($"PASS: {applications} real AppearanceManager applications; dynamic resources, contrast for 16 palettes, modal keyboard contract and scoped resources.");
    }
    private static Color Color(Window window, string role) => ((SolidColorBrush)window.FindResource($"AniT.{role}")).Color;
    private static void SendKey(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Complete(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && timer.ElapsedMilliseconds < 5000) Pump();
        Require(task.IsCompletedSuccessfully, $"Dialog completed: {task.Exception}");
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
