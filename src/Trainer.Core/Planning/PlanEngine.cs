using Trainer.Core.Models;
using Trainer.Core.Training;
using Trainer.Core.Workouts;

namespace Trainer.Core.Planning;

/// <summary>
/// Rule-based plan engine. Counts back from each A race to give every week a phase
/// (base / build / specialty / taper), then fills each week's days from the workout library.
/// Deterministic: the same input always gives the same plan.
/// </summary>
public static class PlanEngine
{
    public static PlanResult Generate(PlanInput input) => new Builder(input).Run();

    /// <summary>Monday on or before the date (weeks start Monday).</summary>
    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    internal sealed class WeekSpec
    {
        public DateOnly Start;
        public Phase Phase = Phase.General;
        public WeekType Type = WeekType.Load;
        public double HoursFactor = 1;
        /// <summary>Position inside a load block (0 = first load week).</summary>
        public int LoadPos;
        /// <summary>Rolling no-race weeks: index inside the 2-cycle block (FTP progression).</summary>
        public int BlockIndex;
        public bool Anchored;
        public int Step;
        public int PhaseLoadWeeks = 1;
        public double? ProgressOverride;
        public Race? TargetRace;
        public bool RampTest;
        public bool RepeatsLoad;

        public double Progress(int cycle)
        {
            if (ProgressOverride is { } p) return p;
            if (Anchored)
            {
                var load = cycle - 1;
                var half = BlockIndex / cycle;
                return Math.Clamp((half * load + LoadPos) / (double)Math.Max(1, 2 * load - 1), 0, 1);
            }
            return PhaseLoadWeeks <= 1 ? 0.5 : Math.Clamp(Step / (double)(PhaseLoadWeeks - 1), 0, 1);
        }
    }

    private enum DayRole { None = 0, NoKey = 10, Easy = 30, RecoveryRide = 40, Opener = 50, Race = 100 }

    private sealed class Slot
    {
        public required DateOnly Date;
        public required WorkoutTemplate Template;
        public double Hours;
        public bool IsKey;
        public bool IsLong;
        public bool IsEndurance;
        public string? Note;
        public PlannedWorkout? Built;
    }

    private sealed class Builder
    {
        private readonly PlanInput _in;
        private readonly Athlete _athlete;
        private readonly AthleteLevel _level;
        private readonly int _cycle;
        private readonly TemplatePicker _picker;
        private readonly HashSet<DateOnly> _blocked;
        private readonly List<PlannedWorkout> _frozen;
        private readonly HashSet<DateOnly> _frozenDates;
        private readonly Dictionary<DateOnly, DayRole> _roles = [];
        private readonly HashSet<DateOnly> _keyDates = [];
        private readonly HashSet<DayOfWeek> _trainingDays;
        private readonly PlanResult _result;
        private readonly DateOnly _start;
        private readonly WorkoutTemplate _fallbackEndurance;

        public Builder(PlanInput input)
        {
            _in = input;
            _athlete = input.Athlete;
            _level = _athlete.Level;
            _cycle = LevelRules.CycleLength(_level);
            _start = input.Start;
            _picker = new TemplatePicker(input.Templates);
            _blocked = input.BlockedDays.Select(b => b.Date).ToHashSet();
            _frozen = input.Existing.Where(w => !w.Superseded && (w.Date < _start || w.Locked)).ToList();
            _frozenDates = _frozen.Where(w => w.Date >= _start).Select(w => w.Date).ToHashSet();
            _trainingDays = _athlete.TrainingDays.ToHashSet();
            _fallbackEndurance = new WorkoutTemplate
            {
                Id = 0, Name = "Endurance", Kind = WorkoutKind.Endurance,
                Steps = IntervalNotation.Parse("wu 10m 50-65; fill 60m 65-72; cd 5m 50"),
            };

            var target = input.Races.Where(r => r.Priority == RacePriority.A && r.Date >= _start).MinBy(r => r.Date);
            _result = new PlanResult { Start = _start, TargetRaceId = target?.Id };
        }

        public PlanResult Run()
        {
            var week0 = WeekStart(_start);
            var aRaces = _in.Races.Where(r => r.Priority == RacePriority.A && r.Date >= week0.AddDays(-7))
                .OrderBy(r => r.Date).ToList();

            var end = _start.AddDays(7 * _in.MinWeeksAhead - 1);
            var futureA = aRaces.Where(r => r.Date >= _start).ToList();
            if (futureA.Count > 0 && futureA[^1].Date.AddDays(7) > end) end = futureA[^1].Date.AddDays(7);
            var lastAny = _in.Races.Where(r => r.Date >= _start).Select(r => r.Date).DefaultIfEmpty(_start).Max();
            if (lastAny > end) end = lastAny;
            if (end > _start.AddYears(2)) end = _start.AddYears(2);
            var endWeek = WeekStart(end);

            var t0 = week0.AddDays(-7);
            if (_in.SeasonStart is { } season && WeekStart(season) < t0) t0 = WeekStart(season);
            if (t0 < week0.AddDays(-7 * 60)) t0 = week0.AddDays(-7 * 60);

            var weeks = new List<DateOnly>();
            for (var w = t0; w <= endWeek; w = w.AddDays(7)) weeks.Add(w);
            var specs = BuildTimeline(weeks, aRaces);
            ApplyProgression(specs);
            var i0 = weeks.IndexOf(week0);
            ApplyBadWeeks(specs, i0);
            ScheduleRampTests(specs, i0);
            ComputeRoles();

            foreach (var w in _frozen.Where(w => w.IsKey && w.Status != WorkoutStatus.Missed)) _keyDates.Add(w.Date);
            foreach (var r in _in.Races.Where(r => r.Priority != RacePriority.A)) _keyDates.Add(r.Date);

            var ctl = StartingCtl();
            for (var i = i0; i < specs.Count; i++) ctl = FillWeek(specs[i], ctl);

            ApplyFatigueGuard();
            return new PlanResultBuilder(_result, endWeek.AddDays(6)).Result;
        }

        // ---------- Timeline: phases and load/recovery weeks ----------

        private List<WeekSpec> BuildTimeline(List<DateOnly> weeks, List<Race> aRaces)
        {
            var specs = weeks.Select(w => new WeekSpec { Start = w }).ToList();
            var n = specs.Count;
            var segStart = 0;
            Race? prev = null;
            var prevR = -1;

            foreach (var race in aRaces)
            {
                var r = weeks.IndexOf(WeekStart(race.Date));
                if (r < 0 || r < segStart) continue;
                var taperLen = race.DurationHours > 4 ? 2 : 1;
                var taperStart = Math.Max(segStart, r - taperLen + 1);
                var available = taperStart - segStart;
                var first = prev is null;
                var spec = available >= 14 ? 6 : Math.Min(4, available);
                var build = Math.Min(8, available - spec);
                var baseLen = !first && r - prevR < 16 ? 0 : Math.Min(12, available - spec - build);
                var general = available - spec - build - baseLen;

                var idx = segStart;
                void Assign(int count, Phase phase)
                {
                    for (var c = 0; c < count; c++, idx++)
                    {
                        var s = specs[idx];
                        s.Phase = phase;
                        s.TargetRace = race;
                        var k = taperStart - idx;
                        var rr = k % _cycle;
                        s.Type = rr == 0 ? WeekType.Recovery : WeekType.Load;
                        s.LoadPos = _cycle - 1 - rr;
                    }
                }
                Assign(general, Phase.General);
                Assign(baseLen, Phase.Base);
                Assign(build, Phase.Build);
                Assign(spec, Phase.Specialty);
                for (var t = taperStart; t <= r; t++)
                {
                    specs[t].Phase = Phase.Taper;
                    specs[t].TargetRace = race;
                    specs[t].Type = WeekType.Load;
                    // Volume down 40–50 %: a 2-week taper steps down 0.6 → 0.5, a 1-week taper sits at 0.55.
                    specs[t].HoursFactor = r - taperStart == 1 && t == taperStart ? 0.6 : (taperLen == 2 ? 0.5 : 0.55);
                }
                if (r + 1 < n)
                {
                    specs[r + 1].Phase = Phase.Recovery;
                    specs[r + 1].Type = WeekType.Recovery;
                    specs[r + 1].HoursFactor = 0.5;
                    specs[r + 1].TargetRace = race;
                }
                segStart = r + 2;
                prev = race;
                prevR = r;
            }

            // Rolling no-race blocks, anchored to a fixed date so regenerating never shifts recovery weeks.
            if (segStart < n)
            {
                var anchor = prev is null ? WeekStart(_athlete.PlanAnchor) : weeks[segStart];
                for (var i = segStart; i < n; i++)
                {
                    var s = specs[i];
                    var idx = (s.Start.DayNumber - anchor.DayNumber) / 7;
                    var rr = Mod(idx, _cycle);
                    s.Phase = Phase.General;
                    s.Anchored = true;
                    s.Type = rr == _cycle - 1 ? WeekType.Recovery : WeekType.Load;
                    s.LoadPos = rr;
                    s.BlockIndex = Mod(idx, 2 * _cycle);
                }
            }

            foreach (var s in specs.Where(s => s.Phase is not (Phase.Taper or Phase.Recovery)))
            {
                if (s.Type == WeekType.Recovery) s.HoursFactor = 0.6;
                else s.HoursFactor = (_cycle - 1) switch
                {
                    3 => new[] { 0.92, 1.0, 1.08 }[Math.Clamp(s.LoadPos, 0, 2)],
                    _ => new[] { 0.95, 1.05 }[Math.Clamp(s.LoadPos, 0, 1)],
                };
            }
            return specs;
        }

        private static void ApplyProgression(List<WeekSpec> specs)
        {
            var i = 0;
            while (i < specs.Count)
            {
                var j = i;
                while (j < specs.Count && specs[j].Phase == specs[i].Phase && specs[j].TargetRace == specs[i].TargetRace) j++;
                var step = 0;
                for (var k = i; k < j; k++)
                {
                    specs[k].Step = step;
                    if (specs[k].Type == WeekType.Load) step++;
                }
                for (var k = i; k < j; k++) specs[k].PhaseLoadWeeks = step;
                i = j;
            }
        }

        private void ApplyBadWeeks(List<WeekSpec> specs, int i0)
        {
            for (var i = Math.Max(1, i0); i < specs.Count; i++)
            {
                var cur = specs[i];
                var prev = specs[i - 1];
                if (!_in.BadWeeks.Contains(prev.Start)) continue;
                if (cur.Type != WeekType.Load || prev.Type != WeekType.Load) continue;
                if (cur.Phase is Phase.Taper or Phase.Recovery || prev.Phase is Phase.Taper or Phase.Recovery) continue;
                cur.HoursFactor = prev.HoursFactor;
                cur.ProgressOverride = prev.Progress(_cycle);
                cur.RepeatsLoad = true;
                _result.Notes.Add($"Week of {cur.Start:d MMM} repeats last week's load: last week was under plan.");
            }
        }

        private void ScheduleRampTests(List<WeekSpec> specs, int i0)
        {
            DateOnly? last = _in.FtpHistory.Where(f => f.Method == FtpMethod.Ramp).Select(f => (DateOnly?)f.Date).Max();
            var frozenTest = _frozen.Where(w => w.Kind == WorkoutKind.RampTest && w.Status != WorkoutStatus.Missed)
                .Select(w => (DateOnly?)w.Date).Max();
            if (frozenTest > last || last is null) last = frozenTest ?? last;

            for (var i = i0; i < specs.Count; i++)
            {
                var s = specs[i];
                if (s.Phase is Phase.Taper or Phase.Recovery) continue;
                var weekEnd = s.Start.AddDays(6);
                if (_frozen.Any(w => w.Kind == WorkoutKind.RampTest && w.Date >= s.Start && w.Date <= weekEnd)) continue;

                // No measured FTP yet (none, or only a guess/estimate): start with a ramp test.
                var due = i == i0 && last is null && _in.FtpHistory.All(f => f.Method == FtpMethod.Estimate);
                if (!due && i > 0 && specs[i - 1].Type == WeekType.Recovery)
                    due = last is null || s.Start.DayNumber - last.Value.DayNumber >= 24;
                if (!due) continue;
                s.RampTest = true;
                last = s.Start;
            }
        }

        private void ComputeRoles()
        {
            void Set(DateOnly d, DayRole role)
            {
                if (!_roles.TryGetValue(d, out var cur) || role > cur) _roles[d] = role;
            }
            foreach (var r in _in.Races)
            {
                Set(r.Date, DayRole.Race);
                switch (r.Priority)
                {
                    case RacePriority.A:
                        Set(r.Date.AddDays(-1), DayRole.Opener);
                        Set(r.Date.AddDays(-2), DayRole.Easy);
                        Set(r.Date.AddDays(1), DayRole.RecoveryRide);
                        for (var d = r.Date.AddDays(2); d <= WeekStart(r.Date).AddDays(6); d = d.AddDays(1)) Set(d, DayRole.NoKey);
                        break;
                    case RacePriority.B:
                        Set(r.Date.AddDays(-1), DayRole.Opener);
                        Set(r.Date.AddDays(-2), DayRole.Easy);
                        Set(r.Date.AddDays(-3), DayRole.Easy);
                        Set(r.Date.AddDays(1), DayRole.RecoveryRide);
                        break;
                    case RacePriority.C:
                        Set(r.Date.AddDays(-1), DayRole.NoKey);
                        Set(r.Date.AddDays(1), DayRole.NoKey);
                        break;
                }
            }
        }

        private DayRole Role(DateOnly d) => _roles.TryGetValue(d, out var r) ? r : DayRole.None;

        // ---------- Filling a week ----------

        private double FillWeek(WeekSpec spec, double ctl)
        {
            var days = Enumerable.Range(0, 7).Select(spec.Start.AddDays).ToList();
            var weekEnd = days[^1];
            var progress = spec.Progress(_cycle);
            var race = spec.TargetRace;
            var frozenInWeek = _frozen.Where(w => w.Date >= spec.Start && w.Date <= weekEnd).ToList();
            var racesInWeek = _in.Races.Where(r => r.Date >= spec.Start && r.Date <= weekEnd).ToList();
            var aRaceThisWeek = racesInWeek.FirstOrDefault(r => r.Priority == RacePriority.A);

            var hours = _athlete.WeeklyHours * spec.HoursFactor;
            // A plan created mid-week only plans the share of the week that is left.
            var trainingDaysInWeek = days.Count(d => _trainingDays.Contains(d.DayOfWeek));
            var pastEmpty = days.Count(d => d < _start && _trainingDays.Contains(d.DayOfWeek) && frozenInWeek.All(w => w.Date != d));
            if (trainingDaysInWeek > 0 && pastEmpty > 0) hours *= 1 - (double)pastEmpty / trainingDaysInWeek;

            var budget = hours - frozenInWeek.Sum(w => w.DurationSec) / 3600.0
                               - racesInWeek.Where(r => r.Date >= _start).Sum(r => r.DurationHours);
            var slots = new List<Slot>();
            bool Open(DateOnly d) => d >= _start && !_blocked.Contains(d) && !_frozenDates.Contains(d);

            // 1. Race-driven days: openers, recovery rides, easy mini-taper days.
            foreach (var d in days.Where(Open))
            {
                switch (Role(d))
                {
                    case DayRole.Opener:
                        slots.Add(new Slot { Date = d, Template = Pick(WorkoutKind.Opener), Hours = 1.0, Note = "Day before the race" });
                        break;
                    case DayRole.RecoveryRide:
                        slots.Add(new Slot { Date = d, Template = Pick(WorkoutKind.Recovery), Hours = 0.75, Note = "Day after the race" });
                        break;
                    case DayRole.Easy when _trainingDays.Contains(d.DayOfWeek):
                        slots.Add(new Slot { Date = d, Template = Pick(WorkoutKind.Endurance), Hours = 1.0, IsEndurance = true, Note = "Easy before the race" });
                        break;
                }
            }
            budget -= slots.Sum(s => s.Hours);

            var avail = days.Where(d => Open(d) && _trainingDays.Contains(d.DayOfWeek) && Role(d) < DayRole.Easy
                                         && slots.All(s => s.Date != d)).ToList();
            bool KeyOk(DateOnly d) => Role(d) != DayRole.NoKey;

            // 2. Key budget.
            var keysWanted = KeysFor(spec, aRaceThisWeek is not null);
            var satisfied = frozenInWeek.Count(w => w.IsKey && w.Status != WorkoutStatus.Missed)
                            + racesInWeek.Count(r => r.Priority != RacePriority.A);
            var keysNeeded = Math.Max(0, keysWanted - satisfied);
            var missedKeys = frozenInWeek.Where(w => w.IsKey && w.Status == WorkoutStatus.Missed && w.Date < _start).ToList();

            // 3. Ramp test on the first free day after a recovery week.
            if (spec.RampTest)
            {
                var testDay = avail.FirstOrDefault(d => KeyOk(d) && !Adjacent(d, _keyDates)
                                                        && !_in.Races.Any(r => r.Priority == RacePriority.A && Math.Abs(r.Date.DayNumber - d.DayNumber) < 10));
                if (testDay != default)
                {
                    var t = Pick(WorkoutKind.RampTest);
                    slots.Add(new Slot { Date = testDay, Template = t, Hours = t.Steps.Sum(s => s.TotalSeconds) / 3600.0, IsKey = true, Note = "Monthly FTP retest" });
                    avail.Remove(testDay);
                    _keyDates.Add(testDay);
                    keysNeeded = Math.Max(0, keysNeeded - 1);
                    budget -= slots[^1].Hours;
                }
            }

            // 4. Long ride on the preferred day.
            var longDays = new List<DateOnly>();
            var longHours = 0.0;
            var hasLong = frozenInWeek.Any(w => w.Kind == WorkoutKind.LongRide);
            // A B or C race is the week's big day, so it replaces the long ride.
            var minorRace = racesInWeek.Any(r => r.Priority != RacePriority.A);
            if (!hasLong && !minorRace && _trainingDays.Count >= 2 && aRaceThisWeek is null && avail.Count > 0)
            {
                longHours = hours * (spec.Phase == Phase.Recovery ? 0.35 : 0.32);
                string? hint = spec.Phase is Phase.Build || (spec.Phase is Phase.Base or Phase.General && progress >= 0.5)
                    ? "tempo" : "Long endurance";
                if (spec.Type == WeekType.Recovery || spec.Phase is Phase.Recovery or Phase.Taper) hint = "Long endurance";
                if (spec.Phase == Phase.Specialty && race is not null && spec.Type == WeekType.Load)
                {
                    var (h, raceHint) = RaceRules.SpecialtyLongRide(race, longHours);
                    longHours = h;
                    hint = raceHint ?? "Long endurance";
                }
                if (spec.Phase == Phase.Taper) longHours = Math.Min(longHours, Math.Max(1.5, hours * 0.3));
                longHours = Math.Clamp(longHours, 1.0, Math.Max(1.0, hours * 0.5));

                var longDay = ChooseLongDay(avail, KeyOk);
                longDays.Add(longDay);
                avail.Remove(longDay);
                slots.Add(new Slot { Date = longDay, Template = Pick(WorkoutKind.LongRide, hint), Hours = longHours, IsLong = true });

                if (spec.Phase == Phase.Specialty && race?.Type == EventType.StageRace && spec.Type == WeekType.Load)
                {
                    var second = avail.Where(d => Math.Abs(d.DayNumber - longDay.DayNumber) == 1).OrderBy(d => d).FirstOrDefault();
                    if (second != default)
                    {
                        avail.Remove(second);
                        longDays.Add(second);
                        slots.Add(new Slot { Date = second, Template = Pick(WorkoutKind.LongRide, "tempo"), Hours = Math.Min(4, longHours * 0.8), IsLong = true, Note = "Stage race: back-to-back long days" });
                    }
                }
                budget -= slots.Where(s => s.IsLong).Sum(s => s.Hours);
            }

            // 5. Key sessions, never on consecutive days.
            var keyDays = ChooseKeyDays(avail.Where(KeyOk).ToList(), keysNeeded, longDays);
            if (keyDays.Count < keysNeeded)
            {
                foreach (var m in missedKeys.Skip(keyDays.Count))
                    _result.Notes.Add($"Missed {m.Name} on {m.Date:ddd d MMM} was dropped: no free day left that week.");
            }
            var keyHours = spec.Phase == Phase.Taper || spec.Type == WeekType.Recovery ? 1.0 : LevelRules.KeySessionHours(_level);
            var menu = KeyMenu(spec, progress);
            var keyProgress = spec.Phase == Phase.Taper || spec.Type == WeekType.Recovery ? -1 : progress;
            for (var j = 0; j < keyDays.Count; j++)
            {
                WorkoutTemplate t;
                string? note = null;
                if (j < missedKeys.Count && FindTemplate(missedKeys[j].TemplateId) is { } moved)
                {
                    t = moved;
                    note = $"Moved from {missedKeys[j].Date:ddd d MMM} (missed)";
                }
                else
                {
                    var slot = menu[(j - Math.Min(j, missedKeys.Count)) % menu.Count];
                    t = Pick(slot.Kind, slot.Hint, keyProgress);
                }
                slots.Add(new Slot { Date = keyDays[j], Template = t, Hours = keyHours, IsKey = t.Kind.IsKey(), Note = note });
                _keyDates.Add(keyDays[j]);
            }
            budget -= keyDays.Count * keyHours;

            // 6. Endurance rides fill the rest of the hours.
            var endDays = avail.Except(keyDays).ToList();
            var maxEndurance = spec.Phase == Phase.Taper || aRaceThisWeek is not null ? 1.5
                : minorRace ? 2.0
                : Math.Max(1.5, longHours > 0 ? longHours : 4);
            if (endDays.Count > 0 && budget >= 0.5)
            {
                while (endDays.Count > 1 && budget / endDays.Count < 0.75)
                {
                    var drop = endDays.OrderByDescending(d => HardNeighbours(d, slots)).ThenByDescending(d => d).First();
                    endDays.Remove(drop);
                }
                var per = Math.Min(budget / endDays.Count, maxEndurance);
                foreach (var d in endDays)
                {
                    var (kind, hint) = EnduranceFlavour(spec, progress, race);
                    // Days next to a C race stay short.
                    var h = Role(d) == DayRole.NoKey ? Math.Min(per, 1.0) : per;
                    slots.Add(new Slot { Date = d, Template = Pick(kind, hint, 0, d.DayNumber / 7 + (int)d.DayOfWeek), Hours = h, IsEndurance = true });
                    budget -= h;
                }
            }

            // Hours left over (few days, many hours): lengthen key sessions to 2 h, then the long ride.
            if (budget > 0.25 && spec.Type == WeekType.Load && spec.Phase is not (Phase.Taper or Phase.Recovery))
            {
                var keys = slots.Where(s => s.IsKey && s.Template.Kind != WorkoutKind.RampTest).ToList();
                foreach (var k in keys)
                {
                    var add = Math.Min(budget / keys.Count, Math.Max(0, 2.0 - k.Hours));
                    k.Hours += add;
                }
                budget -= keys.Sum(k => k.Hours) - keys.Count * keyHours;
                // In specialty the long ride stays sized to the race.
                var longSlot = slots.FirstOrDefault(s => s.IsLong);
                if (budget > 0.25 && longSlot is not null && spec.Phase != Phase.Specialty)
                    longSlot.Hours += Math.Min(budget, Math.Max(0, Math.Min(hours * 0.45, 6) - longSlot.Hours));
            }

            foreach (var s in slots) Build(s);

            // 7. CTL ramp cap for load weeks: shorten volume rides if the week would raise CTL too fast.
            var openDays = days.Count(d => d >= _start);
            if (spec.Type == WeekType.Load && spec.Phase is Phase.General or Phase.Base or Phase.Build or Phase.Specialty && openDays > 0)
            {
                var allowed = Pmc.MaxDailyTssForRamp(ctl, LevelRules.CtlRampPerWeek(_level)) * openDays;
                // Volume rides shrink first; key sessions only lose their endurance padding (never the intervals).
                var adjustable = slots.Where(s => s.IsEndurance || s.IsLong || (s.IsKey && s.Template.Kind != WorkoutKind.RampTest)).ToList();
                var fixedTss = slots.Except(adjustable).Sum(s => s.Built!.Tss)
                               + racesInWeek.Where(r => r.Date >= _start).Sum(RaceRules.EstimatedTss);
                var adjTss = adjustable.Sum(s => s.Built!.Tss);
                if (adjTss > 0 && fixedTss + adjTss > allowed)
                {
                    var f = Math.Clamp((allowed - fixedTss) / adjTss, 0.4, 1.0);
                    foreach (var s in adjustable)
                    {
                        s.Hours = Math.Max(s.IsKey ? 0.5 : 0.75, s.Hours * f);
                        Build(s);
                    }
                    _result.Notes.Add($"Week of {spec.Start:d MMM}: volume trimmed to keep the CTL rise within +{LevelRules.CtlRampPerWeek(_level)}/week.");
                }
            }

            foreach (var s in slots.OrderBy(s => s.Date)) _result.Workouts.Add(s.Built!);

            // 8. Project CTL through the week.
            foreach (var d in days.Where(d => d >= _start))
            {
                var tss = slots.Where(s => s.Date == d).Sum(s => s.Built!.Tss)
                          + _frozen.Where(w => w.Date == d).Sum(w => w.Tss)
                          + racesInWeek.Where(r => r.Date == d).Sum(RaceRules.EstimatedTss);
                ctl += (tss - ctl) / Pmc.CtlDays;
            }

            var weekTss = slots.Sum(s => s.Built!.Tss) + frozenInWeek.Sum(w => w.Tss) + racesInWeek.Sum(RaceRules.EstimatedTss);
            _result.Weeks.Add(new PlanWeek
            {
                WeekStart = spec.Start,
                Phase = spec.Phase,
                WeekType = spec.Type,
                TargetHours = Math.Round(_athlete.WeeklyHours * spec.HoursFactor, 1),
                TargetTss = Math.Round(weekTss),
                PlannedCtl = Math.Round(ctl, 1),
                RaceId = racesInWeek.OrderBy(r => r.Priority).FirstOrDefault()?.Id,
            });
            return ctl;
        }

        private int KeysFor(WeekSpec spec, bool aRaceThisWeek)
        {
            var days = _trainingDays.Count;
            var byDays = days <= 2 ? 1
                : days == 3 ? (_level >= AthleteLevel.Advanced ? 2 : 1)
                : days <= 5 ? 2
                : (_level >= AthleteLevel.Advanced ? 3 : 2);
            var intensity = spec.TargetRace?.Intensity ?? 6;
            var byIntensity = intensity <= 3 ? 1 : intensity <= 7 ? 2 : 3;
            var k = Math.Min(byDays, byIntensity);
            return spec.Phase switch
            {
                Phase.Recovery => 0,
                Phase.Taper => aRaceThisWeek ? 1 : Math.Min(k, 2),
                _ when spec.Type == WeekType.Recovery => Math.Min(k, 1),
                Phase.Base or Phase.General => Math.Min(k, 2),
                _ => k,
            };
        }

        private List<KeySlot> KeyMenu(WeekSpec spec, double progress)
        {
            var ftpGoal = _athlete.NoRaceGoal == NoRaceGoal.Ftp;
            var race = spec.TargetRace;
            var secondHalf = spec.Anchored ? spec.BlockIndex >= _cycle : progress >= 0.5;
            List<KeySlot> menu = spec.Phase switch
            {
                Phase.General when ftpGoal => secondHalf
                    ? [new(WorkoutKind.Threshold), new(WorkoutKind.Vo2Max), new(WorkoutKind.OverUnder)]
                    : [new(WorkoutKind.SweetSpot), new(WorkoutKind.Threshold), new(WorkoutKind.SweetSpot)],
                Phase.General => secondHalf
                    ? [new(WorkoutKind.SweetSpot), new(WorkoutKind.Tempo), new(WorkoutKind.LongTempo)]
                    : [new(WorkoutKind.Tempo), new(WorkoutKind.SweetSpot), new(WorkoutKind.Tempo)],
                Phase.Base when ftpGoal => [new(WorkoutKind.SweetSpot), new(WorkoutKind.Tempo), new(WorkoutKind.SweetSpot)],
                Phase.Base => progress < 0.5
                    ? [new(WorkoutKind.Tempo), new(WorkoutKind.Tempo), new(WorkoutKind.LongTempo)]
                    : [new(WorkoutKind.SweetSpot), new(WorkoutKind.Tempo), new(WorkoutKind.LongTempo)],
                Phase.Build => progress < 0.3
                    ? [new(WorkoutKind.Threshold), new(WorkoutKind.OverUnder), new(WorkoutKind.SweetSpot)]
                    : [new(WorkoutKind.Threshold), new(WorkoutKind.Vo2Max), new(WorkoutKind.OverUnder)],
                Phase.Specialty when race is not null => [.. RaceRules.SpecialtyKeys(race.Type)],
                Phase.Taper => [new(WorkoutKind.Vo2Max, "3-min"), new(WorkoutKind.Threshold)],
                Phase.Recovery => [new(WorkoutKind.Tempo)],
                _ => [new(WorkoutKind.Threshold), new(WorkoutKind.Vo2Max)],
            };
            if (race?.Intensity >= 8 && spec.Phase is Phase.Build or Phase.Specialty)
            {
                static bool Hard(KeySlot k) => k.Kind is WorkoutKind.Vo2Max or WorkoutKind.Vo2Short or WorkoutKind.ThirtyThirty
                    or WorkoutKind.Anaerobic or WorkoutKind.AnaerobicStarts or WorkoutKind.RaceSim;
                if (!menu.Take(2).Any(Hard)) menu.Insert(1, new KeySlot(WorkoutKind.Vo2Max));
                if (!menu.Any(k => k.Kind is WorkoutKind.Anaerobic or WorkoutKind.AnaerobicStarts or WorkoutKind.ThirtyThirty))
                    menu.Insert(2, new KeySlot(WorkoutKind.Anaerobic));
            }
            return menu;
        }

        private static (WorkoutKind Kind, string? Hint) EnduranceFlavour(WeekSpec spec, double progress, Race? race)
        {
            if (spec.Type == WeekType.Recovery || spec.Phase is Phase.Taper or Phase.Recovery) return (WorkoutKind.Endurance, "Endurance");
            if (spec.Phase == Phase.Specialty && race?.Type is EventType.RoadRace or EventType.Crit or EventType.Cyclocross or EventType.MtbXc)
                return (WorkoutKind.EnduranceSurges, "surges");
            if (race?.Intensity <= 3 || (spec.Phase == Phase.Base && progress >= 0.5)) return (WorkoutKind.EnduranceSurges, "tempo");
            return (WorkoutKind.Endurance, null);
        }

        private DateOnly ChooseLongDay(List<DateOnly> avail, Func<DateOnly, bool> keyOk)
        {
            var preferred = avail.FirstOrDefault(d => d.DayOfWeek == _athlete.LongRideDay && keyOk(d));
            if (preferred != default) return preferred;
            var weekend = avail.Where(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && keyOk(d))
                .OrderBy(d => Math.Abs(d.DayNumber - avail[0].DayNumber)).ToList();
            if (weekend.Count > 0) return weekend[0];
            return avail.LastOrDefault(keyOk) is { } last && last != default ? last : avail[^1];
        }

        /// <summary>
        /// Picks key days: never consecutive, never next to another key. A key next to the long ride is avoided,
        /// at the cost of one key if need be; only if that leaves none is it allowed.
        /// </summary>
        private List<DateOnly> ChooseKeyDays(List<DateOnly> candidates, int count, List<DateOnly> longDays)
        {
            var max = Math.Min(count, candidates.Count);
            return ChooseKeyDays(candidates, max, max, longDays, strict: true)
                   // Three-key weeks (advanced/pro, 6+ days) accept a key next to the long ride.
                   ?? (max >= 3 ? ChooseKeyDays(candidates, max, max, longDays, strict: false) : null)
                   ?? ChooseKeyDays(candidates, max - 1, Math.Max(1, max - 1), longDays, strict: true)
                   ?? ChooseKeyDays(candidates, max, 1, longDays, strict: false)
                   ?? [];
        }

        private List<DateOnly>? ChooseKeyDays(List<DateOnly> candidates, int maxSize, int minSize, List<DateOnly> longDays, bool strict)
        {
            for (var size = maxSize; size >= minSize && size > 0; size--)
            {
                List<DateOnly>? best = null;
                var bestScore = double.MinValue;
                foreach (var combo in Combinations(candidates, size))
                {
                    if (combo.Any(d => Adjacent(d, _keyDates))) continue;
                    if (strict && combo.Any(d => Adjacent(d, longDays))) continue;
                    var consecutive = false;
                    for (var i = 1; i < combo.Count; i++)
                        if (combo[i].DayNumber - combo[i - 1].DayNumber == 1) consecutive = true;
                    if (consecutive) continue;
                    var minGap = 3;
                    for (var i = 1; i < combo.Count; i++) minGap = Math.Min(minGap, combo[i].DayNumber - combo[i - 1].DayNumber);
                    var score = minGap * 10.0 - 30 * combo.Count(d => Adjacent(d, longDays))
                                - 0.01 * combo.Sum(d => d.DayNumber - candidates[0].DayNumber);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = combo;
                    }
                }
                if (best is not null) return best;
            }
            return null;
        }

        private static IEnumerable<List<DateOnly>> Combinations(List<DateOnly> items, int size, int start = 0)
        {
            if (size == 0)
            {
                yield return [];
                yield break;
            }
            for (var i = start; i <= items.Count - size; i++)
                foreach (var rest in Combinations(items, size - 1, i + 1))
                {
                    rest.Insert(0, items[i]);
                    yield return rest;
                }
        }

        private static bool Adjacent(DateOnly d, IEnumerable<DateOnly> others) => others.Any(o => Math.Abs(o.DayNumber - d.DayNumber) == 1);

        private int HardNeighbours(DateOnly d, List<Slot> slots) =>
            slots.Count(s => (s.IsKey || s.IsLong) && Math.Abs(s.Date.DayNumber - d.DayNumber) == 1)
            + _keyDates.Count(k => Math.Abs(k.DayNumber - d.DayNumber) == 1);

        // ---------- Workouts ----------

        private WorkoutTemplate Pick(WorkoutKind kind, string? hint = null, double progress = 0.5, int variety = 0) =>
            _picker.Pick(kind, _level, progress, hint, variety)
            ?? _picker.Pick(WorkoutKind.Endurance, _level, 0)
            ?? _fallbackEndurance;

        private WorkoutTemplate? FindTemplate(int? id) => id is null ? null : _in.Templates.FirstOrDefault(t => t.Id == id);

        private void Build(Slot s)
        {
            var hours = Math.Max(0.5, Math.Round(s.Hours * 4) / 4);
            var steps = s.Template.Kind == WorkoutKind.RampTest
                ? s.Template.Steps.Select(x => x.Clone()).ToList()
                : WorkoutSizer.Fit(s.Template.Steps, (int)(hours * 3600));
            var (tss, intensity, seconds) = LoadMath.ForSteps(steps);
            s.Built = new PlannedWorkout
            {
                Date = s.Date,
                TemplateId = s.Template.Id == 0 ? null : s.Template.Id,
                Name = s.Template.Name,
                Kind = s.Template.Kind,
                IsKey = s.Template.Kind.IsKey(),
                Steps = steps,
                DurationSec = seconds,
                Tss = Math.Round(tss, 1),
                IntensityFactor = Math.Round(intensity, 2),
                Indoor = _athlete.DefaultIndoor,
                Status = WorkoutStatus.Planned,
                Notes = s.Note,
            };
        }

        // ---------- Load ----------

        private double StartingCtl()
        {
            var last = _in.Loads.Where(l => l.Date < _start).MaxBy(l => l.Date);
            if (last is null) return _athlete.WeeklyHours * 45 / 7; // assume current hours at ~0.67 IF
            var ctl = last.Ctl;
            for (var d = last.Date.AddDays(1); d < _start; d = d.AddDays(1)) ctl -= ctl / Pmc.CtlDays;
            return ctl;
        }

        private void ApplyFatigueGuard()
        {
            var recent = _in.Loads.Where(l => l.Date < _start && l.Date >= _start.AddDays(-3)).ToList();
            if (recent.Count < 3 || recent.Any(l => l.Tsb >= -30)) return;
            var next = _result.Workouts.Where(w => w.IsKey && w.Kind != WorkoutKind.RampTest).MinBy(w => w.Date);
            if (next is null) return;
            var t = Pick(WorkoutKind.Endurance, "Endurance");
            var steps = WorkoutSizer.Fit(t.Steps, next.DurationSec);
            var (tss, intensity, seconds) = LoadMath.ForSteps(steps);
            _result.Notes.Add($"Fatigue guard: TSB below −30 for 3 days, so {next.Name} on {next.Date:ddd d MMM} became an endurance ride.");
            next.Notes = $"Was {next.Name}: swapped by the fatigue guard (TSB below −30)";
            next.TemplateId = t.Id == 0 ? null : t.Id;
            next.Name = t.Name;
            next.Kind = t.Kind;
            next.IsKey = false;
            next.Steps = steps;
            next.DurationSec = seconds;
            next.Tss = Math.Round(tss, 1);
            next.IntensityFactor = Math.Round(intensity, 2);
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;
    }

    private sealed class PlanResultBuilder
    {
        public PlanResult Result { get; }

        public PlanResultBuilder(PlanResult r, DateOnly end)
        {
            Result = new PlanResult { Start = r.Start, End = end, TargetRaceId = r.TargetRaceId };
            Result.Weeks.AddRange(r.Weeks);
            Result.Workouts.AddRange(r.Workouts.OrderBy(w => w.Date));
            Result.Notes.AddRange(r.Notes);
        }
    }
}
