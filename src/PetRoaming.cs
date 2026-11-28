using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

namespace BlueStar
{
    sealed class RoamStroke
    {
        public Vector Heading;
        public double Phase,Strength;
        double lastStroke,settleAge;

        public void Follow(Vector tangent,double progress,double duration,double width)
        {
            Phase=Math.Min(1,progress);
            double length=tangent.Length;
            if(length>0.0001) Heading=tangent/length;
            // The quadratic path supplies the heading and its eased speed in body lengths per second.
            double speed=length*12*Phase*(1-Phase)/(duration*width);
            Strength=IdleMotion.Ease(Phase/0.12)*Math.Min(1,0.44+7.5*speed);
            lastStroke=Strength; settleAge=0;
        }
        public void Settle(double dt)
        {
            if(Strength==0) return;
            // Release from the last stroke without snapping a fully extended foot back on the first frame.
            settleAge+=dt;
            Strength=lastStroke*(1-IdleMotion.Ease(settleAge/0.48));
        }
    }

    sealed partial class PetWindow
    {
        enum RoamStep { Rest, Drift, BlinkOut }
        const double ArrivalSeconds=1.52,ExitSeconds=1.16,BlinkOutSeconds=0.88;
        RoamStep roamStep;
        bool roaming,exiting,finishClose;
        Point? roamHome;
        Point roamFrom,roamBend,roamTo;
        double roamWait=4,roamAge,roamDuration,exitStart,exitPresence;

        public void SetRoaming(bool enabled)
        {
            roaming=Settings.Roaming=enabled;
            ResetRoam(); Persist();
        }
        void ResetRoam()
        {
            if(roamStep!=RoamStep.Rest) audio.FadeMotion();
            roamHome=null; roamStep=RoamStep.Rest; roamWait=4;
        }

        void SetPresence(double value)
        {
            Art.Presence=value;
            if(value==1) Art.BlinkHeading=new Vector();
            grid.Opacity=IdleMotion.Ease(value/0.12);
            orbits.Opacity=IdleMotion.Ease((value-0.40)/0.52);
            bubble.Opacity=IdleMotion.Ease((value-0.77)/0.23);
        }
        void AdvanceExit(double now)
        {
            SetPresence(Math.Max(0,exitPresence-(now-exitStart)/ExitSeconds));
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if(e.Cancel || finishClose || !IsVisible || !IsLoaded) return;
            e.Cancel=true;
            if(exiting) return;
            exiting=true; exitStart=elapsed.Elapsed.TotalSeconds; exitPresence=Art.Presence;
            StopMealLens();
            EndReference();
            audio.PlayMotion(PetCue.Disappear);
            AllowDrop=false; IsHitTestVisible=false;
            CloseStarFiles();
            if(panel!=null) panel.Close();
            if(shelf!=null) shelf.Hide();
            if(meteor!=null) meteor.Hide();
            if(wave!=null) wave.Hide();
            if(grandWave!=null) { grandWave.Close(); grandWave=null; }
            FinishExit();
        }
        async void FinishExit()
        {
            // Closing must also finish when the compositor pauses (for example on a locked desktop).
            await Task.Delay(TimeSpan.FromSeconds(exitPresence*ExitSeconds+0.08));
            if(!IsLoaded) return;
            finishClose=true; Close();
        }
        bool NearWindow(Point cursor)
        {
            double pad=Width*0.10;
            return cursor.X>=-pad && cursor.X<=Width+pad && cursor.Y>=-pad && cursor.Y<=Height+pad;
        }
        static Point ClampPoint(Point p,Rect bounds)
        {
            return new Point(Math.Max(bounds.Left,Math.Min(bounds.Right,p.X)),Math.Max(bounds.Top,Math.Min(bounds.Bottom,p.Y)));
        }
        void BeginRoam(bool blink)
        {
            roamFrom=new Point(Left,Top);
            var work=ScreenArea();
            var limits=new Rect(work.Left,work.Top,Math.Max(0,work.Width-Width),Math.Max(0,work.Height-Height));
            Point home=ClampPoint(roamHome??roamFrom,limits); roamHome=home;
            var aroundHome=new Rect(home,home);
            aroundHome.Inflate(Width*(blink?2.0:0.85),Height*(blink?1.85:0.65));
            // After a distant blink, the next drift still starts with a short, local step.
            aroundHome.Union(roamFrom);
            limits.Intersect(aroundHome);
            double distance=Width*(blink?1.25+0.60*idleRandom.NextDouble():0.12+0.15*idleRandom.NextDouble());
            double minDistance=(blink?0.95:0.75)*distance;
            double angle=idleRandom.NextDouble()*2*Math.PI;
            roamTo=ClampPoint(roamFrom,limits);
            // Try inward headings at a screen edge so the pet does not blink in place.
            for(int i=0;i<8;i++,angle+=Math.PI/4)
            {
                var target=ClampPoint(roamFrom+new Vector(Math.Cos(angle),Math.Sin(angle))*distance,limits);
                if((target-roamFrom).LengthSquared>(roamTo-roamFrom).LengthSquared) roamTo=target;
                if((roamTo-roamFrom).Length>=minDistance) break;
            }
            Vector path=roamTo-roamFrom;
            roamWait=7+idleRandom.NextDouble()*11;
            if(path.Length<3) return;
            roamBend=ClampPoint(roamFrom+path*0.50+new Vector(-path.Y,path.X)*(idleRandom.NextDouble()*0.42-0.21),limits);
            roamDuration=2.2+path.Length/(Width*0.18); roamAge=0;
            roamStep=blink?RoamStep.BlinkOut:RoamStep.Drift;
            if(blink) audio.PlayMotion(PetCue.Blink);
            else audio.PlayRoam(idleRandom);
            if(blink) Art.BlinkHeading=path/path.Length;
        }
        bool AdvanceRoaming(double now,double dt,Point cursor)
        {
            bool busy=!roaming || NearWindow(cursor) || InteractionBusy(now) || fileLeftAt>=0 || orbits.Fold>0 || Art.Idle.Act!=IdleAct.None;
            if(roamStep==RoamStep.BlinkOut && NearWindow(cursor-(roamTo-roamFrom))) busy=true;
            if(busy && roamStep!=RoamStep.Rest) { audio.FadeMotion(); roamStep=RoamStep.Rest; }
            if(roamStep!=RoamStep.Drift) Art.Swim.Settle(dt);
            if(roamStep!=RoamStep.BlinkOut && Art.Presence==1) Art.BlinkHeading=new Vector();
            if(roamStep==RoamStep.BlinkOut)
            {
                // Present a transparent frame at the old position before moving the native window.
                if(Art.Presence>0) { SetPresence(Math.Max(0,Art.Presence-dt/BlinkOutSeconds)); return false; }
                Left=roamTo.X; Top=roamTo.Y; roamStep=RoamStep.Rest;
                return true;
            }
            if(Art.Presence<1)
            {
                SetPresence(Math.Min(1,Art.Presence+dt/ArrivalSeconds)); return false;
            }
            if(busy) return false;
            if(roamStep==RoamStep.Rest)
            {
                if((roamWait-=dt)<=0) BeginRoam(idleRandom.Next(16)==0);
                return false;
            }
            roamAge+=dt;
            double u=IdleMotion.Ease(roamAge/roamDuration);
            Point a=roamFrom+(roamBend-roamFrom)*u,b=roamBend+(roamTo-roamBend)*u;
            Point next=a+(b-a)*u;
            Art.Swim.Follow(b-a,roamAge/roamDuration,roamDuration,Width);
            Left=next.X; Top=next.Y;
            if(u>=1) roamStep=RoamStep.Rest;
            return true;
        }
    }
}
