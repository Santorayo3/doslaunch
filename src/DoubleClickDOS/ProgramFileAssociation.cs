using Microsoft.Win32;

namespace DoubleClickDOS;

/// <summary>
/// Routes shell launches of .exe, .com and .bat files (e.g. an Explorer double-click) through this
/// application by overriding the per-user associations. The association the user had before is
/// remembered per file type and restored as soon as the app stops.
/// </summary>
internal static class ProgramFileAssociation
{
    internal const string LaunchSwitch = "--launch";

    private const string ExeClass = "exefile";
    // The key keeps its original name so backups written by earlier versions are still found and the
    // associations the user really had are restored.
    private const string StateKeyPath = @"Software\DosLaunch";

    // Values written by versions that only intercepted .exe files.
    private const string LegacyBackupPresentValue = "BackupPresent";
    private const string LegacyBackupCommandValue = "BackupCommand";

    private const string PresentValue = "Present";
    private const string CommandValue = "Command";

    /// <summary>ProgIDs of the file types whose shell launches are intercepted.</summary>
    private static readonly string[] ProgramClasses = [ExeClass, "comfile", "batfile"];

    public static void Register()
    {
        string applicationPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the path of this executable.");
        string command = BuildCommand(applicationPath);

        MigrateLegacyBackup();

        foreach (string programClass in ProgramClasses)
        {
            Register(programClass, command);
        }
    }

    public static void Unregister()
    {
        foreach (string programClass in ProgramClasses)
        {
            Unregister(programClass);
        }

        Registry.CurrentUser.DeleteSubKeyTree(StateKeyPath, throwOnMissingSubKey: false);
        Log.Write("Removed program interception.");
    }

    internal static string BuildCommand(string applicationPath) =>
        $"\"{applicationPath}\" {LaunchSwitch} \"%1\" %*";

    private static void Register(string programClass, string command)
    {
        string commandKeyPath = CommandKeyPath(programClass);
        using var commandKey = Registry.CurrentUser.CreateSubKey(commandKeyPath, writable: true)
                               ?? throw new InvalidOperationException($@"Could not open HKCU\{commandKeyPath}.");

        // Remember the previously installed association once, so unregistering (even in a later
        // process, after a crash) restores what the user had instead of what we wrote.
        RememberPreviousCommand(programClass, commandKey.GetValue(null) as string);

        commandKey.SetValue(null, command, RegistryValueKind.String);
        Log.Write($"Registered {programClass} interception: {command}");
    }

    private static void RememberPreviousCommand(string programClass, string? previousCommand)
    {
        using var backup = Registry.CurrentUser.CreateSubKey(BackupKeyPath(programClass), writable: true)
                           ?? throw new InvalidOperationException($@"Could not open HKCU\{BackupKeyPath(programClass)}.");

        if (backup.GetValue(PresentValue) is not null)
        {
            return;
        }

        backup.SetValue(PresentValue, previousCommand is null ? 0 : 1, RegistryValueKind.DWord);
        backup.SetValue(CommandValue, previousCommand ?? string.Empty, RegistryValueKind.String);
    }

    private static void Unregister(string programClass)
    {
        using var backup = Registry.CurrentUser.OpenSubKey(BackupKeyPath(programClass));
        if (backup is null)
        {
            // This file type was never taken over, so its association is left untouched.
            return;
        }

        bool backupPresent = backup.GetValue(PresentValue) is int present && present != 0;
        string backupCommand = backup.GetValue(CommandValue) as string ?? string.Empty;

        if (backupPresent)
        {
            using var commandKey = Registry.CurrentUser.CreateSubKey(CommandKeyPath(programClass), writable: true)!;
            commandKey.SetValue(null, backupCommand, RegistryValueKind.String);
        }
        else
        {
            using (var commandKey = Registry.CurrentUser.OpenSubKey(CommandKeyPath(programClass), writable: true))
            {
                commandKey?.DeleteValue(string.Empty, throwOnMissingValue: false);
            }

            DeleteEmptyKeyTree(programClass);
        }
    }

    /// <summary>Moves a backup that an .exe-only version stored directly under the state key.</summary>
    private static void MigrateLegacyBackup()
    {
        using var state = Registry.CurrentUser.OpenSubKey(StateKeyPath, writable: true);
        if (state?.GetValue(LegacyBackupPresentValue) is not int present)
        {
            return;
        }

        using (var backup = Registry.CurrentUser.CreateSubKey(BackupKeyPath(ExeClass), writable: true))
        {
            if (backup is not null && backup.GetValue(PresentValue) is null)
            {
                backup.SetValue(PresentValue, present, RegistryValueKind.DWord);
                backup.SetValue(
                    CommandValue,
                    state.GetValue(LegacyBackupCommandValue) as string ?? string.Empty,
                    RegistryValueKind.String);
            }
        }

        state.DeleteValue(LegacyBackupPresentValue, throwOnMissingValue: false);
        state.DeleteValue(LegacyBackupCommandValue, throwOnMissingValue: false);
    }

    private static string CommandKeyPath(string programClass) =>
        $@"Software\Classes\{programClass}\shell\open\command";

    private static string BackupKeyPath(string programClass) =>
        $@"Software\DosLaunch\Backup\{programClass}";

    /// <summary>Removes the keys we created, stopping at the first key that still holds data.</summary>
    private static void DeleteEmptyKeyTree(string programClass)
    {
        string top = $@"Software\Classes\{programClass}";
        string path = CommandKeyPath(programClass);

        while (true)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key is not null && (key.ValueCount > 0 || key.SubKeyCount > 0))
                {
                    return;
                }
            }

            Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);

            if (path.Length <= top.Length)
            {
                return;
            }

            path = path[..path.LastIndexOf('\\')];
        }
    }
}
