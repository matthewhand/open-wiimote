using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OpenWiimote.Core.Storage;

namespace OpenWiimote.App;

public partial class App : Application
{

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogException(exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(args.Exception);
            args.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            LogException(args.Exception);
            args.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void LogException(Exception exception)
    {
        try
        {
            var directory = AppDataPaths.ResolveOpenWiimoteDirectory();
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "error.log"),
                $"[{DateTime.Now:O}]\n{exception}\n\n");
        }
        catch
        {
        }
    }
}
