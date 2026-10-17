using System;
using Afterecho.Feedback;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
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
        const string HpShakePrefab = "FB_Runner_HpShake";
        const string BoostFlashPrefab = "FB_Runner_BoostFlash";
        const string BoostFlashName = "BoostFlash";
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
            // Left-right shake of the HP gauge, like Feel's MMProgressBar demo (which shakes vertically).
            new PrefabSpec(HpShakePrefab, name => ShakeX(name, 0.25f, 7f)),
            // One full-screen flash per OVERDRIVE start (rare, far below 3 flashes per second).
            new PrefabSpec(BoostFlashPrefab, name => Flash(name, 0.3f, 0.45f)),
        };

        // Scene-object fields the feedbacks animate; scene instances override them.
        static readonly string[] TargetProperties =
        {
            "SquashAndStretchTarget", "AnimateScaleTarget", "AnimatePositionTarget", "AnimateRotationTarget", "BoundImage",
        };

        // Variants inherit everything from their base prefab and override only the peak,
        // so tuning the base (duration, curve) carries over.
        static readonly VariantSpec[] Variants =
        {
            new VariantSpec(HitPerfectPrefab, HitPrefab, 1.25f),
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
            var hpShake = PlaceInstance(container, HpShakePrefab, view.hpFill.transform.parent);
            var boostFlash = PlaceInstance(container, BoostFlashPrefab, EnsureFlashImage(view.canvas.transform));

            var player = view.GetComponent<RunnerFeedbackPlayer>();
            if (player == null)
                player = view.gameObject.AddComponent<RunnerFeedbackPlayer>();
            SetBindings(player,
                (FeedbackId.HitGood, FeedbackChannel.Motion, hit),
                (FeedbackId.HitPerfect, FeedbackChannel.Motion, hitPerfect),
                (FeedbackId.HpLoss, FeedbackChannel.Motion, hpLoss),
                (FeedbackId.HpLoss, FeedbackChannel.Motion, hpShake),
                (FeedbackId.Fall, FeedbackChannel.Motion, fall),
                (FeedbackId.BoostStart, FeedbackChannel.Motion, boostStart),
                (FeedbackId.BoostStart, FeedbackChannel.Flash, boostFlash),
                (FeedbackId.JudgementText, FeedbackChannel.Motion, judgementText));

            view.feedback = player;
            view.hpBar = InstallHealthBar(view.hpFill);
            EditorUtility.SetDirty(view);
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

        // Decaying left-right oscillation that ends where it started.
        static AnimationCurve ShakeCurve()
        {
            return new AnimationCurve(
                new Keyframe(0, 0), new Keyframe(0.1f, 1), new Keyframe(0.3f, -0.8f), new Keyframe(0.5f, 0.55f),
                new Keyframe(0.7f, -0.3f), new Keyframe(0.85f, 0.12f), new Keyframe(1, 0));
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

        // Moves a UI element left and right around its anchored position (amplitude in canvas units).
        static MMF_Feedback ShakeX(string label, float duration, float amplitude)
        {
            return new MMF_Position
            {
                Label = label,
                Mode = MMF_Position.Modes.AlongCurve,
                Space = MMF_Position.Spaces.RectTransform,
                RelativePosition = true,
                AnimatePositionDuration = duration,
                AnimateX = true,
                AnimateY = false,
                AnimateZ = false,
                AnimatePositionTweenX = new MMTweenType(ShakeCurve()),
                RemapCurveZero = 0,
                RemapCurveOne = amplitude,
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
