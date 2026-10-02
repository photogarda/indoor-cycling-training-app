using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.App.Infrastructure;
using Trainer.Core.Models;
using Trainer.Core.Planning;
using Trainer.Integrations.Edge;
using Trainer.Integrations.Zwift;

namespace Trainer.App.ViewModels;

public record CalendarWorkout(PlannedWorkout W)
{
    public int Id => W.Id;
    public string Name => W.Name;
    public string Duration => Format.Duration(W.DurationSec);
    public string Summary => $"{Format.Duration(W.DurationSec)} · {W.Tss:0} TSS";
    public WorkoutStatus Status => W.Status;
    public WorkoutKind Kind => W.Kind;
    public bool IsKey => W.IsKey;
    public bool Locked => W.Locked;
    public bool Outdoor => !W.Indoor;
    public IList<WorkoutStep> Steps => W.Steps;
    public string Tooltip => $"{W.Name}\n{Summary} · IF {W.IntensityFactor:0.00}\n{W.Status}{(W.Locked ? " · locked" : "")}{(W.Indoor ? " · indoor" : " · outdoor")}" +
                             (string.IsNullOrEmpty(W.Notes) ? "" : $"\n{W.Notes}");
}

public record CalendarRace(Race R)
{
    public string Label => $"{R.Priority} · {R.Name}";
    public string Tooltip => $"{R.Name} ({R.Priority} race)\n{R.Type.Display()} · {R.DurationHours:0.#} h · intensity {R.Intensity}/10";
}

public record CalendarActivity(Activity A)
{
    public string Label => $"✓ {Format.Duration(A.DurationSec)} · {A.Tss:0} TSS";
    public string Tooltip => $"{A.Name}\n{A.StartTime:HH:mm} · {Format.Duration(A.DurationSec)}" +
                             (A.NormalizedPower is null ? "" : $" · NP {A.NormalizedPower:0} W") + $" · {A.Tss:0} TSS{(A.HrBasedTss ? " (HR)" : "")}";
}

public partial class DayCell : ObservableObject
{
    public required DateOnly Date { get; init; }
    public bool IsToday { get; init; }
    public bool IsOtherMonth { get; init; }
    public bool IsPast { get; init; }
    public BlockedDay? Blocked { get; init; }
    public bool IsBlocked => Blocked is not null;
    public string DayLabel => Date.Day == 1 ? Date.ToString("d MMM", CultureInfo.CurrentCulture) : Date.Day.ToString(CultureInfo.CurrentCulture);
    public string WeekDayLabel => Date.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    public PlanWeek? WeekInfo { get; init; }
    public bool ShowWeekInfo => WeekInfo is not null && Date.DayOfWeek == DayOfWeek.Monday;
    public string WeekInfoLabel => WeekInfo is null ? "" : $"{WeekInfo.Phase}{(WeekInfo.WeekType == WeekType.Recovery ? " · rec" : "")}";
    public ObservableCollection<CalendarWorkout> Workouts { get; } = [];
    public ObservableCollection<CalendarRace> Races { get; } = [];
    public ObservableCollection<CalendarActivity> Activities { get; } = [];
}

public partial class CalendarViewModel : ViewModelBase
{
    public override string Title => "Calendar";

    [ObservableProperty] private bool _monthMode = true;
    [ObservableProperty] private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _weekSummary = "";
    [ObservableProperty] private string? _notes;

    public ObservableCollection<DayCell> Days { get; } = [];
    public string[] DayNames { get; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    partial void OnMonthModeChanged(bool value) => Load();

    public override void Load()
    {
        var today = Trainer.Today;
        DateOnly from, to;
        if (MonthMode)
        {
            var first = new DateOnly(Anchor.Year, Anchor.Month, 1);
            from = PlanEngine.WeekStart(first);
            to = from.AddDays(41);
            Heading = first.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        }
        else
        {
            from = PlanEngine.WeekStart(Anchor);
            to = from.AddDays(6);
            Heading = $"Week of {from:d MMM yyyy}";
        }

        var data = Trainer.GetCalendar(from, to);
        var weeks = Trainer.GetPlanOverview().Weeks.ToDictionary(w => w.WeekStart);
        Days.Clear();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var day = d;
            weeks.TryGetValue(PlanEngine.WeekStart(day), out var wk);
            var cell = new DayCell
            {
                Date = day,
                IsToday = day == today,
                IsPast = day < today,
                IsOtherMonth = MonthMode && day.Month != Anchor.Month,
                Blocked = data.BlockedDays.FirstOrDefault(b => b.Date == day),
                WeekInfo = wk,
            };
            foreach (var r in data.Races.Where(r => r.Date == day)) cell.Races.Add(new CalendarRace(r));
            foreach (var w in data.Workouts.Where(w => w.Date == day)) cell.Workouts.Add(new CalendarWorkout(w));
            foreach (var a in data.Activities.Where(a => a.Date == day)) cell.Activities.Add(new CalendarActivity(a));
            Days.Add(cell);
        }

        var thisWeek = PlanEngine.WeekStart(MonthMode ? today : Anchor);
        var weekWorkouts = Trainer.GetWorkouts(thisWeek, thisWeek.AddDays(6));
        var plannedHours = weekWorkouts.Sum(w => w.DurationSec) / 3600.0;
        WeekSummary = weeks.TryGetValue(thisWeek, out var info)
            ? $"Week of {thisWeek:d MMM}: {info.Phase} ({info.WeekType.ToString().ToLowerInvariant()}) · {plannedHours:0.#} h planned · {weekWorkouts.Sum(w => w.Tss):0} TSS"
            : $"Week of {thisWeek:d MMM}: {plannedHours:0.#} h planned";
        Notes = Trainer.LastPlanNotes.Count > 0 ? string.Join("\n", Trainer.LastPlanNotes.Take(4)) : null;
    }

    [RelayCommand]
    private void Previous()
    {
        Anchor = MonthMode ? Anchor.AddMonths(-1) : Anchor.AddDays(-7);
        Load();
    }

    [RelayCommand]
    private void Next()
    {
        Anchor = MonthMode ? Anchor.AddMonths(1) : Anchor.AddDays(7);
        Load();
    }

    [RelayCommand]
    private void GoToday()
    {
        Anchor = Trainer.Today;
        Load();
    }

    [RelayCommand]
    private void Open(int id) => Shell?.OpenWorkout(id);

    [RelayCommand]
    private Task Regenerate() => RunAsync(async () =>
    {
        await Task.Run(() => Trainer.Refresh());
        Shell?.Toast("Plan regenerated from today.");
    }, "Regenerating the plan…");

    public void MoveWorkout(int id, DateOnly date) => Run(() => Trainer.MoveWorkout(id, date));

    public void BlockDay(DateOnly date)
    {
        var reason = Dialogs.Prompt($"Block {date:dddd d MMMM}? The plan will shift around it.\n\nReason (optional):", "Block day", "Unavailable");
        if (reason is null) return;
        Run(() => Trainer.BlockDay(date, string.IsNullOrWhiteSpace(reason) ? "Unavailable" : reason));
    }

    public void UnblockDay(DateOnly date) => Run(() => Trainer.UnblockDay(date));

    /// <summary>Exports the next 7 days of workouts to the Edge in one go.</summary>
    [RelayCommand]
    private Task SendWeekToEdge() => RunAsync(async () =>
    {
        var today = Trainer.Today;
        var workouts = Trainer.GetWorkouts(today, today.AddDays(6)).Where(w => w.Steps.Count > 0).ToList();
        if (workouts.Count == 0)
        {
            Dialogs.Info("There are no workouts in the next 7 days.");
            return;
        }
        if (Trainer.CurrentFtp() <= 0)
        {
            Dialogs.Error("Set your FTP in Settings first: workout targets are worked out from it.");
            return;
        }
        var edgePath = Trainer.GetAthlete().EdgePath;
        var devices = await Task.Run(() => EdgeLocator.FindAll(edgePath));
        try
        {
            if (devices.Count > 0)
            {
                var device = devices[0];
                var written = await Task.Run(() => S.Fit.SendToEdge(workouts, device));
                Dialogs.Info($"{written.Count} workouts copied to {device.DisplayName} (Garmin\\NewFiles).\n\nUnplug the Edge: it imports them and they appear under Training → Workouts.");
                return;
            }
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }

        if (!Dialogs.Confirm("No Garmin Edge found (drive letter or MTP).\n\nSave the FIT files to a folder instead, so you can copy them into Garmin\\NewFiles by hand?"))
            return;
        var folder = Dialogs.PickFolder("Save workout FIT files", Trainer.Paths.ExportFolder) ?? Trainer.Paths.ExportFolder;
        var files = S.Fit.SaveToFolder(workouts, folder);
        Shell?.Toast($"{files.Count} workout files saved to {folder}.");
        Dialogs.OpenInExplorer(folder);
    }, "Sending this week's workouts…");

    /// <summary>Saves the next 7 days of workouts as Zwift .zwo files (default: Zwift's own workouts folder).</summary>
    [RelayCommand]
    private void ExportWeekToZwo() => Run(() =>
    {
        var today = Trainer.Today;
        var workouts = Trainer.GetWorkouts(today, today.AddDays(6)).Where(w => w.Steps.Count > 0).ToList();
        if (workouts.Count == 0)
        {
            Dialogs.Info("There are no workouts in the next 7 days.");
            return;
        }
        var zwift = ZwiftFolder.Find();
        var folder = Dialogs.PickFolder(zwift is null
            ? "Save Zwift workouts (.zwo)"
            : "Save Zwift workouts (.zwo) — Zwift's workouts folder is preselected", zwift ?? Trainer.Paths.ExportFolder);
        if (folder is null) return;
        var files = ZwiftFolder.SaveAll(workouts, folder);
        Shell?.Toast($"{files.Count} .zwo files saved to {folder}." +
                     (zwift is not null && string.Equals(folder, zwift, StringComparison.OrdinalIgnoreCase) ? " Restart Zwift to see them under Custom Workouts." : ""));
    });
}
