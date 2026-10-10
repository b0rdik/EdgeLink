using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

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
            using var owner = new Control(); _ = owner.Handle;
            using (var input = new Input(owner)) {
                int hookThread = input.ProbeThread().GetAwaiter().GetResult();
                if (hookThread == uiThread) throw new Exception("Hooks share the UI thread.");
                if (MouseRegistrations() != before) throw new Exception("Raw mouse is registered while control is on PC.");
                results.Add(new { check = "separate input thread; no PC-side raw registration", pass = true });
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
            }
            if (MouseRegistrations() != before) throw new Exception("Raw registration leaked after input shutdown.");
            results.Add(new { check = "shutdown preserves previous raw registrations", pass = true });
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, results }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        } catch (Exception error) {
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, results, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
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
