using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace BlueStar
{
    sealed class ReferenceTether : IDisposable
    {
        readonly PetWindow pet;
        readonly PinnedViewer viewer;
        readonly IntPtr petHandle;
        ReferenceResizeGrip grip;
        Rect[] screens;
        Rect previous,petFrame;
        Rect requested=Rect.Empty,requestOrigin;
        double requestAge;
        bool awaitingPlacement,gripFinishing;
        Point localFraction,edgeFraction,resizePin,sampledFrameOrigin;
        Vector speed,remainder,viewerRate;
        bool petLeads, sizing;
        double stretchedTime, sampleAge, desktopAge, orderAge;
        public Point Anchor { get; private set; }
        public Vector Outward { get; private set; }
        public double Load { get; private set; }
        // Signed distance along the visible edge in the leg rig's 300-unit coordinate space.
        public Vector EdgeSpan
        {
            get { return SpanAlongEdge(previous,Contact(previous),Outward,5*Scale,petFrame.Width/300); }
        }
        internal static Vector SpanAlongEdge(Rect frame,Point p,Vector normal,double inset,double unit)
        {
            bool vertical=normal.X!=0;
            double lo=vertical?frame.Top+inset-p.Y:frame.Left+inset-p.X;
            double hi=vertical?frame.Bottom-inset-p.Y:frame.Right-inset-p.X;
            return (vertical?normal.X:-normal.Y)>0?new Vector(lo/unit,hi/unit):new Vector(-hi/unit,-lo/unit);
        }

        public ReferenceTether(PetWindow pet,PinnedViewer viewer,Point localAnchor,Vector? caughtEdge=null)
        {
            this.pet=pet; this.viewer=viewer;
            petHandle=new WindowInteropHelper(pet).Handle;
            ReadPetFrame();
            if(!caughtEdge.HasValue) viewer.RestoreForPin();
            var bounds=viewer.Bounds;
            if(!Usable(bounds)) throw new InvalidOperationException("参照窗口不可用");
            RefreshScreens();
            if(caughtEdge.HasValue)
            {
                Outward=caughtEdge.Value;
                Point pin=ScreenPoint(localAnchor)-Outward*(5*Scale);
                edgeFraction=new Point(Outward.X!=0?(1-Outward.X)/2:Clamp((pin.X-bounds.Left)/bounds.Width,0,1),
                    Outward.Y!=0?(1-Outward.Y)/2:Clamp((pin.Y-bounds.Top)/bounds.Height,0,1));
                Point contact=LocalContact(bounds);
                localFraction=new Point(contact.X/pet.Width,contact.Y/pet.Height);
            }
            else Arrange(bounds,localAnchor);
            previous=viewer.Bounds;
            if(!Usable(previous)) throw new InvalidOperationException("参照窗口已关闭");
            sampledFrameOrigin=previous.Location;
            Anchor=LocalContact(previous);
            Load=WindowLoad(previous);
            if(!StackFeetAboveViewer()) throw new InvalidOperationException("无法置顶参照窗口");
            if(viewer.CanResize)
            {
                grip=new ReferenceResizeGrip(pet,viewer.Handle,edgeFraction);
                grip.Follow(previous,screens,Scale);
            }
        }

        double Scale { get { return petFrame.Width/pet.ActualWidth; } }
        Point ScreenPoint(Point local) { return petFrame.Location+(Vector)local*Scale; }
        Point LocalContact(Rect frame) { return (Point)((Contact(frame)-petFrame.Location)/Scale); }
        Point Rest { get { return ScreenPoint(new Point(localFraction.X*pet.Width,localFraction.Y*pet.Height)); } }
        void ReadPetFrame()
        {
            petFrame=PhysicalPetFrame(petHandle);
        }
        internal static Rect PhysicalPetFrame(IntPtr handle)
        {
            NativeRect r;
            using(new PinnedViewer.PhysicalCoordinates(true))
                return GetWindowRect(handle,out r)?new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top):Rect.Empty;
        }
        bool Usable(Rect bounds)
        {
            return viewer.Alive && !viewer.Minimized && !viewer.Maximized && !bounds.IsEmpty && bounds.Width>0 && bounds.Height>0;
        }
        void RefreshScreens()
        {
            using(new PinnedViewer.PhysicalCoordinates(true))
            {
                var monitors=Forms.Screen.AllScreens;
                screens=new Rect[monitors.Length];
                for(int i=0;i<monitors.Length;i++)
                {
                    var r=monitors[i].WorkingArea;
                    screens[i]=new Rect(r.Left,r.Top,r.Width,r.Height);
                }
            }
            desktopAge=0;
        }
        static double Clamp(double v,double lo,double hi) { return Math.Max(lo,Math.Min(hi,v)); }
        static Point Center(Rect r) { return new Point(r.Left+r.Width/2,r.Top+r.Height/2); }
        static Rect OnScreen(Point p,Rect[] monitors)
        {
            Rect best=monitors[0]; double distance=double.MaxValue;
            foreach(Rect monitor in monitors)
            {
                var nearest=new Point(Clamp(p.X,monitor.Left,monitor.Right),Clamp(p.Y,monitor.Top,monitor.Bottom));
                double d=(p-nearest).LengthSquared;
                if(d<distance) { best=monitor; distance=d; }
            }
            return best;
        }
        // Merge touching work areas on the travel axis so a follower can straddle the seam.
        internal static Point Constrain(Rect r,Rect[] monitors)
        {
            Point center=Center(r);
            Rect m=OnScreen(center,monitors);
            double left=m.Left,right=m.Right,top=m.Top,bottom=m.Bottom;
            for(int i=0;i<monitors.Length;i++)
                foreach(Rect next in monitors)
                {
                    if(center.Y>=next.Top && center.Y<=next.Bottom && next.Left<=right && next.Right>=left)
                    { left=Math.Min(left,next.Left); right=Math.Max(right,next.Right); }
                    if(center.X>=next.Left && center.X<=next.Right && next.Top<=bottom && next.Bottom>=top)
                    { top=Math.Min(top,next.Top); bottom=Math.Max(bottom,next.Bottom); }
                }
            return new Point(Clamp(r.Left,left,right-r.Width),Clamp(r.Top,top,bottom-r.Height));
        }
        Point Contact(Rect r)
        {
            // GetWindowRect includes the invisible sizing border of modern desktop windows.
            return new Point(r.Left+edgeFraction.X*r.Width,r.Top+edgeFraction.Y*r.Height)+Outward*(5*Scale);
        }
        Rect AtContact(Point pin,Size size)
        {
            var r=new Rect(new Point(),size);
            r.Offset(pin-Contact(r)); return r;
        }
        void Arrange(Rect initial,Point anchor)
        {
            double scale=Scale;
            Point origin=petFrame.Location, heart=pet.Art.Heart;
            Rect work=OnScreen(ScreenPoint(heart),screens);
            Vector heading=anchor-heart;
            if(heading.LengthSquared<1) heading=new Vector(1,0);
            double reach=heading.Length; heading.Normalize();
            double best=double.PositiveInfinity;
            Rect placement=Rect.Empty;
            Vector petShift=new Vector();
            bool resizable=viewer.CanResize;
            Size minimum=resizable?ReferenceResizeGrip.MinimumSize(viewer.Handle):initial.Size;
            double minFit=Math.Max(minimum.Width/initial.Width,minimum.Height/initial.Height);
            double referenceFit=Math.Min(1,430*scale/Math.Max(initial.Width,initial.Height));
            foreach(var normal in new[]{new Vector(1,0),new Vector(-1,0),new Vector(0,1),new Vector(0,-1)})
            {
                double affinity=Vector.Multiply(normal,heading);
                Point local=affinity>=0.70?anchor:heart+normal*reach;
                Point pin=ScreenPoint(local)-normal*(5*scale);
                double petSpanX=normal.X>0?local.X:pet.Width-local.X;
                double petSpanY=normal.Y>0?local.Y:pet.Height-local.Y;
                double widthLimit=work.Width-(normal.X!=0?petSpanX*scale:0);
                double heightLimit=work.Height-(normal.Y!=0?petSpanY*scale:0);
                double fit=resizable?Math.Max(minFit,Math.Min(referenceFit,Math.Min(widthLimit/initial.Width,heightLimit/initial.Height))):1;
                fit=Math.Max(0.01,fit);
                Size size=new Size(initial.Width*fit,initial.Height*fit);
                Point uv=new Point((1-normal.X)/2,(1-normal.Y)/2);
                if(normal.X==0) uv.X=Clamp((pin.X-Clamp(pin.X-size.Width/2,work.Left,work.Right-size.Width))/size.Width,0.13,0.87);
                if(normal.Y==0) uv.Y=Clamp((pin.Y-Clamp(pin.Y-size.Height/2,work.Top,work.Bottom-size.Height))/size.Height,0.13,0.87);
                Rect r=new Rect(pin.X-uv.X*size.Width,pin.Y-uv.Y*size.Height,size.Width,size.Height);
                Vector shift=Constrain(r,screens)-r.Location;
                double score=(1-affinity)*pet.Width+shift.Length/scale+(1-fit)*250;
                if(score>=best) continue;
                best=score; Outward=normal; edgeFraction=uv;
                localFraction=new Point(local.X/pet.Width,local.Y/pet.Height);
                petShift=shift; r.Offset(shift); placement=r;
            }
            if(!viewer.Place(placement)) throw new InvalidOperationException("无法定位参照窗口");
            Rect actual=viewer.Bounds;
            if(!Usable(actual)) throw new InvalidOperationException("参照窗口不可用");
            // A native viewer may enforce a larger minimum or keep the video's aspect ratio.
            Vector correction=Contact(actual)-Contact(placement);
            Rect petRect=new Rect(origin+petShift+correction,new Size(pet.Width*scale,pet.Height*scale));
            MovePet(Constrain(petRect,screens)-origin);
        }
        void MovePet(Vector pixels)
        {
            if(Math.Abs(pixels.X)<0.5 && Math.Abs(pixels.Y)<0.5) return;
            var dest=petFrame.Location+pixels;
            using(PinnedViewer.PhysicalCoordinates.ForWindow(petHandle))
                SetWindowPos(petHandle,IntPtr.Zero,(int)Math.Round(dest.X),(int)Math.Round(dest.Y),0,0,0x0215);
            ReadPetFrame();
        }
        double WindowLoad(Rect r)
        {
            return Clamp((Math.Sqrt(r.Width*r.Height)/Scale-320)/550,0,1);
        }
        static Vector Limit(Vector value,double limit)
        {
            return value.Length>limit?value*(limit/value.Length):value;
        }
        // Integrate the passive body. The finite reach keeps the toes inside the pet's drawing.
        Vector Follow(Vector error,double dt)
        {
            Vector remaining=Limit(error,pet.Width*Scale*0.14);
            double spring=petLeads?100-48*Load:80, damping=2*Math.Sqrt(spring);
            for(double left=dt;left>0.000001;)
            {
                double h=Math.Min(left,1.0/120); left-=h;
                speed+=(remaining*spring-speed*damping)*h;
                remaining-=speed*h;
            }
            if(remaining.Length<0.7 && speed.Length<5*Scale) { remaining=new Vector(); speed=new Vector(); }
            return error-remaining;
        }
        public bool Advance(double dt,bool draggingPet,out bool torn)
        {
            torn=false;
            Rect b=viewer.NormalBounds;
            if(b.IsEmpty) return false;
            var gesture=viewer.ReadGesture(b);
            bool viewerOwnsFrame=gesture!=PinnedViewer.Gesture.None;
            if(viewerOwnsFrame) draggingPet=pet.StopReferenceCoast();
            bool applied=awaitingPlacement && b!=requestOrigin;
            requestAge+=dt;
            if(applied || requestAge>=0.16) awaitingPlacement=false;
            if(viewerOwnsFrame) { awaitingPlacement=gripFinishing=false; requested=Rect.Empty; }
            // The native move loop can advance while our own windows are being positioned.
            // Measure velocity between the same point in successive render frames.
            Vector frameTravel=b.Location-sampledFrameOrigin;
            sampledFrameOrigin=b.Location;
            sampleAge+=dt;
            double sampleInterval=sampleAge;
            if(frameTravel.LengthSquared>0) sampleAge=0;
            ReadPetFrame();
            if(petFrame.IsEmpty || pet.ActualWidth<=0) return false;
            desktopAge+=dt; orderAge+=dt;
            if(desktopAge>1.5) RefreshScreens();
            bool gripSizing=grip!=null && grip.Dragging;
            bool gripOwnsFrame=!viewerOwnsFrame && (gripSizing || gripFinishing);
            if(orderAge>0.12 && !viewerOwnsFrame && !gripOwnsFrame)
            {
                if(!StackFeetAboveViewer()) return false;
                orderAge=0;
            }
            if(dt<=0) return true;
            double scale=Scale;
            bool sizeChanged=b.Size!=previous.Size;
            if(gesture==PinnedViewer.Gesture.Sizing || (sizeChanged && !gripOwnsFrame && gesture!=PinnedViewer.Gesture.Moving))
            {
                // The system owns native resize geometry, including corner, aspect ratio and DPI.
                MoveWithContact(b);
                sizing=true; stretchedTime=0; speed=remainder=viewerRate=new Vector();
            }
            else if(gripOwnsFrame)
            {
                if(!gripFinishing) resizePin=Contact(b);
                gripFinishing=true;
                if(!awaitingPlacement)
                {
                    // If the app enforces its own size or aspect, put its accepted frame
                    // back on the toe once at release, with a move-only request.
                    Size size=gripSizing?grip.RequestSize(scale,screens):b.Size;
                    if(!PlaceIfChanged(b,AtContact(resizePin,size),gripSizing)) return false;
                    if(gripSizing) grip.FinishPlacement();
                    else gripFinishing=false;
                }
                sizing=true; stretchedTime=0; speed=remainder=viewerRate=new Vector();
            }
            else
            {
                bool userMoves=gesture==PinnedViewer.Gesture.Moving;
                Vector observed=b.Location-previous.Location;
                bool leader=petLeads;
                if(viewerOwnsFrame || (!applied && observed.LengthSquared>2)) leader=false;
                else if(draggingPet) leader=true;
                if(leader!=petLeads || sizing) { speed=remainder=new Vector(); petLeads=leader; }
                sizing=false;
                if(petLeads)
                {
                    // The load meets the desktop edge first. Stop the hand there as well.
                    Rect target=AtContact(Rest,b.Size);
                    MovePet(Constrain(target,screens)-target.Location);
                }
                else if(!viewerOwnsFrame)
                {
                    // Once the user releases the frame, settle both bodies inside the work area.
                    Rect target=petFrame; target.Offset(Contact(b)-Rest);
                    Vector correction=Constrain(target,screens)-target.Location;
                    if(correction.Length>1)
                    {
                        Rect adjusted=b;
                        adjusted.Offset(correction*Math.Min(1,Math.Max(1/correction.Length,1-Math.Exp(-10*dt))));
                        adjusted.Location=Constrain(adjusted,screens);
                        if(!PlaceIfChanged(b,adjusted,false)) return false;
                    }
                }
                Vector offset=Rest-Contact(b);
                Point heart=ScreenPoint(pet.Art.Heart);
                Vector reach=Contact(b)-heart;
                if(userMoves)
                {
                    // A player may publish its position less often than WPF renders.
                    if(frameTravel.LengthSquared>0)
                        viewerRate+=(Limit(frameTravel/sampleInterval,4000*scale)-viewerRate)*(1-Math.Exp(-30*sampleInterval));
                    else if(sampleAge>0.05) viewerRate*=Math.Exp(-30*dt);
                }
                else { viewerRate=new Vector(); sampleAge=0; }
                bool pulling=userMoves && !draggingPet && viewerRate.Length>1000*scale &&
                    offset.Length>pet.Width*scale*0.105 && reach.Length>(Rest-heart).Length &&
                    Vector.Multiply(viewerRate,reach)>0 && Vector.Multiply(viewerRate,offset)<0;
                stretchedTime=pulling?stretchedTime+Math.Min(dt,0.04):0;
                if(stretchedTime>=0.095)
                {
                    torn=true; pet.ReleaseReferenceVelocity(speed/scale); return false;
                }
                // The follower already owns this fraction of a physical pixel. Feeding
                // it into the spring again makes a motionless window alternate pixels.
                Vector step=Follow((petLeads?offset:-offset)-remainder,Math.Min(dt,0.13));
                if(petLeads)
                {
                    Rect desired=b; desired.Offset(step+remainder);
                    Point restricted=Constrain(desired,screens);
                    Point rounded=new Point(Math.Round(restricted.X),Math.Round(restricted.Y));
                    remainder=restricted-rounded; desired.Location=rounded;
                    if(!PlaceIfChanged(b,desired,false)) return false;
                }
                else
                {
                    Point origin=petFrame.Location;
                    Rect dest=new Rect(origin+step+remainder,new Size(pet.Width*scale,pet.Height*scale));
                    Point restricted=Constrain(dest,screens);
                    Point rounded=new Point(Math.Round(restricted.X),Math.Round(restricted.Y));
                    remainder=restricted-rounded;
                    MovePet(rounded-origin);
                }
            }
            previous=b;
            Anchor=LocalContact(previous); Load=WindowLoad(previous);
            if(grip!=null) grip.Follow(previous,screens,Scale);
            return true;
        }
        void MoveWithContact(Rect frame)
        {
            Rect target=petFrame; target.Offset(Contact(frame)-Rest);
            MovePet(Constrain(target,screens)-petFrame.Location);
        }
        bool PlaceIfChanged(Rect actual,Rect target,bool resize)
        {
            target=new Rect(Math.Round(target.X),Math.Round(target.Y),
                resize?Math.Round(target.Width):actual.Width,resize?Math.Round(target.Height):actual.Height);
            if(actual==target || awaitingPlacement || (requested==target && requestOrigin==actual)) return true;
            if(!viewer.RequestPlacement(target,resize && actual.Size!=target.Size)) return false;
            // Coalesce while the foreign window has not reported the previous request.
            requested=target; requestOrigin=actual; awaitingPlacement=true; requestAge=0;
            return true;
        }
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint relation);
        struct NativeRect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
        bool StackFeetAboveViewer()
        {
            // Activating a topmost viewer raises it over the feet. Reorder just this pair, without focus.
            if(!viewer.PinTopmost()) return false;
            for(IntPtr above=GetWindow(petHandle,3);above!=IntPtr.Zero;above=GetWindow(above,3))
                if(above==viewer.Handle)
                {
                    // Reorder our surface without interrupting native caption drags in the player.
                    IntPtr previousWindow=GetWindow(viewer.Handle,3);
                    return SetWindowPos(petHandle,previousWindow==IntPtr.Zero?new IntPtr(-1):previousWindow,0,0,0,0,0x0013|0x0200);
                }
            return true;
        }
        public void Dispose()
        {
            if(grip!=null) { grip.Close(); grip=null; }
        }
    }
}
