using System.Runtime.InteropServices;

namespace EdgeLink;

internal sealed class Input : IDisposable
{
    private readonly Control owner;
    private readonly Func<Link?> link;
    private readonly Native.Hook keyboardCallback, mouseCallback;
    private nint keyboardHook, mouseHook;
    private readonly HashSet<int> physicalKeys = [];
    private readonly HashSet<int> swallowedKeys = [];
    private Rectangle pcBounds;
    private double x, y;
    private uint epoch;
    private long rearmAt;
    private bool inwardRequired;
    public bool Active { get; private set; }
    public string Side { get; set; } = "left";
    public bool SwapControlCommand { get; set; } = true;
    public double Gain { get; set; } = 1;
    public event Action? Changed;
    public Input(Control owner, Func<Link?> link)
    {
        this.owner = owner; this.link = link;
        pcBounds = Screen.PrimaryScreen!.Bounds;
        keyboardCallback = Keyboard; mouseCallback = Mouse;
        keyboardHook = Native.SetWindowsHookEx(13, keyboardCallback, Native.GetModuleHandle(null), 0);
        mouseHook = Native.SetWindowsHookEx(14, mouseCallback, Native.GetModuleHandle(null), 0);
        if (keyboardHook == 0 || mouseHook == 0) { Dispose(); throw new System.ComponentModel.Win32Exception(); }
        var device = new Native.RawDevice { UsagePage = 1, Usage = 2, Flags = 0x100, Target = owner.Handle };
        if (!Native.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<Native.RawDevice>()))
        { Dispose(); throw new System.ComponentModel.Win32Exception(); }
    }
    public void SetScreen(Screen screen) { Return(); pcBounds = screen.Bounds; }
    private nint Keyboard(int code, nint message, nint pointer)
    {
        if (code < 0) return Native.CallNextHookEx(keyboardHook, code, message, pointer);
        var key = Marshal.PtrToStructure<Native.KeyboardData>(pointer);
        if ((key.Flags & 0x10) != 0) return Native.CallNextHookEx(keyboardHook, code, message, pointer);
        bool down = message == 0x100 || message == 0x104;
        int identity = (int)key.Scan | (((key.Flags & 1) != 0) ? 0x100 : 0);
        if (down) physicalKeys.Add(identity); else physicalKeys.Remove(identity);
        if (Active)
        {
            if (down) swallowedKeys.Add(identity);
            if (down && key.VK == 0x1B && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0 && (Native.GetAsyncKeyState(0x12) & 0x8000) != 0)
                Return();
            else link()?.Command(new { type = "key", epoch, scan = identity, down, swap = SwapControlCommand });
            if (!down) swallowedKeys.Remove(identity);
            return 1;
        }
        if (swallowedKeys.Contains(identity)) { if (!down) swallowedKeys.Remove(identity); return 1; }
        return Native.CallNextHookEx(keyboardHook, code, message, pointer);
    }
    private nint Mouse(int code, nint message, nint pointer)
    {
        if (code < 0) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        var data = Marshal.PtrToStructure<Native.MouseData>(pointer);
        if ((data.Flags & 1) != 0) return Native.CallNextHookEx(mouseHook, code, message, pointer);
        var current = link();
        if (!Active && current != null && message == 0x200)
        {
            bool inward = Side == "left" ? data.Point.X > pcBounds.Left + 5 : data.Point.X < pcBounds.Right - 6;
            if (inward) inwardRequired = false;
            bool atEdge = Side == "left" ? data.Point.X <= pcBounds.Left : data.Point.X >= pcBounds.Right - 1;
            bool inHeight = data.Point.Y >= pcBounds.Top && data.Point.Y < pcBounds.Bottom;
            bool buttonHeld = new[] { 1, 2, 4, 5, 6 }.Any(vk => (Native.GetAsyncKeyState(vk) & 0x8000) != 0);
            if (atEdge && inHeight && !buttonHeld && physicalKeys.Count == 0 && !inwardRequired && Environment.TickCount64 >= rearmAt)
            {
                epoch = unchecked(epoch + 1); x = Side == "left" ? current.MacWidth - 2 : 1;
                y = Math.Clamp((data.Point.Y - pcBounds.Top) / (double)pcBounds.Height * current.MacHeight, 0, current.MacHeight - 1);
                if (!current.Command(new { type = "enter", epoch, x, y })) return 0;
                Active = true;
                var clip = new Native.Rect { Left = pcBounds.Left + pcBounds.Width / 2, Top = pcBounds.Top + pcBounds.Height / 2 };
                clip.Right = clip.Left + 1; clip.Bottom = clip.Top + 1;
                if (!Native.ClipCursor(ref clip)) { Return(); return 0; }
                Cursor.Hide(); Changed?.Invoke();
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
    public void Raw(nint handle)
    {
        uint size = 0, header = (uint)Marshal.SizeOf<Native.RawHeader>();
        Native.GetRawInputData(handle, 0x10000003, 0, ref size, header);
        if (size < header + 24 || size > 4096) return;
        nint memory = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.GetRawInputData(handle, 0x10000003, memory, ref size, header) != size || Marshal.ReadInt32(memory) != 0 || !Active) return;
            int p = (int)header;
            if ((Marshal.ReadInt16(memory, p) & 1) != 0) return; // relative mouse only
            var current = link(); if (current == null) { Return(); return; }
            int dx = Marshal.ReadInt32(memory, p + 12), dy = Marshal.ReadInt32(memory, p + 16);
            if (dx == 0 && dy == 0) return;
            x += dx * Gain; y = Math.Clamp(y + dy * Gain, 0, current.MacHeight - 1);
            if ((Side == "left" && x >= current.MacWidth) || (Side == "right" && x < 0)) { Return(); return; }
            x = Math.Clamp(x, 0, current.MacWidth - 1); current.Move(epoch, x, y);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Return()
    {
        if (!Active) return;
        link()?.Command(new { type = "leave", epoch }); Active = false;
        Native.ClipCursor(0); Cursor.Show();
        var current = link();
        int pcY = current == null ? pcBounds.Top + pcBounds.Height / 2 : pcBounds.Top + (int)(y / current.MacHeight * pcBounds.Height);
        Native.SetCursorPos(Side == "left" ? pcBounds.Left + 3 : pcBounds.Right - 4, Math.Clamp(pcY, pcBounds.Top, pcBounds.Bottom - 1));
        rearmAt = Environment.TickCount64 + 200; inwardRequired = true; Changed?.Invoke();
    }
    public void Dispose()
    {
        Return();
        if (keyboardHook != 0) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = 0; }
        if (mouseHook != 0) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
        var remove = new Native.RawDevice { UsagePage = 1, Usage = 2, Flags = 1, Target = 0 };
        Native.RegisterRawInputDevices([remove], 1, (uint)Marshal.SizeOf<Native.RawDevice>());
    }
}

internal static class Native
{
    public delegate nint Hook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardData { public uint VK, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)] public struct RawHeader { public uint Type, Size; public nint Device, WParam; }
    [DllImport("user32", SetLastError = true)] public static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("user32")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32", SetLastError = true)] public static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32")] public static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32")] public static extern bool ClipCursor(ref Rect rect);
    [DllImport("user32")] public static extern bool ClipCursor(nint rect);
    [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("winmm")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm")] public static extern uint timeEndPeriod(uint period);
}
