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
        string preset="easy";
        bool runnerMode=true;
        RunnerGame Runner => UnityEngine.Object.FindFirstObjectByType<RunnerGame>();
        ChartEngine ActiveRun => runnerMode ? Runner?.Run : Game?.Run;
        ScrollView table;
        Label status;
        DoubleField from,to,addTime;
        string reason="playtest timing adjustment";
        AfterechoGame Game => UnityEngine.Object.FindFirstObjectByType<AfterechoGame>();
        [MenuItem("Afterecho/Chart Lab")]
        public static void Open() { var w=GetWindow<AfterechoChartLab>();w.titleContent=new GUIContent("잔향 · 채보 테스트실");w.minSize=new Vector2(760,500); }
        public void CreateGUI()
        {
            var root=rootVisualElement;root.Clear();root.AddToClassList("lab");
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Afterecho/Editor/ChartLab.uss"));
            var h=new Label("AFTERECHO  /  채보 테스트실");h.AddToClassList("heading");root.Add(h);
            var notice=new Label("원본 AI 출처와 수정 이유를 보존합니다. 수정본은 별도 파일로 저장됩니다.\n게임 뷰로 돌아가 계속하기를 눌러야 음악이 재개됩니다. 플레이 테스트 결과를 검수 완료와 구분하세요.");notice.AddToClassList("notice");root.Add(notice);
            var tools=Row(root);
            var mode=new DropdownField("모드",new List<string>{"runner","legacy combat"},0);tools.Add(mode);mode.RegisterValueChangedCallback(e=>{runnerMode=e.newValue=="runner";Load();});
            var difficulty=new DropdownField("난이도",new List<string>{"easy","normal","hard"},0);tools.Add(difficulty);difficulty.RegisterValueChangedCallback(e=>{preset=e.newValue;Load();});
            tools.Add(new Button(Load){text="원본 다시 읽기"});tools.Add(new Button(SaveVariant){text="수정본 JSON 저장"});tools.Add(new Button(ExportCSV){text="CSV 저장"});
            tools.Add(new Button(()=>{string p=EditorUtility.OpenFilePanel("채보 열기","Assets/Afterecho/Resources/Afterecho/Charts","json");if(p!=""){document=JObject.Parse(File.ReadAllText(p));preset=(string)document["preset"];runnerMode=(string)document["gameplayMode"]=="runner";mode.SetValueWithoutNotify(runnerMode?"runner":"legacy combat");difficulty.SetValueWithoutNotify(preset);DrawRows();}}){text="JSON 불러오기"});
            var play=Row(root);
            from=new DoubleField("구간 시작"){value=21.86};to=new DoubleField("끝"){value=31.18};play.Add(from);play.Add(to);
            play.Add(new Button(PlaySection){text="4박 후 구간 재생"});
            var auto=new Toggle("자동 입력");auto.RegisterValueChangedCallback(e=>{if(runnerMode&&Runner)Runner.autoPlay=e.newValue;else if(Game)Game.autoPlay=e.newValue;});play.Add(auto);
            var god=new Toggle("무적");god.RegisterValueChangedCallback(e=>{if(runnerMode&&Runner)Runner.invincible=e.newValue;else if(Game)Game.invincible=e.newValue;});play.Add(god);
            var repeat=new Toggle("반복");repeat.RegisterValueChangedCallback(e=>{if(runnerMode&&Runner){Runner.loop=e.newValue;Runner.loopStart=from.value;Runner.loopEnd=to.value;}else if(Game){Game.loop=e.newValue;Game.loopStart=from.value;Game.loopEnd=to.value;}});play.Add(repeat);
            var edits=Row(root);addTime=new DoubleField("추가 시각"){value=15};edits.Add(addTime);edits.Add(new Button(AddNote){text="노트 추가"});
            var why=new TextField("수정 이유"){value=reason};why.RegisterValueChangedCallback(e=>reason=e.newValue);edits.Add(why);
            edits.Add(new Button(ExportLog){text="입력 로그 CSV"});edits.Add(new Button(()=>status.text=runnerMode?RunnerTests.RunAll():AfterechoTests.RunAll()){text="판정 자동 검증"});
            var tune=Row(root);tune.Add(new Button(()=>{Selection.activeObject=AssetDatabase.LoadAssetAtPath<RunnerRules>(RunnerSceneBuilder.RulesPath);EditorGUIUtility.PingObject(Selection.activeObject);}){text="러닝 규칙 조절 (HP·질주·속도·미리보기)"});
            var header=new Label("노트 ID                       시각(초)           구간 / 판정창 / 검수 상태                  이동 · 삭제");root.Add(header);
            table=new ScrollView();table.AddToClassList("notes");root.Add(table);
            status=new Label();status.AddToClassList("status");root.Add(status);Load();
        }
        VisualElement Row(VisualElement parent){var r=new VisualElement();r.AddToClassList("toolbar");parent.Add(r);return r;}
        void Load(){document=JObject.Parse(File.ReadAllText("Assets/Afterecho/Resources/Afterecho/"+(runnerMode?"RunnerCharts/":"Charts/")+preset+".json"));DrawRows();}
        void DrawRows()
        {
            table.Clear();var notes=(JArray)document["notes"];
            foreach(var note in notes.ToArray())
            {
                var row=new VisualElement();row.AddToClassList("note-row");table.Add(row);
                var id=new Label((string)note["id"]);id.AddToClassList("note-id");row.Add(id);
                var time=new DoubleField(){value=(double)note["time"],isDelayed=true};time.AddToClassList("note-time");row.Add(time);
                var meta=new Label($"{note["section"]} / ±{(double?)note["window"]*1000:0}ms / {note["teamReview"]}");meta.AddToClassList("note-meta");row.Add(meta);
                time.RegisterValueChangedCallback(e=>{Record((string)note["id"],"shift",(double)note["time"],e.newValue);note["time"]=e.newValue;note["teamReview"]="pending";note["reviewStatus"]="edited_pending";status.text="수정됨. 적용/저장할 때 현재 모드의 채보 규칙을 검증합니다.";});
                var seek=new Button(()=>{from.value=Math.Max(0,(double)note["time"]-.5);to.value=Math.Min(136.36,from.value+8);PlaySection();}){text="▶"};seek.AddToClassList("note-action");row.Add(seek);
                var del=new Button(()=>{Record((string)note["id"],"delete",(double)note["time"],null);note.Remove();DrawRows();}){text="삭제"};del.AddToClassList("note-action");row.Add(del);
            }
            status.text=$"{preset} · {notes.Count} notes · 자동 기술 검증 / 팀 청음 및 한 손 난이도 검수 대기";
        }
        void Record(string id,string action,double? original,double? revised)
        {
            if(document["edits"]==null)document["edits"]=new JArray();
            ((JArray)document["edits"]).Add(new JObject{["noteId"]=id,["action"]=action,["originalTime"]=original,["revisedTime"]=revised,["reason"]=reason,["timestampUtc"]=DateTime.UtcNow.ToString("o"),["reviewStatus"]="team_pending"});
            document["review"]!["musicalAndOneHand"]="team_pending";document["review"]!["trainingEligible"]=false;
        }
        void AddNote()
        {
            string id="EDIT-"+Guid.NewGuid().ToString("N").Substring(0,12);
            ((JArray)document["notes"]).Add(new JObject{["id"]=id,["time"]=addTime.value,["originalTime"]=addTime.value,["source"]="manual_edit",["reviewStatus"]="edited_pending",["teamReview"]="pending"});
            Record(id,"add",null,addTime.value);DrawRows();
        }
        ChartData ValidateAndUpdate()
        {
            if((string)document["format"]!="afterecho-chart-v1")throw new Exception("afterecho-chart-v1 형식만 지원합니다. 이전 자유/큰 박 설정은 웹 비교 모드에서 사용하세요.");
            var stage=JsonUtility.FromJson<StageData>(Resources.Load<TextAsset>("Afterecho/stage").text);
            // Keep the same JObjects: visible row callbacks retain these references after export.
            var noteArray=(JArray)document["notes"];
            var sorted=noteArray.OrderBy(n=>(double)n["time"]).ToArray();
            noteArray.RemoveAll();foreach(var note in sorted)noteArray.Add(note);
            var chart=ChartData.Load(document.ToString());
            if(runnerMode)RunnerTests.ValidateChart(chart,stage);else AfterechoTests.ValidateChart(chart,stage);
            var encounters=(JArray)document["encounters"];
            double ratio=preset=="easy"?.5:preset=="normal"?.65:.8;
            for(int i=0;i<chart.notes.Length;i++)
            {
                var n=(JObject)document["notes"]![i];double time=(double)n["time"];
                n["window"]=ChartEngine.Window(chart,i);n["role"]=runnerMode?"run":time<stage.MainTime?"practice":"move";n.Remove("encounter");
                var section=stage.sections.LastOrDefault(s=>stage.beats[s.fromBeat]<=time)??stage.sections[0];n["section"]=section.code;
            }
            if(!runnerMode)foreach(JObject e in encounters)
            {
                var members=((JArray)document["notes"]).Where(n=>(double)n["time"]>=(double)e["start"] && (double)n["time"]<(double)e["end"]).ToArray();
                if(members.Length<4)throw new Exception(e["id"]+": 전투 구간에는 최소 4개 노트가 필요합니다.");
                e["noteIds"]=new JArray(members.Select(n=>(string)n["id"]));e["hp"]=(int)Math.Ceiling(members.Length*ratio);
                foreach(JObject n in members){n["role"]="attack";n["encounter"]=e["id"].DeepClone();}
            }
            return ChartData.Load(document.ToString());
        }
        string ExportDir {get{string p=Path.GetFullPath(runnerMode?"PlaytestExports/Runner":"PlaytestExports");Directory.CreateDirectory(p);return p;}}
        void SaveVariant()
        {
            try{ValidateAndUpdate();string p=Path.Combine(ExportDir,$"{preset}-edited-{DateTime.Now:yyyyMMdd-HHmmss}.json");File.WriteAllText(p,document.ToString());status.text="저장: "+p;}
            catch(Exception e){status.text="저장하지 않음: "+e.Message;}
        }
        static string Q(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
        void ExportCSV()
        {
            try{var c=ValidateAndUpdate();string p=Path.Combine(ExportDir,preset+"-notes.csv");
                File.WriteAllLines(p,new[]{"noteId,timeSeconds,windowSeconds,section,role,source,reviewStatus,teamReview"}.Concat(c.notes.Select((n,i)=>string.Join(",",Q(n.id),n.time.ToString("F6",CultureInfo.InvariantCulture),ChartEngine.Window(c,i).ToString("F6",CultureInfo.InvariantCulture),Q(n.section),Q(n.role),Q(n.source),Q(n.reviewStatus),Q(n.teamReview)))));status.text="CSV 저장: "+p;}
            catch(Exception e){status.text=e.Message;}
        }
        void ExportLog()
        {
            if(ActiveRun==null){status.text="Play Mode에서 게임을 먼저 실행하세요.";return;}
            string p=Path.Combine(ExportDir,"input-log.csv");File.WriteAllLines(p,new[]{"time,noteId,targetTime,error,result,source"}.Concat(ActiveRun.Inputs.Select(l=>string.Join(",",l.time.ToString("F6",CultureInfo.InvariantCulture),Q(l.noteId),l.targetTime.ToString("F6",CultureInfo.InvariantCulture),l.error.ToString("F6",CultureInfo.InvariantCulture),Q(l.result),Q(l.source)))));status.text="로그 저장: "+p;
        }
        void PlaySection()
        {
            try
            {
                if(!EditorApplication.isPlaying || (runnerMode?Runner==null:Game==null)){status.text="Unity Play를 누른 후 사용하세요.";return;}
                if(from.value<0 || to.value<=from.value || to.value>136.36)throw new Exception("구간은 0~136.36초 안에서 지정하세요.");
                if(runnerMode){Runner.difficulty=preset;Runner.loopStart=from.value;Runner.loopEnd=to.value;Runner.LabSeek(from.value,ValidateAndUpdate());}else{Game.difficulty=preset;Game.loopStart=from.value;Game.loopEnd=to.value;Game.LabSeek(from.value,ValidateAndUpdate());}status.text="채보 적용 완료. Game 뷰로 돌아가 4박 후 계속하기를 누르세요.";
            }catch(Exception e){status.text=e.Message;}
        }
    }
}
