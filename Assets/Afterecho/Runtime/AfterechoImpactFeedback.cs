using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;

namespace Afterecho
{
    public enum ImpactType { Normal, Perfect, Attack, Kill }

    public sealed class AfterechoImpactFeedback : MonoBehaviour
    {
        [Header("Audio Balance")]
        [Range(0f, 2f)] public float sfxGain = 1.0f;
        [Range(0f, 1f)] public float normalVolume = 0.55f;
        [Range(0f, 1f)] public float perfectVolume = 0.95f;
        [Range(0f, 1f)] public float attackVolume = 0.42f;
        [Range(0f, 1f)] public float killVolume = 0.65f;

        [Header("Visual Scales")]
        public float normalPixelSize = 90f;
        public float perfectPixelSize = 150f;
        public float slashWorldSize = 1.6f;
        public float breakWorldSize = 2.0f;

        [Header("Durations")]
        public float normalDuration = 0.13f;
        public float perfectDuration = 0.20f;
        public float slashDuration = 0.12f;
        public float breakDuration = 0.22f;

        public AfterechoGame game;
        public AfterechoView view;

        // Audio Clips
        AudioClip perfectClip, attackClip, breakClip;

        // Sprites (sliced from ImpactAtlas)
        Sprite starRingSprite, slashSprite, breakSprite;
        readonly List<Sprite> createdSprites = new List<Sprite>();

        GameObject soundContainer, visualContainer, canvasHolder;

        // Pooled Canvas Impacts (Judge Point)
        sealed class CanvasImpactSlot
        {
            public GameObject root;
            public RectTransform rect;
            public Image image;
            public CanvasGroup group;
            public Tween tween;
        }
        CanvasImpactSlot[] canvasImpactPool;
        int nextCanvasImpact;

        // Pooled World Slashes (Enemy Position)
        sealed class WorldSlashSlot
        {
            public GameObject root;
            public Transform transform;
            public SpriteRenderer renderer;
            public Tween tween;
        }
        WorldSlashSlot[] slashPool;
        int nextSlashSlot;

        // Pooled World Break Bursts (Enemy Death)
        sealed class WorldBreakSlot
        {
            public GameObject root;
            public Transform transform;
            public SpriteRenderer renderer;
            public Tween tween;
        }
        WorldBreakSlot[] breakPool;
        int nextBreakSlot;

        // Feel Sound Player Pools
        MMF_Player[] normalSoundPool;
        int nextNormalSound;
        MMF_Player[] perfectSoundPool;
        int nextPerfectSound;
        MMF_Player[] attackSoundPool;
        int nextAttackSound;
        MMF_Player[] killSoundPool;
        int nextKillSound;

        // Judge icon feel
        MMF_Player judgePunchFeel;
        Tween enemyKnockbackTween;
        Tween enemyFlashTween;

        bool isInitialized;

        public void Initialize(AfterechoGame owner, AfterechoView ownerView)
        {
            game = owner;
            view = ownerView;

            CleanupAllPoolObjects();

            LoadClipsAndSprites();
            BuildSoundPools();
            BuildVisualPools();
            BuildJudgePunchFeel();

            isInitialized = true;
        }

        void CleanupAllPoolObjects()
        {
            StopAllFeedbacks();

            if (soundContainer != null) DestroyImmediate(soundContainer);
            if (visualContainer != null) DestroyImmediate(visualContainer);
            if (canvasHolder != null) DestroyImmediate(canvasHolder);

            // Also search and clean any leftover preview children
            var oldSound = transform.Find("SoundPools_Container");
            if (oldSound != null) DestroyImmediate(oldSound.gameObject);
            var oldWorld = transform.Find("WorldFX_Container");
            if (oldWorld != null) DestroyImmediate(oldWorld.gameObject);
            var oldPunch = transform.Find("JudgePoint_PunchFeel");
            if (oldPunch != null) DestroyImmediate(oldPunch.gameObject);

            if (view != null && view.judgment != null)
            {
                var oldCanvas = view.judgment.Find("CanvasImpactFX_Pool");
                if (oldCanvas != null) DestroyImmediate(oldCanvas.gameObject);
            }
        }

        void LoadClipsAndSprites()
        {
            perfectClip = Resources.Load<AudioClip>("ImpactFX/Perfect");
            attackClip = Resources.Load<AudioClip>("ImpactFX/Attack");
            breakClip = Resources.Load<AudioClip>("ImpactFX/Break");

            var atlasTex = Resources.Load<Texture2D>("ImpactFX/ImpactAtlas");
            if (atlasTex != null)
            {
                // TR (512,512, 512,512) -> StarRing for both Normal and Perfect
                starRingSprite = MakeSprite(atlasTex, new Rect(512, 512, 512, 512), "ImpactFX_StarRing");
                // BL (0,0, 512,512) -> Slash
                slashSprite = MakeSprite(atlasTex, new Rect(0, 0, 512, 512), "ImpactFX_Slash");
                // BR (512,0, 512,512) -> Break
                breakSprite = MakeSprite(atlasTex, new Rect(512, 0, 512, 512), "ImpactFX_Break");
            }
        }

        Sprite MakeSprite(Texture2D tex, Rect pixelRect, string spriteName)
        {
            var s = Sprite.Create(tex, pixelRect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = spriteName;
            s.hideFlags = HideFlags.DontSave;
            createdSprites.Add(s);
            return s;
        }

        void BuildSoundPools()
        {
            soundContainer = new GameObject("SoundPools_Container");
            soundContainer.hideFlags = HideFlags.DontSave;
            soundContainer.transform.SetParent(transform, false);

            normalSoundPool = CreateSoundPool("NormalHit_Feel", soundContainer, perfectClip, normalVolume, 0.95f, 4);
            perfectSoundPool = CreateSoundPool("PerfectHit_Feel", soundContainer, perfectClip, perfectVolume, 1.00f, 4);
            attackSoundPool = CreateSoundPool("AttackHit_Feel", soundContainer, attackClip != null ? attackClip : perfectClip, attackVolume, 1.00f, 4);
            killSoundPool = CreateSoundPool("KillHit_Feel", soundContainer, breakClip != null ? breakClip : perfectClip, killVolume, 1.00f, 3);
        }

        MMF_Player[] CreateSoundPool(string baseName, GameObject parentContainer, AudioClip clip, float volume, float pitch, int poolSize)
        {
            var pool = new MMF_Player[poolSize];
            var container = new GameObject(baseName + "_Pool");
            container.hideFlags = HideFlags.DontSave;
            container.transform.SetParent(parentContainer.transform, false);

            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"{baseName}_{i}");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(container.transform, false);

                var player = go.AddComponent<MMF_Player>();
                player.InitializationMode = MMFeedbacks.InitializationModes.Script;
                player.AutoPlayOnStart = false;
                player.AutoPlayOnEnable = false;
                player.CanPlayWhileAlreadyPlaying = true;
                player.StopFeedbacksOnDisable = true;
                player.RestoreInitialValuesOnDisable = true;
                player.FeedbacksList?.Clear();

                if (clip != null)
                {
                    var soundFeedback = new MMF_Sound
                    {
                        Label = $"{baseName}_{i}_Sound",
                        Sfx = clip,
                        PlayMethod = MMF_Sound.PlayMethods.Cached,
                        MinVolume = volume * sfxGain,
                        MaxVolume = volume * sfxGain,
                        MinPitch = pitch,
                        MaxPitch = pitch,
                        Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled, CooldownDuration = 0f }
                    };
                    player.AddFeedback(soundFeedback);
                }

                player.PreInitialization();
                player.Initialization(true);
                pool[i] = player;
            }
            return pool;
        }

        void BuildVisualPools()
        {
            // 1. Canvas Impact FX Pool at Judgment Point
            if (view != null && view.judgment != null)
            {
                int cPoolSize = 6;
                canvasImpactPool = new CanvasImpactSlot[cPoolSize];
                canvasHolder = new GameObject("CanvasImpactFX_Pool", typeof(RectTransform));
                canvasHolder.hideFlags = HideFlags.DontSave;
                canvasHolder.transform.SetParent(view.judgment, false);
                var cr = (RectTransform)canvasHolder.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
                cr.anchoredPosition = Vector2.zero;
                cr.sizeDelta = Vector2.zero;

                for (int i = 0; i < cPoolSize; i++)
                {
                    var slotGo = new GameObject($"ImpactFX_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
                    slotGo.hideFlags = HideFlags.DontSave;
                    slotGo.transform.SetParent(canvasHolder.transform, false);
                    var r = (RectTransform)slotGo.transform;
                    r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                    r.anchoredPosition = Vector2.zero;
                    r.sizeDelta = new Vector2(normalPixelSize, normalPixelSize);

                    var img = slotGo.GetComponent<Image>();
                    img.sprite = starRingSprite;
                    img.raycastTarget = false;
                    img.color = Color.white;

                    var cg = slotGo.GetComponent<CanvasGroup>();
                    cg.alpha = 0f;

                    slotGo.SetActive(false);
                    canvasImpactPool[i] = new CanvasImpactSlot { root = slotGo, rect = r, image = img, group = cg };
                }
            }

            visualContainer = new GameObject("WorldFX_Container");
            visualContainer.hideFlags = HideFlags.DontSave;
            visualContainer.transform.SetParent(transform, false);

            // 3. World Slash FX Pool (Enemy Attack Slash)
            int slashPoolSize = 4;
            slashPool = new WorldSlashSlot[slashPoolSize];

            for (int i = 0; i < slashPoolSize; i++)
            {
                var sGo = new GameObject($"WorldSlash_{i}");
                sGo.hideFlags = HideFlags.DontSave;
                sGo.transform.SetParent(visualContainer.transform, false);
                var sr = sGo.AddComponent<SpriteRenderer>();
                sr.sprite = slashSprite;
                sr.sortingOrder = 25;
                if (view != null && view.playerSprite != null) sr.sharedMaterial = view.playerSprite.sharedMaterial;
                sGo.SetActive(false);
                slashPool[i] = new WorldSlashSlot { root = sGo, transform = sGo.transform, renderer = sr };
            }

            // 4. World Break FX Pool (Enemy Kill Break)
            int breakPoolSize = 4;
            breakPool = new WorldBreakSlot[breakPoolSize];
            for (int i = 0; i < breakPoolSize; i++)
            {
                var bGo = new GameObject($"WorldBreak_{i}");
                bGo.hideFlags = HideFlags.DontSave;
                bGo.transform.SetParent(visualContainer.transform, false);
                var sr = bGo.AddComponent<SpriteRenderer>();
                sr.sprite = breakSprite;
                sr.sortingOrder = 26;
                if (view != null && view.playerSprite != null) sr.sharedMaterial = view.playerSprite.sharedMaterial;
                bGo.SetActive(false);
                breakPool[i] = new WorldBreakSlot { root = bGo, transform = bGo.transform, renderer = sr };
            }
        }

        void BuildJudgePunchFeel()
        {
            if (view == null || view.judgment == null) return;

            var go = new GameObject("JudgePoint_PunchFeel");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            judgePunchFeel = go.AddComponent<MMF_Player>();
            judgePunchFeel.InitializationMode = MMFeedbacks.InitializationModes.Script;
            judgePunchFeel.AutoPlayOnStart = false;
            judgePunchFeel.AutoPlayOnEnable = false;
            judgePunchFeel.CanPlayWhileAlreadyPlaying = true;
            judgePunchFeel.StopFeedbacksOnDisable = true;
            judgePunchFeel.RestoreInitialValuesOnDisable = true;

            var scaleFeedback = new MMF_Scale
            {
                Label = "Judge_ScalePunch",
                AnimateScaleTarget = view.judgment,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = 0.12f,
                RemapCurveZero = 1.0f,
                RemapCurveOne = 1.18f,
                AnimateScaleTweenX = new MMTweenType(new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.2f, 1f), new Keyframe(1, 0)), "x"),
                AnimateScaleTweenY = new MMTweenType(new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.2f, 1f), new Keyframe(1, 0)), "y"),
                Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled, CooldownDuration = 0f }
            };
            judgePunchFeel.AddFeedback(scaleFeedback);
            judgePunchFeel.PreInitialization();
            judgePunchFeel.Initialization(true);
        }

        /// <summary>
        /// Exact RunEvent entry point. Called whenever ChartEngine dispatches events.
        /// </summary>
        public void HandleRunEvent(RunEvent e)
        {
            if (!isInitialized) return;

            bool reduced = game != null && game.reducedMotion;

            if (e.kind == "step" || e.kind == "attack")
            {
                // Strict definition: abs(error) <= min(0.035s, 0.5 * actual note window)
                double window = (game != null && game.Run != null && e.index >= 0 && e.index < game.Run.Chart.notes.Length)
                    ? game.Run.WindowAt(e.index)
                    : 0.150;
                double maxPerfectMargin = Math.Min(0.035, 0.5 * window);
                bool isPerfect = Math.Abs(e.error) <= maxPerfectMargin;

                ImpactType type = e.kind == "attack" ? ImpactType.Attack : (isPerfect ? ImpactType.Perfect : ImpactType.Normal);

                // 1. Play central judge impact FX
                TriggerCanvasImpact(isPerfect, reduced);

                // 2. Play Audio via Feel Cached MMF_Sound
                if (type == ImpactType.Attack) PlayAttackSound();
                else if (isPerfect) PlayPerfectSound();
                else PlayNormalSound();

                // 3. Punch Judge Point
                PunchJudgePoint(isPerfect, reduced);

                // 4. If attack: trigger enemy slash & visual flash & subtle knockback
                if (e.kind == "attack")
                {
                    TriggerEnemyAttackFeedback(reduced);
                }
            }
            else if (e.kind == "kill")
            {
                PlayKillSound();
                TriggerEnemyKillFeedback(reduced);
            }
        }

        void PlayNormalSound()
        {
            PlaySoundFromPool(normalSoundPool, ref nextNormalSound, normalVolume);
        }

        void PlayPerfectSound()
        {
            PlaySoundFromPool(perfectSoundPool, ref nextPerfectSound, perfectVolume);
        }

        void PlayAttackSound()
        {
            PlaySoundFromPool(attackSoundPool, ref nextAttackSound, attackVolume);
        }

        void PlayKillSound()
        {
            PlaySoundFromPool(killSoundPool, ref nextKillSound, killVolume);
        }

        void PlaySoundFromPool(MMF_Player[] pool, ref int nextIdx, float baseVolume)
        {
            if (pool == null || pool.Length == 0) return;
            var p = pool[nextIdx];
            nextIdx = (nextIdx + 1) % pool.Length;
            if (p != null)
            {
                if (p.FeedbacksList != null && p.FeedbacksList.Count > 0 && p.FeedbacksList[0] is MMF_Sound soundFb)
                {
                    float targetVol = Mathf.Clamp(baseVolume * sfxGain, 0f, 2f);
                    soundFb.MinVolume = targetVol;
                    soundFb.MaxVolume = targetVol;
                }
                PlayFeel(p);
            }
        }

        static void PlayFeel(MMF_Player p)
        {
            if (p == null) return;
            p.StopFeedbacks();
            p.RestoreInitialValues();
            p.ResetAllCooldowns();
            p.PlayFeedbacks();
        }

        void TriggerCanvasImpact(bool isPerfect, bool reduced)
        {
            if (canvasImpactPool == null || canvasImpactPool.Length == 0) return;

            var slot = canvasImpactPool[nextCanvasImpact];
            nextCanvasImpact = (nextCanvasImpact + 1) % canvasImpactPool.Length;

            slot.tween?.Kill();
            slot.root.SetActive(true);

            float targetPx = isPerfect ? perfectPixelSize : normalPixelSize;
            if (reduced) targetPx *= 0.75f;
            float duration = isPerfect ? perfectDuration : normalDuration;

            Color impactColor = isPerfect ? new Color(0.92f, 0.98f, 1f, 1f) : new Color(0.55f, 0.88f, 0.82f, 0.9f);
            slot.image.color = impactColor;
            slot.rect.sizeDelta = new Vector2(targetPx * 0.4f, targetPx * 0.4f);
            slot.group.alpha = 1f;

            var seq = DOTween.Sequence().SetUpdate(true).SetLink(slot.root);
            seq.Join(slot.rect.DOSizeDelta(new Vector2(targetPx, targetPx), duration).SetEase(Ease.OutQuad));
            seq.Join(slot.group.DOFade(0f, duration * 0.8f).SetEase(Ease.InQuad).SetDelay(duration * 0.2f));
            seq.OnComplete(() => {
                slot.root.SetActive(false);
                slot.group.alpha = 0f;
            });
            slot.tween = seq;
        }

        void PunchJudgePoint(bool isPerfect, bool reduced)
        {
            if (view == null || view.judgment == null) return;
            if (!reduced && judgePunchFeel != null)
            {
                PlayFeel(judgePunchFeel);
            }
        }

        void TriggerEnemyAttackFeedback(bool reduced)
        {
            if (view == null || view.enemyRoot == null || !view.enemyRoot.gameObject.activeInHierarchy) return;

            // 1. World Slash FX
            if (slashPool != null && slashPool.Length > 0)
            {
                var slot = slashPool[nextSlashSlot];
                nextSlashSlot = (nextSlashSlot + 1) % slashPool.Length;

                slot.tween?.Kill();
                slot.root.SetActive(true);
                slot.transform.position = view.enemyRoot.position + new Vector3(0, 0.15f, 0);
                slot.transform.localScale = Vector3.one * (slashWorldSize * (reduced ? 0.6f : 0.8f));
                slot.renderer.color = new Color(1f, 1f, 1f, 1f);

                var seq = DOTween.Sequence().SetUpdate(true).SetLink(slot.root);
                seq.Join(slot.transform.DOScale(Vector3.one * slashWorldSize * 1.15f, slashDuration).SetEase(Ease.OutQuad));
                seq.Join(slot.renderer.DOFade(0f, slashDuration).SetEase(Ease.InCubic));
                seq.OnComplete(() => slot.root.SetActive(false));
                slot.tween = seq;
            }

            // 2. Enemy Sprite Flash (pure crisp white flash)
            if (view.enemySprite != null)
            {
                enemyFlashTween?.Kill();
                view.enemySprite.color = new Color(1.8f, 1.8f, 1.8f, 1f);
                enemyFlashTween = view.enemySprite.DOColor(Color.white, 0.09f).SetUpdate(true).SetLink(view.enemySprite.gameObject);
            }

            // 3. Visual-only knockback (no game logic impact)
            if (!reduced && view.enemyVisual != null)
            {
                enemyKnockbackTween?.Kill();
                view.enemyVisual.localPosition = new Vector3(0.12f, 0.05f, 0);
                enemyKnockbackTween = view.enemyVisual.DOLocalMove(Vector3.zero, 0.10f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(view.enemyVisual.gameObject);
            }
        }

        void TriggerEnemyKillFeedback(bool reduced)
        {
            if (view == null || view.enemyRoot == null) return;

            if (breakPool != null && breakPool.Length > 0)
            {
                var slot = breakPool[nextBreakSlot];
                nextBreakSlot = (nextBreakSlot + 1) % breakPool.Length;

                slot.tween?.Kill();
                slot.root.SetActive(true);
                slot.transform.position = view.enemyRoot.position + new Vector3(0, 0.2f, 0);
                slot.transform.localScale = Vector3.one * (breakWorldSize * 0.7f);
                slot.transform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-25f, 25f));
                slot.renderer.color = new Color(1f, 1f, 1f, 1f);

                var seq = DOTween.Sequence().SetUpdate(true).SetLink(slot.root);
                seq.Join(slot.transform.DOScale(Vector3.one * breakWorldSize * 1.25f, breakDuration).SetEase(Ease.OutQuad));
                seq.Join(slot.renderer.DOFade(0f, breakDuration).SetEase(Ease.InQuad));
                seq.OnComplete(() => slot.root.SetActive(false));
                slot.tween = seq;
            }
        }

        /// <summary>
        /// Cleanly stop all pooled visual FX and sound players on pause / menu / restart.
        /// </summary>
        public void StopAllFeedbacks()
        {
            if (canvasImpactPool != null)
            {
                foreach (var s in canvasImpactPool)
                {
                    s.tween?.Kill();
                    if (s.root != null) s.root.SetActive(false);
                }
            }
            if (slashPool != null)
            {
                foreach (var s in slashPool)
                {
                    s.tween?.Kill();
                    if (s.root != null) s.root.SetActive(false);
                }
            }
            if (breakPool != null)
            {
                foreach (var b in breakPool)
                {
                    b.tween?.Kill();
                    if (b.root != null) b.root.SetActive(false);
                }
            }

            enemyFlashTween?.Kill();
            enemyKnockbackTween?.Kill();

            if (view != null)
            {
                if (view.enemyVisual != null) view.enemyVisual.localPosition = Vector3.zero;
                if (view.enemySprite != null) view.enemySprite.color = Color.white;
                if (view.judgment != null) view.judgment.localScale = Vector3.one;
            }

            StopPool(normalSoundPool);
            StopPool(perfectSoundPool);
            StopPool(attackSoundPool);
            StopPool(killSoundPool);
            if (judgePunchFeel != null) judgePunchFeel.StopFeedbacks();
        }

        static void StopPool(MMF_Player[] pool)
        {
            if (pool == null) return;
            foreach (var p in pool)
            {
                if (p != null) { p.StopFeedbacks(); p.RestoreInitialValues(); }
            }
        }

        /// <summary>
        /// Safe Preview Method for Editor Panel — Never modifies chart score or health.
        /// </summary>
        public void PlayPreview(ImpactType type)
        {
            if (!isInitialized) return;

            bool reduced = game != null && game.reducedMotion;

            switch (type)
            {
                case ImpactType.Normal:
                    TriggerCanvasImpact(false, reduced);
                    PlayNormalSound();
                    PunchJudgePoint(false, reduced);
                    break;
                case ImpactType.Perfect:
                    TriggerCanvasImpact(true, reduced);
                    PlayPerfectSound();
                    PunchJudgePoint(true, reduced);
                    break;
                case ImpactType.Attack:
                    TriggerCanvasImpact(true, reduced);
                    PlayAttackSound();
                    PunchJudgePoint(true, reduced);
                    TriggerEnemyAttackFeedback(reduced);
                    break;
                case ImpactType.Kill:
                    PlayKillSound();
                    TriggerEnemyKillFeedback(reduced);
                    break;
            }
        }

        void OnDisable()
        {
            StopAllFeedbacks();
        }

        void OnDestroy()
        {
            StopAllFeedbacks();
            foreach (var s in createdSprites) if (s != null) Destroy(s);
        }
    }
}
