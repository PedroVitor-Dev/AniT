using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AniT.Presentation;

/// <summary>Modal keyboard/focus contract for simple informational or confirmation dialogs.</summary>
public sealed class CinematicDialog(Window owner, ContentDialogHost host)
{
    public ContentDialog? Current { get; private set; }
    public async Task<ContentDialogResult> ShowAsync(string title, string message, string primaryText = "Entendi", string closeText = "Voltar")
    {
        if (Current is not null) return ContentDialogResult.None;
        var previous = Keyboard.FocusedElement;
        var body = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        body.SetResourceReference(FrameworkElement.StyleProperty, "AniT.Body");
        var dialog = new ContentDialog(host)
        {
            Title = title, Content = body, PrimaryButtonText = primaryText, CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary, DialogMaxWidth = 480, DialogMargin = new Thickness(16)
        };
        dialog.SetResourceReference(Control.FontSizeProperty, "AniT.BodySize");
        FocusManager.SetIsFocusScope(dialog, true);
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        Current = dialog;
        void FocusPrimary() { if (Current == dialog) Focus(Action("PrimaryButton")); }
        dialog.Loaded += (_, _) => owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusPrimary));
        owner.PreviewKeyDown += OnKeyDown;
        owner.Closed += OnClosed;
        try { return await dialog.ShowAsync(); }
        finally
        {
            owner.PreviewKeyDown -= OnKeyDown;
            owner.Closed -= OnClosed;
            Current = null;
            if (previous is UIElement element && element.IsEnabled && element.IsVisible) Keyboard.Focus(element);
        }
    }

    private Control? Action(string name)
    {
        Current?.ApplyTemplate();
        return Current?.Template.FindName(name, Current) as Control;
    }
    private void Focus(Control? control)
    {
        if (control is not { IsEnabled: true, IsVisible: true } || Current is null) return;
        FocusManager.SetFocusedElement(Current, control);
        Keyboard.Focus(control);
    }
    private void OnClosed(object? sender, EventArgs e) => Current?.Hide();
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Current is null) return;
        if (e.Key == Key.Escape) { Current.Hide(); e.Handled = true; }
        else if (e.Key == Key.Tab)
        {
            var primary = Action("PrimaryButton"); var close = Action("CloseButton");
            var focused = Keyboard.FocusedElement;
            var next = ReferenceEquals(focused, primary) ? close : primary;
            Focus(next is { IsEnabled: true } ? next : close); e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ReferenceEquals(Keyboard.FocusedElement, Action("CloseButton"))) Current.Hide();
            else if (Current.IsPrimaryButtonEnabled) Current.Hide(ContentDialogResult.Primary);
            e.Handled = true;
        }
    }
}
