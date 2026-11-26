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

        return ExecutableInspector.IsDosExecutable(file)
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
                ? $"Started {displayName} for 16-bit program: {file}"
                : $"Started {displayName} for 16-bit program: {file} (config: {configPath})");
            return 0;
        }
        catch (Win32Exception ex)
        {
            ReportError($"{displayName} could not be started:{Environment.NewLine}{ex.Message}");
            Log.Write($"Failed to start {displayName} ({dosBox}): {ex}");
            return 1;
        }
    }

    private static int LaunchWindowsProgram(string file, string[] programArguments)
    {
        bool isExecutable = string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo(file)
        {
            // The target must never be shell-executed: the shell would hand it straight back to us.
            UseShellExecute = !isExecutable,
            WorkingDirectory = Path.GetDirectoryName(file) ?? string.Empty,
        };

        foreach (string argument in programArguments)
        {
            startInfo.ArgumentList.Add(argument);
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
            $"\"{Path.GetFileName(file)}\" is a 16-bit DOS program and needs DOSBox.{Environment.NewLine}{Environment.NewLine}" +
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
