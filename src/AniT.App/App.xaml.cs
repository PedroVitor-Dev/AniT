using System.Configuration;
using System.Data;
using System.Windows;

namespace AniT.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static global::AniT.Infrastructure.AniTDbContext Database { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = global::System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AniT", "Data");
        Database = global::AniT.Infrastructure.AniTDatabase.Create(global::System.IO.Path.Combine(dataDirectory, "anit.db"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Database?.Dispose();
        base.OnExit(e);
    }
}

