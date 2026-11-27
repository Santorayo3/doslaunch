using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace DoubleClickDOS;

/// <summary>Small modal window with the logo, a short description and a link to the project page.</summary>
internal sealed class AboutForm : Form
{
    private const string RepositoryUrl = "https://github.com/Santorayo3/doslaunch";

    private readonly PictureBox _logo;

    public AboutForm()
    {
        Text = "About Double-Click DOS";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(415, 380);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _logo = new PictureBox
        {
            Size = new Size(375, 200),
            SizeMode = PictureBoxSizeMode.Zoom,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
        };
        _logo.Image = LoadLogo(Path.Combine(AppContext.BaseDirectory, "assets", "about.png"));
        layout.Controls.Add(_logo, 0, 0);

        var description = new Label
        {
            Text = "Double-Click DOS makes old DOS games start in DOSBox or DOSBox-X " +
                   "when you double-click them in Explorer.",
            AutoSize = true,
            MaximumSize = new Size(375, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 16, 0, 8),
        };
        layout.Controls.Add(description, 0, 1);

        var link = new LinkLabel
        {
            Text = RepositoryUrl,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
        };
        link.LinkClicked += (_, _) => OpenRepository();
        layout.Controls.Add(link, 0, 2);

        var okay = new Button
        {
            Text = "Okay",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(90, 30),
            Anchor = AnchorStyles.None,
            Margin = Padding.Empty,
        };
        layout.Controls.Add(okay, 0, 4);

        AcceptButton = okay;
        CancelButton = okay;
        Controls.Add(layout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logo.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Image? LoadLogo(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = Image.FromStream(stream);
            // Copy the pixels so the file is not kept locked for the lifetime of the dialog.
            return new Bitmap(image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            Log.Write($"Could not load about image {path}: {ex.Message}");
            return null;
        }
    }

    private static void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Write($"Could not open the project page: {ex.Message}");
        }
    }
}
