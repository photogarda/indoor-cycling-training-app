using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.Core.Models;
using Trainer.Core.Planning;

namespace Trainer.Desktop.ViewModels;

public record WeekRow(PlanWeek Week, string? RaceName, double PlannedHours)
{
    public string Start => Week.WeekStart.ToString("d MMM yyyy");
    public Phase Phase => Week.Phase;
    public string Type => Week.Phase is Phase.Taper ? "Taper" : Week.WeekType == WeekType.Recovery ? "Recovery" : "Load";
    public string Hours => $"{Week.TargetHours:0.0} h";
    public string Planned => PlannedHours > 0 ? $"{PlannedHours:0.0} h" : "";
    public string Tss => $"{Week.TargetTss:0}";
    public string Ctl => $"{Week.PlannedCtl:0}";
    public string Race => RaceName ?? "";
    public string Tooltip => $"Week of {Start}: {Phase}, {Type.ToLowerInvariant()}\n{Hours} target · {Tss} TSS · CTL {Ctl}{(RaceName is null ? "" : "\n🏁 " + RaceName)}";
}

public record PhaseBlock(Phase Phase, int Weeks, DateOnly From)
{
    public string Label => Weeks >= 2 ? $"{Phase} ({Weeks})" : "";
    public double Width => Math.Max(8, Weeks * 18.0);
    public string Tooltip => $"{Phase}: {Weeks} week{(Weeks == 1 ? "" : "s")} from {From:d MMM}";
}

public partial class PlanOverviewViewModel : ViewModelBase
{
    public override string Title => "Plan overview";

    public ObservableCollection<WeekRow> Weeks { get; } = [];
    public ObservableCollection<PhaseBlock> Blocks { get; } = [];
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private int _chartVersion;
    [ObservableProperty] private DateTime? _periodFrom;
    [ObservableProperty] private DateTime? _periodTo;
    [ObservableProperty] private string _periodText = "";

    public List<DailyLoad> ActualLoads { get; private set; } = [];
    public List<Race> Races { get; private set; } = [];

    public override void Load()
    {
        var overview = Trainer.GetPlanOverview();
        Races = overview.Races;
        var raceById = overview.Races.ToDictionary(r => r.Id);
        var workouts = Trainer.GetWorkouts(overview.Weeks.FirstOrDefault()?.WeekStart ?? Trainer.Today, overview.Plan?.EndDate ?? Trainer.Today);
        var hoursByWeek = workouts.GroupBy(w => PlanEngine.WeekStart(w.Date)).ToDictionary(g => g.Key, g => g.Sum(w => w.DurationSec) / 3600.0);

        Weeks.Clear();
        foreach (var w in overview.Weeks)
            Weeks.Add(new WeekRow(w, w.RaceId is { } id && raceById.TryGetValue(id, out var r) ? $"{r.Name} ({r.Priority})" : null,
                hoursByWeek.GetValueOrDefault(w.WeekStart)));

        Blocks.Clear();
        foreach (var w in overview.Weeks)
        {
            if (Blocks.Count > 0 && Blocks[^1].Phase == w.Phase) Blocks[^1] = Blocks[^1] with { Weeks = Blocks[^1].Weeks + 1 };
            else Blocks.Add(new PhaseBlock(w.Phase, 1, w.WeekStart));
        }

        var target = overview.Plan?.TargetRaceId is { } t && raceById.TryGetValue(t, out var tr) ? tr : null;
        Headline = overview.Plan is null
            ? "No plan yet. Set your FTP and weekly hours in Settings."
            : target is null
                ? $"No A race on the calendar: rolling 8-week blocks ({Trainer.GetAthlete().NoRaceGoal} goal) through {overview.Plan.EndDate:d MMM yyyy}."
                : $"Building to {target.Name} on {target.Date:d MMM yyyy} ({(target.Date.DayNumber - Trainer.Today.DayNumber) / 7} weeks). Plan generated {overview.Plan.CreatedAt:g}.";
        var athlete = Trainer.GetAthlete();
        PeriodFrom = athlete.PlanStartDate?.ToDateTime(TimeOnly.MinValue);
        PeriodTo = athlete.PlanEndDate?.ToDateTime(TimeOnly.MinValue);
        PeriodText = (athlete.PlanStartDate, athlete.PlanEndDate) switch
        {
            (null, null) => "Automatic: from today until your last A race (at least 8 weeks ahead).",
            ({ } f, null) => $"From {f:d MMM yyyy}, then automatic.",
            (null, { } until) => $"From today until {until:d MMM yyyy}.",
            ({ } f, { } until) => $"From {f:d MMM yyyy} to {until:d MMM yyyy} ({(until.DayNumber - f.DayNumber + 1) / 7} weeks).",
        };
        ActualLoads = Trainer.GetLoads(Trainer.Today.AddDays(-84));
        ChartVersion++;
    }

    /// <summary>Saves the plan period and regenerates. Either date may be empty (automatic).</summary>
    [RelayCommand]
    private Task SavePeriod() => RunAsync(async () =>
    {
        DateOnly? from = PeriodFrom is { } f ? DateOnly.FromDateTime(f) : null;
        DateOnly? to = PeriodTo is { } t ? DateOnly.FromDateTime(t) : null;
        await Task.Run(() => Trainer.SetPlanPeriod(from, to));
        Shell?.Toast("Plan period saved. The plan was regenerated for it.");
    }, "Re-planning for the new period…");

    [RelayCommand]
    private Task AutomaticPeriod() => RunAsync(async () =>
    {
        await Task.Run(() => Trainer.SetPlanPeriod(null, null));
        Shell?.Toast("Plan period set back to automatic.");
    }, "Re-planning…");
}
