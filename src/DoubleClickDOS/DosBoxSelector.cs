using System.Windows.Forms;

namespace DoubleClickDOS;

/// <summary>Lets the user pick the DOSBox executable (DOSBox, DOSBox SVN or DOSBox-X) and stores it.</summary>
internal static class DosBoxSelector
{
    /// <summary>
    /// Explains why DOSBox is needed and asks whether to pick the executable. Shared by the first start
    /// and the double-click flow. Returns the selected executable, or <c>null</c> when declined/cancelled.
    /// </summary>
    public static string? AskForExecutable(AppSettings settings, string? programName = null)
    {
        string reason = string.IsNullOrWhiteSpace(settings.DosBoxPath)
            ? "No DOSBox executable has been selected yet."
            : $"The configured DOSBox executable was not found:{Environment.NewLine}{settings.DosBoxPath}";

        string introduction = programName is null
            ? "Double-Click DOS needs DOSBox to run DOS games."
            : $"{programName} is a DOS program and needs DOSBox.";

        DialogResult answer = MessageBox.Show(
            $"{introduction}{Environment.NewLine}{Environment.NewLine}" +
            $"{reason}{Environment.NewLine}{Environment.NewLine}" +
            "Do you want to select the DOSBox executable now?",
            "Double-Click DOS",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        Log.Write($"DOSBox not configured ({reason.Replace(Environment.NewLine, " ")}), user answered {answer}.");

        return answer == DialogResult.Yes ? PromptForExecutable(settings) : null;
    }

    /// <summary>Shows the file picker. Returns the selected executable, or <c>null</c> when cancelled.</summary>
    public static string? PromptForExecutable(AppSettings settings)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the DOSBox executable",
            Filter = "DOSBox (dosbox.exe, dosbox-x.exe)|dosbox.exe;dosbox-x.exe;dosbox-x-sdl2.exe;dosbox64.exe|" +
                     "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
        };

        if (settings.ResolveDosBoxExecutable() is { } current)
        {
            dialog.FileName = current;
        }
        else if (Directory.Exists(settings.DosBoxPath))
        {
            dialog.InitialDirectory = settings.DosBoxPath;
        }

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        settings.DosBoxPath = dialog.FileName;
        if (!settings.Save())
        {
            MessageBox.Show(
                $"The DOSBox path could not be saved:{Environment.NewLine}{AppSettings.SettingsFilePath}",
                "Double-Click DOS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        DosBoxFlavor flavor = DosBoxInstallation.DetectFlavor(dialog.FileName);
        Log.Write($"DOSBox executable selected: {dialog.FileName} ({DosBoxInstallation.DisplayName(flavor)})");
        return dialog.FileName;
    }
}
