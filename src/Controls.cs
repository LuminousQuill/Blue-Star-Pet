using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BlueStar
{
    sealed class ControlWindow : Window
    {
        readonly StackPanel body = new StackPanel();
        readonly PetWindow pet;
        readonly DispatcherTimer clock=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        Action refresh;
        bool activated;
        static readonly Brush Ink = BrushOf("#141315"), Line = BrushOf("#49403C"),
            Paper = BrushOf("#F2EDE3"), Cyan = BrushOf("#91E5E9"), Copper=BrushOf("#D98E55"),
            Red=BrushOf("#F06A72"), Ivory=BrushOf("#ECE1AD"), Violet=BrushOf("#CA94D9"), Jade=BrushOf("#67CEC1");

        static Brush BrushOf(string hex) { var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        static TextBlock Label(string text, double size, Brush color)
        {
            return new TextBlock { Text=text, FontSize=size, Foreground=color, VerticalAlignment=VerticalAlignment.Center };
        }
        static Button Button(string name, Action click)
        {
            var button=new Button {Content=name, MinHeight=32};
            button.Click += delegate { click(); };
            return button;
        }
        static void Accent(Button b) { b.Background=Cyan; b.Foreground=Ink; b.BorderBrush=Cyan; }

        public ControlWindow(PetWindow pet)
        {
            this.pet=pet;
            Owner=pet; Topmost=pet.Topmost; ShowInTaskbar=false; ResizeMode=ResizeMode.NoResize;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            Title="碧蓝新星 · 工作指令";
            Width=322; SizeToContent=SizeToContent.Height;
            FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI");
            Content=new Border {Background=Ink, BorderBrush=Copper, BorderThickness=new Thickness(1,3,1,1),
                Padding=new Thickness(13,12,13,13), Child=body};
            ShowOrders();
            KeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key==Key.Escape) { Close(); e.Handled=true; } };
            Activated+=delegate { activated=true; };
            Deactivated+=delegate { if(activated && IsVisible) Close(); };
            Loaded+=delegate { pet.PlacePanel(this); clock.Start(); };
            SizeChanged+=delegate { if(IsLoaded) pet.PlacePanel(this); };
            clock.Tick+=delegate { if(refresh!=null) refresh(); };
            Closed+=delegate { clock.Stop(); pet.Persist(); };
        }

        void Header(string title,Brush accent,bool back)
        {
            body.Children.Clear(); refresh=null;
            var header=new DockPanel { LastChildFill=true };
            var close=Button("×",Close); close.Width=32; close.Padding=new Thickness(0);
            close.ToolTip="关闭面板"; close.VerticalAlignment=VerticalAlignment.Top;
            System.Windows.Automation.AutomationProperties.SetName(close,"关闭面板");
            DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
            if(back)
            {
                var home=Button("‹",ShowOrders); home.ToolTip="返回工作指令"; home.Width=30;
                home.FontSize=21; home.Padding=new Thickness(0); home.Margin=new Thickness(0,0,10,0);
                System.Windows.Automation.AutomationProperties.SetName(home,"返回工作指令");
                DockPanel.SetDock(home,Dock.Left); header.Children.Add(home);
            }
            else
            {
                var portrait=new Image { Source=pet.Art.Portrait,Width=43,Height=43,Margin=new Thickness(0,0,9,0) };
                DockPanel.SetDock(portrait,Dock.Left); header.Children.Add(portrait);
            }
            var names=new StackPanel();
            names.Children.Add(Label("碧蓝新星    /    O-03-93    /    ALEPH",10,accent));
            var heading=Label(title,21,Paper); heading.FontWeight=FontWeights.DemiBold; names.Children.Add(heading);
            header.Children.Add(names); body.Children.Add(header);
            Divider();
        }

        public void ShowOrders()
        {
            Header("工作指令",Copper,false);
            var grid=new UniformGrid { Columns=2,Rows=2,Margin=new Thickness(-3) }; body.Children.Add(grid);
            Tile(grid,"本能","Instinct",Red,"#291A1D",BuildControls);
            Tile(grid,"洞察","Insight",Ivory,"#25231D",ShowInsight);
            Tile(grid,"沟通","Attachment",Violet,"#251D29",ShowCommunication);
            Tile(grid,"压迫","Repression",Jade,"#162724",delegate { Close(); pet.SealCore(); });
        }
        void Tile(Panel grid,string title,string icon,Brush color,string tint,Action action)
        {
            var b=Button("",action); b.Style=(Style)FindResource("OrderTile");
            b.Foreground=color; b.Background=BrushOf(tint); b.Margin=new Thickness(3);
            var content=new StackPanel();
            content.Children.Add(new Image { Source=(ImageSource)FindResource(icon),Width=64,Height=62,Margin=new Thickness(0,0,0,3) });
            var name=Label(title,17,color); name.TextAlignment=TextAlignment.Center; name.FontWeight=FontWeights.Bold;
            content.Children.Add(name);
            b.Content=content; System.Windows.Automation.AutomationProperties.SetName(b,title);
            grid.Children.Add(b);
        }
        TextBox TimeField(int value,string name)
        {
            var box=new TextBox { Text=value.ToString("00"), Width=59, Foreground=Paper, Background=Ink,
                BorderBrush=Line, CaretBrush=Ivory, FontSize=22, FontFamily=new FontFamily("Consolas"),
                Padding=new Thickness(2,4,2,4), TextAlignment=TextAlignment.Center,MaxLength=name=="分钟"?3:2 };
            System.Windows.Automation.AutomationProperties.SetName(box,name); return box;
        }
        void ShowInsight()
        {
            Header("洞察 · 观测计时",Ivory,true);
            body.Children.Add(Label("计时结束时，心核将释放一次新星之声",12,Paper));
            if(pet.FocusRunning)
            {
                var time=Label("",40,Ivory); time.FontFamily=new FontFamily("Consolas");
                time.Margin=new Thickness(0,12,0,14); body.Children.Add(time);
                body.Children.Add(Button("取消本次观测",delegate { pet.CancelFocus(); ShowInsight(); }));
                refresh=delegate {
                    if(!pet.FocusRunning) { ShowInsight(); return; }
                    time.Text=PetWindow.ClockText(pet.FocusRemaining);
                };
                refresh(); return;
            }
            var inputs=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,8)};
            var minutes=TimeField((int)pet.FocusDuration.TotalMinutes,"分钟");
            var seconds=TimeField(pet.FocusDuration.Seconds,"秒");
            inputs.Children.Add(minutes); inputs.Children.Add(Label("  分     ",13,Ivory));
            inputs.Children.Add(seconds); inputs.Children.Add(Label("  秒",13,Ivory)); body.Children.Add(inputs);
            var presets=new UniformGrid { Columns=3, Margin=new Thickness(-2,0,-2,10) };
            foreach(int n in new[]{5,15,25})
            {
                int m=n; var preset=Button(m+" 分钟",delegate { minutes.Text=m.ToString("00"); seconds.Text="00"; });
                preset.Margin=new Thickness(2); presets.Children.Add(preset);
            }
            body.Children.Add(presets);
            var error=Label("请输入 0–999 分、0–59 秒，总时长至少 1 秒。",12,Red);
            error.Visibility=Visibility.Collapsed; error.Margin=new Thickness(0,0,0,8); body.Children.Add(error);
            var start=Button("开始洞察",delegate {
                int m,s;
                if(!int.TryParse(minutes.Text,out m) || !int.TryParse(seconds.Text,out s) ||
                   m<0 || m>999 || s<0 || s>59 || m+s==0) { error.Visibility=Visibility.Visible; return; }
                pet.StartFocus(TimeSpan.FromSeconds(60*m+s)); ShowInsight();
            });
            minutes.TextChanged+=delegate { error.Visibility=Visibility.Collapsed; };
            seconds.TextChanged+=delegate { error.Visibility=Visibility.Collapsed; };
            start.Background=Ivory; start.Foreground=Ink; start.IsDefault=true; body.Children.Add(start);
        }
        void ShowCommunication()
        {
            Header("沟通",Violet,true);
            body.Children.Add(Button("请求归还",delegate { Close(); pet.RequestReturn(); }));
            body.Children.Add(Label("找回最近吸入的一批文件，送回星轨。",12,Paper));
            Divider();
            body.Children.Add(Button("回归星之怀抱",delegate { Close(); pet.EmptyBin(); }));
            body.Children.Add(Label("清空系统回收站中的全部项目。",12,Paper));
        }
        void Divider()
        {
            body.Children.Add(new Border { Height=1, Background=Line, Margin=new Thickness(0,12,0,12) });
        }
        void Row(string title, UIElement action)
        {
            var row=new DockPanel { Margin=new Thickness(0,3,0,3) };
            DockPanel.SetDock(action,Dock.Right); row.Children.Add(action);
            row.Children.Add(Label(title,13,Paper)); body.Children.Add(row);
        }
        void AddStartupSetting()
        {
            var toggle=new Button {MinHeight=32,ToolTip="登录 Windows 后自动启动碧蓝新星"};
            System.Windows.Automation.AutomationProperties.SetName(toggle,"开机启动");
            Row("开机启动",toggle);
            var error=Label("",12,Red); error.TextWrapping=TextWrapping.Wrap; body.Children.Add(error);
            bool? enabled=null;
            Action<bool> update=delegate(bool clicked) {
                try
                {
                    if(clicked && enabled.HasValue) StartupAtLogin.SetEnabled(!enabled.Value);
                    enabled=StartupAtLogin.IsEnabled();
                    toggle.Content=enabled.Value?"开启":"关闭";
                    error.Visibility=Visibility.Collapsed;
                }
                catch(Exception ex)
                {
                    if(!(ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException)) throw;
                    enabled=null; toggle.Content="重试";
                    error.Text=ex is System.IO.PathTooLongException?ex.Message:"无法访问 Windows 启动项，请检查当前账户权限后重试。";
                    error.Visibility=Visibility.Visible;
                }
            };
            toggle.Click+=delegate { update(true); }; update(false);
        }
        void BuildControls()
        {
            Header("本能 · 收容设置",Red,true);
            var sound=Button(pet.Settings.Sound?"开启":"关闭",delegate {});
            sound.Click+=delegate { pet.SetSound(!pet.Settings.Sound); sound.Content=pet.Settings.Sound?"开启":"关闭"; };
            Row("互动音效",sound);
            var level=Label(pet.Settings.Volume+"%",12,Cyan);
            level.Width=38; level.TextAlignment=TextAlignment.Right;
            var slider=new Slider {Minimum=0,Maximum=100,Value=pet.Settings.Volume,Width=132,
                TickFrequency=1,IsSnapToTickEnabled=true,IsMoveToPointEnabled=true,
                SmallChange=1,LargeChange=10,VerticalAlignment=VerticalAlignment.Center};
            System.Windows.Automation.AutomationProperties.SetName(slider,"总音量");
            slider.ValueChanged+=delegate {
                pet.SetVolume((int)Math.Round(slider.Value)); level.Text=pet.Settings.Volume+"%";
            };
            var volume=new StackPanel {Orientation=Orientation.Horizontal,MinHeight=28};
            volume.Children.Add(slider); volume.Children.Add(level); Row("总音量",volume);
            var top=Button(pet.Topmost?"开启":"关闭",delegate {});
            top.Click+=delegate { pet.Topmost=!pet.Topmost; Topmost=pet.Topmost; pet.Settings.Pinned=pet.Topmost;
                top.Content=pet.Topmost?"开启":"关闭"; pet.Persist(); };
            Row("始终置顶",top);
            var roam=Button(pet.Settings.Roaming?"开启":"关闭",delegate {});
            roam.ToolTip="在附近缓慢游荡，偶尔闪现到另一处";
            System.Windows.Automation.AutomationProperties.SetName(roam,"自行移动");
            roam.Click+=delegate { pet.SetRoaming(!pet.Settings.Roaming); roam.Content=pet.Settings.Roaming?"开启":"关闭"; };
            Row("自行移动",roam);
            AddStartupSetting();
            var sizes=new StackPanel { Orientation=Orientation.Horizontal };
            var buttons=new Button[3];
            string[] labels={"小","中","大"};
            for (int i=0;i<3;i++)
            {
                int selected=i;
                buttons[i]=Button(labels[i],delegate {
                    pet.SetSize(selected);
                    foreach (var b in buttons) { b.ClearValue(BackgroundProperty); b.ClearValue(ForegroundProperty); b.ClearValue(BorderBrushProperty); }
                    Accent(buttons[selected]); pet.PlacePanel(this);
                });
                buttons[i].Margin=new Thickness(3,0,0,0); sizes.Children.Add(buttons[i]);
                if (i==pet.Settings.SizeIndex) Accent(buttons[i]);
            }
            Row("桌宠大小",sizes); Divider();
            var bottom=new DockPanel();
            var exit=Button("退出桌宠",delegate { Close(); pet.Close(); });
            DockPanel.SetDock(exit,Dock.Right); bottom.Children.Add(exit);
            var bin=Button("打开回收站",delegate { Close(); Process.Start("explorer.exe","shell:RecycleBinFolder"); });
            bin.HorizontalAlignment=HorizontalAlignment.Left; bottom.Children.Add(bin); body.Children.Add(bottom);
        }
    }
}
