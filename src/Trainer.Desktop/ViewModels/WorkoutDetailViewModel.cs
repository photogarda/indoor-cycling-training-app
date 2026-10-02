using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.Desktop.Infrastructure;
using Trainer.Core.Models;
using Trainer.Core.Training;
using Trainer.Integrations.Edge;
using Trainer.Integrations.Fit;
using Trainer.Integrations.Zwift;

namespace Trainer.Desktop.ViewModels;

public record StepRow(string Start, string Length, string Target, string Watts, string Zone, string Label);

public partial class WorkoutDetailViewModel(int workoutId, ViewModelBase back) : ViewModelBase
{
    public override string Title => Workout?.Name ?? "Workout";
    public ViewModelBase Back { get; } = back;

    [ObservableProperty] private PlannedWorkout? _workout;
    [ObservableProperty] private int _ftp;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private bool _indoor;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private WorkoutTemplate? _swapTo;
    [ObservableProperty] private string _durationText = "";
    [ObservableProperty] private string? _activityText;
    [ObservableProperty] private bool _canEdit;

    public ObservableCollection<StepRow> Rows { get; } = [];
    public ObservableCollection<WorkoutTemplate> Templates { get; } = [];
    public IList<WorkoutStep>? Steps => Workout?.Steps;

    private bool _loading;

    public override void Load()
    {
        _loading = true;
        var w = Trainer.GetWorkout(workoutId);
        Workout = w;
        if (w is null)
        {
            Summary = "This workout no longer exists (the plan was regenerated).";
            _loading = false;
            return;
        }
        Ftp = Ftp_On(w.Date);
        Indoor = w.Indoor;
        Locked = w.Locked;
        CanEdit = w.Date >= Trainer.Today;
        DurationText = Format.Duration(w.DurationSec);
        var load = Core.Workouts.WorkoutMetrics.Analyze(w.Steps);
        Summary = $"{w.Date:dddd d MMMM yyyy} · {w.Kind.Display()}{(w.IsKey ? " (key session)" : "")} · {Format.Duration(w.DurationSec)} · " +
                  $"{w.Tss:0} TSS · IF {w.IntensityFactor:0.00} · {w.Status}" +
                  (load.ThresholdSec + load.Vo2Sec > 0 ? $" · {load.ThresholdSec / 60} min threshold, {load.Vo2Sec / 60} min VO2+" : "");

        var templates = Trainer.GetTemplates();
        Description = string.Join("\n", new[] { templates.FirstOrDefault(t => t.Id == w.TemplateId)?.Description, w.Notes }.Where(s => !string.IsNullOrWhiteSpace(s)));
        Templates.Clear();
        foreach (var t in templates.OrderBy(t => t.Kind != w.Kind).ThenBy(t => t.Kind).ThenBy(t => t.Name)) Templates.Add(t);
        SwapTo = Templates.FirstOrDefault(t => t.Id == w.TemplateId);

        Rows.Clear();
        var at = 0;
        foreach (var s in WorkoutStep.Flatten(w.Steps))
        {
            var pct = s.Kind == StepKind.Free ? "ERG off"
                : Math.Abs(s.PowerHigh - s.PowerLow) < 0.005 ? $"{s.PowerLow * 100:0} %" : $"{s.PowerLow * 100:0}–{s.PowerHigh * 100:0} %";
            var watts = s.Kind == StepKind.Free || Ftp <= 0 ? "" : $"{FitWorkoutWriter.Watts(s, Ftp).Low}–{FitWorkoutWriter.Watts(s, Ftp).High} W";
            Rows.Add(new StepRow($"{at / 3600}:{at % 3600 / 60:00}:{at % 60:00}",
                s.DurationSec >= 60 ? $"{s.DurationSec / 60}:{s.DurationSec % 60:00}" : $"{s.DurationSec} s",
                pct, watts, s.Kind == StepKind.Free ? "—" : Zones.Name(Zones.Of(s.PowerMid)), s.Label ?? s.Kind.ToString()));
            at += s.DurationSec;
        }

        var linked = Trainer.GetActivities(w.Date.AddDays(-1), w.Date.AddDays(1)).FirstOrDefault(a => a.PlannedWorkoutId == w.Id);
        ActivityText = linked is null ? null
            : $"Ridden: {linked.Name} on {linked.StartTime:ddd d MMM HH:mm} · {Format.Duration(linked.DurationSec)} · {linked.Tss:0} TSS " +
              $"({(w.Tss > 0 ? linked.Tss / w.Tss * 100 : 100):0} % of plan)" + (linked.NormalizedPower is null ? "" : $" · NP {linked.NormalizedPower:0} W");
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(Title));
        _loading = false;
    }

    private static int Ftp_On(DateOnly date) => Core.Training.Ftp.On(Trainer.GetFtpHistory(), date);

    partial void OnIndoorChanged(bool value)
    {
        if (_loading || Workout is null) return;
        Run(() => Trainer.SetIndoor(Workout.Id, value));
    }

    partial void OnLockedChanged(bool value)
    {
        if (_loading || Workout is null) return;
        Run(() => Trainer.SetLocked(Workout.Id, value));
    }

    [RelayCommand]
    private void GoBack() => Shell?.GoBack(Back);

    [RelayCommand]
    private void Swap()
    {
        if (Workout is null || SwapTo is null || SwapTo.Id == Workout.TemplateId) return;
        Run(() => Trainer.SwapTemplate(Workout.Id, SwapTo.Id));
    }

    [RelayCommand]
    private async Task Resize()
    {
        if (Workout is null) return;
        if (!TimeSpan.TryParse(DurationText.Contains(':') ? DurationText : $"0:{DurationText}", out var ts) || ts < TimeSpan.FromMinutes(20) || ts > TimeSpan.FromHours(8))
        {
            await Dialogs.Error("Enter a duration like 1:30 (between 20 minutes and 8 hours).");
            return;
        }
        Run(() => Trainer.ResizeWorkout(Workout.Id, ts));
    }

    [RelayCommand]
    private Task ExportToEdge() => RunAsync(async () =>
    {
        if (Workout is null) return;
        if (Ftp <= 0) throw new InvalidOperationException("Set your FTP in Settings first.");
        var path = Trainer.GetAthlete().EdgePath;
        var devices = await Task.Run(() => EdgeLocator.FindAll(path));
        try
        {
            if (devices.Count == 0)
            {
                await Dialogs.Error("No Garmin Edge found. Plug it in, or use \"Save FIT file…\" and copy it into Garmin/NewFiles by hand.");
                return;
            }
            await Task.Run(() => S.Fit.SendToEdge([Workout], devices[0]));
            Shell?.Toast($"{Workout.Name} copied to {devices[0].DisplayName}. Unplug the Edge to load it.");
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }, "Copying to the Edge…");

    [RelayCommand]
    private Task SaveFit()
    {
        if (Workout is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            if (Ftp <= 0) throw new InvalidOperationException("Set your FTP in Settings first.");
            var file = await Dialogs.SaveFile("Save workout", "fit", "FIT workout", FitWorkoutWriter.FileName(Workout));
            if (file is null) return;
            File.WriteAllBytes(file, FitWorkoutWriter.Write(Workout, Ftp));
            Shell?.Toast($"Saved {Path.GetFileName(file)}.");
        });
    }

    /// <summary>Zwift workout file (% FTP, so Zwift applies its own FTP).</summary>
    [RelayCommand]
    private Task SaveZwo()
    {
        if (Workout is null) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var file = await Dialogs.SaveFile("Save Zwift workout", "zwo", "Zwift workout",
                ZwoWriter.FileName(Workout.Name, Workout.Date), ZwiftFolder.Find());
            if (file is null) return;
            File.WriteAllText(file, ZwoWriter.Write(Workout));
            Shell?.Toast($"Saved {Path.GetFileName(file)}.");
        });
    }
}
