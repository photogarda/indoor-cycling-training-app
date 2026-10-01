using Trainer.Data;
using Trainer.Data.Services;

namespace Trainer.Tests;

/// <summary>A real, migrated SQLite database in a temp folder, deleted afterwards.</summary>
public sealed class TestDb : IDisposable
{
    public TestDb(DateOnly? today = null)
    {
        Paths = new DataPaths(Path.Combine(Path.GetTempPath(), "trainer-tests", Guid.NewGuid().ToString("N")));
        Paths.Ensure();
        Today = today ?? PlanTestKit.Today;
        Service = Create();
        Service.Initialize();
    }

    public DataPaths Paths { get; }
    public DateOnly Today { get; set; }
    public TrainerService Service { get; private set; }

    /// <summary>A fresh service on the same file, like restarting the app.</summary>
    public TrainerService Create() => new(Paths, () => Today);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Paths.Root, true); } catch (IOException) { }
    }
}
