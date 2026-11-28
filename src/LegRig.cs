using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BlueStar
{
    // Each sprite has its own hip, knee and ankle. The three skinning transforms
    // share a rest pose and blend only in a narrow band around each joint.
    sealed class LegRig
    {
        sealed class Drawing
        {
            public readonly double Width, Height;
            public readonly Point Hip, Knee, Ankle;
            // Projected ankle-to-toes at full flex, in rest foot lengths with the shin pointing down.
            public readonly Vector FlexedFoot;
            public Drawing(int w,int h,int hx,int hy,int kx,int ky,int ax,int ay,double flexX,double flexY)
            {
                Width=w; Height=h; Hip=new Point(hx,hy);
                Knee=new Point(kx,ky); Ankle=new Point(ax,ay); FlexedFoot=new Vector(flexX,flexY);
            }
        }
        static readonly Drawing[] drawings={
            new Drawing(380,864,119,53,312,284,148,698,-0.82,0.22), // three-quarter, toes left
            new Drawing(392,653,85,47,332,227,118,534,0.98,-0.07), // profile, toes right
            new Drawing(188,881,69,36,125,303,64,693,0.14,0.54)    // toes towards the viewer
        };
        struct Vertex { public double X,Y,Shin,Foot,Toe,AlongThigh,AlongShin; }
        readonly Vertex[] vertices;
        readonly Drawing drawing;
        readonly double direction,length,side,rootX,rootY,phase,depth;
        public double PaintDepth;
        public Vector? TuckedToe;
        public bool SupportsHeartTip;
        readonly bool heartThigh;
        readonly int template;
        readonly bool centerRoot,folded;
        readonly double restKnee,restAnkle,idleSway;
        public readonly Vector RestToe;
        readonly Vector gripThigh,gripShin,gripSole;
        readonly double naturalBend,gripReach;
        Vector posedToe,posedBody;
        public Vector SelectionToe {get {return folded?posedToe-posedBody:RestToe;}}
        Vector Heading
        {
            get
            {
                if(!folded) return toeDirection;
                Vector ray=SelectionToe;
                return ray.LengthSquared>0.01?ray/ray.Length:toeDirection;
            }
        }
        double hipCorrection;
        bool followingGrip,hasPose;
        public int GripSlot=-1;
        public double GripBlend,GripTransfer;
        public Vector GripTarget,GripFoot;
        readonly Vector hipResponse,kneeResponse,ankleArm,ankleLever,toeDirection;
        readonly double longness,longest;
        readonly double mealOuter,mealLane;
        readonly Vector mealBrace,mealOpen;
        double mealStart=double.NegativeInfinity,mealDuration,mealStrength;
        Vector3D mealFrom;
        readonly double turnDepth;
        readonly double extendedKnee,extendedAnkle,extendedLength;
        const double BurstRadius=135; // In the pet's 300-unit drawing space.
        double burstHip,burstStretch;
        readonly double burstKnee,burstAnkle,burstFootLength;
        readonly Vector footAxis;
        readonly Vector extendedAxis;
        double pointWeight,pointHip;
        Vector pointRoot;
        public double PointScore {get; private set;}
        public double CurlDelay;
        public int Rank;
        double bendAt=-10,bendStart;
        const int Across=4, Down=17;
        public const int ProfileFoot=3;
        const int FootSeam=13;
        public const int VertexCount=(Across+1)*(Down+1);
        readonly double toeU;
        // Cross sections of the painted profile, from the inside contour to the outside.
        // Closely spaced sections follow the knee pit and rounded cap around the bend.
        static readonly Point[] crownInside={new Point(0,0),new Point(0,52),new Point(0,86),
            new Point(23,113),new Point(63,136),new Point(80,137),new Point(94,140),
            new Point(102,142),new Point(109,145),
            new Point(124,149),new Point(114,154),new Point(101,160)};
        static readonly Point[] crownOutside={new Point(78,0),new Point(81,3),new Point(95,25),
            new Point(122,49),new Point(156,80),new Point(175,94),new Point(188,105),
            new Point(190,111),new Point(191,116),
            new Point(192,128),new Point(192,140),new Point(192,153)};
        static readonly double[] crownShin={0,0,0,0,0.06,0.22,0.40,0.47,0.53,0.65,0.78,0.90};

        // Register the profile foot's shin width and slope to the three-quarter calf.
        // Both sides of the texture seam use these same rest-space coordinates and weights.
        static double ProfileRestU(double u,double v)
        {
            return 0.115+0.85*u+0.24*(v-FootSeam/(double)Down);
        }

        public Point3D ToeOnMesh(Point3D[] points,int start)
        {
            int row=start+(Down-1)*(Across+1);
            var a=points[row]+(points[row+Across]-points[row])*toeU;
            row+=Across+1;
            var b=points[row]+(points[row+Across]-points[row])*toeU;
            return a+(b-a)*(0.97*Down-(Down-1));
        }

        // At a deep bend the inside ends of neighboring cross sections can pass through
        // one another. Terminate the inside crease at their intersection while the
        // outside of the cap and the two bone directions keep their full silhouette.
        public void SettleKneeCrease(Point3D[] points,int start)
        {
            for(int row=3;row<=crownInside.Length;row++)
            {
                int i=start+row*(Across+1);
                double inset=Math.Max(CreaseInset(points,i,i-Across-1,1),CreaseInset(points,i,i+Across+1,-1));
                var inner=points[i];
                var across=points[i+Across]-inner;
                for(int col=0;col<Across;col++)
                {
                    double u=col/(double)Across;
                    if(row==crownInside.Length) u+=0.42*u*(1-u);
                    points[i+col]=inner+across*(inset+(1-inset)*u);
                }
            }
        }
        static double CreaseInset(Point3D[] points,int i,int adjacent,double sign)
        {
            var inner=points[adjacent];
            var axis=points[adjacent+Across]-inner;
            var a=points[i]-inner;
            double near=sign*(axis.X*a.Y-axis.Y*a.X);
            if(near>=0) return 0;
            var b=points[i+Across]-inner;
            double far=sign*(axis.X*b.Y-axis.Y*b.X);
            return far>0?Math.Min(1,-near/(far-near)+0.001):1;
        }

        public static LegRig Folded(int shape,int shade,Point root,Point knee,Vector calf,double ankle,
                                    int index,PointCollection uv,Int32Collection triangles,Int32Collection front,int frontRow,bool heartThigh=false)
        {
            Drawing d=drawings[shape==ProfileFoot?0:shape];
            Vector thigh=d.Knee-d.Hip,shin=d.Ankle-d.Knee,upper=knee-root;
            double bend=Vector.AngleBetween(upper,calf);
            double mirror=bend<0?-1:1;
            Vector upperSprite=new Vector(thigh.X*mirror,thigh.Y);
            double hip=Vector.AngleBetween(upperSprite,upper);
            double length=upper.Length/thigh.Length*(d.Height-d.Hip.Y);
            return new LegRig(shape,shade,hip,length,mirror,root.X,root.Y,index,uv,triangles,true,
                (Math.Abs(bend)-Vector.AngleBetween(thigh,shin))*Math.PI/180,ankle*Math.PI/180,front,frontRow,heartThigh);
        }

        public LegRig(int shape,int shade,double direction,double length,double side,
                      double rootX,double rootY,int index,PointCollection uv,Int32Collection triangles,
                      bool folded=false,double restKnee=0,double restAnkle=0,Int32Collection front=null,int frontRow=Down,bool heartThigh=false)
        {
            bool profile=shape==ProfileFoot;
            template=profile?0:shape; drawing=drawings[template]; this.heartThigh=heartThigh;
            this.folded=folded; this.restKnee=restKnee; this.restAnkle=restAnkle;
            this.direction=direction*Math.PI/180; this.length=length;
            this.side=side; this.rootX=rootX; this.rootY=rootY; phase=index*2.39996;
            PaintDepth=depth=index*0.0001;centerRoot=shade==4;
            turnDepth=(shade-2)*11;
            toeU=profile || shape==1?0.56:shape==0?0.13:0.32;
            var toe=new Point(drawing.Width*(profile?ProfileRestU(toeU,0.97):toeU),drawing.Height*0.97);
            double scale=length/(drawing.Height-drawing.Hip.Y);
            Vector thigh=drawing.Knee-drawing.Hip,shin=drawing.Ankle-drawing.Knee,foot=toe-drawing.Ankle;
            gripThigh=new Vector(side*thigh.X,thigh.Y); gripShin=new Vector(side*shin.X,shin.Y);
            gripSole=new Vector(side*foot.X,foot.Y);
            naturalBend=Vector.AngleBetween(gripThigh,gripShin)*Math.PI/180;
            footAxis=foot/foot.Length;
            burstKnee=-naturalBend*side*0.97;
            // A frontal foot pitches towards the viewer, with a visible sideways roll on the crown's spokes.
            Vector flex=profile?drawings[1].FlexedFoot:folded && shape==2?new Vector(0.54,0.06):drawing.FlexedFoot;
            burstAnkle=(Vector.AngleBetween(new Vector(0,1),flex)-Vector.AngleBetween(shin,foot))*Math.PI/180;
            burstFootLength=flex.Length;
            gripReach=(gripThigh.Length+(gripShin+gripSole).Length)*scale;
            extendedKnee=0.85*Vector.AngleBetween(shin,thigh)*Math.PI/180;
            extendedAnkle=0.75*Vector.AngleBetween(foot,shin)*Math.PI/180;
            extendedAxis=WorldVector(thigh+Rotate(shin,extendedKnee)+Rotate(foot,extendedKnee+extendedAnkle));
            extendedLength=extendedAxis.Length*scale;
            Vector footRest=folded?Rotate(foot,restKnee+restAnkle):foot;
            Vector lowerRest=folded?Rotate(shin,restKnee)+footRest:toe-drawing.Knee;
            Vector fullRest=folded?thigh+lowerRest:toe-drawing.Hip;
            RestToe=new Vector(rootX,rootY)+WorldVector(fullRest)*scale;
            if(folded) { posedBody=new Vector(0,-12); posedToe=RestToe+posedBody; }
            toeDirection=RestToe/RestToe.Length;
            mealOuter=folded?Smooth((RestToe.Length-65)/67):Smooth((RestToe.Length-90)/40);
            mealLane=0.5+0.5*(folded?toeDirection.Y:0.70*(toeDirection.X-toeDirection.Y));
            Vector fan=folded?new Vector(RestToe.X<0?-32:32,0)+toeDirection*13:
                toeDirection*20+Perpendicular(toeDirection)*(side*23);
            mealBrace=MealReach(-toeDirection*(21-12*mealOuter),scale,-0.20);
            mealOpen=MealReach(fan*(0.24+0.76*mealOuter),scale,0.18+0.32*mealOuter);
            idleSway=folded?0.32+0.68*Smooth((RestToe.Length-28)/28):1;
            Vector hipArm=Perpendicular(WorldVector(fullRest))*scale;
            Vector kneeArm=Perpendicular(WorldVector(lowerRest))*scale;
            ankleLever=WorldVector(footRest); ankleArm=Perpendicular(ankleLever)*scale; ankleLever.Normalize();
            // Damped two-joint inverse: share a desired toe displacement between hip and knee.
            // The damping also keeps the almost straight template away from a locked-knee singularity.
            double xx=hipArm.X*hipArm.X+kneeArm.X*kneeArm.X+36,xy=hipArm.X*hipArm.Y+kneeArm.X*kneeArm.Y;
            double yy=hipArm.Y*hipArm.Y+kneeArm.Y*kneeArm.Y+36,det=xx*yy-xy*xy;
            hipResponse=new Vector(yy*hipArm.X-xy*hipArm.Y,xx*hipArm.Y-xy*hipArm.X)/det;
            kneeResponse=new Vector(yy*kneeArm.X-xy*kneeArm.Y,xx*kneeArm.Y-xy*kneeArm.X)/det;
            longness=Smooth((length-84)/44); longest=Smooth((length-119)/9);
            vertices=new Vertex[VertexCount];
            int start=uv.Count;
            for (int row=0;row<=Down;row++)
            for (int col=0;col<=Across;col++)
            {
                double u=col/(double)Across, v=row/(double)Down;
                if(heartThigh && row<crownInside.Length)
                {
                    Point sample=crownInside[row]+(crownOutside[row]-crownInside[row])*u;
                    u=sample.X/192; v=sample.Y/384;
                }
                else if(heartThigh && row==crownInside.Length)
                {
                    u=0.26+0.74*(u+0.42*u*(1-u));
                    v=8.0/Down;
                }
                double restU=u,texU=u;
                int chart=template;
                if(profile && row>=FootSeam-1)
                {
                    v=Math.Max(row,FootSeam)/(double)Down;
                    restU=ProfileRestU(u,v);
                    if(row<FootSeam) texU=restU;
                    else chart=1;
                }
                double y=v*drawing.Height;
                int i=row*(Across+1)+col;
                vertices[i]=new Vertex {X=restU*drawing.Width,Y=y,
                    AlongThigh=Math.Max(0,Math.Min(1,(y-drawing.Hip.Y)/(drawing.Knee.Y-drawing.Hip.Y))),
                    AlongShin=Math.Max(0,Math.Min(1,(y-drawing.Knee.Y)/(drawing.Ankle.Y-drawing.Knee.Y))),
                    Shin=JointWeight(y,drawing.Knee.Y,0.075*drawing.Height),
                    Foot=JointWeight(y,drawing.Ankle.Y,0.045*drawing.Height),Toe=Smooth((v-0.83)/0.14)};
                if(heartThigh && row<crownInside.Length)
                {
                    vertices[i].Shin=crownShin[row];
                    Vector arm=new Point(vertices[i].X,y)-drawing.Hip;
                    vertices[i].AlongThigh=(1-crownShin[row])*Vector.Multiply(arm,thigh)/thigh.LengthSquared+crownShin[row];
                }
                uv.Add(new Point((chart+texU)/3,(shade+v)/5));
                if(row==Down || col==Across || (profile && row==FootSeam-1)) continue;
                int a=start+i,b=a+Across+1;
                var faces=row>=frontRow?front:triangles;
                // Mirroring reverses triangle winding.
                if(side>0) { faces.Add(a);faces.Add(b);faces.Add(a+1); faces.Add(a+1);faces.Add(b);faces.Add(b+1); }
                else { faces.Add(a);faces.Add(a+1);faces.Add(b); faces.Add(a+1);faces.Add(b+1);faces.Add(b); }
            }
        }
        public void SetBurstSpoke(double angle)
        {
            var radial=new Vector(Math.Sin(angle),Math.Cos(angle));
            var shaft=gripThigh+Rotate(gripShin,burstKnee*side);
            var sole=Rotate(gripSole,(burstKnee+burstAnkle)*side)*burstFootLength;
            var axis=shaft/shaft.Length;
            // Solve for the hip-to-ankle length with the flexed foot held at its painted size.
            double radius=BurstRadius*(drawing.Height-drawing.Hip.Y)/length;
            double across=Vector.CrossProduct(axis,sole);
            double shaftLength=Math.Sqrt(radius*radius-across*across)-Vector.Multiply(axis,sole);
            burstStretch=shaftLength/shaft.Length-1;
            burstHip=Vector.AngleBetween(axis*shaftLength+sole*0.5,radial)*Math.PI/180;
        }
        static double JointWeight(double y,double pivot,double width)
        {
            return Smooth((y-pivot+width)/(2*width));
        }
        Vector WorldVector(Vector local)
        {
            double x=side*local.X,c=Math.Cos(direction),s=Math.Sin(direction);
            return new Vector(c*x-s*local.Y,s*x+c*local.Y);
        }
        static Vector Perpendicular(Vector v) {return new Vector(-v.Y,v.X);}
        static Vector Rotate(Vector v,double radians) {return v*Math.Cos(radians)+Perpendicular(v)*Math.Sin(radians);}
        public void ResetPointing() { Rank=int.MaxValue; pointWeight=pointHip=0; pointRoot=new Vector(); }
        public double CompareFanLane(LegRig other,Vector heading)
        {
            return folded?Vector.CrossProduct(heading,new Vector(rootX-other.rootX,rootY-other.rootY)):
                Vector.CrossProduct(other.RestToe,RestToe);
        }
        public void ScorePointing(Vector heading,int count)
        {
            // Prefer a readable silhouette inside the window and keep the current foot through small cursor jitters.
            double margin=Math.Min(144-Math.Abs(rootX+heading.X*extendedLength),144-Math.Abs(rootY-12+heading.Y*extendedLength));
            PointScore=Vector.Multiply(Heading,heading)+0.006*turnDepth+0.009*Math.Min(140,extendedLength)+
                Math.Min(0,margin)*0.04+(Rank<count?0.12:0);
            if(folded)
            {
                PointScore-=0.011*Math.Abs(Vector.AngleBetween(extendedAxis,heading));
                PointScore-=0.022*Math.Abs(Vector.CrossProduct(heading,new Vector(rootX,rootY)));
            }
        }
        public void FollowPoint(Vector target,int rank,int count,int lane,double age,double duration,double dt)
        {
            Rank=rank;
            double goal=0;
            if(rank<count)
            {
                Vector root=new Vector(rootX,rootY);
                if(count==3)
                {
                    Vector across=Perpendicular(target)/target.Length;
                    Vector spread=across*(lane*8.5-Vector.Multiply(across,root));
                    pointRoot+=(spread-pointRoot)*(1-Math.Exp(-8*dt));
                }
                Vector ray=target-root-pointRoot;
                double angle=Limit(Vector.AngleBetween(extendedAxis,ray)*Math.PI/180,folded?Math.PI:1.60);
                double delta=angle-pointHip;
                if(folded && pointWeight>0.01) delta=Math.IEEERemainder(delta,2*Math.PI);
                pointHip+=delta*(pointWeight<0.01?1:1-Math.Exp(-10*dt));
                double delay=0.06+0.14*rank;
                goal=IdleMotion.Beat(age-delay,0.56,duration-1.16-delay-rank*0.10,duration-0.18-delay-rank*0.10);
            }
            pointWeight+=(goal-pointWeight)*(1-Math.Exp(-(goal>pointWeight?9:7)*dt));
        }
        static double Limit(double v,double max) {return Math.Max(-max,Math.Min(max,v));}
        static double Smooth(double t)
        {
            t=Math.Max(0,Math.Min(1,t));
            return t*t*(3-2*t);
        }
        double BendAmount(double time)
        {
            double age=time-bendAt;
            if(age<0.20) return bendStart+(1-bendStart)*Smooth(age/0.20);
            if(age<0.50) return 1;
            return 1-Smooth((age-0.50)/0.85);
        }
        public void Bend(double time) { bendStart=BendAmount(time); bendAt=time; }
        public double GripCost(Vector target,Vector body)
        {
            double reach=(target-body-new Vector(rootX,rootY)).Length;
            Vector toe=hasPose?posedToe:body+RestToe;
            return (toe-target).Length+4*Math.Max(0,reach+13-gripReach)
                +Math.Max(0,0.43*gripReach-reach)-0.12*turnDepth;
        }
        void SolveReach(Vector ray,Vector distal,double scale,out double hip,out double knee,
                        double maxKnee=double.PositiveInfinity,double minKnee=double.NegativeInfinity)
        {
            double a=gripThigh.Length,b=distal.Length,d=ray.Length/scale;
            double bend=Math.Acos(Limit((d*d-a*a-b*b)/(2*a*b),1));
            // Leave a slight bend at full reach and room behind the knee on a short reach.
            bend=Math.Sign(naturalBend)*Math.Max(0.12,Math.Min(2.40,bend));
            double rest=Vector.AngleBetween(gripThigh,distal)*Math.PI/180;
            knee=Math.Max(minKnee,Math.Min((bend-rest)*side,maxKnee));
            hip=Vector.AngleBetween(gripThigh+Rotate(distal,knee*side),ray)*Math.PI/180;
        }
        Vector MealReach(Vector offset,double scale,double heel)
        {
            Vector ray=RestToe-new Vector(rootX,rootY)+offset;
            Vector distal=gripShin+Rotate(gripSole,(restAnkle+heel)*side);
            double bend=Math.Abs(naturalBend),hip,knee;
            double lower=folded?Math.Max(restKnee-0.86,0.08-bend):(template==2?-0.20:-0.42);
            double upper=folded?Math.Min(restKnee+0.86,2.60-bend):0.86;
            SolveReach(ray,distal,scale,out hip,out knee,upper,lower);
            return new Vector(Limit(Math.IEEERemainder(hip-direction,2*Math.PI),0.56),knee-restKnee);
        }
        public void StartMeal(double now,double weight,double duration)
        {
            mealFrom=MealJoints(now); mealStart=now;
            mealDuration=duration; mealStrength=0.70*(0.63+0.37*weight);
        }
        Vector3D MealJoints(double now)
        {
            double age=now-mealStart;
            if(age>=mealDuration) return new Vector3D();
            double u=age/mealDuration;
            double delay=0.17*mealOuter+0.055*mealLane;
            double brace=IdleMotion.Beat(u-delay,0.14,0.21,0.45);
            double swing=u-0.17-0.18*mealOuter-0.04*mealLane;
            double open=IdleMotion.Beat(swing,0.18,0.24,0.47);
            double heel=IdleMotion.Beat(swing-0.075,0.18,0.25,0.52);
            Vector joints=mealBrace*brace+mealOpen*open;
            var pose=new Vector3D(joints.X,joints.Y,-0.20*brace+(0.18+0.32*mealOuter)*heel)*mealStrength;
            // A new batch takes over the current joint angles without stacking kicks.
            return pose+(mealFrom-pose)*(1-Smooth(age/0.075));
        }
        Vector GripJoints(Vector root,ref double scale,ref double hip,ref double knee,ref double ankle)
        {
            double blend=Smooth(GripBlend);
            Vector ray=GripTarget-root;
            if(ray.LengthSquared<0.01) return new Vector();
            double reach=ray.Length,thigh=gripThigh.Length,sole=gripSole.Length;
            Vector distal=gripShin+gripSole;
            double span=thigh+distal.Length;
            Vector slide=ray/reach*Math.Max(0,Math.Min(13,reach-span*scale*1.10));
            ray-=slide; reach=ray.Length;
            // First find the resting sole direction for this reach.
            double gripScale=Math.Max(scale,Math.Min(scale*1.20,reach/(0.995*span)));
            double gripHip,gripKnee;
            SolveReach(ray,distal,gripScale,out gripHip,out gripKnee);
            Vector restingFoot=Rotate(gripSole/sole,gripHip+side*gripKnee);
            Vector face=GripFoot; if(face.LengthSquared>0.001) face.Normalize();
            // Let the ankle roll a little towards the ledge. Rotate the hip and knee
            // around that ankle pose so the painted foot keeps its natural profile.
            double gripAnkle=0.55*Vector.CrossProduct(restingFoot,face)*side;
            distal=gripShin+Rotate(gripSole,gripAnkle*side);
            gripScale=Math.Max(scale,Math.Min(scale*1.20,reach/(0.995*(thigh+distal.Length))));
            SolveReach(ray,distal,gripScale,out gripHip,out gripKnee);
            double from=followingGrip && blend<1?hipCorrection:0;
            hipCorrection=from+Math.IEEERemainder(gripHip-hip-from,2*Math.PI);
            followingGrip=true;
            hip+=hipCorrection*blend;
            knee+=(gripKnee-knee)*blend;
            ankle+=(gripAnkle-ankle)*blend;
            scale+=(gripScale-scale)*blend;
            return slide*blend;
        }
        void CatchSag(IdleMotion idle,Vector rootShift,double scale,ref double hip,ref double knee,ref double ankle)
        {
            double facing=Vector.Multiply(Heading,new Vector(Math.Sign(idle.Direction.X)*0.27,0.96));
            double support=Smooth((facing-0.25)/0.67);
            double lag=0.11+0.10*longness+0.045*(0.5+0.5*Math.Sin(phase));
            Vector ankleShift=idle.SagAt(idle.Age-lag)*(0.58-0.51*support);
            Vector delta=Rotate(ankleShift-rootShift,-hip)/scale;
            delta.X*=side;
            if(delta.LengthSquared<0.000001) return;

            // Solve for an ankle that lags in desktop space, keeping both bone lengths and the natural bend.
            Vector thigh=drawing.Knee-drawing.Hip,shin=drawing.Ankle-drawing.Knee;
            Vector target=thigh+Rotate(shin,knee)+delta;
            double rest=Vector.AngleBetween(thigh,shin)*Math.PI/180;
            double cosine=(target.LengthSquared-thigh.LengthSquared-shin.LengthSquared)/(2*thigh.Length*shin.Length);
            double bend=Math.Acos(Limit(cosine,1));
            bend=Math.Max(Math.Min(0.23,Math.Abs(rest+knee)),Math.Min(bend,Math.Max(2.12,Math.Abs(rest+knee))));
            double nextKnee=Math.Sign(rest+knee)*bend-rest;
            double swing=Vector.AngleBetween(thigh+Rotate(shin,nextKnee),target)*Math.PI/180;
            hip+=side*swing;
            ankle-=0.86*(swing+nextKnee-knee);
            knee=nextKnee;
        }
        public void Pose(Point3D[] output,int start,double time,double poseTime,double fold,double tilt,
                         double lift,double breath,double cx,double cy,double aim,IdleMotion idle,DragWake wake,Vector glide,double presence,bool departing,RoamStroke swim,Vector sagRoot,double burstAge=double.PositiveInfinity,Vector heartOffset=default(Vector),double footTuck=0,double dragLean=0,Vector rightLobe=default(Vector))
        {
            double hipSweep=0,ankleLag=0;
            if(presence<1)
            {
                // Short, rear legs lead the unfurl. Long ankles finish the swing after the knees.
                double delay=0.12*longness+0.06*(turnDepth+22)/44;
                if(departing)
                {
                    double t=1-presence;
                    double brace=IdleMotion.Beat(t,0.15,0.29,0.46);
                    double gather=Smooth((t-0.39+delay)/0.25);
                    fold+=1.00*gather-0.34*brace;
                    breath*=1-0.76*Smooth((t-0.49+0.65*delay)/0.25);
                    hipSweep=side*(0.17+0.11*longness)*(gather-0.76*brace);
                    ankleLag=0.53*(gather-Smooth((t-0.47+delay)/0.31));
                }
                else
                {
                    double t=presence-delay;
                    double open=Smooth((t-0.32)/0.23);
                    double swing=IdleMotion.Beat(t-0.45,0.14,0.18,0.36);
                    fold+=0.98*(1-open)-0.34*swing;
                    breath*=0.27+0.73*Smooth((t-0.28)/0.22);
                    hipSweep=side*(0.20+0.12*longness)*(1-open-0.59*swing);
                    ankleLag=-0.54*(open-Smooth((t-0.37)/0.37))+0.22*swing;
                }
            }
            double tap=time-bendAt<1.35?BendAmount(time):0;
            fold+=tap;
            double still=idleSway*(1-idle.Still*(Rank<7?0.90:0.37));
            if(idle.Act==IdleAct.Meteor && Rank<2)
                fold+=0.25*idle.Weight*IdleMotion.Beat(idle.Age-0.68-Rank*0.12,0.42,0.95,1.58);
            if(idle.Act==IdleAct.Listen && Rank<5)
                fold-=0.36*idle.Weight*(1-IdleMotion.Ease(idle.Turn))*IdleMotion.Beat(idle.Age-0.64-Rank*0.05,0.60,2.10,2.94);
            if(idle.Act==IdleAct.Probe && Rank<2)
            {
                double t=idle.Age;
                if(Rank==0) fold+=idle.Weight*(0.64*IdleMotion.Beat(t,0.31,0.37,0.88)
                    +0.43*IdleMotion.Beat(t-1.40,0.24,0.32,1.10));
                else fold+=0.24*idle.Weight*IdleMotion.Beat(t-0.48,0.38,0.94,1.75);
            }
            if(idle.Act==IdleAct.Curl)
            {
                double t=idle.Age-CurlDelay;
                fold+=idle.Weight*(0.56*IdleMotion.Beat(t,0.33,0.44,1.04)
                    -0.11*IdleMotion.Beat(t-0.86,0.22,0.26,0.54));
            }
            Vector paddle=new Vector();
            if(swim.Strength>0)
            {
                Vector heading=swim.Heading;
                Vector toeHeading=toeDirection;
                double facing=Vector.Multiply(toeHeading,heading),flank=Vector.CrossProduct(heading,toeHeading);
                double front=Smooth((facing-0.02)/0.82),rear=Smooth((-facing-0.02)/0.82),wing=flank*flank;
                double up=Math.Max(0,-heading.Y),down=Math.Max(0,heading.Y);
                double t=swim.Phase-0.055*longness-0.025*(1+flank);
                double gather=IdleMotion.Beat(t,0.12,0.20,0.40);
                double push=IdleMotion.Beat(t-0.15,0.20,0.37,0.65);
                double brake=IdleMotion.Beat(t-0.61,0.18,0.28,0.53);
                // The leading knees tuck. The trailing legs scull against the path; lateral feet fan on a dive's brake.
                fold+=swim.Strength*(front*(0.27+0.54*push)+rear*(0.62*gather-0.28*push)+wing*(0.20*up*gather-0.18*down*brake));
                double thrust=(7+14*rear+7*up*wing)*push*(0.55+0.45*longness);
                var across=new Vector(-heading.Y,heading.X);
                paddle=swim.Strength*(heading*(10*brake*longness-thrust)+across*(flank*wing*(9*up*push+15*down*brake)));
                ankleLag+=side*Vector.CrossProduct(ankleLever,heading)*swim.Strength*(0.28*push-0.23*brake);
            }
            double sway=still*Math.Sin(poseTime*(1.05+0.08*template)+phase);
            double hip=direction+tilt+0.041*sway+side*(0.19*fold+lift*0.032)+hipSweep;
            double knee=(template==2?0.72:0.9)*fold+still*0.085*Math.Sin(poseTime*1.37+phase)-side*tilt*0.5;
            double ankle=-0.43*fold+still*0.075*Math.Sin(poseTime*1.65+phase+0.8)+ankleLag;
            if(time<mealStart+mealDuration && GripBlend<1)
            {
                var meal=MealJoints(time)*(1-Smooth(GripBlend));
                hip+=meal.X; knee+=meal.Y; ankle+=meal.Z;
            }
            Vector drag=wake.Load(longness,longest);
            double footAngle=0.40*Vector.CrossProduct(ankleLever,drag);
            Vector load=drag*21-ankleArm*footAngle+paddle;
            if(SupportsHeartTip)
            {
                // The thigh and supporting shin bank with the heart. Only the foot trails the drag.
                hip-=side*lift*0.032;
                knee+=side*dragLean*0.5;
                // This knee is already deeply folded. Let the supporting foot tuck on a direct tap.
                ankle-=0.38*tap;
                load=paddle;
            }
            if(idle.Act==IdleAct.Glide)
                load+=(idle.GlideAt(idle.Age-0.13-0.33*longness)-glide)*(1+0.70*longness);
            if(idle.Act==IdleAct.Probe && Rank==0)
            {
                double reach=idle.Weight*IdleMotion.Beat(idle.Age-0.34,0.64,1.08,1.54);
                load+=new Vector(idle.Direction.X*14,16)*reach;
                ankle-=0.24*reach;
            }
            if(idle.Act==IdleAct.Dip)
                load.Y+=idle.DepthAt(idle.Age-0.17-0.24*longness)-idle.DepthAt(idle.Age);
            if(idle.Act==IdleAct.StarSlip && Rank==0)
                ankle+=side*0.24*idle.Weight*IdleMotion.Beat(idle.Age-2.26,0.13,0.17,0.51);
            if(idle.Act==IdleAct.Bank)
            {
                // Pale foreground legs sweep across the darker layer; the feet follow the hip.
                double body=idle.BankAt(idle.Age),foot=idle.BankAt(idle.Age-0.12-0.23*longness);
                hip+=0.16*body;
                load.X+=turnDepth*foot+(foot-body)*9;
                ankle+=side*0.26*(foot-body);
            }
            hip+=Limit(Vector.Multiply(hipResponse,load),0.27);
            knee+=side*Limit(Vector.Multiply(kneeResponse,load),0.44);
            ankle+=side*footAngle;
            Vector toes=SupportsHeartTip?drag*3:new Vector();
            if(idle.Act==IdleAct.Wind && Rank<5)
            {
                double t=idle.Age-Rank*0.105;
                double force=1.5*idle.Weight*(1-0.115*Rank);
                double tail=IdleMotion.Beat(t,0.29,1.47,2.10+0.28*longness)
                    -0.23*longness*IdleMotion.Beat(t-1.89,0.30,0.30,0.75);
                toes=idle.Direction*(4.2*force*tail);
                footAngle=0.22*Vector.CrossProduct(ankleLever,idle.Direction);
                load=idle.Direction*11-ankleArm*footAngle;
                ankle+=side*footAngle*force*tail*Smooth(t/0.25);
                knee+=side*Limit(Vector.Multiply(kneeResponse,load),0.44)*force*IdleMotion.Beat(t-0.15,0.40,1.15,2.10);
                hip+=Limit(Vector.Multiply(hipResponse,load),0.24)*force*IdleMotion.Beat(t-0.30,0.46,1.02,1.81);
            }
            if(idle.Act==IdleAct.Listen && Rank==2)
                ankle+=0.085*idle.Weight*IdleMotion.Beat(idle.Age-1.70,0.27,0.27,0.65);
            // The nearly straight drawing has less room to extend before the knee locks.
            if(folded)
            {
                // Add motion around this knee's actual rest bend and keep it on its anatomical side.
                double painted=Math.Abs(naturalBend);
                knee=Limit(knee,0.94)+restKnee;
                knee=Math.Max(0.08-painted,Math.Min(2.60-painted,knee));
                ankle+=restAnkle;
            }
            else knee=Math.Max(template==2?-0.20:-0.42,Math.Min(0.92,knee));
            double scale=length*breath/(drawing.Height-drawing.Hip.Y);
            double footLength=1;
            Vector heart=new Vector(cx,cy)+heartOffset;
            double dx=rootX+(centerRoot?0:(depth*3500-3)*aim),dy=rootY;
            if(SupportsHeartTip)
            {
                var root=Rotate(new Vector(rootX,rootY),dragLean)+heartOffset;
                if(idle.Act==IdleAct.CoreSag) root-=sagRoot;
                dx=root.X; dy=root.Y;
            }
            if(TuckedToe.HasValue && footTuck>0)
            {
                // Fold towards this leg's place beside the heart, retaining both bone lengths.
                double targetHip,targetKnee,targetAnkle=restAnkle-0.74;
                var distal=gripShin+Rotate(gripSole,targetAnkle*side);
                SolveReach(heartOffset+TuckedToe.Value-new Vector(dx,dy),distal,scale,out targetHip,out targetKnee,2.60-Math.Abs(naturalBend));
                hip+=Math.IEEERemainder(targetHip-hip,2*Math.PI)*footTuck;
                knee+=(targetKnee-knee)*footTuck;
                ankle+=(targetAnkle-ankle)*footTuck;
            }
            if(idle.Pointing)
            {
                double reach=pointWeight*idle.Weight;
                hip+=(direction+pointHip-hip)*Smooth(reach/0.86);
                knee+=(extendedKnee-knee)*Smooth((reach-0.09)/0.91);
                ankle+=(extendedAnkle-ankle)*Smooth((reach-0.21)/0.79);
            }
            Vector thighExtension=new Vector(),shinExtension=new Vector();
            double spoke=0;
            if(burstAge>=0 && burstAge<1.4)
            {
                double t=burstAge-0.045*longness;
                double thrust=IdleMotion.Beat(t,0.15,0.54,1.20+0.12*longness);
                hip+=Math.IEEERemainder(burstHip-hip,2*Math.PI)*thrust;
                knee+=(burstKnee-knee)*thrust;
                double flex=IdleMotion.Beat(t-0.045,0.13,0.67,1.33);
                ankle+=(burstAnkle-ankle)*flex;
                double free=1-Smooth(GripBlend);
                footLength+=(burstFootLength-1)*flex*free;
                double spread=thrust*free;
                spoke=spread;
                dx+=(heartOffset.X-dx)*spread; dy+=(heartOffset.Y-dy)*spread;
                thighExtension=(drawing.Knee-drawing.Hip)*(burstStretch*spread);
                shinExtension=Rotate(drawing.Ankle-drawing.Knee,knee)*(burstStretch*spread);
                toes*=1-thrust;
            }
            if(idle.Act==IdleAct.CoreSag)
            {
                CatchSag(idle,sagRoot,scale,ref hip,ref knee,ref ankle);
                cx+=sagRoot.X; cy+=sagRoot.Y;
            }
            if(idle.Act==IdleAct.PointFan) { dx+=pointRoot.X*pointWeight*idle.Weight; dy+=pointRoot.Y*pointWeight*idle.Weight; }
            if(GripBlend>0)
            {
                Vector slide=GripJoints(new Vector(cx+dx,cy+dy),ref scale,ref hip,ref knee,ref ankle);
                dx+=slide.X; dy+=slide.Y;
                toes*=1-Smooth(GripBlend);
            }
            else followingGrip=false;
            double ck=Math.Cos(knee),sk=Math.Sin(knee),ca=Math.Cos(ankle),sa=Math.Sin(ankle);
            double footXX=ca-1,footXY=-sa,footYX=sa,footYY=ca-1;
            if(footLength<1)
            {
                // Foreshorten along the sole around the ankle. Keep the width across the toes.
                var shorten=new Vector(ca*footAxis.X-sa*footAxis.Y,sa*footAxis.X+ca*footAxis.Y)*(footLength-1);
                footXX+=shorten.X*footAxis.X; footXY+=shorten.X*footAxis.Y;
                footYX+=shorten.Y*footAxis.X; footYY+=shorten.Y*footAxis.Y;
            }
            double ch=Math.Cos(hip)*scale,sh=Math.Sin(hip)*scale;
            for(int i=0;i<vertices.Length;i++)
            {
                Vertex v=vertices[i];
                double x=v.X-drawing.Hip.X,y=v.Y-drawing.Hip.Y;
                double kx=v.X-drawing.Knee.X,ky=v.Y-drawing.Knee.Y;
                x+=v.Shin*((ck-1)*kx-sk*ky);
                y+=v.Shin*(sk*kx+(ck-1)*ky);
                double ax=v.X-drawing.Ankle.X,ay=v.Y-drawing.Ankle.Y;
                double fx=footXX*ax+footXY*ay,fy=footYX*ax+footYY*ay;
                x+=v.Foot*(ck*fx-sk*fy);
                y+=v.Foot*(sk*fx+ck*fy);
                x+=v.AlongThigh*thighExtension.X+v.AlongShin*shinExtension.X;
                y+=v.AlongThigh*thighExtension.Y+v.AlongShin*shinExtension.Y;
                x*=side;
                output[start+i]=new Point3D((cx+dx+ch*x-sh*y+toes.X*v.Toe)/300,
                    -(cy+dy+sh*x+ch*y+toes.Y*v.Toe)/300,PaintDepth);
            }
            if(heartThigh)
            {
                // Rebind the painted thigh between the heart and its moving knee. Rotate its
                // cross sections with the bone so the skin retains its width on the inside bend.
                Vector thigh=drawing.Knee-drawing.Hip+thighExtension;
                Vector shaft=new Vector(ch*side*thigh.X-sh*thigh.Y,sh*side*thigh.X+ch*thigh.Y);
                Vector rootDelta=heart+rightLobe-new Vector(cx+dx,cy+dy);
                Vector pivot=new Vector(cx+dx,cy+dy)+shaft;
                Vector kneeShift=new Vector(),ankleShift=new Vector(),anklePivot=new Vector();
                double soleTurn=0;
                if(spoke>0)
                {
                    // Hold the equal-radius toe fixed. Swing its hooked sole about that
                    // contact and apportion the remaining displacement along the two bones.
                    Vector calf=Rotate(drawing.Ankle-drawing.Knee,knee)+shinExtension;
                    anklePivot=pivot+new Vector(ch*side*calf.X-sh*calf.Y,sh*side*calf.X+ch*calf.Y);
                    var tip=ToeOnMesh(output,start);
                    Vector contact=new Vector(tip.X*300,-tip.Y*300);
                    Vector ray=contact-new Vector(cx+dx,cy+dy);
                    soleTurn=Vector.AngleBetween(ray,ray-rootDelta)*Math.PI/180*spoke;
                    ankleShift=contact+Rotate(anklePivot-contact,soleTurn)-anklePivot;
                    double lower=calf.Length/(thigh.Length+calf.Length);
                    kneeShift=rootDelta*(spoke*lower)+ankleShift*(1-lower);
                }
                Vector bound=shaft+kneeShift-rootDelta;
                if(bound.LengthSquared>0.000001)
                {
                    Vector axis=shaft/shaft.Length;
                    double turn=Vector.AngleBetween(shaft,bound)*Math.PI/180;
                    double stretch=bound.Length/shaft.Length-1;
                    // Keep the painted insertion under the lobe even as the heart contracts.
                    // This ellipse encloses the slanted end face in the source bone's axes.
                    double capRadius=Math.Sqrt(Math.Pow(65*bound.Length/gripThigh.Length,2)+Math.Pow(63*scale,2));
                    double capScale=Math.Min(1,rightLobe.Length*0.245/capRadius);
                    for(int i=0;i<vertices.Length;i++)
                    {
                        Vertex v=vertices[i];
                        double w=1-v.Shin;
                        if(w==0 && spoke==0) break;
                        var p=output[start+i];
                        Vector offset=new Vector(p.X*300,-p.Y*300)-pivot;
                        Vector placed=pivot+kneeShift*(1-v.AlongShin)+ankleShift*v.AlongShin
                            +Rotate(offset+axis*(Vector.Multiply(axis,offset)*stretch*w),turn*w);
                        if(i<4*(Across+1))
                        {
                            double taper=1-Smooth((i/(Across+1)-1)/2.5);
                            placed+=(heart+rightLobe-placed)*((1-capScale)*taper);
                        }
                        if(spoke>0 && v.Foot>0)
                        {
                            Vector sole=offset+pivot-anklePivot;
                            placed+=(Rotate(sole,soleTurn)-sole)*v.Foot;
                        }
                        p.X=placed.X/300; p.Y=-placed.Y/300; output[start+i]=p;
                    }
                }
            }
            hasPose=true; posedBody=new Vector(cx,cy);
            var toe=ToeOnMesh(output,start); posedToe=new Vector(toe.X*300,-toe.Y*300);
        }
    }
}
