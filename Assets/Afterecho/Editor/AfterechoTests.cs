using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace Afterecho.Editor
{
    public static class AfterechoTests
    {
        static int count;
        static List<string> rows;
        static void Check(bool pass,string label) { if(!pass)throw new Exception("FAIL: "+label);count++;rows.Add("PASS "+label); }
        public static void ValidateChart(ChartData c,StageData stage)
        {
            if(c.format!="afterecho-chart-v1" || c.notes==null || c.notes.Length<24)throw new Exception("Invalid chart format/count");
            var original=ChartData.Load(Resources.Load<TextAsset>("Afterecho/Charts/"+c.preset).text);
            if(c.song.id!=original.song.id)throw new Exception("Song ID mismatch");
            var seen=new HashSet<string>();int burst=1;
            for(int i=0;i<c.notes.Length;i++)
            {
                var n=c.notes[i];if(!seen.Add(n.id))throw new Exception("Duplicate ID: "+n.id);
                if(double.IsNaN(n.time)||double.IsInfinity(n.time)||n.time<stage.beats[stage.listenBeats]-.1 || n.time>stage.doorTime-.15)throw new Exception(n.id+": time out of range");
                if(i==0)continue;
                double gap=n.time-c.notes[i-1].time;
                if(gap<c.rules.minGap-1e-7)throw new Exception(n.id+": minimum gap");
                if(burst>=4 && gap<c.rules.recoveryGap-1e-7)throw new Exception(n.id+": recovery gap after four-note burst");
                burst=gap<c.rules.fastGap?burst+1:1;
            }
        }
        [MenuItem("Afterecho/Run Core Tests")]
        public static string RunAll()
        {
            count=0;rows=new List<string>();
            var stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
            foreach(string preset in new[]{"easy","normal","hard"})
            {
                var c=ChartData.Load(Resources.Load<TextAsset>("Afterecho/Charts/"+preset).text);ValidateChart(c,stage);Check(true,preset+" schema/density/burst");
                int first=Array.FindIndex(c.notes,n=>n.time>=stage.MainTime);double t=c.notes[first].time,w=ChartEngine.Window(c,first);
                foreach(double sign in new[]{-1d,1d})
                {
                    var b=new ChartEngine(c,stage);b.Seek(stage.MainTime);Check(b.Tap(t+sign*w)=="hit",preset+" inclusive boundary "+sign);
                    b=new ChartEngine(c,stage);b.Seek(stage.MainTime);Check(b.Tap(t+sign*(w+.00001))!="hit",preset+" outside boundary "+sign);
                }
                var d=new ChartEngine(c,stage);d.Seek(stage.MainTime);d.Tap(t);int hits=d.Hits;
                Check(d.Tap(t+.001)=="bounce"&&d.Hits==hits,preset+" contact bounce no duplicate hit");
                d.Tap(t+.04);Check(d.Hits==hits&&d.Extras==1&&d.Combo==0&&d.Health==c.rules.health,preset+" extra resets combo without damage");
                var idle=new ChartEngine(c,stage);idle.Advance(c.encounters[0].end-.001);Check(idle.Misses>0&&idle.Damage==0,preset+" note misses never damage");
                Check(idle.Enemies[0].state==EnemyState.Active,preset+" warned encounter active");
                idle.Advance(c.encounters[0].end);idle.Advance(c.encounters[0].end+.001);Check(idle.Damage==1,preset+" one deadline one damage");
                idle.Advance(stage.duration);Check(idle.Status==RunStatus.Lost&&idle.Damage==c.rules.health,preset+" no input loses at health limit");
                var auto=new ChartEngine(c,stage);
                foreach(var n in c.notes)auto.Tap(n.time,"test-auto");auto.Advance(stage.duration);
                Check(auto.Status==RunStatus.Won&&auto.Damage==0&&auto.Kills==6,preset+" full autoplay wins all six encounters");
                Check(auto.Hits==c.notes.Count(n=>n.time>=stage.MainTime)&&auto.Misses==0,preset+" every main note exactly once");
                Check(auto.Best==auto.Hits&&auto.Multiplier==4,preset+" combo and multiplier");
                Check(auto.Steps==auto.Hits-c.encounters.Sum(e=>e.hp),preset+" remaining notes move after early kills");
                var main=new ChartEngine(c,stage){PracticeEnabled=false};
                foreach(var n in c.notes)main.Tap(n.time,"full-song-main");main.Advance(stage.duration);
                Check(main.Status==RunStatus.Won && main.Hits==c.notes.Length && main.PracticeHits==0,preset+" full-song main starts at zero and scores intro notes");
                Check(main.Misses==0 && main.Damage==0 && main.Kills==6,preset+" full-song main completes without double-offset");
                var battle=c.encounters[0];var early=new ChartEngine(c,stage);early.Seek(battle.warning);
                foreach(var n in c.notes.Where(n=>battle.noteIds.Contains(n.id)).Take(battle.hp))early.Tap(n.time);
                Check(early.Enemies[0].state==EnemyState.Killed,preset+" early kill");early.Advance(battle.end);Check(early.Damage==0,preset+" killed enemy no delayed damage");
                var r=new ChartEngine(c,stage);r.Seek(stage.MainTime);r.Tap(t);
                int next=r.NextPending();double at=c.notes[next].time-.02;double safe=r.SafeResume(at);r.PrepareResume(safe);
                int damage=r.Damage;int previousHits=r.Hits;r.Advance(safe);
                Check(r.Misses==0&&r.Damage==damage&&r.Hits==previousHits,preset+" safe resume does not reset decisions or cause miss");
                r.Tap(c.notes[next].time);Check(r.Hits==previousHits+1,preset+" next note after resume hittable");
                var inv=new ChartEngine(c,stage){Invincible=true};inv.Advance(stage.duration);Check(inv.Status==RunStatus.Won&&inv.Damage==0,preset+" invincible no input completes");
                Check(c.notes.Select((n,i)=>ChartEngine.Window(c,i)<=c.rules.maxWindow+1e-9).All(x=>x),preset+" window cap");
                for(int i=0;i<c.notes.Length-1;i++)
                    if(ChartEngine.Window(c,i)+ChartEngine.Window(c,i+1)>=c.notes[i+1].time-c.notes[i].time)throw new Exception("Overlapping windows");
                Check(true,preset+" adjacent windows never overlap");
                for(int ei=0;ei<c.encounters.Length;ei++)
                {
                    var e=c.encounters[ei];
                    int last=Array.FindLastIndex(c.notes,n=>e.noteIds.Contains(n.id));
                    double edge=c.notes[last].time+ChartEngine.Window(c,last);
                    var late=new ChartEngine(c,stage);late.Seek(e.warning);late.Enemies[ei].remaining=1;
                    Check(late.Tap(edge)=="hit" && late.Enemies[ei].state==EnemyState.Killed && late.Damage==0,preset+" last combat note inclusive late boundary "+e.id);
                    var omitted=new ChartEngine(c,stage);omitted.Seek(e.warning);
                    double deadline=Math.Max(e.end,edge)+.00001;omitted.Advance(deadline);omitted.Advance(deadline+.1);
                    Check(omitted.Damage==1 && omitted.Enemies[ei].damageApplied,preset+" last combat window deadline exactly one damage "+e.id);
                }
                int gap=Enumerable.Range(first,c.notes.Length-first-1).First(i=>c.notes[i+1].time-c.notes[i].time>.6);
                var rest=new ChartEngine(c,stage);rest.Seek(c.notes[gap].time);rest.Tap(c.notes[gap].time);int misses=rest.Misses;
                rest.Advance((c.notes[gap].time+c.notes[gap+1].time)/2);Check(rest.Misses==misses,preset+" formal rest no omission");
            }
            string report=$"{count} checks passed\n"+string.Join("\n",rows);
            Directory.CreateDirectory("PlaytestExports");File.WriteAllText("PlaytestExports/core-tests.txt",report);Debug.Log("AFTERECHO: "+count+" core checks passed");return report;
        }
    }
}
