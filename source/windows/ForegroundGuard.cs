using System.Drawing;
using System.Runtime.InteropServices;

namespace EdgeLink;

// Asynchronous window events, never part of the synchronous mouse hook chain.
// All state and callbacks belong to the input pump, including registration/cleanup.
internal sealed class ForegroundGuard(Action<Action> post, Action changed) : IDisposable
{
    private const uint Foreground = 3, LocationChange = 0x800B;
    private GCHandle callbackRoot;
    private Native.WinEvent? handler;
    private nint foregroundHook, locationHook, watchedWindow;
    private Rectangle bounds;
    private bool pending;
    private int generation;
    public bool Blocked { get; private set; }
    internal bool Registered => foregroundHook != 0;
    internal long Refreshes { get; private set; }

    public void Start(Rectangle screen)
    {
        Stop(); bounds = screen;
        // Root the managed delegate for the full native registration lifetime.
        handler = OnEvent;
        callbackRoot = GCHandle.Alloc(handler);
        foregroundHook = Native.SetWinEventHook(Foreground, Foreground, 0, handler, 0, 0, 0);
        if (!Registered) { Stop(); throw new System.ComponentModel.Win32Exception("Не удалось включить защиту полноэкранных приложений."); }
        Refresh();
    }
    private void OnEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (!Registered || (hook != foregroundHook && hook != locationHook) ||
            (eventId != Foreground && (window != watchedWindow || objectId != 0 || childId != 0)) || pending) return;
        pending = true; int current = generation;
        // Coalesce repeated geometry notifications. Inspect the current foreground,
        // not a potentially stale event HWND. Never enumerate processes or windows.
        post(() => { if (generation != current) return; pending = false; Refresh(); });
    }
    public void Refresh()
    {
        if (!Registered) return;
        Refreshes++; nint window = Native.GetForegroundWindow();
        if(window != watchedWindow) {
            if(locationHook != 0)Native.UnhookWinEvent(locationHook);
            locationHook = 0; watchedWindow = window;
            uint thread = Native.GetWindowThreadProcessId(window, out uint process);
            // Scope geometry events to the foreground window's owner thread.
            // In particular, do not subscribe globally to cursor location events.
            if(thread != 0)locationHook = Native.SetWinEventHook(LocationChange, LocationChange, 0, handler!, process, thread, 0);
        }
        bool blocked = IsFullscreen(watchedWindow, bounds);
        if (blocked == Blocked) return;
        Blocked = blocked; changed();
    }
    internal static bool IsFullscreen(nint window, Rectangle screen)
    {
        if (window == 0 || !Desktop.IsWindowVisible(window) || Desktop.IsIconic(window) || !Desktop.GetClientRect(window, out var client)) return false;
        // Hiding the dashboard can activate Explorer's desktop. Its client area
        // covers the monitor, but it must not pause transfers as if it were a game.
        if (window == Native.GetShellWindow() || window == Native.GetDesktopWindow()) return false;
        var name = new System.Text.StringBuilder(256);
        if (Native.GetClassName(window, name, name.Capacity) > 0 && IsShellClass(name.ToString())) return false;
        var origin = new Native.Point { X = client.Left, Y = client.Top };
        if (!Native.ClientToScreen(window, ref origin)) return false;
        return CoversScreen(new Rectangle(origin.X, origin.Y, client.Right-client.Left, client.Bottom-client.Top), screen);
    }
    internal static bool IsShellClass(string name) => name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    internal static bool CoversScreen(Rectangle client, Rectangle screen) =>
        screen.Width > 0 && screen.Height > 0 && client.Width > 0 && client.Height > 0 &&
        client.Left <= screen.Left + 1 && client.Top <= screen.Top + 1 &&
        client.Right >= screen.Right - 1 && client.Bottom >= screen.Bottom - 1;
    public void Stop()
    {
        generation++; pending = false;
        if (foregroundHook != 0) Native.UnhookWinEvent(foregroundHook);
        if (locationHook != 0) Native.UnhookWinEvent(locationHook);
        foregroundHook = locationHook = watchedWindow = 0;
        if (callbackRoot.IsAllocated) callbackRoot.Free();
        handler = null;
        Blocked = false;
    }
    public void Dispose() => Stop();
}
