using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Trainer.Data.Services;

/// <summary>Backup is the whole data folder (database + imported FIT files) in one zip.</summary>
public static class BackupService
{
    public static void Backup(DataPaths paths, string zipPath)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(paths.Root, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file) == Path.GetFullPath(zipPath)) continue;
            var rel = Path.GetRelativePath(paths.Root, file);
            if (rel.StartsWith("export", StringComparison.OrdinalIgnoreCase)) continue;
            // Copy first so an open database file can still be read.
            using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(rel.Replace('\\', '/'), CompressionLevel.Optimal);
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
    }

    /// <summary>Replaces the data folder's contents with a backup. Restart the app afterwards.</summary>
    public static void Restore(DataPaths paths, string zipPath)
    {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.GetEntry("trainer.db") is null) throw new InvalidDataException("That zip is not a Training Planner backup (no trainer.db inside).");
        }
        SqliteConnection.ClearAllPools();
        var staging = Path.Combine(Path.GetTempPath(), "trainer-restore-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zipPath, staging);
        foreach (var name in new[] { "trainer.db-wal", "trainer.db-shm" })
            if (File.Exists(Path.Combine(paths.Root, name))) File.Delete(Path.Combine(paths.Root, name));
        foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(paths.Root, Path.GetRelativePath(staging, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        Directory.Delete(staging, true);
    }
}
