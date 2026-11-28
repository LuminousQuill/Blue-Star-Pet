using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace BlueStar
{
    sealed partial class PetWindow
    {
        FileStar referenceStar;
        PinnedViewer referenceViewer;
        ReferenceTether referenceTether;
        Point referenceAnchor;
        Vector referenceDirection;
        double attachedAt;
        bool referencePreview;
        int referenceRequest;
        bool referenceButtonDown,polledReferenceDrag;
        Point? referencePressOrigin;
        bool catchButtonDown;
        PinnedViewer windowCandidate;
        Rect candidateFrame;
        bool candidateDragged,candidateEligible;
        const double WindowContactHold=1.5;
        double contactSince=-1;
        Point contactOrigin,contactToe;
        Vector contactInward;
        bool HasReference { get { return referenceStar!=null || referenceViewer!=null; } }
        internal static bool ReferenceButtonDown() { return DragButtonDown(); }

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPhysicalPoint(System.Drawing.Point p);

        void SampleReferencePress(Point pixel)
        {
            // Foreground media apps can delay delivery of the activation click to WPF.
            // Start the existing drag from its true origin when the button is already moving.
            bool down=DragButtonDown();
            if(down && !referenceButtonDown)
            {
                polledReferenceDrag=false;
                var screen=new System.Drawing.Point((int)pixel.X,(int)pixel.Y);
                var local=PointFromScreen(pixel);
                if(!pressed && !handlingDrop && !binCommand && orbits.Grabbed==null && exportingStar==null &&
                    WindowFromPhysicalPoint(screen)==new WindowInteropHelper(this).Handle && orbits.Hit(local)==null &&
                    (Art.InsideHeart(local) || Art.LegAt(local)>=0)) referencePressOrigin=pixel;
            }
            referenceButtonDown=down;
            if(!down || pressed) { referencePressOrigin=null; return; }
            if(!referencePressOrigin.HasValue) return;
            var travel=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice.Transform(pixel-referencePressOrigin.Value);
            if(travel.LengthSquared<25) return;
            BeginPetPress(referencePressOrigin.Value,true);
            referencePressOrigin=null; polledReferenceDrag=true;
        }

        bool ReferenceTarget(string[] paths,Point point,out Point anchor)
        {
            anchor=point;
            return !HasReference && !exiting && !binCommand && !handlingDrop &&
                paths.Length==1 && PinnedViewer.IsMedia(paths[0]) && Art.TryGripPoint(point,out anchor);
        }
        void ClearReferencePreview()
        {
            if(!referencePreview) return;
            referencePreview=false;
            Art.ClearGrip();
            orbits.ShowGripHint(null);
            if(referenceStar==null) orbits.ShowGripStar(null,new Point(),0);
        }
        void PreviewReference(Point anchor,Point pointer)
        {
            referencePreview=true;
            Vector direction=anchor-Art.Heart;
            if(direction.LengthSquared>0) direction.Normalize();
            Art.SetGrip(anchor,direction,0,true);
            orbits.ShowGripHint(pointer);
            if(exportingWholeStar) orbits.ShowGripStar(exportingStar,pointer,1);
        }
        async void OpenReference(FileStar star,Point anchor)
        {
            if(HasReference || exiting) return;
            if(!File.Exists(star.Paths[0]))
            {
                ClearReferencePreview(); EndFileHover(); Say("原位置未找到这个媒体文件",3); return;
            }
            referencePreview=false;
            orbits.ShowGripHint(null);
            EndFileHover(); CloseStarFiles();
            orbits.Remove(star);
            referenceStar=star; referenceAnchor=anchor;
            referenceDirection=anchor-Art.Heart; referenceDirection.Normalize();
            int request=++referenceRequest;
            Art.Idle.Stop(); ResetRoam(); Art.Swim.Strength=0;
            Art.SetGrip(anchor,referenceDirection,0,false);
            orbits.ShowGripStar(star,anchor,1);
            try
            {
                // Give the catching feet a beat before the shell brings the viewer forward.
                await Task.Delay(190);
                if(referenceRequest!=request) return;
                var viewer=await PinnedViewer.OpenAsync(star.Paths[0]);
                if(referenceRequest!=request) { if(viewer!=null) viewer.Dispose(); return; }
                if(viewer==null)
                {
                    EndReference(); Say("未找到可夹持的独立窗口，小星已回轨",3); return;
                }
                AttachReference(viewer,anchor,null);
            }
            catch(Exception ex)
            {
                if(referenceRequest!=request) return;
                EndReference();
                Say("参照打开失败，小星已回轨 · "+ex.Message,4);
            }
        }
        void AttachReference(PinnedViewer viewer,Point anchor,Vector? edge)
        {
            referenceViewer=viewer;
            if(!viewer.PinTopmost()) throw new InvalidOperationException("窗口未能置顶");
            Topmost=true;
            referenceTether=new ReferenceTether(this,viewer,anchor,edge);
            referenceButtonDown=DragButtonDown(); polledReferenceDrag=false; referencePressOrigin=null;
            attachedAt=elapsed.Elapsed.TotalSeconds;
        }
        void StopWatchingWindow()
        {
            ResetContact();
            candidateDragged=candidateEligible=false;
            if(windowCandidate==null) return;
            windowCandidate.Dispose(); windowCandidate=null;
        }
        void ResetContact()
        {
            contactSince=-1; Art.ContactProgress=-1;
        }
        void CatchDraggedWindow(double now)
        {
            bool down=DragButtonDown(), started=down && !catchButtonDown;
            catchButtonDown=down;
            if(!down || HasReference || exiting || pressed || coasting || draggingFiles || handlingDrop || binCommand ||
                orbits.Grabbed!=null || exportingStar!=null || panel!=null || starFiles!=null)
            { StopWatchingWindow(); return; }
            if(started)
            {
                windowCandidate=PinnedViewer.WindowAtPress(new WindowInteropHelper(this).Handle);
                if(windowCandidate!=null) candidateFrame=windowCandidate.Bounds;
            }
            if(windowCandidate==null) return;
            Rect frame=windowCandidate.NormalBounds,previous=candidateFrame; candidateFrame=frame;
            var gesture=windowCandidate.ReadGesture(frame);
            bool movingFrame=gesture==PinnedViewer.Gesture.Moving;
            if(frame.IsEmpty || previous.IsEmpty || frame.Size!=previous.Size || gesture==PinnedViewer.Gesture.Sizing)
            { StopWatchingWindow(); return; }
            if(movingFrame && frame.Location!=previous.Location) candidateDragged=true;
            if(!candidateDragged) return;
            // Custom player chrome can stop reporting a move while its caption is held still.
            if(!movingFrame && (frame.Location!=previous.Location || !windowCandidate.Foreground))
            { StopWatchingWindow(); return; }
            Rect body=ReferenceTether.PhysicalPetFrame(new WindowInteropHelper(this).Handle);
            if(body.IsEmpty || body.Width<=0 || ActualWidth<=0) { ResetContact(); return; }
            double scale=ActualWidth/body.Width;
            var local=new Rect((frame.X-body.X)*scale,(frame.Y-body.Y)*scale,frame.Width*scale,frame.Height*scale);
            Point touch; Vector inward;
            Vector drift=local.Location-contactOrigin;
            if(contactSince>=0 && drift.LengthSquared<=36)
            {
                // A stationary hand keeps its contact while the unpinned legs breathe.
                touch=contactToe+drift; inward=contactInward;
            }
            else if(!Art.TryWindowContact(local,out touch,out inward,contactSince>=0)) { ResetContact(); return; }
            else
            {
                if(contactSince>=0 && (touch-contactToe).Length>ActualWidth*0.20) ResetContact();
                contactOrigin=local.Location; contactToe=touch; contactInward=inward;
            }
            if(!candidateEligible)
            {
                if(!windowCandidate.CanCatchOpenWindow()) { StopWatchingWindow(); return; }
                candidateEligible=true;
            }
            if(contactSince<0 || Art.ContactNormal!=inward) Art.ContactPoint=touch;
            else
            {
                Vector delta=touch-Art.ContactPoint;
                Vector normalStep=inward*Vector.Multiply(delta,inward);
                Art.ContactPoint+=normalStep+(delta-normalStep)*(1-Math.Exp(-18*Math.Max(0,now-Art.Time)));
            }
            if(contactSince<0) contactSince=now;
            Art.ContactNormal=inward;
            Art.ContactSpan=ReferenceTether.SpanAlongEdge(local,Art.ContactPoint,inward,5,ActualWidth/300);
            Art.ContactProgress=Math.Min(1,(now-contactSince)/WindowContactHold);
            if(Art.ContactProgress<1) return;
            var viewer=windowCandidate; windowCandidate=null;
            ResetContact();
            try
            {
                AttachReference(viewer,touch,inward);
                Art.Idle.Stop(); ResetRoam(); Art.Swim.Strength=0;
                Art.SetGrip(referenceTether.Anchor,inward,referenceTether.Load,false,referenceTether.EdgeSpan);
                Art.ContactFlashAt=now;
            }
            catch(InvalidOperationException) { EndReference(false); }
        }
        void EndReference(bool returnToOrbit=true)
        {
            StopWatchingWindow();
            ClearReferencePreview();
            if(!HasReference) return;
            referenceRequest++;
            if(referenceTether!=null) { referenceTether.Dispose(); referenceTether=null; }
            if(referenceViewer!=null) { referenceViewer.Dispose(); referenceViewer=null; }
            orbits.ShowGripStar(null,referenceAnchor,0);
            if(returnToOrbit && referenceStar!=null) orbits.ReturnFromGrip(referenceStar,referenceAnchor);
            referenceStar=null; Art.ClearGrip(); Art.ContactFlashAt=double.NegativeInfinity; Topmost=Settings.Pinned;
        }
        void AdvanceReference(double now,double dt)
        {
            CatchDraggedWindow(now);
            if(referenceTether==null) return;
            bool torn;
            if(!referenceTether.Advance(dt,moving || coasting,out torn))
            {
                EndReference(!torn); return;
            }
            referenceAnchor=referenceTether.Anchor;
            Art.ContactPoint=referenceAnchor;
            Art.ContactSpan=referenceTether.EdgeSpan;
            Art.SetGrip(referenceAnchor,referenceTether.Outward,referenceTether.Load,false,Art.ContactSpan);
            double remaining=1-(now-attachedAt)/0.30;
            orbits.ShowGripStar(remaining>0?referenceStar:null,referenceAnchor,Math.Max(0,remaining));
        }
        internal bool StopReferenceCoast()
        {
            if(coasting) { coasting=false; velocity=new Vector(); }
            return moving;
        }
        internal void ReleaseReferenceVelocity(Vector dipVelocity)
        {
            if(pressed || moving) return;
            double speed=dipVelocity.Length;
            if(speed<2) return;
            velocity=dipVelocity*Math.Min(1,Width*2.8/speed);
            Art.Wake.Follow(velocity,1.0/60); Art.Wake.Release();
            coasting=true;
        }
        public void SetPinned(bool value)
        {
            Settings.Pinned=value; Topmost=value || referenceViewer!=null; Persist();
        }
    }
}
