using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Afterecho.Editor
{
    // One persistent Editor callback; CLI status reads do not recompile or reload the game.
    public static class AfterechoMusicMovementQA
    {
        static AfterechoGame game;
        static readonly List<string> checks=new List<string>();
        static readonly List<object> samples=new List<object>();
        static string state="idle";
        static double deadline,nextSample,pausedTime,maxDrift;
        static int pauseMisses,movementBefore,reducedBefore;
        static bool pausedOnce,resumedChecked,reducedDone,interruptedOnce,endAudio;
        static bool sawLift,sawStretch,sawGhost;
        static float originalSync;
        public static object Status()=>new{state,checks=checks.ToArray(),maxDrift,sampleCount=samples.Count,game=game!=null?game.Snapshot():null};
        [MenuItem("Afterecho/Run Music and Movement Playtest")]
        public static string Start()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Enter Play mode before starting this test.");
            EditorApplication.update-=Tick;
            game=UnityEngine.Object.FindFirstObjectByType<AfterechoGame>();
            checks.Clear();samples.Clear();maxDrift=0;nextSample=0;
            pausedOnce=resumedChecked=reducedDone=interruptedOnce=endAudio=false;sawLift=sawStretch=sawGhost=false;
            originalSync=game.syncMs;game.syncMs=0;game.autoPlay=true;game.invincible=false;game.reducedMotion=false;game.loop=false;
            Application.runInBackground=true;game.Menu();game.StartGame();state="tutorial";
            deadline=EditorApplication.timeSinceStartup+25;EditorApplication.update+=Tick;
            return "started";
        }
        static void Check(bool ok,string message)
        {if(!ok)throw new Exception(message);checks.Add("PASS "+message);}
        static void Tick()
        {
            try
            {
                if(game==null || !EditorApplication.isPlaying)throw new Exception("Play mode ended before verification completed");
                double now=EditorApplication.timeSinceStartup;
                if(now>deadline)throw new Exception("Timed out in "+state);
                if(state=="tutorial" && game.Phase==GamePhase.Ready)
                {
                    Check(!game.clock.music.isPlaying,"tutorial ends with music stopped");
                    pausedTime=game.LogicalTime;state="ready-wait";deadline=now+10;nextSample=now+1;
                }
                else if(state=="ready-wait" && now>=nextSample)
                {
                    Check(game.LogicalTime==pausedTime,"ready screen does not consume music time");
                    game.BeginMain();Check(game.LogicalTime==0 && game.clock.Position==0,"main music and chart reset together to zero");
                    Check(!game.IsTutorial && game.Run.Hits==0 && game.Run.Steps==0,"main starts with fresh counters");
                    state="count-in";deadline=now+10;
                }
                else if(state=="count-in")
                {
                    CheckOnce(game.Run.Misses==0 && game.Run.Damage==0,"count-in cannot cause misses or damage");
                    if(game.Phase==GamePhase.Playing){Check(game.clock.SongTime<.5,"main begins at song opening");state="main";deadline=now+160;nextSample=0;}
                }
                else if(state=="pause-wait" && now>=nextSample)
                {
                    Check(game.clock.SongTime==pausedTime && game.Run.Misses==pauseMisses,"pause freezes music and chart");
                    Check(game.view.playerVisual.localPosition.sqrMagnitude<.0001f && (game.view.playerVisual.localScale-Vector3.one).sqrMagnitude<.0001f,"pause resets Feel movement pose");
                    game.ResumeGame();state="main";
                }
                else if(state=="interrupt-wait")
                {
                    if(game.Phase==GamePhase.Paused)
                    {Check(game.Run.Misses==pauseMisses,"unexpected audio stop pauses without unfair miss");game.ResumeGame();state="main";}
                }
                if(state!="main")return;
                if(game.Phase==GamePhase.Playing)
                {
                    double t=game.clock.SongTime;
                    double drift=Math.Abs(game.clock.music.time-t);maxDrift=Math.Max(maxDrift,drift);
                    sawLift|=game.view.playerVisual.localPosition.y>.04f;
                    sawStretch|=Math.Abs(game.view.playerVisual.localScale.y-1)>.08f;
                    sawGhost|=game.GetComponentsInChildren<SpriteRenderer>().Any(r=>r.enabled && r.name.StartsWith("StepAfterimage_") && r.color.a>.01f);
                    if(now>=nextSample){samples.Add(new{phase=game.Phase.ToString(),song=t,audio=game.clock.music.time,drift,game.Run.Misses,game.Run.Steps});nextSample=now+2;}
                    if(!pausedOnce && t>20)
                    {
                        game.PauseGame();pausedTime=game.clock.SongTime;pauseMisses=game.Run.Misses;
                        pausedOnce=true;state="pause-wait";nextSample=now+.6;return;
                    }
                    if(pausedOnce && !resumedChecked && t>23)
                    {Check(game.Run.Misses==0,"four-beat resume preserves next note");resumedChecked=true;}
                    if(!reducedDone && t>35)
                    {
                        if(!game.reducedMotion){game.reducedMotion=true;reducedBefore=game.view.movementFeedback.PlayedSteps;movementBefore=game.Run.Steps;}
                        if(t>40)
                        {
                            Check(game.Run.Steps>movementBefore && game.view.movementFeedback.PlayedSteps==reducedBefore,"reduced motion keeps steps and suppresses movement effects");
                            Check(game.view.playerVisual.localPosition.sqrMagnitude<.0001f,"reduced motion restores visual position");
                            game.reducedMotion=false;reducedDone=true;
                        }
                    }
                    if(!interruptedOnce && t>50)
                    {pauseMisses=game.Run.Misses;game.clock.music.Stop();interruptedOnce=true;state="interrupt-wait";return;}
                    if(t>133 && t<135){CheckOnce(game.clock.music.isPlaying,"original song still playing at the final door");endAudio=true;}
                }
                if(game.Phase==GamePhase.Result)
                {
                    Check(game.Run.Status==RunStatus.Won && game.Run.Kills==6,"real-time full-song run clears six encounters");
                    Check(game.Run.Hits==game.Run.Chart.notes.Length && game.Run.Misses==0,"all notes resolve once across pause and interruption");
                    Check(endAudio && game.LogicalTime>=136.36 && game.LogicalTime<136.6,"music reaches the stage ending at 136 seconds");
                    Check(maxDrift<.15,"observed audio and DSP time stay within 150 ms");
                    Check(sawLift && sawStretch && sawGhost,"Feel lift, body stretch, and afterimage observed in motion");
                    game.Menu();game.StartGame();Check(game.LogicalTime==0 && game.Run.Hits==0 && game.clock.Position==0,"retry returns audio and counters to zero");
                    game.Menu();game.autoPlay=false;game.syncMs=originalSync;state="passed";Finish();
                }
            }
            catch(Exception e){checks.Add("FAIL "+e.Message);state="failed";if(game!=null){game.PauseGame();game.autoPlay=false;game.syncMs=originalSync;}Finish();}
        }
        static void CheckOnce(bool ok,string text){if(!checks.Contains("PASS "+text))Check(ok,text);else if(!ok)throw new Exception(text);}
        static void Finish()
        {
            EditorApplication.update-=Tick;
            Directory.CreateDirectory("PlaytestExports");
            File.WriteAllText("PlaytestExports/music-movement-live.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{state,checks,maxDrift,samples},Newtonsoft.Json.Formatting.Indented));
            Debug.Log("AFTERECHO MUSIC MOVEMENT QA: "+state+" ("+checks.Count+" checks)");
        }
    }
}
