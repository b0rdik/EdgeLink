using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using System.Runtime.InteropServices.WindowsRuntime;

namespace EdgeLink;

// An isolated native XAML window shown outside every monitor without activation.
// No credentials, input hooks, audio output, real connection or tray icon.
internal static class BackgroundUiChecks
{
    internal static async Task<int> Run(MainWindow window) {
        string folder=Path.Combine(Path.GetTempPath(),"EdgeLink-background-ui-checks");Directory.CreateDirectory(folder);
        var checks=new List<object>();var failures=new List<string>();
        try {
            await window.VerifyConnectionPresentation(folder,checks,failures);
            if(await LayoutChecks.Run(window)!=0)failures.Add("Native layout checks failed; see EdgeLink-layout-checks/report.json.");
        } catch(Exception error){failures.Add(error.ToString());}
        File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new {Version=Updates.CurrentVersion,Framework="WinUI 3",NoActivation=true,Checks=checks,Failures=failures},new JsonSerializerOptions {WriteIndented=true}));
        return failures.Count==0?0:1;
    }
}

public sealed partial class MainWindow
{
    internal async Task VerifyConnectionPresentation(string folder,List<object> checks,List<string> failures) {
        void Check(string name,bool passed){checks.Add(new{Name=name,Passed=passed});if(!passed)failures.Add(name);}
        Check("Isolated preview has no input, audio, real connection or tray",previewMode&&input==null&&output==null&&link==null&&trayIcon==null);
        Check("Background window never receives foreground focus",Native.GetForegroundWindow()!=Handle);
        Check("Background window is excluded from Alt+Tab",!AppWindow.IsShownInSwitchers);
        Check("Background window remains outside the desktop",AppWindow.Position.X<=-8000&&AppWindow.Position.Y<=-8000);
        SelectPage(0);
        foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}) {
            Root.RequestedTheme=theme;await Settle();
            foreach(bool onRight in new[]{false,true}) {
                side.SelectedIndex=onRight?1:0;
                foreach(var state in new[]{(connected:false,active:false,pending:false),(connected:false,active:false,pending:true),(connected:true,active:false,pending:false),(connected:true,active:true,pending:false)}) {
                    RenderConnectionState(state.connected,state.active,false,state.pending);await Settle();
                    string label=$"{theme}-{(onRight?"right":"left")}-{(state.pending?"connecting":!state.connected?"disconnected":state.active?"mac":"pc")}";
                    bool macHighlighted=ReferenceEquals(macTile.BorderBrush,Theme.Mint),pcHighlighted=ReferenceEquals(pcTile.BorderBrush,Theme.Mint);
                    Check(label+": exactly the input owner is highlighted",macHighlighted==(state.connected&&state.active)&&pcHighlighted==(state.connected&&!state.active));
                    Check(label+": active borders have visible thickness",macTile.BorderThickness.Left>0&&pcTile.BorderThickness.Left>0);
                    Check(label+": keyboard, mouse and audio match state",keyboardStatus.Text==mouseStatus.Text&&keyboardStatus.Text==(!state.connected?"Не подключено":state.active?"На MacBook":"На ПК")&&audioStatus.Text==(state.connected?"Принимается":"Не передаётся"));
                    Check(label+": diagram follows left/right setting",Grid.GetColumn(macTile)==(onRight?2:0)&&Grid.GetColumn(pcTile)==(onRight?0:2));
                    Check(label+": connection heading follows state",connectionHeading.Text==(state.pending?"Подключение…":!state.connected?"Не подключено":state.active?"Управление на MacBook":"Подключено"));
                    Check(label+": connect button follows state",connect.Text==(state.connected?"Отключить":"Подключить"));
                    Check(label+": explanation matches connected state",state.pending?status.Text.Contains("Подключение"):state.connected?status.Text.Contains(state.active?"управляют Mac":"соединены"):status.Text.Contains("Подключите Mac"));
                    if(!onRight)await SaveConnectionSnapshot(Path.Combine(folder,label+".png"));
                }
            }
            RenderConnectionState(true,false,true,false);
            Check(theme+": fullscreen pause is explained",inputHint.Text.Contains("приостановлены"));
        }
        Root.RequestedTheme=ElementTheme.Default;side.SelectedIndex=0;
        lastError="Не удалось открыть файл: диагностический пример";RenderConnectionState(true,false,false,false);
        Check("Import errors stay visible while the existing connection is alive",status.Text==lastError&&keyboardStatus.Text=="На ПК");
        RenderConnectionState(false,false,false,false);Check("Disconnected errors remain visible",status.Text==lastError);
        lastError="";RenderConnectionState(true,false,false,false);Check("Successful connection clears old error presentation",status.Text.Contains("соединены"));
        using(var fixture=new Link(10)) {
            link=fixture;fixture.RoundTripMs=17;
            connectionDiagnostics.IsExpanded=false;UpdateStatsTimer();
            Check("Collapsed diagnostics stops statistics",!refresh.IsRunning);
            connectionDiagnostics.IsExpanded=true;await Settle();
            Check("Expanding diagnostics starts the actual timer",refresh.IsRunning);
            Check("First expansion immediately displays live counters",stats.Text.Contains("RTT: 17")&&stats.Text.Contains("пакеты: 0"));
            Interlocked.Exchange(ref fixture.AudioPackets,100);Interlocked.Exchange(ref fixture.AudioBytes,100500);
            await Task.Delay(1150);
            Check("Dispatcher timer advances counters without manual refresh",stats.Text.Contains("пакеты: 100"));
            SelectPage(2);await Settle();Check("Other page stops statistics",!refresh.IsRunning);
            SelectPage(0);await Settle();Check("Returning to connection resumes statistics",refresh.IsRunning);
            AppWindow.Hide();UpdateStatsTimer();Check("Hidden window stops statistics",!refresh.IsRunning);
            AppWindow.Show(false);UpdateStatsTimer();Check("Non-activating restore resumes statistics",refresh.IsRunning);
            connectionDiagnostics.IsExpanded=false;Check("Collapsing diagnostics stops statistics",!refresh.IsRunning);
            connectionDiagnostics.IsExpanded=true;fixture.Dispose();UpdateStatsTimer();
            Check("Lost connection stops statistics",!refresh.IsRunning);
            link=null;UpdateStatsTimer();
        }
        Check("No physical input or audio was started by checks",input==null&&output==null&&endpoint==null&&trayIcon==null);
        Check("Checks never activated their window",Native.GetForegroundWindow()!=Handle);
        connectionDiagnostics.IsExpanded=false;UpdateWizard();
    }
    private async Task SaveConnectionSnapshot(string path) {
        await Task.Delay(180);
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);
        CryptographicBuffer.CopyToByteArray(await bitmap.GetPixelsAsync(),out byte[] pixels);
        using var file=File.Create(path);using var stream=file.AsRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
    }
}
