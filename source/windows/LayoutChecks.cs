using System.Text.Json;

namespace EdgeLink;

// Native Windows regression checks: no input hook, network, audio initialization,
// Credential Manager access or settings writes. Uses the real controls and OS fonts.
internal static class LayoutChecks
{
    public static int Run()
    {
        int result = 1;
        using var window = new MainWindow(layoutCheck: true);
        window.Shown += (_, _) => window.BeginInvoke(() =>
        {
            string output = Path.Combine(Path.GetTempPath(), "EdgeLink-layout-checks");
            Directory.CreateDirectory(output);
            try
            {
                var report = window.VerifyLayouts(output);
                File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                result = report.Failures.Count == 0 ? 0 : 1;
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); }
            finally { window.Close(); }
        });
        Application.Run(window); return result;
    }
}

internal sealed record LayoutReport(int Dpi, int Cases, List<string> Failures);

internal sealed partial class MainWindow
{
    internal LayoutReport VerifyLayouts(string output)
    {
        var failures = new List<string>(); int cases = 0;
        MinimumSize = Size.Empty;
        void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        void Validate(Control parent, string scenario)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                string name = scenario + "/" + child.GetType().Name;
                Check(child.Left >= 0 && child.Right <= parent.ClientSize.Width, name + ": horizontal clipping");
                if (parent is VerticalStackPanel stack && stack.AutoScroll)
                    Check(child.Bottom - stack.AutoScrollPosition.Y <= stack.AutoScrollMinSize.Height, name + ": unreachable by scrolling");
                else Check(child.Top >= 0 && child.Bottom <= parent.ClientSize.Height, name + ": vertical clipping");
                if (child is Label label && label.Text.Length > 0)
                {
                    using var g = label.CreateGraphics();
                    int height = TextRenderer.MeasureText(g, label.Text, label.Font, new Size(label.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
                    Check(label.Height >= height && label.Width > 0, name + ": truncated label");
                }
                if (child is TextBox or ComboBox or NumericUpDown or Button or CheckBox)
                    Check(child.Width > 0 && child.Height > 0, name + ": empty interactive control");
                Validate(child, name);
            }
        }
        void Snapshot(string scenario)
        {
            PerformLayout(); foreach (var page in pages) if (page.Visible) page.PerformLayout();
            Application.DoEvents(); Validate(this, scenario); cases++;
        }
        foreach (var size in new[] { new Size(800,600), new Size(1060,760), new Size(1400,900) })
        {
            ClientSize = new Size(UiLayout.Px(this,size.Width),UiLayout.Px(this,size.Height));
            for (int cycle=0;cycle<3;cycle++)
            {
                SelectPage(2); automatic.Checked=false;
                Check(device.Parent is FieldPanel && jitter.Parent is FieldPanel, "Settings audio fields were disposed during navigation");
                Snapshot($"{size.Width}x{size.Height}-settings-{cycle}");
                SelectPage(1);
                Check(device.Parent==setupStages[1].Controls[2] && side.Parent==setupStages[1].Controls[3], "Wizard selectors failed to return from settings");
                for(int stage=0;stage<4;stage++) { setupStage=stage; UpdateWizard(); Snapshot($"{size.Width}x{size.Height}-wizard-{stage}-{cycle}"); }
                SelectPage(0);
                status.Text="Не удалось подключиться: проверьте Ethernet, разрешения Mac и настройки firewall. " + string.Join(" ",Enumerable.Repeat("Подробное сообщение соединения.",12));
                Snapshot($"{size.Width}x{size.Height}-home-long-status-{cycle}");
            }
        }
        ClientSize=new Size(UiLayout.Px(this,1060),UiLayout.Px(this,760)); SelectPage(1); setupStage=0; UpdateWizard(); Application.DoEvents();
        using (var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(Path.Combine(output,"setup.png")); }
        SelectPage(2);Application.DoEvents();
        var settings=pages[2]; settings.AutoScrollPosition=new Point(0,int.MaxValue);Application.DoEvents();
        var last=settings.Controls.Cast<Control>().Last(c=>c.Visible);
        Check(last.Bottom<=settings.ClientSize.Height, "The last settings card cannot be reached by scrolling");
        return new LayoutReport(DeviceDpi,cases,failures);
    }
}
