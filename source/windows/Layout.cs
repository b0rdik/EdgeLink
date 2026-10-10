namespace EdgeLink;

internal static class UiLayout
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    public static int ScrollbarWidth(Control control) => GetSystemMetricsForDpi(2, (uint)control.DeviceDpi);
    public static int Px(Control control, int value) => (int)Math.Ceiling(value * control.DeviceDpi / 96d);

    public static int TextHeight(Control control, string text, int width)
    {
        if (text.Length == 0) return 0;
        var proposed = new Size(Math.Max(1, width), int.MaxValue);
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
        if (control.IsHandleCreated)
        {
            using var graphics = control.CreateGraphics();
            return TextRenderer.MeasureText(graphics, text, control.Font, proposed, flags).Height + Px(control, 2);
        }
        return TextRenderer.MeasureText(text, control.Font, proposed, flags).Height + Px(control, 2);
    }

    public static int Height(Control control, int width)
    {
        width = Math.Max(1, width);
        if (control is Label) return TextHeight(control, control.Text, width);
        if (control is PictureBox) return Px(control, 48);
        if (control is CheckBox)
            return Math.Max(Px(control, 30), TextHeight(control, control.Text, width - Px(control, 26)) + control.Padding.Vertical);
        return Math.Max(1, control.GetPreferredSize(new Size(width, 0)).Height);
    }
}

// The viewport owns width. Containers measure height for that width, without AutoSize
// feeding a preferred width back into a FlowLayoutPanel on the next layout pass.
internal class VerticalStackPanel : Panel
{
    private bool layingOut;
    public VerticalStackPanel() { AutoSize = false; DoubleBuffered = true; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = Math.Max(1, proposedSize.Width > 0 ? proposedSize.Width : Width);
        int height = Padding.Vertical;
        foreach (Control child in Controls)
            if (child.Visible)
                height += child.Margin.Vertical + UiLayout.Height(child, width - Padding.Horizontal - child.Margin.Horizontal);
        return new Size(width, height);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (layingOut) return;
        layingOut = true;
        try
        {
            base.OnLayout(e);
            // Reserve scrollbar space even while it is hidden, so toggling it cannot
            // change line wrapping and make the scrollbar repeatedly appear/disappear.
            int scrollbar = AutoScroll ? UiLayout.ScrollbarWidth(this) : 0;
            int viewport = ClientSize.Width + (AutoScroll && VerticalScroll.Visible ? scrollbar : 0);
            int width = Math.Max(1, viewport - Padding.Horizontal - scrollbar);
            var rows = new List<(Control child, int width, int height)>();
            int total = Padding.Vertical;
            foreach (Control child in Controls)
            {
                if (!child.Visible) continue;
                int childWidth = Math.Max(1, width - child.Margin.Horizontal);
                int height = UiLayout.Height(child, childWidth);
                rows.Add((child, childWidth, height));
                total += height + child.Margin.Vertical;
            }
            if (AutoScroll && AutoScrollMinSize != new Size(0, total))
            {
                AutoScrollMinSize = new Size(0, total);
                // Setting the extent requests a nested layout, which our guard skips.
                // Let ScrollableControl update its range before applying the offset.
                base.OnLayout(e);
            }
            int y = Padding.Top + (AutoScroll ? AutoScrollPosition.Y : 0);
            foreach (var row in rows)
            {
                y += row.child.Margin.Top;
                row.child.SetBounds(Padding.Left + row.child.Margin.Left, y, row.width, row.height);
                y += row.height + row.child.Margin.Bottom;
            }
        }
        finally { layingOut = false; }
    }

    private void ChildChanged(object? sender, EventArgs e)
    {
        if (layingOut || IsDisposed) return;
        PerformLayout(); Parent?.PerformLayout();
    }
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not { } child) return;
        child.TextChanged += ChildChanged; child.FontChanged += ChildChanged;
        child.VisibleChanged += ChildChanged; child.SizeChanged += ChildChanged;
        PerformLayout();
    }
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is { } child)
        {
            child.TextChanged -= ChildChanged; child.FontChanged -= ChildChanged;
            child.VisibleChanged -= ChildChanged; child.SizeChanged -= ChildChanged;
        }
        base.OnControlRemoved(e);
    }
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) PerformLayout();
    }
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e); PerformLayout();
    }
}

internal sealed class ButtonRowPanel : Panel
{
    private bool layingOut;
    public ButtonRowPanel() { AutoSize = false; }
    private int Arrange(int width, bool apply)
    {
        int x = 0, y = 0, rowHeight = 0;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            int available = Math.Max(1, width - child.Margin.Horizontal);
            var preferred = child.GetPreferredSize(new Size(available, 0));
            int w = Math.Min(available, Math.Max(UiLayout.Px(this, 100), preferred.Width));
            int h = Math.Max(UiLayout.Px(this, 38), preferred.Height);
            int outerWidth = w + child.Margin.Horizontal, outerHeight = h + child.Margin.Vertical;
            if (x > 0 && x + outerWidth > width) { y += rowHeight; x = 0; rowHeight = 0; }
            if (apply) child.SetBounds(x + child.Margin.Left, y + child.Margin.Top, w, h);
            x += outerWidth; rowHeight = Math.Max(rowHeight, outerHeight);
        }
        return y + rowHeight;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = Math.Max(1, proposedSize.Width > 0 ? proposedSize.Width : Width);
        return new Size(width, Arrange(width, false));
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (layingOut) return;
        layingOut = true;
        try { base.OnLayout(e); Arrange(ClientSize.Width, true); }
        finally { layingOut = false; }
    }
    private void ChildChanged(object? sender, EventArgs e)
    {
        if (layingOut || IsDisposed) return;
        PerformLayout(); Parent?.PerformLayout();
    }
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not { } child) return;
        child.TextChanged += ChildChanged; child.FontChanged += ChildChanged; child.VisibleChanged += ChildChanged;
    }
    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is { } child) { child.TextChanged -= ChildChanged; child.FontChanged -= ChildChanged; child.VisibleChanged -= ChildChanged; }
        base.OnControlRemoved(e);
    }
}

internal sealed class FieldPanel : Panel
{
    private readonly Label caption;
    private Control editor;
    public FieldPanel(string name, Control control)
    {
        AutoSize = false; BackColor = Theme.Surface; Margin = Padding.Empty;
        caption = Theme.Label(name, muted: true); caption.Margin = Padding.Empty;
        Controls.Add(caption); editor = control; Attach(control);
    }
    public void Attach(Control control)
    {
        editor = control; control.Dock = DockStyle.None; control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        control.Margin = Padding.Empty; Controls.Add(control); PerformLayout(); Parent?.PerformLayout();
    }
    private int Arrange(int width, bool apply)
    {
        width = Math.Max(1, width);
        int gap = UiLayout.Px(this, 10), bottom = UiLayout.Px(this, 12);
        bool vertical = width < UiLayout.Px(this, 520);
        int captionWidth = vertical ? width : UiLayout.Px(this, 165);
        int editorWidth = Math.Max(1, vertical ? width : width - captionWidth - gap);
        int captionHeight = UiLayout.TextHeight(caption, caption.Text, captionWidth);
        int editorHeight = UiLayout.Height(editor, editorWidth);
        int editorY = vertical ? captionHeight + UiLayout.Px(this, 6) : 0;
        if (apply)
        {
            caption.SetBounds(0, vertical ? 0 : Math.Max(0, (editorHeight - captionHeight) / 2), captionWidth, captionHeight);
            if (editor.Parent == this) editor.SetBounds(vertical ? 0 : captionWidth + gap, editorY, editorWidth, editorHeight);
        }
        return (vertical ? editorY + editorHeight : Math.Max(editorHeight, captionHeight)) + bottom;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = Math.Max(1, proposedSize.Width > 0 ? proposedSize.Width : Width);
        return new Size(width, Arrange(width, false));
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (caption != null && editor != null) Arrange(ClientSize.Width, true); }
}
