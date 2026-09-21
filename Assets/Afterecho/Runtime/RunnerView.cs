using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;

namespace Afterecho
{
    public sealed class RunnerView : MonoBehaviour
    {
        public Canvas canvas;
        public RectTransform safeArea,track,runnerRoot,runnerVisual,judge,healthRoot;
        public UnityEngine.UI.Image background,runnerImage,hpFill,progressFill,boostStripe,overdriveBadge;
        public TMP_Text combo,hpText,score,progress,status,modeLabel,countText,boostText;
        public CanvasGroup overlay;
        public TMP_Text overlayTitle,overlayBody,primaryText,secondaryText;
        public UnityEngine.UI.Button primary,secondary,pauseButton;
        public GameObject menuOptions,portrait;
        public UnityEngine.UI.Slider volumeSlider,syncSlider;
        public UnityEngine.UI.Toggle motionToggle;
        public TMP_Text syncLabel;
        public UnityEngine.UI.Button[] difficultyButtons;
        public RunnerRing targetRing,hitRing;
        public RunnerRing[] notes;
        public Sprite[] runFrames,sprintFrames,fallFrames,recoverFrames;
        public float[] runFeet,sprintFeet,fallFeet,recoverFeet;
        public AudioSource effects;
        public AudioClip hitSound,perfectSound,fallSound,boostSound,clearSound;
        public int CurrentSide {get;private set;}
        public double LapPosition {get;private set;}
        public bool Falling=>fallUntil>game.ActiveSeconds;
        public int ShownNotes {get;private set;}
        public int FeedbackPlays {get;private set;}
        RunnerGame game;
        double fallStart,fallUntil=-1;
        float messageUntil,impactUntil,ghostAt;
        bool boosted;
        MMF_Player hitFeel,hpFeel,boostFeel,fallFeel;
        Tween stumbleTween;
        UnityEngine.UI.Image[] ghosts;
        float[] ghostEnd;
        UnityEngine.UI.Image[] dust;
        float[] dustEnd;
        float dustAt;
        int dustCursor;
        int ghostCursor;
        readonly Color ink=new Color(.055f,.06f,.085f);
        readonly Color paper=new Color(1,.97f,.88f);
        public void Initialize(RunnerGame owner)
        {
            game=owner;
            hitFeel=Feel("HitFeel",hitRing.transform,.115f,1.16f);
            hpFeel=Feel("HpFeel",healthRoot,.17f,1.10f);
            boostFeel=Feel("BoostFeel",runnerVisual,.18f,1.22f);
            fallFeel=Feel("FallFeel",runnerVisual,.15f,.8f);
            ghosts=new UnityEngine.UI.Image[10];ghostEnd=new float[10];
            for(int i=0;i<ghosts.Length;i++)
            {
                var go=new GameObject("RunnerGhost_"+i,typeof(RectTransform),typeof(UnityEngine.UI.Image)){hideFlags=HideFlags.DontSave};
                var rt=(RectTransform)go.transform;rt.SetParent(safeArea,false);rt.SetSiblingIndex(runnerRoot.GetSiblingIndex());
                rt.pivot=new Vector2(.5f,.5f);rt.sizeDelta=runnerImage.rectTransform.sizeDelta;
                ghosts[i]=go.GetComponent<UnityEngine.UI.Image>();ghosts[i].raycastTarget=false;ghosts[i].preserveAspect=true;go.SetActive(false);
            }
            dust=new UnityEngine.UI.Image[8];dustEnd=new float[8];
            for(int i=0;i<dust.Length;i++)
            {
                var go=new GameObject("FootSpark_"+i,typeof(RectTransform),typeof(UnityEngine.UI.Image)){hideFlags=HideFlags.DontSave};
                var rt=(RectTransform)go.transform;rt.SetParent(safeArea,false);rt.SetSiblingIndex(runnerRoot.GetSiblingIndex());
                dust[i]=go.GetComponent<UnityEngine.UI.Image>();dust[i].raycastTarget=false;go.SetActive(false);
            }
            primary.onClick.AddListener(Primary);
            secondary.onClick.AddListener(()=>{if(game.Phase==GamePhase.Menu)game.StartRun(true);else game.Menu();});
            pauseButton.onClick.AddListener(game.PauseGame);
            for(int i=0;i<difficultyButtons.Length;i++){string d=new[]{"easy","normal","hard"}[i];difficultyButtons[i].onClick.AddListener(()=>game.SelectDifficulty(d));}
            volumeSlider.SetValueWithoutNotify(game.volume);volumeSlider.onValueChanged.AddListener(game.SetVolume);
            syncSlider.SetValueWithoutNotify(game.syncMs);syncSlider.onValueChanged.AddListener(game.SetSync);
            motionToggle.SetIsOnWithoutNotify(game.reducedMotion);motionToggle.onValueChanged.AddListener(game.SetReducedMotion);
            ResetRun();
        }
        MMF_Player Feel(string name,Transform target,float duration,float peak)
        {
            var go=new GameObject(name){hideFlags=HideFlags.DontSave};go.transform.SetParent(transform,false);
            var p=go.AddComponent<MMF_Player>();p.InitializationMode=MMFeedbacks.InitializationModes.Script;
            p.AutoPlayOnStart=false;p.AutoPlayOnEnable=false;p.CanPlayWhileAlreadyPlaying=true;p.CooldownDuration=0;
            p.StopFeedbacksOnDisable=true;p.RestoreInitialValuesOnDisable=true;
            p.AddFeedback(new MMF_SquashAndStretch{Label=name,SquashAndStretchTarget=target,Mode=MMF_SquashAndStretch.Modes.Absolute,
                Axis=MMF_SquashAndStretch.PossibleAxis.YtoX,AnimateScaleDuration=duration,RemapCurveZero=1,RemapCurveOne=peak,
                AnimateCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.3f,1),new Keyframe(1,0)),AllowAdditivePlays=false,
                Timing=new MMFeedbackTiming{TimescaleMode=TimescaleModes.Unscaled,CooldownDuration=0}});
            p.PreInitialization();p.Initialization(true);return p;
        }
        void Play(MMF_Player p)
        {if(game.reducedMotion)return;p.StopFeedbacks();p.RestoreInitialValues();p.ResetAllCooldowns();p.PlayFeedbacks();FeedbackPlays++;}
        void Primary()
        {
            if(game.Phase==GamePhase.Paused)game.ResumeGame();
            else if(game.Phase==GamePhase.Ready)game.BeginMain();else game.StartRun(false);
        }
        public void ResetRun()
        {LapPosition=0;CurrentSide=0;fallUntil=-1;boosted=false;messageUntil=0;impactUntil=0;ghostAt=0;ghostCursor=0;dustAt=0;dustCursor=0;if(dustEnd!=null)Array.Clear(dustEnd,0,dustEnd.Length);if(ghostEnd!=null)Array.Clear(ghostEnd,0,ghostEnd.Length);status.text="";PlaceRunner();}
        public void AdvanceRunner(float dt)
        {
            double speed=game.Run.Boosted?game.rules.boostSpeed:1;
            if(Falling)speed*=.22;
            LapPosition=(LapPosition+dt/Math.Max(1,game.rules.lapSeconds)*speed)%1;
        }
        public static Vector3 PositionOnTrack(double lap,Vector2 size,out int side)
        {
            float w=Mathf.Max(100,size.x),h=Mathf.Max(100,size.y),perimeter=2*(w+h);
            float d=(float)(lap-Math.Floor(lap))*perimeter;
            if(d<w){side=0;return new Vector3(-w*.5f+d,-h*.5f,0);}d-=w;
            if(d<h){side=1;return new Vector3(w*.5f,-h*.5f+d,90);}d-=h;
            if(d<w){side=2;return new Vector3(w*.5f-d,h*.5f,180);}d-=w;
            side=3;return new Vector3(-w*.5f,h*.5f-d,270);
        }
        void PlaceRunner()
        {
            if(track==null)return;
            Vector3 p=PositionOnTrack(LapPosition,track.rect.size,out int side);CurrentSide=side;
            runnerRoot.anchoredPosition=new Vector2(p.x,p.y);runnerRoot.localRotation=Quaternion.Euler(0,0,p.z);
        }
        void ApplyFrame(Sprite[] frames,float[] feet,int index)
        {
            if(frames==null||frames.Length==0)return;
            index=Mathf.Clamp(index,0,frames.Length-1);runnerImage.sprite=frames[index];
            float baseline=feet!=null&&index<feet.Length?feet[index]:.12f;
            runnerImage.rectTransform.anchoredPosition=new Vector2(0,-baseline*runnerImage.rectTransform.rect.height);
        }
        void AnimateRunner()
        {
            bool fast=game.Run.Boosted;
            Sprite[] frames=fast?sprintFrames:runFrames;float[] feet=fast?sprintFeet:runFeet;
            runnerVisual.localRotation=Quaternion.Euler(0,0,fast&&!Falling?-10:0);
            if(Falling)
            {
                float elapsed=(float)(game.ActiveSeconds-fallStart),duration=game.rules.fallSeconds;
                bool recovery=elapsed>duration*.57f;var set=recovery?recoverFrames:fallFrames;var baseline=recovery?recoverFeet:fallFeet;
                float f=recovery?(elapsed-duration*.57f)/(duration*.43f):elapsed/(duration*.57f);
                if(set!=null&&set.Length>0)ApplyFrame(set,baseline,(int)(f*(set.Length-.001f)));
            }
            else if(frames!=null&&frames.Length>0)
            {int index=(int)(game.ActiveSeconds*frames.Length*(fast?3.6f:1.8f))%frames.Length;ApplyFrame(frames,feet,index);}
        }
        public void Render()
        {
            // Browser safe-area padding is owned by the HTML host. Unity can report a
            // stale device safeArea at the first WebGL frame (especially emulation).
            #if UNITY_WEBGL && !UNITY_EDITOR
            Rect s=new Rect(0,0,Screen.width,Screen.height);
            #else
            Rect s=Screen.safeArea;
            #endif
            safeArea.anchorMin=s.min/new Vector2(Screen.width,Screen.height);safeArea.anchorMax=s.max/new Vector2(Screen.width,Screen.height);
            safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
            bool active=game.Phase==GamePhase.Playing||game.Phase==GamePhase.Preparing;
            bool hud=!overlay.gameObject.activeSelf;
            judge.gameObject.SetActive(hud);combo.gameObject.SetActive(hud);healthRoot.gameObject.SetActive(hud);
            boostText.gameObject.SetActive(hud);boostStripe.gameObject.SetActive(hud);status.gameObject.SetActive(hud);
            pauseButton.gameObject.SetActive(active);portrait.SetActive(Screen.height>Screen.width);
            float touchSize=Mathf.Max(44,44/canvas.scaleFactor);
            var pauseRect=(RectTransform)pauseButton.transform;pauseRect.sizeDelta=new Vector2(touchSize,touchSize);
            pauseRect.anchoredPosition=new Vector2(-touchSize*.5f-6,-touchSize*.5f-6);
            if(portrait.activeSelf)
            {
                var guide=portrait.GetComponentInChildren<TMP_Text>();guide.fontSize=Mathf.Max(35,22/canvas.scaleFactor);
                guide.rectTransform.sizeDelta=new Vector2(Screen.width/canvas.scaleFactor-80,220/canvas.scaleFactor);
            }
            combo.text=$"{game.Run.Combo}<size=18>  COMBO</size>";
            hpFill.fillAmount=game.Run.Health/(float)game.rules.maxHealth;hpText.text=$"HP  {game.Run.Health:000} / {game.rules.maxHealth}";
            hpFill.color=game.Run.Health<=25?new Color(1f,.22f,.18f):new Color(.12f,.85f,.91f);
            if(overdriveBadge!=null)overdriveBadge.gameObject.SetActive(hud&&game.Run.Boosted);
            score.text=$"{game.Run.Score:000000}<size=14>  ×{game.Run.Multiplier}</size>";
            float completion=game.IsTutorial?0:(float)Math.Clamp(game.LogicalTime/game.Stage.duration,0,1);
            progress.text=$"{Math.Max(0,game.LogicalTime):000.0} / {game.Stage.duration:000.0}   •   {completion*100:00}%";progressFill.fillAmount=completion;
            modeLabel.text=game.autoPlay?"AUTO · TEST":game.invincible?"INVINCIBLE · TEST":game.IsTutorial?"PRACTICE / SAFE":"01 / "+(game.difficulty=="easy"?"입문":game.difficulty=="normal"?"보통":"도전");
            syncLabel.text=$"싱크 {game.syncMs:+0;-0;0} ms";
            boostText.text=game.Run.Boosted?$"OVERDRIVE  ×{game.rules.boostSpeed:0.0}":$"{game.rules.boostCombo} COMBO → OVERDRIVE";
            boostStripe.color=game.Run.Boosted?paper:new Color(0,0,0,.08f);
            Color target=game.LogicalTime<43.59?game.rules.orange:game.LogicalTime<86.995?game.rules.cyan:game.rules.magenta;
            background.color=Color.Lerp(background.color,target,Mathf.Clamp01(Time.unscaledDeltaTime*3));
            if(game.Phase!=GamePhase.Preparing)countText.text="";
            if(Time.unscaledTime>messageUntil)status.text=game.Phase==GamePhase.Preparing?"준비 · 4박 후 시작":game.Phase==GamePhase.Playing?(ShownNotes==0?"다음 원을 기다려요":game.IsTutorial?"연습 · 실수해도 괜찮아요":""):"";
            PlaceRunner();AnimateRunner();RenderNotes(active);RenderGhosts();RenderFootSparks();
            if(game.Run.Boosted!=boosted)
            {boosted=game.Run.Boosted;if(boosted){Play(boostFeel);Sound(boostSound,.65f);Message("OVERDRIVE",paper,.65f);}}
            hitRing.gameObject.SetActive(active&&Time.unscaledTime<impactUntil);
            if(hitRing.gameObject.activeSelf){float p=1-(impactUntil-Time.unscaledTime)/.16f;hitRing.SetDiameter(116+p*45,3,new Color(1,1,1,1-p));}
        }
        void RenderNotes(bool active)
        {
            int shown=0;
            if(active)
            {
                for(int i=game.Run.NextIndex;i<game.Run.Chart.notes.Length;i++)
                {
                    var n=game.Run.Chart.notes[i];double remaining=n.time-game.LogicalTime;
                    if(remaining>game.rules.previewSeconds)break;
                    if(game.Run.Decisions[i]!=NoteState.Pending||remaining < -game.Run.WindowAt(i))continue;
                    if(shown>=notes.Length){Debug.LogError("Runner preview pool exhausted; chart validation must reject this density.");break;}
                    var ring=notes[shown];ring.gameObject.SetActive(true);
                    float p=(float)Math.Clamp(remaining/game.rules.previewSeconds,-.15,1);
                    float diameter=116+Math.Max(0,p)*152;
                    ring.SetDiameter(diameter,shown==0?5:2.5f,shown==0?paper:new Color(ink.r,ink.g,ink.b,.50f));
                    shown++;
                }
            }
            ShownNotes=shown;for(int i=shown;i<notes.Length;i++)notes[i].gameObject.SetActive(false);
        }
        void RenderGhosts()
        {
            if(ghosts==null)return;
            if(game.Phase==GamePhase.Playing&&!game.reducedMotion&&!Falling&&game.ActiveSeconds>=ghostAt)
            {
                ghostAt=game.ActiveSeconds+(game.Run.Boosted?.025f:.12f);
                int i=ghostCursor++%ghosts.Length;var g=ghosts[i];g.sprite=runnerImage.sprite;
                g.rectTransform.position=runnerImage.rectTransform.position;g.rectTransform.rotation=runnerImage.rectTransform.rotation;
                g.rectTransform.localScale=runnerVisual.localScale;g.gameObject.SetActive(true);ghostEnd[i]=game.ActiveSeconds+.2f;
            }
            for(int i=0;i<ghosts.Length;i++)
            {float a=(ghostEnd[i]-game.ActiveSeconds)/.2f;ghosts[i].gameObject.SetActive(a>0&&!game.reducedMotion&&game.Phase==GamePhase.Playing);ghosts[i].color=new Color(1,1,1,a*(game.Run.Boosted?.4f:.16f));}
        }
        void RenderFootSparks()
        {
            if(dust==null)return;
            if(game.Phase==GamePhase.Playing&&!game.reducedMotion&&!Falling&&game.ActiveSeconds>=dustAt)
            {
                dustAt=game.ActiveSeconds+(game.Run.Boosted?.055f:.19f);
                int i=dustCursor++%dust.Length;var r=dust[i].rectTransform;
                r.position=runnerRoot.TransformPoint(new Vector3(-11,3,0));r.rotation=runnerRoot.rotation;
                r.sizeDelta=new Vector2(game.Run.Boosted?18:8,2);dustEnd[i]=game.ActiveSeconds+.16f;
            }
            for(int i=0;i<dust.Length;i++)
            {float a=(dustEnd[i]-game.ActiveSeconds)/.16f;dust[i].gameObject.SetActive(a>0&&!game.reducedMotion&&game.Phase==GamePhase.Playing);dust[i].color=new Color(1,1,1,a*.6f);}
        }
        public void React(RunEvent e)
        {
            if(e.kind=="step")
            {
                bool perfect=Math.Abs(e.error)<=Math.Min(.035,game.Run.WindowAt(e.index)*.5);
                Message(perfect?"PERFECT":"GOOD",perfect?paper:ink,.23f);impactUntil=Time.unscaledTime+.16f;
                Play(hitFeel);Sound(perfect?perfectSound:hitSound,perfect?.62f:.42f);
            }
            if(e.kind=="miss")
            {
                Message(e.practice?"다음 원에 맞춰보세요":$"MISS  −{game.rules.missDamage}",ink,.4f);
                if(!Falling){fallStart=game.ActiveSeconds;fallUntil=fallStart+game.rules.fallSeconds;Play(fallFeel);Sound(fallSound,.55f);}
                if(!e.practice)Play(hpFeel);
            }
            if(e.kind=="extra")
            {Message(e.practice?"원이 겹칠 때 입력":$"OFF BEAT  −{game.rules.extraDamage}",ink,.25f);if(!game.reducedMotion)PlayStumble();if(!e.practice)Play(hpFeel);}
            if(e.kind=="won")Sound(clearSound,.65f);
        }
        void PlayStumble()
        {
            // Restart from the resting pose, never from another punch's in-flight angle.
            StopStumble();
            stumbleTween=runnerImage.rectTransform.DOPunchRotation(new Vector3(0,0,-9),.12f,3)
                .SetUpdate(true).OnKill(()=>stumbleTween=null);
        }
        void StopStumble()
        {
            stumbleTween?.Kill();stumbleTween=null;
            runnerImage.rectTransform.localRotation=Quaternion.identity;
        }
        void Sound(AudioClip clip,float volume){if(clip!=null)effects.PlayOneShot(clip,volume);}
        void Message(string text,Color c,float duration){status.text=text;status.color=c;messageUntil=Time.unscaledTime+duration;}
        public void StopFeedback()
        {
            StopStumble();
            if(dust!=null)foreach(var d in dust)d.gameObject.SetActive(false);
            if(dustEnd!=null)Array.Clear(dustEnd,0,dustEnd.Length);
            foreach(var p in new[]{hitFeel,hpFeel,boostFeel,fallFeel})if(p!=null){p.StopFeedbacks();p.RestoreInitialValues();}
            if(ghosts!=null)foreach(var g in ghosts)g.gameObject.SetActive(false);
            if(ghostEnd!=null)Array.Clear(ghostEnd,0,ghostEnd.Length);
            judge.localScale=healthRoot.localScale=runnerVisual.localScale=Vector3.one;
            runnerVisual.localRotation=Quaternion.identity;hitRing.gameObject.SetActive(false);
        }
        void Overlay(string heading,string body,string action,string alternative,bool options)
        {
            overlay.gameObject.SetActive(true);overlay.alpha=1;overlay.blocksRaycasts=true;overlay.interactable=true;
            overlayTitle.text=heading;overlayBody.text=body;primaryText.text=action;secondaryText.text=alternative;
            secondary.gameObject.SetActive(alternative.Length>0);menuOptions.SetActive(options);
        }
        public void HideOverlay(){overlay.gameObject.SetActive(false);}
        public void Menu()
        {
            Overlay("잔향 <size=26>AFTERECHO</size>","원이 겹치는 순간, 어디든 한 번.\n리듬을 이어 15콤보를 만들면 질주합니다.","4박 후 시작","안전한 연습",true);
            for(int i=0;i<difficultyButtons.Length;i++)difficultyButtons[i].GetComponent<UnityEngine.UI.Image>().color=i==(game.difficulty=="easy"?0:game.difficulty=="normal"?1:2)?paper:new Color(1,1,1,.25f);
        }
        public void Ready()=>Overlay("리듬을 잡았나요?","미스 −10 · 헛입력 −3 · 성공 +1 HP\n4박 준비 후 노래가 처음부터 시작됩니다.","실전 시작","처음으로",false);
        public void Pause()=>Overlay("잠시 쉬어가기","음악과 원, 달리기가 함께 멈췄습니다.\n4박 준비 후 같은 위치에서 이어갑니다.","계속 달리기","처음으로",false);
        public void AudioProblem(string text)=>Overlay("음악 연결 확인",text,"다시 이어가기","처음으로",false);
        public void Count(int value){countText.text=value==0?"LOADING":value.ToString();status.text="준비 · 원이 겹칠 때 입력";}
        public void Result(bool won)
        {
            var r=game.Run;double accuracy=r.Hits*100d/Math.Max(1,r.Hits+r.Misses+r.Extras);
            Overlay(won?"끝까지 달렸다!":"다시, 리듬을 잡아봐요",$"점수 {r.Score:N0}    정확도 {accuracy:0.0}%\n최고 콤보 {r.Best}    미스 {r.Misses}    헛입력 {r.Extras}","다시 달리기","처음으로",false);
        }
        void OnDisable(){if(judge!=null)StopFeedback();}
    }
}
