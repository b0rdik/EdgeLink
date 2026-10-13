using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using System.Runtime.InteropServices.WindowsRuntime;

namespace EdgeLink;
internal static class LayoutChecks
{
    internal static async Task<int> Run(MainWindow window) {
        string folder=Path.Combine(Path.GetTempPath(),"EdgeLink-layout-checks");Directory.CreateDirectory(folder);
        var cases=new List<object>();var failures=new List<string>();
        try{await window.VerifyLayouts(folder,cases,failures);}catch(Exception e){failures.Add(e.ToString());}
        File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(new {Framework="WinUI 3",Version=Updates.CurrentVersion,Dpi=Desktop.GetDpiForWindow(window.Handle),Cases=cases,Failures=failures},new JsonSerializerOptions {WriteIndented=true}));
        return failures.Count==0?0:1;
    }
}
public sealed partial class MainWindow
{
    internal async Task VerifyLayouts(string folder,List<object> cases,List<string> failures) {
        (int width,int height)[] sizes=[(664,521),(800,600),(1060,760),(1400,900)];
        automatic.CheckedValue=false;connectionDiagnostics.IsExpanded=true;
        // Exercise wrapping of live values without opening sockets or loading credentials.
        stats.Text="RTT: 123 мс · звук: 12,34 Мбит/с (IPv4)\nБуфер: 123,4 мс · запас: 120,0 мс · прерывания: 12345 · пакеты: 1234567890";
        for(int cycle=0;cycle<3;cycle++)foreach(var size in sizes) {
            double scale=Desktop.GetDpiForWindow(Handle)/96.0;
            AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(size.width*scale),(int)(size.height*scale)));
            await Settle();
            SelectPage(2);await Settle();await CheckLayout($"{cycle}-{size.width}x{size.height}-settings",folder,cases,failures,cycle==0);
            viewport.ChangeView(null,viewport.ScrollableHeight,null,true);await Settle();
            double lastBottom=pages[2].Children.Last().TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0,0,((FrameworkElement)pages[2].Children.Last()).ActualWidth,((FrameworkElement)pages[2].Children.Last()).ActualHeight)).Bottom;
            if(lastBottom>viewport.ActualHeight+1)failures.Add($"Settings bottom unreachable at {size}: {lastBottom}/{viewport.ActualHeight}");
            cases.Add(new {Name=$"{cycle}-{size.width}x{size.height}-scroll",ScrollHeight=viewport.ScrollableHeight,LastBottom=lastBottom,ViewportHeight=viewport.ActualHeight});
            for(int stage=0;stage<4;stage++){setupStage=stage;UpdateWizard();SelectPage(1);await Settle();await CheckLayout($"{cycle}-{size.width}x{size.height}-wizard-{stage}",folder,cases,failures,cycle==0&&size.width==800);}
            side.SelectedIndex=cycle%2;SelectPage(0);status.Text="Не удалось подключиться: длинное сообщение для проверки переноса текста и доступности кнопки. Проверьте Ethernet, разрешения Mac и Windows Firewall.";UpdateWizard();await Settle();
            if(wizardSide.SelectedIndex!=side.SelectedIndex)failures.Add("Wizard side selector did not mirror settings");
            await CheckLayout($"{cycle}-{size.width}x{size.height}-home",folder,cases,failures,cycle==0);
            viewport.ChangeView(null,viewport.ScrollableHeight,null,true);await Settle();
            var hint=(FrameworkElement)pages[0].Children.Last();
            double homeBottom=hint.TransformToVisual(viewport).TransformBounds(new Windows.Foundation.Rect(0,0,hint.ActualWidth,hint.ActualHeight)).Bottom;
            if(homeBottom>viewport.ActualHeight+1)failures.Add($"Home bottom unreachable at {size}: {homeBottom}/{viewport.ActualHeight}");
            if(!Descendants(pages[0]).Contains(stats)||Descendants(pages[2]).Contains(stats))failures.Add("Live statistics are not on Home only");
            cases.Add(new {Name=$"{cycle}-{size.width}x{size.height}-home-scroll",LastBottom=homeBottom,ViewportHeight=viewport.ActualHeight});
            // Queue multiple real NavigationView selections before dispatching page changes.
            // Only the final selection may replace the page or restore its scroll offset.
            var navBefore=navigation.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0,0,navigation.ActualWidth,navigation.ActualHeight));
            foreach(int index in new[]{2,0,1,0,2,1})navigation.SelectedItem=navigationItems[index];
            await Settle();
            if(selectedPage!=1||!ReferenceEquals(viewport.Content,pages[1]))failures.Add("Queued navigation did not show the last selected page");
            var navAfter=navigation.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0,0,navigation.ActualWidth,navigation.ActualHeight));
            if(navBefore!=navAfter)failures.Add("Navigation moved after page changes");
            await CheckLayout($"{cycle}-{size.width}x{size.height}-rapid-navigation",folder,cases,failures,false);
        }
        connectionDiagnostics.IsExpanded=false;side.SelectedIndex=0;SelectPage(0);
        AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800,740));
        status.Text="Подключите MacBook по Ethernet, чтобы начать работу.";UpdateWizard();await Settle();
        await CheckLayout("home-default-800x740",folder,cases,failures,true);
        // Theme changes exercise real WinUI resource resolution and native control rendering.
        foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark}) {
            Root.RequestedTheme=theme;await Settle();
            await CheckLayout("home-"+theme.ToString().ToLowerInvariant()+"-800x740",folder,cases,failures,true);
        }
        Root.RequestedTheme=ElementTheme.Default;
    }
    internal async Task Settle(){Root.UpdateLayout();await Task.Delay(60);Root.UpdateLayout();}
    private async Task CheckLayout(string name,string folder,List<object> cases,List<string> failures,bool snapshot) {
        if(viewport.ScrollableWidth>.5)failures.Add(name+": horizontal overflow");
        if(navigation.SelectedItem is not NavigationViewItem selected||(int)selected.Tag!=selectedPage||!navigationItems[selectedPage].IsSelected)failures.Add(name+": navigation selection does not match the page");
        foreach(var element in Descendants(pages[selectedPage]).OfType<FrameworkElement>()) {
            if(element.Visibility!=Visibility.Visible||element.ActualWidth==0||element.ActualHeight==0)continue;
            var rect=element.TransformToVisual(pages[selectedPage]).TransformBounds(new Windows.Foundation.Rect(0,0,element.ActualWidth,element.ActualHeight));
            if(rect.Left<-.5||rect.Right>pages[selectedPage].ActualWidth+.5)failures.Add(name+": horizontal bounds "+element.GetType().Name);
            if(element is TextBlock label&&label.IsTextTrimmed)failures.Add(name+": trimmed text "+label.Text);
        }
        cases.Add(new {Name=name,Width=Root.ActualWidth,Height=Root.ActualHeight,Page=selectedPage,ScrollHeight=viewport.ScrollableHeight});
        if(snapshot) {
            viewport.ChangeView(null,0,null,true);await Settle();await Task.Delay(250);
            var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);var buffer=await bitmap.GetPixelsAsync();CryptographicBuffer.CopyToByteArray(buffer,out byte[] pixels);
            using var file=File.Create(Path.Combine(folder,name+".png"));using var stream=file.AsRandomAccessStream();
            var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is UIElement element&&element.Visibility==Visibility.Collapsed)continue;
            yield return child;foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
}
