namespace Trainer.Data;

/// <summary>
/// Where the app keeps its data: one SQLite file plus a folder of imported FIT files.
/// Backing up is copying the whole folder.
/// </summary>
public sealed class DataPaths
{
    public DataPaths(string? root = null)
    {
        // Windows: %LOCALAPPDATA%\Trainer · macOS: ~/Library/Application Support/Trainer · Linux: ~/.local/share/Trainer
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        Root = root ?? Path.Combine(appData, "Trainer");
    }

    public string Root { get; }
    public string Database => Path.Combine(Root, "trainer.db");
    public string FitFolder => Path.Combine(Root, "fit");
    public string ExportFolder => Path.Combine(Root, "export");

    public void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(FitFolder);
        Directory.CreateDirectory(ExportFolder);
    }
}
