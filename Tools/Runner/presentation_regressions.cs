if(!UnityEditor.EditorApplication.isPlaying)
    throw new System.InvalidOperationException("Enter Play Mode in the runner scene first.");
var g=UnityEngine.Object.FindAnyObjectByType<Afterecho.RunnerGame>();
if(g==null||g.Run==null||g.view==null)
    throw new System.InvalidOperationException("An initialized RunnerGame is required.");

var originalAuto=g.autoPlay;
var originalInvincible=g.invincible;
var originalReducedMotion=g.reducedMotion;
var originalLoop=g.loop;
var originalVolume=g.volume;
var originalListenerVolume=UnityEngine.AudioListener.volume;
var v=g.view;
var sprite=v.runnerImage.rectTransform;
var lines=new System.Collections.Generic.List<string>();
var output="PlaytestExports/Runner/presentation-regressions.txt";
double started=UnityEditor.EditorApplication.timeSinceStartup;
double phaseStarted=started;
int phase=0;
bool finished=false;
UnityEngine.GameObject probe=null;
DG.Tweening.Tween unrelated=null;
DG.Tweening.Tween firstPunch=null;
UnityEditor.EditorApplication.CallbackFunction tick=null;

System.Action<bool,string> check=(condition,message)=>{
    if(!condition)throw new System.Exception(message);
    lines.Add("PASS "+message);
};
System.Func<int> activeSpriteTweens=()=>{
    var list=DG.Tweening.DOTween.TweensByTarget(sprite,false);
    int count=0;
    if(list!=null)foreach(var tween in list)
        if(DG.Tweening.TweenExtensions.IsActive(tween))count++;
    return count;
};
System.Func<bool> neutral=()=>UnityEngine.Quaternion.Angle(sprite.localRotation,UnityEngine.Quaternion.identity)<.01f;
System.Action finish=()=>{
    if(finished)return;
    finished=true;
    UnityEditor.EditorApplication.update-=tick;
    if(unrelated!=null)DG.Tweening.TweenExtensions.Kill(unrelated,false);
    if(probe!=null)UnityEngine.Object.Destroy(probe);
    if(g!=null&&UnityEditor.EditorApplication.isPlaying)
    {
        g.autoPlay=originalAuto;g.invincible=originalInvincible;
        g.reducedMotion=originalReducedMotion;g.loop=originalLoop;g.volume=originalVolume;
        g.Menu();
    }
    UnityEngine.AudioListener.volume=originalListenerVolume;
    System.IO.Directory.CreateDirectory("PlaytestExports/Runner");
    lines.Add("Elapsed seconds: "+(UnityEditor.EditorApplication.timeSinceStartup-started).ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
    System.IO.File.WriteAllLines(output,lines);
};

try
{
    g.autoPlay=false;g.invincible=true;g.reducedMotion=false;g.loop=false;g.volume=0;
    UnityEngine.AudioListener.volume=0;
    g.Menu();
    probe=new UnityEngine.GameObject("PresentationRegression_UnrelatedTween");
    probe.hideFlags=UnityEngine.HideFlags.DontSave;
    unrelated=DG.Tweening.ShortcutExtensions.DOMove(probe.transform,UnityEngine.Vector3.one,4f);
    DG.Tweening.TweenSettingsExtensions.SetUpdate(unrelated,true);
    // Sharing the old broad ID proves cleanup no longer kills unrelated feedback.
    DG.Tweening.TweenSettingsExtensions.SetId(unrelated,v);
    v.React(new Afterecho.RunEvent("extra",p:true));
    check(activeSpriteTweens()==1,"first offbeat starts exactly one sprite tween");
    firstPunch=DG.Tweening.DOTween.TweensByTarget(sprite,false)[0];
    phaseStarted=UnityEditor.EditorApplication.timeSinceStartup;
}
catch(System.Exception ex)
{
    lines.Add("FAIL "+ex.Message);finish();UnityEngine.Debug.LogException(ex);
    return "presentation regressions failed during setup; see "+output;
}

tick=()=>{
    try
    {
        if(g==null||!UnityEditor.EditorApplication.isPlaying)
            throw new System.Exception("Runner Play Mode ended during regression checks.");
        double now=UnityEditor.EditorApplication.timeSinceStartup;
        if(now-started>5)throw new System.TimeoutException("Presentation regression exceeded five seconds.");
        if(phase==0&&now-phaseStarted>=.05)
        {
            check(DG.Tweening.TweenExtensions.IsActive(firstPunch),"second offbeat arrives while the first punch is active");
            v.React(new Afterecho.RunEvent("extra",p:true));
            check(!DG.Tweening.TweenExtensions.IsActive(firstPunch),"second offbeat cancels the previous punch");
            check(activeSpriteTweens()==1,"overlapping offbeats retain exactly one sprite tween");
            phaseStarted=now;phase=1;
        }
        else if(phase==1&&now-phaseStarted>=.25)
        {
            check(neutral(),"overlapping offbeats return sprite rotation to zero");
            check(activeSpriteTweens()==0,"completed offbeat leaves no active sprite tween");
            v.React(new Afterecho.RunEvent("extra",p:true));
            v.StopFeedback();
            check(neutral()&&activeSpriteTweens()==0,"feedback cleanup cancels punch and restores neutral rotation");
            check(DG.Tweening.TweenExtensions.IsActive(unrelated),"feedback cleanup preserves an unrelated tween with the same view ID");
            g.StartRun(false);
            v.React(new Afterecho.RunEvent("extra",p:true));
            g.PauseGame();
            check(g.Phase==Afterecho.GamePhase.Paused&&neutral()&&activeSpriteTweens()==0,"pause during preparation cancels punch and restores neutral rotation");
            check(DG.Tweening.TweenExtensions.IsActive(unrelated),"pause preserves unrelated tween");
            g.reducedMotion=true;
            v.React(new Afterecho.RunEvent("extra",p:true));
            check(activeSpriteTweens()==0,"reduced motion creates no punch tween");
            phaseStarted=now;phase=2;
        }
        else if(phase==2&&now-phaseStarted>=.18)
        {
            check(neutral()&&activeSpriteTweens()==0,"reduced motion keeps neutral rotation after feedback interval");
            lines.Add("PASS all presentation regressions");
            finish();UnityEngine.Debug.Log("Runner presentation regressions passed: "+output);
        }
    }
    catch(System.Exception ex)
    {
        lines.Add("FAIL "+ex.Message);finish();UnityEngine.Debug.LogException(ex);
    }
};
UnityEditor.EditorApplication.update+=tick;
return "presentation regressions started; completion report: "+output;
