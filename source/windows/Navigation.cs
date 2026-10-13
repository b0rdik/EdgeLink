using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace EdgeLink;

public sealed partial class MainWindow
{
    private bool CanAnimateUI()=>Desktop.IsWindowVisible(Handle)&&!Desktop.IsIconic(Handle)&&new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private void AnimatePage(UIElement page) {
        var visual=ElementCompositionPreview.GetElementVisual(page);visual.StopAnimation("Opacity");visual.Opacity=1;
        if(!CanAnimateUI())return;
        var animation=visual.Compositor.CreateScalarKeyFrameAnimation();animation.Duration=TimeSpan.FromMilliseconds(140);
        animation.InsertKeyFrame(0,0);animation.InsertKeyFrame(1,1);visual.StartAnimation("Opacity",animation);
    }
    private void AnimateStatusIcon(FontIcon icon) {
        if(!CanAnimateUI())return;
        var visual=ElementCompositionPreview.GetElementVisual(icon);visual.CenterPoint=new Vector3((float)icon.ActualWidth/2,(float)icon.ActualHeight/2,0);
        var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.Duration=TimeSpan.FromMilliseconds(180);
        animation.InsertKeyFrame(0,new Vector3(.88f,.88f,1));animation.InsertKeyFrame(1,Vector3.One);visual.StartAnimation("Scale",animation);
    }
}
