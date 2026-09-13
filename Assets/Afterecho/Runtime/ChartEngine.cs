using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Afterecho
{
    [Serializable] public class SongInfo { public string id, title, sha256; public double duration; }
    [Serializable] public class ChartRules { public double minGap, maxWindow, fastGap, recoveryGap; public int health, burstLimit; }
    [Serializable] public class ChartNote { public string id, source, reviewStatus, teamReview, section, role, encounter; public double time, originalTime, window; }
    [Serializable] public class EncounterData { public string id; public int beat, hp; public double warning, start, end; public string[] noteIds; }
    [Serializable] public class ChartData
    {
        public string format, preset;
        public SongInfo song;
        public ChartRules rules;
        public ChartNote[] notes;
        public EncounterData[] encounters;
        public static ChartData Load(string json) => JsonUtility.FromJson<ChartData>(json);
    }
    [Serializable] public class StageSection { public int fromBeat; public string name, code; }
    [Serializable] public class RoutePoint { public float x, y; }
    [Serializable] public class StageData
    {
        public double bpm, beat, duration, doorTime, previewSeconds;
        public int listenBeats, mainBeat;
        public double[] beats;
        public RoutePoint[] route;
        public StageSection[] sections;
        public double MainTime => beats[mainBeat];
    }
    public enum RunStatus { Running, Arrived, Won, Lost }
    public enum NoteState { Pending, Hit, Missed, Skipped }
    public enum EnemyState { Pending, Warning, Active, Killed, Escaped, Skipped }
    public sealed class EnemyRun
    {
        public EncounterData data;
        public int remaining;
        public EnemyState state;
        public bool damageApplied;
        public double lastValidInput = double.NegativeInfinity;
    }
    public struct RunEvent
    {
        public string kind, noteId, enemyId;
        public bool practice;
        public int index;
        public double error;
        public RunEvent(string k, int i = -1, string n = null, string e = null, bool p = false, double err = 0)
        { kind = k; index = i; noteId = n; enemyId = e; practice = p; error = err; }
    }
    [Serializable] public class InputLog { public double time, targetTime, error, window; public string noteId, result, source; }

    // Pure deterministic state. No animation, AudioSource, Time or MonoBehaviour dependency.
    public sealed class ChartEngine
    {
        public readonly ChartData Chart;
        public readonly StageData Stage;
        public readonly NoteState[] Decisions;
        public readonly EnemyRun[] Enemies;
        public readonly List<InputLog> Inputs = new List<InputLog>();
        public readonly Queue<RunEvent> Events = new Queue<RunEvent>();
        public RunStatus Status { get; private set; } = RunStatus.Running;
        public int Combo, Best, Score, Hits, Misses, Extras, Damage, Kills, Steps, PracticeHits;
        public int Multiplier => Combo >= 24 ? 4 : Combo >= 8 ? 2 : 1;
        public int Health => Math.Max(0, Chart.rules.health - Damage);
        public double Time { get; private set; }
        public bool Invincible;
        public bool PracticeEnabled = true;
        public int NextIndex { get; private set; }
        double lastInput = double.NegativeInfinity;
        readonly double[] windows;
        readonly int[] enemyForNote;
        public ChartEngine(ChartData chart, StageData stage)
        {
            Chart = chart; Stage = stage;
            Decisions = new NoteState[chart.notes.Length]; windows = new double[chart.notes.Length];
            Enemies = chart.encounters.Select(e => new EnemyRun { data = e, remaining = e.hp }).ToArray();
            enemyForNote = new int[chart.notes.Length];
            for (int i = 0; i < chart.notes.Length; i++)
            {
                windows[i] = Window(chart, i);
                enemyForNote[i] = Array.FindIndex(Enemies, e => e.data.noteIds.Contains(chart.notes[i].id));
                if (enemyForNote[i] >= 0)
                    Enemies[enemyForNote[i]].lastValidInput = Math.Max(Enemies[enemyForNote[i]].lastValidInput,chart.notes[i].time + windows[i]);
            }
        }
        public static double Window(ChartData c, int i)
        {
            double left = i == 0 ? double.PositiveInfinity : c.notes[i].time - c.notes[i - 1].time;
            double right = i + 1 == c.notes.Length ? double.PositiveInfinity : c.notes[i + 1].time - c.notes[i].time;
            return Math.Min(c.rules.maxWindow, .4 * Math.Min(left, right));
        }
        public double WindowAt(int i) => windows[i];
        public int NextPending()
        { for (int i = NextIndex; i < Decisions.Length; i++) if (Decisions[i] == NoteState.Pending) return i; return -1; }
        public EnemyRun VisibleEnemy => Enemies.FirstOrDefault(e => e.state == EnemyState.Warning || e.state == EnemyState.Active);
        public void Seek(double t)
        {
            Time = t; lastInput = double.NegativeInfinity;
            for (int i = 0; i < Decisions.Length; i++) if (Chart.notes[i].time < t) Decisions[i] = NoteState.Skipped;
            NextIndex = Array.FindIndex(Chart.notes, n => n.time >= t);
            if (NextIndex < 0) NextIndex = Decisions.Length;
            foreach (var e in Enemies) if (e.data.end <= t) e.state = EnemyState.Skipped;
        }
        public void Advance(double t)
        {
            Time = t;
            if (Status == RunStatus.Running)
            {
                while (NextIndex < Decisions.Length && Chart.notes[NextIndex].time + windows[NextIndex] < t - 1e-9)
                { if (Decisions[NextIndex] == NoteState.Pending) Resolve(NextIndex, false, 0); NextIndex++; }
                foreach (var e in Enemies)
                {
                    if (e.state == EnemyState.Pending && t >= e.data.warning)
                    { e.state = EnemyState.Warning; Events.Enqueue(new RunEvent("warning", e: e.data.id)); }
                    if (e.state == EnemyState.Warning && t >= e.data.start)
                    { e.state = EnemyState.Active; Events.Enqueue(new RunEvent("enemy-start", e: e.data.id)); }
                    // A valid late hit on the last note must resolve before the attack deadline.
                    if (e.state == EnemyState.Active && t >= e.data.end && t > e.lastValidInput + 1e-9 && !e.damageApplied)
                    {
                        e.state = EnemyState.Escaped; e.damageApplied = true;
                        if (!Invincible) Damage++;
                        Events.Enqueue(new RunEvent("damage", e: e.data.id));
                        if (Health == 0) { Status = RunStatus.Lost; Events.Enqueue(new RunEvent("lost")); break; }
                    }
                }
                if (Status == RunStatus.Running && t >= Stage.doorTime)
                { Status = RunStatus.Arrived; Events.Enqueue(new RunEvent("door")); }
            }
            if (Status == RunStatus.Arrived && t >= Stage.duration)
            { Status = RunStatus.Won; Events.Enqueue(new RunEvent("won")); }
        }
        public string Tap(double t, string source = "manual")
        {
            Advance(t);
            if (Status != RunStatus.Running) return Log(t, -1, "ended", source);
            if (t < Stage.beats[Stage.listenBeats] - .1) return Log(t, -1, "listen", source);
            if (t - lastInput < .025) return Log(t, -1, "bounce", source);
            lastInput = t;
            int closest = -1; double distance = double.PositiveInfinity;
            for (int i = NextIndex; i < Decisions.Length; i++)
            {
                if (Chart.notes[i].time > t + Chart.rules.maxWindow + 1e-9) break;
                double d = Math.Abs(Chart.notes[i].time - t);
                if (Decisions[i] == NoteState.Pending && d < distance) { closest = i; distance = d; }
            }
            if (closest >= 0 && distance <= windows[closest] + 1e-9)
            { Resolve(closest, true, t - Chart.notes[closest].time); return Log(t, closest, "hit", source); }
            Extras++; Combo = 0; Events.Enqueue(new RunEvent("extra", p: PracticeEnabled && t < Stage.MainTime));
            return Log(t, closest, "extra", source);
        }
        string Log(double t, int i, string result, string source)
        {
            Inputs.Add(new InputLog { time = t, result = result, source = source, noteId = i < 0 ? "" : Chart.notes[i].id,
                targetTime = i < 0 ? -1 : Chart.notes[i].time, error = i < 0 ? 0 : t - Chart.notes[i].time, window = i < 0 ? 0 : windows[i] });
            return result;
        }
        void Resolve(int i, bool hit, double error)
        {
            if (Decisions[i] != NoteState.Pending) return;
            Decisions[i] = hit ? NoteState.Hit : NoteState.Missed;
            var n = Chart.notes[i]; bool practice = PracticeEnabled && n.time < Stage.MainTime;
            string kind = hit ? "step" : "miss"; string enemyId = null;
            if (practice) { if (hit) PracticeHits++; }
            else if (hit)
            {
                Hits++; Combo++; Best = Math.Max(Best, Combo); Score += 100 * Multiplier;
                int ei = enemyForNote[i]; var e = ei >= 0 ? Enemies[ei] : null;
                if (e != null && e.state != EnemyState.Killed && e.state != EnemyState.Escaped && e.state != EnemyState.Skipped)
                {
                    e.state = EnemyState.Active; e.remaining--; kind = "attack"; enemyId = e.data.id;
                    if (e.remaining == 0) { e.state = EnemyState.Killed; Kills++; Events.Enqueue(new RunEvent("kill", e: e.data.id)); }
                }
                else Steps++;
            }
            else { Misses++; Combo = 0; }
            Events.Enqueue(new RunEvent(kind, i, n.id, enemyId, practice, error));
        }
        public double SafeResume(double at)
        { int n = NextPending(); return Math.Max(0, Math.Min(at, n < 0 ? at : Chart.notes[n].time - windows[n] - .12)); }
        public void PrepareResume(double at) { Time = at; lastInput = double.NegativeInfinity; }
    }
}
