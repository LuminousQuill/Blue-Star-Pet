using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlueStar
{
    sealed partial class PetArt
    {
        sealed partial class Effects
        {
            static readonly Pen LiquidRim=Stroke(250,109,212,255,2.15);

            Nebula.Shell shell;

            void DrawLiquid(DrawingContext dc,double level,double time,double gather,double tilt)
            {
                double lean=Math.Sin(tilt*Math.PI/180),starlight=0.76+0.24*Ease(level);
                dc.PushClip(HeartOutline.Shape);
                dc.DrawGeometry(Nebula.Night,null,HeartOutline.Shape);
                dc.PushOpacity(starlight);
                dc.PushTransform(new TranslateTransform(0.65*Math.Sin(time*0.18)+lean,0.55*Math.Cos(time*0.21)));
                dc.DrawDrawing(Nebula.FarStars); dc.Pop(); dc.Pop();
                dc.PushOpacity(0.46+0.08*Ease(level-2));
                dc.DrawImage(Halo,HaloBounds); dc.Pop();
                if(shell==null) shell=new Nebula.Shell();
                shell.Draw(dc,level,time+2*gather);
                dc.PushOpacity(starlight);
                dc.PushTransform(new TranslateTransform(1.3*Math.Sin(time*0.27)+0.6*lean,0.9*Math.Cos(time*0.21)));
                dc.DrawDrawing(Nebula.Cluster); dc.Pop(); dc.Pop();
                dc.DrawGeometry(null,Nebula.Limb,HeartOutline.Shape);
                dc.Pop();
            }

            static class Nebula
            {
                internal static readonly Rect CoreBounds=new Rect(-42,-30,84,72);
                internal static readonly Brush Night=MakeNight();
                internal static readonly Pen Limb=Stroke(225,172,230,255,1.5);
                internal static readonly Drawing FarStars=MakeFarStars(),Cluster=MakeCluster();
                static Brush MakeNight()
                {
                    var b=new RadialGradientBrush { Center=new Point(0.46,0.44),GradientOrigin=new Point(0.46,0.44),RadiusX=0.65,RadiusY=0.70 };
                    b.GradientStops.Add(new GradientStop(Color.FromRgb(2,6,18),0));
                    b.GradientStops.Add(new GradientStop(Color.FromRgb(5,14,37),0.46));
                    b.GradientStops.Add(new GradientStop(Color.FromRgb(17,36,76),0.84));
                    b.GradientStops.Add(new GradientStop(Color.FromRgb(19,60,110),1));
                    b.Freeze(); return b;
                }

                static Drawing MakeFarStars()
                {
                    var stars=new Random(713);
                    var drawing=new DrawingGroup();
                    using(var dc=drawing.Open())
                    {
                        for(int i=0;i<89;i++)
                        {
                            double x=84*stars.NextDouble()-42;
                            // The unresolved stars gather along an oblique band behind the nearer cluster.
                            double y=i<38 ? 78*stars.NextDouble()-34 :
                                9-0.38*x+3*Math.Sin(x/13)+15*(stars.NextDouble()-stars.NextDouble());
                            Point p=new Point(x,y);
                            if(((Vector)p).Length<10) continue;
                            double radius=0.32+0.54*Math.Pow(stars.NextDouble(),2);
                            dc.DrawEllipse(i%4==0?Brushes.LightSteelBlue:Brushes.SlateGray,null,p,radius,radius);
                        }
                    }
                    drawing.Freeze(); return drawing;
                }

                static Drawing MakeCluster()
                {
                    var group=new DrawingGroup();
                    using(var dc=group.Open())
                    {
                        // A loose group on the upper bank, balanced by a distant pair near the tip.
                        Star(dc,-25,-12,1.00); Star(dc,-21,-13.8,0.52); Star(dc,-18.6,-10,0.70);
                        Star(dc,-21.8,-6.6,0.46); Star(dc,-13.2,-9,0.57); Star(dc,-29.5,-7.8,0.43);
                        Star(dc,23,-7,0.73); Star(dc,26.5,-4.2,0.48);
                        Star(dc,7.5,20.3,0.89); Star(dc,3.6,23,0.50);
                    }
                    group.Freeze(); return group;
                }

                static void Star(DrawingContext dc,double x,double y,double radius)
                {
                    var p=new Point(x,y);
                    if(radius>0.85)
                    {
                        dc.PushOpacity(0.62);
                        dc.DrawEllipse(Glow,null,p,4.1*radius,4.1*radius);
                        dc.Pop();
                    }
                    if(radius>=1)
                    {
                        dc.PushTransform(new TranslateTransform(x,y));
                        dc.PushTransform(new ScaleTransform(3.0,2.0));
                        dc.DrawGeometry(Ice,null,Glint); dc.Pop(); dc.Pop();
                    }
                    dc.DrawEllipse(radius>0.65?Brushes.AliceBlue:Brushes.LightSteelBlue,null,p,radius,radius);
                }

                internal sealed class Shell
                {
                    const int Columns=512,Rows=128,Width=168,Height=144;
                    static readonly byte[] disk=LoadDisk();
                    static readonly Point pole=new Point(0,2);
                    static readonly Geometry CometWake=(Geometry)Geometry.Parse(
                        "M 0,-1.1 Q -2.7,-0.72 -7.2,0 Q -2.7,0.18 0,1.1 Z").GetAsFrozen();
                    readonly double[] width=new double[Columns];
                    readonly double[] ridge=new double[Columns],reflection=new double[Columns],density=new double[Columns];
                    readonly double[] surge=new double[Columns];
                    readonly int[] pixels=new int[Width*Height];
                    readonly float[] arc=new float[Width*Height],depth=new float[Width*Height];
                    readonly double[] contourRadius=new double[Width*Height],notchScale=new double[Columns];
                    readonly int[][] visibleTexels=new int[4][];
                    readonly WriteableBitmap surface=new WriteableBitmap(Width,Height,192,192,PixelFormats.Bgra32,null);
                    double lastFrame=double.NaN,lastLevel=-1;
                    int lastBand=-1;

                    static byte[] LoadDisk()
                    {
                        var image=new FormatConvertedBitmap(LoadTexture("galaxy-disc.png",Columns),PixelFormats.Gray8,null,0);
                        var luminance=new byte[Columns*Rows];
                        image.CopyPixels(luminance,Columns,0);
                        return luminance;
                    }

                    internal Shell()
                    {
                        // Tabulate distance along the closed contour and depth towards the well.
                        var angles=new double[Columns+1];
                        var radius=new double[Columns];
                        for(int j=0;j<Columns;j++)
                        {
                            double u=j/(double)Columns;
                            Vector ray=HeartOutline.At(u)-pole;
                            angles[j]=Math.Atan2(-ray.X,-ray.Y);
                            if(angles[j]<0) angles[j]+=2*Math.PI;
                            radius[j]=ray.Length;
                            double notch=Math.Min(u,1-u)/0.055;
                            notchScale[j]=1-0.52*Math.Exp(-notch*notch);
                        }
                        angles[Columns]=2*Math.PI;
                        for(int i=0;i<depth.Length;i++)
                        {
                            double x=CoreBounds.X+(i%Width+0.5)/2-pole.X;
                            double y=CoreBounds.Y+(i/Width+0.5)/2-pole.Y;
                            double a=Math.Atan2(-x,-y);
                            if(a<0) a+=2*Math.PI;
                            int j=Array.BinarySearch(angles,a);
                            if(j<0) j=~j-1;
                            double f=(a-angles[j])/(angles[j+1]-angles[j]);
                            double r=radius[j]+f*(radius[(j+1)%Columns]-radius[j]);
                            arc[i]=(float)(j+f);
                            depth[i]=(float)(1-Math.Sqrt(x*x+y*y)/r);
                            int column=(int)arc[i],sample=column&(Columns-1);
                            contourRadius[i]=radius[sample]+(arc[i]-column)*(radius[(sample+1)&(Columns-1)]-radius[sample]);
                        }
                        // The widest wave in each tier bounds the entire feather. Precompute
                        // which texels can contribute, including intermediate charge levels.
                        for(int tier=0;tier<visibleTexels.Length;tier++)
                        {
                            var indices=new List<int>();
                            double widest=(0.105+0.19*tier)*1.065;
                            for(int i=0;i<depth.Length;i++)
                            {
                                double r=contourRadius[i];
                                double feather=Math.Min(6.8,0.85+0.40*widest*r);
                                if(depth[i]>=-0.16 && (widest-depth[i])*r+0.26*feather>=-1e-9)
                                    indices.Add(i);
                            }
                            visibleTexels[tier]=indices.ToArray();
                        }
                    }

                    static double Sample(double x,double v)
                    {
                        int col=(int)x;
                        if(x<col) col--;
                        int a=col&(Columns-1),b=(a+1)&(Columns-1);
                        double y=Math.Max(0,Math.Min(1,v))*(Rows-1);
                        int row=Math.Min((int)y,Rows-2),top=row*Columns,bottom=top+Columns;
                        double fx=x-col;
                        double p=disk[top+a]+fx*(disk[top+b]-disk[top+a]);
                        double q=disk[bottom+a]+fx*(disk[bottom+b]-disk[bottom+a]);
                        return (p+(y-row)*(q-p))/255.0;
                    }

                    Point StarPosition(double u,double v)
                    {
                        u-=Math.Floor(u);
                        Point rim=HeartOutline.At(u);
                        return rim+(pole-rim)*(width[(int)(Columns*u)]*v);
                    }

                    void DrawChargeLights(DrawingContext dc,double seconds,double second,double full,
                        double reach,double burst,double flare)
                    {
                        if(second<=0) return;
                        // The leading star glances over the lobes. Its short wake follows the same path.
                        double phase=seconds*0.67-2.45,u=0.21*Math.Sin(phase);
                        Point head=StarPosition(u,0.36);
                        Vector tangent=HeartOutline.Tangent(u);
                        dc.PushOpacity(second);
                        for(int i=3;i>=1;i--)
                        {
                            double trail=0.21*Math.Sin(phase-i*0.092);
                            Point p=StarPosition(trail,0.36+0.025*i);
                            double radius=1.18-0.21*i;
                            dc.DrawEllipse(i==1?Ice:Blue,null,p,radius,radius);
                        }
                        dc.DrawEllipse(Glow,null,head,6.7,6.7);
                        dc.PushTransform(new MatrixTransform(tangent.X,tangent.Y,-tangent.Y,tangent.X,head.X,head.Y));
                        dc.PushTransform(new ScaleTransform(Math.Cos(phase),1));
                        dc.DrawGeometry(Ice,null,CometWake); dc.Pop();
                        dc.PushTransform(new ScaleTransform(4.8,2.55));
                        dc.DrawGeometry(Brushes.AliceBlue,null,Glint); dc.Pop(); dc.Pop();
                        dc.DrawEllipse(Brushes.AliceBlue,null,head,0.96,0.96);
                        dc.Pop();
                        if(full<=0) return;

                        Point tip=StarPosition(0.50,0.28);
                        dc.PushOpacity(full);
                        dc.DrawEllipse(Glow,null,tip,5.0+4.0*flare,4.0+4.0*flare);
                        dc.PushTransform(new MatrixTransform(2.4+1.2*flare,0,0,3.1+2.3*flare,tip.X,tip.Y));
                        dc.DrawGeometry(Ice,null,Glint); dc.Pop();
                        dc.DrawEllipse(Brushes.AliceBlue,null,tip,0.75+0.45*flare,0.75+0.45*flare);
                        dc.PushOpacity(burst);
                        for(int hand=-1;hand<=1;hand+=2)
                            for(int i=0;i<3;i++)
                            {
                                Point p=StarPosition(0.50+hand*reach*(1-0.18*i),0.27+0.14*i+0.035*hand*i);
                                double r=1.16-0.21*i;
                                dc.DrawEllipse(i==0?Brushes.AliceBlue:Ice,null,p,r,r);
                            }
                        dc.Pop(); dc.Pop();
                    }

                    internal void Draw(DrawingContext dc,double level,double time)
                    {
                        double frame=Math.Round(time*60);
                        double first=Ease(level),second=Ease(level-1),full=Ease(level-2);
                        double seconds=frame/60.0,travel=(seconds*0.046)%1,back=(seconds*0.034)%1;
                        double beat=(seconds/4.6)%1;
                        double reach=beat<0.30 ? 0.075*(1-Ease(beat/0.30)) : 0.19*Ease((beat-0.30)/0.70);
                        double burst=Ease(beat/0.12)*(1-Ease((beat-0.70)/0.30));
                        double flare=1-Ease(Math.Abs(beat-0.30)/0.32);
                        if(frame!=lastFrame || level!=lastLevel)
                        {
                            // Each charge adds glass around the entire perimeter, including both lobes.
                            double thickness=0.105+0.19*first+0.19*second+0.19*full;
                            double charge=(first+second+full)/3;
                            double dustGain=0.41+0.24*first+0.23*second+0.10*full;
                            double lensGain=0.46*first+0.59*charge;
                            for(int j=0;j<Columns;j++)
                            {
                                double u=j/(double)Columns,a=2*Math.PI*u;
                                double wave=Math.Sin(2*(a-2*Math.PI*travel)+0.42*Math.Sin(3*a));
                                width[j]=thickness*(1+0.065*wave)*notchScale[j];
                                ridge[j]=0.34+0.23*Math.Sin(a+0.75-2*Math.PI*back)+0.18*Math.Sin(3*a-2*Math.PI*travel);
                                double facing=0.5+0.5*Math.Cos(a-2*Math.PI*travel);
                                reflection[j]=0.27+0.73*facing*facing;
                                double flow=a-2*Math.PI*travel;
                                double knots=0.50+0.30*Math.Sin(3*flow+0.64*Math.Sin(a+2*Math.PI*back))
                                    +0.21*Math.Cos(5*flow-0.72*Math.Cos(2*a));
                                density[j]=Ease((knots-0.23)/0.57);
                                if(full>0)
                                {
                                    double delta=(Math.Abs(u-0.50)-reach)/0.037;
                                    surge[j]=full*burst*Math.Exp(-delta*delta);
                                }
                            }
                            int band=(int)Math.Ceiling(first+second+full);
                            if(band<lastBand) Array.Clear(pixels,0,pixels.Length);
                            lastBand=band;
                            foreach(int i in visibleTexels[band])
                            {
                                double d=depth[i];
                                int col=(int)arc[i],j=col&(Columns-1),next=(j+1)&(Columns-1);
                                double mix=arc[i]-col,w=width[j]+mix*(width[next]-width[j]);
                                double r=contourRadius[i];
                                double edge=(w-d)*r;
                                // Feather in heart-space units. Thin areas keep a lit lip at the notch and tip.
                                double feather=Math.Min(6.8,0.85+0.40*w*r);
                                double coverage=Ease(edge/feather+0.26);
                                if(coverage<=0) { pixels[i]=0; continue; }
                                double v=Math.Max(0,Math.Min(1,d/w)),outer=(1-v)*(1-v);
                                double bank=ridge[j]+mix*(ridge[next]-ridge[j]);
                                double facing=reflection[j]+mix*(reflection[next]-reflection[j]);
                                // Keep each patch of stardust at a physical depth in the glass.
                                // More charge exposes more of a patch without pulling it into a ribbon.
                                double knot=density[j]+mix*(density[next]-density[j]);
                                double depthFromKnot=(v-bank)*w*r;
                                double longitude=2*(arc[i]-Columns*travel)+4.1*depthFromKnot;
                                double latitude=0.49+depthFromKnot*(0.021+0.011*facing);
                                double dust=Sample(longitude,latitude);
                                double light=dust*(0.17+0.83*knot)*dustGain;
                                if(second>0)
                                {
                                    double nearby=Sample(longitude+4,latitude+0.027);
                                    double grain=Math.Max(0,dust-nearby);
                                    light+=(1-light)*second*knot*grain;
                                }
                                double deep=0;
                                if(full>0)
                                {
                                    double lower=Sample(2*arc[i]-Columns*back+163-3.6*depthFromKnot,
                                        0.58-0.025*depthFromKnot);
                                    deep=full*lower*(0.13+0.60*knot);
                                    light+=(1-light)*full*knot*Ease((dust-0.60)/0.40)*0.38;
                                    double wave=surge[j]+mix*(surge[next]-surge[j]);
                                    light+=(1-light)*wave*(0.39+0.56*dust)*(0.35+2.6*v*(1-v));
                                }
                                double lens=4*v*(1-v);
                                double glass=(0.68+0.32*facing)*(0.15+0.82*outer+lensGain*(0.45+0.55*lens));
                                double pearl=light*light;
                                int red=(int)(13+19*glass+(211-19*glass)*pearl+35*deep*(1-pearl));
                                int green=(int)(41+83*glass+(198-83*glass)*light+54*deep*(1-light));
                                int blue=(int)Math.Min(255,110+84*glass+(145-84*glass)*light+79*deep*(1-light));
                                int alpha=(int)(255*coverage*(0.60+0.14*outer+0.18*light+0.07*charge));
                                pixels[i]=(alpha<<24)|(red<<16)|(green<<8)|blue;
                            }
                            surface.WritePixels(new Int32Rect(0,0,Width,Height),pixels,Width*4,0);
                            lastFrame=frame; lastLevel=level;
                        }
                        dc.DrawImage(surface,CoreBounds);

                        // Near stars ride the two currents. The background stars remain visible between them.
                        for(int i=0;i<9;i++)
                        {
                            double visible=Ease(2+2*first+2*second+3*full-i);
                            if(visible<=0) break;
                            double u=i*0.381966+(i%3==0?-back:travel);
                            double v=0.27+0.46*(0.5+0.5*Math.Sin(i*2.7+0.4*time));
                            Point p=StarPosition(u,v);
                            double r=i%4==0?0.72:0.47;
                            dc.PushOpacity(visible*(0.65+0.20*first));
                            dc.DrawEllipse(Brushes.LightSteelBlue,null,p,r,r);
                            dc.Pop();
                        }
                        DrawChargeLights(dc,seconds,second,full,reach,burst,flare);
                    }
                }
            }
        }
    }
}
