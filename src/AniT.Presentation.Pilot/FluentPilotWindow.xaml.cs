using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace AniT.Presentation.Pilot;

public partial class FluentPilotWindow : Window
{
    private bool ready;
    private readonly ThemesDictionary theme = new();
    public PilotDraft Draft { get; } = new();
    public PilotOptions Options { get; private set; } = new();
    public ContentDialog? CurrentDialog { get; private set; }

    public FluentPilotWindow()
    {
        Resources.MergedDictionaries.Add(theme);

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AniT.Presentation.Pilot;component/PilotTokens.xaml", UriKind.Relative) });
        InitializeComponent();
        DataContext = this;
        ThemePicker.ItemsSource = Enum.GetValues<PilotTheme>();
        InterfacePicker.ItemsSource = new[] { 80, 100, 125 };
        TextPicker.ItemsSource = new[] { 85, 100, 140 };
        RadiusPicker.ItemsSource = new[] { 0, 16, 32 };
        RendererPicker.ItemsSource = Enum.GetValues<PilotRenderer>();
        ready = true;
        ApplyOptions(new());
        SourceInitialized += (_, _) => ApplyRenderer();
        Closed += (_, _) => CurrentDialog?.Hide();
    }

    public void ApplyOptions(PilotOptions options)
    {
        Options = options;
        ready = false;
        ThemePicker.SelectedItem = options.Theme;
        InterfacePicker.SelectedItem = options.InterfaceScale;
        TextPicker.SelectedItem = options.TextScale;
        RadiusPicker.SelectedItem = options.Radius;
        RendererPicker.SelectedItem = options.Renderer;
        MotionSwitch.IsChecked = options.ReduceMotion;
        ready = true;
        var dark = options.Theme == PilotTheme.Dark || options.Theme == PilotTheme.System && ApplicationThemeManager.GetSystemTheme().ToString() is "Dark" or "CapturedMotion" or "Glow";
        theme.Theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        string[] roles = ["Background", "Surface", "Raised", "Text", "Muted", "Border", "Accent", "Error"];
        string[] colors = dark
            ? ["#0C1017", "#171E29", "#1E2735", "#EDF2F8", "#A7B5C7", "#364256", "#77B5FF", "#FF9AAA"]
            : ["#F3F5F9", "#FFFFFF", "#E7EDF5", "#182337", "#4B5D73", "#B5C2D3", "#175CB4", "#A92336"];
        for (var i = 0; i < roles.Length; i++) Resources[$"Pilot.{roles[i]}"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed", "AccentControlElevationBorderBrush" }) Resources[key] = Resources["Pilot.Accent"];
        foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver" }) Resources[key] = new SolidColorBrush(dark ? Color.FromRgb(8,20,39) : Colors.White);
        Resources["Pilot.Radius"] = new CornerRadius(options.Radius);
        Resources["ControlCornerRadius"] = new CornerRadius(Math.Min(options.Radius, 16));
        foreach (var (role, size) in new[] { ("Title", 28d), ("Section", 19d), ("Body", 14d), ("Caption", 12d) })
            Resources[$"Pilot.{role}Size"] = size * options.TextScale / 100;
        PilotRoot.LayoutTransform = new ScaleTransform(options.InterfaceScale / 100d, options.InterfaceScale / 100d);
        if (options.ReduceMotion) MotionSwitch.Template = (ControlTemplate)FindResource("Pilot.StaticToggle");
        else MotionSwitch.ClearValue(Control.TemplateProperty);
        ApplyRenderer();
        AdaptSidebar();
    }

    private void ApplyRenderer()
    {
        if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is { } target)
            target.RenderMode = Options.Renderer == PilotRenderer.Software ? RenderMode.SoftwareOnly : RenderMode.Default;
    }

    private void Options_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ready) ApplyOptions(Options with { Theme = (PilotTheme)ThemePicker.SelectedItem, InterfaceScale = (int)InterfacePicker.SelectedItem, TextScale = (int)TextPicker.SelectedItem, Radius = (int)RadiusPicker.SelectedItem, Renderer = (PilotRenderer)RendererPicker.SelectedItem });
    }
    private void Motion_Changed(object sender, RoutedEventArgs e) { if (ready) ApplyOptions(Options with { ReduceMotion = MotionSwitch.IsChecked == true }); }
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => AdaptSidebar();
    private void AdaptSidebar()
    {
        if (Sidebar is null) return;
        var compact = ActualWidth / (Options.InterfaceScale / 100d) < 900;
        Sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarWidth.Width = new GridLength(compact ? 0 : 210);
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Draft.Validate()) ActionStatus.Text = $"Prévia atualizada: {Draft.Name.Trim()}";
        else CollectionName.Focus();
    }
    private async void Dialog_Click(object sender, RoutedEventArgs e) => await OpenDialogAsync();
    public async Task<ContentDialogResult> OpenDialogAsync()
    {
        if (CurrentDialog is not null) return ContentDialogResult.None;
        var previous = Keyboard.FocusedElement;
        var dialog = new ContentDialog(DialogHost)
        {
            Title = "Uma pausa para sua próxima história",
            Content = new System.Windows.Controls.TextBlock { Text = "Esta é uma prévia de diálogo. Sua biblioteca e seus perfis continuam intactos.", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Entendi", CloseButtonText = "Voltar", DefaultButton = ContentDialogButton.Primary,
            DialogMaxWidth = 480, DialogMargin = new Thickness(16)
        };
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        CurrentDialog = dialog;
        dialog.Loaded += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => { dialog.ApplyTemplate(); if (dialog.Template.FindName("PrimaryButton", dialog) is UIElement button) Keyboard.Focus(button); }));
        try { return await dialog.ShowAsync(); }
        finally { CurrentDialog = null; if (previous is UIElement element && element.IsEnabled) element.Focus(); }
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CurrentDialog is null) return;
        if (e.Key == Key.Escape) { CurrentDialog.Hide(); e.Handled = true; }
    }
}
