using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using EdgeLink;

// Real encrypted loopback traffic while the originating UI context is blocked.
// No Mac, pairing file, WASAPI device or synthetic hardware input is involved.
internal static class UiLoopCheck
{
    internal static async Task<bool> Run() {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener=new TcpListener(IPAddress.Loopback,48791);listener.Start();
        using var mouse=new UdpClient(new IPEndPoint(IPAddress.Loopback,48793));
        using var audio=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        using var connected=new ManualResetEventSlim();using var finishedProbe=new ManualResetEventSlim();
        var uiResult=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionKey=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] master=RandomNumberGenerator.GetBytes(32);Link? link=null;
        var server=Task.Run(async ()=> {
            using var client=await listener.AcceptTcpClientAsync(timeout.Token);var stream=client.GetStream();
            var request=await Wire.Receive(stream,master,timeout.Token);byte[] serverNonce=RandomNumberGenerator.GetBytes(16);
            byte[] key=Wire.SessionKey(master,Convert.FromBase64String(request.GetProperty("nonce").GetString()!),serverNonce);
            await Wire.Send(stream,new{type="hello",version=1,nonce=Convert.ToBase64String(serverNonce),width=1920,height=1080,audioBatchFrames=480},master,timeout.Token);
            sessionKey.SetResult(key);
            try{await Task.Delay(Timeout.Infinite,timeout.Token);}catch(OperationCanceledException){}
        });
        var ui=new Thread(()=> {
            using var context=new PumpContext();SynchronizationContext.SetSynchronizationContext(context);
            try {
                using var candidate=new Link(10);link=candidate;
                var connect=candidate.Connect("127.0.0.1","127.0.0.1",Convert.ToBase64String(master));
                while(!connect.IsCompleted)context.Pump(timeout.Token);connect.GetAwaiter().GetResult();
                connected.Set();Thread.Sleep(500); // Deliberately stop pumping this UI context.
                uiResult.TrySetResult(finishedProbe.IsSet&&Interlocked.Read(ref candidate.AudioPackets)>0&&candidate.IsAlive);
            }catch(Exception error){uiResult.TrySetException(error);connected.Set();}
            finally{SynchronizationContext.SetSynchronizationContext(null);}
        }){IsBackground=true,Name="EdgeLink simulated busy UI"};
        ui.Start();
        try {
            await Task.Run(()=>connected.Wait(timeout.Token));
            if(uiResult.Task.IsFaulted)return await uiResult.Task;
            byte[] key=await sessionKey.Task.WaitAsync(timeout.Token);
            var packet=new byte[977];packet[0]=2;BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(1),1);
            await audio.SendAsync(Wire.Seal(packet,key),new IPEndPoint(IPAddress.Loopback,48792),timeout.Token);
            link!.Move(7,123.25,321.5);
            var received=await mouse.ReceiveAsync(timeout.Token).AsTask().WaitAsync(TimeSpan.FromMilliseconds(300));
            byte[] position=Wire.Open(received.Buffer,key);
            if(position.Length!=29||position[0]!=1||BinaryPrimitives.ReadUInt32LittleEndian(position.AsSpan(1))!=7||
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(position.AsSpan(13)))!=123.25||
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(position.AsSpan(21)))!=321.5)return false;
            var deadline=DateTime.UtcNow.AddMilliseconds(150);
            while(Interlocked.Read(ref link.AudioPackets)==0&&DateTime.UtcNow<deadline)await Task.Delay(1,timeout.Token);
            if(Interlocked.Read(ref link.AudioPackets)>0)finishedProbe.Set();
            return await uiResult.Task.WaitAsync(timeout.Token);
        }catch(TimeoutException){return false;}
        finally{timeout.Cancel();ui.Join(2000);await server;}
    }
    private sealed class PumpContext:SynchronizationContext,IDisposable
    {
        private readonly BlockingCollection<Action> queue=new();
        private volatile bool disposed;
        public override void Post(SendOrPostCallback callback,object? state){if(!disposed)try{queue.Add(()=>callback(state));}catch(InvalidOperationException){}}
        internal void Pump(CancellationToken stop){if(queue.TryTake(out var action,50,stop))action();}
        public void Dispose(){disposed=true;queue.Dispose();}
    }
}
