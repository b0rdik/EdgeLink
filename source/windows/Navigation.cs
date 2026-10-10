using System.Numerics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace EdgeLink;

public sealed partial class MainWindow
{
    private void MoveNavigationIndicator(bool animate) {
        if(navigation.SelectedItem is not NavigationViewItem item||item.ActualWidth==0)return;
        var bounds=item.TransformToVisual(navigationStrip).TransformBounds(new Windows.Foundation.Rect(0,0,item.ActualWidth,item.ActualHeight));
        var target=new Vector3((float)(bounds.Left+(bounds.Width-navigationIndicator.Width)/2),0,0);
        var visual=ElementCompositionPreview.GetElementVisual(navigationIndicator);
        if(!animate||navigationIndicator.Opacity==0||!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) {
            visual.StopAnimation("Translation");visual.Properties.InsertVector3("Translation",target);
        } else {
            var animation=visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.Duration=TimeSpan.FromMilliseconds(160);
            // A second click continues from the current animated position, not the old tab.
            animation.InsertExpressionKeyFrame(0,"this.StartingValue");
            animation.InsertKeyFrame(1,target,visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f,0),new Vector2(0,1)));
            visual.StartAnimation("Translation",animation);
        }
        navigationIndicator.Opacity=1;
    }
}
