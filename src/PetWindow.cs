using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BlueStar
{
    public sealed class Preferences : ApplicationSettingsBase
    {
        [UserScopedSetting, SettingsSerializeAs(SettingsSerializeAs.Xml)]
        public AbsorbedBatch[] Absorptions { get {return (AbsorbedBatch[])this["Absorptions"];} set {this["Absorptions"]=value;} }
        [UserScopedSetting, DefaultSettingValue("1")]
        public int SizeIndex { get {return (int)this["SizeIndex"];} set {this["SizeIndex"]=value;} }
        [UserScopedSetting, DefaultSettingValue("True")]
        public bool Pinned { get {return (bool)this["Pinned"];} set {this["Pinned"]=value;} }
        [UserScopedSetting, DefaultSettingValue("True")]
        public bool Sound { get {return (bool)this["Sound"];} set {this["Sound"]=value;} }
        [UserScopedSetting, DefaultSettingValue("False")]
        public bool Roaming { get {return (bool)this["Roaming"];} set {this["Roaming"]=value;} }
        [UserScopedSetting, DefaultSettingValue("100")]
        public int Volume { get {return (int)this["Volume"];} set {this["Volume"]=value;} }
        [UserScopedSetting, DefaultSettingValue("NaN")]
        public double X { get {return (double)this["X"];} set {this["X"]=value;} }
        [UserScopedSetting, DefaultSettingValue("NaN")]
        public double Y { get {return (double)this["Y"];} set {this["Y"]=value;} }
    }

    sealed partial class PetWindow : Window
    {
        public readonly Preferences Settings=new Preferences();
        public readonly PetArt Art=new PetArt();
        readonly Stopwatch elapsed=Stopwatch.StartNew();
        TimeSpan lastRenderTime=TimeSpan.MinValue;
        readonly TextBlock message=new TextBlock { Foreground=Brushes.AliceBlue, FontSize=12,
            FontFamily=new FontFamily("Microsoft YaHei UI"), TextAlignment=TextAlignment.Center,
            TextWrapping=TextWrapping.Wrap };
        readonly Border bubble;
        readonly PetAudio audio;
        readonly DispatcherTimer binTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1.5)};
        DropBatch hoverBatch;
        bool binHadItems, checkingBin, binPulseQueued, ambientReleasePending, binCommand;
        int binRevision;
        double hoverWeight, ambient=-10;
        readonly double[] sizes={210,290,380};
        double last, pulse=-10, eject=-10, meal=-10, messageUntil, leanVelocity;
        Vector velocity;
        bool coasting, releasePending;
        ResonanceWave wave;
        MeteorWindow meteor;
        readonly Random idleRandom=new Random();
        readonly IdleAct[] idleActs={IdleAct.Wind,IdleAct.Meteor,IdleAct.Wind,IdleAct.Listen,IdleAct.Meteor,IdleAct.Wind,
            IdleAct.Probe,IdleAct.Dip,IdleAct.Stardust,IdleAct.Curl,IdleAct.Bank,IdleAct.Glide,IdleAct.Constellation};
        int idleIndex;
        double idleWait,lastLegTap=-10;
        double waveBorn;
        DateTime focusEnd;
        public TimeSpan FocusDuration { get; private set; }
        public bool FocusRunning { get { return focusEnd!=DateTime.MinValue; } }
        bool pressed, moving, draggingFiles, handlingDrop, physicalPress;
        Point pressPixel,windowOrigin,dragTarget,sampledDragTarget;
        Point? mouseDownPixel;
        bool pressedHeart;
        int pressedLeg=-1;
        Matrix dragFromDevice;
        double motionStamp,heldTargetTime;
        ControlWindow panel;
        public double FocusRemaining { get {return Math.Max(0,(focusEnd-DateTime.UtcNow).TotalSeconds);} }

        public PetWindow()
        {
            Title="碧蓝新星"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=false; ShowActivated=false; Topmost=Settings.Pinned;
            Settings.SizeIndex=Math.Max(0,Math.Min(2,Settings.SizeIndex));
            Width=Height=sizes[Settings.SizeIndex]; WindowStartupLocation=WindowStartupLocation.Manual;
            Rect work=SystemParameters.WorkArea;
            Left=double.IsNaN(Settings.X)?work.Right-Width-24:Settings.X;
            Top=double.IsNaN(Settings.Y)?work.Bottom-Height-16:Settings.Y;
            Art.IsHitTestVisible=false;
            grid.Children.Add(Art);
            grid.Children.Add(orbits);
            bubble=new Border { Child=message, Background=new SolidColorBrush(Color.FromArgb(240,16,29,40)),
                BorderBrush=new SolidColorBrush(Color.FromRgb(66,103,120)), BorderThickness=new Thickness(1),
                CornerRadius=new CornerRadius(8), Padding=new Thickness(8,3,8,4), Margin=new Thickness(2),
                VerticalAlignment=VerticalAlignment.Bottom, HorizontalAlignment=HorizontalAlignment.Center,
                Visibility=Visibility.Hidden, IsHitTestVisible=false };
            grid.Children.Add(bubble); Content=grid;
            AllowDrop=true; DragEnter+=DragOverPet; DragOver+=DragOverPet;
            DragLeave+=delegate {
                if(!draggingFiles) return;
                // A hand-drawn loop can briefly leave this window near the tip of a leg.
                fileLeftAt=elapsed.Elapsed.TotalSeconds; draggingFiles=false;
                audio.EndHover(); orbits.ClearPointer();
            }; Drop+=FilesDropped;
            MouseLeftButtonDown+=Press; MouseLeftButtonUp+=Lift;
            MouseMove+=MoveFileStar;
            PreviewKeyDown+=delegate(object s,KeyEventArgs e) {
                if(e.Key==Key.Escape && orbits.Grabbed!=null) { ReleaseMouseCapture(); e.Handled=true; }
            };
            QueryCursor+=delegate(object s,QueryCursorEventArgs e) {
                if(orbits.Grabbed!=null || orbits.Hit(e.GetPosition(this))!=null) { e.Cursor=Cursors.Hand; e.Handled=true; }
            };
            SourceInitialized+=delegate { ((HwndSource)PresentationSource.FromVisual(this)).AddHook(ObserveDragInput); };
            LostMouseCapture+=delegate {
                if(pressed) EndDrag();
                if(orbits.Grabbed!=null) { orbits.Grabbed=null; EndFileHover(); }
            };
            MouseRightButtonUp+=delegate { if (!handlingDrop) OpenPanel(); };
            Settings.Volume=Math.Max(0,Math.Min(100,Settings.Volume));
            audio=new PetAudio(Settings.Sound,Settings.Volume);
            roaming=Settings.Roaming;
            binTimer.Tick+=PollBin;
            FocusDuration=TimeSpan.FromMinutes(25);
            idleWait=9+idleRandom.NextDouble()*9;
            Loaded+=delegate { FitToScreen(); SetPresence(0); last=elapsed.Elapsed.TotalSeconds; audio.PlayMotion(PetCue.Appear); Art.Draw(); CompositionTarget.Rendering+=OnFrame; PollBin(null,EventArgs.Empty); binTimer.Start(); };
            Closed+=delegate { CompositionTarget.Rendering-=OnFrame; binTimer.Stop(); EndFileHover(); Persist(); audio.Dispose(); };
        }
        public void Chime() { audio.Play(PetCue.Chime); }
        public void SetSound(bool enabled)
        {
            Settings.Sound=enabled;
            audio.SetEnabled(enabled);
            if(enabled) Chime();
            Persist();
        }
        public void SetVolume(int percent)
        {
            Settings.Volume=Math.Max(0,Math.Min(100,percent));
            audio.SetVolume(Settings.Volume);
        }
        async void PollBin(object sender,EventArgs e)
        {
            if(checkingBin || handlingDrop || binCommand) return;
            checkingBin=true; double lastMeal=meal; int revision=binRevision;
            try
            {
                long? count=await Task.Run(()=>RecycleBin.ItemCount());
                if(IsLoaded && !handlingDrop && !binCommand && binRevision==revision && meal==lastMeal && count.HasValue) ObserveBin(count.Value);
            }
            finally { checkingBin=false; }
        }
        void ObserveBin(long count)
        {
            if(count==0 && binHadItems) binPulseQueued=true;
            binHadItems=count>0;
        }
        void StartResonance()
        {
            if(releasePending) return;
            pulse=elapsed.Elapsed.TotalSeconds; releasePending=true;
            audio.Play(PetCue.Charge);
        }
        public void Persist() { Point home=roamHome??new Point(Left,Top); Settings.X=home.X; Settings.Y=home.Y; try { Settings.Save(); } catch(ConfigurationErrorsException) {} }
        public void SetSize(int i)
        {
            ResetRoam();
            double old=Width; Settings.SizeIndex=i; Width=Height=sizes[i];
            Left+=(old-Width)/2; Top+=(old-Height)/2; FitToScreen(); Persist();
        }
        internal Rect ScreenArea(bool fullDisplay=false)
        {
            var source=PresentationSource.FromVisual(this);
            if (source==null) return SystemParameters.WorkArea;
            var monitor=Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var screen=fullDisplay?monitor.Bounds:monitor.WorkingArea;
            var m=source.CompositionTarget.TransformFromDevice;
            return new Rect(m.Transform(new Point(screen.Left,screen.Top)),m.Transform(new Point(screen.Right,screen.Bottom)));
        }
        void FitToScreen()
        {
            Rect r=ScreenArea(); Left=Math.Max(r.Left,Math.Min(Left,r.Right-Width)); Top=Math.Max(r.Top,Math.Min(Top,r.Bottom-Height));
        }
        public void PlacePanel(Window w)
        {
            Rect r=ScreenArea(); double x=Left+Width*0.79;
            if (x+w.Width>r.Right-6) x=Left+Width*0.21-w.Width;
            w.Left=Math.Max(r.Left+4,Math.Min(x,r.Right-w.Width-4));
            w.Top=Math.Max(r.Top+4,Math.Min(Top+Height*0.35-w.ActualHeight/2,r.Bottom-w.ActualHeight-4));
        }
        void OpenPanel()
        {
            if (panel!=null) { panel.ShowOrders(); panel.Activate(); return; }
            panel=new ControlWindow(this); panel.Closed+=delegate {panel=null;};
            panel.Show(); if (panel!=null) panel.Activate();
        }
        public static string ClockText(double seconds)
        {
            int whole=(int)Math.Ceiling(Math.Max(0,seconds));
            return (whole/60).ToString("00")+":"+(whole%60).ToString("00");
        }
        public void StartFocus(TimeSpan duration)
        {
            FocusDuration=duration; focusEnd=DateTime.UtcNow+duration;
            Art.FocusProgress=0;
            Art.Idle.Stop(); roamStep=RoamStep.Rest; Art.Swim.Strength=0;
            if(meteor!=null) { meteor.Close(); meteor=null; }
            SetPresence(1); audio.FadeMotion();
            Say("洞察观测  "+ClockText(duration.TotalSeconds),3);
        }
        public void CancelFocus()
        {
            focusEnd=DateTime.MinValue; Art.FocusProgress=-1; Say("观测已取消",2);
        }
        public async void RequestReturn()
        {
            if(binCommand || handlingDrop) return;
            binCommand=true; binRevision++;
            try
            {
                var history=new List<AbsorbedBatch>(Settings.Absorptions??new AbsorbedBatch[0]);
                var paths=await RecycleBin.OnShellThread(()=>RecycleBin.RestoreLatest(history));
                Settings.Absorptions=history.ToArray(); Persist();
                long? count=await Task.Run(()=>RecycleBin.ItemCount());
                if(count.HasValue) binHadItems=count.Value>0;
                if(paths.Length==0) { Say("沉默不语",3); return; }
                eject=elapsed.Elapsed.TotalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(PetArt.EjectBeat));
                if(!IsLoaded) return;
                orbits.Store(paths,new Vector(1,-1),1,elapsed.Elapsed.TotalSeconds);
                Say("已归还 "+paths.Length+" 项",3);
            }
            catch(COMException) { Say("沉默不语",3); }
            finally { binCommand=false; }
        }
        public async void EmptyBin()
        {
            if(binCommand || handlingDrop) return;
            binCommand=true; binRevision++;
            try
            {
                long? before=await Task.Run(()=>RecycleBin.ItemCount());
                if(!before.HasValue || before==0) { Say("沉默不语",3); return; }
                IntPtr owner=new WindowInteropHelper(this).Handle;
                int result=await RecycleBin.OnShellThread(()=>RecycleBin.SHEmptyRecycleBinW(owner,null,0));
                long? after=await Task.Run(()=>RecycleBin.ItemCount());
                if(result>=0 && after==0)
                {
                    Settings.Absorptions=new AbsorbedBatch[0]; Persist();
                    binHadItems=false; binPulseQueued=true;
                }
                else if(result<0 && result!=unchecked((int)0x800704C7) && result!=unchecked((int)0x80004004))
                    Say("沉默不语",3);
            }
            finally { binCommand=false; }
        }
        OrbitShelf shelf;
        public void SealCore()
        {
            if(orbits.Sealed) return;
            orbits.Sealed=true;
            if(shelf==null)
            {
                shelf=new OrbitShelf(this,orbits);
                shelf.Owner=this;
                shelf.ArrangeBeside(ScreenArea());
                shelf.Show();
            }
        }
        void Say(string text,double duration)
        { message.Text=text; bubble.Visibility=Visibility.Visible; messageUntil=elapsed.Elapsed.TotalSeconds+duration; }
        void OnFrame(object sender,EventArgs args)
        {
            var frame=(RenderingEventArgs)args;
            if(frame.RenderingTime==lastRenderTime) return;
            lastRenderTime=frame.RenderingTime;
            Tick(sender,args);
        }
        bool InteractionBusy(double now)
        {
            return pressed || coasting || draggingFiles || handlingDrop || binCommand || orbits.Sealed || orbits.Grabbed!=null ||
                exportingStar!=null || panel!=null || starFiles!=null || FocusRunning || wave!=null || now-pulse<2.4 ||
                now-meal<2.1 || now-eject<1.4 || now-ambient<2.5 || now-lastLegTap<1.35;
        }
        void AdvanceIdle(double now,double dt,bool mouseNear)
        {
            bool busy=InteractionBusy(now) || roamStep!=RoamStep.Rest || Art.Presence<1;
            if(Art.Idle.Act!=IdleAct.None)
            {
                if(busy && Art.Idle.Act==IdleAct.Constellation) audio.FadeMotion();
                Art.Idle.Advance(dt,busy,mouseNear); return;
            }
            if(busy || (idleWait-=dt)>0) return;
            if(idleIndex==0)
                for(int i=idleActs.Length-1;i>0;i--)
                {
                    int j=idleRandom.Next(i+1); var swap=idleActs[i]; idleActs[i]=idleActs[j]; idleActs[j]=swap;
                }
            var act=idleActs[idleIndex]; idleIndex=(idleIndex+1)%idleActs.Length;
            if(act==IdleAct.Listen && mouseNear) act=IdleAct.Wind;
            idleWait=16+idleRandom.NextDouble()*18;
            double angle=(act==IdleAct.Wind?18+idleRandom.NextDouble()*15:20+idleRandom.NextDouble()*40)*Math.PI/180;
            var dir=new Vector(Math.Cos(angle)*(idleRandom.Next(2)==0?-1:1),Math.Sin(angle)*(idleRandom.Next(2)==0?-1:1));
            if(act==IdleAct.Probe) dir=new Vector(Math.Sign(dir.X)*Math.Sin(angle),Math.Cos(angle));
            MeteorFlight flight=null;
            if(act==IdleAct.Meteor)
            {
                Rect screen=ScreenArea();
                var center=new Point(Left+Art.Heart.X,Top+Art.Heart.Y);
                if(!screen.Contains(center)) return;
                flight=MeteorFlight.Choose(idleRandom,center,Width/300*Art.CoreSize,Art.CoreTilt,screen);
                dir=flight.CatchSide; meteor=new MeteorWindow(this,flight,screen);
            }
            Art.BeginIdle(act,dir,flight);
            if(act==IdleAct.Meteor) meteor.Show();
            if(act==IdleAct.Constellation) audio.PlayMotion(PetCue.Constellation);
        }
        void Tick(object sender,EventArgs args)
        {
            double now=elapsed.Elapsed.TotalSeconds, dt=Math.Min(0.13,now-last); last=now;
            if(exiting)
            {
                Art.Swim.Settle(dt);
                AdvanceExit(now);
                Art.Time=now; Art.Draw(); return;
            }
            var position=Forms.Cursor.Position;
            var pixel=new Point(position.X,position.Y);
            SampleMotion(now,pixel);
            var cursor=PointFromScreen(pixel);
            if(AdvanceRoaming(now,dt,cursor)) cursor=PointFromScreen(pixel);
            UpdateFileSurface(cursor);
            if(!orbits.Sealed && orbits.Grabbed!=null && starMoved)
            {
                bool inside=Art.InsideHeart(cursor);
                if(inside && !draggingFiles) StartFileHover(orbits.Grabbed.Paths,true);
                else if(!inside && draggingFiles) EndFileHover();
            }
            double x=(cursor.X-Width/2)/(Width*0.55), y=(cursor.Y-Height*0.43)/(Height*0.55);
            double near=Math.Min(1,Math.Max(0,1.18-Math.Sqrt(x*x+y*y)*0.52)), blend=1-Math.Exp(-9*dt);
            double aim=moving || coasting?0:near;
            if(!FocusRunning)
            {
                Art.AimX+=(Math.Max(-1,Math.Min(1,x))*aim-Art.AimX)*blend;
                Art.AimY+=(Math.Max(-1,Math.Min(1,y))*aim-Art.AimY)*blend;
                Art.Attention+=(near-Art.Attention)*blend;
            }
            if(binPulseQueued && !releasePending && wave==null)
            { binPulseQueued=false; StartResonance(); }
            audio.Advance(dt);
            double remaining=FocusRemaining;
            bool focusDone=FocusRunning && remaining==0;
            if(focusDone)
            {
                focusEnd=DateTime.MinValue;
                ambient=now; ambientReleasePending=true;
                Say("洞察完成 · 新星发来回响",5);
            }
            Art.AmbientAge=now-ambient;
            Art.EjectAge=now-eject;
            Art.Seal=Math.Max(0,Math.Min(1,Art.Seal+dt*(orbits.Sealed?1.55:-1.8)));
            Art.Gravity+=(((draggingFiles || handlingDrop)?1:0)-Art.Gravity)*blend;
            Art.HoverWeight+=(hoverWeight-Art.HoverWeight)*blend;
            Art.Time=now; Art.PulseAge=now-pulse; Art.MealAge=now-meal;
            Art.FocusProgress=remaining>0?1-remaining/FocusDuration.TotalSeconds:-1;
            if(draggingFiles)
            { message.Text=orbits.Sealed?"拖至心核松开暂存":canRecycleHover && Art.InsideHeart(cursor)?"松开移入回收站":"五秒内绕心两圈暂存"; bubble.Visibility=Visibility.Visible; }
            else if (now>messageUntil)
            {
                if (remaining>0 && IsMouseOver && panel==null) { message.Text="观测中  "+ClockText(remaining); bubble.Visibility=Visibility.Visible; }
                else bubble.Visibility=Visibility.Hidden;
            }
            AdvanceIdle(now,dt,(cursor-Art.Heart).Length<Width*0.64);
            Art.Draw();
            if(meteor!=null)
            {
                if(Art.Idle.Act!=IdleAct.Meteor || Art.Idle.Age>MeteorFlight.Impact+MeteorFlight.TailLife)
                { meteor.Close(); meteor=null; }
                else meteor.Advance(Art.Idle.Age,new Point(Left+Art.Heart.X,Top+Art.Heart.Y),Art.CoreSize*Width/300,Art.CoreTilt,Art.Idle.Weight);
            }
            if(draggingFiles && orbits.Grabbed==null) orbits.Pointer=cursor;
            if(shelf!=null) shelf.UpdatePlacement();
            orbits.Advance(Art.Heart,now);
            if(shelf!=null)
            {
                if(!orbits.Sealed && orbits.Fold==0) { shelf.Close(); shelf=null; }
                else shelf.Refresh();
            }
            if(ambientReleasePending && Art.AmbientAge>=PetArt.AmbientWindup)
            {
                ambientReleasePending=false;
                audio.Play(PetCue.Ambient);
            }
            if (releasePending && Art.PulseAge>=PetArt.ReleaseBeat)
            {
                releasePending=false;
                if (wave!=null) wave.Close();
                var center=new Point(Left+Art.Heart.X,Top+Art.Heart.Y);
                wave=new ResonanceWave(this,ScreenArea(true),center);
                waveBorn=now;
                audio.Play(PetCue.Resonance);
                wave.Show();
            }
            if (wave!=null)
            {
                if (now-waveBorn<ResonanceWave.Duration) wave.Advance(now-waveBorn);
                else { wave.Close(); wave=null; }
            }
        }

        void SampleMotion(double now,Point cursorPixel)
        {
            if(pressed)
            {
                if(!physicalPress || DragButtonDown()) UpdateDragTarget(cursorPixel);
                else if(moving) FinishPress();
            }
            double dt=Math.Min(0.13,now-motionStamp);
            motionStamp=now;
            if(dt>0) AdvanceMotion(dt);
        }
        void UpdateDragTarget(Point pixel)
        {
            Vector delta=dragFromDevice.Transform(pixel-pressPixel);
            if(!moving && delta.LengthSquared<25) return;
            if(!moving) ResetRoam();
            moving=true;
            dragTarget=windowOrigin+delta;
        }
        void AdvanceMotion(double dt)
        {
            // Keep the spring and the body's tilt on the same clock at every refresh rate.
            var motionPosition=new Point(Left,Top);
            bool targetStill=dragTarget==sampledDragTarget;
            sampledDragTarget=dragTarget;
            for(double left=dt;left>0.00001;)
            {
                double h=Math.Min(left,1.0/120); left-=h;
                if(moving)
                {
                    heldTargetTime=targetStill?heldTargetTime+h:0;
                    // Ease into the stop so the body carries past a held target and drifts back.
                    double settle=Math.Max(0,Math.Min(1,(heldTargetTime-0.06)/0.12));
                    Vector pull=dragTarget-motionPosition;
                    double extraDamping=7*settle;
                    Vector acceleration=pull*(36+28*settle)-velocity*(5+extraDamping);
                    double force=acceleration.Length;
                    double limit=2800;
                    if(settle>0 && Vector.Multiply(acceleration,velocity)<0) limit+=velocity.Length*extraDamping;
                    if(force>limit) acceleration*=limit/force;
                    velocity+=acceleration*h;
                }
                else velocity*=Math.Exp(-2.75*h);
                if(moving || coasting) motionPosition+=velocity*h;
                double targetLean=Math.Max(-0.21,Math.Min(0.21,velocity.X/2700));
                leanVelocity+=((targetLean-Art.DragLean)*58-leanVelocity*9)*h;
                Art.DragLean+=leanVelocity*h;
                Art.DragLift+=(Math.Max(-1,Math.Min(1,velocity.Y/680))-Art.DragLift)*(1-Math.Exp(-7*h));
            }
            if(!moving) Art.Wake.Settle(dt);
            if (!moving && !coasting) return;
            if(coasting)
            {
                var area=ScreenArea();
                double x=Math.Max(area.Left,Math.Min(area.Right-Width,motionPosition.X));
                double y=Math.Max(area.Top,Math.Min(area.Bottom-Height,motionPosition.Y));
                if(x!=motionPosition.X) velocity.X=0;
                if(y!=motionPosition.Y) velocity.Y=0;
                motionPosition=new Point(x,y);
            }
            if(moving) Art.Wake.Follow((motionPosition-new Point(Left,Top))/dt,dt);
            Left=motionPosition.X; Top=motionPosition.Y;
            if (coasting && velocity.Length<2) { coasting=false; velocity=new Vector(); Persist(); }
        }
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        static bool DragButtonDown()
        {
            return GetAsyncKeyState(Forms.SystemInformation.MouseButtonsSwapped?2:1)<0;
        }
        IntPtr ObserveDragInput(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            // Use the coordinates of the actual gesture, including flicks between render frames.
            const int mouseMove=0x200,leftDown=0x201,leftUp=0x202,leftDoubleClick=0x203;
            bool down=msg==leftDown || msg==leftDoubleClick;
            bool heldMove=msg==mouseMove && (wParam.ToInt64()&1)!=0;
            if(!down && !(pressed && physicalPress && (heldMove || msg==leftUp))) return IntPtr.Zero;
            long xy=lParam.ToInt64();
            Point pixel=PointToScreen(new Point())+new Vector((short)xy,(short)(xy>>16));
            if(down) mouseDownPixel=pixel;
            else
            {
                UpdateDragTarget(pixel);
                if(msg==leftUp) FinishPress();
            }
            return IntPtr.Zero;
        }
        void Press(object sender, MouseButtonEventArgs e)
        {
            if (handlingDrop || binCommand) return;
            if(PressFileStar(e)) return;
            physicalPress=e.StylusDevice==null;
            pressPixel=physicalPress && mouseDownPixel.HasValue?mouseDownPixel.Value:PointToScreen(e.GetPosition(this));
            var point=PointFromScreen(pressPixel);
            pressedHeart=Art.InsideHeart(point);
            pressedLeg=pressedHeart?-1:Art.LegAt(point);
            mouseDownPixel=null;
            dragFromDevice=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            sampledDragTarget=dragTarget=windowOrigin=new Point(Left,Top);
            heldTargetTime=0;
            motionStamp=elapsed.Elapsed.TotalSeconds;pressed=true;moving=false;coasting=false;velocity=new Vector();
            CaptureMouse(); e.Handled=true;
        }
        void EndDrag()
        {
            pressed=false;physicalPress=false;
            if (moving)
            {
                Art.Wake.Release();
                // A short flick stores some pull even before the body has had time to accelerate.
                Vector pull=dragTarget-new Point(Left,Top);
                double carry=Math.Min(85,pull.Length*1.8);
                if(velocity.Length<carry)
                {
                    Vector direction=velocity.Length>8?velocity:pull;
                    direction.Normalize(); velocity=direction*carry;
                }
                double limit=Width*2.8;
                if(velocity.Length>limit) velocity*=limit/velocity.Length;
                coasting=true;
            }
            moving=false;
        }
        void Lift(object sender,MouseButtonEventArgs e)
        {
            if(ReleaseFileStar(e)) return;
            if(pressed && !physicalPress) UpdateDragTarget(PointToScreen(e.GetPosition(this)));
            FinishPress();
        }
        void FinishPress()
        {
            if (!pressed) return;
            bool click=!moving;
            EndDrag();
            if(IsMouseCaptured) ReleaseMouseCapture();
            if(click && pressedHeart)
            {
                if(orbits.Sealed) { CloseStarFiles(); orbits.Sealed=false; }
                else StartResonance();
            }
            else if(click && pressedLeg>=0) { lastLegTap=elapsed.Elapsed.TotalSeconds; Art.BendLeg(pressedLeg,lastLegTap); }
        }
        void DragOverPet(object sender,DragEventArgs e)
        {
            if(fileDragLatched && latchReleased && (e.KeyStates&DragDropKeyStates.LeftMouseButton)!=0)
                fileDragLatched=latchReleased=false;
            var point=e.GetPosition(this);
            bool returning=exportingStar!=null;
            bool accepted=!binCommand && !handlingDrop && (returning?!orbits.Sealed && Art.InsideHeart(point):!fileDragLatched) && e.Data.GetDataPresent(DataFormats.FileDrop,false) &&
                (e.AllowedEffects&(DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link))!=0;
            if(accepted && !draggingFiles)
            {
                string[] paths=DropPaths(e);
                accepted=paths.Length>0;
                if(accepted) StartFileHover(paths,(e.AllowedEffects&DragDropEffects.Move)!=0);
            }
            if(!accepted) EndFileHover();
            e.Effects=DragDropEffects.None; e.Handled=true;
            if(!accepted) return;
            orbits.Pointer=point;
            if(orbits.Sealed)
            {
                if(Art.InsideHeart(point))
                    e.Effects=(e.AllowedEffects&DragDropEffects.Copy)!=0?DragDropEffects.Copy:
                        (e.AllowedEffects&DragDropEffects.Link)!=0?DragDropEffects.Link:DragDropEffects.Move;
                return;
            }
            if(returning) { e.Effects=e.AllowedEffects&DragDropEffects.Move; return; }
            Vector offset=point-Art.Heart;
            double now=elapsed.Elapsed.TotalSeconds;
            int direction=gesture.Trace(offset,ActualWidth,now);
            if(direction!=0 && (e.KeyStates&DragDropKeyStates.LeftMouseButton)!=0)
            {
                orbits.Store(hoverBatch.Paths,offset,direction,now);
                fileDragLatched=true; latchReleased=false;
                EndFileHover();
                // The originating window owns the OLE drag loop. Escape ends it without a source-side drop.
                if(DragButtonDown()) CancelShellDrag();
                Say("已暂存 · 点击星星查看，向外拖出取用",4);
                return;
            }
            if(canRecycleHover && Art.InsideHeart(point)) e.Effects=DragDropEffects.Move;
            else if((e.AllowedEffects&DragDropEffects.Copy)!=0) e.Effects=DragDropEffects.Copy;
        }
        void StartFileHover(string[] paths,bool canRecycle)
        {
            if(fileLeftAt>=0 && (elapsed.Elapsed.TotalSeconds-fileLeftAt>ReentryGrace ||
                !paths.SequenceEqual(hoverBatch.Paths,StringComparer.OrdinalIgnoreCase))) EndFileHover();
            fileLeftAt=-1;
            if(hoverBatch==null)
            {
                hoverBatch=new DropBatch(paths);
                ReadHoverSize(hoverBatch);
            }
            draggingFiles=true; canRecycleHover=canRecycle; messageUntil=0;
            audio.StartHover();
        }
        async void ReadHoverSize(DropBatch batch)
        {
            try
            {
                long[] sizes=await batch.Sizes;
                if(hoverBatch==batch) hoverWeight=DropBatch.Weight(sizes.Sum());
            }
            catch(OperationCanceledException) { }
        }
        void EndFileHover()
        {
            if(draggingFiles) audio.EndHover();
            gesture.Reset(); orbits.ClearPointer();
            draggingFiles=false; hoverWeight=0; fileLeftAt=-1;
            if(hoverBatch!=null) { hoverBatch.Dispose(); hoverBatch=null; }
        }
        static string[] DropPaths(DragEventArgs e)
        {
            var paths=e.Data.GetData(DataFormats.FileDrop,false) as string[];
            if(paths==null) return new string[0];
            var selected=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string path in paths)
            {
                string full;
                try { full=Path.GetFullPath(path); }
                catch(ArgumentException) { return new string[0]; }
                catch(NotSupportedException) { return new string[0]; }
                catch(IOException) { return new string[0]; }
                string root=Path.GetPathRoot(full);
                full=full.TrimEnd('\\','/');
                if(full.Equals(root.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)) return new string[0];
                selected.Add(full);
            }
            return selected.Where(path=>{
                for(string parent=Path.GetDirectoryName(path);parent!=null;parent=Path.GetDirectoryName(parent))
                    if(selected.Contains(parent)) return false;
                return true;
            }).ToArray();
        }
        void FilesDropped(object sender,DragEventArgs e)
        {
            // The shell operation belongs to this process. Never request source-side deletion.
            e.Effects=DragDropEffects.None; e.Handled=true;
            if (binCommand || handlingDrop || (exportingStar==null && fileDragLatched) || !Art.InsideHeart(e.GetPosition(this)) ||
                (e.AllowedEffects&(orbits.Sealed?(DragDropEffects.Copy|DragDropEffects.Link|DragDropEffects.Move):DragDropEffects.Move))==0 ||
                !e.Data.GetDataPresent(DataFormats.FileDrop,false))
            { EndFileHover(); return; }
            var paths=DropPaths(e);
            if(paths.Length==0) { EndFileHover(); Say("此项目无法投递",3); return; }
            if(orbits.Sealed)
            {
                EndFileHover();
                if(exportingStar==null)
                {
                    orbits.Store(paths,e.GetPosition(this)-Art.Heart,1,elapsed.Elapsed.TotalSeconds);
                    Say("已暂存 "+paths.Length+" 项",2);
                }
                return;
            }
            if(exportingStar!=null && paths.Except(exportingStar.Paths,StringComparer.OrdinalIgnoreCase).Any())
            { EndFileHover(); return; }
            BeginRecycle(paths,exportingStar);
        }
        void BeginRecycle(string[] paths,FileStar star)
        {
            DropBatch batch;
            if(hoverBatch!=null && paths.SequenceEqual(hoverBatch.Paths,StringComparer.OrdinalIgnoreCase))
            { batch=hoverBatch; hoverBatch=null; }
            else batch=new DropBatch(paths);
            EndFileHover();
            handlingDrop=true;
            if(star!=null) orbits.Remove(star);
            Dispatcher.BeginInvoke(new Action(delegate { RecycleDrop(batch,star); }));
        }
        async void RecycleDrop(DropBatch batch,FileStar star)
        {
            try
            {
                if (panel!=null) panel.Close();
                Say("正在移入回收站…",3600);
                string[] files=batch.Paths;
                long[] sizes=await batch.Sizes;
                hoverWeight=DropBatch.Weight(sizes.Sum());
                long recycledBytes=0;
                var recycled=new List<RecycledFile>();
                // Shell providers use an STA. Keep their disk work off the animation thread.
                string[] errors=await RecycleBin.OnShellThread(delegate {
                    var failed=new List<string>();
                    for(int i=0;i<files.Length;i++)
                        try { recycled.Add(RecycleBin.SendTracked(files[i])); recycledBytes+=sizes[i]; }
                        catch(Exception ex) { failed.Add(Path.GetFileName(files[i])+" — "+ex.Message); }
                    return failed.ToArray();
                });
                int count=recycled.Count;
                if(star!=null) star.Delivered(recycled.Select(f=>f.OriginalPath).ToArray());
                if(count>0)
                {
                    var history=new List<AbsorbedBatch>(Settings.Absorptions??new AbsorbedBatch[0]);
                    history.Add(new AbsorbedBatch { Files=recycled.ToArray() });
                    Settings.Absorptions=history.ToArray(); Persist();
                    meal=elapsed.Elapsed.TotalSeconds;
                    binHadItems=true;
                    Art.MealWeight=DropBatch.Weight(recycledBytes);
                    audio.Play(PetCue.Absorb);
                }
                string result="已回收 "+count+" 项";
                if(errors.Length>0) result+=" · 失败 "+errors.Length+" 项\n"+errors[0];
                Say(result,errors.Length>0?7:3);
            }
            catch(Exception ex) { Say("投递未完成\n"+ex.Message,7); }
            finally
            {
                if(star!=null && star.Paths.Length>0) orbits.Restore(star);
                batch.Dispose(); hoverWeight=0; handlingDrop=false;
            }
        }
    }
}
