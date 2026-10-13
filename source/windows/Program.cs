using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using Windows.Storage.Pickers;

namespace EdgeLink;

internal static class Program
{
    internal static readonly string[] Arguments=Environment.GetCommandLineArgs();
    internal static bool Has(string argument)=>Arguments.Contains(argument);
    internal static bool Diagnostic=>Has("--layout-checks")||Has("--tray-checks")||Has("--fullscreen-checks")||Has("--ui-preview")||Has("--background-ui-checks");
    [STAThread] private static void Main() {
        if(Has("--status-json")) {
            int index=Array.IndexOf(Arguments,"--status-json");
            Environment.ExitCode=Arguments.Length>index+1?SingleInstance.SaveStatus(Arguments[index+1]).GetAwaiter().GetResult():2;return;
        }
        if(Has("--background-ui-checks")&&(Has("--input-checks")||Has("--tray-checks")||Has("--fullscreen-checks"))){Environment.ExitCode=2;return;}
        if(Has("--input-checks")){Environment.ExitCode=InputChecks.Run();return;}
        if(!Diagnostic&&!SingleInstance.Acquire())return;
        try {
            Desktop.SetCurrentProcessExplicitAppUserModelID("b0rdik.EdgeLink");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters=> {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _=new App();
            });
        } finally {SingleInstance.Release();}
    }
}

public sealed partial class App:Application
{
    private MainWindow? window;
    public App(){InitializeComponent();}
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        window=new MainWindow(Program.Diagnostic,Program.Has("--tray-checks"));
        if(Program.Has("--background-ui-checks")) {
            window.AppWindow.IsShownInSwitchers=false;
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-16000,-16000));
            window.AppWindow.Show(false);
        } else if(!Program.Has("--start-hidden"))window.Activate();
        window.StartServices();
    }
}

public sealed partial class MainWindow:Window
{
    private readonly TextBox local=new(){Text="192.168.233.1"},remote=new(){Text="192.168.233.2"};
    private readonly PasswordBox pairing=new();
    private readonly ComboBox side=new(),screen=new(),device=new(),jitter=new(),mouseRate=new();
    private readonly SettingsCheckBox swap=new(){Content="Ctrl на ПК → Command на Mac",CheckedValue=true};
    private readonly SettingsCheckBox automatic=new(){Content="Найти Mac и Ethernet автоматически",CheckedValue=true};
    private readonly SettingsCheckBox reconnect=new(){Content="Запоминать Mac и подключаться автоматически",CheckedValue=true};
    private readonly SettingsCheckBox protectFullscreen=new(){Content="Блокировать переходы в полноэкранных приложениях",CheckedValue=true};
    private readonly ActionButton importPairing=Theme.Button("Открыть файл подключения с Mac…"),firewall=Theme.Button("Разрешить соединение по Ethernet…");
    private readonly NumberBox gain=new(){Minimum=.1,Maximum=4,SmallChange=.1,Value=1,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline};
    private readonly ActionButton connect=Theme.Button("Подключить",true),updateButton=Theme.Button("Проверить обновления");
    private readonly TextBlock status=Theme.Label("Не подключено"),stats=Theme.Mono("Нет активного соединения",12,muted:true),updateStatus=Theme.Label("",14,muted:true);
    private readonly CancellationTokenSource updateCancellation=new();
    private ReleaseUpdate? availableUpdate;
    private bool manuallyPaused,updating,installing,connecting,started;
    private bool updateReadyReported;
    private bool legacyBackupCleanupStarted;
    private readonly DispatcherQueueTimer reconnectTimer,refresh;
    private readonly List<(string name,string id)> devices=[];
    private readonly List<string> screenDevices=[];
    private Input? input;
    private Link? link,connectingLink;
    private WasapiOut? output;
    private MMDevice? endpoint;
    private long lastBytes,lastRefresh=Stopwatch.GetTimestamp();
    private string lastError="";
    private readonly string configPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EdgeLink","settings.json");
    private readonly bool previewMode;
    internal bool IsDisposed {get;private set;}
    internal nint Handle {get;}

    internal MainWindow(bool previewMode=false,bool trayCheck=false) {
        this.previewMode=previewMode;Title="EdgeLink "+Updates.CurrentVersion+(previewMode?" — предпросмотр WinUI 3":"");
        Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Desktop.Asset("EdgeLink.ico"));
        side.Items.Add("Mac слева");side.Items.Add("Mac справа");side.SelectedIndex=0;
        var screens=Desktop.Screens;
        foreach(var s in screens){screenDevices.Add(s.DeviceName);screen.Items.Add($"{s.DeviceName} — {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary?" (основной)":"")}");}
        screen.SelectedIndex=Array.FindIndex(screens,s=>s.Primary);
        foreach(var value in new[]{"Авто — от 10 мс","Авто — от 20 мс","Авто — от 40 мс"})jitter.Items.Add(value);jitter.SelectedIndex=0;
        foreach(var value in new[]{"До 1000 Гц — минимальная задержка","До 500 Гц","До 250 Гц — меньше пакетов"})mouseRate.Items.Add(value);mouseRate.SelectedIndex=0;
        devices.Add(("Вывод Windows по умолчанию",""));
        if(!previewMode)using(var enumerator=new MMDeviceEnumerator())foreach(var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){devices.Add((d.FriendlyName,d.ID));d.Dispose();}
        foreach(var d in devices)device.Items.Add(d.name);device.SelectedIndex=0;
        reconnectTimer=DispatcherQueue.CreateTimer();reconnectTimer.Interval=TimeSpan.FromSeconds(10);reconnectTimer.Tick+=async (_,_)=>await TryReconnect();
        refresh=DispatcherQueue.CreateTimer();refresh.Interval=TimeSpan.FromSeconds(1);refresh.Tick+=(_,_)=>RefreshStats();
        BuildDashboard();
        if(!previewMode){LoadSettings();try{pairing.Password=CredentialStore.Load()??"";}catch(Exception e){lastError="Не удалось загрузить сопряжение: "+e.Message;}
            if(HasPairingKey()){setupComplete=true;setupStage=1;}}
        SelectPage(setupComplete?0:1);UpdateWizard();SetFields(true);
        InitializeWindowLifecycle();
        if(!previewMode||trayCheck)InitializeTray();
        connect.Click+=async (_,_)=>{if(!previewMode)await ToggleConnection();};
        importPairing.Click+=async (_,_)=>{if(previewMode)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".edgelink");WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);var file=await picker.PickSingleFileAsync();if(file!=null)await ImportPairing(file.Path,setupComplete);};
        firewall.Click+=async (_,_)=>{if(!previewMode)await ConfigureFirewall();};
        updateButton.Click+=async (_,_)=>{if(previewMode)return;if(availableUpdate is {} update)await OfferUpdate(update);else await CheckUpdates(true);};
        gain.ValueChanged+=(_,_)=>{if(double.IsNaN(gain.Value))gain.Value=1;};
        protectFullscreen.Checked+=(_,_)=>ApplyFullscreenProtection();
        protectFullscreen.Unchecked+=(_,_)=>ApplyFullscreenProtection();
        // Diagnostics need a loaded XAML tree. Ordinary connection/input startup
        // must also work if the window is hidden before Root.Loaded is delivered.
        Root.Loaded+=async (_,_)=>{if(Program.Diagnostic)await Start();};
        Closed+=(_,_)=> {
            IsDisposed=true;updateCancellation.Cancel();reconnectTimer.Stop();refresh.Stop();
            Disconnect();input?.Dispose();DisposeTray();if(!previewMode)SaveSettings();
        };
    }
    internal void BeginInvoke(Action action){if(!IsDisposed)DispatcherQueue.TryEnqueue(()=>{if(!IsDisposed)action();});}
    internal void StartServices(){if(!Program.Diagnostic)_=Start();}
    private bool EnsureInput() {
        try{
            if(input==null){input=new Input(BeginInvoke);input.SetFullscreenProtection(protectFullscreen.CheckedValue);input.Changed+=UpdateStatus;}
            if(!updateReadyReported)updateReadyReported=Updates.ReportReady(Program.Arguments);
            if(!legacyBackupCleanupStarted){legacyBackupCleanupStarted=true;_=Task.Run(()=>Updates.RemoveLegacyBackups());}
            return true;
        }
        catch(Exception e){input?.Dispose();input=null;lastError="Обработка ввода: "+e.Message;status.Text=lastError;return false;}
    }
    private async Task Start() {
        if(started)return;started=true;
        if(Program.Has("--layout-checks")){Environment.ExitCode=await LayoutChecks.Run(this);ExitFromTray();return;}
        if(Program.Has("--tray-checks")){Environment.ExitCode=await TrayChecks.Run(this);ExitFromTray();return;}
        if(Program.Has("--fullscreen-checks")){Environment.ExitCode=await FullscreenChecks.Run(this);ExitFromTray();return;}
        if(Program.Has("--background-ui-checks")){Environment.ExitCode=await BackgroundUiChecks.Run(this);ExitFromTray();return;}
        if(previewMode)return;
        EnsureInput();
        SingleInstance.StartServer(this,updateCancellation.Token);RegisterPairingType();_ = CheckUpdates(false);
        reconnectTimer.Start();
        string[] args=Program.Arguments;
        if(args.Length>=3&&args[1]=="--pair")await ImportPairing(args[2],setupComplete);else await TryReconnect();
    }
    private void RefreshStats() {
        if(!NeedsStats)return;
        long now=Stopwatch.GetTimestamp();double seconds=(now-lastRefresh)/(double)Stopwatch.Frequency;lastRefresh=now;
        if(link==null)return;long bytes=Interlocked.Read(ref link.AudioBytes),packets=Interlocked.Read(ref link.AudioPackets);
        if(Desktop.IsWindowVisible(Handle)&&!Desktop.IsIconic(Handle))stats.Text=$"RTT: {link.RoundTripMs} мс · звук: {(bytes-lastBytes)*8/Math.Max(seconds,.001)/1_000_000:F2} Мбит/с (IPv4)\nБуфер: {link.Audio.BufferedMs:F1} мс · запас: {link.Audio.TargetMs:F1} мс · прерывания: {link.Audio.Underruns} · пакеты: {packets}";
        lastBytes=bytes;
    }
    private void UpdateStatsTimer() {
        bool needed=NeedsStats;
        if(needed&&!refresh.IsRunning){lastBytes=Interlocked.Read(ref link!.AudioBytes);lastRefresh=Stopwatch.GetTimestamp();RefreshStats();refresh.Start();}
        else if(!needed&&refresh.IsRunning)refresh.Stop();
    }
    private bool NeedsStats=>!IsDisposed&&link?.IsAlive==true&&selectedPage==0&&connectionDiagnostics.IsExpanded&&Desktop.IsWindowVisible(Handle)&&!Desktop.IsIconic(Handle);
    internal async Task<string> GetStatusJson() {
        var state=input==null?((bool MouseHook,bool Raw,bool Polling)?)null:await input.ProbeState();
        return System.Text.Json.JsonSerializer.Serialize(new {
            Version=Updates.CurrentVersion,Connected=link?.IsAlive==true,InputReady=input!=null,
            InputOwner=input?.Active==true?"Mac":"PC",FullscreenBlocked=input?.FullscreenBlocked==true,
            WindowVisible=Desktop.IsWindowVisible(Handle),Minimized=Desktop.IsIconic(Handle),Page=selectedPage,
            DiagnosticsExpanded=connectionDiagnostics.IsExpanded,StatisticsTimerRunning=refresh.IsRunning,
            MouseHook=state?.MouseHook??false,RawInput=state?.Raw??false,EdgePolling=state?.Polling??false,
            RoundTripMs=link?.RoundTripMs,AudioPackets=link==null?0:Interlocked.Read(ref link.AudioPackets),
            AudioBytes=link==null?0:Interlocked.Read(ref link.AudioBytes),
            AudioBufferedMs=link?.Audio.BufferedMs,AudioTargetMs=link?.Audio.TargetMs,AudioUnderruns=link?.Audio.Underruns
        });
    }
    private async Task<bool> ConfirmUpdate(ReleaseUpdate update) {
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=$"Установить EdgeLink {update.Version}?",Content="Приложение скачает и проверит новую версию, заменит установленную и перезапустится. Старые версии и резервные копии не сохраняются.",PrimaryButtonText="Установить",CloseButtonText="Отмена",DefaultButton=ContentDialogButton.Close,RequestedTheme=Root.ActualTheme};
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
}
