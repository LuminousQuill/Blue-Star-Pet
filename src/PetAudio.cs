using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace BlueStar
{
    enum PetCue { Charge, Resonance, Chime, Attract, Absorb, Ambient, Appear, Disappear, Roam, RoamUndertow, RoamEcho, RoamVeil, Blink, Constellation, GrandCall, GrandStrike, GrandChoir }

    sealed class PetAudio : IDisposable
    {
        readonly Dictionary<PetCue,MediaPlayer> players=new Dictionary<PetCue,MediaPlayer>();
        readonly PetCue[] roamCues={PetCue.Roam,PetCue.RoamUndertow,PetCue.RoamEcho,PetCue.RoamVeil};
        int roamCuesLeft;
        readonly string directory=Path.Combine(Path.GetTempPath(),"BlueStar-"+Guid.NewGuid().ToString("N"));
        MediaPlayer foreground, motion;
        readonly PrerolledPlayback hover, chargeRise, meteorFlash;
        readonly Stopwatch meteorFade=new Stopwatch();
        readonly DispatcherTimer meteorFadeTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(12) };
        bool enabled, hovering, hoverPlaying, fadingMotion, chargeRisePlayed, meteorFlashPlayed;
        double gain, hoverEnvelope, motionEnvelope=1, motionFadeSeconds, meteorEnvelope;
        const double MeteorFadeSeconds=0.064;

        public PetAudio(bool sound,int percent)
        {
            roamCuesLeft=roamCues.Length;
            Directory.CreateDirectory(directory);
            try
            {
                foreach(PetCue cue in Enum.GetValues(typeof(PetCue)))
                {
                    string path=Extract(cue.ToString());
                    if(cue==PetCue.Attract) { hover=new PrerolledPlayback(path); continue; }
                    var player=new MediaPlayer();
                    players.Add(cue,player);
                    player.Open(new Uri(path));
                }
                chargeRise=new PrerolledPlayback(Extract("chargerise"));
                meteorFlash=new PrerolledPlayback(Extract("meteorflash"));
                meteorFadeTimer.Tick+=AdvanceMeteorFade;
                SetVolume(percent);
                SetEnabled(sound);
            }
            catch { Dispose(); throw; }
        }

        string Extract(string cue)
        {
            string name=cue.ToLowerInvariant()+".wav";
            string path=Path.Combine(directory,name);
            using(var input=typeof(PetAudio).Assembly.GetManifestResourceStream(name))
            using(var file=File.Create(path)) input.CopyTo(file);
            return path;
        }
        public void SetVolume(int percent)
        {
            gain=percent/100.0;
            foreach(var pair in players)
                pair.Value.Volume=gain*(pair.Value==motion?motionEnvelope:1);
            hover.SetGain(gain*hoverEnvelope);
            chargeRise.SetGain(gain);
            if(meteorFlashPlayed) meteorFlash.SetGain(gain*meteorEnvelope);
        }
        public void SetEnabled(bool sound)
        {
            enabled=sound;
            if(sound) return;
            foreach(var player in players.Values) player.Stop();
            if(hoverPlaying) { hover.SetGain(0); hover.Rewind(); }
            if(chargeRisePlayed) { chargeRise.Rewind(); chargeRisePlayed=false; }
            RewindMeteor();
            motion=null; fadingMotion=false;
            hovering=hoverPlaying=false;
            hoverEnvelope=0;
        }
        public void Play(PetCue cue)
        {
            if(!enabled) return;
            FadeMotion();
            if(foreground!=null) foreground.Stop();
            foreground=players[cue]; foreground.Position=TimeSpan.Zero; foreground.Play();
        }
        public void PlayLayer(PetCue cue)
        {
            if(!enabled) return;
            var player=players[cue]; player.Position=TimeSpan.Zero; player.Play();
        }
        public void PlayChargeRise()
        {
            if(!enabled) return;
            if(chargeRisePlayed) chargeRise.Rewind();
            chargeRisePlayed=true;
            chargeRise.Play();
        }
        public void PlayMeteorFlash()
        {
            if(!enabled) return;
            RewindMeteor();
            meteorFlashPlayed=true;
            meteorEnvelope=1;
            meteorFlash.SetGain(gain);
            meteorFlash.Play();
        }
        public void StopMeteor()
        {
            if(!meteorFlashPlayed || meteorFadeTimer.IsEnabled) return;
            meteorFade.Restart();
            meteorFadeTimer.Start();
        }
        void AdvanceMeteorFade(object sender,EventArgs args)
        {
            meteorEnvelope=Math.Max(0,1-meteorFade.Elapsed.TotalSeconds/MeteorFadeSeconds);
            meteorFlash.SetGain(gain*meteorEnvelope);
            if(meteorEnvelope==0) RewindMeteor();
        }
        void RewindMeteor()
        {
            if(!meteorFlashPlayed) return;
            meteorFadeTimer.Stop();
            meteorFlash.Rewind();
            meteorFlashPlayed=false;
        }
        public void PlayRoam(Random random)
        {
            if(!enabled) return;
            int first=0;
            if(roamCuesLeft==0)
            {
                // The last sound drawn from the previous round sits at index zero.
                roamCuesLeft=roamCues.Length;
                first=1;
            }
            int index=random.Next(first,roamCuesLeft);
            PetCue cue=roamCues[index];
            roamCuesLeft--;
            roamCues[index]=roamCues[roamCuesLeft];
            roamCues[roamCuesLeft]=cue;
            PlayMotion(cue,0.38);
        }
        public void PlayMotion(PetCue cue,double fadeSeconds=0.12)
        {
            if(!enabled) return;
            if(motion!=null) motion.Stop();
            motion=players[cue]; fadingMotion=false; motionEnvelope=1; motionFadeSeconds=fadeSeconds;
            motion.Volume=gain; motion.Position=TimeSpan.Zero; motion.Play();
        }
        public void FadeMotion() { fadingMotion=motion!=null; }
        public void StartHover()
        {
            if(!enabled) return;
            FadeMotion();
            hovering=true;
            if(hoverPlaying && !hover.Ended) return;
            if(hoverPlaying) hover.Rewind();
            hoverPlaying=true; hoverEnvelope=1;
            hover.SetGain(gain); hover.Play();
        }
        public void EndHover() { hovering=false; }
        public void Advance(double dt)
        {
            if(fadingMotion)
            {
                motionEnvelope=Math.Max(0,motionEnvelope-dt/motionFadeSeconds);
                motion.Volume=gain*motionEnvelope;
                if(motionEnvelope==0) { motion.Stop(); motion=null; fadingMotion=false; }
            }
            if(!hoverPlaying || (hovering && hoverEnvelope>=1)) return;
            hoverEnvelope=Math.Max(0,Math.Min(1,hoverEnvelope+(hovering?dt/0.035:-dt/0.45)));
            hover.SetGain(gain*hoverEnvelope);
            if(hoverEnvelope==0) { hover.Rewind(); hoverPlaying=false; }
        }
        public void Dispose()
        {
            enabled=false;
            meteorFadeTimer.Stop();
            foreach(var player in players.Values) player.Close();
            if(hover!=null) hover.Dispose();
            if(chargeRise!=null) chargeRise.Dispose();
            if(meteorFlash!=null) meteorFlash.Dispose();
            try
            {
                foreach(string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
            catch(IOException) { }
        }
    }

    // A paused PCM graph already owns its renderer and samples when the cue starts.
    sealed class PrerolledPlayback : IDisposable
    {
        [ComImport,Guid("56A868B1-0AD4-11CE-B03A-0020AF0BA770"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IMediaControl
        {
            void Run(); void Pause(); void Stop(); void GetState(int timeout,out int state);
            void RenderFile([MarshalAs(UnmanagedType.BStr)]string filename);
        }
        [ComImport,Guid("56A868B2-0AD4-11CE-B03A-0020AF0BA770"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IMediaPosition { double Duration(); void Seek(double seconds); double Position(); }
        [ComImport,Guid("56A868B3-0AD4-11CE-B03A-0020AF0BA770"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IBasicAudio { void PutVolume(int hundredthsOfDb); }

        readonly Uri source;
        IMediaControl graph;
        IMediaPosition position;
        IBasicAudio volume;
        MediaPlayer fallback;
        double duration,gain;
        bool playing,ended;

        public PrerolledPlayback(string path)
        {
            source=new Uri(path);
            try
            {
                var cls=Type.GetTypeFromCLSID(new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770"));
                graph=(IMediaControl)Activator.CreateInstance(cls);
                graph.RenderFile(path);
                position=(IMediaPosition)graph; volume=(IBasicAudio)graph;
                volume.PutVolume(-10000); duration=position.Duration();
                graph.Pause(); int state; graph.GetState(150,out state);
            }
            catch(COMException) { UseFallback(); }
            catch(InvalidCastException) { UseFallback(); }
        }
        void ReleaseGraph()
        {
            if(graph==null) return;
            Marshal.FinalReleaseComObject(graph); graph=null; position=null; volume=null;
        }
        void UseFallback()
        {
            ReleaseGraph();
            fallback=new MediaPlayer { Volume=gain };
            fallback.MediaEnded+=delegate { ended=true; };
            fallback.Open(source);
            if(playing) fallback.Play(); else fallback.Pause();
        }
        bool UseGraph(Action action)
        {
            if(graph==null) return false;
            try { action(); return true; }
            catch(COMException) { UseFallback(); return false; }
        }
        public bool Ended
        {
            get { UseGraph(delegate { ended=position.Position()>=duration-0.001; }); return ended; }
        }
        public void SetGain(double value)
        {
            gain=value;
            if(!UseGraph(delegate { volume.PutVolume(value<=0?-10000:Math.Max(-10000,(int)Math.Round(2000*Math.Log10(value)))); }))
                fallback.Volume=value;
        }
        public void Play()
        {
            ended=false; playing=true;
            if(!UseGraph(delegate { graph.Run(); })) fallback.Play();
        }
        public void Rewind()
        {
            playing=false; ended=false;
            if(!UseGraph(delegate { graph.Pause(); position.Seek(0); }))
            { fallback.Pause(); fallback.Position=TimeSpan.Zero; }
        }
        public void Dispose()
        {
            ReleaseGraph();
            if(fallback!=null) fallback.Close();
        }
    }
}
