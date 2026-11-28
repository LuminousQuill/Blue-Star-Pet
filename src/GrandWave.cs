using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing=System.Drawing;
using ImagingFormat=System.Drawing.Imaging.PixelFormat;

namespace BlueStar
{
    sealed class GrandWave
    {
        public const double Release=2.32,CallTime=Release-0.79,PrepareTime=CallTime-0.30,Duration=3.10;
        readonly WaveSurface surface;
        WaveAnchor anchor;
        bool emitted,budgetWide;
        FrameBudget budget;
        WaveSurface.Plate backdrop;
        Drawing.Rectangle requested;
        Task<WaveSurface.Plate> pending;
        static readonly WaveSurface.Stroke[] crest={
            new WaveSurface.Stroke(0,18,0x412D9BFF),new WaveSurface.Stroke(-2,5,0xA42EA3FF),
            new WaveSurface.Stroke(0,2.5,0xEBB5F8FF),new WaveSurface.Stroke(-13,1.3,0x9271CDFF)
        };
        static readonly WaveSurface.Stroke[] echo={ new WaveSurface.Stroke(0,1.3,0x9271CDFF) };
        static readonly double[] reflections={1,0.60,0.36};

        public GrandWave(PetWindow pet)
        {
            surface=new WaveSurface(pet);
        }
        public void Close() { surface.Dispose(); backdrop=null; pending=null; }

        bool Track(PetWindow pet)
        {
            WaveAnchor current;
            if(!WaveAnchor.TryRead(pet,pet.Art,pet.Art.Heart,false,out current)) return false;
            if(anchor.Monitor!=current.Monitor)
            {
                backdrop=null;
                budgetWide=FrameBudget.NeedsLimit(current.Monitor,current.RefreshRate());
                surface.Prepare(current.Monitor.Size);
            }
            anchor=current;
            return true;
        }
        void RequestPlate(PetWindow pet)
        {
            ReadPendingPlate();
            if(pending!=null || requested==anchor.Monitor || anchor.Monitor.IsEmpty) return;
            requested=anchor.Monitor;
            var area=requested;
            // The UI supplies its current body mask. The desktop readback and packed plate run on a worker.
            WaveClient client;
            if(!WaveClient.TryRead(PresentationSource.FromVisual(pet) as HwndSource,out client) ||
                !client.Bounds.IntersectsWith(new Rect(area.X,area.Y,area.Width,area.Height)))
            {
                pending=Task.Run(()=>CapturePlate(area,Rect.Empty,null,0));
                return;
            }
            Rect body=client.Bounds;
            var alpha=new RenderTargetBitmap((int)body.Width,(int)body.Height,
                96*client.ToScreen.M11,96*client.ToScreen.M22,PixelFormats.Pbgra32);
            alpha.Render((Visual)pet.Content);
            int stride=alpha.PixelWidth*4;
            var skin=new byte[stride*alpha.PixelHeight];
            alpha.CopyPixels(skin,stride,0);
            pending=Task.Run(()=>CapturePlate(area,body,skin,stride));
        }

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        internal static BitmapSource CapturePixels(Drawing.Rectangle area)
        {
            // The local meal lens also uses this bitmap-source capture path.
            using(var image=new Drawing.Bitmap(area.Width,area.Height,ImagingFormat.Format24bppRgb))
            {
                using(var g=Drawing.Graphics.FromImage(image))
                using(new PinnedViewer.PhysicalCoordinates(true))
                    g.CopyFromScreen(area.Left,area.Top,0,0,image.Size);
                IntPtr handle=image.GetHbitmap();
                try
                {
                    var source=Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze(); return source;
                }
                finally { DeleteObject(handle); }
            }
        }
        internal static WaveSurface.Plate CapturePlate(Drawing.Rectangle area,Rect body,byte[] skin,int skinStride)
        {
            try
            {
                var pixels=new int[area.Width*area.Height];
                using(var image=new Drawing.Bitmap(area.Width,area.Height,ImagingFormat.Format32bppRgb))
                {
                    using(var g=Drawing.Graphics.FromImage(image))
                    using(new PinnedViewer.PhysicalCoordinates(true))
                        g.CopyFromScreen(area.Left,area.Top,0,0,image.Size);
                    var data=image.LockBits(new Drawing.Rectangle(Drawing.Point.Empty,image.Size),
                        Drawing.Imaging.ImageLockMode.ReadOnly,ImagingFormat.Format32bppRgb);
                    try
                    {
                        if(data.Stride==area.Width*4) Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                        else for(int y=0;y<area.Height;y++)
                            Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*area.Width,area.Width);
                    }
                    finally { image.UnlockBits(data); }
                }
                for(int i=0;i<pixels.Length;i++) pixels[i]|=unchecked((int)0xFF000000);
                Rect patch=Rect.Intersect(body,new Rect(area.X,area.Y,area.Width,area.Height));
                if(!patch.IsEmpty && skin!=null)
                {
                    int width=(int)patch.Width,height=(int)patch.Height;
                    int dst=((int)patch.Y-area.Y)*area.Width+(int)patch.X-area.X;
                    int src=((int)(patch.Y-body.Y))*skinStride+4*(int)(patch.X-body.X)+3;
                    for(int y=0;y<height;y++,dst+=area.Width,src+=skinStride)
                        for(int x=0;x<width;x++)
                        {
                            int alpha=255-Math.Min(255,skin[src+x*4]*4);
                            pixels[dst+x]=WaveSurface.Premultiply(pixels[dst+x],alpha);
                        }
                }
                return new WaveSurface.Plate(area,pixels);
            }
            catch(Win32Exception) { return null; }
            catch(ExternalException) { return null; }
        }
        void ReadPendingPlate()
        {
            if(pending==null || !pending.IsCompleted) return;
            if(requested==anchor.Monitor) backdrop=pending.GetAwaiter().GetResult();
            pending=null;
        }
        public void Advance(PetWindow pet,double seconds)
        {
            // A pulse can release during a monitor transition. Wait for a valid launch sample
            // and keep that sample for every direct and reflected front.
            if(!emitted && !Track(pet)) return;
            RequestPlate(pet);
            if(anchor.Monitor.IsEmpty || seconds<CallTime-Release) return;
            emitted|=seconds>=0;
            if(!budget.ShouldDraw(seconds,budgetWide)) return;
            DrawScene(surface,anchor,seconds,backdrop);
            surface.Present();
        }
        internal struct FrameBudget
        {
            const double Period=1.0/60,ClockSlack=0.0018;
            double next;
            public static bool NeedsLimit(Drawing.Rectangle area,int refreshRate)
            { return refreshRate>75 && (long)area.Width*area.Height>=4000000; }
            public bool ShouldDraw(double seconds,bool wideHighRefresh)
            {
                if(!wideHighRefresh || seconds<0.18) return true;
                double clock=seconds+ClockSlack;
                if(clock<next) return false;
                // An absolute deadline avoids turning a jittery 60 Hz callback into 30 Hz.
                next+=(Math.Floor((clock-next)/Period)+1)*Period;
                return true;
            }
        }
        internal static void DrawScene(WaveSurface surface,WaveAnchor anchor,double t,WaveSurface.Plate scene)
        {
            Point c=anchor.Heart;
            double unit=anchor.Scale,body=anchor.Body.Width;
            if(t<0)
            {
                surface.Begin(anchor.View(body*1.04),anchor.Monitor.Size);
                double u=IdleMotion.Ease(1+t/(Release-CallTime));
                double intensity=IdleMotion.Beat(t+Release-CallTime,0.14,0.68,0.79);
                double r=body*(0.91-0.65*u);
                surface.Draw(false,p=>
                {
                    p.Refract(scene,c,r,body*0.045,-body*0.042,intensity*0.88);
                    p.Refract(scene,c,r+body*0.070,body*0.025,body*0.018,intensity*0.66);
                    p.Ink(c,r,unit,intensity*0.40,echo);
                });
                return;
            }
            var area=anchor.Monitor;
            double w=area.Width,h=area.Height;
            double uWave=t/Duration,diagonal=Math.Sqrt(w*w+h*h);
            double radius=18*unit+diagonal*1.53*uWave*(1.22-0.22*uWave);
            double fade=1-IdleMotion.Ease((uWave-0.72)/0.28);
            bool singlePass=scene!=null && unit>=1 && (long)area.Width*area.Height>=4000000;
            // A reflected route cannot reach a point sooner than the direct radius does.
            surface.Begin(anchor.View(Math.Max(body*1.04,radius+25*unit)),area.Size);
            surface.Draw(true,p=>
            {
                for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
                {
                    var center=new Point(x<0?2*area.Left-c.X:x>0?2*area.Right-c.X:c.X,
                        y<0?2*area.Top-c.Y:y>0?2*area.Bottom-c.Y:c.Y);
                    double nearX=Math.Max(0,Math.Max(area.Left-center.X,center.X-area.Right));
                    double nearY=Math.Max(0,Math.Max(area.Top-center.Y,center.Y-area.Bottom));
                    double farX=Math.Max(Math.Abs(center.X-area.Left),Math.Abs(center.X-area.Right));
                    double farY=Math.Max(Math.Abs(center.Y-area.Top),Math.Abs(center.Y-area.Bottom));
                    double outer=radius+25*unit,inner=Math.Max(0,radius-30*unit);
                    if(nearX*nearX+nearY*nearY>outer*outer || farX*farX+farY*farY<inner*inner) continue;
                    double power=fade*reflections[Math.Abs(x)+Math.Abs(y)];
                    if(singlePass) p.Ink(center,radius,unit,power,crest,scene);
                    else
                    {
                        p.Refract(scene,center,radius+4*unit,6*unit,6.5*unit,power*0.91);
                        p.Refract(scene,center,radius-8*unit,5*unit,-4.5*unit,power*0.73);
                        p.Ink(center,radius,unit,power,crest);
                    }
                }
            });
        }
    }
}
