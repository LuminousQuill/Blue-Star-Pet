using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Drawing=System.Drawing;
using Forms=System.Windows.Forms;

namespace BlueStar
{
    sealed class MealRefraction : ShaderEffect
    {
        static readonly DependencyProperty input=RegisterPixelShaderSamplerProperty("Input",typeof(MealRefraction),0,SamplingMode.Bilinear);
        static readonly DependencyProperty center=DependencyProperty.Register("Center",typeof(Point),typeof(MealRefraction),
            new UIPropertyMetadata(new Point(0.5,0.5),PixelShaderConstantCallback(0)));
        static readonly DependencyProperty bend=DependencyProperty.Register("Bend",typeof(Point),typeof(MealRefraction),
            new UIPropertyMetadata(new Point(1,0),PixelShaderConstantCallback(1)));
        static readonly PixelShader shader=Load();
        static PixelShader Load()
        {
            var p=new PixelShader();
            using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("meal-lens.ps")) p.SetStreamSource(s);
            p.Freeze(); return p;
        }
        public MealRefraction()
        {
            PixelShader=shader; UpdateShaderValue(input); UpdateShaderValue(center); UpdateShaderValue(bend);
        }
        public void Set(Point origin,double radius,double power)
        {
            SetValue(center,origin); SetValue(bend,new Point(1/radius,power));
        }
        public void Detach()
        {
            // WPF unsubscribes this effect from the shared shader when the property is cleared.
            PixelShader=null;
        }
    }

    // A separate, input-transparent surface sits immediately behind the pet's native window.
    sealed class MealLens : Window
    {
        readonly Image image=new Image { Stretch=Stretch.Fill,IsHitTestVisible=false };
        readonly MealRefraction lens=new MealRefraction();
        Drawing.Rectangle plate;
        IntPtr handle,body;
        double power,from,reach,fromReach,lastCapture=double.NegativeInfinity;
        [DllImport("user32")] static extern IntPtr GetWindow(IntPtr h,uint relation);
        [DllImport("user32")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
        [StructLayout(LayoutKind.Sequential)]
        struct WindowPos { public IntPtr hwnd,after; public int x,y,width,height; public uint flags; }

        public MealLens()
        {
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.Manual; AllowsTransparency=true;
            ShowActivated=false; ShowInTaskbar=false; Focusable=false; IsHitTestVisible=false;
            Background=Brushes.Transparent; image.Effect=lens; Content=image;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e); handle=new WindowInteropHelper(this).Handle;
            ResonanceWave.MakeClickThrough(this);
            HwndSource.FromHwnd(handle).AddHook(PlaceBehindPet);
        }
        IntPtr PlaceBehindPet(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
        {
            // ShowWindow may reorder even a non-activating window. Set the insertion point
            // in the show transaction itself, before the compositor can see the surface.
            if(msg==0x46 && body!=IntPtr.Zero)
            {
                var pos=(WindowPos)Marshal.PtrToStructure(lp,typeof(WindowPos));
                if((pos.flags&0x40)!=0 || (pos.flags&4)==0)
                {
                    pos.after=body; pos.flags=(pos.flags&~4u)|0x210;
                    Marshal.StructureToPtr(pos,lp,false);
                }
            }
            return IntPtr.Zero;
        }
        protected override void OnClosed(EventArgs e)
        {
            image.Effect=null; lens.Detach();
            image.Source=null; base.OnClosed(e);
        }
        static void DrawWindowMask(DrawingContext dc,Window window,Drawing.Rectangle area)
        {
            if(!window.IsVisible || window.ActualWidth<=0 || window.ActualHeight<=0) return;
            Rect frame=ReferenceTether.PhysicalPetFrame(new WindowInteropHelper(window).Handle);
            if(frame.IsEmpty || !frame.IntersectsWith(new Rect(area.X,area.Y,area.Width,area.Height))) return;
            frame.Offset(-area.X,-area.Y);
            dc.DrawRectangle(window.Background,null,frame);
            var content=window.Content as UIElement;
            if(content==null || content.RenderSize.Width<=0 || content.RenderSize.Height<=0) return;
            var box=new Rect(content.RenderSize);
            Rect dest=content.TransformToAncestor(window).TransformBounds(box);
            dest.Scale(frame.Width/window.ActualWidth,frame.Height/window.ActualHeight); dest.Offset(frame.X,frame.Y);
            // Read the already-arranged content. Brush layout must never measure the live Window.
            var ink=new VisualBrush { AutoLayoutContent=false,ViewboxUnits=BrushMappingMode.Absolute,
                Viewbox=box,Stretch=Stretch.Fill,Visual=content };
            dc.DrawRectangle(ink,null,dest);
        }
        static void MaskPet(Window pet,Drawing.Rectangle area,byte[] pixels)
        {
            var silhouette=new DrawingVisual();
            using(var dc=silhouette.RenderOpen())
            {
                DrawWindowMask(dc,pet,area);
                foreach(Window w in pet.OwnedWindows) DrawWindowMask(dc,w,area);
            }
            // The mask is feathered over several physical pixels. Only the desktop needs full resolution.
            const int step=3;
            int width=(area.Width+step-1)/step,height=(area.Height+step-1)/step;
            var mask=new RenderTargetBitmap(width,height,96.0/step,96.0/step,PixelFormats.Pbgra32);
            mask.Render(silhouette);
            var ink=new byte[width*height*4]; mask.CopyPixels(ink,width*4,0);
            var coverage=new byte[width*height];
            int pad=Math.Max(1,(int)Math.Round(width*0.011));
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                int near=0;
                for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
                {
                    int row=Math.Max(0,Math.Min(height-1,y+dy*pad));
                    int col=Math.Max(0,Math.Min(width-1,x+dx*pad));
                    near=Math.Max(near,ink[(row*width+col)*4+3]);
                }
                coverage[y*width+x]=(byte)Math.Max(0,Math.Min(255,(154-near)*255/120));
            }
            for(int y=0;y<area.Height;y++) for(int x=0;x<area.Width;x++)
            {
                int i=(y*area.Width+x)*4;
                if(pixels[i+3]==0) continue;
                int cell=(y/step)*width+x/step,alpha=ink[cell*4+3],keep=coverage[cell];
                // Opaque legs, stars and the shell supply no background pixels. In lighter
                // glow, undo its premultiplied colour, then feather the sample near the skin.
                if(keep==255 && alpha==0) continue;
                for(int k=0;k<3;k++)
                    pixels[i+k]=(byte)Math.Max(0,Math.Min(keep,(pixels[i+k]-ink[cell*4+k])*keep/Math.Max(1,255-alpha)));
                pixels[i+3]=(byte)keep;
            }
        }
        static bool HasTexture(byte[] pixels)
        {
            // Some protected surfaces return a successful, solid-colour copy. Inspect only
            // clear desktop samples and ignore isolated edge pixels from the occlusion mask.
            var bins=new int[3,256]; int count=0;
            for(int i=0;i<pixels.Length;i+=4*17)
            {
                if(pixels[i+3]<250) continue;
                for(int k=0;k<3;k++) bins[k,pixels[i+k]*255/pixels[i+3]]++;
                count++;
            }
            if(count<8) return false;
            int trim=Math.Max(1,count/100);
            for(int k=0;k<3;k++)
            {
                int low=0,high=255,n=0;
                while(low<255 && n+bins[k,low]<=trim) n+=bins[k,low++];
                n=0;
                while(high>low && n+bins[k,high]<=trim) n+=bins[k,high--];
                if(high-low>6) return true;
            }
            return false;
        }
        static bool HasBlackPatch(byte[] pixels,int width,int height)
        {
            // A protected window can occupy just part of an otherwise textured plate.
            // Test overlapping squares of exposed desktop; the dark heart and monitor gaps
            // have already been masked out.
            int side=Math.Max(16,Math.Min(width,height)/10),step=side/2;
            for(int y=0;y+side<=height;y+=step) for(int x=0;x+side<=width;x+=step)
            {
                bool black=true;
                for(int dy=0;black && dy<side;dy+=2) for(int dx=0;dx<side;dx+=2)
                {
                    int i=((y+dy)*width+x+dx)*4;
                    if(pixels[i+3]<250 || pixels[i]>12 || pixels[i+1]>12 || pixels[i+2]>12)
                    { black=false; break; }
                }
                if(black) return true;
            }
            return false;
        }
        BitmapSource Capture(Window pet,Drawing.Rectangle area)
        {
            try
            {
                int stride=area.Width*4; var pixels=new byte[stride*area.Height];
                bool sampled=false;
                using(new PinnedViewer.PhysicalCoordinates(true))
                    foreach(var screen in Forms.Screen.AllScreens)
                    {
                        var tile=Drawing.Rectangle.Intersect(screen.Bounds,area);
                        if(tile.Width<=0 || tile.Height<=0) continue;
                        var source=new FormatConvertedBitmap(GrandWave.CapturePixels(tile),PixelFormats.Pbgra32,null,0);
                        source.CopyPixels(Int32Rect.Empty,pixels,stride,(tile.Y-area.Y)*stride+4*(tile.X-area.X));
                        sampled=true;
                    }
                if(!sampled || !HasTexture(pixels)) return null;
                MaskPet(pet,area,pixels);
                if(HasBlackPatch(pixels,area.Width,area.Height))
                {
                    for(int i=0;i<pixels.Length;i+=4)
                    {
                        int alpha=pixels[i+3]; if(alpha==0) continue;
                        int shade=Math.Max(pixels[i],Math.Max(pixels[i+1],pixels[i+2]))*255/alpha;
                        if(shade>=30) continue;
                        // Reveal the real desktop for the black samples, feathering into trusted colour.
                        int factor=Math.Max(0,shade-14);
                        for(int k=0;k<4;k++) pixels[i+k]=(byte)(pixels[i+k]*factor/16);
                    }
                }
                if(!HasTexture(pixels)) return null;
                // On a moving/retriggered lens, the screen can contain our previous refraction.
                // Keep the original, masked pixels in the overlap instead of feeding it back.
                var old=image.Source as BitmapSource;
                var overlap=Drawing.Rectangle.Intersect(plate,area);
                if(old!=null && overlap.Width>0 && overlap.Height>0)
                {
                    var src=new Int32Rect(overlap.X-plate.X,overlap.Y-plate.Y,overlap.Width,overlap.Height);
                    old.CopyPixels(src,pixels,stride,(overlap.Y-area.Y)*stride+4*(overlap.X-area.X));
                }
                var result=BitmapSource.Create(area.Width,area.Height,96,96,PixelFormats.Pbgra32,null,pixels,stride);
                result.Freeze(); return result;
            }
            catch(Win32Exception) { return null; }
            catch(ExternalException) { return null; }
            catch(InvalidOperationException) { return null; }
        }
        public bool Advance(Window pet,Point heart,double now,double age,double weight,bool start)
        {
            if(age<0 || age>=PetArt.MealImpactEnd(weight) || !pet.IsVisible || pet.ActualWidth<=0 || pet.ActualHeight<=0) return false;
            body=new WindowInteropHelper(pet).Handle;
            Rect frame=ReferenceTether.PhysicalPetFrame(body);
            if(frame.IsEmpty || frame.Width<=0 || frame.Height<=0) return false;
            double scale=frame.Width/pet.ActualWidth;
            Point c=new Point(frame.X+heart.X*scale,frame.Y+heart.Y*frame.Height/pet.ActualHeight);
            int span=(int)Math.Ceiling(frame.Width*1.28);
            bool moved=Math.Abs(c.X-plate.X-plate.Width*0.5)>span*0.15 || Math.Abs(c.Y-plate.Y-plate.Height*0.5)>span*0.15;
            bool refresh=plate.IsEmpty || ((moved || span!=plate.Width) && now-lastCapture>=0.18);
            if(refresh)
            {
                var area=new Drawing.Rectangle((int)Math.Floor(c.X-span*0.5),(int)Math.Floor(c.Y-span*0.5),span,span);
                var sample=Capture(pet,area);
                if(sample==null) return false;
                image.Source=sample; plate=area; lastCapture=now;
            }
            if(start) { from=power; fromReach=reach; }
            double rise=0.14+0.085*weight;
            double back=(age-rise)/(PetArt.MealImpactEnd(weight)-rise);
            double swell=IdleMotion.Ease(age/rise)*(1-IdleMotion.Ease(back/0.61));
            double recoil=IdleMotion.Beat(back-0.43,0.22,0.24,0.57);
            double handoff=1-IdleMotion.Ease(age/0.10);
            power=(0.23+0.30*weight)*(swell-0.37*recoil)+from*handoff;
            reach=0.33+0.085*weight+0.035*swell;
            if(fromReach>0) reach+=(fromReach-reach)*handoff;
            lens.Set(new Point((c.X-plate.X)/plate.Width,(c.Y-plate.Y)/plate.Height),
                frame.Width*reach/plate.Width,power);
            if(Topmost!=pet.Topmost) Topmost=pet.Topmost;
            if(!IsVisible)
            {
                Left=pet.Left+(plate.X-frame.X)/scale; Top=pet.Top+(plate.Y-frame.Y)/scale;
                Width=Height=span/scale;
                using(new PinnedViewer.PhysicalCoordinates(true))
                {
                    new WindowInteropHelper(this).EnsureHandle();
                    if(!SetWindowPos(handle,body,plate.X,plate.Y,plate.Width,plate.Height,0x210)) return false;
                    Show();
                }
            }
            // WPF can apply the new monitor's suggested DPI rectangle after Show returns.
            var actual=ReferenceTether.PhysicalPetFrame(handle);
            if(actual.X!=plate.X || actual.Y!=plate.Y || actual.Width!=plate.Width ||
               actual.Height!=plate.Height || GetWindow(body,2)!=handle)
                using(new PinnedViewer.PhysicalCoordinates(true))
                    if(!SetWindowPos(handle,body,plate.X,plate.Y,plate.Width,plate.Height,0x210)) return false;
            return true;
        }
    }
}
