using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace EdgeLink;

internal sealed record EthernetPeer(string LocalIP, string MacIP, string Name);
internal static class Discovery
{
    public static async Task<EthernetPeer?> Find(string pairingKey, CancellationToken token)
    {
        byte[] key = Convert.FromBase64String(pairingKey.Trim()); if (key.Length != 32) throw new ArgumentException("Сначала добавьте файл подключения с Mac.");
        string fingerprint = Convert.ToHexString(SHA256.HashData(key).AsSpan(0, 8)).ToLowerInvariant();
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => (address: a.Address, mask: a.IPv4Mask, gateway: n.GetIPProperties().GatewayAddresses.Count > 0)))
            .Where(a => !a.gateway || a.address.ToString().StartsWith("169.254."))
            .OrderByDescending(a => a.address.ToString().StartsWith("169.254.")).ToArray();
        if (candidates.Length == 0) return null;
        var probes = candidates.Select(async c =>
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(c.address, 0)) { EnableBroadcast = true };
                var ip = c.address.GetAddressBytes(); var mask = c.mask.GetAddressBytes();
                byte[] broadcast = Enumerable.Range(0, 4).Select(i => (byte)(ip[i] | ~mask[i])).ToArray();
                string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(new { type = "edgelink-discover", nonce });
                await udp.SendAsync(data, new IPEndPoint(new IPAddress(broadcast), 48794), token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(1500);
                while (!timeout.IsCancellationRequested)
                {
                    var response = await udp.ReceiveAsync(timeout.Token);
                    if (response.Buffer.Length > 1024 || response.RemoteEndPoint.Port != 48794) continue;
                    using var document = JsonDocument.Parse(response.Buffer); var root = document.RootElement;
                    if (root.GetProperty("type").GetString() == "edgelink-mac" && root.GetProperty("protocol").GetInt32() == 1 &&
                        root.GetProperty("nonce").GetString() == nonce && root.GetProperty("fingerprint").GetString() == fingerprint &&
                        root.GetProperty("ip").GetString() == response.RemoteEndPoint.Address.ToString())
                        return new EthernetPeer(c.address.ToString(), response.RemoteEndPoint.Address.ToString(), root.GetProperty("name").GetString() ?? "Mac");
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (JsonException) { }
            catch (KeyNotFoundException) { }
            return null;
        }).ToList();
        while (probes.Count > 0)
        { var done = await Task.WhenAny(probes); probes.Remove(done); if (await done is { } found) return found; }
        return null;
    }
}

internal static class CredentialStore
{
    private const string Name = "EdgeLink/paired-mac";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public long LastWritten;
        public uint BlobSize;
        public nint Blob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential credential, uint flags);
    [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32")] private static extern void CredFree(nint credential);
    public static string? Load()
    {
        if (!Read(Name, 1, 0, out var pointer)) return null;
        try { var c = Marshal.PtrToStructure<Credential>(pointer); if (c.BlobSize != 32) return null; byte[] key = new byte[32]; Marshal.Copy(c.Blob, key, 0, 32); return Convert.ToBase64String(key); }
        finally { CredFree(pointer); }
    }
    public static void Save(string keyText)
    {
        byte[] key = Convert.FromBase64String(keyText.Trim()); if (key.Length != 32) throw new ArgumentException("Invalid key");
        nint memory = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.Copy(key, 0, memory, 32);
            var c = new Credential { Type = 1, TargetName = Name, Comment = "EdgeLink paired Mac", BlobSize = 32, Blob = memory, Persist = 2, UserName = Environment.UserName };
            if (!Write(ref c, 0)) throw new System.ComponentModel.Win32Exception();
        }
        finally { for (int i = 0; i < 32; i++) Marshal.WriteByte(memory, i, 0); Marshal.FreeHGlobal(memory); CryptographicOperations.ZeroMemory(key); }
    }
}
