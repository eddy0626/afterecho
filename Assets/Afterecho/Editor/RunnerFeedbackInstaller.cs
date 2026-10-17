using System;
using Afterecho.Feedback;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Afterecho.Editor
{
    // Places the runner's feedback prefabs in the scene, binds them to RunnerFeedbackPlayer,
    // and sets up the HP bar's delayed damage trail (Feel MMProgressBar).
    // Idempotent: RunnerSceneBuilder calls it for new scenes, and the menu item updates an
    // existing scene without rebuilding it. Prefabs are created only when missing, so values
    // tuned in the Inspector are never overwritten.
    public static class RunnerFeedbackInstaller
    {
        public const string PrefabFolder = "Assets/Afterecho/Runner/Feedbacks";
        const string ContainerName = "Feedbacks";
        const string HitPrefab = "FB_Runner_Hit";
        const string HitPerfectPrefab = "FB_Runner_HitPerfect";
        const string HpLossPrefab = "FB_Runner_HpLoss";
        const string BoostStartPrefab = "FB_Runner_BoostStart";
        const string FallPrefab = "FB_Runner_Fall";
        const string JudgementTextPrefab = "FB_Runner_JudgementText";
        const string HpWobblePrefab = "FB_Runner_HpWobble";
        const string TargetPopPrefab = "FB_Runner_TargetPop";
        const string DotPopPrefab = "FB_Runner_DotPop";
        const string ComboPopPrefab = "FB_Runner_ComboPop";
        const string ComboBreakWobblePrefab = "FB_Runner_ComboBreakWobble";
        const string ComboBreakColorPrefab = "FB_Runner_ComboBreakColor";
        const string BeatPulsePrefab = "FB_Runner_BeatPulse";
        const string BeatRingName = "BeatPulse";
        const string JudgementPerfectPrefab = "FB_Runner_JudgementPerfect";
        const string JudgementTiltPrefab = "FB_Runner_JudgementTilt";
        const string HpBeatPrefab = "FB_Runner_HpBeat";
        const string HpHeartbeatPrefab = "FB_Runner_HpHeartbeat";
        static readonly Color Paper = new Color(1f, 0.97f, 0.88f);
        static readonly Color Danger = new Color(1f, 0.22f, 0.18f);

        // Instances that earlier versions placed and that are no longer bound; removed on install.
        static readonly string[] RetiredInstances = { "FB_Runner_HpShake" };
        const string BoostFlashPrefab = "FB_Runner_BoostFlash";
        const string BoostFlashName = "BoostFlash";
        const string StumblePrefab = "FB_Runner_Stumble";
        const string ResultPopPrefab = "FB_Runner_ResultPop";
        const string HealthTrailName = "HpDelayed";
        static readonly Color HealthTrailColor = new Color(1f, 0.97f, 0.88f, 0.9f);

        // One prefab = one MMF_Player with one feedback, so every effect has a single target and a
        // single channel. The first four use the values RunnerView.Feel() used.
        static readonly PrefabSpec[] Specs =
        {
            new PrefabSpec(HitPrefab, name => Squash(name, 0.115f, 1.16f)),
            new PrefabSpec(HpLossPrefab, name => Squash(name, 0.17f, 1.10f)),
            new PrefabSpec(BoostStartPrefab, name => Squash(name, 0.18f, 1.22f)),
            new PrefabSpec(FallPrefab, name => Squash(name, 0.15f, 0.8f)),
            // Uniform pop for PERFECT / GOOD / MISS / OFF BEAT text; shorter than the 0.17 s minimum note gap.
            new PrefabSpec(JudgementTextPrefab, name => Pop(name, 0.12f, 1.4f)),
            // The HP gauge staggers (tilts back and forth) when HP drops.
            new PrefabSpec(HpWobblePrefab, name => PunchZ(name, 0.35f, 7f)),
            // On every successful hit the fixed target ring and the centre dot pop.
            new PrefabSpec(TargetPopPrefab, name => Pop(name, 0.12f, 1.12f)),
            new PrefabSpec(DotPopPrefab, name => Pop(name, 0.12f, 1.6f)),
            // Combo up: pop. Combo broken: stagger plus a red flash of the text.
            new PrefabSpec(ComboPopPrefab, name => Pop(name, 0.12f, 1.25f)),
            new PrefabSpec(ComboBreakWobblePrefab, name => PunchZ(name, 0.35f, 10f)),
            new PrefabSpec(ComboBreakColorPrefab, name => TextColorFlash(name, 0.45f, Danger)),
            // Ring that breathes on the stage beat grid (fast attack, slow release).
            new PrefabSpec(BeatPulsePrefab, name => Pulse(name, 0.45f, 1.12f)),
            // MISS / OFF BEAT: the text drops clockwise around its left edge, rebounds, drops again
            // and settles (a head-nod). RunnerView moves the pivot to the glyphs' left edge first.
            new PrefabSpec(JudgementTiltPrefab, name => Nod(name, 0.5f, -12f)),
            // HP gauge on every beat: a barely visible pulse, kept far below the HP-loss squash (1.1)
            // and wobble so damage stays the strongest signal on the bar.
            new PrefabSpec(HpBeatPrefab, name => Pulse(name, 0.3f, 1.03f)),
            // While HP is in danger the same beat becomes a double-thump heartbeat.
            new PrefabSpec(HpHeartbeatPrefab, name => Heartbeat(name, 0.5f, 1.08f)),
            // One full-screen flash per OVERDRIVE start (rare, far below 3 flashes per second).
            new PrefabSpec(BoostFlashPrefab, name => Flash(name, 0.3f, 0.45f)),
            // Off-beat stumble: same as the former DOTween DOPunchRotation(0, 0, -9), 0.12 s.
            new PrefabSpec(StumblePrefab, name => PunchZ(name, 0.12f, -9f)),
            // Result panel grows from 85% with a slight overshoot and settles at 100%.
            new PrefabSpec(ResultPopPrefab, name => PopIn(name, 0.3f, 0.85f)),
        };

        // Scene-object fields the feedbacks animate; scene instances override them.
        static readonly string[] TargetProperties =
        {
            "SquashAndStretchTarget", "AnimateScaleTarget", "AnimatePositionTarget", "AnimateRotationTarget", "BoundImage",
            "TargetTMPText",
        };

        // Variants inherit everything from their base prefab and override only the peak,
        // so tuning the base (duration, curve) carries over.
        static readonly VariantSpec[] Variants =
        {
            new VariantSpec(HitPerfectPrefab, HitPrefab, 1.25f),
            // PERFECT text pops to twice its size (GOOD / OVERDRIVE: 1.4).
            new VariantSpec(JudgementPerfectPrefab, JudgementTextPrefab, 2f),
        };

        [MenuItem("Afterecho/Runner/Install Feedbacks In Open Scene")]
        public static string InstallInOpenScene()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before installing feedbacks.");
            var view = Object.FindFirstObjectByType<RunnerView>();
            if (view == null)
                throw new InvalidOperationException("Open the runner scene first.");
            Install(view);
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            EditorSceneManager.SaveScene(view.gameObject.scene);
            return "runner feedbacks installed in " + view.gameObject.scene.path;
        }

        public static void Install(RunnerView view)
        {
            EnsurePrefabs();
            var container = view.transform.Find(ContainerName);
            if (container == null)
            {
                container = new GameObject(ContainerName).transform;
                container.SetParent(view.transform, false);
            }

            var hit = PlaceInstance(container, HitPrefab, view.hitRing.transform);
            var hitPerfect = PlaceInstance(container, HitPerfectPrefab, view.hitRing.transform);
            var hpLoss = PlaceInstance(container, HpLossPrefab, view.healthRoot);
            var boostStart = PlaceInstance(container, BoostStartPrefab, view.runnerVisual);
            var fall = PlaceInstance(container, FallPrefab, view.runnerVisual);
            var judgementText = PlaceInstance(container, JudgementTextPrefab, view.status.rectTransform);
            foreach (string retired in RetiredInstances)
            {
                var stale = container.Find(retired);
                if (stale != null)
                    Object.DestroyImmediate(stale.gameObject);
            }
            var hpWobble = PlaceInstance(container, HpWobblePrefab, view.hpFill.transform.parent);
            var targetPop = PlaceInstance(container, TargetPopPrefab, view.targetRing.transform);
            var dotPop = PlaceInstance(container, DotPopPrefab, view.judge.Find("CenterDot"));
            var comboPop = PlaceInstance(container, ComboPopPrefab, view.combo.rectTransform);
            var comboBreakWobble = PlaceInstance(container, ComboBreakWobblePrefab, view.combo.rectTransform);
            var comboBreakColor = PlaceInstance(container, ComboBreakColorPrefab, view.combo.rectTransform);
            var beatPulse = PlaceInstance(container, BeatPulsePrefab, EnsureBeatRing(view.judge));
            var judgementPerfect = PlaceInstance(container, JudgementPerfectPrefab, view.status.rectTransform);
            var judgementTilt = PlaceInstance(container, JudgementTiltPrefab, view.status.rectTransform);
            var hpBeat = PlaceInstance(container, HpBeatPrefab, view.hpFill.transform.parent);
            var hpHeartbeat = PlaceInstance(container, HpHeartbeatPrefab, view.hpFill.transform.parent);
            var boostFlash = PlaceInstance(container, BoostFlashPrefab, EnsureFlashImage(view.canvas.transform));
            var stumble = PlaceInstance(container, StumblePrefab, view.runnerImage.rectTransform);
            var resultPop = PlaceInstance(container, ResultPopPrefab, view.overlay.transform);

            var player = view.GetComponent<RunnerFeedbackPlayer>();
            if (player == null)
                player = view.gameObject.AddComponent<RunnerFeedbackPlayer>();
            SetBindings(player,
                (FeedbackId.HitGood, FeedbackChannel.Motion, hit),
                (FeedbackId.HitGood, FeedbackChannel.Motion, targetPop),
                (FeedbackId.HitGood, FeedbackChannel.Motion, dotPop),
                (FeedbackId.HitPerfect, FeedbackChannel.Motion, hitPerfect),
                (FeedbackId.HitPerfect, FeedbackChannel.Motion, targetPop),
                (FeedbackId.HitPerfect, FeedbackChannel.Motion, dotPop),
                (FeedbackId.HpLoss, FeedbackChannel.Motion, hpLoss),
                (FeedbackId.HpLoss, FeedbackChannel.Motion, hpWobble),
                (FeedbackId.Fall, FeedbackChannel.Motion, fall),
                (FeedbackId.BoostStart, FeedbackChannel.Motion, boostStart),
                (FeedbackId.BoostStart, FeedbackChannel.Flash, boostFlash),
                (FeedbackId.JudgementText, FeedbackChannel.Motion, judgementText),
                (FeedbackId.Stumble, FeedbackChannel.Motion, stumble),
                (FeedbackId.ResultShow, FeedbackChannel.Motion, resultPop),
                (FeedbackId.ComboUp, FeedbackChannel.Motion, comboPop),
                (FeedbackId.ComboBreak, FeedbackChannel.Motion, comboBreakWobble),
                // Colour is information, not motion: it stays with Reduced Motion.
                (FeedbackId.ComboBreak, FeedbackChannel.Text, comboBreakColor),
                (FeedbackId.Beat, FeedbackChannel.Motion, beatPulse),
                (FeedbackId.JudgementPerfect, FeedbackChannel.Motion, judgementPerfect),
                (FeedbackId.JudgementMiss, FeedbackChannel.Motion, judgementTilt),
                (FeedbackId.HpBeat, FeedbackChannel.Motion, hpBeat),
                (FeedbackId.HpHeartbeat, FeedbackChannel.Motion, hpHeartbeat));

            view.feedback = player;
            view.hpBar = InstallHealthBar(view.hpFill);
            EditorUtility.SetDirty(view);
        }

        // Decorative ring outside the largest approach ring (268), drawn behind the judge UI.
        static Transform EnsureBeatRing(Transform judge)
        {
            var existing = judge.Find(BeatRingName);
            if (existing != null)
                return existing;
            var ringObject = new GameObject(BeatRingName, typeof(RectTransform));
            var rect = (RectTransform)ringObject.transform;
            rect.SetParent(judge, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.SetAsFirstSibling();
            var ring = ringObject.AddComponent<RunnerRing>();
            ring.raycastTarget = false;
            ring.SetDiameter(300, 2, new Color(Paper.r, Paper.g, Paper.b, 0.45f));
            return rect;
        }

        // Full-screen white image, invisible until the flash feedback raises its alpha.
        static Transform EnsureFlashImage(Transform canvas)
        {
            var existing = canvas.Find(BoostFlashName);
            if (existing != null)
                return existing;
            var flashObject = new GameObject(BoostFlashName, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rect = (RectTransform)flashObject.transform;
            rect.SetParent(canvas, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();
            var image = flashObject.GetComponent<UnityEngine.UI.Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;
            // Skip drawing the full-screen quad while it is transparent (fill rate on mobile web).
            image.canvasRenderer.cullTransparentMesh = true;
            return rect;
        }

        // Adds a delayed damage trail behind the HP fill and lets Feel's MMProgressBar drive both.
        // Existing bar settings are kept so Inspector tuning survives a re-install.
        static MMProgressBar InstallHealthBar(UnityEngine.UI.Image fill)
        {
            var housing = fill.transform.parent;
            var trailTransform = housing.Find(HealthTrailName);
            UnityEngine.UI.Image trail;
            if (trailTransform == null)
            {
                var trailObject = new GameObject(HealthTrailName, typeof(RectTransform), typeof(UnityEngine.UI.Image));
                trailObject.transform.SetParent(housing, false);
                // Same parent, earlier sibling: drawn behind the fill.
                trailObject.transform.SetSiblingIndex(fill.transform.GetSiblingIndex());
                trail = trailObject.GetComponent<UnityEngine.UI.Image>();
                trail.raycastTarget = false;
                trail.color = HealthTrailColor;
            }
            else
            {
                trail = trailTransform.GetComponent<UnityEngine.UI.Image>();
            }
            CopyFillLayout(fill, trail);

            var bar = housing.GetComponent<MMProgressBar>();
            if (bar == null)
            {
                bar = housing.gameObject.AddComponent<MMProgressBar>();
                bar.FillMode = MMProgressBar.FillModes.FillAmount;
                bar.BarDirection = MMProgressBar.BarDirections.LeftToRight;
                bar.TimeScale = MMProgressBar.TimeScales.UnscaledTime;
                bar.BarFillMode = MMProgressBar.BarFillModes.FixedDuration;
                // HP itself updates instantly as before; only the trail lags behind.
                bar.LerpForegroundBar = false;
                // Short delay: the next successful hit (+1 HP) resets the trail anyway.
                bar.DecreasingDelay = 0.2f;
                bar.LerpDecreasingDelayedBar = true;
                bar.LerpDecreasingDelayedBarDuration = 0.3f;
                // The HP squash already comes from FB_Runner_HpLoss.
                bar.BumpScaleOnChange = false;
                bar.BumpOnIncrease = false;
                bar.BumpOnDecrease = false;
                bar.ChangeColorWhenBumping = false;
            }
            bar.ForegroundBar = fill.transform;
            bar.DelayedBarDecreasing = trail.transform;
            EditorUtility.SetDirty(bar);
            return bar;
        }

        static void CopyFillLayout(UnityEngine.UI.Image from, UnityEngine.UI.Image to)
        {
            var source = from.rectTransform;
            var target = to.rectTransform;
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition;
            to.sprite = from.sprite;
            to.type = from.type;
            to.fillMethod = from.fillMethod;
            to.fillOrigin = from.fillOrigin;
            to.fillAmount = from.fillAmount;
            EditorUtility.SetDirty(to);
        }

        static void EnsurePrefabs()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/Afterecho/Runner", "Feedbacks");
            foreach (var spec in Specs)
            {
                string path = PrefabPath(spec.Name);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    continue;
                var root = new GameObject(spec.Name);
                try
                {
                    ConfigurePlayer(root.AddComponent<MMF_Player>(), spec);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            foreach (var variant in Variants)
            {
                string path = PrefabPath(variant.Name);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    continue;
                var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(variant.BaseName));
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                try
                {
                    instance.name = variant.Name;
                    var serialized = new SerializedObject(instance.GetComponent<MMF_Player>());
                    ForEachFeedback(serialized, feedback =>
                    {
                        SetFloat(feedback, "RemapCurveOne", variant.Peak);
                        SetString(feedback, "Label", variant.Name);
                    });
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    // Saving a prefab instance as a new asset creates a Prefab Variant of its base.
                    PrefabUtility.SaveAsPrefabAsset(instance, path);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        static void ConfigurePlayer(MMF_Player player, PrefabSpec spec)
        {
            player.InitializationMode = MMFeedbacks.InitializationModes.Awake;
            player.AutoPlayOnStart = false;
            player.AutoPlayOnEnable = false;
            player.CanPlayWhileAlreadyPlaying = true;
            player.CooldownDuration = 0;
            player.StopFeedbacksOnDisable = true;
            player.RestoreInitialValuesOnDisable = true;
            // Targets are scene objects, so each scene instance overrides the target field.
            player.AddFeedback(spec.Create(spec.Name));
        }

        static MMFeedbackTiming UnscaledTiming()
        {
            return new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled, CooldownDuration = 0 };
        }

        static AnimationCurve PopCurve()
        {
            return new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 1), new Keyframe(1, 0));
        }

        // Damped back-and-forth that ends at rest, shaped like DOTween's punch (vibrato 3).
        static AnimationCurve PunchCurve()
        {
            return new AnimationCurve(
                new Keyframe(0, 0), new Keyframe(0.1f, 1), new Keyframe(0.3f, -0.67f), new Keyframe(0.5f, 0.44f),
                new Keyframe(0.7f, -0.22f), new Keyframe(0.85f, 0.07f), new Keyframe(1, 0));
        }

        static MMF_Feedback Squash(string label, float duration, float peak)
        {
            return new MMF_SquashAndStretch
            {
                Label = label,
                Mode = MMF_SquashAndStretch.Modes.Absolute,
                Axis = MMF_SquashAndStretch.PossibleAxis.YtoX,
                AnimateScaleDuration = duration,
                RemapCurveZero = 1,
                RemapCurveOne = peak,
                AnimateCurve = PopCurve(),
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        static MMF_Feedback Pop(string label, float duration, float peak)
        {
            return new MMF_Scale
            {
                Label = label,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = 1,
                RemapCurveOne = peak,
                UniformScaling = true,
                AnimateScaleTweenX = new MMTweenType(PopCurve()),
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // Rotates around local Z from the rest pose and back; Absolute mode re-applies from the
        // initial rotation every frame, so restarts never stack angles.
        static MMF_Feedback PunchZ(string label, float duration, float degrees)
        {
            return new MMF_Rotation
            {
                Label = label,
                Mode = MMF_Rotation.Modes.Absolute,
                RotationSpace = Space.Self,
                AnimateRotationDuration = duration,
                AnimateX = false,
                AnimateY = false,
                AnimateZ = true,
                AnimateRotationTweenZ = new MMTweenType(PunchCurve(), "AnimateZ"),
                RemapCurveZero = 0,
                RemapCurveOne = degrees,
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // Two clockwise drops with a rebound in between, then back to rest (fraction of the angle).
        static AnimationCurve NodCurve()
        {
            return new AnimationCurve(
                new Keyframe(0, 0), new Keyframe(0.1f, 1), new Keyframe(0.25f, 0.4f), new Keyframe(0.4f, 0.9f),
                new Keyframe(0.58f, 0.45f), new Keyframe(0.78f, 0.15f), new Keyframe(1, 0));
        }

        static MMF_Feedback Nod(string label, float duration, float degrees)
        {
            return new MMF_Rotation
            {
                Label = label,
                Mode = MMF_Rotation.Modes.Absolute,
                RotationSpace = Space.Self,
                AnimateRotationDuration = duration,
                AnimateX = false,
                AnimateY = false,
                AnimateZ = true,
                AnimateRotationTweenZ = new MMTweenType(NodCurve(), "AnimateZ"),
                RemapCurveZero = 0,
                RemapCurveOne = degrees,
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // "Ba-dum": a strong thump, a short dip, a second slightly weaker thump, then rest.
        static MMF_Feedback Heartbeat(string label, float duration, float peak)
        {
            return new MMF_Scale
            {
                Label = label,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = 1,
                RemapCurveOne = peak,
                UniformScaling = true,
                AnimateScaleTweenX = new MMTweenType(new AnimationCurve(
                    new Keyframe(0, 0), new Keyframe(0.08f, 1), new Keyframe(0.2f, 0.25f), new Keyframe(0.32f, 0.85f),
                    new Keyframe(0.5f, 0.1f), new Keyframe(1, 0)), "AnimateX"),
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // Uniform scale with a fast attack and a slower release, for beat pulses.
        static MMF_Feedback Pulse(string label, float duration, float peak)
        {
            return new MMF_Scale
            {
                Label = label,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = 1,
                RemapCurveOne = peak,
                UniformScaling = true,
                AnimateScaleTweenX = new MMTweenType(
                    new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.1f, 1), new Keyframe(1, 0)), "AnimateX"),
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // Tints a TextMeshPro text towards color and back to its own colour.
        static MMF_Feedback TextColorFlash(string label, float duration, Color color)
        {
            return new MMF_TMPColor
            {
                Label = label,
                ColorMode = MMF_TMPColor.ColorModes.Interpolate,
                Duration = duration,
                DestinationColor = color,
                ColorCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.15f, 1), new Keyframe(1, 0)),
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // Raises a UI image's alpha to peakAlpha and back.
        static MMF_Feedback Flash(string label, float duration, float peakAlpha)
        {
            return new MMF_ImageAlpha
            {
                Label = label,
                Mode = MMF_ImageAlpha.Modes.OverTime,
                Duration = duration,
                Curve = new MMTweenType(new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.15f, 1), new Keyframe(1, 0))),
                CurveRemapZero = 0,
                CurveRemapOne = peakAlpha,
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }

        // Scales a panel in from startScale to 1 with a small overshoot; ends exactly at 1.
        static MMF_Feedback PopIn(string label, float duration, float startScale)
        {
            return new MMF_Scale
            {
                Label = label,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = startScale,
                RemapCurveOne = 1,
                UniformScaling = true,
                AnimateScaleTweenX = new MMTweenType(
                    new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.6f, 1.1f), new Keyframe(1, 1)), "AnimateX"),
                AllowAdditivePlays = false,
                Timing = UnscaledTiming(),
            };
        }


        static MMF_Player PlaceInstance(Transform container, string prefabName, Transform target)
        {
            var existing = container.Find(prefabName);
            GameObject instance;
            if (existing != null)
            {
                instance = existing.gameObject;
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabName));
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
                instance.name = prefabName;
            }

            var player = instance.GetComponent<MMF_Player>();
            var serialized = new SerializedObject(player);
            ForEachFeedback(serialized, feedback =>
            {
                foreach (string name in TargetProperties)
                {
                    var targetProperty = feedback.FindPropertyRelative(name);
                    if (targetProperty != null)
                        targetProperty.objectReferenceValue = TargetValue(name, target);
                }
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        // Feel feedbacks reference their targets with different types.
        static Object TargetValue(string property, Transform target)
        {
            switch (property)
            {
                case "AnimatePositionTarget":
                    return target.gameObject;
                case "BoundImage":
                    return target.GetComponent<UnityEngine.UI.Image>();
                case "TargetTMPText":
                    return target.GetComponent<TMP_Text>();
                default:
                    return target;
            }
        }

        static void ForEachFeedback(SerializedObject player, Action<SerializedProperty> action)
        {
            var feedbacks = player.FindProperty("FeedbacksList");
            for (int i = 0; i < feedbacks.arraySize; i++)
                action(feedbacks.GetArrayElementAtIndex(i));
        }

        static void SetFloat(SerializedProperty feedback, string name, float value)
        {
            var property = feedback.FindPropertyRelative(name);
            if (property != null)
                property.floatValue = value;
        }

        static void SetString(SerializedProperty feedback, string name, string value)
        {
            var property = feedback.FindPropertyRelative(name);
            if (property != null)
                property.stringValue = value;
        }

        static void SetBindings(RunnerFeedbackPlayer player, params (FeedbackId id, FeedbackChannel channel, MMF_Player feel)[] bindings)
        {
            var serialized = new SerializedObject(player);
            var array = serialized.FindProperty("bindings");
            array.arraySize = bindings.Length;
            for (int i = 0; i < bindings.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").intValue = (int)bindings[i].id;
                element.FindPropertyRelative("channel").intValue = (int)bindings[i].channel;
                element.FindPropertyRelative("player").objectReferenceValue = bindings[i].feel;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

        readonly struct PrefabSpec
        {
            public readonly string Name;
            public readonly Func<string, MMF_Feedback> Create;

            public PrefabSpec(string name, Func<string, MMF_Feedback> create)
            {
                Name = name;
                Create = create;
            }
        }

        readonly struct VariantSpec
        {
            public readonly string Name;
            public readonly string BaseName;
            public readonly float Peak;

            public VariantSpec(string name, string baseName, float peak)
            {
                Name = name;
                BaseName = baseName;
                Peak = peak;
            }
        }
    }
}
