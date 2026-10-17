using System;
using Afterecho.Feedback;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Afterecho.Editor
{
    // Places the runner's feedback prefabs in the scene and binds them to RunnerFeedbackPlayer.
    // Idempotent: RunnerSceneBuilder calls it for new scenes, and the menu item updates an
    // existing scene without rebuilding it. Prefabs are created only when missing, so values
    // tuned in the Inspector are never overwritten.
    public static class RunnerFeedbackInstaller
    {
        public const string PrefabFolder = "Assets/Afterecho/Runner/Feedbacks";
        const string ContainerName = "Feedbacks";
        const string HitPrefab = "FB_Runner_Hit";
        const string HpLossPrefab = "FB_Runner_HpLoss";
        const string BoostStartPrefab = "FB_Runner_BoostStart";
        const string FallPrefab = "FB_Runner_Fall";

        // Same values RunnerView.Feel() used, so the migration does not change the feel.
        static readonly PrefabSpec[] Specs =
        {
            new PrefabSpec(HitPrefab, 0.115f, 1.16f),
            new PrefabSpec(HpLossPrefab, 0.17f, 1.10f),
            new PrefabSpec(BoostStartPrefab, 0.18f, 1.22f),
            new PrefabSpec(FallPrefab, 0.15f, 0.8f),
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
            var hpLoss = PlaceInstance(container, HpLossPrefab, view.healthRoot);
            var boostStart = PlaceInstance(container, BoostStartPrefab, view.runnerVisual);
            var fall = PlaceInstance(container, FallPrefab, view.runnerVisual);

            var player = view.GetComponent<RunnerFeedbackPlayer>();
            if (player == null)
                player = view.gameObject.AddComponent<RunnerFeedbackPlayer>();
            SetBindings(player,
                (FeedbackId.HitGood, hit),
                (FeedbackId.HitPerfect, hit),
                (FeedbackId.HpLoss, hpLoss),
                (FeedbackId.Fall, fall),
                (FeedbackId.BoostStart, boostStart));

            view.feedback = player;
            EditorUtility.SetDirty(view);
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
            // The target is a scene object, so each scene instance overrides SquashAndStretchTarget.
            player.AddFeedback(new MMF_SquashAndStretch
            {
                Label = spec.Name,
                Mode = MMF_SquashAndStretch.Modes.Absolute,
                Axis = MMF_SquashAndStretch.PossibleAxis.YtoX,
                AnimateScaleDuration = spec.Duration,
                RemapCurveZero = 1,
                RemapCurveOne = spec.Peak,
                AnimateCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 1), new Keyframe(1, 0)),
                AllowAdditivePlays = false,
                Timing = new MMFeedbackTiming { TimescaleMode = TimescaleModes.Unscaled, CooldownDuration = 0 },
            });
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
            var feedbacks = serialized.FindProperty("FeedbacksList");
            for (int i = 0; i < feedbacks.arraySize; i++)
            {
                var targetProperty = feedbacks.GetArrayElementAtIndex(i).FindPropertyRelative("SquashAndStretchTarget");
                if (targetProperty != null)
                    targetProperty.objectReferenceValue = target;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return player;
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

            public PrefabSpec(string name, float duration, float peak)
            {
                Name = name;
                Duration = duration;
                Peak = peak;
            }
        }
    }
}
