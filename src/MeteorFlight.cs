using System;
using System.Windows;
using System.Windows.Media;

namespace BlueStar
{
    static class HeartOutline
    {
        internal static readonly Geometry Shape=Geometry.Parse("M 0,-12 C -12,-33 -33,-30 -38,-12 C -46,9 -20,22 0,40 C 17,23 43,9 39,-12 C 35,-31 13,-34 0,-12 Z");
        static readonly Point[] rim=new Point[256];
        static HeartOutline()
        {
            Shape.Freeze();
            var path=Shape.GetFlattenedPathGeometry(0.06,ToleranceType.Absolute);
            for(int i=0;i<rim.Length;i++)
            {
                Point tangent;
                path.GetPointAtFractionLength(i/(double)rim.Length,out rim[i],out tangent);
            }
        }
        public static Point At(double fraction)
        {
            double index=(fraction-Math.Floor(fraction))*rim.Length;
            int i=(int)index%rim.Length;
            return rim[i]+(rim[(i+1)%rim.Length]-rim[i])*(index-Math.Floor(index));
        }
        public static Vector Tangent(double fraction)
        {
            Vector v=At(fraction+0.004)-At(fraction-0.004); v.Normalize(); return v;
        }
        public static double Facing(Vector direction)
        {
            double best=double.MinValue; int index=0;
            for(int i=0;i<rim.Length;i++)
            {
                Vector v=(Vector)rim[i];
                double dot=Vector.Multiply(v,direction)/v.Length;
                if(dot>best) { best=dot; index=i; }
            }
            return index/(double)rim.Length;
        }
    }

    sealed class MeteorFlight
    {
        public const double Pass=0.40, RimTime=1.10, Dive=1.43, Impact=1.72;
        public const double TailLife=0.21, LongestTrail=0.46, FlashTime=Impact+TailLife+0.12, Duration=2.87;
        public readonly double NearRim,Entry,Arc;
        public readonly Vector CatchSide;
        public readonly Rect Bounds;
        readonly Vector travel;
        readonly Point pass,bend1,bend2,rimIn,rimOut,dive1,dive2;
        readonly Point origin;
        readonly double scale,rotation,run;
        MeteorFlight(Point center,double size,double tilt,Vector heading,int hand,double distance)
        {
            origin=center; scale=size; rotation=tilt; run=distance;
            travel=Rotate(heading,-tilt);
            Vector side=Rotate(new Vector(-heading.Y,heading.X)*hand,-tilt);
            pass=(Point)(side*102);
            NearRim=HeartOutline.Facing(side);
            Entry=HeartOutline.Facing(travel*0.68-side*0.73);
            rimIn=HeartOutline.At(Entry);
            var tangent=HeartOutline.Tangent(Entry);
            int winding=Vector.CrossProduct((Vector)rimIn,tangent)*Vector.CrossProduct(side,travel)>0?1:-1;
            Arc=winding*0.155; rimOut=HeartOutline.At(Entry+Arc);
            CatchSide=Rotate((Vector)HeartOutline.At(Entry+0.27*Arc),tilt); CatchSide.Normalize();
            // Match velocity at both joins. The star is already abeam of the core at the first join.
            bend1=pass+travel*(run/Pass*(RimTime-Pass)/3);
            bend2=rimIn-tangent*(winding*25);
            dive1=rimOut+HeartOutline.Tangent(Entry+Arc)*(winding*11);
            dive2=(Point)((Vector)rimOut*0.24);
            Rect bounds=Rect.Empty;
            for(int i=0;i<=96;i++) bounds.Union(Sample(Impact*i/96,center,size,tilt));
            bounds.Inflate(26*size+3,26*size+3); Bounds=bounds;
        }
        public static MeteorFlight Choose(Random random,Point center,double size,double tilt,Rect display)
        {
            int first=random.Next(8); double slope=(23+random.NextDouble()*18)*Math.PI/180;
            double distance=360+random.NextDouble()*89;
            MeteorFlight best=null; double least=double.PositiveInfinity;
            for(int i=0;i<16;i++)
            {
                int pick=(first+i)%8, hand=(pick&1)==0?1:-1;
                var dir=new Vector(((pick&2)==0?1:-1)*Math.Cos(slope),((pick&4)==0?1:-1)*Math.Sin(slope));
                var flight=new MeteorFlight(center,size,tilt,dir,hand,i<8?distance:distance*0.65);
                Rect b=flight.Bounds;
                double excess=Math.Max(0,display.Left-b.Left)+Math.Max(0,b.Right-display.Right)
                    +Math.Max(0,display.Top-b.Top)+Math.Max(0,b.Bottom-display.Bottom);
                if(excess==0) return flight;
                if(excess<least) { least=excess; best=flight; }
            }
            return best;
        }
        static Vector Rotate(Vector v,double degrees)
        {
            double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
            return new Vector(c*v.X-s*v.Y,s*v.X+c*v.Y);
        }
        public static double TrailLifeAt(double sampleTime)
        {
            // Keep the final piece of the flyby visible through the hook, then shorten near the rim.
            return 0.17+0.285*IdleMotion.Beat(sampleTime-0.17,0.20,0.33,0.93)
                +0.04*IdleMotion.Ease((sampleTime-0.74)/0.42);
        }
        static Point Cubic(Point a,Point b,Point c,Point d,double t)
        {
            double v=1-t;
            return a+(b-a)*(3*v*v*t)+(c-a)*(3*v*t*t)+(d-a)*(t*t*t);
        }
        public Point Sample(double time,Point live,double liveScale,double liveTilt)
        {
            Point p;
            if(time<=Pass) p=pass+travel*(run*(time/Pass-1));
            else if(time<RimTime) p=Cubic(pass,bend1,bend2,rimIn,(time-Pass)/(RimTime-Pass));
            else if(time<Dive) p=HeartOutline.At(Entry+Arc*(time-RimTime)/(Dive-RimTime));
            else p=Cubic(rimOut,dive1,dive2,new Point(),Math.Min(1,(time-Dive)/(Impact-Dive)));
            double tether=IdleMotion.Ease((time-Pass)/(RimTime-Pass));
            return origin+(live-origin)*tether+Rotate((Vector)p,rotation+(liveTilt-rotation)*tether)*(scale+(liveScale-scale)*tether);
        }
    }
}
