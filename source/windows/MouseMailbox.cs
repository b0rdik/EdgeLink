using System.Threading.Channels;

namespace EdgeLink;

internal readonly record struct MousePosition(uint Epoch, ulong Sequence, double X, double Y);

// Keep one latest position and one wake-up. No polling while the mouse is on PC.
internal sealed class MouseMailbox
{
    private readonly object gate = new();
    private readonly Channel<byte> changed = Channel.CreateBounded<byte>(new BoundedChannelOptions(1) {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite, AllowSynchronousContinuations = false
    });
    private MousePosition? pending;
    private ulong sequence;
    public ulong Sequence { get { lock (gate) return sequence; } }
    public void Publish(uint epoch, double x, double y)
    {
        lock (gate) {
            bool wake = pending == null;
            pending = new MousePosition(epoch, ++sequence, x, y);
            if (wake) changed.Writer.TryWrite(0);
        }
    }
    public async ValueTask Wait(CancellationToken token) { _ = await changed.Reader.ReadAsync(token); }
    public MousePosition? TakeLatest() { lock (gate) { var value = pending; pending = null; return value; } }
}
