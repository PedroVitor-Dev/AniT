using System;
using System.Windows;
using System.Windows.Controls;
using AniT.Presentation;

namespace AniT.App;

public partial class CinematicSidebar : UserControl, IAppearanceHost
{
    private readonly CinematicTheme theme;
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(nameof(Section), typeof(string), typeof(CinematicSidebar), new PropertyMetadata("Home", (d, _) => ((CinematicSidebar)d).SelectCurrent()));
    public string Section { get => (string)GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public bool IsPreview { get; set; }
    public bool UseBackForHome { get; set; }
    public event EventHandler<string>? NavigationRequested;
    public CinematicSidebar()
    {
        theme = new CinematicTheme(this, scaleInterface: false);
        InitializeComponent();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AniT;component/Styles/ShellIcons.xaml", UriKind.Relative) });
        SelectCurrent();
        SizeChanged += (_, _) => Identity.Visibility = ActualHeight < 650 ? Visibility.Collapsed : Visibility.Visible;
    }
    public void ApplyAppearance(AppearanceOptions options)
    {
        theme.Apply(options);
        if (Parent is Grid grid && Grid.GetColumn(this) < grid.ColumnDefinitions.Count)
            grid.ColumnDefinitions[Grid.GetColumn(this)].MinWidth = Math.Max(184, 184 * options.TextScale / 100d);
    }
    private void SelectCurrent()
    {
        if (NavigationItems is null) return;
        foreach (var child in NavigationItems.Children)
            if (child is RadioButton item) item.IsChecked = Equals(item.Tag, Section);
    }
    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string destination }) return;
        NavigationRequested?.Invoke(this, destination);
        SelectCurrent(); // Selection describes the displayed page; Library opens separately.
        if (IsPreview || Window.GetWindow(this) is not { } owner) return;
        if (destination == Section && destination != "Library") return;
        switch (destination)
        {
            case "Home": if (UseBackForHome) AppNavigation.Back(owner); else AppNavigation.Home(owner); break;
            case "Explore": AppNavigation.Explore(owner); break;
            case "Library": AppNavigation.OpenLibrary(owner); break;
            case "Calendar": AppNavigation.Calendar(owner); break;
            case "History": AppNavigation.History(owner); break;
            case "Achievements": AppNavigation.Achievements(owner); break;
            case "Profile": AppNavigation.Profile(owner); break;
            case "Settings": AppNavigation.Settings(owner); break;
        }
    }
}
