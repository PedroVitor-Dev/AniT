using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AniT.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void AddLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Selecione a pasta que contém seus animes" };
        if (dialog.ShowDialog() is not true) return;

        var path = dialog.FolderName;
        if (App.Database.LibraryRoots.Any(root => root.Path == path))
        {
            StatusText.Text = "Essa pasta já faz parte da sua biblioteca.";
            return;
        }

        App.Database.LibraryRoots.Add(new global::AniT.Core.LibraryRoot
        {
            Path = path,
            DisplayName = global::System.IO.Path.GetFileName(path)
        });
        await App.Database.SaveChangesAsync();
        StatusText.Text = "Pasta adicionada. O scanner chegará no próximo marco do MVP.";
    }
}
