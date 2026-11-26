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
            public Drawing(int w,int h,int hx,int hy,int kx,int ky,int ax,int ay)
            {
                Width=w; Height=h; Hip=new Point(hx,hy);
                Knee=new Point(kx,ky); Ankle=new Point(ax,ay);
            }
        }
        static readonly Drawing[] drawings={
            new Drawing(380,864,119,53,312,284,148,698),
            new Drawing(392,653,85,47,332,227,118,534),
            new Drawing(188,881,69,36,125,303,64,693)
        };
        struct Vertex { public double X,Y,Shin,Foot,Toe; }
        readonly Vertex[] vertices;
        readonly Drawing drawing;
        readonly double direction,length,side,rootX,rootY,phase,depth;
        readonly int template;
        readonly bool centerRoot;
        public readonly Vector RestToe;
        readonly Vector hipResponse,kneeResponse,ankleArm,ankleLever,toeDirection;
        readonly double longness,longest;
        readonly double turnDepth;
        public double CurlDelay;
        public int Rank;
        double bendAt=-10,bendStart;
        const int Across=4, Down=17;
        public const int VertexCount=(Across+1)*(Down+1);

        public LegRig(int shape,int shade,double direction,double length,double side,
                      double rootX,double rootY,int index,PointCollection uv,Int32Collection triangles)
        {
            template=shape; drawing=drawings[shape];
            this.direction=direction*Math.PI/180; this.length=length;
            this.side=side; this.rootX=rootX; this.rootY=rootY; phase=index*2.39996;
            depth=index*0.0001;centerRoot=shade==4;
            turnDepth=(shade-2)*11;
            var toe=new Point(drawing.Width*(shape==0?0.13:shape==1?0.56:0.32),drawing.Height*0.97);
            double scale=length/(drawing.Height-drawing.Hip.Y);
            RestToe=new Vector(rootX,rootY)+WorldVector(toe-drawing.Hip)*scale;
            toeDirection=RestToe/RestToe.Length;
            Vector hipArm=Perpendicular(WorldVector(toe-drawing.Hip))*scale;
            Vector kneeArm=Perpendicular(WorldVector(toe-drawing.Knee))*scale;
            ankleLever=WorldVector(toe-drawing.Ankle); ankleArm=Perpendicular(ankleLever)*scale; ankleLever.Normalize();
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
                double y=v*drawing.Height;
                int i=row*(Across+1)+col;
                vertices[i]=new Vertex {X=u*drawing.Width,Y=y,
                    Shin=JointWeight(y,drawing.Knee.Y,0.075*drawing.Height),
                    Foot=JointWeight(y,drawing.Ankle.Y,0.045*drawing.Height),Toe=Smooth((v-0.83)/0.14)};
                uv.Add(new Point((shape+u)/3,(shade+v)/5));
                if(row==Down || col==Across) continue;
                int a=start+i,b=a+Across+1;
                // Mirroring reverses triangle winding.
                if(side>0) { triangles.Add(a);triangles.Add(b);triangles.Add(a+1); triangles.Add(a+1);triangles.Add(b);triangles.Add(b+1); }
                else { triangles.Add(a);triangles.Add(a+1);triangles.Add(b); triangles.Add(a+1);triangles.Add(b+1);triangles.Add(b); }
            }
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
        public void Pose(Point3D[] output,int start,double time,double fold,double tilt,
                         double lift,double breath,double cx,double cy,double aim,IdleMotion idle,DragWake wake,Vector glide,double presence,bool departing,RoamStroke swim)
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
            if(time-bendAt<1.35) fold+=BendAmount(time);
            double still=1-idle.Still*(Rank<7?0.90:0.37);
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
                double facing=Vector.Multiply(toeDirection,heading),flank=Vector.CrossProduct(heading,toeDirection);
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
            double sway=still*Math.Sin(time*(1.05+0.08*template)+phase);
            double hip=direction+tilt+0.041*sway+side*(0.19*fold+lift*0.032)+hipSweep;
            double knee=(template==2?0.72:0.9)*fold+still*0.085*Math.Sin(time*1.37+phase)-side*tilt*0.5;
            double ankle=-0.43*fold+still*0.075*Math.Sin(time*1.65+phase+0.8)+ankleLag;
            Vector drag=wake.Load(longness,longest);
            double footAngle=0.40*Vector.CrossProduct(ankleLever,drag);
            Vector load=drag*21-ankleArm*footAngle+paddle;
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
            Vector toes=new Vector();
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
            knee=Math.Max(template==2?-0.20:-0.42,Math.Min(0.92,knee));
            double ck=Math.Cos(knee),sk=Math.Sin(knee),ca=Math.Cos(ankle),sa=Math.Sin(ankle);
            double scale=length*breath/(drawing.Height-drawing.Hip.Y);
            double ch=Math.Cos(hip)*scale,sh=Math.Sin(hip)*scale;
            double dx=rootX+(centerRoot?0:(depth*3500-3)*aim),dy=rootY;
            for(int i=0;i<vertices.Length;i++)
            {
                Vertex v=vertices[i];
                double x=v.X-drawing.Hip.X,y=v.Y-drawing.Hip.Y;
                double kx=v.X-drawing.Knee.X,ky=v.Y-drawing.Knee.Y;
                x+=v.Shin*((ck-1)*kx-sk*ky);
                y+=v.Shin*(sk*kx+(ck-1)*ky);
                double ax=v.X-drawing.Ankle.X,ay=v.Y-drawing.Ankle.Y;
                double fx=(ca-1)*ax-sa*ay,fy=sa*ax+(ca-1)*ay;
                x+=v.Foot*(ck*fx-sk*fy);
                y+=v.Foot*(sk*fx+ck*fy);
                x*=side;
                output[start+i]=new Point3D((cx+dx+ch*x-sh*y+toes.X*v.Toe)/300,
                    -(cy+dy+sh*x+ch*y+toes.Y*v.Toe)/300,depth);
            }
        }
    }
}
