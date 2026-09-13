using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MoreMountains.Feedbacks;

namespace Afterecho
{
    // Presentation only: the existing chart and DSP clock remain authoritative.
    public sealed class AfterechoPolish : MonoBehaviour
    {
        public float dashDuration = .12f;
        public float attackDuration = .10f;
        AfterechoGame game;
        AfterechoView view;
        Sprite idle, buttonArt;
        Sprite[] dash;
        readonly List<Sprite> sprites = new List<Sprite>();
        readonly List<MMF_Player> feedbacks = new List<MMF_Player>();
        readonly List<Image> hearts = new List<Image>();
        MMF_Player comboFeel, heartFeel, attackFeel;
        object previousRun;
        int previousHits, previousSteps, previousDamage;
        float actionAge = 100f;
        bool attacking, initialized;
        Vector3 lastPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var owner = UnityEngine.Object.FindFirstObjectByType<AfterechoGame>();
            if (owner != null && owner.GetComponent<AfterechoPolish>() == null)
                owner.gameObject.AddComponent<AfterechoPolish>();
        }
        void Start()
        {
            game = GetComponent<AfterechoGame>();
            if (game == null || game.view == null) { enabled = false; return; }
            view = game.view;
            idle = view.playerSprite.sprite;
            var textures = Resources.LoadAll<Texture2D>("TravelerDash");
            Array.Sort(textures, (a,b) => StringComparer.Ordinal.Compare(a.name,b.name));
            dash = new Sprite[textures.Length];
            for (int i=0; i<textures.Length; i++)
                dash[i] = MakeSprite(textures[i],new Rect(0,0,1,1),textures[i].height/2f,0);
            var atlas = Resources.Load<Texture2D>("Afterecho_UI_Gameaify");
            if (atlas != null)
            {
                var panelArt = MakeSprite(atlas,new Rect(.043f,.492f,.390f,.495f),100,24);
                buttonArt = MakeSprite(atlas,new Rect(.486f,.671f,.505f,.140f),100,0);
                var heartArt = MakeSprite(atlas,new Rect(.540f,.022f,.422f,.416f),100,0);
                Skin(view.overlayPanel.GetComponent<Image>(),panelArt,true);
                Skin(view.primary.GetComponent<Image>(),buttonArt,false);
                Skin(view.secondary.GetComponent<Image>(),buttonArt,false);
                foreach (var button in view.difficulties) Skin(button.GetComponent<Image>(),buttonArt,false);
                CreateHearts(heartArt);
            }
            comboFeel = MakeFeedback("ComboImpact_Feel",view.comboText.transform,.11f,1.13f);
            heartFeel = MakeFeedback("HealthImpact_Feel",view.healthText.transform,.20f,.82f);
            attackFeel = MakeFeedback("TravelerAttack_Feel",view.playerVisual,.10f,.86f);
            lastPosition = view.playerRoot.position;
            initialized = true;
        }
        Sprite MakeSprite(Texture2D texture,Rect normalized,float ppu,float border)
        {
            var rect = new Rect(Mathf.Round(normalized.x*texture.width),Mathf.Round(normalized.y*texture.height),
                Mathf.Floor(normalized.width*texture.width),Mathf.Floor(normalized.height*texture.height));
            var result = Sprite.Create(texture,rect,new Vector2(.5f,.5f),ppu,0,SpriteMeshType.FullRect,
                new Vector4(border,border,border,border));
            result.name = texture.name + "_Presentation"; sprites.Add(result); return result;
        }
        static void Skin(Image image,Sprite sprite,bool sliced)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite; image.color = Color.white;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
        }
        void CreateHearts(Sprite art)
        {
            view.healthText.enabled = false;
            for (int i=0;i<5;i++)
            {
                var go = new GameObject("CrystalHealth_"+(i+1),typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(view.healthText.transform,false);
                rect.anchorMin = rect.anchorMax = new Vector2(0,.5f);
                rect.pivot = new Vector2(0,.5f); rect.anchoredPosition = new Vector2(i*29,0);
                rect.sizeDelta = new Vector2(26,28);
                var image = go.GetComponent<Image>();
                image.sprite = art; image.preserveAspect = true; image.raycastTarget = false;
                hearts.Add(image);
            }
        }
        MMF_Player MakeFeedback(string label,Transform target,float duration,float strength)
        {
            // Reuse the scene's validated Feel player configuration, with independent cloned feedback data.
            var player = Instantiate(view.stepFeedback,transform);
            player.name = label;
            player.StopFeedbacks();
            var squash = player.FeedbacksList[0] as MMF_SquashAndStretch;
            if (squash == null) { Destroy(player.gameObject); return null; }
            squash.SquashAndStretchTarget = target;
            squash.Mode = MMF_SquashAndStretch.Modes.Absolute;
            squash.Axis = MMF_SquashAndStretch.PossibleAxis.YtoX;
            squash.AnimateScaleDuration = duration;
            squash.RemapCurveZero = 1f; squash.RemapCurveOne = strength;
            squash.AnimateCurve = new AnimationCurve(new Keyframe(0,0),new Keyframe(.18f,1),new Keyframe(1,0));
            squash.AllowAdditivePlays = false;
            player.PreInitialization(); player.Initialization(true);
            feedbacks.Add(player); return player;
        }
        static void Replay(MMF_Player player)
        {
            if (player == null) return;
            player.StopFeedbacks(); player.RestoreInitialValues(); player.ResetAllCooldowns(); player.PlayFeedbacks();
        }
        void StopPresentation()
        {
            actionAge = 100f;
            foreach (var f in feedbacks) if (f != null) { f.StopFeedbacks(); f.RestoreInitialValues(); }
            if (view != null && view.playerSprite != null) { view.playerSprite.sprite = idle; view.playerSprite.flipX = false; }
        }
        void LateUpdate()
        {
            if (!initialized || game.Run == null) return;
            var run = game.Run;
            int hits = run.Hits + run.PracticeHits;
            if (!ReferenceEquals(previousRun,run))
            {
                previousRun=run; previousHits=hits; previousSteps=run.Steps;
                previousDamage=run.Damage; StopPresentation();
            }
            bool playing = game.Phase == GamePhase.Playing;
            if (playing && !game.reducedMotion && hits > previousHits)
            {
                actionAge=0;
                attacking=run.Steps==previousSteps && game.LogicalTime>=game.Stage.MainTime;
                if (view.impactFeedback == null)
                {
                    if (attacking) Replay(attackFeel);
                    Replay(comboFeel);
                }
            }
            if (playing && !game.reducedMotion && run.Damage > previousDamage) Replay(heartFeel);
            if (!playing || game.reducedMotion) StopPresentation();
            else
            {
                float duration=attacking ? attackDuration : dashDuration;
                if (actionAge < duration && dash.Length > 0)
                {
                    int frame=Mathf.Clamp(Mathf.FloorToInt(actionAge/duration*dash.Length),0,dash.Length-1);
                    view.playerSprite.sprite=dash[frame];
                    float dx=view.playerRoot.position.x-lastPosition.x;
                    if (Mathf.Abs(dx)>.001f) view.playerSprite.flipX=dx<0;
                    actionAge+=Time.unscaledDeltaTime;
                }
                else { view.playerSprite.sprite=idle; view.playerSprite.flipX=false; }
            }
            for (int i=0;i<hearts.Count;i++)
            {
                hearts[i].gameObject.SetActive(i<run.Health+run.Damage);
                hearts[i].color=i<run.Health ? Color.white : new Color(.23f,.28f,.29f,.65f);
            }
            int selected=game.difficulty=="easy"?0:game.difficulty=="normal"?1:2;
            for (int i=0;i<view.difficulties.Length;i++)
                if (buttonArt!=null) view.difficulties[i].GetComponent<Image>().color =
                    i==selected ? Color.white : new Color(.53f,.58f,.59f);
            previousHits=hits; previousSteps=run.Steps; previousDamage=run.Damage;
            lastPosition=view.playerRoot.position;
        }
        void OnDisable() { if (initialized) StopPresentation(); }
        void OnDestroy() { foreach (var sprite in sprites) if (sprite!=null) Destroy(sprite); }
    }
}
