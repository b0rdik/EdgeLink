using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using Windows.ApplicationModel.DataTransfer;

namespace EdgeLink;

public sealed partial class MainWindow
{
    internal readonly Grid Root=new() {RequestedTheme=ElementTheme.Default};
    private readonly NavigationView navigation=new() {PaneDisplayMode=NavigationViewPaneDisplayMode.Top,IsSettingsVisible=false,IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsPaneToggleButtonVisible=false};
    private readonly Grid navigationStrip=new() {Margin=new Thickness(24,8,24,8)};
    private readonly Border navigationIndicator=new() {Width=24,Height=3,CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,4),IsHitTestVisible=false,Opacity=0};
    private readonly ScrollViewer viewport=new() {HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly List<StackPanel> pages=[];
    private readonly List<NavigationViewItem> navigationItems=[];
    private readonly TextBlock connectionHeading=Theme.Mono("Не подключено",14,true);
    private readonly TextBlock inputHint=Theme.Label("",13,muted:true);
    private readonly TextBlock keyboardStatus=Theme.Mono("Не подключено",12,muted:true),mouseStatus=Theme.Mono("Не подключено",12,muted:true),audioStatus=Theme.Mono("Не передаётся",12,muted:true);
    private readonly List<FontIcon> deviceStatusIcons=[];
    private readonly Expander connectionDiagnostics=new() {Header="Диагностика соединения",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private string devicePresentation="";
    private readonly TextBlock setupHint=Theme.Label("",14,muted:true);
    private readonly ActionButton setupNext=Theme.Button("Продолжить",true),setupBack=Theme.Button("Назад");
    private readonly List<Border> setupStages=[];
    private readonly List<TextBlock> progressTitles=[];
    private readonly List<Border> progressDots=[];
    private readonly List<TextBlock> progressNumbers=[];
    private readonly Border macTile=Theme.Card("",""),pcTile=Theme.Card("","");
    private readonly Grid hero=new() {MinHeight=62};
    private readonly Border connectionCard=Theme.Card("","");
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse connectionDot=new() {Width=7,Height=7,VerticalAlignment=VerticalAlignment.Center};
    private readonly FontIcon connectionDirection=new() {Glyph="\uE8AB",FontSize=16,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
    private readonly ComboBox wizardDevice=new(),wizardSide=new();
    private Border? manualNetwork;
    private bool setupComplete;
    private int setupStage,selectedPage;
    private string macName="Ваш Mac";
    private bool syncingSelectors;
    private readonly double[] pageOffsets=new double[3];

    private void BuildDashboard() {
        Theme.AttachResources(Root);
        Root.Background=Theme.Resource("ApplicationPageBackgroundThemeBrush");
        Root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        Root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
        Root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(48)});
        string[] names=["Соединение","Настройка","Параметры"];
        for(int i=0;i<names.Length;i++) {
            var item=new NavigationViewItem {Content=names[i],Tag=i};
            navigationItems.Add(item);navigation.MenuItems.Add(item);
        }
        navigation.HorizontalAlignment=HorizontalAlignment.Center;
        navigation.Resources["NavigationViewContentBackground"]=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        navigation.Resources["NavigationViewSelectionIndicatorForeground"]=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        navigation.SelectionChanged+=(_,e)=> {
            if(e.SelectedItem is not NavigationViewItem item)return;int index=(int)item.Tag;
            BeginInvoke(()=> {if(navigation.SelectedItem is NavigationViewItem current&&(int)current.Tag==index)MoveNavigationIndicator(true);});
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=> {
                if(!IsDisposed&&navigation.SelectedItem is NavigationViewItem current&&(int)current.Tag==index)SelectPage(index);
            });
        };
        navigationStrip.Children.Add(navigation);navigationStrip.Children.Add(navigationIndicator);navigationIndicator.Background=Theme.Mint;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(navigationIndicator,true);
        navigationStrip.SizeChanged+=(_,_)=>MoveNavigationIndicator(false);navigation.SizeChanged+=(_,_)=>MoveNavigationIndicator(false);
        navigation.Loaded+=(_,_)=>MoveNavigationIndicator(false);Root.Children.Add(navigationStrip);
        viewport.Margin=new Thickness(24,16,24,0);Grid.SetRow(viewport,1);Root.Children.Add(viewport);
        var footer=new Grid {Padding=new Thickness(24,0,24,0),BorderBrush=Theme.Resource("DividerStrokeColorDefaultBrush"),BorderThickness=new Thickness(0,1,0,0)};
        footer.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var shortcut=Theme.Mono("Ctrl + Alt + Esc · вернуть на ПК",11,muted:true);shortcut.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(shortcut);
        var version=Theme.Mono("v"+Updates.CurrentVersion,11,muted:true);version.VerticalAlignment=VerticalAlignment.Center;version.Margin=new Thickness(12,0,16,0);Grid.SetColumn(version,1);footer.Children.Add(version);
        var menu=Theme.Button("Меню");menu.MinHeight=28;menu.Padding=new Thickness(12,4,12,4);menu.VerticalAlignment=VerticalAlignment.Center;
        var flyout=new MenuFlyout();var open=new MenuFlyoutItem {Text="Открыть EdgeLink"};open.Click+=(_,_)=>RestoreFromTray();
        var toggle=new MenuFlyoutItem {Text="Подключить / отключить"};toggle.Click+=async (_,_)=>await ToggleConnection();
        var settings=new MenuFlyoutItem {Text="Параметры"};settings.Click+=(_,_)=>SelectPage(2);
        var exit=new MenuFlyoutItem {Text="Выйти из EdgeLink"};exit.Click+=(_,_)=>ExitFromTray();
        flyout.Items.Add(open);flyout.Items.Add(settings);flyout.Items.Add(toggle);flyout.Items.Add(new MenuFlyoutSeparator());flyout.Items.Add(exit);
        flyout.Opening+=(_,_)=>toggle.IsEnabled=!previewMode&&!connecting&&!installing&&input!=null;menu.Flyout=flyout;Grid.SetColumn(menu,2);footer.Children.Add(menu);Grid.SetRow(footer,2);Root.Children.Add(footer);
        Root.ActualThemeChanged+=(_,_)=>{Theme.SetAppearance(Root.ActualTheme);UpdateWizard();};
        Theme.SetAppearance(Root.ActualTheme);

        var home=Page();
        var macContent=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        macContent.Children.Add(new FontIcon {Glyph="\uE7F8",FontSize=28});macContent.Children.Add(Theme.Mono("MacBook",13,true));
        var pcContent=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        pcContent.Children.Add(new FontIcon {Glyph="\uE7F4",FontSize=28});pcContent.Children.Add(Theme.Mono("ПК",13,true));
        macTile.Child=macContent;pcTile.Child=pcContent;macTile.Padding=pcTile.Padding=new Thickness(12,14,12,14);
        macTile.CornerRadius=pcTile.CornerRadius=new CornerRadius(8);macTile.BorderThickness=pcTile.BorderThickness=new Thickness(1);
        hero.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});hero.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(32)});hero.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetColumn(connectionDirection,1);hero.Children.Add(connectionDirection);hero.Children.Add(macTile);hero.Children.Add(pcTile);
        connectionCard.Child=hero;connectionCard.Padding=new Thickness(10);connectionCard.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);connectionCard.CornerRadius=new CornerRadius(10);home.Children.Add(connectionCard);
        var heading=new Grid {ColumnSpacing=10};heading.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});heading.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        heading.Children.Add(connectionDot);Grid.SetColumn(connectionHeading,1);heading.Children.Add(connectionHeading);
        home.Children.Add(new StatusActionRow(heading,connect));status.FontSize=13;status.Foreground=Theme.Resource("TextFillColorSecondaryBrush");home.Children.Add(status);
        home.Children.Add(Divider());home.Children.Add(BuildDeviceStatus());home.Children.Add(Divider());
        connectionDiagnostics.Content=new Border {Padding=new Thickness(16),Child=Theme.Stack(stats)};
        connectionDiagnostics.RegisterPropertyChangedCallback(Expander.IsExpandedProperty,(_,_)=>UpdateStatsTimer());
        home.Children.Add(connectionDiagnostics);
        ToolTipService.SetToolTip(stats,"Обновляется каждую секунду. RTT — время ответа сети; полная задержка звука здесь не измеряется. Пакеты — полученные блоки звука с начала соединения.");
        var trayHint=Theme.Label("Крестик скрывает окно в трей. Соединение продолжает работать.",12,muted:true);
        trayHint.Margin=new Thickness(0,4,0,4);home.Children.Add(trayHint);

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
        preferences.Children.Add(Theme.Card("Игры и полный экран","Переходы отключаются, когда активное приложение занимает весь выбранный экран ПК. Работает и в режиме «полноэкранный в окне», включая меню игры. После Alt+Tab переходы восстанавливаются; отведите курсор от края перед новым переходом.",protectFullscreen,Theme.Label("Также действует для полноэкранного видео и браузера. Звук Mac продолжает работать. Настройка применяется сразу.",13,muted:true)));
        preferences.Children.Add(Theme.Card("Соединение","Обычно достаточно автоматического поиска.",automatic,reconnect));
        manualNetwork=Theme.Card("Ручное подключение","Используйте, если автоматический поиск не подходит.",Theme.Field("IPv4 ПК",local),Theme.Field("IPv4 Mac",remote),Theme.Field("Ключ с Mac",pairing));preferences.Children.Add(manualNetwork);
        preferences.Children.Add(Theme.Card("Обновления","Новые версии из b0rdik/EdgeLink. Установка — после подтверждения.",updateButton,updateStatus));
        automatic.Checked+=(_,_)=>UpdateManualNetwork();automatic.Unchecked+=(_,_)=>UpdateManualNetwork();
        side.SelectionChanged+=(_,_)=>UpdateWizard();pairing.PasswordChanged+=(_,_)=>UpdateWizard();
        Content=Root;SelectPage(0);UpdateManualNetwork();UpdateWizard();
    }
    private static Border Divider()=>new() {Height=1,Background=Theme.Resource("DividerStrokeColorDefaultBrush"),Margin=new Thickness(0,6,0,6)};
    private StackPanel BuildDeviceStatus() {
        var rows=new StackPanel();
        string[] names=["Клавиатура","Мышь","Звук"];string[] glyphs=["\uE765","\uE962","\uE767"];
        TextBlock[] states=[keyboardStatus,mouseStatus,audioStatus];
        for(int i=0;i<3;i++) {
            var row=new Grid {MinHeight=38};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(34)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var icon=new FontIcon {Glyph=glyphs[i],FontSize=16,Width=22,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Foreground=Theme.Resource("TextFillColorSecondaryBrush")};deviceStatusIcons.Add(icon);
            var title=Theme.Label(names[i],13);title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);
            states[i].VerticalAlignment=VerticalAlignment.Center;states[i].Margin=new Thickness(12,0,0,0);Grid.SetColumn(states[i],2);
            row.Children.Add(icon);row.Children.Add(title);row.Children.Add(states[i]);rows.Children.Add(row);
        }
        return rows;
    }
    private void Synchronize(ComboBox original,ComboBox mirror) {
        void Sync(ComboBox source,ComboBox target) {if(syncingSelectors)return;syncingSelectors=true;try{target.SelectedIndex=source.SelectedIndex;}finally{syncingSelectors=false;}}
        original.SelectionChanged+=(_,_)=>Sync(original,mirror);mirror.SelectionChanged+=(_,_)=>Sync(mirror,original);
    }
    private StackPanel Page() {var page=new StackPanel {Spacing=16,Margin=new Thickness(0,0,16,20)};pages.Add(page);return page;}
    private void SelectPage(int index) {
        if(index<0||index>=pages.Count)return;
        if(selectedPage==index&&ReferenceEquals(viewport.Content,pages[index]))return;
        pageOffsets[selectedPage]=viewport.VerticalOffset;selectedPage=index;viewport.Content=pages[index];AnimatePage(pages[index]);UpdateStatsTimer();
        if(navigation.SelectedItem is not NavigationViewItem selected||(int)selected.Tag!=index)navigation.SelectedItem=navigationItems[index];
        BeginInvoke(()=> {if(selectedPage==index)viewport.ChangeView(null,pageOffsets[index],null,true);});
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
            progressNumbers[i].Foreground=i<=setupStage?new SolidColorBrush(Root.ActualTheme==ElementTheme.Dark?Windows.UI.Color.FromArgb(255,20,32,30):Microsoft.UI.Colors.White):Theme.Resource("TextFillColorSecondaryBrush");
            progressNumbers[i].Text=i<setupStage?"✓":(i+1).ToString();
        }
        RenderConnectionState(link?.IsAlive==true,input?.Active==true,input?.FullscreenBlocked==true,connecting);
        setupHint.Text=link!=null?"Соединение установлено. Можно продолжить.":lastError.Length>0?lastError:"Нажмите «Подключить и проверить».";
        setupHint.Visibility=setupStage==2?Visibility.Visible:Visibility.Collapsed;
        setupBack.IsEnabled=setupStage>0&&!connecting;setupNext.Text=setupStage==3?"На главный экран":"Продолжить";
        setupNext.IsEnabled=!connecting&&(setupStage==0?HasPairingKey():setupStage==2?link!=null:true);
    }
    private void RenderConnectionState(bool connected,bool active,bool fullscreen,bool pending) {
        if(!pending)status.Text=lastError.Length>0?lastError:!connected?"Подключите Mac по Ethernet, чтобы начать работу.":active?"Мышь и клавиатура управляют Mac. Звук передаётся в наушники ПК.":fullscreen?"Связь активна. Переходы приостановлены для полноэкранного приложения; звук продолжает работать.":"Mac и ПК соединены. Звук передаётся в наушники ПК.";
        else if(!status.Text.StartsWith("Ищем ",StringComparison.Ordinal))status.Text="Подключение…";
        connect.Text=connected?"Отключить":"Подключить";
        connect.Style=connected?null:(Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        connectionHeading.Text=pending?"Подключение…":!connected?"Не подключено":active?"Управление на MacBook":"Подключено";
        connectionDot.Fill=connected?Theme.Mint:Theme.Resource("TextFillColorSecondaryBrush");
        connectionHeading.Foreground=connected?Theme.Mint:Theme.Resource("TextFillColorPrimaryBrush");
        connectionDirection.Foreground=connected?Theme.Mint:Theme.Resource("TextFillColorSecondaryBrush");connectionDirection.Opacity=connected?1:.45;
        keyboardStatus.Text=mouseStatus.Text=!connected?"Не подключено":active?"На MacBook":"На ПК";
        audioStatus.Text=connected?"Принимается":"Не передаётся";
        foreach(var label in new[]{keyboardStatus,mouseStatus,audioStatus})label.Foreground=connected?Theme.Mint:Theme.Resource("TextFillColorSecondaryBrush");
        string presentation=keyboardStatus.Text+"|"+audioStatus.Text;
        if(presentation!=devicePresentation) {
            foreach(var icon in deviceStatusIcons){icon.Foreground=connected?Theme.Mint:Theme.Resource("TextFillColorSecondaryBrush");if(devicePresentation.Length>0)AnimateStatusIcon(icon);}
            devicePresentation=presentation;
        }
        inputHint.Text=side.SelectedIndex==0?"Доведите курсор до левого края экрана ПК. На Mac он появится справа.":"Доведите курсор до правого края экрана ПК. На Mac он появится слева.";
        if(fullscreen)inputHint.Text="Переходы приостановлены: активно полноэкранное приложение. После Alt+Tab отведите курсор от края и повторите переход.";
        ToolTipService.SetToolTip(keyboardStatus,inputHint.Text);ToolTipService.SetToolTip(mouseStatus,inputHint.Text);
        Grid.SetColumn(macTile,side.SelectedIndex==0?0:2);Grid.SetColumn(pcTile,side.SelectedIndex==0?2:0);
        ToolTipService.SetToolTip(macTile,side.SelectedIndex==0?"MacBook слева от ПК":"MacBook справа от ПК");
        macTile.BorderBrush=connected&&active?Theme.Mint:Theme.Resource("CardStrokeColorDefaultBrush");
        pcTile.BorderBrush=connected&&!active?Theme.Mint:Theme.Resource("CardStrokeColorDefaultBrush");
        macTile.Background=connected&&active?Theme.Resource("ControlFillColorSecondaryBrush"):connectionCard.Background;
        pcTile.Background=connected&&!active?Theme.Resource("ControlFillColorSecondaryBrush"):connectionCard.Background;
        AutomationProperties.SetName(macTile,connected&&active?"MacBook — управление мышью и клавиатурой":"MacBook");
        AutomationProperties.SetName(pcTile,connected&&!active?"ПК — управление мышью и клавиатурой":"ПК");
    }
}
