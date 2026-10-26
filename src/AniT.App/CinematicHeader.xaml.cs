using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AniT.Presentation;

namespace AniT.App;

public partial class CinematicHeader : UserControl, IAppearanceHost
{
    private readonly CinematicTheme theme;
    public TextBox SearchBox => HeaderSearchBox;
    public TextBlock SearchHint => HeaderSearchHint;
    public FrameworkElement SearchContainer => SearchSurface;
    public UIElement LibraryButton => HeaderLibrary;
    public bool IsPreview { get; set; }
    public static readonly DependencyProperty ActionLabelProperty = DependencyProperty.Register(nameof(ActionLabel), typeof(string), typeof(CinematicHeader), new PropertyMetadata("Continuar", (d,e) => { if (((CinematicHeader)d).HeaderAction is { } button) button.Content = e.NewValue; }));
    public string ActionLabel { get => (string)GetValue(ActionLabelProperty); set => SetValue(ActionLabelProperty, value); }
    public bool ShowSearch { get => SearchSurface.Visibility == Visibility.Visible; set => SearchSurface.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public bool ShowAction { get => HeaderAction.Visibility == Visibility.Visible; set => HeaderAction.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public event TextChangedEventHandler? SearchChanged;
    public event KeyEventHandler? SearchSubmitted;
    public event RoutedEventHandler? HomeRequested;
    public event RoutedEventHandler? ActionRequested;
    public CinematicHeader()
    {
        theme = new CinematicTheme(this, scaleInterface: false);
        InitializeComponent();
        Loaded += (_, _) => RefreshAvatar();
        IsVisibleChanged += (_, e) => { if (e.NewValue is true && IsLoaded) RefreshAvatar(); };
        SizeChanged += (_, _) => Adapt();
    }
    public void ApplyAppearance(AppearanceOptions options) { theme.Apply(options); Adapt(); }
    private void Adapt()
    {
        if (HeaderGrid is null) return;
        // Search remains available; optional duplicate links yield space first.
        HeaderHome.Visibility = ActualWidth < 820 ? Visibility.Collapsed : Visibility.Visible;
        HeaderLibrary.Visibility = ActualWidth < 720 ? Visibility.Collapsed : Visibility.Visible;
        HeaderAction.Visibility = ActionRequested is null || ActualWidth < 580 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void RefreshAvatar()
    {
        if (IsPreview) return;
        var path = ProfileSettingsStore.Load().AvatarPath;
        AvatarBrush.ImageSource = ComfortableImageSource.Load("Assets/Profile/1.png", 128);
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { AvatarBrush.ImageSource = ComfortableImageSource.Load(Path.GetFullPath(path), 128); }
            catch { /* Keep the packaged avatar when a local picture is unavailable. */ }
        }
    }
    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HeaderSearchHint is not null) HeaderSearchHint.Visibility = string.IsNullOrEmpty(HeaderSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        SearchChanged?.Invoke(sender, e);
    }
    private void Search_KeyDown(object sender, KeyEventArgs e) => SearchSubmitted?.Invoke(sender, e);
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        if (HomeRequested is not null) HomeRequested(sender, e);
        else if (!IsPreview && Window.GetWindow(this) is { } owner && owner is not DashboardWindow) AppNavigation.Home(owner);
    }
    private void Action_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(sender, e);
    private void Library_Click(object sender, RoutedEventArgs e) { if (!IsPreview && Window.GetWindow(this) is { } owner) AppNavigation.OpenLibrary(owner); }
    private void Profile_Click(object sender, RoutedEventArgs e) { if (!IsPreview && Window.GetWindow(this) is { } owner) AppNavigation.Profile(owner); }
}
