// Run through the connected Editor CLI after scripts compile, outside Play mode.
if(UnityEditor.EditorApplication.isPlaying)throw new System.Exception("Stop Play before tooling regressions");
var checks=new System.Collections.Generic.List<string>();
System.Action<bool,string> check=(ok,label)=>{if(!ok)throw new System.Exception(label);checks.Add("PASS "+label);};
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var lab=UnityEngine.ScriptableObject.CreateInstance<Afterecho.Editor.AfterechoChartLab>();
var fixture=new UnityEngine.GameObject("Afterecho_ToolingRegression");fixture.SetActive(false);
try
{
    lab.CreateGUI();
    var type=lab.GetType();
    var documentField=type.GetField("document",flags);
    var status=(UnityEngine.UIElements.Label)type.GetField("status",flags).GetValue(lab);
    var validate=type.GetMethod("ValidateCurrentDocument",flags);
    var original=Newtonsoft.Json.Linq.JObject.Parse(UnityEngine.Resources.Load<UnityEngine.TextAsset>("Afterecho/RunnerCharts/easy").text);
    var edited=(Newtonsoft.Json.Linq.JObject)original.DeepClone();
    edited["notes"][1]["time"]=(double)edited["notes"][0]["time"];
    documentField.SetValue(lab,edited);validate.Invoke(lab,null);
    check(status.text.StartsWith("현재 편집 채보 검증 실패:"),"duplicate time in visible document is rejected");
    check((double)edited["notes"][1]["time"]==(double)edited["notes"][0]["time"],"failed validation does not replace edits with Resources");

    edited=(Newtonsoft.Json.Linq.JObject)original.DeepClone();
    ((Newtonsoft.Json.Linq.JArray)edited["notes"]).RemoveAt(0);
    int count=((Newtonsoft.Json.Linq.JArray)edited["notes"]).Count;
    documentField.SetValue(lab,edited);validate.Invoke(lab,null);
    check(status.text.StartsWith("현재 편집 채보 검증 통과")&&status.text.Contains(count+" notes"),"valid edited document reports its own note count");
    check(object.ReferenceEquals(documentField.GetValue(lab),edited),"validation preserves the edited document");
    var buttons=UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Button>(lab.rootVisualElement).ToList();
    check(System.Linq.Enumerable.Any(buttons,b=>b.text=="현재 채보 검증")&&System.Linq.Enumerable.Any(buttons,b=>b.text=="기본 채보 회귀검사"),"current chart and baseline validation are separate actions");

    var chart=Afterecho.ChartData.Load(UnityEngine.Resources.Load<UnityEngine.TextAsset>("Afterecho/Charts/easy").text);
    var stage=UnityEngine.JsonUtility.FromJson<Afterecho.StageData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("Afterecho/stage").text);
    var run=new Afterecho.ChartEngine(chart,stage){PracticeEnabled=false};
    run.Seek(117.9);run.Damage=chart.rules.health-1;
    var game=fixture.AddComponent<Afterecho.AfterechoGame>();game.autoPlay=true;
    typeof(Afterecho.AfterechoGame).GetProperty("Run").SetValue(game,run);
    typeof(Afterecho.AfterechoGame).GetMethod("AutoTapUntil",flags).Invoke(game,new object[]{118.3});
    check(run.Status==Afterecho.RunStatus.Lost&&run.Inputs.Count==2,"legacy autoplay exits when an enemy deadline ends the run");
    typeof(Afterecho.AfterechoGame).GetMethod("AutoTapUntil",flags).Invoke(game,new object[]{119.0});
    check(run.Inputs.Count==2,"ended legacy autoplay does not append repeated input logs");
    return checks.ToArray();
}
finally
{
    UnityEngine.Object.DestroyImmediate(fixture);
    UnityEngine.Object.DestroyImmediate(lab);
}
