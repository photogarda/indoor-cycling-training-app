using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.Desktop.Infrastructure;
using Trainer.Core.Models;
using Trainer.Core.Planning;
using Trainer.Integrations.Edge;
using Trainer.Integrations.Zwift;

namespace Trainer.Desktop.ViewModels;

public record CalendarWorkout(PlannedWorkout W)
{
    public int Id => W.Id;
    public string Name => W.Name;
    public string Duration => Format.Duration(W.DurationSec);
    public string Summary => W.IsSkipped ? "removed · right-click to restore" : $"{Format.Duration(W.DurationSec)} · {W.Tss:0} TSS";
    public bool IsSkipped => W.IsSkipped;
    public bool CanEdit { get; init; }
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
    /// <summary>Freed by moving its workout away: you can still drop a workout here.</summary>
    public bool IsMovedAway => Blocked?.Reason.StartsWith(global::Trainer.Data.Services.TrainerService.MovedAwayPrefix, StringComparison.Ordinal) == true;
    public bool CanDrop => !IsPast && (!IsBlocked || IsMovedAway);
    public string DayLabel => Date.Day == 1 ? Date.ToString("d MMM", CultureInfo.CurrentCulture) : Date.Day.ToString(CultureInfo.CurrentCulture);
    public string WeekDayLabel => Date.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    public ObservableCollection<CalendarWorkout> Workouts { get; } = [];
    public ObservableCollection<CalendarRace> Races { get; } = [];
    public ObservableCollection<CalendarActivity> Activities { get; } = [];
}

/// <summary>One calendar row: seven days plus the week's prescribed and done totals.</summary>
public class CalendarWeek
{
    public required DateOnly Start { get; init; }
    public List<DayCell> Days { get; } = [];
    public PlanWeek? Plan { get; init; }
    public double PlannedHours { get; init; }
    public double PlannedTss { get; init; }
    public double DoneHours { get; init; }
    public double DoneTss { get; init; }
    public bool Started { get; init; }

    public string PhaseLabel => Plan is null ? "" : $"{Plan.Phase}{(Plan.WeekType == WeekType.Recovery && Plan.Phase != Phase.Recovery ? " · recovery" : "")}";
    public string PlannedText => PlannedHours > 0 ? $"{Hm(PlannedHours)} h · {PlannedTss:0} TSS" : "—";
    public string DoneText => Started ? $"{Hm(DoneHours)} h · {DoneTss:0} TSS" : "—";

    private static string Hm(double hours)
    {
        var minutes = (int)Math.Round(hours * 60);
        return $"{minutes / 60}:{minutes % 60:00}";
    }
    /// <summary>Done TSS as a share of prescribed, for the bar (capped at 120 %).</summary>
    public double DoneFraction => PlannedTss > 0 ? Math.Min(1.2, DoneTss / PlannedTss) : 0;
    public string PercentText => Started && PlannedTss > 0 ? $"{DoneTss / PlannedTss * 100:0} %" : "";
    public string Tooltip => $"Week of {Start:d MMM}" + (Plan is null ? "" : $" · {PhaseLabel} · target {Plan.TargetHours:0.#} h") +
                             $"\nPrescribed: {PlannedText}\nDone: {DoneText}";
}

public partial class CalendarViewModel : ViewModelBase
{
    public override string Title => "Calendar";

    [ObservableProperty] private bool _monthMode = true;
    [ObservableProperty] private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _weekSummary = "";
    [ObservableProperty] private string? _notes;

    public ObservableCollection<CalendarWeek> Weeks { get; } = [];

    public bool WeekMode
    {
        get => !MonthMode;
        set => MonthMode = !value;
    }

    partial void OnMonthModeChanged(bool value)
    {
        OnPropertyChanged(nameof(WeekMode));
        Load();
    }

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
        Weeks.Clear();
        for (var ws = from; ws <= to; ws = ws.AddDays(7))
        {
            var we = ws.AddDays(6);
            weeks.TryGetValue(ws, out var wk);
            var planned = data.Workouts.Where(w => w.Date >= ws && w.Date <= we && !w.IsSkipped).ToList();
            var done = data.Activities.Where(a => a.Date >= ws && a.Date <= we).ToList();
            var week = new CalendarWeek
            {
                Start = ws,
                Plan = wk,
                PlannedHours = planned.Sum(w => w.DurationSec) / 3600.0,
                PlannedTss = planned.Sum(w => w.Tss),
                DoneHours = done.Sum(a => a.DurationSec) / 3600.0,
                DoneTss = done.Sum(a => a.Tss),
                Started = ws <= today,
            };
            for (var day = ws; day <= we; day = day.AddDays(1))
            {
                var d = day;
                var cell = new DayCell
                {
                    Date = d,
                    IsToday = d == today,
                    IsPast = d < today,
                    IsOtherMonth = MonthMode && d.Month != Anchor.Month,
                    Blocked = data.BlockedDays.FirstOrDefault(b => b.Date == d),
                };
                foreach (var r in data.Races.Where(r => r.Date == d)) cell.Races.Add(new CalendarRace(r));
                foreach (var w in data.Workouts.Where(w => w.Date == d)) cell.Workouts.Add(new CalendarWorkout(w) { CanEdit = d >= today });
                foreach (var a in data.Activities.Where(a => a.Date == d)) cell.Activities.Add(new CalendarActivity(a));
                week.Days.Add(cell);
            }
            Weeks.Add(week);
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

    public async Task BlockDay(DateOnly date)
    {
        var reason = await Dialogs.Prompt($"Block {date:dddd d MMMM}? The plan will shift around it.\n\nReason (optional):", "Block day", "Unavailable");
        if (reason is null) return;
        Run(() => Trainer.BlockDay(date, string.IsNullOrWhiteSpace(reason) ? "Unavailable" : reason));
    }

    public void UnblockDay(DateOnly date) => Run(() => Trainer.UnblockDay(date));

    /// <summary>Removes a workout; the plan adapts (no back-fill, next week holds its load if a key session goes).</summary>
    public async Task SkipWorkout(CalendarWorkout w)
    {
        if (!await Dialogs.Confirm($"Remove {w.Name} on {w.W.Date:dddd d MMM}?\n\nThe day becomes a rest day." +
                                   (w.IsKey ? " Because it's a key session, next week repeats this week's load instead of stepping up." : "")))
            return;
        Run(() => Trainer.SkipWorkout(w.Id));
        Shell?.Toast($"{w.Name} removed. The plan was adapted.");
    }

    public void RestoreWorkout(CalendarWorkout w) => Run(() => Trainer.RestoreWorkout(w.Id));

    /// <summary>Days a workout can be moved to: today onwards, three weeks ahead, not blocked.</summary>
    public IEnumerable<DateOnly> MoveTargets(CalendarWorkout w)
    {
        var today = Trainer.Today;
        var from = w.W.Date.AddDays(-7) < today ? today : w.W.Date.AddDays(-7);
        var blocked = Trainer.GetCalendar(from, from.AddDays(27)).BlockedDays
            .Where(b => !b.Reason.StartsWith(global::Trainer.Data.Services.TrainerService.MovedAwayPrefix, StringComparison.Ordinal))
            .Select(b => b.Date).ToHashSet();
        return Enumerable.Range(0, 28).Select(from.AddDays).Where(d => d != w.W.Date && !blocked.Contains(d));
    }

    /// <summary>Exports the next 7 days of workouts to the Edge in one go.</summary>
    [RelayCommand]
    private Task SendWeekToEdge() => RunAsync(async () =>
    {
        var today = Trainer.Today;
        var workouts = Trainer.GetWorkouts(today, today.AddDays(6)).Where(w => w.Steps.Count > 0).ToList();
        if (workouts.Count == 0)
        {
            await Dialogs.Info("There are no workouts in the next 7 days.");
            return;
        }
        if (Trainer.CurrentFtp() <= 0)
        {
            await Dialogs.Error("Set your FTP in Settings first: workout targets are worked out from it.");
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
                await Dialogs.Info($"{written.Count} workouts copied to {device.DisplayName} (Garmin\\NewFiles).\n\nUnplug the Edge: it imports them and they appear under Training → Workouts.");
                return;
            }
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }

        if (!await Dialogs.Confirm((OperatingSystem.IsMacOS()
                ? "No Garmin Edge found. On a Mac only Edges that mount as a drive can be reached; newer ones use MTP.\n\n"
                : "No Garmin Edge found (drive letter or MTP).\n\n") +
                "Save the FIT files to a folder instead, so you can copy them into Garmin/NewFiles by hand?"))
            return;
        var folder = await Dialogs.PickFolder("Save workout FIT files", Trainer.Paths.ExportFolder) ?? Trainer.Paths.ExportFolder;
        var files = S.Fit.SaveToFolder(workouts, folder);
        Shell?.Toast($"{files.Count} workout files saved to {folder}.");
        Dialogs.OpenFolder(folder);
    }, "Sending this week's workouts…");

    /// <summary>Saves the next 7 days of workouts as Zwift .zwo files (default: Zwift's own workouts folder).</summary>
    [RelayCommand]
    private Task ExportWeekToZwo() => RunAsync(async () =>
    {
        var today = Trainer.Today;
        var workouts = Trainer.GetWorkouts(today, today.AddDays(6)).Where(w => w.Steps.Count > 0).ToList();
        if (workouts.Count == 0)
        {
            await Dialogs.Info("There are no workouts in the next 7 days.");
            return;
        }
        var zwift = ZwiftFolder.Find();
        var folder = await Dialogs.PickFolder(zwift is null
            ? "Save Zwift workouts (.zwo)"
            : "Save Zwift workouts (.zwo) — Zwift's workouts folder is preselected", zwift ?? Trainer.Paths.ExportFolder);
        if (folder is null) return;
        var files = ZwiftFolder.SaveAll(workouts, folder);
        Shell?.Toast($"{files.Count} .zwo files saved to {folder}." +
                     (zwift is not null && string.Equals(folder, zwift, StringComparison.OrdinalIgnoreCase) ? " Restart Zwift to see them under Custom Workouts." : ""));
    });
}
