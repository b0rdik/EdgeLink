using System.Text.Json;

namespace EdgeLink;

// Real NotifyIcon and window lifecycle, isolated from settings, pairing, hooks,
// network and audio. No synthetic mouse/keyboard input or system shutdown.
internal static class TrayChecks
{
    public static int Run()
    {
        int result = 1;
        using var window = new MainWindow(layoutCheck: true, trayCheck: true);
        window.Shown += (_, _) => window.BeginInvoke(async () =>
        {
            string report = Path.Combine(Path.GetTempPath(), "EdgeLink-tray-checks.json");
            var checks = new List<string>();
            try
            {
                await window.VerifyTray(checks);
                File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
                result = 0;
            }
            catch (Exception error) { File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
            finally { if (!window.IsDisposed) window.ExitFromTray(); }
        });
        Application.Run(window); return result;
    }
}

internal sealed partial class MainWindow
{
    internal async Task VerifyTray(List<string> checks)
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        Check(trayIcon?.Visible == true, "Native tray icon is registered");
        Close(); Application.DoEvents();
        Check(!IsDisposed && !Visible && trayIcon?.Visible == true, "Window close hides the window and preserves the tray lifetime");
        await AcceptInvocation(null); Application.DoEvents();
        Check(Visible && !IsDisposed, "Second invocation restores the same hidden window");
        Close(); ((ToolStripMenuItem)trayMenu!.Items[0]).PerformClick(); Application.DoEvents();
        Check(Visible && !IsDisposed, "Tray Open menu restores the hidden window");
        WindowState = FormWindowState.Minimized; RestoreFromTray(); Application.DoEvents();
        Check(Visible && WindowState == FormWindowState.Normal, "Tray open restores a minimized window");
        using (var updaterWindow = new MainWindow(layoutCheck: true, trayCheck: true))
        {
            updaterWindow.Show(); updaterWindow.installing = true; updaterWindow.Close();
            Check(updaterWindow.IsDisposed && updaterWindow.trayIcon == null, "Updater close exits and removes the tray icon");
        }
        var shutdown = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
        OnFormClosing(shutdown);
        Check(!shutdown.Cancel, "Windows shutdown close is not intercepted (closing event only)");
        ((ToolStripMenuItem)trayMenu!.Items[trayMenu.Items.Count - 1]).PerformClick();
        Check(IsDisposed && trayIcon == null, "Explicit tray exit closes the window and removes the icon");
    }
}
