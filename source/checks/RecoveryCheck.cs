using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using EdgeLink;

internal static class RecoveryCheck
{
    internal static async Task Run(Action<bool,string> check)
    {
        if(Environment.GetEnvironmentVariable("WINDOWS_INSTALLER_SCRIPT_EXPORT") is {} export)File.WriteAllText(export,Updates.InstallerScript);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        string fingerprint = new string('a',16);
        var reply = Task.Run(async () => {
            // Drop the first probe, then send malformed and unrelated responses
            // before a valid answer. The old one-shot search cannot complete.
            _ = await server.ReceiveAsync(limit.Token);
            var request = await server.ReceiveAsync(limit.Token);
            using var json = JsonDocument.Parse(request.Buffer);
            string nonce = json.RootElement.GetProperty("nonce").GetString()!;
            await server.SendAsync("garbage"u8.ToArray(),request.RemoteEndPoint,limit.Token);
            await server.SendAsync("{}"u8.ToArray(),request.RemoteEndPoint,limit.Token);
            await server.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {type="edgelink-mac",protocol=1,nonce,fingerprint="wrong",ip="127.0.0.1",name="Other"}),request.RemoteEndPoint,limit.Token);
            await server.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new {type="edgelink-mac",protocol=1,nonce,fingerprint,ip="127.0.0.1",name="Fixture"}),request.RemoteEndPoint,limit.Token);
        });
        var peer = await Discovery.Probe(IPAddress.Loopback,(IPEndPoint)server.Client.LocalEndPoint!,fingerprint,limit.Token);
        await reply;
        check(peer is {LocalIP:"127.0.0.1",MacIP:"127.0.0.1",Name:"Fixture"},"discovery survives a dropped probe, malformed packet and unrelated peer");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        check(await Discovery.Probe(IPAddress.Loopback,(IPEndPoint)server.Client.LocalEndPoint!,fingerprint,cancelled.Token)==null,"cancelled discovery releases the probe socket");
        string folder=Path.Combine(Path.GetTempPath(),"EdgeLink-update-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try {
            string nonce=Guid.NewGuid().ToString("N"),ready=Path.Combine(folder,"ready-"+nonce+".txt");
            check(Updates.ReportReady(["fixture","--update-ready",ready,nonce])&&File.ReadAllText(ready)==nonce,"update readiness confirms the matching private temporary receipt");
            string outside=Path.Combine(folder,"unrelated.txt");
            check(!Updates.ReportReady(["fixture","--update-ready",outside,nonce])&&!File.Exists(outside),"update readiness refuses an unrelated file path");
        } finally {Directory.Delete(folder,true);}
    }
}
