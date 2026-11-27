using System.ComponentModel;
using System.Diagnostics;

namespace DoubleClickDOS;

/// <summary>
/// A MUNT (MT-32) instance started for one DOSBox session. It is closed again when the session ends,
/// unless MUNT was already running before the launch.
/// </summary>
internal sealed class MuntSession : IDisposable
{
    private const int ReadyTimeoutMilliseconds = 3000;
    private const int CloseTimeoutMilliseconds = 3000;

    private Process? _process;

    private MuntSession(Process? process) => _process = process;

    /// <summary>Whether this session has to close the MUNT instance it started.</summary>
    public bool OwnsProcess => _process is not null;

    public static MuntSession Start(string executablePath)
    {
        // A MUNT the user started themselves is not ours to close afterwards.
        if (IsAlreadyRunning(executablePath))
        {
            Log.Write("MUNT is already running, so it is left untouched.");
            return new MuntSession(null);
        }

        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            };

            Process? process = Process.Start(startInfo);
            if (process is null)
            {
                Log.Write($"MUNT could not be started: {executablePath}");
                return new MuntSession(null);
            }

            // Give MUNT a moment to open its audio device and MIDI port before DOSBox needs them.
            try
            {
                process.WaitForInputIdle(ReadyTimeoutMilliseconds);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // A windowless build is ready as soon as it runs.
            }

            if (process.HasExited)
            {
                Log.Write($"MUNT exited immediately: {executablePath} (exit code {process.ExitCode})");
                process.Dispose();
                return new MuntSession(null);
            }

            Log.Write($"Started MUNT for MT-32 music: {executablePath} (pid {process.Id})");
            return new MuntSession(process);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            Log.Write($"MUNT could not be started ({executablePath}): {ex.Message}");
            return new MuntSession(null);
        }
    }

    public void Dispose()
    {
        Process? process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                bool closed = process.CloseMainWindow() && process.WaitForExit(CloseTimeoutMilliseconds);
                if (!closed)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(CloseTimeoutMilliseconds);
                }

                Log.Write("Stopped MUNT because DOSBox was closed.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            Log.Write($"MUNT could not be stopped: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static bool IsAlreadyRunning(string executablePath)
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath));
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
