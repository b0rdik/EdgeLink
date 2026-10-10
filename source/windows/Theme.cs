using System.Drawing.Drawing2D;

namespace EdgeLink;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(27, 28, 30), Sidebar = Color.FromArgb(39, 40, 43), Surface = Color.FromArgb(33, 35, 38), Border = Color.FromArgb(58, 61, 65), Text = Color.FromArgb(238, 240, 242), Muted = Color.FromArgb(159, 165, 170), Mint = Color.FromArgb(89, 226, 199);
    public static Font Font(float size = 10, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Label Label(string text, float size = 10, bool bold = false, bool muted = false) => new() { Text = text, AutoSize = false, Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = muted ? Muted : Text, Margin = new Padding(0, 0, 0, 8) };
    public static void Style(Control c)
    {
        c.Font = Font(); c.ForeColor = Text; c.BackColor = Surface;
        c.Margin = new Padding(0, 4, 12, 8);
        if (c is TextBox t) { t.BorderStyle = BorderStyle.FixedSingle; t.Height = 30; }
        if (c is ComboBox cb) {
            cb.FlatStyle = FlatStyle.Flat; cb.DrawMode = DrawMode.OwnerDrawFixed; cb.ItemHeight = 26;
            cb.DrawItem += (_, e) => {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var fill = new SolidBrush(selected && cb.Enabled ? Sidebar : Surface); e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font,
                    Rectangle.Inflate(e.Bounds, -8, 0), cb.Enabled ? Text : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }
        if (c is Label label) { label.AutoSize = false; label.MaximumSize = Size.Empty; }
        if (c is CheckBox box) { box.AutoSize = false; box.FlatStyle = FlatStyle.Standard; box.Padding = new Padding(0, 4, 0, 4); }
        if (c is NumericUpDown n) { n.BorderStyle = BorderStyle.FixedSingle; }
        if (c is Button b) { b.AutoSize = false; b.MinimumSize = Size.Empty; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = Border; b.Padding = new Padding(16, 8, 16, 8); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; }
    }
    public static Button Button(string text, bool primary = false)
    {
        var b = new ThemedButton { Text = text, MinimumSize = new Size(100, 38) }; Style(b);
        if (primary) Primary(b); return b;
    }
    public static void Primary(Button b) { b.BackColor = Mint; b.ForeColor = Background; b.Font = Font(10, FontStyle.Bold); b.FlatAppearance.BorderSize = 0; }
    public static Panel Row(params Control[] controls)
    {
        var row = new ButtonRowPanel { BackColor = Surface, Margin = new Padding(0) };
        row.Controls.AddRange(controls); return row;
    }
    public static Panel Card(string title, string hint, params Control[] controls)
    {
        var card = new RoundedPanel { Padding = new Padding(20), Width = 710, Margin = new Padding(0, 0, 0, 16) };
        void Add(Control c) { c.Dock = DockStyle.None; c.Anchor = AnchorStyles.Top | AnchorStyles.Left; card.Controls.Add(c); }
        if (title.Length > 0) Add(Label(title, 15, true));
        if (hint.Length > 0) Add(Label(hint, 10, muted: true));
        foreach (var c in controls) Add(c);
        return card;
    }
    public static Panel Field(string name, Control control)
    {
        return new FieldPanel(name, control);
    }
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var p = new GraphicsPath(); radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2); float d = Math.Max(1, radius * 2);
        p.AddArc(bounds.X,bounds.Y,d,d,180,90); p.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);
        p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90); p.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
}
internal sealed class RoundedPanel : VerticalStackPanel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Accent { get; set; }
    public RoundedPanel() { DoubleBuffered = true; BackColor = Theme.Surface; ResizeRedraw = true; }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16 * DeviceDpi / 96f);
        using var fill = new SolidBrush(BackColor); e.Graphics.FillPath(fill, path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),16*DeviceDpi/96f);
        using var pen = new Pen(Accent ? Color.FromArgb(55, 91, 85) : Theme.Border); e.Graphics.DrawPath(pen,path);
    }
}
internal sealed class ThemedButton : Button
{
    private bool hovered, pressed;
    public ThemedButton() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color fillColor = BackColor;
        if (Enabled && hovered) fillColor = ControlPaint.Light(fillColor, pressed ? .05f : .10f);
        if (!Enabled) fillColor = Theme.Sidebar;
        using var shape = Theme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8 * DeviceDpi / 96f);
        using var fill = new SolidBrush(fillColor); e.Graphics.FillPath(fill, shape);
        if (FlatAppearance.BorderSize > 0) { using var pen = new Pen(Theme.Border); e.Graphics.DrawPath(pen, shape); }
        var textBounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Theme.Muted, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Theme.Mint, fillColor);
    }
}
internal sealed class ThemedCheckBox : CheckBox
{
    public ThemedCheckBox() { DoubleBuffered = true; ResizeRedraw = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int size = UiLayout.Px(this, 16), gap = UiLayout.Px(this, 10);
        var box = new RectangleF(1, (Height - size) / 2f, size, size);
        using var shape = Theme.Rounded(box, UiLayout.Px(this, 4));
        using var fill = new SolidBrush(Checked ? Enabled ? Theme.Mint : Color.FromArgb(66, 82, 78) : Theme.Sidebar);
        e.Graphics.FillPath(fill, shape);
        using var border = new Pen(Checked && Enabled ? Theme.Mint : Theme.Muted); e.Graphics.DrawPath(border, shape);
        if (Checked) {
            using var tick = new Pen(Enabled ? Theme.Background : Theme.Muted, Math.Max(1.5f, DeviceDpi / 48f));
            e.Graphics.DrawLines(tick, [new PointF(box.X + size * .23f, box.Y + size * .52f), new PointF(box.X + size * .44f, box.Y + size * .72f), new PointF(box.X + size * .79f, box.Y + size * .28f)]);
        }
        var textBounds = new Rectangle(size + gap, Padding.Top, Math.Max(1, Width - size - gap), Math.Max(1, Height - Padding.Vertical));
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, textBounds, Theme.Mint, BackColor);
    }
}
internal sealed class ConnectionHero : Control
{
    public bool Connected, Active, MacLeft = true;
    public ConnectionHero() { DoubleBuffered=true; ResizeRedraw=true; Height=162; BackColor=Theme.Surface; AccessibleName="Расположение экранов и активный компьютер"; }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, UiLayout.Px(this, 162));
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        float scale=DeviceDpi/96f; g.ScaleTransform(scale,scale); float w=Width/scale;
        using var mint=new SolidBrush(Theme.Mint); using var muted=new SolidBrush(Theme.Muted); using var text=new SolidBrush(Theme.Text);
        using var title=new Font("Segoe UI",14,FontStyle.Bold,GraphicsUnit.Pixel); using var small=new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel);
        void Monitor(float x, float cellWidth, string name, bool focused, string caption)
        {
            using var tile=Theme.Rounded(new RectangleF(x,0,cellWidth,158),12);
            using var tileFill=new SolidBrush(focused ? Color.FromArgb(43,56,55) : Color.FromArgb(39,43,46));g.FillPath(tileFill,tile);
            float center=x+cellWidth/2;
            var rect=new RectangleF(center-28,20,56,40); using var shape=Theme.Rounded(rect,4);
            using var pen=new Pen(focused ? Theme.Mint : Theme.Muted,2.5f); g.DrawPath(pen,shape);
            g.DrawLine(pen,center,60,center,69);g.DrawLine(pen,center-12,69,center+12,69);
            var t=g.MeasureString(name,title); g.DrawString(name,title,text,center-t.Width/2,82);
            using var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter };
            g.DrawString(caption,small,muted,new RectangleF(x+8,107,cellWidth-16,36),format);
        }
        float cell=(w-84)/2;
        Monitor(0,cell,MacLeft ? "Mac" : "Windows",MacLeft ? Active : Connected&&!Active,MacLeft ? "Экран слева" : "Мышь · клавиатура · наушники");
        Monitor(w-cell,cell,MacLeft ? "Windows" : "Mac",MacLeft ? Connected&&!Active : Active,MacLeft ? "Мышь · клавиатура · наушники" : "Экран справа");
        using var arrow=new Pen(Connected ? Theme.Mint : Theme.Muted,2);
        g.DrawLine(arrow,w/2-22,55,w/2+22,55); g.DrawLine(arrow,w/2+22,55,w/2+15,49); g.DrawLine(arrow,w/2+22,55,w/2+15,61);
        g.DrawLine(arrow,w/2+22,72,w/2-22,72); g.DrawLine(arrow,w/2-22,72,w/2-15,66); g.DrawLine(arrow,w/2-22,72,w/2-15,78);
        var measure=g.MeasureString("Ethernet",small); g.DrawString("Ethernet",small,muted,w/2-measure.Width/2,90);
    }
}
