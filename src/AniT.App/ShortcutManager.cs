using AniT.Infrastructure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AniT.App;

internal static class ShortcutManager
{
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized) return;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        initialized = true;
    }

    public static void Shutdown()
    {
        if (!initialized) return;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        initialized = false;
    }

    public static bool TryParseGesture(string value, out KeyGesture? gesture)
    {
        try
        {
            gesture = new KeyGestureConverter().ConvertFromInvariantString(value) as KeyGesture;
            return gesture is not null;
        }
        catch
        {
            gesture = null;
            return false;
        }
    }

    private static void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is not KeyEventArgs keyEvent || keyEvent.RoutedEvent != Keyboard.PreviewKeyDownEvent || keyEvent.IsRepeat) return;
        var current = Application.Current.Windows.Cast<Window>().FirstOrDefault(window => window.IsActive)
                      ?? Application.Current.MainWindow;
        if (current is null) return;
        var shortcuts = AniTSystemSettingsStore.Load().KeyboardShortcuts ?? AniTSystemSettings.DefaultKeyboardShortcuts;
        foreach (var item in shortcuts)
        {
            if (!TryParseGesture(item.Value, out var gesture) || !gesture!.Matches(null, keyEvent)) continue;
            if (!Execute(item.Key, current)) continue;
            keyEvent.Handled = true;
            return;
        }
    }

    private static bool Execute(string action, Window current)
    {
        switch (action)
        {
            case "Home": AppNavigation.Home(current); return true;
            case "Library": AppNavigation.OpenLibrary(current); return true;
            case "History": AppNavigation.History(current); return true;
            case "Settings": AppNavigation.Settings(current, SettingsSection.Advanced); return true;
            case "Search": return FocusSearch(current);
            default: return false;
        }
    }

    private static bool FocusSearch(Window window)
    {
        foreach (var name in new[] { "SearchBox", "GlobalSearchBox", "LibrarySearchBox", "HistorySearchBox" })
        {
            if (window.FindName(name) is not TextBox search) continue;
            search.Focus();
            search.SelectAll();
            return true;
        }
        return false;
    }
}
