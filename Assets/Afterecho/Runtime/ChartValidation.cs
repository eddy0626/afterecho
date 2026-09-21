using System;
using System.Collections.Generic;

namespace Afterecho
{
    /// <summary>Shared validation for imported documents and playback, independent of Editor UI.</summary>
    public static class ChartValidation
    {
        // Matches the current RunnerSceneBuilder approach-ring pool.
        public const int RunnerRingCapacity = 12;
        static void Require(bool condition, string field, string message)
        {
            if (!condition) throw new FormatException("Invalid chart " + field + ": " + message);
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static void Positive(double value, string field) => Require(Finite(value) && value > 0, field, "must be finite and greater than zero");

        public static void Validate(ChartData chart)
        {
            Require(chart != null, "document", "is required");
            Require(chart.format == "afterecho-chart-v1", "format", "expected afterecho-chart-v1");
            Require(!string.IsNullOrWhiteSpace(chart.preset), "preset", "is required");
            Require(chart.song != null, "song", "is required");
            Require(!string.IsNullOrWhiteSpace(chart.song.id), "song.id", "is required");
            Positive(chart.song.duration, "song.duration");
            Require(chart.rules != null, "rules", "is required");
            Positive(chart.rules.maxWindow, "rules.maxWindow");
            Positive(chart.rules.minGap, "rules.minGap");
            Positive(chart.rules.fastGap, "rules.fastGap");
            Positive(chart.rules.recoveryGap, "rules.recoveryGap");
            Require(chart.rules.health > 0, "rules.health", "must be greater than zero");
            Require(chart.rules.burstLimit > 0, "rules.burstLimit", "must be greater than zero");
            // Zero means the optional runner metadata was absent in an older document.
            Require(Finite(chart.rules.previewSeconds) && chart.rules.previewSeconds >= 0, "rules.previewSeconds", "must be zero (default) or finite and positive");
            Require(chart.rules.maxVisibleNotes >= 0 && chart.rules.maxVisibleNotes <= RunnerRingCapacity, "rules.maxVisibleNotes", "must be zero (default) or between one and " + RunnerRingCapacity);
            Require(chart.notes != null && chart.notes.Length > 0, "notes", "must contain at least one note");

            var ids = new HashSet<string>();
            for (int i = 0; i < chart.notes.Length; i++)
            {
                var note = chart.notes[i];
                string field = "notes[" + i + "]";
                Require(note != null, field, "is required");
                Require(!string.IsNullOrWhiteSpace(note.id) && ids.Add(note.id), field + ".id", "must be nonempty and unique");
                Require(Finite(note.time) && note.time >= 0 && note.time <= chart.song.duration, field + ".time", "must be finite and within the song");
                if (i > 0) Require(note.time > chart.notes[i - 1].time, field + ".time", "notes must be in strictly increasing order");
            }
        }

        static void ValidateStage(ChartData chart, StageData stage)
        {
            Require(stage != null, "stage", "is required");
            Positive(stage.duration, "stage.duration");
            Positive(stage.beat, "stage.beat");
            Require(stage.beats != null && stage.beats.Length > 0, "stage.beats", "is required");
            Require(stage.listenBeats >= 0 && stage.listenBeats < stage.beats.Length, "stage.listenBeats", "must index the beat list");
            Require(stage.mainBeat >= 0 && stage.mainBeat < stage.beats.Length, "stage.mainBeat", "must index the beat list");
            for (int i = 0; i < stage.beats.Length; i++)
                Require(Finite(stage.beats[i]) && stage.beats[i] >= 0 && (i == 0 || stage.beats[i] > stage.beats[i - 1]), "stage.beats", "must be finite, nonnegative and strictly increasing");
            Require(chart.notes[chart.notes.Length - 1].time <= stage.duration, "notes", "must end within the stage");
        }

        public static double RunnerPreviewSeconds(ChartData chart, double? previewSeconds = null)
        {
            double preview = previewSeconds ?? (chart.rules.previewSeconds > 0 ? chart.rules.previewSeconds : 1.2);
            Positive(preview, "runner.previewSeconds");
            return preview;
        }

        public static int RunnerVisibleNoteLimit(ChartData chart)
            => chart.rules.maxVisibleNotes > 0 ? chart.rules.maxVisibleNotes : chart.preset == "easy" ? 4 : 5;

        /// <summary>
        /// Worst-case visible pending notes. A missed note remains pending through its
        /// inclusive late window, while another note can already enter the preview.
        /// Count closed intervals [time - preview, time + actual judgement window].
        /// </summary>
        public static int MaxVisiblePendingNotes(ChartData chart, double previewSeconds)
        {
            Validate(chart);
            Positive(previewSeconds, "runner.previewSeconds");
            var events = new List<KeyValuePair<double, int>>(chart.notes.Length * 2);
            for (int i = 0; i < chart.notes.Length; i++)
            {
                events.Add(new KeyValuePair<double, int>(chart.notes[i].time - previewSeconds, 1));
                events.Add(new KeyValuePair<double, int>(chart.notes[i].time + ChartEngine.Window(chart, i), -1));
            }
            // Entries precede exits at the same instant: both rings are visible at
            // an inclusive judgement boundary, even if this happens for one frame.
            events.Sort((a, b) => a.Key == b.Key ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
            int visible = 0, maximum = 0;
            foreach (var item in events)
            {
                visible += item.Value;
                maximum = Math.Max(maximum, visible);
            }
            return maximum;
        }

        public static void ValidateRunnerChart(ChartData chart, StageData stage, double? previewSeconds = null)
        {
            Validate(chart);
            ValidateStage(chart, stage);
            Require(chart.gameplayMode == "runner", "gameplayMode", "expected runner");
            int burst = 1;
            for (int i = 0; i < chart.notes.Length; i++)
            {
                var note = chart.notes[i];
                Require(note.time >= 1.2 && note.time <= stage.duration - .12, "note " + note.id, "must allow a 1.2 second introduction and finish before the song tail");
                if (i == 0) continue;
                double gap = note.time - chart.notes[i - 1].time;
                Require(gap >= chart.rules.minGap - 1e-7, "note " + note.id, "minimum interval violated");
                Require(burst < chart.rules.burstLimit || gap >= chart.rules.recoveryGap - 1e-7, "note " + note.id, "burst recovery interval violated");
                burst = gap < chart.rules.fastGap ? burst + 1 : 1;
            }
            double preview = RunnerPreviewSeconds(chart, previewSeconds);
            int maximum = MaxVisiblePendingNotes(chart, preview), limit = RunnerVisibleNoteLimit(chart);
            Require(maximum <= limit, "runner.visibleNotes", maximum + " simultaneous pending rings exceeds " + limit + " at preview " + preview + " seconds (including late judgement windows)");
        }

        public static void ValidateForPlayback(ChartData chart, StageData stage, RunnerRules runner = null)
        {
            Validate(chart);
            ValidateStage(chart, stage);

            if (runner != null)
            {
                Require(runner.maxHealth > 0, "runner.maxHealth", "must be greater than zero");
                Require(runner.hitRecovery >= 0 && runner.missDamage >= 0 && runner.extraDamage >= 0, "runner.healthChanges", "must not be negative");
                Require(runner.boostCombo > 0, "runner.boostCombo", "must be greater than zero");
                Positive(runner.lapSeconds, "runner.lapSeconds");
                Positive(runner.boostSpeed, "runner.boostSpeed");
                Positive(runner.previewSeconds, "runner.previewSeconds");
                Positive(runner.fallSeconds, "runner.fallSeconds");
                ValidateRunnerChart(chart, stage, runner.previewSeconds);
                return;
            }

            Require(Finite(stage.doorTime) && stage.doorTime > 0 && stage.doorTime <= stage.duration, "stage.doorTime", "must be within the stage");
            // The lab rebuilds encounter membership after editing notes. Validate these
            // derived references only when the completed document enters playback.
            var knownNotes = new HashSet<string>();
            foreach (var note in chart.notes) knownNotes.Add(note.id);
            var enemyIds = new HashSet<string>();
            var assignedNotes = new HashSet<string>();
            foreach (var enemy in chart.encounters ?? Array.Empty<EncounterData>())
            {
                Require(enemy != null, "encounters", "must not contain null entries");
                Require(!string.IsNullOrWhiteSpace(enemy.id) && enemyIds.Add(enemy.id), "encounter.id", "must be nonempty and unique");
                Require(Finite(enemy.warning) && Finite(enemy.start) && Finite(enemy.end) && enemy.warning >= 0 && enemy.warning <= enemy.start && enemy.start < enemy.end && enemy.end <= stage.duration, "encounter " + enemy.id, "invalid warning/start/end times");
                Require(enemy.noteIds != null && enemy.noteIds.Length > 0 && enemy.hp > 0 && enemy.hp <= enemy.noteIds.Length, "encounter " + enemy.id, "requires notes and attainable positive health");
                foreach (string id in enemy.noteIds)
                    Require(id != null && knownNotes.Contains(id) && assignedNotes.Add(id), "encounter " + enemy.id + ".noteIds", "must reference existing notes assigned to only one encounter");
            }
        }
    }
}
