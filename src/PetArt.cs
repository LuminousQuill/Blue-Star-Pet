using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace BlueStar
{
    sealed partial class PetArt : Grid
    {
        public const double ReleaseBeat=1.35;
        public const double EjectBeat=0.38;
        public int ChargeLevel;
        public bool LiquidCore;
        public double GrandAge=double.PositiveInfinity;
        readonly MeshGeometry3D mesh=new MeshGeometry3D();
        Point3D[] positions;
        Point3DCollection sparePositions;
        LegRig[] legs;
        GripPose grip;
        Point? gripPointer;
        Point gripSnap;
        double lastGripFrame;
        readonly Viewport3D legView;
        readonly Material legMaterial;
        LegOverflow overflow;
        public Point ContactPoint;
        public Vector ContactNormal;
        public Vector ContactSpan=new Vector(double.NegativeInfinity,double.PositiveInfinity);
        public double ContactProgress=-1,ContactFlashAt=double.NegativeInfinity;
        readonly byte[] atlasAlpha;
        readonly int atlasWidth,atlasHeight;
        readonly Effects behind,front;
        public double Time { get; set; }
        public double AimX,AimY,Attention,Gravity,PulseAge=10,MealAge=10,EjectAge=10,Seal;
        public double HoverWeight,MealWeight,AmbientAge=10;
        public const double AmbientWindup=0.95;
        double mealStarted=double.NegativeInfinity,mealFrom,shellExtra,previousGravity;
        double twitchStarted=double.NegativeInfinity,twitchAmplitude,twitchDuration,twitchFrom,twitchEchoEnd;
        bool hideSpentDust;
        public static double MealImpactEnd(double weight) { return 0.48+0.36*weight; }

        public void StartHoverTwitch(double now) { StartCoreTwitch(now,0.105,0.13,0.92); }
        void StartCoreTwitch(double now,double amplitude,double duration,double echoEnd=0.42)
        {
            if(now<twitchStarted) return;
            twitchFrom=CoreTwitch(now); twitchStarted=now;
            twitchAmplitude=Math.Max(amplitude,twitchFrom); twitchDuration=1.20*duration;
            twitchEchoEnd=echoEnd;
        }
        double CoreTwitch(double now)
        {
            double age=now-twitchStarted;
            const double echoStart=0.88;
            if(age>=(echoStart+twitchEchoEnd)*twitchDuration) return 0;
            if(age<=0) return twitchFrom;
            double u=age/twitchDuration;
            // Begin at the visible scale on a quick release. Let go before the main swell crests.
            double swell=twitchFrom+(twitchAmplitude-twitchFrom)*Ease(u/0.32);
            double echo=0.34*twitchAmplitude*IdleMotion.Beat(u-echoStart,0.13,0.16,twitchEchoEnd);
            return swell*(1-Ease((u-0.68)/0.16))+echo;
        }

        double MealShellExtra(double hoverSize)
        {
            double end=MealImpactEnd(MealWeight);
            bool playing=MealAge>=0 && MealAge<end;
            double started=Time-MealAge;
            if(playing && started>mealStarted+0.000001)
            {
                // Start at the last displayed size, even if another pulse is still returning.
                mealStarted=started; mealFrom=shellExtra; hideSpentDust=true;
                StartCoreTwitch(started,0.11+0.055*MealWeight,0.14+0.085*MealWeight);
                // The crossed crown legs and the planted guards retain their heart contacts.
                for(int i=legLayout==2?2:0;i<legs.Length;i++)
                    if(!legs[i].SupportsHeartTip && !legs[i].TuckedToe.HasValue)
                        legs[i].StartMeal(started,MealWeight,end);
            }
            else if(Gravity>previousGravity || Gravity<=0.001) hideSpentDust=false;
            previousGravity=Gravity;
            double hover=hideSpentDust?0:hoverSize;
            if(!playing) return shellExtra=hover;

            double rise=0.14+0.085*MealWeight;
            double u=Math.Min(1,MealAge/rise);
            double amplitude=0.18+0.34*MealWeight;
            // A second hit has room to grow without stacking the shell size indefinitely.
            double extra=mealFrom+(1-mealFrom)*amplitude*u*(2-u);
            double back=Ease((MealAge-rise)/(end-rise));
            extra*=(1-back)*(1-1.45*back);
            return shellExtra=extra+(1-extra)*hover;
        }
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
                guardView.Opacity=legView.Opacity=departing?Ease((value-0.31)/0.36):Ease((value-0.25)/0.34);
            }
        }
        double coreAspect=1;
        public readonly IdleMotion Idle=new IdleMotion();
        public readonly DragWake Wake=new DragWake();
        public readonly RoamStroke Swim=new RoamStroke();
        public Vector BlinkHeading;
        Vector starOffset;
        double chartFocus,chartReturn;
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
            BuildLegs(1);
            var atlas=LoadTexture("leg-atlas.png",576);
            atlasWidth=atlas.PixelWidth; atlasHeight=atlas.PixelHeight;
            var rgba=new FormatConvertedBitmap(atlas,PixelFormats.Bgra32,null,0);
            atlasAlpha=new byte[atlasWidth*atlasHeight];
            var pixels=new byte[atlasAlpha.Length*4]; rgba.CopyPixels(pixels,atlasWidth*4,0);
            for(int i=0;i<atlasAlpha.Length;i++) atlasAlpha[i]=pixels[4*i+3];
            legMaterial=new DiffuseMaterial(new ImageBrush(atlas));
            legMaterial.Freeze();
            behind=new Effects(this,false);front=new Effects(this,true);
            legView=LegLayer(mesh,legMaterial);
            Children.Add(behind);Children.Add(legView);Children.Add(front);
            guardView=LegLayer(guardMesh,legMaterial);
            guardView.Visibility=Visibility.Hidden; Children.Add(guardView);
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
        public void Draw(bool movingWindow=false)
        {
            double age=PulseAge,charge=0,fold=0,flare=0;
            bool grand=GrandAge<GrandWave.Release+1.45;
            double draw=0,pinch=0,strike=0;
            if(grand)
            {
                age=GrandAge<GrandWave.Release?GrandAge*ReleaseBeat/GrandWave.Release:ReleaseBeat+GrandAge-GrandWave.Release;
                draw=IdleMotion.Beat(GrandAge,0.77,1.48,GrandWave.Release);
                pinch=IdleMotion.Beat(GrandAge-1.40,0.80,0.91,1.03);
                strike=IdleMotion.Beat(GrandAge-GrandWave.Release,0.12,0.22,0.89);
            }
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
            double drawIn=Gravity*(0.65+0.35*Math.Sin(Time*(3+HoverWeight)));
            double sinceWhisper=AmbientAge-AmbientWindup;
            double gather=sinceWhisper<0?Ease(AmbientAge/AmbientWindup):1-Ease(sinceWhisper/0.14);
            double unfurl=Ease(sinceWhisper/0.14)*(1-Ease((sinceWhisper-0.14)/0.57));
            double tuck=Ease(EjectAge/EjectBeat)*(1-Ease((EjectAge-EjectBeat)/0.13));
            double expel=Ease((EjectAge-EjectBeat)/0.13)*(1-Ease((EjectAge-EjectBeat-0.13)/0.62));
            double flash=FocusProgress>=0?0.5-0.5*Math.Cos(Time*2*Math.PI/1.8):0;
            double focusLight=FocusProgress>=0?0.90*flash-0.28:0;
            double closed=Ease(Seal);
            double resonanceTuck=Math.Max(0,Math.Min(1,fold+gather*0.42+pinch*0.48));
            double footTuck=Math.Max(closed,resonanceTuck);
            fold+=0.93*closed+tuck*0.45-expel*0.28;
            fold+=drawIn*(0.09+0.12*HoverWeight)+gather*0.42-unfurl*0.19;
            double drift=(1-0.82*Idle.Still)*(1-0.50*grip.Effort);
            fold+=0.48*pinch;
            double breath=1+0.009*drift*Math.Sin(Time*2.1);
            double tilt=drift*0.034*Math.Sin(Time*0.94)+AimX*0.14+DragLean;
            Vector glide=Idle.GlideAt(Idle.Age);
            double cx=drift*2.4*Math.Sin(Time*0.83)+AimX*11+glide.X;
            double cy=-12+drift*(5.7*Math.Sin(Time*1.34)+1.2*Math.Sin(Time*2.27))+AimY*8+Idle.DepthAt(Idle.Age)+glide.Y;
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
            starOffset+=Idle.Direction*(8*Idle.FanGaze);
            grip.Step(new Vector(cx,cy),Math.Max(0,Math.Min(0.13,Time-lastGripFrame)));
            lastGripFrame=Time;
            Vector braceShift=grip.Recoil*(7.6+1.6*Math.Sin(Time*5.2));
            cx+=braceShift.X; cy+=braceShift.Y;
            starOffset+=grip.Recoil*3.4;
            Vector sag=Idle.SagAt(Idle.Age);
            Vector sagRoot=sag*0.90;
            double bank=Idle.BankAt(Idle.Age-0.18);
            Vector coreShift=sag+Idle.Direction*(9*Idle.FanLean);
            coreShift.Y-=3*draw+5*pinch+6*strike;
            Vector heartOffset=coreShift+new Vector(AimX*5+5*bank+2.2*lead.X,AimY*4+2.2*lead.Y);
            coreAspect=(1-0.17*bank*bank)*(1+0.08*lead.Y);
            Heart=new Point((0.5+(cx+heartOffset.X)/300)*ActualWidth,(0.5+(cy+heartOffset.Y)/300)*ActualHeight);
            double heartbeat=0.5-0.5*Math.Cos(2*Math.PI*age*2.0);
            CoreSize=breath+charge*(0.15+0.15*heartbeat)+0.29*flare+MealShellExtra(drawIn*(0.10+0.19*HoverWeight))
                -gather*0.17+unfurl*0.23-tuck*0.22+expel*0.34;
            if(legLayout==2) CoreSize*=0.80;
            CoreSize*=1-0.042*grip.Effort;
            CoreSize*=1+0.18*draw-0.57*pinch+0.23*strike;
            coreAspect*=1+0.08*draw-0.19*pinch+0.10*strike;
            if(Idle.Act==IdleAct.CoreSag)
            {
                double compression=Idle.Weight*(0.08*IdleMotion.Beat(Idle.Age-0.69,0.12,0.17,0.49)
                    -0.045*IdleMotion.Beat(Idle.Age-0.35,0.24,0.27,0.39));
                CoreSize*=1-compression; coreAspect*=1+2*compression;
            }
            CoreLight=Math.Min(1.85,0.58+0.06*Math.Sin(Time*2.2)+Attention*0.54+drawIn*(0.29+0.26*HoverWeight)
                +charge*(0.42+0.34*heartbeat)+0.71*flare+gather*0.40+unfurl*0.58+expel*0.72+focusLight)*(1-0.73*closed);
            if(presence<1)
            {
                double form=departing?Ease((presence-0.25)/0.23):Ease((presence-0.30)/0.19);
                double entrySwell=departing?brace:impact;
                double recoil=departing?0:IdleMotion.Beat(presence-0.58,0.10,0.12,0.32);
                CoreSize*=0.075+0.925*form+0.25*entrySwell-0.075*recoil;
                coreAspect*=0.46+0.54*form+0.09*entrySwell;
                CoreLight+=0.44*(1-form)+0.94*entrySwell;
            }
            CoreLight+=0.75*pinch+0.48*strike;
            // The resting legs keep their own sway while a gust slowly tilts the core.
            CoreTilt=(tilt+Idle.CoreAngle(AimX*0.14)+0.095*bank+coreShift.X*0.012+grip.Recoil.X*0.09)*180/Math.PI;
            chartFocus=chartReturn=0;
            if(Idle.Act==IdleAct.Constellation)
            {
                chartFocus=Idle.Weight*IdleMotion.Beat(Idle.Age,0.29,1.82,2.70);
                chartReturn=Idle.Weight*IdleMotion.Beat(Idle.Age-2.30,0.35,0.38,0.87);
                CoreTilt+=3.5*Idle.Direction.X*chartFocus;
                CoreSize*=1-0.055*chartFocus+0.038*chartReturn;
            }
            // This skin insertion uses the same local frame as the painted heart.
            var lobeTransform=Matrix.Identity;
            lobeTransform.Scale(coreAspect*CoreSize,CoreSize);lobeTransform.Rotate(CoreTilt);
            Vector rightLobe=lobeTransform.Transform(new Vector(30.75,-16));
            for(int i=0;i<legs.Length;i++) legs[i].Pose(positions,i*LegRig.VertexCount,Time,Time,fold,tilt,DragLift,breath,cx,cy,AimX,Idle,Wake,glide,presence,departing,Swim,sagRoot,GrandAge-GrandWave.Release,heartOffset,footTuck,DragLean,rightLobe);
            if(grand && GrandAge>GrandWave.CallTime && GrandAge<GrandWave.Release)
                BendGrandLens(cx/300,-cy/300,GrandAge-GrandWave.CallTime);
            if(legLayout==2) legs[0].SettleKneeCrease(positions,0);
            // Keep the frozen submission while the layered window is moving.
            // Otherwise update the detached buffer and swap it in once for both layers.
            var points=sparePositions;
            if(movingWindow || points==null || points.Count!=positions.Length)
                points=new Point3DCollection(positions);
            else
                for(int i=0;i<positions.Length;i++) points[i]=positions[i];
            if(movingWindow) points.Freeze();
            else
            {
                var previous=mesh.Positions;
                sparePositions=previous.IsFrozen?null:previous;
            }
            mesh.Positions=points;
            UpdateGuardDepth(new Vector(cx,cy)+heartOffset,points);
            DrawOverflow();
            behind.InvalidateVisual();front.InvalidateVisual();
        }

        void DrawOverflow()
        {
            // The original viewport remains the interaction surface. Only protruding skin
            // is drawn on the mouse-transparent surround, using this frame's same mesh.
            // Both layers use the same positions. Bounds include the full vertex buffer.
            var b=mesh.Bounds;
            double extent=Math.Max(Math.Max(-b.X,b.X+b.SizeX),Math.Max(-b.Y,b.Y+b.SizeY));
            if(extent<=0.5 || legView.Opacity<=0 || !IsVisible)
            {
                if(overflow!=null && overflow.IsVisible) overflow.Hide();
                return;
            }
            if(overflow==null)
            {
                var owner=Window.GetWindow(this);
                if(owner==null || !owner.IsVisible) return;
                overflow=new LegOverflow(owner,LegLayer(mesh,legMaterial),LegLayer(guardMesh,legMaterial));
            }
            overflow.Follow(b,legView.Opacity);
        }

        public bool InsideHeart(Point point)
        {
            double scale=ActualWidth/300*CoreSize;
            if(scale<=0) return false;
            Vector local=(point-Heart)/scale;
            var inverse=Matrix.Identity; inverse.Rotate(-CoreTilt);
            local=inverse.Transform(local); local.X/=coreAspect;
            return Effects.HeartShape.FillContains((Point)local) && (!guardRaised || HitLegMesh(point,guardView,guardMesh)<0);
        }
        public int LegAt(Point point)
        {
            int top=guardRaised?HitLegMesh(point,guardView,guardMesh):-1;
            return top>=0?top:HitLegMesh(point,legView,mesh);
        }
        int HitLegMesh(Point point,Viewport3D view,MeshGeometry3D skin)
        {
            int frontmost=-1;
            double nearest=double.PositiveInfinity;
            VisualTreeHelper.HitTest(view,null,delegate(HitTestResult hit) {
                var ray=hit as RayMeshGeometry3DHitTestResult;
                if(ray!=null && ray.MeshHit==skin)
                {
                    var uv=skin.TextureCoordinates;
                    Point a=uv[ray.VertexIndex1],b=uv[ray.VertexIndex2],c=uv[ray.VertexIndex3];
                    Point tex=a+(b-a)*ray.VertexWeight2+(c-a)*ray.VertexWeight3;
                    int x=Math.Max(0,Math.Min(atlasWidth-1,(int)(tex.X*atlasWidth)));
                    int y=Math.Max(0,Math.Min(atlasHeight-1,(int)(tex.Y*atlasHeight)));
                    if(ray.DistanceToRayOrigin<nearest && atlasAlpha[y*atlasWidth+x]>=32)
                    {
                        nearest=ray.DistanceToRayOrigin;
                        frontmost=ray.VertexIndex1/LegRig.VertexCount;
                    }
                }
                return HitTestResultBehavior.Continue;
            },new PointHitTestParameters(point));
            return frontmost;
        }
        public bool TryGripPoint(Point local,out Point anchor)
        {
            anchor=local;
            double unit=ActualWidth/300;
            if(unit<=0 || InsideHeart(local) || local.X<0 || local.Y<0 || local.X>ActualWidth || local.Y>ActualHeight) return false;
            if(grip.Previewing && gripPointer.HasValue && (local-gripPointer.Value).Length<=12*unit)
            { anchor=gripSnap; return true; }
            if(LegAt(local)<0)
            {
                // The dragged star has a visible footprint. Its rim may touch a narrow
                // ankle even when the cursor itself is over a gap between the legs.
                bool touching=false;
                for(int i=0;i<8 && !touching;i++)
                {
                    double angle=i*Math.PI/4;
                    Vector direction=new Vector(Math.Cos(angle),Math.Sin(angle));
                    touching=LegAt(local+direction*(7*unit))>=0 || LegAt(local+direction*(14*unit))>=0;
                }
                if(!touching) return false;
            }
            // The drop may land on an inner thigh. Keep the resulting window edge
            // outside the heart, independently of where the star touched that leg.
            Vector ray=local-Heart;
            if(ray.LengthSquared<0.001) return false;
            anchor=Heart+ray*(Math.Max(77*unit,Math.Min(ray.Length,101*unit))/ray.Length);
            gripPointer=local; gripSnap=anchor; return true;
        }
        public void SetGrip(Point localAnchor,Vector outward,double load,bool preview,Vector? edgeSpan=null)
        {
            if(ActualWidth<=0 || outward.LengthSquared<0.0001) { ClearGrip(); return; }
            outward.Normalize();
            grip.Set((localAnchor-new Point(ActualWidth/2,ActualHeight/2))*(300/ActualWidth),outward,load,preview,
                edgeSpan??new Vector(double.NegativeInfinity,double.PositiveInfinity));
        }
        public void ClearGrip() { grip.Clear(); gripPointer=null; }
        public bool TryWindowContact(Rect frame,out Point touch,out Vector inward,bool holding=false)
        {
            touch=new Point(); inward=new Vector();
            if(frame.IsEmpty || frame.Contains(Heart) || ActualWidth<=0) return false;
            // A resting toe still sways a few pixels while the user holds the window still.
            double nearest=Math.Pow(Math.Max(12,ActualWidth*0.065)+(holding?ActualWidth*0.022:0),2);
            for(int i=0;i<legs.Length;i++)
            {
                var model=legs[i].ToeOnMesh(positions,i*LegRig.VertexCount);
                var toe=new Point((0.5+model.X)*ActualWidth,(0.5-model.Y)*ActualHeight);
                double x=Math.Max(frame.Left,Math.Min(frame.Right,toe.X)), y=Math.Max(frame.Top,Math.Min(frame.Bottom,toe.Y));
                if(Heart.X<frame.Left) CompareToe(toe,new Point(frame.Left+5,y),new Vector(1,0),ref nearest,ref touch,ref inward);
                else if(Heart.X>frame.Right) CompareToe(toe,new Point(frame.Right-5,y),new Vector(-1,0),ref nearest,ref touch,ref inward);
                if(Heart.Y<frame.Top) CompareToe(toe,new Point(x,frame.Top+5),new Vector(0,1),ref nearest,ref touch,ref inward);
                else if(Heart.Y>frame.Bottom) CompareToe(toe,new Point(x,frame.Bottom-5),new Vector(0,-1),ref nearest,ref touch,ref inward);
            }
            return inward.LengthSquared>0;
        }
        static void CompareToe(Point toe,Point edge,Vector normal,ref double nearest,ref Point touch,ref Vector inward)
        {
            double distance=(toe-edge).LengthSquared;
            if(distance>nearest) return;
            nearest=distance; touch=edge; inward=normal;
        }
        public void BendLeg(int index,double time) { legs[index].Bend(time); }


        sealed partial class Effects : FrameworkElement
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
            static readonly Pen ContactRim=Stroke(244,165,246,255,1.7);
            static readonly Pen ContactMist=Stroke(85,52,179,232,4);
            static readonly Geometry ContactRail=Rail(0,1,double.PositiveInfinity);
            static readonly Geometry Glint = MakeGlint();
            internal static readonly Geometry HeartShape = HeartOutline.Shape;
            static readonly Brush Void = Frozen(Color.FromRgb(2,6,16));
            static readonly Pen Rim = Stroke(255,121,247,255,2.25);
            static readonly Geometry ChartRim=MakeChartRim();
            static readonly Brush ChartSilver=MakeChartSilver();
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
            static readonly Pen CrossingBand=Stroke(235,99,223,255,4.8);
            static void LitArc(DrawingContext dc,double from,double to,double light,Pen band=null)
            {
                if(light<=0.003) return;
                var g=new StreamGeometry();
                using(var path=g.Open())
                {
                    path.BeginFigure(HeartOutline.At(from),false,false);
                    int steps=Math.Max(2,(int)(Math.Abs(to-from)*190));
                    for(int i=1;i<=steps;i++) path.LineTo(HeartOutline.At(from+(to-from)*i/steps),true,true);
                }
                dc.PushOpacity(light); dc.DrawGeometry(null,RimBloom,g);
                if(band!=null) dc.DrawGeometry(null,band,g);
                dc.DrawGeometry(null,RimLight,g); dc.Pop();
            }
            static void MeteorLight(DrawingContext dc,IdleMotion idle)
            {
                var f=idle.Flight; double t=idle.Age;
                var primary=f.Tracks[0];
                double skim=t-primary.RimTime;
                double light=0.43*idle.Weight*IdleMotion.Beat(skim+0.16,0.12,0.18,0.29);
                if(light<=0.003) return;
                double contact=HeartOutline.Facing((Vector)primary.Sample(Math.Min(t,primary.Impact-0.032)));
                LitArc(dc,contact-0.032,contact+0.032,light);
            }
            static double CrossingLight(DrawingContext dc,IdleMotion idle)
            {
                // Fraction zero is the notch. The two passes stop on opposite banks of it.
                bool arriving=idle.Age<1.27;
                double t=arriving?idle.Age:idle.Age-1.30;
                double light=idle.Weight*(arriving?IdleMotion.Beat(t,0.20,1.01,1.13):IdleMotion.Beat(t,0.09,0.90,1.27));
                if(light<0.002) return 0;
                double sign=Math.Sign(idle.Direction.X)*(arriving?1:-1);
                double u=Ease((t-(arriving?0.15:0.04))/(arriving?0.76:0.90));
                double center=arriving?0.37-0.358*u:0.012+0.40*u;
                double trail=arriving?0.145*(1-0.74*u):-0.16*u;
                Point head=HeartOutline.At(sign*center);
                LitArc(dc,sign*Math.Min(0.48,center+trail),sign*center,light,CrossingBand);
                dc.PushOpacity(light);
                dc.DrawEllipse(Glow,null,head,20,20);
                dc.PushClip(HeartShape);
                Point wash=new Point(head.X*0.86,head.Y*0.89);
                dc.DrawEllipse(Glow,null,wash,23,17);
                dc.Pop();
                dc.DrawEllipse(Ice,null,head,4.0,4.0);
                dc.DrawEllipse(Brushes.White,null,head,2.5,2.5); dc.Pop();
                return -sign*light;
            }
            static Geometry SlippingGlint(IdleMotion idle)
            {
                double t=idle.Age,angle=Math.Sign(idle.Direction.X)*idle.Weight*Math.PI/180;
                double twist=IdleMotion.Beat(t,0.76,1.19,1.69);
                double major=angle*54*twist;
                double minor=angle*(46*IdleMotion.Beat(t-0.26,0.67,1.20,1.55)-24*IdleMotion.Beat(t-1.66,0.24,0.35,0.68));
                double reach=1+0.53*idle.Weight*twist;
                var path=new StreamGeometry();
                using(var ctx=path.Open())
                {
                    Point top=new Point(reach*Math.Sin(major),-reach*Math.Cos(major));
                    Point right=new Point(0.85*Math.Cos(minor),0.85*Math.Sin(minor));
                    Point bottom=new Point(-top.X,-top.Y),left=new Point(-right.X,-right.Y);
                    ctx.BeginFigure(top,true,true);
                    foreach(Point tip in new[]{right,bottom,left,top})
                    {
                        Point control=new Point((top.X+tip.X)*0.09,(top.Y+tip.Y)*0.09);
                        ctx.QuadraticBezierTo(control,tip,true,true); top=tip;
                    }
                }
                return path;
            }
            static readonly Brush LeftGlint=GlintWash(0),RightGlint=GlintWash(180);
            static Brush GlintWash(double angle)
            {
                var wash=new LinearGradientBrush(Colors.White,Color.FromArgb(0,255,255,255),angle);
                wash.Freeze(); return wash;
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
            static Point ChartAnchor(IdleMotion idle,Point heart)
            {
                // Allow room for the rotated pattern and its bloom even at the top of a hover.
                Point anchor=heart+idle.Direction*100;
                return new Point(Math.Max(50,Math.Min(250,anchor.X)),Math.Max(50,Math.Min(250,anchor.Y)));
            }
            static Geometry MakeChartRim()
            {
                var outline=HeartShape.GetWidenedPathGeometry(Rim,0.08,ToleranceType.Absolute);
                outline.Freeze(); return outline;
            }
            static Brush MakeChartSilver()
            {
                var wash=new RadialGradientBrush();
                wash.GradientStops.Add(new GradientStop(Colors.White,0));
                wash.GradientStops.Add(new GradientStop(Color.FromArgb(230,180,246,255),0.42));
                wash.GradientStops.Add(new GradientStop(Color.FromArgb(0,88,209,255),1));
                wash.Freeze(); return wash;
            }
            static void DrawChart(DrawingContext dc,StarChart chart,double age,double weight,Pen thread)
            {
                var stars=chart.Stars;
                int last=stars.Length-1;
                double stroke=chart.Stroke(age);
                for(int i=0;i<chart.Lines;i++)
                {
                    int from=chart.From(i),to=chart.To(i);
                    double reach=Ease(stroke-i);
                    double fade=Ease((age-2.08-0.48*Math.Min(from,to)/last)/0.72);
                    if(reach<=0 || fade>=1) continue;
                    Point p=stars[from],tip=p+(stars[to]-p)*reach;
                    dc.PushOpacity(weight*(1-fade));
                    dc.DrawLine(StarShadow,p,tip); dc.DrawLine(thread,p,tip); dc.Pop();
                }
                for(int i=0;i<=last;i++)
                {
                    double t=age-(i==0?0:0.06+i*chart.Beat);
                    double fade=Ease((age-2.08-0.48*i/last)/0.72);
                    double light=weight*Ease(t/0.17)*(1-fade);
                    if(light<=0) continue;
                    Point p=stars[i];
                    dc.PushOpacity(light);
                    double halo=7+4*IdleMotion.Beat(t,0.12,0.15,0.44);
                    dc.DrawEllipse(Glow,null,p,halo,halo);
                    dc.DrawEllipse(Blue,null,p,2.7,2.7);
                    dc.DrawEllipse(Ice,null,p,2.0,2.0);
                    dc.Pop();
                }
            }
            Vector ChartInHeart(DrawingContext dc,Point heart)
            {
                var idle=pet.Idle;
                double t=idle.Age,echo=t-0.10;
                var rotation=Matrix.Identity; rotation.Rotate(idle.ChartAngle);
                Vector toward=ChartAnchor(idle,heart)-heart+(Vector)rotation.Transform(idle.Chart.PenAt(t));
                var local=Matrix.Identity; local.Rotate(-pet.CoreTilt);
                toward=local.Transform(toward); toward.X/=pet.coreAspect;
                Point rim=HeartOutline.At(HeartOutline.Facing(toward));
                double spread=16+26*Ease((t-1.65)/0.68);
                dc.PushOpacity(idle.Weight*IdleMotion.Beat(t,0.30,1.86,2.65));
                dc.DrawEllipse(Glow,null,rim,spread*0.65,spread*0.65);
                dc.PushClip(ChartRim); dc.DrawEllipse(ChartSilver,null,rim,spread,spread);
                dc.Pop(); dc.Pop();

                double gather=1-Ease((t-2.02)/0.64);
                if(gather<=0) return new Vector();
                var imprint=Matrix.Identity;
                imprint.Scale(0.56*gather,0.56*gather);
                imprint.Rotate(idle.ChartAngle-pet.CoreTilt);
                imprint.Translate(0,8.5*gather);
                dc.PushClip(HeartShape); dc.PushTransform(new MatrixTransform(imprint));
                DrawChart(dc,idle.Chart,echo,0.88*pet.chartFocus,RimLight);
                dc.Pop(); dc.Pop();
                return (Vector)imprint.Transform(idle.Chart.PenAt(echo))*(idle.Weight*Ease(t/0.29));
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
                bool gathering=pet.GrandAge<GrandWave.Release;
                int orbitLevel=gathering?3:pet.ChargeLevel;
                if(orbitLevel>=2)
                {
                    dc.PushOpacity(pet.presence*(1-0.65*Ease(pet.Seal)));
                    DrawChargeDust(dc,c,orbitLevel,pet.Time,gathering?Ease(pet.GrandAge/GrandWave.Release):0,foreground);
                    dc.Pop();
                }
                if (!foreground)
                {
                    dc.PushOpacity(0.40+pet.Attention*0.25+pet.Gravity*0.3);
                    dc.DrawEllipse(Glow,null,c,118,105);
                    dc.Pop();
                    int motes=orbitLevel<2?13:0;
                    double returnLight=Ease((pet.GrandAge-GrandWave.Release)/0.65);
                    for (int i=0; i<motes; i++)
                    {
                        double a = pet.Time*(0.19+i%3*0.06)+i*2.39996;
                        double r = 96+i%5*6;
                        var p = new Point(c.X+Math.Cos(a)*r,c.Y+Math.Sin(a)*r*0.7);
                        dc.PushOpacity(returnLight*(0.22+0.4*(0.5+0.5*Math.Sin(pet.Time*1.3+i))));
                        dc.DrawEllipse(i%2==0?Ice:Blue,null,p,i%3==0?1.5:0.9,i%3==0?1.5:0.9);
                        dc.Pop();
                    }
                }
                else
                {
                    DrawContact(dc,unit,c);
                    if(pet.GrandAge<GrandWave.Release+0.50) DrawInfall(dc,c,pet.GrandAge);
                    bool chart=pet.Idle.Act==IdleAct.Constellation;
                    if(chart)
                    {
                        Point anchor=ChartAnchor(pet.Idle,c);
                        dc.PushTransform(new TranslateTransform(anchor.X,anchor.Y));
                        dc.PushTransform(new RotateTransform(pet.Idle.ChartAngle));
                        DrawChart(dc,pet.Idle.Chart,pet.Idle.Age,pet.Idle.Weight,StarThread);
                        dc.Pop(); dc.Pop();
                    }
                    dc.PushTransform(new TranslateTransform(c.X,c.Y));
                    if(pet.presence<1) PresenceLight(dc,pet.presence,pet.departing,pet.BlinkHeading);
                    dc.PushTransform(new RotateTransform(pet.CoreTilt));
                    double shellScale=pet.CoreSize*(1+pet.CoreTwitch(t));
                    dc.PushTransform(new ScaleTransform(shellScale*pet.coreAspect,shellScale));
                    double pass=pet.Idle.Act==IdleAct.RimCrossing?pet.Idle.Weight*IdleMotion.Beat(pet.Idle.Age,0.22,2.29,2.62):0;
                    dc.PushOpacity(Math.Min(1,pet.CoreLight)*(1-0.55*pass));
                    dc.DrawImage(Halo,HaloBounds);
                    dc.Pop();
                    double closed=Ease(pet.Seal);
                    dc.DrawGeometry(Void,null,HeartShape);
                    double flash=pet.Idle.Flash;
                    dc.PushOpacity(Math.Min(0.95,0.26+0.35*pet.CoreLight+0.30*flash));
                    dc.PushClip(HeartShape);dc.DrawImage(Halo,HaloBounds);dc.Pop();dc.Pop();
                    dc.PushOpacity((1-0.72*closed)*(1-0.56*pass)); dc.DrawGeometry(null,pet.LiquidCore?LiquidRim:Rim,HeartShape); dc.Pop();
                    double mantle=1-Ease((pet.GrandAge-GrandWave.Release-0.32)/0.68);
                    if(pet.LiquidCore)
                    {
                        dc.PushOpacity((1-0.52*closed)*pet.presence);
                        DrawLiquid(dc,pet.ChargeLevel+(3-pet.ChargeLevel)*mantle,pet.Time,mantle*Ease(pet.GrandAge/GrandWave.Release),pet.CoreTilt);
                        dc.Pop();
                    }
                    if(pet.ChargeLevel>0)
                        DrawStoredLight(dc,pet.ChargeLevel,pet.Time,double.PositiveInfinity,closed,pet.presence);
                    if(mantle>0)
                    {
                        DrawStoredLight(dc,3,pet.Time,pet.GrandAge,closed,pet.presence*mantle);
                    }
                    if(pet.GrandAge<GrandWave.Release+0.55) DrawGrandLight(dc,pet.GrandAge);
                    if(pet.presence<1) PresenceRim(dc,pet.presence,pet.departing);
                    if(pet.Idle.Act==IdleAct.Meteor) MeteorLight(dc,pet.Idle);
                    double crossing=pet.Idle.Act==IdleAct.RimCrossing?CrossingLight(dc,pet.Idle):0;
                    if(pet.Idle.Act==IdleAct.Stardust) Stardust(dc,pet.Idle);
                    Vector chartPen=chart?ChartInHeart(dc,c):new Vector();
                    if(pet.CoreLight>0.82)
                    {
                        dc.PushOpacity(Math.Min(0.76,pet.CoreLight-0.82));
                        dc.DrawEllipse(Glow,null,new Point(),29,23);dc.Pop();
                    }
                    double breath=0.5+0.5*Math.Sin(pet.Time*(1.8+pet.Gravity));
                    double shine=Math.Min(1,pet.CoreLight+0.27*pet.Gravity);
                    double size=4+shine*shine*(9+3*breath)+16*flash;
                    size+=(4.8-size)*pet.chartFocus+5.0*pet.chartReturn;
                    double slip=pet.Idle.Act==IdleAct.StarSlip?pet.Idle.Weight*IdleMotion.Beat(pet.Idle.Age,0.59,2.09,2.72):0;
                    size+=(13-0.50*size)*slip+2.8*Math.Abs(crossing);
                    if(pet.presence<0.56)
                        size+=(3+13*IdleMotion.Beat(pet.presence,0.15,0.21,0.56))*(1-Ease(pet.presence/0.56))/Math.Max(0.04,pet.CoreSize);
                    bool clipStar=slip>0 || pet.chartFocus>=0.98;
                    if(clipStar) dc.PushClip(HeartShape);
                    dc.PushTransform(new TranslateTransform(pet.starOffset.X+chartPen.X,pet.starOffset.Y+chartPen.Y));
                    if(chart)
                    {
                        dc.PushOpacity(pet.chartFocus*0.75+pet.chartReturn*0.65);
                        dc.DrawEllipse(Glow,null,new Point(),11,11); dc.Pop();
                    }
                    if(flash>0) { dc.PushOpacity(flash); dc.DrawEllipse(Glow,null,new Point(),33,30); dc.Pop(); }
                    dc.PushOpacity(Math.Min(1,0.65+shine*0.35+flash*0.35)*(1-0.40*closed));
                    dc.PushTransform(new ScaleTransform(size*(1+0.31*Math.Abs(crossing)),size));
                    if(slip>0)
                    {
                        dc.PushOpacity(0.27*slip); dc.DrawGeometry(Blue,null,Glint); dc.Pop();
                    }
                    dc.DrawGeometry(Ice,null,pet.Idle.Act==IdleAct.StarSlip?SlippingGlint(pet.Idle):Glint);
                    if(Math.Abs(crossing)>0.002)
                    {
                        dc.PushOpacity(Math.Abs(crossing));
                        dc.DrawGeometry(crossing<0?LeftGlint:RightGlint,null,Glint); dc.Pop();
                    }
                    if(flash>0) { dc.PushOpacity(0.88*flash); dc.DrawGeometry(Brushes.White,null,Glint); dc.Pop(); }
                    dc.Pop(); dc.Pop(); dc.Pop();
                    if(clipStar) dc.Pop();
                    DrawChargeRise(dc);
                    dc.Pop(); dc.Pop(); dc.Pop();
                    double hoverDust=pet.hideSpentDust?0:pet.Gravity;
                    if(hoverDust>0.015)
                    {
                        for(int i=0;i<2;i++)
                        {
                            double phase=(t*(0.58+0.25*pet.HoverWeight)+0.5*i)%1;
                            double r=25+(1-phase)*(45+32*pet.HoverWeight);
                            dc.PushOpacity(hoverDust*0.52*Math.Sin(Math.PI*phase));
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
                    if(pet.EjectAge>=EjectBeat && pet.EjectAge<EjectBeat+0.62)
                    {
                        double u=(pet.EjectAge-EjectBeat)/0.62;
                        double r=23+42*Ease(u); dc.PushOpacity(0.8*(1-u));
                        dc.DrawEllipse(null,Ring,c,r,r); dc.Pop();
                    }
                    if (hoverDust>0.01)
                        for (int i=0; i<31; i++)
                        {
                            double phase = (t*(0.65+0.05*(i%4))+i*0.61803)%1;
                            double a = i*2.39996+phase*2.3;
                            double r = 6+(1-phase)*123;
                            var p = new Point(c.X+Math.Cos(a)*r,c.Y+Math.Sin(a)*r*0.85);
                            var tail = new Point(c.X+Math.Cos(a-0.10)*(r+4),c.Y+Math.Sin(a-0.10)*(r+4)*0.85);
                            dc.PushOpacity(hoverDust*Math.Sin(phase*Math.PI));
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


            void DrawContact(DrawingContext dc,double unit,Point heart)
            {
                double p=pet.ContactProgress,age=pet.Time-pet.ContactFlashAt;
                if(p<0 && (age<0 || age>=0.52)) return;
                Point edge=(Point)((Vector)pet.ContactPoint/unit);
                Vector inside=-pet.ContactNormal;
                if(inside.LengthSquared<0.01) inside=heart-edge;
                if(inside.LengthSquared>1) inside.Normalize();
                Point mark=edge+inside*7;
                mark=new Point(Math.Max(9,Math.Min(291,mark.X)),Math.Max(9,Math.Min(291,mark.Y)));
                dc.PushTransform(new MatrixTransform(inside.Y,-inside.X,inside.X,inside.Y,mark.X,mark.Y));
                double head=p>=0?Ease(p):1-Ease(age/0.52);
                double fade=p>=0?0.40+0.60*Ease(p/0.18):1-Ease(age/0.52);
                dc.PushOpacity(fade);
                for(int side=-1;side<=1;side+=2)
                {
                    dc.PushTransform(new ScaleTransform(side,1));
                    double corner=Math.Max(0,side>0?pet.ContactSpan.Y:-pet.ContactSpan.X);
                    var path=corner>=31?ContactRail:Rail(0,1,corner);
                    dc.PushOpacity(0.45); dc.DrawGeometry(null,StarShadow,path);
                    dc.DrawGeometry(null,StarThread,path); dc.Pop();
                    var trail=p>=0?Rail(Math.Max(0,head-0.39),head,corner):Rail(head,Math.Min(1,head+0.39),corner);
                    dc.DrawGeometry(null,ContactMist,trail);
                    dc.DrawGeometry(null,ContactRim,trail);
                    Point tip=RailPoint(head,corner);
                    dc.DrawEllipse(Glow,null,tip,5,5);
                    dc.DrawEllipse(Ice,null,tip,1.4,1.4);
                    dc.Pop();
                }
                double light=p>=0?Ease((p-0.25)/0.75):1-Ease(age/0.52);
                double radius=p>=0?2+2.5*light:4+7*light;
                dc.DrawEllipse(Glow,null,new Point(),5+10*light,5+10*light);
                dc.PushTransform(new ScaleTransform(radius*0.74,radius));
                dc.DrawGeometry(Ice,null,Glint);
                dc.Pop(); dc.Pop(); dc.Pop();
            }
            static Point RailPoint(double u,double corner)
            {
                double r=1-u,x=3+28*r,y=1+6*r*r;
                if(x<=corner) return new Point(x,y);
                double radius=7+y,turn=x-corner;
                double angle=Math.Min(Math.PI/2,turn/radius);
                return new Point(corner+radius*Math.Sin(angle),radius*Math.Cos(angle)-7-Math.Max(0,turn-radius*Math.PI/2));
            }
            static Geometry Rail(double start,double end,double corner)
            {
                var g=new StreamGeometry();
                using(var c=g.Open())
                {
                    c.BeginFigure(RailPoint(start,corner),false,false);
                    for(int i=1;i<=12;i++) c.LineTo(RailPoint(start+(end-start)*i/12,corner),true,true);
                }
                g.Freeze(); return g;
            }
        }
    }
}
