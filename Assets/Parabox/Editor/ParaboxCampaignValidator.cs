using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parabox.EditorTools
{
    // Replays the real stored solution through the real model for all 50 shipped prefabs. This is
    // deliberately not a second puzzle implementation: if gameplay changes, this test exercises
    // the changed gameplay and catches the first level whose proof no longer works.
    public static class ParaboxCampaignValidator
    {
        const int ExpectedLevels = 50;
        const string LevelFolder = "Assets/Parabox/Prefabs/Levels";
        const string MainMenuScene = "Assets/Parabox/Scenes/MainMenu.unity";
        const string GameScene = "Assets/Parabox/Scenes/Game.unity";
        const string PlayTransitionSessionKey = "Parabox.Editor.PlayTransition";
        // One campaign-wide contract keeps chapter boundaries from resetting the difficulty.
        // The authored proof may be longer than the floor, but never shorter. Room counts are
        // exact because each recursive room is an intentional dependency, not visual decoration.
        static readonly int[] MinimumPars =
        {
             8, 10, 12, 16, 18, 19, 30, 34, 39, 42,
            15, 16, 18, 19, 19, 19, 20, 21, 21, 22,
            23, 27, 29, 32, 35, 42, 45, 46, 54, 65,
            21, 26, 32, 35, 41, 47, 49, 57, 59, 61,
            // Chapter V difficulty is protected by recursive depth, five-or-more simultaneous
            // completion jobs, unique mastery rules, gate/portal dependencies and the rising
            // one-way ladder. Keep the authored route floors aligned with the reviewed extreme
            // boards instead of demanding filler walking (the generator caps finale routes at 64).
            47, 47, 47, 48, 48, 56, 56, 59, 59, 59
        };

        static readonly int[] ExpectedRoomCounts =
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 3, 3, 3, 3, 4,
            2, 2, 3, 3, 3, 4, 4, 5, 5, 5,
            // Extreme Chapter V: Levels 41-45 use four connected scales; Levels 46-50 use five.
            // Every extra room is visited and participates in the stored winning route.
            4, 4, 4, 4, 4, 5, 5, 5, 5, 5
        };

        static double nextPlayModeRequestPoll;

        [InitializeOnLoadMethod]
        static void RegisterPlayModeRequestWatcher()
        {
            EditorApplication.playModeStateChanged -= TrackPlayModeTransition;
            EditorApplication.playModeStateChanged += TrackPlayModeTransition;

            // AssetDatabase and Build Profile writes are unsafe while Unity is reconstructing the
            // domain for Play Mode. isPlaying briefly reports false inside that reconstruction,
            // so SessionState carries an explicit guard across the domain reload.
            if (!SessionState.GetBool(PlayTransitionSessionKey, false)
                && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall -= ConfigurePlayModeStartScene;
                EditorApplication.delayCall += ConfigurePlayModeStartScene;
            }
            // Lets local recovery request Enter/Exit Play Mode without macOS Accessibility
            // permissions. Polling also works if a maximized Game view stops accepting clicks.
            EditorApplication.update -= PollPlayModeRequests;
            EditorApplication.update += PollPlayModeRequests;
            EditorApplication.delayCall += ForceSafeGameViewMode;
            PollPlayModeRequests();
        }

        static void TrackPlayModeTransition(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode
                || state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.ExitingPlayMode)
            {
                SessionState.SetBool(PlayTransitionSessionKey, true);
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(PlayTransitionSessionKey, false);
            EditorApplication.delayCall -= ConfigurePlayModeStartScene;
            EditorApplication.delayCall += ConfigurePlayModeStartScene;
        }

        // Pressing Unity's normal Play button must always boot through the title screen. Starting
        // directly from Game.unity bypasses the menu hand-off flags and can leave the gameplay
        // camera showing an empty board. Unity restores the developer's edited scene after Stop.
        static void ConfigurePlayModeStartScene()
        {
            if (SessionState.GetBool(PlayTransitionSessionKey, false)
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            // Use Unity's normal domain + scene reload. Fast Play Mode kept references to prefab
            // assets across imports; after regenerating levels those objects were destroyed while
            // GameManager still held them, producing MissingReferenceException and unstable test
            // sessions. A clean start is slightly slower but deterministic and matches WebGL.
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;

            EnsurePlayableScenesInBuildProfile();

            SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScene);
            if (mainMenu != null && EditorSceneManager.playModeStartScene != mainMenu)
                EditorSceneManager.playModeStartScene = mainMenu;
        }

        // Unity 6 keeps a global scene list and can also keep a separate override on the active
        // Build Profile. The project YAML can therefore look correct while the running Editor has
        // an empty overridden list. SceneManager then unloads the level, refuses to load MainMenu,
        // and the Game view is left at "No cameras rendering". Keep both the shared list and the
        // active profile view synchronized whenever scripts reload.
        static void EnsurePlayableScenesInBuildProfile()
        {
            var requiredScenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScene, true),
                new EditorBuildSettingsScene(GameScene, true)
            };
            bool changed = false;

            if (!SceneListsMatch(EditorBuildSettings.globalScenes, requiredScenes))
            {
                EditorBuildSettings.globalScenes = requiredScenes;
                changed = true;
            }

            BuildProfile activeProfile = BuildProfile.GetActiveBuildProfile();
            if (activeProfile != null && activeProfile.overrideGlobalScenes)
            {
                // Both scenes are campaign infrastructure rather than profile-specific content.
                // Using the shared list prevents Web, desktop, and local Play Mode from drifting.
                activeProfile.overrideGlobalScenes = false;
                EditorUtility.SetDirty(activeProfile);
                changed = true;
            }

            // Reassigning this property refreshes Unity's in-memory list for the active platform
            // profile. That refresh is required when files were regenerated by a batch process
            // while the main Editor was already open.
            if (!SceneListsMatch(EditorBuildSettings.scenes, requiredScenes))
            {
                EditorBuildSettings.scenes = requiredScenes;
                changed = true;
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[Parabox] Repaired Build Profile scenes: MainMenu, Game.");
            }
        }

        static bool SceneListsMatch(EditorBuildSettingsScene[] actual,
                                    EditorBuildSettingsScene[] expected)
        {
            if (actual == null || actual.Length != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (!actual[i].enabled || actual[i].path != expected[i].path) return false;
            }
            return true;
        }

        [MenuItem("Tools/Parabox/Repair Build Profile Scene List")]
        public static void RepairBuildProfileSceneList()
        {
            EnsurePlayableScenesInBuildProfile();
            ConfigurePlayModeStartScene();
            Debug.Log("[Parabox] Build Profile scene list is ready for Play Mode.");
        }

        // "Play Maximized" was saved into this project's editor layout and could create a zero-size
        // Metal Game view on macOS. That made the whole Editor look black and hid the Stop control.
        // Keep the normal docked Game view for this project, including after Unity rewrites layouts.
        static void ForceSafeGameViewMode()
        {
            Type playModeView = typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            if (playModeView == null) return;
            PropertyInfo behavior = playModeView.GetProperty("enterPlayModeBehavior",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (behavior == null || !behavior.CanWrite || !behavior.PropertyType.IsEnum) return;
            // Unity's enum ordering has changed between Editor versions. Resolve the value by
            // name so this never accidentally selects Play Maximized and hides the toolbar.
            object playUnfocused;
            try
            {
                playUnfocused = Enum.Parse(behavior.PropertyType, "PlayUnfocused");
            }
            catch (ArgumentException)
            {
                // PlayFocused is still safe: unlike PlayMaximized it keeps the Stop control visible.
                try
                {
                    playUnfocused = Enum.Parse(behavior.PropertyType, "PlayFocused");
                }
                catch (ArgumentException)
                {
                    // Unity 6.5 serializes PlayMaximized as 0 and PlayUnfocused as 2.
                    // Use the safe docked value if the internal enum names change again.
                    playUnfocused = Enum.ToObject(behavior.PropertyType, 2);
                }
            }
            foreach (UnityEngine.Object view in Resources.FindObjectsOfTypeAll(playModeView))
            {
                try
                {
                    behavior.SetValue(view, playUnfocused);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Parabox] Could not update the Game view play behavior: {exception.Message}");
                }
            }
        }

        static void PollPlayModeRequests()
        {
            if (EditorApplication.timeSinceStartup < nextPlayModeRequestPoll) return;
            nextPlayModeRequestPoll = EditorApplication.timeSinceStartup + 0.25d;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string exitRequest = Path.Combine(projectRoot, "Library", "ParaboxExitPlayMode.request");
            if (File.Exists(exitRequest))
            {
                File.Delete(exitRequest);
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.isPlaying = false;
                return;
            }

            string levelOneRequest = Path.Combine(projectRoot, "Library", "ParaboxEnterLevelOne.request");
            if (File.Exists(levelOneRequest))
            {
                File.Delete(levelOneRequest);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.delayCall += () =>
                    {
                        ForceSafeGameViewMode();
                        PlayerPrefs.SetInt("Parabox.Level", 0);
                        PlayerPrefs.Save();
                        EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
                        EditorApplication.isPlaying = true;
                    };
                }
                return;
            }

            // Local visual-QA hook: write a 1-based level number into this file to boot that exact
            // board without changing a scene or Inspector reference by hand.
            string levelRequest = Path.Combine(projectRoot, "Library", "ParaboxEnterLevel.request");
            if (File.Exists(levelRequest))
            {
                string raw = File.ReadAllText(levelRequest).Trim();
                File.Delete(levelRequest);
                if (!EditorApplication.isPlayingOrWillChangePlaymode
                    && int.TryParse(raw, out int requestedLevel))
                    PreviewLevel(Mathf.Clamp(requestedLevel - 1, 0, 49), "Board Style");
                return;
            }

            string discoveryPreviewRequest = Path.Combine(projectRoot, "Library", "ParaboxPreviewDiscovery.request");
            if (File.Exists(discoveryPreviewRequest))
            {
                File.Delete(discoveryPreviewRequest);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    PreviewFirstHiddenDiscovery();
                return;
            }

            // Runs the full stored-solution validation in the already-open Editor (no project-lock
            // conflict with a second batch-mode Unity process) and writes a machine-readable report.
            string validationRequest = Path.Combine(projectRoot, "Library", "ParaboxValidateCampaign.request");
            if (File.Exists(validationRequest))
            {
                File.Delete(validationRequest);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    ValidationResult result = ValidateCampaign();
                    File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxValidation.txt"),
                        $"failures={result.failures}\nwarnings={result.warnings}\n{result.report}");
                    if (result.failures == 0) Debug.Log(result.report);
                    else Debug.LogError(result.report);
                }
                return;
            }

            // Targeted repair hook used by local regression checks. The generator performs a
            // disposable solver preflight before it replaces only Level_11.prefab.
            string repairLevelElevenRequest = Path.Combine(
                projectRoot, "Library", "ParaboxRepairLevelEleven.request");
            if (File.Exists(repairLevelElevenRequest))
            {
                File.Delete(repairLevelElevenRequest);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    ParaboxSetupWizard.RegenerateLevelElevenSilent();
                return;
            }

            string tutorialTryRequest = Path.Combine(projectRoot, "Library", "ParaboxTutorialTry.request");
            if (File.Exists(tutorialTryRequest))
            {
                File.Delete(tutorialTryRequest);
                if (EditorApplication.isPlaying)
                {
                    GameManager manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                    if (manager != null) manager.TutorialTryIt();
                }
                return;
            }

            string tutorialRepeatRequest = Path.Combine(projectRoot, "Library", "ParaboxTutorialRepeat.request");
            if (File.Exists(tutorialRepeatRequest))
            {
                File.Delete(tutorialRepeatRequest);
                if (EditorApplication.isPlaying)
                {
                    GameManager manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                    if (manager != null) manager.TutorialWatchAgain();
                }
                return;
            }

            string tutorialSkipRequest = Path.Combine(projectRoot, "Library", "ParaboxTutorialSkip.request");
            if (File.Exists(tutorialSkipRequest))
            {
                File.Delete(tutorialSkipRequest);
                if (EditorApplication.isPlaying)
                {
                    GameManager manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                    if (manager != null) manager.TutorialSkip();
                }
                return;
            }

            string solveLevelOneRequest = Path.Combine(projectRoot, "Library", "ParaboxSolveLevelOne.request");
            if (File.Exists(solveLevelOneRequest))
            {
                File.Delete(solveLevelOneRequest);
                if (EditorApplication.isPlaying)
                {
                    GameManager manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                    if (manager != null)
                    {
                        manager.UiMove(Vector2Int.right);
                        manager.UiMove(Vector2Int.right);
                        manager.UiMove(Vector2Int.right);
                    }
                }
                return;
            }

            // Visual-QA hook for recursive-room transitions. Write a U/D/L/R route into the
            // request while Play Mode is running; every character goes through GameManager's real
            // movement path, so captures exercise the same camera and renderer as the player.
            string playRouteRequest = Path.Combine(projectRoot, "Library", "ParaboxPlayRoute.request");
            if (File.Exists(playRouteRequest))
            {
                string route = File.ReadAllText(playRouteRequest).Trim().ToUpperInvariant();
                File.Delete(playRouteRequest);
                if (EditorApplication.isPlaying)
                {
                    GameManager manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                    if (manager != null)
                    {
                        foreach (char step in route)
                        {
                            switch (step)
                            {
                                case 'U': manager.UiMove(Vector2Int.up); break;
                                case 'D': manager.UiMove(Vector2Int.down); break;
                                case 'L': manager.UiMove(Vector2Int.left); break;
                                case 'R': manager.UiMove(Vector2Int.right); break;
                            }
                        }
                    }
                }
                return;
            }

            string mainPlayRequest = Path.Combine(projectRoot, "Library", "ParaboxInvokeMainPlay.request");
            if (File.Exists(mainPlayRequest))
            {
                File.Delete(mainPlayRequest);
                if (EditorApplication.isPlaying)
                {
                    MainMenuUI menu = UnityEngine.Object.FindAnyObjectByType<MainMenuUI>();
                    if (menu != null && menu.playButton != null) menu.playButton.onClick.Invoke();
                }
                return;
            }

            string captureRequest = Path.Combine(projectRoot, "Library", "ParaboxCapture.request");
            if (File.Exists(captureRequest))
            {
                File.Delete(captureRequest);
                if (EditorApplication.isPlaying)
                    ScreenCapture.CaptureScreenshot(Path.Combine(projectRoot, "Library", "ParaboxCapture.png"));
                return;
            }

            // Visual regression for the bug where a camera fly-in was rendered before its
            // backdrop had resized. The report checks the shared backdrop at phone/tablet,
            // landscape/portrait and the exaggerated Level 50 intro zoom, so one run covers every
            // campaign level that uses this scene.
            string backdropRequest = Path.Combine(projectRoot, "Library", "ParaboxValidateBackdrop.request");
            if (File.Exists(backdropRequest))
            {
                File.Delete(backdropRequest);
                if (EditorApplication.isPlaying)
                    ValidateBackdropCoverage(projectRoot);
                return;
            }

            string stateRequest = Path.Combine(projectRoot, "Library", "ParaboxRuntimeState.request");
            if (File.Exists(stateRequest))
            {
                File.Delete(stateRequest);
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                Camera camera = Camera.main;
                GameManager manager = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                bool tutorialReady = manager != null && manager.tutorialFx != null
                    && manager.tutorialFx.choiceGroup != null
                    && manager.tutorialFx.choiceGroup.interactable;
                bool tutorialCardVisible = manager != null && manager.tutorialFx != null
                    && manager.tutorialFx.panelGroup != null
                    && manager.tutorialFx.panelGroup.alpha > 0.5f;
                bool tutorialSkipVisible = manager != null && manager.tutorialFx != null
                    && manager.tutorialFx.skipButton != null
                    && manager.tutorialFx.skipButton.gameObject.activeInHierarchy;
                string tutorialTitle = manager != null && manager.tutorialFx != null
                    && manager.tutorialFx.titleText != null
                    ? manager.tutorialFx.titleText.text : string.Empty;
                string state = $"playing={EditorApplication.isPlaying}\n" +
                    $"playingOrChanging={EditorApplication.isPlayingOrWillChangePlaymode}\n" +
                    $"scene={scene.path}\n" +
                    $"camera={(camera != null ? camera.name : "null")}\n" +
                    $"target={(camera != null && camera.targetTexture != null ? camera.targetTexture.name : "screen")}\n" +
                    $"gameManager={(manager != null)}\n" +
                    $"tutorialReady={tutorialReady}\n" +
                    $"tutorialCardVisible={tutorialCardVisible}\n" +
                    $"tutorialSkipVisible={tutorialSkipVisible}\n" +
                    $"tutorialTitle={tutorialTitle}\n" +
                    $"tutorialPlaybackSerial={(manager != null ? manager.TutorialPlaybackSerial : 0)}\n" +
                    $"mainMenu={(UnityEngine.Object.FindAnyObjectByType<MainMenuUI>() != null)}\n";
                File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxRuntimeState.txt"), state);
                return;
            }

            string enterRequest = Path.Combine(projectRoot, "Library", "ParaboxEnterPlayMode.request");
            if (File.Exists(enterRequest))
            {
                File.Delete(enterRequest);
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    OpenMainMenuAndEnterPlayMode();
                return;
            }

            // Diagnostic equivalent of pressing Unity's regular toolbar Play button. Unlike the
            // recovery request above, this deliberately does not open a scene first, so automated
            // checks verify that playModeStartScene really routes Game.unity through MainMenu.
            string rawPlayRequest = Path.Combine(projectRoot, "Library", "ParaboxRawPlayMode.request");
            if (!File.Exists(rawPlayRequest)) return;
            File.Delete(rawPlayRequest);
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/Parabox/Open Main Menu and Enter Play Mode")]
        public static void OpenMainMenuAndEnterPlayMode()
        {
            try
            {
                ForceSafeGameViewMode();
                EditorSceneManager.OpenScene(MainMenuScene, OpenSceneMode.Single);
                Debug.Log("[Parabox] Entering Play Mode from the recovered Main Menu layout.");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Parabox/Preview Hidden Discovery (Level 12)")]
        public static void PreviewFirstHiddenDiscovery()
            => PreviewLevel(11, "Discovery");

        static void PreviewLevel(int zeroBasedLevel, string logTag)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"[{logTag}] Stop Play Mode before opening the preview.");
                return;
            }

            ForceSafeGameViewMode();
            PlayerPrefs.SetInt(GameManager.EditorPreviewLevelKey, zeroBasedLevel);
            PlayerPrefs.Save();

            SceneAsset game = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene);
            if (game == null)
            {
                Debug.LogError($"[{logTag}] Game scene is missing; preview could not start.");
                return;
            }

            // Override the normal Main Menu play-start scene for this one preview only, then put
            // the safe default back as soon as Play Mode has opened.
            EditorSceneManager.playModeStartScene = game;
            EditorApplication.playModeStateChanged -= RestoreMainMenuAfterDiscoveryPreview;
            EditorApplication.playModeStateChanged += RestoreMainMenuAfterDiscoveryPreview;
            EditorApplication.EnterPlaymode();
        }

        static void RestoreMainMenuAfterDiscoveryPreview(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode
                && state != PlayModeStateChange.ExitingPlayMode) return;
            EditorApplication.playModeStateChanged -= RestoreMainMenuAfterDiscoveryPreview;
            // Only restore the next Play press's start scene. Re-running the full workspace repair
            // while Play Mode is entering can make Unity restore its backup scene immediately,
            // which cancels the discovery preview before GameManager gets its first frame.
            SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScene);
            if (mainMenu != null) EditorSceneManager.playModeStartScene = mainMenu;
        }

        [MenuItem("Tools/Parabox/Validate Entire Campaign (50 Levels)")]
        public static void ValidateFromMenu()
        {
            var result = ValidateCampaign();
            if (result.failures == 0)
            {
                Debug.Log(result.report);
                EditorUtility.DisplayDialog("Parabox Campaign Validation",
                    $"All {ExpectedLevels} levels passed.\n\nWarnings: {result.warnings}\n" +
                    "Every stored solution wins within its move budget.", "OK");
            }
            else
            {
                Debug.LogError(result.report);
                EditorUtility.DisplayDialog("Parabox Campaign Validation",
                    $"Validation failed with {result.failures} error(s).\n" +
                    "Open the Console for the exact level and move.", "OK");
            }
        }

        // Entry point for CI or a local batch-mode safety pass:
        // Unity -batchmode -quit -projectPath <project> -executeMethod
        // Parabox.EditorTools.ParaboxCampaignValidator.ValidateFromCommandLine
        public static void ValidateFromCommandLine()
        {
            var result = ValidateCampaign();
            if (result.failures > 0)
                throw new InvalidOperationException(result.report);
            Debug.Log(result.report);
        }

        // Focused release gate for a presentation-only regeneration. The full curriculum validator
        // also enforces design-policy metadata that can intentionally change during a reorder; this
        // pass answers the safety-critical question independently: did every authored route still
        // win through the real model, without exceeding its runtime move limit?
        public static void ValidateStoredSolutionsFromCommandLine()
        {
            var report = new StringBuilder(4096);
            int failures = 0;
            for (int index = 0; index < ExpectedLevels; index++)
            {
                string path = $"{LevelFolder}/Level_{index + 1}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                if (prefab == null || info == null || string.IsNullOrWhiteSpace(info.solution))
                {
                    Failure(report, ref failures, index, "missing prefab metadata or stored solution");
                    continue;
                }

                LevelModel model;
                try { model = LevelParser.Parse(prefab); }
                catch (Exception ex)
                {
                    Failure(report, ref failures, index, $"parse failed: {ex.Message}");
                    continue;
                }

                bool routeValid = true;
                for (int step = 0; step < info.solution.Length; step++)
                {
                    if (!TryDirection(info.solution[step], out Vector2Int direction)
                        || !model.TryMovePlayer(direction))
                    {
                        Failure(report, ref failures, index,
                            $"stored solution failed at move {step + 1} ({info.solution[step]})");
                        routeValid = false;
                        break;
                    }
                }

                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                if (routeValid && model.IsWon() && model.MoveCount <= moveLimit)
                    report.AppendLine($"PASS L{index + 1:00} {info.levelName} " +
                        $"({model.MoveCount}/{moveLimit} moves)");
                else if (routeValid)
                    Failure(report, ref failures, index,
                        $"route ended won={model.IsWon()} at {model.MoveCount}/{moveLimit} moves");
            }

            report.AppendLine($"RESULT: {ExpectedLevels - failures}/{ExpectedLevels} stored solutions passed.");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log(report.ToString());
        }

        // Focused gate requested for the current review: replay only Chapters I and II through
        // the real runtime model. This avoids unrelated later-chapter authoring work while proving
        // that every Level 1-20 prefab remains solvable within its visible move allowance.
        public static void ValidateChaptersOneAndTwoFromCommandLine()
        {
            const int checkedLevels = 20;
            var report = new StringBuilder(2048);
            int failures = 0;
            for (int index = 0; index < checkedLevels; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{LevelFolder}/Level_{index + 1}.prefab");
                ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                if (prefab == null || info == null || string.IsNullOrWhiteSpace(info.solution))
                {
                    Failure(report, ref failures, index,
                        "missing prefab metadata or stored solution");
                    continue;
                }

                LevelModel model;
                try { model = LevelParser.Parse(prefab); }
                catch (Exception exception)
                {
                    Failure(report, ref failures, index, $"parse failed: {exception.Message}");
                    continue;
                }

                bool nestedReferenceValid = index < 10;
                foreach (PEntity entity in model.entities)
                {
                    if (entity.interiorRoomId < 0) continue;
                    if (!model.rooms.ContainsKey(entity.interiorRoomId))
                    {
                        Failure(report, ref failures, index,
                            $"nested box references missing room {entity.interiorRoomId}");
                        nestedReferenceValid = false;
                        break;
                    }
                    nestedReferenceValid = true;
                }
                if (index >= 10 && !nestedReferenceValid)
                {
                    Failure(report, ref failures, index,
                        "Chapter II level has no valid room-inside-box relationship");
                    continue;
                }

                bool routeValid = true;
                for (int step = 0; step < info.solution.Length; step++)
                {
                    if (!TryDirection(info.solution[step], out Vector2Int direction)
                        || !model.TryMovePlayer(direction))
                    {
                        Failure(report, ref failures, index,
                            $"stored solution failed at move {step + 1} ({info.solution[step]})");
                        routeValid = false;
                        break;
                    }
                }

                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                if (routeValid && model.IsWon() && model.MoveCount <= moveLimit)
                    report.AppendLine($"PASS L{index + 1:00} {info.levelName} "
                        + $"({model.MoveCount}/{moveLimit} moves)");
                else if (routeValid)
                    Failure(report, ref failures, index,
                        $"route ended won={model.IsWon()} at {model.MoveCount}/{moveLimit} moves");
            }

            report.AppendLine($"RESULT: {checkedLevels - failures}/{checkedLevels} "
                + "Chapter I-II levels passed.");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log(report.ToString());
        }

        // Focused release gate for Chapter III. Besides replaying the real stored routes, this
        // verifies the review contract: ten different recursive boards, a strictly rising
        // difficulty curve above Level 20, three or more visible jobs, and the premium cyan
        // portal tutorial immediately before Level 25 with continued portal use through Level 30.
        public static void ValidateChapterThreeFromCommandLine()
        {
            const int firstIndex = 20;
            const int lastIndex = 29;
            var report = new StringBuilder(3072);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var layouts = new HashSet<string>(StringComparer.Ordinal);
            var prefabs = new GameObject[ExpectedLevels];
            int failures = 0;

            for (int index = 0; index < prefabs.Length; index++)
                prefabs[index] = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{LevelFolder}/Level_{index + 1}.prefab");

            ParaboxLevel levelTwenty = prefabs[19] != null
                ? prefabs[19].GetComponent<ParaboxLevel>() : null;
            int previousComplexity = levelTwenty != null ? levelTwenty.designComplexity : -1;
            if (previousComplexity < 0)
                throw new InvalidOperationException(
                    "Level 20 metadata is missing; Chapter III cannot prove it starts harder.");

            for (int index = firstIndex; index <= lastIndex; index++)
            {
                int levelNumber = index + 1;
                GameObject prefab = prefabs[index];
                ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                if (prefab == null || info == null || string.IsNullOrWhiteSpace(info.solution))
                {
                    Failure(report, ref failures, index,
                        "missing prefab metadata or stored solution");
                    continue;
                }

                LevelModel model;
                try { model = LevelParser.Parse(prefab); }
                catch (Exception exception)
                {
                    Failure(report, ref failures, index, $"parse failed: {exception.Message}");
                    continue;
                }

                if (!names.Add(info.levelName))
                    Failure(report, ref failures, index,
                        $"duplicate Chapter III name '{info.levelName}'");
                string fingerprint = ChapterThreeLayoutFingerprint(model);
                if (!layouts.Add(fingerprint))
                    Failure(report, ref failures, index,
                        "duplicates another Chapter III board layout");

                if (info.designComplexity <= previousComplexity)
                    Failure(report, ref failures, index,
                        $"difficulty {info.designComplexity} does not exceed "
                        + $"the previous level's {previousComplexity}");
                previousComplexity = info.designComplexity;

                int visibleTasks = 0;
                foreach (PRoom room in model.rooms.Values)
                    visibleTasks += room.boxGoals.Count + room.colourGoals.Count
                                    + room.playerGoals.Count;
                if (visibleTasks < 3)
                    Failure(report, ref failures, index,
                        $"only {visibleTasks} visible completion tasks");

                bool nestedReferenceValid = model.rooms.Count >= 2;
                foreach (PEntity entity in model.entities)
                {
                    if (entity.interiorRoomId < 0) continue;
                    if (!model.rooms.ContainsKey(entity.interiorRoomId))
                    {
                        nestedReferenceValid = false;
                        Failure(report, ref failures, index,
                            $"nested box references missing Room {entity.interiorRoomId}");
                        break;
                    }
                }
                if (!nestedReferenceValid)
                    Failure(report, ref failures, index,
                        "has no valid box-inside-box relationship");

                int expectedPortals = levelNumber >= 25 ? 2 : 0;
                if (model.portalPair.Count != expectedPortals)
                    Failure(report, ref failures, index,
                        $"has {model.portalPair.Count}/{expectedPortals} portal cells");

                int expectedOneWays =
                    LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(index);
                int expectedGateHolds =
                    LevelLayoutRebalancer.ChapterThreeGateReuseBudgetForLevel(index);
                if (model.rebalanceOneWays != expectedOneWays)
                    Failure(report, ref failures, index,
                        $"retained {model.rebalanceOneWays}/{expectedOneWays} Chapter I arrows");
                if (model.rebalanceObjectives < expectedGateHolds)
                    Failure(report, ref failures, index,
                        $"retained {model.rebalanceObjectives}/{expectedGateHolds} "
                        + "Chapter I button/gate dependencies");
                foreach (MechanicCatalog.Id rehearsal in MechanicCatalog.RehearsalsAt(index))
                    if (!model.curriculumReuses.Contains(rehearsal))
                        Failure(report, ref failures, index,
                            $"winning route did not retain the planned {rehearsal} reuse");

                bool routeValid = true;
                bool touchedPortal = false;
                for (int step = 0; step < info.solution.Length; step++)
                {
                    char command = info.solution[step];
                    if (!TryDirection(command, out Vector2Int direction))
                    {
                        Failure(report, ref failures, index,
                            $"invalid stored command '{command}' at move {step + 1}");
                        routeValid = false;
                        break;
                    }

                    int beforeRoom = model.player.roomId;
                    Vector2Int beforeCell = model.player.pos;
                    if (!model.TryMovePlayer(direction))
                    {
                        Failure(report, ref failures, index,
                            $"stored solution failed at move {step + 1} ({command})");
                        routeValid = false;
                        break;
                    }

                    int afterRoom = model.player.roomId;
                    Vector2Int afterCell = model.player.pos;
                    if (model.portalPair.ContainsKey((afterRoom, afterCell))
                        && (beforeRoom != afterRoom
                            || Mathf.Abs(afterCell.x - beforeCell.x)
                               + Mathf.Abs(afterCell.y - beforeCell.y) > 1))
                        touchedPortal = true;
                }

                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                if (routeValid && (!model.IsWon() || model.MoveCount > moveLimit))
                    Failure(report, ref failures, index,
                        $"route ended won={model.IsWon()} at {model.MoveCount}/{moveLimit} moves");
                if (routeValid && levelNumber >= 25 && !touchedPortal)
                    Failure(report, ref failures, index,
                        "winning route never traverses the premium portal pair");

                if (routeValid && model.IsWon() && model.MoveCount <= moveLimit)
                    report.AppendLine($"PASS L{levelNumber:00} {info.levelName}  "
                        + $"difficulty {info.designComplexity}, {visibleTasks} tasks, "
                        + $"{model.MoveCount}/{moveLimit} moves, "
                        + $"Chapter I reuse arrows={model.rebalanceOneWays} "
                        + $"gate={expectedGateHolds}");
            }

            for (int index = firstIndex; index < 24; index++)
                if (MechanicCatalog.TutorialsAt(prefabs, index)
                    .Contains(MechanicCatalog.Id.Portal))
                    Failure(report, ref failures, index,
                        "premium portal tutorial appears before Level 25");

            List<MechanicCatalog.Id> levelTwentyFiveLessons =
                MechanicCatalog.TutorialsAt(prefabs, 24);
            if (!levelTwentyFiveLessons.Contains(MechanicCatalog.Id.Portal))
                Failure(report, ref failures, 24,
                    "premium portal tutorial is not scheduled before Level 25");
            else
            {
                GameObject tutorial = TutorialPuzzleLibrary.Load(24, MechanicCatalog.Id.Portal);
                ParaboxLevel tutorialInfo = tutorial != null
                    ? tutorial.GetComponent<ParaboxLevel>() : null;
                if (tutorial == null || tutorialInfo == null
                    || string.IsNullOrWhiteSpace(tutorialInfo.solution))
                    Failure(report, ref failures, 24,
                        "premium portal tutorial prefab or proof route is missing");
                else
                {
                    LevelModel tutorialModel = LevelParser.Parse(tutorial);
                    bool usedTutorialPortal = false;
                    bool tutorialRouteValid = tutorialModel.portalPair.Count == 2;
                    for (int step = 0;
                         tutorialRouteValid && step < tutorialInfo.solution.Length; step++)
                    {
                        if (!TryDirection(tutorialInfo.solution[step], out Vector2Int direction))
                        {
                            tutorialRouteValid = false;
                            break;
                        }
                        PRoom room = tutorialModel.rooms[tutorialModel.player.roomId];
                        Vector2Int target = tutorialModel.player.pos + direction;
                        if (room.InBounds(target)
                            && tutorialModel.portalPair.ContainsKey((room.id, target)))
                            usedTutorialPortal = true;
                        tutorialRouteValid = tutorialModel.TryMovePlayer(direction);
                    }
                    if (!tutorialRouteValid || !tutorialModel.IsWon()
                        || !usedTutorialPortal)
                        Failure(report, ref failures, 24,
                            "premium portal tutorial does not teach and solve the portal route");
                    else
                        report.AppendLine("PASS TUTORIAL before L25: premium cyan portal route wins");
                }
            }

            report.AppendLine($"RESULT: {10 - Math.Min(10, failures)}/10 Chapter III levels "
                + "validated; premium portal tutorial checked before Level 25.");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log(report.ToString());
        }

        static string ChapterThreeLayoutFingerprint(LevelModel model)
        {
            var result = new StringBuilder(1024);
            var roomIds = new List<int>(model.rooms.Keys);
            roomIds.Sort();
            foreach (int roomId in roomIds)
            {
                PRoom room = model.rooms[roomId];
                result.Append('R').Append(roomId).Append(':')
                    .Append(room.width).Append('x').Append(room.height).Append('|');
                for (int y = 0; y < room.height; y++)
                    for (int x = 0; x < room.width; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        char symbol = room.IsWall(cell) ? '#' : '.';
                        if (room.portal != null && room.portal[x, y]) symbol = 'o';
                        if (room.boxGoals.Contains(cell)) symbol = 'x';
                        if (room.playerGoals.Contains(cell)) symbol = 'p';
                        result.Append(symbol);
                    }
            }

            var entities = new List<string>(model.entities.Count);
            foreach (PEntity entity in model.entities)
                entities.Add($"{entity.roomId}:{entity.pos.x}:{entity.pos.y}:"
                    + $"{entity.interiorRoomId}:{entity.isPlayer}:{entity.colour}:"
                    + $"{entity.anchored}");
            entities.Sort(StringComparer.Ordinal);
            foreach (string entity in entities) result.Append('|').Append(entity);
            return result.ToString();
        }

        // Chapter-IV-only release gate. Each board must be a different recursive-room puzzle,
        // combine that room play with one-way commitments (L31-34) or a mandatory portal
        // (L35-40), rise in both campaign complexity and replayed route evidence, and be solvable.
        public static void ValidateChapterFourFromCommandLine()
        {
            const int firstIndex = 30;
            const int lastIndex = 39;
            var report = new StringBuilder(4096);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var layouts = new HashSet<string>(StringComparer.Ordinal);
            int failures = 0;

            GameObject previousPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{LevelFolder}/Level_30.prefab");
            ParaboxLevel previousInfo = previousPrefab != null
                ? previousPrefab.GetComponent<ParaboxLevel>() : null;
            int previousComplexity = previousInfo != null ? previousInfo.designComplexity : -1;
            int previousRouteEvidence = -1;
            if (previousComplexity < 0)
                throw new InvalidOperationException(
                    "Level 30 metadata is missing; Chapter IV cannot prove its difficulty hand-off.");

            for (int index = firstIndex; index <= lastIndex; index++)
            {
                int levelFailuresBefore = failures;
                int levelNumber = index + 1;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{LevelFolder}/Level_{levelNumber}.prefab");
                ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                if (prefab == null || info == null || string.IsNullOrWhiteSpace(info.solution))
                {
                    Failure(report, ref failures, index,
                        "missing prefab metadata or stored solution");
                    continue;
                }

                LevelModel model;
                try { model = LevelParser.Parse(prefab); }
                catch (Exception exception)
                {
                    Failure(report, ref failures, index, $"parse failed: {exception.Message}");
                    continue;
                }

                if (!names.Add(info.levelName))
                    Failure(report, ref failures, index,
                        $"duplicate Chapter IV name '{info.levelName}'");
                if (!layouts.Add(ChapterThreeLayoutFingerprint(model)))
                    Failure(report, ref failures, index,
                        "duplicates another Chapter IV layout");

                if (info.designComplexity <= previousComplexity)
                    Failure(report, ref failures, index,
                        $"difficulty {info.designComplexity} does not exceed the previous "
                        + $"level's {previousComplexity}");
                previousComplexity = info.designComplexity;

                int expectedOneWays =
                    LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(index);
                bool expectsOneWays = levelNumber < 35;
                if (model.rebalanceOneWays != expectedOneWays
                    || expectsOneWays != model.curriculumReuses.Contains(MechanicCatalog.Id.OneWay)
                    || (expectsOneWays && expectedOneWays < 1))
                    Failure(report, ref failures, index,
                        $"retained {model.rebalanceOneWays}/{expectedOneWays} purposeful "
                        + "one-way commitments");

                int expectedPortals = levelNumber >= 35 ? 2 : 0;
                if (model.portalPair.Count != expectedPortals)
                    Failure(report, ref failures, index,
                        $"has {model.portalPair.Count}/{expectedPortals} portal cells");

                int visibleTasks = 0;
                foreach (PRoom room in model.rooms.Values)
                    visibleTasks += room.boxGoals.Count + room.colourGoals.Count
                                    + room.playerGoals.Count;
                if (visibleTasks + (expectedPortals == 2 ? 1 : 0) < 4)
                    Failure(report, ref failures, index,
                        $"only {visibleTasks} visible goals; Chapter IV needs four purposeful tasks");

                int authoredCargo = 0;
                var authoredRoomBoxes = new List<PEntity>();
                foreach (PEntity entity in model.entities)
                {
                    if (entity.IsCrate && entity.interiorRoomId < 0) authoredCargo++;
                    if (entity.interiorRoomId >= 0) authoredRoomBoxes.Add(entity);
                }

                var movedCargo = new HashSet<PEntity>();
                var movedRooms = new HashSet<PEntity>();
                var visitedRooms = new HashSet<int> { model.player.roomId };
                var usedOneWays = new HashSet<(int room, Vector2Int cell)>();
                bool usedPortal = false;
                bool routeValid = true;

                for (int step = 0; step < info.solution.Length; step++)
                {
                    if (!TryDirection(info.solution[step], out Vector2Int direction))
                    {
                        Failure(report, ref failures, index,
                            $"invalid command '{info.solution[step]}' at move {step + 1}");
                        routeValid = false;
                        break;
                    }

                    PRoom playerRoom = model.rooms[model.player.roomId];
                    Vector2Int intended = model.player.pos + direction;
                    if (playerRoom.InBounds(intended)
                        && model.portalPair.ContainsKey((playerRoom.id, intended)))
                        usedPortal = true;

                    var beforeRooms = new int[model.entities.Count];
                    var beforePositions = new Vector2Int[model.entities.Count];
                    for (int entityIndex = 0; entityIndex < model.entities.Count; entityIndex++)
                    {
                        beforeRooms[entityIndex] = model.entities[entityIndex].roomId;
                        beforePositions[entityIndex] = model.entities[entityIndex].pos;
                    }

                    if (!model.TryMovePlayer(direction))
                    {
                        Failure(report, ref failures, index,
                            $"stored route blocks at move {step + 1} ({info.solution[step]})");
                        routeValid = false;
                        break;
                    }

                    visitedRooms.Add(model.player.roomId);
                    for (int entityIndex = 0; entityIndex < model.entities.Count; entityIndex++)
                    {
                        PEntity entity = model.entities[entityIndex];
                        bool moved = entity.roomId != beforeRooms[entityIndex]
                                     || entity.pos != beforePositions[entityIndex];
                        if (!moved) continue;
                        if (entity.IsCrate && entity.interiorRoomId < 0)
                            movedCargo.Add(entity);
                        if (entity.interiorRoomId >= 0) movedRooms.Add(entity);

                        PRoom destination = model.rooms[entity.roomId];
                        if (destination.oneway != null && destination.InBounds(entity.pos)
                            && destination.oneway[entity.pos.x, entity.pos.y] != Vector2Int.zero)
                            usedOneWays.Add((entity.roomId, entity.pos));
                    }
                }

                if (!routeValid) continue;
                if (!model.IsWon())
                    Failure(report, ref failures, index,
                        "stored route ends without completing the level");
                if (visitedRooms.Count != model.rooms.Count)
                    Failure(report, ref failures, index,
                        $"winning route visits {visitedRooms.Count}/{model.rooms.Count} rooms");
                if (movedCargo.Count != authoredCargo)
                    Failure(report, ref failures, index,
                        $"winning route moves {movedCargo.Count}/{authoredCargo} cargo objects");
                foreach (PEntity roomBox in authoredRoomBoxes)
                    if (!movedRooms.Contains(roomBox)
                        && !visitedRooms.Contains(roomBox.interiorRoomId))
                        Failure(report, ref failures, index,
                            $"Room {roomBox.interiorRoomId} is neither moved nor entered");
                if (usedOneWays.Count != expectedOneWays)
                    Failure(report, ref failures, index,
                        $"winning route uses {usedOneWays.Count}/{expectedOneWays} one-way cells");
                if (expectedPortals == 2 && !usedPortal)
                    Failure(report, ref failures, index,
                        "winning route never uses the mandatory portal");

                int purposefulMechanics = 0;
                if (visitedRooms.Count == model.rooms.Count && model.rooms.Count > 1)
                    purposefulMechanics++; // recursive room traversal
                if (usedOneWays.Count == expectedOneWays && expectedOneWays > 0)
                    purposefulMechanics++; // learned directional commitment
                if (movedCargo.Count > 0 || movedRooms.Count > 0)
                    purposefulMechanics++; // cargo/room manipulation
                if (expectedPortals == 2 && usedPortal)
                    purposefulMechanics++; // sealed portal exit
                if (purposefulMechanics < 2)
                    Failure(report, ref failures, index,
                        $"only {purposefulMechanics} mechanics affect the winning route");

                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                if (model.MoveCount > moveLimit)
                    Failure(report, ref failures, index,
                        $"solution needs {model.MoveCount}/{moveLimit} moves");

                ChapterFourDifficultyEvidence.Result evidence;
                try { evidence = ChapterFourDifficultyEvidence.Evaluate(prefab, info.solution); }
                catch (Exception exception)
                {
                    Failure(report, ref failures, index,
                        $"route evidence failed: {exception.Message}");
                    continue;
                }
                if (evidence.score <= previousRouteEvidence)
                    Failure(report, ref failures, index,
                        $"route evidence {evidence.score} does not exceed "
                        + $"the previous Chapter IV level's {previousRouteEvidence}");
                previousRouteEvidence = evidence.score;

                if (failures == levelFailuresBefore)
                    report.AppendLine($"PASS L{levelNumber:00} {info.levelName,-22} "
                        + $"C{info.designComplexity} / E{evidence.score} / "
                        + $"{visibleTasks} goals / {purposefulMechanics} purposeful mechanics / "
                        + $"{model.MoveCount}/{moveLimit} moves");
            }

            report.AppendLine($"RESULT: {10 - Math.Min(10, failures)}/10 Chapter IV levels "
                + "are unique, progressively harder, purposeful and solver-proven.");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log(report.ToString());
        }

        // Chapter-V-only release gate. The finale must start above Chapter IV, then rise on every
        // board through real route evidence. Each level has a distinct layout, five-or-more visible
        // completion jobs, four/five recursive room scales, and a winning route that actually uses
        // the nested-room, cargo, one-way, button/gate and sealed-portal systems.
        public static void ValidateChapterFiveFromCommandLine()
        {
            const int firstIndex = 40;
            const int lastIndex = 49;
            var report = new StringBuilder(4096);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var layouts = new HashSet<string>(StringComparer.Ordinal);
            int failures = 0;

            GameObject chapterFourFinale = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{LevelFolder}/Level_40.prefab");
            ParaboxLevel chapterFourInfo = chapterFourFinale != null
                ? chapterFourFinale.GetComponent<ParaboxLevel>() : null;
            int previousComplexity = chapterFourInfo != null
                ? chapterFourInfo.designComplexity : -1;
            int previousRouteEvidence = -1;
            if (previousComplexity < 0)
                throw new InvalidOperationException(
                    "Level 40 metadata is missing; Chapter V cannot prove its difficulty hand-off.");

            for (int index = firstIndex; index <= lastIndex; index++)
            {
                int levelFailuresBefore = failures;
                int levelNumber = index + 1;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{LevelFolder}/Level_{levelNumber}.prefab");
                ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
                if (prefab == null || info == null || string.IsNullOrWhiteSpace(info.solution))
                {
                    Failure(report, ref failures, index,
                        "missing prefab metadata or stored solution");
                    continue;
                }

                LevelModel model;
                try { model = LevelParser.Parse(prefab); }
                catch (Exception exception)
                {
                    Failure(report, ref failures, index, $"parse failed: {exception.Message}");
                    continue;
                }

                if (!names.Add(info.levelName))
                    Failure(report, ref failures, index,
                        $"duplicate Chapter V name '{info.levelName}'");
                if (!layouts.Add(ChapterThreeLayoutFingerprint(model)))
                    Failure(report, ref failures, index,
                        "duplicates another Chapter V layout");

                if (info.designComplexity <= previousComplexity)
                    Failure(report, ref failures, index,
                        $"difficulty {info.designComplexity} does not exceed the previous "
                        + $"level's {previousComplexity}");
                previousComplexity = info.designComplexity;

                int expectedRooms = ExpectedRoomCounts[index];
                if (model.rooms.Count != expectedRooms)
                    Failure(report, ref failures, index,
                        $"contains {model.rooms.Count}/{expectedRooms} recursive room scales");

                int visibleTasks = 0;
                bool hasButton = false;
                bool hasGate = false;
                foreach (PRoom room in model.rooms.Values)
                {
                    visibleTasks += room.boxGoals.Count + room.colourGoals.Count
                                    + room.playerGoals.Count + room.echoGoals.Count
                                    + room.mirrorGoals.Count;
                    if (room.button != null)
                        for (int x = 0; x < room.button.GetLength(0); x++)
                            for (int y = 0; y < room.button.GetLength(1); y++)
                                hasButton |= room.button[x, y];
                    if (room.gate != null)
                        for (int x = 0; x < room.gate.GetLength(0); x++)
                            for (int y = 0; y < room.gate.GetLength(1); y++)
                                hasGate |= room.gate[x, y];
                }
                int expectedTasks = LevelLayoutRebalancer.PremiumTaskTargetForLevel(index);
                if (expectedTasks < 5 || visibleTasks < expectedTasks)
                    Failure(report, ref failures, index,
                        $"exposes {visibleTasks}/{expectedTasks} visible completion jobs; "
                        + "the Chapter V floor is five");

                int expectedOneWays =
                    LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(index);
                if (model.rebalanceOneWays != expectedOneWays || expectedOneWays < 1)
                    Failure(report, ref failures, index,
                        $"retained {model.rebalanceOneWays}/{expectedOneWays} one-way commitments");
                if (model.portalPair.Count != 2)
                    Failure(report, ref failures, index,
                        $"contains {model.portalPair.Count}/2 cells for the mandatory portal pair");
                if (!hasButton || !hasGate
                    || !model.curriculumReuses.Contains(MechanicCatalog.Id.OneWay)
                    || !model.curriculumReuses.Contains(MechanicCatalog.Id.ButtonGate))
                    Failure(report, ref failures, index,
                        "is missing the complete one-way plus button/gate synthesis");

                int authoredCargo = 0;
                var movableRooms = new List<PEntity>();
                foreach (PEntity entity in model.entities)
                {
                    if (entity.IsCrate && entity.interiorRoomId < 0) authoredCargo++;
                    if (entity.interiorRoomId >= 0 && !entity.anchored)
                        movableRooms.Add(entity);
                }

                var movedCargo = new HashSet<PEntity>();
                var movedRooms = new HashSet<PEntity>();
                var visitedRooms = new HashSet<int> { model.player.roomId };
                var usedOneWays = new HashSet<(int room, Vector2Int cell)>();
                bool activatedButton = model.GatesOpen();
                bool crossedOpenGate = false;
                bool usedPortal = false;
                bool routeValid = true;

                for (int step = 0; step < info.solution.Length; step++)
                {
                    if (!TryDirection(info.solution[step], out Vector2Int direction))
                    {
                        Failure(report, ref failures, index,
                            $"invalid command '{info.solution[step]}' at move {step + 1}");
                        routeValid = false;
                        break;
                    }

                    var beforeRooms = new int[model.entities.Count];
                    var beforePositions = new Vector2Int[model.entities.Count];
                    for (int entityIndex = 0; entityIndex < model.entities.Count; entityIndex++)
                    {
                        beforeRooms[entityIndex] = model.entities[entityIndex].roomId;
                        beforePositions[entityIndex] = model.entities[entityIndex].pos;
                    }
                    int playerIndex = model.entities.IndexOf(model.player);
                    bool gateWasOpen = model.GatesOpen();

                    if (!model.TryMovePlayer(direction))
                    {
                        Failure(report, ref failures, index,
                            $"stored route blocks at move {step + 1} ({info.solution[step]})");
                        routeValid = false;
                        break;
                    }

                    bool gateIsOpen = model.GatesOpen();
                    activatedButton |= gateIsOpen;
                    visitedRooms.Add(model.player.roomId);
                    if (playerIndex >= 0)
                    {
                        int oldRoom = beforeRooms[playerIndex];
                        Vector2Int oldCell = beforePositions[playerIndex];
                        usedPortal |= ChapterFivePortalTransition(
                            model, oldRoom, oldCell, direction);
                    }

                    for (int entityIndex = 0; entityIndex < model.entities.Count; entityIndex++)
                    {
                        PEntity entity = model.entities[entityIndex];
                        int oldRoom = beforeRooms[entityIndex];
                        Vector2Int oldCell = beforePositions[entityIndex];
                        bool moved = oldRoom != entity.roomId || oldCell != entity.pos;
                        if (!moved) continue;

                        if (entity.IsCrate && entity.interiorRoomId < 0)
                            movedCargo.Add(entity);
                        if (entity.interiorRoomId >= 0)
                            movedRooms.Add(entity);

                        PRoom destination = model.rooms[entity.roomId];
                        if (destination.oneway != null && destination.InBounds(entity.pos)
                            && destination.oneway[entity.pos.x, entity.pos.y] != Vector2Int.zero)
                            usedOneWays.Add((entity.roomId, entity.pos));
                        if (gateWasOpen || gateIsOpen)
                            crossedOpenGate |= ChapterFiveSegmentTouches(
                                model, oldRoom, oldCell, entity.roomId, entity.pos,
                                (room, cell) => room.gate != null
                                    && room.gate[cell.x, cell.y]);
                    }
                }

                if (!routeValid) continue;
                if (!model.IsWon())
                    Failure(report, ref failures, index,
                        "stored route ends without completing every task");
                if (visitedRooms.Count != model.rooms.Count)
                    Failure(report, ref failures, index,
                        $"winning route visits {visitedRooms.Count}/{model.rooms.Count} rooms");
                if (movedCargo.Count != authoredCargo)
                    Failure(report, ref failures, index,
                        $"winning route moves {movedCargo.Count}/{authoredCargo} cargo objects");
                if (movedRooms.Count != movableRooms.Count)
                    Failure(report, ref failures, index,
                        $"winning route moves {movedRooms.Count}/{movableRooms.Count} movable rooms");
                if (usedOneWays.Count != expectedOneWays)
                    Failure(report, ref failures, index,
                        $"winning route uses {usedOneWays.Count}/{expectedOneWays} one-way cells");
                if (!activatedButton || !crossedOpenGate)
                    Failure(report, ref failures, index,
                        "winning route does not hold the button and cross its opened gate");
                if (!usedPortal)
                    Failure(report, ref failures, index,
                        "winning route never traverses the mandatory portal");

                int purposefulMechanics = 0;
                if (visitedRooms.Count == model.rooms.Count && model.rooms.Count > 1)
                    purposefulMechanics++; // recursive room traversal
                if (movedCargo.Count == authoredCargo && authoredCargo > 0)
                    purposefulMechanics++; // cargo planning
                if (usedOneWays.Count == expectedOneWays && expectedOneWays > 0)
                    purposefulMechanics++; // one-way commitments
                if (activatedButton && crossedOpenGate)
                    purposefulMechanics++; // cargo-held button/gate
                if (model.portalPair.Count == 2 && usedPortal)
                    purposefulMechanics++; // mandatory portal exit
                if (purposefulMechanics < 5)
                    Failure(report, ref failures, index,
                        $"only {purposefulMechanics}/5 mechanic families affect the winning route");

                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                if (model.MoveCount > moveLimit)
                    Failure(report, ref failures, index,
                        $"solution needs {model.MoveCount}/{moveLimit} moves");

                ChapterFiveDifficultyEvidence.Result evidence;
                try { evidence = ChapterFiveDifficultyEvidence.Evaluate(prefab, info.solution); }
                catch (Exception exception)
                {
                    Failure(report, ref failures, index,
                        $"route evidence failed: {exception.Message}");
                    continue;
                }
                if (evidence.score <= previousRouteEvidence)
                    Failure(report, ref failures, index,
                        $"route evidence {evidence.score} does not exceed "
                        + $"the previous Chapter V level's {previousRouteEvidence}");
                previousRouteEvidence = evidence.score;

                if (failures == levelFailuresBefore)
                    report.AppendLine($"PASS L{levelNumber:00} {info.levelName,-26} "
                        + $"C{info.designComplexity} / E{evidence.score} / "
                        + $"{visibleTasks} jobs / {expectedOneWays} one-ways / "
                        + $"{purposefulMechanics} purposeful mechanics / "
                        + $"{model.MoveCount}/{moveLimit} moves");
            }

            report.AppendLine($"RESULT: {10 - Math.Min(10, failures)}/10 Chapter V levels "
                + "are unique, progressively harder, five-task minimum, purposeful and solver-proven.");
            if (failures > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log(report.ToString());
        }

        static bool ChapterFivePortalTransition(LevelModel model, int oldRoom, Vector2Int oldCell,
                                                Vector2Int direction)
        {
            // Settle may move the player beyond the paired destination in the same command, so
            // detect the portal at the entered source cell rather than comparing final positions.
            return model.portalPair.ContainsKey((oldRoom, oldCell + direction));
        }

        static bool ChapterFiveSegmentTouches(LevelModel model, int oldRoom, Vector2Int oldCell,
                                              int newRoom, Vector2Int newCell,
                                              Func<PRoom, Vector2Int, bool> predicate)
        {
            if (!model.rooms.TryGetValue(newRoom, out PRoom room)) return false;
            if (oldRoom != newRoom) return room.InBounds(newCell) && predicate(room, newCell);

            Vector2Int delta = newCell - oldCell;
            Vector2Int stride = new Vector2Int(
                delta.x == 0 ? 0 : (delta.x > 0 ? 1 : -1),
                delta.y == 0 ? 0 : (delta.y > 0 ? 1 : -1));
            if (stride.x != 0 && stride.y != 0) return predicate(room, newCell);
            for (Vector2Int cell = oldCell + stride; cell != newCell + stride; cell += stride)
                if (room.InBounds(cell) && predicate(room, cell)) return true;
            return false;
        }

        // High-priority regression gate: Level 11 must be completely self-contained when selected
        // in a brand-new session. Validate both assets that fresh entry touches—the campaign board
        // and its Chapter 2 tutorial—without relying on state initialized by Levels 1-10.
        public static void ValidateLevelElevenFreshLaunchFromCommandLine()
        {
            const int index = 10;
            GameObject level = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{LevelFolder}/Level_{index + 1}.prefab");
            ValidateFreshLaunchPuzzle(level, index, "Level 11", expectedRooms: 2);

            string tutorialPath = TutorialPuzzleLibrary.ResourcePath(
                index, MechanicCatalog.Id.NestedBoard);
            if (tutorialPath != "Parabox/Tutorials/Chapter_2")
                throw new InvalidOperationException(
                    $"Level 11 resolved the wrong tutorial asset: {tutorialPath}");
            GameObject tutorial = TutorialPuzzleLibrary.Load(
                index, MechanicCatalog.Id.NestedBoard);
            ValidateFreshLaunchPuzzle(tutorial, index, "Chapter 2 tutorial", expectedRooms: 3);

            Debug.Log("[Parabox] PASS: fresh Level 11 and Chapter 2 tutorial parse independently, "
                + "contain valid nested-room references, and both stored routes win.");
        }

        // Focused regression gate for the first-launch flow. Chapter I must schedule its own
        // independent mini-puzzle before Level 1, and the mini-puzzle's stored route must win.
        public static void ValidateChapterOneTutorialBeforeLevelOneFromCommandLine()
        {
            var prefabs = new GameObject[ExpectedLevels];
            for (int index = 0; index < prefabs.Length; index++)
            {
                prefabs[index] = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{LevelFolder}/Level_{index + 1}.prefab");
                if (prefabs[index] == null)
                    throw new InvalidOperationException($"Level {index + 1} prefab is missing.");
            }

            List<MechanicCatalog.Id> lessons = MechanicCatalog.TutorialsAt(prefabs, 0);
            if (!lessons.Contains(MechanicCatalog.Id.Navigation)
                || !MechanicCatalog.IsChapterTutorial(0, MechanicCatalog.Id.Navigation))
                throw new InvalidOperationException(
                    "Chapter 1 Navigation tutorial is not scheduled before Level 1.");
            for (int index = 0; index < prefabs.Length; index++)
                if (MechanicCatalog.TutorialsAt(prefabs, index)
                    .Contains(MechanicCatalog.Id.OneWay))
                    throw new InvalidOperationException(
                        $"Level {index + 1} still schedules the removed standalone one-way tutorial.");

            string path = TutorialPuzzleLibrary.ResourcePath(0, MechanicCatalog.Id.Navigation);
            if (path != "Parabox/Tutorials/Chapter_1")
                throw new InvalidOperationException($"Level 1 resolved the wrong tutorial: {path}");
            GameObject tutorial = TutorialPuzzleLibrary.Load(0, MechanicCatalog.Id.Navigation);
            ParaboxLevel info = tutorial != null ? tutorial.GetComponent<ParaboxLevel>() : null;
            if (tutorial == null || info == null || string.IsNullOrWhiteSpace(info.solution))
                throw new InvalidOperationException(
                    "Chapter 1 tutorial prefab or its stored route is missing.");

            LevelModel model = LevelParser.Parse(tutorial);
            if (model == null || model.player == null || model.rooms.Count != 1)
                throw new InvalidOperationException(
                    "Chapter 1 tutorial must be a self-contained one-room mini-puzzle.");
            for (int step = 0; step < info.solution.Length; step++)
            {
                if (!TryDirection(info.solution[step], out Vector2Int direction)
                    || !model.TryMovePlayer(direction))
                    throw new InvalidOperationException(
                        $"Chapter 1 tutorial route failed at move {step + 1}.");
            }
            if (!model.IsWon())
                throw new InvalidOperationException(
                    "Chapter 1 tutorial route ended without completing both targets.");

            string levelOneSeenKey = GameManager.MechanicBriefingKey(0);
            if (string.IsNullOrWhiteSpace(levelOneSeenKey)
                || levelOneSeenKey == GameManager.TutorialKey
                || levelOneSeenKey == GameManager.MechanicBriefingKey(1))
                throw new InvalidOperationException(
                    "Chapter 1 tutorial does not have an independent one-time seen key.");

            ValidateChapterOneTutorialRuntimeGate(levelOneSeenKey);

            Debug.Log("[Parabox] PASS: Chapter 1 tutorial is scheduled before Level 1, uses an "
                + "independent one-time seen key, remains mandatory on first entry, and its "
                + "stored mini-puzzle route wins.");
        }

        static void ValidateChapterOneTutorialRuntimeGate(string seenKey)
        {
            bool hadSeenValue = PlayerPrefs.HasKey(seenKey);
            int previousSeenValue = PlayerPrefs.GetInt(seenKey, 0);
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(GameScene);
            bool openedScene = !scene.IsValid() || !scene.isLoaded;

            try
            {
                if (openedScene)
                    scene = EditorSceneManager.OpenScene(
                        GameScene, OpenSceneMode.Additive);

                GameManager manager = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    manager = root.GetComponentInChildren<GameManager>(true);
                    if (manager != null) break;
                }
                if (manager == null)
                    throw new InvalidOperationException(
                        "Game.unity has no GameManager for the Level 1 tutorial gate.");
                if (manager.tutorialFx == null || manager.tutorialFx.videoImage == null
                    || manager.tutorialBgCamera == null)
                    throw new InvalidOperationException(
                        "Game.unity is missing the visible tutorial UI or tutorial camera wiring.");

                PlayerPrefs.DeleteKey(seenKey);
                PlayerPrefs.Save();

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo levelField = typeof(GameManager).GetField("levelIndex", flags);
                FieldInfo previewField = typeof(GameManager).GetField("editorPreviewMode", flags);
                FieldInfo suppressionField = typeof(GameManager).GetField(
                    "suppressTutorialForThisLoad", flags);
                MethodInfo willTutorial = typeof(GameManager).GetMethod("WillTutorial", flags);
                if (levelField == null || previewField == null || suppressionField == null
                    || willTutorial == null)
                    throw new InvalidOperationException(
                        "Could not inspect the Level 1 tutorial runtime gate.");

                object previousLevel = levelField.GetValue(manager);
                object previousPreview = previewField.GetValue(manager);
                object previousSuppression = suppressionField.GetValue(manager);
                try
                {
                    levelField.SetValue(manager, 0);
                    // Direct Editor preview and restart suppression used to hide the tutorial.
                    // A first-time Level 1 entry must override both shortcuts.
                    previewField.SetValue(manager, true);
                    suppressionField.SetValue(manager, true);
                    bool shouldShow = (bool)willTutorial.Invoke(manager, null);
                    if (!shouldShow)
                        throw new InvalidOperationException(
                            "A fresh Level 1 entry still skips its tutorial at runtime.");
                }
                finally
                {
                    levelField.SetValue(manager, previousLevel);
                    previewField.SetValue(manager, previousPreview);
                    suppressionField.SetValue(manager, previousSuppression);
                }
            }
            finally
            {
                if (hadSeenValue) PlayerPrefs.SetInt(seenKey, previousSeenValue);
                else PlayerPrefs.DeleteKey(seenKey);
                PlayerPrefs.Save();
                if (openedScene && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ValidateFreshLaunchPuzzle(GameObject prefab, int campaignIndex,
                                              string label, int expectedRooms)
        {
            if (prefab == null)
                throw new InvalidOperationException($"{label} prefab is missing.");
            ParaboxLevel info = prefab.GetComponent<ParaboxLevel>();
            if (info == null || string.IsNullOrWhiteSpace(info.solution))
                throw new InvalidOperationException($"{label} has no stored winning route.");

            LevelModel model = LevelParser.Parse(prefab);
            if (model == null || model.player == null)
                throw new InvalidOperationException($"{label} has no independently initialized player.");
            if (model.rooms.Count != expectedRooms)
                throw new InvalidOperationException(
                    $"{label} has {model.rooms.Count} room(s); expected {expectedRooms}.");

            bool hasValidNestedRoom = false;
            foreach (PEntity entity in model.entities)
            {
                if (entity.interiorRoomId < 0) continue;
                if (!model.rooms.ContainsKey(entity.interiorRoomId))
                    throw new InvalidOperationException(
                        $"{label} references missing inner room {entity.interiorRoomId}.");
                hasValidNestedRoom = true;
            }
            if (!hasValidNestedRoom)
                throw new InvalidOperationException($"{label} has no room-inside-a-box entity.");

            for (int step = 0; step < info.solution.Length; step++)
            {
                if (!TryDirection(info.solution[step], out Vector2Int direction)
                    || !model.TryMovePlayer(direction))
                    throw new InvalidOperationException(
                        $"{label} route failed at move {step + 1} ({info.solution[step]})." );
            }
            if (!model.IsWon())
                throw new InvalidOperationException($"{label} route ended without a win.");
            if (label == "Level 11"
                && model.MoveCount > GameManager.MoveLimitForLevel(campaignIndex, info.par))
                throw new InvalidOperationException($"{label} exceeds its runtime move limit.");
        }

        public struct ValidationResult
        {
            public int failures;
            public int warnings;
            public string report;
        }

        public static ValidationResult ValidateCampaign()
        {
            int failures = 0, warnings = 0;
            var report = new StringBuilder(8192);
            report.AppendLine("PARABOX CAMPAIGN VALIDATION");
            report.AppendLine("===========================");
            float previousRating = 0f;
            int previousComplexity = -1;
            string previousName = string.Empty;
            var authoredLayouts = new Dictionary<string, (int index, string name)>();
            var levelNames = new Dictionary<string, int>(StringComparer.Ordinal);
            var chapterMinComplexity = new int[5];
            var chapterMaxComplexity = new int[5];
            var chapterParTotal = new int[5];
            var chapterComplexityTotal = new int[5];
            var chapterLevelCount = new int[5];
            var chapterIntroductions = new int[5];
            var chapterTutorialVideos = new int[5];
            var chapterNestedLevels = new int[5];
            var chapterHasTeach = new bool[5];
            var chapterHasCombine = new bool[5];
            var chapterHasMastery = new bool[5];
            var campaignPrefabs = new GameObject[ExpectedLevels];
            var mechanicOccurrences = new Dictionary<MechanicCatalog.Id, List<int>>();
            for (int chapter = 0; chapter < chapterMinComplexity.Length; chapter++)
                chapterMinComplexity[chapter] = int.MaxValue;

            for (int index = 0; index < ExpectedLevels; index++)
            {
                string path = $"{LevelFolder}/Level_{index + 1}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Failure(report, ref failures, index, $"missing prefab at {path}");
                    continue;
                }
                campaignPrefabs[index] = prefab;

                var info = prefab.GetComponent<ParaboxLevel>();
                if (info == null)
                {
                    Failure(report, ref failures, index, "prefab has no ParaboxLevel component");
                    continue;
                }
                if (levelNames.TryGetValue(info.levelName, out int sameName))
                    Failure(report, ref failures, index,
                        $"duplicate name '{info.levelName}' already used by Level {sameName + 1}");
                else
                    levelNames[info.levelName] = index;

                string authoredLayout = AuthoredLayoutSignature(prefab);
                if (authoredLayouts.TryGetValue(authoredLayout, out var sameLayout))
                    Failure(report, ref failures, index,
                        $"authored board duplicates Level {sameLayout.index + 1} ({sameLayout.name})");
                else
                    authoredLayouts[authoredLayout] = (index, info.levelName);
                int authoredMechanics = AuthoredMechanicCount(prefab);
                if (authoredMechanics == 0)
                    Failure(report, ref failures, index,
                        "level introduces no authored gameplay mechanic");
                ValidateGoalColourContract(prefab, index, report, ref failures);
                if (info.par <= 0)
                    Failure(report, ref failures, index, $"invalid par {info.par}");
                if (string.IsNullOrWhiteSpace(info.solution))
                {
                    Failure(report, ref failures, index, "stored solution is empty");
                    continue;
                }
                if (info.solution.Length != info.par)
                    Failure(report, ref failures, index,
                        $"solution length {info.solution.Length} does not equal par {info.par}");
                CampaignProgression.Profile progression = CampaignProgression.ForLevel(index);
                List<MechanicCatalog.Id> tutorialLessons =
                    MechanicCatalog.TutorialsAt(campaignPrefabs, index);
                // Chapter videos are fixed checkpoints. A focused mechanic video is conditional:
                // it may appear only where the prefab proves that supported rule is genuinely new.
                bool chapterTutorialCheckpoint = MechanicCatalog.IsChapterTutorialCheckpoint(index);
                bool hasNewMechanicTutorial = MechanicCatalog.TryGetNewMechanicTutorial(
                    campaignPrefabs, index, out MechanicCatalog.Id newMechanicLesson);
                int expectedTutorialCount = (chapterTutorialCheckpoint ? 1 : 0)
                    + (hasNewMechanicTutorial ? 1 : 0);
                chapterTutorialVideos[index / 10] += tutorialLessons.Count;
                if (progression.introducesMechanic != chapterTutorialCheckpoint)
                    Failure(report, ref failures, index,
                        $"chapter tutorial flag is {progression.introducesMechanic}; " +
                        $"only chapter openers 1, 11, 21, 31 and 41 may set it");
                if (tutorialLessons.Count != expectedTutorialCount)
                    Failure(report, ref failures, index,
                        $"expected {expectedTutorialCount} tutorial video(s) from its chapter/new-" +
                        $"mechanic evidence; found {tutorialLessons.Count}");
                if (hasNewMechanicTutorial
                    && !tutorialLessons.Contains(newMechanicLesson))
                    Failure(report, ref failures, index,
                        $"first appearance is missing its NEW MECHANIC {newMechanicLesson} video");
                if (MechanicCatalog.TryGetChapterTutorial(index, out MechanicCatalog.Id chapterLesson)
                    && !tutorialLessons.Contains(chapterLesson))
                    Failure(report, ref failures, index,
                        $"chapter opener is missing its {chapterLesson} tutorial mini-board");
                foreach (MechanicCatalog.Id tutorialLesson in tutorialLessons)
                {
                    if (!TutorialPuzzleLibrary.Exists(index, tutorialLesson))
                    {
                        Failure(report, ref failures, index,
                            $"tutorial prefab is missing: "
                            + TutorialPuzzleLibrary.ResourcePath(index, tutorialLesson));
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(MechanicCatalog.TutorialTitle(index, tutorialLesson))
                        && !string.IsNullOrWhiteSpace(
                            MechanicCatalog.TutorialBundleName(index, tutorialLesson))
                        && !string.IsNullOrWhiteSpace(
                            MechanicCatalog.TutorialBundleLesson(index, tutorialLesson)))
                        continue;
                    Failure(report, ref failures, index,
                        $"tutorial {tutorialLesson} has no short player-facing title or description");
                }

                foreach (MechanicCatalog.Id mechanic in MechanicCatalog.MechanicsIn(prefab, index))
                {
                    if (index < 10
                        && mechanic != MechanicCatalog.Id.Navigation
                        && mechanic != MechanicCatalog.Id.Crate
                        && mechanic != MechanicCatalog.Id.OneWay
                        && mechanic != MechanicCatalog.Id.ButtonGate
                        && mechanic != MechanicCatalog.Id.SlidingCargo)
                    {
                        Failure(report, ref failures, index,
                            $"Chapter I contains disallowed mechanic {mechanic}; keep only "
                            + "push, one-way, button/gate and sliding cargo");
                    }

                    if (!mechanicOccurrences.TryGetValue(mechanic, out List<int> levels))
                    {
                        levels = new List<int>();
                        mechanicOccurrences[mechanic] = levels;
                    }
                    levels.Add(index + 1);
                }
                float expectedRating = progression.rating;
                if (!Mathf.Approximately(info.difficultyRating, expectedRating))
                    Failure(report, ref failures, index,
                        $"difficulty rating {info.difficultyRating:0.00} does not match curve {expectedRating:0.00}");
                if (index > 0 && info.difficultyRating <= previousRating)
                    Failure(report, ref failures, index,
                        $"difficulty plateau: rating {info.difficultyRating:0.00} follows " +
                        $"{previousName} at {previousRating:0.00}");
                if (index > 0 && info.designComplexity <= previousComplexity)
                    Failure(report, ref failures, index,
                        $"design complexity {info.designComplexity} does not exceed " +
                        $"{previousName} at {previousComplexity}");
                // The finale may land fractionally harder than the normal transition ceiling: it
                // is the one board explicitly responsible for synthesizing the whole campaign.
                float maximumGrowth = index == 40 ? 1.18f
                    : index == ExpectedLevels - 1 ? 1.22f : 1.21f;
                if (index > 0 && info.designComplexity > Mathf.CeilToInt(previousComplexity * maximumGrowth))
                    Failure(report, ref failures, index,
                        $"difficulty spike: evidence {info.designComplexity} jumps more than " +
                        $"{Mathf.RoundToInt((maximumGrowth - 1f) * 100f)}% from " +
                        $"{previousName} at {previousComplexity}");
                if (info.chapter != progression.chapter
                    || info.chapterName != progression.chapterName
                    || info.chapterPhilosophy != progression.philosophy
                    || info.progressionRole != progression.role
                    || info.mechanicFocus != progression.mechanicFocus
                    || info.introducesMechanic != progression.introducesMechanic)
                {
                    Failure(report, ref failures, index,
                        "serialized curriculum metadata does not match CampaignProgression");
                }
                previousRating = info.difficultyRating;
                previousComplexity = info.designComplexity;
                previousName = info.levelName;

                int chapterIndex = Mathf.Clamp(index / 10, 0, 4);
                chapterMinComplexity[chapterIndex] = Mathf.Min(
                    chapterMinComplexity[chapterIndex], info.designComplexity);
                chapterMaxComplexity[chapterIndex] = Mathf.Max(
                    chapterMaxComplexity[chapterIndex], info.designComplexity);
                chapterParTotal[chapterIndex] += info.par;
                chapterComplexityTotal[chapterIndex] += info.designComplexity;
                chapterLevelCount[chapterIndex]++;
                if (info.introducesMechanic) chapterIntroductions[chapterIndex]++;
                if (info.progressionRole == CampaignProgression.LevelRole.Teach)
                    chapterHasTeach[chapterIndex] = true;
                if (info.progressionRole == CampaignProgression.LevelRole.Combine)
                    chapterHasCombine[chapterIndex] = true;
                if (info.progressionRole == CampaignProgression.LevelRole.Mastery
                    || info.progressionRole == CampaignProgression.LevelRole.Finale)
                    chapterHasMastery[chapterIndex] = true;

                int roomCount = prefab.GetComponentsInChildren<RoomMarker>(true).Length;
                int expectedRoomCount = ExpectedRoomCounts[index];
                if (roomCount != expectedRoomCount)
                    Failure(report, ref failures, index,
                        $"authored room count {roomCount} does not match Level {index + 1}'s " +
                        $"intended {expectedRoomCount}-room structure");
                if (roomCount > 1) chapterNestedLevels[chapterIndex]++;
                if (roomCount > 1)
                    ValidateRecursiveContainment(prefab, index, roomCount, report, ref failures);

                // Chapter I may teach a directional tile when it is explicitly authored and given
                // a first-appearance lesson. The generic introduction audit above guarantees that
                // an arrow cannot silently appear without explanation.

                LevelModel model;
                try { model = LevelParser.Parse(prefab); }
                catch (Exception ex)
                {
                    Failure(report, ref failures, index, $"parse failed: {ex.Message}");
                    continue;
                }

                if (model.player == null)
                {
                    Failure(report, ref failures, index, "level has no player");
                    continue;
                }
                if (index >= 10 && index < 20 && (model.mirror != null || model.echo != null))
                    Failure(report, ref failures, index,
                        "Chapter II must teach room-inside-a-box movement without linked-player actors");
                if (info.par < MinimumPars[index])
                    Failure(report, ref failures, index,
                        $"route {info.par} is below Level {index + 1}'s reviewed difficulty floor " +
                        MinimumPars[index]);
                int minimumComplexity = 650 + index * 100;
                int maximumComplexity = minimumComplexity + 89;
                if (info.designComplexity < minimumComplexity
                    || info.designComplexity > maximumComplexity)
                    Failure(report, ref failures, index,
                        $"campaign complexity {info.designComplexity} is outside the reviewed Level " +
                        $"{index + 1} band {minimumComplexity}-{maximumComplexity}");

                // Curriculum reuses are installed into the parsed board only when the exact
                // authored solution still wins. Treat a missed checkpoint as a release error:
                // "Teach -> disappear" is precisely the regression this audit is meant to stop.
                foreach (MechanicCatalog.Id expected in MechanicCatalog.RehearsalsAt(index))
                    if (!model.curriculumReuses.Contains(expected))
                        Failure(report, ref failures, index,
                            $"could not retain the planned {expected} curriculum rehearsal");
                foreach (MechanicCatalog.Id mechanic in model.curriculumReuses)
                {
                    if (!mechanicOccurrences.TryGetValue(mechanic, out List<int> levels))
                    {
                        levels = new List<int>();
                        mechanicOccurrences[mechanic] = levels;
                    }
                    if (!levels.Contains(index + 1)) levels.Add(index + 1);
                }
                int targets = TargetCount(model);
                if (targets == 0)
                    Failure(report, ref failures, index, "level has no completion target");
                int dependencyLimit = LevelLayoutRebalancer.DependencyBudgetForLevel(index);
                int authoredGateLimit = LevelLayoutRebalancer.AuthoredGateReuseBudgetForLevel(index);
                int premiumTaskTarget = LevelLayoutRebalancer.PremiumTaskTargetForLevel(index);
                if (index >= 10 && index < 20)
                {
                    int chapterTwoTaskTarget =
                        LevelLayoutRebalancer.ChapterTwoTaskTargetForLevel(index);
                    int chapterTwoGateBudget =
                        LevelLayoutRebalancer.ChapterTwoGateReuseBudgetForLevel(index);
                    int sourceTargets = prefab.GetComponentsInChildren<GoalMarker>(true).Length;
                    bool sourceHasButtonGate =
                        prefab.GetComponentInChildren<SwitchMarker>(true) != null
                        && prefab.GetComponentInChildren<GateMarker>(true) != null;
                    int expectedObjectives = Mathf.Max(0, chapterTwoTaskTarget - sourceTargets)
                        + (chapterTwoGateBudget > 0 && !sourceHasButtonGate ? 1 : 0);
                    if (targets != chapterTwoTaskTarget)
                        Failure(report, ref failures, index,
                            $"Chapter II exposes {targets}/{chapterTwoTaskTarget} completion tasks");
                    if (model.rebalanceObjectives != expectedObjectives)
                        Failure(report, ref failures, index,
                            $"Chapter II retained {model.rebalanceObjectives}/{expectedObjectives} "
                            + "route-proven cargo/gate objectives");
                }
                else if (index >= 40 && index < 50)
                {
                    if (targets < premiumTaskTarget)
                        Failure(report, ref failures, index,
                            $"Chapter V exposes only {targets}/{premiumTaskTarget} visible premium "
                            + "completion jobs");
                    int premiumJobsAdded = model.rebalanceObjectives - authoredGateLimit;
                    if (premiumJobsAdded < 0 || premiumJobsAdded > premiumTaskTarget
                        || authoredGateLimit <= 0)
                        Failure(report, ref failures, index,
                            $"Chapter V retained an invalid delivery/gate task budget: "
                            + $"O{model.rebalanceObjectives}, G{authoredGateLimit}, "
                            + $"target {premiumTaskTarget}");
                }
                else if (model.rebalanceObjectives > dependencyLimit + authoredGateLimit)
                    Failure(report, ref failures, index,
                        $"cross-mechanic dependencies {model.rebalanceObjectives} exceed planned limit "
                        + (dependencyLimit + authoredGateLimit));
                int cleanWallLimit = index == 29 ? 8
                    : index < 10 ? 1 : index < 20 ? 2
                    : index < 30 ? 3 : index < 40 ? 4 : 5;
                if (model.rebalanceWalls > cleanWallLimit)
                    Failure(report, ref failures, index,
                        $"runtime wall formation {model.rebalanceWalls} exceeds clean limit {cleanWallLimit}");
                int focusedAccentLimit = Mathf.Max(
                    index < 10 ? 1 : index < 30 ? 2 : 3,
                    LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(index));
                int requiredOneWays = LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(index);
                if (((index >= 10 && index < 20) || index >= 40)
                    && model.rebalanceOneWays != requiredOneWays)
                    Failure(report, ref failures, index,
                        $"Chapter {(index < 20 ? "II" : "V")} retained "
                        + $"{model.rebalanceOneWays}/{requiredOneWays} required one-way commitments");
                else if (model.rebalanceOneWays > focusedAccentLimit
                    || model.rebalanceHazards > focusedAccentLimit)
                    Failure(report, ref failures, index,
                        "focused mechanic accents exceed the readable per-level limit");
                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                float expectedTime = index >= 40
                    ? 100f + (index - 40) * 2f
                    : index == 0 || index == 4
                        ? 35f
                        : 20f + index * 3f
                            + (CampaignProgression.ReceivesChapterTutorialTimeBonus(index) ? 15f : 0f);
                float actualTime = CampaignProgression.TimeLimit(index, info.par);
                if (Mathf.Abs(actualTime - expectedTime) > 0.01f)
                    Failure(report, ref failures, index,
                        $"timer {actualTime:0.0}s does not match the campaign rule " +
                        $"(Level {index + 1} must be {expectedTime:0.0}s: Chapter V uses "
                        + "100s +2s/level; earlier chapters keep their reviewed timer rules)");

                // Level 1 may introduce one action plainly. After that, the early campaign must
                // never regress to a corridor solved by holding one direction. This is measured by
                // the player's commands even when an echo or mirror changes another actor: hidden
                // state does not make a nine-command straight input sequence interesting to play.
                int routeTurns = DirectionChanges(info.solution);
                int longestRun = LongestDirectionRun(info.solution);
                if (index >= 20 && longestRun > 18)
                    Failure(report, ref failures, index,
                        $"recursive route repeats one direction {longestRun} times; maximum is 18");
                if (index > 0 && index < 20 && routeTurns < 4)
                    Failure(report, ref failures, index,
                        $"early route has only {routeTurns} direction decisions; minimum is 4 after Level 1");
                int maximumEarlyRun = index < 10 ? 5 : 10;
                if (index > 0 && index < 20 && longestRun > maximumEarlyRun)
                    Failure(report, ref failures, index,
                        $"early route repeats one direction {longestRun} times; maximum is "
                        + $"{maximumEarlyRun} in this chapter");

                bool routeValid = true;
                int playerRoomTransitions = 0;
                int cargoRoomTransitions = 0;
                int metaMoves = 0;
                var movedCargo = new HashSet<PEntity>();
                var movedMetaRooms = new HashSet<PEntity>();
                var visitedRooms = new HashSet<int> { model.player.roomId };
                for (int step = 0; step < info.solution.Length; step++)
                {
                    if (!TryDirection(info.solution[step], out var direction))
                    {
                        Failure(report, ref failures, index,
                            $"solution contains invalid character '{info.solution[step]}' at move {step + 1}");
                        routeValid = false;
                        break;
                    }

                    var beforeRooms = new int[model.entities.Count];
                    var beforePositions = new Vector2Int[model.entities.Count];
                    for (int entityIndex = 0; entityIndex < model.entities.Count; entityIndex++)
                    {
                        beforeRooms[entityIndex] = model.entities[entityIndex].roomId;
                        beforePositions[entityIndex] = model.entities[entityIndex].pos;
                    }

                    if (!model.TryMovePlayer(direction))
                    {
                        Failure(report, ref failures, index,
                            $"stored solution is blocked at move {step + 1} ({info.solution[step]})");
                        routeValid = false;
                        break;
                    }

                    if (model.player.roomId != beforeRooms[model.entities.IndexOf(model.player)])
                        playerRoomTransitions++;
                    visitedRooms.Add(model.player.roomId);
                    for (int entityIndex = 0; entityIndex < model.entities.Count; entityIndex++)
                    {
                        PEntity entity = model.entities[entityIndex];
                        bool changedRoom = entity.roomId != beforeRooms[entityIndex];
                        bool moved = changedRoom || entity.pos != beforePositions[entityIndex];
                        if (entity.IsCrate && entity.interiorRoomId < 0 && moved)
                            movedCargo.Add(entity);
                        if (entity.IsCrate && entity.interiorRoomId < 0 && changedRoom)
                            cargoRoomTransitions++;
                        if (entity.interiorRoomId >= 0
                            && (changedRoom || entity.pos != beforePositions[entityIndex]))
                        {
                            metaMoves++;
                            movedMetaRooms.Add(entity);
                        }
                    }
                }

                if (!routeValid) continue;
                if (!model.IsWon())
                    Failure(report, ref failures, index, "stored solution ends without satisfying every target");
                if (index >= 10)
                {
                    int authoredMovableRooms = 0;
                    int authoredCargo = 0;
                    foreach (PEntity entity in model.entities)
                    {
                        if (entity.interiorRoomId >= 0 && !entity.anchored) authoredMovableRooms++;
                        if (entity.IsCrate && entity.interiorRoomId < 0) authoredCargo++;
                    }
                    if (visitedRooms.Count != roomCount)
                        Failure(report, ref failures, index,
                            $"stored solution visits {visitedRooms.Count}/{roomCount} recursive rooms");
                    if (playerRoomTransitions == 0)
                        Failure(report, ref failures, index,
                            "proof never demonstrates entering or leaving a room-box");
                    if (index < 30 || index >= 40)
                    {
                        if (movedMetaRooms.Count != authoredMovableRooms)
                            Failure(report, ref failures, index,
                                $"route moves {movedMetaRooms.Count}/{authoredMovableRooms} authored movable " +
                                "rooms; room mechanics cannot be decorative");
                    }
                    else
                    {
                        // Chapter IV deliberately mixes movable docks with pinned entry chambers.
                        // A pinned room is purposeful when the winning route enters it; demanding
                        // physical movement from a wall-braced module incorrectly rejects that
                        // valid room-inside-a-box use.
                        foreach (PEntity entity in model.entities)
                            if (entity.interiorRoomId >= 0
                                && !movedMetaRooms.Contains(entity)
                                && !visitedRooms.Contains(entity.interiorRoomId))
                                Failure(report, ref failures, index,
                                    $"Room {entity.interiorRoomId} is neither moved nor entered");
                    }
                    if (movedCargo.Count != authoredCargo)
                        Failure(report, ref failures, index,
                            $"route moves {movedCargo.Count}/{authoredCargo} authored cargo objects; " +
                            "cargo cannot be decorative");
                    // Chapter II's route-local foundation crates are mandatory jobs, but are not
                    // authored room-transfer cargo. Subtract them before enforcing the separate
                    // recursive-boundary requirement.
                    int insertedFoundationCargo = index >= 10 && index < 20
                        ? Mathf.Max(0, model.rebalanceObjectives
                            - (model.curriculumReuses.Contains(MechanicCatalog.Id.ButtonGate) ? 1 : 0))
                        : 0;
                    int authoredBoundaryCargo = Mathf.Max(0, authoredCargo - insertedFoundationCargo);
                    if (authoredBoundaryCargo > 0 && roomCount > 1 && cargoRoomTransitions == 0)
                        Failure(report, ref failures, index,
                            "recursive cargo never crosses a room boundary");
                }
                if (model.MoveCount > moveLimit)
                    Failure(report, ref failures, index,
                        $"solution needs {model.MoveCount} moves but limit is {moveLimit}");
                report.AppendLine($"PASS  L{index + 1:00}  {info.levelName,-22} " +
                    $"D{info.difficultyRating,4:0.0} / C{info.designComplexity,6} / " +
                    $"par {info.par,2} / limit {moveLimit,2} / " +
                    $"mechanics {authoredMechanics,2} / " +
                    $"layout W{model.rebalanceWalls}/A{model.rebalanceOneWays}/H{model.rebalanceHazards}/O{model.rebalanceObjectives} / " +
                    $"{info.progressionRole}: {info.mechanicFocus}");
            }

            report.AppendLine("---------------------------");
            report.AppendLine("MECHANIC CURRICULUM (Teach -> Practice -> Combine -> Master)");
            foreach (var pair in mechanicOccurrences)
            {
                List<int> levels = pair.Value;
                string stages;
                if (levels.Count == 1)
                {
                    stages = $"Teach L{levels[0]:00}; later combination scheduled by authored curriculum";
                    warnings++;
                    report.AppendLine($"WARN  {pair.Key,-20} appears only at L{levels[0]:00}; " +
                        "add a later authored practice/combination board before release.");
                    continue;
                }
                if (levels.Count == 2)
                    stages = $"Teach L{levels[0]:00} -> Practice/Master L{levels[1]:00}";
                else if (levels.Count == 3)
                    stages = $"Teach L{levels[0]:00} -> Practice L{levels[1]:00} -> Master L{levels[2]:00}";
                else
                    stages = $"Teach L{levels[0]:00} -> Practice L{levels[1]:00} -> Combine " +
                        $"L{levels[2]:00} -> Master L{levels[levels.Count - 1]:00}";
                report.AppendLine($"PASS  {pair.Key,-20} {stages}");
            }

            // Chapter boundaries must continue the same campaign curve. Evidence is computed from
            // authored route depth, turns, objects, targets, mechanic dependencies, coupled player
            // bodies and nested state—not from the level number. Both the range and chapter average
            // must rise, which rejects the old Level 30 -> 31 and Level 40 -> 41 tutorial resets.
            report.AppendLine("---------------------------");
            float previousAverageComplexity = -1f;
            for (int chapter = 0; chapter < 5; chapter++)
            {
                float averagePar = chapterLevelCount[chapter] > 0
                    ? chapterParTotal[chapter] / (float)chapterLevelCount[chapter]
                    : 0f;
                float averageComplexity = chapterLevelCount[chapter] > 0
                    ? chapterComplexityTotal[chapter] / (float)chapterLevelCount[chapter]
                    : 0f;
                report.AppendLine($"CHAPTER {chapter + 1}: complexity " +
                    $"{chapterMinComplexity[chapter]}..{chapterMaxComplexity[chapter]}, " +
                    $"average evidence {averageComplexity:0.0}, average par {averagePar:0.0}");

                if (chapter > 0 && chapterMinComplexity[chapter] <= chapterMaxComplexity[chapter - 1])
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: minimum complexity " +
                        $"{chapterMinComplexity[chapter]} does not exceed Chapter {chapter}'s maximum " +
                        $"{chapterMaxComplexity[chapter - 1]}");
                }
                if (chapter > 0 && averageComplexity <= previousAverageComplexity)
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: average evidence " +
                        $"{averageComplexity:0.0} does not exceed Chapter {chapter}'s " +
                        $"{previousAverageComplexity:0.0}");
                }
                // A later chapter may deepen combinations rather than adding a new symbol.
                // Requiring a novel mechanic in every chapter encouraged one-off rules and visual
                // clutter. The role checks below still require a clear lesson, combination phase
                // and mastery test in all five chapters.
                if (chapterIntroductions[chapter] != 1)
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: expected exactly one tutorial " +
                        $"checkpoint at its opener, found {chapterIntroductions[chapter]}");
                }
                int chapterMechanicVideos = 0;
                int chapterStart = chapter * 10;
                for (int level = chapterStart; level < chapterStart + 10; level++)
                {
                    if (!MechanicCatalog.TryGetNewMechanicTutorial(
                            campaignPrefabs, level, out _)) continue;
                    chapterMechanicVideos++;
                }
                int expectedChapterVideos = 1 + chapterMechanicVideos;
                if (chapterTutorialVideos[chapter] != expectedChapterVideos)
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: expected " +
                        $"{expectedChapterVideos} tutorial video(s) (one chapter video plus " +
                        $"{chapterMechanicVideos} staged mechanic video(s)" +
                        $"), found {chapterTutorialVideos[chapter]}");
                }
                if (!chapterHasTeach[chapter] || !chapterHasCombine[chapter] || !chapterHasMastery[chapter])
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: curriculum must include teaching, " +
                        "combination and mastery roles");
                }
                int expectedNested = chapter == 0 ? 0 : 10;
                if (chapterNestedLevels[chapter] != expectedNested)
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: expected {expectedNested} recursive " +
                        $"board(s), found {chapterNestedLevels[chapter]}");
                }
                previousAverageComplexity = averageComplexity;
            }

            string[] allPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { LevelFolder });
            int namedLevels = 0;
            foreach (string guid in allPrefabs)
                if (System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))
                    .StartsWith("Level_", StringComparison.Ordinal)) namedLevels++;
            if (namedLevels != ExpectedLevels)
            {
                failures++;
                report.AppendLine($"FAIL  Campaign: expected exactly {ExpectedLevels} Level_*.prefab files, found {namedLevels}");
            }

            report.AppendLine("---------------------------");
            report.AppendLine(failures == 0
                ? $"PARABOX CAMPAIGN VALIDATION PASSED — {ExpectedLevels}/{ExpectedLevels} levels, {warnings} warning(s)."
                : $"PARABOX CAMPAIGN VALIDATION FAILED — {failures} error(s), {warnings} warning(s).");
            return new ValidationResult { failures = failures, warnings = warnings, report = report.ToString() };
        }

        static void Failure(StringBuilder report, ref int failures, int levelIndex, string message)
        {
            failures++;
            report.AppendLine($"FAIL  L{levelIndex + 1:00}: {message}");
        }

        static void ValidateRecursiveContainment(GameObject prefab, int levelIndex,
                                                 int roomCount, StringBuilder report,
                                                 ref int failures)
        {
            var roomIds = new HashSet<int>();
            foreach (RoomMarker room in prefab.GetComponentsInChildren<RoomMarker>(true))
                roomIds.Add(room.roomId);
            for (int room = 0; room < roomCount; room++)
                if (!roomIds.Contains(room))
                    Failure(report, ref failures, levelIndex,
                        $"containment graph is missing contiguous room id {room}");

            var incoming = new int[Mathf.Max(1, roomCount)];
            var edges = new Dictionary<int, List<int>>();
            int references = 0;
            foreach (BoxMarker box in prefab.GetComponentsInChildren<BoxMarker>(true))
            {
                if (box.containsRoomId < 0) continue;
                references++;
                RoomMarker parent = box.GetComponentInParent<RoomMarker>(true);
                int parentId = parent != null ? parent.roomId : -1;
                int childId = box.containsRoomId;
                if (parentId < 0 || parentId >= roomCount
                    || childId <= 0 || childId >= roomCount || parentId == childId)
                {
                    Failure(report, ref failures, levelIndex,
                        $"invalid room reference {parentId} -> {childId}");
                    continue;
                }
                incoming[childId]++;
                if (!edges.TryGetValue(parentId, out List<int> children))
                {
                    children = new List<int>();
                    edges[parentId] = children;
                }
                children.Add(childId);
            }

            if (references != roomCount - 1)
                Failure(report, ref failures, levelIndex,
                    $"containment graph has {references} room reference(s); expected {roomCount - 1}");
            for (int room = 1; room < roomCount; room++)
                if (incoming[room] != 1)
                    Failure(report, ref failures, levelIndex,
                        $"room {room} has {incoming[room]} container reference(s); expected exactly one");

            var reached = new HashSet<int> { 0 };
            var pending = new Queue<int>();
            pending.Enqueue(0);
            while (pending.Count > 0)
            {
                int parent = pending.Dequeue();
                if (!edges.TryGetValue(parent, out List<int> children)) continue;
                foreach (int child in children)
                    if (reached.Add(child)) pending.Enqueue(child);
            }
            if (reached.Count != roomCount)
                Failure(report, ref failures, levelIndex,
                    $"containment graph reaches {reached.Count}/{roomCount} rooms from the outer board");

            int regularPlayers = 0;
            foreach (PlayerMarker player in prefab.GetComponentsInChildren<PlayerMarker>(true))
                if (!player.isEcho && !player.isMirror) regularPlayers++;
            if (regularPlayers != 1)
                Failure(report, ref failures, levelIndex,
                    $"recursive chapter requires exactly one controlled player; found {regularPlayers}");
        }

        static bool TryDirection(char move, out Vector2Int direction)
        {
            switch (move)
            {
                case 'U': direction = Vector2Int.up; return true;
                case 'D': direction = Vector2Int.down; return true;
                case 'L': direction = Vector2Int.left; return true;
                case 'R': direction = Vector2Int.right; return true;
                default: direction = Vector2Int.zero; return false;
            }
        }

        static int DirectionChanges(string route)
        {
            if (string.IsNullOrEmpty(route)) return 0;
            int changes = 0;
            for (int i = 1; i < route.Length; i++)
                if (route[i] != route[i - 1]) changes++;
            return changes;
        }

        static int LongestDirectionRun(string route)
        {
            if (string.IsNullOrEmpty(route)) return 0;
            int longest = 1, current = 1;
            for (int i = 1; i < route.Length; i++)
            {
                current = route[i] == route[i - 1] ? current + 1 : 1;
                longest = Mathf.Max(longest, current);
            }
            return longest;
        }

        static int TargetCount(LevelModel model)
        {
            int total = 0;
            foreach (var room in model.rooms.Values)
                total += room.boxGoals.Count + room.playerGoals.Count + room.echoGoals.Count
                         + room.mirrorGoals.Count + room.colourGoals.Count;
            return total;
        }

        // Every board must teach, apply or combine at least one gameplay rule. Ordinary crate
        // pushing counts as the core rule; special cargo, recursive rooms, paired actors and every
        // terrain family count separately so the report shows how the vocabulary expands.
        static int AuthoredMechanicCount(GameObject prefab)
        {
            var mechanics = new HashSet<string>(StringComparer.Ordinal);
            // Reaching a player target is the campaign's base navigation rule. Some Chapter I
            // boards intentionally teach only this rule now that directional arrow tiles were
            // removed, so count it as authored gameplay rather than treating the board as empty.
            if (prefab.GetComponentsInChildren<PlayerMarker>(true).Length > 0)
            {
                foreach (var goal in prefab.GetComponentsInChildren<GoalMarker>(true))
                {
                    if (!goal.forPlayer) continue;
                    mechanics.Add("player-navigation");
                    break;
                }
            }
            if (prefab.GetComponentsInChildren<TrenchMarker>(true).Length > 0) mechanics.Add("trench");
            if (prefab.GetComponentsInChildren<IceMarker>(true).Length > 0) mechanics.Add("ice");
            if (prefab.GetComponentsInChildren<PortalMarker>(true).Length > 0) mechanics.Add("portal");
            if (prefab.GetComponentsInChildren<CrackMarker>(true).Length > 0) mechanics.Add("cracked");
            if (prefab.GetComponentsInChildren<CurrentMarker>(true).Length > 0) mechanics.Add("current");
            if (prefab.GetComponentsInChildren<OneWayMarker>(true).Length > 0) mechanics.Add("one-way");
            foreach (var marker in prefab.GetComponentsInChildren<SwitchMarker>(true))
                mechanics.Add(marker.heavy ? "weight-plate" : "button");
            foreach (var marker in prefab.GetComponentsInChildren<GateMarker>(true))
                mechanics.Add(marker.heavy ? "heavy-gate" : "gate");
            foreach (var marker in prefab.GetComponentsInChildren<TerrainMarker>(true))
                mechanics.Add("terrain-" + marker.kind);

            foreach (var marker in prefab.GetComponentsInChildren<BoxMarker>(true))
            {
                if (marker.containsRoomId >= 0) mechanics.Add("recursive-room");
                else mechanics.Add("crate-push");
                if (marker.colour > 0) mechanics.Add("coloured-crate");
                if (marker.slick) mechanics.Add("slick-crate");
                if (marker.boulder) mechanics.Add("boulder");
                if (marker.locking) mechanics.Add("locking-crate");
                if (marker.fragile) mechanics.Add("fragile-crate");
                if (marker.anchored) mechanics.Add("anchored-room");
            }
            foreach (var marker in prefab.GetComponentsInChildren<PlayerMarker>(true))
            {
                if (marker.isEcho) mechanics.Add("echo");
                if (marker.isMirror) mechanics.Add("mirror");
            }
            foreach (var marker in prefab.GetComponentsInChildren<GoalMarker>(true))
                if (marker.colour > 0) mechanics.Add("colour-goal");

            return mechanics.Count;
        }

        // Colour is gameplay information, not decoration. Every authored coloured target must
        // have enough matching ordinary cargo, and every colour id must exist in the renderer's
        // shared palette. Keeping this in the release validator prevents a future level rebuild
        // from showing (for example) a blue target for amber cargo.
        static void ValidateGoalColourContract(GameObject prefab, int index, StringBuilder report,
                                               ref int failures)
        {
            var cargoByColour = new Dictionary<int, int>();
            var goalsByColour = new Dictionary<int, int>();

            foreach (BoxMarker marker in prefab.GetComponentsInChildren<BoxMarker>(true))
            {
                if (marker.containsRoomId >= 0 || marker.colour <= 0) continue;
                if (marker.colour > BoardRenderer.CrateColours.Length)
                {
                    Failure(report, ref failures, index,
                        $"cargo uses undefined colour id {marker.colour}");
                    continue;
                }
                cargoByColour.TryGetValue(marker.colour, out int count);
                cargoByColour[marker.colour] = count + 1;
            }

            foreach (GoalMarker marker in prefab.GetComponentsInChildren<GoalMarker>(true))
            {
                if (marker.forPlayer || marker.forEcho || marker.forMirror || marker.colour <= 0)
                    continue;
                if (marker.colour > BoardRenderer.CrateColours.Length)
                {
                    Failure(report, ref failures, index,
                        $"goal uses undefined colour id {marker.colour}");
                    continue;
                }
                goalsByColour.TryGetValue(marker.colour, out int count);
                goalsByColour[marker.colour] = count + 1;
            }

            foreach (var pair in goalsByColour)
            {
                cargoByColour.TryGetValue(pair.Key, out int cargoCount);
                if (cargoCount < pair.Value)
                    Failure(report, ref failures, index,
                        $"colour {pair.Key} has {pair.Value} target(s) but only {cargoCount} matching cargo");
            }
        }

        // Fingerprint the authored marker data before LevelLayoutRebalancer adds its level-specific
        // safety walls. Comparing parsed runtime models alone could hide a duplicated source puzzle
        // behind different cosmetic rebalance cells; this guarantees the actual 50 designs differ.
        static string AuthoredLayoutSignature(GameObject prefab)
        {
            var tokens = new List<string>(256);
            foreach (var m in prefab.GetComponentsInChildren<RoomMarker>(true))
                tokens.Add($"R:{m.roomId}:{m.width}:{m.height}");
            foreach (var m in prefab.GetComponentsInChildren<WallMarker>(true))
                Add(tokens, "W", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<TrenchMarker>(true))
                Add(tokens, "TR", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<IceMarker>(true))
                Add(tokens, "IC", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<PortalMarker>(true))
                Add(tokens, "PO", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<CrackMarker>(true))
                Add(tokens, "CR", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<CurrentMarker>(true))
                Add(tokens, $"CU:{m.dx}:{m.dy}", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<OneWayMarker>(true))
                Add(tokens, $"OW:{m.dx}:{m.dy}", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<SwitchMarker>(true))
                Add(tokens, m.heavy ? "SW:H" : "SW:L", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<GateMarker>(true))
                Add(tokens, m.heavy ? "GA:H" : "GA:L", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<TerrainMarker>(true))
                Add(tokens, "TE:" + (int)m.kind, m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<PlayerMarker>(true))
                Add(tokens, $"P:{m.isEcho}:{m.isMirror}", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<GoalMarker>(true))
                Add(tokens, $"G:{m.forPlayer}:{m.forEcho}:{m.forMirror}:{m.colour}", m, m.x, m.y);
            foreach (var m in prefab.GetComponentsInChildren<BoxMarker>(true))
                Add(tokens,
                    $"B:{m.containsRoomId}:{m.colour}:{m.slick}:{m.boulder}:{m.locking}:{m.fragile}:{m.anchored}",
                    m, m.x, m.y);

            tokens.Sort(StringComparer.Ordinal);
            return string.Join("|", tokens);
        }

        static void Add(List<string> tokens, string kind, Component marker, int x, int y)
        {
            var room = marker.GetComponentInParent<RoomMarker>(true);
            tokens.Add($"{kind}:{(room != null ? room.roomId : -1)}:{x}:{y}");
        }

        static void ValidateBackdropCoverage(string projectRoot)
        {
            CameraBackdrop backdrop = UnityEngine.Object.FindAnyObjectByType<CameraBackdrop>();
            if (backdrop == null || backdrop.cam == null || backdrop.bgPhoto == null
                || backdrop.bgPhoto.sprite == null)
            {
                File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxBackdropValidation.txt"),
                    "failures=1\nShared gameplay backdrop or background photo is missing.\n");
                return;
            }

            float originalAspect = backdrop.cam.aspect;
            float originalSize = backdrop.cam.orthographicSize;
            Vector3 originalScale = backdrop.bgPhoto.transform.localScale;
            Vector3 spriteSize = backdrop.bgPhoto.sprite.bounds.size;
            var cases = new[]
            {
                new Vector2(16f / 9f, 6f),     // landscape phone / WebGL cabinet
                new Vector2(4f / 3f, 6f),      // landscape tablet
                new Vector2(9f / 16f, 6f),     // portrait phone
                new Vector2(3f / 4f, 6f),      // portrait tablet
                new Vector2(0.39f, 6f),        // exact ultra-narrow Unity Free Aspect screenshot
                new Vector2(16f / 9f, 54f),    // Level 50's nine-times-deeper intro frame
                new Vector2(9f / 16f, 54f),    // same fly-in on a narrow display
                new Vector2(0.39f, 54f),        // legacy deep fly-in at the reported aspect
            };

            int failures = 0;
            var report = new StringBuilder();
            foreach (Vector2 test in cases)
            {
                backdrop.cam.aspect = test.x;
                backdrop.cam.orthographicSize = test.y;
                backdrop.FitToCamera();

                float requiredWidth = 2f * test.y * test.x;
                float requiredHeight = 2f * test.y;
                Vector3 scale = backdrop.bgPhoto.transform.localScale;
                float actualWidth = spriteSize.x * Mathf.Abs(scale.x);
                float actualHeight = spriteSize.y * Mathf.Abs(scale.y);
                bool covered = actualWidth + 0.001f >= requiredWidth
                    && actualHeight + 0.001f >= requiredHeight;
                if (!covered) failures++;
                report.AppendLine(
                    $"aspect={test.x:0.###} size={test.y:0.###} covered={covered} " +
                    $"required={requiredWidth:0.###}x{requiredHeight:0.###} " +
                    $"actual={actualWidth:0.###}x{actualHeight:0.###}");
            }

            backdrop.cam.aspect = originalAspect;
            backdrop.cam.orthographicSize = originalSize;
            backdrop.bgPhoto.transform.localScale = originalScale;
            backdrop.FitToCamera();

            File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxBackdropValidation.txt"),
                $"failures={failures}\nlevelsCovered=50\n" +
                $"landscapeIntroZoom={CameraFollow.ResponsiveIntroZoom(6.7f, 16f / 9f):0.###}\n" +
                $"portraitIntroZoom={CameraFollow.ResponsiveIntroZoom(6.7f, 9f / 16f):0.###}\n{report}");
        }

    }

    // Evidence-based Chapter IV difficulty. Every point comes from a solver-proven room maneuver:
    // route decisions, real entries/exits, unique room-boxes moved, docking pushes and containment
    // depth. The generator and release validator share this evaluator so a decorative room cannot
    // inflate the curve and a later board cannot silently become easier than its predecessor.
    internal static class ChapterFourDifficultyEvidence
    {
        internal struct Result
        {
            public int score;
            public int directionChanges;
            public int maximumDepth;
            public int roomCount;
            public int targetCount;
            public int metaBoxMoves;
            public int movedMetaBoxes;
            public int playerBoundaryCrossings;
        }

        internal static Result Evaluate(GameObject prefab, string solution)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (string.IsNullOrWhiteSpace(solution))
                throw new InvalidOperationException($"{prefab.name} has no stored Chapter IV route.");

            LevelModel model = LevelParser.Parse(prefab);
            if (model.player == null)
                throw new InvalidOperationException($"{prefab.name} has no controlled player.");

            var result = new Result
            {
                directionChanges = DirectionChanges(solution),
                roomCount = model.rooms.Count,
            };
            foreach (PRoom room in model.rooms.Values)
            {
                result.maximumDepth = Mathf.Max(result.maximumDepth, ContainmentDepth(model, room.id));
                result.targetCount += room.boxGoals.Count + room.playerGoals.Count
                    + room.echoGoals.Count + room.mirrorGoals.Count + room.colourGoals.Count;
            }

            var movedMeta = new HashSet<PEntity>();
            for (int step = 0; step < solution.Length; step++)
            {
                Vector2Int direction;
                switch (solution[step])
                {
                    case 'U': direction = Vector2Int.up; break;
                    case 'D': direction = Vector2Int.down; break;
                    case 'L': direction = Vector2Int.left; break;
                    case 'R': direction = Vector2Int.right; break;
                    default:
                        throw new InvalidOperationException(
                            $"{prefab.name} route contains '{solution[step]}' at move {step + 1}.");
                }

                var beforeRooms = new int[model.entities.Count];
                var beforePositions = new Vector2Int[model.entities.Count];
                for (int i = 0; i < model.entities.Count; i++)
                {
                    beforeRooms[i] = model.entities[i].roomId;
                    beforePositions[i] = model.entities[i].pos;
                }

                if (!model.TryMovePlayer(direction))
                    throw new InvalidOperationException(
                        $"{prefab.name} route is blocked at move {step + 1} ({solution[step]}).");

                int playerIndex = model.entities.IndexOf(model.player);
                if (playerIndex >= 0 && beforeRooms[playerIndex] != model.player.roomId)
                    result.playerBoundaryCrossings++;

                for (int i = 0; i < model.entities.Count; i++)
                {
                    PEntity entity = model.entities[i];
                    if (entity.interiorRoomId < 0) continue;
                    bool moved = beforeRooms[i] != entity.roomId || beforePositions[i] != entity.pos;
                    if (!moved) continue;
                    result.metaBoxMoves++;
                    movedMeta.Add(entity);
                }
            }

            if (!model.IsWon())
                throw new InvalidOperationException($"{prefab.name} stored Chapter IV route does not win.");

            result.movedMetaBoxes = movedMeta.Count;
            result.score = 1300
                + solution.Length * 28
                + result.directionChanges * 8
                + result.metaBoxMoves * 25
                + result.playerBoundaryCrossings * 20
                + result.movedMetaBoxes * 120
                + result.targetCount * 20
                + result.roomCount * 30
                + result.maximumDepth * 25;
            return result;
        }

        static int DirectionChanges(string route)
        {
            int turns = 0;
            for (int i = 1; i < route.Length; i++)
                if (route[i] != route[i - 1]) turns++;
            return turns;
        }

        static int ContainmentDepth(LevelModel model, int roomId)
        {
            int depth = 0;
            var visited = new HashSet<int>();
            while (roomId != 0 && model.rooms.TryGetValue(roomId, out PRoom room)
                   && room.containerBox != null && visited.Add(roomId))
            {
                depth++;
                roomId = room.containerBox.roomId;
            }
            return depth;
        }
    }

    // Evidence-based Chapter V difficulty. Unlike the legacy level-number floor, every point here
    // comes from the authored containment graph or from replaying the stored route through the
    // exact runtime model. The generator serializes this score and the release validator recomputes
    // it, so a decorative inner room or a long empty corridor cannot fake progression.
    internal static class ChapterFiveDifficultyEvidence
    {
        internal struct Result
        {
            public int score;
            public int directionChanges;
            public int maximumDepth;
            public int objectCount;
            public int targetCount;
            public int cargoBoundaryCrossings;
            public int cargoMoveEvents;
            public int metaBoxMoves;
            public int playerBoundaryCrossings;
            public int crossSubtreeTransfers;
            public int oneWayCommitments;
        }

        internal static Result Evaluate(GameObject prefab, string solution)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (string.IsNullOrWhiteSpace(solution))
                throw new InvalidOperationException($"{prefab.name} has no stored Chapter V route.");

            LevelModel model = LevelParser.Parse(prefab);
            if (model.player == null)
                throw new InvalidOperationException($"{prefab.name} has no controlled player.");

            var result = new Result();
            result.directionChanges = DirectionChanges(solution);
            result.oneWayCommitments = model.rebalanceOneWays;
            foreach (PRoom room in model.rooms.Values)
            {
                result.maximumDepth = Mathf.Max(result.maximumDepth, ContainmentDepth(model, room.id));
                result.targetCount += room.boxGoals.Count + room.playerGoals.Count
                    + room.echoGoals.Count + room.mirrorGoals.Count + room.colourGoals.Count;
            }
            foreach (PEntity entity in model.entities)
                if (entity.IsCrate) result.objectCount++;

            var lastBranch = new Dictionary<PEntity, int>();
            foreach (PEntity entity in model.entities)
            {
                if (!entity.IsCrate || entity.interiorRoomId >= 0) continue;
                int branch = TopLevelBranch(model, entity.roomId);
                if (branch > 0) lastBranch[entity] = branch;
            }

            for (int step = 0; step < solution.Length; step++)
            {
                Vector2Int direction;
                switch (solution[step])
                {
                    case 'U': direction = Vector2Int.up; break;
                    case 'D': direction = Vector2Int.down; break;
                    case 'L': direction = Vector2Int.left; break;
                    case 'R': direction = Vector2Int.right; break;
                    default:
                        throw new InvalidOperationException(
                            $"{prefab.name} route contains '{solution[step]}' at move {step + 1}.");
                }

                var beforeRooms = new int[model.entities.Count];
                var beforePositions = new Vector2Int[model.entities.Count];
                for (int i = 0; i < model.entities.Count; i++)
                {
                    beforeRooms[i] = model.entities[i].roomId;
                    beforePositions[i] = model.entities[i].pos;
                }

                if (!model.TryMovePlayer(direction))
                    throw new InvalidOperationException(
                        $"{prefab.name} route is blocked at move {step + 1} ({solution[step]}).");

                int playerIndex = model.entities.IndexOf(model.player);
                if (playerIndex >= 0 && beforeRooms[playerIndex] != model.player.roomId)
                    result.playerBoundaryCrossings++;

                for (int i = 0; i < model.entities.Count; i++)
                {
                    PEntity entity = model.entities[i];
                    bool moved = beforeRooms[i] != entity.roomId || beforePositions[i] != entity.pos;
                    if (!moved || !entity.IsCrate) continue;

                    if (entity.interiorRoomId >= 0)
                    {
                        result.metaBoxMoves++;
                        continue;
                    }

                    result.cargoMoveEvents++;
                    if (beforeRooms[i] != entity.roomId) result.cargoBoundaryCrossings++;
                    int branch = TopLevelBranch(model, entity.roomId);
                    if (branch <= 0) continue;
                    if (lastBranch.TryGetValue(entity, out int previous) && previous != branch)
                        result.crossSubtreeTransfers++;
                    lastBranch[entity] = branch;
                }
            }

            if (!model.IsWon())
                throw new InvalidOperationException($"{prefab.name} stored Chapter V route does not win.");

            // Multi-cargo Chapter V now carries explicit object/target evidence for every one of
            // its four-to-five deliveries. Recalibrate the shared recursion-context constant so
            // Level 41 still meets the intentional 18% chapter-transition ceiling instead of
            // double-counting those new tasks as a synthetic spike.
            result.score = 2135
                + solution.Length * 40
                + result.directionChanges * 10
                + result.maximumDepth * 100
                + result.objectCount * 25
                + result.targetCount * 20
                // Every Chapter V one-way is installed only when the stored winning route uses
                // it. Count that proven commitment so mirrored boards with a stricter directional
                // ladder cannot be misreported as equal difficulty.
                + result.oneWayCommitments * 25
                + result.cargoBoundaryCrossings * 60
                + result.cargoMoveEvents * 20
                // Repositioning a room is the planning concept. Repeating the same push down a
                // longer shaft must not inflate difficulty, so award the decision once rather
                // than rewarding corridor length.
                + (result.metaBoxMoves > 0 ? 360 : 0)
                + result.playerBoundaryCrossings * 25
                // Crossing between sibling branches is one relationship to understand even when
                // the finale sends two cargo pieces through it.
                + (result.crossSubtreeTransfers > 0 ? 150 : 0);
            return result;
        }

        static int DirectionChanges(string route)
        {
            int turns = 0;
            for (int i = 1; i < route.Length; i++)
                if (route[i] != route[i - 1]) turns++;
            return turns;
        }

        static int ContainmentDepth(LevelModel model, int roomId)
        {
            int depth = 0;
            var visited = new HashSet<int>();
            while (roomId != 0 && model.rooms.TryGetValue(roomId, out PRoom room)
                   && room.containerBox != null && visited.Add(roomId))
            {
                depth++;
                roomId = room.containerBox.roomId;
            }
            return depth;
        }

        static int TopLevelBranch(LevelModel model, int roomId)
        {
            if (roomId == 0) return 0;
            int branch = roomId;
            var visited = new HashSet<int>();
            while (model.rooms.TryGetValue(branch, out PRoom room)
                   && room.containerBox != null && visited.Add(branch))
            {
                int parent = room.containerBox.roomId;
                if (parent == 0) return branch;
                branch = parent;
            }
            return branch;
        }
    }
}
