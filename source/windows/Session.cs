using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.UI.Xaml.Controls;
namespace EdgeLink;
public sealed partial class MainWindow {
    private bool HasPairingKey() { try { return Convert.FromBase64String(pairing.Password.Trim()).Length == 32; } catch { return false; } }
    private async Task TryReconnect()
    { if (!IsDisposed && setupComplete && reconnect.CheckedValue && !manuallyPaused && link == null && !connecting && !installing && HasPairingKey()) await Connect(); }
    private async Task ImportPairing(string path, bool connectAfter = true)
    {
        try
        {
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Файл подключения слишком большой.");
            using var document = JsonDocument.Parse(File.ReadAllText(path)); var root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("repository").GetString() != Updates.Repository) throw new InvalidDataException("Несовместимый файл подключения.");
            string text = root.GetProperty("key").GetString()!;
            if (Convert.FromBase64String(text).Length != 32) throw new InvalidDataException("Неверный ключ в файле.");
            Disconnect(); pairing.Password = text; automatic.CheckedValue = true; manuallyPaused = false;
            if (reconnect.CheckedValue) CredentialStore.Save(text);
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
        firewall.IsEnabled = false;
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
        finally { if (!IsDisposed) firewall.IsEnabled = true; }
    }
    private async Task CheckUpdates(bool manual)
    {
        if (updating) return;
        string attemptFile = Path.Combine(Path.GetDirectoryName(configPath)!, "update-last-attempt.txt");
        if (!manual && File.Exists(attemptFile) && DateTimeOffset.TryParse(File.ReadAllText(attemptFile), out var last) && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1)) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(attemptFile)!); File.WriteAllText(attemptFile, DateTimeOffset.UtcNow.ToString("O")); } catch { }
        updating = true; updateButton.IsEnabled = false; updateStatus.Text = "Проверка GitHub Releases…";
        try
        {
            availableUpdate = await Updates.Check(updateCancellation.Token);
            if (IsDisposed) return;
            updateStatus.Text = availableUpdate == null ? $"Установлена актуальная версия {Updates.CurrentVersion}" : $"Доступна версия {availableUpdate.Version}";
            updateButton.Text = availableUpdate == null ? "Проверить обновления" : "Установить обновление";
        }
        catch (Exception e) { if (!IsDisposed) updateStatus.Text = manual ? e.Message : "Не удалось проверить обновления. Доступна ручная проверка."; }
        finally { updating = false; if (!IsDisposed) updateButton.IsEnabled = true; }
        if (manual && !IsDisposed && availableUpdate is { } update) await OfferUpdate(update);
    }
    private async Task OfferUpdate(ReleaseUpdate update)
    {
        if (updating || connecting) return;
        if (!await ConfirmUpdate(update)) return;
        installing = true; updating = true; updateButton.IsEnabled = false; updateStatus.Text = "Скачивание и проверка обновления…";
        try
        {
            string staged = await Updates.Stage(update, updateCancellation.Token);
            if (IsDisposed) return;
            Updates.LaunchInstaller(staged); Close();
        }
        catch (Exception e) { if (!IsDisposed) updateStatus.Text = "Обновление не установлено: " + e.Message; }
        finally { installing = false; updating = false; if (!IsDisposed) updateButton.IsEnabled = true; }
    }
    internal async Task AcceptInvocation(string? pairFile)
    {
        if (IsDisposed) return; RestoreFromTray();
        if (pairFile != null) await ImportPairing(pairFile, setupComplete);
    }
    private async Task Connect()
    {
        if (connecting || installing || IsDisposed || !EnsureInput()) return;
        connecting = true; connect.IsEnabled = false; lastError = ""; status.Text = "Подключение…"; UpdateWizard();
        Link? candidate = null;
        try
        {
            if (automatic.CheckedValue)
            {
                status.Text = "Ищем ваш Mac по Ethernet…";
                string savedKey = pairing.Password;
                var found = await Task.Run(() => Discovery.Find(savedKey, updateCancellation.Token), updateCancellation.Token);
                if (IsDisposed) throw new OperationCanceledException();
                if (found == null) throw new IOException("Mac пока не найден. Проверьте кабель, разрешения Mac и firewall Windows.");
                local.Text = found.LocalIP; remote.Text = found.MacIP;
            }
            int buffer = new[] { 10, 20, 40 }[jitter.SelectedIndex];
            candidate = new Link(buffer, new[] { 1, 2, 4 }[mouseRate.SelectedIndex]);
            connectingLink = candidate;
            candidate.Failed += error => { if (!IsDisposed) BeginInvoke(() => { if (ReferenceEquals(link, candidate)) { lastError = error; Disconnect(); } }); };
            await candidate.Connect(local.Text.Trim(), remote.Text.Trim(), pairing.Password);
            if (IsDisposed) throw new OperationCanceledException();
            using var enumerator = new MMDeviceEnumerator();
            string id = devices[device.SelectedIndex].id;
            endpoint = id == "" ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(id);
            var candidateOutput = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 10);
            output = candidateOutput;
            candidateOutput.Init(candidate.Audio);
            candidateOutput.PlaybackStopped += (_, e) => { if (e.Exception != null && !IsDisposed) BeginInvoke(() => {
                if (!ReferenceEquals(output, candidateOutput) || !ReferenceEquals(link, candidate)) return;
                lastError = "Аудиовыход: " + e.Exception.Message; Disconnect();
            }); };
            candidateOutput.Play();
            if (!candidate.IsAlive) throw new IOException("Соединение потеряно во время запуска.");
            link = candidate;
            connectingLink = null;
            input.Configure(candidate, Desktop.Screens[screen.SelectedIndex].Bounds, side.SelectedIndex == 0 ? "left" : "right", swap.CheckedValue, (double)gain.Value);
            lastBytes = 0; lastRefresh=Stopwatch.GetTimestamp(); UpdateStatsTimer();
            connect.Text = "Отключить"; SaveSettings(); SetFields(false); UpdateStatus();
            if (reconnect.CheckedValue) CredentialStore.Save(pairing.Password);
        }
        catch (Exception e) { candidate?.Dispose(); connectingLink = null; if (!IsDisposed) { lastError = "Не удалось подключиться: " + e.Message; Disconnect(); } }
        finally { connecting = false; if (!IsDisposed) { connect.IsEnabled = true; UpdateWizard(); } }
    }
    private void Disconnect()
    {
        input?.Disconnect(); var old = link; link = null; old?.Dispose();
        UpdateStatsTimer();
        connectingLink?.Dispose(); connectingLink = null;
        output?.Stop(); output?.Dispose(); output = null; endpoint?.Dispose(); endpoint = null;
        if (!IsDisposed) { connect.Text = "Подключить"; SetFields(true); UpdateStatus(); }
    }
    private void SetFields(bool enabled)
    { foreach (var field in new Control[] { local, remote, pairing, side, screen, device, wizardDevice, wizardSide, jitter, mouseRate, gain, swap, automatic, importPairing }) field.IsEnabled = enabled; local.IsEnabled = remote.IsEnabled = enabled && !automatic.CheckedValue; }
    private void UpdateStatus()
    {
        status.Text = link == null ? (lastError == "" ? "Подключите Mac по Ethernet, чтобы начать работу." : lastError) : input?.Active == true ? "Мышь и клавиатура управляют Mac. Звук передаётся в наушники ПК." : input?.FullscreenBlocked == true ? "Связь активна. Переходы приостановлены для полноэкранного приложения; звук продолжает работать." : "Mac и ПК соединены. Звук передаётся в наушники ПК.";
        connect.Style = link == null ? (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] : null;
        if (link == null) stats.Text = "Нет активного соединения.";
        UpdateWizard(); UpdateTrayStatus();
    }
    private void ApplyFullscreenProtection()
    {
        if(previewMode)return;
        try { input?.SetFullscreenProtection(protectFullscreen.CheckedValue); SaveSettings(); UpdateStatus(); }
        catch(Exception error) { status.Text="Не удалось изменить защиту полного экрана: "+error.Message; }
    }
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            // Pairing secret deliberately stays out of the settings file.
            File.WriteAllText(configPath, JsonSerializer.Serialize(new { setupComplete, macName, local = local.Text, remote = remote.Text, automatic = automatic.CheckedValue, reconnect = reconnect.CheckedValue, protectFullscreen = protectFullscreen.CheckedValue, side = side.SelectedIndex, jitter = jitter.SelectedIndex, mouseRate = mouseRate.SelectedIndex, gain = gain.Value, swap = swap.CheckedValue, device = devices[device.SelectedIndex].id }));
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
            if (root.TryGetProperty("automatic", out var auto)) automatic.CheckedValue = auto.GetBoolean();
            if (root.TryGetProperty("reconnect", out var reconnectValue)) reconnect.CheckedValue = reconnectValue.GetBoolean();
            if (root.TryGetProperty("protectFullscreen", out var protection)) protectFullscreen.CheckedValue = protection.GetBoolean();
            gain.Value = Math.Clamp(root.GetProperty("gain").GetDouble(), gain.Minimum, gain.Maximum);
            swap.CheckedValue = root.GetProperty("swap").GetBoolean();
            int index = devices.FindIndex(v => v.id == root.GetProperty("device").GetString()); if (index >= 0) device.SelectedIndex = index;
        }
        catch { }
    }
}
