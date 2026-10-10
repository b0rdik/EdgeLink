using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using Microsoft.Win32;

namespace EdgeLink;

internal static class Program
{
    [STAThread] static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "EdgeLink");
        SingleInstance.Run();
    }
}

internal sealed partial class MainWindow : Form
{
    private readonly TextBox local = new() { Text = "192.168.233.1", Width = 230 };
    private readonly TextBox remote = new() { Text = "192.168.233.2", Width = 230 };
    private readonly TextBox pairing = new() { Width = 430, UseSystemPasswordChar = true };
    private readonly ComboBox side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox screen = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    private readonly ComboBox device = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    private readonly ComboBox jitter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly ComboBox mouseRate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    private readonly CheckBox swap = new() { Text = "Ctrl на ПК → Command на Mac", Checked = true, AutoSize = true };
    private readonly CheckBox automatic = new() { Text = "Найти Mac и Ethernet автоматически", Checked = true, AutoSize = true };
    private readonly CheckBox reconnect = new() { Text = "Запоминать Mac и подключаться автоматически", Checked = true, AutoSize = true };
    private readonly Button importPairing = new() { Text = "Открыть файл подключения с Mac…", AutoSize = true };
    private readonly Button firewall = new() { Text = "Разрешить соединение по Ethernet…", AutoSize = true };
    private readonly System.Windows.Forms.Timer reconnectTimer = new() { Interval = 10000 };
    private bool manuallyPaused;
    private readonly NumericUpDown gain = new() { Minimum = 0.1m, Maximum = 4, DecimalPlaces = 2, Increment = 0.1m, Value = 1, Width = 100 };
    private readonly Button connect = new() { Text = "Подключить", AutoSize = true };
    private readonly Label status = new() { Text = "Не подключено", AutoSize = true, MaximumSize = new Size(610, 0) };
    private readonly Label stats = new() { AutoSize = true, MaximumSize = new Size(610, 0) };
    private readonly Button updateButton = new() { Text = "Проверить обновления", AutoSize = true };
    private readonly Label updateStatus = new() { AutoSize = true, MaximumSize = new Size(430, 0) };
    private readonly CancellationTokenSource updateCancellation = new();
    private ReleaseUpdate? availableUpdate;
    private bool updating, installing;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    private readonly List<(string name, string id)> devices = [];
    private Input? input;
    private Link? link;
    private Link? connectingLink;
    private WasapiOut? output;
    private MMDevice? endpoint;
    private long lastBytes;
    private string lastError = "";
    private bool connecting, timerResolution;
    private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgeLink", "settings.json");

    public MainWindow()
    {
        Text = $"EdgeLink {Updates.CurrentVersion}"; StartPosition = FormStartPosition.CenterScreen;
        using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("EdgeLink.App.ico")) {
            if (stream != null) { using var original = new Icon(stream, 256, 256); Icon = (Icon)original.Clone(); }
        }
        side.Items.AddRange(["Mac слева", "Mac справа"]); side.SelectedIndex = 0;
        foreach (var s in Screen.AllScreens) screen.Items.Add($"{s.DeviceName} — {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? " (основной)" : "")}");
        screen.SelectedIndex = Array.FindIndex(Screen.AllScreens, s => s.Primary);
        jitter.Items.AddRange(["10 мс — минимальный буфер", "20 мс", "40 мс — устойчивее к сбоям"]); jitter.SelectedIndex = 0;
        mouseRate.Items.AddRange(["До 1000 Гц — минимальная задержка", "До 500 Гц", "До 250 Гц — меньше пакетов"]); mouseRate.SelectedIndex = 0;
        devices.Add(("Вывод Windows по умолчанию", ""));
        using (var enumerator = new MMDeviceEnumerator())
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            { devices.Add((d.FriendlyName, d.ID)); d.Dispose(); }
        foreach (var d in devices) device.Items.Add(d.name); device.SelectedIndex = 0;
        BuildDashboard();
        LoadSettings();
        try { pairing.Text = CredentialStore.Load() ?? ""; } catch (Exception e) { status.Text = "Не удалось загрузить сопряжение: " + e.Message; }
        if (HasPairingKey()) { setupComplete = true; setupStage = 1; }
        SelectPage(setupComplete ? 0 : 1); UpdateWizard(); SetFields(true);
        connect.Click += async (_, _) => { if (link == null) { manuallyPaused = false; await Connect(); } else { manuallyPaused = true; Disconnect(); } };
        importPairing.Click += async (_, _) => { using var dialog = new OpenFileDialog { Filter = "Файл EdgeLink|*.edgelink|Все файлы|*.*" }; if (dialog.ShowDialog(this) == DialogResult.OK) await ImportPairing(dialog.FileName, setupComplete); };
        firewall.Click += async (_, _) => await ConfigureFirewall();
        automatic.CheckedChanged += (_, _) => { local.Enabled = remote.Enabled = !automatic.Checked && link == null; };
        updateButton.Click += async (_, _) => { if (availableUpdate is { } update) await OfferUpdate(update); else await CheckUpdates(true); };
        Shown += async (_, _) => {
            try { input = new Input(this, () => link); input.Changed += UpdateStatus; } catch (Exception e) { status.Text = e.Message; connect.Enabled = false; }
            RegisterPairingType(); _ = CheckUpdates(false);
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length >= 3 && args[1] == "--pair") await ImportPairing(args[2], setupComplete); else await TryReconnect();
        };
        reconnectTimer.Tick += async (_, _) => await TryReconnect(); reconnectTimer.Start();
        refresh.Tick += (_, _) =>
        {
            if (link == null) return;
            long bytes = Interlocked.Read(ref link.AudioBytes), packets = Interlocked.Read(ref link.AudioPackets);
            stats.Text = $"RTT: {link.RoundTripMs} мс · звук: {(bytes - lastBytes) * 8 / 1_000_000.0:F2} Мбит/с (IPv4)\nБуфер: {link.Audio.BufferedMs:F1} мс · опустошения: {link.Audio.Underruns} · пакеты: {packets}";
            lastBytes = bytes;
        };
        refresh.Start(); FormClosing += (_, _) => { updateCancellation.Cancel(); reconnectTimer.Dispose(); Disconnect(); input?.Dispose(); refresh.Dispose(); SaveSettings(); };
    }
    private bool HasPairingKey() { try { return Convert.FromBase64String(pairing.Text.Trim()).Length == 32; } catch { return false; } }
    private async Task TryReconnect()
    { if (!IsDisposed && setupComplete && reconnect.Checked && !manuallyPaused && link == null && !connecting && !installing && HasPairingKey()) await Connect(); }
    private async Task ImportPairing(string path, bool connectAfter = true)
    {
        try
        {
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Файл подключения слишком большой.");
            using var document = JsonDocument.Parse(File.ReadAllText(path)); var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("repository").GetString() != Updates.Repository) throw new InvalidDataException("Несовместимый файл подключения.");
            string text = root.GetProperty("key").GetString()!;
            if (Convert.FromBase64String(text).Length != 32) throw new InvalidDataException("Неверный ключ в файле.");
            Disconnect(); pairing.Text = text; automatic.Checked = true; manuallyPaused = false;
            if (reconnect.Checked) CredentialStore.Save(text);
            macName = root.TryGetProperty("macName", out var name) ? name.GetString() ?? "Mac" : "Mac";
            status.Text = "Mac добавлен. Выберите наушники и подключитесь.";
            setupStage = 1; SelectPage(connectAfter ? 0 : 1); UpdateWizard(); SaveSettings();
            if (connectAfter) await Connect();
        }
        catch (Exception e) { status.Text = "Не удалось открыть файл: " + e.Message; }
    }
    private void RegisterPairingType()
    {
        try
        {
            using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.edgelink");
            string? existing = extension.GetValue(null) as string;
            if (!string.IsNullOrEmpty(existing) && existing != "EdgeLink.Pairing") return;
            extension.SetValue("", "EdgeLink.Pairing");
            using var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\EdgeLink.Pairing"); type.SetValue("", "EdgeLink Mac connection");
            using var icon = type.CreateSubKey("DefaultIcon"); icon.SetValue("", $"\"{Environment.ProcessPath}\",0");
            using var command = type.CreateSubKey(@"shell\open\command"); command.SetValue("", $"\"{Environment.ProcessPath}\" --pair \"%1\"");
        }
        catch { }
    }
    private async Task ConfigureFirewall()
    {
        firewall.Enabled = false;
        try
        {
            string file = Path.Combine(Path.GetTempPath(), "EdgeLink-firewall-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, """
param([string]$Program)
$ErrorActionPreference = 'Stop'
foreach ($name in @('EdgeLink audio (automatic Ethernet)', 'EdgeLink discovery (Ethernet)')) {
  Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
New-NetFirewallRule -DisplayName 'EdgeLink audio (automatic Ethernet)' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 48792 -RemoteAddress LocalSubnet -InterfaceType Wired -Program $Program -Profile Any | Out-Null
New-NetFirewallRule -DisplayName 'EdgeLink discovery (Ethernet)' -Direction Inbound -Action Allow -Protocol UDP -RemotePort 48794 -RemoteAddress LocalSubnet -InterfaceType Wired -Program $Program -Profile Any | Out-Null
""");
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file, "-Program", Environment.ProcessPath! }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Не удалось открыть настройку firewall.");
            await process.WaitForExitAsync(); File.Delete(file);
            if (process.ExitCode != 0) throw new IOException("Windows не добавила правило firewall.");
            status.Text = "Соединение по Ethernet разрешено."; manuallyPaused = false; await TryReconnect();
        }
        catch (Exception e) { status.Text = e.Message; }
        finally { if (!IsDisposed) firewall.Enabled = true; }
    }
    private async Task CheckUpdates(bool manual)
    {
        if (updating) return;
        string attemptFile = Path.Combine(Path.GetDirectoryName(configPath)!, "update-last-attempt.txt");
        if (!manual && File.Exists(attemptFile) && DateTimeOffset.TryParse(File.ReadAllText(attemptFile), out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1)) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(attemptFile)!); File.WriteAllText(attemptFile, DateTimeOffset.UtcNow.ToString("O")); } catch { }
        updating = true; updateButton.Enabled = false; updateStatus.Text = "Проверка GitHub Releases…";
        try
        {
            availableUpdate = await Updates.Check(updateCancellation.Token);
            if (IsDisposed) return;
            updateStatus.Text = availableUpdate == null ? $"Установлена актуальная версия {Updates.CurrentVersion}" : $"Доступна версия {availableUpdate.Version}";
            updateButton.Text = availableUpdate == null ? "Проверить обновления" : "Установить обновление";
        }
        catch (Exception e) { if (!IsDisposed) updateStatus.Text = manual ? e.Message : "Не удалось проверить обновления. Доступна ручная проверка."; }
        finally { updating = false; if (!IsDisposed) updateButton.Enabled = true; }
        if (manual && !IsDisposed && availableUpdate is { } update) await OfferUpdate(update);
    }
    private async Task OfferUpdate(ReleaseUpdate update)
    {
        if (updating || connecting) return;
        if (MessageBox.Show(this, $"Установить EdgeLink {update.Version}?\nПриложение скачает обновление из b0rdik/EdgeLink, проверит SHA-256 и перезапустится. Предыдущая версия сохранится рядом с приложением.", "EdgeLink", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        installing = true; updating = true; updateButton.Enabled = false; updateStatus.Text = "Скачивание и проверка обновления…";
        try
        {
            string staged = await Updates.Stage(update, updateCancellation.Token);
            if (IsDisposed) return;
            Updates.LaunchInstaller(staged); Close();
        }
        catch (Exception e) { if (!IsDisposed) updateStatus.Text = "Обновление не установлено: " + e.Message; }
        finally { installing = false; updating = false; if (!IsDisposed) updateButton.Enabled = true; }
    }
    internal async Task AcceptInvocation(string? pairFile)
    {
        if (IsDisposed) return; Show(); WindowState = FormWindowState.Normal; Activate();
        if (pairFile != null) await ImportPairing(pairFile, setupComplete);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); int dark = 1;
        _ = SingleInstance.DwmSetWindowAttribute(Handle,20,ref dark,sizeof(int));
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0xFF) input?.Raw(message.LParam);
        base.WndProc(ref message);
    }
    private async Task Connect()
    {
        if (connecting || input == null || installing || IsDisposed) return;
        connecting = true; connect.Enabled = false; lastError = ""; status.Text = "Подключение…"; UpdateWizard();
        Link? candidate = null;
        try
        {
            if (automatic.Checked)
            {
                status.Text = "Ищем ваш Mac по Ethernet…";
                var found = await Discovery.Find(pairing.Text, updateCancellation.Token);
                if (IsDisposed || Disposing) throw new OperationCanceledException();
                if (found == null) throw new IOException("Mac пока не найден. Проверьте кабель, разрешения Mac и firewall Windows.");
                local.Text = found.LocalIP; remote.Text = found.MacIP;
            }
            int buffer = new[] { 10, 20, 40 }[jitter.SelectedIndex];
            candidate = new Link(buffer, new[] { 1, 2, 4 }[mouseRate.SelectedIndex]);
            connectingLink = candidate;
            candidate.Failed += error => { if (!IsDisposed) BeginInvoke(() => { if (ReferenceEquals(link, candidate)) { lastError = error; Disconnect(); } }); };
            await candidate.Connect(local.Text.Trim(), remote.Text.Trim(), pairing.Text);
            if (IsDisposed || Disposing) throw new OperationCanceledException();
            using var enumerator = new MMDeviceEnumerator();
            string id = devices[device.SelectedIndex].id;
            endpoint = id == "" ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
            output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 10);
            output.Init(candidate.Audio);
            output.PlaybackStopped += (_, e) => { if (e.Exception != null && !IsDisposed) BeginInvoke(() => { lastError = "Аудиовыход: " + e.Exception.Message; Disconnect(); }); };
            output.Play();
            if (!candidate.IsAlive) throw new IOException("Соединение потеряно во время запуска.");
            link = candidate;
            connectingLink = null;
            input.Side = side.SelectedIndex == 0 ? "left" : "right";
            input.SwapControlCommand = swap.Checked; input.Gain = (double)gain.Value;
            input.SetScreen(Screen.AllScreens[screen.SelectedIndex]);
            timerResolution = Native.timeBeginPeriod(1) == 0;
            lastBytes = 0; connect.Text = "Отключить"; SaveSettings(); SetFields(false); UpdateStatus();
            if (reconnect.Checked) CredentialStore.Save(pairing.Text);
        }
        catch (Exception e) { candidate?.Dispose(); connectingLink = null; if (!IsDisposed) { lastError = "Не удалось подключиться: " + e.Message; Disconnect(); } }
        finally { connecting = false; if (!IsDisposed) { connect.Enabled = true; UpdateWizard(); } }
    }
    private void Disconnect()
    {
        input?.Return(); var old = link; link = null; old?.Dispose();
        connectingLink?.Dispose(); connectingLink = null;
        output?.Stop(); output?.Dispose(); output = null; endpoint?.Dispose(); endpoint = null;
        if (timerResolution) { Native.timeEndPeriod(1); timerResolution = false; }
        connect.Text = "Подключить"; SetFields(true); UpdateStatus();
    }
    private void SetFields(bool enabled)
    { foreach (var field in new Control[] { local, remote, pairing, side, screen, device, jitter, mouseRate, gain, swap, automatic, importPairing }) field.Enabled = enabled; local.Enabled = remote.Enabled = enabled && !automatic.Checked; }
    private void UpdateStatus() { status.Text = link == null ? (lastError == "" ? "Не подключено" : lastError) : input?.Active == true ? "Управление: Mac · звук подключён" : "Управление: ПК · звук подключён"; UpdateWizard(); }
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            // Pairing secret deliberately stays out of the settings file.
            File.WriteAllText(configPath, JsonSerializer.Serialize(new { setupComplete, macName, local = local.Text, remote = remote.Text, automatic = automatic.Checked, reconnect = reconnect.Checked, side = side.SelectedIndex, jitter = jitter.SelectedIndex, mouseRate = mouseRate.SelectedIndex, gain = gain.Value, swap = swap.Checked, device = devices[device.SelectedIndex].id }));
        }
        catch { }
    }
    private void LoadSettings()
    {
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(configPath)); var root = d.RootElement;
            if (root.TryGetProperty("setupComplete", out var complete)) setupComplete = complete.GetBoolean();
            if (root.TryGetProperty("macName", out var name)) macName = name.GetString() ?? "Mac";
            local.Text = root.GetProperty("local").GetString(); remote.Text = root.GetProperty("remote").GetString();
            side.SelectedIndex = Math.Clamp(root.GetProperty("side").GetInt32(), 0, 1);
            jitter.SelectedIndex = Math.Clamp(root.GetProperty("jitter").GetInt32(), 0, 2);
            if (root.TryGetProperty("mouseRate", out var rate)) mouseRate.SelectedIndex = Math.Clamp(rate.GetInt32(), 0, 2);
            if (root.TryGetProperty("automatic", out var auto)) automatic.Checked = auto.GetBoolean();
            if (root.TryGetProperty("reconnect", out var reconnectValue)) reconnect.Checked = reconnectValue.GetBoolean();
            gain.Value = Math.Clamp(root.GetProperty("gain").GetDecimal(), gain.Minimum, gain.Maximum);
            swap.Checked = root.GetProperty("swap").GetBoolean();
            int index = devices.FindIndex(v => v.id == root.GetProperty("device").GetString()); if (index >= 0) device.SelectedIndex = index;
        }
        catch { }
    }
}
