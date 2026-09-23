using Microsoft.UI.Xaml;
using Temizlikci.Presentation.Main;
using Temizlikci.Services.Volumes;

namespace Temizlikci.App;

/// <summary>The composition root: the only place that creates services and hands them to view models.</summary>
public partial class App : Application
{
    private MainWindow? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var volumes = new SystemVolumeInfo();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var main = new MainViewModel(volumes, home);
        window = new MainWindow(main);
        window.Activate();
    }
}
