namespace AniT.App;

// Transitional aliases preserve each page's search and responsive contracts.
public partial class DashboardWindow
{
    private System.Windows.FrameworkElement SearchContainer => ShellHeader.SearchContainer;
    private System.Windows.Controls.TextBlock SearchHint => ShellHeader.SearchHint;
    private System.Windows.Controls.TextBox SearchBox => ShellHeader.SearchBox;
    private System.Windows.UIElement MyListTopButton => ShellHeader.LibraryButton;
}
