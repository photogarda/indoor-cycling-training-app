using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.Desktop.Infrastructure;
using Trainer.Core.Models;

namespace Trainer.Desktop.ViewModels;

public record ComplianceRow(Data.Services.WeekCompliance C)
{
    public string Week => C.WeekStart.ToString("d MMM");
    public string Planned => $"{C.PlannedTss:0}";
    public string Actual => $"{C.ActualTss:0}";
    public string Percent => C.PlannedTss > 0 ? $"{C.ActualTss / C.PlannedTss * 100:0} %" : "";
    public double Fraction => C.PlannedTss > 0 ? Math.Min(1.2, C.ActualTss / C.PlannedTss) : 0;
    public string Workouts => $"{C.Done} done · {C.Partial} partial · {C.Missed} missed of {C.Planned}";
}

public partial class AnalysisViewModel : ViewModelBase
{
    public override string Title => "Analysis";

    public int[] Ranges { get; } = [42, 90, 180, 365, 730];
    [ObservableProperty] private int _rangeDays = 180;
    [ObservableProperty] private int _chartVersion;
    [ObservableProperty] private string _formText = "";
    [ObservableProperty] private string _ftpText = "";
    [ObservableProperty] private int? _estimate;

    public ObservableCollection<ComplianceRow> Compliance { get; } = [];
    public List<DailyLoad> Loads { get; private set; } = [];
    public List<Race> Races { get; private set; } = [];
    public Dictionary<int, double> CurveSixWeeks { get; private set; } = [];
    public Dictionary<int, double> CurveAllTime { get; private set; } = [];

    partial void OnRangeDaysChanged(int value) => Load();

    public override void Load()
    {
        var today = Trainer.Today;
        Loads = Trainer.GetLoads(today.AddDays(-RangeDays));
        Races = Trainer.GetRaces().Where(r => r.Date >= today.AddDays(-RangeDays) && r.Date <= today.AddDays(14)).ToList();
        (CurveSixWeeks, CurveAllTime) = Trainer.GetPowerCurves();

        var last = Loads.LastOrDefault(l => l.Date <= today);
        FormText = last is null
            ? "No rides imported yet."
            : $"Today: fitness (CTL) {last.Ctl:0} · fatigue (ATL) {last.Atl:0} · form (TSB) {last.Tsb:+0;-0;0}" +
              (last.Tsb < -30 ? " — very fatigued: the plan eases off after 3 days like this." : last.Tsb > 15 ? " — fresh." : "");

        var ftp = Trainer.CurrentFtp();
        Estimate = Trainer.EstimateFtp();
        FtpText = $"Current FTP {ftp} W" + (Estimate is { } e ? $" · estimate from the last 6 weeks: {e} W (95 % of best 20 min)" : " · no 20-minute power in the last 6 weeks to estimate from");

        Compliance.Clear();
        foreach (var c in Trainer.GetCompliance(12).OrderByDescending(c => c.WeekStart)) Compliance.Add(new ComplianceRow(c));
        ChartVersion++;
    }

    [RelayCommand]
    private Task UseEstimate() => RunAsync(async () =>
    {
        if (Estimate is not { } e || !await Dialogs.Confirm($"Set FTP to {e} W from your recent rides? Future workout watts change and the plan is regenerated.")) return;
        await Task.Run(() => Trainer.AddFtp(e, FtpMethod.Estimate));
        Shell?.Toast($"FTP set to {e} W.");
    }, "Updating FTP…");
}
