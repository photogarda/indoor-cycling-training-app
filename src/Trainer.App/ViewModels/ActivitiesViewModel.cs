using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.App.Infrastructure;
using Trainer.Core.Models;
using Trainer.Integrations.Edge;

namespace Trainer.App.ViewModels;

public record ActivityRow(Activity A, string? Linked)
{
    public string Date => A.StartTime.ToString("ddd d MMM yyyy HH:mm");
    public string Name => A.Name;
    public string Source => A.Source == ActivitySource.Fit ? (A.StravaId is null ? "FIT" : "FIT + Strava") : "Strava";
    public string Duration => Format.Duration(A.DurationSec);
    public string Distance => A.DistanceKm is null ? "" : $"{A.DistanceKm:0.0} km";
    public string Power => A.AvgPower is null ? "" : $"{A.AvgPower:0} / {A.NormalizedPower:0} W";
    public string If => A.IntensityFactor is null ? "" : $"{A.IntensityFactor:0.00}";
    public string Tss => $"{A.Tss:0}{(A.HrBasedTss ? " (HR)" : "")}";
    public string Hr => A.AvgHr is null ? "" : $"{A.AvgHr:0}";
    public string Cadence => A.AvgCadence is null ? "" : $"{A.AvgCadence:0}";
    public string LinkedTo => Linked ?? "";
}

public record LinkOption(int? Id, string Label);

public partial class ActivitiesViewModel : ViewModelBase
{
    public override string Title => "Activities";

    public ObservableCollection<ActivityRow> Rows { get; } = [];
    public ObservableCollection<LinkOption> LinkOptions { get; } = [];
    [ObservableProperty] private ActivityRow? _selected;
    [ObservableProperty] private LinkOption? _selectedLink;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _stravaConnected;

    public override void Load()
    {
        var selectedId = Selected?.A.Id;
        var acts = Trainer.GetActivities();
        var workouts = acts.Count == 0 ? [] : Trainer.GetWorkouts(acts.Min(a => a.Date).AddDays(-1), acts.Max(a => a.Date).AddDays(1)).ToDictionary(w => w.Id);
        Rows.Clear();
        foreach (var a in acts)
            Rows.Add(new ActivityRow(a, a.PlannedWorkoutId is { } id && workouts.TryGetValue(id, out var w) ? $"{w.Name} ({w.Date:ddd d MMM})" : null));
        Selected = Rows.FirstOrDefault(r => r.A.Id == selectedId);
        var last28 = acts.Where(a => a.Date > Trainer.Today.AddDays(-28)).ToList();
        Summary = $"{acts.Count} rides · last 4 weeks: {last28.Count} rides, {last28.Sum(a => a.DurationSec) / 3600.0:0.0} h, {last28.Sum(a => a.Tss):0} TSS";
        StravaConnected = S.Strava.IsConnected;
    }

    partial void OnSelectedChanged(ActivityRow? value)
    {
        LinkOptions.Clear();
        if (value is null) return;
        LinkOptions.Add(new LinkOption(null, "— not linked —"));
        foreach (var w in Trainer.GetWorkouts(value.A.Date.AddDays(-3), value.A.Date.AddDays(3)))
            LinkOptions.Add(new LinkOption(w.Id, $"{w.Date:ddd d MMM}: {w.Name} ({Format.Duration(w.DurationSec)}, {w.Tss:0} TSS)"));
        SelectedLink = LinkOptions.FirstOrDefault(o => o.Id == value.A.PlannedWorkoutId) ?? LinkOptions[0];
    }

    [RelayCommand]
    private void Link()
    {
        if (Selected is null || SelectedLink is null) return;
        Run(() => Trainer.LinkActivity(Selected.A.Id, SelectedLink.Id));
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null || !Dialogs.Confirm($"Delete {Selected.Name} ({Selected.Date})? The FIT file stays in the data folder.")) return;
        Run(() => Trainer.DeleteActivity(Selected.A.Id));
    }

    [RelayCommand]
    private Task ImportFromEdge() => RunAsync(async () =>
    {
        var path = Trainer.GetAthlete().EdgePath;
        var devices = await Task.Run(() => EdgeLocator.FindAll(path));
        if (devices.Count == 0)
        {
            Dialogs.Error("No Garmin Edge found. Plug it in and wait until Windows shows it, or set its drive in Settings.");
            return;
        }
        try
        {
            var report = await Task.Run(() => S.Fit.ImportFromEdge(devices[0]));
            Shell?.Toast($"{devices[0].DisplayName}: {report}");
            if (report.Errors.Count > 0) Dialogs.Error(string.Join("\n", report.Errors.Take(10)));
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }, "Importing rides from the Edge…");

    [RelayCommand]
    private Task ImportFiles() => RunAsync(async () =>
    {
        var files = Dialogs.PickFiles("Import FIT rides", "FIT files (*.fit)|*.fit|All files (*.*)|*.*");
        if (files.Length == 0) return;
        var report = await Task.Run(() => S.Fit.ImportFiles(files));
        Shell?.Toast(report.ToString());
        if (report.Errors.Count > 0) Dialogs.Error(string.Join("\n", report.Errors.Take(10)));
    }, "Importing rides…");

    [RelayCommand]
    private Task SyncStrava() => RunAsync(async () =>
    {
        if (!S.Strava.IsConnected)
        {
            Dialogs.Info("Connect Strava in Settings first (you need your own Strava API application).");
            return;
        }
        var progress = new Progress<string>(s => Shell?.SetBusy(s));
        var report = await S.Strava.SyncAsync(progress);
        Shell?.Toast($"Strava: {report}");
        if (report.Errors.Count > 0) Dialogs.Error(string.Join("\n", report.Errors.Take(10)));
    }, "Checking Strava…");

    [RelayCommand]
    private void OpenFitFolder() => Dialogs.OpenInExplorer(Trainer.Paths.FitFolder);
}
