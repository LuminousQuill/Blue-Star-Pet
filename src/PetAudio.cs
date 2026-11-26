using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace BlueStar
{
    enum PetCue { Charge, Resonance, Chime, Attract, Absorb, Ambient }

    sealed class PetAudio : IDisposable
    {
        readonly Dictionary<PetCue,MediaPlayer> players=new Dictionary<PetCue,MediaPlayer>();
        readonly string directory=Path.Combine(Path.GetTempPath(),"BlueStar-"+Guid.NewGuid().ToString("N"));
        MediaPlayer foreground;
        bool enabled, hovering, hoverPlaying;
        double gain, hoverEnvelope;

        public PetAudio(bool sound,int percent)
        {
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
                pair.Value.Volume=gain*(pair.Key==PetCue.Attract?hoverEnvelope:1);
        }
        public void SetEnabled(bool sound)
        {
            enabled=sound;
            if(sound) return;
            foreach(var player in players.Values) player.Stop();
            hovering=hoverPlaying=false;
            hoverEnvelope=0;
        }
        public void Play(PetCue cue)
        {
            if(!enabled) return;
            if(foreground!=null) foreground.Stop();
            foreground=players[cue]; foreground.Position=TimeSpan.Zero; foreground.Play();
        }
        public void StartHover()
        {
            if(!enabled) return;
            hovering=true;
            if(hoverPlaying) return;
            hoverPlaying=true; hoverEnvelope=1;
            var player=players[PetCue.Attract];
            player.Volume=gain; player.Position=TimeSpan.Zero; player.Play();
        }
        public void EndHover() { hovering=false; }
        public void Advance(double dt)
        {
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
