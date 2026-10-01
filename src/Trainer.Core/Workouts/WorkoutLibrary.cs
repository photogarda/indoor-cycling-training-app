using Trainer.Core.Models;

namespace Trainer.Core.Workouts;

/// <summary>The built-in workout library (60 templates), written in <see cref="IntervalNotation"/>.</summary>
public static class WorkoutLibrary
{
    private const string Wu = "wu 12m 50-70";
    private const string Cd = "cd 8m 50";

    public static List<WorkoutTemplate> BuiltIn()
    {
        var list = new List<WorkoutTemplate>();

        void Add(string name, WorkoutKind kind, string notation, string description,
            AthleteLevel min = AthleteLevel.Basic, AthleteLevel max = AthleteLevel.Pro)
        {
            list.Add(new WorkoutTemplate
            {
                Id = list.Count + 1,
                Name = name,
                Kind = kind,
                Steps = IntervalNotation.Parse(notation),
                Description = description,
                MinLevel = min,
                MaxLevel = max,
                BuiltIn = true,
            });
        }

        static int Rest(int workMin) => Math.Clamp(workMin / 2, 3, 8);

        // Recovery and endurance
        Add("Recovery spin", WorkoutKind.Recovery, "wu 5m 45; fill 30m 45-55; cd 5m 45",
            "Easy spin. Keep it truly easy: legs moving, no pressure.");
        Add("Endurance", WorkoutKind.Endurance, "wu 10m 50-65; fill 60m 65-72; cd 5m 50",
            "Steady zone 2. Builds aerobic base and fat use.");
        Add("Endurance cadence drills", WorkoutKind.Endurance,
            "wu 10m 50-65; fill 20m 65-70; 5x(2m 68 \"high cadence 100+ rpm\", 3m 65); fill 15m 65-70; cd 5m 50",
            "Zone 2 with high-cadence blocks for pedalling efficiency.");
        Add("Endurance with surges", WorkoutKind.EnduranceSurges,
            "wu 10m 50-65; fill 20m 65-72; 6x(30s 130 \"surge\", 4m30s 68); fill 15m 65-72; cd 5m 50",
            "Zone 2 with short race-like surges.");
        Add("Endurance with tempo bursts", WorkoutKind.EnduranceSurges,
            "wu 10m 50-65; fill 20m 65-72; 4x(5m 82-86 \"tempo\", 5m 65); fill 15m 65-72; cd 5m 50",
            "Zone 2 with tempo inserts.");

        // Long rides
        Add("Long endurance ride", WorkoutKind.LongRide, "wu 15m 50-65; fill 120m 65-72; cd 10m 50",
            "The week's long ride at steady zone 2. Eat and drink from the first hour.");
        Add("Long ride with tempo", WorkoutKind.LongRide,
            "wu 15m 50-65; fill 40m 65-72; 3x(15m 80-85 \"tempo\", 10m 68); fill 30m 65-72; cd 10m 50",
            "Long ride with tempo blocks in the middle.");
        Add("Fueling ride", WorkoutKind.LongRide, "wu 15m 50-65; fill 150m 65-75 \"eat 60–90 g carbs per hour\"; cd 10m 50",
            "Long steady ride to practise race fueling: 60–90 g carbs and 500–750 ml fluid per hour.");
        Add("Long ride race-pace finish", WorkoutKind.LongRide,
            "wu 15m 50-65; fill 100m 65-72; 2x(15m 88-92 \"race pace\", 5m 65); cd 10m 50",
            "Long ride finishing with race-pace efforts on tired legs.");

        // Tempo
        foreach (var (reps, min) in new[] { (2, 20), (3, 20) })
            Add($"Tempo {reps}×{min}", WorkoutKind.Tempo, $"{Wu}; {reps}x({min}m 78-86 \"tempo\", 5m 60); {Cd}",
                "Muscular endurance at tempo. Smooth, controlled pressure.");
        foreach (var (reps, min) in new[] { (1, 60), (2, 40) })
            Add(reps == 1 ? $"Long tempo {min}" : $"Long tempo {reps}×{min}", WorkoutKind.LongTempo,
                $"{Wu}; {reps}x({min}m 76-85 \"tempo\", 5m 60); {Cd}",
                "Long sustained tempo for gravel and long-distance events.");

        // Sweet spot
        foreach (var (reps, min) in new[] { (3, 10), (2, 15), (2, 20), (3, 15), (3, 20), (4, 15), (4, 20) })
            Add($"Sweet spot {reps}×{min}", WorkoutKind.SweetSpot,
                $"{Wu}; {reps}x({min}m 88-94 \"sweet spot\", {Rest(min)}m 55); {Cd}",
                "88–94 % FTP: lots of FTP stimulus for the fatigue it costs.");

        // Threshold
        foreach (var (reps, min) in new[] { (3, 8), (2, 15), (3, 12), (2, 20), (3, 15), (3, 20), (4, 15) })
            Add($"Threshold {reps}×{min}", WorkoutKind.Threshold,
                $"{Wu}; 3x(1m 100, 1m 55); {reps}x({min}m 95-100 \"threshold\", {Rest(min)}m 55); {Cd}",
                "Steady efforts at FTP. The core FTP builder.");
        foreach (var (reps, min) in new[] { (4, 6), (4, 8), (3, 12) })
            Add($"Threshold climbs {reps}×{min}", WorkoutKind.ThresholdClimbs,
                $"{Wu}; {reps}x({min}m 96-102 \"seated climb, 65–75 rpm\", {Rest(min)}m 55); {Cd}",
                "Threshold at low cadence to mimic long climbs.");

        // Over-unders
        foreach (var (sets, cycles) in new[] { (2, 3), (3, 3), (3, 4), (4, 5) })
            Add($"Over-unders {sets}×{cycles * 3}", WorkoutKind.OverUnder,
                $"{Wu}; {sets}x({cycles}x(2m 92-95 \"under\", 1m 105-108 \"over\"), 5m 55); {Cd}",
                "Alternating just under and just over FTP to clear lactate under load.");

        // VO2 max
        foreach (var (reps, min) in new[] { (3, 3), (4, 3), (5, 3) })
            Add($"VO2 3-min {reps}×3", WorkoutKind.Vo2Max, $"{Wu}; {reps}x(3m 110-120 \"VO2\", 3m 50); {Cd}",
                "3-minute VO2 max efforts. Hard but even; don't start too fast.");
        foreach (var (reps, min) in new[] { (4, 4), (5, 4), (5, 5), (6, 5) })
            Add($"VO2 {reps}×{min}", WorkoutKind.Vo2Max, $"{Wu}; {reps}x({min}m 106-115 \"VO2\", {min}m 50); {Cd}",
                "Classic VO2 max intervals. Raise the ceiling that FTP sits under.");
        foreach (var (sets, reps) in new[] { (2, 10), (3, 10), (3, 13) })
            Add($"30/15s {sets}×{reps}", WorkoutKind.Vo2Short,
                $"{Wu}; {sets}x({reps}x(30s 120-130 \"on\", 15s 50), 5m 50); {Cd}",
                "Short VO2 work (Rønnestad style): lots of time near VO2 max.");
        foreach (var (sets, reps) in new[] { (2, 8), (3, 8), (3, 10) })
            Add($"30/30s {sets}×{reps}", WorkoutKind.ThirtyThirty,
                $"{Wu}; {sets}x({reps}x(30s 125-135 \"on\", 30s 55), 5m 50); {Cd}",
                "30 s hard, 30 s easy. Repeatability for crits and cyclocross.");

        // Anaerobic, starts, sprints
        foreach (var reps in new[] { 6, 8, 10 })
            Add($"1-min repeats ×{reps}", WorkoutKind.Anaerobic, $"{Wu}; {reps}x(1m 130-140 \"anaerobic\", 3m 50); {Cd}",
                "1-minute anaerobic capacity repeats.");
        Add("2-min anaerobic ×6", WorkoutKind.Anaerobic, $"{Wu}; 6x(2m 120-125 \"hard\", 4m 50); {Cd}",
            "2-minute efforts above VO2 power.");
        foreach (var reps in new[] { 6, 8 })
            Add($"Standing starts ×{reps}", WorkoutKind.AnaerobicStarts,
                $"{Wu}; {reps}x(free 20s \"standing start, ERG off\", 1m40s 110 \"hold\", 4m 50); {Cd}",
                "Race starts: explosive launch, then settle into a hard pace.");
        foreach (var reps in new[] { 6, 8 })
            Add($"Sprints ×{reps}", WorkoutKind.Sprint, $"wu 15m 50-70; {reps}x(free 12s \"sprint, ERG off\", 4m48s 55); {Cd}",
                "Full sprints with full recovery. Turn ERG off for the sprint.");

        // Race simulation
        Add("Crit simulation", WorkoutKind.RaceSim,
            $"{Wu}; 3x(10x(free 15s \"corner sprint\", 45s 80), 5m 55); {Cd}",
            "Repeated accelerations out of corners at crit pace. ERG off.");
        Add("Road race simulation", WorkoutKind.RaceSim,
            $"{Wu}; 3x(free 30s \"attack\", 8m 92-98 \"hold\", free 1m \"bridge\", 5m 60); {Cd}",
            "Attacks and sustained pressure like a road race.");
        Add("Indoor race simulation", WorkoutKind.RaceSim,
            $"{Wu}; free 2m \"start\"; free 20m \"race pace, ERG off\"; 5m 55; free 5m \"finale\"; {Cd}",
            "Race-format effort with ERG off: hard start, sustained middle, finale.");

        // Openers and test
        Add("Openers", WorkoutKind.Opener,
            "wu 10m 50-65; fill 15m 65; 3x(1m 110-120 \"opener\", 3m 55); 2x(free 10s \"short sprint\", 2m50s 55); cd 5m 50",
            "Pre-race openers: short, sharp efforts to wake the legs up.");
        Add("Ramp test", WorkoutKind.RampTest, RampTestNotation(),
            "1-minute steps that rise until you can't hold the power. FTP = 75 % of your best 1-minute power.");
        return list;
    }

    private static string RampTestNotation()
    {
        var steps = new List<string> { "wu 5m 45-55", "rec 2m 50" };
        for (var pct = 60; pct <= 150; pct += 6)
            steps.Add($"1m {pct} \"ramp – hold until failure\"");
        steps.Add("cd 10m 40");
        return string.Join("; ", steps);
    }
}
