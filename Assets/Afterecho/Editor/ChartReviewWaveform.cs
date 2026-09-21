using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterecho.Editor
{
    public sealed class ChartReviewWaveform : VisualElement
    {
        JObject document;
        double from, to;
        float[] peaks;
        AudioClip clip;
        public string AudioStatus { get; private set; } = "파형을 아직 읽지 않았습니다.";
        public event Action<double> TimePicked;
        public ChartReviewWaveform()
        {
            AddToClassList("review-waveform");
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 || contentRect.width <= 0 || to <= from) return;
                TimePicked?.Invoke(from + Mathf.Clamp01(e.localPosition.x / contentRect.width) * (to - from));
                e.StopPropagation();
            });
        }
        public void SetDocument(JObject value, double start, double end)
        { document = value;from = start;to = end;MarkDirtyRepaint(); }
        public string ReadAudio(AudioClip source)
        {
            if (source == clip && peaks != null) return AudioStatus;
            peaks = null;clip = source;
            if (clip == null) return AudioStatus = "원곡 AudioClip을 찾지 못했습니다. 노트 시각만 표시합니다.";
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
                if (clip.loadState != AudioDataLoadState.Loaded)
                    return AudioStatus = "오디오 로딩 중입니다. 파형 다시 읽기를 누르세요.";
            }
            const int bins = 32768, framesPerRead = 4096;
            var data = new float[framesPerRead * clip.channels];
            var result = new float[bins];
            for (int offset = 0; offset < clip.samples; offset += framesPerRead)
            {
                if (!clip.GetData(data, offset))
                    return AudioStatus = "이 오디오 가져오기 설정에서는 PCM을 읽을 수 없습니다. 파형 없음; 실제 청음 여부와 무관합니다.";
                int frames = Math.Min(framesPerRead, clip.samples - offset);
                for (int f = 0; f < frames; f++)
                {
                    int b = (int)((long)(offset + f) * bins / clip.samples);
                    for (int ch = 0; ch < clip.channels; ch++)
                        result[b] = Mathf.Max(result[b], Mathf.Abs(data[f * clip.channels + ch]));
                }
            }
            peaks = result;MarkDirtyRepaint();
            return AudioStatus = "원곡 PCM 진폭 파형입니다. 큰 파형이 정답 타격음을 뜻하지 않으며 청음 검수는 별도입니다.";
        }
        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1 || h < 1 || to <= from) return;
            var p = ctx.painter2D;
            float X(double t) => (float)((t - from) / (to - from) * w);
            void Line(float x, float y1, float y2, Color c, float thickness = 1)
            { p.strokeColor = c;p.lineWidth = thickness;p.BeginPath();p.MoveTo(new Vector2(x, y1));p.LineTo(new Vector2(x, y2));p.Stroke(); }
            p.strokeColor = new Color(.2f,.3f,.31f);p.lineWidth = 1;
            double tick = to - from > 20 ? 5 : to - from > 8 ? 1 : .25;
            for (double t = Math.Ceiling(from / tick) * tick; t < to; t += tick)
                Line(X(t), 0, h, new Color(.2f,.3f,.31f));
            if (peaks != null && clip != null)
            {
                p.strokeColor = new Color(.54f,.69f,.67f);p.lineWidth = 1;p.BeginPath();
                for (int x = 0; x < w; x += 2)
                {
                    double time = from + x / (double)w * (to - from);
                    if (time < 0 || time > clip.length) continue;
                    int a = Mathf.Clamp((int)(time / clip.length * peaks.Length), 0, peaks.Length - 1);
                    int b = Mathf.Clamp((int)((time + 2 / (double)w * (to - from)) / clip.length * peaks.Length), a, peaks.Length - 1);
                    float peak = 0;for (int i = a; i <= b; i++) peak = Mathf.Max(peak, peaks[i]);
                    float amplitude = Mathf.Min(1, peak) * h * .24f;
                    p.MoveTo(new Vector2(x, h * .27f - amplitude));p.LineTo(new Vector2(x, h * .27f + amplitude));
                }
                p.Stroke();
            }
            if (document == null) return;
            foreach (var note in AfterechoChartReview.Notes(document))
            {
                double? raw = AfterechoChartReview.SourceTime(note), previous = AfterechoChartReview.Number(note["parentTime"]);
                double current = (double)note["time"];
                if (raw >= from && raw <= to) Line(X(raw.Value), h * .59f, h * .73f, new Color(1,.65f,.35f), 2);
                if (previous >= from && previous <= to) Line(X(previous.Value), h * .75f, h * .84f, new Color(.69f,.72f,.79f));
                if (current >= from && current <= to)
                {
                    Color c = AfterechoChartReview.State(document, note) == "rejected" ? new Color(1,.32f,.37f) : new Color(.3f,.95f,.81f);
                    Line(X(current), h * .86f, h, c, 2);
                }
            }
        }
    }
}
