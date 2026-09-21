// Run in Play mode through eval_file. Uses the real RunnerGame.Update with an isolated clock.
if(!UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Enter Play mode first");
var g=UnityEngine.Object.FindFirstObjectByType<Afterecho.RunnerGame>();
if(g==null)throw new System.Exception("Runner scene is required");
var lines=new System.Collections.Generic.List<string>();
System.Action<bool,string> check=(ok,label)=>{if(!ok)throw new System.Exception(label);lines.Add("PASS "+label);};
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var clockType=typeof(Afterecho.RhythmClock);
var update=typeof(Afterecho.RunnerGame).GetMethod("Update",flags);
var phase=typeof(Afterecho.RunnerGame).GetProperty("Phase");
var originalClock=g.clock;var originalDifficulty=g.difficulty;float originalSync=g.syncMs,originalVolume=g.volume;
bool originalAuto=g.autoPlay,originalLoop=g.loop,originalInvincible=g.invincible;
double originalFrom=g.loopStart,originalTo=g.loopEnd;
g.Menu();g.autoPlay=false;g.loop=false;g.invincible=false;g.volume=0;
var fixture=new UnityEngine.GameObject("Runner_TimingRegression");
var music=fixture.AddComponent<UnityEngine.AudioSource>();music.playOnAwake=false;music.clip=originalClock.music.clip;
var clock=fixture.AddComponent<Afterecho.RhythmClock>();clock.music=music;clock.click=originalClock.click;clock.countVoices=new UnityEngine.AudioSource[0];g.clock=clock;
System.Action<double,double> tail=(position,elapsed)=>{
    clock.Stop(0);clock.UsePlaybackPosition=true;
    clockType.GetField("<Scheduled>k__BackingField",flags).SetValue(clock,true);
    clockType.GetField("<ScheduledStart>k__BackingField",flags).SetValue(clock,UnityEngine.AudioSettings.dspTime-2);
    clockType.GetField("observedPlayback",flags).SetValue(clock,true);
    clockType.GetField("observedPosition",flags).SetValue(clock,position);
    clockType.GetField("observedAt",flags).SetValue(clock,UnityEngine.Time.realtimeSinceStartupAsDouble-elapsed);
};
try
{
    foreach(string difficulty in new[]{"easy","normal","hard"})
    {
        g.difficulty=difficulty;
        foreach(float sync in new[]{-250f,-100f,0f,50f,100f,250f})
        {
            g.syncMs=sync;g.NewRun();
            foreach(var note in g.Run.Chart.notes)g.Run.Tap(note.time);
            g.Run.Events.Clear();tail(music.clip.length,1);
            phase.SetValue(g,Afterecho.GamePhase.Playing);update.Invoke(g,null);
            check(g.Phase==Afterecho.GamePhase.Result&&g.Run.Status==Afterecho.RunStatus.Won&&g.Run.Health==100,difficulty+" EOF completes with sync "+sync+"ms");
        }
        g.syncMs=-250;g.NewRun();foreach(var note in g.Run.Chart.notes)g.Run.Tap(note.time);g.Run.Events.Clear();
        tail(g.Stage.duration-.2,0);phase.SetValue(g,Afterecho.GamePhase.Playing);update.Invoke(g,null);
        check(g.Phase==Afterecho.GamePhase.Playing&&g.Run.DeferCompletion&&g.Run.Status==Afterecho.RunStatus.Running,difficulty+" negative sync does not cut the song short");
    }
    // Early audio interruption must still freeze the clock and request a pause.
    tail(50,1);check(System.Math.Abs(clock.SongTime-50)<.001&&clock.PlaybackInterrupted,"early interruption freezes instead of extrapolating to completion");

    g.syncMs=0;g.difficulty="easy";
    var edited=Afterecho.ChartData.Load(UnityEngine.Resources.Load<UnityEngine.TextAsset>("Afterecho/RunnerCharts/easy").text);
    edited.notes=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(edited.notes,1));
    g.NewRun(edited);g.Run.Seek(20);g.loop=true;g.loopStart=20;g.loopEnd=20.1;
    for(int pass=0;pass<2;pass++)
    {
        clock.Stop(20.2);clock.UsePlaybackPosition=false;
        clockType.GetField("<Scheduled>k__BackingField",flags).SetValue(clock,true);
        clockType.GetField("<ScheduledStart>k__BackingField",flags).SetValue(clock,UnityEngine.AudioSettings.dspTime+60);
        phase.SetValue(g,Afterecho.GamePhase.Playing);update.Invoke(g,null);
        check(object.ReferenceEquals(g.Run.Chart,edited)&&g.Run.Chart.notes.Length==238&&g.Phase==Afterecho.GamePhase.Preparing,"runner loop "+(pass+1)+" keeps the edited chart");
    }
    System.IO.File.WriteAllLines("PlaytestExports/Runner/review-timing-checks.txt",lines);
    return lines.ToArray();
}
finally
{
    clock.Stop(0);g.clock=originalClock;g.difficulty=originalDifficulty;g.syncMs=originalSync;g.volume=originalVolume;
    g.autoPlay=originalAuto;g.loop=originalLoop;g.invincible=originalInvincible;g.loopStart=originalFrom;g.loopEnd=originalTo;
    g.Menu();UnityEngine.AudioListener.volume=originalVolume;UnityEngine.Object.DestroyImmediate(fixture);
}
