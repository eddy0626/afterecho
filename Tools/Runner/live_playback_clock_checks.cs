var g=UnityEngine.Object.FindAnyObjectByType<Afterecho.RunnerGame>();g.autoPlay=true;g.clock.UsePlaybackPosition=true;g.LabSeek(120);int step=0;double pausedAt=0;
UnityEditor.EditorApplication.CallbackFunction tick=null;tick=()=>{
 if(g==null){UnityEditor.EditorApplication.update-=tick;return;}
 if(step==0&&g.Phase==Afterecho.GamePhase.Playing&&g.LogicalTime>123){g.PauseGame();pausedAt=g.LogicalTime;g.ResumeGame();step=1;}
 if(step==1&&g.Phase==Afterecho.GamePhase.Result){UnityEditor.EditorApplication.update-=tick;System.IO.File.WriteAllText("PlaytestExports/Runner/live-playback-clock.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{pauseAt=pausedAt,result=g.Snapshot()},Newtonsoft.Json.Formatting.Indented));g.Menu();g.autoPlay=false;g.clock.UsePlaybackPosition=false;UnityEngine.Debug.Log("Playback-position clock tail and resume checked");}
};UnityEditor.EditorApplication.update+=tick;return "playback clock check started";
