using System.Runtime.InteropServices;

namespace EdgeLink;

internal sealed class Input : IDisposable
{
    private readonly Control owner;
    private readonly InputPump pump;
    private Link? connection;
    private readonly Native.Hook keyboardCallback, mouseCallback;
    private nint keyboardHook, mouseHook;
    private readonly HashSet<int> physicalKeys = [];
    private readonly HashSet<int> swallowedKeys = [];
    private Rectangle pcBounds;
    private double x, y;
    private uint epoch;
    private long rearmAt;
    private bool inwardRequired;
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
    public event Action? Changed;
    public Input(Control owner)
    {
        this.owner = owner;
        pcBounds = Screen.PrimaryScreen!.Bounds;
        keyboardCallback = Keyboard; mouseCallback = Mouse;
        pump = new InputPump(Install, Cleanup, Raw);
    }
    private void Install()
    {
        rawMemory = Marshal.AllocHGlobal(RawCapacity);
        keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
        if (keyboardHook == 0 || mouseHook == 0) throw new System.ComponentModel.Win32Exception();
    }
    public void Configure(Link connection, Rectangle bounds, string side, bool swap, double gain)
    {
        pump.Invoke(() => { ReturnCore(); this.connection = connection; pcBounds = bounds; Side = side; SwapControlCommand = swap; Gain = gain; });
    }
    public void Disconnect() { pump.Invoke(() => { ReturnCore(); connection = null; }); }
    private void NotifyChanged()
    {
        try { if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(() => Changed?.Invoke()); }
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
        if (!Active && connection == null) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        var data = *(Native.MouseData*)pointer;
        if ((data.Flags & 1) != 0) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        var current = connection;
        if (Active && current?.IsAlive != true) ReturnCore();
        if (!Active && current != null && message == 0x200)
        {
            bool inward = Side == "left" ? data.Point.X > pcBounds.Left + 5 : data.Point.X < pcBounds.Right - 6;
            if (inward) inwardRequired = false;
            bool atEdge = Side == "left" ? data.Point.X <= pcBounds.Left : data.Point.X >= pcBounds.Right - 1;
            bool inHeight = data.Point.Y >= pcBounds.Top && data.Point.Y < pcBounds.Bottom;
            // Button state syscalls are only needed for an actual edge crossing.
            if (atEdge && inHeight && physicalKeys.Count == 0 && !inwardRequired && Environment.TickCount64 >= rearmAt && !ButtonHeld())
            {
                epoch = unchecked(epoch + 1); x = Side == "left" ? current.MacWidth - 2 : 1;
                y = Math.Clamp((data.Point.Y - pcBounds.Top) / (double)pcBounds.Height * current.MacHeight, 0, current.MacHeight - 1);
                if (!current.Command(new { type = "enter", epoch, x, y })) return Native.CallNextHookEx(mouseHook, code, message, pointer);
                if (!RegisterRaw()) { current.Command(new { type = "leave", epoch }); return Native.CallNextHookEx(mouseHook, code, message, pointer); }
                active = true;
                var clip = new Native.Rect { Left = pcBounds.Left + pcBounds.Width / 2, Top = pcBounds.Top + pcBounds.Height / 2 };
                clip.Right = clip.Left + 1; clip.Bottom = clip.Top + 1;
                if (!Native.ClipCursor(ref clip)) { ReturnCore(); return Native.CallNextHookEx(mouseHook, code, message, pointer); }
                timerResolution = Native.timeBeginPeriod(1) == 0;
                Cursor.Hide(); NotifyChanged();
                return 1;
            }
        }
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
        if (timerResolution) { Native.timeEndPeriod(1); timerResolution = false; }
        Native.ClipCursor(0); Cursor.Show();
        var current = connection;
        int pcY = current == null ? pcBounds.Top + pcBounds.Height / 2 : pcBounds.Top + (int)(y / current.MacHeight * pcBounds.Height);
        Native.SetCursorPos(Side == "left" ? pcBounds.Left + 3 : pcBounds.Right - 4, Math.Clamp(pcY, pcBounds.Top, pcBounds.Bottom - 1));
        rearmAt = Environment.TickCount64 + 200; inwardRequired = true; NotifyChanged();
    }
    internal Task<int> ProbeThread() => pump.Query(() => Environment.CurrentManagedThreadId);
    private void Cleanup()
    {
        ReturnCore(); UnregisterRaw();
        if (keyboardHook != 0) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
        if (mouseHook != 0) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
        if (rawMemory != 0) { Marshal.FreeHGlobal(rawMemory); rawMemory = 0; }
    }
    public void Dispose() { pump.Dispose(); }
}
