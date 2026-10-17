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
        const string HealthTrailName = "HpDelayed";
        static readonly Color HealthTrailColor = new Color(1f, 0.97f, 0.88f, 0.9f);

        // The first four use the values RunnerView.Feel() used, so the migration does not change the feel.
        static readonly PrefabSpec[] Specs =
        {
            new PrefabSpec(HitPrefab, 0.115f, 1.16f, EffectKind.SquashAndStretch),
            new PrefabSpec(HpLossPrefab, 0.17f, 1.10f, EffectKind.SquashAndStretch),
            new PrefabSpec(BoostStartPrefab, 0.18f, 1.22f, EffectKind.SquashAndStretch),
            new PrefabSpec(FallPrefab, 0.15f, 0.8f, EffectKind.SquashAndStretch),
            // Uniform pop for PERFECT / GOOD / MISS / OFF BEAT text; shorter than the 0.17 s minimum note gap.
            new PrefabSpec(JudgementTextPrefab, 0.12f, 1.4f, EffectKind.Scale),
        };

        enum EffectKind { SquashAndStretch, Scale }

        // Scene-object fields each effect kind animates; scene instances override them.
        static readonly string[] TargetProperties = { "SquashAndStretchTarget", "AnimateScaleTarget" };

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

            var player = view.GetComponent<RunnerFeedbackPlayer>();
            if (player == null)
                player = view.gameObject.AddComponent<RunnerFeedbackPlayer>();
            SetBindings(player,
                (FeedbackId.HitGood, hit),
                (FeedbackId.HitPerfect, hitPerfect),
                (FeedbackId.HpLoss, hpLoss),
                (FeedbackId.Fall, fall),
                (FeedbackId.BoostStart, boostStart),
                (FeedbackId.JudgementText, judgementText));

            view.feedback = player;
            view.hpBar = InstallHealthBar(view.hpFill);
            EditorUtility.SetDirty(view);
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
            player.AddFeedback(CreateFeedback(spec));
        }

        static MMF_Feedback CreateFeedback(PrefabSpec spec)
        {
            var pop = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 1), new Keyframe(1, 0));
            var timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled, CooldownDuration = 0 };
            switch (spec.Kind)
            {
                case EffectKind.Scale:
                    return new MMF_Scale
                    {
                        Label = spec.Name,
                        Mode = MMF_Scale.Modes.Absolute,
                        AnimateScaleDuration = spec.Duration,
                        RemapCurveZero = 1,
                        RemapCurveOne = spec.Peak,
                        UniformScaling = true,
                        AnimateScaleTweenX = new MMTweenType(pop),
                        AllowAdditivePlays = false,
                        Timing = timing,
                    };
                default:
                    return new MMF_SquashAndStretch
                    {
                        Label = spec.Name,
                        Mode = MMF_SquashAndStretch.Modes.Absolute,
                        Axis = MMF_SquashAndStretch.PossibleAxis.YtoX,
                        AnimateScaleDuration = spec.Duration,
                        RemapCurveZero = 1,
                        RemapCurveOne = spec.Peak,
                        AnimateCurve = pop,
                        AllowAdditivePlays = false,
                        Timing = timing,
                    };
            }
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
                        targetProperty.objectReferenceValue = target;
                }
            });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return player;
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

        static void SetBindings(RunnerFeedbackPlayer player, params (FeedbackId id, MMF_Player feel)[] bindings)
        {
            var serialized = new SerializedObject(player);
            var array = serialized.FindProperty("bindings");
            array.arraySize = bindings.Length;
            for (int i = 0; i < bindings.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").intValue = (int)bindings[i].id;
                element.FindPropertyRelative("channel").intValue = (int)FeedbackChannel.Motion;
                element.FindPropertyRelative("player").objectReferenceValue = bindings[i].feel;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static string PrefabPath(string name) => PrefabFolder + "/" + name + ".prefab";

        readonly struct PrefabSpec
        {
            public readonly string Name;
            public readonly float Duration;
            public readonly float Peak;
            public readonly EffectKind Kind;

            public PrefabSpec(string name, float duration, float peak, EffectKind kind)
            {
                Name = name;
                Duration = duration;
                Peak = peak;
                Kind = kind;
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
