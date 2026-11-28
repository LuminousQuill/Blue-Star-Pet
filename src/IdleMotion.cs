using System;
using System.Linq;
using System.Windows;

namespace BlueStar
{
    enum IdleAct { None, Wind, Meteor, Listen, Probe, Dip, Stardust, Curl, Bank, Glide, Constellation,
                   PointFoot, PointFan, CoreSag, RimCrossing, StarSlip }

    sealed class IdleMotion
    {
        public IdleAct Act {get; private set;}
        public double Age,Weight,Turn;
        public Vector Direction;
        public MeteorFlight Flight;
        public event Action MeteorStopped;
        public StarChart Chart {get; private set;}
        public int ChartAngle {get; private set;}
        readonly StarChart[] charts=(StarChart[])StarChart.Shapes.Clone();
        readonly Random chartRandom=new Random();
        int chartsLeft;
        bool yielding,heardMouse;
        double duration;
        LegRig[] pointingLegs;
        Vector lastPointer;
        public bool Pointing {get {return Act==IdleAct.PointFoot || Act==IdleAct.PointFan;}}

        public static double Ease(double u)
        {
            u=Math.Max(0,Math.Min(1,u)); return u*u*(3-2*u);
        }
        public static double Beat(double t,double rise,double hold,double end)
        {
            return Ease(t/rise)*(1-Ease((t-hold)/(end-hold)));
        }
        public void Begin(IdleAct act,Vector direction,LegRig[] legs,MeteorFlight flight)
        {
            Act=act; Direction=direction; Flight=flight; Age=Turn=0; Weight=1; yielding=heardMouse=false;
            switch(act)
            {
                case IdleAct.Wind: duration=2.85; break;
                case IdleAct.Listen: duration=3.75; break;
                case IdleAct.Meteor: duration=flight.Duration; break;
                case IdleAct.Probe: duration=2.70; break;
                case IdleAct.Dip: duration=3.85; break;
                case IdleAct.Stardust: duration=3.0; break;
                case IdleAct.Curl: duration=3.55; break;
                case IdleAct.Bank: duration=4.18; break;
                case IdleAct.Glide: duration=4.20; break;
                case IdleAct.Constellation: duration=3.30; break;
                case IdleAct.PointFoot: duration=6.40; break;
                case IdleAct.PointFan: duration=8.00; break;
                case IdleAct.CoreSag: duration=3.18; break;
                case IdleAct.RimCrossing: duration=2.62; break;
                case IdleAct.StarSlip: duration=2.78; break;
                default: duration=0; break;
            }
            if(act==IdleAct.Constellation)
            {
                ChartAngle=(ChartAngle+45*(Chart==null?chartRandom.Next(8):chartRandom.Next(2,7)))%360;
                if(chartsLeft==0) chartsLeft=charts.Length;
                // At the start of the next bag its first slot holds the last chart shown.
                int first=chartsLeft==charts.Length && Chart!=null?1:0;
                int pick=chartRandom.Next(first,chartsLeft);
                Chart=charts[pick]; charts[pick]=charts[--chartsLeft]; charts[chartsLeft]=Chart;
                double angle=ChartAngle*Math.PI/180;
                Direction=new Vector(Math.Sin(angle),-Math.Cos(angle));
                return;
            }
            RebindLegs(legs);
        }
        public void RebindLegs(LegRig[] legs,bool switching=false)
        {
            if(Act==IdleAct.None || Act==IdleAct.Constellation) return;
            if(Pointing)
            {
                pointingLegs=(LegRig[])legs.Clone();
                if(!switching) lastPointer=Direction*160;
                foreach(var leg in legs) leg.ResetPointing();
                if(switching) TrackPointer(lastPointer,1);
                return;
            }
            if(Act==IdleAct.Curl)
            {
                foreach(var leg in legs)
                {
                    double angle=Vector.AngleBetween(Direction,leg.SelectionToe)*Math.Sign(Direction.X);
                    leg.CurlDelay=(angle<0?angle+360:angle)*(2.12/360);
                }
                return;
            }
            Vector side=Act==IdleAct.Wind?-Direction:Direction;
            int rank=0;
            foreach(var leg in legs.OrderByDescending(l=>Vector.Multiply(l.SelectionToe,side)))
                leg.Rank=rank++;
        }
        public void Advance(double dt,bool busy,bool mouseNear)
        {
            if(Act==IdleAct.None) return;
            Age+=dt;
            yielding|=busy;
            if(yielding) Weight=Math.Max(0,Weight-dt/0.28);
            if(Act==IdleAct.Listen && mouseNear) heardMouse=true;
            if(heardMouse) Turn=Math.Min(1,Turn+dt/0.50);
            if(Age>=duration || Weight<=0) Stop();
        }
        public void Stop()
        {
            bool meteor=Act==IdleAct.Meteor;
            Act=IdleAct.None; Weight=0; Flight=null;
            if(meteor && MeteorStopped!=null) MeteorStopped();
        }
        public void TrackPointer(Vector target,double dt)
        {
            if(!Pointing || yielding) return;
            // Retain the last heading as the cursor crosses the hollow center.
            if(target.Length<32) target=lastPointer;
            else lastPointer=target;
            Vector heading=target/target.Length;
            if(Age==0) Direction=heading;
            double turn=Vector.AngleBetween(Direction,heading)*Math.PI/180*(1-Math.Exp(-8*dt));
            Direction=Direction*Math.Cos(turn)+new Vector(-Direction.Y,Direction.X)*Math.Sin(turn);
            int count=Act==IdleAct.PointFan?3:1;
            foreach(var leg in pointingLegs) leg.ScorePointing(heading,count);
            Array.Sort(pointingLegs,(a,b)=>b.PointScore.CompareTo(a.PointScore));
            for(int i=0;i<pointingLegs.Length;i++)
            {
                // Preserve the order of the three hips across the fan as all toes aim at the same point.
                int lane=0;
                if(count==3 && i<count)
                    for(int j=0;j<count;j++)
                        lane+=Math.Sign(pointingLegs[i].CompareFanLane(pointingLegs[j],heading));
                pointingLegs[i].FollowPoint(target,i,count,lane,Age,duration,dt);
            }
        }
        public double FanLean {get {return Act==IdleAct.PointFan?Weight*Beat(Age-0.50,0.75,duration-1.66,duration-0.72):0;}}
        public double FanGaze {get {return Act==IdleAct.PointFan?Weight*Beat(Age-0.22,0.40,duration-1.38,duration-0.58):0;}}
        public Vector SagAt(double t)
        {
            if(Act!=IdleAct.CoreSag) return new Vector();
            // Free fall and braking meet at the same velocity. The legs take the load before the slow lift.
            double fall=Math.Max(0,Math.Min(1,(t-0.35)/0.28));
            double brake=Math.Max(0,Math.Min(1,(t-0.63)/0.14));
            double drop=-0.08*Ease(t/0.22)+0.72*fall*fall+0.36*brake*(2-brake)
                -0.14*Ease((t-0.77)/0.37)-0.86*Ease((t-1.22)/1.55)
                -0.035*Beat(t-2.31,0.30,0.34,0.60);
            return new Vector(Math.Sign(Direction.X)*10,36)*(Weight*drop);
        }
        public double DepthAt(double t)
        {
            if(Act!=IdleAct.Dip) return 0;
            return Weight*(15*Beat(t,0.81,1.18,2.73)-2.5*Beat(t-2.34,0.43,0.53,1.05));
        }
        public double BankAt(double t)
        {
            if(Act!=IdleAct.Bank) return 0;
            return Math.Sign(Direction.X)*Weight*(-0.13*Beat(t,0.26,0.28,0.60)
                +Beat(t-0.28,0.99,1.43,2.88)-0.10*Beat(t-2.85,0.40,0.45,0.91));
        }
        public Vector GlideAt(double t)
        {
            if(Act!=IdleAct.Glide || t<=0 || t>=3.72) return new Vector();
            double phase=2*Math.PI*Ease(t/3.72);
            return new Vector(Math.Sign(Direction.X)*9*(1-Math.Cos(phase)),-7*Math.Sin(phase))*Weight;
        }
        public double Still {get {return Act==IdleAct.Listen?Weight*Beat(Age,0.48,2.85,3.75)*(1-0.72*Ease(Turn)):0;} }
        public double Look {get {return Act==IdleAct.Listen?Weight*Beat(Age-0.36,0.42,2.44,3.35):0;} }
        public double CoreAngle(double mouseLean)
        {
            if(Act==IdleAct.Wind) return Weight*Direction.X*0.055*Beat(Age-0.63,0.53,0.62,1.17);
            if(Act==IdleAct.Listen) return Weight*(Direction.X*0.17-mouseLean)*(1-Ease(Turn))*Beat(Age-0.61,0.58,2.18,3.06);
            return 0;
        }
        public double Flash {get {return Act==IdleAct.Meteor?Weight*Beat(Age-Flight.FlashTime,0.046,0.081,0.38):0;} }
    }

    // Actual window velocity supplies the drag. The stored release pose is also the first coast pose.
    sealed class DragWake
    {
        Vector shortLag,longLag,over,drive,startShort,startLong,startOver,releaseDrive;
        double age=2;
        public void Follow(Vector velocity,double dt)
        {
            double speed=velocity.Length;
            Vector flow=velocity/(210+speed);
            drive+=(flow-drive)*(1-Math.Exp(-22*dt));
            shortLag+=(-flow-shortLag)*(1-Math.Exp(-13*dt));
            longLag+=(-flow-longLag)*(1-Math.Exp(-7.5*dt));
            over*=Math.Exp(-9*dt);
        }
        public void Release()
        {
            startShort=shortLag; startLong=longLag; startOver=over;
            releaseDrive=drive; age=0;
        }
        public void Settle(double dt)
        {
            if(age>=1.65) return;
            age+=dt;
            shortLag=startShort*(1-IdleMotion.Ease(age/0.43));
            longLag=startLong*(1-IdleMotion.Ease(age/0.83));
            over=startOver*(1-IdleMotion.Ease(age/0.34))
                +releaseDrive*(0.77*IdleMotion.Beat(age-0.55,0.40,0.40,1.10));
            drive*=Math.Exp(-12*dt);
        }
        public Vector Load(double length,double longest)
        {
            return (shortLag+(longLag-shortLag)*length)*(0.15+0.85*length)+over*longest;
        }
    }
}
