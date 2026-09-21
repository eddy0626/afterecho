// Run through eval_file in the runner scene's Play mode. Finishes asynchronously in ~4 seconds.
if(!UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Enter Play mode first");
var g=UnityEngine.Object.FindFirstObjectByType<Afterecho.RunnerGame>();
if(g==null)throw new System.Exception("Runner scene is required");
var clock=g.clock;
var lines=new System.Collections.Generic.List<string>();
System.Action<bool,string> check=(ok,label)=>{if(!ok)throw new System.Exception(label);lines.Add("PASS "+label);};
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var type=typeof(Afterecho.RhythmClock);
var phase=typeof(Afterecho.RunnerGame).GetProperty("Phase");
string originalDifficulty=g.difficulty;
float originalSync=g.syncMs,originalVolume=g.volume,originalListenerVolume=UnityEngine.AudioListener.volume;
bool originalAuto=g.autoPlay,originalLoop=g.loop,originalInvincible=g.invincible,originalPlayback=clock.UsePlaybackPosition;
double originalFrom=g.loopStart,originalTo=g.loopEnd;
double started=UnityEditor.EditorApplication.timeSinceStartup,pausedTime=0,resumedAt=0;
double pausedLap=0,pausedActive=0;
int step=0,totalNotes=0;
bool observedPreparation=false,finished=false;
UnityEditor.EditorApplication.CallbackFunction tick=null;
System.Action<System.Exception> finish=error=>{
    if(finished)return;finished=true;
    if(tick!=null)UnityEditor.EditorApplication.update-=tick;
    if(error!=null)lines.Add("FAIL "+error.Message);
    try
    {
        if(g!=null)
        {
            g.Menu();clock.UsePlaybackPosition=originalPlayback;
            g.difficulty=originalDifficulty;g.syncMs=originalSync;g.volume=originalVolume;
            g.autoPlay=originalAuto;g.loop=originalLoop;g.invincible=originalInvincible;
            g.loopStart=originalFrom;g.loopEnd=originalTo;g.Menu();
            UnityEngine.AudioListener.volume=originalListenerVolume;
        }
    }
    catch(System.Exception cleanup){lines.Add("FAIL cleanup: "+cleanup.Message);error=error??cleanup;}
    System.IO.Directory.CreateDirectory("PlaytestExports/Runner");
    System.IO.File.WriteAllLines("PlaytestExports/Runner/review-tail-resume-checks.txt",lines);
    if(error==null)UnityEngine.Debug.Log("Runner EOF pause/resume regression passed: "+lines.Count+" checks");
    else UnityEngine.Debug.LogError("Runner EOF pause/resume regression failed: "+error);
};
try
{
    g.Menu();g.difficulty="easy";g.syncMs=250;g.autoPlay=true;g.loop=false;g.invincible=false;g.volume=0;
    UnityEngine.AudioListener.volume=0;clock.UsePlaybackPosition=true;g.NewRun();
    foreach(var note in g.Run.Chart.notes)g.Run.Tap(note.time,"tail-regression-setup");
    totalNotes=g.Run.Chart.notes.Length;g.Run.Events.Clear();
    // This is the real observed-EOF state: audio ended, but +250ms leaves 120ms of judgement time.
    clock.Stop(0);
    type.GetField("<Scheduled>k__BackingField",flags).SetValue(clock,true);
    type.GetField("<ScheduledStart>k__BackingField",flags).SetValue(clock,UnityEngine.AudioSettings.dspTime-2);
    type.GetField("observedPlayback",flags).SetValue(clock,true);
    type.GetField("observedPosition",flags).SetValue(clock,(double)clock.music.clip.length);
    double playbackTime=g.Stage.duration-.12+g.syncMs/1000d;
    type.GetField("observedAt",flags).SetValue(clock,UnityEngine.Time.realtimeSinceStartupAsDouble-(playbackTime-clock.music.clip.length));
    phase.SetValue(g,Afterecho.GamePhase.Playing);
    check(clock.SongTime>clock.music.clip.length&&!clock.music.isPlaying,"fixture is an observed post-audio judgement tail");
    g.PauseGame();pausedTime=g.LogicalTime;pausedLap=g.view.LapPosition;pausedActive=g.ActiveSeconds;
    check(g.Phase==Afterecho.GamePhase.Paused&&g.Run.Status==Afterecho.RunStatus.Running,"EOF tail can pause before completion");
    check(pausedTime+g.syncMs/1000d>clock.music.clip.length,"resume request lies beyond the final audio sample");
    tick=()=>{
        try
        {
            if(g==null||!UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Play mode ended during regression");
            double now=UnityEditor.EditorApplication.timeSinceStartup;
            if(now-started>10)throw new System.Exception("EOF resume timeout at step "+step+" / "+g.Phase);
            if(clock.music.isPlaying)throw new System.Exception("Finished song was replayed during EOF resume");
            if(step==0)
            {
                if(now-started<.35)return;
                check(g.LogicalTime==pausedTime&&clock.SongTime==pausedTime&&g.view.LapPosition==pausedLap&&g.ActiveSeconds==pausedActive,"paused music time, judgement and runner stay frozen");
                g.ResumeGame();
                check(g.Phase==Afterecho.GamePhase.Preparing,"EOF resume enters the preparation phase");
                step=1;return;
            }
            if(!string.IsNullOrEmpty(clock.LoadError))throw new System.Exception(clock.LoadError);
            if(clock.StartTimedOut||clock.PlaybackInterrupted)throw new System.Exception("Intentional silent tail was treated as audio failure");
            if(g.Phase==Afterecho.GamePhase.Paused)throw new System.Exception("Resume fell back to the paused/audio-error screen");
            if(step==1)
            {
                if(!clock.Scheduled)return;
                resumedAt=now+clock.PreparationRemaining-.25-4*g.Stage.beat;
                check(clock.PreparationRemaining>=4*g.Stage.beat-.10&&clock.PreparationRemaining<=4*g.Stage.beat+.30,"resume retains all four preparation beats");
                check(clock.Position>clock.music.clip.length,"silent resume preserves the requested tail position");
                step=2;
            }
            if(!observedPreparation&&now-resumedAt>.15&&now-resumedAt<4*g.Stage.beat-.05)
            {
                check(g.Phase==Afterecho.GamePhase.Preparing&&g.LogicalTime==pausedTime,"judgement stays frozen during the four-beat preparation");
                observedPreparation=true;
            }
            if(g.Phase==Afterecho.GamePhase.Result)
            {
                check(observedPreparation&&now-resumedAt>=4*g.Stage.beat,"completion follows the full preparation interval");
                check(g.Run.Status==Afterecho.RunStatus.Won&&g.Run.Health==100&&g.Run.Hits==totalNotes&&g.Run.Misses==0&&g.Run.Extras==0,"silent tail resumes to a clean win without extra judgements");
                check(!clock.music.isPlaying&&string.IsNullOrEmpty(clock.LoadError),"no song replay or load error during EOF resume");
                finish(null);
            }
        }
        catch(System.Exception ex){finish(ex);}
    };
    UnityEditor.EditorApplication.update+=tick;
    return "EOF pause/resume regression started; report: PlaytestExports/Runner/review-tail-resume-checks.txt";
}
catch(System.Exception ex)
{
    finish(ex);throw;
}
