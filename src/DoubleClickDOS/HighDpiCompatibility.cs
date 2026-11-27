using Microsoft.Win32;

namespace DoubleClickDOS;

/// <summary>
/// Classic DOSBox has no setting for DPI awareness. With Windows display scaling (e.g. 125%) the OS
/// then upscales its fullscreen output a second time, which makes it blurry. The documented fix is the
/// per-application "Override high DPI scaling behaviour" compatibility flag, applied here per user.
/// </summary>
internal static class HighDpiCompatibility
{
    private const string LayersKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string Flag = "HIGHDPIAWARE";

    public static void EnsureApplied(string executablePath)
    {
        try
        {
            using RegistryKey? layers = Registry.CurrentUser.CreateSubKey(LayersKeyPath, writable: true);
            if (layers is null)
            {
                return;
            }

            string? existing = layers.GetValue(executablePath) as string;
            var flags = (existing ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (flags.Any(flag => flag.Equals(Flag, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (flags.Count == 0 || flags[0] != "~")
            {
                flags.Insert(0, "~");
            }

            flags.Add(Flag);
            layers.SetValue(executablePath, string.Join(' ', flags), RegistryValueKind.String);
            Log.Write($"Enabled DPI awareness for {executablePath} so fullscreen stays sharp.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Write($"Could not enable DPI awareness for {executablePath}: {ex.Message}");
        }
    }
}
