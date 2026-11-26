using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace BlueStar
{
    // Compare unwrapped angles inside a rolling window. Backtracking subtracts from a turn.
    sealed class OrbitGesture
    {
        struct Sample { public double Time,Angle; }
        readonly Queue<Sample> samples=new Queue<Sample>();
        const double TriggerAngle=1.8*2*Math.PI;
        double previous,total,lastValid;
        bool outsideBand;
        public void Reset() { samples.Clear(); total=0; outsideBand=false; }

        public int Trace(Vector offset,double width,double now)
        {
            while(samples.Count>0 && now-samples.Peek().Time>5) samples.Dequeue();
            double distance=offset.Length;
            if(distance<width*0.08 || distance>width*0.78)
            {
                outsideBand=true;
                return 0;
            }
            double angle=Math.Atan2(offset.Y,offset.X);
            if(samples.Count>0)
            {
                double delta=Math.IEEERemainder(angle-previous,Math.PI*2);
                // Bridge brief radial slips, but never credit a shortcut through the centre.
                if((outsideBand && now-lastValid>0.5) || Math.Abs(delta)>0.70*Math.PI) Reset();
                else total+=delta;
            }
            previous=angle; lastValid=now; outsideBand=false;
            foreach(var sample in samples)
                if(Math.Abs(total-sample.Angle)>=TriggerAngle) return Math.Sign(total-sample.Angle);
            samples.Enqueue(new Sample { Time=now,Angle=total });
            return 0;
        }
    }

    sealed class FileStar
    {
        public string[] Paths { get; private set; }
        public Point? Dock;
        public FormattedText Label,CountLabel;
        public readonly double Radius,Phase,Born,Speed;
        readonly double entryRadius;
        public readonly SolidColorBrush Color;
        public readonly RadialGradientBrush Halo;
        public readonly Pen Track;

        public FileStar(string[] paths,double radius,double entry,double phase,int direction,double now,SolidColorBrush ink,RadialGradientBrush halo)
        {
            Paths=paths; Radius=radius; entryRadius=entry; Phase=phase; Born=now; Speed=direction*0.46;
            Color=ink; Halo=halo;
            Track=new Pen(Color,1.4) { DashStyle=new DashStyle(new double[]{2.3,3.4},0) }; Track.Freeze();
        }
        public void Delivered(string[] paths)
        {
            Paths=Paths.Except(paths,StringComparer.OrdinalIgnoreCase).ToArray();
            Label=CountLabel=null;
        }
        public static SolidColorBrush BatchColor(int index)
        {
            double hue=2*Math.PI*(0.52+index*0.381966);
            Func<double,byte> channel=shift=>(byte)(168+83*Math.Cos(hue+shift));
            var color=System.Windows.Media.Color.FromRgb(channel(0),channel(-2*Math.PI/3),channel(2*Math.PI/3));
            var ink=new SolidColorBrush(color); ink.Freeze(); return ink;
        }
        public static RadialGradientBrush Glow(SolidColorBrush ink)
        {
            var color=ink.Color;
            var halo=new RadialGradientBrush(color,System.Windows.Media.Color.FromArgb(2,color.R,color.G,color.B)); halo.Freeze(); return halo;
        }
        public Point Position(Point heart,double width,double now)
        {
            double radius=Radius+(entryRadius-Radius)*Math.Exp(-8*Math.Max(0,now-Born));
            return heart+Direction(now)*(radius*width);
        }
        public Vector Direction(double now)
        {
            double angle=Phase+(now-Born)*Speed;
            return new Vector(Math.Cos(angle),Math.Sin(angle));
        }
    }

    sealed class FileOrbits : FrameworkElement
    {
        readonly List<FileStar> stars=new List<FileStar>();
        public const int PageSize=5;
        public const double ShelfWidth=228;
        const double LabelWidth=156;
        static readonly Typeface Font=new Typeface("Microsoft YaHei UI");
        static readonly Brush LabelBack=new SolidColorBrush(Color.FromArgb(236,13,22,31));
        static readonly Pen LabelEdge=new Pen(new SolidColorBrush(Color.FromArgb(170,65,86,99)),0.8);
        public bool Sealed,OnRight;
        public double Fold {get; private set;}
        public int Page {get; private set;}
        public int Pages {get {return Math.Max(1,(stars.Count+PageSize-1)/PageSize);} }
        public int Rows {get {return Math.Min(PageSize,stars.Count-Page*PageSize);} }
        public Point ShelfOrigin;
        bool dirty=true;
        public bool NeedsDraw {get; private set;}
        public double ShelfHeight {get {return Math.Max(90,Rows*42+24)+(Pages>1?30:0);} }
        static readonly double[] lanes={0.23,0.30,0.37};
        SolidColorBrush nextColor=FileStar.BatchColor(0);
        RadialGradientBrush nextHalo;
        static readonly Geometry Glint=Geometry.Parse("M 0,-10 Q 1,-2 8,0 Q 1,1 0,10 Q -1,1 -8,0 Q -1,-2 0,-10 Z");
        static readonly Pen Edge=new Pen(new SolidColorBrush(Color.FromArgb(175,13,25,41)),1.2);
        public FileStar Grabbed;
        public Point GrabPoint;
        public Point? Pointer;
        int sequence;
        double now;
        Point heart;
        public FileOrbits() { IsHitTestVisible=false; nextHalo=FileStar.Glow(nextColor); }
        public void Advance(Point center,double time)
        {
            double dt=Math.Max(0,time-now);
            heart=center; now=time;
            double lastFold=Fold;
            Fold=Math.Max(0,Math.Min(1,Fold+dt*(Sealed?1:-1)/0.94));
            NeedsDraw=dirty || Fold!=lastFold || Pointer.HasValue || Grabbed!=null || (Fold<1 && stars.Count>0);
            dirty=false;
            if(Fold>0)
            {
                double blend=1-Math.Exp(-13*dt);
                for(int i=0;i<stars.Count;i++)
                {
                    Point target=Slot(i%PageSize);
                    var star=stars[i];
                    if(!star.Dock.HasValue || (target-star.Dock.Value).LengthSquared<0.01)
                        star.Dock=target;
                    else { star.Dock=star.Dock.Value+(target-star.Dock.Value)*blend; NeedsDraw=true; }
                    if(i/PageSize==Page && (time-star.Born<0.9 || star.Label==null)) NeedsDraw=true;
                }
            }
            if(NeedsDraw) InvalidateVisual();
        }
        public void ChangePage(int delta)
        {
            int page=Math.Max(0,Math.Min(Pages-1,Page+delta));
            if(page!=Page) { Page=page; dirty=true; }
        }
        Point Slot(int row)
        {
            int n=Math.Max(1,Rows);
            row=Math.Min(row,n-1);
            double u=n==1?0.5:row/(double)(n-1);
            double inset=n==1?24:16+ArcBow(n)*(1-(2*u-1)*(2*u-1));
            return ShelfOrigin+new Vector(OnRight?inset:ShelfWidth-inset,
                (ShelfHeight-(Pages>1?30:0))/2+(row-(n-1)*0.5)*42);
        }
        static double ArcBow(int n) {return Math.Min(30,9*(n-1));}
        public void ClearPointer() { Pointer=null; InvalidateVisual(); }
        public void Store(string[] paths,Vector offset,int direction,double time)
        {
            double entry=offset.Length/ActualWidth;
            double radius=lanes.OrderBy(lane=>stars.Count(s=>s.Radius==lane)).ThenBy(lane=>Math.Abs(lane-entry)).First();
            double angle=Math.Atan2(offset.Y,offset.X);
            // Leave space between batches parked with the same gesture.
            for(int i=0;i<stars.Count;i++)
            {
                var pos=new Vector(Math.Cos(angle),Math.Sin(angle))*radius;
                if(!stars.Any(s=>(s.Direction(time)*s.Radius-pos).Length*ActualWidth<16)) break;
                angle+=0.30;
            }
            stars.Add(new FileStar(paths,radius,entry,angle,direction,time,nextColor,nextHalo));
            dirty=true;
            if(Sealed) Page=Pages-1;
            nextColor=FileStar.BatchColor(++sequence); nextHalo=FileStar.Glow(nextColor); InvalidateVisual();
        }
        public FileStar Hit(Point point)
        {
            FileStar closest=null; double best=16*16;
            for(int i=0;i<stars.Count;i++)
            {
                if(Fold>0.5 && i/PageSize!=Page) continue;
                var star=stars[i]; Point p=DisplayPosition(star);
                double d=(p-point).LengthSquared;
                if(d<=best) { best=d; closest=star; }
                if(Sealed && i/PageSize==Page && Progress(star)>0.75 && LabelBounds(p).Contains(point)) return star;
            }
            return closest;
        }
        public void Remove(FileStar star) { stars.Remove(star); dirty=true; ChangePage(0); InvalidateVisual(); }
        public void Restore(FileStar star)
        {
            int i=stars.FindIndex(other=>other.Born>star.Born);
            stars.Insert(i<0?stars.Count:i,star); dirty=true; InvalidateVisual();
        }
        double Progress(FileStar star)
        {
            double u=Math.Max(0,Math.Min(Fold,(now-star.Born)/0.82));
            return u*u*(3-2*u);
        }
        Point Guide(FileStar star,Point start,Point end)
        {
            Vector arm=start-heart;
            return start+(end-start)*0.28+new Vector(-arm.Y,arm.X)*(0.64*Math.Sign(star.Speed));
        }
        Point DisplayPosition(FileStar star)
        {
            Point a=star.Position(heart,ActualWidth,now);
            if(Fold==0 || !star.Dock.HasValue) return a;
            Point b=star.Dock.Value; double u=Progress(star);
            Point c=Guide(star,a,b);
            return a+(c-a)*(2*u*(1-u))+(b-a)*(u*u);
        }
        Rect LabelBounds(Point p) { return new Rect(OnRight?p.X+11:p.X-11-LabelWidth,p.Y-12,LabelWidth,24); }
        void DrawLabel(DrawingContext dc,FileStar star,Point p)
        {
            if(star.Paths.Length==0) return;
            double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if(star.Label==null || star.Label.PixelsPerDip!=dpi)
            {
                star.CountLabel=new FormattedText(star.Paths.Length+" 项",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Font,11,star.Color,dpi);
                star.Label=new FormattedText(Path.GetFileName(star.Paths[0]),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Font,12,Brushes.AliceBlue,dpi);
                star.Label.MaxTextWidth=Math.Max(12,LabelWidth-star.CountLabel.Width-24);
                star.Label.MaxLineCount=1; star.Label.Trimming=TextTrimming.CharacterEllipsis;
            }
            var rect=LabelBounds(p);
            dc.DrawRoundedRectangle(LabelBack,LabelEdge,rect,5,5);
            dc.DrawText(star.Label,new Point(rect.X+8,p.Y-star.Label.Height/2));
            dc.DrawText(star.CountLabel,new Point(rect.Right-8-star.CountLabel.Width,p.Y-star.CountLabel.Height/2));
        }
        protected override void OnRender(DrawingContext dc)
        {
            DrawOrbits(dc);
            if(Pointer.HasValue)
            {
                dc.DrawEllipse(nextHalo,null,Pointer.Value,19,19);
                DrawStar(dc,Pointer.Value,nextColor);
            }
        }
        internal void DrawOrbits(DrawingContext dc)
        {
            double mix=Fold*Fold*(3-2*Fold);
            foreach(var star in stars)
            {
                if(mix<1)
                {
                    double radius=ActualWidth*star.Radius;
                    dc.PushOpacity(0.80*(1-mix)); dc.DrawEllipse(null,star.Track,heart,radius,radius); dc.Pop();
                }
            }
            Point? previous=null;
            for(int i=0;i<stars.Count;i++)
            {
                var star=stars[i]; bool onPage=i/PageSize==Page;
                if(!onPage && mix==1) continue;
                double u=Progress(star);
                var p=star==Grabbed?GrabPoint:DisplayPosition(star);
                dc.PushOpacity(onPage?1:1-mix);
                if(onPage && star.Dock.HasValue && u>0 && u<1)
                {
                    Point a=star.Position(heart,ActualWidth,now),b=star.Dock.Value;
                    var track=new StreamGeometry();
                    using(var g=track.Open()) { g.BeginFigure(a,false,false); g.QuadraticBezierTo(Guide(star,a,b),b,true,false); }
                    dc.PushOpacity(0.55*Math.Sin(u*Math.PI)); dc.DrawGeometry(null,star.Track,track); dc.Pop();
                }
                if(onPage && u>0.55 && star.Dock.HasValue)
                {
                    Point dock=star.Dock.Value;
                    dc.PushOpacity((u-0.55)/0.45);
                    if(previous.HasValue)
                    {
                        int gaps=Math.Max(1,Rows-1);
                        Point a=previous.Value, mid=a+(dock-a)*0.5;
                        mid.X+=(OnRight?1:-1)*2*ArcBow(Rows)/(gaps*gaps);
                        var arc=new StreamGeometry();
                        using(var g=arc.Open()) { g.BeginFigure(a,false,false); g.QuadraticBezierTo(mid,dock,true,false); }
                        dc.PushOpacity(0.46); dc.DrawGeometry(null,star.Track,arc); dc.Pop();
                    }
                    previous=dock;
                    DrawLabel(dc,star,p); dc.Pop();
                }
                dc.DrawEllipse(star.Halo,null,p,16,16);
                DrawStar(dc,p,star.Color);
                dc.Pop();
            }
        }
        static void DrawStar(DrawingContext dc,Point p,Brush color)
        {
            dc.PushTransform(new TranslateTransform(p.X,p.Y));
            dc.DrawGeometry(color,Edge,Glint);
            dc.PushTransform(new ScaleTransform(0.50,0.50));
            dc.DrawGeometry(Brushes.White,null,Glint); dc.Pop(); dc.Pop();
        }
    }
}
