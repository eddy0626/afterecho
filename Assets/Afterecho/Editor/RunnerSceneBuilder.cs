using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
namespace Afterecho.Editor
{
 public static class RunnerSceneBuilder
 {
  public const string ScenePath="Assets/Afterecho/Scenes/Stage01_RunnerCircle.unity";
  public const string RulesPath="Assets/Afterecho/Runner/RunnerRules.asset";
  static TMP_FontAsset font;static readonly Color Ink=new Color(.055f,.06f,.085f),Paper=new Color(1,.97f,.88f);
  public static string Build()
  {
   if(EditorApplication.isPlaying)throw new Exception("Stop play before building");
   if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Save active scene first");
   font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Afterecho/Generated/AfterechoKorean.asset");
   font.TryAddCharacters(string.Join("",Directory.GetFiles("Assets/Afterecho/Runtime","Runner*.cs").Select(File.ReadAllText)),out string missing,true);
   EditorUtility.SetDirty(font);
   var rules=AssetDatabase.LoadAssetAtPath<RunnerRules>(RulesPath);
   if(rules==null){rules=ScriptableObject.CreateInstance<RunnerRules>();AssetDatabase.CreateAsset(rules,RulesPath);}
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var root=new GameObject("Afterecho_RunnerCircle");var g=root.AddComponent<RunnerGame>();var v=root.AddComponent<RunnerView>();g.view=v;g.rules=rules;
   var camGo=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));camGo.tag="MainCamera";var cam=camGo.GetComponent<Camera>();cam.orthographic=true;cam.allowHDR=false;cam.transform.position=new Vector3(0,0,-10);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=rules.orange;
   var a=new GameObject("Audio_DSP");a.transform.SetParent(root.transform);g.clock=a.AddComponent<RhythmClock>();
   g.clock.music=Audio("OriginalSong",a.transform,Resources.Load<AudioClip>("Afterecho/Audio/Untitled"));g.clock.music.priority=0;
   g.clock.click=Resources.Load<AudioClip>("Afterecho/Audio/Tick");g.clock.countVoices=new AudioSource[4];for(int i=0;i<4;i++)g.clock.countVoices[i]=Audio("Count_"+i,a.transform,g.clock.click);
   v.effects=Audio("FeedbackSFX",a.transform,null);v.effects.priority=64;
   var c=new GameObject("RunnerCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));c.transform.SetParent(root.transform);v.canvas=c.GetComponent<Canvas>();v.canvas.renderMode=RenderMode.ScreenSpaceCamera;v.canvas.worldCamera=cam;v.canvas.planeDistance=1;
   var scaler=c.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,600);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
   v.background=Img("FlatColor",c.transform,Vector2.zero,Vector2.zero,rules.orange);Stretch(v.background.rectTransform,0);
   v.safeArea=Rect("SafeArea",c.transform,Vector2.zero,Vector2.zero);Stretch(v.safeArea,0);
   v.track=Rect("PerimeterTrack",v.safeArea,Vector2.zero,Vector2.zero);Stretch(v.track,58);
   Edge(v.track,"Bottom",new Vector2(0,0),new Vector2(1,0),new Vector2(0,3));Edge(v.track,"Right",new Vector2(1,0),new Vector2(1,1),new Vector2(3,0));Edge(v.track,"Top",new Vector2(0,1),new Vector2(1,1),new Vector2(0,3));Edge(v.track,"Left",new Vector2(0,0),new Vector2(0,1),new Vector2(3,0));
   // Keep the runner and corners in the same safe-area coordinate space.
   v.runnerRoot=Rect("RunnerAnchor",v.safeArea,new Vector2(1,1),Vector2.zero);
   v.runnerVisual=Rect("RunnerVisual_Feel",v.runnerRoot,new Vector2(98,98),new Vector2(0,0));v.runnerVisual.pivot=new Vector2(.5f,0);
   v.runnerImage=Img("Sprite",v.runnerVisual,new Vector2(98,98),new Vector2(0,-12),Color.white);v.runnerImage.preserveAspect=true;
   v.judge=Rect("Judge",v.safeArea,new Vector2(360,360),new Vector2(0,0));
   v.targetRing=Ring("FixedTarget",v.judge,116,3,Ink);v.notes=new RunnerRing[12];for(int i=0;i<v.notes.Length;i++)v.notes[i]=Ring("Approach_"+i,v.judge,116,3,Paper);
   v.hitRing=Ring("HitResponse",v.judge,116,3,Paper);v.hitRing.gameObject.SetActive(false);
   Text("CenterDot",v.judge,new Vector2(80,50),Vector2.zero,"•",30,Ink);
   v.combo=Text("Combo",v.safeArea,new Vector2(480,64),new Vector2(0,224),"0  COMBO",46,Ink);v.combo.fontStyle=FontStyles.Bold;
   v.boostText=Text("BoostLabel",v.safeArea,new Vector2(440,28),new Vector2(0,182),"15 COMBO → OVERDRIVE",14,Ink);
   v.healthRoot=Rect("Health",v.safeArea,new Vector2(288,48),new Vector2(0,-204));
   var hpbg=Img("HpBackground",v.healthRoot,new Vector2(288,13),Vector2.zero,new Color(0,0,0,.14f));v.hpFill=Filled("HpFill",hpbg.transform,new Vector2(288,13),Ink);
   v.hpText=Text("HP",v.healthRoot,new Vector2(288,25),new Vector2(0,-23),"HP 100 / 100",20,Ink);
   v.status=Text("Judgement",v.safeArea,new Vector2(470,34),new Vector2(0,-119),"",23,Ink);v.status.fontStyle=FontStyles.Bold;
   v.countText=Text("CountIn",v.judge,new Vector2(140,90),Vector2.zero,"",64,Ink);
   v.score=Text("Score",v.safeArea,new Vector2(270,33),new Vector2(185,-22),"000000",20,Ink,TextAlignmentOptions.Left,new Vector2(0,1));
   v.modeLabel=Text("Mode",v.safeArea,new Vector2(290,28),new Vector2(190,22),"01 / 입문",13,Ink,TextAlignmentOptions.Left,new Vector2(0,0));
   v.progress=Text("SongProgress",v.safeArea,new Vector2(310,25),new Vector2(-213,22),"",13,Ink,TextAlignmentOptions.Right,new Vector2(1,0));
   var prog=Img("ProgressTrack",v.safeArea,new Vector2(250,3),new Vector2(-183,-22),new Color(0,0,0,.15f),new Vector2(1,1));v.progressFill=Filled("Progress",prog.transform,new Vector2(250,3),Ink);
   v.pauseButton=Button("Pause",v.safeArea,new Vector2(42,36),new Vector2(-26,-24),"Ⅱ",out var pauseText);v.pauseButton.GetComponent<RectTransform>().anchorMin=v.pauseButton.GetComponent<RectTransform>().anchorMax=new Vector2(1,1);
   v.boostStripe=Img("BoostStripe",v.safeArea,new Vector2(100,4),new Vector2(0,-254),new Color(0,0,0,.08f));
   var panel=Img("MenuOverlay",v.safeArea,new Vector2(620,560),Vector2.zero,new Color(.055f,.06f,.085f,1f));panel.raycastTarget=true;v.overlay=panel.gameObject.AddComponent<CanvasGroup>();
   v.overlayTitle=Text("Title",panel.transform,new Vector2(560,72),new Vector2(0,205),"잔향 AFTERECHO",40,Paper);v.overlayTitle.fontStyle=FontStyles.Bold;
   v.overlayBody=Text("Description",panel.transform,new Vector2(550,82),new Vector2(0,124),"",20,Paper);
   v.primary=Button("Play",panel.transform,new Vector2(330,52),new Vector2(0,37),"4박 후 시작",out v.primaryText);
   v.secondary=Button("Practice",panel.transform,new Vector2(330,44),new Vector2(0,-22),"안전한 연습",out v.secondaryText);
   v.menuOptions=new GameObject("Options",typeof(RectTransform));v.menuOptions.transform.SetParent(panel.transform,false);var op=v.menuOptions.transform;
   v.difficultyButtons=new UnityEngine.UI.Button[3];for(int i=0;i<3;i++)v.difficultyButtons[i]=Button("Difficulty_"+i,op,new Vector2(105,36),new Vector2((i-1)*114,-83),new[]{"입문","보통","도전"}[i],out var tx);
   Text("VolumeLabel",op,new Vector2(125,30),new Vector2(-158,-137),"음량",16,Paper,TextAlignmentOptions.Left);v.volumeSlider=Slider("Volume",op,new Vector2(73,-137),0,1,.65f);
   v.syncLabel=Text("SyncLabel",op,new Vector2(150,30),new Vector2(-146,-181),"싱크 0 ms",16,Paper,TextAlignmentOptions.Left);v.syncSlider=Slider("Sync",op,new Vector2(73,-181),-250,250,0);
   var tog=Rect("ReducedMotion",op,new Vector2(310,32),new Vector2(0,-227));v.motionToggle=tog.gameObject.AddComponent<UnityEngine.UI.Toggle>();var box=Img("Box",tog,new Vector2(24,24),new Vector2(-128,0),new Color(1,1,1,.2f));box.raycastTarget=true;var check=Img("Check",box.transform,new Vector2(14,14),Vector2.zero,Paper);v.motionToggle.targetGraphic=box;v.motionToggle.graphic=check;Text("Label",tog,new Vector2(240,28),new Vector2(23,0),"움직임 줄이기",16,Paper,TextAlignmentOptions.Left);var toggleHit=Img("TouchArea",tog,new Vector2(310,40),Vector2.zero,new Color(1,1,1,0));toggleHit.raycastTarget=true;toggleHit.transform.SetAsFirstSibling();v.motionToggle.navigation=new Navigation{mode=Navigation.Mode.None};
   var portrait=Img("LandscapePlease",c.transform,Vector2.zero,Vector2.zero,Ink);Stretch(portrait.rectTransform,0);portrait.raycastTarget=true;Text("Message",portrait.transform,new Vector2(570,170),Vector2.zero,"가로로 돌려주세요\n<size=65%>음악과 게임은 잠시 멈췄어요</size>",35,Paper);v.portrait=portrait.gameObject;v.portrait.SetActive(false);
   var es=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
   BindArt(v);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();if(AssetDatabase.LoadAssetAtPath<Texture2D>(RunnerUiStyling.AtlasPath)!=null)RunnerUiStyling.Apply();Selection.activeGameObject=root;return ScenePath;
  }
  public static string RefreshArt(){var v=UnityEngine.Object.FindFirstObjectByType<RunnerView>();BindArt(v);EditorUtility.SetDirty(v);EditorSceneManager.MarkSceneDirty(v.gameObject.scene);EditorSceneManager.SaveScene(v.gameObject.scene);return "runner art rebound";}
  static void BindArt(RunnerView v)
  {
   string dir="Assets/Afterecho/Runner/Art/Frames";
   if(Directory.Exists(dir))foreach(string path in Directory.GetFiles(dir,"*.png",SearchOption.AllDirectories))
   {var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.textureType=TextureImporterType.Sprite;ti.spriteImportMode=SpriteImportMode.Single;ti.alphaIsTransparency=true;ti.mipmapEnabled=false;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=512;ti.SaveAndReimport();}
   Sprite[] Frames(string name)=>Directory.Exists(dir+"/"+name)?Directory.GetFiles(dir+"/"+name,"*.png").OrderBy(p=>p).Select(AssetDatabase.LoadAssetAtPath<Sprite>).ToArray():Array.Empty<Sprite>();
   v.runFrames=Frames("Run");v.sprintFrames=Frames("Sprint");v.fallFrames=Frames("Fall");v.recoverFrames=Frames("Recover");
   v.runFeet=Feet(v.runFrames);v.sprintFeet=Feet(v.sprintFrames);v.fallFeet=Feet(v.fallFrames);v.recoverFeet=Feet(v.recoverFrames);
   if(v.runFrames.Length>0)v.runnerImage.sprite=v.runFrames[0];
   else {string p="Assets/Afterecho/Runner/Art/RunnerReference_GPT.png";var ti=(TextureImporter)AssetImporter.GetAtPath(p);ti.textureType=TextureImporterType.Sprite;ti.mipmapEnabled=false;ti.SaveAndReimport();v.runnerImage.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(p);}
   AudioClip S(string n)=>AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Afterecho/Runner/Audio/"+n+".wav");
   v.hitSound=S("Good");v.perfectSound=S("Perfect");v.fallSound=S("Fall");v.boostSound=S("Boost");v.clearSound=S("Clear");
  }
  static float[] Feet(Sprite[] frames)
  {
   return frames.Select(s=>{
    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);ImageConversion.LoadImage(texture,File.ReadAllBytes(AssetDatabase.GetAssetPath(s)));
    var pixels=texture.GetPixels32();int bottom=0;bool found=false;
    for(int y=0;y<texture.height&&!found;y++)for(int x=0;x<texture.width;x++)if(pixels[y*texture.width+x].a>128){bottom=y;found=true;break;}
    float value=bottom/(float)texture.height;UnityEngine.Object.DestroyImmediate(texture);return value;
   }).ToArray();
  }
  static AudioSource Audio(string n,Transform p,AudioClip clip){var g=new GameObject(n);g.transform.SetParent(p);var a=g.AddComponent<AudioSource>();a.playOnAwake=false;a.spatialBlend=0;a.clip=clip;return a;}
  static RectTransform Rect(string n,Transform p,Vector2 size,Vector2 pos,Vector2? anchor=null){var go=new GameObject(n,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(p,false);r.anchorMin=r.anchorMax=anchor??new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=pos;return r;}
  static void Stretch(RectTransform r,float inset){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*inset;r.offsetMax=-Vector2.one*inset;}
  static UnityEngine.UI.Image Img(string n,Transform p,Vector2 size,Vector2 pos,Color c,Vector2? anchor=null){var r=Rect(n,p,size,pos,anchor);var i=r.gameObject.AddComponent<UnityEngine.UI.Image>();i.color=c;i.raycastTarget=false;return i;}
  static UnityEngine.UI.Image Filled(string n,Transform p,Vector2 size,Color c){var i=Img(n,p,size,Vector2.zero,c);i.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Afterecho/Generated/Square.asset");i.type=UnityEngine.UI.Image.Type.Filled;i.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;return i;}
  static void Edge(RectTransform p,string n,Vector2 amin,Vector2 amax,Vector2 size){var i=Img(n,p,size,Vector2.zero,Ink);i.rectTransform.anchorMin=amin;i.rectTransform.anchorMax=amax;}
  static TMP_Text Text(string n,Transform p,Vector2 size,Vector2 pos,string value,float fs,Color c,TextAlignmentOptions align=TextAlignmentOptions.Center,Vector2? anchor=null){var r=Rect(n,p,size,pos,anchor);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=fs;t.color=c;t.alignment=align;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;}
  static RunnerRing Ring(string n,Transform p,float d,float t,Color c){var r=Rect(n,p,new Vector2(d,d),Vector2.zero);var g=r.gameObject.AddComponent<RunnerRing>();g.raycastTarget=false;g.SetDiameter(d,t,c);return g;}
  static UnityEngine.UI.Button Button(string n,Transform p,Vector2 size,Vector2 pos,string label,out TMP_Text text){var i=Img(n,p,size,pos,Paper);i.raycastTarget=true;var b=i.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=i;b.navigation=new Navigation{mode=Navigation.Mode.None};text=Text("Label",i.transform,size,Vector2.zero,label,18,Ink);return b;}
  static UnityEngine.UI.Slider Slider(string n,Transform p,Vector2 pos,float min,float max,float value){var r=Rect(n,p,new Vector2(230,30),pos);var s=r.gameObject.AddComponent<UnityEngine.UI.Slider>();var hit=Img("Touch",r,new Vector2(240,40),Vector2.zero,new Color(1,1,1,0));hit.raycastTarget=true;Img("Track",r,new Vector2(230,3),Vector2.zero,new Color(1,1,1,.25f));var area=Rect("HandleArea",r,new Vector2(214,30),Vector2.zero);var handle=Img("Handle",area,new Vector2(16,24),Vector2.zero,Paper);handle.raycastTarget=true;s.targetGraphic=handle;s.handleRect=handle.rectTransform;handle.rectTransform.sizeDelta=new Vector2(16,-6);s.minValue=min;s.maxValue=max;s.value=value;s.navigation=new Navigation{mode=Navigation.Mode.None};return s;}
 }
}
