using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace EdgeLink;

public sealed partial class MainWindow
{
    private readonly Windows.UI.ViewManagement.UISettings uiSettings=new();
    private bool CanAnimateUI()=>Desktop.IsWindowVisible(Handle)&&!Desktop.IsIconic(Handle)&&uiSettings.AnimationsEnabled;
    private void MoveNavigationIndicator(bool animate) {
        if(navigation.SelectedItem is not NavigationViewItem item||item.ActualWidth==0)return;
        var bounds=item.TransformToVisual(navigationStrip).TransformBounds(new Windows.Foundation.Rect(0,0,item.ActualWidth,item.ActualHeight));
        var target=new Vector3((float)(bounds.Left+(bounds.Width-navigationIndicator.Width)/2),0,0);
        var visual=ElementCompositionPreview.GetElementVisual(navigationIndicator);
        if(!animate||navigationIndicator.Opacity==0||!CanAnimateUI()) {
            visual.StopAnimation("Translation");visual.Properties.InsertVector3("Translation",target);
        } else {
            var animation=visual.Compositor.CreateVector3KeyFrameAnimation();animation.Duration=TimeSpan.FromMilliseconds(160);
            animation.InsertExpressionKeyFrame(0,"this.StartingValue");
            animation.InsertKeyFrame(1,target,visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f,0),new Vector2(0,1)));
            visual.StartAnimation("Translation",animation);
        }
        navigationIndicator.Opacity=1;
    }
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
