using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // One-click scene authoring for every static menu/gameplay interface object. Run this command
    // after UI changes and before a build. Runtime code may animate and update these objects, but it
    // does not create replacement panels, buttons, badges, hit targets or input components.
    public static class ParaboxPrebuiltUiGenerator
    {
        const string MenuPath = "Assets/Parabox/Scenes/MainMenu.unity";
        const string GamePath = "Assets/Parabox/Scenes/Game.unity";
        const string ReportName = "ParaboxPrebuiltUiValidation.txt";
        const string RequestName = "ParaboxGeneratePrebuiltUi.request";
        const string MenuUiRequestName = "ParaboxBakeMenuUi.request";
        const string GameUiRequestName = "ParaboxBakeGameUi.request";
        const string ValidationRequestName = "ParaboxValidatePrebuiltUi.request";
        const string ValidationResultName = "ParaboxValidatePrebuiltUi.result";
        static double nextRequestPoll;

        // Lets the already-open Editor run the prebuilder after a script recompile. This avoids a
        // second Unity process fighting for the project lock and gives the team a one-file command
        // that is also suitable for automated local repair.
        [InitializeOnLoadMethod]
        static void RegisterRequestWatcher()
        {
            EditorApplication.update -= PollRequest;
            EditorApplication.update += PollRequest;
            PollRequest();
        }

        static void PollRequest()
        {
            if (EditorApplication.timeSinceStartup < nextRequestPoll) return;
            nextRequestPoll = EditorApplication.timeSinceStartup + 0.5d;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string menuUiRequest = Path.Combine(projectRoot, "Library", MenuUiRequestName);
            if (File.Exists(menuUiRequest))
            {
                File.Delete(menuUiRequest);
                try
                {
                    LuxoddArcadeUiSceneBaker.BakeMenuOnlySilent();
                    File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxMenuUiGeneration.result"),
                        "success=1\nOriginal two-button main menu was serialized.\n");
                }
                catch (System.Exception exception)
                {
                    File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxMenuUiGeneration.result"),
                        "success=0\n" + exception + "\n");
                    Debug.LogException(exception);
                }
                return;
            }

            string gameUiRequest = Path.Combine(projectRoot, "Library", GameUiRequestName);
            if (File.Exists(gameUiRequest))
            {
                File.Delete(gameUiRequest);
                try
                {
                    LuxoddArcadeUiSceneBaker.BakeGameOnlySilent();
                    File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxGameUiGeneration.result"),
                        "success=1\nGameplay UI was serialized into Game.unity.\n");
                }
                catch (System.Exception exception)
                {
                    File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxGameUiGeneration.result"),
                        "success=0\n" + exception + "\n");
                    Debug.LogException(exception);
                }
                return;
            }

            string validationRequest = Path.Combine(projectRoot, "Library", ValidationRequestName);
            if (File.Exists(validationRequest))
            {
                File.Delete(validationRequest);
                try
                {
                    ValidateSilent();
                    File.WriteAllText(Path.Combine(projectRoot, "Library", ValidationResultName),
                        "success=1\nMainMenu and Game prebuilt UI, references, input and missing-script checks passed.\n");
                }
                catch (System.Exception exception)
                {
                    File.WriteAllText(Path.Combine(projectRoot, "Library", ValidationResultName),
                        "success=0\n" + exception + "\n");
                    Debug.LogException(exception);
                }
                return;
            }

            string request = Path.Combine(projectRoot, "Library", RequestName);
            if (!File.Exists(request)) return;
            File.Delete(request);

            try
            {
                GenerateSilent();
                File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxPrebuiltUiGeneration.result"),
                    "success=1\nPrebuilt tutorial video UI, controls and button audio are serialized.\n");
            }
            catch (System.Exception exception)
            {
                File.WriteAllText(Path.Combine(projectRoot, "Library", "ParaboxPrebuiltUiGeneration.result"),
                    "success=0\n" + exception + "\n");
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Parabox/Generate Prebuilt UI (Run This)", priority = 1)]
        public static void GenerateFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Parabox", "Exit Play Mode before generating the UI.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                GenerateSilent();
                EditorUtility.DisplayDialog("Parabox",
                    "Prebuilt UI generated and saved.\n\n" +
                    "MainMenu.unity and Game.unity passed validation. Runtime now updates the " +
                    "serialized interface instead of creating UI objects.", "OK");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Parabox UI generation failed",
                    exception.Message + "\n\nSee the Console and " + ReportName + ".", "OK");
            }
        }

        // Batch-mode entry point for CI and command-line verification.
        public static void GenerateSilent()
        {
            LuxoddArcadeUiSceneBaker.BakeAllSilent();

            ValidateSilent();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void ValidateSilent()
        {
            var problems = new List<string>();
            ValidateMenu(problems);
            ValidateGame(problems);

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportPath = Path.Combine(projectRoot, "Library", ReportName);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            string report = problems.Count == 0
                ? "failures=0\nPASS: All static UI is serialized in MainMenu.unity and Game.unity.\n"
                : "failures=" + problems.Count + "\nFAIL:\n- " + string.Join("\n- ", problems) + "\n";
            File.WriteAllText(reportPath, report);

            if (problems.Count > 0)
                throw new InvalidDataException(string.Join("\n", problems));

            ParaboxLuxoddControlsValidator.ValidateSilent();
            Debug.Log("Parabox prebuilt-UI validation passed. Report: " + reportPath);
        }

        // Focused release gate for the level-complete board. This deliberately ignores unrelated
        // campaign/sound audits so a result-board layout change can be verified independently.
        public static void ValidateWinBoardFromCommandLine()
        {
            var problems = new List<string>();
            WithScene(GamePath, scene =>
            {
                GameManager game = FindComponent<GameManager>(scene);
                if (game == null)
                {
                    problems.Add("Game.unity has no GameManager component.");
                    return;
                }

                RectTransform nextRect = game.nextButton != null
                    ? game.nextButton.transform as RectTransform : null;
                if (nextRect == null || !game.nextButton.gameObject.activeSelf
                    || Mathf.Abs(nextRect.anchoredPosition.x) > 0.01f)
                    problems.Add("NEXT LEVEL is not visible and centred on the result board.");
                if (game.menuButton == null || game.menuButton.gameObject.activeSelf
                    || game.menuButton.interactable)
                    problems.Add("The result-board LEVEL SELECT button is not fully removed.");
                if (game.winCountdownRoot == null || game.winCountdownLabel == null
                    || game.winCountdownBorder == null || game.winCountdownBorder.Length != 4)
                    problems.Add("The four-sided result-board countdown is not serialized.");
                else
                {
                    if (game.winCountdownRoot.gameObject.activeSelf)
                        problems.Add("The countdown border must start hidden before a win.");
                    if (game.winCountdownLabel.text != "NEXT LEVEL IN 5")
                        problems.Add("The countdown label does not start at 5 seconds.");
                    if (game.winCountdownLabel.rectTransform.anchoredPosition.y < 350f)
                        problems.Add("The countdown label is not positioned above the result board.");
                    if (game.winCountdownLabel.fontSize < 30
                        || game.winCountdownLabel.rectTransform.sizeDelta.x < 400f)
                        problems.Add("The countdown label is too small for arcade readability.");
                    for (int i = 0; i < game.winCountdownBorder.Length; i++)
                    {
                        Image segment = game.winCountdownBorder[i];
                        if (segment != null && segment.type == Image.Type.Filled
                            && !segment.raycastTarget) continue;
                        problems.Add("Countdown border segment " + (i + 1)
                            + " is missing or not configured as a non-interactive fill.");
                    }
                }
            });

            if (problems.Count > 0)
                throw new InvalidDataException(string.Join("\n", problems));
            Debug.Log("Parabox win-board audit passed: LEVEL SELECT removed, NEXT LEVEL centred, "
                + "and the 5-second four-sided auto-advance timer is prebuilt.");
        }

        static void ValidateMenu(List<string> problems)
        {
            WithScene(MenuPath, scene =>
            {
                MainMenuUI menu = FindComponent<MainMenuUI>(scene);
                if (menu == null)
                {
                    problems.Add("MainMenu.unity has no MainMenuUI component.");
                    return;
                }

                if (!menu.IsStaticUiPrebuilt)
                    problems.Add("MainMenu timer or one of the 50 completion badges is not prebuilt.");

                ValidateVisibleHomeButton(menu.playButton, "PLAY", problems);
                ValidateVisibleHomeButton(menu.levelsButton, "LEVEL SELECT", problems);
                ValidateHitTarget(menu.levelBoardBackButton, "BACK", problems);
                if (menu.levelButtons == null || menu.levelButtons.Length != 50)
                    problems.Add("MainMenu must contain exactly 50 serialized level buttons.");
                else
                    for (int i = 0; i < menu.levelButtons.Length; i++)
                    {
                        ValidateHitTarget(menu.levelButtons[i], "Level " + (i + 1), problems);
                        ValidateLevelSelector(menu.levelButtons[i], i + 1, problems);
                    }
                ValidateCampaignPrefabOrder(menu.levelPrefabs, "MainMenu", problems);

                ValidateButtonSoundComponents(scene, "MainMenu", problems);
                ValidateNoMissingScripts(scene, "MainMenu", problems);
                ValidateSingleControllerPath(scene, "MainMenu", problems);
            });
        }

        static void ValidateGame(List<string> problems)
        {
            WithScene(GamePath, scene =>
            {
                GameManager game = FindComponent<GameManager>(scene);
                if (game == null)
                {
                    problems.Add("Game.unity has no GameManager component.");
                    return;
                }

                if (game.scoreRoot == null || game.scoreLabel == null
                    || game.levelPointsLabel == null)
                    problems.Add("The score HUD is not serialized in Game.unity.");
                if (game.winScoreValue == null)
                    problems.Add("The win score's large points readout is not serialized in Game.unity.");
                RectTransform nextLevelRect = game.nextButton != null
                    ? game.nextButton.transform as RectTransform : null;
                if (nextLevelRect == null || !game.nextButton.gameObject.activeSelf
                    || Mathf.Abs(nextLevelRect.anchoredPosition.x) > 0.01f)
                    problems.Add("The win board's NEXT LEVEL action is not visible and centred.");
                if (game.menuButton == null || game.menuButton.gameObject.activeSelf
                    || game.menuButton.interactable)
                    problems.Add("The retired win-board LEVEL SELECT action is still visible or active.");
                if (game.winCountdownRoot == null || game.winCountdownLabel == null
                    || game.winCountdownBorder == null || game.winCountdownBorder.Length != 4)
                    problems.Add("The win board's four-sided automatic-next border timer is not prebuilt.");
                else
                    for (int i = 0; i < game.winCountdownBorder.Length; i++)
                        if (game.winCountdownBorder[i] == null)
                        {
                            problems.Add("The win board countdown is missing border segment " + (i + 1) + ".");
                            break;
                        }
                Transform winWindow = game.winPanel != null
                    ? game.winPanel.transform.Find("Window") : null;
                Transform premiumBackdrop = winWindow != null
                    ? winWindow.Find("ResultBackdropPattern") : null;
                if (premiumBackdrop == null
                    || premiumBackdrop.GetComponent<PremiumResultBackdrop>() == null)
                    problems.Add("The win/results board premium circuit background is not serialized.");
                for (int i = 0; i < 3; i++)
                {
                    Transform badge = winWindow != null ? winWindow.Find("WinStar" + i) : null;
                    if (badge == null || badge.Find("PlayerBody") == null
                        || badge.Find("PlayerEyeL") == null || badge.Find("PlayerEyeR") == null)
                    {
                        problems.Add("Win reward " + (i + 1)
                            + " is not prebuilt as the pink player badge.");
                        break;
                    }
                }
                if (game.finaleOverlay == null || !game.finaleOverlay.IsFullyPrebuilt)
                    problems.Add("The campaign finale overlay and its two buttons are not prebuilt.");

                TutorialFx tutorial = game.tutorialFx;
                if (tutorial == null || tutorial.titleText == null
                    || tutorial.panelGroup == null || tutorial.panelRT == null
                    || tutorial.videoImage == null || tutorial.captionGroup == null
                    || tutorial.captionText == null
                    || tutorial.mechanicDemo == null
                    || tutorial.againButton == null || tutorial.tryButton == null
                    || tutorial.skipButton == null || tutorial.skipCountdownText == null)
                    problems.Add("The mechanic demonstration, walkthrough card, caption, REPEAT, "
                        + "TRY IT YOURSELF or numbered centred Skip is not prebuilt.");
                if (tutorial != null && tutorial.mechanicDemo != null
                    && !tutorial.mechanicDemo.IsGameplayStylePrebuilt)
                    problems.Add("The tutorial still contains the old abstract mechanic diagram; "
                        + "the prebuilt gameplay-style mini-board is missing.");
                if (tutorial != null && tutorial.tryButton != null)
                {
                    Text label = tutorial.tryButton.GetComponentInChildren<Text>(true);
                    if (label == null || label.text != "TRY IT YOURSELF")
                        problems.Add("Every tutorial primary action must be TRY IT YOURSELF, not NEXT.");
                }
                if (tutorial != null && tutorial.skipButton != null)
                {
                    Text label = tutorial.skipButton.GetComponentInChildren<Text>(true);
                    if (label == null || label.text != "SKIP TUTORIAL")
                        problems.Add("The prebuilt walkthrough Skip label is incorrect.");
                    Transform badge = tutorial.skipButton.transform.Find("ButtonNumberBadge");
                    Text number = badge != null ? badge.GetComponentInChildren<Text>(true) : null;
                    if (number == null || number.text != "6")
                        problems.Add("Purple walkthrough Skip must show Luxodd button number 6.");
                    if (tutorial.skipCountdownText == null
                        || tutorial.skipCountdownText.text != "AUTO-CONTINUE IN 10")
                        problems.Add("Purple walkthrough Skip must have a visible 10-second auto-continue timer.");
                    RectTransform skipRect = tutorial.skipButton.transform as RectTransform;
                    Vector2 centre = new Vector2(0.5f, 0.5f);
                    if (skipRect == null || skipRect.anchorMin != centre
                        || skipRect.anchorMax != centre
                        || Mathf.Abs(skipRect.anchoredPosition.x) > 0.01f)
                        problems.Add("Walkthrough Skip must stay centred below the card.");
                    if (tutorial.skipButton.gameObject.activeSelf)
                        problems.Add("Walkthrough Skip must start hidden on ordinary gameplay.");
                }
                ValidateCampaignPrefabOrder(game.levelPrefabs, "Game", problems);
                ValidateWorldOneTeachingOrder(game.levelPrefabs, problems);

                LoseFx loss = game.loseFx;
                if (loss == null || loss.leaderboard == null || !loss.leaderboard.IsFullyPrebuilt)
                    problems.Add("The 10-row loss leaderboard is not fully serialized.");
                ValidateHiddenLossAction(game.retryButton, "local Continue/Retry", problems);
                ValidateHiddenLossAction(game.backToLevelsButton, "local Levels", problems);

                ValidateHold(game.upButton, "Up", problems);
                ValidateHold(game.downButton, "Down", problems);
                ValidateHold(game.leftButton, "Left", problems);
                ValidateHold(game.rightButton, "Right", problems);

                ValidateBorderSoundButton(game, problems);

                Transform bar = game.upButton != null ? game.upButton.transform.parent
                    : game.undoButton != null ? game.undoButton.transform.parent : null;
                Transform joystick = bar != null ? bar.Find("ArcadeJoystickHud") : null;
                if (joystick == null || joystick.GetComponent<ArcadeJoystickControl>() == null)
                    problems.Add("ArcadeJoystickHud or its prebuilt ArcadeJoystickControl is missing.");

                ValidateButtonSoundComponents(scene, "Game", problems);
                ValidateNoMissingScripts(scene, "Game", problems);
                ValidateSingleControllerPath(scene, "Game", problems);
            });
        }

        static void ValidateBorderSoundButton(GameManager game, List<string> problems)
        {
            if (game.muteButton == null || game.muteOnIcon == null || game.muteOffIcon == null)
            {
                problems.Add("The gameplay-border sound toggle or one of its state icons is missing.");
                return;
            }

            RectTransform rect = game.muteButton.transform as RectTransform;
            bool belowTimer = game.timerRoot == null || (rect != null
                && rect.anchoredPosition.y < game.timerRoot.anchoredPosition.y
                - game.timerRoot.rect.height * 0.5f);
            if (!game.muteButton.gameObject.activeSelf || rect == null
                || rect.anchorMin != Vector2.one || rect.anchorMax != Vector2.one || !belowTimer)
                problems.Add("The sound toggle is not prebuilt directly below the gameplay timer.");

            Transform soundOn = game.muteOnIcon.transform.Find("PremiumSoundState");
            Transform soundOff = game.muteOffIcon.transform.Find("PremiumSoundState");
            Text soundOnText = soundOn != null && soundOn.Find("StateLabel") != null
                ? soundOn.Find("StateLabel").GetComponent<Text>() : null;
            Text soundOffText = soundOff != null && soundOff.Find("StateLabel") != null
                ? soundOff.Find("StateLabel").GetComponent<Text>() : null;
            bool hasOnGlyph = soundOn != null && soundOn.Find("SoundGlyph") != null
                && soundOn.Find("SoundGlyph").GetComponent<PremiumSoundIcon>() != null;
            bool hasOffGlyph = soundOff != null && soundOff.Find("SoundGlyph") != null
                && soundOff.Find("SoundGlyph").GetComponent<PremiumSoundIcon>() != null;
            bool hasOnDial = soundOn != null && soundOn.Find("SoundDial") != null
                && soundOn.Find("SoundDial").GetComponent<PremiumSoundDial>() != null;
            bool hasOffDial = soundOff != null && soundOff.Find("SoundDial") != null
                && soundOff.Find("SoundDial").GetComponent<PremiumSoundDial>() != null;
            if (!hasOnGlyph || !hasOffGlyph || !hasOnDial || !hasOffDial
                || soundOnText == null || soundOnText.text != "ON"
                || soundOffText == null || soundOffText.text != "OFF")
                problems.Add("The gameplay sound toggle does not have its premium SOUND ON/OFF states.");
        }

        static void ValidateSingleControllerPath(Scene scene, string label, List<string> problems)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                    if (eventSystem.sendNavigationEvents)
                        problems.Add(label + " EventSystem still processes controller navigation; "
                            + "this would double-fire Luxodd/Gamepad input.");
        }

        static void ValidateHitTarget(Button button, string label, List<string> problems)
        {
            if (button == null)
            {
                problems.Add(label + " button reference is missing.");
                return;
            }
            CanvasGroup group = button.GetComponent<CanvasGroup>();
            Image image = button.GetComponent<Image>();
            if (group == null || image == null)
                problems.Add(label + " does not have a prebuilt CanvasGroup and root Image hit target.");
            else if (image.color.a > 0.01f)
                problems.Add(label + " root hit target is visible and will draw a rectangular background.");
        }

        static void ValidateLevelSelector(Button button, int levelNumber, List<string> problems)
        {
            if (button == null) return;
            Transform selector = button.transform.Find("LevelFocusSelector");
            UIHoverScale hover = button.GetComponent<UIHoverScale>();
            if (selector == null || selector.GetComponent<Image>() == null
                || selector.GetComponent<UIPulse>() == null)
            {
                problems.Add("Level " + levelNumber
                    + " does not have the prebuilt visible focus selector.");
                return;
            }
            if (hover == null || hover.highlight != selector.gameObject)
                problems.Add("Level " + levelNumber
                    + " focus selector is not wired to joystick/mouse selection.");
            if (selector.gameObject.activeSelf)
                problems.Add("Level " + levelNumber
                    + " focus selector must start hidden until the node is selected.");
        }

        static void ValidateVisibleHomeButton(Button button, string label, List<string> problems)
        {
            if (button == null)
            {
                problems.Add(label + " button reference is missing.");
                return;
            }

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            Image image = button.GetComponent<Image>();
            if (group == null || image == null)
            {
                problems.Add(label + " does not have its visible Button face and CanvasGroup.");
                return;
            }
            if (group.alpha < 0.99f)
                problems.Add(label + " is still an invisible artwork hotspot instead of a visible Button.");
            if (image.color.a < 0.99f || image.sprite == null)
                problems.Add(label + " is missing its rounded visible Button face.");
            if (button.targetGraphic != image || !image.raycastTarget)
                problems.Add(label + " does not use its visible face as the complete click target.");
        }

        static void ValidateHiddenLossAction(Button button, string label, List<string> problems)
        {
            if (button == null) return;
            Transform holder = button.transform.parent;
            if ((holder != null && holder.gameObject.activeSelf)
                || (holder == null && button.gameObject.activeSelf))
                problems.Add("The " + label + " button is still visible on the local loss screen.");
        }

        static void ValidateHold(Button button, string label, List<string> problems)
        {
            if (button != null && button.GetComponent<HoldRepeatButton>() == null)
                problems.Add(label + " is missing its prebuilt HoldRepeatButton component.");
        }

        static void ValidateCampaignPrefabOrder(GameObject[] prefabs, string sceneLabel,
            List<string> problems)
        {
            if (prefabs == null || prefabs.Length != 50)
            {
                problems.Add(sceneLabel + " must serialize exactly 50 campaign prefabs.");
                return;
            }
            for (int i = 0; i < prefabs.Length; i++)
            {
                string expected = "Assets/Parabox/Prefabs/Levels/Level_" + (i + 1) + ".prefab";
                if (prefabs[i] == null || AssetDatabase.GetAssetPath(prefabs[i]) != expected)
                    problems.Add(sceneLabel + " campaign slot " + (i + 1)
                        + " does not reference " + expected + ".");
            }
        }

        static void ValidateWorldOneTeachingOrder(GameObject[] prefabs, List<string> problems)
        {
            if (prefabs == null || prefabs.Length < 10) return;
            MechanicCatalog.Id[][] expected =
            {
                new[]
                {
                    MechanicCatalog.Id.Navigation,
                    MechanicCatalog.Id.Crate,
                },
                System.Array.Empty<MechanicCatalog.Id>(),
                System.Array.Empty<MechanicCatalog.Id>(),
                new[] { MechanicCatalog.Id.OneWay },
                System.Array.Empty<MechanicCatalog.Id>(),
                System.Array.Empty<MechanicCatalog.Id>(),
                new[] { MechanicCatalog.Id.ButtonGate },
                System.Array.Empty<MechanicCatalog.Id>(),
                System.Array.Empty<MechanicCatalog.Id>(),
                System.Array.Empty<MechanicCatalog.Id>(),
            };

            for (int level = 0; level < expected.Length; level++)
            {
                List<MechanicCatalog.Id> actual = MechanicCatalog.IntroductionsAt(prefabs, level);
                if (actual.Count != expected[level].Length)
                {
                    problems.Add("World 1 Level " + (level + 1) + " introduces "
                        + string.Join(", ", actual) + " instead of "
                        + string.Join(", ", expected[level]) + ".");
                    continue;
                }
                for (int i = 0; i < expected[level].Length; i++)
                    if (!actual.Contains(expected[level][i]))
                        problems.Add("World 1 " + expected[level][i]
                            + " walkthrough is not attached to its first appearance at Level "
                            + (level + 1) + ".");
            }
        }

        static void ValidateButtonSoundComponents(Scene scene, string label, List<string> problems)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                {
                    UIButtonSfx[] sounds = button.GetComponents<UIButtonSfx>();
                    if (sounds.Length != 1)
                        problems.Add(label + " button " + button.name
                            + " must contain exactly one prebuilt button-sound component (found "
                            + sounds.Length + ").");
                    else
                    {
                        MonoScript script = MonoScript.FromMonoBehaviour(sounds[0]);
                        string path = script != null ? AssetDatabase.GetAssetPath(script) : string.Empty;
                        if (path != "Assets/Parabox/Scripts/UIButtonSfx.cs")
                            problems.Add(label + " button " + button.name
                                + " still uses an embedded runtime button-sound script.");
                    }
                }
        }

        static void ValidateNoMissingScripts(Scene scene, string label, List<string> problems)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(candidate.gameObject);
                if (count > 0)
                    problems.Add(label + " object " + candidate.name + " contains " + count
                        + " missing script reference(s).");
            }
        }

        static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        static void WithScene(string path, System.Action<Scene> action)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                action(scene);
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
