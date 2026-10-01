namespace Trainer.Data;

/// <summary>
/// Where the app keeps its data: one SQLite file plus a folder of imported FIT files.
/// Backing up is copying the whole folder.
/// </summary>
public sealed class DataPaths
{
    public DataPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trainer");
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
