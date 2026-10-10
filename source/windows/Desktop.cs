using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace EdgeLink;

internal sealed record DesktopScreen(string DeviceName, Rectangle Bounds, Rectangle WorkingArea, bool Primary);

// Shared Win32 adapters; no WinForms/WPF runtime or hidden Form is used.
internal static class Desktop
{
    internal delegate nint WindowProcedure(nint window,uint message,nuint wParam,nint lParam);
    private delegate bool MonitorCallback(nint monitor,nint dc,nint rect,nint parameter);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct WindowClass {
        public uint Size,Style; public WindowProcedure Procedure; public int ClassExtra,WindowExtra;
        public nint Instance,Icon,Cursor,Background; public string? Menu; public string ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Message {
        public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Native.Point Point; public uint Private;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct MonitorInfo {
        public uint Size; public Native.Rect Monitor,Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device;
    }
    public static DesktopScreen[] Screens {
        get {
            var screens=new List<DesktopScreen>();
            MonitorCallback callback=(monitor,_,_,_)=> {
                var info=new MonitorInfo {Size=(uint)Marshal.SizeOf<MonitorInfo>(),Device=""};
                if(!GetMonitorInfo(monitor,ref info))return false;
                screens.Add(new(info.Device,Rectangle.FromLTRB(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right,info.Monitor.Bottom),
                    Rectangle.FromLTRB(info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom),(info.Flags&1)!=0)); return true;
            };
            if(!EnumDisplayMonitors(0,0,callback,0)||screens.Count==0)throw new Win32Exception();
            return screens.ToArray();
        }
    }
    public static DesktopScreen PrimaryScreen=>Screens.First(s=>s.Primary);
    public static string Asset(string name)=>Path.Combine(AppContext.BaseDirectory,"Assets",name);
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern bool UnregisterClass(string name,nint instance);
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern nint CreateWindowEx(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern nint DefWindowProc(nint window,uint message,nuint wParam,nint lParam);
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern int GetMessage(out Message message,nint window,uint min,uint max);
    [DllImport("user32")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32")] internal static extern void PostQuitMessage(int result);
    [DllImport("user32",SetLastError=true)] internal static extern bool PostMessage(nint window,uint message,nuint wParam,nint lParam);
    [DllImport("user32")] private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorCallback callback,nint parameter);
    [DllImport("user32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32")] internal static extern int ShowCursor(bool show);
    [DllImport("user32")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32")] internal static extern bool IsIconic(nint window);
    [DllImport("user32")] internal static extern bool ShowWindow(nint window,int command);
    [DllImport("user32")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32")] internal static extern bool GetClientRect(nint window,out Native.Rect rect);
    [DllImport("user32")] internal static extern bool GetWindowRect(nint window,out Native.Rect rect);
    [DllImport("user32")] internal static extern bool GetCursorPos(out Native.Point point);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern int MessageBox(nint owner,string text,string title,uint flags);
    [DllImport("shell32",CharSet=CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}

internal sealed class NativeMessageWindow:IDisposable
{
    private readonly string className="EdgeLink-"+Guid.NewGuid().ToString("N");
    private readonly Desktop.WindowProcedure procedure;
    private readonly nint instance=Native.GetModuleHandle(null);
    private readonly Func<uint,nuint,nint,nint?> handler;
    public nint Handle {get;private set;}
    public NativeMessageWindow(Func<uint,nuint,nint,nint?> handler,bool messageOnly=true) {
        this.handler=handler; procedure=Dispatch;
        var definition=new Desktop.WindowClass {Size=(uint)Marshal.SizeOf<Desktop.WindowClass>(),Procedure=procedure,Instance=instance,ClassName=className};
        if(Desktop.RegisterClassEx(ref definition)==0)throw new Win32Exception();
        Handle=Desktop.CreateWindowEx(0,className,"EdgeLink",0,0,0,0,0,messageOnly?new nint(-3):0,0,instance,0);
        if(Handle==0) {Desktop.UnregisterClass(className,instance);throw new Win32Exception();}
    }
    private nint Dispatch(nint window,uint message,nuint wParam,nint lParam)=>handler(message,wParam,lParam)??Desktop.DefWindowProc(window,message,wParam,lParam);
    public void Dispose() {
        if(Handle==0)return;
        Desktop.DestroyWindow(Handle); Handle=0; Desktop.UnregisterClass(className,instance); GC.KeepAlive(procedure);
    }
}
