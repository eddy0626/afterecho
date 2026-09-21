using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Afterecho.Editor
{
    public static class AfterechoChartReviewTests
    {
        static readonly List<string> results=new List<string>();
        static void Check(bool condition,string name)
        { if(!condition)throw new Exception("FAIL "+name);results.Add("PASS "+name); }
        static void Reject(Action action,string name)
        {
            try{action();}catch(ArgumentException){Check(true,name);return;}catch(InvalidOperationException){Check(true,name);return;}
            throw new Exception("FAIL "+name+": accepted invalid review");
        }
        static JObject Fixture()=>JObject.Parse(@"{
          'format':'afterecho-chart-v1','preset':'easy','gameplayMode':'runner',
          'song':{'id':'synthetic-review-fixture','duration':20},
          'rules':{'maxWindow':0.1},'origin':{'generator':'fixture only','seed':42,'extraProvenance':{'keep':true}},
          'review':{'trainingEligible':false,'technical':'fixture'},'encounters':[],
          'notes':[
            {'id':'FIX-A','originalTime':1,'parentTime':1.02,'time':1.08,'source':'fixture_ai','teamReview':'pending'},
            {'id':'FIX-B','originalTime':1.5,'parentTime':1.55,'time':1.7,'source':'fixture_ai','teamReview':'pending'},
            {'id':'FIX-C','originalTime':2.2,'parentTime':2.2,'time':2.2,'source':'fixture_ai','teamReview':'pending'}
          ]}");
        static JObject Note(JObject d,string id)=>AfterechoChartReview.Notes(d).Single(n=>(string)n["id"]==id);
        static void Approve(JObject d,params string[] ids)=>AfterechoChartReview.Review(d,ids,"approved","Fixture reviewer","Synthetic fixture assertion only",true);
        [MenuItem("Afterecho/Chart Review/Run Workflow Tests")]
        public static string RunAll()
        {
            results.Clear();
            var d=Fixture();string provenance=d["origin"].ToString();
            AfterechoChartReview.RefreshSummary(d);
            Check(AfterechoChartReview.Notes(d).All(n=>AfterechoChartReview.State(d,n)=="pending"),"technical refresh does not approve any note");
            Reject(()=>AfterechoChartReview.Review(d,new[]{"FIX-A"},"approved","","why",true),"approval requires reviewer");
            Reject(()=>AfterechoChartReview.Review(d,new[]{"FIX-A"},"approved","person","",true),"approval requires reason");
            Reject(()=>AfterechoChartReview.Review(d,new[]{"FIX-A"},"approved","person","why",false),"approval requires explicit human confirmation");
            Check(d["humanReviews"]==null,"rejected approval attempts create no approval history");
            Check(!AfterechoChartReview.LargeMove(d,Note(d,"FIX-A"))&&AfterechoChartReview.LargeMove(d,Note(d,"FIX-B")),"large move uses actual adjacent-limited judgement window");
            Approve(d,"FIX-A");var first=AfterechoChartReview.LatestRecord(d,Note(d,"FIX-A"));
            Check(AfterechoChartReview.State(d,Note(d,"FIX-A"))=="approved","explicit note review approves current timing");
            Check((bool)first["sourceRetimed"]&&(double)first["sourceTime"]==1&&(double)first["previousTime"]==1.02,"review stores raw and previous timing plus retiming evidence");
            Check(!string.IsNullOrWhiteSpace((string)first["reviewedUtc"])&&!string.IsNullOrWhiteSpace((string)first["id"]),"review records stable ID and UTC timestamp");
            AfterechoChartReview.Shift(d,Note(d,"FIX-A"),1.15,"fixture timing edit");
            Check(AfterechoChartReview.State(d,Note(d,"FIX-A"))=="pending","editing approved timing invalidates its approval");
            Check(((JArray)d["humanReviews"]).Count==2&&(string)((JObject)((JArray)d["humanReviews"])[0])["decision"]=="approved","edit preserves historic approval and appends invalidation");
            Check((double)Note(d,"FIX-A")["originalTime"]==1&&(double)Note(d,"FIX-A")["parentTime"]==1.02,"editing never overwrites raw or previous corrected timing");
            Check((string)AfterechoChartReview.LatestRecord(d,Note(d,"FIX-A"))["reviewer"]=="system:edit","automatic invalidation is identified as system edit");
            Approve(d,"FIX-A","FIX-B","FIX-C");
            Check((string)d["review"]["musicalAndOneHand"]=="human_review_complete"&&(bool)d["review"]["trainingEligible"]==false,"all current approvals still require separate training approval");
            AfterechoChartReview.Review(d,new[]{"FIX-A","FIX-B"},"rejected","Phrase reviewer","Fixture phrase rejection",false,true,1,2);
            var a=AfterechoChartReview.LatestRecord(d,Note(d,"FIX-A"));var b=AfterechoChartReview.LatestRecord(d,Note(d,"FIX-B"));
            Check((string)a["phraseId"]==(string)b["phraseId"]&&(string)a["scope"]=="phrase"&&(double)a["from"]==1,"phrase review stores shared identity and range for each note");
            Check((int)d["review"]["rejectedNotes"]==2&&(string)d["review"]["musicalAndOneHand"]=="team_pending","rejected phrase cannot remain globally approved");
            Approve(d,"FIX-C");AfterechoChartReview.Delete(d,Note(d,"FIX-C"),"fixture deletion");
            Check(AfterechoChartReview.Notes(d).Count()==2&&((JArray)d["humanReviews"]).OfType<JObject>().Last(r=>(string)r["noteId"]=="FIX-C")["decision"].ToString()=="pending","deletion preserves history and invalidates removed approval");
            var manual=AfterechoChartReview.Add(d,3,"fixture manual note");Approve(d,(string)manual["id"]);
            var manualEvidence=AfterechoChartReview.LatestRecord(d,manual);
            Check(AfterechoChartReview.SourceTime(manual)==null&&(bool)manualEvidence["sourceTimeKnown"]==false&&manualEvidence["sourceRetimed"].Type==JTokenType.Null,"manual note is never labeled AI generated or source-retimed");
            Check(d["origin"].ToString()==provenance,"unknown nested model provenance survives edits and review");

            var forged=Fixture();var n=Note(forged,"FIX-A");n["teamReview"]="approved";
            Check(AfterechoChartReview.State(forged,n)=="pending","imported approved label without human evidence is unverified");
            Approve(forged,"FIX-A");var record=AfterechoChartReview.LatestRecord(forged,n);record.Remove("id");n.Remove("humanReviewId");
            Check(!AfterechoChartReview.CurrentEvidence(forged,n)&&AfterechoChartReview.State(forged,n)=="pending","two missing review IDs cannot validate one another");
            var stale=Fixture();Approve(stale,"FIX-A");Note(stale,"FIX-A")["time"]=1.19;
            Check(AfterechoChartReview.State(stale,Note(stale,"FIX-A"))=="pending","external timing edit invalidates stale evidence without UI callback");
            var rawChanged=Fixture();Approve(rawChanged,"FIX-A");Note(rawChanged,"FIX-A")["originalTime"]=.98;
            Check(AfterechoChartReview.State(rawChanged,Note(rawChanged,"FIX-A"))=="pending","changed raw provenance invalidates old comparison evidence");

            var compare=Fixture();Note(compare,"FIX-A")["originalTime"]=.8;Note(compare,"FIX-B")["originalTime"]=2.1;
            AfterechoChartReview.Add(compare,1.9,"fixture manual note excluded from paired AI timing comparison");string snapshot=compare.ToString();
            var selection=AfterechoChartReview.BuildComparison(compare,1,2);
            Check(selection.noteIds.SequenceEqual(new[]{"FIX-A","FIX-B"})&&selection.sourceTimes.Length==selection.currentTimes.Length,"A/B compares the exact same retained AI note IDs");
            Check(selection.from==.8&&Math.Abs(selection.to-2.13)<1e-9,"A/B expands both shared endpoints instead of dropping shifted source notes");
            Check(selection.manualExcluded==1,"A/B explicitly counts manual notes excluded from both variants");
            Check(compare.ToString()==snapshot,"A/B construction does not alter judgement times or review state");
            var raw=AfterechoChartReview.ComparisonTimes(compare,true,1,2);var current=AfterechoChartReview.ComparisonTimes(compare,false,1,2);
            Check(raw.SequenceEqual(new[]{.8,2.1})&&current.SequenceEqual(new[]{1.08,1.7}),"A/B preserves original and current times without retiming or quantizing");
            Reject(()=>AfterechoChartReview.BuildComparison(compare,2,1),"invalid comparison range rejected");
            Reject(()=>AfterechoChartReview.Shift(compare,Note(compare,"FIX-A"),double.NaN,"invalid"),"nonfinite timing edit rejected");

            const int rate=48000;var audio=ChartReviewAudition.RenderClicks(rate,.5,1,2,new[]{1.01,1.25});
            int click=(int)Math.Round((2+.01)*rate), second=(int)Math.Round((2+.25)*rate);
            Check(audio[click-1]==0&&Math.Abs(audio[click+1])>.0001&&audio[second-1]==0&&Math.Abs(audio[second+1])>.0001,"comparison click starts are pre-rendered at exact rounded sample positions");
            Check(audio[1]!=0&&audio[(int)(.5*rate)+1]!=0&&audio[rate+1]!=0&&audio[(int)(1.5*rate)+1]!=0,"comparison buffer includes exactly scheduled four-beat preparation");
            Check(audio.SequenceEqual(ChartReviewAudition.RenderClicks(rate,.5,1,2,new[]{1.01,1.25})),"click buffer timing is deterministic and independent of Editor update frequency");
            Reject(()=>ChartReviewAudition.RenderClicks(rate,.5,1,2,new[]{.99}),"out-of-range click is rejected instead of silently omitted");
            string result=results.Count+" review workflow checks passed\n"+string.Join("\n",results);
            Directory.CreateDirectory("PlaytestExports/Runner");File.WriteAllText("PlaytestExports/Runner/review-workflow-tests.txt",result);
            return result;
        }
    }
}
