using Microsoft.EntityFrameworkCore;
using Trainer.Core.Models;
using Trainer.Core.Workouts;

namespace Trainer.Data.Services;

/// <summary>Raised for user-facing validation problems (e.g. A races too close together).</summary>
public class TrainerValidationException(string message) : Exception(message);

/// <summary>
/// The single entry point the UI uses. Every change that the plan rules list as a regenerate trigger
/// (race added/moved/changed, day blocked, workout moved, FTP changed, hours or days changed, rides imported)
/// ends by calling <see cref="Refresh"/>, which re-evaluates compliance, recomputes load and regenerates
/// the plan from today forward.
/// </summary>
public partial class TrainerService
{
    private readonly Func<TrainerDbContext> _open;
    private readonly Func<DateOnly> _today;

    public TrainerService(DataPaths paths, Func<DateOnly>? today = null)
        : this(() => TrainerDbContext.Open(paths.Database), today)
    {
        Paths = paths;
    }

    public TrainerService(Func<TrainerDbContext> open, Func<DateOnly>? today = null)
    {
        _open = open;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        Paths = new DataPaths();
    }

    public DataPaths Paths { get; }
    public DateOnly Today => _today();

    /// <summary>Notes from the last regenerate (fatigue guard, dropped workouts, …) for the UI.</summary>
    public IReadOnlyList<string> LastPlanNotes { get; private set; } = [];

    /// <summary>Raised after anything changes, so open screens can reload.</summary>
    public event EventHandler? Changed;

    private TrainerDbContext Db() => _open();

    /// <summary>Creates or migrates the database, seeds the athlete and the built-in library.</summary>
    public void Initialize(bool migrate = true)
    {
        using var db = Db();
        if (migrate) db.Database.Migrate();
        else db.Database.EnsureCreated();

        if (!db.Athletes.Any())
        {
            var today = Today;
            db.Athletes.Add(new Athlete { PlanAnchor = Core.Planning.PlanEngine.WeekStart(today) });
        }
        SeedLibrary(db);
        db.SaveChanges();
    }

    private static void SeedLibrary(TrainerDbContext db)
    {
        var existing = db.WorkoutTemplates.ToDictionary(t => t.Name);
        foreach (var t in WorkoutLibrary.BuiltIn())
        {
            if (existing.TryGetValue(t.Name, out var cur))
            {
                if (!cur.BuiltIn) continue; // the user's own template with the same name wins
                cur.Kind = t.Kind;
                cur.Steps = t.Steps;
                cur.Description = t.Description;
                cur.MinLevel = t.MinLevel;
                cur.MaxLevel = t.MaxLevel;
            }
            else
            {
                t.Id = 0;
                db.WorkoutTemplates.Add(t);
            }
        }
    }

    public bool IsFirstRun()
    {
        using var db = Db();
        return !db.FtpHistory.Any();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
