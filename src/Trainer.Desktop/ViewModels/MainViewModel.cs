using System.Collections.ObjectModel;
using Avalonia.Threading;
using Trainer.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.Integrations;

namespace Trainer.Desktop.ViewModels;

public record NavItem(string Label, string Icon, ViewModelBase Screen);

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Calendar = new CalendarViewModel();
        Nav =
        [
            new("Calendar", "", Calendar),
            new("Races", "", new RacesViewModel()),
            new("Plan overview", "", new PlanOverviewViewModel()),
            new("Activities", "", new ActivitiesViewModel()),
            new("Analysis", "", new AnalysisViewModel()),
            new("Settings", "", new SettingsViewModel()),
        ];
        foreach (var n in Nav) n.Screen.Shell = this;

        App.Services.Trainer.Changed += (_, _) => Dispatcher.UIThread.Post(ReloadCurrent);
        App.Services.FolderImported += (_, r) => Dispatcher.UIThread.Post(() => Toast($"Watched folder: {r}"));

        if (App.Services.Trainer.IsFirstRun())
        {
            SelectedNav = Nav[^1];
            FirstRun = true;
        }
        else
        {
            SelectedNav = Nav[0];
        }
    }

    public CalendarViewModel Calendar { get; }
    public string Version { get; } = $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)} · {(OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : "Linux")}";
    public ObservableCollection<NavItem> Nav { get; }

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private ViewModelBase? _current;
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private string? _toastText;
    [ObservableProperty] private bool _firstRun;

    public bool IsBusy => BusyText is not null;

    partial void OnBusyTextChanged(string? value) => OnPropertyChanged(nameof(IsBusy));

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null) Show(value.Screen);
    }

    public void Show(ViewModelBase screen)
    {
        screen.Shell = this;
        screen.Load();
        Current = screen;
    }

    public void OpenWorkout(int id)
    {
        var vm = new WorkoutDetailViewModel(id, Current ?? Calendar);
        Show(vm);
    }

    public void GoBack(ViewModelBase target) => Show(target);

    public void SetBusy(string? text) => BusyText = text;

    /// <summary>Set while a long job (like a Strava history sync) can be stopped from the busy overlay.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCancelBusy))] private CancellationTokenSource? _busyCancel;
    public bool CanCancelBusy => BusyCancel is not null;

    [RelayCommand]
    private void CancelBusy()
    {
        BusyCancel?.Cancel();
        BusyText = "Stopping…";
    }

    private CancellationTokenSource? _toastCts;

    public async void Toast(string text)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        ToastText = text;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cts.Token);
            ToastText = null;
        }
        catch (TaskCanceledException)
        {
        }
    }

    [RelayCommand]
    private void DismissToast() => ToastText = null;

    /// <summary>Sun in dark mode (click for light), moon in light mode (click for dark).</summary>
    public string ThemeIcon => ThemeManager.IsDark ? "☀" : "☾";
    public string ThemeTip => ThemeManager.IsDark ? "Switch to light mode" : "Switch to dark mode";

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(ThemeTip));
    }

    private void ReloadCurrent() => Current?.Load();
}
