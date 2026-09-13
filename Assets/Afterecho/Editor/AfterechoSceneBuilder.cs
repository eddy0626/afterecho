using System;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using MoreMountains.Feedbacks;

namespace Afterecho.Editor
{
    public static class AfterechoSceneBuilder
    {
        const string Root = "Assets/Afterecho";
        static TMP_FontAsset font;
        static Material spriteMaterial;
        static Sprite square, halo;
        static readonly Color Bone = new Color(.87f,.91f,.87f), Muted = new Color(.47f,.61f,.59f), Teal = new Color(.48f,.8f,.73f), Panel = new Color(.025f,.042f,.046f,.98f);
        public static string Build()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode before scene creation");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new Exception("Active scene has unsaved changes; save it first");
            Directory.CreateDirectory(Root + "/Scenes"); Directory.CreateDirectory(Root + "/Generated");
            PrepareAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root = new GameObject("AfterechoStage01");
            var game = root.AddComponent<AfterechoGame>();
            var view = root.AddComponent<AfterechoView>(); game.view = view;
            var impact = root.AddComponent<AfterechoImpactFeedback>(); view.impactFeedback = impact;
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 5.6f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.014f,.023f,.027f);
            cam.transform.position = new Vector3(0,2,-10); cam.nearClipPlane = .1f; cam.farClipPlane = 100;
            camGo.AddComponent<AudioListener>(); view.gameCamera = cam;
            var world = Child("World",root.transform);
            view.playerRoot = Child("PlayerRoot_DOTweenPosition",world);
            view.playerVisual = Child("PlayerVisual_FeelScale",view.playerRoot);
            view.playerSprite = SpriteObject("Traveler",view.playerVisual,Art("Traveler"),Color.white,20);
            view.playerSprite.transform.localScale = Vector3.one * .9f;
            view.enemyRoot = Child("EnemyRoot",world); view.enemyRoot.position = new Vector3(0,3,0);
            view.enemyVisual = Child("EnemyVisual_FeelScale",view.enemyRoot);
            view.enemySprite = SpriteObject("Sentinel",view.enemyVisual,Art("Sentinel"),Color.white,19);
            view.enemySprite.transform.localScale = Vector3.one * .92f;
            view.footHalo = SpriteObject("FootLight",world,halo,new Color(.3f,.75f,.66f,.2f),2); view.footHalo.transform.localScale = new Vector3(2.1f,1.1f,1);
            view.enemyHalo = SpriteObject("ThreatLight",world,halo,new Color(.8f,.12f,.08f,.25f),3); view.enemyHalo.transform.localScale = new Vector3(3,1.6f,1);
            view.doorRoot = Child("IronDoor",world); view.doorRoot.position = new Vector3(0,4,0);
            view.doorLeft = SpriteObject("LeftLeaf",view.doorRoot,Art("DoorLeft"),Color.white,18).transform; view.doorLeft.localPosition = new Vector3(-.5f,0,0);
            view.doorRight = SpriteObject("RightLeaf",view.doorRoot,Art("DoorRight"),Color.white,18).transform; view.doorRight.localPosition = new Vector3(.5f,0,0);
            var floors = Child("FloorPool_285",world); var edges = Child("WallPool_285",world);
            view.floorPool = new SpriteRenderer[285]; view.edgePool = new SpriteRenderer[285];
            for (int i = 0; i < 285; i++)
            {
                var sr = SpriteObject("Floor_"+i,floors,Art("Floor"),new Color(.38f,.48f,.5f),0);
                sr.transform.localScale = new Vector3(.499f,.491f,1); view.floorPool[i] = sr;
                sr.transform.position = new Vector3(i%19-9,i/19-7,0);
                var wall = SpriteObject("Wall_"+i,edges,square,new Color(.037f,.068f,.071f),1);
                wall.transform.localScale = new Vector3(.96f,.96f,1); view.edgePool[i] = wall; wall.gameObject.SetActive(false);
            }
            var audio = Child("Audio",root.transform);
            game.clock = audio.gameObject.AddComponent<RhythmClock>();
            game.clock.music = Audio("OriginalSong_DSP",audio,Resources.Load<AudioClip>("Afterecho/Audio/Untitled"));
            game.clock.music.priority = 0;
            game.clock.click = Resources.Load<AudioClip>("Afterecho/Audio/Tick");
            game.clock.countVoices = new AudioSource[4]; for (int i=0;i<4;i++) game.clock.countVoices[i]=Audio("Count_"+i,audio,game.clock.click);
            view.sfx = Audio("SFX",audio,null); view.sfx.priority = 80;
            view.stepSound = Clip("Step"); view.hitSound = Clip("Hit"); view.warningSound = Clip("Warning"); view.damageSound = Clip("Damage"); view.doorSound = Clip("Door");
            return CompleteExisting();
        }
        public static string CompleteExisting()
        {
            PrepareAssets();
            var view=UnityEngine.Object.FindFirstObjectByType<AfterechoView>();
            if(view==null)throw new Exception("No partial stage exists");
            var root=view.gameObject;var game=root.GetComponent<AfterechoGame>();
            var scene=root.scene;
            if(view.stepFeedback==null)view.stepFeedback=Feel("StepFeedback_90ms",root.transform,view.playerVisual,.09f,1.13f);
            if(view.attackFeedback==null)view.attackFeedback=Feel("AttackFeedback_100ms",root.transform,view.enemyVisual,.1f,.81f);
            if(view.damageFeedback==null)view.damageFeedback=Feel("DamageFeedback_180ms",root.transform,view.playerVisual,.18f,.73f);
            if(view.impactFeedback==null)
            {
                var impact = root.GetComponent<AfterechoImpactFeedback>() ?? root.AddComponent<AfterechoImpactFeedback>();
                view.impactFeedback = impact;
            }
            if(view.safeArea==null)BuildUI(view,root.transform);
            if(UnityEngine.Object.FindFirstObjectByType<EventSystem>()==null)
            {
                var es=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            view.enemyRoot.gameObject.SetActive(false);view.enemyHalo.gameObject.SetActive(false);view.doorRoot.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/Stage01_BlackCorridor.unity");
            var current=EditorBuildSettings.scenes.ToList();
            if(!current.Any(x=>x.path==scene.path))current.Insert(0,new EditorBuildSettingsScene(scene.path,true));
            EditorBuildSettings.scenes=current.ToArray();
            EditorUtility.SetDirty(game);AssetDatabase.SaveAssets();Selection.activeGameObject=root;return scene.path;
        }
        static void PrepareAssets()
        {
            // Canonical noninteractive TMP resources import; never open the blocking importer window.
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") == null)
                TMP_PackageResourceImporter.ImportResources(true,false,false);
            string fontPath = Root+"/Generated/AfterechoKorean.asset";
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (font == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(Root+"/Fonts/NotoSansCJKkr-Regular.otf");
                font = TMP_FontAsset.CreateFontAsset(source,64,7,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
                font.name = "AfterechoKorean"; AssetDatabase.CreateAsset(font,fontPath);
                AssetDatabase.AddObjectToAsset(font.material,font);
                foreach (var t in font.atlasTextures) AssetDatabase.AddObjectToAsset(t,font);
                string text = string.Join("",Directory.GetFiles(Root+"/Runtime","*.cs").Select(File.ReadAllText));
                text += string.Join("",Directory.GetFiles(Root+"/Resources/Afterecho","*.json").Select(File.ReadAllText));
                font.TryAddCharacters(text,out string missing,true);
                EditorUtility.SetDirty(font);
            }
            spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root+"/Generated/SpriteUnlit.mat");
            if (spriteMaterial == null)
            { spriteMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")); AssetDatabase.CreateAsset(spriteMaterial,Root+"/Generated/SpriteUnlit.mat"); }
            square = Shape("Square",false); halo = Shape("Halo",true);
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip",new[]{Root+"/Resources/Afterecho/Audio"}))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true; importer.defaultSampleSettings = settings; importer.SaveAndReimport();
            }
        }
        static Sprite Shape(string name,bool radial)
        {
            string path=Root+"/Generated/"+name+".asset";
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(path); if(old!=null)return old;
            int n=radial?64:2; var t=new Texture2D(n,n,TextureFormat.RGBA32,false); t.name=name+"Texture";
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            { float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(n/2f,n/2f))/(n/2f); t.SetPixel(x,y,new Color(1,1,1,radial?Mathf.Pow(Mathf.Clamp01(1-d),2):1)); }
            t.Apply(); var s=Sprite.Create(t,new Rect(0,0,n,n),new Vector2(.5f,.5f),n);s.name=name;
            AssetDatabase.CreateAsset(s,path);AssetDatabase.AddObjectToAsset(t,s);return s;
        }
        static Sprite Art(string name)=>AssetDatabase.LoadAllAssetsAtPath(Root+"/Art/Afterecho_Gameaify.png").OfType<Sprite>().First(s=>s.name==name);
        static AudioClip Clip(string name)=>Resources.Load<AudioClip>("Afterecho/Audio/"+name);
        static Transform Child(string name,Transform parent)
        {var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
        static SpriteRenderer SpriteObject(string name,Transform parent,Sprite s,Color color,int order)
        {var g=Child(name,parent);var r=g.gameObject.AddComponent<SpriteRenderer>();r.sprite=s;r.color=color;r.sortingOrder=order;r.sharedMaterial=spriteMaterial;return r;}
        static AudioSource Audio(string name,Transform parent,AudioClip clip)
        {var a=Child(name,parent).gameObject.AddComponent<AudioSource>();a.playOnAwake=false;a.clip=clip;a.spatialBlend=0;return a;}
        static MMF_Player Feel(string name,Transform parent,Transform target,float duration,float peak)
        {
            var existing=parent.Find(name); var p=existing!=null?existing.GetComponent<MMF_Player>():Child(name,parent).gameObject.AddComponent<MMF_Player>();
            p.InitializationMode=MMFeedbacks.InitializationModes.Script;p.AutoPlayOnStart=false;p.AutoPlayOnEnable=false;p.CooldownDuration=0;p.CanPlayWhileAlreadyPlaying=true;p.StopFeedbacksOnDisable=true;p.RestoreInitialValuesOnDisable=true;
            var f=new MMF_SquashAndStretch {Label=name,SquashAndStretchTarget=target,AnimateScaleDuration=duration,RemapCurveZero=1,RemapCurveOne=peak,Axis=MMF_SquashAndStretch.PossibleAxis.YtoX,
                AnimateCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.25f,1),new Keyframe(1,0)),AllowAdditivePlays=false};
            p.FeedbacksList?.Clear(); f.Timing=new MMFeedbackTiming { TimescaleMode=TimescaleModes.Unscaled, CooldownDuration=0 }; p.AddFeedback(f);
            return p;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position,Vector2? anchor=null)
        {
            var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(parent,false);
            r.anchorMin=r.anchorMax=anchor??new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=position;return r;
        }
        static UnityEngine.UI.Image Image(string name,Transform parent,Vector2 size,Vector2 pos,Color color,Vector2? anchor=null)
        {var r=Rect(name,parent,size,pos,anchor);var i=r.gameObject.AddComponent<UnityEngine.UI.Image>();i.color=color;i.raycastTarget=false;return i;}
        static TMP_Text Text(string name,Transform parent,Vector2 size,Vector2 pos,string value,float fontSize,Color color,TextAlignmentOptions align=TextAlignmentOptions.Center,Vector2? anchor=null)
        {
            var r=Rect(name,parent,size,pos,anchor);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=fontSize;t.color=color;t.alignment=align;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.Normal;return t;
        }
        static UnityEngine.UI.Button Button(string name,Transform parent,Vector2 size,Vector2 pos,string label,out TMP_Text text)
        {
            var i=Image(name,parent,size,pos,new Color(.11f,.22f,.22f));i.raycastTarget=true;
            var b=i.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=i;
            var colors=b.colors;colors.highlightedColor=new Color(1.25f,1.25f,1.25f);colors.pressedColor=new Color(.7f,.9f,.9f);b.colors=colors;
            b.navigation=new UnityEngine.UI.Navigation {mode=UnityEngine.UI.Navigation.Mode.None};
            text=Text("Label",i.transform,size,Vector2.zero,label,19,Bone);return b;
        }
        static UnityEngine.UI.Slider Slider(string name,Transform parent,Vector2 pos,float min,float max,float value)
        {
            var r=Rect(name,parent,new Vector2(210,28),pos);var s=r.gameObject.AddComponent<UnityEngine.UI.Slider>();
            var bg=Image("Track",r,new Vector2(210,3),Vector2.zero,new Color(.15f,.24f,.24f));
            var area=Rect("HandleArea",r,new Vector2(196,28),Vector2.zero);
            var handle=Image("Handle",area,new Vector2(18,22),Vector2.zero,Teal);handle.raycastTarget=true;
            var hit=Image("TouchArea",r,new Vector2(230,44),Vector2.zero,new Color(0,0,0,0));hit.raycastTarget=true;hit.transform.SetAsFirstSibling();
            s.targetGraphic=handle;s.handleRect=handle.rectTransform;s.direction=UnityEngine.UI.Slider.Direction.LeftToRight;s.minValue=min;s.maxValue=max;s.value=value;
            s.navigation=new UnityEngine.UI.Navigation {mode=UnityEngine.UI.Navigation.Mode.None};return s;
        }
        static void BuildUI(AfterechoView v,Transform root)
        {
            var c=new GameObject("AfterechoUI",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster));c.transform.SetParent(root,false);
            c.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=c.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=1f;
            v.safeArea=Rect("SafeArea",c.transform,Vector2.zero,Vector2.zero);v.safeArea.anchorMin=Vector2.zero;v.safeArea.anchorMax=Vector2.one;
            var top=Image("TopShade",v.safeArea,new Vector2(0,90),new Vector2(0,-45),new Color(.01f,.022f,.026f,.88f),new Vector2(.5f,1));top.rectTransform.anchorMin=new Vector2(0,1);top.rectTransform.anchorMax=Vector2.one;
            v.sectionText=Text("Stage",v.safeArea,new Vector2(700,32),new Vector2(382,-28),"01 / 검은 복도",18,Bone,TextAlignmentOptions.Left,new Vector2(0,1));
            v.healthText=Text("Health",v.safeArea,new Vector2(270,36),new Vector2(167,-62),"●●●●●",21,Teal,TextAlignmentOptions.Left,new Vector2(0,1));
            v.difficultyText=Text("Difficulty",v.safeArea,new Vector2(210,26),new Vector2(338,-62),"입문",13,Muted,TextAlignmentOptions.Left,new Vector2(0,1));
            v.comboText=Text("Combo",v.safeArea,new Vector2(240,44),new Vector2(-310,-34),"00 COMBO",15,Bone,TextAlignmentOptions.Right,new Vector2(1,1));
            v.progressText=Text("Progress",v.safeArea,new Vector2(80,30),new Vector2(-142,-37),"00%",20,Bone,TextAlignmentOptions.Right,new Vector2(1,1));
            var bar=Image("ProgressTrack",v.safeArea,new Vector2(296,3),new Vector2(-250,-70),new Color(.12f,.2f,.2f),new Vector2(1,1));
            v.progressFill=Image("Fill",bar.transform,new Vector2(296,3),Vector2.zero,Teal);v.progressFill.sprite=square;v.progressFill.type=UnityEngine.UI.Image.Type.Filled;v.progressFill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
            v.pauseButton=Button("Pause",v.safeArea,new Vector2(84,84),Vector2.zero,"Ⅱ",out _);var pr=(RectTransform)v.pauseButton.transform;pr.anchorMin=pr.anchorMax=new Vector2(1,1);pr.anchoredPosition=new Vector2(-50,-45);
            var bottom=Image("RhythmShade",v.safeArea,new Vector2(0,190),new Vector2(0,95),new Color(.01f,.022f,.026f,.9f),new Vector2(.5f,0));bottom.rectTransform.anchorMin=Vector2.zero;bottom.rectTransform.anchorMax=new Vector2(1,0);
            v.rail=Rect("NoteRail_DSP",v.safeArea,new Vector2(920,4),new Vector2(0,115),new Vector2(.5f,0));
            Image("Guide",v.rail,new Vector2(920,2),Vector2.zero,new Color(.15f,.27f,.27f));
            for(int i=-4;i<=4;i++)if(i!=0)Image("RailTick"+i,v.rail,new Vector2(1,10),new Vector2(i*110,0),new Color(.18f,.31f,.3f));
            v.judgment=Rect("JudgmentPoint",v.rail,new Vector2(44,44),Vector2.zero);
            var outer=Image("Outer",v.judgment,new Vector2(35,35),Vector2.zero,new Color(.21f,.4f,.37f));outer.rectTransform.localRotation=Quaternion.Euler(0,0,45);
            var inner=Image("Inner",v.judgment,new Vector2(29,29),Vector2.zero,new Color(.025f,.06f,.06f));inner.rectTransform.localRotation=Quaternion.Euler(0,0,45);
            v.hitMark=Image("Hit",v.judgment,new Vector2(5,18),Vector2.zero,Teal);
            v.noteLeft=new RectTransform[24];v.noteRight=new RectTransform[24];v.noteLeftImages=new UnityEngine.UI.Image[24];v.noteRightImages=new UnityEngine.UI.Image[24];
            for(int i=0;i<24;i++)
            {
                var l=Image("Note_"+i+"_L",v.rail,new Vector2(7,28),new Vector2(-300,0),Bone);
                var r=Image("Note_"+i+"_R",v.rail,new Vector2(7,28),new Vector2(300,0),Bone);
                v.noteLeft[i]=l.rectTransform;v.noteRight[i]=r.rectTransform;v.noteLeftImages[i]=l;v.noteRightImages[i]=r;l.gameObject.SetActive(false);r.gameObject.SetActive(false);
            }
            v.statusText=Text("JudgmentStatus",v.safeArea,new Vector2(720,30),new Vector2(0,163),"ONE TAP · ONE ACTION",17,Teal,anchor:new Vector2(.5f,0));
            v.scoreText=Text("Score",v.safeArea,new Vector2(430,32),new Vector2(0,55),"000000   ×1",20,Bone,anchor:new Vector2(.5f,0));
            Text("ControlLegend",v.safeArea,new Vector2(900,22),new Vector2(0,24),"TAP ANYWHERE     /     SPACE · F · J",11,Muted,anchor:new Vector2(.5f,0));
            v.enemyText=Text("EnemyTelegraph",v.safeArea,new Vector2(500,28),new Vector2(0,405),"",17,new Color(.89f,.48f,.36f),anchor:new Vector2(.5f,0));
            v.enemyFill=Image("EnemyHP",v.safeArea,new Vector2(150,3),new Vector2(0,385),new Color(.9f,.35f,.25f),new Vector2(.5f,0));v.enemyFill.sprite=square;v.enemyFill.type=UnityEngine.UI.Image.Type.Filled;v.enemyFill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
            v.countText=Text("CountIn",v.safeArea,new Vector2(240,130),new Vector2(0,30),"4",88,Bone);v.countText.gameObject.SetActive(false);
            var scrim=Image("Overlay",v.safeArea,Vector2.zero,Vector2.zero,new Color(.005f,.013f,.017f,.64f));scrim.rectTransform.anchorMin=Vector2.zero;scrim.rectTransform.anchorMax=Vector2.one;scrim.raycastTarget=true;
            v.overlay=scrim.gameObject.AddComponent<CanvasGroup>();
            v.overlayPanel=Image("Panel",scrim.transform,new Vector2(710,630),Vector2.zero,Panel).rectTransform;
            Image("Accent",v.overlayPanel,new Vector2(46,3),new Vector2(0,264),Teal);
            v.title=Text("Title",v.overlayPanel,new Vector2(660,90),new Vector2(0,194),"잔향",52,Bone);
            v.subtitle=Text("Subtitle",v.overlayPanel,new Vector2(660,32),new Vector2(0,131),"AFTERECHO / THE BLACK CORRIDOR",13,Teal);
            v.body=Text("Description",v.overlayPanel,new Vector2(610,154),new Vector2(0,36),"",18,Bone);
            v.menuExtras=Rect("Difficulties",v.overlayPanel,new Vector2(470,46),new Vector2(0,-79)).gameObject;
            v.difficulties=new UnityEngine.UI.Button[3];
            string[] labels={"입문","보통","도전"};for(int i=0;i<3;i++)v.difficulties[i]=Button(labels[i],v.menuExtras.transform,new Vector2(132,42),new Vector2((i-1)*148,0),labels[i],out _);
            v.primary=Button("Primary",v.overlayPanel,new Vector2(430,82),new Vector2(0,-145),"시작하기",out v.primaryText);v.primaryButton=v.primary.gameObject;
            v.secondary=Button("Secondary",v.overlayPanel,new Vector2(170,40),new Vector2(0,-215),"처음으로",out v.secondaryText);v.secondaryButton=v.secondary.gameObject;
            Text("VolumeLabel",v.overlayPanel,new Vector2(210,25),new Vector2(-147,-248),"음량",13,Muted);
            v.syncText=Text("SyncLabel",v.overlayPanel,new Vector2(210,25),new Vector2(147,-248),"싱크 0 ms",13,Muted);
            v.volumeSlider=Slider("Volume",v.overlayPanel,new Vector2(-147,-279),0,1,.65f);
            v.syncSlider=Slider("Sync",v.overlayPanel,new Vector2(147,-279),-250,250,0);v.syncSlider.wholeNumbers=true;
            var toggle=Image("ReducedMotion",v.safeArea,new Vector2(20,20),new Vector2(34,29),new Color(.12f,.22f,.22f),new Vector2(0,0));toggle.raycastTarget=true;
            v.motionToggle=toggle.gameObject.AddComponent<UnityEngine.UI.Toggle>();v.motionToggle.targetGraphic=toggle;
            var mark=Image("Check",toggle.transform,new Vector2(12,12),Vector2.zero,Teal);v.motionToggle.graphic=mark;
            Text("MotionLabel",v.safeArea,new Vector2(140,22),new Vector2(120,29),"연출 줄이기",12,Muted,TextAlignmentOptions.Left,new Vector2(0,0));
            var rotation=Image("PortraitNotice",c.transform,Vector2.zero,Vector2.zero,new Color(.01f,.025f,.03f));rotation.rectTransform.anchorMin=Vector2.zero;rotation.rectTransform.anchorMax=Vector2.one;rotation.raycastTarget=true;v.portrait=rotation.gameObject;
            var rotateText=Text("Rotate",rotation.transform,new Vector2(-48,240),Vector2.zero,"기기를 가로로\n돌려 주세요\n\n<size=20>음악은 잠시 멈춰 있습니다.</size>",30,Bone);
            rotateText.rectTransform.anchorMin=new Vector2(0,.5f);rotateText.rectTransform.anchorMax=new Vector2(1,.5f);v.portrait.SetActive(false);
        }
    }
}
