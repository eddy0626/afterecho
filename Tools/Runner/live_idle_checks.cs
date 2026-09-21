var g=UnityEngine.Object.FindAnyObjectByType<Afterecho.RunnerGame>();var input=(UnityEngine.InputSystem.InputAction)typeof(Afterecho.RunnerGame).GetField("input",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(g);input.Disable();g.autoPlay=false;g.invincible=false;g.difficulty="easy";g.StartRun();int index=0;string[] difficulties={"easy","normal","hard"};var results=new System.Collections.Generic.List<object>();
UnityEditor.EditorApplication.CallbackFunction tick=null;tick=()=>{
 if(g==null){UnityEditor.EditorApplication.update-=tick;return;}
 if(g.Phase!=Afterecho.GamePhase.Result)return;
 results.Add(g.Snapshot());index++;
 if(index<3){g.difficulty=difficulties[index];g.StartRun();}
 else{UnityEditor.EditorApplication.update-=tick;System.IO.File.WriteAllText("PlaytestExports/Runner/live-no-input.json",Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));g.Menu();input.Enable();UnityEngine.Debug.Log("Runner no-input QA complete");}
};UnityEditor.EditorApplication.update+=tick;return "3 no-input runs started";
