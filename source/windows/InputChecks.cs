using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Drawing;

namespace EdgeLink;

// Native Windows check; no pairing, audio, network or synthetic user input.
internal static class InputChecks
{
    public static int Run()
    {
        string report = Path.Combine(Path.GetTempPath(), "EdgeLink-input-checks.json");
        var results = new List<object>();
        try {
            int uiThread = Environment.CurrentManagedThreadId;
            uint before = MouseRegistrations();
            using (var input = new Input(_ => { })) {
                int hookThread = input.ProbeThread().GetAwaiter().GetResult();
                if (hookThread == uiThread) throw new Exception("Hooks share the UI thread.");
                if (MouseRegistrations() != before) throw new Exception("Raw mouse is registered while control is on PC.");
                var idle = input.ProbeState().GetAwaiter().GetResult();
                if (idle.MouseHook || idle.Raw || idle.Polling) throw new Exception("Disconnected input retains mouse capture or polling.");
                results.Add(new { check = "separate input thread; no disconnected mouse hook, raw registration or polling", pass = true });
                using var started = new ManualResetEventSlim();
                using var completed = new ManualResetEventSlim();
                Exception? failure = null; double elapsedMs = 0;
                var probe = new Thread(() => {
                    started.Set(); Thread.Sleep(30);
                    var watch = Stopwatch.StartNew();
                    try { if (input.ProbeThread().GetAwaiter().GetResult() != hookThread) throw new Exception("Input thread changed."); }
                    catch (Exception error) { failure = error; }
                    elapsedMs = watch.Elapsed.TotalMilliseconds; completed.Set();
                }) { IsBackground = true };
                probe.Start(); started.Wait();
                Thread.Sleep(300); // Simulate a blocked dashboard without pumping UI messages.
                bool progressedDuringUIStall = completed.IsSet;
                probe.Join();
                if (failure != null) throw failure;
                if (!progressedDuringUIStall || elapsedMs > 150) throw new Exception("Input pump stalled with the dashboard.");
                results.Add(new { check = "input pump responds while UI is blocked for 300 ms", pass = true, elapsedMs });
                // A local unconnected Link never opens sockets. Unreachable test bounds
                // prevent a real edge transfer, clipping or changing the cursor.
                using var link = new Link(10);
                input.Configure(link, new Rectangle(100000, 100000, 1000, 1000), "left", true, 1);
                var connected = input.ProbeState().GetAwaiter().GetResult();
                if (connected.MouseHook || connected.Raw || !connected.Polling || MouseRegistrations() != before)
                    throw new Exception("PC-side connection enables mouse capture instead of polling.");
                results.Add(new { check = "configured PC-side input polls without mouse hook or raw registration", pass = true });
                if(!input.ProbeForeground().GetAwaiter().GetResult().Registered)throw new Exception("Foreground watcher was not registered.");
                input.SetFullscreenProtection(false);
                if(input.ProbeForeground().GetAwaiter().GetResult().Registered)throw new Exception("Disabled fullscreen protection retains event hooks.");
                input.SetFullscreenProtection(true);
                if(!input.ProbeForeground().GetAwaiter().GetResult().Registered)throw new Exception("Fullscreen watcher did not restart.");
                results.Add(new { check = "native foreground hooks register, disable and restart without PC-side mouse capture", pass = true });
                for (int i = 0; i < 5; i++) if (!input.ProbeMouseHookCycle().GetAwaiter().GetResult())
                    throw new Exception("Native mouse hook install/remove failed.");
                if (input.ProbeState().GetAwaiter().GetResult().MouseHook) throw new Exception("Mouse hook leaked after capture lifecycle probes.");
                results.Add(new { check = "five native mouse hook install/remove cycles leave PC uncaptured", pass = true });
                input.Disconnect();
                var disconnected = input.ProbeState().GetAwaiter().GetResult();
                if (disconnected.MouseHook || disconnected.Raw || disconnected.Polling) throw new Exception("Disconnect leaks input resources.");
                if(input.ProbeForeground().GetAwaiter().GetResult().Registered)throw new Exception("Disconnect leaks foreground watcher.");
                results.Add(new { check = "disconnect stops edge polling and preserves uncaptured PC input", pass = true });
                using var lost = new Link(10);
                input.Configure(lost, new Rectangle(100000, 100000, 1000, 1000), "right", true, 1);
                lost.Dispose(); Thread.Sleep(100);
                var failedConnection = input.ProbeState().GetAwaiter().GetResult();
                if (failedConnection.MouseHook || failedConnection.Raw || failedConnection.Polling)
                    throw new Exception("Lost connection leaves mouse capture or edge polling enabled.");
                input.Disconnect();
                results.Add(new { check = "connection loss stops PC-side polling without mouse capture", pass = true });
            }
            if (MouseRegistrations() != before) throw new Exception("Raw registration leaked after input shutdown.");
            results.Add(new { check = "shutdown preserves previous raw registrations", pass = true });
            int cleaned=0;
            for(int cycle=0;cycle<5;cycle++) {
                using var pump=new InputPump(()=>{},()=>Interlocked.Increment(ref cleaned),_=>{});
                var queued=Enumerable.Range(0,200).Select(value=>pump.Query(()=>value)).ToArray();
                if(!Task.WaitAll(queued,5000)||queued.Where((task,index)=>task.Result!=index).Any())throw new Exception("Win32 work messages lost queued probes.");
            }
            if(cleaned!=5)throw new Exception("Win32 pump did not clean up on every restart.");
            results.Add(new {check="1000 queued Win32 probes and five clean pump restarts",pass=true});
            using (var tick = new ManualResetEventSlim()) {
                using var pollPump = new InputPump(() => { }, () => { }, _ => { }, () => tick.Set());
                pollPump.Invoke(() => pollPump.PollEvery(16));
                if (!tick.Wait(1000)) throw new Exception("Native edge timer did not run on input thread.");
                pollPump.Invoke(() => pollPump.PollEvery(0)); tick.Reset();
                if (tick.Wait(80)) throw new Exception("Disabled edge timer still invokes polling callback.");
            }
            results.Add(new { check = "native WM_TIMER starts and stops on input pump", pass = true });
            CheckEdges();
            results.Add(new { check = "sampled edge crossings: both sides, held input, height, parked cursor, cooldown and inward rearm", pass = true });
            CheckFullscreenPolicy();
            results.Add(new { check = "fullscreen client geometry: borderless, title/taskbar, negative coordinates, second monitor and tolerance", pass = true });
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        } catch (Exception error) {
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, results, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
    }
    private static void CheckEdges()
    {
        var bounds = new Rectangle(100, 50, 800, 600);
        var edge = new InputEdge();
        Native.Point P(int x, int y) => new() { X = x, Y = y };
        void Expect(bool value, bool expected, string name) { if (value != expected) throw new Exception("Edge policy: " + name); }
        edge.Reset(P(500, 300));
        Expect(edge.Observe(P(100, 300), bounds, "left", false, 1000), true, "left crossing");
        Expect(edge.Observe(P(100, 300), bounds, "left", false, 1016), false, "parked cursor");
        edge.Reset(P(100, 300));
        Expect(edge.Observe(P(100, 300), bounds, "left", false, 1032), false, "connect while parked at edge");
        edge.Reset(P(500, 300));
        Expect(edge.Observe(P(100, 300), bounds, "left", true, 1100), false, "held key or button");
        Expect(edge.Observe(P(100, 300), bounds, "left", false, 1116), false, "release without movement");
        Expect(edge.Observe(P(100, 301), bounds, "left", false, 1132), true, "new movement after release");
        edge.Reset(P(500, 300));
        Expect(edge.Observe(P(100, 49), bounds, "left", false, 1200), false, "above chosen screen");
        Expect(edge.Observe(P(100, 650), bounds, "left", false, 1216), false, "below chosen screen");
        edge.Reset(P(103, 300), true, 1400);
        Expect(edge.Observe(P(100, 300), bounds, "left", false, 1350), false, "return needs inward movement");
        Expect(edge.Observe(P(107, 300), bounds, "left", false, 1360), false, "move inward during cooldown");
        Expect(edge.Observe(P(100, 300), bounds, "left", false, 1399), false, "cooldown blocks edge");
        Expect(edge.Observe(P(100, 301), bounds, "left", false, 1400), true, "cooldown expires");
        edge.Reset(P(500, 300));
        Expect(edge.Observe(P(899, 300), bounds, "right", false, 2000), true, "right crossing");
        edge.Reset(P(896, 300), true, 2200);
        Expect(edge.Observe(P(899, 300), bounds, "right", false, 2300), false, "right inward required");
        Expect(edge.Observe(P(893, 300), bounds, "right", false, 2300), false, "right inward");
        Expect(edge.Observe(P(899, 300), bounds, "right", false, 2316), true, "right rearmed crossing");
    }
    private static void CheckFullscreenPolicy()
    {
        var screen = new Rectangle(-2560, 0, 2560, 1440);
        void Expect(Rectangle client, bool expected) {
            if(ForegroundGuard.CoversScreen(client,screen)!=expected)throw new Exception("Fullscreen policy: "+client);
        }
        Expect(screen,true);
        Expect(new Rectangle(-2560,32,2560,1408),false); // ordinary title bar
        Expect(new Rectangle(-2560,0,2560,1392),false); // taskbar remains visible
        Expect(new Rectangle(0,0,2560,1440),false); // game on another monitor
        Expect(new Rectangle(-2560,0,5120,1440),true); // spans chosen monitor
        Expect(new Rectangle(-2559,1,2558,1438),true); // one-pixel rounding
        Expect(new Rectangle(-2558,0,2558,1440),false);
        Expect(Rectangle.Empty,false);
    }
    private static uint MouseRegistrations()
    {
        uint count = 0, size = (uint)Marshal.SizeOf<Native.RawDevice>();
        if (Native.GetRegisteredRawInputDevices(0, ref count, size) == uint.MaxValue) throw new System.ComponentModel.Win32Exception();
        if (count == 0) return 0;
        nint data = Marshal.AllocHGlobal(checked((int)(count * size)));
        try {
            uint received = Native.GetRegisteredRawInputDevices(data, ref count, size);
            if (received == uint.MaxValue) throw new System.ComponentModel.Win32Exception();
            uint mice = 0;
            for (int i = 0; i < received; i++) {
                var device = Marshal.PtrToStructure<Native.RawDevice>(data + (int)(i * size));
                if (device.UsagePage == 1 && device.Usage == 2) mice++;
            }
            return mice;
        } finally { Marshal.FreeHGlobal(data); }
    }
}
