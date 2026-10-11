using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EdgeLink;

// Real borderless WinUI fixture, foreground and resize events. No sockets, audio,
// settings, credentials, synthetic mouse/keyboard events or capture/clipping.
internal static class FullscreenChecks
{
    internal static async Task<int> Run(MainWindow window)
    {
        string report=Path.Combine(Path.GetTempPath(),"EdgeLink-fullscreen-checks.json");
        var results=new List<object>(); Window? other=null;
        try {
            using var input=new Input(_=>{});
            using var link=new Link(10);
            var screen=Desktop.PrimaryScreen.Bounds;
            var unreachable=new Rectangle(100000,100000,1000,1000);
            input.Configure(link,unreachable,"left",true,1);
            input.ProbeProtectionScreen(screen);
            window.Activate(); await WaitFor(input,false);
            results.Add(new {check="ordinary foreground window keeps edge polling and no mouse hook/raw capture",pass=true});
            var ordinary=await Measure(input,1000);
            if(ordinary.samples<10)throw new Exception("Ordinary foreground did not sample the edge.");
            window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            await WaitFor(input,true);
            if(!ForegroundGuard.IsFullscreen(window.Handle,screen))throw new Exception("Native borderless fixture was not fullscreen.");
            results.Add(new {check="real borderless fullscreen foreground disables edge timer without mouse capture",pass=true});
            var blocked=await Measure(input,1000);
            if(blocked.samples!=0||blocked.foregroundRefreshes!=0)throw new Exception("Fullscreen idle keeps polling cursor/foreground.");
            results.Add(new {check="one second fullscreen idle has zero edge samples and zero repeated foreground inspections",pass=true,ordinary,blocked});
            await Task.Run(()=> {
                var watch=Stopwatch.StartNew();input.ProbeThread().GetAwaiter().GetResult();
                if(watch.ElapsedMilliseconds>150)throw new Exception("Input pump stalled in fullscreen.");
            });
            input.SetFullscreenProtection(false); await WaitFor(input,false);
            if((await input.ProbeForeground()).Registered)throw new Exception("Disabled protection keeps native foreground hooks.");
            input.SetFullscreenProtection(true); input.ProbeProtectionScreen(screen); await WaitFor(input,true);
            results.Add(new {check="live protection toggle unregisters events/resumes polling and re-enabling pauses again",pass=true});
            other=new Window {Title="EdgeLink — проверка смены окна",Content=new TextBlock {Text="Проверка автоматического восстановления переходов",Margin=new Thickness(24)}};
            other.Activate(); await WaitFor(input,false);
            results.Add(new {check="focus switch from fullscreen to ordinary second window automatically resumes polling",pass=true});
            window.Activate(); await WaitFor(input,true);
            results.Add(new {check="returning focus to the fullscreen fixture pauses polling again",pass=true});
            window.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            await WaitFor(input,false);
            results.Add(new {check="foreground window leaves fullscreen without changing focus; geometry event resumes polling",pass=true});
            input.Disconnect();
            var stopped=await input.ProbeForeground();var state=await input.ProbeState();
            if(stopped.Registered||stopped.Blocked||state.Polling||state.MouseHook||state.Raw)throw new Exception("Disconnect leaks foreground/capture resources.");
            results.Add(new {check="disconnect removes event watchers and edge polling",pass=true});
            File.WriteAllText(report,JsonSerializer.Serialize(new {passed=true,version=Updates.CurrentVersion,results},new JsonSerializerOptions {WriteIndented=true}));
            return 0;
        } catch(Exception error) {
            File.WriteAllText(report,JsonSerializer.Serialize(new {passed=false,results,error=error.ToString()},new JsonSerializerOptions {WriteIndented=true}));return 1;
        } finally {other?.Close();}
    }
    private static async Task WaitFor(Input input,bool blocked)
    {
        var watch=Stopwatch.StartNew();
        while(watch.ElapsedMilliseconds<3000) {
            var focus=await input.ProbeForeground(); var state=await input.ProbeState();
            if(state.MouseHook||state.Raw)throw new Exception("Native fixture captured physical input.");
            if(focus.Blocked==blocked&&state.Polling!=blocked)return;
            await Task.Delay(20);
        }
        throw new Exception("Foreground protection did not reach expected state: blocked="+blocked);
    }
    private sealed record Sample(double elapsedMs,long samples,long foregroundRefreshes,double inputThreadCpuMs);
    private static async Task<Sample> Measure(Input input,int milliseconds)
    {
        await Task.Delay(150);var before=await input.ProbeSampling();var watch=Stopwatch.StartNew();
        await Task.Delay(milliseconds);var after=await input.ProbeSampling();
        return new(watch.Elapsed.TotalMilliseconds,after.Samples-before.Samples,after.Refreshes-before.Refreshes,(after.CpuTicks-before.CpuTicks)/10000.0);
    }
}
