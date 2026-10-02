using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using Trainer.Data;

namespace Trainer.Desktop.Infrastructure;

/// <summary>
/// Light/dark switch. The choice is saved in ui-settings.json in the data folder; with nothing saved the app
/// follows the operating system.
/// </summary>
public static class ThemeManager
{
    private sealed record UiSettings(string? Theme);

    private static string SettingsFile => Path.Combine(new DataPaths().Root, "ui-settings.json");

    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Raised after the theme changes, so code-drawn charts can redraw.</summary>
    public static event EventHandler? Changed;

    public static void Load()
    {
        if (Application.Current is not { } app) return;
        app.ActualThemeVariantChanged += (_, _) => Changed?.Invoke(null, EventArgs.Empty);
        try
        {
            if (!File.Exists(SettingsFile)) return; // follow the OS
            var s = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsFile));
            app.RequestedThemeVariant = s?.Theme switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings: keep the OS theme.
        }
    }

    public static void Toggle()
    {
        if (Application.Current is not { } app) return;
        var dark = !IsDark;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new UiSettings(dark ? "Dark" : "Light")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved; the switch still applies for this session.
        }
    }
}
