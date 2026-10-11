using System.Runtime.InteropServices;
using System.Drawing;

namespace EdgeLink;

internal sealed class Input : IDisposable
{
    private readonly Action<Action> dispatch;
    private readonly InputPump pump;
    private Link? connection;
    private readonly Native.Hook keyboardCallback, mouseCallback;
    private nint keyboardHook, mouseHook;
    private readonly HashSet<int> physicalKeys = [];
    private readonly HashSet<int> swallowedKeys = [];
    private Rectangle pcBounds;
    private double x, y;
    private uint epoch;
    private readonly InputEdge edge = new();
    private ForegroundGuard? foreground;
    private bool fullscreenProtection = true;
    private volatile bool fullscreenBlocked;
    private long edgeSamples;
    private volatile bool active;
    private bool rawRegistered, timerResolution;
    private nint rawMemory;
    private const int RawCapacity = 4096;
    private static readonly uint RawHeaderSize = (uint)Marshal.SizeOf<Native.RawHeader>();
    private readonly Native.RawDevice[] rawDevice = [new() { UsagePage = 1, Usage = 2 }];
    private string Side = "left";
    private bool SwapControlCommand = true;
    private double Gain = 1;
    public bool Active => active;
    public bool FullscreenBlocked => fullscreenBlocked;
    public event Action? Changed;
    public Input(Action<Action> dispatch)
    {
        this.dispatch = dispatch;
        pcBounds = Desktop.PrimaryScreen.Bounds;
        keyboardCallback = Keyboard; mouseCallback = Mouse;
        pump = new InputPump(Install, Cleanup, Raw, PollEdge);
    }
    private void Install()
    {
        rawMemory = Marshal.AllocHGlobal(RawCapacity);
        keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
        if (keyboardHook == 0) throw new System.ComponentModel.Win32Exception();
    }
    public void Configure(Link connection, Rectangle bounds, string side, bool swap, double gain)
    {
        pump.Invoke(() => {
            ReturnCore(); this.connection = connection; pcBounds = bounds; Side = side; SwapControlCommand = swap; Gain = gain;
            Desktop.GetCursorPos(out var point); edge.Reset(point);
            ConfigureForeground();
        });
    }
    public void SetFullscreenProtection(bool enabled) => pump.Invoke(() => {
        if(fullscreenProtection==enabled)return;
        fullscreenProtection = enabled; ConfigureForeground();
    });
    private void ConfigureForeground()
    {
        if (fullscreenProtection && connection?.IsAlive == true) {
            foreground ??= new ForegroundGuard(pump.Post, ForegroundChanged);
            foreground.Start(pcBounds);
        }
        else foreground?.Stop();
        ForegroundChanged();
    }
    private void ForegroundChanged()
    {
        bool blocked = foreground?.Blocked == true;
        bool wasBlocked = fullscreenBlocked;
        fullscreenBlocked = blocked;
        if (wasBlocked && !blocked && !Active && Desktop.GetCursorPos(out var point))
            edge.Reset(point, requireInward: true, notBefore: Environment.TickCount64 + 200);
        UpdatePolling();
        if (wasBlocked != blocked) NotifyChanged();
    }
    private void UpdatePolling() => pump.PollEvery(connection?.IsAlive == true && !fullscreenBlocked ? 16u : 0u);
    public void Disconnect() { pump.Invoke(() => { ReturnCore(); connection = null; foreground?.Stop(); ForegroundChanged(); }); }
    private void PollEdge()
    {
        edgeSamples++;
        var current = connection;
        if (current?.IsAlive != true) { ReturnCore(); foreground?.Stop(); ForegroundChanged(); return; }
        if (FullscreenBlocked) return;
        if (Active || !Desktop.GetCursorPos(out var point)) return;
        bool atEdge = Side == "left" ? point.X <= pcBounds.Left : point.X >= pcBounds.Right - 1;
        bool blocked = physicalKeys.Count != 0 || (atEdge && ButtonHeld());
        if (!edge.Observe(point, pcBounds, Side, blocked, Environment.TickCount64)) return;
        // Close the asynchronous focus-notification race only at an attempted edge
        // crossing. No foreground/window inspection in the ordinary 16 ms loop.
        foreground?.Refresh();
        if (FullscreenBlocked) return;
        epoch = unchecked(epoch + 1); x = Side == "left" ? current.MacWidth - 2 : 1;
        y = Math.Clamp((point.Y - pcBounds.Top) / (double)pcBounds.Height * current.MacHeight, 0, current.MacHeight - 1);
        if (!current.Command(new { type = "enter", epoch, x, y })) return;
        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
        if (mouseHook == 0 || !RegisterRaw()) {
            RemoveMouseHook(); current.Command(new { type = "leave", epoch }); return;
        }
        // Guard the small interval between the sampled crossing and hook install.
        if (physicalKeys.Count != 0 || ButtonHeld()) {
            UnregisterRaw(); RemoveMouseHook(); current.Command(new { type = "leave", epoch }); return;
        }
        var clip = new Native.Rect { Left = pcBounds.Left + pcBounds.Width / 2, Top = pcBounds.Top + pcBounds.Height / 2 };
        clip.Right = clip.Left + 1; clip.Bottom = clip.Top + 1;
        if (!Native.ClipCursor(ref clip)) {
            UnregisterRaw(); RemoveMouseHook(); current.Command(new { type = "leave", epoch }); return;
        }
        active = true;
        timerResolution = Native.timeBeginPeriod(1) == 0;
        Desktop.ShowCursor(false); NotifyChanged();
    }
    private void NotifyChanged()
    {
        try { dispatch(() => Changed?.Invoke()); }
        catch (InvalidOperationException) { /* dashboard is closing; never block the hook */ }
    }
    private unsafe nint Keyboard(int code, nint message, nint pointer)
    {
        if (code < 0) return Native.CallNextHookEx(keyboardHook, code, message, pointer);
        var key = *(Native.KeyboardData*)pointer;
        if ((key.Flags & 0x10) != 0) return Native.CallNextHookEx(keyboardHook, code, message, pointer);
        if (Active && connection?.IsAlive != true) ReturnCore();
        bool down = message == 0x100 || message == 0x104;
        int identity = (int)key.Scan | (((key.Flags & 1) != 0) ? 0x100 : 0);
        if (down) physicalKeys.Add(identity); else physicalKeys.Remove(identity);
        if (Active)
        {
            if (down) swallowedKeys.Add(identity);
            if (down && key.VK == 0x1B && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0 && (Native.GetAsyncKeyState(0x12) & 0x8000) != 0)
                ReturnCore();
            else connection?.Command(new { type = "key", epoch, scan = identity, down, swap = SwapControlCommand });
            if (!down) swallowedKeys.Remove(identity);
            return 1;
        }
        if (swallowedKeys.Contains(identity)) { if (!down) swallowedKeys.Remove(identity); return 1; }
        return Native.CallNextHookEx(keyboardHook, code, message, pointer);
    }
    private unsafe nint Mouse(int code, nint message, nint pointer)
    {
        if (code < 0) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        if (!Active) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        var data = *(Native.MouseData*)pointer;
        if ((data.Flags & 1) != 0) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        var current = connection;
        if (Active && current?.IsAlive != true) ReturnCore();
        if (!Active || current == null) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        int button = (int)message switch { 0x201 or 0x202 => 0, 0x204 or 0x205 => 1, 0x207 or 0x208 => 2, 0x20B or 0x20C => (data.Data >> 16) == 1 ? 3 : 4, _ => -1 };
        if (button >= 0)
            current.Command(new { type = "button", epoch, button, down = message == 0x201 || message == 0x204 || message == 0x207 || message == 0x20B, x, y, positionSequence = current.PositionSequence });
        else if (message == 0x20A || message == 0x20E)
            current.Command(new { type = "scroll", epoch, delta = (short)(data.Data >> 16), horizontal = message == 0x20E, x, y, positionSequence = current.PositionSequence });
        return 1;
    }
    private static bool ButtonHeld() => Native.GetAsyncKeyState(1) < 0 || Native.GetAsyncKeyState(2) < 0 ||
        Native.GetAsyncKeyState(4) < 0 || Native.GetAsyncKeyState(5) < 0 || Native.GetAsyncKeyState(6) < 0;
    private bool RegisterRaw()
    {
        rawDevice[0].Flags = 0x100; rawDevice[0].Target = pump.Handle;
        rawRegistered = Native.RegisterRawInputDevices(rawDevice, 1, (uint)Marshal.SizeOf<Native.RawDevice>());
        return rawRegistered;
    }
    private void UnregisterRaw()
    {
        if (!rawRegistered) return;
        rawDevice[0].Flags = 1; rawDevice[0].Target = 0;
        if (Native.RegisterRawInputDevices(rawDevice, 1, (uint)Marshal.SizeOf<Native.RawDevice>())) rawRegistered = false;
    }
    private void Raw(nint handle)
    {
        if (!Active) return;
        uint size = RawCapacity;
        uint received = Native.GetRawInputData(handle, 0x10000003, rawMemory, ref size, RawHeaderSize);
        if (received == uint.MaxValue || received < RawHeaderSize + 24 || received > RawCapacity || Marshal.ReadInt32(rawMemory) != 0) return;
        int p = (int)RawHeaderSize;
        if ((Marshal.ReadInt16(rawMemory, p) & 1) != 0) return; // relative mouse only
        var current = connection; if (current == null || !current.IsAlive) { ReturnCore(); return; }
        int dx = Marshal.ReadInt32(rawMemory, p + 12), dy = Marshal.ReadInt32(rawMemory, p + 16);
        if (dx == 0 && dy == 0) return;
        x += dx * Gain; y = Math.Clamp(y + dy * Gain, 0, current.MacHeight - 1);
        if ((Side == "left" && x >= current.MacWidth) || (Side == "right" && x < 0)) { ReturnCore(); return; }
        x = Math.Clamp(x, 0, current.MacWidth - 1); current.Move(epoch, x, y);
    }
    private void ReturnCore()
    {
        if (!Active) return;
        connection?.Command(new { type = "leave", epoch }); active = false;
        UnregisterRaw();
        RemoveMouseHook();
        if (timerResolution) { Native.timeEndPeriod(1); timerResolution = false; }
        Native.ClipCursor(0); Desktop.ShowCursor(true);
        var current = connection;
        int pcY = current == null ? pcBounds.Top + pcBounds.Height / 2 : pcBounds.Top + (int)(y / current.MacHeight * pcBounds.Height);
        var point = new Native.Point { X = Side == "left" ? pcBounds.Left + 3 : pcBounds.Right - 4, Y = Math.Clamp(pcY, pcBounds.Top, pcBounds.Bottom - 1) };
        Native.SetCursorPos(point.X, point.Y);
        edge.Reset(point, requireInward: true, notBefore: Environment.TickCount64 + 200); NotifyChanged();
    }
    private void RemoveMouseHook()
    {
        if (mouseHook != 0) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
    }
    internal Task<int> ProbeThread() => pump.Query(() => Environment.CurrentManagedThreadId);
    internal Task<(bool MouseHook, bool Raw, bool Polling)> ProbeState() => pump.Query(() => (mouseHook != 0, rawRegistered, pump.Polling));
    internal Task<(bool Registered, bool Blocked, long Refreshes)> ProbeForeground() => pump.Query(() => (foreground?.Registered == true, fullscreenBlocked, foreground?.Refreshes ?? 0));
    // Diagnostics use unreachable transfer bounds and independently watch a real
    // fixture screen, so they exercise foreground events without capturing input.
    internal void ProbeProtectionScreen(Rectangle bounds) => pump.Invoke(() => {
        foreground!.Start(bounds); ForegroundChanged();
    });
    internal Task<(long Samples, long Refreshes, long CpuTicks)> ProbeSampling() => pump.Query(() => {
        if(!Native.GetThreadTimes(new nint(-2),out _,out _,out long kernel,out long user))throw new System.ComponentModel.Win32Exception();
        return (edgeSamples,foreground?.Refreshes ?? 0,kernel+user);
    });
    internal Task<bool> ProbeMouseHookCycle() => pump.Query(() => {
        if (Active || mouseHook != 0) throw new InvalidOperationException("Mouse capture already active.");
        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
        bool installed = mouseHook != 0; RemoveMouseHook(); return installed && mouseHook == 0;
    });
    private void Cleanup()
    {
        foreground?.Dispose();
        ReturnCore(); UnregisterRaw();
        if (keyboardHook != 0) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
        RemoveMouseHook();
        if (rawMemory != 0) { Marshal.FreeHGlobal(rawMemory); rawMemory = 0; }
    }
    public void Dispose() { pump.Dispose(); }
}
