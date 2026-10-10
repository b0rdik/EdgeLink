using System.Reflection;

namespace EdgeLink;
internal sealed partial class MainWindow
{
    private readonly ConnectionHero hero = new();
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly List<Panel> pages = [];
    private readonly List<Button> navigation = [];
    private readonly Label connectionHeading = Theme.Label("Давайте подключимся",20,true);
    private readonly Label inputHint = Theme.Label("",10,muted:true);
    private readonly Label setupHint = Theme.Label("",11,muted:true);
    private readonly WizardProgress setupProgress = new();
    private readonly Button setupNext = Theme.Button("Продолжить",true), setupBack = Theme.Button("Назад");
    private readonly List<Panel> setupStages = [];
    private Panel? wizardCard;
    private Panel? manualNetwork;
    private bool setupComplete;
    private int setupStage;
    private string macName = "Ваш Mac";
    private TableLayoutPanel? dashboardRoot;

    private void BuildDashboard()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
        Font=Theme.Font(); BackColor=Theme.Background; ForeColor=Theme.Text;
        ClientSize = new Size(880,760); MinimumSize=new Size(680,560);
        foreach (var c in new Control[] {local,remote,pairing,side,screen,device,jitter,mouseRate,gain,swap,automatic,reconnect,connect,importPairing,firewall,updateButton,status,stats,updateStatus}) Theme.Style(c);
        Theme.Primary(connect); Theme.Primary(importPairing); connect.Text="Подключить"; importPairing.Text="Добавить Mac…";
        automatic.Text="Находить Mac по Ethernet автоматически"; reconnect.Text="Восстанавливать соединение автоматически";
        status.Font=Theme.Font(10); status.ForeColor=Theme.Muted; stats.Font=Theme.Font(9); stats.ForeColor=Theme.Muted;
        status.MaximumSize=stats.MaximumSize=updateStatus.MaximumSize=Size.Empty;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty,Padding=new Padding(26,16,26,0),BackColor=Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,78)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        var brand=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,BackColor=Theme.Background };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,72));brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        using (var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("EdgeLink.Brand.png")) {
            if (stream != null) { using var original=Image.FromStream(stream); brand.Controls.Add(new PictureBox { Image=new Bitmap(original),Size=new Size(46,46),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,7,0,0) },0,0); }
        }
        var title=new VerticalStackPanel { Dock=DockStyle.Fill,BackColor=Theme.Background,Margin=Padding.Empty };
        var brandTitle=Theme.Label("EdgeLink",22,true);brandTitle.Margin=Padding.Empty;title.Controls.Add(brandTitle);title.Controls.Add(Theme.Label("Мышь, клавиатура и звук — вместе",10,muted:true));brand.Controls.Add(title,1,0);
        var version=Theme.Label("v"+Updates.CurrentVersion,9,muted:true);version.Dock=DockStyle.Fill;version.TextAlign=ContentAlignment.MiddleRight;brand.Controls.Add(version,2,0);root.Controls.Add(brand,0,0);
        var nav=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=Theme.Sidebar,Margin=new Padding(0,0,17,12),Padding=new Padding(3) };
        nav.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        string[] names=["Главная","Первое подключение","Параметры"];
        for(int i=0;i<names.Length;i++) { nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));int index=i;var b=Theme.Button(names[i]);b.Dock=DockStyle.Fill;b.Margin=Padding.Empty;b.Padding=new Padding(8,4,8,4);b.FlatAppearance.BorderSize=0;b.BackColor=Theme.Sidebar;b.Click+=(_,_)=>SelectPage(index);navigation.Add(b);nav.Controls.Add(b,i,0); }
        root.Controls.Add(nav,0,1);content.Margin=Padding.Empty;root.Controls.Add(content,0,2);
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Theme.Background,Margin=Padding.Empty,Padding=new Padding(0,10,0,0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var returnHint=Theme.Label("Вернуть управление на ПК",9,muted:true);returnHint.Dock=DockStyle.Fill;returnHint.TextAlign=ContentAlignment.MiddleLeft;returnHint.Margin=Padding.Empty;
        var shortcut=Theme.Label("Ctrl + Alt + Esc",9,true,muted:true);shortcut.Dock=DockStyle.Fill;shortcut.TextAlign=ContentAlignment.MiddleRight;shortcut.Margin=Padding.Empty;
        footer.Controls.Add(returnHint,0,0);footer.Controls.Add(shortcut,1,0);footer.Paint+=(_,e)=>{using var pen=new Pen(Theme.Border);e.Graphics.DrawLine(pen,0,0,footer.Width,0);};root.Controls.Add(footer,0,3);Controls.Add(root);
        dashboardRoot=root;SizeChanged+=(_,_)=>UpdateViewport();Shown+=(_,_)=>FitWindow();
        var home=Page();
        var connectionCard=(RoundedPanel)Theme.Card("", "",connectionHeading,hero,new StatusActionPanel(status,connect));connectionCard.Accent=true;home.Controls.Add(connectionCard);
        home.Controls.Add(new SplitCardsPanel(
            Theme.Card("Одна мышь и клавиатура","",inputHint),
            Theme.Card("Звук в наушниках ПК","Выберите аудиовыход в параметрах. Звук Windows и Mac смешивается.")));
        home.Controls.Add(Theme.Label("Закрытие окна скрывает EdgeLink в трей. Соединение продолжает работать.",9,muted:true));
        var wizard=Page();
        wizard.Controls.Add(setupProgress);
        var stages = new[] {
            Theme.Card("1. Добавить Mac","Соедините свободные Ethernet-порты кабелем. На Mac выдайте разрешения и сохраните файл подключения.",Theme.Label("Откройте файл .edgelink здесь или перетащите его в это окно.",10,muted:true),Theme.Row(importPairing)),
            Theme.Card("2. Выберите наушники","Полученный звук Mac будет смешиваться со звуком Windows.",Theme.Field("Аудиовыход",device),Theme.Field("Mac расположен",side),Theme.Label("Основной монитор и минимальный буфер выбраны автоматически. Их можно изменить позже.",10,muted:true)),
            Theme.Card("3. Подключитесь","Mac должен показывать «Mac ждёт ваш ПК». Если соединение блокируется, настройте firewall одной кнопкой.",Theme.Row(firewall),Theme.Row(CreateWizardConnect()),Theme.Label("Запрос администратора нужен только для правила Windows Firewall.",10,muted:true)),
            Theme.Card("4. Проверьте переход","Доведите курсор до края выбранного экрана в сторону Mac. Для возврата пройдите через противоположный край Mac.",Theme.Label("Ctrl + Alt + Esc немедленно вернёт управление на ПК.",12,true),Theme.Label("Переход выполняется, когда клавиши и кнопки мыши отпущены.",10,muted:true))
        };
        foreach(var p in stages) {setupStages.Add(p);wizard.Controls.Add(p);}
        wizard.Controls.Add(setupHint);wizardCard=Theme.Row(setupBack,setupNext);wizardCard.BackColor=Theme.Background;wizard.Controls.Add(wizardCard);
        setupBack.Click+=(_,_)=>{setupStage=Math.Max(0,setupStage-1);UpdateWizard();};
        setupNext.Click+=(_,_)=>{if(setupStage==3){setupComplete=true;SaveSettings();SelectPage(0);}else{setupStage++;UpdateWizard();}};
        AllowDrop=true; DragEnter+=(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Length==1 && Path.GetExtension(f[0]).Equals(".edgelink",StringComparison.OrdinalIgnoreCase)) e.Effect=DragDropEffects.Copy;};
        DragDrop+=async (_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Length==1) await ImportPairing(f[0],false);};
        var preferences=Page();
        // The wizard and settings share values, not Control instances: move the selectors only when needed.
        preferences.Controls.Add(Theme.Card("Рабочий стол","Выберите монитор ПК, у края которого начнётся переход.",Theme.Field("Экран ПК",screen),Theme.Field("Передача мыши",mouseRate),Theme.Field("Скорость мыши",gain),swap));
        var audioSettings=Theme.Card("Звук и расположение","Выбранные здесь параметры используются при следующем соединении.");
        audioSettings.Tag="audio-settings"; preferences.Controls.Add(audioSettings);
        preferences.Controls.Add(Theme.Card("Соединение","Обычно достаточно автоматического поиска: адреса вручную не нужны.",automatic,reconnect));
        manualNetwork=Theme.Card("Ручное подключение","Используйте только если автоматический поиск не подходит. Ключ берётся из EdgeLink на Mac.",Theme.Field("IPv4 ПК",local),Theme.Field("IPv4 Mac",remote),Theme.Field("Ключ с Mac",pairing));preferences.Controls.Add(manualNetwork);manualNetwork.Visible=!automatic.Checked;
        preferences.Controls.Add(Theme.Card("Обновления","Новые версии из b0rdik/EdgeLink. Загрузка начинается после подтверждения.",Theme.Row(updateButton),updateStatus));
        preferences.Controls.Add(Theme.Card("Диагностика","Показатели текущего соединения. Полная задержка звука здесь не измеряется.",stats));
        automatic.CheckedChanged+=(_,_)=>{if(manualNetwork!=null)manualNetwork.Visible=!automatic.Checked;};
        side.SelectedIndexChanged+=(_,_)=>UpdateWizard();
        pairing.TextChanged+=(_,_)=>UpdateWizard();
        SelectPage(0);UpdateWizard();ResumeLayout(true);
    }
    private Button CreateWizardConnect() { var b=Theme.Button("Подключить и проверить",true);b.Click+=async (_,_)=>{manuallyPaused=false;await Connect();UpdateWizard();};return b; }
    private VerticalStackPanel Page()
    {
        var panel=new VerticalStackPanel { Dock=DockStyle.Fill,AutoScroll=true,BackColor=Theme.Background,Margin=Padding.Empty,Padding=new Padding(0,0,4,0) };
        content.Controls.Add(panel);pages.Add(panel);return panel;
    }
    private void SelectPage(int index)
    {
        for(int i=0;i<pages.Count;i++) pages[i].Visible=i==index;
        if(index<pages.Count)pages[index].BringToFront();
        for(int i=0;i<navigation.Count;i++){navigation[i].BackColor=i==index ? Color.FromArgb(70,73,77) : Theme.Sidebar;navigation[i].ForeColor=i==index ? Theme.Text : Theme.Muted;navigation[i].Invalidate();}
        if(index==2) MoveAudioSettings(); else if(index==1) MoveWizardAudio();
        if(index<pages.Count)pages[index].PerformLayout();
    }
    private void MoveAudioSettings()
    {
        if(pages.Count<3)return;var card=pages[2].Controls.Cast<Control>().First(c=>Equals(c.Tag,"audio-settings"));
        var stack=card;
        foreach(var c in new Control[]{device,side,jitter}) if(c.Parent!=null) {var field=c.Parent;field.Controls.Remove(c);}
        while(stack.Controls.Count>2) {var field=stack.Controls[2];stack.Controls.Remove(field);field.Dispose();}
        foreach(var field in new[]{Theme.Field("Аудиовыход",device),Theme.Field("Mac расположен",side),Theme.Field("Буфер звука",jitter)}) stack.Controls.Add(field);
        stack.PerformLayout();
    }
    private void MoveWizardAudio()
    {
        if(setupStages.Count<2)return;var stack=setupStages[1];
        // Fixed rows 2 and 3 are the reusable containers created for the wizard selectors.
        for(int i=0;i<2;i++){Control c=i==0 ? device : side;var field=(FieldPanel)stack.Controls[i+2];field.Attach(c);}
    }
    private void UpdateWizard()
    {
        if(setupStages.Count==0)return;
        for(int i=0;i<setupStages.Count;i++)setupStages[i].Visible=i==setupStage;
        connectionHeading.Text=link==null ? "Давайте подключимся" : input?.Active==true ? "Управление на Mac" : "Всё подключено";
        inputHint.Text=side.SelectedIndex==0 ? "Доведите курсор до левого края экрана ПК. На Mac он появится справа." : "Доведите курсор до правого края экрана ПК. На Mac он появится слева.";
        setupHint.Text=setupStage==2 ? (link!=null ? "Соединение установлено. Можно продолжить." : lastError.Length>0 ? lastError : "Нажмите «Подключить и проверить».") : setupStage==0 && HasPairingKey() ? "Ключ доступа сохранён в Credential Manager." : "Настройки можно изменить в любой момент.";
        setupHint.Visible=setupStage==2;setupProgress.Stage=setupStage;setupProgress.Invalidate();setupBack.Enabled=setupStage>0&&!connecting;
        setupNext.Text=setupStage==3 ? "На главный экран" : "Продолжить";
        setupNext.Enabled=!connecting&&(setupStage==0 ? HasPairingKey() : setupStage==2 ? link!=null : true);
        hero.Connected=link!=null;hero.Active=input?.Active==true;hero.MacLeft=side.SelectedIndex==0;hero.Invalidate();
        if(pages.Count>0)pages[0].PerformLayout();
        if(pages.Count>1)pages[1].PerformLayout();
    }
    private void UpdateViewport()
    {
        if(dashboardRoot==null)return;
        bool compact=ClientSize.Width<UiLayout.Px(this,820);
        int horizontal=UiLayout.Px(this,compact?20:26);
        dashboardRoot.Padding=new Padding(horizontal,UiLayout.Px(this,16),horizontal,0);
        foreach(var page in pages)page.PerformLayout();
    }
    private void FitWindow()
    {
        var area=Screen.FromControl(this).WorkingArea;int gap=UiLayout.Px(this,16);
        int maxWidth=Math.Max(1,area.Width-gap*2),maxHeight=Math.Max(1,area.Height-gap*2);
        MinimumSize=new Size(Math.Min(UiLayout.Px(this,680),maxWidth),Math.Min(UiLayout.Px(this,560),maxHeight));
        Size=new Size(Math.Min(Width,maxWidth),Math.Min(Height,maxHeight));
        Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        UpdateViewport();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);UpdateViewport();
    }
}
