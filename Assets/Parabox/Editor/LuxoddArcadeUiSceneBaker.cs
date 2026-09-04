using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // Scene-authoring only. This converts the Luxodd cabinet presentation into normal serialized
    // Unity UI objects; no HUD objects are created, hidden, moved, or recoloured during Play Mode.
    public static class LuxoddArcadeUiSceneBaker
    {
        const string MenuPath = "Assets/Parabox/Scenes/MainMenu.unity";
        const string GamePath = "Assets/Parabox/Scenes/Game.unity";
        const string MenuBackgroundPath = "Assets/Parabox/Art/MainMenuApproved.png";
        const string GameBackgroundPath = "Assets/Parabox/Art/GameBGApproved.png";
        const string LevelMapBackgroundPath = "Assets/Parabox/Art/LevelMapApproved.png";
        // Rounded lettering keeps the menu welcoming and matches the soft Parabox shapes.
        const string MenuFontPath = "Assets/Parabox/Fonts/Fredoka-SemiBold.ttf";
        const string Marker = "LuxoddArcadeUI_Baked_v4";

        static readonly Color BlackTop = Hex("27313A");
        static readonly Color BlackBottom = Hex("0D1218");
        static readonly Color RedTop = Hex("F45B44");
        static readonly Color RedBottom = Hex("B7231B");
        static readonly Color GreenTop = Hex("55DC4C");
        static readonly Color GreenBottom = Hex("168536");
        static readonly Color PurpleTop = Hex("B76CFF");
        static readonly Color PurpleBottom = Hex("5A2297");
        static readonly Color BlueTop = Hex("46B8FF");
        static readonly Color BlueBottom = Hex("1556A8");
        static readonly Color YellowTop = Hex("FFD83D");
        static readonly Color YellowBottom = Hex("E69B0B");
        static readonly Color WhiteTop = Hex("FFFFFF");
        static readonly Color WhiteBottom = Hex("CBD8DE");
        static readonly Color LightLabel = Hex("F4FCFF");
        static readonly Color DarkLabel = Hex("081722");
        static readonly Color Cyan = Hex("46CEE0");
        static readonly Color[] SpiralChapterColors =
        {
            Hex("42E3F5"),
            Hex("318BFF"),
            Hex("43D7B2"),
            Hex("8F6BFF"),
            Hex("F04FC8")
        };
        static readonly Vector2[] ConceptChapterTwoPositions =
        {
            new Vector2(520f, -180f), new Vector2(580f, -110f),
            new Vector2(620f, -35f),  new Vector2(645f, 45f),
            new Vector2(650f, 125f),  new Vector2(645f, 205f),
            new Vector2(620f, 280f),  new Vector2(575f, 345f),
            new Vector2(515f, 400f),  new Vector2(445f, 440f)
        };
        static readonly Vector2[] ConceptChapterThreePositions =
        {
            new Vector2(-520f, 180f), new Vector2(-455f, 250f),
            new Vector2(-365f, 315f), new Vector2(-260f, 365f),
            new Vector2(-145f, 395f), new Vector2(-20f, 410f),
            new Vector2(105f, 400f),  new Vector2(225f, 370f),
            new Vector2(330f, 325f),  new Vector2(415f, 260f)
        };

        // The editor is already open during collaborative iterations. Apply the approved menu once
        // after this script recompiles, without launching another Unity or Unity Hub process.
        const string ApprovedMenuAutoBakeKey = "Parabox.RestoreOriginalHomeScreen.20260826.EqualSize.EqualHoverMotion.v11";

        [InitializeOnLoadMethod]
        static void QueueApprovedMenuBake()
        {
            if (EditorPrefs.GetBool(ApprovedMenuAutoBakeKey, false)) return;
            EditorApplication.delayCall += TryAutoBakeApprovedMenu;
        }

        static void TryAutoBakeApprovedMenu()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryAutoBakeApprovedMenu;
                return;
            }

            try
            {
                RestoreClassicMainScreenSilent();
                EditorPrefs.SetBool(ApprovedMenuAutoBakeKey, true);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Parabox/Bake Luxodd Arcade UI Into Scenes")]
        public static void BakeFromMenu()
        {
            BakeAll(true);
        }

        [MenuItem("Tools/Parabox/Restore Old Main Screen")]
        public static void RestoreOldMainScreen()
        {
            RestoreClassicMainScreenSilent();
            EditorUtility.DisplayDialog("Parabox",
                "The old main screen has been restored. Levels and gameplay were not changed.", "OK");
        }

        [MenuItem("Tools/Parabox/Remove Main Menu Sound Button")]
        public static void RemoveMainMenuSoundButtonFromScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Parabox",
                    "Stop Play Mode before removing the main-menu sound button.", "OK");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(MenuPath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Additive);

            MainMenuUI ui = FindComponent<MainMenuUI>(scene);
            if (ui == null) throw new InvalidDataException("MainMenuUI is missing from MainMenu.unity.");
            RemoveMainMenuSoundButton(ui);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Parabox",
                "The sound button was removed from the main screen. The in-level control was kept.", "OK");
        }

        public static void RestoreClassicMainScreenSilent()
        {
            Scene scene = SceneManager.GetSceneByPath(MenuPath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Additive);

            MainMenuUI ui = FindComponent<MainMenuUI>(scene);
            if (ui == null) throw new InvalidDataException("MainMenuUI is missing from MainMenu.unity.");
            InstallArcadeCoreBackground(ui);
            LayoutHomeTitle(ui);
            if (ui.homeGroup != null)
            {
                DestroyNamed(ui.homeGroup.transform, "MainMenuTutorialPanel");
                DestroyNamed(ui.homeGroup.transform, "BtnSkins");
                DestroyNamed(ui.homeGroup.transform, "BtnSettings");
            }
            LayoutButton(ui.playButton, new Vector2(-225f, -365f), new Vector2(370f, 112f));
            LayoutButton(ui.levelsButton, new Vector2(225f, -365f), new Vector2(370f, 112f));
            RemoveMainMenuSoundButton(ui);
            ui.PrebuildStaticUi();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: PLAY and LEVEL SELECT restored with PLAY focused by default.");
        }

        // Command-line friendly entry point used to update only the menu scene without opening a
        // dialog or rewriting the gameplay scene.
        public static void BakeMenuOnlySilent()
        {
            BakeAsset(MenuPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: focused single-button main menu baked into MainMenu.unity.");
        }

        public static void BakeGameOnlySilent()
        {
            BakeAsset(GamePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: selected indigo arcade-chamber background baked into Game.unity.");
        }

        public static void BakeAllSilent()
        {
            BakeAll(false);
        }

        public static void AuditSpiralMenuSilent()
        {
            Scene scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            MainMenuUI ui = FindComponent<MainMenuUI>(scene);
            if (ui == null) throw new InvalidDataException("MainMenuUI is missing from MainMenu.unity.");
            if (ui.levelButtons == null || ui.levelButtons.Length != 50)
                throw new InvalidDataException("The spiral map must contain exactly 50 serialized level buttons.");
            if (ui.pathDots == null || ui.pathDotsPerLink <= 0
                || ui.pathDots.Length != 49 * ui.pathDotsPerLink)
                throw new InvalidDataException("The spiral route-dot references are incomplete.");
            if (ui.mapCam == null || ui.mapCam.Find("ApprovedFiveChapterMap") == null)
                throw new InvalidDataException("The approved five-chapter map was not serialized.");

            Canvas menuCanvas = ui.GetComponentInParent<Canvas>();
            if (menuCanvas == null || menuCanvas.transform.localScale.sqrMagnitude < 0.01f)
                throw new InvalidDataException("The MainMenu Canvas is collapsed; menu and map buttons cannot receive clicks.");

            Button[] primaryButtons = { ui.playButton, ui.levelsButton, ui.levelBoardBackButton };
            string[] primaryNames = { "Play", "Level Select", "Back" };
            for (int i = 0; i < primaryButtons.Length; i++)
                ValidateArtworkHitTarget(primaryButtons[i], primaryNames[i]);

            var positions = new HashSet<Vector2Int>();
            var nodeRects = new List<RectTransform>();
            for (int i = 0; i < ui.levelButtons.Length; i++)
            {
                if (ui.levelButtons[i] == null)
                    throw new InvalidDataException("Missing level-button reference at index " + i + ".");
                ValidateArtworkHitTarget(ui.levelButtons[i], "Level " + (i + 1));
                RectTransform rect = ui.levelButtons[i].transform as RectTransform;
                nodeRects.Add(rect);
                Vector2Int key = Vector2Int.RoundToInt(rect.anchoredPosition);
                if (!positions.Add(key))
                    throw new InvalidDataException("Two spiral nodes occupy the same position at level " + (i + 1) + ".");
            }

            float minimumClearance = float.MaxValue;
            for (int a = 0; a < nodeRects.Count; a++)
            for (int b = a + 1; b < nodeRects.Count; b++)
            {
                float distance = Vector2.Distance(nodeRects[a].anchoredPosition, nodeRects[b].anchoredPosition);
                float radiusA = Mathf.Max(nodeRects[a].sizeDelta.x, nodeRects[a].sizeDelta.y) * 0.5f;
                float radiusB = Mathf.Max(nodeRects[b].sizeDelta.x, nodeRects[b].sizeDelta.y) * 0.5f;
                float clearance = distance - radiusA - radiusB;
                minimumClearance = Mathf.Min(minimumClearance, clearance);
                if (clearance < 6f)
                    throw new InvalidDataException("Spiral levels " + (a + 1) + " and " + (b + 1)
                        + " overlap or are too close (clearance " + clearance.ToString("0.0") + " px).");
            }

            Debug.Log("Parabox approved-map audit passed: the home action, Back and all 50 " +
                "level hit targets are raycastable; 50 unique nodes, " + ui.pathDots.Length
                + " route dots, " + minimumClearance.ToString("0.0") + " px minimum clearance.");
        }

        static void ValidateArtworkHitTarget(Button button, string label)
        {
            if (button == null || !button.gameObject.activeSelf || !button.interactable)
                throw new InvalidDataException(label + " button is missing, inactive or disabled.");
            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null || group.alpha <= 0f || !group.interactable || !group.blocksRaycasts)
                throw new InvalidDataException(label + " artwork hit target is not raycastable.");
            if (button.image == null || !button.image.raycastTarget)
                throw new InvalidDataException(label + " button graphic cannot receive raycasts.");
        }

        public static void AuditTutorialChoicesSilent()
        {
            Shader progressShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Parabox/Resources/Shaders/LevelMapProgress.shader");
            if (progressShader == null || ShaderUtil.ShaderHasError(progressShader))
            {
                if (progressShader != null)
                    foreach (var message in ShaderUtil.GetShaderMessages(progressShader))
                        Debug.LogError($"LevelMapProgress shader {message.severity} at line " +
                            $"{message.line}: {message.message} ({message.platform})");
                throw new InvalidDataException("The level-map progress shader is missing or does not compile.");
            }

            Scene scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
            GameManager gm = FindComponent<GameManager>(scene);
            TutorialFx tutorial = gm != null ? gm.tutorialFx : null;
            if (tutorial == null || tutorial.choiceRT == null)
                throw new InvalidDataException("The tutorial choice panel is not wired in Game.unity.");

            Button[] buttons = { tutorial.againButton, tutorial.tryButton, tutorial.skipButton };
            string[] labels = { "REPEAT", "TRY IT YOURSELF", "SKIP TUTORIAL" };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || (i < 2 && !buttons[i].gameObject.activeSelf))
                    throw new InvalidDataException("Tutorial choice " + labels[i] + " is missing or disabled.");
                Text text = ButtonLabel(buttons[i]);
                if (text == null || text.text != labels[i])
                    throw new InvalidDataException("Tutorial choice label is incorrect: " + labels[i] + ".");
            }

            float repeatX = ((RectTransform)tutorial.againButton.transform).anchoredPosition.x;
            float tryX = ((RectTransform)tutorial.tryButton.transform).anchoredPosition.x;
            if (!(repeatX < tryX))
                throw new InvalidDataException("Tutorial choices are not ordered Repeat, Try.");

            RectTransform skipRect = tutorial.skipButton != null
                ? tutorial.skipButton.transform as RectTransform : null;
            Vector2 centre = new Vector2(0.5f, 0.5f);
            if (skipRect == null || skipRect.anchorMin != centre || skipRect.anchorMax != centre
                || Mathf.Abs(skipRect.anchoredPosition.x) > 0.01f
                || skipRect.anchoredPosition.y > -350f)
                throw new InvalidDataException("Tutorial Skip must stay centred below the card.");
            Text skipNumber = FindChildText(tutorial.skipButton.transform, "ButtonNumber");
            if (skipNumber == null || skipNumber.text != "6")
                throw new InvalidDataException("Purple tutorial Skip must display cabinet button number 6.");
            if (tutorial.skipCountdownText == null
                || tutorial.skipCountdownText.text != "AUTO-CONTINUE IN 10")
                throw new InvalidDataException("Purple tutorial Skip must have a 10-second auto-continue timer.");
            if (tutorial.skipButton.gameObject.activeSelf)
                throw new InvalidDataException("Tutorial Skip must start hidden outside a walkthrough.");
            if (tutorial.panelGroup == null || tutorial.panelRT == null
                || tutorial.videoImage == null || tutorial.captionGroup == null
                || tutorial.captionText == null)
                throw new InvalidDataException("The first-appearance board-video walkthrough is not prebuilt.");

            Debug.Log("Parabox tutorial audit passed: first-appearance board video, Repeat, Try "
                + "and Purple/RB Skip are prebuilt.");
        }

        static void BakeAll(bool revealResult)
        {
            BakeAsset(MenuPath);
            BakeAsset(GamePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: Luxodd arcade UI was baked into MainMenu.unity and Game.unity (no runtime HUD generation)." );
            if (revealResult)
                EditorUtility.DisplayDialog("Parabox", "Luxodd arcade UI is now stored directly in both scenes.", "OK");
        }

        static void BakeAsset(string path)
        {
            if (!File.Exists(path)) return;

            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            BakeScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        public static void BakeScene(Scene scene)
        {
            int removedMissingScripts = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject gameObject = candidate.gameObject;
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                if (count <= 0) continue;
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                removedMissingScripts += count;
            }
            if (removedMissingScripts > 0)
                Debug.Log("Parabox: removed " + removedMissingScripts
                    + " stale missing-script component(s) from " + scene.name + ".");

            MainMenuUI menu = FindComponent<MainMenuUI>(scene);
            if (menu != null) BakeMenu(menu);

            GameManager game = FindComponent<GameManager>(scene);
            if (game != null) BakeGame(game);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                {
                    // UIButtonSfx used to be a secondary class inside Sfx.cs. Unity could keep
                    // those instances alive only as embedded runtime-script records, producing
                    // duplicate components and "referenced script is missing" errors after a
                    // reload. Normalize every button to one asset-backed component.
                    foreach (UIButtonSfx sound in button.GetComponents<UIButtonSfx>())
                        Object.DestroyImmediate(sound, true);
                    Sfx.AttachButton(button);
                }
                foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                    // Controller navigation is owned by LuxoddArcadeAdapter. Leaving Unity's
                    // navigation events enabled makes a Gamepad A press invoke the same button a
                    // second time while cabinet JoystickButton0 is handled only by the adapter.
                    // Pointer/touch clicks continue through InputSystemUIInputModule normally.
                    eventSystem.sendNavigationEvents = false;
            }

            EnsureMarker(scene);
        }

        static void BakeMenu(MainMenuUI ui)
        {
            ui.levelPrefabs = LoadCampaignLevelPrefabs();
            InstallArcadeCoreBackground(ui);
            LayoutHomeTitle(ui);
            if (ui.homeGroup != null)
            {
                DestroyNamed(ui.homeGroup.transform, "MainMenuTutorialPanel");
                DestroyNamed(ui.homeGroup.transform, "BtnSkins");
                DestroyNamed(ui.homeGroup.transform, "BtnSettings");
            }
            InstallFiveChapterBoardMap(ui);

            LayoutButton(ui.playButton, new Vector2(-225f, -365f), new Vector2(370f, 112f));
            LayoutButton(ui.levelsButton, new Vector2(225f, -365f), new Vector2(370f, 112f));
            RemoveMainMenuSoundButton(ui);

            LayoutButton(ui.levelBoardBackButton, new Vector2(0f, -440f), new Vector2(520f, 122f));
            ArcadeActionButtonStyle.ApplyLevelMapBack(ui.levelBoardBackButton);
            HideButtonAndDecorations(ui.homeGroup != null ? ui.homeGroup.transform : null, ui.quitButton);

            if (ui.homeGroup != null)
                DestroyNamed(ui.homeGroup.transform, "LuxoddHomeControls");

            if (ui.levelBoardScreen != null)
                DestroyNamed(ui.levelBoardScreen.transform, "LuxoddMapControls");

            ui.PrebuildStaticUi();
        }

        // Sound remains available during gameplay, but the home screen intentionally keeps only
        // its two primary actions. Remove the retired control from already-baked scenes as well as
        // preventing future menu rebuilds from bringing it back.
        static void RemoveMainMenuSoundButton(MainMenuUI ui)
        {
            if (ui == null || ui.homeGroup == null) return;
            DestroyNamed(ui.homeGroup.transform, "MainMenuSoundButton");
        }

        // Matches the approved five-board mock-up exactly while retaining the existing serialized
        // level Buttons as transparent hit targets. Gameplay callbacks, Luxodd stick navigation,
        // chapter locking and score/progress data therefore remain unchanged.
        static void InstallFiveChapterBoardMap(MainMenuUI ui)
        {
            if (ui == null || ui.mapCam == null || ui.levelButtons == null
                || ui.levelButtons.Length == 0) return;

            RectTransform map = ui.mapCam;
            map.anchoredPosition = Vector2.zero;
            map.localScale = Vector3.one;

            Image mapScreen = ui.levelBoardScreen != null
                ? ui.levelBoardScreen.GetComponent<Image>() : null;
            if (mapScreen != null) mapScreen.color = Color.clear;

            DestroyNamed(map, "HolographicSpiralBackdrop");
            DestroyNamed(map, "ApprovedFiveChapterMap");

            RectTransform backdrop = CreateImage(map, "ApprovedFiveChapterMap", LoadSprite("Fill.png"),
                Color.white, Vector2.zero, new Vector2(1920f, 1080f)).rectTransform;
            backdrop.SetAsFirstSibling();

            Sprite approvedMap = ImportUiSprite(LevelMapBackgroundPath);
            if (approvedMap != null)
            {
                Image artwork = CreateImage(backdrop, "Artwork", approvedMap, Color.white,
                    Vector2.zero, new Vector2(1920f, 1080f));
                artwork.type = Image.Type.Simple;
                artwork.preserveAspect = false;
            }

            // The approved artwork already contains all five board frames, chapter labels,
            // numbered faces, connecting energy path and final pedestal.
            for (int chapter = 0; chapter < SpiralChapterColors.Length; chapter++)
            {
                Transform region = map.Find("Region" + chapter);
                if (region == null) continue;
                SetChildActive(region, "Zone", false);
                SetChildActive(region, "ChName" + chapter, false);
                SetChildActive(region, "ChapterLeader" + chapter, false);
                SetChildActive(region, "ChapterLeaderDot" + chapter, false);
            }

            int count = Mathf.Min(50, ui.levelButtons.Length);
            for (int i = 0; i < count; i++)
            {
                Button button = ui.levelButtons[i];
                if (button == null) continue;

                RectTransform node = button.transform as RectTransform;
                Vector2 position = ApprovedBoardPosition(i);
                node.anchoredPosition = position;
                node.sizeDelta = new Vector2(76f, 58f);
                node.localScale = Vector3.one;

                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.None;
                button.navigation = navigation;

                CanvasGroup group = button.GetComponent<CanvasGroup>();
                if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
                // Exactly zero can be culled by Unity 6's GraphicRaycaster. This remains visually
                // invisible but keeps the approved artwork's level-node hit targets clickable.
                group.alpha = 0.001f;
                group.interactable = true;
                group.blocksRaycasts = true;
                if (button.image != null) button.image.raycastTarget = true;

                int chapter = Mathf.Clamp(i / MainMenuUI.PerCategory, 0,
                    SpiralChapterColors.Length - 1);
                EnsureLevelFocusSelector(button, SpiralChapterColors[chapter], 116f);
            }

            if (ui.pathDots != null)
                foreach (Image dot in ui.pathDots)
                    if (dot != null) dot.gameObject.SetActive(false);

            RectTransform finalAura = FindChildRect(map, "FinalAura");
            if (finalAura != null) finalAura.gameObject.SetActive(false);
            RectTransform finalRing = FindChildRect(map, "FinalRing");
            if (finalRing != null) finalRing.gameObject.SetActive(false);
            RectTransform finalLabel = FindChildRect(map, "FinalLabel");
            if (finalLabel != null) finalLabel.gameObject.SetActive(false);

            if (ui.regionBaseY != null)
                for (int i = 0; i < ui.regionBaseY.Length; i++) ui.regionBaseY[i] = 0f;
        }

        static Vector2 ApprovedBoardPosition(int index)
        {
            // Pixel-matched to the approved five tilted-tablet artwork. Keeping the exact table in
            // the baker prevents a future UI rebake from restoring the older, smaller board hitboxes.
            Vector2[] positions =
            {
                new Vector2(-831.4f,192.2f), new Vector2(-731.5f,169.3f), new Vector2(-635.0f,156.7f), new Vector2(-795.8f,58.0f), new Vector2(-681.0f,56.8f), new Vector2(-795.8f,-48.8f), new Vector2(-679.8f,-47.6f), new Vector2(-833.7f,-162.4f), new Vector2(-738.4f,-165.8f), new Vector2(-639.6f,-164.7f),
                new Vector2(-458.2f,160.1f), new Vector2(-367.5f,154.4f), new Vector2(-275.6f,148.6f), new Vector2(-423.7f,49.9f), new Vector2(-312.3f,47.6f), new Vector2(-423.7f,-53.4f), new Vector2(-312.3f,-54.5f), new Vector2(-460.5f,-164.7f), new Vector2(-367.5f,-165.8f), new Vector2(-276.7f,-165.8f),
                new Vector2(-94.2f,147.5f), new Vector2(-2.3f,147.5f), new Vector2(81.5f,145.2f), new Vector2(-57.4f,40.7f), new Vector2(44.8f,40.7f), new Vector2(-59.7f,-59.1f), new Vector2(42.5f,-59.1f), new Vector2(-99.9f,-162.4f), new Vector2(-2.3f,-162.4f), new Vector2(83.8f,-162.4f),
                new Vector2(259.5f,156.7f), new Vector2(351.4f,157.8f), new Vector2(437.5f,155.5f), new Vector2(292.8f,49.9f), new Vector2(400.8f,49.9f), new Vector2(291.7f,-52.2f), new Vector2(403.1f,-52.2f), new Vector2(256.1f,-162.4f), new Vector2(353.7f,-162.4f), new Vector2(449.0f,-162.4f),
                new Vector2(636.2f,171.6f), new Vector2(729.2f,173.9f), new Vector2(823.3f,175.0f), new Vector2(675.2f,61.4f), new Vector2(790.0f,62.6f), new Vector2(676.4f,-48.8f), new Vector2(793.5f,-47.6f), new Vector2(637.3f,-161.3f), new Vector2(736.1f,-162.4f), new Vector2(830.2f,-161.3f)
            };
            return positions[Mathf.Clamp(index, 0, positions.Length - 1)];
        }

        // Re-authors the existing serialized 50-level map into one premium holographic spiral.
        // This deliberately moves the existing buttons and route dots instead of replacing them,
        // so every level callback, lock, score and progress reference remains intact.
        static void InstallHolographicSpiralMap(MainMenuUI ui)
        {
            if (ui == null || ui.mapCam == null || ui.levelButtons == null || ui.levelButtons.Length == 0)
                return;

            RectTransform map = ui.mapCam;
            map.anchoredPosition = Vector2.zero;
            map.localScale = Vector3.one;

            // Let the generated arcade arena remain clearly visible behind the map while keeping
            // enough dark contrast for 50 small labels and route markers.
            Image mapScreen = ui.levelBoardScreen != null
                ? ui.levelBoardScreen.GetComponent<Image>() : null;
            if (mapScreen != null) mapScreen.color = WithAlpha(Hex("020817"), 0.48f);

            DestroyNamed(map, "HolographicSpiralBackdrop");
            RectTransform backdrop = CreateImage(map, "HolographicSpiralBackdrop", LoadSprite("Fill.png"),
                WithAlpha(Hex("020817"), 0.08f), Vector2.zero, new Vector2(1920f, 1080f)).rectTransform;
            backdrop.SetAsFirstSibling();

            Sprite portalBackground = ImportUiSprite(LevelMapBackgroundPath);
            if (portalBackground != null)
            {
                Image photo = CreateImage(backdrop, "PortalMapPhoto", portalBackground, Color.white,
                    Vector2.zero, new Vector2(1920f, 1080f));
                photo.type = Image.Type.Simple;
                photo.preserveAspect = false;
                photo.rectTransform.SetAsFirstSibling();
            }

            Sprite ring = LoadSprite("RingCircle.png");
            Sprite glow = LoadSprite("Glow.png");
            Sprite disc = LoadSprite("Disc.png");

            // Layered ellipses echo the concentric holographic platform in the selected concept.
            CreateImage(backdrop, "OuterGlow", glow, WithAlpha(Hex("266CFF"), 0.06f),
                new Vector2(0f, 12f), new Vector2(1690f, 820f));
            CreateSpiralRing(backdrop, "Orbit0", ring, SpiralChapterColors[0], 0.018f,
                new Vector2(1540f, 820f));
            CreateSpiralRing(backdrop, "Orbit1", ring, SpiralChapterColors[1], 0.02f,
                new Vector2(1280f, 720f));
            CreateSpiralRing(backdrop, "Orbit2", ring, SpiralChapterColors[2], 0.018f,
                new Vector2(1080f, 690f));
            CreateSpiralRing(backdrop, "Orbit3", ring, SpiralChapterColors[3], 0.025f,
                new Vector2(820f, 560f));
            CreateSpiralRing(backdrop, "Orbit4", ring, SpiralChapterColors[4], 0.03f,
                new Vector2(560f, 450f));

            CreateImage(backdrop, "CoreGlow", glow, WithAlpha(Hex("38CFFF"), 0.08f),
                new Vector2(0f, 35f), new Vector2(310f, 310f));
            CreateImage(backdrop, "CoreDisc", disc, WithAlpha(Hex("06112A"), 0.88f),
                new Vector2(0f, 35f), new Vector2(174f, 174f));
            CreateSpiralRing(backdrop, "CoreRingA", ring, Hex("38CFFF"), 0.22f,
                new Vector2(170f, 170f), new Vector2(0f, 35f));
            CreateSpiralRing(backdrop, "CoreRingB", ring, Hex("FFD15A"), 0.55f,
                new Vector2(116f, 116f), new Vector2(0f, 35f));

            // Remove the old horizontal chapter washes. The five orbit rings now provide the
            // chapter zoning and allow the arena background to remain visible.
            for (int chapter = 0; chapter < SpiralChapterColors.Length; chapter++)
            {
                Transform region = map.Find("Region" + chapter);
                if (region == null) continue;
                SetChildActive(region, "Zone", false);
                StyleSpiralChapterLabel(region, chapter);
            }

            Font font = LoadMenuFont();
            int levelCount = ui.levelButtons.Length;
            for (int i = 0; i < levelCount; i++)
            {
                Button button = ui.levelButtons[i];
                if (button == null) continue;

                int chapter = Mathf.Clamp(i / MainMenuUI.PerCategory, 0, SpiralChapterColors.Length - 1);
                Color accent = SpiralChapterColors[chapter];
                Vector2 position = SpiralMapPosition(i, levelCount);
                float size = SpiralNodeSize(i, levelCount);
                RectTransform node = button.transform as RectTransform;
                node.anchoredPosition = position;
                node.sizeDelta = Vector2.one * size;
                node.localScale = Vector3.one;

                // MainMenuUI handles route-aware joystick selection, so Unity's automatic
                // cross-spiral guesses are disabled for these buttons.
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.None;
                button.navigation = navigation;

                Image fill = i < ui.levelFills.Length ? ui.levelFills[i] : null;
                if (fill != null)
                {
                    UIGradient gradient = fill.GetComponent<UIGradient>();
                    if (gradient == null) gradient = fill.gameObject.AddComponent<UIGradient>();
                    gradient.top = Color.Lerp(accent, Color.white, 0.24f);
                    gradient.bottom = Color.Lerp(accent, Hex("061229"), 0.48f);

                    Shadow shadow = fill.GetComponent<Shadow>();
                    if (shadow == null) shadow = fill.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0f, 0f, 0.04f, 0.82f);
                    shadow.effectDistance = new Vector2(0f, -5f);

                    Outline neonEdge = fill.GetComponent<Outline>();
                    if (neonEdge == null) neonEdge = fill.gameObject.AddComponent<Outline>();
                    neonEdge.effectColor = WithAlpha(Color.Lerp(accent, Color.white, 0.22f), 0.86f);
                    neonEdge.effectDistance = new Vector2(1.8f, -1.8f);
                }

                Image border = i < ui.levelBorders.Length ? ui.levelBorders[i] : null;
                if (border != null)
                {
                    border.rectTransform.sizeDelta = Vector2.one * size;
                    border.color = WithAlpha(Color.Lerp(accent, Color.white, 0.18f), 0.92f);
                }

                Text number = i < ui.levelNumbers.Length ? ui.levelNumbers[i] : null;
                if (number != null)
                {
                    number.font = font;
                    number.fontStyle = FontStyle.Normal;
                    number.fontSize = Mathf.RoundToInt(size * 0.37f);
                    number.rectTransform.sizeDelta = Vector2.one * size;
                    StyleDisplayText(number, LightLabel, Hex("020817"), 1.15f, 2.5f);
                }

                RectTransform current = FindChildRect(node, "Current");
                if (current != null)
                {
                    current.sizeDelta = Vector2.one * (size + (i == 0 ? 22f : 16f));
                    Image currentImage = current.GetComponent<Image>();
                    if (currentImage != null)
                        currentImage.color = i == 0 ? Hex("63FFD2") : accent;
                }

                RectTransform perfect = FindChildRect(node, "Perfect");
                if (perfect != null)
                {
                    perfect.anchoredPosition = new Vector2(-size * 0.5f + 10f, size * 0.5f - 10f);
                    perfect.localScale = Vector3.one * Mathf.Clamp(size / 64f, 0.72f, 1f);
                }

                EnsureLevelFocusSelector(button, accent, Mathf.Max(116f, size + 36f));

                if (i == levelCount - 1)
                {
                    RectTransform finalRing = FindChildRect(node, "FinalRing");
                    if (finalRing != null)
                    {
                        finalRing.sizeDelta = Vector2.one * (size + 22f);
                        Image finalRingImage = finalRing.GetComponent<Image>();
                        if (finalRingImage != null) finalRingImage.color = Hex("FFD05A");
                    }
                    Text finalLabel = FindChildText(node, "FinalLabel");
                    if (finalLabel != null)
                    {
                        finalLabel.font = font;
                        finalLabel.fontSize = 16;
                        finalLabel.color = Hex("FFE58A");
                        finalLabel.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f - 20f);
                        StyleDisplayText(finalLabel, Hex("FFE58A"), Hex("150A00"), 1.1f, 2f);
                    }
                }
            }

            // Re-sample every serialized route dot along the new route. Keeping the original
            // dot objects preserves progress lighting and chapter reveal effects.
            if (ui.pathDots != null && ui.pathDotsPerLink > 0)
            {
                int links = Mathf.Min(levelCount - 1, ui.pathDots.Length / ui.pathDotsPerLink);
                for (int link = 0; link < links; link++)
                {
                    Vector2 a = SpiralMapPosition(link, levelCount);
                    Vector2 b = SpiralMapPosition(link + 1, levelCount);
                    int chapter = Mathf.Clamp(link / MainMenuUI.PerCategory, 0, SpiralChapterColors.Length - 1);
                    bool chapterBreak = (link + 1) % MainMenuUI.PerCategory == 0;
                    Vector2 direction = b - a;
                    float routeAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    for (int d = 0; d < ui.pathDotsPerLink; d++)
                    {
                        int dotIndex = link * ui.pathDotsPerLink + d;
                        Image dot = ui.pathDots[dotIndex];
                        if (dot == null) continue;
                        dot.gameObject.SetActive(!chapterBreak);
                        if (chapterBreak) continue;
                        float t = (d + 1f) / (ui.pathDotsPerLink + 1f);
                        dot.rectTransform.anchoredPosition = Vector2.Lerp(a, b, t);
                        dot.rectTransform.sizeDelta = new Vector2(link >= 29 ? 18f : 22f, 5f);
                        dot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, routeAngle);
                        dot.sprite = LoadSprite("Fill.png");
                        dot.type = Image.Type.Sliced;
                        dot.color = WithAlpha(SpiralChapterColors[chapter], 0.42f);
                    }
                }
            }

            RectTransform finalAura = FindChildRect(map, "FinalAura");
            if (finalAura != null)
            {
                finalAura.anchoredPosition = SpiralMapPosition(levelCount - 1, levelCount);
                finalAura.sizeDelta = Vector2.one * 240f;
                Image auraImage = finalAura.GetComponent<Image>();
                if (auraImage != null) auraImage.color = WithAlpha(Hex("FFD05A"), 0.34f);
            }

            if (ui.regionBaseY != null)
                for (int i = 0; i < ui.regionBaseY.Length; i++) ui.regionBaseY[i] = 0f;
        }

        static void CreateSpiralRing(Transform parent, string name, Sprite sprite, Color color,
            float alpha, Vector2 size, Vector2? position = null)
        {
            Image image = CreateImage(parent, name, sprite, WithAlpha(color, alpha),
                position ?? new Vector2(0f, 16f), size);
            image.type = Image.Type.Simple;
        }

        static void StyleSpiralChapterLabel(Transform region, int chapter)
        {
            Text label = FindChildText(region, "ChName" + chapter);
            if (label == null) return;
            Vector2[] positions =
            {
                new Vector2(690f, -340f),
                new Vector2(720f, 285f),
                new Vector2(-700f, 285f),
                new Vector2(-650f, 65f),
                new Vector2(-690f, -190f)
            };
            string[] names =
            {
                "CHAPTER I", "CHAPTER II", "CHAPTER III", "CHAPTER IV", "CHAPTER V"
            };
            label.rectTransform.anchoredPosition = positions[Mathf.Clamp(chapter, 0, positions.Length - 1)];
            label.rectTransform.sizeDelta = new Vector2(240f, 42f);
            label.text = names[Mathf.Clamp(chapter, 0, names.Length - 1)];
            label.font = LoadMenuFont();
            label.fontSize = 24;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            StyleDisplayText(label, Color.Lerp(SpiralChapterColors[chapter], Color.white, 0.24f),
                Hex("020817"), 1.05f, 2.5f);

            string lineName = "ChapterLeader" + chapter;
            string dotName = "ChapterLeaderDot" + chapter;
            DestroyNamed(region, lineName);
            DestroyNamed(region, dotName);
            bool labelOnRight = chapter <= 1;
            float direction = labelOnRight ? -1f : 1f;
            Vector2 linePosition = positions[chapter] + new Vector2(direction * 205f, -16f);
            Vector2 dotPosition = positions[chapter] + new Vector2(direction * 300f, -16f);
            CreateImage(region, lineName, LoadSprite("Fill.png"),
                WithAlpha(SpiralChapterColors[chapter], 0.82f), linePosition, new Vector2(190f, 3f));
            CreateImage(region, dotName, LoadSprite("Disc.png"),
                WithAlpha(SpiralChapterColors[chapter], 0.96f), dotPosition, new Vector2(10f, 10f));
        }

        static Vector2 SpiralMapPosition(int index, int levelCount)
        {
            if (index >= levelCount - 1) return new Vector2(0f, 35f);

            int chapter = Mathf.Clamp(index / MainMenuUI.PerCategory, 0, 4);
            int within = index % MainMenuUI.PerCategory;
            if (chapter == 1) return ConceptChapterTwoPositions[within];
            if (chapter == 2) return ConceptChapterThreePositions[within];

            float startAngle, endAngle, startRadius, endRadius, verticalScale;
            switch (chapter)
            {
                case 0:
                    startAngle = 225f; endAngle = 315f; startRadius = 750f; endRadius = 645f;
                    verticalScale = 0.56f; break;
                case 3:
                    startAngle = 150f; endAngle = -170f; startRadius = 405f; endRadius = 340f;
                    verticalScale = 0.72f; break;
                default:
                    startAngle = 180f; endAngle = -130f; startRadius = 270f; endRadius = 145f;
                    verticalScale = 0.82f; break;
            }

            // Chapter V uses indices 40-48; Level 50 is the separate destination at the core.
            float denominator = chapter == 4 ? 8f : 9f;
            float t = Mathf.Clamp01(within / denominator);
            float angle = Mathf.Lerp(startAngle, endAngle, t) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(startRadius, endRadius, t);
            return new Vector2(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * verticalScale + 35f);
        }

        static float SpiralNodeSize(int index, int levelCount)
        {
            if (index >= levelCount - 1) return 86f;
            int chapter = Mathf.Clamp(index / MainMenuUI.PerCategory, 0, 4);
            float[] sizes = { 64f, 58f, 58f, 54f, 52f };
            return sizes[chapter];
        }

        static void InstallArcadeCoreBackground(MainMenuUI ui)
        {
            if (ui == null || ui.backdrop == null || !File.Exists(MenuBackgroundPath)) return;

            AssetDatabase.ImportAsset(MenuBackgroundPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(MenuBackgroundPath) as TextureImporter;
            if (importer != null)
            {
                bool dirty = importer.textureType != TextureImporterType.Sprite
                             || importer.spriteImportMode != SpriteImportMode.Single
                             || importer.mipmapEnabled
                             || importer.textureCompression != TextureImporterCompression.Uncompressed
                             || importer.wrapMode != TextureWrapMode.Clamp;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.spritePixelsPerUnit = 100f;
                if (dirty) importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MenuBackgroundPath);
            if (sprite == null) return;

            Transform backdrop = ui.backdrop.transform;
            DestroyNamed(backdrop, "GamePhoto");
            DestroyNamed(backdrop, "MenuPhoto");
            DestroyNamed(backdrop, "PhotoWash");

            var photo = new GameObject("MenuPhoto");
            photo.transform.SetParent(backdrop, false);
            photo.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            var sr = photo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            sr.color = Color.white;
            sr.sortingOrder = -206;
            ui.backdrop.bgPhoto = sr;

            FilmGridBackdrop generatedBackdrop = backdrop.GetComponent<FilmGridBackdrop>();
            if (generatedBackdrop != null) generatedBackdrop.enabled = false;
            DestroyNamed(backdrop, "AnimatedGridBackdropFX");

            // The generated plate already contains its own lighting, particles and vignette.
            // Disable the old procedural overlay so the scene does not become noisy or washed out.
            if (ui.backdrop.glow1 != null) ui.backdrop.glow1.enabled = false;
            if (ui.backdrop.glow2 != null) ui.backdrop.glow2.enabled = false;
            if (ui.backdrop.vignette != null) ui.backdrop.vignette.enabled = false;
            if (ui.backdrop.rays != null)
                foreach (SpriteRenderer ray in ui.backdrop.rays)
                    if (ray != null) ray.enabled = false;

            if (ui.backgroundGroup != null)
            {
                SetChildActive(ui.backgroundGroup.transform, "MenuTopGlow", false);
                SetChildActive(ui.backgroundGroup.transform, "MenuParticles", false);
                SetChildActive(ui.backgroundGroup.transform, "MenuVignette", false);
            }
        }

        static void LayoutHomeTitle(MainMenuUI ui)
        {
            if (ui.homeGroup == null) return;
            Transform home = ui.homeGroup.transform;
            ui.useStaticHomeArtwork = true;

            // The approved full-screen artwork contains these elements already. Disable only the
            // older serialized presentation so the generated composition is shown exactly once.
            string[] authoredIntoArtwork =
            {
                "Kicker", "KickerPlate", "TitleGlow", "TitleRow",
                "Credit1", "Credit2", "CreditPlate"
            };
            foreach (string child in authoredIntoArtwork) SetChildActive(home, child, false);
        }

        static void StyleArtworkHotspot(Button button)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);

            RectTransform rect = button.transform as RectTransform;
            Transform parent = rect != null ? rect.parent : null;
            if (parent != null)
            {
                DestroyNamed(parent, button.name + "ArcadeFrame");
                SetChildActive(parent, button.name + "Shadow", false);
                SetChildActive(parent, button.name + "HL", false);
                SetChildActive(parent, button.name + "Glow", false);
            }
            DestroyNamed(button.transform, "ArcadeInnerEdge");

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            // Exactly zero can be culled by Unity 6's GraphicRaycaster. Keep the invisible artwork
            // hotspot just above zero so mouse/touch clicks reach the real Unity Button.
            group.alpha = 0.001f;
            group.interactable = true;
            group.blocksRaycasts = true;

            // Put the hit graphic on the Button root, not on the smaller legacy Face child. Keep
            // it barely above zero alpha so the complete rectangle remains clickable without
            // baking a white backing behind visible controls such as the level-map BACK button.
            Image hitTarget = button.GetComponent<Image>();
            if (hitTarget == null) hitTarget = button.gameObject.AddComponent<Image>();
            hitTarget.sprite = null;
            hitTarget.color = new Color(1f, 1f, 1f, 0.001f);
            hitTarget.raycastTarget = true;
            button.targetGraphic = hitTarget;

            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            foreach (Graphic graphic in graphics)
                if (graphic != null) graphic.raycastTarget = graphic == hitTarget;
        }

        static void CreateTechLabelPlate(Transform home, string name, RectTransform label,
            Vector2 size, Color leftAccent, Color rightAccent)
        {
            if (home == null || label == null) return;
            DestroyNamed(home, name);

            Image plate = CreateImage(home, name, LoadSprite("Fill.png"),
                WithAlpha(Hex("041226"), 0.72f), label.anchoredPosition, size);
            plate.rectTransform.SetSiblingIndex(label.GetSiblingIndex());
            var gradient = plate.gameObject.AddComponent<UIGradient>();
            gradient.top = WithAlpha(Hex("102B4B"), 0.78f);
            gradient.bottom = WithAlpha(Hex("020817"), 0.90f);

            var edge = plate.gameObject.AddComponent<Outline>();
            edge.effectColor = WithAlpha(leftAccent, 0.42f);
            edge.effectDistance = new Vector2(1.25f, -1.25f);
            var shadow = plate.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0.04f, 0.72f);
            shadow.effectDistance = new Vector2(0f, -5f);

            Image left = CreateImage(plate.transform, "LeftAccent", LoadSprite("Fill.png"),
                WithAlpha(leftAccent, 0.92f), new Vector2(-size.x * 0.5f + 28f, 0f),
                new Vector2(32f, 3f));
            Image right = CreateImage(plate.transform, "RightAccent", LoadSprite("Fill.png"),
                WithAlpha(rightAccent, 0.92f), new Vector2(size.x * 0.5f - 28f, 0f),
                new Vector2(32f, 3f));
            left.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            right.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        static void StyleLogoWord(Text text, Font font)
        {
            if (text == null) return;
            text.font = font;
            text.fontSize = 126;
            // Fredoka is already semi-bold; Normal avoids the harsh synthetic-bold look.
            text.fontStyle = FontStyle.Normal;
            text.color = Color.white;

            UIGradient gradient = text.GetComponent<UIGradient>();
            if (gradient == null) gradient = text.gameObject.AddComponent<UIGradient>();
            gradient.top = Hex("C4FCFF");
            gradient.bottom = Hex("1599E4");

            Outline outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = WithAlpha(Hex("031229"), 0.96f);
            outline.effectDistance = new Vector2(2.8f, -2.8f);

            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.035f, 0.12f, 0.90f);
            shadow.effectDistance = new Vector2(0f, -10f);
        }

        static void StyleDisplayText(Text text, Color color, Color outlineColor, float edge, float shadowDepth)
        {
            if (text == null) return;
            text.color = color;
            text.fontStyle = FontStyle.Normal;
            Outline outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = WithAlpha(outlineColor, 0.88f);
            outline.effectDistance = new Vector2(edge, -edge);
            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
            shadow.effectDistance = new Vector2(0f, -shadowDepth);
        }

        static void SetRect(Transform parent, string name, Vector2 position, Vector2 size, Vector3 scale)
        {
            RectTransform rect = parent != null ? parent.Find(name) as RectTransform : null;
            if (rect == null) return;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = scale;
        }

        static void BakeGame(GameManager gm)
        {
            gm.levelPrefabs = LoadCampaignLevelPrefabs();
            InstallPuzzleFieldBackground(gm);
            RestoreTutorialControls(gm.tutorialFx);

            Transform bar = gm.upButton != null ? gm.upButton.transform.parent
                : gm.undoButton != null ? gm.undoButton.transform.parent : null;
            if (bar == null)
            {
                gm.PrebuildStaticUi();
                return;
            }

            HideButtonAndDecorations(bar, gm.upButton);
            HideButtonAndDecorations(bar, gm.downButton);
            HideButtonAndDecorations(bar, gm.leftButton);
            HideButtonAndDecorations(bar, gm.rightButton);
            HideButtonAndDecorations(bar, gm.hudMenuButton);

            SetChildActive(bar, "Key_MOVE", false);
            SetChildActive(bar, "KeyShadow_MOVE", false);
            foreach (string key in new[] { "Z", "R", "M", "ESC" })
            {
                SetChildActive(bar, "Key_" + key, false);
                SetChildActive(bar, "KeyShadow_" + key, false);
            }

            LayoutButton(gm.undoButton, new Vector2(40f, 4f), new Vector2(170f, 60f));
            StyleButton(gm.undoButton, RedTop, RedBottom, LightLabel, "UNDO");
            LayoutButton(gm.restartButton, new Vector2(230f, 4f), new Vector2(170f, 60f));
            StyleButton(gm.restartButton, YellowTop, YellowBottom, DarkLabel, "RESTART");
            HideButtonAndDecorations(bar, gm.muteButton);

            StyleButton(gm.nextButton, BlackTop, BlackBottom, LightLabel, "NEXT LEVEL");
            if (gm.menuButton != null)
            {
                gm.menuButton.interactable = false;
                gm.menuButton.gameObject.SetActive(false);
            }
            HideLegacyLossAction(gm.retryButton);
            HideLegacyLossAction(gm.backToLevelsButton);

            Font font = gm.movesLabel != null ? gm.movesLabel.font : ButtonFont(gm.undoButton);
            BuildJoystick(bar, font);
            DestroyNamed(bar, "LuxoddSystemControls");

            if (gm.tutorialFx != null && gm.tutorialFx.choiceRT != null)
                DestroyNamed(gm.tutorialFx.choiceRT, "LuxoddTutorialControls");

            gm.PrebuildStaticUi();
        }

        // Sound is an always-visible screen setting, not one of the large cabinet action buttons.
        // Reuse the existing serialized mute Button and its two state labels, but dock it to the
        // top-right gameplay border as a compact icon. Purple therefore remains unambiguously Skip.
        static void InstallBorderSoundButton(GameManager gm, Transform oldBar)
        {
            if (gm == null || gm.muteButton == null || gm.hudGroup == null) return;

            SetChildActive(oldBar, gm.muteButton.name + "Shadow", false);
            SetChildActive(oldBar, gm.muteButton.name + "HL", false);
            SetChildActive(oldBar, "Key_M", false);
            SetChildActive(oldBar, "KeyShadow_M", false);

            RectTransform rect = gm.muteButton.transform as RectTransform;
            if (rect == null) return;
            rect.SetParent(gm.hudGroup.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-54f, -54f);
            rect.sizeDelta = new Vector2(68f, 68f);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            // The two state objects are switched by GameManager.UpdateMuteIcon().  Each state owns
            // its complete badge, so the one visible control is blue while sound is on and red
            // while muted.  The Button face itself stays transparent and adds no second icon.
            StyleButton(gm.muteButton, BlueTop, BlueBottom, LightLabel, string.Empty);
            gm.muteButton.gameObject.SetActive(true);

            UIHoverScale hover = gm.muteButton.GetComponent<UIHoverScale>();
            if (hover != null)
            {
                hover.highlight = null;
                hover.hover = 1.07f;
                hover.press = 0.94f;
            }

            Image face = gm.muteButton.targetGraphic as Image;
            if (face != null)
            {
                face.color = Color.clear;
                Outline outline = face.GetComponent<Outline>();
                if (outline != null) Object.DestroyImmediate(outline);
            }
            gm.muteButton.transition = Selectable.Transition.None;

            ConfigureSoundStateIcon(gm.muteOnIcon, false);
            ConfigureSoundStateIcon(gm.muteOffIcon, true);
        }

        static void ConfigureSoundStateIcon(GameObject iconObject, bool muted)
        {
            if (iconObject == null) return;
            Text text = iconObject.GetComponent<Text>();
            if (text == null) return;

            text.text = string.Empty;
            text.fontSize = 1;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.clear;
            text.raycastTarget = false;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            DestroyNamed(rect, "SoundDisc");
            DestroyNamed(rect, "SpeakerBody");
            DestroyNamed(rect, "SpeakerCone");
            DestroyNamed(rect, "StateMark");

            Color discTop = muted ? Hex("FF3F45") : Hex("2797FF");
            Image disc = CreateImage(rect, "SoundDisc", LoadSprite("Disc.png"), discTop,
                Vector2.zero, new Vector2(64f, 64f));
            disc.raycastTarget = false;
            disc.rectTransform.SetAsFirstSibling();
            var discOutline = disc.gameObject.AddComponent<Outline>();
            discOutline.effectColor = muted ? Hex("8E111C") : Hex("0B4EAF");
            discOutline.effectDistance = new Vector2(1.5f, -1.5f);

            Image body = CreateImage(rect, "SpeakerBody", LoadSprite("Fill.png"), Color.white,
                new Vector2(-13f, 0f), new Vector2(10f, 18f));
            body.raycastTarget = false;

            Image cone = CreateImage(rect, "SpeakerCone", LoadSprite("TriIcon.png"), Color.white,
                new Vector2(-2f, 0f), new Vector2(25f, 25f));
            cone.raycastTarget = false;
            cone.rectTransform.localEulerAngles = new Vector3(0f, 0f, -90f);

            Text mark = CreateText(rect, "StateMark", text.font,
                muted ? "\u00D7" : ")))" , muted ? 29 : 18,
                new Vector2(14f, 0f), new Vector2(29f, 34f));
            mark.color = Color.white;
            mark.fontStyle = FontStyle.Bold;
            mark.raycastTarget = false;
        }

        static void HideLegacyLossAction(Button button)
        {
            if (button == null) return;
            Transform holder = button.transform.parent;
            if (holder != null) holder.gameObject.SetActive(false);
            else button.gameObject.SetActive(false);
        }

        static void RestoreTutorialControls(TutorialFx tutorial)
        {
            if (tutorial == null || tutorial.choiceRT == null) return;

            tutorial.choiceRT.anchoredPosition = new Vector2(0f, -410f);
            tutorial.choiceRT.sizeDelta = new Vector2(760f, 120f);

            if (tutorial.againButton != null)
            {
                tutorial.againButton.gameObject.SetActive(true);
                LayoutButton(tutorial.againButton, new Vector2(-180f, 0f), new Vector2(270f, 72f));
                StyleButton(tutorial.againButton, BlackTop, BlackBottom, LightLabel, "REPEAT");
            }

            if (tutorial.tryButton != null)
            {
                tutorial.tryButton.gameObject.SetActive(true);
                LayoutButton(tutorial.tryButton, new Vector2(180f, 0f), new Vector2(310f, 82f));
                StyleButton(tutorial.tryButton, GreenTop, GreenBottom, LightLabel, "TRY IT YOURSELF");
            }

            // Skip is intentionally outside choiceGroup: it stays available during the complete
            // demonstration, not only after Repeat/Try appear. Clone an existing authored button
            // in the editor so no GameObject or visual component is created in a player build.
            Transform tutorialRoot = tutorial.choiceRT.parent;
            Button skip = tutorial.skipButton;
            if (skip == null && tutorialRoot != null)
            {
                Transform existing = tutorialRoot.Find("TutorialSkip");
                if (existing != null) skip = existing.GetComponent<Button>();
            }
            if (skip == null && tutorialRoot != null && tutorial.tryButton != null)
            {
                GameObject clone = Object.Instantiate(tutorial.tryButton.gameObject, tutorialRoot, false);
                clone.name = "TutorialSkip";
                skip = clone.GetComponent<Button>();
                if (skip != null) skip.onClick = new Button.ButtonClickedEvent();
            }
            if (skip != null)
            {
                tutorial.skipButton = skip;
                tutorial.skipCountdownText = ConfigureNumberedTutorialSkip(skip, tutorialRoot);
                Navigation skipNav = skip.navigation;
                skipNav.mode = Navigation.Mode.None;
                skip.navigation = skipNav;
                skip.gameObject.SetActive(false);
            }

            // Keep pointer navigation deterministic as well; Luxodd stick navigation is handled
            // by GameManager and uses this same left-to-right order.
            if (tutorial.againButton != null && tutorial.tryButton != null)
            {
                Navigation againNav = tutorial.againButton.navigation;
                againNav.mode = Navigation.Mode.Explicit;
                againNav.selectOnRight = tutorial.tryButton;
                tutorial.againButton.navigation = againNav;

                Navigation tryNav = tutorial.tryButton.navigation;
                tryNav.mode = Navigation.Mode.Explicit;
                tryNav.selectOnLeft = tutorial.againButton;
                tryNav.selectOnRight = null;
                tutorial.tryButton.navigation = tryNav;
            }

            BuildMechanicBriefing(tutorial, tutorialRoot);
        }

        static void BuildMechanicBriefing(TutorialFx tutorial, Transform tutorialRoot)
        {
            if (tutorial == null || tutorialRoot == null) return;
            DestroyNamed(tutorialRoot, "MechanicBriefingPanel");

            Font font = tutorial.captionText != null ? tutorial.captionText.font : LoadMenuFont();
            Image panel = CreateImage(tutorialRoot, "MechanicBriefingPanel", LoadSprite("Fill.png"),
                Hex("071525"), new Vector2(0f, 285f), new Vector2(1040f, 164f));
            var gradient = panel.gameObject.AddComponent<UIGradient>();
            gradient.top = Hex("18375A");
            gradient.bottom = Hex("040B17");
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = WithAlpha(Cyan, 0.88f);
            outline.effectDistance = new Vector2(2f, -2f);
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0.02f, 0.78f);
            shadow.effectDistance = new Vector2(0f, -9f);

            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            CreateImage(panel.transform, "LeftAccent", LoadSprite("Fill.png"), PurpleTop,
                new Vector2(-504f, 0f), new Vector2(8f, 126f));
            Text title = CreateText(panel.transform, "BriefingTitle", font, "NEW MECHANIC", 22,
                new Vector2(0f, 45f), new Vector2(970f, 36f));
            title.color = Hex("83EDFF");
            title.fontStyle = FontStyle.Bold;

            Text lesson = CreateText(panel.transform, "BriefingText", font, string.Empty, 27,
                new Vector2(0f, -17f), new Vector2(970f, 82f));
            lesson.color = LightLabel;
            lesson.resizeTextForBestFit = true;
            lesson.resizeTextMinSize = 18;
            lesson.resizeTextMaxSize = 27;
            lesson.horizontalOverflow = HorizontalWrapMode.Wrap;
            lesson.verticalOverflow = VerticalWrapMode.Truncate;

            Text hint = CreateText(panel.transform, "SkipHint", font, "PURPLE 6 / RB  •  SKIP", 16,
                new Vector2(390f, -63f), new Vector2(190f, 24f));
            hint.color = WithAlpha(PurpleTop, 0.96f);
            hint.alignment = TextAnchor.MiddleRight;

            tutorial.briefingGroup = group;
            tutorial.briefingRT = panel.rectTransform;
            tutorial.briefingTitleText = title;
            tutorial.briefingText = lesson;
        }

        static GameObject[] LoadCampaignLevelPrefabs()
        {
            var prefabs = new GameObject[50];
            for (int i = 0; i < prefabs.Length; i++)
            {
                string path = "Assets/Parabox/Prefabs/Levels/Level_" + (i + 1) + ".prefab";
                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabs[i] == null)
                    throw new InvalidDataException("Campaign level prefab is missing: " + path);
            }
            return prefabs;
        }

        static void InstallPuzzleFieldBackground(GameManager gm)
        {
            if (gm == null || !File.Exists(GameBackgroundPath)) return;

            AssetDatabase.ImportAsset(GameBackgroundPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(GameBackgroundPath) as TextureImporter;
            if (importer != null)
            {
                bool dirty = importer.textureType != TextureImporterType.Sprite
                             || importer.spriteImportMode != SpriteImportMode.Single
                             || importer.mipmapEnabled
                             || importer.textureCompression != TextureImporterCompression.Uncompressed
                             || importer.wrapMode != TextureWrapMode.Clamp;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.spritePixelsPerUnit = 100f;
                if (dirty) importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GameBackgroundPath);
            CameraBackdrop backdrop = FindComponent<CameraBackdrop>(gm.gameObject.scene);
            if (sprite == null || backdrop == null) return;

            Transform root = backdrop.transform;
            DestroyNamed(root, "GamePhoto");
            DestroyNamed(root, "PhotoWash");

            var photo = new GameObject("GamePhoto");
            photo.transform.SetParent(root, false);
            photo.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            var renderer = photo.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            renderer.color = Color.white;
            renderer.sortingOrder = -206;
            backdrop.bgPhoto = renderer;

            FilmGridBackdrop generatedBackdrop = root.GetComponent<FilmGridBackdrop>();
            if (generatedBackdrop != null) generatedBackdrop.enabled = false;
            DestroyNamed(root, "AnimatedGridBackdropFX");

            // The selected chamber art already contains its own blue halo and vignette.
            backdrop.baseColors = new[] { Hex("020A14"), Hex("020812"), Hex("01060D") };
            backdrop.glowAColors = new[] { Color.clear, Color.clear, Color.clear };
            backdrop.glowBColors = new[] { Color.clear, Color.clear, Color.clear };
            backdrop.rayColors = new[] { Color.clear, Color.clear, Color.clear };
            backdrop.vignetteAlpha = 0f;

            AmbientParticles ambient = FindComponent<AmbientParticles>(gm.gameObject.scene);
            if (ambient != null) ambient.gameObject.SetActive(false);
        }

        static void BuildJoystick(Transform parent, Font font)
        {
            DestroyNamed(parent, "ArcadeJoystickHud");

            Sprite fill = LoadSprite("Fill.png");
            Sprite disc = LoadSprite("Disc.png");
            var root = CreateImage(parent, "ArcadeJoystickHud", fill, Hex("071D2B"),
                new Vector2(-220f, 4f), new Vector2(238f, 126f));
            var panelOutline = root.gameObject.AddComponent<Outline>();
            panelOutline.effectColor = WithAlpha(Cyan, 0.82f);
            panelOutline.effectDistance = new Vector2(1.5f, -1.5f);
            var panelShadow = root.gameObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0f, 0.01f, 0.02f, 0.62f);
            panelShadow.effectDistance = new Vector2(0f, -7f);

            CreateImage(root.rectTransform, "BaseShadow", disc, new Color(0f, 0f, 0f, 0.62f),
                new Vector2(-52f, -27f), new Vector2(112f, 34f));
            Image joystickBase = CreateImage(root.rectTransform, "Base", disc, Hex("0B3547"),
                new Vector2(-52f, -22f), new Vector2(112f, 34f));
            var baseOutline = joystickBase.gameObject.AddComponent<Outline>();
            baseOutline.effectColor = WithAlpha(Cyan, 0.68f);
            baseOutline.effectDistance = new Vector2(1f, -1f);

            CreateImage(root.rectTransform, "ShaftShadow", fill, new Color(0f, 0f, 0f, 0.55f),
                new Vector2(-49f, 12f), new Vector2(15f, 61f));
            CreateImage(root.rectTransform, "Shaft", fill, Hex("B4CDD5"),
                new Vector2(-52f, 16f), new Vector2(13f, 61f));
            CreateImage(root.rectTransform, "BallShadow", disc, new Color(0f, 0f, 0f, 0.62f),
                new Vector2(-49f, 47f), new Vector2(58f, 58f));
            Image ball = CreateImage(root.rectTransform, "Ball", disc, RedTop,
                new Vector2(-52f, 52f), new Vector2(54f, 54f));
            var ballOutline = ball.gameObject.AddComponent<Outline>();
            ballOutline.effectColor = Hex("FF9A7E");
            ballOutline.effectDistance = new Vector2(1.4f, -1.4f);
            CreateImage(ball.rectTransform, "Highlight", disc, new Color(1f, 1f, 1f, 0.25f),
                new Vector2(-9f, 9f), new Vector2(19f, 19f));

            Text title = CreateText(root.rectTransform, "Title", font, "JOYSTICK", 18,
                new Vector2(49f, 20f), new Vector2(112f, 28f));
            title.alignment = TextAnchor.MiddleLeft;
            title.color = LightLabel;
            Text action = CreateText(root.rectTransform, "Action", font, "MOVE", 13,
                new Vector2(49f, -12f), new Vector2(112f, 25f));
            action.alignment = TextAnchor.MiddleLeft;
            action.color = Hex("5EE8EC");
        }

        static void LayoutButton(Button button, Vector2 position, Vector2 size)
        {
            if (button == null) return;
            var rect = button.transform as RectTransform;
            if (rect == null) return;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Transform parent = rect.parent;
            if (parent != null)
            {
                RectTransform shadow = parent.Find(button.name + "Shadow") as RectTransform;
                if (shadow != null)
                {
                    shadow.anchoredPosition = position + new Vector2(0f, -9f);
                    shadow.sizeDelta = size + new Vector2(4f, 14f);
                }
                RectTransform highlight = parent.Find(button.name + "HL") as RectTransform;
                if (highlight != null)
                {
                    highlight.anchoredPosition = position;
                    highlight.sizeDelta = size + new Vector2(62f, 62f);
                }
                RectTransform glow = parent.Find(button.name + "Glow") as RectTransform;
                if (glow != null)
                {
                    glow.anchoredPosition = position;
                    glow.sizeDelta = size + new Vector2(70f, 70f);
                }
            }

            foreach (RectTransform child in button.GetComponentsInChildren<RectTransform>(true))
            {
                if (child == rect) continue;
                if (child.GetComponent<Text>() != null)
                {
                    child.anchorMin = Vector2.zero;
                    child.anchorMax = Vector2.one;
                    child.offsetMin = Vector2.zero;
                    child.offsetMax = Vector2.zero;
                }
                else if (child.name == "Gloss") child.sizeDelta = size - new Vector2(6f, 6f);
                else if (child.name == "Outline") child.sizeDelta = size;
                else if (child.name == "OutlineGlow") child.sizeDelta = size + Vector2.one * 7f;
            }
        }

        static void StyleButton(Button button, Color top, Color bottom, Color labelColor, string label)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);
            Graphic target = button.targetGraphic != null ? button.targetGraphic : button.image;
            if (target != null)
            {
                target.color = Color.white;
                UIGradient gradient = target.GetComponent<UIGradient>();
                if (gradient == null) gradient = target.gameObject.AddComponent<UIGradient>();
                gradient.top = top;
                gradient.bottom = bottom;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text text = ButtonLabel(button);
            if (text != null)
            {
                text.text = label;
                text.color = labelColor;
                text.fontStyle = FontStyle.Bold;
            }

            UIHoverScale hover = button.GetComponent<UIHoverScale>();
            if (hover != null && hover.highlight != null)
            {
                Image highlight = hover.highlight.GetComponent<Image>();
                if (highlight != null) highlight.color = WithAlpha(Color.Lerp(top, Color.white, 0.35f), 0.9f);
            }

            Image outline = FindChildImage(button.transform, "Outline");
            if (outline != null) outline.color = WithAlpha(Color.Lerp(top, Color.white, 0.52f), 0.96f);
            Image outlineGlow = FindChildImage(button.transform, "OutlineGlow");
            if (outlineGlow != null) outlineGlow.color = WithAlpha(top, 0.32f);
        }

        static void StyleArcadeButton(Button button, Color top, Color bottom, Color labelColor, string label)
        {
            if (button == null) return;
            StyleButton(button, top, bottom, labelColor, label);

            RectTransform rect = button.transform as RectTransform;
            Transform parent = rect != null ? rect.parent : null;
            if (rect == null || parent == null) return;

            string frameName = button.name + "ArcadeFrame";
            DestroyNamed(parent, frameName);
            Sprite fill = LoadSprite("Fill.png");
            Image frame = CreateImage(parent, frameName, fill, Hex("07111F"),
                rect.anchoredPosition + new Vector2(0f, -7f), rect.sizeDelta + new Vector2(34f, 30f));
            frame.rectTransform.SetSiblingIndex(rect.GetSiblingIndex());
            var frameGradient = frame.gameObject.AddComponent<UIGradient>();
            frameGradient.top = Color.Lerp(top, Hex("18253B"), 0.72f);
            frameGradient.bottom = Hex("020711");
            var frameOutline = frame.gameObject.AddComponent<Outline>();
            frameOutline.effectColor = WithAlpha(Color.Lerp(top, Color.white, 0.28f), 0.92f);
            frameOutline.effectDistance = new Vector2(2.2f, -2.2f);
            var frameShadow = frame.gameObject.AddComponent<Shadow>();
            frameShadow.effectColor = new Color(0f, 0f, 0f, 0.82f);
            frameShadow.effectDistance = new Vector2(0f, -10f);

            DestroyNamed(button.transform, "ArcadeInnerEdge");
            Image inner = CreateImage(button.transform, "ArcadeInnerEdge", LoadSprite("RingThin.png"),
                WithAlpha(Color.Lerp(top, Color.white, 0.50f), 0.95f), Vector2.zero,
                rect.sizeDelta - new Vector2(14f, 14f));
            inner.rectTransform.SetAsLastSibling();

            Text text = ButtonLabel(button);
            if (text != null)
            {
                text.rectTransform.SetAsLastSibling();
                text.font = LoadMenuFont();
                text.fontSize = label == "PLAY" ? 38 : 32;
                text.fontStyle = FontStyle.Normal;
                var shadow = text.GetComponent<Shadow>();
                if (shadow == null) shadow = text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
                shadow.effectDistance = new Vector2(0f, -3f);
            }
        }

        static void HideButtonAndDecorations(Transform parent, Button button)
        {
            if (button == null) return;
            button.gameObject.SetActive(false);
            SetChildActive(parent, button.name + "Shadow", false);
            SetChildActive(parent, button.name + "HL", false);
            SetChildActive(parent, button.name + "Glow", false);
        }

        static void SetChildActive(Transform parent, string name, bool active)
        {
            if (parent == null) return;
            Transform child = parent.Find(name);
            if (child != null) child.gameObject.SetActive(active);
        }

        static Text ButtonLabel(Button button)
        {
            if (button == null) return null;
            Text fallback = null;
            foreach (Text text in button.GetComponentsInChildren<Text>(true))
            {
                if (fallback == null) fallback = text;
                if (text.name == "Label" || text.name == "Lbl" || text.name == "MuteLbl") return text;
            }
            return fallback;
        }

        static void EnsureLevelFocusSelector(Button button, Color accent, float size)
        {
            if (button == null) return;
            Transform existing = button.transform.Find("LevelFocusSelector");
            Image ring;
            if (existing == null)
            {
                ring = CreateImage(button.transform, "LevelFocusSelector", LoadSprite("CellRing.png"),
                    Color.white, Vector2.zero, Vector2.one * size);
            }
            else ring = existing.GetComponent<Image>();
            if (ring == null) ring = existing.gameObject.AddComponent<Image>();

            RectTransform selector = ring.rectTransform;
            selector.anchorMin = selector.anchorMax = new Vector2(0.5f, 0.5f);
            selector.pivot = new Vector2(0.5f, 0.5f);
            selector.anchoredPosition = Vector2.zero;
            selector.sizeDelta = Vector2.one * size;
            selector.localScale = Vector3.one;
            selector.localRotation = Quaternion.identity;
            ring.sprite = LoadSprite("CellRing.png");
            ring.type = Image.Type.Sliced;
            ring.color = Color.Lerp(accent, Color.white, 0.72f);
            ring.raycastTarget = false;

            Outline outline = ring.GetComponent<Outline>();
            if (outline == null) outline = ring.gameObject.AddComponent<Outline>();
            outline.effectColor = WithAlpha(accent, 0.95f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            Shadow shadow = null;
            foreach (Shadow candidate in ring.GetComponents<Shadow>())
                if (candidate.GetType() == typeof(Shadow)) { shadow = candidate; break; }
            if (shadow == null) shadow = ring.gameObject.AddComponent<Shadow>();
            shadow.effectColor = WithAlpha(accent, 0.80f);
            shadow.effectDistance = new Vector2(0f, -4f);
            UIPulse pulse = ring.GetComponent<UIPulse>();
            if (pulse == null) pulse = ring.gameObject.AddComponent<UIPulse>();
            pulse.amplitude = 0.06f;
            pulse.speed = 3.5f;

            DestroyNamed(selector, "SelectorPointer");

            UIHoverScale hover = button.GetComponent<UIHoverScale>();
            if (hover == null) hover = button.gameObject.AddComponent<UIHoverScale>();
            if (hover.highlight != null && hover.highlight != ring.gameObject)
                hover.highlight.SetActive(false);
            hover.highlight = ring.gameObject;
            hover.hover = 1.16f;
            hover.press = 0.90f;
            selector.SetAsLastSibling();
            ring.gameObject.SetActive(false);
        }

        static Text ConfigureNumberedTutorialSkip(Button skip, Transform tutorialRoot)
        {
            if (skip == null || tutorialRoot == null) return null;

            RectTransform rect = skip.transform as RectTransform;
            if (rect == null) return null;
            rect.SetParent(tutorialRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -410f);
            rect.sizeDelta = new Vector2(310f, 72f);

            foreach (RectTransform child in skip.GetComponentsInChildren<RectTransform>(true))
            {
                if (child == rect) continue;
                if (child.name == "Face" || child.name == "Lip")
                    child.sizeDelta = rect.sizeDelta;
                else if (child.name == "Highlight")
                    child.sizeDelta = rect.sizeDelta + new Vector2(130f, 130f);
                else if (child.name == "Gloss")
                    child.sizeDelta = rect.sizeDelta - new Vector2(6f, 6f);
            }

            StyleButton(skip, PurpleTop, PurpleBottom, LightLabel, "SKIP TUTORIAL");
            Text label = ButtonLabel(skip);
            if (label != null)
            {
                label.fontSize = 20;
                label.alignment = TextAnchor.MiddleCenter;
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
            }

            DestroyNamed(skip.transform, "ButtonNumberBadge");
            Image badge = CreateImage(skip.transform, "ButtonNumberBadge", LoadSprite("Disc.png"),
                PurpleBottom, Vector2.zero, new Vector2(48f, 48f));
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            badge.rectTransform.pivot = new Vector2(0f, 0.5f);
            badge.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            var badgeOutline = badge.gameObject.AddComponent<Outline>();
            badgeOutline.effectColor = WithAlpha(Color.Lerp(PurpleTop, Color.white, 0.55f), 0.95f);
            badgeOutline.effectDistance = new Vector2(2f, -2f);

            Text number = CreateText(badge.transform, "ButtonNumber", ButtonFont(skip), "6", 26,
                Vector2.zero, new Vector2(48f, 48f));
            number.fontStyle = FontStyle.Bold;
            number.rectTransform.anchorMin = Vector2.zero;
            number.rectTransform.anchorMax = Vector2.one;
            number.rectTransform.offsetMin = Vector2.zero;
            number.rectTransform.offsetMax = Vector2.zero;
            badge.rectTransform.SetAsLastSibling();
            if (label != null) label.rectTransform.SetAsLastSibling();

            DestroyNamed(skip.transform, "SkipCountdown");
            Text countdown = CreateText(skip.transform, "SkipCountdown", ButtonFont(skip),
                "AUTO-CONTINUE IN 10", 18, Vector2.zero, new Vector2(310f, 30f));
            countdown.color = new Color(0.82f, 0.68f, 1f, 1f);
            countdown.rectTransform.anchorMin = countdown.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            countdown.rectTransform.pivot = new Vector2(0.5f, 0f);
            countdown.rectTransform.anchoredPosition = new Vector2(0f, 10f);
            var countdownOutline = countdown.gameObject.AddComponent<Outline>();
            countdownOutline.effectColor = new Color(0.01f, 0.02f, 0.06f, 0.90f);
            countdownOutline.effectDistance = new Vector2(1.5f, -1.5f);
            countdown.gameObject.SetActive(false);
            return countdown;
        }

        static Text FindChildText(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (Text text in parent.GetComponentsInChildren<Text>(true))
                if (text.name == name) return text;
            return null;
        }

        static RectTransform FindChildRect(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (RectTransform rect in parent.GetComponentsInChildren<RectTransform>(true))
                if (rect.name == name) return rect;
            return null;
        }

        static Font ButtonFont(Button button)
        {
            Text label = ButtonLabel(button);
            return label != null ? label.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static Image FindChildImage(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child.GetComponent<Image>();
            return null;
        }

        static Image CreateImage(Transform parent, string name, Sprite sprite, Color color,
            Vector2 position, Vector2 size, Vector2? anchor = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = a;
            rect.pivot = anchor.HasValue ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text CreateText(Transform parent, string name, Font font, string value, int fontSize,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = go.GetComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = LightLabel;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        static void DestroyNamed(Transform parent, string name)
        {
            if (parent == null) return;
            Transform existing = parent.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
        }

        static void EnsureMarker(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == Marker) return;
                if (root.name.StartsWith("LuxoddArcadeUI_Baked_"))
                    Object.DestroyImmediate(root);
            }
            var marker = new GameObject(Marker);
            marker.SetActive(false);
            SceneManager.MoveGameObjectToScene(marker, scene);
        }

        static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T value = root.GetComponentInChildren<T>(true);
                if (value != null) return value;
            }
            return null;
        }

        static Sprite LoadSprite(string file)
            => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Parabox/Sprites/" + file);

        static Sprite ImportUiSprite(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool dirty = importer.textureType != TextureImporterType.Sprite
                             || importer.spriteImportMode != SpriteImportMode.Single
                             || importer.mipmapEnabled
                             || importer.textureCompression != TextureImporterCompression.Uncompressed
                             || importer.wrapMode != TextureWrapMode.Clamp;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.spritePixelsPerUnit = 100f;
                if (dirty) importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Font LoadMenuFont()
            => AssetDatabase.LoadAssetAtPath<Font>(MenuFontPath)
               ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
