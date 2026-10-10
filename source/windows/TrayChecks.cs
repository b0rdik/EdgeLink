using System.Text.Json;

namespace EdgeLink;
internal static class TrayChecks
{
    internal static async Task<int> Run(MainWindow window) {
        var checks=new List<object>();var failures=new List<string>();
        try{await window.VerifyTray(checks,failures);}catch(Exception e){failures.Add(e.ToString());}
        File.WriteAllText(Path.Combine(Path.GetTempPath(),"EdgeLink-tray-checks.json"),JsonSerializer.Serialize(new {Framework="WinUI 3",Version=Updates.CurrentVersion,Checks=checks,Failures=failures},new JsonSerializerOptions {WriteIndented=true}));
        return failures.Count==0?0:1;
    }
}
public sealed partial class MainWindow
{
    internal async Task VerifyTray(List<object> checks,List<string> failures) {
        void Check(string name,bool pass){checks.Add(new{Name=name,Passed=pass});if(!pass)failures.Add(name);}
        Check("Shell notification icon registered",trayIcon?.Registered==true);
        Desktop.PostMessage(Handle,0x10,0,0);await Settle();Check("Window close hides without terminating",!IsDisposed&&!Desktop.IsWindowVisible(Handle));
        await AcceptInvocation(null);await Settle();Check("Second instance invocation restores",Desktop.IsWindowVisible(Handle)&&!IsDisposed);
        Desktop.ShowWindow(Handle,0);trayIcon!.Route(1);await Settle();Check("Tray Open restores",Desktop.IsWindowVisible(Handle));
        Desktop.ShowWindow(Handle,6);RestoreFromTray();await Settle();Check("Restore leaves minimized state",!Desktop.IsIconic(Handle));
        ShuttingDown=true;Check("OS shutdown bypasses hide",!ShouldHideOnClose);ShuttingDown=false;
        installing=true;Check("Installer close bypasses hide",!ShouldHideOnClose);installing=false;
        var icon=trayIcon;icon.Route(3);Check("Explicit Exit disposes window and tray",IsDisposed&&!icon.Registered);
    }
}
