using System.Windows.Forms;

namespace DosLaunch;

internal static class Program
{
    private const string RegisterSwitch = "--register";
    private const string UnregisterSwitch = "--unregister";

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Unhandled exception: {e.ExceptionObject}");

        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case ProgramFileAssociation.LaunchSwitch:
                    return ProgramLauncher.Launch(args[1..]);
                case RegisterSwitch:
                    ProgramFileAssociation.Register();
                    return 0;
                case UnregisterSwitch:
                    ProgramFileAssociation.Unregister();
                    return 0;
            }
        }

        return RunTrayApplication();
    }

    private static int RunTrayApplication()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\DosLaunch.Tray", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Write("Another instance is already running, exiting.");
            MessageBox.Show(
                "DOS Launch is already running." + Environment.NewLine + Environment.NewLine +
                "Use its icon in the notification area to select DOSBox or to close it.",
                "DOS Launch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            ProgramFileAssociation.Register();
        }
        catch (Exception ex)
        {
            Log.Write($"Failed to register program interception: {ex}");
            MessageBox.Show(
                $"Could not take over program launches:{Environment.NewLine}{ex.Message}",
                "DOS Launch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        try
        {
            Application.Run(new TrayApplication());
        }
        finally
        {
            ProgramFileAssociation.Unregister();
        }

        return 0;
    }
}
