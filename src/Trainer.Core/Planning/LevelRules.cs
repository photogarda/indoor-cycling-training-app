using Trainer.Core.Models;

namespace Trainer.Core.Planning;

/// <summary>Per-level limits from the athlete level table.</summary>
public static class LevelRules
{
    public static double CtlRampPerWeek(AthleteLevel l) => l switch
    {
        AthleteLevel.Basic => 3,
        AthleteLevel.Amateur => 5,
        AthleteLevel.Advanced => 6,
        _ => 8,
    };

    public static int MaxThresholdMinutes(AthleteLevel l) => l switch
    {
        AthleteLevel.Basic => 30,
        AthleteLevel.Amateur => 45,
        AthleteLevel.Advanced => 60,
        _ => 80,
    };

    public static int MaxVo2Minutes(AthleteLevel l) => l switch
    {
        AthleteLevel.Basic => 12,
        AthleteLevel.Amateur => 18,
        AthleteLevel.Advanced => 24,
        _ => 30,
    };

    /// <summary>Weeks in one load/recovery cycle: 3 + 1, or 2 + 1 for basic.</summary>
    public static int CycleLength(AthleteLevel l) => l == AthleteLevel.Basic ? 3 : 4;

    /// <summary>Planned length of a key session before the week is split.</summary>
    public static double KeySessionHours(AthleteLevel l) => l switch
    {
        AthleteLevel.Basic => 1.0,
        AthleteLevel.Amateur => 1.25,
        AthleteLevel.Advanced => 1.5,
        _ => 1.75,
    };
}
