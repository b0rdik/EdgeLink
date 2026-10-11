using System.Drawing;

namespace EdgeLink;

// Sampling stays outside Windows' synchronous mouse hook chain.
internal sealed class InputEdge
{
    private Native.Point previous;
    private bool initialized, inwardRequired;
    private long rearmAt;
    public void Reset(Native.Point point, bool requireInward = false, long notBefore = 0)
    {
        previous = point; initialized = true; inwardRequired = requireInward; rearmAt = notBefore;
    }
    public bool Observe(Native.Point point, Rectangle bounds, string side, bool blocked, long now)
    {
        bool moved = initialized && (point.X != previous.X || point.Y != previous.Y);
        previous = point; initialized = true;
        bool left = side == "left";
        if (left ? point.X > bounds.Left + 5 : point.X < bounds.Right - 6) inwardRequired = false;
        bool atEdge = left ? point.X <= bounds.Left : point.X >= bounds.Right - 1;
        return moved && atEdge && point.Y >= bounds.Top && point.Y < bounds.Bottom &&
            !blocked && !inwardRequired && now >= rearmAt;
    }
}
