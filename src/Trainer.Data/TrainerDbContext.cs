using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Trainer.Core.Models;

namespace Trainer.Data;

public class TrainerDbContext(DbContextOptions<TrainerDbContext> options) : DbContext(options)
{
    public DbSet<Athlete> Athletes => Set<Athlete>();
    public DbSet<FtpEntry> FtpHistory => Set<FtpEntry>();
    public DbSet<Race> Races => Set<Race>();
    public DbSet<BlockedDay> BlockedDays => Set<BlockedDay>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanWeek> PlanWeeks => Set<PlanWeek>();
    public DbSet<PlannedWorkout> PlannedWorkouts => Set<PlannedWorkout>();
    public DbSet<WorkoutTemplate> WorkoutTemplates => Set<WorkoutTemplate>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<DailyLoad> DailyLoads => Set<DailyLoad>();

    public static TrainerDbContext Open(string databasePath)
    {
        var options = new DbContextOptionsBuilder<TrainerDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        return new TrainerDbContext(options);
    }

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static (ValueConverter<T, string> Converter, ValueComparer<T> Comparer) JsonColumn<T>(Func<T> empty) where T : class
    {
        var converter = new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, Json),
            s => string.IsNullOrEmpty(s) ? empty() : JsonSerializer.Deserialize<T>(s, Json) ?? empty());
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
            v => JsonSerializer.Serialize(v, Json).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!);
        return (converter, comparer);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        var steps = JsonColumn<List<WorkoutStep>>(() => []);
        var curve = JsonColumn<Dictionary<int, double>>(() => []);
        var days = new ValueConverter<List<DayOfWeek>, string>(
            v => string.Join(",", v.Select(d => (int)d)),
            s => s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => (DayOfWeek)int.Parse(x)).ToList());
        var daysComparer = new ValueComparer<List<DayOfWeek>>(
            (a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, d) => h * 7 + (int)d), v => v.ToList());

        b.Entity<Athlete>(e =>
        {
            e.Property(x => x.Level).HasConversion<string>();
            e.Property(x => x.NoRaceGoal).HasConversion<string>();
            e.Property(x => x.LongRideDay).HasConversion<string>();
            e.Property(x => x.TrainingDays).HasConversion(days, daysComparer);
        });
        b.Entity<FtpEntry>(e =>
        {
            e.ToTable("FtpHistory");
            e.Property(x => x.Method).HasConversion<string>();
            e.HasIndex(x => x.Date);
        });
        b.Entity<Race>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Priority).HasConversion<string>();
            e.HasIndex(x => x.Date);
        });
        b.Entity<BlockedDay>(e => e.HasIndex(x => x.Date).IsUnique());
        b.Entity<Plan>(e => e.HasMany(x => x.Weeks).WithOne().HasForeignKey(w => w.PlanId).OnDelete(DeleteBehavior.Cascade));
        b.Entity<PlanWeek>(e =>
        {
            e.Property(x => x.Phase).HasConversion<string>();
            e.Property(x => x.WeekType).HasConversion<string>();
        });
        b.Entity<PlannedWorkout>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Steps).HasConversion(steps.Converter, steps.Comparer);
            e.Ignore(x => x.Duration);
            e.HasIndex(x => new { x.Date, x.Superseded });
        });
        b.Entity<WorkoutTemplate>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>();
            e.Property(x => x.MinLevel).HasConversion<string>();
            e.Property(x => x.MaxLevel).HasConversion<string>();
            e.Property(x => x.Steps).HasConversion(steps.Converter, steps.Comparer).HasColumnName("Intervals");
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<Activity>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>();
            e.Property(x => x.PowerCurve).HasConversion(curve.Converter, curve.Comparer);
            e.Ignore(x => x.Date);
            e.HasIndex(x => x.StartTime);
            e.HasIndex(x => x.StravaId);
        });
        b.Entity<DailyLoad>(e => e.HasKey(x => x.Date));
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the context at design time.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<TrainerDbContext>
{
    public TrainerDbContext CreateDbContext(string[] args) => TrainerDbContext.Open("design.db");
}
