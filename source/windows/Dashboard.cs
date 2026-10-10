using System.Reflection;

namespace EdgeLink;
internal sealed partial class MainWindow
{
    private readonly ConnectionHero hero = new();
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly List<Panel> pages = [];
    private readonly List<Button> navigation = [];
    private readonly Label pageTitle = Theme.Label("Главная",24,true);
    private readonly Label pageHint = Theme.Label("Два компьютера. Один рабочий стол.",10,muted:true);
    private readonly Label setupHeading = Theme.Label("Добавьте свой Mac",20,true);
    private readonly Label setupHint = Theme.Label("",11,muted:true);
    private readonly Label setupProgress = Theme.Label("Шаг 1 из 4",10,muted:true);
    private readonly Button setupNext = Theme.Button("Продолжить",true), setupBack = Theme.Button("Назад");
    private readonly List<Panel> setupStages = [];
    private Panel? wizardCard;
    private Panel? manualNetwork;
    private bool setupComplete;
    private int setupStage;
    private string macName = "Ваш Mac";
    private TableLayoutPanel? dashboardRoot;
    private Panel? dashboardBody;

    private void BuildDashboard()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96,96); AutoScaleMode = AutoScaleMode.Dpi;
        Font=Theme.Font(); BackColor=Theme.Background; ForeColor=Theme.Text;
        ClientSize = new Size(1060,760); MinimumSize=new Size(680,460);
        foreach (var c in new Control[] {local,remote,pairing,side,screen,device,jitter,mouseRate,gain,swap,automatic,reconnect,connect,importPairing,firewall,updateButton,status,stats,updateStatus}) Theme.Style(c);
        Theme.Primary(connect); Theme.Primary(importPairing); connect.Text="Подключить"; importPairing.Text="Добавить Mac…";
        automatic.Text="Находить Mac по Ethernet автоматически"; reconnect.Text="Восстанавливать соединение автоматически";
        status.Font=Theme.Font(14,FontStyle.Bold); status.ForeColor=Theme.Text; stats.ForeColor=Theme.Muted;
        status.MaximumSize=stats.MaximumSize=updateStatus.MaximumSize=Size.Empty;
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0),Padding=new Padding(0),BackColor=Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var sidebar=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(18,28,18,18),BackColor=Theme.Sidebar,Margin=new Padding(0) };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,130)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,165)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,92));
        var brand=new VerticalStackPanel { Dock=DockStyle.Fill,BackColor=Theme.Sidebar,Margin=Padding.Empty };
        using (var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("EdgeLink.Brand.png")) {
            if (stream != null) { using var original=Image.FromStream(stream); brand.Controls.Add(new PictureBox { Image=new Bitmap(original),Size=new Size(48,48),SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,0,0,12) }); }
        }
        brand.Controls.Add(Theme.Label("EdgeLink",16,true)); sidebar.Controls.Add(brand,0,0);
        var nav=new VerticalStackPanel { Dock=DockStyle.Fill,BackColor=Theme.Sidebar,Margin=Padding.Empty };
        string[] names=["Главная","Подключение","Параметры"];
        for(int i=0;i<names.Length;i++) { int index=i;var b=Theme.Button(names[i]);b.AutoSize=false;b.Size=new Size(154,44);b.Margin=new Padding(0,0,0,8);b.TextAlign=ContentAlignment.MiddleLeft;b.BackColor=Theme.Sidebar;b.Click+=(_,_)=>SelectPage(index);navigation.Add(b);nav.Controls.Add(b); }
        sidebar.Controls.Add(nav,0,1);
        var footer=new VerticalStackPanel { Dock=DockStyle.Fill,BackColor=Theme.Sidebar,Margin=Padding.Empty };
        footer.Controls.Add(Theme.Label("Вернуть курсор на ПК",9,muted:true));footer.Controls.Add(Theme.Label("Ctrl + Alt + Esc",10,true));footer.Controls.Add(Theme.Label("v"+Updates.CurrentVersion,9,muted:true));sidebar.Controls.Add(footer,0,3);
        var body=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(28,24,28,16),BackColor=Theme.Background,Margin=new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,100));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var header=new VerticalStackPanel { Dock=DockStyle.Fill,BackColor=Theme.Background,Margin=Padding.Empty };
        header.Controls.Add(pageTitle);header.Controls.Add(pageHint);content.Margin=Padding.Empty;body.Controls.Add(header,0,0);body.Controls.Add(content,0,1);
        root.Controls.Add(sidebar,0,0);root.Controls.Add(body,1,0);Controls.Add(root);
        dashboardRoot=root;dashboardBody=body;SizeChanged+=(_,_)=>UpdateViewport();Shown+=(_,_)=>FitWindow();
        var home=Page();
        home.Controls.Add(Theme.Card("", "Переводите курсор через край — управление переключится само.",hero,status,Theme.Row(connect),stats));
        home.Controls.Add(Theme.Card("Звук Mac в наушниках ПК","Аудиовыход и буфер выбираются в разделе «Параметры». Звук Windows продолжает работать.",Theme.Label("Подключение идёт по отдельному Ethernet-кабелю.",10,muted:true)));
        var wizard=Page();
        wizard.Controls.Add(Theme.Card("", "",setupProgress,setupHeading,setupHint));
        var stages = new[] {
            Theme.Card("1. Добавить Mac","Соедините свободные Ethernet-порты кабелем. На Mac выдайте разрешения и сохраните файл подключения.",Theme.Label("Откройте файл .edgelink здесь или перетащите его в это окно.",10,muted:true),Theme.Row(importPairing)),
            Theme.Card("2. Выберите наушники","Полученный звук Mac будет смешиваться со звуком Windows.",Theme.Field("Аудиовыход",device),Theme.Field("Mac расположен",side),Theme.Label("Основной монитор и минимальный буфер выбраны автоматически. Их можно изменить позже.",10,muted:true)),
            Theme.Card("3. Подключитесь","Mac должен показывать «Mac ждёт ваш ПК». Если соединение блокируется, настройте firewall одной кнопкой.",Theme.Row(firewall),Theme.Row(CreateWizardConnect()),Theme.Label("Запрос администратора нужен только для правила Windows Firewall.",10,muted:true)),
            Theme.Card("4. Проверьте переход","Доведите курсор до края выбранного экрана в сторону Mac. Для возврата пройдите через противоположный край Mac.",Theme.Label("Ctrl + Alt + Esc немедленно вернёт управление на ПК.",12,true),Theme.Label("Переход выполняется, когда клавиши и кнопки мыши отпущены.",10,muted:true))
        };
        foreach(var p in stages) {setupStages.Add(p);wizard.Controls.Add(p);}
        wizardCard=Theme.Card("","",Theme.Row(setupBack,setupNext));wizard.Controls.Add(wizardCard);
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
        automatic.CheckedChanged+=(_,_)=>{if(manualNetwork!=null)manualNetwork.Visible=!automatic.Checked;};
        side.SelectedIndexChanged+=(_,_)=>{hero.MacLeft=side.SelectedIndex==0;hero.Invalidate();};
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
        for(int i=0;i<navigation.Count;i++){navigation[i].BackColor=i==index ? Theme.Surface : Theme.Sidebar;navigation[i].ForeColor=i==index ? Theme.Mint : Theme.Muted;}
        pageTitle.Text=new[]{"Ваш рабочий стол","Первое подключение","Параметры"}[index];
        pageHint.Text=new[]{"Состояние связи и быстрые действия.","Четыре коротких шага — и можно работать.","Настройте звук, экраны и поведение подключения."}[index];
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
        setupHeading.Text=new[]{HasPairingKey()?"Mac уже добавлен":"Добавьте свой Mac","Куда передавать звук?","Проверим соединение","Всё готово к работе"}[setupStage];
        setupHint.Text=setupStage==2 ? (link!=null ? "Соединение установлено. Можно продолжить." : lastError.Length>0 ? lastError : "Нажмите «Подключить и проверить».") : setupStage==0 && HasPairingKey() ? "Ключ доступа сохранён в Credential Manager." : "Настройки можно изменить в любой момент.";
        setupProgress.Text=$"Шаг {setupStage+1} из 4";setupBack.Enabled=setupStage>0&&!connecting;
        setupNext.Text=setupStage==3 ? "На главный экран" : "Продолжить";
        setupNext.Enabled=!connecting&&(setupStage==0 ? HasPairingKey() : setupStage==2 ? link!=null : true);
        hero.Connected=link!=null;hero.Active=input?.Active==true;hero.MacLeft=side.SelectedIndex==0;hero.Invalidate();
        if(pages.Count>1)pages[1].PerformLayout();
    }
    private void UpdateViewport()
    {
        if(dashboardRoot==null||dashboardBody==null)return;
        bool compact=ClientSize.Width<UiLayout.Px(this,820);
        dashboardRoot.ColumnStyles[0].Width=UiLayout.Px(this,compact?148:190);
        int horizontal=UiLayout.Px(this,compact?16:28);
        dashboardBody.Padding=new Padding(horizontal,UiLayout.Px(this,24),horizontal,UiLayout.Px(this,16));
        foreach(var page in pages)page.PerformLayout();
    }
    private void FitWindow()
    {
        var area=Screen.FromControl(this).WorkingArea;int gap=UiLayout.Px(this,16);
        int maxWidth=Math.Max(1,area.Width-gap*2),maxHeight=Math.Max(1,area.Height-gap*2);
        MinimumSize=new Size(Math.Min(UiLayout.Px(this,680),maxWidth),Math.Min(UiLayout.Px(this,460),maxHeight));
        Size=new Size(Math.Min(Width,maxWidth),Math.Min(Height,maxHeight));
        Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
        UpdateViewport();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);UpdateViewport();
    }
}
