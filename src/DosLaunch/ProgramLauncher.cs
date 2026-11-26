using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace DosLaunch;

/// <summary>Handles "DosLaunch.exe --launch &lt;file&gt;" invocations coming from the shell.</summary>
internal static class ProgramLauncher
{
    public static int Launch(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            ReportError("No program was passed to the launcher.");
            return 1;
        }

        string file = arguments[0];
        string[] programArguments = arguments[1..];

        if (!File.Exists(file))
        {
            ReportError($"The file no longer exists:{Environment.NewLine}{file}");
            return 1;
        }

        return ExecutableInspector.IsDosProgram(file)
            ? LaunchInDosBox(file, programArguments)
            : LaunchWindowsProgram(file, programArguments);
    }

    private static int LaunchInDosBox(string file, string[] programArguments)
    {
        var settings = AppSettings.Load();
        string? dosBox = settings.ResolveDosBoxExecutable();

        if (dosBox is null)
        {
            dosBox = AskForDosBox(settings, file);
            if (dosBox is null)
            {
                return 1;
            }
        }

        DosBoxFlavor flavor = DosBoxInstallation.DetectFlavor(dosBox);
        string displayName = DosBoxInstallation.DisplayName(flavor);

        if (flavor == DosBoxFlavor.DosBox)
        {
            // Classic DOSBox cannot declare itself DPI aware, so Windows would blur its fullscreen output.
            HighDpiCompatibility.EnsureApplied(dosBox);
        }

        var startInfo = new ProcessStartInfo(dosBox)
        {
            UseShellExecute = false,
            // Hides the console window of console builds such as DOSBox SVN.
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(dosBox)!,
        };

        foreach (string argument in settings.GetExtraArguments())
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (flavor == DosBoxFlavor.DosBoxX)
        {
            startInfo.ArgumentList.Add("-fastlaunch");
        }

        string? configPath = DosBoxConfigFile.EnsureConfig(file, programArguments, settings, flavor);
        if (configPath is null)
        {
            startInfo.ArgumentList.Add(file);
        }
        else
        {
            startInfo.ArgumentList.Add("-conf");
            startInfo.ArgumentList.Add(configPath);
        }

        try
        {
            using var process = Process.Start(startInfo);
            Log.Write(configPath is null
                ? $"Started {displayName} for DOS program: {file}"
                : $"Started {displayName} for DOS program: {file} (config: {configPath})");
            return 0;
        }
        catch (Win32Exception ex)
        {
            ReportError($"{displayName} could not be started:{Environment.NewLine}{ex.Message}");
            Log.Write($"Failed to start {displayName} ({dosBox}): {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Starts a program the way Windows would. Intercepted file types must never be shell-executed,
    /// because the shell would hand them straight back to this launcher.
    /// </summary>
    private static int LaunchWindowsProgram(string file, string[] programArguments)
    {
        bool isBatchFile = string.Equals(Path.GetExtension(file), ".bat", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo(isBatchFile
            ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
            : file)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(file) ?? string.Empty,
        };

        if (isBatchFile)
        {
            // A batch file cannot be started directly, so it is handed to the command interpreter the
            // same way Explorer does. /s lets cmd strip exactly the two quotes around the command.
            startInfo.Arguments = $"/d /s /c \"\"{file}\" {string.Join(' ', programArguments)}\"";
        }
        else
        {
            foreach (string argument in programArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        try
        {
            using var process = Process.Start(startInfo);
            return 0;
        }
        catch (Win32Exception ex)
        {
            ReportError($"\"{file}\" could not be started:{Environment.NewLine}{ex.Message}");
            Log.Write($"Failed to start {file}: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Asks for the DOSBox executable when none is configured (or the configured one is gone) and
    /// continues with the user's choice. Returns <c>null</c> when the user declines.
    /// </summary>
    private static string? AskForDosBox(AppSettings settings, string file)
    {
        string reason = string.IsNullOrWhiteSpace(settings.DosBoxPath)
            ? "No DOSBox executable has been selected yet."
            : $"The configured DOSBox executable was not found:{Environment.NewLine}{settings.DosBoxPath}";

        DialogResult answer = MessageBox.Show(
            $"{Path.GetFileName(file)} is a DOS program and needs DOSBox.{Environment.NewLine}{Environment.NewLine}" +
            $"{reason}{Environment.NewLine}{Environment.NewLine}" +
            "Do you want to select the DOSBox executable now?",
            "DOS Launch",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        Log.Write($"DOSBox not configured ({reason.Replace(Environment.NewLine, " ")}), user answered {answer}.");

        return answer == DialogResult.Yes ? DosBoxSelector.PromptForExecutable(settings) : null;
    }

    private static void ReportError(string message)
    {
        Log.Write(message.Replace(Environment.NewLine, " | "));
        MessageBox.Show(message, "DOS Launch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
