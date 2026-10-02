using Avalonia.Media;
using Avalonia.Media.Immutable;
using Trainer.Core.Models;
using Trainer.Core.Training;

namespace Trainer.Desktop.Infrastructure;

/// <summary>Colours for zones, workout status and plan phases, shared by views and charts.</summary>
public static class Palette
{
    private static ImmutableSolidColorBrush B(string hex) => new(Color.Parse(hex));

    public static readonly IBrush Z1 = B("#9EA7AD");
    public static readonly IBrush Z2 = B("#3B8FD9");
    public static readonly IBrush Z3 = B("#3FAE6A");
    public static readonly IBrush SweetSpot = B("#B5C42B");
    public static readonly IBrush Z4 = B("#F2B01E");
    public static readonly IBrush Z5 = B("#EE7B26");
    public static readonly IBrush Z6 = B("#DE3B3B");
    public static readonly IBrush Z7 = B("#8E3BB5");
    public static readonly IBrush Free = B("#5E6B73");

    public static IBrush Zone(PowerZone z) => z switch
    {
        PowerZone.Z1Recovery => Z1,
        PowerZone.Z2Endurance => Z2,
        PowerZone.Z3Tempo => Z3,
        PowerZone.SweetSpot => SweetSpot,
        PowerZone.Z4Threshold => Z4,
        PowerZone.Z5Vo2Max => Z5,
        PowerZone.Z6Anaerobic => Z6,
        _ => Z7,
    };

    public static IBrush ForPower(double fractionOfFtp) => Zone(Zones.Of(fractionOfFtp));

    public static readonly IBrush Done = B("#2E9D57");
    public static readonly IBrush Partial = B("#E9A100");
    public static readonly IBrush Missed = B("#D64545");
    public static readonly IBrush Planned = B("#8A99A3");
    public static readonly IBrush Skipped = B("#D3DAE0");

    public static IBrush Status(WorkoutStatus s) => s switch
    {
        WorkoutStatus.Done => Done,
        WorkoutStatus.Partial => Partial,
        WorkoutStatus.Missed => Missed,
        WorkoutStatus.Skipped => Skipped,
        _ => Planned,
    };

    public static string PhaseHex(Phase p) => p switch
    {
        Core.Models.Phase.Base => "#5DA9E9",
        Core.Models.Phase.Build => "#F5A742",
        Core.Models.Phase.Specialty => "#E35D6A",
        Core.Models.Phase.Taper => "#56B870",
        Core.Models.Phase.Recovery => "#B88AD6",
        _ => "#A7B4BD",
    };

    public static IBrush Phase(Phase p) => B(PhaseHex(p));

    public static IBrush Kind(WorkoutKind k) => k switch
    {
        WorkoutKind.Recovery => Z1,
        WorkoutKind.Endurance or WorkoutKind.LongRide or WorkoutKind.EnduranceSurges => Z2,
        WorkoutKind.Tempo or WorkoutKind.LongTempo => Z3,
        WorkoutKind.SweetSpot => SweetSpot,
        WorkoutKind.Threshold or WorkoutKind.ThresholdClimbs or WorkoutKind.OverUnder => Z4,
        WorkoutKind.Vo2Max or WorkoutKind.Vo2Short or WorkoutKind.ThirtyThirty or WorkoutKind.RampTest => Z5,
        WorkoutKind.Anaerobic or WorkoutKind.AnaerobicStarts or WorkoutKind.RaceSim => Z6,
        WorkoutKind.Sprint => Z7,
        _ => Z2,
    };

    public static ScottPlot.Color Plot(string hex) => ScottPlot.Color.FromHex(hex);
}
