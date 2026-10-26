namespace AniT.App;

// Transitional aliases preserve each page's search and responsive contracts.
public partial class AnimeDetailsWindow
{
    private System.Windows.FrameworkElement SearchContainer => ShellHeader.SearchContainer;
    private System.Windows.Controls.TextBlock SearchHint => ShellHeader.SearchHint;
    private System.Windows.Controls.TextBox SearchBox => ShellHeader.SearchBox;
    private System.Windows.UIElement LibraryTopButton => ShellHeader.LibraryButton;
}
