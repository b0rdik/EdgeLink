using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// Passive, separate-process Windows input observer. Never injects or swallows input.
// Persist only timing/count aggregates, never coordinates, button/key data or device IDs.
internal static class Program
{
    private const int Capacity = 1_000_000;
    private static readonly long[] ChainTicks = new long[Capacity];
    private static readonly long[] RawGapTicks = new long[Capacity];
    private static readonly long[] PerSecondRaw = new long[120];
    private static readonly long[] PerSecondHook = new long[120];
    private static readonly Hook HookCallback = ObserveHook;
    private static readonly WndProc WindowCallback = ObserveRaw;
    private static nint hook, window;
    private static long begin, end, previousRaw;
    private static int hookCount, rawCount, rawGapCount, suppressed, droppedSamples, rawErrors;

    public static int Main(string[] args)
    {
        int seconds = int.Parse(args[0]);
        if (seconds is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(seconds));
        string output = Path.GetFullPath(args[1]);
        string scenario = args[2];
        int targetId = int.Parse(args[3]);
        Process? target = targetId == 0 ? null : Process.GetProcessById(targetId);
        string? version = target?.MainModule?.FileVersionInfo.ProductVersion;
        // Warm the runtime before installing a hook; fixed arrays are allocated up front.
        for (int i = 0; i < 10000; i++) _ = Stopwatch.GetTimestamp();
        nint module = GetModuleHandle(null);
        WndClass cls = new() { Callback = Marshal.GetFunctionPointerForDelegate(WindowCallback), Instance = module, Name = "EdgeLinkMouseImpactObserver" };
        if (RegisterClass(ref cls) == 0) throw new Win32Exception();
        window = CreateWindowEx(0, cls.Name, "", 0, 0, 0, 0, 0, -3, 0, module, 0);
        if (window == 0) throw new Win32Exception();
        RawDevice[] devices = [new() { Page = 1, Usage = 2, Flags = 0x100, Target = window }];
        try
        {
            if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>())) throw new Win32Exception();
            hook = SetWindowsHookEx(14, HookCallback, module, 0);
            if (hook == 0) throw new Win32Exception();
            uint threadId = GetCurrentThreadId();
            begin = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;
            end = begin + seconds * Stopwatch.Frequency;
            Console.WriteLine("Observer ready: 3-second warm-up, then physical mouse sampling.");
            using var cancellation = new CancellationTokenSource();
            Task stopper = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds + 3), cancellation.Token);
                if (!PostThreadMessage(threadId, 0x12, 0, 0)) throw new Win32Exception();
            });
            target?.Refresh();
            double targetCpuStart = target?.TotalProcessorTime.TotalSeconds ?? 0;
            var own = Process.GetCurrentProcess();
            double ownCpuStart = own.TotalProcessorTime.TotalSeconds;
            long wallStart = Stopwatch.GetTimestamp();
            int status;
            while ((status = GetMessage(out Message message, 0, 0, 0)) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            if (status < 0) throw new Win32Exception();
            stopper.GetAwaiter().GetResult();
            double observedSeconds = (Stopwatch.GetTimestamp() - wallStart) / (double)Stopwatch.Frequency;
            target?.Refresh(); own.Refresh();
            double targetCpu = (target?.TotalProcessorTime.TotalSeconds ?? 0) - targetCpuStart;
            double ownCpu = own.TotalProcessorTime.TotalSeconds - ownCpuStart;
            var result = new
            {
                scenario, configuredPollingHz = 4000, targetProductVersion = version,
                warmupSeconds = 3, sampledSeconds = seconds, observedSeconds,
                physicalMoveHookEvents = hookCount, rawMovementEvents = rawCount,
                chainTiming = Summarize(ChainTicks, hookCount), rawArrivalGaps = Summarize(RawGapTicks, rawGapCount),
                perSecond = Enumerable.Range(0, seconds).Select(i => new { second = i + 1, rawMovementEvents = PerSecondRaw[i], physicalMoveHookEvents = PerSecondHook[i] }),
                suppressedDownstream = suppressed, droppedTimingSamples = droppedSamples, rawReadErrors = rawErrors,
                logicalProcessors = Environment.ProcessorCount,
                targetCpuPercentMachine = 100 * targetCpu / observedSeconds / Environment.ProcessorCount,
                targetCpuPercentOneCore = 100 * targetCpu / observedSeconds,
                observerCpuPercentMachine = 100 * ownCpu / observedSeconds / Environment.ProcessorCount,
                targetWorkingSetMiB = target?.WorkingSet64 / 1048576.0,
                note = "Hook timings cover CallNextHookEx and the remaining existing hook chain, including scheduler/IPC costs. Raw arrival gaps are observer delivery gaps, not USB polling latency or proof of lost reports. CPU includes the 3-second warm-up. All physical mouse devices are counted together; injected hook events are excluded."
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        finally
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
            devices[0].Flags = 1; devices[0].Target = 0;
            RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>());
            if (window != 0) DestroyWindow(window);
            UnregisterClass(cls.Name, module);
            target?.Dispose();
        }
    }

    private static unsafe nint ObserveHook(int code, nint message, nint data)
    {
        long now = Stopwatch.GetTimestamp();
        if (code < 0 || message != 0x200 || (((MouseData*)data)->Flags & 1) != 0 || now < begin || now >= end)
            return CallNextHookEx(hook, code, message, data);
        long start = Stopwatch.GetTimestamp();
        nint result = CallNextHookEx(hook, code, message, data);
        long elapsed = Stopwatch.GetTimestamp() - start;
        if (hookCount < Capacity) ChainTicks[hookCount++] = elapsed; else droppedSamples++;
        PerSecondHook[(now - begin) / Stopwatch.Frequency]++;
        if (result != 0) suppressed++;
        return result;
    }

    private static unsafe nint ObserveRaw(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message == 0xff)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= begin && now < end)
            {
                byte* buffer = stackalloc byte[4096]; uint size = 4096;
                uint header = (uint)Marshal.SizeOf<RawHeader>();
                uint received = GetRawInputData(lParam, 0x10000003, (nint)buffer, ref size, header);
                if (received == uint.MaxValue || received < header + 24 || received > 4096) rawErrors++;
                else if (*(uint*)buffer == 0 && (*(ushort*)(buffer + header) & 1) == 0 &&
                         (*(int*)(buffer + header + 12) != 0 || *(int*)(buffer + header + 16) != 0))
                {
                    rawCount++; PerSecondRaw[(now - begin) / Stopwatch.Frequency]++;
                    if (previousRaw != 0)
                    {
                        if (rawGapCount < Capacity) RawGapTicks[rawGapCount++] = now - previousRaw; else droppedSamples++;
                    }
                    previousRaw = now;
                }
            }
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static object Summarize(long[] ticks, int count)
    {
        if (count == 0) return new { samples = 0 };
        long[] values = ticks.AsSpan(0, count).ToArray(); Array.Sort(values);
        double Ms(long value) => 1000.0 * value / Stopwatch.Frequency;
        double P(double fraction) => Ms(values[Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1)]);
        return new { samples = count, meanMs = values.Average(v => Ms(v)), p50Ms = P(.5), p95Ms = P(.95), p99Ms = P(.99), p999Ms = P(.999), maxMs = Ms(values[^1]), over1Ms = values.Count(v => Ms(v) > 1), over5Ms = values.Count(v => Ms(v) > 5) };
    }

    private delegate nint Hook(int code, nint message, nint data);
    private delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type, Size; public nint Device, WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WndClass { public uint Style; public nint Callback; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassW", SetLastError = true)] private static extern ushort RegisterClass(ref WndClass cls);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")] private static extern bool UnregisterClass(string cls, nint module);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint module, nint param);
    [DllImport("user32")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32", EntryPoint = "GetMessageW", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32", EntryPoint = "PostThreadMessageW", SetLastError = true)] private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("user32", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern nint SetWindowsHookEx(int kind, Hook callback, nint module, uint thread);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
}
