using System.Windows.Forms;

namespace DosLaunch;

/// <summary>Lets the user pick the DOSBox executable (DOSBox, DOSBox SVN or DOSBox-X) and stores it.</summary>
internal static class DosBoxSelector
{
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
                "DOS Launch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        DosBoxFlavor flavor = DosBoxInstallation.DetectFlavor(dialog.FileName);
        Log.Write($"DOSBox executable selected: {dialog.FileName} ({DosBoxInstallation.DisplayName(flavor)})");
        return dialog.FileName;
    }
}
