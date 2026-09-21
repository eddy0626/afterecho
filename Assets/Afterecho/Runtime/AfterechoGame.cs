using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using DG.Tweening;

namespace Afterecho
{
    public enum GamePhase { Menu, Preparing, Playing, Ready, Paused, Result }
    public sealed class AfterechoGame : MonoBehaviour
    {
        public RhythmClock clock;
        public AfterechoView view;
        [Header("Playtest controls (live)")] public bool autoPlay, invincible;
        public bool reducedMotion;
        [Range(-250,250)] public float syncMs;
        [Range(0,1)] public float volume = .65f;
        public bool loop;
        public double loopStart = 21.86, loopEnd = 31.18;
        public string difficulty = "easy";
        public GamePhase Phase { get; private set; } = GamePhase.Menu;
        public ChartEngine Run { get; private set; }
        public StageData Stage { get; private set; }
        public double LogicalTime { get; private set; }
        public string CurrentMode = "chart";
        public bool IsTutorial => !passedPractice;
        InputAction tapAction, pauseAction;
        readonly List<PendingTap> pending = new List<PendingTap>();
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        struct PendingTap { public double time; public string source; }
        bool passedPractice, countIn, focusPaused;
        double resumeAt;
        float lastVolume = -1;
        void Awake()
        {
            Application.targetFrameRate = 120;
            Stage = JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
            DOTween.Init(false, true, LogBehaviour.ErrorsOnly).SetCapacity(128,32);
            syncMs = PlayerPrefs.GetFloat("Afterecho.Sync",0); volume = PlayerPrefs.GetFloat("Afterecho.Volume",.65f);
            tapAction = new InputAction("Tap", InputActionType.Button);
            tapAction.AddBinding("<Keyboard>/space"); tapAction.AddBinding("<Keyboard>/f"); tapAction.AddBinding("<Keyboard>/j");
            tapAction.AddBinding("<Mouse>/leftButton"); tapAction.AddBinding("<Touchscreen>/primaryTouch/press");
            tapAction.performed += OnTap;
            pauseAction = new InputAction("Pause",InputActionType.Button,"<Keyboard>/escape");
            pauseAction.performed += _ => { if (Phase == GamePhase.Playing || Phase == GamePhase.Preparing) PauseGame(); };
            view.Initialize(this);
            NewEngine(); view.ShowMenu();
        }
        void OnEnable()
        {
            tapAction?.Enable(); pauseAction?.Enable();
            AudioSettings.OnAudioConfigurationChanged += OnAudioChanged;
        }
        void OnDisable()
        {
            tapAction?.Disable(); pauseAction?.Disable();
            AudioSettings.OnAudioConfigurationChanged -= OnAudioChanged;
            if (clock != null) clock.Stop(LogicalTime);
        }
        void OnDestroy() { tapAction?.Dispose(); pauseAction?.Dispose(); }
        void OnAudioChanged(bool changed) { if (Phase == GamePhase.Playing || Phase == GamePhase.Preparing) PauseGame(); }
        void OnApplicationPause(bool paused) { if (!Application.isEditor && paused) PauseGame(); }
        void OnApplicationFocus(bool focused)
        {
            if (!Application.isEditor && !focused && (Phase == GamePhase.Playing || Phase == GamePhase.Preparing)) { focusPaused = true; PauseGame(); }
            if (focused && focusPaused) focusPaused = false;
        }
        void NewEngine(ChartData replacement = null)
        {
            var chart = replacement ?? ChartData.Load(Resources.Load<TextAsset>("Afterecho/Charts/" + difficulty).text);
            Run = new ChartEngine(chart,Stage); Run.Invincible = invincible;
        }
        public void SelectDifficulty(string preset) { difficulty = preset; NewEngine(); view.RefreshMenu(); }
        public void StartGame()
        {
            clock.Stop(0); NewEngine(); LogicalTime = 0; passedPractice = false; pending.Clear();
            view.ResetWorld(); view.HideOverlay(); Schedule(0,false);
        }
        void Schedule(double at,bool preparation)
        {
            pending.Clear(); resumeAt = at; Run.PrepareResume(at); LogicalTime = at;
            clock.Schedule(preparation && at > 0 ? Math.Max(0,at+syncMs/1000d) : at,Stage.beat,preparation); countIn = preparation; Phase = GamePhase.Preparing;
            view.HideOverlay(); view.StopEffects();
        }
        public void BeginMain()
        {
            if (Phase != GamePhase.Ready) return;
            // Tutorial is a preview run. The real run and original song share a fresh zero epoch.
            clock.Stop(0); NewEngine(); Run.PracticeEnabled = false; passedPractice = true;
            view.ResetWorld(); Schedule(0,true);
        }
        public void PauseGame()
        {
            if (Phase != GamePhase.Playing && Phase != GamePhase.Preparing) return;
            // Pending, timestamped input is consumed before advancing the omission deadline.
            if (Phase == GamePhase.Playing)
            {
                ProcessTaps(); LogicalTime = clock.SongTime - syncMs / 1000d;
                AutoTapUntil(LogicalTime); Run.Advance(LogicalTime); Consume();
            }
            if (Phase == GamePhase.Result) return;
            resumeAt = Phase == GamePhase.Preparing ? resumeAt : Run.SafeResume(LogicalTime);
            LogicalTime = resumeAt; clock.Stop(resumeAt); pending.Clear(); Phase = GamePhase.Paused;
            view.StopEffects(); view.ShowPause();
        }
        public void ResumeGame() { if (Phase == GamePhase.Paused) Schedule(resumeAt,true); }
        public void Menu()
        {
            clock.Stop(0); pending.Clear(); Phase = GamePhase.Menu; LogicalTime = 0;
            view.StopEffects(); view.ResetWorld(); NewEngine(); view.ShowMenu();
        }
        public void SetVolume(float value) { volume = value; PlayerPrefs.SetFloat("Afterecho.Volume",volume); }
        public void SetSync(float value) { syncMs = value; PlayerPrefs.SetFloat("Afterecho.Sync",syncMs); }
        void OnTap(InputAction.CallbackContext ctx)
        {
            if (Phase != GamePhase.Playing || autoPlay || (!Application.isEditor && Screen.height > Screen.width)) return;
            bool pointer = ctx.control.device is Pointer;
            if (pointer && EventSystem.current != null)
            {
                Vector2 pos = ctx.control.device is Touchscreen ts ? ts.primaryTouch.position.ReadValue() : Mouse.current.position.ReadValue();
                var p = new PointerEventData(EventSystem.current) { position = pos }; uiHits.Clear(); EventSystem.current.RaycastAll(p,uiHits);
                if (uiHits.Count != 0) return;
            }
            pending.Add(new PendingTap { time = clock.EventSongTime(ctx.time) - syncMs / 1000d, source = ctx.control.device.displayName });
        }
        void ProcessTaps()
        {
            pending.Sort((a,b) => a.time.CompareTo(b.time));
            foreach (var p in pending) Run.Tap(p.time,p.source);
            pending.Clear();
        }
        void AutoTapUntil(double time)
        {
            if (!autoPlay) return;
            int i;
            while (Run.Status == RunStatus.Running && (i = Run.NextPending()) >= 0 && Run.Chart.notes[i].time <= time)
                Run.Tap(Run.Chart.notes[i].time,"autoplay");
        }
        void Update()
        {
            if (Stage == null || Run == null) return;
            if (Math.Abs(lastVolume - volume) > .0001) { AudioListener.volume = volume; lastVolume = volume; }
            if (!Application.isEditor && Screen.height > Screen.width && (Phase == GamePhase.Playing || Phase == GamePhase.Preparing)) PauseGame();
            if (Phase == GamePhase.Preparing)
            {
                if (!string.IsNullOrEmpty(clock.LoadError))
                {
                    Phase = GamePhase.Paused; view.ShowAudioProblem(clock.LoadError); return;
                }
                if (clock.Scheduled && AudioSettings.dspTime >= clock.ScheduledStart) Phase = GamePhase.Playing;
                else if (!clock.Scheduled) view.countText.text = "AUDIO LOADING";
                else view.ShowCount(countIn ? Mathf.Clamp((int)Math.Ceiling((clock.ScheduledStart - AudioSettings.dspTime) / Stage.beat),1,4) : 0);
            }
            if (Phase == GamePhase.Playing)
            {
                // A stopped source must never leave the chart running silently to an enemy deadline.
                if (clock.PlaybackInterrupted)
                {
                    resumeAt=Run.SafeResume(LogicalTime); LogicalTime=resumeAt;
                    clock.Stop(resumeAt); pending.Clear(); Phase=GamePhase.Paused; view.StopEffects();
                    view.ShowAudioProblem("음악 재생이 중단되었습니다. 4박 준비 후 같은 위치에서 이어집니다."); return;
                }
                LogicalTime = clock.SongTime - syncMs / 1000d;
                double gate = Stage.MainTime - .18;
                if (!passedPractice && LogicalTime >= gate)
                {
                    ProcessTaps(); AutoTapUntil(gate); Run.Advance(gate); Consume(); resumeAt = Run.SafeResume(gate); LogicalTime = resumeAt;
                    clock.Stop(resumeAt); Phase = GamePhase.Ready; view.StopEffects(); view.ShowReady();
                }
                else
                {
                    Run.Invincible = invincible;
                    ProcessTaps();
                    AutoTapUntil(LogicalTime);
                    Run.Advance(LogicalTime); Consume();
                    if (loop && LogicalTime >= loopEnd && Phase == GamePhase.Playing) LabSeek(loopStart,Run.Chart);
                }
            }
            view.Render(LogicalTime,Phase);
        }
        void Consume()
        {
            while (Run.Events.Count > 0)
            {
                var e = Run.Events.Dequeue(); view.React(e);
                if (e.kind == "lost" || e.kind == "won") { clock.Stop(LogicalTime); Phase = GamePhase.Result; view.ShowResult(e.kind == "won"); }
            }
        }
        public void LabSeek(double at, ChartData chart = null)
        {
            clock.Stop(at); NewEngine(chart); Run.PracticeEnabled = false; Run.Seek(at); passedPractice = true; view.ResetWorld(); Schedule(at,true);
        }
        public void DebugTap(double songTime) { Run.Tap(songTime,"test"); Consume(); }
        public object Snapshot() => new { phase = Phase.ToString(), time = LogicalTime, status = Run.Status.ToString(), health=Run.Health,
            Run.Hits,Run.Misses,Run.Extras,Run.Combo,Run.Best,Run.Kills,Run.Steps,Run.Score, difficulty, audio=clock.music.isPlaying,
            pitch=clock.music.pitch, autoPlay, tutorial=IsTutorial, audioPosition=clock.music.time,
            songTime=clock.SongTime, clipDuration=clock.music.clip.length, activeTweens=DOTween.TotalActiveTweens() };
    }
}
