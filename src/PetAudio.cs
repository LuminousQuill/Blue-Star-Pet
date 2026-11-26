using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace BlueStar
{
    enum PetCue { Charge, Resonance, Chime, Attract, Absorb, Ambient, Appear, Disappear, Roam, RoamUndertow, RoamEcho, RoamVeil, Blink, Constellation }

    sealed class PetAudio : IDisposable
    {
        readonly Dictionary<PetCue,MediaPlayer> players=new Dictionary<PetCue,MediaPlayer>();
        readonly PetCue[] roamCues={PetCue.Roam,PetCue.RoamUndertow,PetCue.RoamEcho,PetCue.RoamVeil};
        int roamCuesLeft;
        readonly string directory=Path.Combine(Path.GetTempPath(),"BlueStar-"+Guid.NewGuid().ToString("N"));
        MediaPlayer foreground, motion;
        bool enabled, hovering, hoverPlaying, fadingMotion;
        double gain, hoverEnvelope, motionEnvelope=1, motionFadeSeconds;

        public PetAudio(bool sound,int percent)
        {
            roamCuesLeft=roamCues.Length;
            Directory.CreateDirectory(directory);
            try
            {
                foreach(PetCue cue in Enum.GetValues(typeof(PetCue)))
                {
                    string name=cue.ToString().ToLowerInvariant()+".wav";
                    string path=Path.Combine(directory,name);
                    using(var input=typeof(PetAudio).Assembly.GetManifestResourceStream(name))
                    using(var file=File.Create(path)) input.CopyTo(file);
                    var player=new MediaPlayer();
                    players.Add(cue,player);
                    player.Open(new Uri(path));
                }
                players[PetCue.Attract].MediaEnded+=delegate { hoverPlaying=false; };
                SetVolume(percent);
                SetEnabled(sound);
            }
            catch { Dispose(); throw; }
        }

        public void SetVolume(int percent)
        {
            gain=percent/100.0;
            foreach(var pair in players)
                pair.Value.Volume=gain*(pair.Key==PetCue.Attract?hoverEnvelope:pair.Value==motion?motionEnvelope:1);
        }
        public void SetEnabled(bool sound)
        {
            enabled=sound;
            if(sound) return;
            foreach(var player in players.Values) player.Stop();
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
            if(hoverPlaying) return;
            hoverPlaying=true; hoverEnvelope=1;
            var player=players[PetCue.Attract];
            player.Volume=gain; player.Position=TimeSpan.Zero; player.Play();
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
            // Preserve the source fade-in. Fade from the current level on exit or quick re-entry.
            hoverEnvelope=Math.Max(0,Math.Min(1,hoverEnvelope+(hovering?1:-1)*dt/0.45));
            var player=players[PetCue.Attract];
            player.Volume=gain*hoverEnvelope;
            if(hoverEnvelope==0) { player.Stop(); hoverPlaying=false; }
        }
        public void Dispose()
        {
            enabled=false;
            foreach(var player in players.Values) player.Close();
            try
            {
                foreach(string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
            catch(IOException) { }
        }
    }
}
