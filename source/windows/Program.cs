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
    internal static bool Diagnostic=>Has("--layout-checks")||Has("--tray-checks")||Has("--ui-preview");
    [STAThread] private static void Main() {
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
        window.Activate();
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
    private readonly ActionButton importPairing=Theme.Button("Открыть файл подключения с Mac…"),firewall=Theme.Button("Разрешить соединение по Ethernet…");
    private readonly NumberBox gain=new(){Minimum=.1,Maximum=4,SmallChange=.1,Value=1,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Inline};
    private readonly ActionButton connect=Theme.Button("Подключить",true),updateButton=Theme.Button("Проверить обновления");
    private readonly TextBlock status=Theme.Label("Не подключено"),stats=Theme.Label("Нет активного соединения",14,muted:true),updateStatus=Theme.Label("",14,muted:true);
    private readonly CancellationTokenSource updateCancellation=new();
    private ReleaseUpdate? availableUpdate;
    private bool manuallyPaused,updating,installing,connecting,started;
    private readonly DispatcherQueueTimer reconnectTimer,refresh;
    private readonly List<(string name,string id)> devices=[];
    private Input? input;
    private Link? link,connectingLink;
    private WasapiOut? output;
    private MMDevice? endpoint;
    private long lastBytes,lastRefresh=Stopwatch.GetTimestamp();
    private string lastError="";
    private readonly string configPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EdgeLink","settings.json");
    private readonly bool previewMode;
    internal bool IsDisposed {get;private set;}
    private bool Disposing=>IsDisposed;
    internal nint Handle {get;}

    internal MainWindow(bool previewMode=false,bool trayCheck=false) {
        this.previewMode=previewMode;Title="EdgeLink "+Updates.CurrentVersion+(previewMode?" — предпросмотр WinUI 3":"");
        Handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Desktop.Asset("EdgeLink.ico"));
        side.Items.Add("Mac слева");side.Items.Add("Mac справа");side.SelectedIndex=0;
        var screens=Desktop.Screens;
        foreach(var s in screens)screen.Items.Add($"{s.DeviceName} — {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary?" (основной)":"")}");
        screen.SelectedIndex=Array.FindIndex(screens,s=>s.Primary);
        foreach(var value in new[]{"Авто — от 10 мс","Авто — от 20 мс","Авто — от 40 мс"})jitter.Items.Add(value);jitter.SelectedIndex=0;
        foreach(var value in new[]{"До 1000 Гц — минимальная задержка","До 500 Гц","До 250 Гц — меньше пакетов"})mouseRate.Items.Add(value);mouseRate.SelectedIndex=0;
        devices.Add(("Вывод Windows по умолчанию",""));
        if(!previewMode)using(var enumerator=new MMDeviceEnumerator())foreach(var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){devices.Add((d.FriendlyName,d.ID));d.Dispose();}
        foreach(var d in devices)device.Items.Add(d.name);device.SelectedIndex=0;
        BuildDashboard();
        reconnectTimer=DispatcherQueue.CreateTimer();reconnectTimer.Interval=TimeSpan.FromSeconds(10);reconnectTimer.Tick+=async (_,_)=>await TryReconnect();
        refresh=DispatcherQueue.CreateTimer();refresh.Interval=TimeSpan.FromSeconds(1);refresh.Tick+=(_,_)=>RefreshStats();
        if(!previewMode){LoadSettings();try{pairing.Password=CredentialStore.Load()??"";}catch(Exception e){status.Text="Не удалось загрузить сопряжение: "+e.Message;}
            if(HasPairingKey()){setupComplete=true;setupStage=1;}}
        SelectPage(setupComplete?0:1);UpdateWizard();SetFields(true);
        InitializeWindowLifecycle();
        if(!previewMode||trayCheck)InitializeTray();
        connect.Click+=async (_,_)=>{if(!previewMode)await ToggleConnection();};
        importPairing.Click+=async (_,_)=>{if(previewMode)return;var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".edgelink");WinRT.Interop.InitializeWithWindow.Initialize(picker,Handle);var file=await picker.PickSingleFileAsync();if(file!=null)await ImportPairing(file.Path,setupComplete);};
        firewall.Click+=async (_,_)=>{if(!previewMode)await ConfigureFirewall();};
        updateButton.Click+=async (_,_)=>{if(previewMode)return;if(availableUpdate is {} update)await OfferUpdate(update);else await CheckUpdates(true);};
        gain.ValueChanged+=(_,_)=>{if(double.IsNaN(gain.Value))gain.Value=1;};
        Root.Loaded+=async (_,_)=>await Start();
        Closed+=(_,_)=> {
            IsDisposed=true;updateCancellation.Cancel();reconnectTimer.Stop();refresh.Stop();
            Disconnect();input?.Dispose();DisposeTray();if(!previewMode)SaveSettings();
        };
    }
    internal void BeginInvoke(Action action){if(!IsDisposed)DispatcherQueue.TryEnqueue(()=>{if(!IsDisposed)action();});}
    private async Task Start() {
        if(started)return;started=true;
        if(Program.Has("--layout-checks")){Environment.ExitCode=await LayoutChecks.Run(this);ExitFromTray();return;}
        if(Program.Has("--tray-checks")){Environment.ExitCode=await TrayChecks.Run(this);ExitFromTray();return;}
        if(previewMode)return;
        try{input=new Input(BeginInvoke);input.Changed+=UpdateStatus;}catch(Exception e){status.Text=e.Message;connect.IsEnabled=false;}
        SingleInstance.StartServer(this,updateCancellation.Token);RegisterPairingType();_ = CheckUpdates(false);
        reconnectTimer.Start();refresh.Start();
        string[] args=Program.Arguments;
        if(args.Length>=3&&args[1]=="--pair")await ImportPairing(args[2],setupComplete);else await TryReconnect();
    }
    private void RefreshStats() {
        long now=Stopwatch.GetTimestamp();double seconds=(now-lastRefresh)/(double)Stopwatch.Frequency;lastRefresh=now;
        if(link==null)return;long bytes=Interlocked.Read(ref link.AudioBytes),packets=Interlocked.Read(ref link.AudioPackets);
        if(Desktop.IsWindowVisible(Handle)&&!Desktop.IsIconic(Handle))stats.Text=$"RTT: {link.RoundTripMs} мс · звук: {(bytes-lastBytes)*8/Math.Max(seconds,.001)/1_000_000:F2} Мбит/с (IPv4)\nБуфер: {link.Audio.BufferedMs:F1} мс · запас: {link.Audio.TargetMs:F1} мс · прерывания: {link.Audio.Underruns} · пакеты: {packets}";
        lastBytes=bytes;
    }
    private async Task<bool> ConfirmUpdate(ReleaseUpdate update) {
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=$"Установить EdgeLink {update.Version}?",Content="Приложение скачает обновление из b0rdik/EdgeLink, проверит SHA-256 и перезапустится. Предыдущая версия сохранится рядом с приложением.",PrimaryButtonText="Установить",CloseButtonText="Отмена",DefaultButton=ContentDialogButton.Close,RequestedTheme=ElementTheme.Dark};
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
}
