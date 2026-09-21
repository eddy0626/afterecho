using System;
using UnityEditor;
using UnityEngine;

namespace Afterecho.Editor
{
    // Pre-rendered comparison clicks avoid Editor update jitter changing the audition.
    public sealed class ChartReviewAudition : IDisposable
    {
        GameObject root;
        AudioSource music, clicks;
        AudioClip rendered;
        double end;
        Action<string> status;
        Func<bool> sessionValid;
        string label;
        public bool IsPlaying => root != null;
        public static float[] RenderClicks(int rate, double beat, double from, double to, double[] noteTimes)
        {
            if (rate <= 0 || rate > 192000 || double.IsNaN(beat) || double.IsInfinity(beat)
                || double.IsNaN(from) || double.IsInfinity(from) || double.IsNaN(to) || double.IsInfinity(to)
                || beat <= 0 || beat > 60 || from < 0 || to <= from || to - from > 600 || noteTimes == null)
                throw new ArgumentException("Invalid audition sample range.");
            var data = new float[(int)Math.Ceiling((beat * 4 + to - from + .03) * rate)];
            void Add(double at, float gain)
            {
                int start = (int)Math.Round(at * rate), length = (int)(rate * .025);
                for (int j = 0; j < length && start + j < data.Length; j++)
                {
                    double t = j / (double)rate;
                    data[start + j] += gain * (float)(Math.Sin(Math.PI * 2 * 1500 * t) * Math.Exp(-t * 210));
                }
            }
            for (int i = 0; i < 4; i++) Add(i * beat, .24f);
            foreach (double time in noteTimes)
            {
                if (double.IsNaN(time) || double.IsInfinity(time) || time < from || time > to)
                    throw new ArgumentException("A/B click lies outside the shared audition range.");
                Add(beat * 4 + time - from, .32f);
            }
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -.8f, .8f);
            return data;
        }
        public void Play(AudioClip song, double beat, double from, double to, double[] noteTimes, string name,
            Action<string> report, Func<bool> canContinue)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Unity Play 모드에서 A/B 청음을 사용하세요. 게임 판정은 일시정지됩니다.");
            if (song == null) throw new InvalidOperationException("원곡 AudioClip이 없습니다.");
            if (from < 0 || to <= from || to > song.length || beat <= 0) throw new ArgumentException("비교 구간 또는 박자 값이 올바르지 않습니다.");
            if (song.loadState != AudioDataLoadState.Loaded)
            {
                song.LoadAudioData();
                if (song.loadState != AudioDataLoadState.Loaded) throw new InvalidOperationException("원곡 로딩 중입니다. 잠시 후 청음을 다시 시작하세요.");
            }
            Stop();
            const int rate = 48000;
            float[] data = RenderClicks(rate, beat, from, to, noteTimes);
            try
            {
                status = report;label = name;sessionValid = canContinue;
                root = new GameObject("ChartLab_Audition_Only") { hideFlags = HideFlags.HideAndDontSave };
                rendered = AudioClip.Create("ChartLab_SampleAccurateClicks", data.Length, 1, rate, false);
                rendered.hideFlags = HideFlags.HideAndDontSave;rendered.SetData(data, 0);
                music = root.AddComponent<AudioSource>();music.playOnAwake = false;music.spatialBlend = 0;music.clip = song;
                clicks = root.AddComponent<AudioSource>();clicks.playOnAwake = false;clicks.spatialBlend = 0;clicks.clip = rendered;
                double now = AudioSettings.dspTime + .15, start = now + beat * 4;
                end = start + to - from;
                clicks.PlayScheduled(now);clicks.SetScheduledEndTime(end);
                music.time = (float)from;music.PlayScheduled(start);music.SetScheduledEndTime(end);
                EditorApplication.update += Tick;EditorApplication.playModeStateChanged += PlayModeChanged;
                status?.Invoke($"{label} · 4박 후 공통 범위 {from:F3}–{to:F3}초 · 동일 {noteTimes.Length}개 AI 노트 · 합성 비교 클릭 · 게임 판정 없음");
            }
            catch { Stop();throw; }
        }
        void Tick()
        {
            if (!EditorApplication.isPlaying || root == null || (sessionValid != null && !sessionValid()))
            { Stop();status?.Invoke("게임 상태가 바뀌어 비교 청음을 정지했습니다.");return; }
            if (AudioSettings.dspTime >= end)
            { string done = label;Stop();status?.Invoke(done + " 비교 재생 종료. 이것만으로 검수 승인되지 않습니다."); }
        }
        void PlayModeChanged(PlayModeStateChange state)
        { if (state == PlayModeStateChange.ExitingPlayMode) Stop(); }
        public void Stop()
        {
            EditorApplication.update -= Tick;EditorApplication.playModeStateChanged -= PlayModeChanged;
            if (music != null) music.Stop();if (clicks != null) clicks.Stop();
            music = null;clicks = null;
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (rendered != null) UnityEngine.Object.DestroyImmediate(rendered);
            root = null;rendered = null;
        }
        public void Dispose() => Stop();
    }
}
