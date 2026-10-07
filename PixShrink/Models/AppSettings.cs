using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PixShrink.Models;

public class AppSettings
{
    // Defines where to start the search the next time a search window is opened
    // (at runtime and app restart)
    public string LastInputPath { get; set; } = string.Empty;
 
    // Last output directory selected. Populates OutputPathBox and defines where to
    // start the search on folder selection
    public string LastOutputPath { get; set; } = string.Empty;
    
    // Dimensions of the window before exiting the application
    public WindowSettings WindowSettings { get; set; } = new WindowSettings();
 
    // Where the settings file lives
    private static readonly string s_SettingsPath = ResolveSettingsPath();
    
    private static readonly JsonSerializerOptions s_JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Saves the settings to disk, creating the Settings folder and file if they don't exist.
    /// </summary>
    public void WriteToJson()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(s_SettingsPath)!);
            File.WriteAllText(s_SettingsPath, JsonSerializer.Serialize(this, s_JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not save settings: {ex.Message}");
        }
    }
 
    /// <summary>
    /// Loads the settings from disk into this instance. Does nothing if the file doesn't
    /// exist yet, and keeps the defaults if the file is unreadable or corrupted.
    /// </summary>
    public void ReadFromJson()
    {
        if (!File.Exists(s_SettingsPath)) return;
 
        try
        {
            string json = File.ReadAllText(s_SettingsPath);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded == null) return;
 
            LastInputPath = loaded.LastInputPath ?? string.Empty;
            LastOutputPath = loaded.LastOutputPath ?? string.Empty;
            WindowSettings = loaded.WindowSettings ?? new WindowSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not read settings: {ex.Message}");
        }
    }
    
    private static string GetAppDirectory()
    {
        // Folder of the running exe; unaffected by single-file extraction
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        return string.IsNullOrEmpty(exeDir) ? AppContext.BaseDirectory : exeDir;
    }

    private static string ResolveSettingsPath()
    {
        string portableDir = Path.Combine(GetAppDirectory(), "Settings");
        try
        {
            // Make sure we can actually write there before committing to it
            Directory.CreateDirectory(portableDir);
            string probe = Path.Combine(portableDir, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return Path.Combine(portableDir, "settings.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only location (Program Files, a mounted image...): fall back to AppData/Local
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PixShrink", "settings.json");
        }
    }
}
