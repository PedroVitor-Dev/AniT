namespace AniT.App;

// Transitional aliases preserve each page's search and responsive contracts.
public partial class HistoryWindow
{
    private System.Windows.FrameworkElement SearchContainer => ShellHeader.SearchContainer;
    private System.Windows.Controls.TextBlock GlobalSearchHint => ShellHeader.SearchHint;
    private System.Windows.Controls.TextBox GlobalSearchBox => ShellHeader.SearchBox;
    private System.Windows.UIElement LibraryTopButton => ShellHeader.LibraryButton;
}
