using System.Windows.Forms;

namespace DoubleClickDOS;

/// <summary>Lets the user pick the MUNT (MT-32) executable and stores it.</summary>
internal static class MuntSelector
{
    /// <summary>Shows the file picker. Returns the selected executable, or <c>null</c> when cancelled.</summary>
    public static string? PromptForExecutable(AppSettings settings)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the MUNT (MT-32) executable",
            Filter = "MUNT (mt32emu-qt.exe, munt.exe)|mt32emu-qt.exe;munt.exe;mt32emu.exe;munt-qt.exe|" +
                     "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
        };

        if (settings.ResolveMuntExecutable() is { } current)
        {
            dialog.FileName = current;
        }
        else if (Directory.Exists(settings.MuntPath))
        {
            dialog.InitialDirectory = settings.MuntPath;
        }

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        settings.MuntPath = dialog.FileName;
        if (!settings.Save())
        {
            MessageBox.Show(
                $"The MUNT path could not be saved:{Environment.NewLine}{AppSettings.SettingsFilePath}",
                "Double-Click DOS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Log.Write($"MUNT executable selected: {dialog.FileName}");
        return dialog.FileName;
    }
}
