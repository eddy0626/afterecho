using System;
using UnityEngine;
using UnityEditor;

namespace Afterecho.Editor
{
    public sealed class AfterechoFeedbackTestPanel : EditorWindow
    {
        [MenuItem("Afterecho/Feedback Test Panel", false, 10)]
        public static void Open()
        {
            var window = GetWindow<AfterechoFeedbackTestPanel>("Feedback Test");
            window.minSize = new Vector2(360, 560);
            window.Show();
        }

        Vector2 scrollPos;
        static string lastEventDisplay = "None";

        void OnEnable()
        {
            EditorApplication.update += RepaintIfPlaying;
        }

        void OnDisable()
        {
            EditorApplication.update -= RepaintIfPlaying;
        }

        void RepaintIfPlaying()
        {
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        void OnGUI()
        {
            var game = UnityEngine.Object.FindFirstObjectByType<AfterechoGame>();
            var view = UnityEngine.Object.FindFirstObjectByType<AfterechoView>();
            var impact = UnityEngine.Object.FindFirstObjectByType<AfterechoImpactFeedback>();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("AFTERECHO Manual Control & Feedback", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Direct UI controls & Feel 6.1 telemetry", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            if (impact == null || view == null || game == null)
            {
                EditorGUILayout.HelpBox("Stage01_BlackCorridor scene components not found in active scene.\nPlease open Stage01_BlackCorridor.unity.", MessageType.Warning);
                if (GUILayout.Button("Open Stage01 Scene"))
                {
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Afterecho/Scenes/Stage01_BlackCorridor.unity");
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            // 1. Live Runtime Snapshot Telemetry
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Live Snapshot & Game State", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Phase", Application.isPlaying ? $"{game.Phase}" : "EditMode (Not Playing)");
            EditorGUILayout.LabelField("Song Time", Application.isPlaying ? $"{game.LogicalTime:F3}s" : "0.000s");
            
            if (Application.isPlaying && game.Run != null)
            {
                var run = game.Run;
                EditorGUILayout.LabelField("Health", $"{run.Health} / {run.Chart.rules.health}");
                EditorGUILayout.LabelField("Score / Multiplier", $"{run.Score:N0}  (×{run.Multiplier})");
                EditorGUILayout.LabelField("Hits / Misses / Extras", $"{run.Hits + run.PracticeHits} (Practice: {run.PracticeHits}) / {run.Misses} / {run.Extras}");
                EditorGUILayout.LabelField("Combo (Best)", $"{run.Combo}  (Best: {run.Best})");
                EditorGUILayout.LabelField("Kills / Steps", $"{run.Kills} / 6  (Steps: {run.Steps})");
            }
            else
            {
                EditorGUILayout.LabelField("Status", "Standby / Menu");
            }

            EditorGUILayout.LabelField("Last Event", lastEventDisplay);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 2. Manual Gameplay Flow Controls (Invoking existing game/view methods directly)
            EditorGUILayout.LabelField("Manual Gameplay Controls (Clickable)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: StartGame & BeginMain
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Game", GUILayout.Height(28)))
            {
                game.StartGame();
                lastEventDisplay = "StartGame invoked";
            }

            GUI.enabled = Application.isPlaying && game.Phase == GamePhase.Ready;
            if (GUILayout.Button("Begin Main (Ready)", GUILayout.Height(28)))
            {
                game.BeginMain();
                lastEventDisplay = "BeginMain invoked";
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Row 2: Pause / Resume & Menu
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(game.Phase == GamePhase.Paused ? "Resume Game (4-beat)" : "Pause Game", GUILayout.Height(26)))
            {
                if (game.Phase == GamePhase.Paused)
                {
                    game.ResumeGame();
                    lastEventDisplay = "ResumeGame invoked";
                }
                else
                {
                    game.PauseGame();
                    lastEventDisplay = "PauseGame invoked";
                }
            }

            if (GUILayout.Button("Main Menu", GUILayout.Height(26)))
            {
                game.Menu();
                lastEventDisplay = "Menu invoked";
            }
            EditorGUILayout.EndHorizontal();

            // Row 3: Difficulty Selectors
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Difficulty");
            string[] diffs = { "easy", "normal", "hard" };
            string[] diffLabels = { "입문", "보통", "도전" };
            for (int i = 0; i < diffs.Length; i++)
            {
                bool isSelected = game.difficulty == diffs[i];
                GUI.backgroundColor = isSelected ? new Color(0.4f, 0.85f, 0.75f) : Color.white;
                if (GUILayout.Button(diffLabels[i], GUILayout.Height(22)))
                {
                    game.SelectDifficulty(diffs[i]);
                    lastEventDisplay = $"Difficulty set: {diffs[i]}";
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 4: LabSeek to First Combat Encounter (21.86s)
            EditorGUILayout.Space(2);
            if (GUILayout.Button("Seek to First Combat Encounter (21.86s)", GUILayout.Height(26)))
            {
                game.LabSeek(21.86);
                lastEventDisplay = "LabSeek(21.86s) to first combat";
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 3. Transient FX & Feel Audio Previews
            EditorGUILayout.LabelField("Transient FX & Feel Audio Previews", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("★ Preview Normal Hit (Spark + Tick Sound)", GUILayout.Height(28)))
            {
                EnsureInitialized(game, view, impact);
                impact.PlayPreview(ImpactType.Normal);
                lastEventDisplay = "Preview: Normal Hit";
            }

            if (GUILayout.Button("✦ Preview Perfect Hit (Star/Ring + Full Sound)", GUILayout.Height(28)))
            {
                EnsureInitialized(game, view, impact);
                impact.PlayPreview(ImpactType.Perfect);
                lastEventDisplay = "Preview: Perfect Hit";
            }

            if (GUILayout.Button("⚔ Preview Attack Hit (Slash + Enemy Flash)", GUILayout.Height(28)))
            {
                EnsureInitialized(game, view, impact);
                impact.PlayPreview(ImpactType.Attack);
                lastEventDisplay = "Preview: Attack Hit";
            }

            if (GUILayout.Button("💥 Preview Kill Hit (Break Burst + Break Sound)", GUILayout.Height(28)))
            {
                EnsureInitialized(game, view, impact);
                impact.PlayPreview(ImpactType.Kill);
                lastEventDisplay = "Preview: Kill Hit";
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 4. Live Controls & Settings
            EditorGUILayout.LabelField("Live Controls & Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            bool autoPlay = EditorGUILayout.Toggle("Auto Play", game.autoPlay);
            bool invincible = EditorGUILayout.Toggle("Invincible", game.invincible);
            bool reducedMotion = EditorGUILayout.Toggle("Reduced Motion", game.reducedMotion);
            float sfxGain = EditorGUILayout.Slider("SFX Gain", impact.sfxGain, 0f, 2f);
            float volume = EditorGUILayout.Slider("Master Volume", game.volume, 0f, 1f);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new UnityEngine.Object[] { game, impact }, "Change Feedback Settings");
                game.autoPlay = autoPlay;
                game.invincible = invincible;
                game.reducedMotion = reducedMotion;
                impact.sfxGain = sfxGain;
                game.SetVolume(volume);
                EditorUtility.SetDirty(game);
                EditorUtility.SetDirty(impact);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Stop All Active Feedbacks", GUILayout.Height(24)))
            {
                impact.StopAllFeedbacks();
                lastEventDisplay = "Feedbacks Stopped";
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        void EnsureInitialized(AfterechoGame game, AfterechoView view, AfterechoImpactFeedback impact)
        {
            if (!Application.isPlaying)
            {
                impact.Initialize(game, view);
            }
        }
    }
}

