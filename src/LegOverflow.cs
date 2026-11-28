using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BlueStar
{
    sealed class LegOverflow : Window
    {
        readonly Grid view=new Grid();
        readonly OrthographicCamera camera;
        readonly IntPtr body;
        IntPtr handle;
        Rect lastFrame=Rect.Empty;
        Rect inside=Rect.Empty;

        public LegOverflow(Window owner,Viewport3D viewport,Viewport3D foreground)
        {
            Owner=owner; body=new WindowInteropHelper(owner).Handle;
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            ShowActivated=false; ShowInTaskbar=false; Focusable=false; IsHitTestVisible=false;
            AllowsTransparency=true; Background=Brushes.Transparent;
            WindowStartupLocation=WindowStartupLocation.Manual;
            Left=owner.Left; Top=owner.Top; Width=owner.Width; Height=owner.Height;
            // Separate depth buffers preserve the main window's alpha and foreground order.
            camera=(OrthographicCamera)viewport.Camera; foreground.Camera=camera;
            view.Children.Add(viewport); view.Children.Add(foreground);
            Content=view; view.SizeChanged+=delegate { CutOutBody(); };
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e); handle=new WindowInteropHelper(this).Handle;
            ResonanceWave.MakeClickThrough(this);
        }
        void CutOutBody()
        {
            double w=view.ActualWidth,h=view.ActualHeight;
            if(w<=0 || h<=0 || inside.IsEmpty) return;
            var outside=new GeometryGroup { FillRule=FillRule.EvenOdd };
            outside.Children.Add(new RectangleGeometry(new Rect(0,0,w,h)));
            outside.Children.Add(new RectangleGeometry(new Rect(w*inside.X,h*inside.Y,w*inside.Width,h*inside.Height)));
            outside.Freeze(); view.Clip=outside;
        }
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);

        static int EdgePadding(double excess,double span)
        {
            return excess<=0?0:(int)Math.Ceiling(span*Math.Ceiling((excess+0.004)*8)/8);
        }
        public void Follow(Rect3D skin,double opacity)
        {
            Rect b=ReferenceTether.PhysicalPetFrame(body);
            if(b.IsEmpty || b.Width<=0 || b.Height<=0) return;
            // A foot over one edge needs only a narrow strip of layered desktop pixels.
            int left=EdgePadding(-skin.X-0.5,b.Width),right=EdgePadding(skin.X+skin.SizeX-0.5,b.Width);
            int top=EdgePadding(skin.Y+skin.SizeY-0.5,b.Height),bottom=EdgePadding(-skin.Y-0.5,b.Height);
            bool horizontal=left+right>0,vertical=top+bottom>0;
            var frame=new Rect(
                new Point(left==0 && !vertical?b.Right:b.Left-left,top==0 && !horizontal?b.Bottom:b.Top-top),
                new Point(right==0 && !vertical?b.Left:b.Right+right,bottom==0 && !horizontal?b.Top:b.Bottom+bottom));
            var cut=new Rect((b.Left-frame.X)/frame.Width,(b.Top-frame.Y)/frame.Height,b.Width/frame.Width,b.Height/frame.Height);
            if(Topmost!=Owner.Topmost) Topmost=Owner.Topmost;
            bool hidden=!IsVisible;
            if(hidden) Show();
            if(cut!=inside)
            {
                inside=cut;
                camera.Width=frame.Width/b.Width;
                camera.Position=new Point3D((frame.X+frame.Width/2-b.Left-b.Width/2)/b.Width,
                    (b.Top+b.Height/2-frame.Y-frame.Height/2)/b.Width,3);
                CutOutBody();
            }
            if(hidden || frame!=lastFrame)
                using(new PinnedViewer.PhysicalCoordinates(true))
                    SetWindowPos(handle,IntPtr.Zero,(int)frame.X,(int)frame.Y,(int)frame.Width,(int)frame.Height,0x0214);
            lastFrame=frame; view.Opacity=opacity;
        }
    }
}
