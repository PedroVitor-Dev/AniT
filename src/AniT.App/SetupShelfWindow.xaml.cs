using System.Windows;

namespace AniT.App;

public partial class SetupShelfWindow : Window
{
    private string? folderPath;

    public SetupShelfWindow() => InitializeComponent();

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Onde estão seus animes?" };
        if (dialog.ShowDialog() is not true) return;
        folderPath = dialog.FolderName;
        FolderText.Text = folderPath;
        FinishButton.IsEnabled = true;
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (folderPath is null) return;
        FinishButton.IsEnabled = false;
        if (App.Database.LibraryRoots.Any(root => root.Path == folderPath))
        {
            StatusText.Text = "Essa pasta já está na sua estante.";
            FinishButton.IsEnabled = true;
            return;
        }
        StatusText.Text = "Organizando episódios…";
        var root = new global::AniT.Core.LibraryRoot { Path = folderPath, DisplayName = System.IO.Path.GetFileName(folderPath) };
        App.Database.LibraryRoots.Add(root);
        await App.Database.SaveChangesAsync();
        var result = await new global::AniT.Infrastructure.LibraryScanner(App.Database).ScanAsync(root);
        StatusText.Text = $"{result.EpisodesAdded} episódio(s) encontrado(s).";
        DialogResult = true;
    }
}
