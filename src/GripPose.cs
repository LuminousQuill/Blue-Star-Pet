using System;
using System.Windows;

namespace BlueStar
{
    // Positions here use the 300-unit drawing, with the origin at the pet window's centre.
    sealed class GripPose
    {
        static readonly double[] lanes={-10,12,-29,32,-47};
        static readonly double[] thresholds={0,0,0.22,0.53,0.78};
        LegRig[] legs;
        readonly LegRig[] chosen=new LegRig[lanes.Length];
        Vector anchor,normal,chosenHeading;
        double load,close,age;
        Vector edgeSpan;
        bool active,preview,replacingLegs;
        public bool Previewing {get {return active && preview;}}
        public double Effort {get; private set;}
        public Vector Recoil {get {return -normal*Effort;}}

        public GripPose(LegRig[] legs) {this.legs=legs;}
        public void RebindLegs(LegRig[] next)
        {
            legs=next; chosenHeading=new Vector(); replacingLegs=active;
            if(!active) Array.Clear(chosen,0,chosen.Length);
        }
        public void Set(Vector point,Vector outward,double weight,bool open,Vector span)
        {
            if(!active || (preview && !open) || Vector.Multiply(normal,outward)<0.7)
            { age=0; chosenHeading=new Vector(); }
            active=true; preview=open; load=Math.Max(0,Math.Min(1,weight));
            anchor=point; normal=outward; edgeSpan=span;
        }
        public void Clear()
        {
            if(!active) return;
            active=false; age=0; chosenHeading=new Vector();
        }
        void Select(Vector body,Vector across)
        {
            Vector heading=anchor-body;
            if(heading.LengthSquared<1) heading=normal;
            heading.Normalize();
            if(chosenHeading.LengthSquared>0 && (!preview || Vector.Multiply(chosenHeading,heading)>0.87)) return;
            chosenHeading=heading;
            foreach(var leg in legs) leg.GripSlot=-1;
            for(int i=0;i<lanes.Length;i++)
            {
                Vector target=FootPoint(lanes[i],across);
                LegRig best=null; double bestCost=double.MaxValue;
                foreach(var leg in legs)
                {
                    if(leg.GripSlot>=0) continue;
                    double cost=leg.GripCost(target,body);
                    if(cost<bestCost) {best=leg; bestCost=cost;}
                }
                // Carry planted feet across a change of skeleton. Their contact targets stay in desktop space.
                var old=chosen[i];
                if(replacingLegs && old!=null && old.GripSlot==i)
                {
                    best.GripBlend=old.GripBlend; best.GripTarget=old.GripTarget;
                }
                chosen[i]=best; best.GripSlot=i;
                best.GripTransfer=best.GripBlend>0?0.16:0;
            }
            replacingLegs=false;
        }
        Vector FootPoint(double lane,Vector across)
        {
            double along=Math.Max(edgeSpan.X,Math.Min(edgeSpan.Y,lane));
            // At a corner the next toe lands on the adjoining sill.
            return anchor+across*along+normal*(0.65*Math.Abs(lane-along)+2);
        }
        public void Step(Vector body,double dt)
        {
            if(!active && age>0.85) return;
            age+=dt;
            double goal=active && !preview?1:0;
            close+= (goal-close)*(1-Math.Exp(-13*dt));
            double force=goal*(0.22+0.78*load);
            Effort+=(force-Effort)*(1-Math.Exp(-7*dt));
            Vector across=new Vector(-normal.Y,normal.X);
            if(active) Select(body,across);
            foreach(var leg in legs)
            {
                int i=leg.GripSlot;
                bool engage=active && i>=0 && age>=0.095*i && (i<2 || (!preview &&
                    load>=thresholds[i]-(leg.GripBlend>0?0.04:0) && chosen[i-1].GripBlend>=0.60));
                // The support feet lift first. The original pair opens before the last toe lets go.
                bool wait=!active && i>=0 && age<(i<2?0.14-0.07*i:0.025*(4-i));
                if(!wait) leg.GripBlend=Math.Max(0,Math.Min(1,leg.GripBlend+(engage?2.7:-3.0)*dt));
                if(i<0 || leg.GripBlend==0) continue;
                double sign=Math.Sign(lanes[i]);
                double lane=lanes[i]+sign*(1-close)*(i<2?9:3);
                Vector target=FootPoint(lane,across)-normal*((1-close)*4);
                if(leg.GripTransfer>dt)
                {
                    leg.GripTarget+=(target-leg.GripTarget)*(dt/leg.GripTransfer);
                    leg.GripTransfer-=dt;
                }
                else { leg.GripTarget=target; leg.GripTransfer=0; }
                // An open ankle fans out; once closed, toes curl back towards the file star.
                double around=lane-Math.Max(edgeSpan.X,Math.Min(edgeSpan.Y,lane));
                double turn=Math.Min(1,Math.Abs(around)/9);
                leg.GripFoot=normal*(1-turn)+across*(sign*(0.45-0.80*close)*(1-turn)-Math.Sign(around)*turn);
            }
            if(!active && age>0.85) Effort=0;
        }
    }
}
