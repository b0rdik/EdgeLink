using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;

namespace EdgeLink;

// Native WinUI controls retain their keyboard, focus and accessibility behavior.
internal sealed partial class ActionButton:Button
{
    public string Text {get=>Content?.ToString()??"";set=>Content=value;}
}
internal sealed partial class SettingsCheckBox:CheckBox
{
    public bool CheckedValue {get=>IsChecked==true;set=>IsChecked=value;}
}
internal static class Theme
{
    public static readonly SolidColorBrush Mint=new(Windows.UI.Color.FromArgb(255,89,226,199));
    public static Brush Resource(string key)=>(Brush)Application.Current.Resources[key];
    public static TextBlock Label(string text,double size=14,bool bold=false,bool muted=false)=>new() {
        Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,
        TextWrapping=TextWrapping.Wrap,Foreground=Resource(muted?"TextFillColorSecondaryBrush":"TextFillColorPrimaryBrush")
    };
    public static ActionButton Button(string text,bool primary=false) {
        var button=new ActionButton {Text=text,MinHeight=40,Padding=new Thickness(18,8,18,8)};
        if(primary)Primary(button);return button;
    }
    public static void Primary(ActionButton button)=>button.Style=(Style)Application.Current.Resources["AccentButtonStyle"];
    public static StackPanel Stack(params UIElement[] children) {
        var stack=new StackPanel {Spacing=12};foreach(var child in children)stack.Children.Add(child);return stack;
    }
    public static Border Card(string title,string hint,params UIElement[] children) {
        var stack=Stack();if(title.Length>0)stack.Children.Add(Label(title,19,true));
        if(hint.Length>0)stack.Children.Add(Label(hint,14,muted:true));
        foreach(var child in children)stack.Children.Add(child);
        var card=new Border {Padding=new Thickness(22),CornerRadius=new CornerRadius(12),
            Background=Resource("CardBackgroundFillColorDefaultBrush"),BorderBrush=Resource("CardStrokeColorDefaultBrush"),BorderThickness=new Thickness(1),Child=stack};
        if(title.Length>0)AutomationProperties.SetName(card,title);return card;
    }
    public static FieldRow Field(string caption,Control editor)=>new(caption,editor);
}
