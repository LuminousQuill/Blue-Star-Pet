using System;
using System.Windows;
using System.Windows.Media;

namespace BlueStar
{
    sealed partial class PetArt
    {
        public double ChargeRiseAge=double.PositiveInfinity;
        const double ChargeRiseBaseDuration=1.52;
        public const double ChargeRiseTimeScale=1.20;
        public const double ChargeRiseDuration=ChargeRiseBaseDuration*ChargeRiseTimeScale;
        public int ChargeRiseLevel;

        void BendGrandLens(double cx,double cy,double age)
        {
            double radius=0.49-0.34*Ease(age/(GrandWave.Release-GrandWave.CallTime));
            double pull=0.035*IdleMotion.Beat(age,0.18,0.68,0.79);
            for(int leg=0;leg<legs.Length;leg++)
            {
                double strength=pull*(1-legs[leg].GripBlend);
                for(int i=leg*LegRig.VertexCount;i<(leg+1)*LegRig.VertexCount;i++)
                {
                    var p=positions[i]; double dx=p.X-cx,dy=p.Y-cy,r=Math.Sqrt(dx*dx+dy*dy);
                    double u=(r-radius)/0.12;
                    if(Math.Abs(u)>=1 || r<0.001) continue;
                    double delta=strength*(1-u*u)*(1-u*u)/r;
                    p.X-=(dx-0.38*dy)*delta; p.Y-=(dy+0.38*dx)*delta; positions[i]=p;
                }
            }
        }
        sealed partial class Effects
        {
            static readonly Geometry FirstShell=InnerBand(3.1),SecondShell=InnerBand(5.8);
            static readonly Geometry InnerWell=ChargeContour(
                "M 0,-7 C -7,-12 -11,-21 -21,-19 C -27,-18 -30,-11 -29,-6 Q -32,0 -24,9 "+
                "Q -26,4 -26,2 Q -23,12 -15,15 Q -7,18 0,29 Q 5,22 11,19 L 10,22 Q 21,16 25,7 "+
                "L 23,8 Q 30,1 30,-7 Q 28,-4 26,-7 Q 23,-10 24,-16 C 15,-23 5,-15 0,-7 Z");
            static readonly Geometry IceFaces=ChargeContour(
                "M -36,-9 Q -34,-23 -22,-25 L -28,-19 L -31,-15 Z M -33,3 Q -27,13 -18,17 L -22,15 Z "+
                "M 11,-18 Q 18,-26 25,-24 L 21,-21 Z M 30,-19 Q 36,-14 36,-8 L 32,-12 Z "+
                "M 4,31 L 17,19 L 11,26 L 0,37 Z");
            static readonly Geometry FacetEdges=ChargeContour(
                "M -35,-8 Q -35,-18 -23,-23 L -29,-15 M -35,1 Q -32,8 -25,12 "+
                "M 10,-17 L 19,-22 L 25,-22 M 35,-8 Q 36,0 29,6 M 16,20 L 2,35");
            static readonly Geometry ShellSurface=(Geometry)Geometry.Combine(HeartOutline.Shape,InnerWell,GeometryCombineMode.Exclude,null).GetAsFrozen();
            static readonly Geometry Reflection=ChargeContour("M 3,-36 L 18,-36 L -6,42 L -24,42 Z");
            static readonly Geometry Subsurface=ChargeContour(
                "M -31,-4 Q -29,-20 -19,-21 Q -26,-15 -25,-7 Q -27,-10 -28,0 Z "+
                "M 29,-11 Q 34,0 21,12 Q 26,4 25,1 Q 28,-3 29,-11 Z "+
                "M -22,11 Q -11,17 -6,19 L 1,27 Q -5,24 -9,20 Q -15,19 -22,11 Z");
            static readonly Geometry Shard=ChargeContour("M 2.8,0 L -0.7,-1.2 L -2.5,0 L -0.6,1.05 Z");
            static readonly Geometry ShardFace=ChargeContour("M 2.8,0 L -0.7,-1.2 L -2.5,0 Z");
            static readonly Pen FacetCut=Stroke(159,15,98,164,1.25),FacetLip=Stroke(214,212,255,252,0.82);
            static readonly Pen DustVapor=Stroke(98,78,164,226,1.7),DustSilver=Stroke(210,170,236,255,1.1);
            static readonly Pen DustShade=Stroke(111,14,51,84,2.5);
            static readonly LinearGradientBrush Silver=MakeReflection();
            static LinearGradientBrush MakeReflection()
            {
                var b=new LinearGradientBrush { StartPoint=new Point(0,0.35),EndPoint=new Point(1,0.63) };
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0,113,207,255),0.12));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(85,167,240,255),0.48));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(203,235,255,255),0.57));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(20,133,223,255),0.63));
                b.Freeze(); return b;
            }
            static readonly Pen ShellEdge=Stroke(240,16,105,165,1.45);
            static Geometry ChargeContour(string path)
            { var g=Geometry.Parse(path); g.Freeze(); return g; }
            static Geometry InnerBand(double thickness)
            {
                // Measure inward from the same closed contour, including the notch and the tip.
                var heart=HeartOutline.Shape;
                var stroke=new Pen(Brushes.White,2*thickness) { LineJoin=PenLineJoin.Round };
                var rim=heart.GetWidenedPathGeometry(stroke,0.04,ToleranceType.Absolute);
                var band=Geometry.Combine(heart,rim,GeometryCombineMode.Intersect,null,0.04,ToleranceType.Absolute);
                band.Freeze(); return band;
            }
            static readonly LinearGradientBrush Mantle=MakeMantle();
            static LinearGradientBrush MakeMantle()
            {
                var b=new LinearGradientBrush { StartPoint=new Point(0.18,0.02),EndPoint=new Point(0.77,0.94) };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(56,181,253),0));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(133,255,247),0.28));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(35,171,225),0.51));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(42,223,239),0.76));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(138,255,246),1));
                b.Freeze(); return b;
            }
            static readonly RadialGradientBrush Well=MakeWell();
            static RadialGradientBrush MakeWell()
            {
                var b=new RadialGradientBrush { Center=new Point(0.47,0.46),GradientOrigin=new Point(0.46,0.45),RadiusX=0.56,RadiusY=0.59 };
                b.GradientStops.Add(new GradientStop(Color.FromRgb(2,12,26),0.18));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(7,35,85),0.43));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(21,78,180),0.75));
                b.GradientStops.Add(new GradientStop(Color.FromRgb(30,125,206),1));
                b.Freeze(); return b;
            }

            static void DrawCrystalSurface(DrawingContext dc,int level,double time,double gather)
            {
                if(level==1)
                {
                    dc.PushOpacity(0.73+0.09*Math.Sin(time*1.18));
                    dc.DrawGeometry(Mantle,null,FirstShell); dc.Pop();
                    dc.PushClip(FirstShell);
                    dc.PushTransform(new ScaleTransform(0.97,0.97,0,4));
                    double head=time/8.4;
                    LitArc(dc,head-0.080,head,0.60);
                    LitArc(dc,head-0.018,head,0.85);
                    dc.Pop(); dc.Pop();
                }
                else if(level>=2)
                {
                    bool full=level==3;
                    dc.DrawGeometry(Mantle,full?ShellEdge:null,full?HeartShape:SecondShell);
                    if(full) DrawInnerWell(dc,time);
                    dc.PushClip(full?ShellSurface:SecondShell);
                    dc.DrawGeometry(null,FacetCut,FacetEdges);
                    dc.PushTransform(new TranslateTransform(-0.45,-0.75));
                    dc.DrawGeometry(null,FacetLip,FacetEdges); dc.Pop();
                    dc.PushOpacity(0.44+0.16*Math.Sin(time*1.65));
                    dc.DrawGeometry(Brushes.Azure,null,IceFaces); dc.Pop();
                    double cycle=time*(full?0.26:0.18)+gather*0.32;
                    double phase=cycle-Math.Floor(cycle),travel=0.44*phase+0.055;
                    double glimmer=Math.Sin(phase*Math.PI)*(full?0.94:0.70);
                    dc.PushTransform(new TranslateTransform(-59+118*phase,0));
                    dc.DrawGeometry(Silver,null,Reflection); dc.Pop();
                    dc.PushTransform(new ScaleTransform(0.94,0.94,0,4));
                    LitArc(dc,travel-0.033,travel+0.022,glimmer);
                    LitArc(dc,-travel-0.022,-travel+0.033,glimmer);
                    dc.Pop();
                    if(full)
                    {
                        // Light pools at the point just as the two rim highlights meet.
                        double focus=IdleMotion.Beat(phase-0.63,0.17,0.23,0.37);
                        dc.PushOpacity(focus);
                        dc.DrawEllipse(Glow,null,new Point(0,32),12,8);
                        dc.DrawLine(FacetLip,new Point(-4,31),new Point(0,35));
                        dc.DrawLine(FacetLip,new Point(0,35),new Point(5,29)); dc.Pop();
                    }
                    dc.Pop();
                }
            }
            static void DrawInnerWell(DrawingContext dc,double time)
            {
                dc.DrawGeometry(Well,null,InnerWell);
                dc.PushClip(InnerWell);
                double breath=0.5+0.5*Math.Sin(time*1.65);
                dc.PushTransform(new TranslateTransform(1.6*Math.Sin(time*0.83),2.4*breath-0.8));
                dc.PushOpacity(0.41+0.31*breath);
                dc.DrawGeometry(Mantle,null,Subsurface); dc.Pop(); dc.Pop();
                dc.PushOpacity(0.32+0.36*breath);
                dc.DrawEllipse(Glow,null,new Point(-23,5-10*breath),9,13);
                dc.DrawEllipse(Glow,null,new Point(24,-9+8*breath),7,10);
                dc.Pop(); dc.Pop();
            }
            void DrawStoredLight(DrawingContext dc,int level,double time,double age,double sealedWeight,double presence)
            {
                double gather=double.IsPositiveInfinity(age)?0:Ease(age/GrandWave.Release);
                dc.PushOpacity((1-0.52*sealedWeight)*presence);
                if(!pet.LiquidCore) DrawCrystalSurface(dc,level,time,gather);
                dc.PushOpacity(1-gather);
                for(int i=0;i<level;i++)
                {
                    double angle=time*0.52+2*Math.PI*i/level+gather*2.4;
                    double shrink=1-0.80*gather;
                    double tilt=level>=2?(level==2?-4:5):0;
                    var p=new Point(Math.Cos(angle)*21*shrink,5+(Math.Sin(angle)*11+tilt*Math.Cos(angle))*shrink);
                    double depth=level>=2?0.84+0.24*Math.Sin(angle):1;
                    if(level>=2)
                    {
                        double a=angle-0.30;
                        var echo=new Point(Math.Cos(a)*21*shrink,5+(Math.Sin(a)*11+tilt*Math.Cos(a))*shrink);
                        dc.DrawLine(DustVapor,echo,p);
                        dc.DrawEllipse(Blue,null,echo,0.85*depth,0.85*depth);
                    }
                    dc.DrawEllipse(Glow,null,p,5.5,5.5);
                    dc.PushTransform(new TranslateTransform(p.X,p.Y));
                    dc.PushTransform(new ScaleTransform(2.4*depth,3.5*depth));
                    dc.DrawGeometry(Ice,null,Glint); dc.Pop(); dc.Pop();
                }
                dc.Pop(); dc.Pop();
            }

            static Point OrbitPoint(double a,double rx,double ry)
            { return new Point(rx*Math.Cos(a),ry*Math.Sin(a)); }

            static readonly Geometry DustRays=ChargeContour(
                "M 0,-20 L 0.9,-2.4 L 9,-10 L 2.4,-1 L 17,0.6 L 2.7,1.2 L 8,11 L 1,2.4 "+
                "L -0.7,17 L -1.3,2.3 L -10,9 L -2.5,0.9 L -16,-0.5 L -2.4,-1.2 L -9,-12 L -1,-2.4 Z");

            static readonly Pen DustRayEdge=Stroke(90,29,111,158,0.65);
            static readonly Brush DustRayBlue=Frozen(Color.FromRgb(103,212,241));
            static void DrawDustFlare(DrawingContext dc,double time,bool opposite)
            {
                double angle=0.26*time+0.9+(opposite?Math.PI:0),z=Math.Sin(angle);
                Point p=OrbitPoint(angle,122,70);
                double phase=(time/1.12+(opposite?0.48:0))%1;
                double light=phase<0.15?Ease(phase/0.15):1-Ease((phase-0.15)/0.85);
                light*=light;
                double size=0.66+0.04*z+0.50*light;
                dc.PushOpacity(0.75+0.04*z+0.20*light);
                dc.PushTransform(new MatrixTransform(size,0,0,size,p.X,p.Y));
                dc.DrawEllipse(Glow,null,new Point(),8+4*light,8+4*light);
                dc.DrawGeometry(DustRayBlue,DustRayEdge,DustRays);
                dc.PushOpacity(light); dc.DrawGeometry(Brushes.White,null,DustRays); dc.Pop();
                dc.DrawEllipse(Brushes.White,null,new Point(),1.4+0.8*light,1.4+0.8*light);
                dc.Pop(); dc.Pop();
            }

            static readonly RadialGradientBrush RiseStar=MakeRiseStar();
            static RadialGradientBrush MakeRiseStar()
            {
                var brush=new RadialGradientBrush();
                brush.GradientStops.Add(new GradientStop(Colors.White,0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(218,249,255),0.19));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(132,218,241),0.48));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(96,185,225),1));
                brush.Freeze(); return brush;
            }
            static readonly Geometry RiseSpine=ChargeContour(
                "M 0,-0.94 Q 0.014,-0.08 0.045,-0.027 L 0.80,0 Q 0.085,0.014 0.032,0.047 L 0,0.94 "+
                "Q -0.012,0.08 -0.045,0.026 L -0.80,0 Q -0.08,-0.014 -0.03,-0.046 Z");
            static readonly Geometry RiseMark=ChargeContour("M 0,-5.54256 L 6.4,5.54256 -6.4,5.54256 Z");
            static readonly Pen RiseMarkEdge=Stroke(255,34,123,138,1.75);

            void DrawChargeRise(DrawingContext dc)
            {
                double age=pet.ChargeRiseAge/ChargeRiseTimeScale;
                if(age<0 || age>=ChargeRiseBaseDuration) return;
                dc.PushOpacity(pet.presence*(1-0.45*Ease(pet.Seal)));
                double shade=IdleMotion.Beat(age,0.14,0.68,0.99);
                dc.PushOpacity(0.79*shade); dc.DrawGeometry(Void,null,HeartShape); dc.Pop();

                double head=0.04+0.46*Ease((age-0.12)/0.49);
                double tail=Math.Max(0,head-0.16*(1-Ease((age-0.57)/0.15)));
                double edge=IdleMotion.Beat(age-0.06,0.15,0.66,0.83);
                for(int side=-1;side<=1;side+=2)
                {
                    LitArc(dc,side*tail,side*head,edge,CrossingBand);
                }
                // The two fronts meet at the point and hold before the center ignites.
                double tip=IdleMotion.Beat(age-0.53,0.09,0.25,0.39);
                Point point=HeartOutline.At(0.5);
                dc.PushOpacity(tip);
                dc.DrawEllipse(Glow,null,point,11,9);
                dc.PushTransform(new MatrixTransform(6,0,0,8,point.X,point.Y));
                dc.DrawGeometry(Brushes.White,null,Glint); dc.Pop(); dc.Pop();

                double spark=IdleMotion.Beat(age-0.79,0.10,0.18,0.54);
                double afterglow=IdleMotion.Beat(age-0.82,0.11,0.32,0.65);
                dc.PushOpacity(afterglow*0.60);
                dc.DrawGeometry(null,RimLight,HeartShape); dc.Pop();
                dc.PushOpacity(spark);
                Point center=(Point)pet.starOffset;
                dc.DrawEllipse(Glow,null,center,24,21);
                double size=8+19*spark;
                dc.PushTransform(new MatrixTransform(size*1.23,0,0,size,center.X,center.Y));
                dc.DrawGeometry(RiseStar,null,Glint);
                dc.DrawGeometry(Brushes.Azure,null,RiseSpine); dc.Pop(); dc.Pop();

                // The earned facet lights on the central beat, including each separately queued level.
                double marks=IdleMotion.Beat(age-0.21,0.15,0.99,ChargeRiseBaseDuration-0.21);
                double lit=Ease((age-0.79)/0.10);
                dc.PushOpacity(marks);
                for(int i=1;i<=3;i++)
                {
                    dc.PushTransform(new MatrixTransform(1,0,0,i==2?-1:1,(i-2)*13.4,i==2?23:16));
                    dc.DrawGeometry(null,RiseMarkEdge,RiseMark);
                    dc.PushOpacity(i<pet.ChargeRiseLevel?0.90:i==pet.ChargeRiseLevel?lit:0);
                    dc.DrawGeometry(i==pet.ChargeRiseLevel?Brushes.White:Ice,null,RiseMark);
                    dc.Pop(); dc.Pop();
                }
                dc.Pop();
                dc.Pop();
            }

            void DrawChargeDust(DrawingContext dc,Point center,int level,double time,double gather,bool front)
            {
                bool stored=double.IsPositiveInfinity(pet.GrandAge);
                dc.PushTransform(new TranslateTransform(center.X,center.Y));
                dc.PushTransform(new ScaleTransform(1-0.76*gather,1-0.76*gather));
                for(int lane=0;lane<level-1;lane++)
                {
                    double tilt=(lane==0?-23:38)+6*Math.Sin(time*0.14+lane);
                    dc.PushTransform(new RotateTransform(tilt));
                    int count=lane==0?6:5, direction=lane==0?1:-1;
                    for(int i=0;i<count;i++)
                    {
                        if(stored)
                        {
                            if(lane==0 && (i==3 || i==5) || lane==1 && (i==0 || i==2)) continue;
                            if(pet.ChargeLevel==3 && lane==0 && (i==1 || i==4))
                            {
                                // Keep both stars legible over the limbs; their size and light carry the depth.
                                if(front) DrawDustFlare(dc,time,i==4);
                                continue;
                            }
                        }
                        double angle=direction*(time*(lane==0?0.65:0.51)+gather*1.8)+i*(2*Math.PI/count)+0.24*Math.Sin(i*3.8)+lane*0.93;
                        double z=Math.Sin(angle),near=Ease((z+0.22)/0.44);
                        double opacity=(front?near:1-near)*(0.73+0.21*z)*(1-gather);
                        if(opacity<0.012) continue;
                        bool shard=i%3==0;
                        double rx=(lane==0?120:127)+5*Math.Sin(i*4.31),ry=(lane==0?68:61)+4*Math.Cos(i*2.5);
                        double tail=direction*(shard?0.26:0.084)*(stored?0.68:1);
                        Point p=OrbitPoint(angle,rx,ry),mid=OrbitPoint(angle-tail*0.45,rx,ry),end=OrbitPoint(angle-tail,rx,ry);
                        dc.PushOpacity(opacity);
                        if(shard || !stored)
                        {
                            if(front && shard) dc.DrawLine(DustShade,mid,p);
                            dc.DrawLine(DustVapor,end,mid); dc.DrawLine(DustSilver,mid,p);
                        }
                        if(shard)
                        {
                            Vector tangent=p-mid; tangent.Normalize();
                            double size=1.32+0.37*z;
                            dc.DrawEllipse(Glow,null,p,5.9*size,5.9*size);
                            dc.PushTransform(new MatrixTransform(tangent.X*size,tangent.Y*size,-tangent.Y*size,tangent.X*size,p.X,p.Y));
                            dc.DrawGeometry(Blue,null,Shard); dc.DrawGeometry(Brushes.Azure,null,ShardFace); dc.Pop();
                            if(!stored)
                            {
                                dc.DrawEllipse(Ice,null,OrbitPoint(angle-tail*1.32,rx,ry+1.2),0.8,0.8);
                                dc.DrawEllipse(Blue,null,OrbitPoint(angle-tail*1.52,rx+1.8,ry-1.2),0.65,0.65);
                            }
                        }
                        else dc.DrawEllipse(Ice,null,p,1.25+0.28*z,1.05+0.20*z);
                        dc.Pop();
                    }
                    dc.Pop();
                }
                dc.Pop(); dc.Pop();
            }

            static Point DustPosition(Point c,double angle,double radius,double u,double spin)
            {
                double a=angle+spin*u*u,r=radius*(1-u*u)+4;
                return c+new Vector(Math.Cos(a)*r,Math.Sin(a)*r);
            }
            static void DrawInfall(DrawingContext dc,Point c,double age)
            {
                if(age>=GrandWave.Release) return;
                for(int i=0;i<68;i++)
                {
                    double start=0.02+1.83*Math.Pow(i/67.0,0.77);
                    double end=Math.Min(GrandWave.Release-0.015,start+0.65+0.24*Math.Sin(i*3.71));
                    double u=(age-start)/(end-start);
                    if(u<=0 || u>=1) continue;
                    double angle=i*2.399963, radius=105+25*Math.Sin(i*4.31);
                    double spin=(i%4==0?-1:1)*(0.38+0.24*Math.Sin(i));
                    Point head=DustPosition(c,angle,radius,u,spin);
                    Point tail=DustPosition(c,angle,radius,Math.Max(0,u-0.075),spin);
                    dc.PushOpacity(IdleMotion.Beat(u,0.18,0.90,1)*(0.72+0.25*Math.Sin(i*5)));
                    dc.DrawLine(StarThread,tail,head);
                    dc.DrawEllipse(Glow,null,head,4.3,4.3);
                    dc.DrawEllipse(i%3==0?Brushes.White:Ice,null,head,1+u,0.9+u); dc.Pop();
                }
            }
            static void DrawGrandLight(DrawingContext dc,double age)
            {
                double late=IdleMotion.Beat(age-GrandWave.CallTime,0.43,0.72,0.83);
                double after=IdleMotion.Beat(age-GrandWave.Release,0.055,0.10,0.52);
                double light=Math.Max(late,after);
                if(light<=0) return;
                dc.PushOpacity(0.91*light);
                dc.DrawEllipse(Glow,null,new Point(),59+19*light,50+14*light);
                dc.PushClip(HeartShape);
                dc.DrawEllipse(Glow,null,new Point(),29*light+4,26*light+4);
                for(int i=0;i<5;i++)
                {
                    double a=i*(2*Math.PI/5)+age*1.8;
                    Vector axis=new Vector(Math.Cos(a),Math.Sin(a));
                    dc.DrawLine(StarThread,new Point()+axis*(8+9*(1-light)),new Point()+axis*3);
                }
                dc.Pop();
                dc.DrawEllipse(Glow,null,new Point(),35+59*light,3.5+3*light);
                dc.DrawEllipse(Brushes.White,null,new Point(),3+9*light,1.2+0.8*light);
                dc.DrawGeometry(null,RimLight,HeartShape); dc.Pop();
            }
        }
    }
}
