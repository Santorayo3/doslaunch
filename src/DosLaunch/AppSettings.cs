using System.Text.Json;
using System.Text.Json.Serialization;

namespace DosLaunch;

/// <summary>User settings, stored in the user profile so they survive rebuilds.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Either a DOSBox folder or the DOSBox executable itself; empty until the user selects one.</summary>
    public string DosBoxPath { get; set; } = string.Empty;

    /// <summary>Space separated extra arguments passed to DOSBox before the config.</summary>
    public string ExtraArguments { get; set; } = string.Empty;

    /// <summary>Whether a game's release year may be looked up online when a new config is created.</summary>
    public bool LookUpGameInfoOnline { get; set; } = true;

    // Settings files written by older versions used DOSBox-X specific names.
    [JsonInclude]
    internal string? DosBoxXPath { get; set; }

    [JsonInclude]
    internal string? DosBoxXArguments { get; set; }

    public static string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DosLaunch",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                AppSettings settings =
                    JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath), SerializerOptions)
                    ?? new AppSettings();
                settings.MigrateLegacyValues();
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read settings, using defaults: {ex.Message}");
        }

        return new AppSettings();
    }

    /// <summary>Persists the settings and reports whether they could be written.</summary>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, SerializerOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save settings: {ex.Message}");
            return false;
        }
    }

    /// <summary>Moves the DOSBox-X only settings of older versions into the generic ones.</summary>
    internal void MigrateLegacyValues()
    {
        if (string.IsNullOrWhiteSpace(DosBoxPath) && !string.IsNullOrWhiteSpace(DosBoxXPath))
        {
            DosBoxPath = DosBoxXPath!;
        }

        if (string.IsNullOrWhiteSpace(ExtraArguments) && !string.IsNullOrWhiteSpace(DosBoxXArguments))
        {
            // DOSBox-X adds -fastlaunch on its own now, so drop it from inherited arguments.
            ExtraArguments = string.Join(
                ' ',
                DosBoxXArguments!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(argument => !argument.Equals("-fastlaunch", StringComparison.OrdinalIgnoreCase))
                    .ToArray());
        }

        DosBoxXPath = null;
        DosBoxXArguments = null;
    }

    /// <summary>Returns the DOSBox executable, or <c>null</c> when it cannot be located.</summary>
    public string? ResolveDosBoxExecutable()
    {
        if (string.IsNullOrWhiteSpace(DosBoxPath))
        {
            return null;
        }

        if (File.Exists(DosBoxPath))
        {
            return DosBoxPath;
        }

        if (Directory.Exists(DosBoxPath))
        {
            foreach (string candidate in new[] { "dosbox-x.exe", "dosbox-x-sdl2.exe", "dosbox.exe", "dosbox64.exe" })
            {
                string path = Path.Combine(DosBoxPath, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    public IEnumerable<string> GetExtraArguments() =>
        (ExtraArguments ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
