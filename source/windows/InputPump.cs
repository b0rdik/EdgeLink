using System.Collections.Concurrent;

namespace EdgeLink;

// Dedicated Win32 message-only window. Never waits for WinUI layout or dialogs.
internal sealed class InputPump:IDisposable
{
    private const uint WorkMessage=0x8001,ExitMessage=0x8002;
    private readonly ConcurrentQueue<Action> work=new();
    private readonly Thread thread;
    private readonly Action<nint> raw;
    private readonly Action install,cleanup;
    private readonly Action? poll;
    private readonly ManualResetEventSlim ready=new();
    private Exception? startupError;
    private NativeMessageWindow? window;
    private int threadId,disposed;
    public nint Handle=>window?.Handle??0;
    public bool Polling {get;private set;}
    public InputPump(Action install,Action cleanup,Action<nint> raw,Action? poll=null) {
        this.install=install; this.cleanup=cleanup; this.raw=raw; this.poll=poll;
        thread=new Thread(Run) {IsBackground=true,Name="EdgeLink input",Priority=ThreadPriority.AboveNormal};
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if(startupError!=null) {thread.Join();ready.Dispose();throw new InvalidOperationException("Не удалось запустить обработку ввода.",startupError);}
    }
    private void Run() {
        threadId=Environment.CurrentManagedThreadId;
        try {
            window=new NativeMessageWindow(Dispatch); install(); ready.Set();
            int result;
            while((result=Desktop.GetMessage(out var message,0,0,0))>0) {Desktop.TranslateMessage(ref message);Desktop.DispatchMessage(ref message);}
            if(result<0)throw new System.ComponentModel.Win32Exception();
        } catch(Exception error) {startupError=error;ready.Set();}
        finally {if(window!=null)PollEvery(0);cleanup();window?.Dispose();}
    }
    private nint? Dispatch(uint message,nuint wParam,nint lParam) {
        if(message==WorkMessage) {while(work.TryDequeue(out var action))action();return 0;}
        if(message==ExitMessage) {Desktop.PostQuitMessage(0);return 0;}
        if(message==0x113&&wParam==1) {if(Polling)poll?.Invoke();return 0;}
        if(message==0xff)raw(lParam);
        return null; // DefWindowProc performs foreground WM_INPUT cleanup.
    }
    // Called on this pump's thread; ordinary WM_TIMER, no high-resolution timer.
    public void PollEvery(uint milliseconds) {
        if(milliseconds==0) {if(Polling)Native.KillTimer(Handle,1);Polling=false;return;}
        if(poll==null)throw new InvalidOperationException("No input poll callback.");
        if(Native.SetTimer(Handle,1,milliseconds,0)==0)throw new System.ComponentModel.Win32Exception();
        Polling=true;
    }
    public Task<T> Query<T>(Func<T> action) {
        if(Environment.CurrentManagedThreadId==threadId)return Task.FromResult(action());
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(Volatile.Read(ref disposed)!=0)return Task.FromException<T>(new ObjectDisposedException(nameof(InputPump)));
        work.Enqueue(()=> {try {result.TrySetResult(action());}catch(Exception error){result.TrySetException(error);}});
        if(!Desktop.PostMessage(Handle,WorkMessage,0,0))result.TrySetException(new System.ComponentModel.Win32Exception());
        return result.Task;
    }
    public void Invoke(Action action)=>Query(()=> {action();return true;}).GetAwaiter().GetResult();
    public void Dispose() {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        Desktop.PostMessage(Handle,ExitMessage,0,0);
        if(Environment.CurrentManagedThreadId!=threadId)thread.Join();ready.Dispose();
    }
}
