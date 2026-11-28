using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing=System.Drawing;

namespace BlueStar
{
    sealed class ResonanceWave : Window
    {
        public const double Duration=1.28;
        WaveAnchor launch;
        Drawing.Rectangle viewport,full;
        double reach,phase=-1;
        static readonly Pen[] band={
            Stroke(28,49,131,255,28),Stroke(80,51,157,255,13),
            Stroke(196,101,220,255,5),Stroke(240,218,252,255,1.8)
        };

        public ResonanceWave(Window owner,WaveAnchor anchor)
        {
            Owner=owner; Topmost=owner.Topmost; ShowActivated=false; ShowInTaskbar=false;
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true; Background=Brushes.Transparent; IsHitTestVisible=false;
            Focusable=false; WindowStartupLocation=WindowStartupLocation.Manual;
            Place(anchor);
        }
        void Place(WaveAnchor anchor)
        {
            launch=anchor;
            reach=0.55*Math.Max(anchor.Monitor.Width,anchor.Monitor.Height)/anchor.Scale;
            double extent=(reach+16)*anchor.Scale;
            full=Drawing.Rectangle.Intersect(anchor.Monitor,Drawing.Rectangle.FromLTRB(
                (int)Math.Floor(anchor.Heart.X-extent),(int)Math.Floor(anchor.Heart.Y-extent),
                (int)Math.Ceiling(anchor.Heart.X+extent),(int)Math.Ceiling(anchor.Heart.Y+extent)));
            ResizeFor(18,true);
        }
        public void Emit(WaveAnchor anchor)
        {
            Place(anchor);
            phase=0;
            InvalidateVisual();
        }
        public void ShowPrepared()
        {
            Show();
            ResizeFor(18,true);
        }
        void ResizeFor(double radius,bool place=false)
        {
            double half=(reach+16)/2;
            double extent=(radius+16<=half?half:2*half)*launch.Scale;
            // Keep the screen origin stable while the compositor can still hold our previous pixels.
            var area=Drawing.Rectangle.FromLTRB(full.Left,full.Top,
                Math.Min(full.Right,(int)Math.Ceiling(launch.Heart.X+extent)),
                Math.Min(full.Bottom,(int)Math.Ceiling(launch.Heart.Y+extent)));
            if(area.Width<=0 || area.Height<=0 || (!place && viewport==area)) return;
            if(IsVisible)
            {
                using(new PinnedViewer.PhysicalCoordinates(true))
                    if(!SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,area.X,area.Y,
                        area.Width,area.Height,place?0x214u:0x216u)) return;
            }
            else
            {
                var source=PresentationSource.FromVisual(Owner);
                Matrix fromPixels=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
                Point topLeft=fromPixels.Transform(new Point(area.Left,area.Top));
                Point bottomRight=fromPixels.Transform(new Point(area.Right,area.Bottom));
                Left=topLeft.X;Top=topLeft.Y;Width=bottomRight.X-topLeft.X;Height=bottomRight.Y-topLeft.Y;
            }
            viewport=area;
        }
        public void Advance(double seconds)
        {
            if(phase<0) return;
            phase=Math.Min(1,Math.Max(0,seconds/Duration));
            ResizeFor(Radius(reach,phase));
            InvalidateVisual();
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e); MakeClickThrough(this);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if(phase<0 || phase>=1) return;
            var source=PresentationSource.FromVisual(this) as HwndSource;
            WaveClient client;
            if(!WaveClient.TryRead(source,out client)) return;
            Matrix fromScreen=client.ToScreen;
            fromScreen.Invert();
            Matrix map=new Matrix(launch.Scale,0,0,launch.Scale,launch.Heart.X,launch.Heart.Y);
            map.Append(fromScreen);
            dc.PushTransform(new MatrixTransform(map));
            DrawFront(dc,new Point(),reach,phase);
            dc.Pop();
        }
        static double Radius(double reach,double phase)
        { return 18+(reach-18)*(1-Math.Pow(1-phase,1.35)); }
        internal static void DrawFront(DrawingContext dc,Point center,double reach,double phase)
        {
            if(phase<0 || phase>=1) return;
            double r=Radius(reach,phase);
            dc.PushOpacity(Math.Min(1,(1-phase)/0.44));
            foreach(var pen in band) dc.DrawEllipse(null,pen,center,r,r);
            dc.Pop();
        }
        static Pen Stroke(byte alpha,byte red,byte green,byte blue,double width)
        {
            var pen=new Pen(new SolidColorBrush(Color.FromArgb(alpha,red,green,blue)),width);
            pen.Freeze(); return pen;
        }
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
        internal static void MakeClickThrough(Window window)
        {
            var hwnd=new WindowInteropHelper(window).Handle;
            SetWindowLong(hwnd,-20,GetWindowLong(hwnd,-20)|0x20|0x08000000|0x80);
        }
    }

    struct WaveClient
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ClientRect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out ClientRect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref Drawing.Point point);
        public Rect Bounds;
        public Matrix ToScreen;

        public static bool TryRead(HwndSource source,out WaveClient client)
        {
            client=new WaveClient();
            if(source==null || source.CompositionTarget==null) return false;
            IntPtr handle=source.Handle;
            ClientRect backing,physical;
            var origin=new Drawing.Point();
            using(PinnedViewer.PhysicalCoordinates.ForWindow(handle))
                if(!GetClientRect(handle,out backing) || backing.Right<=0 || backing.Bottom<=0) return false;
            using(new PinnedViewer.PhysicalCoordinates(true))
                if(!GetClientRect(handle,out physical) || physical.Right<=0 || physical.Bottom<=0 ||
                    !ClientToScreen(handle,ref origin)) return false;
            // WPF writes into the backing client. A system-aware HWND can be scaled
            // again by DWM on a different-DPI monitor. Calibrate each HWND separately.
            Matrix map=source.CompositionTarget.TransformToDevice;
            map.Scale((double)physical.Right/backing.Right,(double)physical.Bottom/backing.Bottom);
            map.Translate(origin.X,origin.Y);
            client.Bounds=new Rect(origin.X,origin.Y,physical.Right,physical.Bottom);
            client.ToScreen=map;
            return true;
        }
    }

    struct WaveAnchor
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public int Size,L,T,R,B,WorkL,WorkT,WorkR,WorkB,Flags;
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct MonitorDevice
        {
            public MonitorInfo Info;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
        }
        [StructLayout(LayoutKind.Explicit,Size=220)]
        struct DisplayMode
        {
            [FieldOffset(68)] public ushort Size;
            [FieldOffset(184)] public int Refresh;
        }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Drawing.Point point,uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorDevice info);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string device,int mode,ref DisplayMode info);
        public Drawing.Rectangle Monitor;
        IntPtr monitorHandle;
        public Rect Body;
        public Point Heart;
        public double Scale;

        public static bool TryRead(Window owner,FrameworkElement art,Point localHeart,bool useOwnerMonitor,out WaveAnchor anchor)
        {
            anchor=new WaveAnchor();
            var source=PresentationSource.FromVisual(art) as HwndSource;
            WaveClient client;
            if(!WaveClient.TryRead(source,out client)) return false;
            var root=source.RootVisual;
            if(root==null) return false;
            Point inClient=art==root?localHeart:art.TransformToAncestor(root).Transform(localHeart);
            Point heart=client.ToScreen.Transform(inClient);
            var info=new MonitorInfo { Size=40 };
            using(new PinnedViewer.PhysicalCoordinates(true))
            {
                anchor.monitorHandle=useOwnerMonitor?MonitorFromWindow(new WindowInteropHelper(owner).Handle,2):
                    MonitorFromPoint(new Drawing.Point((int)Math.Floor(heart.X),(int)Math.Floor(heart.Y)),2);
                if(!GetMonitorInfo(anchor.monitorHandle,ref info)) return false;
            }
            anchor.Monitor=Drawing.Rectangle.FromLTRB(info.L,info.T,info.R,info.B);
            anchor.Body=client.Bounds; anchor.Heart=heart; anchor.Scale=client.ToScreen.M11;
            return true;
        }
        public int RefreshRate()
        {
            var device=new MonitorDevice { Info=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorDevice)) } };
            var mode=new DisplayMode { Size=220 };
            return GetMonitorInfo(monitorHandle,ref device) && EnumDisplaySettings(device.Name,-1,ref mode)?mode.Refresh:0;
        }
        public Drawing.Rectangle View(double radius)
        {
            // Reallocate in steps so the small first frame stays cheap without resizing on every tick.
            double reach=Math.Ceiling(radius/(192*Scale))*192*Scale;
            var bounds=Drawing.Rectangle.FromLTRB((int)Math.Floor(Heart.X-reach),(int)Math.Floor(Heart.Y-reach),
                (int)Math.Ceiling(Heart.X+reach),(int)Math.Ceiling(Heart.Y+reach));
            return Drawing.Rectangle.Intersect(Monitor,bounds);
        }
    }

    // A click-through per-pixel-alpha HWND. Scan only the annuli, then present their bounded bitmap.
    sealed class WaveSurface : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint
        {
            public int X,Y;
            public NativePoint(int x,int y) { X=x; Y=y; }
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Blend { public byte Operation,Flags,Opacity,AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        struct BitmapHeader
        {
            public uint Size;
            public int Width,Height;
            public ushort Planes,Bits;
            public uint Compression,ImageBytes;
            public int PixelsPerMeterX,PixelsPerMeterY;
            public uint Colors,ImportantColors;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern IntPtr CreateWindowEx(uint ex,string className,string caption,uint style,int x,int y,int width,int height,
            IntPtr owner,IntPtr menu,IntPtr instance,IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr screen,
            ref NativePoint dest,ref NativePoint size,IntPtr src,ref NativePoint origin,uint key,ref Blend blend,uint flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapHeader header,uint usage,
            out IntPtr pixels,IntPtr section,uint offset);

        public struct Stroke
        {
            public readonly double Offset,Half;
            public readonly uint Color;
            public Stroke(double offset,double width,uint color) { Offset=offset; Half=width*0.5; Color=color; }
        }
        public sealed class Plate
        {
            public readonly Drawing.Rectangle Area;
            public readonly int[] Pixels;
            public Plate(Drawing.Rectangle area,int[] pixels) { Area=area; Pixels=pixels; }
        }

        readonly IntPtr owner;
        readonly bool topmost;
        IntPtr handle,dc,bitmap,original,bits;
        Drawing.Rectangle view;
        Drawing.Size capacity,bitmapSize;
        int[] pixels;
        int pixelCount;
        ulong[] inkRows,oldRows;
        int blockWidth;
        bool sparse,layoutChanged,uploadValid;
        readonly Painter[] painters={new Painter(),new Painter(),new Painter(),new Painter()};
        // Reuse a recent buffer if it is still alive. The GC can reclaim it while the pet is idle.
        static readonly WeakReference spare=new WeakReference(null);
        bool disposed,visible;

        public WaveSurface() { }
        public WaveSurface(Window window)
        {
            owner=new WindowInteropHelper(window).Handle;
            topmost=window.Topmost;
        }
        public void Begin(Drawing.Rectangle bounds,Drawing.Size maximum)
        {
            pixelCount=checked(bounds.Width*bounds.Height);
            layoutChanged=view.Size!=bounds.Size || capacity!=maximum;
            SetCapacity(maximum);
            view=bounds;
            sparse=pixelCount>=1400000;
            if(sparse)
            {
                blockWidth=Math.Max(128,(view.Width+63)/64);
                if(inkRows==null || inkRows.Length<maximum.Height)
                { inkRows=new ulong[maximum.Height]; oldRows=new ulong[maximum.Height]; }
            }
        }
        public void Prepare(Drawing.Size maximum)
        {
            SetCapacity(maximum);
            EnsureBitmap();
        }
        void SetCapacity(Drawing.Size maximum)
        {
            int needed=checked(maximum.Width*maximum.Height);
            if(pixels==null || pixels.Length<needed)
            {
                var available=spare.Target as int[];
                if(available!=null && available.Length>=needed)
                {
                    spare.Target=null;
                    pixels=available;
                }
                else pixels=new int[needed];
            }
            capacity=maximum;
        }
        public void Draw(bool wide,Action<Painter> paint)
        {
            int count=wide && pixelCount>=1400000?Math.Min(painters.Length,Math.Max(1,Environment.ProcessorCount/4)):1;
            if(count==1) DrawSlice(0,1,paint);
            else Parallel.For(0,count,i=>DrawSlice(i,count,paint));
        }
        void DrawSlice(int i,int count,Action<Painter> paint)
        {
            int from=view.Height*i/count,to=view.Height*(i+1)/count;
            if(!sparse || layoutChanged) Array.Clear(pixels,from*view.Width,(to-from)*view.Width);
            if(sparse)
                for(int row=from;row<to;row++)
                {
                    ulong previous=layoutChanged?0:inkRows[row];
                    var spans=new Blocks(previous,blockWidth,view.Width);
                    int x,length;
                    while(spans.Next(out x,out length)) Array.Clear(pixels,row*view.Width+x,length);
                    oldRows[row]=previous; inkRows[row]=0;
                }
            var painter=painters[i];
            painter.Reset(pixels,view,view.Y+from,view.Y+to);
            painter.Track(sparse?inkRows:null,blockWidth);
            paint(painter);
        }
        public Drawing.Rectangle View { get { return view; } }
        internal int[] Pixels { get { return pixels; } }

        public bool Present()
        {
            if(disposed || view.Width<=0 || view.Height<=0) return false;
            using(new PinnedViewer.PhysicalCoordinates(true))
            {
                if(handle==IntPtr.Zero)
                    handle=CreateWindowEx(0x080800A0u|(topmost?8u:0u),"STATIC",null,0x80000000,
                        view.X,view.Y,view.Width,view.Height,owner,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
                if(handle==IntPtr.Zero) return false;
                if(!EnsureBitmap()) return false;
                Upload();
                var dest=new NativePoint(view.X,view.Y); var size=new NativePoint(view.Width,view.Height);
                var origin=new NativePoint(); var blend=new Blend { Opacity=255,AlphaFormat=1 };
                uploadValid=UpdateLayeredWindow(handle,IntPtr.Zero,ref dest,ref size,dc,ref origin,0,ref blend,2);
                if(!uploadValid) return false;
                if(!visible) { ShowWindow(handle,4); visible=true; }
                return true;
            }
        }
        bool EnsureBitmap()
        {
            if(bitmap!=IntPtr.Zero && bitmapSize==capacity) return true;
            if(dc==IntPtr.Zero) dc=CreateCompatibleDC(IntPtr.Zero);
            if(dc==IntPtr.Zero) return false;
            ReleaseBitmap();
            var header=new BitmapHeader { Size=40,Width=capacity.Width,Height=-capacity.Height,Planes=1,Bits=32 };
            bitmap=CreateDIBSection(dc,ref header,0,out bits,IntPtr.Zero,0);
            if(bitmap==IntPtr.Zero) return false;
            original=SelectObject(dc,bitmap);
            if(original==IntPtr.Zero || original==new IntPtr(-1))
            {
                DeleteObject(bitmap); bitmap=bits=IntPtr.Zero;
                return false;
            }
            bitmapSize=capacity; uploadValid=false;
            return true;
        }
        void Upload()
        {
            if(!sparse || layoutChanged || !uploadValid)
            {
                if(view.Width==capacity.Width) Marshal.Copy(pixels,0,bits,pixelCount);
                else for(int y=0;y<view.Height;y++) Copy(y,0,view.Width);
                return;
            }
            // Empty blocks retain their zero alpha in the DIB; upload both newly painted and erased blocks.
            for(int y=0;y<view.Height;y++)
            {
                var spans=new Blocks(inkRows[y]|oldRows[y],blockWidth,view.Width);
                int x,length;
                while(spans.Next(out x,out length)) Copy(y,x,length);
            }
        }
        void Copy(int row,int left,int length)
        { Marshal.Copy(pixels,row*view.Width+left,IntPtr.Add(bits,(row*capacity.Width+left)*4),length); }
        struct Blocks
        {
            ulong remaining;
            int column;
            readonly int block,width;
            public Blocks(ulong mask,int block,int width)
            { remaining=mask; this.block=block; this.width=width; column=0; }
            public bool Next(out int x,out int length)
            {
                if(remaining==0) { x=length=0; return false; }
                if((uint)remaining==0) { remaining>>=32; column+=32; }
                if((ushort)remaining==0) { remaining>>=16; column+=16; }
                if((byte)remaining==0) { remaining>>=8; column+=8; }
                while((remaining&1)==0) { remaining>>=1; column++; }
                x=column*block;
                while((remaining&1)!=0) { remaining>>=1; column++; }
                length=Math.Min(width,column*block)-x;
                return true;
            }
        }
        void ReleaseBitmap()
        {
            if(bitmap==IntPtr.Zero) return;
            SelectObject(dc,original); DeleteObject(bitmap); bitmap=bits=IntPtr.Zero;
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true; ReleaseBitmap();
            if(dc!=IntPtr.Zero) DeleteDC(dc);
            if(handle!=IntPtr.Zero) DestroyWindow(handle);
            var available=spare.Target as int[];
            if(available==null || (pixels!=null && available.Length<pixels.Length)) spare.Target=pixels;
            pixels=null;
        }

        public sealed class Painter
        {
            Drawing.Rectangle view;
            int from,to;
            int[] pixels;
            ulong[] ink;
            int block;
            int[] palette=new int[0];
            short[] lensPalette=new short[0];
            internal void Reset(int[] buffer,Drawing.Rectangle bounds,int first,int last)
            { pixels=buffer; view=bounds; from=first; to=last; }
            internal void Track(ulong[] rows,int blockWidth) { ink=rows; block=blockWidth; }
            void SpanAt(Span paint,int left,int right,int row,double dy2)
            {
                if(ink!=null)
                {
                    int low=(left-view.X)/block,high=(right-1-view.X)/block;
                    ink[row]|=(ulong.MaxValue<<low)&(ulong.MaxValue>>(63-high));
                }
                paint(left,right,row,dy2);
            }

            public void Ink(Point center,double radius,double unit,double opacity,Stroke[] strokes,Plate lens=null)
            {
                if(radius<=0 || opacity<=0) return;
                double low=lens==null?0:-13*unit,high=lens==null?0:10*unit;
                foreach(var s in strokes)
                {
                    low=Math.Min(low,(s.Offset-s.Half)*unit);
                    high=Math.Max(high,(s.Offset+s.Half)*unit);
                }
                low-=0.55; high+=0.55;
                const int samplesPerPixel=16;
                int count=(int)Math.Ceiling((high-low)*samplesPerPixel)+1;
                if(palette.Length<count) palette=new int[count];
                if(lens!=null && lensPalette.Length<count) lensPalette=new short[count];
                for(int i=0;i<count;i++)
                {
                    int color=0;
                    double delta=low+i/(double)samplesPerPixel;
                    foreach(var s in strokes)
                    {
                        if(radius+s.Offset*unit<=0) continue;
                        double coverage=Clamp((s.Half*unit-Math.Abs(delta-s.Offset*unit))+0.5);
                        color=Over(FromStraight(s.Color,coverage),color);
                    }
                    palette[i]=Scale(color,(int)Math.Round(opacity*255));
                    if(lens!=null)
                    {
                        double front=Clamp(Math.Min(delta+2*unit,10*unit-delta)+0.5);
                        double back=Clamp(Math.Min(delta+13*unit,-3*unit-delta)+0.5);
                        lensPalette[i]=(short)(int)Math.Round(255*opacity*(0.91*front-0.73*back));
                    }
                }
                if(lens!=null)
                {
                    PaintCrest(center,radius,unit,low,high,count,samplesPerPixel,lens);
                    return;
                }
                Band(center,radius+low,radius+high,delegate(int start,int end,int row,double dy2)
                {
                    double x=start+0.5-center.X;
                    int at=row*view.Width+start-view.X;
                    double first=radius+low;
                    for(int i=start;i<end;i++,x++,at++)
                    {
                        int index=(int)((Math.Sqrt(dy2+x*x)-first)*samplesPerPixel+0.5);
                        if((uint)index<(uint)count) pixels[at]=Over(palette[index],pixels[at]);
                    }
                });
            }
            void PaintCrest(Point center,double radius,double unit,double low,double high,int count,int samplesPerPixel,Plate lens)
            {
                // A single high-resolution pass shades the crest and both refracted bands.
                // The source is sampled at the closest physical pixel; the front itself is still antialiased.
                const int fraction=65536;
                double front=1/(1+6.5*unit/(radius+4*unit)),back=1/(1-4.5*unit/(radius-8*unit));
                int stepFront=(int)Math.Round(front*fraction),stepBack=(int)Math.Round(back*fraction);
                Drawing.Rectangle image=lens.Area;
                Band(center,radius+low,radius+high,delegate(int start,int end,int row,double dy2)
                {
                    double x=start+0.5-center.X,y=row+view.Y+0.5-center.Y;
                    int frontY=(int)Math.Floor(center.Y-image.Y+y*front),backY=(int)Math.Floor(center.Y-image.Y+y*back);
                    int frontRow=(uint)frontY<(uint)image.Height?frontY*image.Width:-1;
                    int backRow=(uint)backY<(uint)image.Height?backY*image.Width:-1;
                    int frontX=(int)Math.Round((center.X-image.X+x*front)*fraction);
                    int backX=(int)Math.Round((center.X-image.X+x*back)*fraction);
                    int at=row*view.Width+start-view.X;
                    double first=radius+low;
                    for(int i=start;i<end;i++,at++,x++,frontX+=stepFront,backX+=stepBack)
                    {
                        int index=(int)((Math.Sqrt(dy2+x*x)-first)*samplesPerPixel+0.5);
                        if((uint)index>=(uint)count) continue;
                        int p=pixels[at],light=lensPalette[index];
                        if(light!=0)
                        {
                            int srcRow=light>0?frontRow:backRow,col=(light>0?frontX:backX)>>16;
                            if(srcRow>=0 && (uint)col<(uint)image.Width)
                                p=Over(Scale(lens.Pixels[srcRow+col],Math.Abs(light)),p);
                        }
                        pixels[at]=Over(palette[index],p);
                    }
                });
            }
            public void Refract(Plate scene,Point center,double radius,double half,double offset,double opacity)
            {
                if(scene==null || radius<=half || opacity<=0) return;
                double inner=radius-half,outer=radius+half,inverse=1/(1+offset/radius);
                double innerSolid=(inner+0.5)*(inner+0.5),outerSolid=(outer-0.5)*(outer-0.5);
                int alpha=(int)Math.Round(255*opacity);
                var area=scene.Area;
                var data=scene.Pixels;
                // At 4K the source retains its physical pixels. A one-axis linear filter
                // avoids two extra samples while the narrow front keeps full coverage.
                bool linearOnly=(long)area.Width*area.Height>=4000000;
                const int fraction=65536;
                int limit=area.Width*fraction-fraction/2;
                int step=(int)Math.Round(inverse*fraction);
                Band(center,inner-0.55,outer+0.55,delegate(int start,int end,int row,double dy2)
                {
                    double y=(row+view.Y+0.5-center.Y)*inverse+center.Y-area.Y-0.5;
                    int iy=(int)Math.Floor(y);
                    if(y<-.5 || y>area.Height-.5) return;
                    int row0=Math.Max(0,iy)*area.Width,row1=Math.Min(area.Height-1,iy+1)*area.Width;
                    int fy=(int)((y-iy)*256+0.5);
                    if(linearOnly && fy>=128) row0=row1;
                    double x=start+0.5-center.X;
                    int sx=(int)Math.Round((x*inverse+center.X-area.X-0.5)*fraction);
                    int at=row*view.Width+start-view.X;
                    for(int i=start;i<end;i++,x++,sx+=step,at++)
                    {
                        if(sx<-fraction/2 || sx>limit) continue;
                        double distance2=dy2+x*x;
                        int strength=alpha;
                        if(distance2<innerSolid || distance2>outerSolid)
                        {
                            double r=Math.Sqrt(distance2);
                            strength=(int)Math.Round(alpha*Clamp(Math.Min(r-inner,outer-r)+0.5));
                            if(strength==0) continue;
                        }
                        int ix=sx>>16;
                        int x0=Math.Max(0,ix),x1=Math.Min(area.Width-1,ix+1),fx=((sx&65535)+128)>>8;
                        int sample=Mix(data[row0+x0],data[row0+x1],fx);
                        if(!linearOnly) sample=Mix(sample,Mix(data[row1+x0],data[row1+x1],fx),fy);
                        pixels[at]=Over(Scale(sample,strength),pixels[at]);
                    }
                });
            }

            delegate void Span(int start,int end,int row,double dy2);
            void Band(Point c,double inner,double outer,Span paint)
            {
                double inner2=Math.Max(0,inner); inner2*=inner2;
                double outer2=outer*outer;
                int first=Math.Max(from,(int)Math.Ceiling(c.Y-outer-0.5));
                int last=Math.Min(to,(int)Math.Floor(c.Y+outer-0.5)+1);
                for(int y=first;y<last;y++)
                {
                    double dy=y+0.5-c.Y,dy2=dy*dy;
                    double x=Math.Sqrt(Math.Max(0,outer2-dy2));
                    int left=Math.Max(view.X,(int)Math.Ceiling(c.X-x-0.5));
                    int right=Math.Min(view.Right,(int)Math.Floor(c.X+x-0.5)+1);
                    if(left>=right) continue;
                    if(dy2>=inner2) { SpanAt(paint,left,right,y-view.Y,dy2); continue; }
                    double hole=Math.Sqrt(inner2-dy2);
                    int leftEnd=Math.Min(right,(int)Math.Floor(c.X-hole-0.5)+1);
                    int rightStart=Math.Max(left,(int)Math.Ceiling(c.X+hole-0.5));
                    if(left<leftEnd) SpanAt(paint,left,leftEnd,y-view.Y,dy2);
                    if(rightStart<right) SpanAt(paint,rightStart,right,y-view.Y,dy2);
                }
            }
        }
        static double Clamp(double x) { return x<0?0:x>1?1:x; }
        static int FromStraight(uint argb,double alpha)
        {
            int a=(int)Math.Round((argb>>24)*alpha);
            return Premultiply((int)argb,a);
        }
        internal static int Premultiply(int rgb,int alpha)
        { return Scale(rgb|unchecked((int)0xff000000),alpha); }
        static int Scale(int p,int weight)
        {
            // Work on alternate byte lanes so their products fit in 16 bits.
            uint bits=(uint)p;
            const uint lanes=0x00ff00ff;
            uint rb=(bits&lanes)*(uint)weight+0x00800080;
            uint ag=((bits>>8)&lanes)*(uint)weight+0x00800080;
            return (int)((((rb+((rb>>8)&lanes))>>8)&lanes)|((ag+((ag>>8)&lanes))&0xff00ff00));
        }
        static int Over(int src,int dst)
        {
            int alpha=(int)((uint)src>>24);
            if(alpha==0) return dst;
            if(dst==0 || alpha==255) return src;
            return src+Scale(dst,255-alpha);
        }
        static int Mix(int first,int last,int weight)
        {
            const uint lanes=0x00ff00ff;
            uint a=(uint)first,b=(uint)last,v=(uint)weight,u=256-v;
            uint rb=(a&lanes)*u+(b&lanes)*v+0x00800080;
            uint ag=((a>>8)&lanes)*u+((b>>8)&lanes)*v+0x00800080;
            return (int)(((rb>>8)&lanes)|(ag&0xff00ff00));
        }
    }
}
