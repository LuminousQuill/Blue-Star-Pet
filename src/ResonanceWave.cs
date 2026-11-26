using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BlueStar
{
    // A separate layered window lets the wave travel beyond the pet's drag-and-drop target.
    sealed class ResonanceWave : Window
    {
        public const double Duration = 1.28;
        readonly Point origin;
        readonly double reach;
        double phase;
        static readonly Pen[] band = {
            Stroke(28,49,131,255,28), Stroke(80,51,157,255,13),
            Stroke(196,101,220,255,5), Stroke(240,218,252,255,1.8)
        };

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);

        public ResonanceWave(Window owner, Rect display, Point center)
        {
            Owner=owner; Topmost=owner.Topmost; ShowActivated=false; ShowInTaskbar=false;
            WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true; Background=Brushes.Transparent; IsHitTestVisible=false;
            Focusable=false; WindowStartupLocation=WindowStartupLocation.Manual;
            reach=0.55*Math.Max(display.Width,display.Height);
            double extent=reach+16;
            Rect bounds=Rect.Intersect(display,new Rect(center.X-extent,center.Y-extent,2*extent,2*extent));
            Left=bounds.Left; Top=bounds.Top; Width=bounds.Width; Height=bounds.Height;
            origin=new Point(center.X-Left,center.Y-Top);
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            MakeClickThrough(this);
        }
        internal static void MakeClickThrough(Window window)
        {
            var hwnd=new WindowInteropHelper(window).Handle;
            // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW.
            SetWindowLong(hwnd,-20,GetWindowLong(hwnd,-20)|0x20|0x08000000|0x80);
        }
        static Pen Stroke(byte a,byte r,byte g,byte b,double width)
        {
            var p=new Pen(new SolidColorBrush(Color.FromArgb(a,r,g,b)),width);
            p.Freeze(); return p;
        }
        public void Advance(double seconds)
        {
            phase=Math.Min(1,Math.Max(0,seconds/Duration));
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            DrawFront(dc,origin,reach,phase);
        }
        internal static void DrawFront(DrawingContext dc,Point center,double radius,double phase)
        {
            if (phase<0 || phase>=1) return;
            double r=18+(radius-18)*(1-Math.Pow(1-phase,1.35));
            dc.PushOpacity(Math.Min(1,(1-phase)/0.44));
            foreach (var pen in band) dc.DrawEllipse(null,pen,center,r,r);
            dc.Pop();
        }
    }
}
