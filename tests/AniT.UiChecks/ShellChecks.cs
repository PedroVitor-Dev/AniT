using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;

internal static class ShellChecks
{
    public static Window Preview()
    {
        var sidebar = new CinematicSidebar { IsPreview = true, Section = "Home" };
        var header = new CinematicHeader { IsPreview = true };
        header.ActionRequested += (_, _) => { };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRowSpan(sidebar, 2); Grid.SetColumn(header, 1);
        var body = new TextBlock { Text = "Sua próxima história\n\nPrévia isolada do shell", Foreground = Brushes.White, FontSize = 28, Margin = new Thickness(32), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(body, 1); Grid.SetRow(body, 1);
        grid.Children.Add(sidebar); grid.Children.Add(header); grid.Children.Add(body);
        var window = new Window { Title = "AniT · Prévia do shell", Width = 1280, Height = 800, MinWidth = 700, MinHeight = 420, Content = grid };
        NameScope.SetNameScope(window, new NameScope());
        window.RegisterName("ShellHeader", header);
        window.Tag = (sidebar, header);
        StableWindowRendering.SetEnabled(window, true);
        return window;
    }

    public static void Run(Application app)
    {
        AppearanceManager.Initialize(app, AniTSystemSettings.Default);
        var window = Preview(); window.ShowActivated = false; window.ShowInTaskbar = false;
        var (sidebar, header) = ((CinematicSidebar, CinematicHeader))window.Tag;
        var textEvents = 0; var submissions = 0;
        header.SearchChanged += (_, _) => textEvents++;
        header.SearchSubmitted += (_, e) => { if (e.Key == Key.Enter) submissions++; };
        header.SearchBox.Text = "Black Lagoon";
        window.Show(); Pump();
        var checks = 0;
        foreach (var theme in Enum.GetValues<AppearanceThemeMode>())
        foreach (var scale in new[] { 80, 100, 125 })
        foreach (var text in new[] { 85, 100, 140 })
        foreach (var size in new[] { (1060d,700d),(1280d,720d),(1920d,1080d),(3440d,1440d) })
        {
            window.Width = size.Item1; window.Height = size.Item2;
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode=theme,InterfaceScalePercent=scale,TextScalePercent=text });
            window.UpdateLayout(); Pump();
            Require(header.SearchBox.Text == "Black Lagoon", "Query survives resize and appearance changes");
            Require(header.SearchContainer.IsVisible && header.SearchContainer.ActualWidth >= 120, "Search remains reachable");
            Require(header.SearchBox.ActualHeight >= header.SearchBox.FontSize, "Search text has vertical space");
            Require(((FrameworkElement)header.Content).LayoutTransform.Value.IsIdentity, "No second interface scale inside header");
            Require(Math.Abs(header.SearchBox.FontSize-14*text/100d)<.01, "Header text follows tokens once");
            var settings = Descendants(sidebar).OfType<RadioButton>().Single(b => Equals(b.Tag,"Settings"));
            settings.BringIntoView(); window.UpdateLayout();
            Require(settings.ActualHeight >= 44 && settings.IsEnabled, "Settings stays reachable in sidebar scroll area");
            foreach (var path in Descendants(sidebar).OfType<System.Windows.Shapes.Path>()) Require(path.Data is not null, "Navigation icon resolves");
            checks++;
        }
        Require(ShortcutManager.FocusSearch(window) && header.SearchBox.SelectionLength == header.SearchBox.Text.Length, "Configured search shortcut reaches the new header");
        Require(textEvents == 1, "Resize does not replace or clear text");
        header.SearchBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        Require(submissions == 1, "Page search handler receives Enter");
        var requests = new List<string>(); sidebar.NavigationRequested += (_, destination) => requests.Add(destination);
        foreach (var button in Descendants(sidebar).OfType<RadioButton>()) button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(requests.Count == 8 && requests.Contains("Library") && requests.Contains("Settings"), "All destinations exposed");
        Require(sidebar.Section == "Home" && Descendants(sidebar).OfType<RadioButton>().Single(b=>Equals(b.Tag,"Home")).IsChecked == true, "Secondary Library does not replace current selection");
        Require(header.SearchBox.FocusVisualStyle is not null, "Search keyboard focus style available");
        window.Close();
        Console.WriteLine($"PASS: {checks} shell theme/scale/text/viewport combinations; query retention, search forwarding, partial appearance, eight destinations and navigation selection.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item; }
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
