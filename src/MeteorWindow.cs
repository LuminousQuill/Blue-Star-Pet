using System;
using System.Windows;
using System.Windows.Media;

namespace BlueStar
{
    sealed class MeteorWindow : Window
    {
        readonly MeteorFlight flight;
        readonly Point offset;
        readonly Trail[] trails;
        double age,previousTime,previousScale,previousTilt,unit,strength;
        Point previousCenter;
        static readonly Brush Tail=new SolidColorBrush(Color.FromArgb(204,144,222,255));
        static readonly Brush Fringe=new SolidColorBrush(Color.FromArgb(121,48,134,218));
        static readonly Brush Glow=new RadialGradientBrush(Color.FromArgb(222,173,239,255),Color.FromArgb(0,44,144,250));
        static readonly Geometry Spark=Geometry.Parse("M 0,-3.6 L 0.8,-0.7 3.2,0 0.8,0.6 0,3.6 -0.7,0.7 -3.2,0 -0.7,-0.6 Z");

        public MeteorWindow(Window owner,MeteorFlight flight,Rect display)
        {
            this.flight=flight;
            trails=Array.ConvertAll(flight.Tracks,track=>new Trail(track));
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
            if(previousScale==0) { previousCenter=center; previousScale=scale; previousTilt=tilt; }
            // Keep already flown positions in screen space while the heart moves.
            foreach(var trail in trails)
            {
                if(time<trail.Track.Start) continue;
                trail.Next=Math.Max(trail.Next,time-MeteorFlight.LongestTrail);
                for(;trail.Next<=Math.Min(time,trail.Track.Impact);trail.Next+=0.01)
                {
                    double u=time>previousTime?Math.Max(0,Math.Min(1,(trail.Next-previousTime)/(time-previousTime))):1;
                    Point p=flight.Sample(trail.Track,trail.Next,previousCenter+(center-previousCenter)*u,
                        previousScale+(scale-previousScale)*u,previousTilt+(tilt-previousTilt)*u);
                    trail.Add((Point)(p-offset),trail.Next);
                }
                trail.Head=(Point)(flight.Sample(trail.Track,time,center,scale,tilt)-offset);
            }
            previousTime=time; previousCenter=center; previousScale=scale; previousTilt=tilt;
            InvalidateVisual();
        }
        sealed class Trail
        {
            struct Sample { public Point Point; public double Time; }
            readonly Sample[] history=new Sample[16];
            readonly Point[] left=new Point[17],right=new Point[17];
            int count,write;
            public readonly MeteorFlight.Track Track;
            public Point Head;
            public double Next;
            public Trail(MeteorFlight.Track track) { Track=track; Next=track.Start; }
            public void Add(Point p,double time)
            {
                history[write]=new Sample {Point=p,Time=time};
                write=(write+1)%history.Length; count=Math.Min(count+1,history.Length);
            }
            public void Ribbon(DrawingContext dc,Brush brush,double age,double width)
            {
                int n=0;
                Point prior=new Point();
                for(int i=0;i<=count;i++)
                {
                    bool isHead=i==count;
                    if(isHead && age>=Track.Impact) break;
                    var sample=isHead?new Sample {Point=Head,Time=age}:history[(write-count+i+history.Length)%history.Length];
                    double life=1-(age-sample.Time)/MeteorFlight.TrailLifeAt(Track,sample.Time);
                    if(life<=0) continue;
                    Point p=sample.Point;
                    if(n==0) { left[0]=right[0]=prior=p; n=1; continue; }
                    Vector d=p-prior;
                    if(d.LengthSquared<0.0001) continue;
                    Vector normal=new Vector(-d.Y,d.X)*(width*life*(0.44+0.56*life)/d.Length);
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
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); DrawTrail(dc);
        }
        internal void DrawTrail(DrawingContext dc)
        {
            dc.PushOpacity(strength);
            for(int i=0;i<trails.Length;i++)
            {
                var trail=trails[i];
                if(age<trail.Track.Start || age>=MeteorFlight.EndOfTrail(trail.Track)) continue;
                double size=i==0?1:0.64;
                dc.PushOpacity(i==0?1:0.46);
                trail.Ribbon(dc,Fringe,age,1.72*size*unit);
                trail.Ribbon(dc,Tail,age,0.68*size*unit);
                if(age<trail.Track.Impact)
                {
                    dc.PushOpacity(1-IdleMotion.Ease((age-trail.Track.Impact+0.018)/0.018));
                    dc.PushTransform(new MatrixTransform(unit*size,0,0,unit*size,trail.Head.X,trail.Head.Y));
                    dc.DrawEllipse(Glow,null,new Point(),6,6); dc.DrawGeometry(Brushes.White,null,Spark);
                    dc.Pop(); dc.Pop();
                }
                dc.Pop();
            }
            dc.Pop();
        }
    }
}
