using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Trainer.Desktop.Views;

namespace Trainer.Desktop.Infrastructure;

/// <summary>Dialogs and OS helpers, kept out of the view-models. Avalonia dialogs are asynchronous.</summary>
public static class Dialogs
{
    private static Window? Owner => App.MainWindow;
    private static IStorageProvider? Storage => Owner?.StorageProvider;

    public static Task Info(string message, string title = "Cycling Training Planner") => Show(title, message, false);

    public static Task Error(string message, string title = "Cycling Training Planner") => Show(title, message, false);

    public static Task<bool> Confirm(string message, string title = "Please confirm") => Show(title, message, true);

    private static async Task<bool> Show(string title, string message, bool askYesNo)
    {
        var w = new MessageWindow(title, message, askYesNo);
        if (Owner is null || !Owner.IsVisible) return false;
        return await w.ShowDialog<bool>(Owner);
    }

    public static async Task<string?> Prompt(string message, string title, string initial = "")
    {
        if (Owner is null) return null;
        return await new InputWindow(title, message, initial).ShowDialog<string?>(Owner);
    }

    private static async Task<IStorageFolder?> Folder(string? path) =>
        Storage is not null && !string.IsNullOrEmpty(path) && Directory.Exists(path) ? await Storage.TryGetFolderFromPathAsync(path) : null;

    public static async Task<string?> PickFolder(string title, string? initial = null)
    {
        if (Storage is null) return null;
        var result = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title, AllowMultiple = false, SuggestedStartLocation = await Folder(initial),
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public static async Task<string[]> PickFiles(string title, string extension, string description)
    {
        if (Storage is null) return [];
        var result = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(description) { Patterns = [$"*.{extension}", $"*.{extension.ToUpperInvariant()}"] }, FilePickerFileTypes.All],
        });
        return result.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToArray();
    }

    public static async Task<string?> PickFile(string title, string extension, string description) =>
        (await PickFiles(title, extension, description)).FirstOrDefault();

    public static async Task<string?> SaveFile(string title, string extension, string description, string fileName, string? initialFolder = null)
    {
        if (Storage is null) return null;
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = fileName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await Folder(initialFolder),
            FileTypeChoices = [new FilePickerFileType(description) { Patterns = [$"*.{extension}"] }],
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>Shows a folder in Explorer / Finder / the file manager.</summary>
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        var opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        Process.Start(new ProcessStartInfo(opener, $"\"{path}\"") { UseShellExecute = false });
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
