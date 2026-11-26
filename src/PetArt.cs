using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace BlueStar
{
    sealed class PetArt : Grid
    {
        public const double ReleaseBeat=1.35;
        public const double EjectBeat=0.38;
        readonly MeshGeometry3D mesh=new MeshGeometry3D();
        readonly Point3D[] positions;
        readonly LegRig[] legs;
        readonly Viewport3D legView;
        readonly byte[] atlasAlpha;
        readonly int atlasWidth,atlasHeight;
        readonly Effects behind,front;
        double time,poseTime;
        public double Time
        {
            get { return time; }
            set { if(FocusProgress<0) poseTime+=value-time; time=value; }
        }
        public double AimX,AimY,Attention,Gravity,PulseAge=10,MealAge=10,EjectAge=10,Seal;
        public double HoverWeight,MealWeight,AmbientAge=10;
        public const double AmbientWindup=0.95;
        double MealPhase { get { return MealAge/(0.95+0.80*MealWeight); } }
        public double FocusProgress=-1,DragLean,DragLift;
        public double CoreSize,CoreLight,CoreTilt;
        double presence=1;
        bool departing;
        public double Presence
        {
            get {return presence;}
            set {
                // On interruption retrace the visible pose until an endpoint is reached.
                if(presence==0 && value>0) departing=false;
                if(presence==1 && value<1) departing=true;
                presence=value;
                behind.Opacity=Ease((value-0.20)/0.58);
                legView.Opacity=departing?Ease((value-0.31)/0.36):Ease((value-0.25)/0.34);
            }
        }
        double coreAspect=1;
        public readonly IdleMotion Idle=new IdleMotion();
        public readonly DragWake Wake=new DragWake();
        public readonly RoamStroke Swim=new RoamStroke();
        public Vector BlinkHeading;
        Vector starOffset;
        public Point Heart;
        public void BeginIdle(IdleAct act,Vector direction,MeteorFlight flight)
        {
            Idle.Begin(act,direction,legs,flight);
        }
        public readonly BitmapSource Portrait;

        static BitmapSource LoadTexture(string name,int width)
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                var image=new BitmapImage();
                image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                image.DecodePixelWidth=width; image.StreamSource=stream; image.EndInit();
                image.Freeze(); return image;
            }
        }

        public PetArt()
        {
            Portrait=LoadTexture("blue-star.png",400);
            var uv=new PointCollection();
            var indices=new Int32Collection();
            var limbs=new System.Collections.Generic.List<LegRig>();
            // Back to front. Angles are measured from downward; every hip is hidden by the core.
            Action<int,int,double,double,double,double,double> add=(shape,shade,angle,length,mirror,x,y)=>
                limbs.Add(new LegRig(shape,shade,angle,length,mirror,x,y,limbs.Count,uv,indices));
            add(1,0,175,87,1,-4,-9);    add(0,0,-147,102,1,5,-9);
            add(1,0,-106,114,-1,12,-1); add(0,0,-55,117,1,9,6);
            add(1,0,-16,128,-1,5,10);   add(1,0,44,122,1,-8,8);
            add(0,0,105,108,-1,-12,0);  add(1,0,137,107,-1,-8,-5);
            add(0,1,-179,106,-1,1,-11); add(1,1,-89,119,1,13,1);
            add(1,1,-46,119,1,10,9);    add(0,1,20,132,-1,-4,11);
            add(1,1,75,109,-1,-13,4);   add(0,1,120,108,1,-11,-4);
            add(1,2,-142,101,1,9,-7);   add(2,2,-17,119,1,8,7);
            add(0,2,54,120,-1,-12,7);   add(1,2,153,102,1,-7,-8);
            add(1,3,-78,94,-1,13,3);   add(0,3,1,118,1,0,12);
            add(1,4,79,116,1,0,0);      add(0,3,-160,85,-1,6,-7);
            add(0,3,130,79,-1,-7,-2);   add(1,4,-113,82,1,0,0);
            add(0,4,-37,86,-1,0,0);     add(1,3,40,84,1,-4,5);
            legs=limbs.ToArray(); positions=new Point3D[legs.Length*LegRig.VertexCount];
            uv.Freeze();indices.Freeze();
            mesh.TextureCoordinates=uv;mesh.TriangleIndices=indices;
            var atlas=LoadTexture("leg-atlas.png",576);
            atlasWidth=atlas.PixelWidth; atlasHeight=atlas.PixelHeight;
            var rgba=new FormatConvertedBitmap(atlas,PixelFormats.Bgra32,null,0);
            atlasAlpha=new byte[atlasWidth*atlasHeight];
            var pixels=new byte[atlasAlpha.Length*4]; rgba.CopyPixels(pixels,atlasWidth*4,0);
            for(int i=0;i<atlasAlpha.Length;i++) atlasAlpha[i]=pixels[4*i+3];
            var material=new DiffuseMaterial(new ImageBrush(atlas));
            material.Freeze();
            behind=new Effects(this,false);front=new Effects(this,true);
            legView=LegLayer(mesh,material);
            Children.Add(behind);Children.Add(legView);Children.Add(front);
            var hit=new System.Windows.Shapes.Ellipse {Fill=new SolidColorBrush(Color.FromArgb(1,0,0,0)),
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            Children.Add(hit);
            SizeChanged+=delegate {hit.Width=ActualWidth*0.59;hit.Height=ActualHeight*0.64;};
        }
        static Viewport3D LegLayer(MeshGeometry3D geometry,Material material)
        {
            var view=new Viewport3D { IsHitTestVisible=false, ClipToBounds=false };
            view.Camera=new OrthographicCamera(new Point3D(0,0,3),new Vector3D(0,0,-1),new Vector3D(0,1,0),1);
            view.Children.Add(new ModelVisual3D { Content=new AmbientLight(Colors.White) });
            view.Children.Add(new ModelVisual3D { Content=new GeometryModel3D(geometry,material) });
            return view;
        }
        static double Ease(double u)
        {
            u=Math.Max(0,Math.Min(1,u));return u*u*(3-2*u);
        }
        public void Draw()
        {
            double age=PulseAge,charge=0,fold=0,flare=0;
            if(age>=0 && age<ReleaseBeat)
            {
                charge=Ease(age/ReleaseBeat);
                fold=0.84*Ease(age/1.12);
            }
            else if(age>=ReleaseBeat && age<ReleaseBeat+0.95)
            {
                double since=age-ReleaseBeat;
                fold=since<0.14 ? 0.84-1.13*Ease(since/0.14) : -0.29*(1-Ease((since-0.14)/0.81));
                flare=Math.Exp(-since*5.4);
            }
            double m=MealPhase;
            double squeeze=Ease(m/0.13)*(1-Ease((m-0.14)/0.19));
            double rebound=Ease((m-0.19)/0.20)*(1-Ease((m-0.40)/0.60));
            double drawIn=Gravity*(0.65+0.35*Math.Sin(Time*(3+HoverWeight)));
            double sinceWhisper=AmbientAge-AmbientWindup;
            double gather=sinceWhisper<0?Ease(AmbientAge/AmbientWindup):1-Ease(sinceWhisper/0.14);
            double unfurl=Ease(sinceWhisper/0.14)*(1-Ease((sinceWhisper-0.14)/0.57));
            double tuck=Ease(EjectAge/EjectBeat)*(1-Ease((EjectAge-EjectBeat)/0.13));
            double expel=Ease((EjectAge-EjectBeat)/0.13)*(1-Ease((EjectAge-EjectBeat-0.13)/0.62));
            double flash=FocusProgress>=0?0.5-0.5*Math.Cos(Time*2*Math.PI/1.8):0;
            double focusLight=FocusProgress>=0?0.90*flash-0.28:0;
            double closed=Ease(Seal);
            fold+=0.93*closed+tuck*0.45-expel*0.28;
            fold+=drawIn*(0.09+0.12*HoverWeight)+squeeze*(0.24+0.34*MealWeight)-rebound*0.10+gather*0.42-unfurl*0.19;
            double drift=1-0.82*Idle.Still;
            double breath=1+0.009*drift*Math.Sin(poseTime*2.1)-0.055*squeeze;
            double tilt=drift*0.034*Math.Sin(poseTime*0.94)+AimX*0.14+DragLean;
            Vector glide=Idle.GlideAt(Idle.Age);
            double cx=drift*2.4*Math.Sin(poseTime*0.83)+AimX*11+glide.X;
            double cy=-12+drift*(5.7*Math.Sin(poseTime*1.34)+1.2*Math.Sin(poseTime*2.27))+AimY*8+Idle.DepthAt(Idle.Age)+glide.Y;
            Vector lead=Swim.Heading*(Swim.Strength*IdleMotion.Beat(Swim.Phase,0.19,0.58,0.97));
            tilt+=lead.X*0.105;
            double brace=0,impact=0;
            Vector blinkLean=new Vector();
            if(presence<1)
            {
                double t=departing?1-presence:presence;
                brace=IdleMotion.Beat(t,0.17,0.29,0.44);
                impact=IdleMotion.Beat(t-0.33,0.16,0.20,0.41);
                cy+=(departing?-1:1)*(5.0*brace-4.2*impact);
                blinkLean=BlinkHeading*((departing?1:-1)*IdleMotion.Beat(t,0.24,0.37,0.68));
                cx+=blinkLean.X*5; cy+=blinkLean.Y*5;
            }
            Vector distantGaze=Idle.Direction*10,mouseGaze=new Vector(AimX,AimY)*6.2;
            starOffset=(distantGaze+(mouseGaze-distantGaze)*Ease(Idle.Turn))*Idle.Look+lead*5.6+blinkLean*7;
            for(int i=0;i<legs.Length;i++) legs[i].Pose(positions,i*LegRig.VertexCount,Time,poseTime,fold,tilt,DragLift,breath,cx,cy,AimX,Idle,Wake,glide,presence,departing,Swim);
            var points=new Point3DCollection(positions);points.Freeze();mesh.Positions=points;
            double bank=Idle.BankAt(Idle.Age-0.18);
            coreAspect=(1-0.17*bank*bank)*(1+0.08*lead.Y);
            Heart=new Point((0.5+(cx+AimX*5+5*bank+2.2*lead.X)/300)*ActualWidth,(0.5+(cy+AimY*4+2.2*lead.Y)/300)*ActualHeight);
            double heartbeat=0.5-0.5*Math.Cos(2*Math.PI*age*2.0);
            CoreSize=breath+charge*(0.15+0.15*heartbeat)+0.29*flare+drawIn*(0.10+0.19*HoverWeight)
                -squeeze*(0.25+0.30*MealWeight)+rebound*(0.17+0.47*MealWeight)-gather*0.17+unfurl*0.23-tuck*0.22+expel*0.34;
            CoreLight=Math.Min(1.85,0.58+0.06*Math.Sin(poseTime*2.2)+Attention*0.54+drawIn*(0.29+0.26*HoverWeight)
                +charge*(0.42+0.34*heartbeat)+0.71*flare+rebound*(0.62+0.48*MealWeight)+gather*0.40+unfurl*0.58+expel*0.72+focusLight)*(1-0.73*closed);
            if(presence<1)
            {
                double form=departing?Ease((presence-0.25)/0.23):Ease((presence-0.30)/0.19);
                double swell=departing?brace:impact;
                double recoil=departing?0:IdleMotion.Beat(presence-0.58,0.10,0.12,0.32);
                CoreSize*=0.075+0.925*form+0.25*swell-0.075*recoil;
                coreAspect*=0.46+0.54*form+0.09*swell;
                CoreLight+=0.44*(1-form)+0.94*swell;
            }
            // The resting legs keep their own sway while a gust slowly tilts the core.
            CoreTilt=(tilt+Idle.CoreAngle(AimX*0.14)+0.095*bank)*180/Math.PI;
            behind.InvalidateVisual();front.InvalidateVisual();
        }


        public bool InsideHeart(Point point)
        {
            double scale=ActualWidth/300*CoreSize;
            if(scale<=0) return false;
            Vector local=(point-Heart)/scale;
            var inverse=Matrix.Identity; inverse.Rotate(-CoreTilt);
            local=inverse.Transform(local); local.X/=coreAspect;
            return Effects.HeartShape.FillContains((Point)local);
        }
        public int LegAt(Point point)
        {
            int frontmost=-1;
            VisualTreeHelper.HitTest(legView,null,delegate(HitTestResult hit) {
                var ray=hit as RayMeshGeometry3DHitTestResult;
                if(ray!=null && ray.MeshHit==mesh)
                {
                    var uv=mesh.TextureCoordinates;
                    Point a=uv[ray.VertexIndex1],b=uv[ray.VertexIndex2],c=uv[ray.VertexIndex3];
                    Point tex=a+(b-a)*ray.VertexWeight2+(c-a)*ray.VertexWeight3;
                    int x=Math.Max(0,Math.Min(atlasWidth-1,(int)(tex.X*atlasWidth)));
                    int y=Math.Max(0,Math.Min(atlasHeight-1,(int)(tex.Y*atlasHeight)));
                    if(atlasAlpha[y*atlasWidth+x]>=32) frontmost=Math.Max(frontmost,ray.VertexIndex1/LegRig.VertexCount);
                }
                return HitTestResultBehavior.Continue;
            },new PointHitTestParameters(point));
            return frontmost;
        }
        public void BendLeg(int index,double time) { legs[index].Bend(time); }


        sealed class Effects : FrameworkElement
        {
            readonly PetArt pet;
            readonly bool foreground;
            static readonly SolidColorBrush Ice = Frozen(Color.FromRgb(156,239,255));
            static readonly SolidColorBrush Blue = Frozen(Color.FromRgb(67,175,243));
            static readonly RadialGradientBrush Glow = MakeGlow();
            static readonly Pen Ring = MakePen();
            static readonly Pen SmallWaveGlow=Stroke(96,57,172,255,5);
            static readonly Pen StarThread=Stroke(209,142,224,255,1.3);
            static readonly Pen StarShadow=Stroke(174,12,52,83,2.8);
            static readonly Geometry Glint = MakeGlint();
            internal static readonly Geometry HeartShape = HeartOutline.Shape;
            static readonly Brush Void = Frozen(Color.FromRgb(2,6,16));
            static readonly Pen Rim = Stroke(255,121,247,255,2.25);
            static readonly Rect HaloBounds=new Rect(-72,-60,144,132);
            static readonly BitmapSource Halo=MakeHalo();
            // Bake the two falloffs once. The black void can then be drawn over every leg.
            static BitmapSource MakeHalo()
            {
                const int resolution=3;
                var visual=new DrawingVisual();
                using(var dc=visual.RenderOpen())
                {
                    dc.PushTransform(new TranslateTransform(-HaloBounds.X,-HaloBounds.Y));
                    for(int width=52;width>=2;width--)
                    {
                        double band=Math.Exp(-width/12.0);
                        byte alpha=(byte)Math.Round(3+35*band);
                        dc.DrawGeometry(null,Stroke(alpha,30,(byte)(105+119*band),255,width),HeartShape);
                    }
                    dc.Pop();
                }
                var bmp=new RenderTargetBitmap((int)HaloBounds.Width*resolution,(int)HaloBounds.Height*resolution,
                    96*resolution,96*resolution,PixelFormats.Pbgra32);
                bmp.Render(visual);bmp.Freeze();return bmp;
            }
            static readonly Pen RimBloom=Stroke(152,49,167,255,7.2);
            static readonly Pen RimLight=Stroke(255,224,255,255,2.1);
            static void LitArc(DrawingContext dc,double from,double to,double light)
            {
                if(light<=0.003) return;
                var g=new StreamGeometry();
                using(var path=g.Open())
                {
                    path.BeginFigure(HeartOutline.At(from),false,false);
                    int steps=Math.Max(2,(int)(Math.Abs(to-from)*190));
                    for(int i=1;i<=steps;i++) path.LineTo(HeartOutline.At(from+(to-from)*i/steps),true,true);
                }
                dc.PushOpacity(light); dc.DrawGeometry(null,RimBloom,g); dc.DrawGeometry(null,RimLight,g); dc.Pop();
            }
            static void MeteorLight(DrawingContext dc,IdleMotion idle)
            {
                var f=idle.Flight; double t=idle.Age;
                double light=idle.Weight*IdleMotion.Beat(t-MeteorFlight.Pass,0.15,0.54,1.19);
                double radius=0.025+0.070*light;
                LitArc(dc,f.NearRim-radius,f.NearRim+radius,light);
                double slide=Math.Max(0,Math.Min(1,(t-MeteorFlight.RimTime)/(MeteorFlight.Dive-MeteorFlight.RimTime)));
                double contact=f.Entry+f.Arc*slide;
                LitArc(dc,contact-0.035,contact+0.035,0.72*idle.Weight*IdleMotion.Beat(t-0.98,0.16,0.42,0.64));
                double after=t-MeteorFlight.FlashTime-0.06;
                if(after<=0) return;
                double reach=Ease(after/0.53)*0.5;
                light=idle.Weight*IdleMotion.Beat(after,0.09,0.32,0.72);
                for(int sign=-1;sign<=1;sign+=2)
                    LitArc(dc,f.Entry+f.Arc+sign*Math.Max(0,reach-0.14),f.Entry+f.Arc+sign*reach,light);
            }
            static Point Infall(int ray,double u)
            {
                double r=5+(65+9*(ray%3))*(1-u*u*u);
                double angle=ray*2.40+0.62+0.74*u;
                return new Point(r*Math.Cos(angle),r*Math.Sin(angle));
            }
            static void PresenceLight(DrawingContext dc,double p,bool leaving,Vector heading)
            {
                if(!leaving && p<0.43)
                    for(int i=0;i<5;i++)
                    {
                        double u=(p-0.012-i*0.015)/0.35;
                        if(u<=0 || u>=1) continue;
                        Point head=Infall(i,u),tail=Infall(i,Math.Max(0,u-0.33));
                        Point mid=Infall(i,(u+Math.Max(0,u-0.33))*0.5);
                        Vector path=head-tail;
                        if(path.LengthSquared<0.01) continue;
                        var normal=new Vector(-path.Y,path.X); normal.Normalize();
                        var ribbon=new StreamGeometry();
                        using(var stroke=ribbon.Open())
                        {
                            stroke.BeginFigure(tail,true,true);
                            stroke.QuadraticBezierTo(mid+normal*1.5,head,true,true);
                            stroke.QuadraticBezierTo(mid-normal*0.9,tail,true,true);
                        }
                        dc.PushOpacity(0.90*IdleMotion.Beat(u,0.22,0.70,1));
                        dc.DrawGeometry(Blue,null,ribbon);
                        dc.DrawEllipse(Glow,null,head,4.2,4.2);
                        dc.DrawEllipse(Ice,null,head,1.1,1.1); dc.Pop();
                    }
                double gleam=leaving?IdleMotion.Beat(1-p-0.61,0.12,0.23,0.37)
                    :IdleMotion.Beat(p-0.10,0.13,0.20,0.30);
                if(gleam<=0) return;
                dc.PushOpacity(gleam);
                if(heading.LengthSquared>0) dc.PushTransform(new RotateTransform(Math.Atan2(heading.Y,heading.X)*180/Math.PI));
                dc.DrawEllipse(Glow,null,new Point(),54*gleam,5.5);
                dc.PushTransform(new ScaleTransform((leaving?72:55)*gleam,2.1));
                dc.DrawGeometry(Ice,null,Glint); dc.Pop();
                if(heading.LengthSquared>0) dc.Pop();
                dc.Pop();
            }
            static void PresenceRim(DrawingContext dc,double p,bool leaving)
            {
                double u=leaving?(1-p-0.12)/0.48:(p-0.28)/0.40;
                if(u<=0 || u>=1) return;
                double head=0.5*Ease(leaving?1-u:u),light=IdleMotion.Beat(u,0.17,0.76,1);
                double tail=leaving?Math.Min(0.5,head+0.12):Math.Max(0,head-0.12);
                LitArc(dc,tail,head,light); LitArc(dc,-tail,-head,light);
            }
            static void Stardust(DrawingContext dc,IdleMotion idle)
            {
                double glow=idle.Weight*IdleMotion.Beat(idle.Age,0.38,0.91,1.58);
                LitArc(dc,0,1,0.46*glow);
                const int count=12;
                for(int i=0;i<count;i++)
                {
                    // Each beat releases four grains a quarter of the perimeter apart.
                    int beat=i%3;
                    double u=(idle.Age-0.38-beat*0.16)/(1.8+0.2*beat);
                    if(u<=0 || u>=1) continue;
                    Point p=HeartOutline.At(i/(double)count);
                    Vector outward=p-new Point(0,4); outward.Normalize();
                    var tangent=new Vector(-outward.Y,outward.X);
                    double drift=8*u*Math.Sin(Math.PI*u)*Math.Sin(4*u+i*2.4+idle.Direction.X);
                    p+=outward*((57+4*beat)*u*(1.42-0.42*u))+tangent*drift;
                    dc.PushOpacity(idle.Weight*IdleMotion.Beat(u,0.10,0.46,1));
                    dc.DrawEllipse(Glow,null,p,7-beat*0.5,7-beat*0.5);
                    dc.DrawLine(Ring,p-outward*(4.8*(1-u)),p);
                    dc.DrawEllipse(Blue,null,p,2.0,1.7);
                    dc.DrawEllipse(Ice,null,p,1.35,1.05); dc.Pop();
                }
            }
            static void Constellation(DrawingContext dc,IdleMotion idle,Point heart)
            {
                var chart=idle.Chart; var stars=chart.Stars;
                // Allow room for the rotated pattern and its bloom even at the top of a hover.
                Point anchor=heart+idle.Direction*100;
                dc.PushTransform(new TranslateTransform(Math.Max(50,Math.Min(250,anchor.X)),Math.Max(50,Math.Min(250,anchor.Y))));
                dc.PushTransform(new RotateTransform(idle.ChartAngle));
                int last=stars.Length-1, lines=last+(chart.CloseTo>=0?1:0);
                double beat=1.48/lines;
                for(int i=0;i<lines;i++)
                {
                    int from=i==last?last:chart.Parent(i+1),to=i==last?chart.CloseTo:i+1;
                    double reach=Ease((idle.Age-0.12-i*beat)/beat);
                    double fade=Ease((idle.Age-2.08-0.48*Math.Min(from,to)/last)/0.72);
                    if(reach<=0 || fade>=1) continue;
                    Point p=stars[from],tip=p+(stars[to]-p)*reach;
                    dc.PushOpacity(idle.Weight*(1-fade));
                    dc.DrawLine(StarShadow,p,tip); dc.DrawLine(StarThread,p,tip); dc.Pop();
                }
                for(int i=0;i<=last;i++)
                {
                    double t=idle.Age-(i==0?0:0.06+i*beat);
                    double fade=Ease((idle.Age-2.08-0.48*i/last)/0.72);
                    double light=idle.Weight*Ease(t/0.17)*(1-fade);
                    if(light<=0) continue;
                    Point p=stars[i];
                    dc.PushOpacity(light);
                    double halo=7+4*IdleMotion.Beat(t,0.12,0.15,0.44);
                    dc.DrawEllipse(Glow,null,p,halo,halo);
                    dc.DrawEllipse(Blue,null,p,2.7,2.7);
                    dc.DrawEllipse(Ice,null,p,2.0,2.0);
                    dc.Pop();
                }
                dc.Pop(); dc.Pop();
            }
            static Pen Stroke(byte a,byte r,byte g,byte b,double width)
            {var p=new Pen(Frozen(Color.FromArgb(a,r,g,b)),width);p.LineJoin=PenLineJoin.Round;p.Freeze();return p;}
            static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
            static Geometry MakeGlint()
            {
                var g=Geometry.Parse("M 0,-1 Q 0.07,-0.1 0.85,0 Q 0.07,0.09 0,1 Q -0.07,0.1 -0.85,0 Q -0.07,-0.1 0,-1 Z");
                g.Freeze(); return g;
            }
            static RadialGradientBrush MakeGlow()
            {
                var b = new RadialGradientBrush();
                b.GradientStops.Add(new GradientStop(Color.FromArgb(144,78,209,255),0));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(50,25,145,235),0.48));
                b.GradientStops.Add(new GradientStop(Color.FromArgb(0,44,137,249),1));
                b.Freeze(); return b;
            }
            static Pen MakePen() { var p = new Pen(Ice,1.25); p.Freeze(); return p; }
            public Effects(PetArt pet, bool foreground)
            { this.pet = pet; this.foreground = foreground; IsHitTestVisible = false; }

            protected override void OnRender(DrawingContext dc)
            {
                if (ActualWidth < 1) return;
                double unit = ActualWidth/300, t = pet.Time;
                dc.PushTransform(new ScaleTransform(unit,unit));
                Point c = new Point(pet.Heart.X/unit,pet.Heart.Y/unit);
                if (!foreground)
                {
                    dc.PushOpacity(0.40+pet.Attention*0.25+pet.Gravity*0.3);
                    dc.DrawEllipse(Glow,null,c,118,105);
                    dc.Pop();
                    for (int i=0; i<13; i++)
                    {
                        double a = pet.poseTime*(0.19+i%3*0.06)+i*2.39996;
                        double r = 96+i%5*6;
                        var p = new Point(c.X+Math.Cos(a)*r,c.Y+Math.Sin(a)*r*0.7);
                        dc.PushOpacity(0.22+0.4*(0.5+0.5*Math.Sin(pet.poseTime*1.3+i)));
                        dc.DrawEllipse(i%2==0?Ice:Blue,null,p,i%3==0?1.5:0.9,i%3==0?1.5:0.9);
                        dc.Pop();
                    }
                }
                else
                {
                    if(pet.Idle.Act==IdleAct.Constellation) Constellation(dc,pet.Idle,c);
                    dc.PushTransform(new TranslateTransform(c.X,c.Y));
                    if(pet.presence<1) PresenceLight(dc,pet.presence,pet.departing,pet.BlinkHeading);
                    dc.PushTransform(new RotateTransform(pet.CoreTilt));
                    dc.PushTransform(new ScaleTransform(pet.CoreSize*pet.coreAspect,pet.CoreSize));
                    dc.PushOpacity(Math.Min(1,pet.CoreLight));
                    dc.DrawImage(Halo,HaloBounds);
                    dc.Pop();
                    dc.DrawGeometry(Void,null,HeartShape);
                    double flash=pet.Idle.Flash;
                    dc.PushOpacity(Math.Min(0.95,0.26+0.35*pet.CoreLight+0.30*flash));
                    dc.PushClip(HeartShape);dc.DrawImage(Halo,HaloBounds);dc.Pop();dc.Pop();
                    double closed=Ease(pet.Seal);
                    dc.PushOpacity(1-0.72*closed); dc.DrawGeometry(null,Rim,HeartShape); dc.Pop();
                    if(pet.presence<1) PresenceRim(dc,pet.presence,pet.departing);
                    if(pet.Idle.Act==IdleAct.Meteor) MeteorLight(dc,pet.Idle);
                    if(pet.Idle.Act==IdleAct.Stardust) Stardust(dc,pet.Idle);
                    if(pet.CoreLight>0.82)
                    {
                        dc.PushOpacity(Math.Min(0.76,pet.CoreLight-0.82));
                        dc.DrawEllipse(Glow,null,new Point(),29,23);dc.Pop();
                    }
                    double breath=0.5+0.5*Math.Sin(pet.poseTime*(1.8+pet.Gravity));
                    double shine=Math.Min(1,pet.CoreLight+0.27*pet.Gravity);
                    double size=4+shine*shine*(9+3*breath)+16*flash;
                    if(pet.presence<0.56)
                        size+=(3+13*IdleMotion.Beat(pet.presence,0.15,0.21,0.56))*(1-Ease(pet.presence/0.56))/Math.Max(0.04,pet.CoreSize);
                    dc.PushTransform(new TranslateTransform(pet.starOffset.X,pet.starOffset.Y));
                    if(flash>0) { dc.PushOpacity(flash); dc.DrawEllipse(Glow,null,new Point(),33,30); dc.Pop(); }
                    dc.PushOpacity(Math.Min(1,0.65+shine*0.35+flash*0.35)*(1-0.40*closed));
                    dc.PushTransform(new ScaleTransform(size,size));
                    dc.DrawGeometry(Ice,null,Glint);
                    if(flash>0) { dc.PushOpacity(0.88*flash); dc.DrawGeometry(Brushes.White,null,Glint); dc.Pop(); }
                    dc.Pop(); dc.Pop(); dc.Pop();
                    dc.Pop(); dc.Pop(); dc.Pop();
                    if(pet.Gravity>0.015)
                    {
                        for(int i=0;i<2;i++)
                        {
                            double phase=(t*(0.58+0.25*pet.HoverWeight)+0.5*i)%1;
                            double r=25+(1-phase)*(45+32*pet.HoverWeight);
                            dc.PushOpacity(pet.Gravity*0.52*Math.Sin(Math.PI*phase));
                            dc.DrawEllipse(null,Ring,c,r,r);dc.Pop();
                        }
                    }
                    double waveAge=pet.AmbientAge-AmbientWindup;
                    if(waveAge>=0 && waveAge<1.45)
                    {
                        double u=waveAge/1.45,r=31+94*(1-Math.Pow(1-u,1.6));
                        dc.PushOpacity(0.88*(1-Ease((u-0.32)/0.68)));
                        dc.DrawEllipse(null,SmallWaveGlow,c,r,r);
                        dc.DrawEllipse(null,Ring,c,r,r);dc.Pop();
                    }
                    if(pet.MealPhase<1.2)
                    {
                        double u=Math.Min(1,pet.MealPhase/0.43);
                        dc.PushOpacity(1-u);
                        for(int i=0;i<15+(int)(20*pet.MealWeight);i++)
                        {
                            double a=i*2.39996+u*2.7, r=(1-u)*(64+6*(i%5)+23*pet.MealWeight);
                            double x=c.X+r*Math.Cos(a), y=c.Y+0.82*r*Math.Sin(a);
                            dc.PushTransform(new TranslateTransform(x,y));
                            dc.PushTransform(new RotateTransform(a*180/Math.PI));
                            dc.DrawRectangle(i%3==0?Ice:Blue,null,new Rect(-2,-1,4,2));
                            dc.Pop();dc.Pop();
                        }
                        dc.Pop();
                        for(int i=0;i<(pet.MealWeight>0.45?2:1);i++)
                        {
                            double echo=(pet.MealPhase-0.25-i*0.16)/0.79;
                            if(echo<0 || echo>=1) continue;
                            double r=16+(52+56*pet.MealWeight)*Ease(echo);
                            dc.PushOpacity(0.85*(1-echo));dc.DrawEllipse(null,Ring,c,r,r*0.86);dc.Pop();
                        }
                    }
                    if(pet.EjectAge>=EjectBeat && pet.EjectAge<EjectBeat+0.62)
                    {
                        double u=(pet.EjectAge-EjectBeat)/0.62;
                        double r=23+42*Ease(u); dc.PushOpacity(0.8*(1-u));
                        dc.DrawEllipse(null,Ring,c,r,r); dc.Pop();
                    }
                    double strength = Math.Max(pet.Gravity,Math.Max(0,1-pet.MealAge/1.8));
                    if (strength>0.01)
                        for (int i=0; i<31; i++)
                        {
                            double phase = (t*(0.65+0.05*(i%4))+i*0.61803)%1;
                            double a = i*2.39996+phase*2.3;
                            double r = 6+(1-phase)*123;
                            var p = new Point(c.X+Math.Cos(a)*r,c.Y+Math.Sin(a)*r*0.85);
                            var tail = new Point(c.X+Math.Cos(a-0.10)*(r+4),c.Y+Math.Sin(a-0.10)*(r+4)*0.85);
                            dc.PushOpacity(strength*Math.Sin(phase*Math.PI));
                            dc.DrawLine(Ring,tail,p);
                            dc.DrawEllipse(Ice,null,p,1.45,1.45);
                            dc.Pop();
                        }
                    if (pet.FocusProgress>=0)
                    {
                        var center = new Point(150,265);
                        dc.PushOpacity(0.42);
                        dc.DrawEllipse(null,Ring,center,60,6); dc.Pop();
                        double a=pet.FocusProgress*2*Math.PI-Math.PI/2;
                        dc.DrawEllipse(Ice,null,new Point(150+60*Math.Cos(a),265+6*Math.Sin(a)),3,3);
                    }
                }
                dc.Pop();
            }
        }
    }
}
