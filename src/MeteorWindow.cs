using System;
using System.Windows;
using System.Windows.Media;

namespace BlueStar
{
    sealed class MeteorWindow : Window
    {
        struct Sample { public Point Point; public double Time; }
        readonly Sample[] history=new Sample[56];
        readonly Point[] left=new Point[57],right=new Point[57];
        readonly MeteorFlight flight;
        readonly Point offset;
        int count,write;
        double age,next,previousTime,previousScale,previousTilt,unit,strength;
        Point previousCenter,head;
        static readonly Brush Tail=new SolidColorBrush(Color.FromArgb(221,144,222,255));
        static readonly Brush Fringe=new SolidColorBrush(Color.FromArgb(157,48,134,218));
        static readonly Brush Glow=new RadialGradientBrush(Color.FromArgb(222,173,239,255),Color.FromArgb(0,44,144,250));
        static readonly Geometry Spark=Geometry.Parse("M 0,-3.6 L 0.8,-0.7 3.2,0 0.8,0.6 0,3.6 -0.7,0.7 -3.2,0 -0.7,-0.6 Z");

        public MeteorWindow(Window owner,MeteorFlight flight,Rect display)
        {
            this.flight=flight;
            Owner=owner; Topmost=owner.Topmost; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.Manual; ShowInTaskbar=false; ShowActivated=false;
            AllowsTransparency=true; Background=Brushes.Transparent; IsHitTestVisible=false; Focusable=false;
            Rect b=Rect.Intersect(display,flight.Bounds);
            Left=b.Left; Top=b.Top; Width=b.Width; Height=b.Height; offset=b.TopLeft;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e); ResonanceWave.MakeClickThrough(this);
        }
        public void Advance(double time,Point center,double scale,double tilt,double weight)
        {
            age=time; unit=scale; strength=weight;
            if(count==0) { previousCenter=center; previousScale=scale; previousTilt=tilt; }
            // Retain historical screen positions, including the straight portion after the head has turned.
            next=Math.Max(next,time-MeteorFlight.LongestTrail);
            for(;next<=Math.Min(time,MeteorFlight.Impact);next+=0.01)
            {
                double u=time>previousTime?Math.Max(0,Math.Min(1,(next-previousTime)/(time-previousTime))):1;
                Point p=flight.Sample(next,previousCenter+(center-previousCenter)*u,previousScale+(scale-previousScale)*u,previousTilt+(tilt-previousTilt)*u);
                history[write]=new Sample {Point=(Point)(p-offset),Time=next};
                write=(write+1)%history.Length; count=Math.Min(count+1,history.Length);
            }
            head=(Point)(flight.Sample(time,center,scale,tilt)-offset);
            previousTime=time; previousCenter=center; previousScale=scale; previousTilt=tilt;
            InvalidateVisual();
        }
        void Ribbon(DrawingContext dc,Brush brush,double width)
        {
            int n=0;
            Point prior=new Point();
            for(int i=0;i<=count;i++)
            {
                bool isHead=i==count;
                if(isHead && age>=MeteorFlight.Impact) break;
                var sample=isHead?new Sample {Point=head,Time=age}:history[(write-count+i+history.Length)%history.Length];
                double life=1-(age-sample.Time)/MeteorFlight.TrailLifeAt(sample.Time);
                if(life<=0) continue;
                Point p=sample.Point;
                if(n==0) { left[0]=right[0]=prior=p; n=1; continue; }
                Vector d=p-prior;
                if(d.LengthSquared<0.0001) continue;
                Vector normal=new Vector(-d.Y,d.X)*(width*unit*life*(0.44+0.56*life)/d.Length);
                left[n]=p+normal; right[n]=p-normal; prior=p; n++;
            }
            if(n<2) return;
            var g=new StreamGeometry();
            using(var c=g.Open())
            {
                c.BeginFigure(left[0],true,true);
                for(int i=1;i<n;i++) c.LineTo(left[i],true,false);
                for(int i=n-1;i>0;i--) c.LineTo(right[i],true,false);
            }
            dc.DrawGeometry(brush,null,g);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); DrawTrail(dc);
        }
        internal void DrawTrail(DrawingContext dc)
        {
            dc.PushOpacity(strength);
            Ribbon(dc,Fringe,3.0); Ribbon(dc,Tail,1.18);
            if(age<MeteorFlight.Impact)
            {
                dc.PushOpacity(1-IdleMotion.Ease((age-MeteorFlight.Impact+0.055)/0.055));
                dc.PushTransform(new TranslateTransform(head.X,head.Y));
                dc.PushTransform(new ScaleTransform(unit,unit));
                dc.DrawEllipse(Glow,null,new Point(),8,8); dc.DrawGeometry(Brushes.White,null,Spark);
                dc.Pop(); dc.Pop(); dc.Pop();
            }
            dc.Pop();
        }
    }
}
