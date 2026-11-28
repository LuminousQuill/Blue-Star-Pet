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

    enum MeteorPattern { Hook, Slingshot, Twin, Switchback }

    sealed class MeteorFlight
    {
        public const double LongestTrail=0.125;
        public readonly MeteorPattern Pattern;
        public readonly Vector CatchSide;
        public readonly Rect Bounds;
        public readonly Track[] Tracks;
        public double TailEnd { get { return EndOfTrail(Tracks[Tracks.Length-1]); } }
        public double FlashTime { get { return TailEnd+0.035; } }
        public double Duration { get { return FlashTime+0.54; } }
        readonly Point origin;
        readonly double scale,rotation;

        MeteorFlight(MeteorPattern pattern,Point center,double size,double tilt,Vector heading,int hand,double distance)
        {
            Pattern=pattern; origin=center; scale=size; rotation=tilt;
            Vector travel=Rotate(heading,-tilt);
            Vector side=Rotate(new Vector(-heading.Y,heading.X)*hand,-tilt);
            var primary=new Track(pattern,travel,side,distance,false);
            Tracks=pattern==MeteorPattern.Twin
                ?new[]{primary,new Track(pattern,travel,side,distance,true)}
                :new[]{primary};
            CatchSide=Rotate((Vector)primary.Sample(primary.RimTime),tilt);
            CatchSide.Normalize();
            Rect bounds=Rect.Empty;
            foreach(var track in Tracks)
                for(int i=0;i<=120;i++)
                    bounds.Union(Sample(track,track.Start+(track.Impact-track.Start)*i/120,center,size,tilt));
            bounds.Inflate(26*size+3,26*size+3); Bounds=bounds;
        }

        public static MeteorFlight Choose(Random random,Point center,double size,double tilt,Rect display,MeteorPattern? previous)
        {
            int pick=random.Next(previous.HasValue?3:4);
            if(previous.HasValue && pick>=(int)previous.Value) pick++;
            return Choose(random,center,size,tilt,display,(MeteorPattern)pick);
        }
        internal static MeteorFlight Choose(Random random,Point center,double size,double tilt,Rect display,MeteorPattern pattern)
        {
            int first=random.Next(8); double slope=(23+random.NextDouble()*18)*Math.PI/180;
            double distance=240+random.NextDouble()*65;
            MeteorFlight best=null; double least=double.PositiveInfinity;
            for(int i=0;i<16;i++)
            {
                int pick=(first+i)%8, hand=(pick&1)==0?1:-1;
                var dir=new Vector(((pick&2)==0?1:-1)*Math.Cos(slope),((pick&4)==0?1:-1)*Math.Sin(slope));
                var flight=new MeteorFlight(pattern,center,size,tilt,dir,hand,i<8?distance:distance*0.65);
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
        public static double TrailLifeAt(Track track,double time)
        {
            return Math.Min(LongestTrail,37/track.SpeedAt(time));
        }
        public static double EndOfTrail(Track track) { return track.Impact+TrailLifeAt(track,track.Impact); }
        public Point Sample(Track track,double time,Point live,double liveScale,double liveTilt)
        {
            double tether=IdleMotion.Ease((time-track.TurnTime)/(track.RimTime-track.TurnTime));
            return origin+(live-origin)*tether+Rotate((Vector)track.Sample(time),rotation+(liveTilt-rotation)*tether)
                *(scale+(liveScale-scale)*tether);
        }

        public sealed class Track
        {
            const int Steps=48;
            const double Acceleration=0.88;
            readonly Vector travel,side;
            readonly Vector[] path=new Vector[Steps+1],headings=new Vector[Steps+1];
            readonly double speed,angle,pull,step,fall;
            public readonly double Start,TurnTime,RimTime,Impact;

            public Track(MeteorPattern pattern,Vector travel,Vector side,double run,bool second)
            {
                this.travel=travel; this.side=side;
                Start=second?0.12:0;
                double length=197,turn=151,curve=3.23;
                speed=304;
                switch(pattern)
                {
                    case MeteorPattern.Hook: length=184; turn=153; curve=3.55; speed=296; break;
                    case MeteorPattern.Slingshot: length=230; turn=155; curve=2.95; speed=310; break;
                    case MeteorPattern.Switchback: length=204; turn=142; curve=3.15; speed=323; break;
                }
                if(second) length+=30;
                angle=turn*Math.PI/180; pull=curve; step=length/Steps;
                TurnTime=Start+run/speed;
                fall=length/(speed*(1+Acceleration/3));
                Impact=TurnTime+fall;

                // Integrate the tangent in equal distance steps. The straight extension shares its first tangent.
                headings[0]=travel;
                for(int i=1;i<=Steps;i++)
                {
                    headings[i]=Heading(i/(double)Steps);
                    path[i]=path[i-1]+(headings[i-1]+4*Heading((i-0.5)/Steps)+headings[i])*(step/6);
                }
                double outside=TurnTime,inside=Impact;
                for(int i=0;i<12;i++)
                {
                    double time=(outside+inside)*0.5;
                    if(HeartOutline.Shape.FillContains(Sample(time))) inside=time;
                    else outside=time;
                }
                RimTime=inside;
            }
            Vector Heading(double progress)
            {
                double turn=angle*Math.Pow(progress,pull);
                return travel*Math.Cos(turn)-side*Math.Sin(turn);
            }
            public double SpeedAt(double time)
            {
                double u=Math.Max(0,Math.Min(1,(time-TurnTime)/fall));
                return speed*(1+Acceleration*u*u);
            }
            public Point Sample(double time)
            {
                if(time<=TurnTime) return (Point)(travel*(speed*(time-TurnTime))-path[Steps]);
                if(time>=Impact) return new Point();
                double u=(time-TurnTime)/fall;
                double distance=Steps*(u+Acceleration*u*u*u/3)/(1+Acceleration/3);
                int i=Math.Min(Steps-1,(int)distance);
                double t=distance-i,v=1-t;
                // Match position and tangent at every subsegment, with no separate brake or rim join.
                Vector p=path[i]+(path[i+1]-path[i])*(t*t*(3-2*t))
                    +step*t*v*(headings[i]*v-headings[i+1]*t);
                return (Point)(p-path[Steps]);
            }
        }
    }
}
