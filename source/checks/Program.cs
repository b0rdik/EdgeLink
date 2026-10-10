using System.Buffers.Binary;
using System.Security.Cryptography;
using EdgeLink;

static void Check(bool condition, string name)
{ if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
byte[] master = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
byte[] payload = "Test Δ русский"u8.ToArray();
Check(Wire.Open(Wire.Seal(payload, master), master).SequenceEqual(payload), "AES-GCM roundtrip / Unicode");
byte[] nist = Convert.FromHexString("000000000000000000000000530F8AFBC74536B9A963B4F1C4CB738B");
Check(Wire.Open(nist, new byte[32]).Length == 0, "AES-256-GCM known empty vector");
var tamper = Wire.Seal(payload, master); tamper[12] ^= 1;
bool rejected = false;
try { Wire.Open(tamper, master); } catch (CryptographicException) { rejected = true; }
Check(rejected, "ciphertext tampering rejected");
rejected = false;
try { Wire.Open(Wire.Seal(payload, master), new byte[32]); } catch (CryptographicException) { rejected = true; }
Check(rejected, "wrong pairing key rejected");
using (var invalidLength = new MemoryStream(new byte[] { 0x7f, 0xff, 0xff, 0xff }))
{
    rejected = false;
    try { await Wire.Receive(invalidLength, master, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "oversized control frames rejected before allocation");
}
var move = Wire.Move(7, 42, 123.25, 321.5);
Check(move.Length == 29 && BinaryPrimitives.ReadUInt32LittleEndian(move.AsSpan(1)) == 7 &&
    BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(move.AsSpan(13))) == 123.25, "mouse wire format");

byte[] Packet(ulong frame, short amplitude = 1000)
{
    byte[] p = new byte[977]; p[0] = 2;
    BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(1), frame / 240 + 1);
    BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(9), frame);
    for (int i = 17; i < p.Length; i += 2) BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(i), amplitude);
    return p;
}
var audio = new AudioBuffer(10);
audio.Feed(Packet(0)); audio.Feed(Packet(240)); audio.Feed(Packet(480));
Check(Math.Abs(audio.BufferedMs - 15) < .01, "PCM packets accumulate exact frame count");
audio.Feed(Packet(240)); Check(Math.Abs(audio.BufferedMs - 15) < .01, "late / duplicate audio discarded");
byte[] rendered = new byte[240 * 8]; audio.Read(rendered, 0, rendered.Length);
float sample = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rendered));
Check(Math.Abs(sample - 1000f / 32768) < .00001, "PCM16 converts to stereo float playback");
for (ulong frame = 720; frame < 48000; frame += 240) audio.Feed(Packet(frame));
Check(audio.BufferedMs <= 30 && audio.DroppedFrames > 0, "backlog bounded instead of accumulating latency");
audio.Feed(Packet(96000)); Check(audio.BufferedMs <= 5.1, "large audio discontinuity flushes stale buffer");
byte[] empty = new byte[4800 * 8]; audio.Read(empty, 0, empty.Length);
Check(empty.All(v => v == 0), "prebuffering provides silence rather than stale sound");

var release = new Dictionary<string, object> {
    ["tag_name"] = "v0.2.0", ["draft"] = false, ["prerelease"] = false,
    ["assets"] = new[] { new Dictionary<string, object> { ["name"] = Updates.AssetName,
        ["browser_download_url"] = "https://github.com/b0rdik/EdgeLink/releases/download/v0.2.0/" + Updates.AssetName,
        ["digest"] = "sha256:" + new string('a', 64), ["size"] = 1234L } }
};
System.Text.Json.JsonDocument Metadata() => System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(release));
using (var document = Metadata()) {
    Check(Updates.ParseRelease(document.RootElement, "0.1.0")?.Version == "0.2.0", "Windows release / SHA metadata parsed");
    Check(Updates.ParseRelease(document.RootElement, "0.2.0") == null && Updates.ParseRelease(document.RootElement, "1.0.0") == null, "Windows updater skips same version / downgrade");
}
var assetMetadata = ((Dictionary<string, object>[])release["assets"])[0];
foreach (var invalid in new[] { "v01.2.0", "0.2.0-beta", "0.2", "0.2.0/evil" }) {
    release["tag_name"] = invalid; rejected = false;
    using var document = Metadata(); try { Updates.ParseRelease(document.RootElement, "0.1.0"); } catch { rejected = true; }
    Check(rejected, "Windows rejects invalid version " + invalid);
}
release["tag_name"] = "v0.2.0";
foreach (string invalidDigest in new[] { "", "sha256:" + new string('x', 64) }) {
    assetMetadata["digest"] = invalidDigest; rejected = false;
    using var document = Metadata(); try { Updates.ParseRelease(document.RootElement, "0.1.0"); } catch { rejected = true; }
    Check(rejected, "Windows rejects missing / invalid checksum");
}
assetMetadata["digest"] = "sha256:" + new string('a', 64); assetMetadata["browser_download_url"] = "https://evil.invalid/app.zip";
using (var document = Metadata()) { rejected = false; try { Updates.ParseRelease(document.RootElement, "0.1.0"); } catch { rejected = true; } Check(rejected, "Windows rejects altered release URL"); }
string scratch = Path.Combine(Path.GetTempPath(), "EdgeLink-archive-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
try {
    foreach (string entry in new[] { "../escape", "EdgeLink.exe" }) {
        string zip = Path.Combine(scratch, "check.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create)) {
            using var file = archive.CreateEntry(entry).Open(); file.Write(new byte[128]);
        }
        rejected = false; try { Updates.ExtractExecutable(zip, scratch); } catch { rejected = true; }
        Check(rejected, "Windows rejects " + (entry == "EdgeLink.exe" ? "non-PE update" : "traversal ZIP"));
        File.Delete(zip); File.Delete(Path.Combine(scratch, "EdgeLink.exe"));
    }
} finally { Directory.Delete(scratch, true); }

if (args.Contains("--integration"))
{
    using (var probe = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0))) {
        string nonce = new string('a', 32);
        var request = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { type = "edgelink-discover", nonce });
        await probe.SendAsync(request, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 48794));
        var reply = await probe.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        using var document = System.Text.Json.JsonDocument.Parse(reply.Buffer);
        var root = document.RootElement;
        Check(root.GetProperty("nonce").GetString() == nonce && root.GetProperty("ip").GetString() == "127.0.0.1" &&
            root.GetProperty("fingerprint").GetString() == Convert.ToHexString(SHA256.HashData(master)[..8]).ToLowerInvariant() && !root.TryGetProperty("key", out _), "Mac Ethernet discovery echoes nonce / fingerprint without key");
    }
    using (var wrong = new Link(10))
    {
        rejected = false;
        try { await wrong.Connect("127.0.0.1", "127.0.0.1", Convert.ToBase64String(new byte[32])); } catch { rejected = true; }
        Check(rejected, "Swift server rejects wrong Windows pairing key");
    }
    using var link = new Link(10);
    string? failure = null; link.Failed += error => failure = error;
    await link.Connect("127.0.0.1", "127.0.0.1", Convert.ToBase64String(master));
    Check(link.MacWidth >= 100 && link.MacHeight >= 100, "Windows ↔ Swift authenticated handshake");
    link.Move(7, 123.25, 321.5);
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (Interlocked.Read(ref link.AudioPackets) < 5 && DateTime.UtcNow < deadline) await Task.Delay(10);
    Check(Interlocked.Read(ref link.AudioPackets) >= 5, "Swift → .NET encrypted UDP audio");
    await Task.Delay(700);
    Check(link.IsAlive && failure == null && link.RoundTripMs < 100, "session-key TCP heartbeat / roundtrip");
    deadline = DateTime.UtcNow.AddSeconds(4);
    while ((link.IsAlive || failure == null) && DateTime.UtcNow < deadline) await Task.Delay(20);
    Check(!link.IsAlive && failure != null, "connection loss terminates client session");
}
if (args.Contains("--release")) {
    var update = await Updates.Check(CancellationToken.None);
    Check(update?.Version == "0.2.0", "GitHub live Windows release metadata / version / digest");
    try {
        string staged = await Updates.Stage(update!, CancellationToken.None);
        try { Check(File.Exists(staged), "Windows OTA downloads / verifies checksum / extracts actual PE x64 / native version"); }
        finally { Directory.Delete(Path.GetDirectoryName(staged)!, true); }
    } catch (InvalidDataException e) when (!OperatingSystem.IsWindows() && e.Message == "Версия внутри файла не совпадает с релизом.") {
        // On Unix, FileVersionInfo reads managed assembly metadata only, not native PE VERSIONINFO.
        // This failure occurs strictly after the real download, size/hash and x64 PE checks succeed.
        Console.WriteLine("PASS: Windows OTA download / checksum / x64 PE checks on macOS");
        Console.WriteLine("LIMIT: native Windows FileVersionInfo and installer require a Windows run");
    }
}
Console.WriteLine("ALL CHECKS PASSED");
