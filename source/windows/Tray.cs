namespace EdgeLink;

internal sealed partial class MainWindow
{
    private NotifyIcon? trayIcon;
    private ContextMenuStrip? trayMenu;
    private ToolStripMenuItem? trayConnection;
    private Icon? trayImage;
    private bool exitRequested;

    private void InitializeTray()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Открыть EdgeLink", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayConnection = new ToolStripMenuItem("Подключить", null, async (_, _) => await ToggleConnection());
        trayMenu.Items.Add(trayConnection);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Выйти из EdgeLink", null, (_, _) => ExitFromTray());
        trayMenu.Opening += (_, _) => UpdateTrayStatus();
        ContextMenuStrip = trayMenu;
        trayImage = new Icon(Icon ?? SystemIcons.Application, SystemInformation.SmallIconSize);
        trayIcon = new NotifyIcon { Icon = trayImage, Text = "EdgeLink — не подключено", ContextMenuStrip = trayMenu, Visible = true };
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private async Task ToggleConnection()
    {
        if (connecting || installing || input == null) return;
        if (link == null) { manuallyPaused = false; await Connect(); }
        else { manuallyPaused = true; Disconnect(); }
    }

    internal void RestoreFromTray()
    {
        if (IsDisposed) return;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    internal void ExitFromTray()
    {
        exitRequested = true;
        Close();
    }

    private void UpdateTrayStatus()
    {
        if (trayIcon == null || trayConnection == null) return;
        trayIcon.Text = link == null ? "EdgeLink — не подключено" : input?.Active == true ? "EdgeLink — управление Mac · звук подключён" : "EdgeLink — управление ПК · звук подключён";
        trayConnection.Text = link == null ? "Подключить" : "Отключить";
        trayConnection.Enabled = !connecting && !installing && input != null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Only a user's window close hides the app. Updates, shutdown and explicit
        // application exit must release the process so the installer/OS can proceed.
        if (trayIcon != null && e.CloseReason == CloseReason.UserClosing && !exitRequested && !installing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
        ContextMenuStrip = null; trayMenu?.Dispose(); trayMenu = null;
        trayImage?.Dispose(); trayImage = null;
        base.OnFormClosed(e);
    }
}
