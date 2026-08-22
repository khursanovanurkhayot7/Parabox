#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    /// <summary>
    /// Runtime-safety audit for the shipped game. Unlike the curriculum/design validator, this
    /// reports only defects that can break compilation, scene boot, UI, tutorials or a level win.
    /// It is read-only and never enters Play Mode or rewrites a campaign prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class ParaboxWholeGameHealthCheck
    {
        const int LevelCount = 50;
        const string LevelFolder = "Assets/Parabox/Prefabs/Levels";
        const string MainMenuScene = "Assets/Parabox/Scenes/MainMenu.unity";
        const string GameScene = "Assets/Parabox/Scenes/Game.unity";
        const string RequestName = "ParaboxWholeGameHealth.request";
        const string ResultName = "ParaboxWholeGameHealth.txt";
        static double nextPoll;

        static ParaboxWholeGameHealthCheck()
        {
            EditorApplication.update -= PollRequest;
            EditorApplication.update += PollRequest;
        }

        [MenuItem("Tools/Parabox/Validation/Check Whole Game Health (No Play Mode)", priority = 250)]
        public static void RunFromMenu()
        {
            HealthResult result = Check();
            WriteResult(result);
            if (result.failures.Count == 0)
            {
                Debug.Log(result.report);
                EditorUtility.DisplayDialog(
                    "Parabox whole-game health",
                    "PASS: compile-time assets, scenes, UI, tutorials and all 50 stored winning "
                    + "routes are healthy. No Play Mode or level rebuild was used.",
                    "OK");
            }
            else
            {
                // A completed audit with reported defects is an expected diagnostic result, not
                // an Editor exception. Keep the exact FAIL list in the report without adding a
                // misleading red Console entry of our own.
                Debug.LogWarning(result.report);
                EditorUtility.DisplayDialog(
                    "Parabox health check found real errors",
                    $"{result.failures.Count} runtime-blocking problem(s) remain. See the Console "
                    + "or Library/ParaboxWholeGameHealth.txt.",
                    "OK");
            }
        }

        [MenuItem("Tools/Parabox/Validation/Check Whole Game Health (No Play Mode)", true)]
        static bool CanRunFromMenu()
            => !Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode
               && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        static void PollRequest()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5d;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            string request = Path.Combine(ProjectRoot, "Library", RequestName);
            if (!File.Exists(request)) return;
            File.Delete(request);
            HealthResult result = Check();
            WriteResult(result);
            if (result.failures.Count == 0) Debug.Log(result.report);
            else Debug.LogWarning(result.report);
        }

        public static void CheckFromCommandLine()
        {
            HealthResult result = Check();
            WriteResult(result);
            if (result.failures.Count > 0)
                throw new InvalidOperationException(result.report);
            Debug.Log(result.report);
        }

        sealed class HealthResult
        {
            public readonly List<string> failures = new List<string>();
            public string report;
        }

        static HealthResult Check()
        {
            var result = new HealthResult();
            var report = new StringBuilder(8192);
            report.AppendLine("PARABOX WHOLE-GAME RUNTIME HEALTH");
            report.AppendLine("=================================");
            report.AppendLine("Mode: Edit Mode only; no gameplay, solver or level regeneration.");
            report.AppendLine();

            ValidateBuildScenes(result.failures);
            ValidateRequiredResources(result.failures);

            var prefabs = new GameObject[LevelCount];
            for (int index = 0; index < LevelCount; index++)
            {
                int number = index + 1;
                string path = $"{LevelFolder}/Level_{number}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                prefabs[index] = prefab;
                if (prefab == null)
                {
                    result.failures.Add($"L{number:00}: missing prefab at {path}");
                    continue;
                }

                ValidateNoMissingScripts(prefab, $"L{number:00}", result.failures);
                ParaboxLevel info = prefab.GetComponent<ParaboxLevel>();
                if (info == null)
                {
                    result.failures.Add($"L{number:00}: missing ParaboxLevel metadata");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(info.solution))
                {
                    result.failures.Add($"L{number:00} {info.levelName}: stored solution is empty");
                    continue;
                }
                if (info.par != info.solution.Length)
                    result.failures.Add(
                        $"L{number:00} {info.levelName}: par {info.par} does not match proof length "
                        + info.solution.Length);

                if (!RouteWins(prefab, info.solution, out string routeError))
                    result.failures.Add($"L{number:00} {info.levelName}: {routeError}");
                else
                    report.AppendLine($"PASS L{number:00} {info.levelName}: {info.solution.Length} moves");
            }

            ValidateTutorials(prefabs, result.failures);
            try
            {
                ParaboxPrebuiltUiGenerator.ValidateSilent();
                report.AppendLine("PASS MainMenu/Game prebuilt UI, controller path and scene scripts");
            }
            catch (Exception exception)
            {
                result.failures.Add("Scene/UI validation: " + exception.Message);
            }

            report.AppendLine();
            if (result.failures.Count == 0)
                report.AppendLine("RESULT: PASS - no runtime-blocking project errors found.");
            else
            {
                report.AppendLine($"RESULT: FAIL - {result.failures.Count} real error(s).");
                foreach (string failure in result.failures) report.AppendLine("FAIL " + failure);
                report.AppendLine();
                report.AppendLine("For stale/empty routes, run: Tools > Parabox > Validation > "
                    + "Repair All 50 Solutions (Keep Levels Unchanged), then run this check again.");
            }

            result.report = report.ToString();
            return result;
        }

        static void ValidateBuildScenes(List<string> failures)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length != 2
                || !scenes[0].enabled || scenes[0].path != MainMenuScene
                || !scenes[1].enabled || scenes[1].path != GameScene)
                failures.Add("Build Settings must contain enabled MainMenu then Game scenes.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScene) == null)
                failures.Add("MainMenu scene asset is missing.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) == null)
                failures.Add("Game scene asset is missing.");
        }

        static void ValidateRequiredResources(List<string> failures)
        {
            if (Resources.Load<LuxoddParaboxSettings>("LuxoddParaboxSettings") == null)
                failures.Add("Resources/LuxoddParaboxSettings is missing.");
            if (Resources.Load<Shader>("Shaders/MenuArtworkBlend") == null)
                failures.Add("MenuArtworkBlend resource shader is missing.");
            if (Resources.Load<Shader>("Shaders/LockedNodeSoftBlur") == null)
                failures.Add("LockedNodeSoftBlur resource shader is missing.");
            if (Resources.Load<Sprite>("UI/LevelMapOption2Base") == null)
                failures.Add("LevelMapOption2Base resource sprite is missing.");
        }

        static void ValidateTutorials(GameObject[] prefabs, List<string> failures)
        {
            if (prefabs == null || prefabs.Length != LevelCount) return;
            for (int index = 0; index < prefabs.Length; index++)
            {
                if (prefabs[index] == null) continue;
                foreach (MechanicCatalog.Id lesson in MechanicCatalog.TutorialsAt(prefabs, index))
                    if (!TutorialPuzzleLibrary.Exists(index, lesson))
                        failures.Add(
                            $"L{index + 1:00}: missing tutorial prefab "
                            + TutorialPuzzleLibrary.ResourcePath(index, lesson));
            }
        }

        static void ValidateNoMissingScripts(GameObject prefab, string label, List<string> failures)
        {
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                if (count > 0)
                    failures.Add($"{label}: {child.name} has {count} missing script reference(s)");
            }
        }

        static bool RouteWins(GameObject prefab, string route, out string error)
        {
            LevelModel model;
            try { model = LevelParser.Parse(prefab); }
            catch (Exception exception)
            {
                error = "parse failed: " + exception.Message;
                return false;
            }
            if (model == null || model.player == null)
            {
                error = "parsed model has no player";
                return false;
            }
            for (int step = 0; step < route.Length; step++)
            {
                if (!TryDirection(route[step], out Vector2Int direction))
                {
                    error = $"invalid proof command '{route[step]}' at move {step + 1}";
                    return false;
                }
                if (!model.TryMovePlayer(direction))
                {
                    error = $"proof blocks at move {step + 1} ({route[step]})";
                    return false;
                }
            }
            if (!model.IsWon())
            {
                error = "proof ends without satisfying every target";
                return false;
            }
            int moveLimit = GameManager.MoveLimitForLevel(LevelNumber(prefab.name) - 1,
                prefab.GetComponent<ParaboxLevel>().par);
            if (model.MoveCount > moveLimit)
            {
                error = $"proof needs {model.MoveCount} moves above limit {moveLimit}";
                return false;
            }
            error = string.Empty;
            return true;
        }

        static int LevelNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return 1;
            int end = name.Length - 1;
            while (end >= 0 && char.IsDigit(name[end])) end--;
            return end < name.Length - 1
                   && int.TryParse(name.Substring(end + 1), out int number) ? number : 1;
        }

        static bool TryDirection(char code, out Vector2Int direction)
        {
            switch (code)
            {
                case 'U': direction = Vector2Int.up; return true;
                case 'R': direction = Vector2Int.right; return true;
                case 'D': direction = Vector2Int.down; return true;
                case 'L': direction = Vector2Int.left; return true;
                default: direction = default; return false;
            }
        }

        static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;

        static void WriteResult(HealthResult result)
        {
            if (string.IsNullOrEmpty(ProjectRoot)) return;
            File.WriteAllText(Path.Combine(ProjectRoot, "Library", ResultName), result.report);
        }
    }
}
#endif
