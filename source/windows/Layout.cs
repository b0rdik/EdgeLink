using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EdgeLink;

internal sealed partial class FieldRow:Grid
{
    private readonly TextBlock caption;
    private readonly Control editor;
    private bool? stacked;
    public FieldRow(string name,Control editor) {
        this.editor=editor;caption=Theme.Label(name,14,muted:true);caption.VerticalAlignment=VerticalAlignment.Center;
        Children.Add(caption);Children.Add(editor);SizeChanged+=(_,_)=>Arrange();Arrange();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor,name);
    }
    private void Arrange() {
        bool vertical=ActualWidth<520;if(stacked==vertical)return;stacked=vertical;
        ColumnDefinitions.Clear();RowDefinitions.Clear();
        RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        ColumnDefinitions.Add(new ColumnDefinition {Width=vertical?new GridLength(1,GridUnitType.Star):new GridLength(170)});
        if(vertical)RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        else ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        SetColumn(editor,vertical?0:1);SetRow(editor,vertical?1:0);
        editor.Margin=vertical?new Thickness(0,8,0,0):new Thickness(16,0,0,0);
        editor.HorizontalAlignment=HorizontalAlignment.Stretch;
    }
}
internal sealed partial class StatusActionRow:Grid
{
    private readonly TextBlock status;
    private readonly ActionButton action;
    private bool? stacked;
    public StatusActionRow(TextBlock status,ActionButton action) {
        this.status=status;this.action=action;status.VerticalAlignment=VerticalAlignment.Center;action.MinWidth=136;
        Children.Add(status);Children.Add(action);SizeChanged+=(_,_)=>Arrange();Arrange();
    }
    private void Arrange() {
        bool vertical=ActualWidth<480;if(stacked==vertical)return;stacked=vertical;
        ColumnDefinitions.Clear();RowDefinitions.Clear();RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        if(vertical)RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        else ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        SetRow(action,vertical?1:0);SetColumn(action,vertical?0:1);action.HorizontalAlignment=HorizontalAlignment.Right;
        action.Margin=vertical?new Thickness(0,12,0,0):new Thickness(20,0,0,0);
    }
}
internal sealed partial class ResponsiveCards:Grid
{
    private readonly FrameworkElement left,right;
    private bool? stacked;
    public ResponsiveCards(FrameworkElement left,FrameworkElement right) {
        this.left=left;this.right=right;Children.Add(left);Children.Add(right);SizeChanged+=(_,_)=>Arrange();Arrange();
    }
    private void Arrange() {
        bool vertical=ActualWidth<620;if(stacked==vertical)return;stacked=vertical;
        RowDefinitions.Clear();ColumnDefinitions.Clear();RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        if(vertical)RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        else ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        SetRow(right,vertical?1:0);SetColumn(right,vertical?0:1);
        left.Margin=vertical?new Thickness(0):new Thickness(0,0,8,0);
        right.Margin=vertical?new Thickness(0,16,0,0):new Thickness(8,0,0,0);
    }
}
