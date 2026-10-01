using System.Windows.Media;
using Trainer.Core.Models;
using Trainer.Core.Training;

namespace Trainer.App.Infrastructure;

/// <summary>Colours for zones, workout status and plan phases, shared by views and charts.</summary>
public static class Palette
{
    private static SolidColorBrush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly SolidColorBrush Z1 = B("#9EA7AD");
    public static readonly SolidColorBrush Z2 = B("#3B8FD9");
    public static readonly SolidColorBrush Z3 = B("#3FAE6A");
    public static readonly SolidColorBrush SweetSpot = B("#B5C42B");
    public static readonly SolidColorBrush Z4 = B("#F2B01E");
    public static readonly SolidColorBrush Z5 = B("#EE7B26");
    public static readonly SolidColorBrush Z6 = B("#DE3B3B");
    public static readonly SolidColorBrush Z7 = B("#8E3BB5");
    public static readonly SolidColorBrush Free = B("#5E6B73");

    public static SolidColorBrush Zone(PowerZone z) => z switch
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

    public static SolidColorBrush ForPower(double fractionOfFtp) => Zone(Zones.Of(fractionOfFtp));

    public static readonly SolidColorBrush Done = B("#2E9D57");
    public static readonly SolidColorBrush Partial = B("#E9A100");
    public static readonly SolidColorBrush Missed = B("#D64545");
    public static readonly SolidColorBrush Planned = B("#8A99A3");

    public static SolidColorBrush Status(WorkoutStatus s) => s switch
    {
        WorkoutStatus.Done => Done,
        WorkoutStatus.Partial => Partial,
        WorkoutStatus.Missed => Missed,
        _ => Planned,
    };

    public static SolidColorBrush Phase(Phase p) => p switch
    {
        Core.Models.Phase.Base => B("#5DA9E9"),
        Core.Models.Phase.Build => B("#F5A742"),
        Core.Models.Phase.Specialty => B("#E35D6A"),
        Core.Models.Phase.Taper => B("#56B870"),
        Core.Models.Phase.Recovery => B("#B88AD6"),
        _ => B("#A7B4BD"),
    };

    public static SolidColorBrush Kind(WorkoutKind k) => k switch
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

    public static ScottPlot.Color Plot(SolidColorBrush b) => new(b.Color.R, b.Color.G, b.Color.B, b.Color.A);
    public static ScottPlot.Color Plot(string hex) => ScottPlot.Color.FromHex(hex);
}
