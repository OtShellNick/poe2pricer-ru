using System.IO;
using Forms = System.Windows.Forms;

namespace Poe2Pricer.App;

// The tool lives in the notification area. A click opens the settings window, which also
// holds "Preise neu laden" and "Beenden": a context menu of its own had to take the
// foreground to open in front of Start and the taskbar, and that closed the taskbar too
// (auto-hiding and fixed taskbar alike).
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;

    public event Action? OpenSettings;

    public TrayIcon(string iconPath)
    {
        icon = new Forms.NotifyIcon
        {
            // In the size of the notification area: there the file holds the mirror alone, drawn for
            // that size, instead of the large picture scaled down.
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath, Forms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Application,
            // Name and version; the status line is the program window's alone.
            Text = Poe2Pricer.Core.AppInfo.NameAndVersion,
            Visible = true,
        };
        icon.MouseClick += (_, _) => OpenSettings?.Invoke();
        icon.BalloonTipClicked += (_, _) => clicked?.Invoke();
        icon.BalloonTipClosed += (_, _) => clicked = null;
    }

    // clicked: what a click on the message does (a newer version: open its page); the message
    // before it, if still shown, loses its click.
    private Action? clicked;

    public void Notify(string text, Action? clicked = null)
    {
        this.clicked = clicked;
        icon.ShowBalloonTip(3000, "poe2pricer", text, Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
