using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace EdgeLink;

internal sealed class Link : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<object> commands = Channel.CreateBounded<object>(256);
    private readonly TcpClient tcp = new(AddressFamily.InterNetwork);
    private UdpClient? audioSocket, mouseSocket;
    private byte[] key = [];
    private readonly object moveGate = new();
    private (uint epoch, ulong sequence, double x, double y)? pendingMove;
    private ulong moveSequence;
    private long lastPong = Environment.TickCount64;
    private readonly int mouseIntervalMs;
    public AudioBuffer Audio { get; }
    public double MacWidth { get; private set; }
    public double MacHeight { get; private set; }
    public long AudioBytes, AudioPackets;
    public int RoundTripMs;
    public bool IsAlive => !stop.IsCancellationRequested;
    public ulong PositionSequence { get { lock (moveGate) return moveSequence; } }
    public event Action<string>? Failed;
    public Link(int jitterMs, int mouseIntervalMs = 1) { Audio = new(jitterMs); this.mouseIntervalMs = mouseIntervalMs; }

    public async Task Connect(string local, string remote, string pairingKey)
    {
        byte[] master = Convert.FromBase64String(pairingKey.Trim());
        if (master.Length != 32) throw new ArgumentException("Ключ должен содержать 32 байта (скопируйте его с Mac).");
        IPAddress localIP = IPAddress.Parse(local), remoteIP = IPAddress.Parse(remote);
        if (localIP.AddressFamily != AddressFamily.InterNetwork || remoteIP.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Используйте IPv4-адреса Ethernet.");
        audioSocket = new UdpClient(new IPEndPoint(localIP, 48792));
        audioSocket.Client.ReceiveBufferSize = 65536;
        mouseSocket = new UdpClient(new IPEndPoint(localIP, 0));
        mouseSocket.Connect(remoteIP, 48793);
        tcp.Client.Bind(new IPEndPoint(localIP, 0)); tcp.NoDelay = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await tcp.ConnectAsync(remoteIP, 48791, timeout.Token);
        byte[] clientNonce = RandomNumberGenerator.GetBytes(16);
        var stream = tcp.GetStream();
        await Wire.Send(stream, new { type = "connect", version = 1, nonce = Convert.ToBase64String(clientNonce), audioPort = 48792 }, master, timeout.Token);
        var hello = await Wire.Receive(stream, master, timeout.Token);
        if (hello.GetProperty("type").GetString() != "hello" || hello.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Несовместимая версия Mac-приложения.");
        byte[] serverNonce = Convert.FromBase64String(hello.GetProperty("nonce").GetString()!);
        if (serverNonce.Length != 16) throw new InvalidDataException("Invalid nonce");
        key = Wire.SessionKey(master, clientNonce, serverNonce);
        CryptographicOperations.ZeroMemory(master);
        MacWidth = hello.GetProperty("width").GetDouble(); MacHeight = hello.GetProperty("height").GetDouble();
        if (MacWidth < 100 || MacHeight < 100) throw new InvalidDataException("Invalid display dimensions");
        lastPong = Environment.TickCount64;
        _ = Guard(() => WriteLoop(stream)); _ = Guard(() => ReadLoop(stream));
        _ = Guard(() => AudioLoop(remoteIP)); _ = Guard(MouseLoop); _ = Guard(Heartbeat);
    }
    public bool Command(object command)
    {
        if (stop.IsCancellationRequested) return false;
        if (commands.Writer.TryWrite(command)) return true;
        Fail("Переполнена очередь ввода. Управление возвращено на ПК."); return false;
    }
    public void Move(uint epoch, double x, double y) { lock (moveGate) pendingMove = (epoch, ++moveSequence, x, y); }
    private async Task WriteLoop(Stream stream)
    {
        await foreach (var message in commands.Reader.ReadAllAsync(stop.Token))
            await Wire.Send(stream, message, key, stop.Token);
    }
    private async Task ReadLoop(Stream stream)
    {
        while (!stop.IsCancellationRequested)
        {
            var message = await Wire.Receive(stream, key, stop.Token);
            if (message.GetProperty("type").GetString() == "pong")
            {
                lastPong = Environment.TickCount64;
                RoundTripMs = (int)Math.Clamp(lastPong - message.GetProperty("time").GetInt64(), 0, 9999);
            }
        }
    }
    private async Task Heartbeat()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stop.Token))
        {
            if (Environment.TickCount64 - lastPong > 2500) throw new IOException("Mac перестал отвечать.");
            Command(new { type = "ping", time = Environment.TickCount64 });
        }
    }
    private async Task MouseLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(mouseIntervalMs));
        while (await timer.WaitForNextTickAsync(stop.Token))
        {
            (uint epoch, ulong sequence, double x, double y)? move;
            lock (moveGate) { move = pendingMove; pendingMove = null; }
            if (move is { } m)
                await mouseSocket!.SendAsync(Wire.Seal(Wire.Move(m.epoch, m.sequence, m.x, m.y), key), stop.Token);
        }
    }
    private async Task AudioLoop(IPAddress remote)
    {
        while (!stop.IsCancellationRequested)
        {
            var packet = await audioSocket!.ReceiveAsync(stop.Token);
            if (!packet.RemoteEndPoint.Address.Equals(remote) || packet.Buffer.Length != 1005) continue;
            try
            {
                byte[] plain = Wire.Open(packet.Buffer, key);
                if (plain[0] != 2) continue;
                Audio.Feed(plain); Interlocked.Add(ref AudioBytes, packet.Buffer.Length + 28); Interlocked.Increment(ref AudioPackets);
            }
            catch (CryptographicException) { /* unauthenticated UDP is ignored */ }
        }
    }
    private async Task Guard(Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!stop.IsCancellationRequested) Fail(error.Message); }
    }
    private void Fail(string message) { Dispose(); Failed?.Invoke(message); }
    public void Dispose()
    {
        if (stop.IsCancellationRequested) return;
        stop.Cancel(); commands.Writer.TryComplete(); tcp.Dispose(); audioSocket?.Dispose(); mouseSocket?.Dispose();
    }
}
