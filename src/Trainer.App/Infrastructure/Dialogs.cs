using System.Windows;
using Microsoft.Win32;
using Trainer.App.Views;

namespace Trainer.App.Infrastructure;

/// <summary>Small wrappers so view-models stay free of WPF dialog code.</summary>
public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static void Info(string message, string title = "Cycling Training Planner") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "Cycling Training Planner") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static bool Confirm(string message, string title = "Please confirm") =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static string? Prompt(string message, string title, string initial = "")
    {
        var dlg = new InputDialog(title, message, initial) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    public static string? PickFolder(string title, string? initial = null)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public static string[] PickFiles(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : [];
    }

    public static string? PickFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public static string? SaveFile(string title, string filter, string fileName, string? initialFolder = null)
    {
        var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName };
        if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder)) dlg.InitialDirectory = initialFolder;
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public static void OpenInExplorer(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
