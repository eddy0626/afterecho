using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
namespace Afterecho.Editor
{
 public static class RunnerTests
 {
  static readonly List<string> rows=new List<string>();
  static void Check(bool p,string msg){if(!p)throw new Exception("FAIL "+msg);rows.Add("PASS "+msg);}
  static void Rejected(Action action,string message)
  {
   try{action();}catch(FormatException){Check(true,message);return;}
   throw new Exception("FAIL "+message+": invalid data was accepted");
  }
  public static void ValidateChart(ChartData c,StageData s,double? previewSeconds=null)
  {
   ChartValidation.ValidateRunnerChart(c,s,previewSeconds);
   if(c.format!="afterecho-chart-v1"||c.gameplayMode!="runner"||c.notes.Length<24)throw new Exception("Invalid runner chart");
   var original=Resources.Load<TextAsset>("Afterecho/Charts/"+c.preset);
   if(original==null)throw new FormatException("Unknown chart preset: "+c.preset);
   if(c.song.id!=ChartData.Load(original.text).song.id)throw new Exception("Song ID mismatch");
  }
  static ChartData VisibilityFixture(string preset,params double[] firstNotes)
  {
   var c=ChartData.Load(Resources.Load<TextAsset>("Afterecho/Charts/"+preset).text);
   c.gameplayMode="runner";c.encounters=Array.Empty<EncounterData>();
   c.rules.previewSeconds=0;c.rules.maxVisibleNotes=0;c.rules.fastGap=.2;c.rules.recoveryGap=.55;c.rules.burstLimit=4;
   c.rules.minGap=preset=="easy"?.3:.22;c.rules.maxWindow=preset=="easy"?.1:.08;
   var times=new List<double>(firstNotes);
   while(times.Count<24)times.Add(times[times.Count-1]+1);
   c.notes=times.Select((t,i)=>new ChartNote{id="visibility-"+i,time=t,originalTime=t,source="regression-fixture"}).ToArray();
   return c;
  }
  static void ValidateRunnerVisibilityConfigurations(StageData stage,RunnerRules rules)
  {
   var c=VisibilityFixture("normal",Enumerable.Range(0,6).Select(i=>1.2+i*.25).ToArray());
   Check(c.notes.Max(n=>c.notes.Count(m=>m.time>=n.time&&m.time<=n.time+1.2))==5,"fixture passes former future-only density count");
   Check(ChartValidation.MaxVisiblePendingNotes(c,1.2)==6,"late pending note makes six visible rings");
   Rejected(()=>ValidateChart(c,stage),"lab rejects late-window overlap");
   Rejected(()=>new ChartEngine(c,stage,rules),"playback rejects same late-window overlap");
   c.rules.maxVisibleNotes=6;ValidateChart(c,stage);new ChartEngine(c,stage,rules);Check(true,"explicit visible-note cap is honored by lab and playback");

   c=VisibilityFixture("easy",1.2,1.52,1.84,2.16,2.48);
   Check(ChartValidation.MaxVisiblePendingNotes(c,1.2)==5,"easy fixture has five pending rings");
   Rejected(()=>ValidateChart(c,stage),"easy defaults to four visible rings");
   Rejected(()=>new ChartEngine(c,stage,rules),"playback enforces easy four-ring limit");
   c=VisibilityFixture("easy",1.2,1.52,1.84,2.16);
   ValidateChart(c,stage);new ChartEngine(c,stage,rules);Check(true,"first note at 1.2 seconds and four-ring limit accepted");
   var practice=new ChartEngine(c,stage,rules){PracticeEnabled=true};
   Check(practice.Tap(1.2)=="hit"&&practice.PracticeHits==1&&practice.Health==100,"restored intro note is hittable in safe runner practice");
   Check(practice.Hits==0&&practice.Score==0,"practice intro does not count toward main score");
   practice.Advance(2.3);Check(practice.Health==100,"restored practice misses remain harmless");

   // Exactly equal start/end times are both visible; a strictly later entry is not.
   c=VisibilityFixture("easy",1.25,1.5625,1.875,2.1875,2.5);c.rules.maxWindow=.0625;
   Check(ChartValidation.MaxVisiblePendingNotes(c,1.1875)==5,"closed visibility intervals count touching boundaries");
   c.notes[4].time+=.0001;
   Check(ChartValidation.MaxVisiblePendingNotes(c,1.1875)==4,"visibility ends after inclusive late boundary");
   c=VisibilityFixture("easy",Enumerable.Range(0,6).Select(i=>1.25+i*.3125).ToArray());c.rules.maxWindow=.5;
   Check(ChartValidation.MaxVisiblePendingNotes(c,1.25)==5,"visibility uses neighbor-capped actual judgement windows");

   c=VisibilityFixture("easy",Enumerable.Range(0,24).Select(i=>1.2+i*.5).ToArray());
   ValidateChart(c,stage);new ChartEngine(c,stage,rules);Check(true,"ordinary preview allows spaced chart");
   rules.previewSeconds=2;
   Rejected(()=>ValidateChart(c,stage,rules.previewSeconds),"lab detects density after preview increases to two seconds");
   Rejected(()=>new ChartEngine(c,stage,rules),"playback detects actual two-second preview density");
   rules.previewSeconds=1.2f;c.rules.previewSeconds=2;
   Rejected(()=>ValidateChart(c,stage),"metadata preview applies when no runtime override supplied");
   ValidateChart(c,stage,rules.previewSeconds);new ChartEngine(c,stage,rules);Check(true,"actual preview overrides document recommendation consistently");
   foreach(double invalid in new[]{-.1,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
   {
    c.rules.previewSeconds=invalid;
    Rejected(()=>ChartValidation.Validate(c),"reject invalid metadata preview "+invalid);
   }
   c.rules.previewSeconds=0;c.rules.maxVisibleNotes=-1;
   Rejected(()=>ChartValidation.Validate(c),"reject negative visible-note metadata");
   c.rules.maxVisibleNotes=ChartValidation.RunnerRingCapacity+1;
   Rejected(()=>ChartValidation.Validate(c),"reject visible-note cap beyond renderer capacity");
   c.rules.maxVisibleNotes=0;
   Rejected(()=>ValidateChart(c,stage,0),"reject zero explicit preview override");
   Rejected(()=>ValidateChart(c,stage,double.NaN),"reject nonfinite explicit preview override");

   c=VisibilityFixture("easy",1.2,1.51,1.82,2.13,2.44);c.rules.maxVisibleNotes=8;
   c.rules.minGap=.32;
   Rejected(()=>ValidateChart(c,stage),"lab rejects insufficient spacing");
   Rejected(()=>new ChartEngine(c,stage,rules),"playback rejects insufficient spacing");
   c.rules.minGap=.3;c.rules.fastGap=.39;
   Rejected(()=>ValidateChart(c,stage),"lab rejects missing burst recovery");
   Rejected(()=>new ChartEngine(c,stage,rules),"playback rejects missing burst recovery");
   c.rules.burstLimit=5;ValidateChart(c,stage);new ChartEngine(c,stage,rules);Check(true,"explicit burst limit uses shared validation");

   // Legacy combat keeps its original rules even if runner readability would fail.
   c=VisibilityFixture("normal",Enumerable.Range(0,6).Select(i=>1.2+i*.25).ToArray());c.gameplayMode=null;
   new ChartEngine(c,stage);Check(true,"runner density constraints do not alter legacy playback");
   c.encounters=new[]{new EncounterData{id="invalid",warning=1,start=2,end=3,hp=1,noteIds=new[]{"missing"}}};
   Rejected(()=>new ChartEngine(c,stage),"legacy encounter validation remains active");
  }
  public static string RunValidationRegressions()
  {
   rows.Clear();var rules=ScriptableObject.CreateInstance<RunnerRules>();
   try{var stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);ValidateRunnerVisibilityConfigurations(stage,rules);return rows.Count+" checks passed\n"+string.Join("\n",rows);}
   finally{UnityEngine.Object.DestroyImmediate(rules);}
  }
  static void ValidateInvalidConfigurations(StageData stage,RunnerRules rules)
  {
   string json=Resources.Load<TextAsset>("Afterecho/RunnerCharts/easy").text;
   foreach(double value in new[]{-.1,0,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
   {
    var c=ChartData.Load(json);c.rules.maxWindow=value;
    Rejected(()=>ValidateChart(c,stage),"lab rejects invalid maxWindow "+value);
    Rejected(()=>new ChartEngine(c,stage,rules),"playback rejects invalid maxWindow "+value);
   }
   var imported=ChartData.Load(json);imported.rules.maxWindow=-.1;
   Rejected(()=>ChartData.Load(JsonUtility.ToJson(imported)),"JSON loader rejects negative maxWindow");
   Rejected(()=>ChartData.Load("{}"),"JSON loader rejects missing required fields");
   Rejected(()=>ChartData.Load(""),"JSON loader rejects empty document");
   Rejected(()=>ChartValidation.Validate(null),"validator rejects null document");
   Action<ChartData>[] invalid={
    c=>c.rules=null,c=>c.song=null,c=>c.notes=null,c=>c.notes=Array.Empty<ChartNote>(),
    c=>c.preset="",c=>c.song.id="",c=>c.song.duration=double.NaN,
    c=>c.rules.minGap=0,c=>c.rules.fastGap=double.NaN,c=>c.rules.recoveryGap=double.PositiveInfinity,
    c=>c.rules.health=0,c=>c.rules.burstLimit=0,c=>c.notes[0]=null,
    c=>c.notes[0].id="",c=>c.notes[1].id=c.notes[0].id,
    c=>c.notes[0].time=double.NaN,c=>c.notes[1].time=c.notes[0].time
   };
   for(int i=0;i<invalid.Length;i++)
   {
    var c=ChartData.Load(json);invalid[i](c);
    Rejected(()=>ValidateChart(c,stage),"required fields and rule safety case "+i);
   }
  }
  [MenuItem("Afterecho/Runner/Run Tests")]
  public static string RunAll()
  {
   rows.Clear();var rules=ScriptableObject.CreateInstance<RunnerRules>();
   try {
    var stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
    ValidateInvalidConfigurations(stage,rules);
    ValidateRunnerVisibilityConfigurations(stage,rules);
    foreach(string p in new[]{"easy","normal","hard"})
    {
     var c=ChartData.Load(Resources.Load<TextAsset>("Afterecho/RunnerCharts/"+p).text);ValidateChart(c,stage,rules.previewSeconds);Check(true,p+" density, IDs, duration, four-hit recovery");
     ChartEngine New()=>new ChartEngine(c,stage,rules){PracticeEnabled=false};
     double t=c.notes[0].time,w=ChartEngine.Window(c,0);
     foreach(double sign in new[]{-1d,1d}){var b=New();Check(b.Tap(t+sign*w)=="hit",p+" inclusive boundary "+sign);b=New();Check(b.Tap(t+sign*(w+.00001))!="hit",p+" outside boundary "+sign);}
     var r=New();Check(r.Health==100&&r.Enemies.Length==0,p+" initial health and no combat");r.Advance(t-.2);Check(r.Health==100,p+" intro silence safe");r.Advance(t+w+.001);r.Advance(t+w+.002);Check(r.Health==90&&r.Misses==1,p+" omitted note once");r.Tap(c.notes[1].time);Check(r.Health==91,p+" hit recovery");
     r=New();r.Tap(t);Check(r.Health==100,p+" recovery cap");Check(r.Tap(t+.001)=="bounce"&&r.Hits==1,p+" duplicate contact ignored");r.Tap(t+.04);Check(r.Health==97&&r.Combo==0&&r.Extras==1,p+" extra damage and combo reset");
     r=New();foreach(var n in c.notes.Take(14))r.Tap(n.time);Check(!r.Boosted&&r.Combo==14,p+" 14 normal");r.Tap(c.notes[14].time);Check(r.Boosted&&r.Combo==15,p+" 15 boost");r.Tap(c.notes[14].time+.03);Check(!r.Boosted,p+" extra ends boost");
     r=New();foreach(var n in c.notes.Take(15))r.Tap(n.time);r.Advance(c.notes[15].time+ChartEngine.Window(c,15)+.001);Check(!r.Boosted&&r.Health==90,p+" miss ends boost");
     r=New();r.Advance(stage.duration);int m=r.Misses;Check(r.Health==0&&r.Status==RunStatus.Lost&&m==10,p+" no-input loses after ten misses");Check(r.Events.Count(e=>e.kind=="lost")==1,p+" game over once");r.Tap(stage.duration+1);r.Advance(stage.duration+2);Check(r.Misses==m&&r.Hits==0,p+" no decisions after loss");
     r=New();for(double at=0;at<stage.duration&&r.Status==RunStatus.Running;at+=.05)r.Tap(at);Check(r.Status==RunStatus.Lost,p+" 20Hz spam loses");
     r=New();foreach(var n in c.notes)r.Tap(n.time);r.Advance(stage.doorTime);Check(r.Status==RunStatus.Running,p+" old door does not end runner");r.Advance(stage.duration);Check(r.Status==RunStatus.Won&&r.Hits==c.notes.Length&&r.Misses==0&&r.Health==100,p+" full song autoplay wins");Check(r.Events.Count(e=>e.kind=="won")==1,p+" completion once");
     r=New();foreach(var n in c.notes)r.Tap(n.time);r.DeferCompletion=true;r.Advance(stage.duration+.1);Check(r.Status==RunStatus.Running&&!r.Events.Any(e=>e.kind=="won"),p+" audible tail defers completion");r.DeferCompletion=false;r.Advance(stage.duration+.1);r.Advance(stage.duration+.2);Check(r.Status==RunStatus.Won&&r.Events.Count(e=>e.kind=="won")==1,p+" audible tail release completes exactly once");
     r=New();r.Invincible=true;r.Advance(stage.duration);Check(r.Status==RunStatus.Won&&r.Health==100,p+" invincible completion");
     r=New();r.PracticeEnabled=true;r.Advance(stage.MainTime-.2);Check(r.Health==100,p+" safe tutorial no damage");
     r=New();r.Tap(t);double safe=r.SafeResume(c.notes[1].time-.01);r.PrepareResume(safe);r.Advance(safe);r.Tap(c.notes[1].time);Check(r.Hits==2&&r.Misses==0,p+" safe resume pending note");
     r=New();r.Tap(t);r.Advance((t+c.notes[1].time)*.5);Check(r.Health==100&&r.Misses==0,p+" rest safe");
     Check(c.notes.Select((n,i)=>Math.Abs(n.window-ChartEngine.Window(c,i))<1e-8).All(a=>a),p+" saved and actual window agree");
    }
    var v=new Vector2(1000,600);double[] laps={.1,.4,.6,.9};for(int i=0;i<4;i++){var pos=RunnerView.PositionOnTrack(laps[i],v,out int side);Check(side==i&&Math.Abs(pos.z-i*90)<.01,"runner side "+i);}
    foreach(Key key in new[]{Key.Space,Key.A,Key.F,Key.J,Key.Digit1,Key.UpArrow})Check(RunnerGame.IsGameplayKey(key),"ordinary key "+key);
    foreach(Key key in new[]{Key.Escape,Key.LeftMeta,Key.LeftCtrl,Key.LeftAlt,Key.Tab,Key.F1,Key.F12})Check(!RunnerGame.IsGameplayKey(key),"reserved key "+key);
    string result=rows.Count+" checks passed\n"+string.Join("\n",rows);Directory.CreateDirectory("PlaytestExports/Runner");File.WriteAllText("PlaytestExports/Runner/core-tests.txt",result);return result;
   }finally{UnityEngine.Object.DestroyImmediate(rules);}
  }
 }
}
