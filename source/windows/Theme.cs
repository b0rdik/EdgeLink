using System.Drawing.Drawing2D;

namespace EdgeLink;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(12, 17, 27), Sidebar = Color.FromArgb(16, 23, 35), Surface = Color.FromArgb(23, 32, 46), Border = Color.FromArgb(43, 57, 74), Text = Color.FromArgb(238, 243, 249), Muted = Color.FromArgb(157, 175, 193), Mint = Color.FromArgb(91, 224, 197);
    public static Font Font(float size = 10, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Label Label(string text, float size = 10, bool bold = false, bool muted = false) => new() { Text = text, AutoSize = true, Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = muted ? Muted : Text, Margin = new Padding(0, 0, 0, 8), MaximumSize = new Size(620, 0) };
    public static void Style(Control c)
    {
        c.Font = Font(); c.ForeColor = Text; c.BackColor = Surface;
        c.Margin = new Padding(0, 4, 12, 8);
        if (c is TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.Height = 30; }
        if (c is ComboBox cb) { cb.FlatStyle = FlatStyle.Flat; cb.Height = 32; }
        if (c is CheckBox box) { box.FlatStyle = FlatStyle.Standard; box.Padding = new Padding(0, 4, 0, 4); }
        if (c is NumericUpDown n) { n.BorderStyle = BorderStyle.FixedSingle; }
        if (c is Button b) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = Border; b.Padding = new Padding(10, 6, 10, 6); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
            b.SizeChanged += (_, _) => { if(b.Width>2 && b.Height>2) {using var path=Rounded(new RectangleF(0,0,b.Width,b.Height),7*b.DeviceDpi/96f);var previous=b.Region;b.Region=new Region(path);previous?.Dispose();} }; }
    }
    public static Button Button(string text, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 38) }; Style(b);
        if (primary) Primary(b); return b;
    }
    public static void Primary(Button b) { b.BackColor = Mint; b.ForeColor = Background; b.Font = Font(10, FontStyle.Bold); b.FlatAppearance.BorderSize = 0; }
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Surface, Margin = new Padding(0), Width = 640 };
        row.Controls.AddRange(controls); return row;
    }
    public static Panel Card(string title, string hint, params Control[] controls)
    {
        var card = new RoundedPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(22), Width = 710, Margin = new Padding(0, 0, 0, 16) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Surface, Margin = new Padding(0), Padding = new Padding(0) };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control c) { c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; stack.Controls.Add(c, 0, stack.RowCount++); }
        if (title.Length > 0) Add(Label(title, 15, true));
        if (hint.Length > 0) Add(Label(hint, 10, muted: true));
        foreach (var c in controls) Add(c);
        stack.SizeChanged += (_, _) => { foreach (Control c in stack.Controls) if (c is Label l) l.MaximumSize = new Size(Math.Max(100, stack.ClientSize.Width - 12), 0); };
        card.Controls.Add(stack); return card;
    }
    public static Panel Field(string name, Control control)
    {
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Surface, Margin = new Padding(0), Width = 630 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var label = Label(name, muted: true); label.Margin = new Padding(0, 9, 10, 8); label.MaximumSize = new Size(150, 0);
        table.Controls.Add(label,0,0); table.Controls.Add(control,1,0); control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        return table;
    }
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(bounds.X,bounds.Y,d,d,180,90); p.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);
        p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90); p.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
}
internal sealed class RoundedPanel : Panel
{
    public RoundedPanel() { DoubleBuffered = true; BackColor = Theme.Surface; }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); if(Width<2||Height<2)return;
        using var path=Theme.Rounded(new RectangleF(0,0,Width,Height),16*DeviceDpi/96f);var previous=Region;Region=new Region(path);previous?.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),16*DeviceDpi/96f);
        using var pen = new Pen(Theme.Border); e.Graphics.DrawPath(pen,path);
    }
}
internal sealed class ConnectionHero : Control
{
    public bool Connected, Active, MacLeft = true;
    public ConnectionHero() { DoubleBuffered=true; Height=190; BackColor=Theme.Surface; AccessibleName="Расположение экранов и активный компьютер"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        float scale=DeviceDpi/96f; g.ScaleTransform(scale,scale); float w=Width/scale;
        using var mint=new SolidBrush(Theme.Mint); using var muted=new SolidBrush(Theme.Muted); using var text=new SolidBrush(Theme.Text);
        using var title=new Font("Segoe UI",14,FontStyle.Bold,GraphicsUnit.Pixel); using var small=new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel);
        void Monitor(float x, string name, bool focused, string caption)
        {
            var rect=new RectangleF(x,20,120,72); using var shape=Theme.Rounded(rect,7);
            using var pen=new Pen(focused ? Theme.Mint : Theme.Muted,2.5f); g.DrawPath(pen,shape);
            g.DrawLine(pen,x+60,92,x+60,107); g.DrawLine(pen,x+37,107,x+83,107);
            var t=g.MeasureString(name,title); g.DrawString(name,title,text,x+60-t.Width/2,120);
            t=g.MeasureString(caption,small); g.DrawString(caption,small,muted,x+60-t.Width/2,145);
        }
        float left=w*.22f-60,right=w*.78f-60;
        Monitor(left,MacLeft ? "Mac" : "Windows",MacLeft ? Active : Connected&&!Active,MacLeft ? "Мышь и клавиатура" : "Наушники подключены здесь");
        Monitor(right,MacLeft ? "Windows" : "Mac",MacLeft ? Connected&&!Active : Active,MacLeft ? "Наушники подключены здесь" : "Мышь и клавиатура");
        using var arrow=new Pen(Connected ? Theme.Mint : Theme.Muted,2);
        g.DrawLine(arrow,w/2-22,55,w/2+22,55); g.DrawLine(arrow,w/2+22,55,w/2+15,49); g.DrawLine(arrow,w/2+22,55,w/2+15,61);
        g.DrawLine(arrow,w/2+22,72,w/2-22,72); g.DrawLine(arrow,w/2-22,72,w/2-15,66); g.DrawLine(arrow,w/2-22,72,w/2-15,78);
        var measure=g.MeasureString("Ethernet",small); g.DrawString("Ethernet",small,muted,w/2-measure.Width/2,90);
    }
}
