using Microsoft.Win32;

namespace DosLaunch;

/// <summary>
/// Routes shell launches of .exe files (e.g. an Explorer double-click) through this application by
/// overriding the per-user "exefile" association. The association the user had before is remembered
/// and restored as soon as the app stops.
/// </summary>
internal static class ExeFileAssociation
{
    internal const string LaunchSwitch = "--launch";

    private const string CommandKeyPath = @"Software\Classes\exefile\shell\open\command";
    private const string TopOfOwnedKeyPath = @"Software\Classes\exefile";
    private const string StateKeyPath = @"Software\DosLaunch";
    private const string BackupPresentValue = "BackupPresent";
    private const string BackupCommandValue = "BackupCommand";

    public static void Register()
    {
        using var commandKey = Registry.CurrentUser.CreateSubKey(CommandKeyPath, writable: true)
                               ?? throw new InvalidOperationException($"Could not open HKCU\\{CommandKeyPath}.");

        // Remember the previously installed association once, so unregistering (even in a later
        // process, after a crash) restores what the user had instead of what we wrote.
        using (var state = Registry.CurrentUser.CreateSubKey(StateKeyPath, writable: true)!)
        {
            if (state.GetValue(BackupPresentValue) is null)
            {
                string? previousCommand = commandKey.GetValue(null) as string;
                state.SetValue(BackupPresentValue, previousCommand is null ? 0 : 1, RegistryValueKind.DWord);
                state.SetValue(BackupCommandValue, previousCommand ?? string.Empty, RegistryValueKind.String);
            }
        }

        string command = BuildCommand(
            Environment.ProcessPath ?? throw new InvalidOperationException("Could not determine the path of this executable."));
        commandKey.SetValue(null, command, RegistryValueKind.String);
        Log.Write($"Registered exe interception: {command}");
    }

    public static void Unregister()
    {
        bool backupPresent;
        string backupCommand;

        using (var state = Registry.CurrentUser.OpenSubKey(StateKeyPath))
        {
            if (state is null)
            {
                return;
            }

            backupPresent = state.GetValue(BackupPresentValue) is int present && present != 0;
            backupCommand = state.GetValue(BackupCommandValue) as string ?? string.Empty;
        }

        if (backupPresent)
        {
            using var commandKey = Registry.CurrentUser.CreateSubKey(CommandKeyPath, writable: true)!;
            commandKey.SetValue(null, backupCommand, RegistryValueKind.String);
        }
        else
        {
            using (var commandKey = Registry.CurrentUser.OpenSubKey(CommandKeyPath, writable: true))
            {
                commandKey?.DeleteValue(string.Empty, throwOnMissingValue: false);
            }

            DeleteEmptyKeyTree();
        }

        Registry.CurrentUser.DeleteSubKeyTree(StateKeyPath, throwOnMissingSubKey: false);
        Log.Write("Removed exe interception.");
    }

    internal static string BuildCommand(string applicationPath) =>
        $"\"{applicationPath}\" {LaunchSwitch} \"%1\" %*";

    /// <summary>Removes the keys we created, stopping at the first key that still holds data.</summary>
    private static void DeleteEmptyKeyTree()
    {
        string path = CommandKeyPath;
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

            if (path.Length <= TopOfOwnedKeyPath.Length)
            {
                return;
            }

            path = path[..path.LastIndexOf('\\')];
        }
    }
}
