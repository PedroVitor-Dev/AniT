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
        ResponsiveWindow.FitToWorkArea(this, 1280, 780);
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

        var root = new global::AniT.Core.LibraryRoot
        {
            Path = path,
            DisplayName = global::System.IO.Path.GetFileName(path)
        };
        App.Database.LibraryRoots.Add(root);
        await App.Database.SaveChangesAsync();
        StatusText.Text = "Lendo seus episódios…";

        try
        {
            var scanner = new global::AniT.Infrastructure.LibraryScanner(App.Database);
            var result = await scanner.ScanAsync(root);
            StatusText.Text = result.EpisodesAdded > 0
                ? $"{result.EpisodesAdded} episódio(s) adicionado(s) à sua biblioteca."
                : result.FilesFound == 0
                    ? "Nenhum vídeo foi encontrado nessa pasta."
                    : "Nenhum episódio novo foi identificado.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Não foi possível analisar a pasta: {exception.Message}";
        }
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        var library = new LibraryWindow { Owner = this };
        library.ShowDialog();
    }
}
