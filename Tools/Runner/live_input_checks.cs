var g=UnityEngine.Object.FindAnyObjectByType<Afterecho.RunnerGame>();
g.autoPlay=false;g.invincible=true;g.Menu();g.invincible=true;g.LabSeek(20);
var lines=new System.Collections.Generic.List<string>();
int step=0;double wall=UnityEditor.EditorApplication.timeSinceStartup;double paused=0,lap=0,active=0;int accepted=0,hits=0;double next=0;
UnityEditor.EditorApplication.CallbackFunction tick=null;
tick=()=>{
 try{
  if(g==null){UnityEditor.EditorApplication.update-=tick;return;}
  if(UnityEditor.EditorApplication.timeSinceStartup-wall>50)throw new System.Exception("QA timeout step "+step);
  if(step==0&&g.Phase==Afterecho.GamePhase.Playing&&g.LogicalTime>20.05){g.PauseGame();paused=g.LogicalTime;lap=g.view.LapPosition;active=g.ActiveSeconds;wall=UnityEditor.EditorApplication.timeSinceStartup;step++;}
  else if(step==1&&UnityEditor.EditorApplication.timeSinceStartup-wall>.6){if(g.LogicalTime!=paused||g.view.LapPosition!=lap||g.ActiveSeconds!=active)throw new System.Exception("pause drift");lines.Add("PASS music, notes, runner freeze while paused");g.ResumeGame();wall=UnityEditor.EditorApplication.timeSinceStartup;step++;}
  else if(step==2&&g.Phase==Afterecho.GamePhase.Playing){lines.Add("PASS four beat resume");accepted=g.AcceptedInputs;UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.A));step++;}
  else if(step==3&&g.AcceptedInputs>accepted){accepted=g.AcceptedInputs;UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.A));wall=UnityEditor.EditorApplication.timeSinceStartup;step++;}
  else if(step==4&&UnityEditor.EditorApplication.timeSinceStartup-wall>.12){if(g.AcceptedInputs!=accepted)throw new System.Exception("held key repeats");lines.Add("PASS held/repeated keyboard state accepts one edge");UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());next=g.Run.Chart.notes[g.Run.NextPending()+2].time;step++;}
  else if(step==5&&g.LogicalTime>next-.008&&g.LogicalTime<next+.04){hits=g.Run.Hits;UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.J));step++;}
  else if(step==6&&g.Run.Hits>hits){lines.Add("PASS real keyboard event hits DSP note");UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());g.clock.music.Stop();step++;}
  else if(step==7&&g.Phase==Afterecho.GamePhase.Paused){lines.Add("PASS interrupted audio pauses game");g.StartRun(false);step++;}
  else if(step==8&&g.Phase==Afterecho.GamePhase.Preparing){if(g.LogicalTime!=0||g.Run.Hits!=0||g.Run.Misses!=0)throw new System.Exception("restart not zero");lines.Add("PASS restart starts fresh at zero");g.Menu();g.invincible=false;UnityEditor.EditorApplication.update-=tick;System.IO.File.WriteAllLines("PlaytestExports/Runner/live-input-checks.txt",lines);UnityEngine.Debug.Log("Runner live input QA passed");}
 }catch(System.Exception ex){UnityEditor.EditorApplication.update-=tick;lines.Add("FAIL "+ex.Message);System.IO.File.WriteAllLines("PlaytestExports/Runner/live-input-checks.txt",lines);UnityEngine.Debug.LogError(ex);}
};
UnityEditor.EditorApplication.update+=tick;return "live checks started";
