using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using Newtonsoft.Json.Linq;

namespace Afterecho.Editor
{
    public sealed class AfterechoChartLab : EditorWindow
    {
        JObject document;
        [SerializeField] string preset="easy", workingJson="", reviewerName="", reason="playtest timing adjustment";
        [SerializeField] bool runnerMode=true, dirty;
        readonly HashSet<string> selected=new HashSet<string>();
        RunnerGame Runner => UnityEngine.Object.FindFirstObjectByType<RunnerGame>();
        AfterechoGame Game => UnityEngine.Object.FindFirstObjectByType<AfterechoGame>();
        ChartEngine ActiveRun => runnerMode ? Runner?.Run : Game?.Run;
        ScrollView table;
        Label status,summary,waveStatus,rangeLabel;
        DoubleField from,to,addTime;
        DropdownField mode,difficulty,reviewDecision,reviewFilter;
        TextField reviewer,reviewReason;
        Toggle largeOnly,rangeOnly,confirmed;
        Button reviewSelected,reviewPhrase;
        ChartReviewWaveform waveform;
        ChartReviewAudition audition;
        double Duration => (double?)document?["song"]?["duration"] ?? 136.36;
        [MenuItem("Afterecho/Chart Lab")]
        public static void Open() { var w=GetWindow<AfterechoChartLab>();w.titleContent=new GUIContent("잔향 · 채보 테스트실");w.minSize=new Vector2(980,640); }
        public void CreateGUI()
        {
            audition?.Dispose();audition=new ChartReviewAudition();
            var root=rootVisualElement;root.Clear();root.AddToClassList("lab");
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Afterecho/Editor/ChartLab.uss"));
            root.Add(Label("AFTERECHO  /  채보 선별·검수", "heading"));
            root.Add(Label("기술 검사와 사람의 청음·한 손 검수를 분리합니다. 승인에는 검수자·이유·직접 확인이 필요합니다.\n모든 노트 승인 후에도 학습 사용은 별도 승인 전까지 false입니다. A/B는 원곡+시각 클릭만 재생하며 게임 판정에 반영하지 않습니다.","notice"));
            var controls=new ScrollView();controls.AddToClassList("lab-controls");root.Add(controls);
            var tools=Row(controls);
            mode=new DropdownField("모드",new List<string>{"runner","legacy combat"},runnerMode?0:1);tools.Add(mode);
            mode.RegisterValueChangedCallback(e=>SwitchSource(e.newValue=="runner",preset));
            difficulty=new DropdownField("난이도",new List<string>{"easy","normal","hard"},Math.Max(0,new List<string>{"easy","normal","hard"}.IndexOf(preset)));tools.Add(difficulty);
            difficulty.RegisterValueChangedCallback(e=>SwitchSource(runnerMode,e.newValue));
            tools.Add(new Button(Load){text="기본 채보 다시 읽기"});tools.Add(new Button(OpenDocument){text="JSON 불러오기"});
            tools.Add(new Button(SaveVariant){text="검증 후 수정본 저장"});tools.Add(new Button(SaveDraft){text="검증 전 초안 저장"});
            tools.Add(new Button(ExportCSV){text="시각·검수 CSV"});

            var reviewFold=new Foldout{text="사람의 검수 기록",value=false};controls.Add(reviewFold);
            var human=Row(reviewFold);reviewer=new TextField("검수자"){value=reviewerName};reviewer.AddToClassList("reviewer-field");human.Add(reviewer);
            reviewer.RegisterValueChangedCallback(e=>{reviewerName=e.newValue;RefreshReviewButtons();});
            reviewReason=new TextField("검수 이유");reviewReason.AddToClassList("review-reason");human.Add(reviewReason);reviewReason.RegisterValueChangedCallback(_=>RefreshReviewButtons());
            var decisionRow=Row(reviewFold);reviewDecision=new DropdownField("결정",new List<string>{"pending","approved","rejected"},0);decisionRow.Add(reviewDecision);
            reviewDecision.RegisterValueChangedCallback(_=>RefreshReviewButtons());
            confirmed=new Toggle("직접 청음·입력과 현재 시각을 확인함");decisionRow.Add(confirmed);confirmed.RegisterValueChangedCallback(_=>RefreshReviewButtons());
            reviewSelected=new Button(()=>RecordReview(false)){text="선택 노트 검수 기록"};decisionRow.Add(reviewSelected);
            reviewPhrase=new Button(()=>RecordReview(true)){text="아래 구간 전체 검수 기록"};decisionRow.Add(reviewPhrase);
            summary=Label("","review-summary");reviewFold.Add(summary);

            var play=Row(controls);from=new DoubleField("구간 시작"){value=21.86,isDelayed=true};to=new DoubleField("끝"){value=31.18,isDelayed=true};play.Add(from);play.Add(to);
            from.RegisterValueChangedCallback(_=>RangeChanged());to.RegisterValueChangedCallback(_=>RangeChanged());
            play.Add(new Button(PlaySection){text="현재 채보 적용 (Game 뷰 4박)"});
            var auto=new Toggle("자동 입력");auto.RegisterValueChangedCallback(e=>{if(runnerMode&&Runner)Runner.autoPlay=e.newValue;else if(Game)Game.autoPlay=e.newValue;});play.Add(auto);
            var god=new Toggle("무적");god.RegisterValueChangedCallback(e=>{if(runnerMode&&Runner)Runner.invincible=e.newValue;else if(Game)Game.invincible=e.newValue;});play.Add(god);
            var repeat=new Toggle("반복");repeat.RegisterValueChangedCallback(e=>{if(runnerMode&&Runner){Runner.loop=e.newValue;Runner.loopStart=from.value;Runner.loopEnd=to.value;}else if(Game){Game.loop=e.newValue;Game.loopStart=from.value;Game.loopEnd=to.value;}});play.Add(repeat);
            var waveFold=new Foldout{text="원본·수정본 A/B 청음과 파형",value=false};controls.Add(waveFold);
            var compare=Row(waveFold);
            compare.Add(new Button(()=>PlayComparison(true)){text="A 원시 AI 시각 청음 (4박)"});
            compare.Add(new Button(()=>PlayComparison(false)){text="B 현재 시각 청음 (4박)"});
            compare.Add(new Button(()=>{audition.Stop();status.text="A/B 청음 정지. 게임을 계속하려면 Game 뷰에서 재개하세요.";}){text="청음 정지"});
            compare.Add(new Button(ReadWaveform){text="파형 읽기/새로고침"});
            waveFold.Add(Label("파형 아래 주황: 원시 AI / 회색: 이전 보정 / 청록: 현재 / 빨강: 거절. 파형 클릭은 추가 시각만 선택합니다. A/B는 현재 남아 있는 AI 노트만 비교하며 수동 추가 노트는 제외합니다.","notice"));
            waveform=new ChartReviewWaveform();waveFold.Add(waveform);waveform.TimePicked+=t=>{addTime.value=t;status.text=$"추가 시각 {t:F6}초 선택 (아직 추가하지 않음)";};
            rangeLabel=Label("","range-label");waveFold.Add(rangeLabel);waveStatus=Label("파형 읽기 버튼으로 원곡 PCM을 표시하세요. 실제 청음 여부와 무관합니다.","notice");waveFold.Add(waveStatus);

            var edits=Row(controls);addTime=new DoubleField("추가 시각"){value=15,isDelayed=true};edits.Add(addTime);edits.Add(new Button(AddNote){text="노트 추가"});
            var why=new TextField("수정 이유"){value=reason};why.RegisterValueChangedCallback(e=>reason=e.newValue);edits.Add(why);
            edits.Add(new Button(ExportLog){text="입력 로그 CSV"});edits.Add(new Button(ExportDecisions){text="누락 포함 노트 결과 CSV"});
            edits.Add(new Button(ValidateCurrentDocument){text="현재 채보 기술 검사"});edits.Add(new Button(RunBaselineTests){text="기본 채보 회귀검사"});
            edits.Add(new Button(()=>{Selection.activeObject=AssetDatabase.LoadAssetAtPath<RunnerRules>(RunnerSceneBuilder.RulesPath);EditorGUIUtility.PingObject(Selection.activeObject);}){text="HP·질주·미리보기 규칙"});
            var filters=Row(root);largeOnly=new Toggle("큰 이동만 (|원시→현재| ≥ 실제 판정창)");largeOnly.RegisterValueChangedCallback(_=>DrawRows());filters.Add(largeOnly);
            rangeOnly=new Toggle("현재 구간만");rangeOnly.RegisterValueChangedCallback(_=>DrawRows());filters.Add(rangeOnly);
            reviewFilter=new DropdownField("검수 필터",new List<string>{"all","pending","approved","rejected"},0);reviewFilter.RegisterValueChangedCallback(_=>DrawRows());filters.Add(reviewFilter);
            filters.Add(new Button(()=>{foreach(var n in VisibleNotes())selected.Add((string)n["id"]);DrawRows();}){text="표시된 노트 선택"});
            filters.Add(new Button(()=>{selected.Clear();DrawRows();}){text="선택 해제"});
            table=new ScrollView();table.AddToClassList("notes");root.Add(table);
            status=Label("","status");root.Add(status);
            if(!string.IsNullOrEmpty(workingJson))
            {
                try{document=JObject.Parse(workingJson);AfterechoChartReview.RefreshSummary(document);DrawRows();status.text="작업 중인 문서를 복원했습니다.";}
                catch(Exception e){status.text="작업 문서 복원 실패: "+e.Message;}
            }
            else Load();
            PauseGameplayForReview();
        }
        void OnFocus()=>PauseGameplayForReview();
        void PauseGameplayForReview()
        {
            if(!EditorApplication.isPlaying)return;
            if(Runner!=null&&Runner.Run!=null)Runner.PauseGame();
            if(Game!=null&&Game.Run!=null)Game.PauseGame();
        }
        void OnDisable(){audition?.Dispose();if(document!=null)workingJson=document.ToString();}
        static Label Label(string text,string css){var l=new Label(text);l.AddToClassList(css);return l;}
        VisualElement Row(VisualElement parent){var r=new VisualElement();r.AddToClassList("toolbar");parent.Add(r);return r;}
        void Touch(){audition?.Stop();dirty=true;workingJson=document.ToString();}
        string ExportDir {get{string p=Path.GetFullPath(runnerMode?"PlaytestExports/Runner":"PlaytestExports");Directory.CreateDirectory(p);return p;}}
        string PreserveDraft()
        {
            if(!dirty||document==null)return "";
            string p=Path.Combine(ExportDir,$"{preset}-draft-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
            AfterechoChartReview.RefreshSummary(document);File.WriteAllText(p,document.ToString());return p;
        }
        void SetDocument(JObject next,string name,string backup)
        {
            audition?.Stop();document=next;AfterechoChartReview.RefreshSummary(document);preset=(string)document["preset"];runnerMode=(string)document["gameplayMode"]=="runner";
            mode.SetValueWithoutNotify(runnerMode?"runner":"legacy combat");difficulty.SetValueWithoutNotify(preset);
            selected.Clear();dirty=false;workingJson=document.ToString();DrawRows();
            status.text=name+(backup.Length>0?"\n이전 편집본은 검증 전 초안으로 보존: "+backup:"");
        }
        void SwitchSource(bool runner,string value)
        {
            try
            {
                var next=JObject.Parse(File.ReadAllText("Assets/Afterecho/Resources/Afterecho/"+(runner?"RunnerCharts/":"Charts/")+value+".json"));
                string backup=PreserveDraft();SetDocument(next,"기본 채보를 읽었습니다. 원본 파일은 수정하지 않습니다.",backup);
            }
            catch(Exception e){mode.SetValueWithoutNotify(runnerMode?"runner":"legacy combat");difficulty.SetValueWithoutNotify(preset);status.text=e.Message;}
        }
        void Load()=>SwitchSource(runnerMode,preset);
        void OpenDocument()
        {
            try
            {
                string p=EditorUtility.OpenFilePanel("채보 열기",ExportDir,"json");if(p.Length==0)return;
                var next=JObject.Parse(File.ReadAllText(p));
                if((string)next["format"]!="afterecho-chart-v1"||!(next["notes"] is JArray)||next["rules"]==null||next["song"]==null)
                    throw new FormatException("afterecho-chart-v1 채보와 notes/rules/song이 필요합니다.");
                string backup=PreserveDraft();SetDocument(next,"불러옴: "+p,backup);
            }
            catch(Exception e){status.text=e.Message;}
        }
        IEnumerable<JObject> VisibleNotes()
        {
            if(document==null)return Enumerable.Empty<JObject>();
            return AfterechoChartReview.Notes(document).OrderBy(n=>(double)n["time"])
                .Where(n=>!largeOnly.value||AfterechoChartReview.LargeMove(document,n))
                .Where(n=>!rangeOnly.value||((double)n["time"]>=from.value&&(double)n["time"]<to.value))
                .Where(n=>reviewFilter.value=="all"||AfterechoChartReview.State(document,n)==reviewFilter.value);
        }
        void DrawRows()
        {
            if(document==null||table==null)return;
            table.Clear();selected.IntersectWith(AfterechoChartReview.Notes(document).Select(n=>(string)n["id"]));
            int shown=0;
            foreach(var note in VisibleNotes().ToArray())
            {
                shown++;string id=(string)note["id"];var row=new VisualElement();row.AddToClassList("note-row");table.Add(row);
                string state=AfterechoChartReview.State(document,note);row.AddToClassList("review-"+state);
                var top=Row(row);var chosen=new Toggle(){value=selected.Contains(id)};top.Add(chosen);
                chosen.RegisterValueChangedCallback(e=>{if(e.newValue)selected.Add(id);else selected.Remove(id);RefreshReviewButtons();});
                top.Add(Label(id,"note-id"));top.Add(Label(state,"note-state"));
                var time=new DoubleField("현재 초"){value=(double)note["time"],isDelayed=true};time.AddToClassList("note-time");top.Add(time);
                double? raw=AfterechoChartReview.SourceTime(note),previous=AfterechoChartReview.Number(note["parentTime"]);
                double window=AfterechoChartReview.Window(document,note),move=raw.HasValue?((double)note["time"]-raw.Value)*1000:0;
                var timing=Label($"원시 AI {(raw.HasValue?raw.Value.ToString("F6"):"없음")}  |  이전 {(previous.HasValue?previous.Value.ToString("F6"):"없음")}  |  이동 {(raw.HasValue?move.ToString("+0.0;-0.0;0.0")+"ms":"해당 없음")}  |  ±{window*1000:0.0}ms","note-meta");
                if(raw.HasValue&&Math.Abs(move)>=window*1000-1e-6)timing.AddToClassList("large-move");row.Add(timing);
                var record=AfterechoChartReview.LatestRecord(document,note);
                string detail=$"{note["section"]} / 출처: {note["source"]} / 원본 재시각화: {(raw.HasValue?(Math.Abs(move)>1e-6?"예":"아니오"):"AI 원시 시각 없음")}";
                if(record!=null)detail+=$"\n최신 검수: {record["reviewer"]} · {record["reviewedUtc"]} · {record["reason"]}";
                row.Add(Label(detail,"note-evidence"));
                time.RegisterValueChangedCallback(e=>{
                    try{AfterechoChartReview.Shift(document,note,e.newValue,reason);Touch();DrawRows();status.text="시각 수정. 기존 승인은 무효화되며 원시·이전 시각과 검수 역사는 보존됩니다.";}
                    catch(Exception ex){time.SetValueWithoutNotify((double)note["time"]);status.text=ex.Message;}
                });
                var seek=new Button(()=>{from.SetValueWithoutNotify(Math.Max(0,(double)note["time"]-.5));to.SetValueWithoutNotify(Math.Min(Duration,from.value+8));RangeChanged();}){text="이 구간 보기"};top.Add(seek);
                var del=new Button(()=>{AfterechoChartReview.Delete(document,note,reason);selected.Remove(id);Touch();DrawRows();status.text="삭제 기록과 이전 검수 내역을 보존했습니다.";}){text="삭제"};top.Add(del);
            }
            var all=AfterechoChartReview.Notes(document).ToArray();int approved=all.Count(n=>AfterechoChartReview.State(document,n)=="approved"),rejected=all.Count(n=>AfterechoChartReview.State(document,n)=="rejected");
            summary.text=$"표시 {shown}/{all.Length} · 승인 {approved} · 거절 {rejected} · 대기 {all.Length-approved-rejected} · 학습 사용 미승인";
            RefreshReviewButtons();RefreshWaveform();
        }
        void RefreshReviewButtons()
        {
            if(reviewSelected==null||reviewer==null||reviewReason==null||reviewDecision==null||confirmed==null)return;
            bool ready=!string.IsNullOrWhiteSpace(reviewer.value)&&!string.IsNullOrWhiteSpace(reviewReason.value)&&(reviewDecision.value!="approved"||confirmed.value);
            reviewSelected.text=$"선택 {selected.Count}개에 검수 기록";reviewSelected.SetEnabled(ready&&selected.Count>0);
            int count=document==null||from==null||to==null?0:AfterechoChartReview.Notes(document).Count(n=>(double)n["time"]>=from.value&&(double)n["time"]<to.value);
            reviewPhrase.text=$"구간 {count}개에 구절 검수 기록";reviewPhrase.SetEnabled(ready&&count>0);
        }
        void RecordReview(bool phrase)
        {
            try
            {
                var ids=phrase?AfterechoChartReview.Notes(document).Where(n=>(double)n["time"]>=from.value&&(double)n["time"]<to.value).Select(n=>(string)n["id"]).ToArray():selected.ToArray();
                int count=AfterechoChartReview.Review(document,ids,reviewDecision.value,reviewer.value,reviewReason.value,confirmed.value,phrase,phrase?from.value:(double?)null,phrase?to.value:(double?)null);
                confirmed.SetValueWithoutNotify(false);Touch();DrawRows();status.text=$"{count}개 {reviewDecision.value} 기록 저장. 학습 사용은 별도 승인 전까지 false입니다.";
            }
            catch(Exception e){status.text=e.Message;}
        }
        void AddNote()
        {
            try{AfterechoChartReview.Add(document,addTime.value,reason);Touch();DrawRows();status.text="수동 노트 추가. AI 생성 또는 사람의 검수 완료로 표시하지 않습니다.";}
            catch(Exception e){status.text=e.Message;}
        }
        void RangeChanged(){RefreshWaveform();RefreshReviewButtons();if(rangeOnly!=null&&rangeOnly.value)DrawRows();}
        void RefreshWaveform()
        {
            if(waveform==null||document==null||from==null||to==null)return;
            waveform.SetDocument(document,from.value,to.value);
            rangeLabel.text=$"{from.value:F3}s — {to.value:F3}s   ·  A/B는 동일한 원곡 절대 시각 구간입니다.";
        }
        void ReadWaveform(){waveStatus.text=waveform.ReadAudio(Resources.Load<AudioClip>("Afterecho/Audio/Untitled"));RefreshWaveform();}
        void PlayComparison(bool raw)
        {
            try
            {
                if(!EditorApplication.isPlaying)throw new Exception("Unity Play를 누른 뒤 A/B 청음을 시작하세요.");
                if(runnerMode?Runner==null:Game==null)throw new Exception("현재 검수 모드에 맞는 씬을 Play로 실행하세요.");
                var stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
                var selection=AfterechoChartReview.BuildComparison(document,from.value,to.value);
                PauseGameplayForReview();
                var owner=Runner;var legacy=Game;var run=ActiveRun;
                var expectedPhase=runnerMode&&owner!=null?owner.Phase:legacy!=null?legacy.Phase:GamePhase.Menu;
                bool useRunner=runnerMode;
                Func<bool> sameSession=()=>useRunner
                    ? owner!=null&&owner.Phase==expectedPhase&&ReferenceEquals(owner.Run,run)
                    : legacy!=null&&legacy.Phase==expectedPhase&&ReferenceEquals(legacy.Run,run);
                string name=(raw?"A 원시 AI 시각":"B 현재 시각")+$" (수동 {selection.manualExcluded}개 제외)";
                audition.Play(Resources.Load<AudioClip>("Afterecho/Audio/Untitled"),stage.beat,selection.from,selection.to,
                    raw?selection.sourceTimes:selection.currentTimes,name,text=>{if(status!=null)status.text=text;},sameSession);
                rangeLabel.text=$"A/B 공통 청음 범위 {selection.from:F3}–{selection.to:F3}s · 같은 {selection.noteIds.Length}개 AI 노트. 구간 밖 원시 시각도 양쪽 비교에 포함하도록 확장합니다.";
            }
            catch(Exception e){status.text=e.Message;}
        }
        ChartData ValidateAndUpdate()
        {
            if((string)document["format"]!="afterecho-chart-v1")throw new Exception("afterecho-chart-v1 형식만 지원합니다.");
            var stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
            var noteArray=(JArray)document["notes"];var sorted=noteArray.OrderBy(n=>(double)n["time"]).ToArray();
            noteArray.RemoveAll();foreach(var note in sorted)noteArray.Add(note);
            var chart=ChartData.Load(document.ToString());
            if(runnerMode)
            {
                var rules=Runner?Runner.rules:AssetDatabase.LoadAssetAtPath<RunnerRules>(RunnerSceneBuilder.RulesPath);
                RunnerTests.ValidateChart(chart,stage,rules!=null?(double?)rules.previewSeconds:null);
            }
            else AfterechoTests.ValidateChart(chart,stage);
            var encounters=(JArray)document["encounters"];double ratio=preset=="easy"?.5:preset=="normal"?.65:.8;
            for(int i=0;i<chart.notes.Length;i++)
            {
                var n=(JObject)document["notes"][i];double time=(double)n["time"];
                n["window"]=ChartEngine.Window(chart,i);n["role"]=runnerMode?"run":time<stage.MainTime?"practice":"move";n.Remove("encounter");
                var section=stage.sections.LastOrDefault(s=>stage.beats[s.fromBeat]<=time)??stage.sections[0];n["section"]=section.code;
            }
            if(!runnerMode)foreach(JObject e in encounters)
            {
                var members=((JArray)document["notes"]).Where(n=>(double)n["time"]>=(double)e["start"]&&(double)n["time"]<(double)e["end"]).ToArray();
                if(members.Length<4)throw new Exception(e["id"]+": 전투 구간에는 최소 4개 노트가 필요합니다.");
                e["noteIds"]=new JArray(members.Select(n=>(string)n["id"]));e["hp"]=(int)Math.Ceiling(members.Length*ratio);
                foreach(JObject n in members){n["role"]="attack";n["encounter"]=e["id"].DeepClone();}
            }
            AfterechoChartReview.RefreshSummary(document);workingJson=document.ToString();return ChartData.Load(workingJson);
        }
        void ValidateCurrentDocument()
        {
            try{var chart=ValidateAndUpdate();DrawRows();status.text=$"현재 편집 채보 기술 검사 통과 · {chart.preset} · {chart.notes.Length} notes\n사람의 청음·한 손 검수와 학습 승인은 별도입니다.";}
            catch(Exception e){status.text="현재 편집 채보 검사 실패: "+e.Message;}
        }
        void RunBaselineTests()
        {
            try
            {
                string result=runnerMode?RunnerTests.RunAll():AfterechoTests.RunAll();
                string summaryLine=result.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"검사 완료";
                string report=Path.GetFullPath(runnerMode?"PlaytestExports/Runner/core-tests.txt":"PlaytestExports/core-tests.txt");
                status.text="Resources 기본 채보 검사 (현재 편집본과 별도)\n"+summaryLine+"\n전체 보고서: "+report;
            }
            catch(Exception e){status.text="기본 채보 검사 실패: "+e.Message;}
        }
        void SaveVariant()
        {
            try{ValidateAndUpdate();string p=Path.Combine(ExportDir,$"{preset}-edited-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");File.WriteAllText(p,document.ToString());dirty=false;workingJson=document.ToString();status.text="수정본 저장: "+p+"\n기본 배포 채보는 바꾸지 않았습니다.";}
            catch(Exception e){status.text="검증 수정본을 저장하지 않음: "+e.Message+"\n계속 편집하거나 검증 전 초안 저장을 사용하세요.";}
        }
        void SaveDraft()
        {
            try{dirty=true;string p=PreserveDraft();workingJson=document.ToString();status.text="검증 전 초안 저장: "+p+"\n이 파일은 기술 검사·배포·학습 승인 결과가 아닙니다.";}
            catch(Exception e){status.text=e.Message;}
        }
        static string Q(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
        static string F(double? d)=>d.HasValue?d.Value.ToString("F6",CultureInfo.InvariantCulture):"";
        void ExportCSV()
        {
            try
            {
                ValidateAndUpdate();string p=Path.Combine(ExportDir,preset+"-notes.csv");
                File.WriteAllLines(p,new[]{"noteId,rawAiTime,previousTime,currentTime,shiftMs,windowSeconds,sourceRetimed,section,source,reviewStatus,teamReview,reviewer,reviewedUtc,reason"}.Concat(AfterechoChartReview.Notes(document).Select(n=>{
                    var raw=AfterechoChartReview.SourceTime(n);var r=AfterechoChartReview.LatestRecord(document,n);double current=(double)n["time"];
                    return string.Join(",",Q((string)n["id"]),F(raw),F(AfterechoChartReview.Number(n["parentTime"])),F(current),F(raw.HasValue?(current-raw.Value)*1000:(double?)null),F(AfterechoChartReview.Window(document,n)),raw.HasValue?(Math.Abs(current-raw.Value)>1e-9?"true":"false"):"unknown",Q((string)n["section"]),Q((string)n["source"]),Q((string)n["reviewStatus"]),Q(AfterechoChartReview.State(document,n)),Q((string)r?["reviewer"]),Q((string)r?["reviewedUtc"]),Q((string)r?["reason"]));
                })));status.text="시각·검수 CSV 저장: "+p;
            }
            catch(Exception e){status.text=e.Message;}
        }
        void ExportLog()
        {
            if(ActiveRun==null){status.text="Play Mode에서 게임을 먼저 실행하세요.";return;}
            string p=Path.Combine(ExportDir,"input-log.csv");File.WriteAllLines(p,new[]{"time,noteId,targetTime,error,result,source"}.Concat(ActiveRun.Inputs.Select(l=>string.Join(",",F(l.time),Q(l.noteId),F(l.targetTime),F(l.error),Q(l.result),Q(l.source)))));status.text="입력 로그 저장: "+p;
        }
        void ExportDecisions()
        {
            var run=ActiveRun;if(run==null){status.text="Play Mode에서 게임을 먼저 실행하세요.";return;}
            string p=Path.Combine(ExportDir,"note-decisions.csv");
            File.WriteAllLines(p,new[]{"noteId,targetTime,windowSeconds,decision,inputTime,errorSeconds,source"}.Concat(run.Chart.notes.Select((n,i)=>{
                var hit=run.Inputs.LastOrDefault(l=>l.noteId==n.id&&l.result=="hit");
                string state=run.Decisions[i]==NoteState.Missed?"omitted":run.Decisions[i].ToString().ToLowerInvariant();
                return string.Join(",",Q(n.id),F(n.time),F(run.WindowAt(i)),Q(state),F(hit?.time),F(hit?.error),Q(hit?.source));
            })));status.text="무입력 누락·대기·건너뜀 포함 노트 결과 CSV 저장: "+p;
        }
        void PlaySection()
        {
            try
            {
                if(!EditorApplication.isPlaying||(runnerMode?Runner==null:Game==null)){status.text="Unity Play를 누른 후 사용하세요.";return;}
                if(double.IsNaN(from.value)||double.IsInfinity(from.value)||double.IsNaN(to.value)||double.IsInfinity(to.value)
                    ||from.value<0||to.value<=from.value||to.value>Duration)throw new Exception("구간은 곡 길이 안의 유한한 숫자로 지정하세요.");
                var chart=ValidateAndUpdate();audition.Stop();
                if(runnerMode){Runner.difficulty=preset;Runner.loopStart=from.value;Runner.loopEnd=to.value;Runner.LabSeek(from.value,chart);Runner.PauseGame();}else{Game.difficulty=preset;Game.loopStart=from.value;Game.loopEnd=to.value;Game.LabSeek(from.value,chart);Game.PauseGame();}
                status.text="검증한 현재 채보를 일시정지 상태로 적용했습니다. Game 뷰에서 재개하면 4박 뒤 시작하며 반복 설정은 유지합니다. A/B 원시 시각은 게임에 반영하지 않았습니다.";
            }
            catch(Exception e){status.text=e.Message;}
        }
    }
}
