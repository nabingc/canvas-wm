using System.Reflection;

namespace CanvasWm.App;

/// <summary>The notification-area icon and its two-item menu (B65). UI thread only.</summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _retry;
    private readonly ToolStripMenuItem _quit;
    private readonly Func<bool> _isRunning;

    internal TrayMenu(Action onRetry, Action onQuit, Func<bool> isRunning)
    {
        _isRunning = isRunning;
        _retry = new ToolStripMenuItem("Retry shortcuts");
        _retry.Click += (_, _) =>
        {
            if (_isRunning())
            {
                onRetry();
            }
        };
        _quit = new ToolStripMenuItem("Quit CanvasWM");
        _quit.Click += (_, _) =>
        {
            if (_isRunning())
            {
                onQuit();
            }
        };
        _menu.Items.Add(_retry);
        _menu.Items.Add(_quit);
        _menu.Opening += (_, _) =>
        {
            bool running = _isRunning();
            _retry.Enabled = running;
            _quit.Enabled = running;
        };
        _icon = new NotifyIcon
        {
            // SPEC-ASSUMPTION A19: the stock application icon until CanvasWM has its own artwork.
            Icon = SystemIcons.Application,
            Text = "CanvasWM",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseUp += OnMouseUp;
    }

    /// <summary>Shows a balloon tip.</summary>
    internal void ShowBalloon(string text) => _icon.ShowBalloonTip(5000, "CanvasWM", text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // The framework shows the menu itself for the right button; the left button shows the same menu.
        MethodInfo? show = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        if (show is not null)
        {
            show.Invoke(_icon, null);
        }
        else
        {
            _menu.Show(Cursor.Position);
        }
    }
}
