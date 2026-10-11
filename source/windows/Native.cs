using System.Runtime.InteropServices;

namespace EdgeLink;

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
    [DllImport("user32", SetLastError = true)] public static extern uint GetRegisteredRawInputDevices(nint devices, ref uint count, uint size);
    [DllImport("user32")] public static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32")] public static extern bool ClipCursor(ref Rect rect);
    [DllImport("user32")] public static extern bool ClipCursor(nint rect);
    [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32", SetLastError = true)] public static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32")] public static extern bool KillTimer(nint window, nuint id);
    [DllImport("winmm")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm")] public static extern uint timeEndPeriod(uint period);
}
