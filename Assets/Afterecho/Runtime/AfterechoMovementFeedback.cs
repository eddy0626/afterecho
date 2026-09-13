using UnityEngine;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using DG.Tweening;

namespace Afterecho
{
    // Only the visual child is animated by Feel. Route position and the DSP clock stay independent.
    public sealed class AfterechoMovementFeedback : MonoBehaviour
    {
        [Range(.08f,.22f)] public float duration = .14f;
        [Range(1f,1.5f)] public float stretch = 1.26f;
        [Range(0f,.4f)] public float hopHeight = .18f;
        [Range(0f,.6f)] public float trailOpacity = .24f;
        AfterechoGame game;
        AfterechoView view;
        MMF_Player stepFeel;
        MMF_Rotation lean;
        GameObject poolRoot;
        sealed class Mark { public SpriteRenderer renderer; public Tween tween; }
        readonly Mark[] ghosts = new Mark[6], dust = new Mark[6];
        int cursor;
        public int PlayedSteps { get; private set; }
        public MMF_Player StepFeel => stepFeel;

        public void Initialize(AfterechoGame owner,AfterechoView ownerView)
        {
            if (stepFeel != null) return;
            game=owner; view=ownerView;
            poolRoot=new GameObject("MovementFX_Pool") { hideFlags=HideFlags.DontSave };
            poolRoot.transform.SetParent(transform,false);
            var feelObject=new GameObject("Movement_Step_Feel") { hideFlags=HideFlags.DontSave };
            feelObject.transform.SetParent(poolRoot.transform,false);
            stepFeel=feelObject.AddComponent<MMF_Player>();
            stepFeel.InitializationMode=MMFeedbacks.InitializationModes.Script;
            stepFeel.AutoPlayOnEnable=false; stepFeel.AutoPlayOnStart=false;
            stepFeel.StopFeedbacksOnDisable=true; stepFeel.RestoreInitialValuesOnDisable=true;
            stepFeel.CanPlayWhileAlreadyPlaying=true; stepFeel.CooldownDuration=0;
            stepFeel.AddFeedback(new MMF_SquashAndStretch {
                Label="Step body elasticity", SquashAndStretchTarget=view.playerVisual,
                Mode=MMF_SquashAndStretch.Modes.Absolute, Axis=MMF_SquashAndStretch.PossibleAxis.YtoX,
                AnimateScaleDuration=duration, RemapCurveZero=1,RemapCurveOne=stretch,
                AnimateCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.18f,-.35f),new Keyframe(.43f,1),new Keyframe(.8f,-.18f),new Keyframe(1,0)),
                AllowAdditivePlays=false, Timing=Unscaled()
            });
            stepFeel.AddFeedback(new MMF_Position {
                Label="Step lift and landing",AnimatePositionTarget=view.playerVisual.gameObject,
                Mode=MMF_Position.Modes.AlongCurve,Space=MMF_Position.Spaces.Local,
                RelativePosition=false,InitialPosition=Vector3.zero,AnimatePositionDuration=duration,
                AnimateX=false,AnimateY=true,AnimateZ=false,RemapCurveZero=0,RemapCurveOne=hopHeight,
                AnimatePositionTweenY=new MMTweenType(new AnimationCurve(new Keyframe(0,0),new Keyframe(.4f,1),new Keyframe(1,0)),"Step lift"),Timing=Unscaled()
            });
            lean=new MMF_Rotation {
                Label="Step directional lean",AnimateRotationTarget=view.playerVisual,
                Mode=MMF_Rotation.Modes.Additive,RotationSpace=Space.Self,
                AnimateRotationDuration=duration,AnimateX=false,AnimateY=false,AnimateZ=true,
                RemapCurveZero=0,RemapCurveOne=-6,
                AnimateRotationTweenZ=new MMTweenType(new AnimationCurve(new Keyframe(0,0),new Keyframe(.3f,1),new Keyframe(1,0)),"Step lean"),Timing=Unscaled()
            };
            stepFeel.AddFeedback(lean); stepFeel.PreInitialization(); stepFeel.Initialization(true);
            for(int i=0;i<ghosts.Length;i++)
            {
                ghosts[i]=CreateMark("StepAfterimage_"+i,view.playerSprite,view.playerSprite.sortingOrder-1);
                dust[i]=CreateMark("FootDust_"+i,view.footHalo,view.playerSprite.sortingOrder-2);
            }
        }
        static MMFeedbackTiming Unscaled() => new MMFeedbackTiming { TimescaleMode=TimescaleModes.Unscaled,CooldownDuration=0 };
        Mark CreateMark(string name,SpriteRenderer source,int order)
        {
            var go=new GameObject(name) { hideFlags=HideFlags.DontSave };
            go.transform.SetParent(poolRoot.transform,false);
            var r=go.AddComponent<SpriteRenderer>(); r.sharedMaterial=source.sharedMaterial;
            r.sprite=source.sprite; r.sortingLayerID=source.sortingLayerID; r.sortingOrder=order; r.enabled=false;
            return new Mark { renderer=r };
        }
        public void PlayStep(Vector3 from,Vector3 to,bool practice)
        {
            if(stepFeel==null || game.reducedMotion) return;
            StopBodyFeedback(); PlayedSteps++;
            Vector3 direction=(to-from).normalized;
            lean.RemapCurveOne=Mathf.Abs(direction.x)>.1f ? -6*Mathf.Sign(direction.x) : (PlayedSteps%2==0?4:-4);
            stepFeel.ResetAllCooldowns(); stepFeel.PlayFeedbacks();
            int i=cursor++%ghosts.Length;
            var puff=dust[i]; puff.tween?.Kill(); var r=puff.renderer;
            r.enabled=true; r.transform.position=from+new Vector3(0,-.54f,0);
            r.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            r.transform.localScale=new Vector3(.48f,.2f,1); r.color=new Color(.62f,.86f,.82f,.5f);
            puff.tween=DOTween.Sequence().Join(r.DOFade(0,.21f))
                .Join(r.transform.DOScale(new Vector3(1.05f,.35f,1),.21f))
                .Join(r.transform.DOMove(r.transform.position-direction*.25f,.21f))
                .OnComplete(()=>r.enabled=false).SetUpdate(true).SetLink(r.gameObject);
            if(practice) return;
            var ghost=ghosts[i]; ghost.tween?.Kill(); var g=ghost.renderer;
            g.enabled=true; g.sprite=view.playerSprite.sprite;g.flipX=view.playerSprite.flipX;
            g.transform.position=view.playerSprite.transform.position;
            g.transform.rotation=view.playerSprite.transform.rotation;
            g.transform.localScale=view.playerSprite.transform.lossyScale;
            g.color=new Color(.55f,.93f,.88f,trailOpacity);
            ghost.tween=g.DOFade(0,.17f).OnComplete(()=>g.enabled=false).SetUpdate(true).SetLink(g.gameObject);
        }
        public void StopBodyFeedback()
        {
            if(stepFeel==null)return;
            stepFeel.StopFeedbacks();stepFeel.RestoreInitialValues();
            view.playerVisual.localPosition=Vector3.zero;view.playerVisual.localRotation=Quaternion.identity;
            view.playerVisual.localScale=Vector3.one;
        }
        public void StopFeedback()
        {
            StopBodyFeedback();
            foreach(var marks in new[]{ghosts,dust})foreach(var m in marks)
                if(m!=null){m.tween?.Kill();if(m.renderer!=null)m.renderer.enabled=false;}
        }
        void LateUpdate(){if(game!=null && (game.reducedMotion || game.Phase!=GamePhase.Playing))StopFeedback();}
        void OnDisable(){StopFeedback();}
    }
}
