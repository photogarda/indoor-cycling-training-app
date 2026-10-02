using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Trainer.Desktop.Infrastructure;
using Trainer.Desktop.Views;

namespace Trainer.Desktop;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    public static Window? MainWindow { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                ThemeManager.Load();
                Services = AppServices.Create();
                MainWindow = new MainWindow();
            }
            catch (Exception ex)
            {
                MainWindow = new MessageWindow("Cycling Training Planner", $"The training database could not be opened.\n\n{ex.Message}", false);
            }
            desktop.MainWindow = MainWindow;
            desktop.ShutdownRequested += (_, _) => Services?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
