using System.Text;
using System.Text.RegularExpressions;

namespace DoubleClickDOS;

/// <summary>
/// Creates the per-program DOSBox configuration file that is stored next to the executable and
/// reused (and editable) afterwards.
/// </summary>
internal static class DosBoxConfigFile
{
    /// <summary>
    /// Extension of the generated configs. Kept from the versions that were still called DOS Launch so the
    /// configs users already edited in their game folders are found and reused.
    /// </summary>
    public const string Extension = ".doslaunch.conf";

    private static readonly Regex MountLinePattern = new("^mount c\\s+\".*\"", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex AutoexecSectionPattern = new(@"^\[autoexec\]", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex ProgramLinePattern = new(@"^# Program: (.+)$", RegexOptions.Multiline);
    private static readonly Regex OutputLinePattern = new(
        @"^([ \t]*)output[ \t]*=[^\r\n]*",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static string GetConfigPath(string executablePath) => Path.ChangeExtension(executablePath, Extension);

    /// <summary>
    /// The config for one program. Programs sharing a base name (WILLY.EXE next to WILLY.BAT) would
    /// otherwise share a config whose autoexec starts the wrong one, so the second program gets its
    /// own file that keeps the extension.
    /// </summary>
    internal static string ResolveConfigPath(string executablePath)
    {
        string configPath = GetConfigPath(executablePath);
        return File.Exists(configPath) && !ConfigBelongsTo(configPath, executablePath)
            ? executablePath + Extension
            : configPath;
    }

    /// <summary>
    /// Whether the config was made for the given program. Configs that do not name their program (e.g.
    /// hand-written ones) are left alone.
    /// </summary>
    private static bool ConfigBelongsTo(string configPath, string executablePath)
    {
        try
        {
            Match match = ProgramLinePattern.Match(File.ReadAllText(configPath));
            if (!match.Success)
            {
                return true;
            }

            string configuredProgram = match.Groups[1].Value.Trim();
            return string.Equals(
                Path.GetFileName(configuredProgram),
                Path.GetFileName(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Returns the config to use, creating it when missing. <c>null</c> means launching without one.</summary>
    public static string? EnsureConfig(
        string executablePath,
        string[] programArguments,
        AppSettings settings,
        DosBoxFlavor flavor,
        int? muntMidiDevice = null)
    {
        string configPath = ResolveConfigPath(executablePath);
        if (File.Exists(configPath))
        {
            Log.Write($"Using existing DOSBox config: {configPath}");
            SyncMountPath(configPath, Path.GetDirectoryName(executablePath)!);
            SyncLaunchOptions(configPath, Path.GetFileName(executablePath), flavor, muntMidiDevice);
            return configPath;
        }

        GameIdentity identity = GameInfoLookup.TryResolve(executablePath, settings.LookUpGameInfoOnline);
        GameProfile profile = GameProfileBuilder.Create(executablePath, identity);
        string content = Render(executablePath, programArguments, profile, identity, flavor, muntMidiDevice);

        try
        {
            File.WriteAllText(configPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Log.Write($"Created DOSBox config: {configPath}");
            return configPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not create {configPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Renderer of every generated config. OpenGL exists in DOSBox-X and classic DOSBox alike and is the
    /// only output that scales to the full screen without DOSBox having to resample in software.
    /// </summary>
    private const string OutputValue = "opengl";

    /// <summary>DOSBox-X can pick the nearest neighbour filter for aspect correction; classic only knows true/false.</summary>
    private static string AspectFor(DosBoxFlavor flavor) =>
        flavor == DosBoxFlavor.DosBoxX ? "nearest" : "true";

    internal static string Render(
        string executablePath,
        string[] programArguments,
        GameProfile profile,
        GameIdentity? identity,
        DosBoxFlavor flavor,
        int? muntMidiDevice = null)
    {
        string folder = Path.GetDirectoryName(executablePath)!;
        var config = new StringBuilder();

        config.AppendLine("# DOSBox configuration generated by Double-Click DOS.");
        config.AppendLine($"# Program: {executablePath}");
        config.AppendLine($"# Created: {DateTime.Now:yyyy-MM-dd HH:mm}");
        AppendIdentityComments(config, identity);

        foreach (string note in profile.Notes)
        {
            config.AppendLine($"# {note}");
        }

        config.AppendLine("#");
        config.AppendLine($"# Full screen with 4:3 aspect correction, {OutputValue} output and no software scaler.");
        config.AppendLine("# Delete this file to have Double-Click DOS generate a new one.");
        config.AppendLine();
        config.AppendLine("[sdl]");
        config.AppendLine("fullscreen=true");
        config.AppendLine("fullresolution=desktop");
        config.AppendLine($"output={OutputValue}");
        config.AppendLine();
        config.AppendLine("[render]");
        config.AppendLine($"aspect={AspectFor(flavor)}");
        config.AppendLine("scaler=none");
        config.AppendLine();
        config.AppendLine("[dosbox]");
        if (flavor == DosBoxFlavor.DosBoxX)
        {
            config.AppendLine("dpi aware=true");
        }

        config.AppendLine($"memsize={profile.MemoryMb}");
        config.AppendLine();
        config.AppendLine("[cpu]");
        config.AppendLine("core=auto");
        config.AppendLine($"cputype={profile.CpuType}");
        config.AppendLine($"cycles={profile.Cycles}");
        config.AppendLine();
        config.AppendLine("[sblaster]");
        config.AppendLine($"sbtype={profile.SoundBlaster}");
        config.AppendLine("sbbase=220");
        config.AppendLine("irq=7");
        config.AppendLine("dma=1");
        config.AppendLine("hdma=5");

        if (muntMidiDevice is { } midiDevice)
        {
            config.AppendLine();
            config.AppendLine("[midi]");
            config.AppendLine("# Sends MIDI to the running MUNT (MT-32) emulator.");
            config.AppendLine("mpu401=intelligent");
            config.AppendLine("mididevice=win32");
            config.AppendLine($"midiconfig={midiDevice}");
        }

        config.AppendLine();
        config.AppendLine("[autoexec]");
        config.AppendLine($"mount c \"{folder}\"");
        config.Append(FormatCdMount(profile.Cd));
        config.AppendLine("c:");
        config.AppendLine(BuildProgramCommand(Path.GetFileName(executablePath), programArguments));
        config.AppendLine("exit");

        return config.ToString();
    }

    /// <summary>
    /// Writes down which game and year the settings are based on, so a wrong guess can be understood
    /// (and ignored) without reading the log.
    /// </summary>
    private static void AppendIdentityComments(StringBuilder config, GameIdentity? identity)
    {
        if (identity is null)
        {
            return;
        }

        config.AppendLine($"# Game:    {identity.Title ?? "unknown program"} - {identity.YearText}");

        if (identity.Evidence.Count == 0)
        {
            config.AppendLine("# Why:     no release year found in the game's text files, executables or online sources");
        }
        else
        {
            config.AppendLine($"# Why:     {identity.Confidence.ToString().ToLowerInvariant()} confidence");
            foreach (string line in ExplainEvidence(identity))
            {
                config.AppendLine($"#          {line}");
            }
        }

        config.AppendLine(identity.Era == GameEra.Unknown
            ? "# Preset:  conservative defaults, because the release year is unknown"
            : $"# Preset:  {identity.Era.Describe()} era");
    }

    private static IEnumerable<string> ExplainEvidence(GameIdentity identity) =>
        identity.Evidence
            .OrderByDescending(hint => hint.Weight)
            .ThenBy(hint => hint.Year)
            .Select(hint => hint.Detail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3);

    private static string FormatCdMount(CdDrive cd)
    {
        string drive = cd.Letter.ToLowerInvariant();
        return cd.IsImage
            ? $"imgmount {drive} \"{cd.Target}\" -t {cd.ImageType}{Environment.NewLine}"
            : $"mount {drive} \"{cd.Target}\" -t cdrom{Environment.NewLine}";
    }

    private static string BuildProgramCommand(string programFileName, string[] programArguments)
    {
        // Classic DOSBox does not accept a quoted command name, so only quote when it is needed.
        string program = programFileName.Contains(' ') ? $"\"{programFileName}\"" : programFileName;

        // A batch file replaces the autoexec batch unless it is called, which would swallow the "exit".
        if (Path.GetExtension(programFileName).Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            program = $"call {program}";
        }

        var command = new StringBuilder(program);
        foreach (string argument in programArguments)
        {
            command.Append(' ').Append(argument);
        }

        return command.ToString();
    }

    /// <summary>Keeps a generated config usable after the game folder was moved or renamed.</summary>
    internal static void SyncMountPath(string configPath, string folder)
    {
        try
        {
            string content = File.ReadAllText(configPath);
            Match match = MountLinePattern.Match(content);
            if (!match.Success)
            {
                return;
            }

            string? previousFolder = ExtractQuotedPath(match.Value);
            if (previousFolder is null || string.Equals(previousFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Rewrites the mount and every image/folder path that pointed into the old location.
            string updated = content
                .Replace($"\"{previousFolder}\"", $"\"{folder}\"", StringComparison.OrdinalIgnoreCase)
                .Replace($"\"{previousFolder}\\", $"\"{folder}\\", StringComparison.OrdinalIgnoreCase);

            if (updated == content)
            {
                return;
            }

            File.WriteAllText(configPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Log.Write($"Updated mount path in {configPath} to {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not update the mount path in {configPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds options introduced by later versions (fullscreen, crisp scaling, auto-exit) to configs
    /// created earlier. Values that are already present are kept, except the renderer, which is always OpenGL.
    /// </summary>
    internal static void SyncLaunchOptions(
        string configPath,
        string programFileName,
        DosBoxFlavor flavor,
        int? muntMidiDevice = null)
    {
        try
        {
            string content = File.ReadAllText(configPath);
            string updated = content;

            updated = EnsureKey(updated, "fullscreen", "true", "sdl");
            updated = EnsureKey(updated, "fullresolution", "desktop", "sdl");
            updated = EnsureKey(updated, "output", OutputValue, "sdl");
            // The renderer is always OpenGL, also in configs written by earlier versions or edited by hand.
            updated = OutputLinePattern.Replace(updated, "${1}output=" + OutputValue);
            updated = EnsureKey(updated, "aspect", AspectFor(flavor), "render");
            updated = EnsureKey(updated, "scaler", "none", "render");
            if (flavor == DosBoxFlavor.DosBoxX)
            {
                updated = EnsureKey(updated, "dpi aware", "true", "dosbox");
            }

            if (muntMidiDevice is { } midiDevice)
            {
                updated = SyncMuntMidi(updated, midiDevice);
            }

            if (AutoexecSectionPattern.IsMatch(updated) && !EndsWithExit(updated))
            {
                updated = updated.TrimEnd() + Environment.NewLine + "exit" + Environment.NewLine;
            }

            updated = UnquoteProgramLine(updated, programFileName);

            if (updated == content)
            {
                return;
            }

            File.WriteAllText(configPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Log.Write($"Added missing launch options to {configPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not update launch options in {configPath}: {ex.Message}");
        }
    }

    private static string EnsureKey(string content, string key, string value, string section)
    {
        if (HasKey(content, key))
        {
            return content;
        }

        var sectionPattern = new Regex(@"^\[" + Regex.Escape(section) + @"\]", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        Match header = sectionPattern.Match(content);
        if (header.Success)
        {
            return content.Insert(header.Index + header.Length, $"{Environment.NewLine}{key}={value}");
        }

        return InsertBeforeAutoexec(content, $"[{section}]{Environment.NewLine}{key}={value}{Environment.NewLine}{Environment.NewLine}");
    }

    private static bool HasKey(string content, string key) =>
        new Regex(@"^[ \t]*" + Regex.Escape(key) + @"[ \t]*=", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .IsMatch(content);

    /// <summary>
    /// Points DOSBox at the MUNT MIDI port when MUNT is enabled. A config that already chooses a MIDI
    /// device is left alone, so a deliberately different setup (for example DOSBox-X's built-in MT-32) survives.
    /// </summary>
    private static string SyncMuntMidi(string content, int deviceIndex)
    {
        if (HasKey(content, "mididevice"))
        {
            return content;
        }

        string updated = EnsureKey(content, "mpu401", "intelligent", "midi");
        updated = EnsureKey(updated, "mididevice", "win32", "midi");
        return EnsureKey(updated, "midiconfig", deviceIndex.ToString(), "midi");
    }

    private static string InsertBeforeAutoexec(string content, string block)
    {
        Match autoexec = AutoexecSectionPattern.Match(content);
        return autoexec.Success ? content.Insert(autoexec.Index, block) : content + Environment.NewLine + block;
    }

    private static bool EndsWithExit(string content)
    {
        string[] lines = content.Split('\n');
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string line = lines[index].Trim();
            if (line.Length > 0)
            {
                return line.Equals("exit", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    /// <summary>Older configs quoted the program name, which classic DOSBox cannot execute.</summary>
    private static string UnquoteProgramLine(string content, string programFileName)
    {
        if (programFileName.Contains(' '))
        {
            return content;
        }

        string quoted = $"\"{programFileName}\"";
        string[] lines = content.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Trim().Equals(quoted, StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = lines[index].Replace(quoted, programFileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        return string.Join('\n', lines);
    }

    private static string? ExtractQuotedPath(string mountLine)
    {
        int start = mountLine.IndexOf('"');
        int end = mountLine.LastIndexOf('"');
        return start >= 0 && end > start ? mountLine[(start + 1)..end] : null;
    }
}
