using System.Windows;
using System.Windows.Input;

namespace AniT.App;

public static class TheaterNavigation
{
    public static RoutedUICommand OpenCommand { get; } = new("Movie Theater", "MovieTheater", typeof(TheaterNavigation));
    static TheaterNavigation()
    {
        CommandManager.RegisterClassCommandBinding(typeof(Window), new CommandBinding(OpenCommand,
            (sender, _) => { if (sender is Window window) AppNavigation.OpenTheater(window); }));
    }
}
