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
            int layoutExit=await LayoutChecks.Run(window);
            string layoutReport=Path.Combine(Path.GetTempPath(),"EdgeLink-layout-checks","report.json");
            File.Copy(layoutReport,Path.Combine(folder,"layout.json"),true);
            if(layoutExit!=0) {
                using var report=JsonDocument.Parse(File.ReadAllText(layoutReport));
                foreach(var failure in report.RootElement.GetProperty("Failures").EnumerateArray())failures.Add("Layout: "+failure.GetString());
            }
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
        int originalScreen=screen.SelectedIndex;
        const string fixtureScreen="EdgeLink-diagnostic-display";
        screenDevices.Add(fixtureScreen);screen.Items.Add(fixtureScreen);screen.SelectedIndex=screenDevices.Count-1;
        using(var saved=JsonDocument.Parse(SerializeSettings())) {
            Check("Selected monitor is saved by stable device name",saved.RootElement.GetProperty("screenDevice").GetString()==fixtureScreen);
            screen.SelectedIndex=originalScreen;RestoreScreen(saved.RootElement.GetProperty("screenDevice").GetString());
            Check("Saved monitor selection restores after restart",screen.SelectedIndex==screenDevices.Count-1);
        }
        RestoreScreen("missing-display");Check("Unavailable saved monitor keeps the valid initial selection",screen.SelectedIndex==screenDevices.Count-1);
        screen.SelectedIndex=originalScreen;screen.Items.RemoveAt(screen.Items.Count-1);screenDevices.RemoveAt(screenDevices.Count-1);
        using(var fixture=new Link(10)) {
            link=fixture;fixture.RoundTripMs=17;
            connectionDiagnostics.IsExpanded=false;UpdateStatsTimer();
            Check("Collapsed diagnostics stops statistics",!refresh.IsRunning);
            connectionDiagnostics.IsExpanded=true;await Settle();
            Check("Expanding diagnostics starts the actual timer",refresh.IsRunning);
            Check("First expansion immediately displays live counters",stats.Text.Contains("RTT: 17")&&stats.Text.Contains("пакеты: 0"));
            // The Expander's native reveal animation can still hide its content
            // after the timer is already running. Measure once it has completed.
            await Task.Delay(250);Root.UpdateLayout();
            var statsOrigin=stats.TransformToVisual(connectionDiagnostics).TransformPoint(new Windows.Foundation.Point(0,0));
            Check("Expanded statistics aligns with panel padding without clipping",statsOrigin.X>=16&&statsOrigin.X<50&&stats.ActualWidth>0&&statsOrigin.X+stats.ActualWidth<=connectionDiagnostics.ActualWidth-16);
            Interlocked.Exchange(ref fixture.AudioPackets,100);Interlocked.Exchange(ref fixture.AudioBytes,100500);
            await Task.Delay(1150);
            Check("Dispatcher timer advances counters without manual refresh",stats.Text.Contains("пакеты: 100"));
            await SaveConnectionSnapshot(Path.Combine(folder,"live-statistics-"+Root.ActualTheme.ToString().ToLowerInvariant()+".png"));
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
