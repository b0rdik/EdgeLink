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
    internal static bool Acquire() {
        string sid=WindowsIdentity.GetCurrent().User?.Value??Environment.UserName;
        id="EdgeLink-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
        mutex=new Mutex(false,@"Local\"+id);
        try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
        if(owns)return true;
        string[] args=Program.Arguments;string? pair=args.Length>=3&&args[1]=="--pair"?args[2]:null;
        try {using var pipe=new NamedPipeClientStream(".",id,PipeDirection.Out,PipeOptions.Asynchronous);pipe.Connect(5000);using var writer=new StreamWriter(pipe,new UTF8Encoding(false));writer.WriteLine(JsonSerializer.Serialize(pair));}
        catch(Exception e){Desktop.MessageBox(0,"EdgeLink уже запущен. Откройте его окно и используйте «Добавить Mac».\n"+e.Message,"EdgeLink",0);}
        Release();return false;
    }
    internal static void Release(){if(owns){mutex!.ReleaseMutex();owns=false;}mutex?.Dispose();mutex=null;}
    internal static void StartServer(MainWindow window,CancellationToken cancellation) {
        _=Task.Run(async ()=> {
            while(!cancellation.IsCancellationRequested)try {
                using var pipe=new NamedPipeServerStream(id,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);using var reader=new StreamReader(pipe,Encoding.UTF8);
                var text=new StringBuilder();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(5000);var character=new char[1];
                while(await reader.ReadAsync(character,timeout.Token)>0&&character[0]!='\n'){if(text.Length>=8192)throw new InvalidDataException();text.Append(character[0]);}
                string? file=JsonSerializer.Deserialize<string?>(text.ToString());window.BeginInvoke(async ()=>await window.AcceptInvocation(file));
            }catch(OperationCanceledException){}catch(IOException){}catch(JsonException){}catch(InvalidOperationException){}
        },cancellation);
    }
}
