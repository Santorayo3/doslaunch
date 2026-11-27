using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoubleClickDOS;

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

    private static readonly string[] DosBoxExecutableNames =
    {
        "dosbox-x.exe", "dosbox-x-sdl2.exe", "dosbox.exe", "dosbox64.exe",
    };

    private static readonly string[] MuntExecutableNames =
    {
        "mt32emu-qt.exe", "munt.exe", "mt32emu.exe", "munt-qt.exe",
    };

    /// <summary>Either a DOSBox folder or the DOSBox executable itself; empty until the user selects one.</summary>
    public string DosBoxPath { get; set; } = string.Empty;

    /// <summary>
    /// Either a MUNT folder or the MUNT executable itself. Empty while MT-32 emulation is not used;
    /// setting it makes MUNT start before DOSBox and close after DOSBox ends.
    /// </summary>
    public string MuntPath { get; set; } = string.Empty;

    /// <summary>Space separated extra arguments passed to DOSBox before the config.</summary>
    public string ExtraArguments { get; set; } = string.Empty;

    /// <summary>Whether a game's release year may be looked up online when a new config is created.</summary>
    public bool LookUpGameInfoOnline { get; set; } = true;

    // Settings files written by older versions used DOSBox-X specific names.
    [JsonInclude]
    internal string? DosBoxXPath { get; set; }

    [JsonInclude]
    internal string? DosBoxXArguments { get; set; }

    public static string SettingsFilePath { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoubleClickDOS",
        "settings.json");

    // Folder used by versions that were still called DOS Launch.
    internal static string LegacySettingsFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DosLaunch",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            string path = File.Exists(SettingsFilePath) ? SettingsFilePath : LegacySettingsFilePath;
            if (File.Exists(path))
            {
                AppSettings settings =
                    JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), SerializerOptions)
                    ?? new AppSettings();
                settings.MigrateLegacyValues();
                if (!string.Equals(path, SettingsFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Continue in the new location so the old folder is only needed this once.
                    settings.Save();
                }

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
    public string? ResolveDosBoxExecutable() => ResolveExecutable(DosBoxPath, DosBoxExecutableNames);

    /// <summary>Returns the MUNT executable, or <c>null</c> when MUNT is not configured or cannot be located.</summary>
    public string? ResolveMuntExecutable() => ResolveExecutable(MuntPath, MuntExecutableNames);

    private static string? ResolveExecutable(string configuredPath, string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if (Directory.Exists(configuredPath))
        {
            foreach (string candidate in candidates)
            {
                string path = Path.Combine(configuredPath, candidate);
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
