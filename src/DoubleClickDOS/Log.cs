namespace DoubleClickDOS;

/// <summary>Minimal append-only log so invisible launch decisions can be diagnosed later.</summary>
internal static class Log
{
    private const long MaxFileSize = 512 * 1024;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DoubleClickDOS",
        "doubleclickdos.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxFileSize)
            {
                File.Delete(LogFilePath);
            }

            File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never break launching programs.
        }
    }
}
