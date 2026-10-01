using System.Windows;
using System.Windows.Threading;
using Trainer.App.Infrastructure;

namespace Trainer.App;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            Services = AppServices.Create();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The training database could not be opened.\n\n{ex.Message}", "Cycling Training Planner",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
