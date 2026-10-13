using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EdgeLink;

internal sealed record ReleaseUpdate(string Version, Uri Download, string Hash, long Size, Uri Page);

internal static partial class Updates
{
    public const string Repository = "b0rdik/EdgeLink";
    public const string AssetName = "EdgeLink-windows-x64.zip";
    public static string CurrentVersion => typeof(Updates).Assembly.GetName().Version!.ToString(3);
    [GeneratedRegex(@"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"^sha256:([a-fA-F0-9]{64})$")]
    private static partial Regex DigestPattern();
    private static HttpClient Client()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EdgeLink/" + CurrentVersion);
        return client;
    }
    public static ReleaseUpdate? ParseRelease(JsonElement release, string currentVersion, string assetName = AssetName)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString()!;
        var match = VersionPattern().Match(tag);
        if (!match.Success) throw new InvalidDataException("Неподдерживаемый номер версии GitHub Release.");
        string version = tag.TrimStart('v');
        if (Version.Parse(version) <= Version.Parse(currentVersion)) return null;
        var asset = release.GetProperty("assets").EnumerateArray().SingleOrDefault(a => a.GetProperty("name").GetString() == assetName);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("В релизе отсутствует сборка для этой платформы.");
        string download = asset.GetProperty("browser_download_url").GetString()!;
        string expected = $"https://github.com/{Repository}/releases/download/{tag}/{assetName}";
        if (download != expected) throw new InvalidDataException("Неожиданный адрес файла обновления.");
        var digest = DigestPattern().Match(asset.GetProperty("digest").GetString() ?? "");
        if (!digest.Success) throw new InvalidDataException("GitHub не предоставил SHA-256 файла; установка отменена.");
        long size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 250 * 1024 * 1024) throw new InvalidDataException("Недопустимый размер обновления.");
        return new(version, new Uri(download), digest.Groups[1].Value.ToLowerInvariant(), size, new Uri($"https://github.com/{Repository}/releases/tag/{tag}"));
    }
    public static async Task<ReleaseUpdate?> Check(CancellationToken token)
    {
        using var client = Client();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new IOException("Репозиторий или опубликованный релиз пока недоступен.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Metadata too large");
        byte[] data = await ReadLimited(response.Content, 1024 * 1024, timeout.Token);
        using var document = JsonDocument.Parse(data);
        return ParseRelease(document.RootElement, CurrentVersion);
    }
    private static bool AllowedRedirect(Uri url) => url.Scheme == "https" &&
        (url.Host == "github.com" || url.Host == "release-assets.githubusercontent.com" || url.Host == "objects.githubusercontent.com") && url.IsDefaultPort && url.UserInfo == "";
    private static async Task<HttpResponseMessage> DownloadResponse(HttpClient client, Uri url, CancellationToken token)
    {
        for (int redirects = 0; redirects < 5; redirects++)
        {
            if (!AllowedRedirect(url)) throw new InvalidDataException("Небезопасный адрес загрузки.");
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location == null) throw new IOException("Redirect without location");
                url = location.IsAbsoluteUri ? location : new Uri(url, location); continue;
            }
            response.EnsureSuccessStatusCode(); return response;
        }
        throw new IOException("Too many redirects");
    }
    private static async Task<byte[]> ReadLimited(HttpContent content, int limit, CancellationToken token)
    {
        using var result = new MemoryStream(); using var source = await content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[8192]; int count;
        while ((count = await source.ReadAsync(buffer, token)) != 0)
        { if (result.Length + count > limit) throw new InvalidDataException("Response too large"); await result.WriteAsync(buffer.AsMemory(0, count), token); }
        return result.ToArray();
    }
    public static async Task<string> Stage(ReleaseUpdate update, CancellationToken token)
    {
        string folder = Path.Combine(Path.GetTempPath(), "EdgeLink-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string zip = Path.Combine(folder, AssetName);
            using var client = Client(); using var response = await DownloadResponse(client, update.Download, token);
            using (var input = await response.Content.ReadAsStreamAsync(token))
            using (var file = File.Create(zip))
            {
                byte[] block = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(block, token)) != 0)
                { total += count; if (total > update.Size) throw new InvalidDataException("Файл превышает заявленный размер."); await file.WriteAsync(block.AsMemory(0, count), token); }
                if (total != update.Size) throw new InvalidDataException("Загрузка не завершена.");
            }
            using (var file = File.OpenRead(zip))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!actual.Equals(update.Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 обновления не совпадает.");
            }
            string exe = ExtractExecutable(zip, folder);
            string version = FileVersionInfo.GetVersionInfo(exe).FileVersion?.Split('+')[0] ?? "";
            if (!Version.TryParse(version, out var built) || built.ToString(3) != update.Version)
                throw new InvalidDataException("Версия внутри файла не совпадает с релизом.");
            using(var verified=File.OpenRead(exe))File.WriteAllText(exe+".sha256",Convert.ToHexString(await SHA256.HashDataAsync(verified,token)));
            return exe;
        }
        catch { Directory.Delete(folder, true); throw; }
    }
    public static string ExtractExecutable(string zip, string folder)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Any(e => e.FullName.Contains('\\') || e.FullName.StartsWith('/') || e.FullName.Split('/').Contains("..") || ((e.ExternalAttributes >> 16) & 0xf000) == 0xa000))
            throw new InvalidDataException("Unsafe ZIP entry");
        var entries = archive.Entries.Where(e => e.FullName == "EdgeLink.exe").ToArray();
        if (entries.Length != 1 || entries[0].Length < 64 || entries[0].Length > 300 * 1024 * 1024) throw new InvalidDataException("Invalid Windows archive");
        string exe = Path.Combine(folder, "EdgeLink.exe"); entries[0].ExtractToFile(exe, false);
        using var stream = File.OpenRead(exe); using var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Not an executable");
        stream.Position = 0x3c; uint pe = reader.ReadUInt32(); if (pe > stream.Length - 6) throw new InvalidDataException("Invalid PE header");
        stream.Position = pe;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Not a Windows x64 executable");
        return exe;
    }
    public static void LaunchInstaller(string stagedExe)
    {
        string target = Environment.ProcessPath ?? throw new IOException("Путь приложения неизвестен.");
        string folder = Path.GetDirectoryName(stagedExe)!;
        string probe = Path.Combine(Path.GetDirectoryName(target)!, ".edgelink-write-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, ""); File.Delete(probe); // fail before quitting if installed in a protected folder
        string script = Path.Combine(folder, "install.ps1");
        string expectedHash = File.ReadAllText(stagedExe + ".sha256").Trim();
        if(!Regex.IsMatch(expectedHash,@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("Контрольная сумма подготовленного обновления отсутствует.");
        File.WriteAllText(script, InstallerScript);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ParentId", Environment.ProcessId.ToString(), "-Target", target, "-Staged", stagedExe, "-ExpectedHash", expectedHash }) start.ArgumentList.Add(arg);
        _ = Process.Start(start) ?? throw new IOException("Не удалось запустить установщик.");
    }
    internal static bool ReportReady(string[] arguments)
    {
        int index = Array.IndexOf(arguments, "--update-ready");
        if (index < 0) return true;
        if (arguments.Length <= index + 2 || !Guid.TryParse(arguments[index + 2], out _)) return false;
        string nonce = arguments[index + 2], path = Path.GetFullPath(arguments[index + 1]);
        string folder = Path.GetDirectoryName(path)!;
        if (Path.GetFileName(path) != "ready-" + nonce + ".txt" ||
            !Path.GetFileName(folder).StartsWith("EdgeLink-update-", StringComparison.Ordinal) ||
            !Guid.TryParse(Path.GetFileName(folder)["EdgeLink-update-".Length..], out _) ||
            !string.Equals(Path.GetDirectoryName(folder)?.TrimEnd(Path.DirectorySeparatorChar), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        string incoming = path + ".writing-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(incoming, nonce); File.Move(incoming, path, true); return true; }
        catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        finally { try { if(File.Exists(incoming))File.Delete(incoming); } catch(IOException) {} catch(UnauthorizedAccessException) {} }
    }
    internal static void RemoveLegacyBackups(string? installedExecutable = null)
    {
        string? target = installedExecutable ?? Environment.ProcessPath; if(target==null)return;
        string folder=Path.GetDirectoryName(target)!;
        string pattern="^"+Regex.Escape(Path.GetFileName(target))+@"\.previous-\d{8}T\d{9}\.exe$";
        try {
            foreach(string file in Directory.EnumerateFiles(folder)) {
                if(!Regex.IsMatch(Path.GetFileName(file),pattern)||File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))continue;
                if(FileVersionInfo.GetVersionInfo(file).ProductName!="EdgeLink")continue;
                try {File.Delete(file);} catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    internal const string InstallerScript = """
param([int]$ParentId, [string]$Target, [string]$Staged, [string]$ExpectedHash)
$ErrorActionPreference = 'Stop'
$log = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EdgeLink/update.log'
$incoming = $Target + '.incoming-' + [Guid]::NewGuid().ToString('N')
$folder = [IO.Path]::GetDirectoryName($Staged)
$nonce = [Guid]::NewGuid().ToString('N')
$ready = Join-Path $folder ('ready-' + $nonce + '.txt')
$committed = $false
$exitCode = 0
$launched = $null
try {
  try { $parent = [Diagnostics.Process]::GetProcessById($ParentId) } catch { $parent = $null }
  if ($parent -and -not $parent.WaitForExit(30000)) { throw 'Application did not exit' }
  [IO.File]::Copy($Staged, $incoming, $false)
  if ($ExpectedHash -notmatch '^[a-fA-F0-9]{64}$' -or $ExpectedHash -ne (Get-FileHash -LiteralPath $incoming -Algorithm SHA256).Hash) { throw 'Prepared file hash mismatch' }
  [IO.File]::Replace($incoming, $Target, [NullString]::Value)
  $committed = $true
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($log)) | Out-Null
  [IO.File]::WriteAllText($log, 'Update installed successfully')
  $launched = Start-Process -FilePath $Target -WorkingDirectory ([IO.Path]::GetDirectoryName($Target)) -ArgumentList @('--update-ready', ('"' + $ready + '"'), $nonce) -PassThru
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  while ([DateTime]::UtcNow -lt $deadline) {
    if ([IO.File]::Exists($ready) -and [IO.File]::ReadAllText($ready) -eq $nonce) { break }
    if ($launched.HasExited) { throw 'New application exited before confirming startup' }
    Start-Sleep -Milliseconds 100
  }
  if (-not [IO.File]::Exists($ready) -or [IO.File]::ReadAllText($ready) -ne $nonce) { throw 'New application did not confirm startup' }
} catch {
  $exitCode = 1
  $failure = $_.Exception.Message
  if ($launched -and -not $launched.HasExited) { $launched.Kill(); $launched.WaitForExit(10000) | Out-Null }
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($log)) | Out-Null
  [IO.File]::WriteAllText($log, $failure)
  if ($env:OS -eq 'Windows_NT') {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show('EdgeLink update failed. ' + $failure, 'EdgeLink') | Out-Null
  }
  if (-not $committed -and [IO.File]::Exists($Target)) { Start-Process -FilePath $Target }
} finally {
  if ([IO.File]::Exists($incoming)) { [IO.File]::Delete($incoming) }
  if ([IO.Directory]::Exists($folder)) { [IO.Directory]::Delete($folder, $true) }
}
exit $exitCode
""";
}
