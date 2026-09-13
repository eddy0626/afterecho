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
        public double SongTime => Scheduled ? Position + Math.Max(0,AudioSettings.dspTime-ScheduledStart) : Position;
        public double EventSongTime(double inputTime) => AudioSettings.dspTime + inputTime - Time.realtimeSinceStartupAsDouble - Anchor;
        public bool PlaybackInterrupted => Scheduled && AudioSettings.dspTime > ScheduledStart + .35
            && SongTime < music.clip.length - .25 && !music.isPlaying;
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
            Position = Math.Clamp(position,0,Math.Max(0,song.length-1d/song.frequency));
            double preparation = AudioSettings.dspTime + .25;
            ScheduledStart = preparation + (countIn ? 4 * beat : 0);
            Anchor = ScheduledStart - Position;
            music.loop = false; music.pitch = 1;
            // Seek in seconds: browser decoding can use a different sample rate from the import.
            music.time = (float)Position;
            music.PlayScheduled(ScheduledStart);
            if (countIn) for (int i = 0; i < Math.Min(4,countVoices.Length); i++)
            { countVoices[i].clip = click; countVoices[i].volume = i == 0 ? .32f : .22f; countVoices[i].PlayScheduled(preparation + i * beat); }
            Scheduled = true; loading = null;
        }
        public void Stop(double position)
        {
            if (loading != null) { StopCoroutine(loading); loading = null; }
            if (music != null) music.Stop();
            if (countVoices != null) foreach (var voice in countVoices) if (voice != null) voice.Stop();
            Position = position; Scheduled = false;
        }
    }
}
