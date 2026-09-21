using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.EventSystems;

namespace Afterecho
{
    public sealed class RunnerGame : MonoBehaviour
    {
        public RunnerRules rules;
        public RhythmClock clock;
        public RunnerView view;
        public string difficulty="easy";
        public bool autoPlay, invincible, reducedMotion, loop;
        public double loopStart=20, loopEnd=32;
        [Range(-250,250)] public float syncMs;
        [Range(0,1)] public float volume=.65f;
        public GamePhase Phase { get; private set; } = GamePhase.Menu;
        public ChartEngine Run { get; private set; }
        public StageData Stage { get; private set; }
        public double LogicalTime { get; private set; }
        public bool IsTutorial { get; private set; }
        public float ActiveSeconds { get; private set; }
        public int AcceptedInputs { get; private set; }
        InputAction input;
        readonly HashSet<InputControl> held=new HashSet<InputControl>();
        readonly List<Pending> queue=new List<Pending>();
        readonly List<RaycastResult> ui=new List<RaycastResult>();
        struct Pending { public double time; public string source; }
        double resumeAt,lastTouch=-100,lastMouse=-100;
        float lastVolume=-1;
        bool diagnostics;double nextDiagnostic;GamePhase lastDiagnosticPhase;
        void Awake()
        {
            Application.targetFrameRate=120;
            Application.runInBackground=true;
            Stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
            syncMs=PlayerPrefs.GetFloat("Afterecho.Sync",0);volume=PlayerPrefs.GetFloat("Afterecho.Volume",.65f);
            reducedMotion=PlayerPrefs.GetInt("Afterecho.ReducedMotion",0)!=0;
            input=new InputAction("RunnerTap",InputActionType.PassThrough);
            input.AddBinding("<Keyboard>/*");input.AddBinding("<Mouse>/leftButton");input.AddBinding("<Touchscreen>/primaryTouch/press");
            input.performed+=Input;
            #if !UNITY_EDITOR
            string url=Application.absoluteURL;
            diagnostics=url.Contains("debug=1");
            #if UNITY_WEBGL
            clock.UsePlaybackPosition=true;
            #endif
            autoPlay=url.Contains("autoplay=1");invincible=url.Contains("invincible=1");
            if(url.Contains("difficulty=normal"))difficulty="normal";else if(url.Contains("difficulty=hard"))difficulty="hard";
            #endif
            NewRun();view.Initialize(this);view.Menu();
        }
        int lastViewportWidth,lastViewportHeight;
        [UnityEngine.Scripting.Preserve]
        public void ResizeViewport(string dimensions)
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
            var parts=dimensions.Split(',');
            if(parts.Length!=2||!int.TryParse(parts[0],out int w)||!int.TryParse(parts[1],out int h)||w<160||h<160||w>8192||h>8192)return;
            if(w==lastViewportWidth&&h==lastViewportHeight)return;
            lastViewportWidth=w;lastViewportHeight=h;
            if(diagnostics)Debug.Log($"RUNNER_VIEWPORT screen={Screen.width}x{Screen.height} request={w}x{h}");
            Screen.SetResolution(w,h,Screen.fullScreenMode);
            #endif
        }
        void OnEnable(){input?.Enable();AudioSettings.OnAudioConfigurationChanged+=AudioChanged;}
        void OnDisable(){input?.Disable();AudioSettings.OnAudioConfigurationChanged-=AudioChanged;clock?.Stop(LogicalTime);}
        void OnDestroy(){input?.Dispose();}
        void AudioChanged(bool changed){PauseGame();}
        void OnApplicationPause(bool paused){if(paused&&!Application.isEditor)PauseGame();}
        void OnApplicationFocus(bool focused){if(!focused&&!Application.isEditor)PauseGame();}
        public void NewRun(ChartData replacement=null)
        {
            var chart=replacement??ChartData.Load(Resources.Load<TextAsset>("Afterecho/RunnerCharts/"+difficulty).text);
            Run=new ChartEngine(chart,Stage,rules){Invincible=invincible,PracticeEnabled=IsTutorial};
        }
        public void SelectDifficulty(string value){difficulty=value;NewRun();view.Menu();}
        public void StartRun(bool tutorial=false)
        {
            IsTutorial=tutorial;clock.Stop(0);NewRun();ActiveSeconds=0;AcceptedInputs=0;
            view.ResetRun();Schedule(0,true);
        }
        public void BeginMain(){StartRun(false);}
        void Schedule(double at,bool prepare)
        {
            queue.Clear();resumeAt=at;LogicalTime=at;Run.PrepareResume(at);
            clock.Schedule(at>0?Math.Max(0,at+syncMs/1000d):0,Stage.beat,prepare);
            Phase=GamePhase.Preparing;view.HideOverlay();view.StopFeedback();
        }
        public void PauseGame()
        {
            if(Phase!=GamePhase.Playing&&Phase!=GamePhase.Preparing)return;
            if(Phase==GamePhase.Playing)
            { RefreshSongTime();ProcessInputs();AutoUntil(LogicalTime);Run.Advance(LogicalTime);Consume(); }
            if(Phase==GamePhase.Result)return;
            resumeAt=Phase==GamePhase.Preparing?resumeAt:Run.SafeResume(LogicalTime);
            LogicalTime=resumeAt;clock.Stop(resumeAt);queue.Clear();Phase=GamePhase.Paused;view.StopFeedback();view.Pause();
        }
        public void ResumeGame(){if(Phase==GamePhase.Paused)Schedule(resumeAt,true);}
        public void Menu()
        {clock.Stop(0);queue.Clear();LogicalTime=0;ActiveSeconds=0;IsTutorial=false;Phase=GamePhase.Menu;NewRun();view.StopFeedback();view.ResetRun();view.Menu();}
        public void SetVolume(float v){volume=v;PlayerPrefs.SetFloat("Afterecho.Volume",v);}
        public void SetSync(float v){syncMs=v;PlayerPrefs.SetFloat("Afterecho.Sync",v);}
        public void SetReducedMotion(bool value){reducedMotion=value;PlayerPrefs.SetInt("Afterecho.ReducedMotion",value?1:0);if(value)view.StopFeedback();}
        void Input(InputAction.CallbackContext ctx)
        {
            if(!(ctx.control is ButtonControl))return;
            if(ctx.control.device is Keyboard&&!(ctx.control is KeyControl))return;
            bool pressed=ctx.ReadValue<float>()>.5f;
            if(!pressed){held.Remove(ctx.control);return;}
            if(!held.Add(ctx.control))return;
            if(ctx.control is KeyControl key)
            {
                if(key.keyCode==Key.Escape){PauseGame();return;}
                if(!IsGameplayKey(key.keyCode))return;
                var k=Keyboard.current;
                if(k!=null&&(k.ctrlKey.isPressed||k.altKey.isPressed||k.leftMetaKey.isPressed||k.rightMetaKey.isPressed))return;
            }
            if(Phase!=GamePhase.Playing||autoPlay||(!Application.isEditor&&Screen.height>Screen.width))return;
            if(ctx.control.device is Touchscreen){lastTouch=ctx.time;if(ctx.time-lastMouse<.08)return;}
            if(ctx.control.device is Mouse){lastMouse=ctx.time;if(ctx.time-lastTouch<.08)return;}
            if(ctx.control.device is Pointer)
            {
                Vector2 pos=ctx.control.device is Touchscreen ts?ts.primaryTouch.position.ReadValue():Mouse.current.position.ReadValue();
                if(EventSystem.current!=null)
                {
                    var p=new PointerEventData(EventSystem.current){position=pos};ui.Clear();EventSystem.current.RaycastAll(p,ui);
                    foreach(var hit in ui)if(hit.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>()!=null)return;
                }
            }
            queue.Add(new Pending{time=clock.EventSongTime(ctx.time)-syncMs/1000d,source=ctx.control.path});AcceptedInputs++;
        }
        public static bool IsGameplayKey(Key key)
        {
            return key!=Key.None&&key!=Key.Escape&&key!=Key.Tab&&key!=Key.LeftShift&&key!=Key.RightShift
                &&key!=Key.LeftCtrl&&key!=Key.RightCtrl&&key!=Key.LeftAlt&&key!=Key.RightAlt
                &&key!=Key.LeftMeta&&key!=Key.RightMeta&&key!=Key.CapsLock&&key!=Key.NumLock
                &&key!=Key.ScrollLock&&!(key>=Key.F1&&key<=Key.F12)&&key!=Key.PrintScreen&&key!=Key.Pause;
        }
        void ProcessInputs(){queue.Sort((a,b)=>a.time.CompareTo(b.time));foreach(var p in queue)Run.Tap(p.time,p.source);queue.Clear();}
        void RefreshSongTime()
        {
            double playbackTime=clock.SongTime;
            LogicalTime=playbackTime-syncMs/1000d;
            // A negative judgement offset must not cut the final audio short.
            Run.DeferCompletion=playbackTime<Stage.duration;
        }
        void AutoUntil(double t)
        {if(!autoPlay)return;int i;while(Run.Status==RunStatus.Running&&(i=Run.NextPending())>=0&&Run.Chart.notes[i].time<=t)Run.Tap(Run.Chart.notes[i].time,"autoplay");}
        void Update()
        {
            if(Run==null)return;
            if(Math.Abs(volume-lastVolume)>.0001f){AudioListener.volume=volume;lastVolume=volume;}
            if(!Application.isEditor&&Screen.height>Screen.width)PauseGame();
            if(Phase==GamePhase.Preparing)
            {
                if(!string.IsNullOrEmpty(clock.LoadError)){Phase=GamePhase.Paused;view.AudioProblem(clock.LoadError);}
                else if(clock.StartTimedOut){Phase=GamePhase.Paused;clock.Stop(resumeAt);view.AudioProblem("음악이 중단됐어요. 4박 준비 후 이어갑니다.");}
                else if(clock.HasStarted)Phase=GamePhase.Playing;
                else view.Count(clock.Scheduled?Mathf.Clamp((int)Math.Ceiling(clock.PreparationRemaining/Stage.beat),1,4):0);
            }
            if(Phase==GamePhase.Playing)
            {
                if(clock.PlaybackInterrupted)
                {
                    resumeAt=Run.SafeResume(LogicalTime);LogicalTime=resumeAt;clock.Stop(resumeAt);queue.Clear();Phase=GamePhase.Paused;
                    view.StopFeedback();view.AudioProblem("음악이 중단됐어요. 4박 준비 후 이어갑니다.");return;
                }
                double previous=LogicalTime;RefreshSongTime();
                ActiveSeconds+=(float)Math.Max(0,LogicalTime-previous);
                Run.Invincible=invincible;
                double gate=Stage.MainTime-.18;
                ProcessInputs();AutoUntil(IsTutorial?Math.Min(LogicalTime,gate):LogicalTime);
                Run.Advance(IsTutorial?Math.Min(LogicalTime,gate):LogicalTime);Consume();
                if(IsTutorial&&LogicalTime>=gate&&Phase==GamePhase.Playing)
                {clock.Stop(0);queue.Clear();Phase=GamePhase.Ready;view.StopFeedback();view.Ready();}
                if(loop&&LogicalTime>=loopEnd&&Phase==GamePhase.Playing)LabSeek(loopStart,Run.Chart);
                if(Phase==GamePhase.Playing)view.AdvanceRunner((float)Math.Max(0,LogicalTime-previous));
            }
            view.Render();
            if(diagnostics&&(Time.realtimeSinceStartupAsDouble>=nextDiagnostic||Phase!=lastDiagnosticPhase))
            {
                nextDiagnostic=Time.realtimeSinceStartupAsDouble+10;lastDiagnosticPhase=Phase;
                Debug.Log(FormattableString.Invariant($"RUNNER_CLOCK phase={Phase} logical={LogicalTime:F3} dsp={clock.DspSongTime:F3} audio={clock.music.time:F3} web={clock.PlaybackPosition:F3} rate={clock.PlaybackRate:F4} duration={clock.music.clip.length:F3} playing={clock.music.isPlaying} hp={Run.Health} hits={Run.Hits} misses={Run.Misses} extras={Run.Extras}"));
            }
        }
        void Consume()
        {
            while(Run.Events.Count>0)
            {
                var e=Run.Events.Dequeue();view.React(e);
                if(e.kind=="lost"||e.kind=="won")
                {clock.Stop(LogicalTime);queue.Clear();Phase=GamePhase.Result;view.StopFeedback();view.Result(e.kind=="won");}
            }
        }
        public void LabSeek(double at,ChartData chart=null)
        {IsTutorial=false;clock.Stop(at);NewRun(chart);Run.Seek(at);ActiveSeconds=0;view.ResetRun();Schedule(at,true);}
        public void DebugTap(double at){Run.Tap(at,"test");Consume();}
        public object Snapshot()=>new{phase=Phase.ToString(),time=LogicalTime,health=Run.Health,Run.Hits,Run.Misses,Run.Extras,Run.Combo,Run.Best,Run.Score,
            status=Run.Status.ToString(),boost=Run.Boosted,runnerSide=view.CurrentSide,lap=view.LapPosition,falling=view.Falling,audio=clock.music.isPlaying,
            audioPosition=clock.music.time,songTime=clock.SongTime,pitch=clock.music.pitch,autoPlay,invincible,difficulty,IsTutorial,AcceptedInputs};
    }
}
