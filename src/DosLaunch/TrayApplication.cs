using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DosLaunch;

/// <summary>Tray icon with a right-click menu to pick the DOSBox-X executable and to close the application.</summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly SessionEndWatcher _sessionEndWatcher;

    public TrayApplication()
    {
        _icon = LoadIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Select DOSBox", null, (_, _) => SelectDosBox()));
        menu.Items.Add(new ToolStripMenuItem("Close", null, (_, _) => ExitThread()));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "DOS Launch - 16-bit programs start in DOSBox-X",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _sessionEndWatcher = new SessionEndWatcher(this);
    }

    protected override void ExitThreadCore()
    {
        _sessionEndWatcher.Destroy();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
        base.ExitThreadCore();
    }

    private static void SelectDosBox() => DosBoxSelector.PromptForExecutable(AppSettings.Load());

    private static Icon LoadIcon()
    {
        string pngPath = Path.Combine(AppContext.BaseDirectory, "assets", "prog_icon.png");
        if (File.Exists(pngPath))
        {
            try
            {
                using var bitmap = new Bitmap(pngPath);
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
            {
                Log.Write($"Could not load tray icon {pngPath}: {ex.Message}");
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Hidden top-level window that Windows notifies when the session ends (shutdown, restart, logoff).
    /// The process is terminated without unwinding normal exit paths, so the .exe association has to be
    /// restored from here instead of relying on the cleanup in <see cref="Program"/>.
    /// </summary>
    private sealed class SessionEndWatcher : NativeWindow
    {
        private const int WmEndSession = 0x0016;
        private const int WmExitAfterSessionEnd = 0x8000 + 1; // WM_APP + 1

        private readonly ApplicationContext _context;

        public SessionEndWatcher(ApplicationContext context)
        {
            _context = context;

            CreateHandle(new CreateParams
            {
                Caption = "DosLaunch.SessionEnd",
                Style = 0,
                ExStyle = 0,
                Parent = IntPtr.Zero,
            });
        }

        public void Destroy() => DestroyHandle();

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WmEndSession when m.WParam != IntPtr.Zero:
                    Log.Write("Session is ending, removing exe interception.");
                    ExeFileAssociation.Unregister();
                    // Shut down from a fresh message so the window is not destroyed inside this handler.
                    PostMessage(Handle, WmExitAfterSessionEnd, IntPtr.Zero, IntPtr.Zero);
                    break;

                case WmExitAfterSessionEnd:
                    _context.ExitThread();
                    break;
            }

            base.WndProc(ref m);
        }
    }
}

