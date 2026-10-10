using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace EdgeLink;

public sealed partial class MainWindow
{
    internal readonly Grid Root=new() {RequestedTheme=ElementTheme.Dark};
    private readonly NavigationView navigation=new() {PaneDisplayMode=NavigationViewPaneDisplayMode.Top,IsSettingsVisible=false,IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsPaneToggleButtonVisible=false};
    private readonly ScrollViewer viewport=new() {HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly List<StackPanel> pages=[];
    private readonly List<NavigationViewItem> navigationItems=[];
    private readonly TextBlock connectionHeading=Theme.Label("Давайте подключимся",26,true);
    private readonly TextBlock inputHint=Theme.Label("",14,muted:true);
    private readonly TextBlock setupHint=Theme.Label("",14,muted:true);
    private readonly ActionButton setupNext=Theme.Button("Продолжить",true),setupBack=Theme.Button("Назад");
    private readonly List<Border> setupStages=[];
    private readonly List<TextBlock> progressTitles=[];
    private readonly List<Border> progressDots=[];
    private readonly List<TextBlock> progressNumbers=[];
    private readonly Border macTile=Theme.Card("",""),pcTile=Theme.Card("","");
    private readonly Grid hero=new() {MinHeight=156};
    private readonly TextBlock heroLabel=Theme.Label("Ethernet",12,muted:true);
    private readonly Border connectionCard=Theme.Card("","");
    private readonly ComboBox wizardDevice=new(),wizardSide=new();
    private Border? manualNetwork;
    private bool setupComplete;
    private int setupStage,selectedPage;
    private string macName="Ваш Mac";
    private bool syncingSelectors;
    private readonly double[] pageOffsets=new double[3];

    private void BuildDashboard() {
        Root.Background=Theme.Resource("ApplicationPageBackgroundThemeBrush");Root.Padding=new Thickness(24,12,24,0);
        Root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(80)});
        Root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
        Root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(48)});
        var brand=new Grid();brand.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(60)});
        brand.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});brand.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var icon=new Image {Source=new BitmapImage(new Uri(Desktop.Asset("Brand.png"))),Width=44,Height=44,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};
        var titles=Theme.Stack(Theme.Label("EdgeLink",25,true),Theme.Label("Мышь, клавиатура и звук — вместе",13,muted:true));titles.Spacing=2;titles.VerticalAlignment=VerticalAlignment.Center;
        var version=Theme.Label("v"+Updates.CurrentVersion,12,muted:true);version.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(titles,1);Grid.SetColumn(version,2);brand.Children.Add(icon);brand.Children.Add(titles);brand.Children.Add(version);Root.Children.Add(brand);
        string[] names=["Главная","Первое подключение","Параметры"];string[] glyphs=["\uE80F","\uE71B","\uE713"];
        for(int i=0;i<names.Length;i++) {
            var item=new NavigationViewItem {Content=names[i],Icon=new FontIcon {Glyph=glyphs[i]},Tag=i};
            navigationItems.Add(item);navigation.MenuItems.Add(item);
        }
        navigation.SelectionChanged+=(_,e)=> {if(e.SelectedItem is NavigationViewItem item)SelectPage((int)item.Tag);};
        viewport.Margin=new Thickness(0,16,0,0);navigation.Content=viewport;Grid.SetRow(navigation,1);Root.Children.Add(navigation);
        var footer=new Grid {BorderBrush=Theme.Resource("DividerStrokeColorDefaultBrush"),BorderThickness=new Thickness(0,1,0,0)};
        footer.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var shortcut=Theme.Label("Вернуть управление на ПК · Ctrl + Alt + Esc",12,muted:true);shortcut.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(shortcut);
        var menu=Theme.Button("Меню");menu.MinHeight=32;menu.Padding=new Thickness(12,4,12,4);menu.VerticalAlignment=VerticalAlignment.Center;
        var flyout=new MenuFlyout();var open=new MenuFlyoutItem {Text="Открыть EdgeLink"};open.Click+=(_,_)=>RestoreFromTray();
        var toggle=new MenuFlyoutItem {Text="Подключить / отключить"};toggle.Click+=async (_,_)=>await ToggleConnection();
        var exit=new MenuFlyoutItem {Text="Выйти из EdgeLink"};exit.Click+=(_,_)=>ExitFromTray();
        flyout.Items.Add(open);flyout.Items.Add(toggle);flyout.Items.Add(new MenuFlyoutSeparator());flyout.Items.Add(exit);
        flyout.Opening+=(_,_)=>toggle.IsEnabled=!previewMode&&!connecting&&!installing&&input!=null;menu.Flyout=flyout;Grid.SetColumn(menu,1);footer.Children.Add(menu);Grid.SetRow(footer,2);Root.Children.Add(footer);

        var home=Page();
        var macContent=Theme.Stack(new FontIcon {Glyph="\uE7F4",FontSize=38,HorizontalAlignment=HorizontalAlignment.Center},Theme.Label("Mac",17,true),Theme.Label("",12,muted:true));
        var pcContent=Theme.Stack(new FontIcon {Glyph="\uE7F8",FontSize=38,HorizontalAlignment=HorizontalAlignment.Center},Theme.Label("Windows",17,true),Theme.Label("Мышь · клавиатура · наушники",12,muted:true));
        foreach(var text in macContent.Children.OfType<TextBlock>().Concat(pcContent.Children.OfType<TextBlock>()))text.TextAlignment=TextAlignment.Center;
        macTile.Child=macContent;pcTile.Child=pcContent;macTile.Padding=pcTile.Padding=new Thickness(12,18,12,18);
        hero.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});hero.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(80)});hero.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        var center=Theme.Stack(new FontIcon {Glyph="\uE8AB",FontSize=25},heroLabel);center.VerticalAlignment=VerticalAlignment.Center;center.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(center,1);hero.Children.Add(center);hero.Children.Add(macTile);hero.Children.Add(pcTile);
        connectionCard.Child=Theme.Stack(connectionHeading,hero,new StatusActionRow(status,connect));connectionCard.BorderBrush=new SolidColorBrush(Windows.UI.Color.FromArgb(255,55,91,85));
        home.Children.Add(connectionCard);
        home.Children.Add(new ResponsiveCards(Theme.Card("Одна мышь и клавиатура","",inputHint),Theme.Card("Звук в наушниках ПК","Звук Windows и Mac смешивается. Выберите аудиовыход в параметрах.")));
        home.Children.Add(Theme.Label("Крестик скрывает окно в трей. Соединение продолжает работать.",12,muted:true));

        var wizard=Page();var progress=new Grid {Margin=new Thickness(0,4,0,8)};
        string[] steps=["Добавить Mac","Наушники","Соединение","Готово"];
        for(int i=0;i<4;i++) {
            progress.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            var number=Theme.Label((i+1).ToString(),12,true);number.HorizontalAlignment=HorizontalAlignment.Center;number.VerticalAlignment=VerticalAlignment.Center;
            var dot=new Border {Width=24,Height=24,CornerRadius=new CornerRadius(12),Child=number};var text=Theme.Label(steps[i],12);text.TextAlignment=TextAlignment.Center;
            var step=Theme.Stack(dot,text);step.Spacing=6;step.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(step,i);progress.Children.Add(step);
            progressTitles.Add(text);progressDots.Add(dot);progressNumbers.Add(number);
        }
        wizard.Children.Add(progress);
        foreach(var value in device.Items)wizardDevice.Items.Add(value);wizardSide.Items.Add("Mac слева");wizardSide.Items.Add("Mac справа");
        wizardDevice.SelectedIndex=device.SelectedIndex;wizardSide.SelectedIndex=side.SelectedIndex;
        Synchronize(device,wizardDevice);Synchronize(side,wizardSide);
        var wizardConnect=Theme.Button("Подключить и проверить",true);wizardConnect.Click+=async (_,_)=> {if(previewMode)return;manuallyPaused=false;await Connect();UpdateWizard();};
        setupStages.Add(Theme.Card("1. Добавить Mac","Соедините свободные Ethernet-порты кабелем. На Mac выдайте разрешения и сохраните файл подключения.",Theme.Label("Откройте файл .edgelink здесь или перетащите его в это окно.",14,muted:true),importPairing));
        setupStages.Add(Theme.Card("2. Выберите наушники","Полученный звук Mac смешивается со звуком Windows.",Theme.Field("Аудиовыход",wizardDevice),Theme.Field("Mac расположен",wizardSide),Theme.Label("Монитор и буфер можно изменить в параметрах.",14,muted:true)));
        setupStages.Add(Theme.Card("3. Подключитесь","Mac должен показывать «Mac ждёт ваш ПК».",firewall,wizardConnect,Theme.Label("Запрос администратора нужен только для правила Windows Firewall.",13,muted:true)));
        setupStages.Add(Theme.Card("4. Проверьте переход","Доведите курсор до края выбранного экрана в сторону Mac. Для возврата пройдите через противоположный край Mac.",Theme.Label("Ctrl + Alt + Esc немедленно вернёт управление на ПК.",16,true),Theme.Label("Переход выполняется, когда клавиши и кнопки мыши отпущены.",14,muted:true)));
        foreach(var stage in setupStages)wizard.Children.Add(stage);wizard.Children.Add(setupHint);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};actions.Children.Add(setupBack);actions.Children.Add(setupNext);wizard.Children.Add(actions);
        setupBack.Click+=(_,_)=> {setupStage=Math.Max(0,setupStage-1);UpdateWizard();};
        setupNext.Click+=(_,_)=> {if(setupStage==3){setupComplete=true;if(!previewMode)SaveSettings();SelectPage(0);}else{setupStage++;UpdateWizard();}};
        Root.AllowDrop=!previewMode;Root.DragOver+=(_,e)=> {if(e.DataView.Contains(StandardDataFormats.StorageItems)){e.AcceptedOperation=DataPackageOperation.Copy;e.DragUIOverride.Caption="Добавить Mac";}};
        Root.Drop+=async (_,e)=> {if(previewMode)return;var items=await e.DataView.GetStorageItemsAsync();if(items.Count==1&&Path.GetExtension(items[0].Path).Equals(".edgelink",StringComparison.OrdinalIgnoreCase))await ImportPairing(items[0].Path,setupComplete);};

        var preferences=Page();preferences.Children.Add(Theme.Card("Рабочий стол","Выберите монитор ПК, у края которого начнётся переход.",Theme.Field("Экран ПК",screen),Theme.Field("Передача мыши",mouseRate),Theme.Field("Скорость мыши",gain),swap));
        preferences.Children.Add(Theme.Card("Звук и расположение","Параметры используются при следующем соединении.",Theme.Field("Аудиовыход",device),Theme.Field("Mac расположен",side),Theme.Field("Буфер звука",jitter)));
        preferences.Children.Add(Theme.Card("Соединение","Обычно достаточно автоматического поиска.",automatic,reconnect));
        manualNetwork=Theme.Card("Ручное подключение","Используйте, если автоматический поиск не подходит.",Theme.Field("IPv4 ПК",local),Theme.Field("IPv4 Mac",remote),Theme.Field("Ключ с Mac",pairing));preferences.Children.Add(manualNetwork);
        preferences.Children.Add(Theme.Card("Обновления","Новые версии из b0rdik/EdgeLink. Установка — после подтверждения.",updateButton,updateStatus));
        preferences.Children.Add(Theme.Card("Диагностика","Показатели текущего соединения. Полная задержка звука здесь не измеряется.",stats));
        automatic.Checked+=(_,_)=>UpdateManualNetwork();automatic.Unchecked+=(_,_)=>UpdateManualNetwork();
        side.SelectionChanged+=(_,_)=>UpdateWizard();pairing.PasswordChanged+=(_,_)=>UpdateWizard();
        Content=Root;SelectPage(0);UpdateManualNetwork();UpdateWizard();
    }
    private void Synchronize(ComboBox original,ComboBox mirror) {
        void Sync(ComboBox source,ComboBox target) {if(syncingSelectors)return;syncingSelectors=true;try{target.SelectedIndex=source.SelectedIndex;}finally{syncingSelectors=false;}}
        original.SelectionChanged+=(_,_)=>Sync(original,mirror);mirror.SelectionChanged+=(_,_)=>Sync(mirror,original);
    }
    private StackPanel Page() {var page=new StackPanel {Spacing=16,Margin=new Thickness(0,0,16,20)};pages.Add(page);return page;}
    private void SelectPage(int index) {
        if(index<0||index>=pages.Count)return;
        if(selectedPage==index&&ReferenceEquals(viewport.Content,pages[index]))return;
        pageOffsets[selectedPage]=viewport.VerticalOffset;selectedPage=index;viewport.Content=pages[index];
        if(navigation.SelectedItem is not NavigationViewItem selected||(int)selected.Tag!=index)navigation.SelectedItem=navigationItems[index];
        BeginInvoke(()=>viewport.ChangeView(null,pageOffsets[index],null,true));
    }
    private void UpdateManualNetwork() {
        if(manualNetwork!=null)manualNetwork.Visibility=automatic.IsChecked==true?Visibility.Collapsed:Visibility.Visible;
        local.IsEnabled=remote.IsEnabled=automatic.IsChecked!=true&&link==null;
    }
    private void UpdateWizard() {
        if(setupStages.Count==0)return;
        for(int i=0;i<4;i++) {
            setupStages[i].Visibility=i==setupStage?Visibility.Visible:Visibility.Collapsed;
            progressTitles[i].Foreground=i<=setupStage?Theme.Mint:Theme.Resource("TextFillColorSecondaryBrush");
            progressDots[i].Background=i<=setupStage?Theme.Mint:Theme.Resource("ControlFillColorDefaultBrush");
            progressNumbers[i].Foreground=i<=setupStage?new SolidColorBrush(Windows.UI.Color.FromArgb(255,20,32,30)):Theme.Resource("TextFillColorSecondaryBrush");
            progressNumbers[i].Text=i<setupStage?"✓":(i+1).ToString();
        }
        connectionHeading.Text=link==null?"Давайте подключимся":input?.Active==true?"Управление на Mac":"Всё подключено";
        inputHint.Text=side.SelectedIndex==0?"Доведите курсор до левого края экрана ПК. На Mac он появится справа.":"Доведите курсор до правого края экрана ПК. На Mac он появится слева.";
        Grid.SetColumn(macTile,side.SelectedIndex==0?0:2);Grid.SetColumn(pcTile,side.SelectedIndex==0?2:0);
        ((TextBlock)((StackPanel)macTile.Child).Children[2]).Text=side.SelectedIndex==0?"Экран слева":"Экран справа";
        macTile.BorderBrush=link!=null&&input?.Active==true?Theme.Mint:Theme.Resource("CardStrokeColorDefaultBrush");
        pcTile.BorderBrush=link!=null&&input?.Active!=true?Theme.Mint:Theme.Resource("CardStrokeColorDefaultBrush");
        setupHint.Text=link!=null?"Соединение установлено. Можно продолжить.":lastError.Length>0?lastError:"Нажмите «Подключить и проверить».";
        setupHint.Visibility=setupStage==2?Visibility.Visible:Visibility.Collapsed;
        setupBack.IsEnabled=setupStage>0&&!connecting;setupNext.Text=setupStage==3?"На главный экран":"Продолжить";
        setupNext.IsEnabled=!connecting&&(setupStage==0?HasPairingKey():setupStage==2?link!=null:true);
    }
}
