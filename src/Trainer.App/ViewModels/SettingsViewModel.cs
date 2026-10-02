using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.App.Infrastructure;
using Trainer.Core.Models;
using Trainer.Core.Workouts;
using Trainer.Data.Services;
using Trainer.Integrations.Edge;
using Trainer.Integrations.Zwift;

namespace Trainer.App.ViewModels;

public partial class DayOption(DayOfWeek day, bool selected) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label => Day.ToString()[..3];
    [ObservableProperty] private bool _selected = selected;
}

public record FtpRow(FtpEntry E)
{
    public string Date => E.Date.ToString("d MMM yyyy");
    public string Watts => $"{E.Watts} W";
    public string Method => E.Method switch { FtpMethod.Ramp => "Ramp test", FtpMethod.Estimate => "Estimate", _ => "Entered" };
}

public partial class SettingsViewModel : ViewModelBase
{
    public override string Title => "Settings";

    public IReadOnlyList<AthleteLevel> Levels { get; } = Enum.GetValues<AthleteLevel>();
    public IReadOnlyList<NoRaceGoal> Goals { get; } = Enum.GetValues<NoRaceGoal>();
    public IReadOnlyList<FtpMethod> Methods { get; } = Enum.GetValues<FtpMethod>();
    public IReadOnlyList<WorkoutKind> Kinds { get; } = Enum.GetValues<WorkoutKind>();
    public IReadOnlyList<DayOfWeek> WeekDays { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    // Athlete
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private AthleteLevel _level;
    [ObservableProperty] private double _weeklyHours;
    [ObservableProperty] private DayOfWeek _longRideDay;
    [ObservableProperty] private NoRaceGoal _noRaceGoal;
    [ObservableProperty] private bool _defaultIndoor;
    [ObservableProperty] private string _thresholdHr = "";
    public ObservableCollection<DayOption> Days { get; } = [];

    // FTP
    public ObservableCollection<FtpRow> FtpHistory { get; } = [];
    [ObservableProperty] private FtpRow? _selectedFtp;
    [ObservableProperty] private string _newFtp = "";
    [ObservableProperty] private FtpMethod _newFtpMethod = FtpMethod.Manual;
    [ObservableProperty] private DateTime _newFtpDate = DateTime.Today;
    [ObservableProperty] private string _currentFtpText = "";
    [ObservableProperty] private bool _showWelcome;

    // Devices
    [ObservableProperty] private string _edgePath = "";
    [ObservableProperty] private string _watchFolder = "";
    [ObservableProperty] private string _edgeStatus = "";
    [ObservableProperty] private string _stravaClientId = "";
    [ObservableProperty] private string _stravaClientSecret = "";
    [ObservableProperty] private string _stravaStatus = "";
    [ObservableProperty] private bool _stravaConnected;

    // Library
    public ObservableCollection<WorkoutTemplate> Templates { get; } = [];
    [ObservableProperty] private string _libraryFilter = "";
    [ObservableProperty] private WorkoutTemplate? _selectedTemplate;
    [ObservableProperty] private string _templateName = "";
    [ObservableProperty] private WorkoutKind _templateKind = WorkoutKind.Threshold;
    [ObservableProperty] private string _templateDescription = "";
    [ObservableProperty] private string _templateNotation = "";
    [ObservableProperty] private string? _templateError;
    [ObservableProperty] private List<WorkoutStep>? _templatePreview;
    [ObservableProperty] private string _templateStats = "";
    [ObservableProperty] private bool _templateEditable;
    [ObservableProperty] private int _previewFtp;

    public string DataFolder => Trainer.Paths.Root;

    public override void Load()
    {
        var a = Trainer.GetAthlete();
        Name = a.Name;
        Level = a.Level;
        WeeklyHours = a.WeeklyHours;
        LongRideDay = a.LongRideDay;
        NoRaceGoal = a.NoRaceGoal;
        DefaultIndoor = a.DefaultIndoor;
        ThresholdHr = a.ThresholdHr?.ToString() ?? "";
        Days.Clear();
        foreach (var d in WeekDays) Days.Add(new DayOption(d, a.TrainingDays.Contains(d)));

        EdgePath = a.EdgePath ?? "";
        WatchFolder = a.WatchFolder ?? "";
        StravaClientId = a.StravaClientId ?? "";
        StravaClientSecret = a.StravaClientSecret ?? "";
        StravaConnected = S.Strava.IsConnected;
        StravaStatus = StravaConnected
            ? $"Connected.{(a.StravaLastSyncUtc is { } t ? $" Last sync {t.ToLocalTime():g}." : "")}"
            : "Not connected.";

        LoadFtp();
        LoadLibrary();
    }

    private void LoadFtp()
    {
        FtpHistory.Clear();
        foreach (var f in Trainer.GetFtpHistory().OrderByDescending(f => f.Date)) FtpHistory.Add(new FtpRow(f));
        var ftp = Trainer.CurrentFtp();
        PreviewFtp = ftp;
        ShowWelcome = FtpHistory.Count == 0;
        CurrentFtpText = ftp > 0 ? $"Current FTP: {ftp} W. A ramp test is scheduled after each recovery week (about monthly)." : "No FTP yet.";
    }

    [RelayCommand]
    private Task SaveAthlete() => RunAsync(async () =>
    {
        var a = Trainer.GetAthlete();
        a.Name = Name.Trim();
        a.Level = Level;
        a.WeeklyHours = WeeklyHours;
        a.TrainingDays = Days.Where(d => d.Selected).Select(d => d.Day).ToList();
        a.LongRideDay = LongRideDay;
        a.NoRaceGoal = NoRaceGoal;
        a.DefaultIndoor = DefaultIndoor;
        a.ThresholdHr = int.TryParse(ThresholdHr, out var hr) && hr is > 80 and < 230 ? hr : null;
        if (!a.TrainingDays.Contains(LongRideDay))
            throw new InvalidOperationException("The long-ride day must be one of your training days.");
        await Task.Run(() => Trainer.SaveAthlete(a));
        Shell?.Toast("Settings saved. The plan follows your new hours and days.");
    }, "Saving and re-planning…");

    // ---------- FTP ----------

    [RelayCommand]
    private Task AddFtp() => RunAsync(async () =>
    {
        if (!int.TryParse(NewFtp, out var w)) throw new InvalidOperationException("Enter FTP in watts, e.g. 250.");
        var date = DateOnly.FromDateTime(NewFtpDate);
        var method = NewFtpMethod;
        await Task.Run(() => Trainer.AddFtp(w, method, date));
        NewFtp = "";
        if (Shell is { FirstRun: true })
        {
            Shell.FirstRun = false;
            Shell.Toast("FTP saved and your plan is ready. Have a look at the calendar.");
        }
        else
        {
            Shell?.Toast($"FTP {w} W saved. Every future workout uses it.");
        }
        LoadFtp();
    }, "Saving FTP and re-planning…");

    /// <summary>First run without a known FTP: store a rough guess and start the plan with a ramp test.</summary>
    [RelayCommand]
    private Task StartWithRampTest() => RunAsync(async () =>
    {
        var guess = int.TryParse(NewFtp, out var w) ? w : 200;
        await Task.Run(() => Trainer.AddFtp(guess, FtpMethod.Estimate));
        if (Shell is not null) Shell.FirstRun = false;
        Dialogs.Info($"The plan starts with a ramp test. Its targets use a provisional FTP of {guess} W; " +
                     "when you import the ride, the app sets your real FTP (75 % of your best minute) and re-plans.");
        LoadFtp();
    }, "Planning…");

    [RelayCommand]
    private void EstimateFtp()
    {
        var e = Trainer.EstimateFtp();
        if (e is null)
        {
            Dialogs.Info("No ride with 20 minutes of power in the last 6 weeks to estimate from.");
            return;
        }
        NewFtp = e.Value.ToString();
        NewFtpMethod = FtpMethod.Estimate;
        NewFtpDate = DateTime.Today;
    }

    [RelayCommand]
    private Task DeleteFtp() => RunAsync(async () =>
    {
        if (SelectedFtp is null || !Dialogs.Confirm($"Delete the FTP entry of {SelectedFtp.Watts} on {SelectedFtp.Date}?")) return;
        var id = SelectedFtp.E.Id;
        await Task.Run(() => Trainer.DeleteFtp(id));
        LoadFtp();
    }, "Re-planning…");

    // ---------- Edge, folder, Strava ----------

    [RelayCommand]
    private void BrowseEdge()
    {
        var f = Dialogs.PickFolder("Select the Edge drive (the folder that contains Garmin)", EdgePath);
        if (f is not null) EdgePath = f;
    }

    [RelayCommand]
    private Task DetectEdge() => RunAsync(async () =>
    {
        var path = string.IsNullOrWhiteSpace(EdgePath) ? null : EdgePath;
        var devices = await Task.Run(() => EdgeLocator.FindAll(path));
        try
        {
            if (devices.Count == 0)
            {
                EdgeStatus = "No Edge found. Plug it in and wait for Windows to show it. Newer Edges appear as a device (MTP), older ones as a drive.";
                return;
            }
            var acts = devices[0].ListActivities();
            EdgeStatus = $"Found {devices[0].DisplayName} with {acts.Count} rides in Garmin\\Activity.";
            if (devices[0] is DriveEdgeDevice d && string.IsNullOrWhiteSpace(EdgePath)) EdgePath = d.Root;
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }, "Looking for the Edge…");

    [RelayCommand]
    private void BrowseWatchFolder()
    {
        var f = Dialogs.PickFolder("Folder to watch for downloaded FIT files", WatchFolder);
        if (f is not null) WatchFolder = f;
    }

    [RelayCommand]
    private void SaveDevices() => Run(() =>
    {
        if (!string.IsNullOrWhiteSpace(WatchFolder) && !Directory.Exists(WatchFolder))
            throw new InvalidOperationException("The watched folder doesn't exist.");
        Trainer.UpdateIntegrationSettings(a =>
        {
            a.EdgePath = string.IsNullOrWhiteSpace(EdgePath) ? null : EdgePath.Trim();
            a.WatchFolder = string.IsNullOrWhiteSpace(WatchFolder) ? null : WatchFolder.Trim();
        });
        var watching = S.RestartWatcher();
        Shell?.Toast(watching is null ? "Saved." : $"Saved. Watching {watching} for new FIT files.");
    });

    [RelayCommand]
    private Task ConnectStrava() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(StravaClientId) || string.IsNullOrWhiteSpace(StravaClientSecret))
            throw new InvalidOperationException("Enter the Client ID and Client Secret of your Strava API application (strava.com/settings/api).");
        await S.Strava.ConnectAsync(StravaClientId.Trim(), StravaClientSecret.Trim());
        Load();
        Shell?.Toast("Strava connected. Use Activities → Sync Strava.");
    }, "Waiting for Strava in your browser…");

    [RelayCommand]
    private void DisconnectStrava()
    {
        S.Strava.Disconnect();
        Load();
    }

    [RelayCommand]
    private void OpenStravaApiPage() => Process.Start(new ProcessStartInfo("https://www.strava.com/settings/api") { UseShellExecute = true });

    // ---------- Library ----------

    partial void OnLibraryFilterChanged(string value) => LoadLibrary();

    private void LoadLibrary()
    {
        var keep = SelectedTemplate?.Id;
        Templates.Clear();
        foreach (var t in Trainer.GetTemplates().Where(t => string.IsNullOrWhiteSpace(LibraryFilter)
                     || t.Name.Contains(LibraryFilter, StringComparison.OrdinalIgnoreCase)
                     || t.Kind.Display().Contains(LibraryFilter, StringComparison.OrdinalIgnoreCase)))
            Templates.Add(t);
        SelectedTemplate = Templates.FirstOrDefault(t => t.Id == keep) ?? Templates.FirstOrDefault();
    }

    partial void OnSelectedTemplateChanged(WorkoutTemplate? value)
    {
        if (value is null) return;
        TemplateName = value.Name;
        TemplateKind = value.Kind;
        TemplateDescription = value.Description;
        TemplateNotation = IntervalNotation.Format(value.Steps);
        TemplateEditable = !value.BuiltIn;
    }

    partial void OnTemplateNotationChanged(string value)
    {
        if (IntervalNotation.TryParse(value, out var steps, out var error))
        {
            TemplateError = null;
            TemplatePreview = steps;
            var load = WorkoutMetrics.Analyze(steps);
            TemplateStats = $"{Format.Duration(load.TotalSec)} · {load.Tss:0} TSS · IF {load.If:0.00} · " +
                            $"{load.SweetSpotSec / 60} min sweet spot · {load.ThresholdSec / 60} min threshold · {load.Vo2Sec / 60} min VO2+";
        }
        else
        {
            TemplateError = error;
        }
    }

    [RelayCommand]
    private void NewTemplate()
    {
        SelectedTemplate = null;
        TemplateName = "My workout";
        TemplateKind = WorkoutKind.Threshold;
        TemplateDescription = "";
        TemplateNotation = "wu 12m 50-70; 3x(10m 95-100 \"threshold\", 5m 55); cd 8m 50";
        TemplateEditable = true;
    }

    [RelayCommand]
    private void DuplicateTemplate()
    {
        if (SelectedTemplate is null) return;
        var name = SelectedTemplate.Name + " (copy)";
        SelectedTemplate = null;
        TemplateName = name;
        TemplateEditable = true;
    }

    [RelayCommand]
    private void SaveTemplate() => Run(() =>
    {
        var steps = IntervalNotation.Parse(TemplateNotation);
        var id = SelectedTemplate is { BuiltIn: false } t ? t.Id : 0;
        Trainer.SaveTemplate(new WorkoutTemplate
        {
            Id = id, Name = TemplateName.Trim(), Kind = TemplateKind, Description = TemplateDescription.Trim(), Steps = steps,
        });
        LibraryFilter = "";
        LoadLibrary();
        SelectedTemplate = Templates.FirstOrDefault(x => x.Name == TemplateName.Trim());
        Shell?.Toast("Workout saved. The plan engine can now pick it.");
    });

    [RelayCommand]
    private void DeleteTemplate() => Run(() =>
    {
        if (SelectedTemplate is not { BuiltIn: false } t || !Dialogs.Confirm($"Delete {t.Name}?")) return;
        Trainer.DeleteTemplate(t.Id);
        LoadLibrary();
    });

    [RelayCommand]
    private void ExportTemplateZwo() => Run(() =>
    {
        var steps = IntervalNotation.Parse(TemplateNotation);
        var name = string.IsNullOrWhiteSpace(TemplateName) ? "Workout" : TemplateName.Trim();
        var file = Dialogs.SaveFile("Save Zwift workout", "Zwift workout (*.zwo)|*.zwo", ZwoWriter.FileName(name), ZwiftFolder.Find());
        if (file is null) return;
        File.WriteAllText(file, ZwoWriter.Write(name, steps, TemplateDescription, kind: TemplateKind));
        Shell?.Toast($"Saved {Path.GetFileName(file)}.");
    });

    // ---------- Data ----------

    [RelayCommand]
    private void OpenDataFolder() => Dialogs.OpenInExplorer(DataFolder);

    [RelayCommand]
    private void Backup() => Run(() =>
    {
        var file = Dialogs.SaveFile("Back up training data", "Zip archive (*.zip)|*.zip", $"trainer-backup-{DateTime.Now:yyyy-MM-dd}.zip");
        if (file is null) return;
        BackupService.Backup(Trainer.Paths, file);
        Shell?.Toast($"Backed up to {file}.");
    });

    [RelayCommand]
    private void Restore() => Run(() =>
    {
        var file = Dialogs.PickFile("Restore a backup", "Zip archive (*.zip)|*.zip");
        if (file is null || !Dialogs.Confirm("Replace all current data with this backup? The app restarts afterwards.")) return;
        S.Dispose();
        BackupService.Restore(Trainer.Paths, file);
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Shutdown();
    });
}
