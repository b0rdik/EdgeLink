using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace EdgeLink;

// A message-only window and its own Win32 message pump. Low-level hooks must
// never wait for the dashboard's layout, audio controls or modal dialogs.
internal sealed class InputPump : NativeWindow, IDisposable
{
    private const int WorkMessage = 0x8001, ExitMessage = 0x8002;
    private readonly ConcurrentQueue<Action> work = new();
    private readonly Thread thread;
    private readonly Action<nint> raw;
    private readonly Action install, cleanup;
    private readonly ManualResetEventSlim ready = new();
    private Exception? startupError;
    private int threadId, disposed;

    public InputPump(Action install, Action cleanup, Action<nint> raw)
    {
        this.install = install; this.cleanup = cleanup; this.raw = raw;
        thread = new Thread(Run) { IsBackground = true, Name = "EdgeLink input", Priority = ThreadPriority.AboveNormal };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        ready.Wait();
        if (startupError != null) { thread.Join(); ready.Dispose(); throw new InvalidOperationException("Не удалось запустить обработку ввода.", startupError); }
    }
    private void Run()
    {
        threadId = Environment.CurrentManagedThreadId;
        try
        {
            CreateHandle(new CreateParams { Caption = "EdgeLink input", Parent = new nint(-3) });
            install(); ready.Set();
            Application.Run();
        }
        catch (Exception error) { startupError = error; ready.Set(); }
        finally { cleanup(); if (Handle != 0) DestroyHandle(); }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WorkMessage) {
            while (work.TryDequeue(out var action)) action();
            return;
        }
        if (message.Msg == ExitMessage) { Application.ExitThread(); return; }
        if (message.Msg == 0xff) raw(message.LParam);
        // DefWindowProc performs the required foreground WM_INPUT cleanup.
        base.WndProc(ref message);
    }
    public Task<T> Query<T>(Func<T> action)
    {
        if (Environment.CurrentManagedThreadId == threadId) return Task.FromResult(action());
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref disposed) != 0) return Task.FromException<T>(new ObjectDisposedException(nameof(InputPump)));
        work.Enqueue(() => { try { result.TrySetResult(action()); } catch (Exception error) { result.TrySetException(error); } });
        if (!PostMessage(Handle, WorkMessage, 0, 0)) result.TrySetException(new System.ComponentModel.Win32Exception());
        return result.Task;
    }
    public void Invoke(Action action) { Query(() => { action(); return true; }).GetAwaiter().GetResult(); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        PostMessage(Handle, ExitMessage, 0, 0);
        if (Environment.CurrentManagedThreadId != threadId) thread.Join();
        ready.Dispose();
    }
    [DllImport("user32", SetLastError = true)] private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
}
