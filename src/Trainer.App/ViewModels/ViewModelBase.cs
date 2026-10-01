using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Trainer.App.Infrastructure;
using Trainer.Data.Services;

namespace Trainer.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected static AppServices S => App.Services;
    protected static TrainerService Trainer => App.Services.Trainer;

    /// <summary>Shell, set when the screen is shown.</summary>
    public MainViewModel? Shell { get; set; }

    public abstract string Title { get; }

    /// <summary>Reload from the database. Called when shown and after any change.</summary>
    public abstract void Load();

    /// <summary>Runs an action with the busy indicator and turns expected failures into a message.</summary>
    protected async Task RunAsync(Func<Task> action, string? busy = null)
    {
        Shell?.SetBusy(busy);
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is TrainerValidationException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or InvalidDataException or System.Net.Http.HttpRequestException
                                       or TimeoutException or FormatException)
        {
            Dialogs.Error(ex.Message);
        }
        finally
        {
            Shell?.SetBusy(null);
        }
    }

    protected void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is TrainerValidationException or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or FormatException)
        {
            Dialogs.Error(ex.Message);
        }
    }
}
