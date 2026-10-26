using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Wpf.Ui.Controls;


namespace AniT.Presentation.Pilot;

public partial class FluentPilotWindow : Window, IAppearanceHost
{
    private bool ready;
    private string accent = "#77B5FF";
    private readonly CinematicTheme visualTheme;
    public PilotDraft Draft { get; } = new();
    public PilotOptions Options { get; private set; } = new();
    private readonly CinematicDialog dialogs;
    public ContentDialog? CurrentDialog => dialogs.Current;

    public FluentPilotWindow()
    {
        visualTheme = new CinematicTheme(this);
        InitializeComponent();
        dialogs = new CinematicDialog(this, DialogHost);
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
        visualTheme.Apply(new AppearanceOptions((VisualTheme)options.Theme, accent, options.InterfaceScale, options.TextScale, options.Radius, options.ReduceMotion));
        ApplyRenderer();
        AdaptSidebar();
    }

    public void ApplyAppearance(AppearanceOptions options)
    {
        accent = options.Accent;
        ApplyOptions(Options with { Theme = (PilotTheme)options.Theme, InterfaceScale = options.InterfaceScale, TextScale = options.TextScale, Radius = options.Radius, ReduceMotion = options.ReduceMotion });
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
    public Task<ContentDialogResult> OpenDialogAsync() => dialogs.ShowAsync(
        "Uma pausa para sua próxima história",
        "Esta é uma prévia de diálogo. Sua biblioteca e seus perfis continuam intactos.");
}