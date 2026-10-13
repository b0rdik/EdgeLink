using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Principal;

namespace EdgeLink;
internal static class SingleInstance
{
    private static Mutex? mutex;
    private static bool owns;
    private static string id="";
    private static string Identifier() {
        string sid=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
        return "EdgeLink-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
    }
    internal static bool Acquire() {
        id=Identifier();
        mutex=new Mutex(false,@"Local\"+id);
        try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
        if(owns)return true;
        string[] args=Program.Arguments;string? pair=args.Length>=3&&args[1]=="--pair"?args[2]:null;
        if(Program.Has("--start-hidden")&&pair==null){Release();return false;}
        try {using var pipe=new NamedPipeClientStream(".",id,PipeDirection.Out,PipeOptions.Asynchronous);pipe.Connect(5000);using var writer=new StreamWriter(pipe,new UTF8Encoding(false));writer.WriteLine(JsonSerializer.Serialize(pair));}
        catch(Exception e){Desktop.MessageBox(0,"EdgeLink уже запущен. Откройте его окно и используйте «Добавить Mac».\n"+e.Message,"EdgeLink",0);}
        Release();return false;
    }
    internal static void Release(){if(owns){mutex!.ReleaseMutex();owns=false;}mutex?.Dispose();mutex=null;}
    internal static async Task<int> SaveStatus(string path) {
        try {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var pipe=new NamedPipeClientStream(".",Identifier(),PipeDirection.InOut,PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,leaveOpen:true);
            using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
            await writer.WriteLineAsync("{\"type\":\"status\"}".AsMemory(),timeout.Token).ConfigureAwait(false);await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
            string response=await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)??throw new IOException("No status response.");
            using var document=JsonDocument.Parse(response);
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(document.RootElement,new JsonSerializerOptions {WriteIndented=true}),timeout.Token).ConfigureAwait(false);
            return 0;
        } catch(IOException) {return 1;} catch(OperationCanceledException) {return 1;} catch(JsonException) {return 1;} catch(UnauthorizedAccessException) {return 1;}
    }
    internal static void StartServer(MainWindow window,CancellationToken cancellation) {
        _=Task.Run(async ()=> {
            while(!cancellation.IsCancellationRequested)try {
                using var pipe=new NamedPipeServerStream(id,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);using var reader=new StreamReader(pipe,Encoding.UTF8);
                var text=new StringBuilder();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(5000);var character=new char[1];
                while(await reader.ReadAsync(character,timeout.Token)>0&&character[0]!='\n'){if(text.Length>=8192)throw new InvalidDataException();text.Append(character[0]);}
                using var request=JsonDocument.Parse(text.ToString());
                if(request.RootElement.ValueKind==JsonValueKind.Object&&request.RootElement.TryGetProperty("type",out var type)&&type.GetString()=="status") {
                    var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.BeginInvoke(async ()=> {try{completion.TrySetResult(await window.GetStatusJson());}catch(Exception error){completion.TrySetException(error);}});
                    string response=await completion.Task.WaitAsync(timeout.Token);
                    using var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,leaveOpen:true);
                    await writer.WriteLineAsync(response.AsMemory(),timeout.Token);await writer.FlushAsync(timeout.Token);
                } else if(request.RootElement.ValueKind is JsonValueKind.String or JsonValueKind.Null) {
                    string? file=request.RootElement.GetString();window.BeginInvoke(async ()=>await window.AcceptInvocation(file));
                }
            }catch(OperationCanceledException){}catch(IOException){}catch(JsonException){}catch(InvalidOperationException){}
        },cancellation);
    }
}
