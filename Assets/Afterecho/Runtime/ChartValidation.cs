using System;
using System.Collections.Generic;

namespace Afterecho
{
    /// <summary>Shared validation for imported documents and playback, independent of Editor UI.</summary>
    public static class ChartValidation
    {
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

        public static void ValidateForPlayback(ChartData chart, StageData stage, RunnerRules runner = null)
        {
            Validate(chart);
            Require(stage != null, "stage", "is required");
            Positive(stage.duration, "stage.duration");
            Positive(stage.beat, "stage.beat");
            Require(stage.beats != null && stage.beats.Length > 0, "stage.beats", "is required");
            Require(stage.listenBeats >= 0 && stage.listenBeats < stage.beats.Length, "stage.listenBeats", "must index the beat list");
            Require(stage.mainBeat >= 0 && stage.mainBeat < stage.beats.Length, "stage.mainBeat", "must index the beat list");
            for (int i = 0; i < stage.beats.Length; i++)
                Require(Finite(stage.beats[i]) && stage.beats[i] >= 0 && (i == 0 || stage.beats[i] > stage.beats[i - 1]), "stage.beats", "must be finite, nonnegative and strictly increasing");
            Require(chart.notes[chart.notes.Length - 1].time <= stage.duration, "notes", "must end within the stage");

            if (runner != null)
            {
                Require(runner.maxHealth > 0, "runner.maxHealth", "must be greater than zero");
                Require(runner.hitRecovery >= 0 && runner.missDamage >= 0 && runner.extraDamage >= 0, "runner.healthChanges", "must not be negative");
                Require(runner.boostCombo > 0, "runner.boostCombo", "must be greater than zero");
                Positive(runner.lapSeconds, "runner.lapSeconds");
                Positive(runner.boostSpeed, "runner.boostSpeed");
                Positive(runner.previewSeconds, "runner.previewSeconds");
                Positive(runner.fallSeconds, "runner.fallSeconds");
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
