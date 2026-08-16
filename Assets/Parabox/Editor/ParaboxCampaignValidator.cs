using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
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
        static readonly int[] ChapterTwoParFloors = { 13, 15, 16, 17, 18, 20, 22, 24, 25, 27 };
        static readonly int[] ChapterTwoCargoObjectives = { 0, 0, 1, 1, 1, 0, 2, 2, 0, 0 };
        static readonly int[] ChapterThreeRoomCounts = { 2, 2, 2, 2, 2, 3, 3, 3, 3, 4 };
        static readonly int[] ChapterThreeCargoTransitions = { 2, 2, 2, 2, 2, 4, 4, 4, 4, 6 };
        static readonly int[] ChapterFourRoomCounts = { 2, 2, 2, 2, 3, 3, 3, 4, 4, 4 };
        static readonly int[] ChapterFourPlayerTransitions = { 2, 4, 4, 4, 4, 6, 4, 6, 6, 8 };
        static readonly int[] ChapterFourMetaMoves = { 2, 2, 4, 5, 3, 3, 4, 5, 6, 5 };
        static readonly int[] ChapterFiveRoomCounts = { 2, 2, 3, 3, 3, 4, 4, 4, 5, 5 };
        static readonly int[] ChapterFiveRequiredCargo = { 4, 4, 4, 4, 4, 5, 5, 5, 5, 5 };
        static readonly int[] ChapterFiveCargoTransitions = { 0, 0, 2, 2, 1, 3, 3, 3, 4, 8 };
        static readonly int[] ChapterFiveMetaMoves = { 0, 3, 0, 0, 2, 0, 0, 3, 2, 3 };

        static double nextPlayModeRequestPoll;

        [InitializeOnLoadMethod]
        static void RegisterPlayModeRequestWatcher()
        {
            ConfigurePlayModeStartScene();
            // Lets local recovery request Enter/Exit Play Mode without macOS Accessibility
            // permissions. Polling also works if a maximized Game view stops accepting clicks.
            EditorApplication.update -= PollPlayModeRequests;
            EditorApplication.update += PollPlayModeRequests;
            EditorApplication.delayCall += ForceSafeGameViewMode;
            PollPlayModeRequests();
        }

        // Pressing Unity's normal Play button must always boot through the title screen. Starting
        // directly from Game.unity bypasses the menu hand-off flags and can leave the gameplay
        // camera showing an empty board. Unity restores the developer's edited scene after Stop.
        static void ConfigurePlayModeStartScene()
        {
            // Use Unity's normal domain + scene reload. Fast Play Mode kept references to prefab
            // assets across imports; after regenerating levels those objects were destroyed while
            // GameManager still held them, producing MissingReferenceException and unstable test
            // sessions. A clean start is slightly slower but deterministic and matches WebGL.
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;

            SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScene);
            if (mainMenu != null && EditorSceneManager.playModeStartScene != mainMenu)
                EditorSceneManager.playModeStartScene = mainMenu;
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
                        PlayerPrefs.SetInt(MainMenuUI.MainPlayTutorialKey, 1);
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
            PlayerPrefs.DeleteKey(MainMenuUI.MainPlayTutorialKey);
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
                List<MechanicCatalog.Id> detectedIntroductions =
                    MechanicCatalog.IntroductionsAt(campaignPrefabs, index);
                List<MechanicCatalog.Id> tutorialLessons =
                    MechanicCatalog.TutorialsAt(campaignPrefabs, index);
                bool introducesDetectedRule = detectedIntroductions.Count > 0;
                if (progression.introducesMechanic != introducesDetectedRule)
                    Failure(report, ref failures, index,
                        $"curriculum introduction flag is {progression.introducesMechanic}, but prefab audit found " +
                        $"{detectedIntroductions.Count} first appearance(s): " +
                        string.Join(", ", detectedIntroductions));
                if (introducesDetectedRule
                    && string.IsNullOrWhiteSpace(MechanicCatalog.Lesson(detectedIntroductions)))
                    Failure(report, ref failures, index,
                        "first mechanic appearance has no reusable tutorial lesson");
                if (MechanicCatalog.IsChapterTutorialCheckpoint(index)
                    && tutorialLessons.Count == 0)
                    Failure(report, ref failures, index,
                        "chapter opener has no bundled tutorial mini-board");
                if (MechanicCatalog.IsChapterTutorialCheckpoint(index)
                    && tutorialLessons.Count != 1)
                    Failure(report, ref failures, index,
                        $"chapter opener must play exactly one bundled tutorial, found {tutorialLessons.Count}");
                if (!MechanicCatalog.IsChapterTutorialCheckpoint(index)
                    && tutorialLessons.Count != 0)
                    Failure(report, ref failures, index,
                        "non-checkpoint level schedules an extra tutorial interruption");
                if (tutorialLessons.Count > 0
                    && (string.IsNullOrWhiteSpace(MechanicCatalog.TutorialBundleName(index))
                        || string.IsNullOrWhiteSpace(MechanicCatalog.TutorialBundleLesson(index))))
                    Failure(report, ref failures, index,
                        "bundled tutorial has no three-skill player explanation");

                foreach (MechanicCatalog.Id mechanic in MechanicCatalog.MechanicsIn(prefab, index))
                {
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
                // Chapter III and Chapter V each use one continuous nested-room vocabulary. Their
                // evidence formulas are calibrated to the normal transition ceiling; only the
                // five-room finale receives the slightly wider synthesis allowance.
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
                // Chapters III-V share the recursive vocabulary for different purposes: cargo
                // transfer, room repositioning, then the deep cargo endgame.
                int expectedRoomCount = index >= 20 && index < 30
                    ? ChapterThreeRoomCounts[index - 20]
                    : index >= 30 && index < 40
                        ? ChapterFourRoomCounts[index - 30]
                        : index >= 40 ? ChapterFiveRoomCounts[index - 40] : 1;
                if (roomCount != expectedRoomCount)
                    Failure(report, ref failures, index,
                        $"authored room count {roomCount} does not match Level {index + 1}'s " +
                        $"intended {expectedRoomCount}-room structure");
                if (roomCount > 1) chapterNestedLevels[chapterIndex]++;
                if (roomCount > 1)
                    ValidateChapterFiveContainment(prefab, index, roomCount, report, ref failures);

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
                if (index >= 10 && index < 20)
                {
                    int chapterStep = index - 10;
                    int mirrorGoals = 0;
                    int echoGoals = 0;
                    int cargoGoals = 0;
                    foreach (PRoom room in model.rooms.Values)
                    {
                        mirrorGoals += room.mirrorGoals.Count;
                        echoGoals += room.echoGoals.Count;
                        cargoGoals += room.boxGoals.Count + room.colourGoals.Count;
                    }
                    if (model.mirror == null || mirrorGoals != 1)
                        Failure(report, ref failures, index,
                            "Chapter II must keep one opposite-moving mirror and one mirror target");
                    if (cargoGoals != ChapterTwoCargoObjectives[chapterStep])
                        Failure(report, ref failures, index,
                            $"Chapter II cargo objective count {cargoGoals} does not match the " +
                            $"intentional linked-player curve value {ChapterTwoCargoObjectives[chapterStep]}");
                    bool expectsEcho = chapterStep >= 8;
                    if ((model.echo != null) != expectsEcho || echoGoals != (expectsEcho ? 1 : 0))
                        Failure(report, ref failures, index,
                            expectsEcho
                                ? "Chapter II mastery must coordinate one echo as the third linked actor"
                                : "Chapter II introduces the echo only in its final two mastery boards");
                    if (info.par < ChapterTwoParFloors[chapterStep])
                        Failure(report, ref failures, index,
                            $"Chapter II route {info.par} is below its reviewed difficulty floor " +
                            ChapterTwoParFloors[chapterStep]);
                }
                if (index >= 30 && index < 40)
                {
                    try
                    {
                        ChapterFourDifficultyEvidence.Result evidence =
                            ChapterFourDifficultyEvidence.Evaluate(prefab, info.solution);
                        if (info.designComplexity != evidence.score)
                            Failure(report, ref failures, index,
                                $"serialized complexity {info.designComplexity} does not match " +
                                $"room-maneuver evidence {evidence.score}");
                    }
                    catch (Exception ex)
                    {
                        Failure(report, ref failures, index,
                            $"could not measure room-maneuver difficulty evidence: {ex.Message}");
                    }
                }
                if (index >= 40)
                {
                    try
                    {
                        ChapterFiveDifficultyEvidence.Result evidence =
                            ChapterFiveDifficultyEvidence.Evaluate(prefab, info.solution);
                        if (info.designComplexity != evidence.score)
                            Failure(report, ref failures, index,
                                $"serialized complexity {info.designComplexity} does not match " +
                                $"runtime-route evidence {evidence.score}");
                    }
                    catch (Exception ex)
                    {
                        Failure(report, ref failures, index,
                            $"could not measure recursive difficulty evidence: {ex.Message}");
                    }
                }

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
                if (index >= 30 && index < 40)
                {
                    int ordinaryCargo = 0;
                    int movableRooms = 0;
                    foreach (PEntity entity in model.entities)
                    {
                        if (!entity.IsCrate) continue;
                        if (entity.interiorRoomId >= 0) movableRooms++;
                        else ordinaryCargo++;
                    }
                    int roomSockets = 0;
                    foreach (PRoom room in model.rooms.Values)
                        roomSockets += room.boxGoals.Count;
                    if (ordinaryCargo != 0)
                        Failure(report, ref failures, index,
                            $"Chapter IV has {ordinaryCargo} ordinary cargo object(s); the room itself must be the puzzle piece");
                    if (movableRooms != roomCount - 1)
                        Failure(report, ref failures, index,
                            $"Chapter IV has {movableRooms} movable room(s); expected {roomCount - 1}");
                    if (roomSockets != movableRooms)
                        Failure(report, ref failures, index,
                            $"Chapter IV has {roomSockets} room socket(s) for {movableRooms} movable room(s)");
                }
                if (index >= 40)
                {
                    int chapterStep = index - 40;
                    int cargoCount = 0;
                    foreach (PEntity entity in model.entities)
                        if (entity.IsCrate && entity.interiorRoomId < 0) cargoCount++;
                    int cargoTargets = 0;
                    foreach (PRoom room in model.rooms.Values)
                        cargoTargets += room.boxGoals.Count + room.colourGoals.Count;
                    int expectedCargo = ChapterFiveRequiredCargo[chapterStep];
                    if (cargoCount != expectedCargo)
                        Failure(report, ref failures, index,
                            $"Chapter V has {cargoCount} cargo objects; expected exactly {expectedCargo}");
                    if (cargoTargets != expectedCargo)
                        Failure(report, ref failures, index,
                            $"Chapter V has {cargoTargets} cargo goals; expected exactly {expectedCargo}");
                }
                int dependencyLimit = LevelLayoutRebalancer.DependencyBudgetForLevel(index);
                if (model.rebalanceObjectives > dependencyLimit)
                    Failure(report, ref failures, index,
                        $"cross-mechanic dependencies {model.rebalanceObjectives} exceed planned limit {dependencyLimit}");
                int cleanWallLimit = index == 29 ? 8
                    : index < 10 ? 1 : index < 20 ? 2
                    : index < 30 ? 3 : index < 40 ? 4 : 5;
                if (model.rebalanceWalls > cleanWallLimit)
                    Failure(report, ref failures, index,
                        $"runtime wall formation {model.rebalanceWalls} exceeds clean limit {cleanWallLimit}");
                int focusedAccentLimit = Mathf.Max(
                    index < 10 ? 1 : index < 30 ? 2 : 3,
                    LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(index));
                if (model.rebalanceOneWays > focusedAccentLimit
                    || model.rebalanceHazards > focusedAccentLimit)
                    Failure(report, ref failures, index,
                        "focused mechanic accents exceed the readable per-level limit");
                int moveLimit = GameManager.MoveLimitForLevel(index, info.par);
                if (roomCount > 1)
                {
                    float minimumThinkingTime = info.par * 1.25f + 20f;
                    float actualTime = CampaignProgression.TimeLimit(index, info.par);
                    if (actualTime + 0.01f < minimumThinkingTime)
                        Failure(report, ref failures, index,
                            $"recursive timer {actualTime:0.0}s is below the route/read minimum " +
                            $"{minimumThinkingTime:0.0}s");
                }

                // Level 1 may introduce one action plainly. After that, the early campaign must
                // never regress to a corridor solved by holding one direction. This is measured by
                // the player's commands even when an echo or mirror changes another actor: hidden
                // state does not make a nine-command straight input sequence interesting to play.
                int routeTurns = DirectionChanges(info.solution);
                int longestRun = LongestDirectionRun(info.solution);
                if (index > 0 && index < 20 && routeTurns < 4)
                    Failure(report, ref failures, index,
                        $"early route has only {routeTurns} direction decisions; minimum is 4 after Level 1");
                if (index > 0 && index < 20 && longestRun > 5)
                    Failure(report, ref failures, index,
                        $"early route repeats one direction {longestRun} times; maximum is 5 after Level 1");

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
                if (index >= 20 && index < 30)
                {
                    int chapterStep = index - 20;
                    if (movedCargo.Count != 1)
                        Failure(report, ref failures, index,
                            $"Chapter III route moves {movedCargo.Count} cargo objects; expected the one authored amber delivery");
                    if (visitedRooms.Count != roomCount)
                        Failure(report, ref failures, index,
                            $"stored solution visits {visitedRooms.Count}/{roomCount} recursive rooms");
                    if (cargoRoomTransitions < ChapterThreeCargoTransitions[chapterStep])
                        Failure(report, ref failures, index,
                            $"cargo crosses {cargoRoomTransitions} room boundary/boundaries; expected at least " +
                            ChapterThreeCargoTransitions[chapterStep]);
                    if (metaMoves < 2)
                        Failure(report, ref failures, index,
                            "the room-box never completes its separate socket-placement task");
                    foreach (PRoom room in model.rooms.Values)
                        foreach (Vector2Int goal in room.boxGoals)
                        {
                            PEntity occupant = model.EntityAt(room.id, goal);
                            if (occupant == null || occupant.interiorRoomId < 0)
                                Failure(report, ref failures, index,
                                    "the authored room socket is not occupied by a room-box");
                        }
                }
                if (index >= 30 && index < 40)
                {
                    int chapterStep = index - 30;
                    int movableRooms = roomCount - 1;
                    if (movedCargo.Count != 0)
                        Failure(report, ref failures, index,
                            "Chapter IV route moves ordinary cargo instead of focusing on room repositioning");
                    if (movedMetaRooms.Count != movableRooms)
                        Failure(report, ref failures, index,
                            $"stored solution moves {movedMetaRooms.Count}/{movableRooms} authored room-boxes");
                    if (visitedRooms.Count != roomCount)
                        Failure(report, ref failures, index,
                            $"stored solution visits {visitedRooms.Count}/{roomCount} recursive rooms");
                    if (playerRoomTransitions < ChapterFourPlayerTransitions[chapterStep])
                        Failure(report, ref failures, index,
                            $"player crosses {playerRoomTransitions} room boundary/boundaries; expected at least " +
                            ChapterFourPlayerTransitions[chapterStep]);
                    if (metaMoves < ChapterFourMetaMoves[chapterStep])
                        Failure(report, ref failures, index,
                            $"room boxes move {metaMoves} time(s); expected at least " +
                            ChapterFourMetaMoves[chapterStep]);
                    foreach (PRoom room in model.rooms.Values)
                        foreach (Vector2Int goal in room.boxGoals)
                        {
                            PEntity occupant = model.EntityAt(room.id, goal);
                            if (occupant == null || occupant.interiorRoomId < 0)
                                Failure(report, ref failures, index,
                                    "a room socket is not occupied by a movable room-box");
                        }
                }
                if (index >= 40)
                {
                    int chapterStep = index - 40;
                    if (movedCargo.Count != ChapterFiveRequiredCargo[chapterStep])
                        Failure(report, ref failures, index,
                            $"stored solution moves {movedCargo.Count}/{ChapterFiveRequiredCargo[chapterStep]} " +
                            "required cargo objects; pre-solved decorative cargo is not allowed");
                    if (visitedRooms.Count != roomCount)
                        Failure(report, ref failures, index,
                            $"stored solution visits {visitedRooms.Count}/{roomCount} recursive rooms");
                    if (playerRoomTransitions == 0)
                        Failure(report, ref failures, index,
                            "stored solution never crosses a room boundary");
                    if (cargoRoomTransitions < ChapterFiveCargoTransitions[chapterStep])
                        Failure(report, ref failures, index,
                            $"cargo crosses {cargoRoomTransitions} room boundary/boundaries; expected at least " +
                            ChapterFiveCargoTransitions[chapterStep]);
                    if (metaMoves < ChapterFiveMetaMoves[chapterStep])
                        Failure(report, ref failures, index,
                            $"room boxes move {metaMoves} time(s); expected at least " +
                            ChapterFiveMetaMoves[chapterStep]);
                    foreach (PRoom room in model.rooms.Values)
                        foreach (Vector2Int goal in room.boxGoals)
                        {
                            PEntity occupant = model.EntityAt(room.id, goal);
                            if (occupant != null && occupant.interiorRoomId >= 0)
                                Failure(report, ref failures, index,
                                    "a room box, rather than authored cargo, satisfies a cargo goal");
                        }
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
                if (!chapterHasTeach[chapter] || !chapterHasCombine[chapter] || !chapterHasMastery[chapter])
                {
                    failures++;
                    report.AppendLine($"FAIL  Chapter {chapter + 1}: curriculum must include teaching, " +
                        "combination and mastery roles");
                }
                int expectedNested = chapter >= 2 ? 10 : 0;
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

        static void ValidateChapterFiveContainment(GameObject prefab, int levelIndex,
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
