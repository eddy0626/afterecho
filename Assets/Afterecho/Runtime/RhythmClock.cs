using System;
using System.Collections;
using UnityEngine;
namespace Afterecho
{
    public sealed class RhythmClock : MonoBehaviour
    {
        public AudioSource music;
        public AudioSource[] countVoices;
        public AudioClip click;
        public double ScheduledStart { get; private set; }
        public double Anchor { get; private set; }
        public double Position { get; private set; }
        public bool Scheduled { get; private set; }
        public string LoadError { get; private set; }
        Coroutine loading;
        bool warming, muteBeforeWarmup;
        // The WebGL runner reads the actual Web Audio node position through its plugin.
        // Use it for the runner so frame stalls cannot drift the chart away from music.
        public bool UsePlaybackPosition { get; set; }
        #if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern double AfterechoWeb_MusicState(double duration,int mode);
        #endif
        public double PlaybackPosition
        {
            get {
                #if UNITY_WEBGL && !UNITY_EDITOR
                if(UsePlaybackPosition)return AfterechoWeb_MusicState(music.clip.length,0);
                #endif
                return music.time;
            }
        }
        public double PlaybackRate
        {
            get {
                #if UNITY_WEBGL && !UNITY_EDITOR
                if(UsePlaybackPosition)return AfterechoWeb_MusicState(music.clip.length,1);
                #endif
                return music.pitch;
            }
        }
        bool MusicActive
        {
            get {
                #if UNITY_WEBGL && !UNITY_EDITOR
                if(UsePlaybackPosition)return PlaybackPosition>-900;
                #endif
                return music.isPlaying;
            }
        }
        double observedPosition, observedAt, scheduledWallStart, lastPositionAdvance;
        bool observedPlayback, silentTail;
        public bool HasStarted => Scheduled && (silentTail ? PreparationRemaining<=0 : UsePlaybackPosition
            ? observedPlayback || (MusicActive && PlaybackPosition>Position+.0001)
            : AudioSettings.dspTime>=ScheduledStart);
        public double PreparationRemaining => Math.Max(0,UsePlaybackPosition
            ? scheduledWallStart-Time.realtimeSinceStartupAsDouble : ScheduledStart-AudioSettings.dspTime);
        public bool StartTimedOut => Scheduled && !HasStarted && Time.realtimeSinceStartupAsDouble>scheduledWallStart+2;
        public double DspSongTime => Scheduled ? Position + Math.Max(0,AudioSettings.dspTime-ScheduledStart) : Position;
        public double SongTime
        {
            get
            {
                if(!Scheduled)return Position;
                if(!UsePlaybackPosition)return DspSongTime;
                if(silentTail)return Position+Math.Max(0,Time.realtimeSinceStartupAsDouble-scheduledWallStart);
                if(!HasStarted)return Position;
                double now=Time.realtimeSinceStartupAsDouble;
                if(MusicActive)
                {
                    double t=PlaybackPosition;
                    if(t>=Position-.03)
                    {if(!observedPlayback||t>observedPosition+.0001)lastPositionAdvance=now;
                        observedPosition=Math.Max(Position,t);observedAt=now;observedPlayback=true;return observedPosition;}
                }
                // AudioSource.time resets after natural completion. Continue from the last
                // observed tail sample so positive sync offsets can finish their judgement
                // timeline. Early interruptions still freeze and pause instead of advancing.
                if(observedPlayback && observedPosition>=music.clip.length-.35)
                    return observedPosition+Math.Max(0,now-observedAt);
                return observedPlayback?observedPosition:DspSongTime;
            }
        }
        public double EventSongTime(double inputTime) => UsePlaybackPosition
            ? SongTime + inputTime - Time.realtimeSinceStartupAsDouble
            : AudioSettings.dspTime + inputTime - Time.realtimeSinceStartupAsDouble - Anchor;
        public bool PlaybackInterrupted => Scheduled && !silentTail && AudioSettings.dspTime > ScheduledStart + .35
            && SongTime < music.clip.length - .25 && (!MusicActive ||
                (UsePlaybackPosition && observedPlayback && Time.realtimeSinceStartupAsDouble-lastPositionAdvance>.8));
        public void Schedule(double position, double beat, bool countIn)
        {
            Stop(position); LoadError = null;
            loading = StartCoroutine(LoadAndSchedule(position,beat,countIn));
        }
        IEnumerator LoadAndSchedule(double position,double beat,bool countIn)
        {
            if (music == null || music.clip == null || (countIn && click == null))
            { LoadError = "음원을 찾을 수 없습니다. 처음으로 돌아가 다시 시작해 주세요."; yield break; }
            AudioClip song = music.clip;
            if (song.loadState == AudioDataLoadState.Unloaded) song.LoadAudioData();
            if (countIn && click.loadState == AudioDataLoadState.Unloaded) click.LoadAudioData();
            double waitStarted = Time.realtimeSinceStartupAsDouble;
            // Web audio decoding is asynchronous. Neither chart nor count-in starts before it is ready.
            while (song.loadState != AudioDataLoadState.Loaded || (countIn && click.loadState != AudioDataLoadState.Loaded))
            {
                if (song.loadState == AudioDataLoadState.Failed || (countIn && click.loadState == AudioDataLoadState.Failed)
                    || Time.realtimeSinceStartupAsDouble - waitStarted > 20)
                { LoadError = "음악을 불러오지 못했습니다. 다시 시도해 주세요."; yield break; }
                yield return null;
            }
            // A positive sync offset can leave a short judgement tail after the song.
            // Resume that tail after the count-in without seeking to an unobservable final sample.
            silentTail=position>=song.length;
            // Prime the browser channel before scheduling the audible count-in. WebGL can
            // report Loaded before its first AudioSource channel is ready for scheduling.
            if(UsePlaybackPosition&&!silentTail)
            {
                warming=true;muteBeforeWarmup=music.mute;music.mute=true;
                music.time=0;music.Play();
                double warmStarted=Time.realtimeSinceStartupAsDouble;
                while(!MusicActive)
                {
                    if(Time.realtimeSinceStartupAsDouble-warmStarted>10)
                    {music.Stop();music.mute=muteBeforeWarmup;warming=false;LoadError="음악을 불러오지 못했습니다. 다시 시도해 주세요.";yield break;}
                    yield return null;
                }
                music.Stop();music.mute=muteBeforeWarmup;warming=false;
            }
            Position = silentTail ? position : Math.Clamp(position,0,Math.Max(0,song.length-1d/song.frequency));
            observedPosition=Position;observedPlayback=false;observedAt=Time.realtimeSinceStartupAsDouble;
            double preparation = AudioSettings.dspTime + .25;
            ScheduledStart = preparation + (countIn ? 4 * beat : 0);
            scheduledWallStart=Time.realtimeSinceStartupAsDouble+.25+(countIn?4*beat:0);
            Anchor = ScheduledStart - Position;
            music.loop = false;
            // Seek in seconds: browser decoding can use a different sample rate from the import.
            if(!silentTail)
            {
                music.time = (float)Position;
                music.PlayScheduled(ScheduledStart);
            }
            // WebGL runner pitch is fixed on the actual Web Audio node by RunnerAudioClock.
            if(!UsePlaybackPosition)music.pitch=1f;
            if (countIn) for (int i = 0; i < Math.Min(4,countVoices.Length); i++)
            { countVoices[i].clip = click; countVoices[i].volume = i == 0 ? .32f : .22f; countVoices[i].PlayScheduled(preparation + i * beat); }
            Scheduled = true; loading = null;
        }
        public void Stop(double position)
        {
            if (loading != null) { StopCoroutine(loading); loading = null; }
            if (music != null) { music.Stop();if(warming){music.mute=muteBeforeWarmup;warming=false;} }
            if (countVoices != null) foreach (var voice in countVoices) if (voice != null) voice.Stop();
            Position = position; Scheduled = false;silentTail=false;
        }
    }
}
