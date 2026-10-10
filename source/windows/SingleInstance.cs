using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace EdgeLink;
internal static class SingleInstance
{
    [DllImport("shell32",CharSet=CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("dwmapi")] internal static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    public static void Run()
    {
        SetCurrentProcessExplicitAppUserModelID("b0rdik.EdgeLink");
        string sid=WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string id="EdgeLink-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
        using var mutex=new Mutex(false,@"Local\"+id); bool owns;
        try {owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
        string[] args=Environment.GetCommandLineArgs();string? pair=args.Length>=3&&args[1]=="--pair" ? args[2] : null;
        if(!owns)
        {
            try {
                using var pipe=new NamedPipeClientStream(".",id,PipeDirection.Out,PipeOptions.Asynchronous);
                pipe.Connect(5000);using var writer=new StreamWriter(pipe,new UTF8Encoding(false));writer.WriteLine(JsonSerializer.Serialize(pair));
            } catch(Exception e) {MessageBox.Show("EdgeLink уже запущен. Откройте его окно и используйте «Добавить Mac».\n"+e.Message,"EdgeLink");}
            return;
        }
        try {
            using var lifetime=new CancellationTokenSource();using var window=new MainWindow();_ = window.Handle;
            window.FormClosed+=(_,_)=>lifetime.Cancel();
            _ = Task.Run(async ()=>{
                while(!lifetime.IsCancellationRequested) {
                    try {
                        using var pipe=new NamedPipeServerStream(id,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                        await pipe.WaitForConnectionAsync(lifetime.Token);
                        using var reader=new StreamReader(pipe,Encoding.UTF8);
                        var text=new StringBuilder();
                        using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);readTimeout.CancelAfter(5000);
                        var character=new char[1];
                        while(await reader.ReadAsync(character,readTimeout.Token)>0 && character[0]!='\n') {if(text.Length>=8192)throw new InvalidDataException();text.Append(character[0]);}
                        string? file=JsonSerializer.Deserialize<string?>(text.ToString());
                        if(!window.IsDisposed)window.BeginInvoke(async ()=>await window.AcceptInvocation(file));
                    }catch(OperationCanceledException){}catch(IOException){}catch(JsonException){}catch(InvalidOperationException){}
                }
            });
            Application.Run(window);
        } finally {mutex.ReleaseMutex();}
    }
}
