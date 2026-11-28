using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BlueStar
{
    // A small grip outside the far corner works even with a player's custom non-client frame.
    sealed class ReferenceResizeGrip : Window
    {
        const double GripSize=17;
        readonly Point edge,corner;
        IntPtr handle;
        Rect current,start;
        Point press,release;
        Rect lastPosition=Rect.Empty;
        bool held,released,buttonWasDown;
        readonly MinMax limits;
        public bool Dragging
        {
            get
            {
                bool down=ButtonDown();
                if(down!=buttonWasDown) ObserveButton(down,Pointer());
                return held || released;
            }
        }

        public ReferenceResizeGrip(PetWindow pet,IntPtr viewer,Point edge)
        {
            this.edge=edge;
            limits=TrackLimits(viewer);
            corner=new Point(edge.X<=0.5?1:0,edge.Y<=0.5?1:0);
            Owner=pet; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
            Title="调整参照窗口大小"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.Manual;
            Width=Height=GripSize; AllowsTransparency=true; Background=Brushes.Transparent;
            Cursor=corner.X==corner.Y?Cursors.SizeNWSE:Cursors.SizeNESW;
            Content=new Border { CornerRadius=new CornerRadius(4), Background=new SolidColorBrush(Color.FromArgb(222,24,49,65)),
                BorderBrush=new SolidColorBrush(Color.FromRgb(106,177,205)), BorderThickness=new Thickness(1),
                Child=new TextBlock { Text=corner.X==corner.Y?"⤡":"⤢",FontSize=13,Foreground=Brushes.AliceBlue,
                    VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center },
                ToolTip="拖动以脚尖为支点调整窗口大小" };
            SourceInitialized+=delegate {
                handle=new WindowInteropHelper(this).Handle;
                var source=HwndSource.FromHwnd(handle);
                SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x80);
                buttonWasDown=ButtonDown();
                source.AddHook(GripInput);
            };
        }
        static bool ButtonDown() { return PetWindow.ReferenceButtonDown(); }
        void Begin(Point point)
        {
            if(held) return;
            held=buttonWasDown=true; released=false;
            start=current; press=point;
        }
        void End(Point point)
        {
            buttonWasDown=false;
            if(!held) return;
            held=false; released=true; release=point;
            if(GetCapture()==handle) ReleaseCapture();
        }
        void ObserveButton(bool down,Point point)
        {
            // Some foreground players retain capture. Only a new press over this HWND can start a drag.
            if(!down) { End(point); return; }
            buttonWasDown=true;
            Pixel pixel=new Pixel { X=(int)point.X,Y=(int)point.Y };
            if(WindowFromPhysicalPoint(pixel)==handle) Begin(point);
        }
        IntPtr GripInput(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(msg==0x21) { handled=true; return new IntPtr(3); }
            if(msg==0x201 || msg==0x203)
            {
                Begin(Pointer()); SetCapture(hwnd); handled=true;
            }
            else if(msg==0x202 && held) { End(Pointer()); handled=true; }
            return IntPtr.Zero;
        }
        public void Follow(Rect frame,Rect[] screens,double scale)
        {
            current=frame;
            double side=GripSize*scale;
            // Outside the media surface wherever space permits. At the desktop edge only the corner is covered.
            Rect handle=new Rect(corner.X==1?frame.Right:frame.Left-side,
                corner.Y==1?frame.Bottom-side:frame.Top,side,side);
            Point location=ReferenceTether.Constrain(handle,screens);
            int x=(int)Math.Round(location.X),y=(int)Math.Round(location.Y),size=(int)Math.Round(side);
            var position=new Rect(x,y,size,size);
            if(IsVisible && position==lastPosition) return;
            using(new PinnedViewer.PhysicalCoordinates(true))
            {
                bool visible=IsVisible;
                if(!visible)
                {
                    var dip=PresentationSource.FromVisual(Owner).CompositionTarget.TransformFromDevice.Transform(location);
                    Left=dip.X; Top=dip.Y;
                    new WindowInteropHelper(this).EnsureHandle();
                }
                using(PinnedViewer.PhysicalCoordinates.ForWindow(this.handle))
                    if(SetWindowPos(this.handle,IntPtr.Zero,x,y,size,size,position.Size==lastPosition.Size?0x215u:0x214u))
                        lastPosition=position;
                if(!visible) Show();
            }
        }
        public Size RequestSize(double scale,Rect[] screens)
        {
            Vector delta=(released?release:Pointer())-press;
            Rect desktop=screens[0];
            foreach(Rect r in screens) desktop.Union(r);
            double width=Extent(start.Left,start.Width,edge.X,corner.X,delta.X,desktop.Left,desktop.Right,
                Math.Max(96*scale,limits.MinTrack.X),limits.MaxTrack.X);
            double height=Extent(start.Top,start.Height,edge.Y,corner.Y,delta.Y,desktop.Top,desktop.Bottom,
                Math.Max(64*scale,limits.MinTrack.Y),limits.MaxTrack.Y);
            return new Size(width,height);
        }
        public void FinishPlacement() { released=false; }
        static double Extent(double origin,double length,double edge,double corner,double delta,
            double minCoord,double maxCoord,double minSize,double maxSize)
        {
            double pin=origin+edge*length;
            if(maxSize<=0) maxSize=double.PositiveInfinity;
            if(edge>0) maxSize=Math.Min(maxSize,(pin-minCoord)/edge);
            if(edge<1) maxSize=Math.Min(maxSize,(maxCoord-pin)/(1-edge));
            return Math.Round(Math.Max(minSize,Math.Min(maxSize,length+delta/(corner-edge))));
        }
        static MinMax TrackLimits(IntPtr viewer)
        {
            var limits=new MinMax(); IntPtr reply;
            using(new PinnedViewer.PhysicalCoordinates(true))
                SendMessageTimeout(viewer,0x24,IntPtr.Zero,ref limits,2,50,out reply);
            return limits;
        }
        internal static Size MinimumSize(IntPtr viewer)
        {
            var limits=TrackLimits(viewer);
            return new Size(Math.Max(0,limits.MinTrack.X),Math.Max(0,limits.MinTrack.Y));
        }
        [StructLayout(LayoutKind.Sequential)] struct Pixel { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] struct MinMax { public Pixel Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack; }
        static Point Pointer()
        {
            Pixel p; GetPhysicalCursorPos(out p); return new Point(p.X,p.Y);
        }
        [DllImport("user32.dll")] static extern bool GetPhysicalCursorPos(out Pixel p);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPhysicalPoint(Pixel p);
        [DllImport("user32.dll")] static extern IntPtr GetCapture();
        [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr wParam,ref MinMax data,uint flags,uint timeout,out IntPtr result);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    }
}
