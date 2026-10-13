using System.ComponentModel;
using System.Runtime.InteropServices;

namespace EdgeLink;

// Shell notification area is a Win32 service; it does not require a WinForms host.
internal sealed class NativeTray:IDisposable
{
    private const uint Callback=0x8003;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct IconData {
        public uint Size;public nint Window;public uint Id,Flags,CallbackMessage;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public nint BalloonIcon;
    }
    private readonly NativeMessageWindow window;
    private readonly MainWindow owner;
    private readonly uint taskbarCreated=RegisterWindowMessage("TaskbarCreated");
    private readonly nint icon;
    private IconData data;
    internal bool Registered {get;private set;}
    internal NativeTray(MainWindow owner) {
        this.owner=owner;window=new NativeMessageWindow(Dispatch,false);
        icon=LoadImage(0,Desktop.Asset("EdgeLink.ico"),1,32,32,0x10);
        if(icon==0){window.Dispose();throw new Win32Exception();}
        data=new IconData {Size=(uint)Marshal.SizeOf<IconData>(),Window=window.Handle,Id=1,Flags=1|2|4,CallbackMessage=Callback,Icon=icon,Tip="EdgeLink — не подключено",Info="",InfoTitle=""};
        if(!Add()){Dispose();throw new Win32Exception();}
    }
    private bool Add(){Registered=ShellNotifyIcon(0,ref data);if(Registered){data.Version=4;ShellNotifyIcon(4,ref data);}return Registered;}
    internal void Update(string tip){if(!Registered)return;data.Tip=tip;ShellNotifyIcon(1,ref data);}
    private nint? Dispatch(uint message,nuint wParam,nint lParam) {
        if(message==taskbarCreated){Add();return 0;}
        if(message==0x11){owner.ShuttingDown=true;return 1;}
        if(message==0x16){if(wParam!=0)owner.BeginInvoke(owner.ExitFromTray);else owner.ShuttingDown=false;return 0;}
        if(message!=Callback)return null;
        uint notification=(uint)((long)lParam&0xffff);
        if(notification==0x203||notification==0x401)owner.BeginInvoke(owner.RestoreFromTray);
        else if(notification==0x7b) {
            nint menu=CreatePopupMenu();if(menu==0)return 0;
            try {
                AppendMenu(menu,0,1,"Открыть EdgeLink");AppendMenu(menu,0x800,0,"");
                AppendMenu(menu,owner.CanToggleConnection?0u:1u,2,owner.IsConnected?"Отключить":"Подключить");
                AppendMenu(menu,0x800,0,"");AppendMenu(menu,0,3,"Выйти из EdgeLink");
                Desktop.GetCursorPos(out var point);Desktop.SetForegroundWindow(window.Handle);
                int command=TrackPopupMenu(menu,0x100|0x80|2,point.X,point.Y,0,window.Handle,0);
                Desktop.PostMessage(window.Handle,0,0,0);owner.BeginInvoke(()=>Route(command));
            }finally{DestroyMenu(menu);}
        }
        return 0;
    }
    internal void Route(int command){if(command==1)owner.RestoreFromTray();else if(command==2&&owner.CanToggleConnection)owner.ToggleFromTray();else if(command==3)owner.ExitFromTray();}
    public void Dispose(){if(Registered){ShellNotifyIcon(2,ref data);Registered=false;}window.Dispose();if(icon!=0)DestroyIcon(icon);}
    [DllImport("shell32",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode)]private static extern bool ShellNotifyIcon(uint operation,ref IconData data);
    [DllImport("user32",CharSet=CharSet.Unicode)]private static extern nint LoadImage(nint instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32")]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32")]private static extern nint CreatePopupMenu();
    [DllImport("user32",CharSet=CharSet.Unicode)]private static extern bool AppendMenu(nint menu,uint flags,nuint id,string text);
    [DllImport("user32")]private static extern int TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint window,nint rect);
    [DllImport("user32")]private static extern bool DestroyMenu(nint menu);
}

public sealed partial class MainWindow
{
    private NativeTray? trayIcon;
    private bool exitRequested;
    internal bool ShuttingDown;
    internal bool CanToggleConnection=>!previewMode&&!connecting&&!installing&&input!=null;
    internal bool IsConnected=>link!=null;
    internal bool ShouldHideOnClose=>trayIcon!=null&&!exitRequested&&!installing&&!ShuttingDown;
    private delegate nint SubclassProcedure(nint window,uint message,nuint wParam,nint lParam,nuint id,nuint reference);
    private SubclassProcedure? subclass;
    [StructLayout(LayoutKind.Sequential)]private struct MinMaxInfo {public Native.Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    private void InitializeWindowLifecycle() {
        subclass=WindowMessage;if(!SetWindowSubclass(Handle,subclass,1,0))throw new Win32Exception();
        AppWindow.Closing+=(_,e)=>{if(ShouldHideOnClose){e.Cancel=true;Desktop.ShowWindow(Handle,0);UpdateStatsTimer();}};
        // Keep DWM's native caption: WinUI's color-customized caption retains
        // close hover when Closing is cancelled and the window is hidden.
        AppWindow.TitleBar.ResetToDefault();
        Root.ActualThemeChanged+=(_,_)=>ApplyCaptionTheme();ApplyCaptionTheme();
        double scale=Desktop.GetDpiForWindow(Handle)/96.0;var work=Desktop.PrimaryScreen.WorkingArea;
        int width=Math.Min((int)(800*scale),work.Width),height=Math.Min((int)(740*scale),work.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.Left+(work.Width-width)/2,work.Top+(work.Height-height)/2,width,height));
    }
    private void ApplyCaptionTheme() {
        int dark=Root.ActualTheme==Microsoft.UI.Xaml.ElementTheme.Dark?1:0,background=dark==1?0x202020:0xf3f3f3,text=dark==1?0xf5f5f5:0x202020;
        DwmSetWindowAttribute(Handle,20,ref dark,sizeof(int));
        DwmSetWindowAttribute(Handle,35,ref background,sizeof(int));
        DwmSetWindowAttribute(Handle,36,ref text,sizeof(int));
    }
    private nint WindowMessage(nint window,uint message,nuint wParam,nint lParam,nuint id,nuint reference) {
        if(message==0x5)UpdateStatsTimer(); // WM_SIZE includes minimize and restore.
        if(message==0x11)ShuttingDown=true;
        if(message==0x16){if(wParam!=0)BeginInvoke(ExitFromTray);else ShuttingDown=false;}
        if(message==0x24) {
            var info=Marshal.PtrToStructure<MinMaxInfo>(lParam);double scale=Desktop.GetDpiForWindow(window)/96.0;
            info.MinTrackSize=new Native.Point{X=(int)(680*scale),Y=(int)(560*scale)};Marshal.StructureToPtr(info,lParam,false);return 0;
        }
        if(message==0x82)RemoveWindowSubclass(window,subclass!,1);
        return DefSubclassProc(window,message,wParam,lParam);
    }
    private void InitializeTray(){trayIcon=new NativeTray(this);}
    private void DisposeTray(){trayIcon?.Dispose();trayIcon=null;}
    private async Task ToggleConnection(){if(!CanToggleConnection)return;if(link==null){manuallyPaused=false;await Connect();}else{manuallyPaused=true;Disconnect();}}
    internal async void ToggleFromTray()=>await ToggleConnection();
    internal void RestoreFromTray(){if(IsDisposed)return;Desktop.ShowWindow(Handle,Desktop.IsIconic(Handle)?9:5);Activate();Desktop.SetForegroundWindow(Handle);RefreshStats();UpdateStatsTimer();}
    internal void ExitFromTray(){if(IsDisposed)return;exitRequested=true;Close();}
    private void UpdateTrayStatus()=>trayIcon?.Update(link==null?"EdgeLink — не подключено":input?.Active==true?"EdgeLink — управление Mac · звук подключён":"EdgeLink — управление ПК · звук подключён");
    [DllImport("comctl32")]private static extern bool SetWindowSubclass(nint window,SubclassProcedure callback,nuint id,nuint reference);
    [DllImport("comctl32")]private static extern bool RemoveWindowSubclass(nint window,SubclassProcedure callback,nuint id);
    [DllImport("comctl32")]private static extern nint DefSubclassProc(nint window,uint message,nuint wParam,nint lParam);
    [DllImport("dwmapi")]private static extern int DwmSetWindowAttribute(nint window,uint attribute,ref int value,int size);
}
