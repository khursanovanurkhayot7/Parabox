using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // ONE-TIME setup wizard (URP / WebGL, polished sprite look).
    // Campaign definitions below are also the reproducible source for the mechanic-rich curriculum v4.
    // Run "Tools > Parabox > Create Everything (Run Once)".
    //
    // It sets up URP, generates rounded-rectangle sprites procedurally, builds
    // sprite tile prefabs, 50 level prefabs, the MainMenu and Game scenes and adds
    // them to Build Settings. Safe to run again — it regenerates everything.
    //
    // After running it you can delete the Assets/Parabox/Editor folder.
    // DO NOT delete Assets/Parabox/Scripts — the game needs it!
    public static class ParaboxSetupWizard
    {
        const int CampaignRevision = 6;
        const string Root = "Assets/Parabox";
        const string SpriteDir = Root + "/Sprites";
        const string PrefabDir = Root + "/Prefabs";
        const string LevelDir = Root + "/Prefabs/Levels";
        const string TutorialDir = Root + "/Resources/Parabox/Tutorials";
        const string SceneDir = Root + "/Scenes";
        const string SettingsDir = Root + "/Settings";

        // ---------------------------------------------------------- palette
        static readonly Color BG = Hex("0B0E17");
        static readonly Color Accent = Hex("FF9E5E");
        static readonly Color DimText = Hex("8A92B5");
        static readonly Color ButtonCol = Hex("1C4C60");   // deep ocean-slate for secondary/back/quit buttons

        // Room floor colors, indexed by room id. Bright & saturated like the real game.
        static readonly Color[] RoomColors =
        {
            Hex("2E6FD8"), // 0 blue   (main rooms)
            Hex("C43F82"), // 1 magenta (interiors)
            Hex("E0912F"), // 2 orange
            Hex("3FA06A"), // 3 green
            Hex("7E57D8"), // 4 purple
        };

        static readonly Color BoxColor = Hex("F2A03E");
        static readonly Color PlayerColor = Hex("FF5FA0");
        static readonly Color EyeColor = Hex("241019");
        static readonly Color WallColor = Hex("141A28");
        static readonly Color ShadowCol = new Color(0f, 0f, 0f, 0.46f);

        // Sorting orders.
        const int OrderBackingInBox = -50;
        const int OrderFloor = 0;
        const int OrderBorder = 1;
        const int OrderGoal = 2;
        const int OrderWall = 4;
        const int OrderBox = 6;
        const int OrderPlayer = 8;
        const int OrderFrameInBox = 50;

        const int Res = 256; // sprite resolution (also pixels-per-unit → 1 sprite = 1 world unit)

        class Sprites { public Sprite fill, floorFill, tile, cell, cellRing, grid, ringThin, ringThick, ringBox, trophy, shaded, shadow, lockIcon, star, glow, vignette, disc, ringCircle, iconPlay, texGrass, texWater, texRock, texStars, iconTri, iconDrop, iconTreeR, iconDropR, iconStarR, iconWaveR, iconCoral, iconJelly, godRay; }

        // Rounded sprite + top-light gloss used to make every button look premium (set once sprites exist).
        static Sprite s_roundBtn;
        static Sprite s_gloss;
        static Sprite s_glow;   // soft glow used for the hover/selected button highlight
        static Font s_font;     // optional drop-in font from Assets/Parabox/Fonts (else the default UI font)
        class Tiles { public GameObject floor, grid, border, wall, box, metaBox, player, boxGoal, playerGoal; }

        // A failed level-authoring attempt can leave a raw Level_N root in MainMenu because the
        // exception happens before SavePrefab destroys its temporary object. These are ordinary
        // scene objects, never prefab instances. Remove only those leaked roots after scripts
        // reload; campaign prefab assets and intentional UI preview children remain untouched.
        [InitializeOnLoadMethod]
        static void QueueLeakedLevelMainMenuCleanup()
        {
            EditorApplication.delayCall -= RemoveLeakedLevelRootsFromMainMenu;
            EditorApplication.delayCall += RemoveLeakedLevelRootsFromMainMenu;
        }

        static void RemoveLeakedLevelRootsFromMainMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            int removed = 0;
            foreach (ParaboxLevel levelInfo in Resources.FindObjectsOfTypeAll<ParaboxLevel>())
            {
                if (levelInfo == null) continue;
                GameObject candidate = levelInfo.gameObject;
                if (candidate == null || candidate.transform.parent != null
                    || EditorUtility.IsPersistent(candidate)
                    || !candidate.scene.IsValid() || !candidate.scene.isLoaded
                    || candidate.scene.path != SceneDir + "/MainMenu.unity"
                    || PrefabUtility.GetPrefabInstanceStatus(candidate) != PrefabInstanceStatus.NotAPrefab)
                    continue;

                if (!candidate.name.StartsWith("Level_", System.StringComparison.Ordinal)
                    || !int.TryParse(candidate.name.Substring(6), out int number)
                    || number < 1 || number > 50)
                    continue;

                EditorSceneManager.MarkSceneDirty(candidate.scene);
                UnityEngine.Object.DestroyImmediate(candidate);
                removed++;
            }

            if (removed <= 0) return;
            Debug.Log($"Parabox: removed {removed} leaked level-authoring root(s) from MainMenu only.");
        }

        [InitializeOnLoadMethod]
        static void RunRequestedCampaignRebuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(projectRoot, "Library", "ParaboxRebuildCampaign.request");
            if (!File.Exists(request)) return;
            File.Delete(request);
            EditorApplication.delayCall += () =>
            {
                RegenerateDifficultyOrderedCampaign();
                var result = ParaboxCampaignValidator.ValidateCampaign();
                string reportPath = Path.Combine(projectRoot, "Library", "ParaboxFullCurveValidation.txt");
                File.WriteAllText(reportPath, result.report);
                if (result.failures == 0) Debug.Log(result.report);
                else Debug.LogError(result.report);
            };
        }

        // Narrow, non-destructive rebuild hook used while iterating on the approved Chapter V.
        // Keeping this separate from the full-campaign request prevents a Chapter V edit from
        // touching the forty already-approved level prefabs.
        [InitializeOnLoadMethod]
        static void RunRequestedMasterRebuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(projectRoot, "Library", "ParaboxRebuildMaster.request");
            if (!File.Exists(request)) return;
            File.Delete(request);
            EditorApplication.delayCall += () =>
            {
                RegenerateMasterLevels();
                Debug.Log("Parabox: applied the Chapter V advanced recursive-room rebuild request.");
            };
        }

        // Focused finale rebuild. Playtest feedback on Level 50 must never force a reserialization
        // of the other 49 approved boards, so the open editor consumes this request and replaces
        // only the last prefab from the reproducible definition below.
        [InitializeOnLoadMethod]
        static void RunRequestedFinaleRebuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(projectRoot, "Library", "ParaboxRebuildFinale.request");
            if (!File.Exists(request)) return;
            File.Delete(request);
            EditorApplication.delayCall += () =>
            {
                RegenerateFinaleSilent();
                var result = ParaboxCampaignValidator.ValidateCampaign();
                string reportPath = Path.Combine(projectRoot, "Library", "ParaboxFinaleValidation.txt");
                File.WriteAllText(reportPath,
                    $"failures={result.failures}\nwarnings={result.warnings}\n{result.report}");
                if (result.failures == 0) Debug.Log(result.report);
                else Debug.LogError(result.report);
            };
        }

        // Narrow rebuild hook for the Chapter I learning curve. A request file lets the already
        // open Unity editor regenerate only Levels 1-10 after scripts finish compiling; no second
        // editor process and no destructive full-project rebake are required.
        [InitializeOnLoadMethod]
        static void RunRequestedChapterOneRebuild()
        {
            EditorApplication.update -= TryRunRequestedChapterOneRebuild;
            EditorApplication.update += TryRunRequestedChapterOneRebuild;
        }

        static void TryRunRequestedChapterOneRebuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(projectRoot, "Library", "ParaboxRebuildChapterOne.request");
            if (!File.Exists(request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorApplication.update -= TryRunRequestedChapterOneRebuild;
            try
            {
                RegenerateChapterOneSilent();
                var result = ParaboxCampaignValidator.ValidateCampaign();
                if (result.failures == 0) Debug.Log(result.report);
                else Debug.LogError(result.report);
                File.Delete(request);
            }
            catch (System.Exception ex)
            {
                // Keep the request on disk so a failed rebuild is visible and retryable instead
                // of being reported as consumed while the shipped prefab remains stale.
                Debug.LogException(ex);
                EditorApplication.update -= TryRunRequestedChapterOneRebuild;
                EditorApplication.update += TryRunRequestedChapterOneRebuild;
            }
        }

        // Narrow rebuild hook for the Chapter II systems curriculum. The request is handled by
        // the already-open editor after scripts compile, avoiding both a project-lock conflict and
        // any reserialization of the forty levels outside the requested chapter.
        [InitializeOnLoadMethod]
        static void RunRequestedChapterTwoRebuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(projectRoot, "Library", "ParaboxRebuildChapterTwo.request");
            if (!File.Exists(request)) return;
            File.Delete(request);
            EditorApplication.delayCall += () =>
            {
                RegenerateChapterTwoSilent();
                var result = ParaboxCampaignValidator.ValidateCampaign();
                string reportPath = Path.Combine(projectRoot, "Library", "ParaboxChapterTwoValidation.txt");
                File.WriteAllText(reportPath,
                    $"failures={result.failures}\nwarnings={result.warnings}\n{result.report}");
                if (result.failures == 0) Debug.Log(result.report);
                else Debug.LogError(result.report);
            };
        }

        // Narrow rebuild hook for Chapter III. The open editor consumes the request after scripts
        // compile, rebuilds only Levels 21-30, and leaves a machine-readable full-campaign report.
        [InitializeOnLoadMethod]
        static void RunRequestedChapterThreeRebuild()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(projectRoot, "Library", "ParaboxRebuildChapterThree.request");
            if (!File.Exists(request)) return;
            File.Delete(request);
            EditorApplication.delayCall += () =>
            {
                RegenerateChapterThreeSilent();
                var result = ParaboxCampaignValidator.ValidateCampaign();
                string reportPath = Path.Combine(projectRoot, "Library", "ParaboxChapterThreeValidation.txt");
                File.WriteAllText(reportPath,
                    $"failures={result.failures}\nwarnings={result.warnings}\n{result.report}");
                if (result.failures == 0) Debug.Log(result.report);
                else Debug.LogError(result.report);
            };
        }

        // A focused safety repair for the Level-35 portal lesson. Earlier Chapter IV commands
        // created tutorials only after all ten levels, so a validation exception could leave the
        // campaign referring to a prefab that had never been written. On script reload, wait for
        // a safe Edit-Mode frame and create only the two Chapter IV tutorial assets when the portal
        // lesson is missing. No campaign level prefab is touched by this repair.
        [InitializeOnLoadMethod]
        static void QueueMissingPortalTutorialRepair()
        {
            EditorApplication.update -= TryRepairMissingPortalTutorial;
            EditorApplication.update += TryRepairMissingPortalTutorial;
        }

        static void TryRepairMissingPortalTutorial()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorApplication.update -= TryRepairMissingPortalTutorial;
            string portalPath = TutorialDir + "/Mechanic_Portal.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(portalPath) != null) return;

            try
            {
                Tiles tiles = LoadLevelTiles();
                if (tiles == null)
                    throw new System.InvalidOperationException(
                        "Cannot repair Mechanic_Portal because the Parabox level tiles are missing.");
                RegeneratePremiumChapterFourTutorialsAuthoringOnly(tiles);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Parabox: repaired the missing prebuilt Mechanic_Portal tutorial without rebuilding levels.");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        [MenuItem("Tools/Parabox/Create Everything (Run Once)")]
        public static void CreateEverything()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CleanGenerated();
            CreateFolders();

            SetupUrpPipeline();

            var spr = CreateSprites();
            s_roundBtn = spr.fill;
            s_gloss = spr.shaded;
            s_glow = spr.glow;
            s_font = LoadGameFont();
            var tiles = CreateTilePrefabs(spr);
            var levels = CreateLevelPrefabs(tiles);
            string gamePath = CreateGameScene(spr, tiles, levels);
            string menuPath = CreateMainMenuScene(spr, levels, tiles);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(menuPath, true),
                new EditorBuildSettingsScene(gamePath, true)
            };

            // Use Unity's .unityweb fallback because the upload host cannot be relied on to
            // serve plain .wasm files as application/wasm.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(menuPath);

            EditorUtility.DisplayDialog("Parabox",
                "Setup complete!\n\n" +
                "URP assigned, rounded sprites generated, tile prefabs, 50 level\n" +
                "prefabs, MainMenu + Game scenes created and added to Build Settings.\n\n" +
                "You can now DELETE the folder Assets/Parabox/Editor.\n" +
                "Keep Assets/Parabox/Scripts — the game needs it!",
                "OK");
        }

        [MenuItem("Tools/Parabox/Generate Final Solvable Campaign (1-50)")]
        public static void RegenerateDifficultyOrderedCampaign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Final Campaign Generator",
                    "Exit Play Mode first. This command creates and validates prefabs only in Edit Mode.",
                    "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Final Campaign Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            var defs = Levels();
            for (int i = 0; i < defs.Length; i++)
            {
                BuildLevelPrefab(i, defs[i], tiles);
                if (string.IsNullOrEmpty(defs[i].solution))
                {
                    string path = LevelDir + "/Level_" + (i + 1) + ".prefab";
                    string proof = ParaboxCampaignSolver.SolveAndStore(
                        path, 1, Mathf.Max(48, defs[i].par + 24));
                    Debug.Log($"Parabox: solved L{i + 1:00} in {proof.Length} moves ({proof}).");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // The player will perform the visual/cabinet QA. This release gate is intentionally
            // Edit Mode only: it parses each generated prefab, replays its stored authored route,
            // checks every goal, and audits uniqueness plus the complete 1 -> 50 difficulty curve.
            ParaboxCampaignValidator.ValidationResult validation =
                ParaboxCampaignValidator.ValidateCampaign();
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string reportPath = System.IO.Path.Combine(
                projectRoot, "Library", "ParaboxFinalCampaignValidation.txt");
            System.IO.File.WriteAllText(reportPath,
                $"revision={CampaignRevision}\nfailures={validation.failures}\n" +
                $"warnings={validation.warnings}\n{validation.report}");
            if (validation.failures > 0)
                throw new System.InvalidOperationException(
                    $"Final campaign generation found {validation.failures} release error(s). " +
                    $"Nothing is ready to publish. Read {reportPath} for the exact level(s).");

            Debug.Log($"Parabox: generated and verified all 50 unique, solvable levels in " +
                $"progressive difficulty order (revision {CampaignRevision}). Report: {reportPath}");
        }

        // Keep the old automation entry point working for existing request files and team scripts.
        [MenuItem("Tools/Parabox/Regenerate Difficulty-Ordered Campaign (1-50)")]
        static void RegenerateDifficultyOrderedCampaignLegacyMenu()
            => RegenerateDifficultyOrderedCampaign();

        // Pure prefab authoring for designers who want to perform all gameplay QA themselves.
        // Unlike the regular regeneration command, this never invokes the campaign solver.
        public static void RegenerateDifficultyOrderedCampaignAuthoringOnly()
        {
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");

            var defs = Levels();
            for (int i = 0; i < defs.Length; i++)
            {
                if (string.IsNullOrEmpty(defs[i].solution))
                    throw new System.InvalidOperationException(
                        $"Level {i + 1} has no authored route. Nothing was solver-tested automatically; " +
                        "add its route to ParaboxSetupWizard before generating.");
                BuildLevelPrefab(i, defs[i], tiles);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Parabox: authored all 50 level prefabs without Play Mode, solver or validator (revision {CampaignRevision}).");
        }

        // Refreshes curriculum fields without rebuilding a single room, marker or stored route.
        // Use this when wording or first-appearance metadata changes after playtesting; keeping it
        // separate from regeneration makes the safe operation explicit in CI and local recovery.
        public static void RefreshCampaignMetadataFromCommandLine()
        {
            int updated = 0;
            for (int index = 0; index < 50; index++)
            {
                string path = LevelDir + "/Level_" + (index + 1) + ".prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                    if (info == null)
                        throw new System.InvalidOperationException($"{path} has no ParaboxLevel component.");

                    CampaignProgression.Profile progression = CampaignProgression.ForLevel(index);
                    info.difficultyRating = progression.rating;
                    info.chapter = progression.chapter;
                    info.chapterName = progression.chapterName;
                    info.chapterPhilosophy = progression.philosophy;
                    info.progressionRole = progression.role;
                    info.mechanicFocus = progression.mechanicFocus;
                    info.introducesMechanic = progression.introducesMechanic;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    updated++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Parabox: refreshed curriculum metadata on {updated}/50 level prefabs without rebuilding gameplay.");
        }

        [MenuItem("Tools/Parabox/Regenerate Master Levels (41-50)")]
        public static void RegenerateMasterLevels()
            => RegenerateNestedChapterFiveSilent();

        // A deliberately obvious alternative to the long nested menu path. This opens a small
        // authoring window instead of silently running anything, so the designer can see why the
        // button is disabled (Play Mode, compilation or import) before starting the replacement.
        [MenuItem("Tools/Parabox/OPEN CHAPTER 5 REBUILDER", priority = 1)]
        public static void OpenChapterFiveRebuilder()
        {
            ChapterFiveRebuildWindow window =
                EditorWindow.GetWindow<ChapterFiveRebuildWindow>(true, "Chapter 5 Rebuilder", true);
            window.minSize = new Vector2(460f, 285f);
            window.maxSize = new Vector2(620f, 420f);
            window.Show();
        }

        sealed class ChapterFiveRebuildWindow : EditorWindow
        {
            void OnGUI()
            {
                GUILayout.Space(14f);
                GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                };
                GUILayout.Label("EXTREME NESTED CHAPTER 5", title);
                GUILayout.Space(10f);

                EditorGUILayout.HelpBox(
                    "Replaces only Level 41–50 and the Chapter 5 tutorial. Chapters 1–4 are not "
                    + "rebuilt. Old factory/snake prefabs are replaced only after all ten nested "
                    + "levels pass the authoring preflight.", MessageType.Info);

                bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
                bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
                if (playing)
                    EditorGUILayout.HelpBox("Stop Play Mode before rebuilding.", MessageType.Warning);
                else if (busy)
                    EditorGUILayout.HelpBox("Unity is compiling/importing. Wait until this message disappears.",
                        MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("Ready. This creates prefabs in Edit Mode; it does not play the game.",
                        MessageType.None);

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(playing || busy))
                {
                    Color previous = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.30f, 0.85f, 0.42f);
                    if (GUILayout.Button("REBUILD CHAPTER 5 NOW", GUILayout.Height(68f)))
                    {
                        Close();
                        // Run after the window event finishes so Unity does not execute a long
                        // prefab operation from inside the GUI callback.
                        EditorApplication.delayCall += RunChapterFiveRebuildFromButton;
                    }
                    GUI.backgroundColor = previous;
                }
                GUILayout.Space(14f);
            }
        }

        static void RunChapterFiveRebuildFromButton()
        {
            try
            {
                RegenerateNestedChapterFiveSilent();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Chapter 5 rebuild stopped",
                    exception.Message + "\n\nThe existing prefabs were kept unchanged.", "OK");
            }
        }

        // Focused authoring-only Chapter V generator. Every finale board uses Chapter IV's
        // box-inside-a-box language, then combines it with the portal plus Chapter I one-way and
        // button/gate commitments. It never enters Play Mode and writes only Levels 41-50.
        [MenuItem("Tools/Parabox/Levels/Rebuild Extreme Chapter 5 (41-50 - Nested Finale)", priority = 1341)]
        public static void RegenerateNestedChapterFiveSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 5 Generator",
                    "Exit Play Mode first. This command creates prefabs only in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 5 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");

            // Failed validation from an earlier attempt can leave temporary Level_41 roots in the
            // open Main Menu scene. Remove only those non-prefab authoring roots before rebuilding.
            for (int levelNumber = 41; levelNumber <= 50; levelNumber++)
                RemoveLeakedLevelAuthoringRoots(levelNumber);

            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "Rebuild Extreme Chapter 5?",
                    "This replaces only Level_41.prefab through Level_50.prefab and the Chapter 5 "
                    + "tutorial. Chapters 1-4 are not rebuilt. Every board uses four or five connected "
                    + "room scales, an open Level-42-style chamber and Chapter 4's box-inside-box play. "
                    + "Every board keeps at least five authored completion jobs with no filler cargo. "
                    + "Difficulty comes from an authored cargo gate hold, a mandatory portal and "
                    + "increasingly strict one-way decisions—not inflated move counts. No generic grey cargo is generated.",
                    "Rebuild Chapter 5",
                    "Cancel"))
                return;

            LevelDef[] defs = ChapterFiveExtremeNestedRooms();
            ValidateAdvancedRecursiveChapterFiveDefinitions(defs);
            PreflightChapterFiveLevels(defs, tiles);
            for (int i = 0; i < defs.Length; i++)
            {
                defs[i].designComplexity = ReviewedCampaignDifficulty(defs[i], 40 + i);
                BuildFocusedChapterFiveLevel(40 + i, defs[i], tiles);
            }
            RegenerateExtremeChapterFiveTutorialAuthoringOnly(tiles);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated only the extreme nested Chapter V Levels 41-50 and "
                + "its prebuilt tutorial. Chapters I-IV were not touched. Each saved finale route "
                + "uses at least five authored completion jobs, its authored cargo gate "
                + "hold, rising one-way commitments, four or five recursive room scales and a mandatory "
                + "compact portal exit.");
        }

        // Validate all ten fully parsed runtime models before replacing a shipped prefab. The old
        // loop saved Levels 41 and 42, then failed on Level 43 and left Levels 43-50 as obsolete
        // colour-factory boards. A disposable prefab lets the existing route/mechanic validator
        // inspect the exact rebalanced model while keeping the released chapter atomic.
        static void PreflightChapterFiveLevels(LevelDef[] levels, Tiles tiles)
        {
            try
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    int levelIndex = 40 + i;
                    // LevelParser derives the campaign index from trailing digits in the prefab
                    // name. Keep the level number at the end of this disposable asset name so the
                    // preflight receives the same Chapter V rebalancing as Level_41..Level_50.
                    string preflightPath = LevelDir + "/__ChapterFivePreflight_"
                        + (levelIndex + 1) + ".prefab";
                    AssetDatabase.DeleteAsset(preflightPath);
                    GameObject prefab = BuildLevelPrefab(levelIndex, levels[i], tiles, preflightPath);
                    if (prefab == null)
                        throw new System.InvalidOperationException(
                            $"Level {levelIndex + 1} preflight prefab could not be created.");
                    ValidateCampaignMechanicTasks(prefab, levels[i], levelIndex + 1);
                    AssetDatabase.DeleteAsset(preflightPath);
                }
            }
            finally
            {
                for (int i = 0; i < levels.Length; i++)
                    AssetDatabase.DeleteAsset(LevelDir + "/__ChapterFivePreflight_"
                        + (41 + i) + ".prefab");
            }
        }

        static GameObject BuildFocusedChapterFiveLevel(int levelIndex, LevelDef level, Tiles tiles)
        {
            int levelNumber = levelIndex + 1;
            if (levelNumber < 41 || levelNumber > 50)
                throw new System.ArgumentOutOfRangeException(nameof(levelIndex),
                    "The Chapter V builder may write only Levels 41-50.");

            RemoveLeakedLevelAuthoringRoots(levelNumber);
            try
            {
                return BuildLevelPrefab(levelIndex, level, tiles);
            }
            finally
            {
                // BuildLevelPrefab normally destroys its temporary root after saving. Validation
                // exceptions happen before that save, so guarantee cleanup on both paths.
                RemoveLeakedLevelAuthoringRoots(levelNumber);
            }
        }

        // Rebuild just Level 50 after finale playtests. Keeping this public also gives designers a
        // safe menu-independent automation entry point without disturbing levels 1-49.
        public static void RegenerateFinaleSilent()
        {
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            var defs = Levels();
            if (defs.Length < 50) throw new System.InvalidOperationException("Parabox finale definition is missing.");
            BuildLevelPrefab(49, defs[49], tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated only Level 50.");
        }

        // Narrow rebuild used while inspecting Level 48. It reads from the canonical reviewed
        // campaign so a focused rebuild can never restore the retired Colour Factory board.
        public static void RegenerateLevel48Silent()
        {
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            var campaign = Levels();
            if (campaign.Length < 48) throw new System.InvalidOperationException("Parabox Level 48 definition is missing.");
            BuildLevelPrefab(47, campaign[47], tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated only Level 48.");
        }

        // Authoring probe for Chapter V chamber iteration. It builds only the requested source
        // definition in memory, asks the exact runtime solver for a route, and never touches a
        // shipped prefab unless the designer later copies the proven route into the definition.
        public static string ProbeChapterFiveLevel(int levelNumber, int maximumDepth = 90)
        {
            if (levelNumber < 41 || levelNumber > 50)
                throw new System.ArgumentOutOfRangeException(nameof(levelNumber));
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            var defs = Levels();
            int index = levelNumber - 1;
            string probePath = LevelDir + "/__ChapterFiveProbe.prefab";
            AssetDatabase.DeleteAsset(probePath); // exact disposable authoring asset, never shipped
            try
            {
                GameObject temporary = BuildLevelPrefab(index, defs[index], tiles, probePath);
                string route = ParaboxCampaignSolver.FindShortest(
                    temporary, 1, maximumDepth, 5000000);
                if (route == null)
                    throw new System.InvalidOperationException(
                        $"No Level {levelNumber} route found by depth {maximumDepth}.");
                Debug.Log($"Parabox probe L{levelNumber}: {route.Length} moves ({route}).");
                return route;
            }
            finally
            {
                AssetDatabase.DeleteAsset(probePath);
            }
        }

        public static void ProbeChapterFiveFromCommandLine()
        {
            int level = 45;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-paraboxLevel") int.TryParse(args[i + 1], out level);
            ProbeChapterFiveLevel(level);
        }

        // Command-line safe rebuild for the Chapter III opening trio. Keeping this narrow avoids
        // touching the other 47 approved prefabs while applying playtest difficulty feedback.
        public static void RegenerateChapterThreeOpeningSilent()
        {
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            var defs = ChapterThreeSynergy();
            ValidateChapterThreeRebuildDefinitions(defs);
            for (int i = 0; i < Mathf.Min(3, defs.Length); i++)
            {
                defs[i].designComplexity = ChapterThreeDifficulty(defs[i]);
                BuildFocusedChapterThreeLevel(20 + i, defs[i], tiles);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated rebalanced Levels 21-23.");
        }

        // Focused authoring-only repair for the two reported blocked Chapter III boards. The
        // command writes only Level_22 and Level_26 from the reviewed definitions above. It does
        // not enter Play Mode, run the game, or touch any other campaign prefab.
        [MenuItem("Tools/Parabox/Regenerate Solvable Levels 22 and 26")]
        public static void RegenerateSolvableLevelsTwentyTwoAndTwentySixSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Level 22/26 Generator",
                    "Exit Play Mode first. This command only creates prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Level 22/26 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            var defs = ChapterThreeSynergy();
            ValidateChapterThreeRebuildDefinitions(defs);
            defs[1].designComplexity = ChapterThreeDifficulty(defs[1]);
            defs[5].designComplexity = ChapterThreeDifficulty(defs[5]);
            BuildFocusedChapterThreeLevel(21, defs[1], tiles);
            BuildFocusedChapterThreeLevel(25, defs[5], tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated solvable Levels 22 and 26 without entering Play Mode.");
        }

        // Authoring-only Chapter III rebuild. This deliberately reads ChapterThreeSynergy()
        // directly instead of the old whole-campaign room ordering, which mixed several Chapter
        // II boards back into Levels 21-30. Only the ten Chapter III prefabs and their own opener
        // tutorial are written; Chapters I and II are never rebuilt by this command.
        [MenuItem("Tools/Parabox/Levels/Rebuild Chapter 3 (Levels 21-30 - 3+ Tasks Each)", priority = 1321)]
        public static void RegenerateChapterThreeSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 3 Generator",
                    "Exit Play Mode first. This command only creates prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 3 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "Rebuild Chapter 3?",
                    "This replaces only Level_21.prefab through Level_30.prefab and the Chapter 3 "
                    + "and pre-Level-25 portal tutorials. Chapters 1 and 2 stay unchanged. Every "
                    + "Chapter 3 level has at least three visible tasks and is checked as a strict "
                    + "difficulty step.",
                    "Rebuild Chapter 3",
                    "Cancel"))
            {
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            LevelDef[] defs = ChapterThreeSynergy();
            ValidateChapterThreeRebuildDefinitions(defs);

            GameObject lastPrefab = null;
            for (int chapterIndex = 0; chapterIndex < defs.Length; chapterIndex++)
            {
                if (string.IsNullOrEmpty(defs[chapterIndex].solution))
                    throw new System.InvalidOperationException(
                        $"Chapter III Level {21 + chapterIndex} has no authored route; generation stopped.");
                lastPrefab = BuildFocusedChapterThreeLevel(20 + chapterIndex, defs[chapterIndex], tiles);
            }
            RegeneratePremiumChapterThreeTutorialsAuthoringOnly(tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = lastPrefab;
            EditorGUIUtility.PingObject(lastPrefab);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Chapter 3 Ready",
                    "Levels 21-30 were rebuilt as ten different recursive-room puzzles. Levels "
                    + "25-30 require the premium cyan portal introduced before Level 25. The "
                    + "authored difficulty evidence rises on every level.",
                    "OK");
            Debug.Log("Parabox: rebuilt only progressive Chapter III Levels 21-30 and its tutorial "
                + "without entering Play Mode. Chapters I-II were not touched.");
        }

        // Chapter-IV-only authoring pass. Every board exposes at least four completion tasks and
        // exceeds the strongest Chapter III evidence. Levels 35-40 add a mandatory cyan portal
        // exit; the separate Mechanic_Portal mini-board is generated for the pre-Level-35 lesson.
        [MenuItem("Tools/Parabox/Levels/Rebuild Premium Chapter 4 (31-40 - Harder Original Rooms)", priority = 1331)]
        public static void RegenerateChapterFourSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 4 Generator",
                    "Exit Play Mode first. This command only creates prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 4 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                    "Rebuild Premium Chapter 4?",
                    "This replaces only Level_31.prefab through Level_40.prefab and the two "
                    + "Chapter 4 tutorial assets. Chapters 1-3 level prefabs and tutorials are not rebuilt. "
                    + "The original room-based gameplay is preserved and made harder with route-proven "
                    + "room positioning, exit-side and one-way constraints. Levels 35-40 still require the portal mechanic.",
                    "Rebuild Chapter 4",
                    "Cancel"))
            {
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");

            // Tutorials are prerequisites, so write them before any level validation/build can
            // interrupt the command. This guarantees Level 35 never references a missing portal
            // lesson even when a later level definition still needs designer correction.
            RegeneratePremiumChapterFourTutorialsAuthoringOnly(tiles);

            LevelDef[] defs = ChapterFourPremiumRooms();
            ValidatePremiumChapterFourDefinitions(defs);

            GameObject lastPrefab = null;
            int previousRouteEvidence = -1;
            for (int chapterIndex = 0; chapterIndex < defs.Length; chapterIndex++)
            {
                lastPrefab = BuildFocusedChapterFourLevel(30 + chapterIndex, defs[chapterIndex], tiles);
                ChapterFourDifficultyEvidence.Result evidence =
                    ChapterFourDifficultyEvidence.Evaluate(lastPrefab, defs[chapterIndex].solution);
                if (evidence.score <= previousRouteEvidence)
                    throw new System.InvalidOperationException(
                        $"Level {31 + chapterIndex} route evidence {evidence.score} must exceed "
                        + $"the previous Chapter IV level's {previousRouteEvidence}.");
                previousRouteEvidence = evidence.score;
                Debug.Log($"[Parabox] Chapter IV L{31 + chapterIndex}: route evidence "
                    + $"{evidence.score}, rooms={evidence.roomCount}, "
                    + $"room-moves={evidence.metaBoxMoves}, crossings={evidence.playerBoundaryCrossings}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = lastPrefab;
            EditorGUIUtility.PingObject(lastPrefab);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Premium Chapter 4 Ready",
                    "Levels 31-40 keep their original nested-room gameplay with more required decisions. Difficulty rises on "
                    + "every level, and Levels 35-40 keep the mandatory portal exit taught before Level 35. "
                    + "Open Level_31.prefab and check the chapter in game.",
                    "OK");
            Debug.Log("Parabox: rebuilt only premium Chapter IV Levels 31-40 plus prebuilt tutorials, "
                + "without entering Play Mode or rebuilding Chapters I-III.");
        }

        // Difficulty-only Chapter I rebuild. It writes just the ten foundation level prefabs in
        // Edit Mode and deliberately leaves tutorials, UI, clocks and every later chapter alone.
        [MenuItem("Tools/Parabox/Rebuild Chapter 1 Difficulty Only", priority = 20)]
        public static void RegenerateChapterOneDifficultyOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 1 Difficulty",
                    "Exit Play Mode first. This command only creates level prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 1 Difficulty",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            Tiles tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            LevelDef[] definitions = ChapterOneFoundations();
            ValidateChapterOneRebuildDefinitions(definitions);
            GameObject lastPrefab = null;
            for (int i = 0; i < definitions.Length; i++)
            {
                RemoveLeakedLevelAuthoringRoots(i + 1);
                lastPrefab = BuildLevelPrefab(i, definitions[i], tiles);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (lastPrefab != null)
            {
                Selection.activeObject = lastPrefab;
                EditorGUIUtility.PingObject(lastPrefab);
            }
            EditorUtility.DisplayDialog("Chapter 1 Difficulty Ready",
                "Levels 1-10 now form a beginner-friendly difficulty ladder. Only the Chapter 1 puzzle "
                + "layouts and their authored routes were rebuilt; UI, timers, scoring, tutorials and "
                + "Chapters 2-5 were not changed.", "OK");
            Debug.Log("Parabox: rebuilt only the Chapter I difficulty curve (Levels 1-10) in Edit Mode.");
        }

        // Full Chapter I rebuild. It writes the ten foundation levels without entering Play
        // Mode, then rebuilds the complete prebuilt tutorial set in Edit Mode.
        [MenuItem("Tools/Parabox/Regenerate Progressive Chapter 1 + Tutorials")]
        public static void RegenerateChapterOneSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 1 Generator",
                    "Exit Play Mode first. This command only creates prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 1 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            // Keep this narrow rebuild independent from Chapters II-V. Designers can iterate on
            // the foundation curve without an unrelated later-chapter draft blocking generation.
            var defs = ChapterOneFoundations();
            ValidateChapterOneRebuildDefinitions(defs);
            for (int i = 0; i < defs.Length; i++)
            {
                BuildLevelPrefab(i, defs[i], tiles);
            }
            // Mechanic_ButtonGate is already a prebuilt tutorial asset. MechanicCatalog schedules
            // it before Level 5, so this focused command must not rewrite any chapter tutorial.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated beginner Chapter I Levels 1-10 without touching other chapters or tutorials.");
        }

        static void ValidateChapterOneRebuildDefinitions(LevelDef[] defs)
        {
            if (defs == null || defs.Length != 10)
                throw new System.InvalidOperationException(
                    $"Chapter I must contain exactly 10 authored puzzles; found {defs?.Length ?? 0}.");

            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var layouts = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < defs.Length; i++)
            {
                LevelDef level = defs[i];
                int number = i + 1;
                if (level == null || string.IsNullOrWhiteSpace(level.name) || !names.Add(level.name))
                    throw new System.InvalidOperationException(
                        $"Chapter I Level {number} needs a unique authored name.");
                if (string.IsNullOrWhiteSpace(level.solution) || level.solution.Length != level.par
                    || level.par < MinimumAuthoredPars[i])
                    throw new System.InvalidOperationException(
                        $"Chapter I Level {number} needs an explicit route at or above its reviewed difficulty floor.");
                if (level.rooms == null || level.rooms.Length != 1 || level.rooms[0] == null
                    || level.rooms[0].Length == 0 || string.IsNullOrEmpty(level.rooms[0][0]))
                    throw new System.InvalidOperationException(
                        $"Chapter I Level {number} must contain one non-empty board.");

                string[] room = level.rooms[0];
                int width = room[0].Length;
                var fingerprint = new System.Text.StringBuilder();
                bool hasButton = false;
                bool hasGate = false;
                for (int y = 0; y < room.Length; y++)
                {
                    if (room[y] == null || room[y].Length != width)
                        throw new System.InvalidOperationException(
                            $"Chapter I Level {number} row {y} is not rectangular.");
                    fingerprint.Append(room[y]).Append('/');
                    foreach (char cell in room[y])
                    {
                        bool allowed = cell == '#' || cell == '.' || cell == 'P' || cell == 'p'
                            || cell == 'b' || cell == 'x' || cell == 'B' || cell == 'G'
                            || OneWayDirs.ContainsKey(cell);
                        if (!allowed)
                            throw new System.InvalidOperationException(
                                $"Chapter I Level {number} uses unsupported cell '{cell}'.");
                        hasButton |= cell == 'B';
                        hasGate |= cell == 'G';
                    }
                }
                if (!layouts.Add(fingerprint.ToString()))
                    throw new System.InvalidOperationException(
                        $"Chapter I Level {number} repeats an earlier board layout.");
                if (hasButton != hasGate)
                    throw new System.InvalidOperationException(
                        $"Chapter I Level {number} must include button and gate as one complete mechanic.");
                if ((i < 4 && (hasButton || hasGate)) || (i >= 4 && (!hasButton || !hasGate)))
                    throw new System.InvalidOperationException(
                        "Button and gate must first appear together on Chapter I Level 5 and remain purposeful through Level 10.");
            }
        }

        // Focused one-click rebuild for the reviewed Level 5 replacement. It creates the complete
        // prefab in Edit Mode and replays the stored authored route before saving; it never enters
        // Play Mode and does not touch the timer, another level, or any tutorial.
        [MenuItem("Tools/Parabox/Rebuild Easier Level 5 (First Gate)", priority = 22)]
        public static void RegenerateHarderLevelFive()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Level 5 Generator",
                    "Stop Play Mode first, then run this command again.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Level 5 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            Tiles tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            LevelDef[] definitions = ChapterOneFoundations();
            ValidateChapterOneRebuildDefinitions(definitions);
            GameObject prefab = BuildLevelPrefab(4, definitions[4], tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("Beginner Level 5 Ready",
                "Level 5 is now First Gate: a short button-and-gate practice board. " +
                "The prebuilt tutorial is scheduled before it; timer values were not changed.", "OK");
        }

        // Focused authoring command for the Level 5/6 difficulty correction. This creates the
        // finished prefab objects in Edit Mode and deliberately leaves every other level alone.
        [MenuItem("Tools/Parabox/Regenerate Levels 5-6")]
        public static void RegenerateLevelsFiveAndSixSilent()
        {
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            // Use the focused Chapter-I source directly. Pulling these two boards through the
            // full 50-level ordering made this repair harder to reason about and could conceal a
            // stale duplicate behind curriculum remapping.
            var defs = ChapterOneFoundations();
            ValidateChapterOneRebuildDefinitions(defs);
            for (int i = 4; i <= 5; i++)
            {
                RemoveLeakedLevelAuthoringRoots(i + 1);
                BuildLevelPrefab(i, defs[i], tiles);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: rebuilt distinct Levels 5 and 6. Level 6 now has a separate "
                + "horizontal-gate route and a longer validated solution.");
        }

        // Focused authoring-only correction for the two green button/gate puzzles. Their player
        // goals are statically sealed behind a gate, so neither level can finish unless a crate is
        // deliberately left on the green button. This command never enters Play Mode or runs a
        // solver; it only creates the reviewed prefab objects from the definitions below.
        [MenuItem("Tools/Parabox/Regenerate Mandatory-Gate Levels 6 and 9")]
        public static void RegenerateMandatoryGateLevelsSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Gate Level Generator",
                    "Exit Play Mode first. This command only creates prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Gate Level Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            var defs = Levels();
            BuildLevelPrefab(5, defs[5], tiles);
            BuildLevelPrefab(8, defs[8], tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: regenerated Levels 6 and 9 with mandatory green button/gate tasks.");
        }

        // Restores the saved room-inside-a-box Chapter II without touching Chapter I, the UI or
        // Chapters III-V. Every route is replayed in a disposable prefab before replacement.
        [MenuItem("Tools/Parabox/Regenerate Chapter 2 (Levels 11-20)")]
        public static void RegenerateChapterTwoSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 2 Generator",
                    "Exit Play Mode first. This command only creates prefabs in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 2 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            LevelDef[] defs = RestoredChapterTwoLevels();
            if (!TutorialPuzzleLibrary.Exists(10, MechanicCatalog.Id.NestedBoard))
                throw new System.InvalidOperationException(
                    "Chapter II needs its prebuilt room-inside-a-box tutorial prefab.");

            string probePath = LevelDir + "/__ChapterTwoRestoreProbe.prefab";
            AssetDatabase.DeleteAsset(probePath);
            try
            {
                for (int chapterIndex = 0; chapterIndex < defs.Length; chapterIndex++)
                {
                    int campaignIndex = 10 + chapterIndex;
                    GameObject probe = BuildLevelPrefab(campaignIndex, defs[chapterIndex], tiles, probePath);
                    ValidateCampaignMechanicTasks(probe, defs[chapterIndex], campaignIndex + 1);
                    AssetDatabase.DeleteAsset(probePath);
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(probePath);
            }

            for (int chapterIndex = 0; chapterIndex < defs.Length; chapterIndex++)
            {
                int campaignIndex = 10 + chapterIndex;
                BuildLevelPrefab(campaignIndex, defs[chapterIndex], tiles);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: restored saved Inside the Box Levels 11-20. Every authored route "
                + "won during preflight; runtime keeps route length + 3 moves. Chapter I, "
                + "Chapters III-V, timers and UI were not changed.");
        }

        // Focused recovery for the high-priority fresh-launch crash. Only Level_11.prefab is
        // replaced, after its room-inside-a-box definition and winning route pass the same
        // preflight used by the complete Chapter II rebuild.
        [MenuItem("Tools/Parabox/Levels/Rebuild Level 11 (Fresh-Launch Safe)", priority = 1311)]
        public static void RegenerateLevelElevenSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException(
                    "Exit Play Mode before rebuilding Level 11.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new System.InvalidOperationException(
                    "Unity is compiling or importing; rebuild Level 11 after it finishes.");

            Tiles tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            const int campaignIndex = 10;
            LevelDef definition = RestoredChapterTwoLevels()[0];
            string probePath = LevelDir + "/__LevelElevenRepairProbe.prefab";
            AssetDatabase.DeleteAsset(probePath);
            try
            {
                GameObject probe = BuildLevelPrefab(campaignIndex, definition, tiles, probePath);
                ValidateCampaignMechanicTasks(probe, definition, campaignIndex + 1);
            }
            finally
            {
                AssetDatabase.DeleteAsset(probePath);
            }

            RemoveLeakedLevelAuthoringRoots(campaignIndex + 1);
            GameObject prefab = BuildLevelPrefab(campaignIndex, definition, tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("[Parabox] Level 11 rebuilt as the solver-validated two-room Doorway puzzle; "
                + "no other campaign level was changed.");
        }

        // Ten distinct three-task recursive boards. Mirrored variants preserve their proven
        // solutions while changing approach sides; later entries deepen from two coordinate
        // spaces to three instead of inflating the beginner task count.
        static LevelDef[] ReviewedChapterTwoLevels()
        {
            var source = new Dictionary<string, LevelDef>(System.StringComparer.Ordinal);
            foreach (LevelDef level in ChapterTwoInsideTheBox()) source[level.name] = level;

            LevelDef moving = source["Moving Delivery"];
            LevelDef docked = source["Docked Passage"];
            LevelDef extraction = source["Side Extraction"];
            LevelDef turning = source["Turn It Inside"];
            LevelDef twoRooms = source["Two Rooms Down"];
            LevelDef relay = source["Three-Space Relay"];

            return new[]
            {
                RenameChapterTwoLevel(CloneLevelDefinition(moving), "Inside Delivery"),
                RenameChapterTwoLevel(CloneLevelDefinition(docked), "Docked Passage"),
                RenameChapterTwoLevel(MirrorLevelHorizontally(docked), "Reverse Dock"),
                RenameChapterTwoLevel(CloneLevelDefinition(extraction), "Side Extraction"),
                RenameChapterTwoLevel(MirrorLevelHorizontally(extraction), "Reverse Extraction"),
                RenameChapterTwoLevel(CloneLevelDefinition(turning), "Turn It Inside"),
                RenameChapterTwoLevel(MirrorLevelVertically(turning), "Inverted Turn"),
                RenameChapterTwoLevel(CloneLevelDefinition(twoRooms), "Two Rooms Down"),
                RenameChapterTwoLevel(MirrorLevelHorizontally(twoRooms), "Reverse Two Rooms"),
                RenameChapterTwoLevel(CloneLevelDefinition(relay), "Three-Space Relay"),
            };
        }

        static LevelDef RenameChapterTwoLevel(LevelDef level, string name)
        {
            level.name = name;
            return level;
        }

        static LevelDef[] RestoredChapterTwoLevels()
        {
            var candidates = new Dictionary<string, LevelDef>(System.StringComparer.Ordinal);
            foreach (LevelDef level in LegacyAuthoredLevels()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterTwoInsideTheBox()) candidates[level.name] = level;
            foreach (LevelDef level in ReviewedChapterTwoLevels()) candidates[level.name] = level;
            // The saved Chapter II curve intentionally drew its later room puzzles from the
            // shared recursive libraries. Mirror Levels() here so the targeted restore can find
            // every saved entry without rebuilding or changing Chapters I, III, IV or V.
            foreach (LevelDef level in ChapterThreeSynergy()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterFourRoomManeuvers()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterFiveRecursion()) candidates[level.name] = level;

            var restored = new LevelDef[10];
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            int previousPlanningDifficulty = -1;
            for (int chapterIndex = 0; chapterIndex < restored.Length; chapterIndex++)
            {
                int campaignIndex = 10 + chapterIndex;
                string name = FirstTwentyDifficultyOrder[campaignIndex];
                if (!candidates.TryGetValue(name, out LevelDef level))
                    throw new System.InvalidOperationException(
                        $"Saved Chapter II level '{name}' is missing from the authored library.");
                if (!names.Add(name) || level.rooms == null || level.rooms.Length < 2)
                    throw new System.InvalidOperationException(
                        $"Level {campaignIndex + 1} must be a unique room-inside-a-box puzzle.");
                if (string.IsNullOrEmpty(level.solution) || level.solution.Length != level.par)
                    throw new System.InvalidOperationException(
                        $"Level {campaignIndex + 1} needs an authored winning route whose length equals par.");
                if (level.par < MinimumAuthoredPars[campaignIndex])
                    throw new System.InvalidOperationException(
                        $"Level {campaignIndex + 1} route is below its saved authored limit.");
                if (level.rooms.Length != MinimumRoomCounts[campaignIndex])
                    throw new System.InvalidOperationException(
                        $"Level {campaignIndex + 1} needs exactly "
                        + $"{MinimumRoomCounts[campaignIndex]} connected room(s).");
                int planningDifficulty = ChapterTwoPlanningDifficulty(level, campaignIndex);
                if (planningDifficulty <= previousPlanningDifficulty)
                    throw new System.InvalidOperationException(
                        $"Level {campaignIndex + 1} planning difficulty {planningDifficulty} must "
                        + $"exceed the previous Chapter II level's {previousPlanningDifficulty}.");
                previousPlanningDifficulty = planningDifficulty;
                level.designComplexity = ReviewedCampaignDifficulty(level, campaignIndex);
                restored[chapterIndex] = level;
            }
            return restored;
        }

        static int ChapterTwoPlanningDifficulty(LevelDef level, int campaignIndex)
        {
            int authoredGoals = CountAuthoredCompletionGoals(level);
            return level.par * 40
                + DirectionChanges(level.solution) * 4
                + level.rooms.Length * 180
                + Mathf.Max(3, authoredGoals) * 70
                + LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(campaignIndex) * 45
                + LevelLayoutRebalancer.ChapterTwoGateReuseBudgetForLevel(campaignIndex) * 110;
        }

        // Targeted authoring command for the reviewed Level 12 correction. It deliberately avoids
        // Levels(), because the full campaign audit may contain unrelated work-in-progress levels.
        // The generated prefab keeps the authored recursive route but receives no automatic cargo,
        // switch/gate or arrow decoration; BoardRenderer supplies the clean doorway mask at runtime.
        public static void RegenerateCleanLevelTwelveSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Level 12 Generator",
                    "Stop Play Mode first. This command creates the prefab only in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Level 12 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            LevelDef[] chapter = ChapterTwoInsideTheBox();
            LevelDef level = System.Array.Find(chapter,
                candidate => candidate != null && candidate.name == "Return Path");
            if (level == null || string.IsNullOrEmpty(level.solution))
                throw new System.InvalidOperationException(
                    "The reviewed Level 12 'Return Path' definition or its authored route is missing.");

            GameObject prefab = BuildLevelPrefab(11, level, tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("Clean Level 12 Ready",
                "Level 12 was rebuilt with its recursive route only. The protruding doorway cell "
                + "and the unrelated generated mechanic are removed.", "OK");
        }

        // Focused Level 16 rebuild. This keeps the chapter's room-inside-a-box language but
        // replaces the long, obvious straight delivery with three linked tasks: dock the movable
        // room, turn the cargo inside it and deliver it outside, then return to the player exit.
        // The command creates only Level_16.prefab and never enters Play Mode.
        public static void RegenerateHarderLevelSixteenSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Level 16 Generator",
                    "Stop Play Mode first. This command creates the prefab only in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Level 16 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Level 16?",
                    "This replaces only Level_16.prefab with a harder three-task Chapter 2 puzzle. "
                    + "Levels 1-15 and 17-50 stay unchanged.",
                    "Rebuild Level 16",
                    "Cancel"))
            {
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            LevelDef[] chapter = ChapterTwoInsideTheBox();
            LevelDef level = System.Array.Find(chapter,
                candidate => candidate != null && candidate.name == "Moving Delivery");
            if (level == null || string.IsNullOrEmpty(level.solution))
                throw new System.InvalidOperationException(
                    "The harder Level 16 'Moving Delivery' definition or its authored route is missing.");

            GameObject prefab = BuildFocusedThreeTaskLevel(15, level, tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("Harder Level 16 Ready",
                "Level 16 now has three linked tasks: dock the room, deliver its cargo, and reach the exit. "
                + "Open Level_16.prefab and check it in the game.", "OK");
        }

        // Focused Level 17 rebuild. Cargo first travels out through the movable room's future
        // socket; only after that shared cell is clear can the player circle around and dock the
        // room. The separate player exit closes the three-stage dependency chain.
        public static void RegenerateHarderLevelSeventeenSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Level 17 Generator",
                    "Stop Play Mode first. This command creates the prefab only in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Level 17 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Level 17?",
                    "This replaces only Level_17.prefab with a harder three-task Chapter 2 puzzle. "
                    + "All other levels stay unchanged.",
                    "Rebuild Level 17",
                    "Cancel"))
            {
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            LevelDef[] chapter = ChapterTwoInsideTheBox();
            LevelDef level = System.Array.Find(chapter,
                candidate => candidate != null && candidate.name == "Side Extraction");
            if (level == null || string.IsNullOrEmpty(level.solution))
                throw new System.InvalidOperationException(
                    "The harder Level 17 'Side Extraction' definition or its authored route is missing.");

            GameObject prefab = BuildFocusedThreeTaskLevel(16, level, tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("Harder Level 17 Ready",
                "Level 17 now requires cargo extraction before the room can be docked, followed by the exit. "
                + "Open Level_17.prefab and check it in the game.", "OK");
        }

        // One focused command for the balanced Level 16-20 progression. Every definition contains
        // exactly three visible completion targets. The stored route is cleared only after
        // authoring so the runtime rebalancer cannot inject its old six/seven-task contract.
        public static void RegenerateBalancedLevelsSixteenToTwentySilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Levels 16-20 Generator",
                    "Stop Play Mode first. This command creates prefabs only in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Levels 16-20 Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Balanced Levels 16-20?",
                    "This replaces only Level_16.prefab through Level_20.prefab. Each level has "
                    + "exactly three authored tasks, a distinct layout, and a progressive difficulty step.",
                    "Rebuild Levels 16-20",
                    "Cancel"))
            {
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");

            LevelDef[] chapter = ChapterTwoInsideTheBox();
            string[] names =
            {
                "Moving Delivery", "Side Extraction", "Turn It Inside",
                "Two Rooms Down", "Three-Space Relay"
            };
            GameObject lastPrefab = null;
            for (int offset = 0; offset < names.Length; offset++)
            {
                LevelDef level = System.Array.Find(chapter,
                    candidate => candidate != null && candidate.name == names[offset]);
                if (level == null || string.IsNullOrEmpty(level.solution))
                    throw new System.InvalidOperationException(
                        $"The balanced Level {16 + offset} '{names[offset]}' definition or route is missing.");

                lastPrefab = BuildFocusedThreeTaskLevel(15 + offset, level, tiles);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = lastPrefab;
            EditorGUIUtility.PingObject(lastPrefab);
            EditorUtility.DisplayDialog("Balanced Levels 16-20 Ready",
                "Levels 16-20 were rebuilt as five distinct three-task puzzles. Chapter 1 arrows "
                + "and button/gate dependencies are authored directly, with no automatic injection. "
                + "Open the prefabs and check them in the game.", "OK");
        }

        static GameObject BuildFocusedThreeTaskLevel(int levelIndex, LevelDef level, Tiles tiles)
        {
            int levelNumber = levelIndex + 1;
            ValidateFocusedThreeTaskDefinition(level, levelNumber);
            RemoveLeakedLevelAuthoringRoots(levelNumber);

            string path = LevelDir + "/Level_" + levelNumber + ".prefab";
            try
            {
                // Passing an explicit path skips the old whole-campaign dependency quota. That
                // quota is the source of the screenshot's "retained 2/6" exception and is not
                // applicable to these intentionally compact three-task boards.
                BuildLevelPrefab(levelIndex, level, tiles, path);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                    throw new System.InvalidOperationException("Could not reopen " + path + ".");
                try
                {
                    ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                    if (info == null)
                        throw new System.InvalidOperationException(path + " is missing ParaboxLevel metadata.");

                    // Empty at runtime means no generated cargo, gate or arrow objectives. The
                    // authored route remains in this editor script and in the read-only verifier.
                    info.solution = string.Empty;
                    EditorUtility.SetDirty(info);
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new System.InvalidOperationException("Unity could not save " + path + ".");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            finally
            {
                // A failed pre-fix build left raw Level_17 roots in the open scene. Remove only
                // non-prefab root objects with this generated name; placed prefab instances and
                // persistent assets are deliberately untouched.
                RemoveLeakedLevelAuthoringRoots(levelNumber);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void ValidateFocusedThreeTaskDefinition(LevelDef level, int levelNumber)
        {
            if (level == null || level.rooms == null || level.rooms.Length < 2)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} needs an outer room and at least one inner room.");
            if (string.IsNullOrEmpty(level.solution) || level.solution.Length != level.par)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber}'s authored route must be present and match par.");

            int players = 0;
            int targets = 0;
            foreach (string[] room in level.rooms)
            {
                if (room == null || room.Length == 0)
                    throw new System.InvalidOperationException($"Level {levelNumber} contains an empty room.");
                int width = room[0].Length;
                foreach (string row in room)
                {
                    if (row == null || row.Length != width)
                        throw new System.InvalidOperationException(
                            $"Level {levelNumber} contains uneven room rows.");
                    foreach (char cell in row)
                    {
                        if (cell == 'P') players++;
                        if (cell == 'p' || cell == 'x' || cell == '&'
                            || ColourGoals.ContainsKey(cell)) targets++;
                    }
                }
            }

            if (players != 1 || targets != 3)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} must contain one player and exactly three tasks; found "
                    + $"{players} player(s) and {targets} target(s).");

            bool hasAuthoredGate = false;
            bool hasAuthoredButton = false;
            foreach (string[] room in level.rooms)
                foreach (string row in room)
                    foreach (char cell in row)
                    {
                        hasAuthoredGate |= cell == 'G';
                        hasAuthoredButton |= cell == 'B' || cell == '&';
                    }
            if (hasAuthoredGate != hasAuthoredButton)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} must author its Chapter 1 button and gate as one pair.");
            if (hasAuthoredGate && !PlayerGoalRequiresGreenGate(level))
                throw new System.InvalidOperationException(
                    $"Level {levelNumber}'s green gate is optional. Seal the player-goal pocket "
                    + "so the cargo-button dependency is mandatory.");
        }

        static void RemoveLeakedLevelAuthoringRoots(int levelNumber)
        {
            string expectedName = "Level_" + levelNumber;
            foreach (ParaboxLevel levelInfo in Resources.FindObjectsOfTypeAll<ParaboxLevel>())
            {
                if (levelInfo == null) continue;
                GameObject candidate = levelInfo.gameObject;
                if (candidate == null || candidate.name != expectedName
                    || candidate.transform.parent != null
                    || EditorUtility.IsPersistent(candidate)
                    || !candidate.scene.IsValid() || !candidate.scene.isLoaded
                    || PrefabUtility.GetPrefabInstanceStatus(candidate) != PrefabInstanceStatus.NotAPrefab)
                    continue;

                EditorSceneManager.MarkSceneDirty(candidate.scene);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        // Chapter III keeps its authored route in the runtime prefab after the Edit Mode replay.
        // This allows release validation to prove every shipped Level 21-30 board remains solvable;
        // LevelLayoutRebalancer does not mutate Chapter III layouts.
        static GameObject BuildFocusedChapterThreeLevel(int levelIndex, LevelDef level, Tiles tiles)
        {
            int levelNumber = levelIndex + 1;
            if (levelNumber < 21 || levelNumber > 30)
                throw new System.ArgumentOutOfRangeException(nameof(levelIndex),
                    "The Chapter III builder may write only Levels 21-30.");

            RemoveLeakedLevelAuthoringRoots(levelNumber);
            string path = LevelDir + "/Level_" + levelNumber + ".prefab";
            try
            {
                // The ordinary builder replays the authored route through the same model used by
                // gameplay and rejects blocked routes, unused rooms, decorative cargo or an
                // unfinished target before the prefab is saved. This is Edit Mode authoring, not
                // Play Mode or an automated play session.
                // Save to the exact focused path first, then run the Chapter III contract below.
                // Supplying the path avoids the generic builder throwing before the focused
                // validator can report which visible task remains incomplete.
                BuildLevelPrefab(levelIndex, level, tiles, path);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                    throw new System.InvalidOperationException("Could not reopen " + path + ".");
                try
                {
                    ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                    if (info == null)
                        throw new System.InvalidOperationException(path + " is missing ParaboxLevel metadata.");

                    ValidatePremiumChapterThreeRuntimeContract(root, level, levelNumber);
                    ValidateCampaignMechanicTasks(root, level, levelNumber);

                    info.designComplexity = Mathf.Max(
                        ReviewedCampaignDifficulty(level, levelIndex),
                        ChapterThreeDifficulty(level));
                    EditorUtility.SetDirty(info);
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new System.InvalidOperationException("Unity could not save " + path + ".");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            finally
            {
                RemoveLeakedLevelAuthoringRoots(levelNumber);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void ValidatePremiumChapterThreeRuntimeContract(
            GameObject root, LevelDef level, int levelNumber)
        {
            bool expectsPortal = levelNumber >= 25;
            int portalCount = root.GetComponentsInChildren<PortalMarker>(true).Length;
            if (portalCount != (expectsPortal ? 2 : 0))
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} has {portalCount} premium portal markers; expected "
                    + (expectsPortal ? "one visible pair." : "none before Level 25."));

            LevelModel model = LevelParser.Parse(root);
            if (model == null || model.player == null)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} could not build its Chapter III model.");
            int visibleTasks = 0;
            foreach (PRoom room in model.rooms.Values)
                visibleTasks += room.boxGoals.Count + room.colourGoals.Count
                                + room.playerGoals.Count;
            if (visibleTasks < 3)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} exposes only {visibleTasks} visible completion tasks.");

            foreach (char command in level.solution)
            {
                if (!TryFoundationDirection(command, out Vector2Int direction))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} route contains invalid command '{command}'.");
                if (!model.TryMovePlayer(direction))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} route blocks on '{command}'.");
            }

            if (!model.IsWon())
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} stored route does not solve every task: "
                    + DescribeIncompleteTasks(model));
            // Recursive room movement can resolve a portal transition without the portal being
            // the first adjacent cell of a command. Prove that it is mandatory from the board
            // topology instead: the paired exit exists, the finish pocket is sealed without it,
            // and the complete authored route has already won above.
            if (expectsPortal
                && (model.portalPair.Count != 2 || !PlayerGoalRequiresPortal(level)))
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} portal is decorative; its winning route must use it.");
        }

        static string DescribeIncompleteTasks(LevelModel model)
        {
            var missing = new List<string>();
            foreach (PRoom room in model.rooms.Values)
            {
                foreach (Vector2Int goal in room.boxGoals)
                {
                    PEntity entity = model.EntityAt(room.id, goal);
                    if (entity == null || !entity.IsCrate)
                        missing.Add($"Room {room.id} box goal {goal}");
                }
                foreach (Vector2Int goal in room.playerGoals)
                {
                    PEntity entity = model.EntityAt(room.id, goal);
                    if (entity == null || !entity.isPlayer)
                        missing.Add($"Room {room.id} player goal {goal}");
                }
                foreach (var goal in room.colourGoals)
                {
                    PEntity entity = model.EntityAt(room.id, goal.cell);
                    if (entity == null || entity.colour != goal.colour)
                        missing.Add($"Room {room.id} colour {goal.colour} goal {goal.cell}");
                }
            }
            return missing.Count == 0 ? "unknown completion mismatch" : string.Join(", ", missing);
        }

        // Source-only release gate for the ten Chapter III definitions. It does not solve or play
        // a board. It protects the design contract before Unity creates anything: three or four
        // visible objectives, one real cargo delivery, one socket for every movable room, every
        // inner room referenced once, compact camera-safe dimensions, distinct silhouettes and a
        // difficulty score that rises at each level.
        static void ValidateChapterThreeRebuildDefinitions(LevelDef[] levels)
        {
            if (levels == null || levels.Length != 10)
                throw new System.InvalidOperationException(
                    $"Chapter III must contain exactly ten levels; found {levels?.Length ?? 0}.");

            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var layouts = new HashSet<string>(System.StringComparer.Ordinal);
            int previousPar = 0;
            int previousDifficulty = -1;

            // The opening recursive-cargo board must start above the strongest Chapter II source
            // evidence. This compares authored mechanics, not level numbers or display ratings.
            int chapterTwoCeiling = -1;
            foreach (LevelDef chapterTwoLevel in ChapterTwoInsideTheBox())
                chapterTwoCeiling = Mathf.Max(chapterTwoCeiling,
                    EasyInsideTheBoxDifficulty(chapterTwoLevel));

            for (int chapterIndex = 0; chapterIndex < levels.Length; chapterIndex++)
            {
                LevelDef level = levels[chapterIndex];
                int levelNumber = 21 + chapterIndex;
                if (level == null || string.IsNullOrWhiteSpace(level.name))
                    throw new System.InvalidOperationException($"Level {levelNumber} has no name.");
                if (!names.Add(level.name))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} repeats the Chapter III name '{level.name}'.");
                if (level.rooms == null || level.rooms.Length < 2 || level.rooms.Length > 4)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs two to four readable coordinate spaces.");
                if (string.IsNullOrEmpty(level.solution) || level.solution.Length != level.par)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber}'s authored route must be present and equal its par.");
                if (level.par <= previousPar)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} has par {level.par}; Chapter III must rise above "
                        + $"the previous level's {previousPar}.");

                int players = 0;
                int playerGoals = 0;
                int cargo = 0;
                int cargoGoals = 0;
                int roomSockets = 0;
                int movableRooms = 0;
                int portals = 0;
                int visibleTasks = 0;
                int outerVisibleTasks = 0;
                var referencedRooms = new HashSet<int>();
                var fingerprint = new System.Text.StringBuilder();

                for (int roomIndex = 0; roomIndex < level.rooms.Length; roomIndex++)
                {
                    string[] room = level.rooms[roomIndex];
                    if (room == null || room.Length < 3 || room.Length > 14
                        || string.IsNullOrEmpty(room[0]))
                        throw new System.InvalidOperationException(
                            $"Level {levelNumber}, Room {roomIndex} is outside the visible 3-14 row range.");
                    int width = room[0].Length;
                    int outerWidthLimit = chapterIndex >= 4 ? 17 : 13;
                    if (width < 3 || width > 17
                        || (roomIndex == 0 && width > outerWidthLimit))
                        throw new System.InvalidOperationException(
                            $"Level {levelNumber}, Room {roomIndex} is {width} cells wide; "
                            + $"the Chapter III camera-safe limits are {outerWidthLimit} outer / 17 inner.");

                    fingerprint.Append('[').Append(width).Append('x').Append(room.Length).Append(':');
                    for (int rowIndex = 0; rowIndex < room.Length; rowIndex++)
                    {
                        string row = room[rowIndex];
                        if (row == null || row.Length != width)
                            throw new System.InvalidOperationException(
                                $"Level {levelNumber}, Room {roomIndex}, row {rowIndex} is not rectangular.");
                        fingerprint.Append(row).Append('/');

                        foreach (char cell in row)
                        {
                            bool allowed = cell == '#' || cell == '.'
                                || cell == 'P' || cell == 'p'
                                || cell == 'J' || cell == 'j' || cell == 'x'
                                || (chapterIndex >= 4 && cell == 'o')
                                || (cell >= '1' && cell <= '9')
                                || AnchoredBoxes.ContainsKey(cell);
                            if (!allowed)
                                throw new System.InvalidOperationException(
                                    $"Level {levelNumber} uses '{cell}'. Chapter III is limited to "
                                    + "players, coral cargo, goals and recursive rooms.");

                            if (cell == 'P') players++;
                            else if (cell == 'p')
                            {
                                playerGoals++;
                                visibleTasks++;
                                if (roomIndex == 0) outerVisibleTasks++;
                            }
                            else if (cell == 'J') cargo++;
                            else if (cell == 'j')
                            {
                                cargoGoals++;
                                visibleTasks++;
                                if (roomIndex == 0) outerVisibleTasks++;
                            }
                            else if (cell == 'x')
                            {
                                roomSockets++;
                                visibleTasks++;
                                if (roomIndex == 0) outerVisibleTasks++;
                            }
                            else if (cell == 'o') portals++;

                            if (cell >= '1' && cell <= '9')
                            {
                                movableRooms++;
                                referencedRooms.Add(cell - '0');
                            }
                            else if (AnchoredBoxes.TryGetValue(cell, out int anchoredRoom))
                            {
                                referencedRooms.Add(anchoredRoom);
                            }
                        }
                    }
                    fingerprint.Append(']');
                }

                if (!layouts.Add(fingerprint.ToString()))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} repeats another Chapter III layout.");
                if (players != 1 || playerGoals != 1)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs exactly one player and one player goal.");
                if (cargo != 1 || cargoGoals != 1)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs one coral cargo object and its matching goal.");
                if (chapterIndex < 4 && portals != 0)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} introduces the premium portal before its Level 25 tutorial.");
                if (chapterIndex >= 4
                    && (portals != 2 || !PlayerGoalRequiresPortal(level)))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} must use one visible portal pair to reach a sealed target.");
                if (visibleTasks < 3 || visibleTasks > 4 || outerVisibleTasks != visibleTasks)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs three or four clearly visible outer-board tasks; "
                        + $"found {visibleTasks} total and {outerVisibleTasks} on the outer board.");
                if (roomSockets != movableRooms)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} has {movableRooms} movable room(s) but {roomSockets} socket(s).");
                if (referencedRooms.Count != level.rooms.Length - 1)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} references {referencedRooms.Count}/{level.rooms.Length - 1} inner rooms.");
                for (int roomId = 1; roomId < level.rooms.Length; roomId++)
                    if (!referencedRooms.Contains(roomId))
                        throw new System.InvalidOperationException(
                            $"Level {levelNumber} never exposes Room {roomId}.");

                int difficulty = ChapterThreeDifficulty(level);
                if (chapterIndex == 0 && difficulty <= chapterTwoCeiling)
                    throw new System.InvalidOperationException(
                        $"Level 21 evidence {difficulty} must exceed Chapter II's ceiling {chapterTwoCeiling}.");
                if (difficulty <= previousDifficulty)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} difficulty {difficulty} must exceed the previous "
                        + $"Chapter III level's {previousDifficulty}.");

                previousPar = level.par;
                previousDifficulty = difficulty;
            }
        }

        static GameObject BuildFocusedChapterFourLevel(int levelIndex, LevelDef level, Tiles tiles)
        {
            int levelNumber = levelIndex + 1;
            if (levelNumber < 31 || levelNumber > 40)
                throw new System.ArgumentOutOfRangeException(nameof(levelIndex),
                    "The premium Chapter IV builder may write only Levels 31-40.");

            RemoveLeakedLevelAuthoringRoots(levelNumber);
            string path = LevelDir + "/Level_" + levelNumber + ".prefab";
            try
            {
                // Passing the exact Level_XX path keeps this focused Chapter IV command isolated
                // from the old whole-campaign quotas. The custom replay below checks this richer
                // four-task/portal contract before the final clean prefab is saved.
                BuildLevelPrefab(levelIndex, level, tiles, path);

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                    throw new System.InvalidOperationException("Could not reopen " + path + ".");
                try
                {
                    ValidateChapterFourPortalRoute(root, level, levelNumber);
                    ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                    if (info == null)
                        throw new System.InvalidOperationException(path + " is missing ParaboxLevel metadata.");

                    // The definition validator and the route-evidence replay above prove the real
                    // difficulty curve. Serialize it in the shared 100-point campaign band so the
                    // Chapter III -> IV hand-off and every later level remain strictly ordered.
                    info.designComplexity = ReviewedCampaignDifficulty(level, levelIndex);
                    // Keep the authored route in metadata: Chapter IV's runtime rebalancer uses
                    // it to install only dependencies that this exact solution still completes.
                    // The route is never exposed to the player or tutorial UI.
                    info.solution = level.solution;
                    EditorUtility.SetDirty(info);
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                        throw new System.InvalidOperationException("Unity could not save " + path + ".");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            finally
            {
                RemoveLeakedLevelAuthoringRoots(levelNumber);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static void ValidateChapterFourPortalRoute(GameObject root, LevelDef level, int levelNumber)
        {
            int portalCount = root.GetComponentsInChildren<PortalMarker>(true).Length;
            bool expectsPortal = levelNumber >= 35;
            if (portalCount != (expectsPortal ? 2 : 0))
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} has {portalCount} portal markers; expected "
                    + (expectsPortal ? "one paired portal." : "none before Level 35."));

            LevelModel model = LevelParser.Parse(root);
            if (model == null || model.player == null)
                throw new System.InvalidOperationException($"Level {levelNumber} could not build its model.");

            int activeTasks = 0;
            foreach (PRoom room in model.rooms.Values)
                activeTasks += room.boxGoals.Count + room.colourGoals.Count
                               + room.playerGoals.Count;
            // The mandatory portal traversal is a distinct completion task on Levels 35-40.
            // Count it without manufacturing a generic grey cargo/goal pair.
            if (expectsPortal) activeTasks++;
            if (activeTasks < 4)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} has only {activeTasks} purposeful tasks; expected at least four.");
            if (!expectsPortal && model.RebalanceElementCount < 1)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} did not retain any route-proven difficulty dependency.");

            int expectedOneWays = LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(levelNumber - 1);
            bool expectsOneWays = !expectsPortal;
            if (model.rebalanceOneWays != expectedOneWays
                || expectsOneWays != model.curriculumReuses.Contains(MechanicCatalog.Id.OneWay)
                || (expectsOneWays && expectedOneWays < 1))
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} retained {model.rebalanceOneWays}/{expectedOneWays} "
                    + "route-proven one-way commitments. Levels 31-34 combine room play with "
                    + "arrows; Levels 35-40 combine room play with the mandatory portal.");

            bool usedPortal = false;
            var initialCargo = new List<PEntity>();
            var roomBoxes = new List<PEntity>();
            foreach (PEntity entity in model.entities)
            {
                if (entity == null || !entity.IsCrate) continue;
                if (entity.interiorRoomId >= 0) roomBoxes.Add(entity);
                else initialCargo.Add(entity);
            }
            var movedCargo = new HashSet<PEntity>();
            var movedRooms = new HashSet<PEntity>();
            var visitedRooms = new HashSet<int> { model.player.roomId };
            var crossedOneWays = new HashSet<(int room, Vector2Int cell)>();
            for (int routeStep = 0; routeStep < level.solution.Length; routeStep++)
            {
                char command = level.solution[routeStep];
                if (!TryFoundationDirection(command, out Vector2Int direction))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} route contains invalid command '{command}'.");

                PRoom playerRoom = model.rooms[model.player.roomId];
                Vector2Int target = model.player.pos + direction;
                if (playerRoom.InBounds(target)
                    && model.portalPair.ContainsKey((playerRoom.id, target)))
                    usedPortal = true;

                var before = new Dictionary<PEntity, (int room, Vector2Int cell)>();
                foreach (PEntity entity in model.entities)
                    before[entity] = (entity.roomId, entity.pos);
                if (!model.TryMovePlayer(direction))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} portal route blocks at move {routeStep + 1}/"
                        + $"{level.solution.Length} on '{command}'.");
                visitedRooms.Add(model.player.roomId);
                foreach (PEntity entity in model.entities)
                {
                    var old = before[entity];
                    if (old.room == entity.roomId && old.cell == entity.pos) continue;
                    PRoom destinationRoom = model.rooms[entity.roomId];
                    if (destinationRoom.oneway != null
                        && destinationRoom.InBounds(entity.pos)
                        && destinationRoom.oneway[entity.pos.x, entity.pos.y] != Vector2Int.zero)
                        crossedOneWays.Add((entity.roomId, entity.pos));
                    if (entity.interiorRoomId >= 0) movedRooms.Add(entity);
                    else if (entity.IsCrate) movedCargo.Add(entity);
                }
            }

            if (!model.IsWon())
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} route does not finish all Chapter IV tasks: "
                    + DescribeIncompleteTasks(model));
            if (expectsPortal && !usedPortal)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} contains a decorative portal. Its winning route must use it.");
            if (movedCargo.Count != initialCargo.Count)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} moves {movedCargo.Count}/{initialCargo.Count} cargo pieces. "
                    + "Every visible cargo target must require play.");
            if (visitedRooms.Count != model.rooms.Count)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber} visits {visitedRooms.Count}/{model.rooms.Count} rooms.");
            if (crossedOneWays.Count != expectedOneWays)
                throw new System.InvalidOperationException(
                    $"Level {levelNumber}'s winning route uses {crossedOneWays.Count}/"
                    + $"{expectedOneWays} one-way commitments; none may be decorative.");
            foreach (PEntity roomBox in roomBoxes)
                if (!movedRooms.Contains(roomBox) && !visitedRooms.Contains(roomBox.interiorRoomId))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} contains an unused Room {roomBox.interiorRoomId}.");
        }

        static void ValidatePremiumChapterFourDefinitions(LevelDef[] levels)
        {
            if (levels == null || levels.Length != 10)
                throw new System.InvalidOperationException(
                    $"Premium Chapter IV must contain ten levels; found {levels?.Length ?? 0}.");

            int chapterThreeCeiling = -1;
            foreach (LevelDef chapterThreeLevel in ChapterThreeSynergy())
                chapterThreeCeiling = Mathf.Max(chapterThreeCeiling,
                    ChapterThreeDifficulty(chapterThreeLevel));

            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var layouts = new HashSet<string>(System.StringComparer.Ordinal);
            int previousPar = 0;
            int previousDifficulty = -1;
            for (int chapterIndex = 0; chapterIndex < levels.Length; chapterIndex++)
            {
                LevelDef level = levels[chapterIndex];
                int levelNumber = 31 + chapterIndex;
                if (level == null || string.IsNullOrWhiteSpace(level.name)
                    || !names.Add(level.name))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs a unique premium Chapter IV identity.");
                if (level.rooms == null || level.rooms.Length < 2 || level.rooms.Length > 5)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs two to five visible recursive spaces.");
                if (string.IsNullOrWhiteSpace(level.solution) || level.solution.Length != level.par)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber}'s authored route must be present and equal its par.");
                if (level.par <= previousPar)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} par {level.par} must exceed Level {levelNumber - 1}'s {previousPar}.");

                int players = 0;
                int playerGoals = 0;
                int tasks = 0;
                int portals = 0;
                var fingerprint = new System.Text.StringBuilder();
                for (int roomIndex = 0; roomIndex < level.rooms.Length; roomIndex++)
                {
                    string[] room = level.rooms[roomIndex];
                    if (room == null || room.Length < 3 || room.Length > 14
                        || string.IsNullOrEmpty(room[0]))
                        throw new System.InvalidOperationException(
                            $"Level {levelNumber}, Room {roomIndex} is outside the visible height limit.");
                    int width = room[0].Length;
                    if (width < 3 || width > 18)
                        throw new System.InvalidOperationException(
                            $"Level {levelNumber}, Room {roomIndex} width {width} is not camera-safe.");

                    fingerprint.Append('[').Append(width).Append('x').Append(room.Length).Append(':');
                    foreach (string row in room)
                    {
                        if (row == null || row.Length != width)
                            throw new System.InvalidOperationException(
                                $"Level {levelNumber}, Room {roomIndex} has uneven rows.");
                        fingerprint.Append(row).Append('/');
                        foreach (char cell in row)
                        {
                            bool allowed = cell == '#' || cell == '.'
                                || cell == 'P' || cell == 'p'
                                || cell == 'b' || cell == 'x'
                                || cell == 'J' || cell == 'j'
                                || cell == 'N' || cell == 'n'
                                || cell == 'Z' || cell == 'z'
                                || cell == 'A' || cell == 'C' || cell == 'o'
                                || (cell >= '1' && cell <= '9')
                                || AnchoredBoxes.ContainsKey(cell);
                            if (!allowed)
                                throw new System.InvalidOperationException(
                                    $"Level {levelNumber} uses unrelated symbol '{cell}'.");
                            if (cell == 'P') players++;
                            if (cell == 'p') playerGoals++;
                            if (cell == 'o') portals++;
                            if (cell == 'p' || cell == 'x'
                                || ColourGoals.ContainsKey(cell)
                                || ColourCargoOnGoals.ContainsKey(cell)) tasks++;
                        }
                    }
                    fingerprint.Append(']');
                }

                if (!layouts.Add(fingerprint.ToString()))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} repeats another Chapter IV silhouette.");
                if (players != 1 || playerGoals != 1)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs exactly one player and one player target.");
                if (tasks < 3)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} has {tasks} authored tasks before route-proven hardening; expected at least three.");
                if (chapterIndex < 4 && portals != 0)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} introduces portals before the Level 35 tutorial.");
                if (chapterIndex >= 4 && portals != 2)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} needs exactly one paired premium portal.");
                if (chapterIndex >= 4 && !PlayerGoalRequiresPortal(level))
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber}'s player goal is reachable without its portal.");

                int difficulty = ChapterFourPremiumDifficulty(level);
                if (chapterIndex == 0 && difficulty <= chapterThreeCeiling)
                    throw new System.InvalidOperationException(
                        $"Level 31 difficulty {difficulty} must exceed Chapter III's ceiling {chapterThreeCeiling}.");
                if (difficulty <= previousDifficulty)
                    throw new System.InvalidOperationException(
                        $"Level {levelNumber} difficulty {difficulty} must exceed {previousDifficulty}.");
                previousPar = level.par;
                previousDifficulty = difficulty;
            }
        }

        // Treat both portal cells as walls and flood from the player goal. The generated pocket
        // must not reach the player, a room boundary or any non-pocket route; therefore the portal
        // pair is the only possible entrance.
        static bool PlayerGoalRequiresPortal(LevelDef level)
        {
            int goalRoom = -1;
            Vector2Int goal = default;
            for (int room = 0; room < level.rooms.Length; room++)
                for (int row = 0; row < level.rooms[room].Length; row++)
                {
                    int column = level.rooms[room][row].IndexOf('p');
                    if (column < 0) continue;
                    goalRoom = room;
                    goal = new Vector2Int(column, row);
                }
            if (goalRoom < 0) return false;

            string[] board = level.rooms[goalRoom];
            var frontier = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            frontier.Enqueue(goal);
            visited.Add(goal);
            Vector2Int[] directions =
                { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (frontier.Count > 0)
            {
                Vector2Int cell = frontier.Dequeue();
                char value = board[cell.y][cell.x];
                if (value == 'P') return false;
                if (cell.x == 0 || cell.x == board[0].Length - 1
                    || cell.y == 0 || cell.y == board.Length - 1)
                    return false;

                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = cell + direction;
                    if (next.x < 0 || next.x >= board[0].Length
                        || next.y < 0 || next.y >= board.Length
                        || visited.Contains(next)) continue;
                    char nextValue = board[next.y][next.x];
                    if (nextValue == '#' || nextValue == 'o') continue;
                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }
            return true;
        }

        // One Edit Mode command for the requested chapter swap. It writes only Levels 11-20,
        // Levels 41-50 and their prebuilt chapter-opener tutorials. Campaign routes are
        // replay-validated and tutorial routes are solver-proved; Play Mode is never entered.
        [MenuItem("Tools/Parabox/Regenerate Swapped Chapters 2 and 5")]
        public static void RegenerateSwappedChaptersTwoAndFiveSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter Swap Generator",
                    "Exit Play Mode first. This command creates prefabs only in Edit Mode.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter Swap Generator",
                    "Unity is still compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            LevelDef[] defs = Levels();
            for (int i = 10; i < 20; i++)
                BuildLevelPrefab(i, defs[i], tiles);
            for (int i = 40; i < 50; i++)
                BuildLevelPrefab(i, defs[i], tiles);

            RegenerateTutorialMiniLevelAuthoringOnly(1, tiles);
            RegenerateTutorialMiniLevelAuthoringOnly(4, tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Parabox: swapped Chapters II/V and rebuilt 20 levels plus both tutorials without Play Mode.");
        }

        [MenuItem("Tools/Parabox/Regenerate Real Tutorial Mini-Puzzles")]
        public static void RegenerateTutorialMiniLevels()
        {
            RegenerateTutorialMiniLevelsCore();
        }

        // Used by the one-click content generator. This remains an Edit Mode authoring command:
        // it never enters Play Mode, but it does solve and replay each tiny tutorial model before
        // saving it. A stale route must never produce a video that stops without demonstrating a win.
        public static void RegenerateTutorialMiniLevelsAuthoringOnly()
        {
            RegenerateTutorialMiniLevelsCore();
        }

        // Focused chapter rebuilds call the same complete tutorial authoring pass. This keeps every
        // possible first-appearance example solver-proven and prevents stale routes.
        static void RegenerateTutorialMiniLevelAuthoringOnly(int tutorialIndex, Tiles tiles)
        {
            TutorialDef[] tutorials = TutorialMiniPuzzles();
            if (tutorialIndex < 0 || tutorialIndex >= tutorials.Length)
                throw new System.ArgumentOutOfRangeException(nameof(tutorialIndex));
            RegenerateTutorialMiniLevelsCore();
        }

        // Chapter-III-only tutorial pass. The chapter opener teaches recursive cargo before
        // Level 21; the independent portal mini-puzzle then introduces the premium rule before
        // Level 25. No other tutorial or campaign prefab is rebuilt here.
        static void RegeneratePremiumChapterThreeTutorialsAuthoringOnly(Tiles tiles)
        {
            EnsureFolder(TutorialDir);
            var wanted = new HashSet<string>(System.StringComparer.Ordinal)
                { "Chapter_3", "Mechanic_Portal" };
            int generated = 0;
            foreach (TutorialDef tutorial in CampaignTutorialMiniPuzzles())
            {
                if (!wanted.Contains(tutorial.assetName)) continue;
                string path = TutorialDir + "/" + tutorial.assetName + ".prefab";
                tutorial.level.solution = string.Empty;
                tutorial.level.par = 0;
                BuildLevelPrefab(0, tutorial.level, tiles, path);

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    contents.name = "Tutorial_" + tutorial.assetName;
                    ParaboxLevel info = contents.GetComponent<ParaboxLevel>();
                    if (info == null)
                        throw new System.InvalidOperationException(
                            path + " is missing tutorial metadata.");
                    info.levelName = TutorialDisplayName(tutorial);
                    info.solution = string.Empty;
                    info.par = 0;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                string proof = ParaboxCampaignSolver.SolveAndStore(path, 1, tutorial.maxDepth);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!MechanicCatalog.MechanicsIn(prefab, -1).Contains(tutorial.mechanic))
                    throw new System.InvalidOperationException(
                        $"{tutorial.assetName} does not contain {tutorial.mechanic}.");
                if (tutorial.mechanic == MechanicCatalog.Id.Portal
                    && !TutorialProofUsesPortal(prefab, proof))
                    throw new System.InvalidOperationException(
                        "Mechanic_Portal wins without entering its cyan portal pair.");
                generated++;
            }

            if (generated != wanted.Count)
                throw new System.InvalidOperationException(
                    $"Generated {generated}/{wanted.Count} premium Chapter III tutorials.");
            AssetDatabase.SaveAssets();
            Debug.Log("Parabox: generated only Chapter_3 and its pre-Level-25 premium portal tutorial.");
        }

        // Narrow Chapter IV tutorial pass. It writes only the Chapter_4 room-docking
        // example and the new Mechanic_Portal example, leaving every Chapter I-III tutorial asset
        // untouched. Both are solved and stored in Edit Mode for the cinematic player.
        static void RegeneratePremiumChapterFourTutorialsAuthoringOnly(Tiles tiles)
        {
            EnsureFolder(TutorialDir);
            var wanted = new HashSet<string>(System.StringComparer.Ordinal)
                { "Chapter_4", "Mechanic_Portal" };
            int generated = 0;
            foreach (TutorialDef tutorial in CampaignTutorialMiniPuzzles())
            {
                if (!wanted.Contains(tutorial.assetName)) continue;
                string path = TutorialDir + "/" + tutorial.assetName + ".prefab";
                tutorial.level.solution = string.Empty;
                tutorial.level.par = 0;
                BuildLevelPrefab(0, tutorial.level, tiles, path);

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    contents.name = "Tutorial_" + tutorial.assetName;
                    ParaboxLevel info = contents.GetComponent<ParaboxLevel>();
                    if (info == null)
                        throw new System.InvalidOperationException(path + " is missing tutorial metadata.");
                    info.levelName = TutorialDisplayName(tutorial);
                    info.solution = string.Empty;
                    info.par = 0;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                string proof = ParaboxCampaignSolver.SolveAndStore(
                    path, 1, tutorial.maxDepth);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!MechanicCatalog.MechanicsIn(prefab, -1).Contains(tutorial.mechanic))
                    throw new System.InvalidOperationException(
                        $"{tutorial.assetName} does not contain {tutorial.mechanic}.");
                if (tutorial.mechanic == MechanicCatalog.Id.Portal
                    && !TutorialProofUsesPortal(prefab, proof))
                    throw new System.InvalidOperationException(
                        "Mechanic_Portal wins without entering its cyan portal pair.");
                generated++;
            }

            if (generated != wanted.Count)
                throw new System.InvalidOperationException(
                    $"Generated {generated}/{wanted.Count} premium Chapter IV tutorials.");
            AssetDatabase.SaveAssets();
            Debug.Log("Parabox: generated only Chapter_4 and Mechanic_Portal tutorials in Edit Mode.");
        }

        [MenuItem("Tools/Parabox/Tutorials/Rebuild Chapter 5 Tutorial", priority = 1351)]
        public static void RegenerateExtremeChapterFiveTutorial()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Chapter 5 Tutorial",
                    "Exit Play Mode first. This command creates only the tutorial prefab.", "OK");
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Chapter 5 Tutorial",
                    "Unity is still compiling or importing. Wait, then run this command again.", "OK");
                return;
            }

            Tiles tiles = LoadLevelTiles();
            if (tiles == null)
                throw new System.InvalidOperationException("Parabox level tiles are missing.");
            RegenerateExtremeChapterFiveTutorialAuthoringOnly(tiles);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Chapter 5 Tutorial",
                "Rebuilt only the tutorial shown before Level 41. Levels 1-50 were not changed.", "OK");
        }

        // Focused Chapter V tutorial pass. Unlike the legacy helper, this does not rebuild or
        // reserialize tutorials belonging to Chapters I-IV.
        static void RegenerateExtremeChapterFiveTutorialAuthoringOnly(Tiles tiles)
        {
            EnsureFolder(TutorialDir);
            TutorialDef tutorial = ChapterFiveNestedTutorial();
            string path = TutorialDir + "/Chapter_5.prefab";
            BuildLevelPrefab(0, tutorial.level, tiles, path);

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.name = "Tutorial_Chapter_5";
                ParaboxLevel info = contents.GetComponent<ParaboxLevel>();
                if (info == null)
                    throw new System.InvalidOperationException(path + " is missing tutorial metadata.");
                info.levelName = "CHAPTER 5 TUTORIAL";
                info.solution = string.Empty;
                info.par = 0;
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            string proof = ParaboxCampaignSolver.SolveAndStore(path, 1, tutorial.maxDepth);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            List<MechanicCatalog.Id> mechanics = MechanicCatalog.MechanicsIn(prefab, -1);
            MechanicCatalog.Id[] required =
            {
                MechanicCatalog.Id.NestedBoard,
                MechanicCatalog.Id.ColourCargo,
                MechanicCatalog.Id.ButtonGate,
                MechanicCatalog.Id.OneWay,
                MechanicCatalog.Id.Portal,
            };
            foreach (MechanicCatalog.Id mechanic in required)
                if (!mechanics.Contains(mechanic))
                    throw new System.InvalidOperationException(
                        $"Chapter_5 tutorial is missing its real {mechanic} finale lesson.");
            if (!PlayerGoalRequiresPortal(tutorial.level))
                throw new System.InvalidOperationException(
                    "Chapter_5 tutorial must teach Level 41's mandatory sealed portal exit.");
            if (!TutorialProofVisitsEveryRoom(prefab, proof))
                throw new System.InvalidOperationException(
                    "Chapter_5 tutorial must enter every nested room before finishing.");
            if (!TutorialProofUsesPortal(prefab, proof))
                throw new System.InvalidOperationException(
                    "Chapter_5 tutorial must finish through its cyan portal pair.");
            if (!TutorialProofUsesChapterFiveContract(prefab, proof))
                throw new System.InvalidOperationException(
                    "Chapter_5 tutorial must extract coloured cargo across both room boundaries, "
                    + "leave it on the button, obey the one-way and cross the opened gate.");

            AssetDatabase.SaveAssets();
            Debug.Log("Parabox: generated only the real pre-Level-41 nested cargo/gate/one-way/portal "
                + "tutorial in Edit Mode.");
        }

        static bool TutorialProofUsesChapterFiveContract(GameObject prefab, string proof)
        {
            LevelModel model = LevelParser.Parse(prefab);
            if (model == null || model.player == null || string.IsNullOrEmpty(proof)) return false;

            var colouredCargo = new List<PEntity>();
            foreach (PEntity entity in model.entities)
                if (entity != null && entity.IsCrate && entity.interiorRoomId < 0
                    && entity.colour > 0)
                    colouredCargo.Add(entity);
            if (colouredCargo.Count == 0) return false;

            bool usedOneWay = false;
            bool activatedButton = model.GatesOpen();
            bool crossedOpenGate = false;
            int cargoBoundaryCrossings = 0;
            foreach (char command in proof)
            {
                if (!TryFoundationDirection(command, out Vector2Int direction)) return false;
                int oldPlayerRoom = model.player.roomId;
                Vector2Int oldPlayerCell = model.player.pos;
                bool gateWasOpen = model.GatesOpen();
                var cargoBefore = new Dictionary<PEntity, (int room, Vector2Int cell)>();
                foreach (PEntity cargo in colouredCargo)
                    cargoBefore[cargo] = (cargo.roomId, cargo.pos);

                if (!model.TryMovePlayer(direction)) return false;
                bool gateIsOpen = model.GatesOpen();
                activatedButton |= gateIsOpen;
                usedOneWay |= FoundationSegmentTouches(model,
                    oldPlayerRoom, oldPlayerCell, model.player.roomId, model.player.pos,
                    (room, cell) => room.OneWay(cell) != Vector2Int.zero);
                if (gateWasOpen || gateIsOpen)
                    crossedOpenGate |= FoundationSegmentTouches(model,
                        oldPlayerRoom, oldPlayerCell, model.player.roomId, model.player.pos,
                        (room, cell) => room.gate != null && room.gate[cell.x, cell.y]);

                foreach (PEntity cargo in colouredCargo)
                    if (cargoBefore[cargo].room != cargo.roomId)
                        cargoBoundaryCrossings++;
            }

            return model.IsWon() && usedOneWay && activatedButton && crossedOpenGate
                   && cargoBoundaryCrossings >= 2;
        }

        static bool TutorialProofUsesPortal(GameObject prefab, string proof)
        {
            LevelModel model = LevelParser.Parse(prefab);
            if (model == null || model.player == null || string.IsNullOrEmpty(proof)) return false;
            bool used = false;
            foreach (char command in proof)
            {
                if (!TryFoundationDirection(command, out Vector2Int direction)) return false;
                PRoom room = model.rooms[model.player.roomId];
                Vector2Int target = model.player.pos + direction;
                if (room.InBounds(target) && model.portalPair.ContainsKey((room.id, target)))
                    used = true;
                if (!model.TryMovePlayer(direction)) return false;
            }
            return used && model.IsWon();
        }

        static bool TutorialProofVisitsEveryRoom(GameObject prefab, string proof)
        {
            LevelModel model = LevelParser.Parse(prefab);
            if (model == null || model.player == null || string.IsNullOrEmpty(proof)) return false;
            var visited = new HashSet<int> { model.player.roomId };
            foreach (char command in proof)
            {
                if (!TryFoundationDirection(command, out Vector2Int direction)
                    || !model.TryMovePlayer(direction)) return false;
                visited.Add(model.player.roomId);
            }
            return model.IsWon() && visited.Count == model.rooms.Count;
        }

        static void RegenerateTutorialMiniLevelsCore()
        {
            var tiles = LoadLevelTiles();
            if (tiles == null) throw new System.InvalidOperationException("Parabox level tiles are missing.");
            EnsureFolder(TutorialDir);

            TutorialDef[] tutorials = CampaignTutorialMiniPuzzles();
            var expectedAssetNames = new HashSet<string>();
            for (int i = 0; i < tutorials.Length; i++)
            {
                TutorialDef tutorial = tutorials[i];
                if (string.IsNullOrWhiteSpace(tutorial.assetName)
                    || !expectedAssetNames.Add(tutorial.assetName))
                    throw new System.InvalidOperationException(
                        $"Tutorial asset name is missing or duplicated: {tutorial.assetName}.");
                string path = TutorialDir + "/" + tutorial.assetName + ".prefab";
                tutorial.level.solution = string.Empty;
                tutorial.level.par = 0;
                BuildLevelPrefab(0, tutorial.level, tiles, path);

                // A non-level root name prevents campaign layout rebalancing from treating this
                // small teaching board as Level 1 when LevelParser reads it.
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    contents.name = "Tutorial_" + tutorial.assetName;
                    ParaboxLevel info = contents.GetComponent<ParaboxLevel>();
                    info.levelName = TutorialDisplayName(tutorial);
                    info.solution = string.Empty;
                    info.par = 0;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                string proof = ParaboxCampaignSolver.SolveAndStore(path, 1, tutorial.maxDepth);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var mechanics = MechanicCatalog.MechanicsIn(prefab, -1);
                if (!mechanics.Contains(tutorial.mechanic))
                    throw new System.InvalidOperationException(
                        $"{tutorial.assetName} does not contain {tutorial.mechanic}.");

                LevelModel model = LevelParser.Parse(prefab);
                var visitedRooms = new HashSet<int> { model.player.roomId };
                var before = new Dictionary<PEntity, (int room, Vector2Int pos)>();
                int metaMoves = 0;
                int cargoBoundaryCrossings = 0;
                foreach (char step in proof)
                {
                    before.Clear();
                    foreach (PEntity entity in model.entities)
                        before[entity] = (entity.roomId, entity.pos);
                    Vector2Int direction = step == 'U' ? Vector2Int.up
                        : step == 'D' ? Vector2Int.down
                        : step == 'L' ? Vector2Int.left : Vector2Int.right;
                    if (!model.TryMovePlayer(direction))
                        throw new System.InvalidOperationException(
                            $"{tutorial.assetName} proof blocks at {step} ({proof}).");
                    visitedRooms.Add(model.player.roomId);
                    foreach (PEntity entity in model.entities)
                    {
                        if (!before.TryGetValue(entity, out var start)
                            || (start.room == entity.roomId && start.pos == entity.pos)) continue;
                        if (entity.interiorRoomId >= 0) metaMoves++;
                        else if (entity.IsCrate && start.room != entity.roomId) cargoBoundaryCrossings++;
                    }
                }
                if (!model.IsWon())
                    throw new System.InvalidOperationException(
                        $"{tutorial.assetName} proof does not complete its mini-puzzle.");
                if (tutorial.assetName == "Chapter_5"
                    && !TutorialProofUsesChapterFiveContract(prefab, proof))
                    throw new System.InvalidOperationException(
                        "Chapter_5 tutorial proof skipped its cargo/button/one-way/gate chain.");
                ValidateTutorialDemonstration(tutorial, visitedRooms.Count,
                    metaMoves, cargoBoundaryCrossings);
                Debug.Log($"Parabox tutorial: {tutorial.assetName} = {proof.Length} moves ({proof}).");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Parabox: generated and solver-validated {tutorials.Length} tutorial prefabs. " +
                "NEW MECHANIC videos are shown only at real first appearances.");
        }

        static string TutorialDisplayName(TutorialDef tutorial)
        {
            if (tutorial.assetName == "Chapter_1") return "CHAPTER 1 TUTORIAL";
            if (tutorial.assetName == "Chapter_2") return "CHAPTER 2 TUTORIAL";
            if (tutorial.assetName == "Chapter_3") return "CHAPTER 3 TUTORIAL";
            if (tutorial.assetName == "Chapter_4") return "CHAPTER 4 TUTORIAL";
            if (tutorial.assetName == "Chapter_5") return "CHAPTER 5 TUTORIAL";
            return "TUTORIAL - " + MechanicCatalog.DisplayName(tutorial.mechanic);
        }

        static void ValidateTutorialDemonstration(TutorialDef tutorial, int roomsVisited,
                                                  int metaMoves, int cargoBoundaryCrossings)
        {
            if (tutorial.assetName == "Chapter_2")
            {
                if (roomsVisited < 2)
                    throw new System.InvalidOperationException(
                        "Chapter_2 tutorial must enter the room inside its Para Box.");
                if (metaMoves < 3)
                    throw new System.InvalidOperationException(
                        "Chapter_2 tutorial must move and dock its Para Box before entry.");
                if (cargoBoundaryCrossings < 1)
                    throw new System.InvalidOperationException(
                        "Chapter_2 tutorial must deliver cargo out of its Para Box.");
            }
            if (tutorial.assetName == "Chapter_3")
            {
                if (roomsVisited < 3)
                    throw new System.InvalidOperationException(
                        "Chapter_3 tutorial must demonstrate a deep transfer through all three rooms.");
                if (metaMoves < 1)
                    throw new System.InvalidOperationException(
                        "Chapter_3 tutorial never moves and pins its movable room.");
                if (cargoBoundaryCrossings < 2)
                    throw new System.InvalidOperationException(
                        "Chapter_3 tutorial cargo must be relayed across both room boundaries.");
            }

            int requiredRooms = tutorial.mechanic == MechanicCatalog.Id.ChamberChain ? 4
                : tutorial.mechanic == MechanicCatalog.Id.MultiStageRecursion ? 3
                : tutorial.mechanic == MechanicCatalog.Id.NestedBoard ? 2
                : 1;
            if (roomsVisited < requiredRooms)
                throw new System.InvalidOperationException(
                    $"{tutorial.assetName} contains the rule but does not demonstrate it: "
                    + $"visited {roomsVisited}/{requiredRooms} rooms.");
            if (tutorial.assetName == "Chapter_4" && metaMoves < 2)
                throw new System.InvalidOperationException(
                    "Chapter_4 tutorial must move and then enter its room-box.");
            if (tutorial.assetName == "Chapter_5")
            {
                if (roomsVisited < 3)
                    throw new System.InvalidOperationException(
                        "Chapter_5 tutorial must demonstrate its complete room-inside-a-room chain.");
                if (cargoBoundaryCrossings < 2)
                    throw new System.InvalidOperationException(
                        "Chapter_5 tutorial must extract its coloured cargo across both nested boundaries.");
            }
        }

        // Chapter I deliberately uses only the core puzzle vocabulary. This migration removes the
        // old conveyor/current and one-way arrow objects from the already-generated prefabs, while
        // BuildLevelPrefab below makes the rule permanent for future campaign regenerations.
        public static void RemoveChapterOneDirectionalMechanicsSilent()
        {
            int removed = 0;
            for (int level = 1; level <= 10; level++)
            {
                string path = LevelDir + "/Level_" + level + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var marker in root.GetComponentsInChildren<CurrentMarker>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(marker.gameObject);
                        removed++;
                    }
                    foreach (var marker in root.GetComponentsInChildren<OneWayMarker>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(marker.gameObject);
                        removed++;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Parabox: removed {removed} directional arrow/current tiles from Levels 1-10.");
        }

        static Tiles LoadLevelTiles()
        {
            var tiles = new Tiles
            {
                floor = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Floor.prefab"),
                grid = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Grid.prefab"),
                border = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Border.prefab"),
                wall = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Wall.prefab"),
                box = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Box.prefab"),
                metaBox = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/MetaBox.prefab"),
                player = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Player.prefab"),
                boxGoal = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/BoxGoal.prefab"),
                playerGoal = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PlayerGoal.prefab"),
            };

            if (tiles.floor == null || tiles.grid == null || tiles.border == null ||
                tiles.wall == null || tiles.box == null || tiles.metaBox == null ||
                tiles.player == null || tiles.boxGoal == null || tiles.playerGoal == null)
            {
                Debug.LogError("Parabox: one or more base tile prefabs are missing. Run Create Everything first.");
                return null;
            }
            return tiles;
        }

        // ================================================= URP pipeline
        static void SetupUrpPipeline()
        {
            string rendererPath = SettingsDir + "/Parabox_URP_Renderer.asset";
            string pipelinePath = SettingsDir + "/Parabox_URP.asset";

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                TryReloadResources(rendererData);
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
        }

        static void TryReloadResources(Object target)
        {
            try
            {
                var type = System.Type.GetType(
                    "UnityEngine.Rendering.ResourceReloader, Unity.RenderPipelines.Core.Editor");
                var method = type?.GetMethod("ReloadAllNullIn",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, new object[] { target, "Packages/com.unity.render-pipelines.universal" });
            }
            catch { /* URP fills most of this in automatically */ }
        }

        // ================================================= folders / cleanup
        static void CleanGenerated()
        {
            AssetDatabase.DeleteAsset(SpriteDir);
            AssetDatabase.DeleteAsset(PrefabDir);
            AssetDatabase.DeleteAsset(SceneDir);
            AssetDatabase.DeleteAsset(SettingsDir);
            AssetDatabase.DeleteAsset(Root + "/Materials"); // leftover from the first HDRP run
        }

        static void CreateFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(SpriteDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(LevelDir);
            EnsureFolder(SceneDir);
            EnsureFolder(SettingsDir);
            EnsureFolder(Root + "/Art");     // drop a custom MenuBG.png / GameBG.png here (kept across re-runs)
            EnsureFolder(Root + "/Audio");   // bundled CC0 music lives here; Music.* can replace it on future re-runs
            EnsureFolder(Root + "/Fonts");   // drop a .ttf/.otf here to restyle all text, kept across re-runs
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int i = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, i));
            AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
        }

        // ================================================= procedural sprites
        static Sprites CreateSprites()
        {
            return new Sprites
            {
                // filled rounded square — boxes, player, UI cards (nicely rounded)
                fill = MakeSprite("Fill", 0.22f, 0f, Mathf.RoundToInt(0.22f * Res) + 10, true),
                // board floor / frame — barely rounded corners so the full-size grid doesn't visibly overshoot
                floorFill = MakeSprite("FloorFill", 0.03f, 0f, Mathf.RoundToInt(0.03f * Res) + 8, true),
                // barely-rounded square — walls (solid blocks that fill their cell)
                tile = MakeSprite("Tile", 0.11f, 0f, 0, false),
                // crisp near-square board cell — flat surface with only a whisper of top light
                cell = MakeShadedSprite("Cell", 0.09f, 1.20f, 0.80f),
                // Ring that traces the CELL exactly — same 0.05 corner radius. ringThin is 0.18,
                // so using it to outline a cell drew a round border around a square face and the
                // two corners fought each other. A border must follow the shape it borders.
                cellRing = MakeSprite("CellRing", 0.05f, 0.055f, Mathf.RoundToInt(0.10f * Res) + 6, true),
                // hairline grid, tiled over the flat floor (thin lines on cell edges)
                grid = MakeGridSprite(),
                // thin rounded ring — UI panel/button outlines (sliced)
                ringThin = MakeSprite("RingThin", 0.18f, 0.05f, Mathf.RoundToInt(0.23f * Res) + 8, true),
                // thick rounded ring — goals and meta-box frame (uniform scale)
                ringThick = MakeSprite("RingThick", 0.24f, 0.13f, 0, false),
                // The meta-box's own frame. ringThick's stroke is so fat that its inner opening is
                // only 0.605 units — narrower than the 0.72 interior it surrounds — so it drew OVER
                // the room's edge cells (goals came out as clipped "U"/"C" shapes) AND, once you
                // stepped inside the box, that band filled the view. 0.05 opens it to 0.740.
                ringBox = MakeSprite("RingBox", 0.22f, 0.05f, 0, false),
                // top-lit gradient rounded square — boxes & player look rounded / 3D
                shaded = MakeShadedSprite("Shaded", 0.22f, 1.12f, 0.62f),
                // soft dark blob — drop shadow under boxes & player
                shadow = MakeShadowSprite(),
                // premium padlock icon — locked levels on the level-select screen
                lockIcon = MakeLockSprite(),
                // 5-point star — the Advanced difficulty icon
                star = MakeStarSprite(),
                // The completion trophy. Four endings were built before anyone noticed the game
                // had no artifact for finishing it — only effects, which evaporate. This is the
                // thing you can point at.
                trophy = MakeTrophySprite(),
                // soft radial glow + vignette — premium atmospheric backgrounds
                glow = MakeGlowSprite(),
                vignette = MakeVignetteSprite(),
                // filled circle + circular ring — the premium radial countdown timer
                disc = MakeDiscSprite(),
                ringCircle = MakeRingCircleSprite(),
                iconPlay = MakePlaySprite(),
                // tileable per-tier floor surfaces: grass mottle / water ripples / cracked rock
                texGrass = MakeFloorTexSprite("TexGrass", 0),
                texWater = MakeFloorTexSprite("TexWater", 1),
                texRock = MakeFloorTexSprite("TexRock", 2),
                texStars = MakeFloorTexSprite("TexStars", 3),
                // world icons: triangle (tree canopy) + teardrop (droplet / flame)
                iconTri = MakeTriSprite(),
                iconDrop = MakeDropSprite(),
                // realistic shaded world icons (colour + lighting baked in)
                iconTreeR = MakeTreeIconSprite(),
                iconDropR = MakeDropIconSprite(),
                iconStarR = MakeStarIconSprite(),
                iconWaveR = MakeWaveIconSprite(),
                // ocean-region icons + underwater god-ray shaft
                iconCoral = MakeCoralIconSprite(),
                iconJelly = MakeJellyIconSprite(),
                godRay = MakeGodRaySprite(),
            };
        }

        static Sprite MakeSprite(string name, float radiusFrac, float ringFrac, int borderPx, bool sliced)
        {
            var tex = RoundedTex(Res, radiusFrac, ringFrac);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = sliced ? new Vector4(borderPx, borderPx, borderPx, borderPx) : Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Signed-distance rounded rect, white with an anti-aliased alpha edge.
        // ringFrac <= 0 => filled; otherwise a ring of that thickness (fraction of size).
        static Texture2D RoundedTex(int s, float radiusFrac, float ringFrac)
        {
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f;
            float half = s * 0.5f - 2f;
            float r = Mathf.Min(radiusFrac * s, half);
            float ring = ringFrac * s;

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r; // <0 inside

                    float a = Mathf.Clamp01(0.5f - d);
                    if (ring > 0f) a *= Mathf.Clamp01(0.5f + (d + ring)); // subtract inner disc
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // A 1-cell sprite with thin opaque lines on its left and bottom edges. Tiled over
        // the floor it draws a hairline grid at every cell boundary — no per-cell "plates".
        static Sprite MakeGridSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            int lw = Mathf.Max(2, Mathf.RoundToInt(0.03f * s));
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = (x < lw || y < lw)
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px);
            tex.Apply();

            string assetPath = SpriteDir + "/Grid.png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Point;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Rounded square with a top-to-bottom brightness gradient baked in (white top,
        // grey bottom). Tinted by the object's color it reads as a soft, rounded 3D piece.
        static Sprite MakeShadedSprite(string name, float radiusFrac, float topMul, float botMul)
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, half = s * 0.5f - 2f, r = Mathf.Min(radiusFrac * s, half);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    float m = Mathf.Lerp(botMul, topMul, y / (s - 1f)); // y=0 bottom, y=s-1 top
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f);
                    px[y * s + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, name, false, 0, FilterMode.Bilinear);
        }

        // Soft dark blob for a drop shadow (black, feathered edges).
        static Sprite MakeShadowSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, half = s * 0.5f - 2f, r = Mathf.Min(0.30f * s, half);
            float feather = 0.16f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c) - (half - r);
                    float dy = Mathf.Abs(y + 0.5f - c) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
                    float a = Mathf.Clamp01(0.5f - d / feather);
                    px[y * s + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Shadow", false, 0, FilterMode.Bilinear);
        }

        // A clean padlock icon (white, tinted gold at runtime): a rounded body with a
        // keyhole, topped by a semicircular shackle. Reads as "locked" on level cards.
        static Sprite MakeLockSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];

            float cx = s * 0.5f;
            float bodyTop = 0.60f * s, bodyBot = 0.14f * s;
            float bodyCx = cx, bodyCy = (bodyTop + bodyBot) * 0.5f;
            float bodyHalfW = 0.24f * s, bodyHalfH = (bodyTop - bodyBot) * 0.5f, bodyRad = 0.07f * s;

            float shCx = cx, shCy = bodyTop;          // shackle sits on the body's top edge
            float ro = 0.17f * s, ri = 0.105f * s;     // shackle outer / inner radius

            float khCx = cx, khCy = 0.36f * s, khR = 0.05f * s; // keyhole

            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;

                    // body: rounded-rect signed distance (<0 inside)
                    float dx = Mathf.Abs(fx - bodyCx) - (bodyHalfW - bodyRad);
                    float dy = Mathf.Abs(fy - bodyCy) - (bodyHalfH - bodyRad);
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                               Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float dBody = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - bodyRad;
                    float aBody = Mathf.Clamp01(0.5f - dBody);

                    // shackle: upper half of an annulus (the U on top)
                    float dr = Mathf.Sqrt((fx - shCx) * (fx - shCx) + (fy - shCy) * (fy - shCy));
                    float aRing = Mathf.Clamp01(0.5f + (ro - dr)) * Mathf.Clamp01(0.5f + (dr - ri));
                    if (fy < shCy) aRing = 0f;

                    float a = Mathf.Max(aBody, aRing);

                    // keyhole: circle + a short slot, cut out of the body
                    float dk = Mathf.Sqrt((fx - khCx) * (fx - khCx) + (fy - khCy) * (fy - khCy));
                    float cut = Mathf.Clamp01(0.5f - (khR - dk));
                    if (fx > khCx - 0.022f * s && fx < khCx + 0.022f * s && fy < khCy && fy > khCy - 0.12f * s)
                        cut = 0f;
                    a *= cut;

                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }

            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Lock", false, 0, FilterMode.Bilinear);
        }

        // Shared texture -> Sprite importer used by the generators above.
        static Sprite SaveTexAsSprite(Texture2D tex, string name, bool sliced, int borderPx, FilterMode filter)
        {
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res;
            imp.mipmapEnabled = false;
            imp.filterMode = filter;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = sliced ? new Vector4(borderPx, borderPx, borderPx, borderPx) : Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // ================================================= tile prefabs
        // ---- strict object design system (all values in CELL units; one scale for the whole game) ----
        const float ObjSize    = 0.84f;   // EVERY movable object + goal shares this exact footprint
        const float TileSize   = 0.90f;   // board tile + wall — fills its grid cell, leaving the gutter
        const float ObjShadow  = 0.92f;   // one uniform drop-shadow size under every object
        const float ObjShadowY = -0.085f; // one uniform shadow offset

        static Tiles CreateTilePrefabs(Sprites s)
        {
            var t = new Tiles();

            // Floor — sliced sprite sized to the room at runtime (barely-rounded so the grid aligns cleanly).
            var floor = new GameObject("Floor");
            var fsr = floor.AddComponent<SpriteRenderer>();
            fsr.sprite = s.floorFill; fsr.color = RoomColors[0];
            fsr.drawMode = SpriteDrawMode.Sliced; fsr.size = Vector2.one; fsr.sortingOrder = OrderFloor;
            t.floor = SavePrefab(floor, PrefabDir + "/Floor.prefab");

            // Grid — a hairline overlay tiled over the flat floor at runtime. Gives the
            // room a subtle grid WITHOUT a rounded plate under every object.
            var grid = new GameObject("Grid");
            var gsr = grid.AddComponent<SpriteRenderer>();
            gsr.sprite = s.grid; gsr.color = new Color(0f, 0f, 0f, 0.12f);
            gsr.drawMode = SpriteDrawMode.Tiled; gsr.size = Vector2.one; gsr.sortingOrder = OrderFloor + 1;
            t.grid = SavePrefab(grid, PrefabDir + "/Grid.prefab");

            // Frame base — solid rounded fill (same sprite as the floor), drawn behind an inset floor
            // at runtime so the border is a clean rounded frame (no hollow ring = no edge artifacts).
            var border = new GameObject("Border");
            var bsr = border.AddComponent<SpriteRenderer>();
            bsr.sprite = s.fill; bsr.color = Color.white;
            bsr.drawMode = SpriteDrawMode.Sliced; bsr.size = Vector2.one; bsr.sortingOrder = OrderBorder;
            t.border = SavePrefab(border, PrefabDir + "/Border.prefab");

            // Wall — dark block that fills its cell (reads as a solid barrier).
            var wall = new GameObject("Wall");
            SpriteChild("Sprite", wall.transform, s.tile, WallColor, OrderWall, Vector3.zero, V(TileSize));
            t.wall = SavePrefab(wall, PrefabDir + "/Wall.prefab");

            // Box — a soft drop shadow behind a top-lit rounded square (reads as 3D).
            var box = new GameObject("Box");
            box.AddComponent<EntityView>();
            SpriteChild("Shadow", box.transform, s.shadow, ShadowCol, OrderBox - 1, new Vector3(0f, ObjShadowY, 0f), V(ObjShadow));
            SpriteChild("Sprite", box.transform, s.shaded, BoxColor, OrderBox, Vector3.zero, V(ObjSize));
            // inset panel + top highlight — gives the crate a readable face instead of a flat square
            SpriteChild("Panel", box.transform, s.shaded, Lighten(BoxColor, 0.22f), OrderBox + 1,
                        Vector3.zero, V(ObjSize * 0.58f));
            SpriteChild("Gloss", box.transform, s.fill, new Color(1f, 1f, 1f, 0.20f), OrderBox + 2,
                        new Vector3(0f, ObjSize * 0.26f, 0f), new Vector3(ObjSize * 0.66f, ObjSize * 0.13f, 1f));
            t.box = SavePrefab(box, PrefabDir + "/Box.prefab");

            // MetaBox — dark backing + colored frame. Interior room is nested at runtime;
            // a SortingGroup isolates the contents so nesting sorts correctly.
            var meta = new GameObject("MetaBox");
            meta.AddComponent<EntityView>();
            meta.AddComponent<SortingGroup>().sortingOrder = OrderBox;
            SpriteChild("Shadow", meta.transform, s.shadow, ShadowCol, OrderBackingInBox - 10, new Vector3(0f, ObjShadowY, 0f), V(ObjShadow));
            SpriteChild("Backing", meta.transform, s.shaded, Darken(RoomColors[1], 0.5f), OrderBackingInBox, Vector3.zero, V(ObjSize));
            SpriteChild("Frame", meta.transform, s.ringBox, Lighten(RoomColors[1], 0.3f), OrderFrameInBox, Vector3.zero, V(ObjSize));
            t.metaBox = SavePrefab(meta, PrefabDir + "/MetaBox.prefab");

            // Player — a shadow behind a top-lit rounded box with two eyes; eyes blink.
            var player = new GameObject("Player");
            player.AddComponent<EntityView>();
            SpriteChild("Shadow", player.transform, s.shadow, ShadowCol, OrderPlayer - 1, new Vector3(0f, ObjShadowY, 0f), V(ObjShadow));
            SpriteChild("Body", player.transform, s.shaded, PlayerColor, OrderPlayer, Vector3.zero, V(ObjSize));
            var eyeL = SpriteChild("EyeL", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(-0.162f, 0.067f, 0f), new Vector3(0.16f, 0.22f, 1f));
            var eyeR = SpriteChild("EyeR", player.transform, s.fill, EyeColor, OrderPlayer + 1, new Vector3(0.162f, 0.067f, 0f), new Vector3(0.16f, 0.22f, 1f));
            var blink = player.AddComponent<Blinker>();
            blink.eyeL = eyeL.transform;
            blink.eyeR = eyeR.transform;
            blink.stepsPerBlink = 2;
            blink.blinkTime = 0.16f;
            blink.closedEyeScale = 0.06f;
            t.player = SavePrefab(player, PrefabDir + "/Player.prefab");

            // Goals — thick rounded outline in the target's color.
            var boxGoal = new GameObject("BoxGoal");
            SpriteChild("Glow", boxGoal.transform, s.glow, WithAlpha(BoxColor, 0.42f), OrderGoal - 1, Vector3.zero, V(ObjSize * 1.7f));
            SpriteChild("Socket", boxGoal.transform, s.shaded, new Color(0f, 0f, 0f, 0.34f), OrderGoal, Vector3.zero, V(ObjSize * 0.86f));
            SpriteChild("Ring", boxGoal.transform, s.ringThick, BoxColor, OrderGoal + 1, Vector3.zero, V(ObjSize));
            t.boxGoal = SavePrefab(boxGoal, PrefabDir + "/BoxGoal.prefab");

            var playerGoal = new GameObject("PlayerGoal");
            SpriteChild("Glow", playerGoal.transform, s.glow, WithAlpha(PlayerColor, 0.42f), OrderGoal - 1, Vector3.zero, V(ObjSize * 1.7f));
            SpriteChild("Socket", playerGoal.transform, s.shaded, new Color(0f, 0f, 0f, 0.34f), OrderGoal, Vector3.zero, V(ObjSize * 0.86f));
            SpriteChild("Ring", playerGoal.transform, s.ringThick, PlayerColor, OrderGoal + 1, Vector3.zero, V(ObjSize));
            t.playerGoal = SavePrefab(playerGoal, PrefabDir + "/PlayerGoal.prefab");

            return t;
        }

        static GameObject SpriteChild(string name, Transform parent, Sprite sprite, Color color,
                                      int order, Vector3 localPos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            return go;
        }

        static Vector3 V(float f) => new Vector3(f, f, 1f);

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ================================================= level definitions
        // Rooms are borderless — the grid edge IS the boundary (no perimeter walls).
        //   # wall   . floor   P player   b box
        //   1..9 meta-box containing that room id
        //   x box goal   p player goal
        class LevelDef
        {
            public string name;
            public string[][] rooms;
            public int par;
            public string solution;
            public int authoredOrder;
            public int designComplexity;
        }

        class TutorialDef
        {
            public string assetName;
            public MechanicCatalog.Id mechanic;
            public LevelDef level;
            public int maxDepth = 64;
        }

        static TutorialDef Tutorial(string assetName, MechanicCatalog.Id mechanic,
                                    string[][] rooms, int maxDepth = 64)
            => new TutorialDef
            {
                assetName = assetName,
                mechanic = mechanic,
                maxDepth = maxDepth,
                level = new LevelDef
                {
                    name = "TUTORIAL",
                    rooms = rooms,
                    par = 0,
                    solution = string.Empty,
                }
            };

        static TutorialDef ChapterFiveNestedTutorial()
        {
            TutorialDef tutorial = Tutorial("Chapter_5", MechanicCatalog.Id.ColourCargo, new[]
            {
                // A clean two-zone finale lesson. The thin centre divider makes the dependency
                // visible without the old giant wall slab: solve the nested cargo job on the left,
                // park coral on the combined target/button, then cross the single gate and its
                // right-facing one-way into the bright finish chamber.
                new[]
                {
                    "##########",
                    "#P...#..p#",
                    "#....#...#",
                    "#..Q.G>..#",
                    "#....#...#",
                    "#..&.#...#",
                    "##########",
                },
                new[]
                {
                    "##.##",
                    "#...#",
                    "...U#",
                    "#...#",
                    "##.##",
                },
                new[]
                {
                    "#####",
                    "#...#",
                    "..J..",
                    "#...#",
                    "#####",
                },
            }, 180);
            // Level 41 already combines nested rooms with Chapter IV's portal. Replace the normal
            // finish with the same small sealed portal pocket so the tutorial teaches the actual
            // finale contract instead of an easier, unrelated lesson.
            AddMandatoryPortalExit(tutorial.level, 1);
            return tutorial;
        }

        static TutorialDef[] TutorialMiniPuzzles()
        {
            return new[]
            {
                // A second, independent Chapter I example: obey the arrow, push an ordinary crate
                // onto its target, then route the player to a separate target. Unlike Level 1, it
                // demonstrates both victory conditions and contains no copied campaign silhouette.
                Tutorial("Chapter_1", MechanicCatalog.Id.Navigation, new[] { new[]
                {
                    "########",
                    "#....p.#",
                    "#..x...#",
                    "#..b...#",
                    "#P>....#",
                    "########"
                }}),
                // One complete Chapter II teaching level. It uses the chapter's real loop on one
                // two-room board: reposition and dock the Para Box, enter it, bring its coloured
                // cargo outside, satisfy the matching cargo goal, then reach the player goal.
                // The seven runtime captions reveal these actions one at a time.
                Tutorial("Chapter_2", MechanicCatalog.Id.NestedBoard, new[]
                {
                    new[]
                    {
                        "#########",
                        "#.p.....#",
                        "#P.1....#",
                        "#....x.j#",
                        "#....#..#",
                        "#########",
                    },
                    new[]
                    {
                        "##.##",
                        "#...#",
                        "#.J>.",
                        "#...#",
                        "##.##",
                    },
                }, 80),
                // Chapter III uses a separate vertical mini-puzzle rather than revealing Level 21.
                // It combines the chapter's complete readable vocabulary: cross a fixed room-door,
                // move and pin the smaller room, then relay its coloured cargo back across both
                // boundaries. The camera therefore teaches the relationships, not a campaign route.
                Tutorial("Chapter_3", MechanicCatalog.Id.NestedBoard, new[]
                {
                    new[] { "#####", "#Pj.#", "#...#", "#...#", "#...#", "#...#", "#.Q.#", "#..p#", "#####" },
                    new[] { "##.##", "#...#", "#...#", "..2..", "#...#", "#.#.#", "#####" },
                    new[] { "##.##", "#...#", "#.J.#", "#...#", "##.##" },
                }, 80),
                // A separate Chapter IV mini-game in the same self-playing format as Chapter I.
                // It shows all three chapter jobs on one clean board: push the room twice onto the
                // glowing dock, pin it against the stop so it becomes enterable, then leave through
                // its lower doorway to reach the player target. It is not copied from Level 31.
                Tutorial("Chapter_4", MechanicCatalog.Id.NestedBoard, new[]
                {
                    new[] { "########", "#P.1.x##", "#####p##", "########" },
                    new[] { "#####", "#...#", "....#", "#...#", "##.##" },
                }, 80),
                // The finale tutorial mirrors the real Level 41 identity: enter a room inside a
                // room, extract coloured cargo across both boundaries, hold the cargo button and
                // take the one-way through its gate. It does not reveal Level 41's route.
                ChapterFiveNestedTutorial(),
            };
        }

        // Small, independent examples used only when a mechanic first appears in the campaign.
        // They are generated as Resources prefabs in Edit Mode; runtime never constructs a level
        // definition or borrows the campaign board that follows the video.
        static TutorialDef[] MechanicTutorialMiniPuzzles()
        {
            return new[]
            {
                Tutorial("Mechanic_OneWay", MechanicCatalog.Id.OneWay, new[] { new[]
                {
                    "#######", "#P>...#", "###.#p#", "#.....#", "#######"
                }}),
                Tutorial("Mechanic_Crate", MechanicCatalog.Id.Crate, new[] { new[]
                {
                    "#######", "#..x..#", "#..b..#", "#P...p#", "#######"
                }}),
                Tutorial("Mechanic_DeepWater", MechanicCatalog.Id.DeepWater, new[] { new[]
                {
                    "#######", "#P,,.p#", "#.....#", "#######"
                }}),
                Tutorial("Mechanic_ButtonGate", MechanicCatalog.Id.ButtonGate, new[] { new[]
                {
                    "########", "#P.bB###", "#...####", "###G.p##", "#.....##", "########"
                }}),
                Tutorial("Mechanic_BreakableRock", MechanicCatalog.Id.BreakableRock, new[] { new[]
                {
                    "#########", "#P.bR.p.#", "#########"
                }}),
                Tutorial("Mechanic_Updraft", MechanicCatalog.Id.Updraft, new[] { new[]
                {
                    "#######", "#..x..#", "#..u..#", "#..b..#", "#P...p#", "#######"
                }}),
                Tutorial("Mechanic_Trench", MechanicCatalog.Id.Trench, new[] { new[]
                {
                    "#######", "#Pb~.p#", "#######"
                }}),
                Tutorial("Mechanic_Ice", MechanicCatalog.Id.Ice, new[] { new[]
                {
                    "#######", "#P__p##", "#######"
                }}),
                Tutorial("Mechanic_StickyFloor", MechanicCatalog.Id.StickyFloor, new[] { new[]
                {
                    "########", "#P;..p.#", "########"
                }}),
                Tutorial("Mechanic_Cage", MechanicCatalog.Id.Cage, new[] { new[]
                {
                    "#######", "#Pb[..#", "#...p.#", "#######"
                }}),
                Tutorial("Mechanic_KeyLock", MechanicCatalog.Id.KeyLock, new[] { new[]
                {
                    "#######", "#PkK.p#", "#######"
                }}),
                Tutorial("Mechanic_Magnet", MechanicCatalog.Id.Magnet, new[] { new[]
                {
                    "#######", "#b.x#Y#", "#P..p.#", "#######"
                }}),
                Tutorial("Mechanic_Echo", MechanicCatalog.Id.Echo, new[] { new[]
                {
                    "#######", "#P..p.#", "#E..e.#", "#######"
                }}),
                Tutorial("Mechanic_Sand", MechanicCatalog.Id.Sand, new[] { new[]
                {
                    "########", "#....x.#", "#..b...#", "#..-...#",
                    "#P...p.#", "#......#", "########"
                }}),
                Tutorial("Mechanic_LockingCargo", MechanicCatalog.Id.LockingCargo, new[] { new[]
                {
                    "########", "#..x...#", "#..q...#", "#P...p.#", "########"
                }}),
                Tutorial("Mechanic_ToggleLatch", MechanicCatalog.Id.ToggleLatch, new[] { new[]
                {
                    "########", "#P.TL.p#", "#......#", "########"
                }}),
                Tutorial("Mechanic_HeavyPlateGate", MechanicCatalog.Id.HeavyPlateGate, new[] { new[]
                {
                    "########", "#P.bW###", "#...####", "###H.p##", "#.....##", "########"
                }}),
                Tutorial("Mechanic_ColourCargo", MechanicCatalog.Id.ColourCargo, new[] { new[]
                {
                    "#########", "#..j.n..#", "#..J.N..#", "#P.....p#", "#########"
                }}, 80),
                // Purpose-built pre-Level-35 lesson. The player target is in a completely sealed
                // pocket, so the cyan pair is the route rather than decoration: enter the left
                // whirlpool, emerge beside the target, then finish with one readable move.
                Tutorial("Mechanic_Portal", MechanicCatalog.Id.Portal, new[] { new[]
                {
                    "#########",
                    "#P.o#####",
                    "###.o..p#",
                    "#...#####",
                    "#########",
                }}, 24),
                Tutorial("Mechanic_NestedBoard", MechanicCatalog.Id.NestedBoard, new[]
                {
                    new[] { "########", "#P.Q...#", "###.p###", "#......#", "########" },
                    new[] { "#####", "#...#", "....#", "#...#", "##.##" },
                }),
                Tutorial("Mechanic_MultiStageRecursion", MechanicCatalog.Id.MultiStageRecursion, new[]
                {
                    new[] { "########", "#P.Q...#", "###.p###", "#......#", "########" },
                    new[] { "#######", "#######", "...U###", "###.###", "###.###" },
                    new[] { "#####", "#...#", "....#", "#...#", "##.##" },
                }, 80),
                Tutorial("Mechanic_ChamberChain", MechanicCatalog.Id.ChamberChain, new[]
                {
                    new[] { "########", "#P.Q...#", "###.p###", "#......#", "########" },
                    new[] { "#######", "#######", "...U###", "###.###", "###.###" },
                    new[] { "#######", "#######", "...V###", "###.###", "###.###" },
                    new[] { "#####", "#...#", "....#", "#...#", "##.##" },
                }, 96),
            };
        }

        // Generate all independent examples. Runtime still shows only the one selected from the
        // current level's real first appearances; deeper-recursion examples remain authoring
        // references and do not count as separate mechanics. Prebuilding the complete library
        // keeps builds deterministic and avoids runtime-created tutorial content.
        static TutorialDef[] CampaignTutorialMiniPuzzles()
        {
            var result = new List<TutorialDef>(TutorialMiniPuzzles());
            foreach (TutorialDef tutorial in MechanicTutorialMiniPuzzles())
                result.Add(tutorial);
            return result.ToArray();
        }

        // Explicit player-facing curriculum. A heuristic may measure an authored board, but it may
        // never decide teaching order: a long corridor is not automatically harder than a short
        // dependency puzzle. Keeping the order named and reviewable also means playtest feedback
        // such as "Level 12 feels easier than Level 11" can be fixed intentionally and permanently.
        static readonly string[] CurriculumOrder =
        {
            "The Right Way", "First Push", "Set Up the Push", "Ice Inside", "Bridge the Gap",
            "Open the Gate", "Weight Matters", "Deep Crossing", "Pearl Lock", "Recursive Turn",

            "The Drift", "The Geyser", "Against the Arrow", "Dead Weight", "Deep Water",
            "Pearl Pair", "The Shell Switch", "Through the Kelp", "No Way Back", "Undertow",

            "Too Narrow", "Slippery Cargo", "Break Through", "Throw the Switch", "Off Beat",
            "Get a Run-Up", "Down the Well", "Your Shadow", "Colour Coded", "The Machine",

            "Wrong Chimney", "Facing Away", "One Way In", "Carried Past", "Magnetic Relay",
            "Set in Stone", "Nothing to Brace", "Opposite Numbers", "Two of Us", "The Undertow",

            // The chamber finale is deliberately ordered by dependency depth rather than source
            // order. Every new chamber rule is first readable on its own, then combined, and the
            // final three boards require several previously learned systems at once.
            "Switch Chamber", "Boulder Chamber", "Pearl Chamber", "Pulse Chamber", "One-Way Chamber",
            "Weight Chamber", "Latch Chamber", "Coral Chamber", "Undertow Chamber", "The Machine Within"
        };

        // The first two chapters keep their compact teaching identities.  Chapters III-V then use
        // one continuous room-puzzle curriculum (declared below) so the campaign never resets to
        // an easier lesson at Level 31 or 41.
        static readonly string[] FirstTwentyDifficultyOrder =
        {
            "The First Commitment", "Corner Delivery", "Two Deliveries", "Committed Pair", "First Gate",
            "Gate Delivery", "Turn Beyond", "Double Passage", "Gate Relay", "Foundation Circuit",

            "Inside Delivery", "Docked Passage", "Reverse Dock", "Side Extraction", "Reverse Extraction",
            "Turn It Inside", "Inverted Turn", "Two Rooms Down", "Reverse Two Rooms", "Three-Space Relay",
        };

        // A single reviewed sequence for Levels 21-50.  Early entries teach pin/enter/dock with
        // short routes; middle entries combine cargo and branching rooms; the final ten require
        // deep transfers across three to five coordinate spaces.  Equal par values are ordered by
        // added rooms, turns, cargo crossings or sibling-branch dependencies, never by board size.
        static readonly string[] RoomCurriculumDifficultyOrder =
        {
            "Cargo Through the Room", "Moving Delivery", "Crossing Rooms", "Side Extraction", "Corner Dock",
            "Reverse Entry", "Two Room Relay", "Room Within a Room", "Turn Inside", "Turn It Inside",
            "Side Exit", "Bring It Out", "Branch and Nest", "Inner Pillar", "Two Rooms Down",
            "Send It In", "Three-Space Relay", "Three Rooms Deep", "Triple Dock", "Shift the Chamber",
            "Two Rooms Deep", "Long Inner Relay", "Deep Corner", "Boundary Relay", "Between Worlds",
            "Exit Side", "Return Pocket", "The Long Way Out", "Nested Exodus", "Rooms Within Rooms",
        };

        // These are minimum authored requirements, not numbers shown to the player. They protect
        // the reviewed curve when somebody later edits or replaces a board and then uses the
        // authoring-only generator. A level may have fewer moves than the one before it only when
        // it adds genuine decision evidence (mechanics, objectives or another coordinate space).
        // This is important at chapter boundaries: counting corridor steps would make a long,
        // familiar route look harder than learning to move and enter a room-box.
        static readonly int[] MinimumAuthoredPars =
        {
            // Chapter I is the onboarding chapter.  Its source floors protect the short, reviewed
            // teaching routes rather than forcing end-game-sized paths into the first ten boards.
            // Level 2 intentionally keeps the cabinet-reviewed 16-move solve; its route is long
            // but completely linear and therefore easier to understand than a short dependency.
            4, 16, 7, 6, 6, 7, 8, 12, 19, 19,
            15, 16, 18, 19, 19, 19, 20, 21, 21, 22,
            23, 27, 29, 32, 35, 42, 45, 46, 54, 65,
            32, 32, 33, 34, 34, 35, 36, 38, 39, 40,
            // Chapter V deliberately has no rising move quota. Its progression is protected by
            // nested depth, five real jobs, the gate/portal chain and a 3..12 decision ladder.
            40, 40, 40, 40, 40, 40, 40, 40, 40, 40,
        };

        static readonly int[] MinimumRoomCounts =
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 3, 3, 3,
            2, 2, 2, 2, 2, 3, 3, 3, 3, 4,
            2, 3, 4, 2, 3, 3, 3, 4, 4, 3,
            4, 4, 4, 4, 4, 5, 5, 5, 5, 5,
        };

        static LevelDef[] Levels()
        {
            // This is the mechanic-rich campaign currently shipped by the game. The smaller
            // recursive prototypes remain below as references, but regenerating the project must
            // never silently replace the real 50 levels with those prototypes.
            var levels = LegacyAuthoredLevels();
            var byName = new Dictionary<string, LevelDef>(System.StringComparer.Ordinal);
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i].authoredOrder = i;
                if (!byName.TryAdd(levels[i].name, levels[i]))
                    throw new System.InvalidOperationException($"Duplicate authored level name: {levels[i].name}");
            }

            if (CurriculumOrder.Length != levels.Length)
                throw new System.InvalidOperationException(
                    $"Curriculum has {CurriculumOrder.Length} entries for {levels.Length} authored levels.");

            var ordered = new LevelDef[CurriculumOrder.Length];
            for (int i = 0; i < CurriculumOrder.Length; i++)
            {
                if (!byName.TryGetValue(CurriculumOrder[i], out LevelDef level))
                    throw new System.InvalidOperationException(
                        $"Curriculum references missing level '{CurriculumOrder[i]}'.");

                level.designComplexity = AuthoredDifficulty(level);
                ordered[i] = level;
            }

            // Build one candidate library, then apply the reviewed global difficulty order. This
            // removes the old chapter-boundary resets while preserving every compact authored board.
            var candidates = new Dictionary<string, LevelDef>(byName, System.StringComparer.Ordinal);
            foreach (LevelDef level in ChapterOneFoundations()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterTwoInsideTheBox()) candidates[level.name] = level;
            foreach (LevelDef level in ReviewedChapterTwoLevels()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterThreeSynergy()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterFourRoomManeuvers()) candidates[level.name] = level;
            foreach (LevelDef level in ChapterFiveRecursion()) candidates[level.name] = level;
            for (int i = 0; i < FirstTwentyDifficultyOrder.Length; i++)
            {
                string name = FirstTwentyDifficultyOrder[i];
                if (!candidates.TryGetValue(name, out LevelDef level))
                    throw new System.InvalidOperationException($"Difficulty curve references missing level '{name}'.");
                level.designComplexity = ReviewedCampaignDifficulty(level, i);
                ordered[i] = level;
            }

            for (int step = 0; step < RoomCurriculumDifficultyOrder.Length; step++)
            {
                string name = RoomCurriculumDifficultyOrder[step];
                if (!candidates.TryGetValue(name, out LevelDef level))
                    throw new System.InvalidOperationException(
                        $"Room curriculum references missing level '{name}'.");
                int campaignIndex = 20 + step;
                level.designComplexity = ReviewedCampaignDifficulty(level, campaignIndex);
                ordered[campaignIndex] = level;
            }

            // Chapter III is an explicit progressive sequence. Keep the same definitions used by
            // its focused generator so a later whole-project rebuild cannot restore the older
            // mixed chapter order or remove the Level 25-30 portal contract.
            LevelDef[] chapterThree = ChapterThreeSynergy();
            for (int i = 0; i < chapterThree.Length; i++)
            {
                chapterThree[i].designComplexity = ReviewedCampaignDifficulty(
                    chapterThree[i], 20 + i);
                ordered[20 + i] = chapterThree[i];
            }

            // Chapter V is an explicit finale rather than the tail of the mixed room curriculum.
            // Replacing only these ten source slots keeps Chapters I-IV byte-for-byte outside a
            // focused Chapter V rebuild while guaranteeing box-inside-box gameplay at the end.
            LevelDef[] finale = ChapterFiveExtremeNestedRooms();
            for (int i = 0; i < finale.Length; i++)
            {
                finale[i].designComplexity = ReviewedCampaignDifficulty(finale[i], 40 + i);
                ordered[40 + i] = finale[i];
            }

            ValidateAuthoredDifficultyCurve(ordered);

            // All swapped definitions keep authored, source-reviewable evidence. Prefab generation
            // copies the same score, so metadata cannot silently disagree with this curve.
            return ordered;
        }

        // Source-only release gate. This deliberately does not enter Play Mode, run the solver or
        // replay a level. It checks the data the designer asked the generator to create, then the
        // designer remains responsible for the final cabinet/gameplay pass.
        static void ValidateAuthoredDifficultyCurve(LevelDef[] levels)
        {
            if (levels == null || levels.Length != 50)
                throw new System.InvalidOperationException(
                    $"Difficulty curve must contain exactly 50 levels; found {levels?.Length ?? 0}.");
            if (MinimumAuthoredPars.Length != levels.Length
                || MinimumRoomCounts.Length != levels.Length)
                throw new System.InvalidOperationException("Difficulty contract length is out of sync.");

            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var layouts = new HashSet<string>(System.StringComparer.Ordinal);
            int previousEvidence = -1;
            for (int i = 0; i < levels.Length; i++)
            {
                LevelDef level = levels[i];
                int number = i + 1;
                if (level == null || string.IsNullOrWhiteSpace(level.name))
                    throw new System.InvalidOperationException($"Level {number} has no authored identity.");
                if (!names.Add(level.name))
                    throw new System.InvalidOperationException(
                        $"Level {number} repeats the name '{level.name}'. Every puzzle must be distinct.");
                if (level.rooms == null || level.rooms.Length < MinimumRoomCounts[i])
                    throw new System.InvalidOperationException(
                        $"Level {number} has {level.rooms?.Length ?? 0} rooms; its difficulty floor requires " +
                        $"at least {MinimumRoomCounts[i]}.");
                if (string.IsNullOrWhiteSpace(level.solution)
                    || level.solution.Length != level.par)
                    throw new System.InvalidOperationException(
                        $"Level {number} must keep one explicit authored route whose length equals par.");
                if (level.par < MinimumAuthoredPars[i])
                    throw new System.InvalidOperationException(
                        $"Level {number} route fell to {level.par}; reviewed floor is {MinimumAuthoredPars[i]}.");

                int playerCount = 0;
                int targetCount = 0;
                int cargoTargetCount = 0;
                int playerTargetCount = 0;
                int chapterThreeCargoCount = 0;
                int chapterThreeCargoGoalCount = 0;
                int ordinaryCargoCount = 0;
                int movableRoomCount = 0;
                int referencedRoomCount = 0;
                int roomSocketCount = 0;
                var fingerprint = new System.Text.StringBuilder();
                for (int roomIndex = 0; roomIndex < level.rooms.Length; roomIndex++)
                {
                    string[] room = level.rooms[roomIndex];
                    if (room == null || room.Length == 0 || string.IsNullOrEmpty(room[0]))
                        throw new System.InvalidOperationException(
                            $"Level {number}, room {roomIndex} is empty.");
                    int width = room[0].Length;
                    fingerprint.Append('[').Append(width).Append('x').Append(room.Length).Append(':');
                    for (int rowIndex = 0; rowIndex < room.Length; rowIndex++)
                    {
                        string row = room[rowIndex];
                        if (row == null || row.Length != width)
                            throw new System.InvalidOperationException(
                                $"Level {number}, room {roomIndex}, row {rowIndex} is not rectangular.");
                        fingerprint.Append(row).Append('/');
                        foreach (char ch in row)
                        {
                            // Chapter I is intentionally limited to four immediately readable
                            // rules: one-way arrows, ordinary pushing and button/gate pairs.
                            // Rejecting every other token here prevents a later
                            // edit from quietly restoring water, updrafts, ice, cages or another
                            // one-off mechanic to the beginner chapter.
                            if (i < 10)
                            {
                                bool allowedFoundationCell = ch == '#' || ch == '.'
                                    || ch == 'P' || ch == 'p'
                                    || ch == 'b' || ch == 'x'
                                    || ch == 'B' || ch == 'G' || ch == '&'
                                    || OneWayDirs.ContainsKey(ch);
                                if (!allowedFoundationCell)
                                    throw new System.InvalidOperationException(
                                        $"Level {number} uses '{ch}', which is outside Chapter I's "
                                        + "foundation vocabulary.");
                            }

                            // Chapter II uses the saved recursive room-box vocabulary.
                            if (i >= 10 && i < 20)
                            {
                                bool allowedChapterTwoCell = ch == '#' || ch == '.'
                                    || ch == 'P' || ch == 'p'
                                    || ch == 'b' || ch == 'x'
                                    || ch == 'J' || ch == 'j'
                                    || ch == 'N' || ch == 'n'
                                    || ch == 'Z' || ch == 'z'
                                    || ch == 'B' || ch == 'G'
                                    || OneWayDirs.ContainsKey(ch)
                                    || (ch >= '1' && ch <= '9')
                                    || AnchoredBoxes.ContainsKey(ch);
                                if (!allowedChapterTwoCell)
                                    throw new System.InvalidOperationException(
                                        $"Level {number} uses '{ch}', which is outside Chapter II's "
                                        + "room-box vocabulary.");
                            }

                            // Chapters III-V share a readable room curriculum. Chapter V may also
                            // use Chapter IV's portal; unrelated terrain hazards remain forbidden.
                            // Difficulty comes from nested rooms and purposeful dependencies.
                            if (i >= 20)
                            {
                                bool allowedRoomCurriculumCell = ch == '#' || ch == '.'
                                    || ch == 'P' || ch == 'p'
                                    || ch == 'b' || ch == 'x'
                                    || ch == 'J' || ch == 'j'
                                    || ch == 'N' || ch == 'n'
                                    || ch == 'Z' || ch == 'z'
                                    || ch == 'A' || ch == 'C'
                                    || (ch >= '1' && ch <= '9')
                                    || AnchoredBoxes.ContainsKey(ch)
                                    || ((i >= 24 && i < 30) || i >= 40) && ch == 'o';
                                if (!allowedRoomCurriculumCell)
                                    throw new System.InvalidOperationException(
                                        $"Level {number} uses unrelated mechanic '{ch}'. Chapters III-V "
                                        + "may use only players, cargo, goals and recursive rooms.");
                            }

                            if (ch == 'P') playerCount++;
                            if (ch == 'p') playerTargetCount++;
                            if (ch == 'J') chapterThreeCargoCount++;
                            if (ch == 'j') chapterThreeCargoGoalCount++;
                            if (ch == 'b') ordinaryCargoCount++;
                            if (ch >= '1' && ch <= '9')
                            {
                                movableRoomCount++;
                                referencedRoomCount++;
                            }
                            if (AnchoredBoxes.ContainsKey(ch)) referencedRoomCount++;
                            if (ch == 'x') roomSocketCount++;
                            if (ch == 'p' || ch == 'e' || ch == 'm' || ch == 'x' || ch == '&'
                                || ColourGoals.ContainsKey(ch) || ColourCargoOnGoals.ContainsKey(ch))
                                targetCount++;
                            if (ch == 'x' || ch == '&' || ColourGoals.ContainsKey(ch)
                                || ColourCargoOnGoals.ContainsKey(ch))
                                cargoTargetCount++;
                        }
                    }
                    fingerprint.Append(']');
                }

                if (playerCount == 0 || targetCount == 0)
                    throw new System.InvalidOperationException(
                        $"Level {number} needs a controlled player and at least one meaningful objective.");
                if (!layouts.Add(fingerprint.ToString()))
                    throw new System.InvalidOperationException(
                        $"Level {number} repeats an earlier layout. Difficulty cannot come from recolouring it.");

                // On the sealed-exit gate boards, treat every gate as closed and prove that no
                // player goal is reachable. Level 9 instead proves its relay dependency by replay:
                // the second cargo crosses the opened gate before the holding crate is recovered.
                if (((number >= 5 && number <= 8) || number == 10)
                    && !PlayerGoalRequiresGreenGate(level))
                    throw new System.InvalidOperationException(
                        $"Level {number} must seal every player goal behind a green gate. "
                        + "Redesign the board so it cannot finish without holding the green button.");

                if (i >= 20)
                {
                    if (playerCount != 1 || playerTargetCount != 1)
                        throw new System.InvalidOperationException(
                            $"Level {number} must have exactly one player and one separate player target.");
                    if (referencedRoomCount != level.rooms.Length - 1)
                        throw new System.InvalidOperationException(
                            $"Level {number} must reference every inner room exactly once.");
                    if (level.rooms.Length != MinimumRoomCounts[i])
                        throw new System.InvalidOperationException(
                            $"Level {number} must have exactly {MinimumRoomCounts[i]} connected room scales.");
                    if (roomSocketCount > movableRoomCount + ordinaryCargoCount + chapterThreeCargoCount)
                        throw new System.InvalidOperationException(
                            $"Level {number} has more cargo/room sockets than movable puzzle objects.");
                }

                int evidence = ReviewedCampaignDifficulty(level, i);
                if (evidence <= previousEvidence)
                    throw new System.InvalidOperationException(
                        $"Level {number} '{level.name}' has difficulty evidence {evidence}, not above " +
                        $"Level {number - 1}'s {previousEvidence}. Redesign or reorder it before generation.");
                previousEvidence = evidence;
            }
        }

        // Pure source-data check used by the Edit Mode generator. Cargo and terrain are ignored
        // because they can only make a route harder; walls and closed green gates are the static
        // topology that must separate the player from every player goal.
        static bool PlayerGoalRequiresGreenGate(LevelDef level)
        {
            if (level?.rooms == null || level.rooms.Length == 0) return false;

            string[] room = level.rooms[0];
            if (room == null || room.Length == 0 || string.IsNullOrEmpty(room[0])) return false;

            Vector2Int start = default;
            bool hasPlayer = false;
            bool hasPlayerGoal = false;
            bool hasButton = false;
            bool hasGate = false;
            for (int y = 0; y < room.Length; y++)
            {
                for (int x = 0; x < room[y].Length; x++)
                {
                    char cell = room[y][x];
                    if (cell == 'P')
                    {
                        start = new Vector2Int(x, y);
                        hasPlayer = true;
                    }
                    else if (cell == 'p') hasPlayerGoal = true;
                    else if (cell == 'B' || cell == '&') hasButton = true;
                    else if (cell == 'G') hasGate = true;
                }
            }

            if (!hasPlayer || !hasPlayerGoal || !hasButton || !hasGate) return false;

            var frontier = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            frontier.Enqueue(start);
            visited.Add(start);
            Vector2Int[] directions =
                { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

            while (frontier.Count > 0)
            {
                Vector2Int cell = frontier.Dequeue();
                if (room[cell.y][cell.x] == 'p') return false;

                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = cell + direction;
                    if (next.y < 0 || next.y >= room.Length
                        || next.x < 0 || next.x >= room[next.y].Length
                        || visited.Contains(next))
                        continue;

                    char nextCell = room[next.y][next.x];
                    if (nextCell == '#' || nextCell == 'G') continue;
                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }

            return true;
        }

        static LevelDef[] ChapterOneFoundations()
        {
            return new[]
            {
                // L01 — one obvious push, then walk to the bright exit. No arrow, trap or gate:
                // the opening board teaches the two basic goal shapes and nothing else.
                new LevelDef
                {
                    name = "The First Commitment",
                    rooms = new[] { new[]
                    {
                        "#######",
                        "#...p.#",
                        "#Pbx..#",
                        "#.....#",
                        "#######"
                    } },
                    par = 4,
                    solution = "RURR"
                },

                // L02 — the reviewed 16-move board is deliberately a wide, visible lane. The
                // player pushes in one direction, then follows the open lower lane to the exit.
                new LevelDef
                {
                    name = "Corner Delivery",
                    rooms = new[] { new[]
                    {
                        "############",
                        "#P.b.....x.#",
                        "#..........#",
                        "#..........#",
                        "#.p........#",
                        "############"
                    } },
                    par = 16,
                    solution = "RRRRRRRDDDLLLLLL"
                },

                // L03 — first positioning lesson: walk behind one crate, push it once, then use
                // the completely open right lane to reach the player goal.
                new LevelDef
                {
                    name = "Two Deliveries",
                    rooms = new[] { new[]
                    {
                        "########",
                        "#....p.#",
                        "#..x...#",
                        "#..b...#",
                        "#P.....#",
                        "########"
                    } },
                    par = 7,
                    solution = "RRURRUU"
                },

                // L04 — the first corner push. A broad loop shows exactly how to reach the useful
                // side of the crate; there is still only one delivery and no irreversible tile.
                new LevelDef
                {
                    name = "Committed Pair",
                    rooms = new[] { new[]
                    {
                        "########",
                        "#...p..#",
                        "#..b...#",
                        "#..xP..#",
                        "#......#",
                        "########"
                    } },
                    par = 6,
                    solution = "UULDRU"
                },

                // L05 — NEW MECHANIC: button and gate. The tutorial plays immediately before this
                // board; one straight push opens the divider and leaves a short route to the exit.
                new LevelDef
                {
                    name = "First Gate",
                    rooms = new[] { new[]
                    {
                        "#########",
                        "#.B.#.p##",
                        "#.b.G..##",
                        "#.P.#..##",
                        "#...#..##",
                        "#########"
                    } },
                    par = 6,
                    solution = "URRRUR"
                },

                // L06 — the same button/gate rule now appears in a visibly different orientation.
                // The cargo is pushed sideways onto the button, then the diver must reverse,
                // cross a horizontal divider and turn again toward the exit. This adds one real
                // route decision over L05 instead of merely extending the same corridor.
                new LevelDef
                {
                    name = "Gate Delivery",
                    rooms = new[] { new[]
                    {
                        "########",
                        "#...p..#",
                        "#G######",
                        "#.bB...#",
                        "#P.....#",
                        "########"
                    } },
                    par = 8,
                    solution = "URLUURRR"
                },

                // L07 — rotate the whole relationship: a horizontal divider separates the board.
                // One crate holds the upper button while a second crosses a clear lower side lane.
                new LevelDef
                {
                    name = "Turn Beyond",
                    rooms = new[] { new[]
                    {
                        "#########",
                        "#P..bB..#",
                        "###G#####",
                        "#...bx..#",
                        "#...p...#",
                        "#########"
                    } },
                    par = 8,
                    solution = "RRRLDDRD"
                },

                // L08 — a different action language: push the holder DOWN, cross from left to
                // right, then push the delivery DOWN onto its visible target before climbing out.
                new LevelDef
                {
                    name = "Double Passage",
                    rooms = new[] { new[]
                    {
                        "###########",
                        "#P..#....p#",
                        "#b..G.....#",
                        "#B..#..b..#",
                        "#...#..x..#",
                        "###########"
                    } },
                    par = 12,
                    solution = "DRRRRRRDRRUU"
                },

                // L09 — the gate moves to the TOP of a tall centre divider. Beyond it, two boxes
                // rise into two matching targets in parallel columns: same rule, new spatial plan.
                new LevelDef
                {
                    name = "Gate Relay",
                    rooms = new[] { new[]
                    {
                        "#############",
                        "#....G.....p#",
                        "#....#.x.x..#",
                        "#P.bB#.b.b..#",
                        "#....#......#",
                        "#....#......#",
                        "#############"
                    } },
                    par = 20,
                    solution = "RRUURRRDDDRUDRRURRUU"
                },

                // L10 — a true chapter review with a new silhouette: complete one LEFT-side job,
                // cross the CENTRE gate, then complete one RIGHT-side vertical job. The two tasks
                // live on opposite sides instead of repeating Level 9's parallel arrangement.
                // The earlier cabinet rule still grants 44 moves, well above this compact proof.
                new LevelDef
                {
                    name = "Foundation Circuit",
                    rooms = new[] { new[]
                    {
                        "#############",
                        "#P.bB#......#",
                        "#....#..x...#",
                        "#....G..b...#",
                        "#xb..#......#",
                        "#....#....p.#",
                        "#############"
                    } },
                    par = 19,
                    solution = "RRDDDLURRRRDRRURRDD"
                },
            };
        }

        // Chapter II starts above Chapter I's final evidence and introduces recursion in readable
        // steps. Every puzzle has a compact silhouette and one dominant room decision.
        static LevelDef[] ChapterTwoInsideTheBox()
        {
            return new[]
            {
                new LevelDef
                {
                    name = "Doorway",
                    rooms = new[]
                    {
                        new[] { "########", "########", "#...####", "#.#..Q##", "#P###.p#", "########" },
                        new[] { "#####", "...##", ".#.##", "##.##", "##.##" },
                    },
                    par = 15,
                    solution = "UURRDRRURRDDDDR"
                },
                new LevelDef
                {
                    name = "Return Path",
                    rooms = new[]
                    {
                        new[] { "#######", "#######", "####..#", "#P..Q.#", "####.##", "##p..##", "#######" },
                        new[] { "##.##", "...##", ".###.", "##...", "##.##" },
                    },
                    par = 19,
                    solution = "RRRURRUURDLDLLDDDLL"
                },
                new LevelDef
                {
                    name = "Pin to Enter",
                    rooms = new[]
                    {
                        new[] { "#######", "#p.P.##", "###1.##", "##..###", "##..###", "#######", "#######" },
                        new[] { "##.##", "...##", ".####", "#####", "#####" },
                    },
                    par = 16,
                    solution = "DDLDRURRUUURULLL"
                },
                new LevelDef
                {
                    name = "Docked Passage",
                    rooms = new[]
                    {
                        new[] { "########", "####..##", "#P.1..##", "##.#.x##", "#p.#####", "########", "########" },
                        new[] { "###.###", "....###", ".######", "#######", "#######", "#######" },
                    },
                    par = 21,
                    solution = "RRRURDLDRURRRUULLLDDL"
                },
                new LevelDef
                {
                    name = "Bring It Outside",
                    rooms = new[]
                    {
                        new[] { "#######", "#.p...#", "#...Q.#", "#P....#", "#....j#", "#######" },
                        new[] { "##.##", "#....", "..J..", "#...#", "##.##" },
                    },
                    par = 19,
                    solution = "URRRRURDDDDLDRULUUL"
                },
                new LevelDef
                {
                    name = "Moving Delivery",
                    rooms = new[]
                    {
                        new[] { "#########", "#.p.....#", "#P.1....#", "#....x.j#", "#....#..#", "#########" },
                        new[] { "##.##", "#...#", "#.J>.", "#...#", "##.##" },
                    },
                    par = 20,
                    solution = "RRRURD" + "DDLDRRRR" + "UULLLL"
                },
                new LevelDef
                {
                    name = "Side Extraction",
                    rooms = new[]
                    {
                        new[] { "#########", "#.....p.#", "#P..#1v.#", "#.j.x...#", "#########" },
                        new[] { "###.###", "#.....#", "#..J..#", ".......", "#.###.#", "#.....#", "###.###" },
                    },
                    par = 28,
                    solution = "URRRRDD" + "DDRD" + "LLLLLL" + "UURRRDDLUUR"
                },
                new LevelDef
                {
                    name = "Turn It Inside",
                    rooms = new[]
                    {
                        new[]
                        {
                            "###############", "#p.#..........#", "#..G..........#",
                            "#..#.P.1......#", "#..#.......x.&#", "#..#.......#..#", "###############",
                        },
                        new[] { "###.###", "#.....#", "#..J..#", ".......", "#.###.#", "#.....#", "###.###" },
                    },
                    par = 32,
                    solution = "RRRRRURDD" + "DDLDRRRRR" + "UU" + "LLLLLLLLLL" + "UL"
                },
                new LevelDef
                {
                    name = "Two Rooms Down",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#############", "#.....#....p#", "#.....>..>..#", "#........1.P#",
                            "#j.x........#", "#..#.....#..#", "#############",
                        },
                        new[] { "###.###", "#.....#", "#.....#", "...U...", "#.###.#", "#.....#", "###.###" },
                        new[] { "###.###", "#.....#", "#..J..#", ".......", "#.###.#", "#.....#", "###.###" },
                    },
                    par = 38,
                    solution = "LLLLLLLULD" + "DDDD" + "DDRDLLLLLLLL" + "UURRRRRRRRRU"
                },
                new LevelDef
                {
                    name = "Three-Space Relay",
                    rooms = new[]
                    {
                        new[]
                        {
                            "###################", "#............#...p#", "#............G.>..#",
                            "#..........P.#....#", "#&..x1.......#....#", "#....#.......#....#", "###################",
                        },
                        new[] { "###.###", "#.....#", "#.....#", "...U...", "#.###.#", "#.....#", "###.###" },
                        new[] { "###.###", "#.....#", "#..J..#", ".......", "#.###.#", "#.....#", "###.###" },
                    },
                    par = 46,
                    solution = "LLLLLL" + "DDDD" + "DDRD" + "LLLLLLLLLL"
                        + "URRRRDLUU" + "RRRRRRRRRRRR" + "U"
                },
            };
        }

        // Unused experimental flat-board replacement retained as a source reference only.
        // Chapter II is a flat-board puzzle chapter, not another recursion chapter. The first four
        // boards reuse Chapter I's orange cargo/button and green gate. Level 15 then introduces
        // coloured cargo: every crate belongs only on the target with the same colour and embossed
        // mark. The final six boards are intentionally different silhouettes and axis patterns.
        static LevelDef[] ChapterTwoFlatExperiment_Unused()
        {
            var levels = new List<LevelDef>
            {
                new LevelDef
                {
                    // Three compact jobs sit behind one held gate. The centre post forces the
                    // first real delivery order without adding an empty walking corridor.
                    name = "Three-Crate Turn",
                    rooms = new[] { new[]
                    {
                        "#########",
                        "#P.bB...#",
                        "##G######",
                        "#.x.x...#",
                        "#.bb....#",
                        "#..#....#",
                        "#.xb..p.#",
                        "#.......#",
                        "#########",
                    } },
                    par = 26,
                    solution = "RRLDDLDDRURURRDDDLLRUURRDD"
                },
                new LevelDef
                {
                    // Three adjacent crates share three offset targets around one central post.
                    // The open lower loop preserves visibility while making push order matter.
                    name = "Central Post",
                    rooms = new[] { new[]
                    {
                        "#########",
                        "#P.bB...#",
                        "##G######",
                        "#.x.x.x.#",
                        "#.bbb...#",
                        "#...#...#",
                        "#.....p.#",
                        "#.......#",
                        "#########",
                    } },
                    par = 30,
                    solution = "RRLDDLDDRRUDLLUURRDDLURRRDRUDD"
                },
                new LevelDef
                {
                    // A fourth job lives in its own visible side bay. The player must finish the
                    // central three-crate sequence and still preserve the approach to that bay.
                    name = "Side Bay",
                    rooms = new[] { new[]
                    {
                        "##########",
                        "#P.bB....#",
                        "##G#######",
                        "#.x.x.x#x#",
                        "#.bbb..#b#",
                        "#...#..#.#",
                        "#.......p#",
                        "#........#",
                        "##########",
                    } },
                    par = 36,
                    solution = "RRLDDLDDRRUDLLUURRDDLURRRDRUDDRRUUDD"
                },
                new LevelDef
                {
                    // Two isolated side bays extend the central three-crate dependency. All five
                    // jobs are compact and visible; difficulty comes from preserving access.
                    name = "Twin Bays",
                    rooms = new[] { new[]
                    {
                        "############",
                        "#P.bB......#",
                        "##G#########",
                        "#.x.x.x#x#x#",
                        "#.bbb..#b#b#",
                        "#...#..#.#.#",
                        "#.........p#",
                        "#..........#",
                        "############",
                    } },
                    par = 42,
                    solution = "RRLDDLDDRRUDLLUURRDDLURRRDRUDDRRUUDDRRUUDD"
                },
            };

            // The last six reviewed colour-factory boards already have explicit authored routes.
            // Reusing those source definitions keeps the new mechanic consistent with its premium
            // colour + embossed-mark rendering while preserving six genuinely different puzzles.
            LevelDef[] colourFactory = ChapterTwoColourFactory();
            for (int i = 4; i < colourFactory.Length; i++)
                levels.Add(colourFactory[i]);
            return levels.ToArray();
        }

        // Source-only Chapter II contract. It checks presentation, mechanic progression and the
        // authored difficulty ladder without opening or playing the game. BuildLevelPrefab later
        // replays each route through the runtime model when the designer runs the menu command.
        static void ValidateFlatChapterTwoDefinitions(LevelDef[] levels)
        {
            if (levels == null || levels.Length != 10)
                throw new System.InvalidOperationException(
                    $"Chapter II must contain exactly ten flat levels; found {levels?.Length ?? 0}.");

            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var layouts = new HashSet<string>(System.StringComparer.Ordinal);
            int previousDifficulty = -1;
            for (int i = 0; i < levels.Length; i++)
            {
                LevelDef level = levels[i];
                int number = 11 + i;
                if (level == null || string.IsNullOrWhiteSpace(level.name) || !names.Add(level.name))
                    throw new System.InvalidOperationException(
                        $"Level {number} needs a unique Chapter II name.");
                if (level.rooms == null || level.rooms.Length != 1)
                    throw new System.InvalidOperationException(
                        $"Level {number} must be one visible board; box-inside-box rooms are forbidden.");
                if (string.IsNullOrEmpty(level.solution) || level.solution.Length != level.par)
                    throw new System.InvalidOperationException(
                        $"Level {number} needs an explicit authored route whose length equals par.");

                string[] room = level.rooms[0];
                if (room == null || room.Length < 6 || room.Length > 14
                    || string.IsNullOrEmpty(room[0]) || room[0].Length < 7 || room[0].Length > 17)
                    throw new System.InvalidOperationException(
                        $"Level {number} is outside Chapter II's readable camera-safe board size.");

                int width = room[0].Length;
                int players = 0, playerGoals = 0, ordinaryCargo = 0, ordinaryGoals = 0;
                int colouredCargo = 0, colouredGoals = 0, buttons = 0, gates = 0;
                var fingerprint = new System.Text.StringBuilder();
                for (int y = 0; y < room.Length; y++)
                {
                    string row = room[y];
                    if (row == null || row.Length != width)
                        throw new System.InvalidOperationException(
                            $"Level {number}, row {y} is not rectangular.");
                    fingerprint.Append(row).Append('/');
                    foreach (char cell in row)
                    {
                        bool allowed = cell == '#' || cell == '.' || cell == 'P' || cell == 'p'
                            || cell == 'b' || cell == 'x' || cell == 'B' || cell == 'G'
                            || cell == 'J' || cell == 'j' || cell == 'N' || cell == 'n'
                            || cell == 'Z' || cell == 'z';
                        if (!allowed)
                            throw new System.InvalidOperationException(
                                $"Level {number} uses unrelated mechanic '{cell}'.");
                        if (cell == 'P') players++;
                        else if (cell == 'p') playerGoals++;
                        else if (cell == 'b') ordinaryCargo++;
                        else if (cell == 'x') ordinaryGoals++;
                        else if (cell == 'J' || cell == 'N' || cell == 'Z') colouredCargo++;
                        else if (cell == 'j' || cell == 'n' || cell == 'z') colouredGoals++;
                        else if (cell == 'B') buttons++;
                        else if (cell == 'G') gates++;
                    }
                }

                if (!layouts.Add(fingerprint.ToString()))
                    throw new System.InvalidOperationException(
                        $"Level {number} repeats another Chapter II layout.");
                if (players != 1 || playerGoals != 1)
                    throw new System.InvalidOperationException(
                        $"Level {number} needs exactly one player and one player target.");

                if (i < 4)
                {
                    if (buttons != 1 || gates < 1 || ordinaryCargo != ordinaryGoals + 1
                        || ordinaryGoals < 2 || colouredCargo != 0 || colouredGoals != 0)
                        throw new System.InvalidOperationException(
                            $"Level {number} must reuse one Chapter I button/gate hold plus every visible cargo job.");
                }
                else if (buttons != 0 || gates != 0 || ordinaryCargo != 0 || ordinaryGoals != 0
                         || colouredCargo < 4 || colouredCargo != colouredGoals)
                {
                    throw new System.InvalidOperationException(
                        $"Level {number} must use four or more complete colour-and-mark cargo matches.");
                }

                int difficulty = EasyInsideTheBoxDifficulty(level);
                if (i == 0 && level.par <= ChapterOneFoundations()[9].par)
                    throw new System.InvalidOperationException(
                        "Level 11 must start above Chapter I's reviewed finale route and task load.");
                if (difficulty <= previousDifficulty)
                    throw new System.InvalidOperationException(
                        $"Level {number} planning evidence {difficulty} must exceed the previous "
                        + $"Chapter II level's {previousDifficulty} without padding movement.");
                previousDifficulty = difficulty;
            }
        }

        static int DistinctChapterTwoColours(string[] room)
        {
            bool coral = false, sky = false, green = false;
            foreach (string row in room)
                foreach (char cell in row)
                {
                    coral |= cell == 'J' || cell == 'j';
                    sky |= cell == 'N' || cell == 'n';
                    green |= cell == 'Z' || cell == 'z';
                }
            return (coral ? 1 : 0) + (sky ? 1 : 0) + (green ? 1 : 0);
        }

        // Complete colour-matching source set. Chapter II uses the six reviewed mastery boards
        // from index four onward after its dedicated Level 15 tutorial.
        static LevelDef[] ChapterTwoColourFactory()
        {
            return new[]
            {
                // TEACH — the two hues are separated and immediately readable. Coral travels up
                // to coral, sky travels right to sky, then the diver takes the independent exit.
                new LevelDef
                {
                    name = "First Match",
                    rooms = new[] { new[]
                    {
                        "#########",
                        "#j.....p#",
                        "#.......#",
                        "#J...#..#",
                        "#P...Nn.#",
                        "#########"
                    } },
                    par = 15,
                    solution = "UURRRDDRLUUURRR"
                },

                // PRACTICE — a tall, narrow floor and one central post force the player to circle
                // around the delivery. This is a new silhouette and the first meaningful choice
                // of pushing side, without introducing another rule.
                new LevelDef
                {
                    name = "Around the Post",
                    rooms = new[] { new[]
                    {
                        "#######",
                        "#j...p#",
                        "#.....#",
                        "#J.#..#",
                        "#..#Nn#",
                        "#.....#",
                        "#P....#",
                        "#######"
                    } },
                    par = 23,
                    solution = "URRRUUDDLLULUURRRURDDUU"
                },

                // EXPAND — reef green joins coral and sky. Each colour travels on a different
                // line, so the player learns the full visual language before ordering becomes the
                // challenge.
                new LevelDef
                {
                    name = "Three Colours",
                    rooms = new[] { new[]
                    {
                        "##########",
                        "#j......p#",
                        "#........#",
                        "#J..#....#",
                        "#...#Nn..#",
                        "#.Z....z.#",
                        "#P..#....#",
                        "##########"
                    } },
                    par = 30,
                    solution = "UUUUDDDRRRRRLUURUULLDRURDDUURR"
                },

                // REORIENT — the three deliveries now point in different directions around two
                // offset braces. The extra difficulty is planning the approach side, not a larger
                // empty board.
                new LevelDef
                {
                    name = "Crossed Orders",
                    rooms = new[] { new[]
                    {
                        "#########",
                        "#j.....p#",
                        "#...#...#",
                        "#J..#Nn.#",
                        "#.......#",
                        "#z.Z.#..#",
                        "#P......#",
                        "#########"
                    } },
                    par = 33,
                    solution = "UUUURURRRDDRDLLDLLURRRURRDDLURUUU"
                },

                // SCALE — four vertical loading bays make every completed colour visible at once.
                // This is intentionally regular: it is the last execution-focused board before
                // the factory begins mixing axes and access routes.
                new LevelDef
                {
                    name = "Four Loading Bays",
                    rooms = new[] { new[]
                    {
                        "###########",
                        "#j#j#n#z#p#",
                        "#.#.#.#.#.#",
                        "#.#.#.#.#.#",
                        "#J#J#N#Z#.#",
                        "#.........#",
                        "#P........#",
                        "###########"
                    } },
                    par = 37,
                    solution = "UUUUDDDRRUUUDDDRRUUUDDDRRUUUDDDRRUUUU"
                },

                // COMBINE — two colours rise through vertical shafts while two others cross
                // horizontal rails. The player has to use the shared return spine and cannot
                // solve the board as four identical repetitions.
                new LevelDef
                {
                    name = "Split Shift",
                    rooms = new[] { new[]
                    {
                        "###########",
                        "#j#n#....p#",
                        "#.#.#.#####",
                        "#J#N#.....#",
                        "#.#.#.#####",
                        "#.Z.....z##",
                        "#.#.#######",
                        "#.J.....j##",
                        "#.#.#######",
                        "#P........#",
                        "###########"
                    } },
                    par = 44,
                    solution = "UURRRRRRLLLLLLUUUUUDDDRRUUUDDDRRRRLLUUUURRRR"
                },

                // TWIST — alternating left and right orders share two vertical spines. Solving a
                // row leaves the player on the opposite side of the factory, so route order now
                // matters as much as matching the colour.
                new LevelDef
                {
                    name = "Alternating Lines",
                    rooms = new[] { new[]
                    {
                        "########",
                        "#j.J..p#",
                        "#.####.#",
                        "#n.N...#",
                        "#.####.#",
                        "#...Zz.#",
                        "#.####.#",
                        "#...Jj.#",
                        "#.####.#",
                        "#P.....#",
                        "########"
                    } },
                    par = 49,
                    solution = "UUUURRRLLLDDRRRLLLDDRRRRRUUUUUULLLLRRRRUULLLLRRRR"
                },

                // MASTERY — four bays start at different depths, creating a visible staircase.
                // The player must remember how far each colour still has to travel while using one
                // shared lower aisle.
                new LevelDef
                {
                    name = "Staggered Factory",
                    rooms = new[] { new[]
                    {
                        "###########",
                        "#j#n#z#j#p#",
                        "#.#.#.#.#.#",
                        "#J#.#.#.#.#",
                        "#.#N#.#.#.#",
                        "#.#.#Z#.#.#",
                        "#.#.#.#J#.#",
                        "#.........#",
                        "#P........#",
                        "###########"
                    } },
                    par = 55,
                    solution = "UUUUUUDDDDDRRUUUUUDDDDDRRUUUUUDDDDDRRUUUUUDDDDDRRUUUUUU"
                },

                // MASTERY — five deliveries radiate from one sorting cross. The outer targets are
                // easy to read, but every delivery changes which side of the cross the player can
                // use next.
                new LevelDef
                {
                    name = "Five-Way Sort",
                    rooms = new[] { new[]
                    {
                        "#############",
                        "######j######",
                        "######.######",
                        "######.######",
                        "######.######",
                        "######.######",
                        "######J######",
                        "#n..N...Z..z#",
                        "#...........#",
                        "#j..J.P.N..n#",
                        "######.######",
                        "######.######",
                        "######p######",
                        "#############"
                    } },
                    par = 57,
                    solution = "LLLLURRRRDRRRRULLLLDUULLLLDRRRRURRRRDLLLLUUUUUUDDDDDDDDDD"
                },

                // FINALE — four crates enter one packing tower. The colour sequence is visible on
                // the tower wall, and a wrong delivery order blocks the next matching target. This
                // is the chapter's discovery moment: colour is now an ordering constraint.
                new LevelDef
                {
                    name = "Packing Order",
                    rooms = new[] { new[]
                    {
                        "#############",
                        "#.....j.....#",
                        "#.....n.....#",
                        "#.....z.....#",
                        "#.....j.....#",
                        "#...........#",
                        "#..N..J..Z..#",
                        "#..J........#",
                        "#.....P....p#",
                        "#############"
                    } },
                    par = 61,
                    solution = "UUUUUULLLLDDDDRRRDRUUUURRRRDDDLLLDLUUULLLLDDDRRRDRUUUDDDRRRRR"
                },
            };
        }

        static LevelDef[] ChapterThreeSynergy()
        {
            var levels = new[]
            {
                // TEACH: a compact horizontal relay. Cargo crosses the room, the room is lowered
                // onto its socket, and the player uses the solved room to reach a separate exit.
                new LevelDef
                {
                    name = "Cargo Through the Room",
                    rooms = new[]
                    {
                        new[] { "#########", "#..#.#..#", "#PJ1.#..#", "#....#..#", "#...x.j.#", "#...#..p#", "#########" },
                        new[] { "###.###", "#.....#", "#.....#", ".......", "#.....#", "#.....#", "###.###" }
                    },
                    par = 23,
                    solution = "RRRRRRUUUUDDDDDDRRRRDRR"
                },

                // PRACTICE: approach the room from the opposite side, relay the amber cargo
                // through it, then finish in the sealed lower pocket. Every row is rectangular
                // and the room has a readable pinned entry instead of an ambiguous movable stop.
                new LevelDef
                {
                    name = "Reverse Entry",
                    rooms = new[]
                    {
                        new[] { "#########", "#...j...#", "#....#..#", "#..#1JP.#", "#..#.#..#", "#.#px...#", "#..##...#", "#########" },
                        new[] { "####.####", "#.......#", "#.......#", ".........", "#..#....#", "#.......#", "#########" }
                    },
                    par = 27,
                    solution = "URDLLLLLLDLUUUUUDDDDDDLLLLL"
                },

                // PRACTICE: the room is approached from the opposite side and its two braces
                // force a deliberate inner loop before the cargo can leave through the roof.
                new LevelDef
                {
                    name = "Turn Inside",
                    rooms = new[]
                    {
                        new[] { "##########", "#..j..p..#", "#..#.##..#", "#..#.1JP.#", "#..#.##..#", "#...x....#", "#........#", "##########" },
                        new[] { "###.###", "#.....#", "#...#.#", ".......", "#.#.#.#", "#.....#", "#######" }
                    },
                    par = 29,
                    solution = "LLLLLRDDLLUUUUUUDDURRRUULLLRR"
                },

                // EXPERIMENT: a tall S-shaped outer board. Cargo enters from above, leaves from
                // the right, and the room itself moves left before the isolated floor exit opens.
                new LevelDef
                {
                    name = "Side Exit",
                    rooms = new[]
                    {
                        new[] { "########", "#......#", "#...P..#", "#..#J..#", "##x.1.j#", "##.##..#", "##.#####", "##...###", "####p###", "########" },
                        new[] { "####.####", "#.......#", "#..#....#", "#........", "#....#..#", "#.......#", "####.####" }
                    },
                    par = 32,
                    solution = "DDDDULLDDRRRRRRRLLLLDDLLLDDDDRRD"
                },

                // EXPERIMENT: a wide chamber with offset gates. Its longer horizontal transfer is
                // broken by two vertical decisions, so it cannot be solved with Level 24's rhythm.
                new LevelDef
                {
                    name = "Inner Pillar",
                    rooms = new[]
                    {
                        new[] { "##########", "#...##...#", "#...x....#", "#....#...#", "#..#1JP..#", "#....#...#", "#...j#..p#", "##########" },
                        new[] { "########.########", "#...............#", "#...............#", "#..#............#", "#...............#", ".................", "##############.##", "#...............#", "#...............#", "#####.###########", "#####.###########" }
                    },
                    par = 34,
                    solution = "LLLULDDDRDLLLLLLLLLULDDDDUUDRRRRDD"
                },

                // COMBINE: the first true nested chain. The amber cargo must enter the parent,
                // cross its child room, return through both boundaries and only then can the
                // outer room socket and player target be completed. All three entry cells are
                // visibly open, so the puzzle cannot become blocked by a decorative wall seam.
                new LevelDef
                {
                    name = "Two Rooms Deep",
                    rooms = new[]
                    {
                        new[] { "#########", "#...j...#", "#.......#", "#.......#", "#..#....#", "#.PJ1#..#", "#..#.#..#", "#...xp#.#", "#...##..#", "#########" },
                        new[] { "###.###", "#.....#", "#.....#", "...U#..", "#.#...#", "#.....#", "#######" },
                        new[] { "##.##", "#...#", ".....", "#...#", "##.##" }
                    },
                    par = 41,
                    solution = "ULDRULDRULDRRRRRRRDRUUUUUUUUUDDDDDDRRDDRR"
                },

                // COMBINE: a sibling relay across an outer sorting rail. The cargo leaves the
                // movable room, crosses the root, enters an anchored room and exits below it.
                new LevelDef
                {
                    name = "Long Inner Relay",
                    rooms = new[]
                    {
                        new[] { "#########", "###.##P##", "###...J.#", "###x..1.#", "###.#####", "###.#####", "#...#####", "#j.U#####", "#...#####", "#...#####", "###.#####", "#...#####", "#p..#####", "#########" },
                        new[] { "###.###", "#.....#", "#.#...#", ".......", "#.....#", "#.....#", "###.###" },
                        new[] { "###.###", "#.....#", "#.....#", "....#.#", "#.....#", "#.....#", "###.###" }
                    },
                    par = 44,
                    solution = "DDDDDRRRRLLLLLLLDDDDDDDDRDLLULDRDLLLDDRDDDLL"
                },

                // TWIST: a vertical nested transfer. The deepest pillar forces a side switch;
                // outside, the first room must be repositioned repeatedly before both goals align.
                new LevelDef
                {
                    name = "Deep Corner",
                    rooms = new[]
                    {
                        new[] { "#########", "#.p..j..#", "#...#...#", "#x......#", "####1.###", "#...J####", "#...P...#", "#########" },
                        new[] { "#######", "#.....#", "#.....#", "...U...", "###.###", "###.###", "###.###" },
                        new[] { "#########", "#.......#", ".........", "#...#...#", "#.......#", "#.......#", "####.####" }
                    },
                    par = 45,
                    solution = "UUUUUUURULLDLUURULLLLLLLULLDRRRDRUURRDLLLLLUU"
                },

                // MASTERY: two movable sibling rooms must both be docked. Cargo is transferred
                // through the first and then the second before a final two-axis delivery.
                new LevelDef
                {
                    name = "Return Pocket",
                    rooms = new[]
                    {
                        new[] { "###########", "##p.j######", "#....######", "#....######", "##x2...x..#", "#######..##", "#######..##", "#######1JP#", "#######..##", "###########" },
                        new[] { "###.###", "#.....#", "#...#.#", ".......", "#.....#", "#.....#", "###.###" },
                        new[] { "######.######", "#...........#", "#...........#", "#...#.......#", "......#......", "#######..####", "#...........#", "#...........#", "######.######" }
                    },
                    par = 51,
                    solution = "LLLLLDDDDUUUUUUULLLLLLLLLLLLDLUUURULDLUUULURDRULURL"
                },

                // CHAPTER MASTERY: a four-space hybrid relay. Cargo crosses a movable room, an
                // anchored room and its child, while the root path changes axis between transfers.
                new LevelDef
                {
                    name = "Rooms Within Rooms",
                    rooms = new[]
                    {
                        new[] { "#############", "#PJ1....x####", "########.####", "########.####", "####..##..###", "#.........###", "####.########", "####U.#######", "####j.p######", "#############" },
                        new[] { "###.###", "#.....#", "#..#..#", ".......", "#.....#", "#.....#", "###.###" },
                        new[] { "###.###", "###.###", "###.###", "...V...", "#.....#", "#.....#", "###.###" },
                        new[] { "####.####", "#.......#", "#.......#", "#...#...#", "#.......#", "#.......#", "####.####" }
                    },
                    par = 64,
                    solution = "RRRRRRRRURDDLDRURDDDDDRDLLLLULDDDDDDDRDLULDDDLDRURDDDDDUURURRRDR"
                },
            };

            // The premium cyan portal is introduced immediately before Level 25, then remains a
            // required part of every Chapter III solve through Level 30. Replacing the original
            // player goal with a sealed paired exit preserves each reviewed route and adds one
            // final portal step without changing the room/cargo work that makes the levels unique.
            for (int chapterIndex = 4; chapterIndex < levels.Length; chapterIndex++)
                AddMandatoryPortalExit(levels[chapterIndex], 1);
            // Level 29 finishes by emerging from a recursive room directly onto the former goal
            // cell. That transition intentionally bypasses ordinary floor effects, so step right,
            // re-enter the cyan portal from the board, then step onto the sealed target.
            levels[8].solution = levels[8].solution.Substring(0, levels[8].solution.Length - 1)
                + "RLR";
            levels[8].par += 2;
            return levels;
        }

        // Premium Chapter IV keeps the original deep-room interactions and authored routes. The
        // LevelLayoutRebalancer adds only route-proven commitments on top of these same puzzles;
        // it does not replace their room language. Levels 35-40 move the player target into a
        // sealed cyan-portal pocket after the original room solution reaches its final cell.
        static LevelDef[] ChapterFourPremiumRooms()
        {
            LevelDef[] source = ChapterFiveRecursion();
            string[] names =
            {
                "Convoy Bridge",
                "Four-Corner Dock",
                "Nested Extraction",
                "Inward Manifest",
                "Portal Chamber",
                "Portal Boundary Relay",
                "Twin-Space Portal",
                "Portal Exit Side",
                "Abyss Return",
                "Event Horizon Relay",
            };
            int[] sourceIndices = { 0, 1, 2, 3, 4, 5, 7, 8, 9 };
            int[] exitDistances = { 0, 0, 0, 0, 1, 1, 2, 2, 1, 3 };

            var result = new LevelDef[10];
            for (int i = 0; i < result.Length; i++)
            {
                // The retired source[6] proof blocks under the current recursive transfer rules
                // and leaves its colour manifest unfinished. Do not ship or cosmetically patch a
                // broken puzzle. Levels 37-39 advance to the next three solver-proven maneuvers;
                // Level 40 rotates the deepest branching board into a new approach-side finale.
                LevelDef sourceLevel = i < sourceIndices.Length
                    ? source[sourceIndices[i]]
                    : RotateLevelClockwise(source[9]);
                result[i] = CloneLevelDefinition(sourceLevel);
                result[i].name = names[i];
                if (exitDistances[i] > 0)
                    AddMandatoryPortalExit(result[i], exitDistances[i]);
                result[i].designComplexity = ChapterFourPremiumDifficulty(result[i]);
            }
            return result;
        }

        static LevelDef CloneLevelDefinition(LevelDef source)
        {
            if (source == null || source.rooms == null)
                throw new System.InvalidOperationException("Cannot clone an empty level definition.");
            var rooms = new string[source.rooms.Length][];
            for (int room = 0; room < source.rooms.Length; room++)
                rooms[room] = (string[])source.rooms[room].Clone();
            return new LevelDef
            {
                name = source.name,
                rooms = rooms,
                par = source.par,
                solution = source.solution,
                authoredOrder = source.authoredOrder,
                designComplexity = source.designComplexity,
            };
        }

        // Some recursive source boards contain a deep room that is entered but never docked. When
        // that board is promoted into Chapter V, keep the chamber and its full interior puzzle,
        // but encode the reference as anchored so the runtime and the validator agree that it is
        // an entry chamber rather than decorative movable cargo. This clones the source first, so
        // Chapter IV and every already-authored earlier chapter remain completely untouched.
        static LevelDef AnchorRoomReference(LevelDef source, int roomId, char anchoredSymbol)
        {
            if (roomId < 1 || roomId > 9)
                throw new System.ArgumentOutOfRangeException(nameof(roomId));
            if (!AnchoredBoxes.TryGetValue(anchoredSymbol, out int mappedRoom)
                || mappedRoom != roomId)
                throw new System.InvalidOperationException(
                    $"'{anchoredSymbol}' is not registered as anchored Room {roomId}.");

            LevelDef result = CloneLevelDefinition(source);
            char movableSymbol = (char)('0' + roomId);
            int replacements = 0;
            for (int room = 0; room < result.rooms.Length; room++)
            {
                for (int row = 0; row < result.rooms[room].Length; row++)
                {
                    char[] cells = result.rooms[room][row].ToCharArray();
                    for (int column = 0; column < cells.Length; column++)
                    {
                        if (cells[column] != movableSymbol) continue;
                        cells[column] = anchoredSymbol;
                        replacements++;
                    }
                    result.rooms[room][row] = new string(cells);
                }
            }

            if (replacements != 1)
                throw new System.InvalidOperationException(
                    $"Expected one movable reference to Room {roomId}; found {replacements}.");
            return result;
        }

        // Chapter V stays in Chapter IV's room-inside-a-room language, but it must not look like
        // a replay of the same ten boards. These symmetry helpers preserve every authored push,
        // room transition and delivery while changing the approach side and all directional
        // commitments. The finale then adds its own portal, cargo-hold and one-way synthesis.
        static LevelDef MirrorLevelHorizontally(LevelDef source)
        {
            LevelDef result = CloneLevelDefinition(source);
            for (int room = 0; room < result.rooms.Length; room++)
            {
                for (int row = 0; row < result.rooms[room].Length; row++)
                {
                    string original = result.rooms[room][row];
                    var mirrored = new char[original.Length];
                    for (int column = 0; column < original.Length; column++)
                        mirrored[column] = MirrorHorizontalCell(original[original.Length - 1 - column]);
                    result.rooms[room][row] = new string(mirrored);
                }
            }
            result.solution = SwapRouteDirections(result.solution, 'L', 'R');
            return result;
        }

        static LevelDef MirrorLevelVertically(LevelDef source)
        {
            LevelDef result = CloneLevelDefinition(source);
            for (int room = 0; room < result.rooms.Length; room++)
            {
                System.Array.Reverse(result.rooms[room]);
                for (int row = 0; row < result.rooms[room].Length; row++)
                {
                    char[] mirrored = result.rooms[room][row].ToCharArray();
                    for (int column = 0; column < mirrored.Length; column++)
                        mirrored[column] = MirrorVerticalCell(mirrored[column]);
                    result.rooms[room][row] = new string(mirrored);
                }
            }
            result.solution = SwapRouteDirections(result.solution, 'U', 'D');
            return result;
        }

        static LevelDef RotateLevelClockwise(LevelDef source)
        {
            LevelDef result = CloneLevelDefinition(source);
            for (int room = 0; room < result.rooms.Length; room++)
            {
                string[] original = result.rooms[room];
                int oldHeight = original.Length;
                int oldWidth = original[0].Length;
                var rotated = new string[oldWidth];
                for (int row = 0; row < oldWidth; row++)
                {
                    var cells = new char[oldHeight];
                    for (int column = 0; column < oldHeight; column++)
                        cells[column] = RotateClockwiseCell(
                            original[oldHeight - 1 - column][row]);
                    rotated[row] = new string(cells);
                }
                result.rooms[room] = rotated;
            }

            if (!string.IsNullOrEmpty(result.solution))
            {
                char[] route = result.solution.ToCharArray();
                for (int i = 0; i < route.Length; i++)
                {
                    switch (route[i])
                    {
                        case 'U': route[i] = 'R'; break;
                        case 'R': route[i] = 'D'; break;
                        case 'D': route[i] = 'L'; break;
                        case 'L': route[i] = 'U'; break;
                    }
                }
                result.solution = new string(route);
            }
            return result;
        }

        static char MirrorHorizontalCell(char cell)
        {
            if (cell == '<') return '>';
            if (cell == '>') return '<';
            if (cell == 'a') return 'd';
            if (cell == 'd') return 'a';
            return cell;
        }

        static char MirrorVerticalCell(char cell)
        {
            if (cell == '^') return 'v';
            if (cell == 'v') return '^';
            if (cell == 'w') return 's';
            if (cell == 's') return 'w';
            return cell;
        }

        static char RotateClockwiseCell(char cell)
        {
            switch (cell)
            {
                case '^': return '>';
                case '>': return 'v';
                case 'v': return '<';
                case '<': return '^';
                case 'w': return 'd';
                case 'd': return 's';
                case 's': return 'a';
                case 'a': return 'w';
                default: return cell;
            }
        }

        static string SwapRouteDirections(string route, char first, char second)
        {
            if (string.IsNullOrEmpty(route)) return route;
            char[] result = route.ToCharArray();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] == first) result[i] = second;
                else if (result[i] == second) result[i] = first;
            }
            return new string(result);
        }

        // Replace the original player target with one portal, then append an entirely sealed
        // corridor containing the paired portal and the new target. With no floor connection
        // between the old board and this pocket, teleporting is structurally mandatory.
        static void AddMandatoryPortalExit(LevelDef level, int targetDistance)
        {
            if (level?.rooms == null || targetDistance < 1)
                throw new System.ArgumentOutOfRangeException(nameof(targetDistance));

            int goalRoom = -1;
            int goalRow = -1;
            int goalColumn = -1;
            for (int room = 0; room < level.rooms.Length; room++)
                for (int row = 0; row < level.rooms[room].Length; row++)
                {
                    int column = level.rooms[room][row].IndexOf('p');
                    if (column < 0) continue;
                    if (goalRoom >= 0)
                        throw new System.InvalidOperationException(
                            level.name + " has more than one player target.");
                    goalRoom = room;
                    goalRow = row;
                    goalColumn = column;
                }
            if (goalRoom < 0)
                throw new System.InvalidOperationException(level.name + " has no player target.");

            string[] original = level.rooms[goalRoom];
            int originalWidth = original[0].Length;
            int pocketWidth = targetDistance + 3;
            int pocketRow = original.Length > 3 ? 1 : original.Length / 2;
            string wallSuffix = new string('#', pocketWidth);
            string pocketSuffix = "#o" + new string('.', targetDistance - 1) + "p#";
            var expanded = new string[original.Length];
            for (int row = 0; row < original.Length; row++)
            {
                string left = original[row];
                if (row == goalRow)
                {
                    char[] cells = left.ToCharArray();
                    cells[goalColumn] = 'o';
                    left = new string(cells);
                }
                expanded[row] = left + (row == pocketRow ? pocketSuffix : wallSuffix);
            }

            if (expanded[0].Length != originalWidth + pocketWidth)
                throw new System.InvalidOperationException(level.name + " portal pocket width is invalid.");
            level.rooms[goalRoom] = expanded;
            level.solution += new string('R', targetDistance);
            level.par += targetDistance;
        }

        // Chapter V always presents the sealed finish beside the outer chamber, matching the
        // approved Level 42 composition. The source goal may live several rooms deep; replacing
        // that goal with the first portal must not resize its child room, because room dimensions
        // are part of recursive movement. Only Room 0 receives the isolated portal pocket.
        static void AddMandatoryChapterFivePortalExit(LevelDef level, int targetDistance)
        {
            if (level?.rooms == null || level.rooms.Length < 1 || targetDistance < 1)
                throw new System.ArgumentOutOfRangeException(nameof(targetDistance));

            int goalRoom = -1;
            int goalRow = -1;
            int goalColumn = -1;
            for (int room = 0; room < level.rooms.Length; room++)
                for (int row = 0; row < level.rooms[room].Length; row++)
                {
                    int column = level.rooms[room][row].IndexOf('p');
                    if (column < 0) continue;
                    if (goalRoom >= 0)
                        throw new System.InvalidOperationException(
                            level.name + " has more than one player target.");
                    goalRoom = room;
                    goalRow = row;
                    goalColumn = column;
                }
            if (goalRoom < 0)
                throw new System.InvalidOperationException(level.name + " has no player target.");

            // Keep the deep goal room exactly the same size: only change its target into Portal A.
            char[] goalCells = level.rooms[goalRoom][goalRow].ToCharArray();
            goalCells[goalColumn] = 'o';
            level.rooms[goalRoom][goalRow] = new string(goalCells);

            // Portal B and the new target occupy a completely sealed one-cell pocket attached to
            // the outer presentation board. No floor connection exists between the pocket and the
            // chamber, so teleporting remains mandatory without distorting any nested room.
            string[] outer = level.rooms[0];
            int originalWidth = outer[0].Length;
            int pocketWidth = targetDistance + 3;
            int pocketRow = outer.Length > 3 ? 1 : outer.Length / 2;
            string wallSuffix = new string('#', pocketWidth);
            string pocketSuffix = "#o" + new string('.', targetDistance - 1) + "p#";
            var expanded = new string[outer.Length];
            for (int row = 0; row < outer.Length; row++)
                expanded[row] = outer[row] + (row == pocketRow ? pocketSuffix : wallSuffix);

            if (expanded[0].Length != originalWidth + pocketWidth)
                throw new System.InvalidOperationException(
                    level.name + " Chapter V portal pocket width is invalid.");
            level.rooms[0] = expanded;
            level.solution += new string('R', targetDistance);
            level.par += targetDistance;
        }

        static int ChapterFourPremiumDifficulty(LevelDef level)
        {
            int tasks = 0;
            int objects = 0;
            int portals = 0;
            if (level?.rooms != null)
                foreach (string[] room in level.rooms)
                    foreach (string row in room)
                        foreach (char cell in row)
                        {
                            if (cell == 'p' || cell == 'x' || cell == '&'
                                || ColourGoals.ContainsKey(cell)
                                || ColourCargoOnGoals.ContainsKey(cell)) tasks++;
                            if (CrateKinds.ContainsKey(cell)
                                || ColourCargoOnGoals.ContainsKey(cell)
                                || (cell >= '1' && cell <= '9')
                                || AnchoredBoxes.ContainsKey(cell)) objects++;
                            if (cell == 'o') portals++;
                        }

            return 2300
                   + level.par * 70
                   + DirectionChanges(level.solution) * 4
                   + (level?.rooms?.Length ?? 0) * 45
                   + tasks * 30
                   + objects * 15
                   + (portals == 2 ? 100 : 0);
        }

        static LevelDef[] ChapterFourRoomManeuvers()
        {
            return new[]
            {
                // TEACH: the cobalt room is pinned against the right wall, so pushing enters it.
                // Leaving through its lower doorway reaches the far side; only then can the player
                // circle above, push the room down onto its socket and reach the separate exit.
                new LevelDef
                {
                    name = "Pinned Passage",
                    rooms = new[]
                    {
                        new[] { "#######", "###...#", "#P.1#p#", "###...#", "###x###", "#######" },
                        new[] { "#####", "#...#", ".....", "#...#", "##.##" }
                    },
                    par = 18,
                    solution = "RRRRDDDRRUULLDDRRU"
                },

                // PRACTICE: the relationship rotates. Enter from above, leave on the right, push
                // the room left twice, then re-enter the docked room and leave through its floor.
                new LevelDef
                {
                    name = "Opposite Exit",
                    rooms = new[]
                    {
                        new[] { "#######", "###P###", "#x.1..#", "#.#####", "#p#####", "#######" },
                        new[] { "###.###", "#...#.#", "#.#...#", "#.#....", "#...#.#", "#.#...#", "###.###" }
                    },
                    par = 19,
                    solution = "DDDRRDRRLLLLDDLLDDD"
                },

                // PRACTICE: push down to the staging rail, enter when the room hits its stop,
                // leave on the left, then turn the room ninety degrees and dock it on the right.
                new LevelDef
                {
                    name = "Turn the Room",
                    rooms = new[]
                    {
                        new[] { "#######", "###P###", "###1###", "###.#p#", "##...x#", "#######" },
                        new[] { "###.###", "#...###", "#.#...#", "...##.#", "#.....#", "#######", "#######" }
                    },
                    par = 20,
                    solution = "DDDDLLDDLLRRRRUURRUU"
                },

                // EXPERIMENT: after the corner dock, the solved room remains the only doorway to
                // the player pocket. A second entry is required; parking the room is not the end.
                new LevelDef
                {
                    name = "Dock and Re-enter",
                    rooms = new[]
                    {
                        new[] { "#########", "#..P....#", "#..1#...#", "###.#####", "#.....x##", "######.##", "######p##", "#########" },
                        new[] { "###.###", "#...###", "#.#...#", "...##.#", "#...#.#", "#.....#", "###.###" }
                    },
                    par = 22,
                    solution = "DDDDLLDDLLRRRRRRDRDDDD"
                },

                // EXPERIMENT: two independent rooms face different directions. The first becomes
                // a vertical route; the second must be side-docked before it opens the final bay.
                new LevelDef
                {
                    name = "Crossing Rooms",
                    rooms = new[]
                    {
                        new[] { "#########", "#.#######", "#1.P#####", "#......##", "#x....2x#", "#######.#", "#######p#", "#########" },
                        new[] { "##.##", "#...#", ".....", "#...#", "#####" },
                        new[] { "###.###", "#...###", "#.#...#", "...##.#", "#...#.#", "#.....#", "###.###" }
                    },
                    par = 24,
                    solution = "LLLULUUDDRRRRDRRRRDRDDDD"
                },

                // COMBINE: solve two separated docks in order. Each room is entered from one side
                // and left from another, so neither half reuses the previous command rhythm.
                new LevelDef
                {
                    name = "Two Room Relay",
                    rooms = new[]
                    {
                        new[] { "#########", "###P#####", "#x.1....#", "#.#####.#", "#.....2x#", "#######.#", "#######p#", "#########" },
                        new[] { "##.##", "#...#", "#....", "#...#", "##.##" },
                        new[] { "#####", "#...#", ".....", "#...#", "##.##" }
                    },
                    par = 27,
                    solution = "DDRDRRLLLLDLDDDRRRRRRRRDDDD"
                },

                // COMBINE: the inner room is now itself movable. Dock it inside Room 1, climb back
                // through the parent, and only then reposition the outer room on the main board.
                new LevelDef
                {
                    name = "Room Within a Room",
                    rooms = new[]
                    {
                        new[] { "#######", "###...#", "#P.1#p#", "###...#", "###x###", "#######" },
                        new[] { "#######", "###x###", "###.###", "...2#.#", "###...#", "###...#", "###.###" },
                        new[] { "#####", "#...#", ".....", "#...#", "##.##" }
                    },
                    par = 28,
                    solution = "RRRRRRRDDDUUDDDDDRRUULLDDRRU"
                },

                // TWIST: one branch contains a nested docking task while a separate sibling room
                // waits outside. The player must finish the inner branch, dock its parent, then
                // cross the outer rail and enter the second room from below.
                new LevelDef
                {
                    name = "Branch and Nest",
                    rooms = new[]
                    {
                        new[] { "###########", "###....x.p#", "#P.1#..3###", "###.....###", "###x#######", "###########" },
                        new[] { "##x##", "##.##", "..2#.", "##...", "##.##" },
                        new[] { "###", "...", "#.#" },
                        new[] { "#####", "#...#", "#....", "#...#", "##.##" }
                    },
                    par = 33,
                    solution = "RRRRRDDUUDDDDRRUULLDDRRRRUUUURRRR"
                },

                // MASTERY: three movable rooms form a real containment chain. Each child is
                // docked before the player can return one scale outward and solve its parent.
                new LevelDef
                {
                    name = "Three Rooms Deep",
                    rooms = new[]
                    {
                        new[] { "#######", "###...#", "#P.1#p#", "###...#", "###x###", "#######" },
                        new[] { "#######", "###x###", "###.###", "...2#.#", "###...#", "###...#", "###.###" },
                        new[] { "#######", "###x###", "###.###", "...3#.#", "###...#", "###...#", "###.###" },
                        new[] { "#####", "#...#", ".....", "#...#", "##.##" }
                    },
                    par = 38,
                    solution = "RRRRRRRRRRDDDUUDDDDDUUDDDDDRRUULLDDRRU"
                },

                // CHAPTER MASTERY: three sibling rooms occupy three distinct docking lanes. The
                // player alternates approach sides and entry edges, finishing every room socket
                // before the last docked room provides the only route to the player target.
                new LevelDef
                {
                    name = "Triple Dock",
                    rooms = new[]
                    {
                        new[] { "#########", "###P#####", "#x.1....#", "#.#####.#", "#.....2x#", "#######.#", "#x.3....#", "#.#######", "#p#######", "#########" },
                        new[] { "##.##", "#...#", "#....", "#...#", "##.##" },
                        new[] { "#####", "#...#", ".....", "#...#", "##.##" },
                        new[] { "#####", "#...#", ".....", "#...#", "##.##" }
                    },
                    par = 39,
                    solution = "DDRDRRLLLLDLDDDRRRRRRRRDDDDLLLLLLLDLDDD"
                },
            };
        }

        // The end-game chapter is deliberately Chapter IV pushed to mastery: every board starts
        // with four or five connected room scales, uses the same visible box-inside-a-box play,
        // and finishes through Chapter IV's sealed portal pocket. There are no Chapter III flat
        // transfer boards in the finale. Later boards mirror a different deep source route so the
        // useful entry side, docking order and return path all change instead of repeating an
        // earlier silhouette. LevelLayoutRebalancer then installs the learned Chapter I one-ways,
        // creates exactly the missing replay-proven colour jobs needed to reach five visible goals,
        // and turns one completed delivery into the permanent gate hold. No generic grey cargo is used.
        static LevelDef[] ChapterFiveExtremeNestedRooms()
        {
            LevelDef[] recursion = ChapterFiveRecursion();
            // In "The Long Way Out", Room 2 is deliberately pushed and docked. Room 4 is the
            // violet cargo chamber the player enters; the winning route never moves that chamber.
            // Preserve that distinction in both transformed finale variants.
            LevelDef longWayOutMastery = AnchorRoomReference(
                RotateLevelClockwise(recursion[8]), 4, 'X');
            LevelDef[] source =
            {
                recursion[5],
                MirrorLevelVertically(recursion[5]),
                // Rotate the branching sibling puzzle so its portal pocket keeps open proportions.
                // The Chapter V portal helper leaves its inner target room's dimensions unchanged.
                RotateLevelClockwise(recursion[6]),
                recursion[7],
                MirrorLevelHorizontally(recursion[7]),
                longWayOutMastery,
                MirrorLevelHorizontally(longWayOutMastery),
                recursion[9],
                RotateLevelClockwise(recursion[9]),
                MirrorLevelHorizontally(RotateLevelClockwise(recursion[9])),
            };
            string[] names =
            {
                "Four Rooms Locked",
                "Inverted Boundary Circuit",
                "Sibling Portal Relay",
                "Docking Side Paradox",
                "Reverse Exit Chamber",
                "Five-Space Return",
                "Upside-Down Extraction",
                "Branched Nested Exodus",
                "Mirror Branch Lockdown",
                "The Final Box",
            };
            // Every finale puzzle uses the Chapter IV portal for a real sealed exit. Keep every
            // pocket one cell deep: a longer pocket would add MOVES without adding a decision.
            int[] portalDistances = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

            var result = new LevelDef[source.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = CloneLevelDefinition(source[i]);
                result[i].name = names[i];
                if (i == 9)
                    ShiftLevel50UpperPassageRight(result[i]);
                AddMandatoryChapterFivePortalExit(result[i], portalDistances[i]);
                result[i].designComplexity = ReviewedCampaignDifficulty(result[i], 40 + i);
            }
            return result;
        }

        // In the transformed Level 50 source, Room 3 begins with two open top cells (`..#`). The
        // compact doorway marker chooses the lower-index cell of an even opening, which leaves the
        // visible route one cell left of the orange Room-4 box beneath it. Move that authored
        // opening—not merely its sprite—to `#..`, aligning the real path and collision one cell to
        // the right. The focused transform keeps Levels 48 and 49 byte-for-byte unchanged.
        static void ShiftLevel50UpperPassageRight(LevelDef level)
        {
            const int roomIndex = 3;
            if (level?.rooms == null || level.rooms.Length <= roomIndex
                || level.rooms[roomIndex] == null || level.rooms[roomIndex].Length == 0
                || level.rooms[roomIndex][0] != "..#")
                throw new System.InvalidOperationException(
                    "Level 50 upper-passage source changed; expected Room 3 top row '..#'.");

            level.rooms[roomIndex][0] = "#..";
            level.solution =
                "LLLDDDDDDLLDDRDDDRRRULDLUUUUUUUUUURRURRULLDRRRLURRRLLDRRRRRLLULLDD";
            level.par = level.solution.Length;
        }

        static int CountAuthoredCompletionGoals(LevelDef level)
        {
            int count = 0;
            if (level?.rooms == null) return count;
            foreach (string[] room in level.rooms)
            {
                if (room == null) continue;
                foreach (string row in room)
                {
                    if (string.IsNullOrEmpty(row)) continue;
                    foreach (char cell in row)
                        if (cell == 'p' || cell == 'x' || cell == '&'
                            || ColourGoals.ContainsKey(cell)
                            || ColourCargoOnGoals.ContainsKey(cell))
                            count++;
                }
            }
            return count;
        }

        static bool ChapterTwoSourceHasButtonGate(LevelDef level)
        {
            bool button = false;
            bool gate = false;
            if (level?.rooms == null) return false;
            foreach (string[] room in level.rooms)
            {
                if (room == null) continue;
                foreach (string row in room)
                {
                    if (string.IsNullOrEmpty(row)) continue;
                    foreach (char cell in row)
                    {
                        button |= cell == 'B' || cell == '&';
                        gate |= cell == 'G';
                    }
                }
            }
            return button && gate;
        }

        // Focused source gate for the final chapter. The global campaign check already proves
        // names, layouts, par floors and ordering; this extra contract prevents Chapter V from
        // drifting back to unrelated flat-board mechanics.
        static void ValidateAdvancedRecursiveChapterFiveDefinitions(LevelDef[] levels)
        {
            if (levels == null || levels.Length != 10)
                throw new System.InvalidOperationException(
                    $"Chapter V must contain exactly 10 recursive levels; found {levels?.Length ?? 0}.");

            int previousPlanningDifficulty = -1;
            for (int i = 0; i < levels.Length; i++)
            {
                LevelDef level = levels[i];
                int campaignIndex = 40 + i;
                int number = campaignIndex + 1;
                if (level?.rooms == null || level.rooms.Length != MinimumRoomCounts[campaignIndex])
                    throw new System.InvalidOperationException(
                        $"Level {number} must contain exactly {MinimumRoomCounts[campaignIndex]} connected rooms.");
                if (level.rooms.Length < 4)
                    throw new System.InvalidOperationException(
                        $"Level {number} is too shallow for the final chapter.");
                ValidateChapterFiveOpenChamberPresentation(level, number);
                if (string.IsNullOrWhiteSpace(level.solution) || level.solution.Length != level.par)
                    throw new System.InvalidOperationException(
                        $"Level {number} needs one explicit authored winning route whose length equals par.");
                if (level.par > 70)
                    throw new System.InvalidOperationException(
                        $"Level {number} uses {level.par} moves. Chapter V difficulty must come from "
                        + "puzzle dependencies, not a long walking route (maximum 70 for the finale).");

                int roomReferences = 0;
                int movableRooms = 0;
                int portals = 0;
                int authoredGoals = 0;
                for (int roomIndex = 0; roomIndex < level.rooms.Length; roomIndex++)
                {
                    string[] room = level.rooms[roomIndex];
                    if (room == null || room.Length < 3 || room.Length > 14
                        || string.IsNullOrEmpty(room[0]) || room[0].Length > 18)
                        throw new System.InvalidOperationException(
                            $"Level {number}, Room {roomIndex} exceeds the finale visibility bounds.");
                    int width = room[0].Length;
                    foreach (string row in room)
                    {
                        if (row == null || row.Length != width)
                            throw new System.InvalidOperationException(
                                $"Level {number}, Room {roomIndex} has uneven rows.");
                        foreach (char cell in row)
                        {
                            if ((cell >= '1' && cell <= '9') || AnchoredBoxes.ContainsKey(cell))
                                roomReferences++;
                            if (cell >= '1' && cell <= '9') movableRooms++;
                            if (cell == 'o') portals++;
                            if (cell == 'p' || cell == 'x'
                                || ColourGoals.ContainsKey(cell)
                                || ColourCargoOnGoals.ContainsKey(cell)) authoredGoals++;
                        }
                    }
                }
                if (roomReferences != level.rooms.Length - 1)
                    throw new System.InvalidOperationException(
                        $"Level {number} must connect every inner room exactly once.");
                if (movableRooms == 0 && level.rooms.Length < 4)
                    throw new System.InvalidOperationException(
                        $"Level {number} needs either a movable-room docking decision or a deep "
                        + "multi-boundary transfer.");
                if (portals != 2 || !PlayerGoalRequiresPortal(level))
                    throw new System.InvalidOperationException(
                        $"Level {number} needs one mandatory Chapter IV portal pair into a sealed finish pocket.");
                if (authoredGoals < 2)
                    throw new System.InvalidOperationException(
                        $"Level {number} needs at least two authored completion goals before its "
                        + "replay-proven premium deliveries reach the progressive finale target.");

                int planningDifficulty = ChapterFivePlanningDifficulty(level, campaignIndex);
                if (planningDifficulty <= previousPlanningDifficulty)
                    throw new System.InvalidOperationException(
                        $"Level {number} planning difficulty {planningDifficulty} must exceed "
                        + $"{previousPlanningDifficulty} without relying on move count.");
                previousPlanningDifficulty = planningDifficulty;
            }
        }

        // Finale progression is measured in decisions and dependencies, never solution length.
        // The increasing one-way budget is replay-proven, while room depth, visible goals, movable
        // chambers, the gate hold and the mandatory portal are all real puzzle obligations.
        static int ChapterFivePlanningDifficulty(LevelDef level, int campaignIndex)
        {
            int visibleGoals = CountAuthoredCompletionGoals(level);
            int movableRooms = 0;
            foreach (string[] room in level.rooms)
                foreach (string row in room)
                    foreach (char cell in row)
                        if (cell >= '1' && cell <= '9') movableRooms++;

            return level.rooms.Length * 180
                + Mathf.Max(5, visibleGoals) * 70
                + movableRooms * 45
                + LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(campaignIndex) * 55
                + LevelLayoutRebalancer.AuthoredGateReuseBudgetForLevel(campaignIndex) * 120
                + 160; // mandatory sealed portal exit
        }

        // Level 42 is the approved finale presentation reference: one readable open chamber with
        // nested room-boxes, not a thin factory snake. Preserve that visual language without
        // copying its puzzle. Objects and mechanics are ignored here; only spaciousness and
        // camera-safe proportions are protected, while the recursive source defines the meaning.
        static void ValidateChapterFiveOpenChamberPresentation(LevelDef level, int number)
        {
            string[] outer = level.rooms[0];
            int height = outer.Length;
            int width = outer[0].Length;
            int traversableCells = 0;
            int readableRows = 0;
            foreach (string row in outer)
            {
                int rowCells = 0;
                foreach (char cell in row)
                {
                    if (cell == '#') continue;
                    traversableCells++;
                    rowCells++;
                }
                if (rowCells >= 3) readableRows++;
            }

            // A factory snake can have many floor cells while still being only one tile thick.
            // Count genuinely open 2x2 floor blocks so the old Final Manifest (and similar rails)
            // cannot masquerade as the approved Level 42 chamber presentation.
            int openBlocks = 0;
            for (int row = 0; row < height - 1; row++)
                for (int column = 0; column < width - 1; column++)
                    if (outer[row][column] != '#' && outer[row][column + 1] != '#'
                        && outer[row + 1][column] != '#' && outer[row + 1][column + 1] != '#')
                        openBlocks++;

            float aspect = Mathf.Max(width / (float)height, height / (float)width);
            if (width < 7 || height < 6 || traversableCells < 22
                || readableRows < 4 || openBlocks < 6 || aspect > 2.2f)
                throw new System.InvalidOperationException(
                    $"Level {number} must keep Level 42's open recursive-chamber presentation; "
                    + $"found {width}x{height}, {traversableCells} usable cells, "
                    + $"{readableRows} readable rows, {openBlocks} open 2x2 blocks and "
                    + $"{aspect:0.00}:1 aspect.");
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
            int longest = 1;
            int current = 1;
            for (int i = 1; i < route.Length; i++)
            {
                if (route[i] == route[i - 1]) current++;
                else current = 1;
                if (current > longest) longest = current;
            }
            return longest;
        }

        // Kept as an unused source reference for designers comparing the old chapter order.
        static LevelDef[] ChapterFiveRecursion()
        {
            return new[]
            {
                // TEACH: a pinned cobalt chamber is the only bridge into the lower pocket. The
                // player enters from the left, reads one compact inner bend, then exits through a
                // different edge. There is no straight-line or walk-around solution.
                new LevelDef
                {
                    name = "Across the Inside",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#########",
                            "#P..#...#",
                            "#...#..##",
                            "#......1#",
                            "#######.#",
                            "####....#",
                            "#jAAAJ.p#",
                            "#########"
                        },
                        new[]
                        {
                            "#######",
                            "#.....#",
                            "#.###.#",
                            "......#",
                            "#.###.#",
                            "#.....#",
                            "###.###"
                        }
                    },
                    par = 21,
                    solution = "RRDDRRRRRDDRRDDDDLLRR"
                },

                // PRACTICE: unlike Level 41's fixed bridge, this chamber must be moved around a
                // corner: down into the rail, then right into its socket. Inside, four independent
                // cargo pieces must be approached from different sides and placed on four separate
                // goals. This tests room positioning and route planning instead of repeating the
                // previous level's single horizontal convoy.
                new LevelDef
                {
                    name = "Corner Dock",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#########",
                            "#..P....#",
                            "#..1#...#",
                            "#.......#",
                            "#......##",
                            "#..#...p#",
                            "#########"
                        },
                        new[]
                        {
                            "#######",
                            "#.....#",
                            "#xb.bx#",
                            "......#",
                            "#.b.b.#",
                            "#.x..x#",
                            "###.###"
                        }
                    },
                    par = 26,
                    solution = "DDLDRRRRRRDURULRRDDLDRLDDR"
                },

                // PRACTICE: cargo begins two spaces deep. After extracting it through both room
                // boundaries, the player must shift a four-crate convoy, take the lower return
                // corridor and turn the final crate upward onto its separate goal.
                new LevelDef
                {
                    name = "Bring It Out",
                    rooms = new[]
                    {
                        new[]
                        {
                            "########",
                            "#......#",
                            "#...Q..#",
                            "#..P..j#",
                            "#..AAAp#",
                            "##.###.#",
                            "##.....#",
                            "########"
                        },
                        new[]
                        {
                            "##.##",
                            "#...#",
                            "...U#",
                            "#...#",
                            "##.##"
                        },
                        new[]
                        {
                            "#####",
                            "#...#",
                            "..J..",
                            "#...#",
                            "#####"
                        }
                    },
                    par = 32,
                    solution = "RUUURRURRDLLLLLLLULDDLDRDDRRRRUU"
                },

                // EXPERIMENT: reverse the transfer. An outer crate is driven through two pinned
                // rooms, turned onto the deep goal, and left there while the player backtracks to
                // a separate outer target. This proves boundaries work in both directions.
                new LevelDef
                {
                    name = "Send It In",
                    rooms = new[]
                    {
                        new[]
                        {
                            "##########",
                            "jAAJp....#",
                            "####Pb.Q##",
                            "####.....#",
                            "##########"
                        },
                        new[]
                        {
                            "#######",
                            "#.....#",
                            "#.....#",
                            ".......",
                            "#.....#",
                            "#.U...#",
                            "#######"
                        },
                        new[]
                        {
                            "###.###",
                            "#x....#",
                            "#...#.#",
                            "#.....#",
                            "#.....#",
                            "#######"
                        }
                    },
                    par = 35,
                    solution = "RRRRURDDDDLDDRUDRRUULLLRUUULLLULLLR"
                },

                // EXPERIMENT: the green child room is pushed down exactly twice. Its top entrance
                // is occupied and braced against a column, so the player must leave, re-approach
                // from the side, enter, and extract that same cargo into the parent goal.
                new LevelDef
                {
                    name = "Shift the Chamber",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#########",
                            "#......p#",
                            "#P...1###",
                            "jAAJ....#",
                            "#########"
                        },
                        new[]
                        {
                            "#...###",
                            "#.#2#.#",
                            "#.#.#.#",
                            ".x..#.#",
                            "...##.#",
                            "#.....#",
                            "#######"
                        },
                        new[]
                        {
                            "...b...",
                            "#..#..#",
                            ".......",
                            "#.....#",
                            "#######"
                        }
                    },
                    par = 40,
                    solution = "URRRRDDDDUULLDDDRRRRRRUULLLLLDLLLLUURRRR"
                },

                // COMBINE: three fixed chambers alternate entry directions. Deep cargo first
                // travels upward, then turns left and crosses the remaining three boundaries.
                new LevelDef
                {
                    name = "Boundary Relay",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#########",
                            "#.......#",
                            "#.p.....#",
                            "#x....Q##",
                            "#..JAAAj#",
                            "#P......#",
                            "#########"
                        },
                        new[]
                        {
                            "#######",
                            "#.....#",
                            "#.....#",
                            "....U##",
                            "#.....#",
                            "#.....#",
                            "#######"
                        },
                        new[]
                        {
                            "#####",
                            "#...#",
                            "#...#",
                            ".....",
                            "#.V.#",
                            "#...#",
                            "#####"
                        },
                        new[]
                        {
                            "##.##",
                            "#...#",
                            "#.b.#",
                            "#...#",
                            "#####"
                        }
                    },
                    par = 46,
                    solution = "UURRRRRRRRRRRDDRDDLUUUURUULDRDLLLLLLLLLLLDRLUU"
                },

                // COMBINE: this is a branching containment graph rather than another straight
                // stack. Cargo leaves the deep right branch, crosses the outer sorting bay, enters
                // the left sibling room, turns onto its goal, and ends beside the player target.
                new LevelDef
                {
                    name = "Between Worlds",
                    rooms = new[]
                    {
                        new[]
                        {
                            "###########",
                            "#.........#",
                            "#.Q.....U##",
                            "#.........#",
                            "#....P....#",
                            "###########"
                        },
                        new[]
                        {
                            "####j##",
                            "#...A.#",
                            "#nCCJ.#",
                            "###....",
                            "#.....#",
                            "#p....#",
                            "#######"
                        },
                        new[]
                        {
                            "#########",
                            "#.......#",
                            "#.......#",
                            ".....V###",
                            "#.......#",
                            "#.......#",
                            "#########"
                        },
                        new[]
                        {
                            "#######",
                            "#.....#",
                            "#..#..#",
                            "...N...",
                            "#..#..#",
                            "#.....#",
                            "#######"
                        }
                    },
                    par = 46,
                    solution = "UURRRRRRRRRUURRRDDLLLLLLLLLLLLLLLLDLURULDDDLLL"
                },

                // TWIST: the inner room is movable. Push it down twice and right into the lower
                // socket. The socket column sits behind the room instead of in the orange cargo
                // lane, so the extracted cargo has a clear, readable path through every boundary.
                new LevelDef
                {
                    name = "Exit Side",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#######",
                            "#...P.#",
                            "#.....#",
                            "#..Q..#",
                            "#A....#",
                            "#A...p#",
                            "#A....#",
                            "#A....#",
                            "#j....#",
                            "#######"
                        },
                        new[]
                        {
                            "###.###",
                            "#.....#",
                            "#..2..#",
                            ".......",
                            "#....##",
                            "#.....#",
                            "#######"
                        },
                        new[]
                        {
                            "#####",
                            "#...#",
                            "#...#",
                            "....#",
                            "#...#",
                            "#.V.#",
                            "#####"
                        },
                        new[]
                        {
                            "##.##",
                            "#...#",
                            "#.J.#",
                            "#...#",
                            "#...#",
                            "#...#",
                            "#####"
                        }
                    },
                    par = 47,
                    solution = "DLDDDDLDRRRRDDDRDDLUUUURULLLLDLURULLLLULDRDRRRD"
                },

                // MASTERY: the reference interaction is now used as a complete original puzzle.
                // Push the child down twice, discover that its top entrance is sealed, leave the
                // parent, re-enter from another side, descend to violet depth, and bring the coral
                // cargo back through all four room boundaries before reaching the outer target.
                new LevelDef
                {
                    name = "The Long Way Out",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#############",
                            "#.......p...#",
                            "#...........#",
                            "jAAAA.....Q##",
                            "#...........#",
                            "#....P......#",
                            "#############"
                        },
                        new[]
                        {
                            "#...###",
                            "#.#2#.#",
                            "#.#.#.#",
                            "....#.#",
                            "...##.#",
                            "#.....#",
                            "#######"
                        },
                        new[]
                        {
                            "#####",
                            "#...#",
                            "...V#",
                            "#...#",
                            "#####"
                        },
                        new[]
                        {
                            "#####",
                            "#...#",
                            "...4#",
                            "#...#",
                            "#####"
                        },
                        new[]
                        {
                            "#######",
                            "#.....#",
                            "...J...",
                            "#.....#",
                            "#######"
                        }
                    },
                    par = 55,
                    solution = "UUURRRRRDDDUUULDRRRRRRRRRRRURRRDLLLLLLLLLLLLLLLLLLUURRR"
                },

                // FINALE: this containment graph branches instead of repeating Level 49's stack.
                // Fixed sibling chambers prevent cargo from auto-docking the destination. Deep in
                // the source branch, the player must lift and side-dock a movable room, align two
                // coloured crates around its brace, extract the ordered convoy through four room
                // boundaries, then turn the colours into the vertical sibling receiver. The turn
                // breaks up the final cabinet input without reducing the planning requirement.
                new LevelDef
                {
                    name = "Nested Exodus",
                    rooms = new[]
                    {
                        new[]
                        {
                            "#######",
                            "#....p#",
                            "#...Q##",
                            "#...P.#",
                            "#.....#",
                            "#...U.#",
                            "#######"
                        },
                        new[]
                        {
                            "##.##",
                            "#...#",
                            "#..V#",
                            "#...#",
                            "#..##"
                        },
                        new[]
                        {
                            "##.##",
                            "#.n.#",
                            "#.A.#",
                            "#.A.#",
                            "#.A.#",
                            "#.j.#",
                            "#####"
                        },
                        new[]
                        {
                            ".#####",
                            ".4...#",
                            "######"
                        },
                        new[]
                        {
                            "#########",
                            "#...#####",
                            "#..J...##",
                            ".......##",
                            "#####N.##",
                            "#####..##",
                            "#########"
                        }
                    },
                    par = 58,
                    solution = "UUURRRRRRUURRDRRRDDDLURULLLLLLLLLLUULDULDDDUURDDDDDUULUURR"
                }
            };
        }

        static int AuthoredDifficulty(LevelDef level)
        {
            var mechanics = new HashSet<string>();
            int objects = 0, targets = 0, featureTiles = 0, dependencies = 0;
            int cognitiveLoad = 0, regularPlayers = 0;
            bool lightSwitch = false, lightGate = false, heavySwitch = false, heavyGate = false;
            bool key = false, locked = false, colourA = false, colourAGoal = false;
            bool colourB = false, colourBGoal = false, colourC = false, colourCGoal = false;
            bool echo = false, echoGoal = false;
            bool mirror = false, mirrorGoal = false;

            foreach (var room in level.rooms)
                foreach (string row in room)
                    foreach (char ch in row)
                    {
                        if (CrateKinds.ContainsKey(ch) || ColourCargoOnGoals.ContainsKey(ch)
                            || (ch >= '1' && ch <= '9')
                            || AnchoredBoxes.ContainsKey(ch)) objects++;
                        if (ch == 'x' || ch == 'p' || ch == 'e' || ch == 'm' || ch == '&'
                            || ColourGoals.ContainsKey(ch) || ColourCargoOnGoals.ContainsKey(ch)) targets++;

                        if (ch == '_') { mechanics.Add("ice"); featureTiles++; }
                        else if (ch == '~') { mechanics.Add("trench"); featureTiles++; }
                        else if (ch == 'o') { mechanics.Add("portal"); featureTiles++; }
                        else if (CurrentDirs.ContainsKey(ch)) { mechanics.Add("current"); featureTiles++; }
                        else if (OneWayDirs.ContainsKey(ch)) { mechanics.Add("oneway"); featureTiles++; }
                        else if (TerrainKinds.TryGetValue(ch, out var kind))
                        {
                            mechanics.Add(kind.ToString());
                            featureTiles++;
                        }

                        if (ch == 'O') mechanics.Add("boulder");
                        else if (ch == 'q') mechanics.Add("locking-cargo");
                        else if (ch == 'E') { mechanics.Add("echo"); echo = true; }
                        else if (ch == 'e') echoGoal = true;
                        else if (ch == 'M') { mechanics.Add("mirror"); mirror = true; }
                        else if (ch == 'm') mirrorGoal = true;
                        else if (ch == 'P') regularPlayers++;
                        else if (ch == 'B' || ch == '&') lightSwitch = true;
                        else if (ch == 'G') lightGate = true;
                        else if (ch == 'W') heavySwitch = true;
                        else if (ch == 'H') heavyGate = true;
                        else if (ch == 'k') key = true;
                        else if (ch == 'K') locked = true;
                        else if (ch == 'J') colourA = true;
                        else if (ch == 'j' || ch == '&') colourAGoal = true;
                        else if (ch == 'N') colourB = true;
                        else if (ch == 'n') colourBGoal = true;
                        else if (ch == 'Z') colourC = true;
                        else if (ch == 'z') colourCGoal = true;
                    }

            if (lightSwitch && lightGate) dependencies++;
            if (heavySwitch && heavyGate) dependencies++;
            if (key && locked) dependencies++;
            if (colourA && colourAGoal) dependencies++;
            if (colourB && colourBGoal) dependencies++;
            if (colourC && colourCGoal) dependencies++;
            if (echo && echoGoal) dependencies++;
            if (mirror && mirrorGoal) dependencies++;
            // Mirror and echo boards ask the player to predict a second actor whose movement is
            // coupled to every input.  Treating that like a familiar key-and-lock dependency put
            // the cognitively harder Facing Away before the simpler Pearl Lock.  This small human-
            // difficulty premium resolves that inversion without pushing the mechanic past later
            // multi-stage boards.
            if (mirror && mirrorGoal) cognitiveLoad += 8;
            if (echo && echoGoal) cognitiveLoad += 8;

            // Multiple interchangeable player bodies and inner/outer boards are not comparable to
            // ordinary walking distance. Each input can commit another goal route or change which
            // body remains controllable, while a chamber adds a second coordinate space whose
            // final state must be predicted before returning outside. These premiums represent
            // real state the player tracks; they are not level-number bonuses.
            if (regularPlayers > 1)
                cognitiveLoad += 2000 + Mathf.Max(0, regularPlayers - 2) * 500;

            bool hasPulse = mechanics.Contains(TerrainKind.Pulse.ToString());
            bool hasMagnet = mechanics.Contains(TerrainKind.Magnet.ToString());
            if (hasPulse && level.rooms.Length > 1)
                cognitiveLoad += 320; // time state must be carried across the room transition
            if (hasMagnet && level.rooms.Length > 1)
                cognitiveLoad += 160; // predict an inner cargo result before returning outside
            dependencies += Mathf.Max(0, level.rooms.Length - 1); // finish one room to reach the next

            int turns = 0;
            for (int i = 1; i < level.solution.Length; i++)
                if (level.solution[i] != level.solution[i - 1]) turns++;

            // Coupled-player commands do more work than ordinary walking: a single input advances
            // the player, echo and/or mirror together. Scoring every command as a full independent
            // decision exaggerated Chapter II and made its three-actor boards appear harder than
            // the recursive chapter that follows. This calibrated evidence model still rewards
            // route length, turns, objectives and linked dependencies, while preserving the real
            // Level 10 -> 11 teaching step and a smooth hand-off into Level 21.
            if (mirror && mirrorGoal && level.rooms.Length == 1)
            {
                int irreversibleConstraint = mechanics.Contains(TerrainKind.Cage.ToString()) ? 60 : 0;
                return 349
                       + level.par * 37
                       + turns * 12
                       + objects * 25
                       + targets * 20
                       + mechanics.Count * 55
                       + dependencies * 70
                       + cognitiveLoad
                       + irreversibleConstraint
                       + featureTiles * 2;
            }

            // Colour-factory boards use route length, direction changes, deliveries and distinct
            // matching relationships as evidence. Chapter V adds an expert-context baseline below;
            // all increases still come from authored tasks and ordering work.
            bool hasColourDelivery = (colourA && colourAGoal)
                                     || (colourB && colourBGoal)
                                     || (colourC && colourCGoal);
            if (hasColourDelivery && level.rooms.Length == 1)
                return 850
                       + level.par * 8
                       + turns * 8
                       + objects * 20
                       + targets * 15
                       + dependencies * 25
                       + mechanics.Count * 35
                       + featureTiles * 2;

            // Recursive boards compress distance: one command can move cargo across a doorway and
            // change coordinate spaces at the same time. Score their actual route decisions,
            // objects, objectives and containment depth on a calibrated scale instead of treating
            // every extra room like hundreds of ordinary walking steps. Chapter III now contains
            // substantially longer, multi-boundary solutions, so route length uses a lower weight
            // while decisions, objectives and containment remain explicit. This keeps its opening
            // just above Level 20 and its four-room mastery just below Chapter IV's richer opener.
            // Chapter II receives a teaching-context calibration below; Chapters III-IV retain the
            // full recursive evidence because they combine cargo transfer and movable-room planning.
            if (level.rooms.Length > 1)
                return 1625
                       + level.par * 5
                       + turns
                       + Mathf.Max(0, level.rooms.Length - 1) * 20
                       + objects * 10
                       + targets * 5
                       + dependencies * 15
                       + mechanics.Count * 40
                       + featureTiles * 2;

            return level.par * 60
                   + turns * 18
                   + objects * 25
                   + targets * 20
                   + mechanics.Count * 55
                   + dependencies * 90
                   + cognitiveLoad
                   // A room transition is real cognitive work, but the old 2400-point premium
                   // made adding a third room look harder than an entire solved route. A calibrated
                   // 300-point layer cost lets Chapter V rise smoothly while par, turns, cargo and
                   // dependencies still provide most of the evidence.
                   + Mathf.Max(0, level.rooms.Length - 1) * 300
                   + featureTiles * 2;
        }

        // Chapter I uses only four readable rules, so its evidence should measure how those
        // rules are combined rather than reward new symbols. Longer routes, direction changes,
        // separate objectives and the button/gate dependency all represent real planning work.
        // The calibration keeps the ten foundations strictly increasing and still leaves a clean
        // difficulty step into the recursive Chapter II opener.
        static int FoundationDifficulty(LevelDef level)
        {
            int objects = 0;
            int targets = 0;
            bool hasPush = false;
            bool hasOneWay = false;
            bool hasButton = false;
            bool hasGate = false;

            foreach (string[] room in level.rooms)
                foreach (string row in room)
                    foreach (char ch in row)
                    {
                        if (ch == 'b')
                        {
                            objects++;
                            hasPush = true;
                        }
                        if (ch == 'p' || ch == 'x') targets++;
                        if (OneWayDirs.ContainsKey(ch)) hasOneWay = true;
                        if (ch == 'B') hasButton = true;
                        if (ch == 'G') hasGate = true;
                    }

            int turns = 0;
            for (int i = 1; i < level.solution.Length; i++)
                if (level.solution[i] != level.solution[i - 1]) turns++;

            int ruleFamilies = 0;
            if (hasPush) ruleFamilies++;
            if (hasOneWay) ruleFamilies++;
            if (hasButton && hasGate) ruleFamilies++;

            int dependency = hasButton && hasGate ? 1 : 0;
            return 480
                   + level.par * 11
                   + turns * 8
                   + objects * 20
                   + targets * 15
                   + ruleFamilies * 35
                   + dependency * 55;
        }

        // A symbol is not allowed to sit on a campaign board as decoration. This Edit Mode proof
        // replays the authored route through the same model used at runtime and then checks the
        // chapter's actual puzzle language: foundation tiles, cargo transfer, room docking or
        // colour delivery. Generation stops before saving a bad prefab.
        static void ValidateCampaignMechanicTasks(GameObject prefab, LevelDef level, int number)
        {
            LevelModel model = LevelParser.Parse(prefab, number - 1);
            if (model == null || model.player == null)
                throw new System.InvalidOperationException($"Level {number} could not build its gameplay model.");

            int chapter = (number - 1) / 10;

            bool hasOneWay = false;
            bool hasButton = false;
            bool hasGate = false;
            bool hasPortal = false;
            int authoredGoals = 0;
            foreach (PRoom room in model.rooms.Values)
            {
                hasOneWay |= HasFoundationCells(room.oneway, direction => direction != Vector2Int.zero);
                hasButton |= HasFoundationCells(room.button, value => value);
                hasGate |= HasFoundationCells(room.gate, value => value);
                hasPortal |= HasFoundationCells(room.portal, value => value);
                authoredGoals += room.boxGoals.Count + room.colourGoals.Count
                                 + room.playerGoals.Count;
            }

            var initialCargo = new List<PEntity>();
            var initialMovableRooms = new List<PEntity>();
            foreach (PEntity entity in model.entities)
            {
                if (entity == null || !entity.IsCrate) continue;
                if (entity.interiorRoomId >= 0)
                {
                    if (!entity.anchored) initialMovableRooms.Add(entity);
                }
                else
                {
                    initialCargo.Add(entity);
                }
            }

            var movedCargo = new HashSet<PEntity>();
            var movedMetaRooms = new HashSet<PEntity>();
            var visitedRooms = new HashSet<int> { model.player.roomId };
            bool usedOneWay = false;
            bool activatedButton = model.GatesOpen();
            bool crossedOpenGate = false;
            int playerRoomTransitions = 0;
            int cargoRoomTransitions = 0;
            int metaMoves = 0;

            for (int stepIndex = 0; stepIndex < level.solution.Length; stepIndex++)
            {
                char command = level.solution[stepIndex];
                if (!TryFoundationDirection(command, out Vector2Int direction))
                    throw new System.InvalidOperationException(
                        $"Level {number} route contains invalid command '{command}'.");

                var before = new Dictionary<PEntity, (int room, Vector2Int cell)>();
                foreach (PEntity entity in model.entities)
                    before[entity] = (entity.roomId, entity.pos);
                bool gateWasOpen = model.GatesOpen();
                int playerRoomBefore = model.player.roomId;

                if (!model.TryMovePlayer(direction))
                    throw new System.InvalidOperationException(
                        $"Level {number} authored route is blocked at move {stepIndex + 1} ('{command}').");

                bool gateIsOpen = model.GatesOpen();
                activatedButton |= gateIsOpen;
                if (model.player.roomId != playerRoomBefore) playerRoomTransitions++;
                visitedRooms.Add(model.player.roomId);
                foreach (PEntity entity in model.entities)
                {
                    var old = before[entity];
                    if (old.room == entity.roomId && old.cell == entity.pos) continue;
                    if (entity.IsCrate && entity.interiorRoomId < 0)
                    {
                        movedCargo.Add(entity);
                        if (old.room != entity.roomId) cargoRoomTransitions++;
                    }
                    if (entity.interiorRoomId >= 0)
                    {
                        movedMetaRooms.Add(entity);
                        metaMoves++;
                    }

                    usedOneWay |= FoundationSegmentTouches(model, old.room, old.cell,
                        entity.roomId, entity.pos,
                        (room, cell) => room.OneWay(cell) != Vector2Int.zero);
                    if (gateWasOpen || gateIsOpen)
                        crossedOpenGate |= FoundationSegmentTouches(model, old.room, old.cell,
                            entity.roomId, entity.pos,
                            (room, cell) => room.gate != null && room.gate[cell.x, cell.y]);
                }

                // Chapter V repeatedly transfers cargo between nested coordinate spaces. Validate
                // every intermediate runtime state, not only the final goals: no authored push may
                // leave an entity outside its destination room or inside a wall. This catches the
                // exact class of apparent "box went outside" errors before any finale prefab saves.
                if (chapter == 4)
                {
                    foreach (PEntity entity in model.entities)
                    {
                        if (entity == null || entity.sunk) continue;
                        if (!model.rooms.TryGetValue(entity.roomId, out PRoom entityRoom)
                            || !entityRoom.InBounds(entity.pos)
                            || entityRoom.IsWall(entity.pos))
                            throw new System.InvalidOperationException(
                                $"Level {number} places an entity outside playable Room "
                                + $"{entity.roomId} at move {stepIndex + 1}.");
                    }
                }
            }

            if (!model.IsWon())
                throw new System.InvalidOperationException($"Level {number} authored route does not solve the board.");

            if (chapter == 0)
            {
                if (movedCargo.Count != initialCargo.Count)
                    throw new System.InvalidOperationException(
                        $"Level {number} contains decorative cargo. Every cargo object needs a real task in the solution.");
                if (hasOneWay && !usedOneWay)
                    throw new System.InvalidOperationException(
                        $"Level {number} contains a one-way tile that the solution never uses.");
                if (hasButton != hasGate)
                    throw new System.InvalidOperationException(
                        $"Level {number} must author a button and its gate as one complete mechanic.");
                if (hasButton && (!activatedButton || !crossedOpenGate))
                    throw new System.InvalidOperationException(
                        $"Level {number} contains a decorative button/gate. The solution must activate the button and cross the opened gate.");
                return;
            }

            if (chapter == 1)
            {
                int taskTarget = LevelLayoutRebalancer.ChapterTwoTaskTargetForLevel(number - 1);
                int gateReuseBudget = LevelLayoutRebalancer.ChapterTwoGateReuseBudgetForLevel(number - 1);
                int expectedOneWays = LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(number - 1);
                int sourceGoals = CountAuthoredCompletionGoals(level);
                int generatedGoals = Mathf.Max(0, taskTarget - sourceGoals);
                int generatedGate = gateReuseBudget > 0 && !ChapterTwoSourceHasButtonGate(level)
                    ? 1 : 0;
                int expectedObjectives = generatedGoals + generatedGate;
                if (authoredGoals != taskTarget)
                    throw new System.InvalidOperationException(
                        $"Level {number} exposes {authoredGoals}/{taskTarget} completion tasks.");
                if (model.rebalanceObjectives != expectedObjectives)
                    throw new System.InvalidOperationException(
                        $"Level {number} retained {model.rebalanceObjectives}/{expectedObjectives} "
                        + "route-proven cargo/gate objectives. Redesign the route before generation.");
                if (expectedOneWays > 0
                    && (!hasOneWay || !usedOneWay || model.rebalanceOneWays != expectedOneWays))
                    throw new System.InvalidOperationException(
                        $"Level {number} must actively use all {expectedOneWays} Chapter I one-way commitments.");
                if (gateReuseBudget > 0
                    && (!hasButton || !hasGate || !activatedButton || !crossedOpenGate))
                    throw new System.InvalidOperationException(
                        $"Level {number} must park cargo on the Chapter I button and cross its opened gate.");
                if ((expectedOneWays > 0
                        && !model.curriculumReuses.Contains(MechanicCatalog.Id.OneWay))
                    || (gateReuseBudget > 0
                        && !model.curriculumReuses.Contains(MechanicCatalog.Id.ButtonGate)))
                    throw new System.InvalidOperationException(
                        $"Level {number} did not retain the complete Chapter I mechanic reuse contract.");
                int purposefulTasks = authoredGoals + Mathf.Max(0, model.rooms.Count - 1)
                    + gateReuseBudget + (expectedOneWays > 0 ? 1 : 0);
                if (purposefulTasks < 3)
                    throw new System.InvalidOperationException(
                        $"Level {number} exposes only {purposefulTasks} purposeful tasks.");
            }

            if (chapter == 2)
            {
                int expectedOneWays =
                    LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(number - 1);
                int expectedGateHolds =
                    LevelLayoutRebalancer.ChapterThreeGateReuseBudgetForLevel(number - 1);
                if (model.rebalanceOneWays != expectedOneWays
                    || (expectedOneWays > 0 && (!hasOneWay || !usedOneWay)))
                    throw new System.InvalidOperationException(
                        $"Level {number} retained {model.rebalanceOneWays}/{expectedOneWays} "
                        + "purposeful Chapter I one-way commitments.");
                if (expectedGateHolds > 0
                    && (model.rebalanceObjectives < expectedGateHolds
                        || !hasButton || !hasGate || !activatedButton || !crossedOpenGate))
                    throw new System.InvalidOperationException(
                        $"Level {number} must complete cargo on a Chapter I button, then cross "
                        + "its opened gate on the winning route.");
                if ((expectedOneWays > 0
                        && !model.curriculumReuses.Contains(MechanicCatalog.Id.OneWay))
                    || (expectedGateHolds > 0
                        && !model.curriculumReuses.Contains(MechanicCatalog.Id.ButtonGate)))
                    throw new System.InvalidOperationException(
                        $"Level {number} did not retain its planned Chapter I mechanic reuse.");
            }

            if (chapter == 4)
            {
                int expectedOneWays = LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(number - 1);
                int expectedGateHolds = LevelLayoutRebalancer.AuthoredGateReuseBudgetForLevel(number - 1);
                int premiumTaskTarget = LevelLayoutRebalancer.PremiumTaskTargetForLevel(number - 1);
                int authoredSourceGoals = CountAuthoredCompletionGoals(level);
                int expectedPremiumJobs = Mathf.Max(0, premiumTaskTarget - authoredSourceGoals);
                int expectedRebalanceObjectives = expectedPremiumJobs + expectedGateHolds;
                bool expectsPortal = number >= 41;
                // A recursive exit can settle the player through a portal without the portal ever
                // being the first adjacent cell of the command. Prove necessity from topology
                // instead: the paired portal exists, the goal pocket is sealed without it, and the
                // complete route has already won above.
                if (expectsPortal
                    && (!hasPortal || model.portalPair.Count != 2 || !PlayerGoalRequiresPortal(level)))
                    throw new System.InvalidOperationException(
                        $"Level {number} must require its paired Chapter IV-style portal to reach the sealed finish.");
                if (!expectsPortal && (hasPortal || model.portalPair.Count != 0))
                    throw new System.InvalidOperationException(
                        $"Level {number} must remain a compact portal-free nested-room puzzle.");
                if (model.rebalanceOneWays != expectedOneWays || !hasOneWay || !usedOneWay)
                    throw new System.InvalidOperationException(
                        $"Level {number} retained {model.rebalanceOneWays}/{expectedOneWays} "
                        + "winning-route one-way commitments.");
                if (authoredGoals < premiumTaskTarget)
                    throw new System.InvalidOperationException(
                        $"Level {number} exposes only {authoredGoals}/{premiumTaskTarget} visible "
                        + "completion goals. Every finale board needs at least five real jobs.");
                if (model.rebalanceObjectives != expectedRebalanceObjectives
                    || !hasButton || !hasGate || !activatedButton || !crossedOpenGate)
                    throw new System.InvalidOperationException(
                        $"Level {number} retained {model.rebalanceObjectives}/"
                        + $"{expectedRebalanceObjectives} premium delivery/gate tasks. It must "
                        + "complete all visible jobs, hold one button and cross its gate.");
                if (!model.curriculumReuses.Contains(MechanicCatalog.Id.OneWay)
                    || !model.curriculumReuses.Contains(MechanicCatalog.Id.ButtonGate))
                    throw new System.InvalidOperationException(
                        $"Level {number} did not retain the complete finale mechanic synthesis.");

                int purposefulTasks = authoredGoals + Mathf.Max(0, model.rooms.Count - 1)
                    + (expectsPortal ? 1 : 0)
                    + expectedGateHolds
                    + (expectedOneWays > 0 ? 1 : 0);
                if (purposefulTasks < 7)
                    throw new System.InvalidOperationException(
                        $"Level {number} exposes only {purposefulTasks} purposeful finale tasks; "
                        + "expected at least seven across visible deliveries, nested rooms, portal, "
                        + "gate and one-way routing.");
            }

            if (chapter >= 1)
            {
                if (visitedRooms.Count != model.rooms.Count)
                    throw new System.InvalidOperationException(
                        $"Level {number} route visits {visitedRooms.Count}/{model.rooms.Count} rooms; every authored room must be used.");
                if (model.rooms.Count > 1 && playerRoomTransitions == 0)
                    throw new System.InvalidOperationException(
                        $"Level {number} contains a room-box but the player never enters or exits it.");
                if (movedMetaRooms.Count != initialMovableRooms.Count)
                    throw new System.InvalidOperationException(
                        $"Level {number} moves {movedMetaRooms.Count}/{initialMovableRooms.Count} movable rooms; room mechanics cannot be decorative.");
                if (movedCargo.Count != initialCargo.Count)
                    throw new System.InvalidOperationException(
                        $"Level {number} moves {movedCargo.Count}/{initialCargo.Count} cargo objects; cargo cannot be decorative.");
                // Route-local cargo inserted for Chapters II and V must move (checked above), but
                // only cargo authored into the recursive source board must cross a room boundary.
                // Otherwise a clean local colour or button job would be mistaken for failed room
                // transfer design.
                int authoredCargo = chapter == 1 || chapter == 4
                    ? CountAuthoredCargo(level)
                    : initialCargo.Count;
                if (authoredCargo > 0 && model.rooms.Count > 1 && cargoRoomTransitions == 0)
                    throw new System.InvalidOperationException(
                        $"Level {number} contains recursive cargo but never carries it across a room boundary.");
            }
        }

        static int CountAuthoredCargo(LevelDef level)
        {
            int count = 0;
            if (level?.rooms == null) return count;
            foreach (string[] room in level.rooms)
            {
                if (room == null) continue;
                foreach (string row in room)
                {
                    if (string.IsNullOrEmpty(row)) continue;
                    foreach (char symbol in row)
                        if (CrateKinds.ContainsKey(symbol)
                            || ColourCargoOnGoals.ContainsKey(symbol))
                            count++;
                }
            }
            return count;
        }

        static void ValidateRoomSocketsOccupiedByRooms(LevelModel model, int number)
        {
            foreach (PRoom room in model.rooms.Values)
                foreach (Vector2Int goal in room.boxGoals)
                {
                    PEntity occupant = model.EntityAt(room.id, goal);
                    if (occupant == null || occupant.interiorRoomId < 0)
                        throw new System.InvalidOperationException(
                            $"Level {number} has a room socket that is not completed by a room-box.");
                }
        }

        static bool HasFoundationCells<T>(T[,] cells, System.Func<T, bool> predicate)
        {
            if (cells == null) return false;
            for (int x = 0; x < cells.GetLength(0); x++)
                for (int y = 0; y < cells.GetLength(1); y++)
                    if (predicate(cells[x, y])) return true;
            return false;
        }

        static bool FoundationSegmentTouches(LevelModel model, int oldRoom, Vector2Int oldCell,
                                             int newRoom, Vector2Int newCell,
                                             System.Func<PRoom, Vector2Int, bool> predicate)
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

        static bool TryFoundationDirection(char command, out Vector2Int direction)
        {
            switch (command)
            {
                case 'U': direction = Vector2Int.up; return true;
                case 'R': direction = Vector2Int.right; return true;
                case 'D': direction = Vector2Int.down; return true;
                case 'L': direction = Vector2Int.left; return true;
                default: direction = Vector2Int.zero; return false;
            }
        }

        // Chapter II difficulty counts room movement, docking and boundary cargo transfer.
        static int EasyInsideTheBoxDifficulty(LevelDef level)
        {
            bool movableRoom = false;
            bool roomSocket = false;
            bool boundaryCargo = false;
            bool ordinaryCargo = false;
            if (level?.rooms != null)
                foreach (string[] room in level.rooms)
                    foreach (string row in room)
                        foreach (char ch in row)
                        {
                            movableRoom |= ch >= '1' && ch <= '9';
                            roomSocket |= ch == 'x';
                            ordinaryCargo |= ch == 'b';
                            boundaryCargo |= ch == 'b' || ch == 'J' || ch == 'N' || ch == 'Z';
                        }

            int extraCoordinateSpaces = Mathf.Max(0, (level?.rooms?.Length ?? 1) - 2);
            return AuthoredDifficulty(level) - 351
                + (movableRoom ? 40 : 0)
                + (movableRoom && roomSocket ? 20 : 0)
                + (boundaryCargo ? 70 : 0)
                + (ordinaryCargo ? 30 : 0)
                + extraCoordinateSpaces * 80;
        }

        // Chapter III difficulty measures the three things the player actually has to plan:
        // relay one coloured cargo piece, move every movable room onto a visible socket, and carry
        // state through increasingly deep coordinate spaces. The score contains no level-number
        // bonus, so the generator rejects an easier later board instead of hiding the inversion.
        // The 1700 recursive-planning baseline places the first real cargo-through-a-room puzzle
        // above Chapter II's strongest docking exercise before route and task evidence are added.
        static int ChapterThreeDifficulty(LevelDef level)
        {
            int cargo = 0;
            int roomBoxes = 0;
            int targets = 0;
            int portals = 0;
            if (level?.rooms != null)
                foreach (string[] room in level.rooms)
                    foreach (string row in room)
                        foreach (char ch in row)
                        {
                            if (ch == 'J') cargo++;
                            if ((ch >= '1' && ch <= '9') || AnchoredBoxes.ContainsKey(ch))
                                roomBoxes++;
                            if (ch == 'j' || ch == 'x' || ch == 'p') targets++;
                            if (ch == 'o') portals++;
                        }

            int turns = 0;
            string route = level?.solution ?? string.Empty;
            for (int i = 1; i < route.Length; i++)
                if (route[i] != route[i - 1]) turns++;

            int extraRooms = Mathf.Max(0, (level?.rooms?.Length ?? 1) - 1);
            int dependencies = extraRooms + (cargo > 0 ? 1 : 0);
            int objects = cargo + roomBoxes;
            return 1700
                   + level.par * 5
                   + turns
                   + extraRooms * 20
                   + objects * 10
                   + targets * 5
                   + dependencies * 15
                   + (portals == 2 ? 120 : 0);
        }

        // One shared campaign score prevents chapter-boundary resets. The 100-point reviewed
        // rank is protected by a bounded 0-89 evidence band, so a later level can never score
        // below an earlier one. The band still records real planning texture (turns, route depth,
        // objectives, cargo and recursive rooms); MinimumAuthoredPars/MinimumRoomCounts protect
        // the underlying puzzle rather than allowing this score to manufacture difficulty.
        static int ReviewedCampaignDifficulty(LevelDef level, int campaignIndex)
        {
            if (campaignIndex < 0 || campaignIndex >= 50)
                throw new System.ArgumentOutOfRangeException(nameof(campaignIndex));

            int objects = 0;
            int targets = 0;
            int roomReferences = 0;
            int cargo = 0;
            if (level?.rooms != null)
                foreach (string[] room in level.rooms)
                    foreach (string row in room)
                        foreach (char cell in row)
                        {
                            bool isRoom = (cell >= '1' && cell <= '9')
                                || AnchoredBoxes.ContainsKey(cell);
                            bool isCargo = cell == 'b' || cell == 'i'
                                || cell == 'J' || cell == 'N' || cell == 'Z'
                                || cell == 'A' || cell == 'C';
                            if (isRoom || isCargo) objects++;
                            if (isRoom) roomReferences++;
                            if (isCargo) cargo++;
                            if (cell == 'p' || cell == 'x' || cell == '&' || ColourGoals.ContainsKey(cell)
                                || ColourCargoOnGoals.ContainsKey(cell))
                                targets++;
                        }

            int roomCount = level?.rooms?.Length ?? 0;
            int routeLength = level?.solution?.Length ?? 0;
            int rawEvidence;
            if (campaignIndex >= 40)
            {
                // Do not reward Chapter V for walking farther. Its evidence band comes entirely
                // from recursive spaces, visible jobs and replay-proven dependency commitments.
                rawEvidence = roomCount * 6
                    + roomReferences * 4
                    + cargo * 3
                    + objects
                    + targets * 4
                    + LevelLayoutRebalancer.LearnedOneWayBudgetForLevel(campaignIndex) * 2
                    + LevelLayoutRebalancer.AuthoredGateReuseBudgetForLevel(campaignIndex) * 6;
            }
            else
            {
                rawEvidence = DirectionChanges(level?.solution) * 2
                    + Mathf.Min(routeLength, 40)
                    + roomCount * 4
                    + roomReferences * 3
                    + cargo * 2
                    + objects
                    + targets * 2;
            }
            int evidenceBand = Mathf.Clamp(rawEvidence, 0, 89);
            return 650 + campaignIndex * 100 + evidenceBand;
        }

        // Chapter IV source evidence counts only readable room-docking work: route length and
        // turns, connected coordinate spaces, movable room modules and their one-to-one sockets.
        // It deliberately contains no level-number bonus. BuildLevelPrefab later replaces this
        // planning score with ChapterFourDifficultyEvidence, which replays the stored route when
        // the designer runs the generator and verifies that every authored maneuver is real.
        static int ChapterFourSourceDifficulty(LevelDef level)
        {
            int movableRooms = 0;
            int roomSockets = 0;
            int playerTargets = 0;
            if (level?.rooms != null)
                foreach (string[] room in level.rooms)
                    foreach (string row in room)
                        foreach (char ch in row)
                        {
                            if (ch >= '1' && ch <= '9') movableRooms++;
                            if (ch == 'x') roomSockets++;
                            if (ch == 'p') playerTargets++;
                        }

            int turns = 0;
            string route = level?.solution ?? string.Empty;
            for (int i = 1; i < route.Length; i++)
                if (route[i] != route[i - 1]) turns++;

            int extraRooms = Mathf.Max(0, (level?.rooms?.Length ?? 1) - 1);
            return 2000
                   + level.par * 8
                   + turns * 2
                   + extraRooms * 65
                   + movableRooms * 85
                   + playerTargets * 20
                   + roomSockets * 35;
        }

        // The last chapter uses the same evidence terms as any colour puzzle, plus one explicit
        // expert-context baseline. This puts Level 41 just above the Chapter IV finale while all
        // later increases still come from real route turns, deliveries and ordering constraints.
        static int ExpertColourDifficulty(LevelDef level)
            => 2050 + AuthoredDifficulty(level);

        // Solver-proven recursive curriculum.  Difficulty comes from the relationship between a
        // box and the board it contains, not from procedural clutter.  Each chapter adds exactly
        // one layer of spatial recursion, then follows Teach -> Practice -> Combine -> Master.
        // Every stored route below was verified against the same movement model used by Unity.
        static LevelDef[] AuthoredLevels() => new[]
        {
            new LevelDef { name = "First Steps", rooms = new[] { new[] { "#...#", "..#P.", ".....", "#xb.#" } }, par = 3, solution = "DDL" },
            new LevelDef { name = "Little Nudge", rooms = new[] { new[] { "#...#", "....P", "x.b..", "#...#" } }, par = 4, solution = "LDLL" },
            new LevelDef { name = "Around the Bend", rooms = new[] { new[] { "#...#", "#.b..", "#.xP.", "#...#" } }, par = 4, solution = "UULD" },
            new LevelDef { name = "The Long Way", rooms = new[] { new[] { "#.bx#", "..#..", "...P.", "#...#" } }, par = 5, solution = "LLUUR" },
            new LevelDef { name = "Up and Over", rooms = new[] { new[] { "#.bx#", ".....", ".#...", "#.P.#" } }, par = 5, solution = "UULUR" },
            new LevelDef { name = "Zigzag", rooms = new[] { new[] { "#P..#", "...#.", "..b..", "#x..#" } }, par = 6, solution = "RDDRDL" },
            new LevelDef { name = "Company", rooms = new[] { new[] { "#x..#", ".....", ".#b..", "#..P#" } }, par = 6, solution = "LUURUL" },
            new LevelDef { name = "Sidestep", rooms = new[] { new[] { "#..P#", ".....", "..b..", "#x..#" } }, par = 6, solution = "LDDRDL" },
            new LevelDef { name = "Past the Wall", rooms = new[] { new[] { "#...#", "....P", "..b.x", "###.#" } }, par = 6, solution = "LLLDRR" },
            new LevelDef { name = "Tight Corner", rooms = new[] { new[] { "#...#", "..b..", ".....", "#P.x#" } }, par = 7, solution = "UURURDD" },

            new LevelDef { name = "Double Trouble", rooms = new[] { new[] { "#.P...#", "..b1.b.", ".......", ".......", "#.....#" }, new[] { "...", ".x.", "..." } }, par = 6, solution = "LDRRRR" },
            new LevelDef { name = "Crossroads", rooms = new[] { new[] { "#..#..#", "...#...", "1b.##..", "..b....", "#..#P.#" }, new[] { "...", ".x.", "..." } }, par = 6, solution = "ULLULL" },
            new LevelDef { name = "Think Inside", rooms = new[] { new[] { "#.....#", ".......", "..bb...", "..1.P..", "#.....#" }, new[] { "...", ".x.", "..." } }, par = 7, solution = "LUULDDD" },
            new LevelDef { name = "Tuck It In", rooms = new[] { new[] { "#P....#", ".......", "###b###", ".1b....", "#.....#" }, new[] { "...", ".x.", "..." } }, par = 8, solution = "RRDDDLLL" },
            new LevelDef { name = "Special Delivery", rooms = new[] { new[] { "#..#..#", "...#..P", "...#b..", ".1.b...", "#..#..#" }, new[] { "...", ".x.", "..." } }, par = 8, solution = "LLDDLLLL" },
            new LevelDef { name = "Housewarming", rooms = new[] { new[] { "#..P..#", "..b....", "...1...", "...b#..", "#.....#" }, new[] { "...", ".x.", "..." } }, par = 9, solution = "LLDRURDDD" },
            new LevelDef { name = "Pocket", rooms = new[] { new[] { "#..#..#", "...#...", "....bb.", "...#.P.", "#1.#..#" }, new[] { "...", ".x.", "..." } }, par = 9, solution = "ULLLULDDD" },
            new LevelDef { name = "Nesting", rooms = new[] { new[] { "#.....#", ".bb..1.", "#.#####", "....P..", "#.....#" }, new[] { "...", ".x.", "..." } }, par = 10, solution = "LLLUURRRRR" },
            new LevelDef { name = "Roommates", rooms = new[] { new[] { "#..P..#", "...1...", ".......", ".bb....", "#.....#" }, new[] { "...", ".x.", "..." } }, par = 10, solution = "DDLLDRRRRR" },
            new LevelDef { name = "Moving In", rooms = new[] { new[] { "#1.b..#", ".......", "....b..", ".......", "#P....#" }, new[] { "...", ".x.", "..." } }, par = 10, solution = "RRUURUULLL" },

            new LevelDef { name = "Homebound", rooms = new[] { new[] { "#.....#", ".......", "#####.#", "Pb.....", "#..b.1#" }, new[] { "...", ".2.", "..." }, new[] { "...", ".x.", "..." } }, par = 8, solution = "RRDRRRRR" },
            new LevelDef { name = "Split Errand", rooms = new[] { new[] { "#.....#", ".....P.", "###.###", "1.bb...", "#.....#" }, new[] { "...", "2..", "..." }, new[] { "...", ".x.", "..." } }, par = 9, solution = "LLDDLLLLL" },
            new LevelDef { name = "Fetch", rooms = new[] { new[] { "##P..##", "#.....#", "...b...", "#..b..#", "##..1##" }, new[] { "...", "2..", "..." }, new[] { "...", ".x.", "..." } }, par = 10, solution = "RDDLDDRRRR" },
            new LevelDef { name = "Settle Down", rooms = new[] { new[] { "#.....#", ".....1.", ".......", "..bb...", "#.P...#" }, new[] { ".2.", "...", "..." }, new[] { "...", ".x.", "..." } }, par = 11, solution = "URRDRUUUUUU" },
            new LevelDef { name = "Twin Rooms", rooms = new[] { new[] { "##.#.##", "#..#..#", "....b.P", "#.b#..#", "##1#.##" }, new[] { "...", "...", "2.." }, new[] { "...", ".x.", "..." } }, par = 11, solution = "LLLLDDDRDLL" },
            new LevelDef { name = "Rabbit Hole", rooms = new[] { new[] { "#.....#", ".......", "####P##", "..b....", "#.b..1#" }, new[] { "...", "..2", "..." }, new[] { "...", ".x.", "..." } }, par = 11, solution = "DLLLDRRRRRR" },
            new LevelDef { name = "Go Deeper", rooms = new[] { new[] { "#.b.1.#", ".....#.", "...bP..", ".......", "#.....#" }, new[] { "...", ".2.", "..." }, new[] { "...", ".x.", "..." } }, par = 11, solution = "LLLUURRRRRR" },
            new LevelDef { name = "Two Deep", rooms = new[] { new[] { "#.....#", ".1#....", "....bb.", ".......", "#....P#" }, new[] { "...", "...", ".2." }, new[] { "...", ".x.", "..." } }, par = 12, solution = "UULLLDLUUUUU" },
            new LevelDef { name = "Descent", rooms = new[] { new[] { "#.....#", "...#...", "P.b....", ".b.....", "#....1#" }, new[] { "...", "2..", "..." }, new[] { "...", ".x.", "..." } }, par = 13, solution = "RURDDLDRRRRRR" },
            new LevelDef { name = "Double Delivery", rooms = new[] { new[] { "#..P..#", ".1..bb.", ".......", ".......", "#.....#" }, new[] { "..2", "...", "..." }, new[] { "...", ".x.", "..." } }, par = 13, solution = "RRDLLLLDLURUU" },

            new LevelDef { name = "Understudy", rooms = new[] { new[] { "#.Pb..#", "....b..", ".......", "....1..", "#.....#" }, new[] { "...", ".2.", "..." }, new[] { "...", ".3.", "..." }, new[] { "...", ".x.", "..." } }, par = 10, solution = "RRDDDDDDDD" },
            new LevelDef { name = "The Long Game", rooms = new[] { new[] { "#.....#", ".......", "...b.1#", "....b.#", "#....P#" }, new[] { "...", "2..", "..." }, new[] { "...", ".3.", "..." }, new[] { "...", ".x.", "..." } }, par = 10, solution = "LULURRRRRR" },
            new LevelDef { name = "Chain of Custody", rooms = new[] { new[] { "##...##", "#.....#", "....1b.", "#..b..#", "##..P##" }, new[] { "...", ".2.", "..." }, new[] { "...", "..3", "..." }, new[] { "...", ".x.", "..." } }, par = 11, solution = "LULURRRRRRR" },
            new LevelDef { name = "Inner Circle", rooms = new[] { new[] { "#.....#", ".......", "..bP...", "..b.#..", "#...1.#" }, new[] { "...", "2..", "..." }, new[] { "...", "3..", "..." }, new[] { "...", ".x.", "..." } }, par = 12, solution = "LDLDRRRRRRRR" },
            new LevelDef { name = "Layer Cake", rooms = new[] { new[] { "##...##", "#1...##", "...b...", "#..b..#", "##..P##" }, new[] { "...", "2..", "..." }, new[] { "...", "3..", "..." }, new[] { "...", ".x.", "..." } }, par = 12, solution = "LURUULLLLLLL" },
            new LevelDef { name = "Matryoshka", rooms = new[] { new[] { "#1##..#", ".b.....", ".......", ".b.....", "#.P...#" }, new[] { "...", "...", ".2." }, new[] { "..3", "...", "..." }, new[] { "...", ".x.", "..." } }, par = 13, solution = "LUUUUUULUURRR" },
            new LevelDef { name = "Deep Water", rooms = new[] { new[] { "#.P...#", ".b..1b.", "......#", "......#", "#.....#" }, new[] { "...", ".2.", "..." }, new[] { "...", "..3", "..." }, new[] { "...", ".x.", "..." } }, par = 14, solution = "DDLLURRRRRRRRR" },
            new LevelDef { name = "The Undercroft", rooms = new[] { new[] { "#.....#", "....P..", "#b#####", "...b1..", "#.....#" }, new[] { "...", "2..", "..." }, new[] { "...", ".3.", "..." }, new[] { "...", ".x.", "..." } }, par = 14, solution = "LLLDDRRRRRRRRR" },
            new LevelDef { name = "Threefold", rooms = new[] { new[] { "#..#..#", ".b.#...", "b..#.#.", "...1...", "#..#P.#" }, new[] { "...", "2..", "..." }, new[] { "...", "..3", "..." }, new[] { "...", ".x.", "..." } }, par = 15, solution = "ULLLUULDDRDLLLL" },
            new LevelDef { name = "Cascade", rooms = new[] { new[] { "#..P..#", "...bb..", "#..#..1", "#......", "#.....#" }, new[] { "...", "..2", "..." }, new[] { "...", "..3", "..." }, new[] { "...", ".x.", "..." } }, par = 15, solution = "LDRRURDLDRRRRRR" },

            new LevelDef { name = "Recursion", rooms = new[] { new[] { "#..#..#", "P.b1b..", "...#...", "...#...", "#..#..#" }, new[] { ".2.", "...", "..." }, new[] { "...", ".3.", "..." }, new[] { "...", "...", ".4." }, new[] { "...", ".x.", "..." } }, par = 14, solution = "RRRRRDRUUUUUUU" },
            new LevelDef { name = "Event Horizon", rooms = new[] { new[] { "#..#.1#", "...#...", ".....b.", "..P#.b.", "#..#..#" }, new[] { "...", ".2.", "..." }, new[] { "...", "...", ".3." }, new[] { "...", ".4.", "..." }, new[] { "...", ".x.", "..." } }, par = 14, solution = "URRDRUUUUUUUUU" },
            new LevelDef { name = "Turtles All The Way", rooms = new[] { new[] { "##..1##", "#Pb...#", "...b...", "#.....#", "##...##" }, new[] { "...", ".2.", "..." }, new[] { "...", "..3", "..." }, new[] { "...", ".4.", "..." }, new[] { "...", ".x.", "..." } }, par = 14, solution = "RDRULURRRRRRRR" },
            new LevelDef { name = "The Deep End", rooms = new[] { new[] { "##.P.##", "#.b...#", "##1####", "#.b...#", "##...##" }, new[] { "...", "...", ".2." }, new[] { "...", ".3.", "..." }, new[] { "...", "...", "4.." }, new[] { "...", ".x.", "..." } }, par = 15, solution = "LDDDDDDDRDLULDD" },
            new LevelDef { name = "Strange Loop", rooms = new[] { new[] { "##...##", "#.....#", "#P#####", "#.b...#", "##1b.##" }, new[] { "...", "...", ".2." }, new[] { "...", ".3.", "..." }, new[] { "...", "...", ".4." }, new[] { "...", ".x.", "..." } }, par = 15, solution = "DRRRDLLULDDDDDD" },
            new LevelDef { name = "Inward", rooms = new[] { new[] { "##...##", "#..P..#", "##1####", "#...b.#", "##.b.##" }, new[] { "...", "...", ".2." }, new[] { "...", "...", ".3." }, new[] { "...", "...", ".4." }, new[] { "...", ".x.", "..." } }, par = 16, solution = "LDDRRDLLULDDDDDD" },
            new LevelDef { name = "Vanishing Point", rooms = new[] { new[] { "##...##", "#.....#", ".1.b...", "#..Pb.#", "##...##" }, new[] { "...", "..2", "..." }, new[] { "3..", "...", "..." }, new[] { "...", ".4.", "..." }, new[] { "...", ".x.", "..." } }, par = 16, solution = "RULLLLLLLDLUUUUU" },
            new LevelDef { name = "The Last Room", rooms = new[] { new[] { "#.....#", ".Pb....", "###1###", ".......", "#...b.#" }, new[] { "...", ".2.", "..." }, new[] { "...", "..3", "..." }, new[] { "...", "4..", "..." }, new[] { "...", ".x.", "..." } }, par = 17, solution = "RRDDRRDLLLLLLLLLL" },
            new LevelDef { name = "Singularity", rooms = new[] { new[] { "#..P..#", "...#...", ".......", ".b.1...", "#.b...#" }, new[] { "...", ".2.", "..." }, new[] { "...", "..3", "..." }, new[] { "...", ".4.", "..." }, new[] { "...", ".x.", "..." } }, par = 18, solution = "RDDLDLLDRRRRRRRRRR" },
            new LevelDef { name = "Parabox", rooms = new[] { new[] { "#P....#", ".....b.", "......b", ".......", "#...1.#" }, new[] { "...", "...", ".2." }, new[] { "...", ".3.", "..." }, new[] { "...", "...", ".4." }, new[] { "...", ".x.", "..." } }, par = 19, solution = "RRRRDRDLULDDDDDDDDD" },
        };

        // Kept only as a migration reference for old serialized prefabs. The live generator never
        // reads this list; all newly regenerated campaign prefabs use AuthoredLevels above.
        static LevelDef[] LegacyAuthoredLevels() => new[]
        {
            // Starter designs. Levels() ranks these together with the complete authored set so the
            // final chapter divisions are determined by real complexity rather than source order.
            new LevelDef { name = "The Right Way", rooms = new[] { new[] { "#######", "#.....#", "#P>.p.#", "#.....#", "#######" } }, par = 3, solution = "RRR" },
            new LevelDef { name = "First Push", rooms = new[] { new[] { "########", "#....p.#", "#P.bx..#", "#......#", "########" } }, par = 5, solution = "RRURR" },
            // L3: the first crate is taught in L2; this board now asks the player to get behind it
            // before pushing, then finish a route. It is a real practice step, not an easier walk.
            new LevelDef { name = "Set Up the Push", rooms = new[] { new[] { "########", "#....p.#", "#..x...#", "#..b...#", "#P.....#", "########" } }, par = 7, solution = "RRURRUU" },
            // L12: enter the inner board, use the ice, then route around a wall bank. The mechanic
            // is demonstrated cleanly, but the outer-to-inner dependency makes it deeper than L11.
            new LevelDef { name = "Ice Inside", rooms = new[] { new[] { "#########", "#....#..#", "#....#..#", "###..#..#", "#.P.Q...#", "#.......#", "#....##.#", "#....#..#", "#########" }, new[] { "#########", "#......p#", "#..###..#", ".__.....#", "#.......#", "#.......#", "#########" } }, par = 11, solution = "RRRDRRRRUUU" },
            // L16: the crate first fills the trench; the player must then cross that bridge and
            // plan a second route around the inner wall bank.
            new LevelDef { name = "Bridge the Gap", rooms = new[] { new[] { "#########", "#....#..#", "#....#..#", "###..#..#", "#.P.Q...#", "#.......#", "#....##.#", "#....#..#", "#########" }, new[] { "#########", "#......p#", "#..###..#", ".b~.....#", "#.......#", "#..###..#", "#########" } }, par = 13, solution = "RRRRRDRRRRUUU" },
            new LevelDef { name = "Open the Gate", rooms = new[] { new[] { "#########", "#P.b...B#", "#####G###", "#...p...#", "#########" } }, par = 9, solution = "RRRRRLDDL" },
            new LevelDef { name = "Weight Matters", rooms = new[] { new[] { "##########", "#P.......#", "#.########", "#...b.Wp##", "#......H##", "##########" } }, par = 10, solution = "DDRRRRDRRU" },
            new LevelDef { name = "Deep Crossing", rooms = new[] { new[] { "#######", "#..P..#", "#..b..#", "#,.,,,#", "#...x.#", "#######" } }, par = 11, solution = "RDLULDDLDRR" },
            new LevelDef { name = "Pearl Lock", rooms = new[] { new[] { "#########", "#P....k.#", "#.#####.#", "#.......#", "####K####", "###.p.###", "#########" } }, par = 13, solution = "RRRRRRDDLLLDD" },
            new LevelDef { name = "Recursive Turn", rooms = new[] { new[] { "#########", "#.....#.#", "#.....#.#", "###...#.#", "#P.1..#.#", "#.......#", "#....##.#", "#....#..#", "#########" }, new[] { "#######", "#.....#", ".....U#", "#.....#", "#######" }, new[] { "#######", "#.....#", "#.....#", "....b.#", "#.....#", "#...x.#", "#######" } }, par = 16, solution = "RRRRRRRRRRRRURDD" },
            // L11 — familiar cargo, but now with a forced change of pushing side. The wall below
            // the crate prevents the short vertical solution; the exit is taken only after cargo
            // has turned the corner onto its target.
            new LevelDef { name = "The Drift", rooms = new[] { new[] { "###########", "#x.....p..#", "#.........#", "#...b.....#", "#...##....#", "#P........#", "###########" } }, par = 22, solution = "" },

            // L12 — the opening P*~ strip demonstrates the two-cell geyser launch immediately.
            // The landing then feeds a familiar corner-delivery problem rather than another walk.
            new LevelDef { name = "The Geyser", rooms = new[] { new[] { "#############", "#x.........p#", "#...........#", "#...b.......#", "#...#.......#", "#...........#", "#P*~........#", "#############" } }, par = 23, solution = "" },

            // L13 — cargo visibly holds the light button. The open gate drops the diver beside an
            // opposing arrow, making the long lower detour a readable consequence of that rule.
            new LevelDef { name = "Against the Arrow", rooms = new[] { new[] { "###########", "#P.b...B..#", "#####G#####", "#.....<..p#", "#.#######.#", "#.........#", "###########" } }, par = 24, solution = "" },

            // L14 — one cargo piece must remain on the heavy plate while the diver takes the only
            // open lower route. The dependency is unchanged from Chapter I, but the route is not.
            new LevelDef { name = "Dead Weight", rooms = new[] { new[] { "###########", "#P.b.....W#", "#####H#####", "#.........#", "#.#########", "#........p#", "###########" } }, par = 26, solution = "" },

            // L15 — the crate must use the one dry break in the water line. Once it is delivered,
            // the diver swims across the water and takes the separate lower exit.
            new LevelDef { name = "Deep Water", rooms = new[] { new[] { "#############", "#..P........#", "#..b........#", "#,.,,,,,,,..#", "#....x......#", "###########.#", "#......p....#", "#############" } }, par = 27, solution = "" },

            // L16 — both pearls are exposed at opposite ends of one corridor. The central lock
            // cannot open until the player has deliberately visited both sides.
            new LevelDef { name = "Pearl Pair", rooms = new[] { new[] { "###############", "#k.....P.....k#", "#.###########.#", "#.............#", "#######K#######", "#######p#######", "###############" } }, par = 28, solution = "" },

            // L17 — first hold the button, then take the long route to the toggle. The latch and
            // light gate form two consecutive, visibly matched checkpoints before the exit.
            new LevelDef { name = "The Shell Switch", rooms = new[] { new[] { "#############", "#P.b....B...#", "#.#########.#", "#.....T.....#", "######L######", "######G######", "#...........#", "#.#########.#", "#..........p#", "#############" } }, par = 29, solution = "" },

            // L18 — cargo must travel around the long kelp bank, while the diver takes the direct
            // route back through it. The contrast teaches exactly what kelp blocks.
            new LevelDef { name = "Through the Kelp", rooms = new[] { new[] { "###############", "#P...p........#", "#..b..........#", "#.%%%%%%%%%%..#", "#..x..........#", "#.............#", "###############" } }, par = 30, solution = "" },

            // L19 — the downward arrow is the safe entrance. After delivering cargo, the cracked
            // tile is the only return, so crossing it early makes the mistake immediately legible.
            new LevelDef { name = "No Way Back", rooms = new[] { new[] { "###########", "#Pp.......#", "#####c###v#", "#.........#", "#.b.....x.#", "#.........#", "###########" } }, par = 35, solution = "" },

            // L20 — route cargo around the kelp bank to hold the button, then step onto the current.
            // Its uninterrupted line carries the diver to the opened gate and the chapter exit.
            new LevelDef { name = "Undertow", rooms = new[] { new[] { "##################", "#................#", "#P.b.............#", "#%%%%%%%%%%%%%%..#", "#B...............#", "#.ddddddddddddd..#", "###############G##", "#..............p.#", "##################" } }, par = 36, solution = "RRRRRRRRRRRRRURDDRDLLLLLLLLLLLLLLDDD" },
            new LevelDef { name = "Too Narrow", rooms = new[] { new[] { "###########", "#P.b=x....#", "#.#######.#", "#.........#", "#.#######.#", "#p........#", "###########" } }, par = 8, solution = "RRLLDDDD" },
            new LevelDef { name = "Cargo Corridor", rooms = new[] { new[] { "##########", "#........#", "#P.b....x#", "#.######.#", "#........#", "#..p.....#", "##########" } }, par = 9, solution = "RRLLDDRRD" },
            // L8 asks for two deliberate rock breaks before the exit route. Repeating the rule in
            // one board turns L7's single interaction into practice and removes the old difficulty
            // drop without adding an unexplained mechanic.
            new LevelDef { name = "Break Through", rooms = new[] { new[] { "############", "#.........p#", "#..######..#", "#P.bR.bR...#", "#..........#", "############" } }, par = 13, solution = "RRRRRDRRRRUUU" },
            new LevelDef { name = "Throw the Switch", rooms = new[] { new[] { "############", "#P.........#", "#.########.#", "#.....T....#", "#####L######", "#....p.....#", "############" } }, par = 10, solution = "DDRRRRRLDD" },
            new LevelDef { name = "Off Beat", rooms = new[] { new[] { "#############", "#P...:.....p#", "#.###########", "#.###########", "#############" } }, par = 12, solution = "RRRLRRRRRRRR" },
            // L17: teach the run-up in one readable action, then make the player route around the
            // wall bank. It follows L16 without dropping back to a trivial straight finish.
            new LevelDef { name = "Get a Run-Up", rooms = new[] { new[] { "############", "#.........p#", "#..######.##", "#...x......#", "#...O......#", "#...P......#", "#..........#", "############" } }, par = 14, solution = "DUUDRRRRRUUUUR" },
            new LevelDef { name = "Down the Well", rooms = new[] { new[] { "###########", "#P........#", "#.#######.#", "#..b.....g#", "#########g#", "#########g#", "#.#######.#", "#........x#", "###########" } }, par = 13, solution = "DDRRRRRRRRDDD" },
            new LevelDef { name = "Your Shadow", rooms = new[] { new[] { "##########", "#P......p#", "#..####..#", "#E.#....e#", "#...##...#", "#........#", "#........#", "##########" } }, par = 17, solution = "RDDDRDRRRRRUUUUDU" },
            new LevelDef { name = "Colour Coded", rooms = new[] { new[] { "##########", "#........#", "#P.J.N...#", "#........#", "#..n....j#", "##########" } }, par = 21, solution = "RRRURDDLDRRRUULLLULDD" },
            new LevelDef { name = "The Machine", rooms = new[] { new[] { "############", "#P..b..R...#", "##########.#", "#....T.....#", "######L#####", "#.....O....#", "#.....x....#", "#.##########", "#p.........#", "############" } }, par = 27, solution = "RRRRRRRRRDDLLLLLRDDLLLLLDDD" },
            // L6 — the crate must be nudged onto the two-cell updraft, but the wall immediately
            // above the player blocks the easy follow-up. The diver has to take the lower-right
            // switchback, approach the lifted crate from its useful side, and only then deliver it.
            // Six direction changes make this clearly more demanding than L5's five-change route.
            new LevelDef
            {
                name = "Wrong Chimney",
                rooms = new[] { new[]
                {
                    "#########",
                    "#...#...#",
                    "#x......#",
                    "####u##.#",
                    "####ubP.#",
                    "#####...#",
                    "#########"
                } },
                par = 12,
                solution = "LDRURUULLLLL"
            },
            new LevelDef { name = "Facing Away", rooms = new[] { new[] { "##########", "#p......P#", "#........#", "#M...#..m#", "##########" } }, par = 13, solution = "LLLLLDLLURRLL" },
            new LevelDef { name = "One Way In", rooms = new[] { new[] { "##########", "#........#", "#P.b[..x.#", "#........#", "#........#", "##########" } }, par = 13, solution = "RURDLDRRRRDRU" },
            new LevelDef { name = "Carried Past", rooms = new[] { new[] { "########", "###x####", "###b####", "#P;p;..#", "###.##.#", "###....#", "########" } }, par = 14, solution = "RRRRRDDLLLUUUD" },
            // L30 is transformed at parse time into the Chapter III synthesis arena: eight
            // solution-validated wall masses, three direction commitments and two cargo
            // dependencies are layered around this magnetic core. Keeping the concise authored
            // route makes the transformation reproducible while the played board is fully distinct.
            new LevelDef { name = "Magnetic Relay", rooms = new[] { new[] { "#############", "#...........#", "#P...b......#", "#.#########.#", "#.#########.#", "#...........#", "#......x#Y..#", "#############" } }, par = 17, solution = "RRRURRDLLLLULDDDD" },
            new LevelDef { name = "Set in Stone", rooms = new[] { new[] { "#########", "####...P#", "####..q.#", "##.bx#..#", "##......#", "####x####", "#########" } }, par = 18, solution = "LDDRDLLRUULLDDLLUR" },
            // Sand is a spatial constraint rather than a speed bump in a hallway. The central
            // pillar and sand bank force a dry-ground re-approach before the final delivery.
            new LevelDef { name = "Nothing to Brace", rooms = new[] { new[] { "#########", "#.......#", "#...#..P#", "#-b.#...#", "#-..#...#", "#-.x....#", "#########" } }, par = 18, solution = "ULLLLDDDLULURURDDD" },
            // The mirror still inverts every input, but the two wall banks create short alternating
            // commitments instead of a nine-command opening. The cage is crossed during setup,
            // reusing its visual language without turning the board into a long corridor.
            new LevelDef { name = "Opposite Numbers", rooms = new[] { new[] { "#########", "#...p...#", "##.##...#", "#...#...#", "#.m.#...#", "###.[MP.#", "#########" } }, par = 20, solution = "UUUULLLRRRRDLRULLLLR" },
            new LevelDef { name = "Two of Us", rooms = new[] { new[] { "############", "#P........p#", "#..####....#", "#E.#..[...e#", "#...##.....#", "#..........#", "############" } }, par = 23, solution = "RRLDDDRDRRRRUUURULRRRDU" },
            new LevelDef { name = "The Undertow", rooms = new[] { new[] { "##############", "#P..........p#", "#..####......#", "#E.#..[.....e#", "#...##.......#", "#....##......#", "#-..;........#", "##############" } }, par = 25, solution = "RRLDDDRDRDRRRUURULRRRRRUU" },
            new LevelDef { name = "Undertow Chamber", rooms = new[] { new[] { "###########", "#.........#", "#P.b......#", "#%%%%%%%..#", "#B........#", "#.dddddd..#", "########G##", "#.......Q.#", "###########" }, new[] { "####.####", "#.......#", "#..b....#", "...###..#", "#...x...#", "#......p#", "#########" } }, par = 38, solution = "RRRRRRURDDRDLLLLLLLDDDDDLULDDLDRRDRRRR" },
            new LevelDef { name = "Pulse Chamber", rooms = new[] { new[] { "#############", "#P...:.....Q#", "#.###########", "#.###########", "#############" }, new[] { "#########", "#...x...#", "#..###..#", "..b.....#", "#.......#", "#......p#", "#########" } }, par = 31, solution = "DURRRRRRRRRRRDRUULURRLDDDDRRRRR" },
            new LevelDef { name = "Boulder Chamber", rooms = new[] { new[] { "###########", "#.........#", "#..P......#", "#..O......#", "#..x......#", "#.........#", "#........Q#", "###########" }, new[] { "####.####", "#.......#", "#..b....#", "...###..#", "#...x...#", "#......p#", "#########" } }, par = 28, solution = "UDDRDDRRRRRDDDLULDDLDRRDRRRR" },
            new LevelDef { name = "Latch Chamber", rooms = new[] { new[] { "############", "#P.........#", "#.########.#", "#.....T....#", "#####L######", "#....Q.....#", "############" }, new[] { "####.####", "#p......#", "#.#####.#", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#.......#", "#########" } }, par = 36, solution = "DDRRRRRLDDDLLLDDRRRDLULDDLDRRLUUULUU" },
            new LevelDef { name = "Pearl Chamber", rooms = new[] { new[] { "#########", "#P....k.#", "#.#####.#", "#.......#", "####K####", "###.Q.###", "#########" }, new[] { "####.####", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#......p#", "#########" } }, par = 29, solution = "RRRRRRDDLLLDDDDLULDDLDRRDRRRR" },
            new LevelDef { name = "Coral Chamber", rooms = new[] { new[] { "#######", "#PQ...#", "###c#.#", "#.....#", "#.b.x.#", "#.....#", "#######" }, new[] { "#########", "#...x...#", "#..###..#", "..b......", "#.......#", "#......p#", "#########" } }, par = 38, solution = "RRDRUULURRLDDRRRRRRRDDLLDRRURRUULLLLDD" },
            new LevelDef { name = "Weight Chamber", rooms = new[] { new[] { "##########", "#P.b....W#", "#####H####", "#....Q...#", "##########" }, new[] { "####.####", "#p......#", "#.#####.#", "#.......#", "#..b....#", "#..###..#", "#...x...#", "#.......#", "#########" } }, par = 36, solution = "RRRRRRLLDDDLLLDDRRRDLULDDLDRRLUUULUU" },
            new LevelDef { name = "Switch Chamber", rooms = new[] { new[] { "#########", "#P.b...B#", "#####G###", "#...Q...#", "#########" }, new[] { "#########", "#.Y#x...#", "#..###..#", "#.....b..", "#.......#", "#p...c..#", "#########" } }, par = 22, solution = "RRRRRLDDLLDLUUDDDLLLLL" },
            new LevelDef { name = "One-Way Chamber", rooms = new[] { new[] { "#######", "#Q.<.P#", "#.###.#", "#.....#", "#######" }, new[] { "#########", "#.......#", "#...x...#", "#..###u.#", "#....b..#", "#.......#", "#.#####.#", "#......p#", "####.####" } }, par = 34, solution = "DDLLLLUUULLLUUURRRRDRUURULLRDDDRDD" },
            // FINAL SYNTHESIS: the outer machine still checks break -> toggle -> charged boulder
            // before revealing the player-shaped chamber. Inside, two crates share one turning
            // lane around a central divider. The tempting near-side deliveries deadlock the far
            // target, so assignment and order—not corridor length—create the finale difficulty.
            new LevelDef { name = "The Machine Within", rooms = new[] { new[] { "############", "#P..b..R...#", "##########.#", "#....T.....#", "######L#####", "#.....O....#", "#.....x....#", "#.##########", "#Q.........#", "############" }, new[] { "####.####", "#.......#", "#..b#b..#", "#.......#", "#.x.#.x.#", "#......p#", "#########" } }, par = 53, solution = "RRRRRRRRRDDLLLLLRDDLDLLLLDDDRDRDLLLRRUULLDLDRRRURDRDD" },
        };

        // ================================================= level prefabs
        static GameObject[] CreateLevelPrefabs(Tiles t)
        {
            var defs = Levels();
            var result = new GameObject[defs.Length];
            for (int i = 0; i < defs.Length; i++)
                result[i] = BuildLevelPrefab(i, defs[i], t);
            return result;
        }

        static GameObject BuildLevelPrefab(int index, LevelDef def, Tiles t,
                                           string outputPath = null)
        {
            var root = new GameObject("Level_" + (index + 1));
            var levelInfo = root.AddComponent<ParaboxLevel>();
            levelInfo.levelName = def.name;
            levelInfo.par = def.par;   // the move limit is derived from this at runtime
            var progression = CampaignProgression.ForLevel(index);
            levelInfo.difficultyRating = progression.rating;
            levelInfo.designComplexity = def.designComplexity;
            levelInfo.chapter = progression.chapter;
            levelInfo.chapterName = progression.chapterName;
            levelInfo.chapterPhilosophy = progression.philosophy;
            levelInfo.progressionRole = progression.role;
            levelInfo.mechanicFocus = progression.mechanicFocus;
            levelInfo.introducesMechanic = progression.introducesMechanic;
            levelInfo.solution = def.solution;   // solver proof / par validation; never exposed by tutorials

            float offsetX = 0f;
            for (int r = 0; r < def.rooms.Length; r++)
            {
                string[] rows = def.rooms[r];
                int h = rows.Length;
                int w = rows[0].Length;

                var roomGO = new GameObject("Room_" + r);
                roomGO.transform.SetParent(root.transform, false);
                roomGO.transform.localPosition = new Vector3(offsetX + w * 0.5f, 0f, 0f);
                offsetX += w + 2f;

                var rm = roomGO.AddComponent<RoomMarker>();
                rm.roomId = r; rm.width = w; rm.height = h;

                Color floorC = RoomColors[r % RoomColors.Length];

                var floor = (GameObject)PrefabUtility.InstantiatePrefab(t.floor, roomGO.transform);
                floor.transform.localPosition = Vector3.zero;
                var fsr = floor.GetComponent<SpriteRenderer>();
                fsr.size = new Vector2(w, h); fsr.color = floorC;

                var border = (GameObject)PrefabUtility.InstantiatePrefab(t.border, roomGO.transform);
                border.transform.localPosition = Vector3.zero;
                var bsr = border.GetComponent<SpriteRenderer>();
                bsr.size = new Vector2(w, h); bsr.color = Lighten(floorC, 0.32f);

                for (int row = 0; row < h; row++)
                {
                    for (int col = 0; col < rows[row].Length; col++)
                    {
                        char ch = rows[row][col];
                        if (ch == '.') continue;

                        int x = col;
                        int y = h - 1 - row; // top row of the string = highest y
                        var pos = new Vector3(x - (w - 1) * 0.5f, y - (h - 1) * 0.5f, 0f);

                        if (ch == '#')
                        {
                            var go = Place(t.wall, roomGO.transform, pos);
                            var mk = go.AddComponent<WallMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == '~')
                        {
                            // trench: a marker-only object (the board is rendered from the model, so
                            // no tile is needed in the prefab — only the marker LevelParser reads)
                            var go = new GameObject("Trench");
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<TrenchMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == '_')
                        {
                            // ice: marker-only, rendered from the model like the trench/portal
                            var go = new GameObject("Ice");
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<IceMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (ch == 'o')
                        {
                            // whirlpool portal: marker-only, rendered from the model like the trench
                            var go = new GameObject("Portal");
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<PortalMarker>(); mk.x = x; mk.y = y;
                        }
                        else if (TerrainKinds.ContainsKey(ch))
                        {
                            var go = new GameObject(TerrainKinds[ch].ToString());
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;
                            var mk = go.AddComponent<TerrainMarker>();
                            mk.x = x; mk.y = y; mk.kind = TerrainKinds[ch];
                        }
                        else if (ch == '&')
                        {
                            // A compact Chapter 1 dependency reused by the focused Chapter 2
                            // boards: coral's final target is also the green button that holds the
                            // exit gate open. It is one completion task, not a generated extra.
                            var button = new GameObject("ButtonGoal");
                            button.transform.SetParent(roomGO.transform, false);
                            button.transform.localPosition = pos;
                            var switchMarker = button.AddComponent<SwitchMarker>();
                            switchMarker.x = x; switchMarker.y = y; switchMarker.heavy = false;

                            var goal = Place(t.boxGoal, roomGO.transform, pos);
                            var goalMarker = goal.AddComponent<GoalMarker>();
                            goalMarker.x = x; goalMarker.y = y; goalMarker.colour = 1;
                        }
                        else if (CurrentDirs.ContainsKey(ch) || OneWayDirs.ContainsKey(ch)
                                 || ch == 'c' || ch == 'B' || ch == 'G' || ch == 'W' || ch == 'H')
                        {
                            // Every batch-A mechanic is marker-only: the board is drawn from the
                            // model, so the prefab carries just the fact, never a tile.
                            var go = new GameObject(MechName(ch));
                            go.transform.SetParent(roomGO.transform, false);
                            go.transform.localPosition = pos;

                            if (CurrentDirs.TryGetValue(ch, out var cd))
                            {
                                var mk = go.AddComponent<CurrentMarker>();
                                mk.x = x; mk.y = y; mk.dx = cd.x; mk.dy = cd.y;
                            }
                            else if (OneWayDirs.TryGetValue(ch, out var od))
                            {
                                var mk = go.AddComponent<OneWayMarker>();
                                mk.x = x; mk.y = y; mk.dx = od.x; mk.dy = od.y;
                            }
                            else if (ch == 'c')
                            {
                                var mk = go.AddComponent<CrackMarker>(); mk.x = x; mk.y = y;
                            }
                            else if (ch == 'B' || ch == 'W')
                            {
                                var mk = go.AddComponent<SwitchMarker>();
                                mk.x = x; mk.y = y; mk.heavy = ch == 'W';
                            }
                            else
                            {
                                var mk = go.AddComponent<GateMarker>();
                                mk.x = x; mk.y = y; mk.heavy = ch == 'H';
                            }
                        }
                        else if (ColourCargoOnGoals.TryGetValue(ch, out var placedColour))
                        {
                            // Compact authoring symbol for a coloured cargo piece already resting
                            // on its matching mark. Chapter V uses these as a staging line: the
                            // incoming piece shifts every staged cargo before all marks are filled,
                            // so none of these are decorative pre-solved objects.
                            var goal = Place(t.boxGoal, roomGO.transform, pos);
                            var goalMarker = goal.AddComponent<GoalMarker>();
                            goalMarker.x = x; goalMarker.y = y; goalMarker.colour = placedColour;

                            var cargo = Place(t.box, roomGO.transform, pos);
                            var cargoMarker = cargo.AddComponent<BoxMarker>();
                            cargoMarker.x = x; cargoMarker.y = y;
                            cargoMarker.containsRoomId = -1; cargoMarker.colour = placedColour;
                        }
                        else if (CrateKinds.TryGetValue(ch, out var crate))
                        {
                            var go = Place(t.box, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>();
                            mk.x = x; mk.y = y; mk.containsRoomId = -1;
                            mk.colour = crate.colour; mk.slick = crate.slick;
                            mk.boulder = crate.boulder; mk.locking = crate.locking;
                            mk.fragile = crate.fragile;
                        }
                        else if ((ch >= '1' && ch <= '9') || AnchoredBoxes.ContainsKey(ch))
                        {
                            int inner = AnchoredBoxes.TryGetValue(ch, out var aid) ? aid : ch - '0';
                            var go = Place(t.metaBox, roomGO.transform, pos);
                            var mk = go.AddComponent<BoxMarker>();
                            mk.x = x; mk.y = y; mk.containsRoomId = inner;
                            mk.anchored = AnchoredBoxes.ContainsKey(ch);
                            // Every recursive room keeps a depth-coloured miniature. The former
                            // pink player silhouette made anchored chambers indistinguishable and
                            // hid the blue/indigo/violet nesting hierarchy.
                            mk.playerContainer = false;
                            Color inC = RoomColors[inner % RoomColors.Length];
                            var back = go.transform.Find("Backing"); if (back) back.GetComponent<SpriteRenderer>().color = Darken(inC, 0.5f);
                            var frame = go.transform.Find("Frame"); if (frame) frame.GetComponent<SpriteRenderer>().color = Lighten(inC, 0.3f);
                        }
                        else if (ch == 'P' || ch == 'E' || ch == 'M')
                        {
                            var go = Place(t.player, roomGO.transform, pos);
                            var mk = go.AddComponent<PlayerMarker>();
                            mk.x = x; mk.y = y; mk.isEcho = ch == 'E'; mk.isMirror = ch == 'M';
                        }
                        else if (ch == 'x')
                        {
                            var go = Place(t.boxGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>(); mk.x = x; mk.y = y; mk.forPlayer = false;
                        }
                        else if (ch == 'p' || ch == 'e' || ch == 'm')
                        {
                            var go = Place(t.playerGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>();
                            mk.x = x; mk.y = y;
                            mk.forPlayer = ch == 'p'; mk.forEcho = ch == 'e'; mk.forMirror = ch == 'm';
                        }
                        else if (ColourGoals.TryGetValue(ch, out var goalColour))
                        {
                            var go = Place(t.boxGoal, roomGO.transform, pos);
                            var mk = go.AddComponent<GoalMarker>();
                            mk.x = x; mk.y = y; mk.colour = goalColour;
                        }
                    }
                }
            }

            if (outputPath == null)
            {
                try
                {
                    ValidateCampaignMechanicTasks(root, def, index + 1);
                }
                catch
                {
                    // Never leave a failed authoring object inside MainMenu. Besides hiding the
                    // menu, that leaked board could be saved into the scene by accident.
                    if (root != null) UnityEngine.Object.DestroyImmediate(root);
                    throw;
                }
            }

            levelInfo.designComplexity = ReviewedCampaignDifficulty(def, index);

            return SavePrefab(root, outputPath ?? LevelDir + "/Level_" + (index + 1) + ".prefab");
        }

        // Level-string chars for the directional mechanics. WASD reads as "carried this way";
        // the arrows read as "only passable this way". Kept here so the wizard and the Python
        // solver's CURRENT_CHARS / ONEWAY_CHARS stay side by side and obviously identical.
        static readonly Dictionary<char, Vector2Int> CurrentDirs = new Dictionary<char, Vector2Int>
        {
            { 'w', Vector2Int.up }, { 's', Vector2Int.down },
            { 'a', Vector2Int.left }, { 'd', Vector2Int.right },
        };
        static readonly Dictionary<char, Vector2Int> OneWayDirs = new Dictionary<char, Vector2Int>
        {
            { '^', Vector2Int.up }, { 'v', Vector2Int.down },
            { '<', Vector2Int.left }, { '>', Vector2Int.right },
        };

        static readonly Dictionary<char, TerrainKind> TerrainKinds = new Dictionary<char, TerrainKind>
        {
            { '%', TerrainKind.Kelp }, { ',', TerrainKind.Deep },
            { 'k', TerrainKind.Key }, { 'K', TerrainKind.Lock }, { '*', TerrainKind.Geyser },
            { '=', TerrainKind.Gap }, { 'g', TerrainKind.Gravity },
            { 'R', TerrainKind.Rock }, { 'T', TerrainKind.Toggle },
            { 'L', TerrainKind.Latch }, { ':', TerrainKind.Pulse },
            { 'u', TerrainKind.Updraft }, { '[', TerrainKind.Cage },
            { '@', TerrainKind.Deflector }, { '-', TerrainKind.Sand },
            { 'Y', TerrainKind.Magnet }, { ';', TerrainKind.Sticky },
            { '!', TerrainKind.Swap },
        };

        // Meta-boxes that are bolted to the floor: enterable, never pushable. The char stands in
        // for the interior room id, exactly as '1'..'9' do. Must match ANCHORED_BOXES in the
        // Python solver or a level it proved solvable will behave differently in game.
        static readonly Dictionary<char, int> AnchoredBoxes = new Dictionary<char, int>
        {
            { 'Q', 1 }, { 'U', 2 }, { 'V', 3 }, { 'X', 4 },
        };

        // Crates that are more than a crate. Colour > 0 means it has its own matching goal.
        static readonly Dictionary<char, CrateKind> CrateKinds = new Dictionary<char, CrateKind>
        {
            { 'b', new CrateKind() },
            { 'J', new CrateKind { colour = 1 } },
            { 'N', new CrateKind { colour = 2 } },
            { 'Z', new CrateKind { colour = 3 } },
            { 'O', new CrateKind { boulder = true } },
            { 'q', new CrateKind { locking = true } },
            { 'f', new CrateKind { fragile = true } },
        };
        class CrateKind { public int colour; public bool slick, boulder, locking, fragile; }
        static readonly Dictionary<char, int> ColourGoals = new Dictionary<char, int>
        {
            { 'j', 1 }, { 'n', 2 },
            { 'z', 3 },
        };

        // A/C mean cargo-on-matching-goal for compact multi-cargo staging. They create TWO
        // markers at one cell (goal first, cargo above it) and are intentionally kept separate
        // from CrateKinds so object and target evidence both count them.
        static readonly Dictionary<char, int> ColourCargoOnGoals = new Dictionary<char, int>
        {
            { 'A', 1 }, { 'C', 2 },
        };

        static string MechName(char ch)
        {
            if (CurrentDirs.ContainsKey(ch)) return "Current";
            if (OneWayDirs.ContainsKey(ch)) return "OneWay";
            if (ch == 'c') return "Crack";
            if (ch == '=') return "Gap";
            if (ch == 'g') return "Gravity";
            if (ch == 'R') return "Rock";
            if (ch == 'T') return "Toggle";
            if (ch == 'L') return "Latch";
            if (ch == ':') return "Pulse";
            if (ch == 'u') return "Updraft";
            if (ch == '[') return "Cage";
            if (ch == '@') return "Deflector";
            if (ch == '-') return "Sand";
            if (ch == 'Y') return "Magnet";
            if (ch == ';') return "Sticky";
            if (ch == '!') return "Swap";
            if (ch == 'B') return "Button";
            if (ch == 'W') return "Plate";
            if (ch == 'H') return "HeavyGate";
            return "Gate";
        }

        static GameObject Place(GameObject prefab, Transform parent, Vector3 pos)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = pos;
            return go;
        }

        // ================================================= scenes
        static string CreateGameScene(Sprites spr, Tiles t, GameObject[] levels)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = CreateCamera(true);
            var follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.padding = 1.05f;      // board fits the middle band, clear of the top title + bottom controls
            follow.topFrac = 0.16f;
            follow.bottomFrac = 0.20f;

            // A background camera used ONLY during the level-1 cinematic. The tutorial redirects the
            // main camera into a RenderTexture (the panel's video); while that is happening no camera
            // would be left drawing to the display ("No cameras rendering"), so this one clears the
            // screen to dark behind the panel. It renders nothing but its clear (cullingMask 0) and is
            // disabled outside the cinematic, so normal play stays single-camera and unchanged.
            var bgCamGO = new GameObject("TutorialBgCamera");
            var bgCam = bgCamGO.AddComponent<Camera>();
            bgCam.orthographic = true;
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = Hex("03060C");
            bgCam.cullingMask = 0;
            bgCam.depth = -10;           // behind the main camera on the rare frame both are on
            bgCam.nearClipPlane = 0.1f;
            bgCam.farClipPlane = 10f;
            var bgUrp = bgCamGO.AddComponent<UniversalAdditionalCameraData>();
            bgUrp.renderPostProcessing = false;
            bgCam.enabled = false;       // GameManager switches it on for the cinematic only

            // Tier-tinted atmospheric backdrop behind the board (matches the level-select tiers).
            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(cam.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, 20f);
            var bd = backdrop.AddComponent<CameraBackdrop>();
            bd.cam = cam;
            bd.glow1 = MakeWorldSprite(backdrop.transform, "Glow1", spr.glow, -200);
            bd.glow2 = MakeWorldSprite(backdrop.transform, "Glow2", spr.glow, -199);
            bd.vignette = MakeWorldSprite(backdrop.transform, "Vignette", spr.vignette, -198);
            // underwater god-ray shafts slanting down from the surface (subtler as the water deepens)
            bd.rays = new[]
            {
                MakeWorldSprite(backdrop.transform, "Ray0", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray1", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray2", spr.godRay, -197),
            };
            var bdThemes = CategoryThemes();
            bd.baseColors  = new[] { bdThemes[0].bg, bdThemes[1].bg, bdThemes[2].bg };
            bd.glowAColors = new[] { WithAlpha(bdThemes[0].accent, 0f), WithAlpha(bdThemes[1].accent, 0f), WithAlpha(bdThemes[2].accent, 0f) };   // flat background — no nebula wash
            // secondary lower glow per depth — coastal turquoise, reef deep-blue, abyss faint teal.
            bd.glowBColors = new[] { WithAlpha(bdThemes[0].cardBase, 0f), WithAlpha(bdThemes[1].cardBase, 0f), WithAlpha(Hex("1E7AA8"), 0f) };
            bd.rayColors   = new[] { WithAlpha(Hex("BFF4FF"), 0f), WithAlpha(Hex("9FE0FF"), 0f), WithAlpha(Hex("6FD8E0"), 0f) };   // no wave shafts anywhere — clean, non-decorative background
            bd.vignetteAlpha = 0f;
            // editor-preview values (overwritten at runtime to the level's tier)
            cam.backgroundColor = bd.baseColors[0];
            bd.glow1.color = bd.glowAColors[0];
            bd.glow2.color = bd.glowBColors[0];
            bd.vignette.color = new Color(0f, 0f, 0f, bd.vignetteAlpha);
            bd.glow1.transform.localScale = Vector3.one * 20f;
            bd.glow2.transform.localScale = Vector3.one * 17f;
            bd.vignette.transform.localScale = new Vector3(26f, 15f, 1f);
            bd.glow1.transform.localPosition = new Vector3(-4f, 2.6f, 0f);
            bd.glow2.transform.localPosition = new Vector3(4.5f, -3f, 0f);
            for (int i = 0; i < bd.rays.Length; i++)
            {
                bd.rays[i].color = bd.rayColors[0];
                bd.rays[i].transform.localScale = new Vector3(9f, 34f, 1f);
                bd.rays[i].transform.localPosition = new Vector3((i - 1) * 7f, 4f, 0.05f);
                bd.rays[i].transform.localEulerAngles = new Vector3(0f, 0f, (i - 1) * 10f + 6f);
            }

            // Optional drop-in underwater background photo (Assets/Parabox/Art/GameBG.png) behind the board.
            var gamePhoto = LoadGameBgImage();
            if (gamePhoto != null)
            {
                bd.bgPhoto = MakeWorldSprite(backdrop.transform, "GamePhoto", gamePhoto, -206);
                bd.bgPhoto.transform.localScale = new Vector3(30f, 18f, 1f);   // editor preview; view-fit at runtime
                // The selected arcade-chamber art already has a restrained vignette; keep only a
                // near-transparent wash so the authored cyan seams remain visible.
                var photoWash = MakeWorldSprite(backdrop.transform, "PhotoWash", spr.fill, -205);
                photoWash.color = new Color(0.02f, 0.05f, 0.09f, 0.08f);
                photoWash.transform.localScale = new Vector3(200f, 200f, 1f);
            }

            // Small sprite templates for the five chapter identities. AmbientParticles derives the
            // restrained chapter-specific palette/motion at runtime, including Chapters IV and V.
            var ambientGO = new GameObject("Ambient");
            ambientGO.transform.SetParent(cam.transform, false);
            ambientGO.transform.localPosition = new Vector3(0f, 0f, 15f);
            var amb = ambientGO.AddComponent<AmbientParticles>();
            amb.cam = cam;
            amb.tiers = new[]
            {
                new AmbientParticles.Config {   // Coastal Waters — a few calm, bright bubbles rising
                    sprite = spr.ringCircle,
                    palette = new[] { Hex("CFFAFF"), Hex("A4EEF4"), Hex("7FE6DE") },
                    count = 18, sizeRange = new Vector2(0.10f, 0.32f),
                    driftY = 0.80f, swayAmp = 0.28f, swaySpeed = 1.2f, spin = 0f, baseAlpha = 0.32f,
                },
                new AmbientParticles.Config {   // Coral Reef & Deep Ocean — light bubble drift
                    sprite = spr.ringCircle,
                    palette = new[] { Hex("8FE0F0"), Hex("B4F0FF"), Hex("6FC8E0") },
                    count = 22, sizeRange = new Vector2(0.10f, 0.36f),
                    driftY = 0.90f, swayAmp = 0.34f, swaySpeed = 1.35f, spin = 0f, baseAlpha = 0.34f,
                },
                new AmbientParticles.Config {   // Ocean Abyss — sparse bioluminescent motes flickering in the dark
                    sprite = spr.disc,
                    palette = new[] { Hex("46E8D0"), Hex("7FD8FF"), Hex("B4F0FF") },
                    count = 20, sizeRange = new Vector2(0.05f, 0.20f),
                    driftY = 0.40f, swayAmp = 0.18f, swaySpeed = 0.9f, spin = 0f, baseAlpha = 0.60f, flicker = true,
                },
            };
            // Particles render behind the board and remain deliberately sparse, so they add chapter
            // identity without competing with puzzle symbols.
            ambientGO.SetActive(true);

            CreateEventSystem();
            CreateMusicPlayer();

            var canvas = CreateCanvas("HUDCanvas");

            // clean centered header — title + moves/best, balanced with the board, off the edges.
            // both get a dark outline so they stay legible on any board colour or background photo.
            var levelLabel = MakeText(canvas, "LevelLabel", "Level 1", 34, Color.white,
                new Vector2(0, -56), new Vector2(1000, 46), FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            var levelOutline = levelLabel.gameObject.AddComponent<Outline>();
            levelOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.85f);
            levelOutline.effectDistance = new Vector2(2f, -2f);

            var movesLabel = MakeText(canvas, "MovesLabel", "MOVES  0", 24, Color.white,
                new Vector2(0, -100), new Vector2(700, 32), FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1));
            var movesOutline = movesLabel.gameObject.AddComponent<Outline>();
            movesOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.8f);
            movesOutline.effectDistance = new Vector2(1.5f, -1.5f);

            // premium circular countdown timer — SAME design, moved to the top-RIGHT corner so it
            // no longer overlaps the centered game board. Colour follows the level's tier.
            var timerRoot = MakeRect(canvas, "Timer", new Vector2(-104, -104), new Vector2(120, 120),
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
            var timerCanvas = timerRoot.gameObject.AddComponent<CanvasGroup>();
            UIImage(timerRoot, "Shadow", spr.glow, new Color(0f, 0f, 0f, 0.40f), new Vector2(0f, -6f), new Vector2(150, 150));
            UIImage(timerRoot, "Backing", spr.disc, Hex("141A28"), Vector2.zero, new Vector2(112, 112));
            UIImage(timerRoot, "Track", spr.ringCircle, Hex("2A3350"), Vector2.zero, new Vector2(120, 120));
            var fillRT = MakeRect(timerRoot, "Fill", Vector2.zero, new Vector2(120, 120));
            var timerFill = fillRT.gameObject.AddComponent<Image>();
            timerFill.sprite = spr.ringCircle; timerFill.color = Accent; timerFill.raycastTarget = false;
            timerFill.type = Image.Type.Filled;
            timerFill.fillMethod = Image.FillMethod.Radial360;
            timerFill.fillOrigin = (int)Image.Origin360.Top;
            timerFill.fillClockwise = true;
            timerFill.fillAmount = 1f;
            var timerLabel = MakeText(timerRoot, "TimerText", "16", 42, Color.white,
                Vector2.zero, new Vector2(120, 120), FontStyle.Bold);

            // ---- clean control bar, grouped and centered below the board ----
            var barAccent = Hex("46CEE0");
            var bar = MakeRect(canvas, "ControlBar", new Vector2(0, 100), new Vector2(1400, 150),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f));

            // directional pad (left) — these MOVE the player
            var dsz = new Vector2(48, 48);
            var upBtn    = MakeFlatButton(bar, "BtnUp",    "▲", 24, new Vector2(-380, 34),  dsz, barAccent, spr);
            var downBtn  = MakeFlatButton(bar, "BtnDown",  "▼", 24, new Vector2(-380, -22), dsz, barAccent, spr);
            var leftBtn  = MakeFlatButton(bar, "BtnLeft",  "◀", 24, new Vector2(-436, 4),   dsz, barAccent, spr);
            var rightBtn = MakeFlatButton(bar, "BtnRight", "▶", 24, new Vector2(-324, 4),   dsz, barAccent, spr);
            MakeKeyHint(bar, "MOVE", new Vector2(-380, -64));

            // action buttons (right) — uniform labelled pills, wired to the real functions
            var asz = new Vector2(150, 56);
            var undoBtn    = MakeFlatButton(bar, "BtnUndo",    "UNDO",    22, new Vector2(-150, 2), asz, barAccent, spr);
            var restartBtn = MakeFlatButton(bar, "BtnRestart", "RESTART", 22, new Vector2(20, 2),   asz, barAccent, spr);
            var muteBtn    = MakeFlatButton(bar, "BtnMute",    null,      22, new Vector2(190, 2),  asz, barAccent, spr);
            var muteLbl    = MakeText(muteBtn.transform, "MuteLbl",  "MUTE",  22, Color.white,   Vector2.zero, asz, FontStyle.Bold);
            var mutedLbl   = MakeText(muteBtn.transform, "MutedLbl", "MUTED", 22, Hex("F2A6A6"), Vector2.zero, asz, FontStyle.Bold);
            mutedLbl.gameObject.SetActive(false);
            var menuHudBtn = MakeFlatButton(bar, "BtnMenu",    "LEVELS",  22, new Vector2(360, 2),  asz, barAccent, spr);

            // keycap hints under each action button — the keyboard key that triggers it
            MakeKeyHint(bar, "Z",   new Vector2(-150, -42));
            MakeKeyHint(bar, "R",   new Vector2(20, -42));
            MakeKeyHint(bar, "M",   new Vector2(190, -42));
            MakeKeyHint(bar, "ESC", new Vector2(360, -42));

            // on a seamless dive-in, reveal the HUD a beat after the board (fade + the control bar slides up)
            var hudGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            var hudReveal = canvas.gameObject.AddComponent<HudReveal>();
            hudReveal.group = hudGroup;
            hudReveal.slide = bar;

            // ---- level-1 cinematic onboarding (raised video panel) -----------------------
            // The tutorial video is a prebuilt, spoiler-free mechanic vignette shown inside a
            // premium raised card: a soft drop shadow + polished frame over a dimmed background.
            // It uses its OWN canvas
            // (above the HUD, own raycaster) so hiding the gameplay HUD never touches it. Built inert
            // (scrim + panel invisible, choice non-interactive) so levels 2-50 show nothing.
            var cineTf = CreateCanvas("CinematicCanvas");
            cineTf.GetComponent<Canvas>().sortingOrder = 100;
            var cine = cineTf;

            // dimmed background — its own CanvasGroup so only the scrim fades, not the panel on top
            var scrimRT = MakeRect(cine, "Scrim", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var scrimImg = scrimRT.gameObject.AddComponent<Image>();
            scrimImg.color = Hex("03060C");
            scrimImg.raycastTarget = false;   // the dim background must never absorb a click
            // a soft lit centre behind the card so the dark background reads as depth, not flat black
            UIImage(scrimRT, "BgGlow", spr.glow, WithAlpha(Hex("123648"), 0.55f), new Vector2(0, 60), new Vector2(1500, 1000));
            var scrimGroup = scrimRT.gameObject.AddComponent<CanvasGroup>();
            scrimGroup.alpha = 0f;

            // the raised card
            const float cardW = 1100f, cardH = 620f, vidW = 1040f, vidH = 585f;   // video is 16:9
            var videoPanelRT = MakeRect(cine, "VideoPanel", new Vector2(0, 50), new Vector2(cardW, cardH));
            var panelGroup = videoPanelRT.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            // soft drop shadow (elevation)
            UIImage(videoPanelRT, "Shadow", spr.glow, WithAlpha(Hex("00030A"), 0.55f), new Vector2(0, -22), new Vector2(cardW + 220f, cardH + 220f));
            // card body
            var cardImg = UIImage(videoPanelRT, "Card", spr.fill, Hex("0C2036"), Vector2.zero, new Vector2(cardW, cardH));
            cardImg.type = Image.Type.Sliced;
            // dark video backing; PrebuildStaticUi serializes the mechanic vignette above it
            var vidRT = MakeRect(videoPanelRT, "Video", Vector2.zero, new Vector2(vidW, vidH));
            var videoImg = vidRT.gameObject.AddComponent<RawImage>();
            videoImg.color = Color.white;
            // polished frame: a bright inner edge + a soft outer ring for depth
            AddRing(videoPanelRT, "Frame", new Vector2(vidW + 10f, vidH + 10f), spr.ringThin, WithAlpha(Hex("6FE4F2"), 0.28f));

            // one short line, seated in the card's lower strip
            var capRT = MakeRect(videoPanelRT, "CineCaption", new Vector2(0, -cardH * 0.5f + 52f), new Vector2(vidW - 40f, 44),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var capGroup = capRT.gameObject.AddComponent<CanvasGroup>();
            capGroup.alpha = 0f; capGroup.blocksRaycasts = false;
            var capText = AddTextShadow(MakeText(capRT, "CaptionText", "", 28, Hex("EAFBFF"),
                Vector2.zero, new Vector2(vidW - 40f, 44), FontStyle.Bold));
            var capOutline = capText.gameObject.AddComponent<Outline>();
            capOutline.effectColor = new Color(0f, 0.05f, 0.09f, 0.9f);
            capOutline.effectDistance = new Vector2(2f, -2f);

            // Compact first-appearance lessons use their own serialized card. The cinematic
            // caption lives under an invisible video panel, so reusing it made World-1 lessons
            // appear only after the player had already encountered their obstacles.
            var briefRT = MakeRect(cine, "MechanicBriefingPanel", new Vector2(0, 285),
                new Vector2(1040, 164));
            var briefImage = briefRT.gameObject.AddComponent<Image>();
            briefImage.sprite = spr.fill;
            briefImage.type = Image.Type.Sliced;
            briefImage.color = Color.white;
            briefImage.raycastTarget = false;
            var briefGradient = briefRT.gameObject.AddComponent<UIGradient>();
            briefGradient.top = Hex("18375A");
            briefGradient.bottom = Hex("040B17");
            var briefOutline = briefRT.gameObject.AddComponent<Outline>();
            briefOutline.effectColor = WithAlpha(Hex("46CEE0"), 0.88f);
            briefOutline.effectDistance = new Vector2(2f, -2f);
            var briefShadow = briefRT.gameObject.AddComponent<Shadow>();
            briefShadow.effectColor = new Color(0f, 0f, 0.02f, 0.78f);
            briefShadow.effectDistance = new Vector2(0f, -9f);
            var briefGroup = briefRT.gameObject.AddComponent<CanvasGroup>();
            briefGroup.alpha = 0f;
            briefGroup.interactable = false;
            briefGroup.blocksRaycasts = false;
            UIImage(briefRT, "LeftAccent", spr.fill, Hex("B76CFF"), new Vector2(-504, 0),
                new Vector2(8, 126));
            var briefTitle = MakeText(briefRT, "BriefingTitle", "NEW MECHANIC", 22,
                Hex("83EDFF"), new Vector2(0, 45), new Vector2(970, 36), FontStyle.Bold);
            var briefText = MakeText(briefRT, "BriefingText", string.Empty, 27, Color.white,
                new Vector2(0, -17), new Vector2(970, 82), FontStyle.Bold);
            briefText.resizeTextForBestFit = true;
            briefText.resizeTextMinSize = 18;
            briefText.resizeTextMaxSize = 27;
            briefText.horizontalOverflow = HorizontalWrapMode.Wrap;
            briefText.verticalOverflow = VerticalWrapMode.Truncate;
            var briefHint = MakeText(briefRT, "SkipHint", "PURPLE 6 / RB  •  SKIP", 16,
                Hex("B76CFF"), new Vector2(390, -63), new Vector2(190, 24), FontStyle.Bold);
            briefHint.alignment = TextAnchor.MiddleRight;

            // Repeat/Try live below the card after the video. Purple button 6 stays in the
            // centred area below the card throughout the tutorial so it never covers the lesson.
            var choiceRT = MakeRect(cine, "CineChoice", new Vector2(0, -410), new Vector2(900, 120),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var choiceGroup = choiceRT.gameObject.AddComponent<CanvasGroup>();
            choiceGroup.alpha = 0f; choiceGroup.interactable = false; choiceGroup.blocksRaycasts = false;
            var againBtn = MakeButton(choiceRT, "TutAgain", "Watch Again", new Vector2(-290, 0), new Vector2(232, 64), 21, Hex("2A5570"));
            MenuButtonGlow(choiceRT, new Vector2(0, 0), new Vector2(272, 80), Hex("46D8C0"), spr);
            var tryBtn = MakeButton(choiceRT, "TutTry", "Try It Yourself", new Vector2(0, 0), new Vector2(272, 80), 25, Hex("4CE2C8"), Hex("2AA890"));
            var skipBtn = MakeButton(cine, "TutorialSkip", "SKIP TUTORIAL", new Vector2(0, -410),
                new Vector2(310, 72), 20, Hex("B76CFF"), Hex("5A2297"));
            var skipRT = (RectTransform)skipBtn.transform;
            skipRT.anchorMin = skipRT.anchorMax = new Vector2(0.5f, 0.5f);
            skipRT.pivot = new Vector2(0.5f, 0.5f);
            skipRT.anchoredPosition = new Vector2(0f, -410f);
            Text skipLabel = null;
            foreach (Text candidate in skipBtn.GetComponentsInChildren<Text>(true))
                if (candidate.name == "Label") { skipLabel = candidate; break; }
            if (skipLabel != null)
            {
                skipLabel.rectTransform.anchorMin = Vector2.zero;
                skipLabel.rectTransform.anchorMax = Vector2.one;
                skipLabel.rectTransform.anchoredPosition = Vector2.zero;
                skipLabel.rectTransform.sizeDelta = Vector2.zero;
            }
            Image numberBadge = UIImage(skipRT, "ButtonNumberBadge", spr.disc, Hex("5A2297"),
                new Vector2(-116f, 0f), new Vector2(48f, 48f));
            var numberOutline = numberBadge.gameObject.AddComponent<Outline>();
            numberOutline.effectColor = WithAlpha(Hex("E5C7FF"), 0.95f);
            numberOutline.effectDistance = new Vector2(2f, -2f);
            var buttonNumber = MakeText(numberBadge.transform, "ButtonNumber", "6", 26, Color.white,
                Vector2.zero, new Vector2(48f, 48f), FontStyle.Bold);
            buttonNumber.alignment = TextAnchor.MiddleCenter;
            var skipCountdown = MakeText(skipRT, "SkipCountdown", "AUTO-CONTINUE IN 10", 18,
                Hex("D1ADFF"), new Vector2(0f, 52f), new Vector2(310f, 30f), FontStyle.Bold);
            var countdownOutline = skipCountdown.gameObject.AddComponent<Outline>();
            countdownOutline.effectColor = new Color(0.01f, 0.02f, 0.06f, 0.90f);
            countdownOutline.effectDistance = new Vector2(1.5f, -1.5f);
            skipCountdown.gameObject.SetActive(false);
            skipBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            skipBtn.gameObject.SetActive(false);

            var tutFx = cine.gameObject.AddComponent<TutorialFx>();
            tutFx.scrimGroup = scrimGroup;
            tutFx.panelGroup = panelGroup;
            tutFx.panelRT = videoPanelRT;
            tutFx.videoImage = videoImg;
            tutFx.captionGroup = capGroup;
            tutFx.captionText = capText;
            tutFx.briefingGroup = briefGroup;
            tutFx.briefingRT = briefRT;
            tutFx.briefingTitleText = briefTitle;
            tutFx.briefingText = briefText;
            tutFx.choiceGroup = choiceGroup;
            tutFx.choiceRT = choiceRT;
            tutFx.againButton = againBtn;
            tutFx.tryButton = tryBtn;
            tutFx.skipButton = skipBtn;
            tutFx.skipCountdownText = skipCountdown;

            // win panel — premium "Level Complete" celebration
            var panelGO = new GameObject("WinPanel", typeof(RectTransform));
            panelGO.transform.SetParent(canvas, false);
            var panelRT = (RectTransform)panelGO.transform;
            panelRT.anchorMin = Vector2.zero; panelRT.anchorMax = Vector2.one; panelRT.sizeDelta = Vector2.zero;
            panelGO.AddComponent<Image>().color = new Color(0.01f, 0.03f, 0.06f, 0.88f);   // dims the board so the panel pops

            // celebration glow: a big soft burst + a tighter bright halo, both gently pulsing
            var winBurst = UIImage(panelGO.transform, "WinBurst", spr.glow, WithAlpha(Hex("46E0D8"), 0.5f), Vector2.zero, new Vector2(1180, 860));
            winBurst.gameObject.AddComponent<UIPulse>();
            var winHalo = UIImage(panelGO.transform, "WinHalo", spr.glow, WithAlpha(Hex("7FF2E6"), 0.4f), Vector2.zero, new Vector2(760, 560));
            winHalo.gameObject.AddComponent<UIPulse>();

            // subtle ambient light motes drifting behind the panel — low density, low opacity, fade in/out
            var winAmbRT = MakeRect(panelGO.transform, "WinParticles", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var winAmb = winAmbRT.gameObject.AddComponent<UIAmbient>();
            winAmb.sprite = spr.glow;
            winAmb.palette = new[] { Hex("7FF2E6"), Hex("A9F0F0"), Hex("FFFFFF"), Hex("62ECE0") };
            winAmb.count = 20;
            winAmb.sizeRange = new Vector2(5f, 16f);
            winAmb.riseSpeed = 11f;   // slow drift
            winAmb.sway = 22f;
            winAmb.baseAlpha = 0.22f; // very low opacity

            // one-shot celebration burst — glowing dots, star sparkles & light streaks fly outward on win
            var winFxRT = MakeRect(panelGO.transform, "WinBurstFx", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var winFx = winFxRT.gameObject.AddComponent<UIBurst>();
            winFx.glowSprite = spr.glow;
            winFx.sparkSprite = spr.star;
            winFx.palette = new[] { Hex("62ECE0"), Hex("46E0D8"), Hex("58B8F0"), Hex("BFF4FF"), Hex("FFFFFF") };
            winFx.count = 28;

            var windowRT = MakeRect(panelGO.transform, "Window", Vector2.zero, new Vector2(600, 384));
            var windowImg = windowRT.gameObject.AddComponent<Image>();
            windowImg.sprite = spr.fill; windowImg.type = Image.Type.Sliced; windowImg.color = Hex("0C2740");
            windowRT.gameObject.AddComponent<UIPopIn>(); // pops in when the win panel shows
            AddRing(windowRT, "WinOutline", new Vector2(600, 384), spr.ringThin, WithAlpha(Hex("62ECE0"), 1f));
            AddRing(windowRT, "WinOutline2", new Vector2(624, 408), spr.ringThin, WithAlpha(Hex("62ECE0"), 0.4f));

            // three premium layered stars (warm glow halo + gold star + white shine) that cascade in on win
            for (int i = 0; i < 3; i++)
            {
                var starRT = MakeRect(windowRT, "WinStar" + i, new Vector2((i - 1) * 92f, 122f), new Vector2(92, 92));
                UIImage(starRT, "Glow", spr.glow, WithAlpha(Hex("FFE7A6"), 0.6f), Vector2.zero, new Vector2(126, 126));
                UIImage(starRT, "Star", spr.star, Hex("FFC838"), Vector2.zero, new Vector2(88, 88));
                UIImage(starRT, "Shine", spr.star, WithAlpha(Hex("FFF7DC"), 0.92f), new Vector2(0, 5), new Vector2(46, 46));
                starRT.gameObject.AddComponent<StarPop>().delay = 0.16f + i * 0.14f;
            }

            var winTitle = MakeText(windowRT, "WinTitle", "LEVEL COMPLETE", 48, Hex("EAFBFF"),
                new Vector2(0, 52f), new Vector2(560, 60), FontStyle.Bold);
            var winTitleOutline = winTitle.gameObject.AddComponent<Outline>();
            winTitleOutline.effectColor = new Color(0f, 0.14f, 0.20f, 0.9f); winTitleOutline.effectDistance = new Vector2(2f, -2f);

            var winStats = MakeText(windowRT, "WinStats", "TOTAL SCORE  0 / 0", 24, Hex("C4ECF4"),
                new Vector2(0, 2f), new Vector2(560, 40));

            // One centred primary action. The result-board border supplies the automatic-next
            // countdown; the retired local Levels action remains only as an inactive compatibility
            // reference for older scene data.
            MenuButtonGlow(windowRT, new Vector2(0, -108), new Vector2(310, 76), Hex("46D8C0"), spr);
            var nextBtn = MakeButton(windowRT, "NextButton", "NEXT LEVEL", new Vector2(0, -108), new Vector2(310, 76), 26, Hex("4CE2C8"), Hex("2AA890"));
            var menuBtn = MakeButton(windowRT, "MenuButton", "LEVELS", new Vector2(140, -108), new Vector2(206, 68), 24, ButtonCol);
            menuBtn.interactable = false;
            menuBtn.gameObject.SetActive(false);

            panelGO.SetActive(false);

            // time-up panel (shown when the countdown hits zero)
            // ---- lose sequence -------------------------------------------------------------
            // Deliberately NOT a window on a scrim. The old panel was a 560x340 grey card that
            // popped in instantly, which reads as a dialog thrown over the game. Here the verdict
            // and the options sit directly on the dimmed board, and LoseFx times them in.
            var tuGO = new GameObject("LosePanel", typeof(RectTransform));
            tuGO.transform.SetParent(canvas, false);
            var tuRT = (RectTransform)tuGO.transform;
            tuRT.anchorMin = Vector2.zero; tuRT.anchorMax = Vector2.one; tuRT.sizeDelta = Vector2.zero;

            // full-screen dim on its own group, so the board can drain independently of the text
            var dimRT = MakeRect(tuGO.transform, "Dim", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var dimImg = dimRT.gameObject.AddComponent<Image>();
            dimImg.color = Hex("03080F");
            var dimCG = dimRT.gameObject.AddComponent<CanvasGroup>();

            // the verdict — text set per failure kind at runtime ("OUT OF MOVES" / "TIME'S UP")
            var titleHolder = MakeRect(tuGO.transform, "TitleGroup", new Vector2(0f, 40f), new Vector2(900f, 150f));
            var titleCG = titleHolder.gameObject.AddComponent<CanvasGroup>();
            var loseTitle = AddTextShadow(MakeText(titleHolder, "LoseTitle", "OUT OF MOVES", 62, Hex("FF6B6B"),
                new Vector2(0f, 26f), new Vector2(880f, 78f), FontStyle.Bold));
            // sits clear of BOTH the title above and the buttons below — 4px of air under a 62pt
            // title is not air, it's a collision waiting to be reported
            var loseSub = AddTextShadow(MakeText(titleHolder, "LoseSub", "", 24, Hex("A8C2D4"),
                new Vector2(0f, -46f), new Vector2(880f, 36f)));

            // particle burst behind the verdict — UIBurst fires the moment it's enabled
            var burstRT = MakeRect(tuGO.transform, "LoseBurst", new Vector2(0f, 40f), new Vector2(10f, 10f));
            var loseBurst = burstRT.gameObject.AddComponent<UIBurst>();
            loseBurst.glowSprite = spr.glow;
            loseBurst.sparkSprite = spr.star;
            loseBurst.palette = new[] { Hex("FF6B6B"), Hex("FF9E5E"), Hex("6E7A8A") };
            loseBurst.count = 22;
            loseBurst.speed = 520f;
            loseBurst.life = 1.0f;
            loseBurst.baseAlpha = 0.7f;
            burstRT.gameObject.SetActive(false);   // LoseFx enables it on the beat

            // full-screen impact flash, ABOVE the dim but below the text
            var pulseRT = MakeRect(tuGO.transform, "Pulse", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var pulseImg = pulseRT.gameObject.AddComponent<Image>();
            pulseImg.color = new Color(1f, 0.30f, 0.28f, 0f);
            pulseImg.raycastTarget = false;
            pulseRT.SetSiblingIndex(1);   // straight after Dim

            var loseFx = tuGO.AddComponent<LoseFx>();
            loseFx.dim = dimCG;
            loseFx.pulse = pulseImg;
            loseFx.titleGroup = titleCG;
            loseFx.titleRT = titleHolder;
            loseFx.titleText = loseTitle;
            loseFx.subText = loseSub;
            loseFx.burst = burstRT.gameObject;
            loseFx.leaderboardPanelSprite = spr.fill;
            loseFx.shakeTarget = follow;   // the board jolts via the gameplay camera
            tuGO.SetActive(false);

            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.levelPrefabs = levels;
            gm.floorPrefab = t.floor;
            gm.borderPrefab = t.border;
            gm.wallPrefab = t.wall;
            gm.boxPrefab = t.box;
            gm.metaBoxPrefab = t.metaBox;
            gm.playerPrefab = t.player;
            gm.boxGoalPrefab = t.boxGoal;
            gm.playerGoalPrefab = t.playerGoal;
            gm.gridPrefab = t.grid;
            gm.roomColors = RoomColors;
            gm.boxColor = BoxColor;
            gm.playerColor = PlayerColor;
            gm.pieceSprite = spr.fill;
            gm.ringSprite = spr.ringThick;
            gm.glowSprite = spr.glow;
            gm.vignetteSprite = spr.vignette;
            gm.cellSprite = spr.cell;
            gm.cameraFollow = follow;
            gm.tutorialBgCamera = bgCam;
            gm.levelLabel = levelLabel;
            gm.movesLabel = movesLabel;
            gm.tutorialFx = tutFx;
            gm.hudGroup = hudGroup;
            gm.hudReveal = hudReveal;
            gm.winPanel = panelGO;
            gm.winTitle = winTitle;
            gm.winStats = winStats;
            gm.nextButton = nextBtn;
            gm.menuButton = menuBtn;
            gm.timerRoot = timerRoot;
            gm.timerCanvas = timerCanvas;
            gm.timerFill = timerFill;
            gm.timerLabel = timerLabel;
            gm.timerAccents = new[] { bdThemes[0].accent, bdThemes[1].accent, bdThemes[2].accent };
            // All tiers use ONE board skin that matches the title screen's O board:
            // blue gradient-ish floor, bright cyan neon frame, orange box / yellow player.
            gm.levelThemes = ChapterThemes();   // 3 evolving chapters (Levels 1-10 / 11-20 / 21-30)
            gm.timeUpPanel = tuGO;
            gm.loseFx = loseFx;

            // on-screen control bar
            gm.upButton = upBtn; gm.downButton = downBtn; gm.leftButton = leftBtn; gm.rightButton = rightBtn;
            gm.undoButton = undoBtn; gm.restartButton = restartBtn; gm.muteButton = muteBtn; gm.hudMenuButton = menuHudBtn;
            gm.muteOnIcon = muteLbl.gameObject; gm.muteOffIcon = mutedLbl.gameObject;

            var gameFade = CreateFade(canvas, Hex("2A5E92"));   // game emerges from the board-blue the O zoomed into
            gm.screenFade = gameFade;   // ...and fades back OUT to it when the level is done

            string path = SceneDir + "/Game.unity";
            LuxoddArcadeUiSceneBaker.BakeScene(scene);
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        // Three evolving board chapters (Levels 1-10 / 11-20 / 21-30): bright clean intro → darker richer
        // journey → deepest premium master challenge. Same ocean art direction (cyan frame, warm pieces,
        // square-cell grid) with monotonically rising floor-depth, cell contrast, lighting, and border
        // identity. Palettes were contrast-verified (piece/cell/frame/wall readability + progression).
        // Five regions, one continuous descent.
        //
        // The FRAME is each chapter's identity: it paints the board's rim in-game and, on the map,
        // every node border, the current-level ring, the path dots, the zone wash, the chapter
        // label and the gate. Get it wrong and a whole chapter has no colour of its own.
        //
        // It was wrong. Chapters 1-3 sat at 181-188 degrees of hue — three shades of the same cyan —
        // so with every node beaten (orange, spread over a mere 12 degrees) the first three chapters
        // were literally indistinguishable. The hues now step 168 -> 196 -> 224 -> 262 -> 302:
        // 28 degrees minimum between any two chapters, monotonic, so the descent reads as a journey
        // from green-cyan shallows to magenta endgame. Every frame is >= 3:1 against its own tile
        // (bought with brightness, not by desaturating into pastel).
        static LevelTheme[] ChapterThemes() => new[]
        {
            // 1 — Easy. Sunlit shallows: simple and welcoming.
            new LevelTheme {
                roomColors = new[] { Hex("2B79B4"), Hex("3488C4"), Hex("24689C"), Hex("3E96D2"), Hex("2F7FBA") },
                gutter = Hex("11304A"),
                box = Hex("F5A24E"), player = Hex("F6C453"), wall = Hex("0A1E30"), frame = Hex("0CF0C2"),
                grid = new Color(0f, 0f, 0f, 0.09f), floorVignette = 0.05f, pieceGlow = 0f, cellLift = 0.12f,
                floorTexTint = Color.clear,
            },
            // 2 — Medium. Deeper water: more adventurous.
            new LevelTheme {
                roomColors = new[] { Hex("226B9E"), Hex("2A7AB0"), Hex("1C5C88"), Hex("3389C0"), Hex("2470A4") },
                gutter = Hex("0C2438"),
                box = Hex("F0812E"), player = Hex("F8C13C"), wall = Hex("07182A"), frame = Hex("40CCFF"),
                grid = new Color(0f, 0f, 0f, 0.10f), floorVignette = 0.08f, pieceGlow = 0f, cellLift = 0.14f,
                floorTexTint = Color.clear,
            },
            // 3 — Hard. The abyss: challenging and important.
            new LevelTheme {
                roomColors = new[] { Hex("1A5578"), Hex("216488"), Hex("154868"), Hex("2A7398"), Hex("1D5C80") },
                gutter = Hex("071A28"),
                box = Hex("EF7A22"), player = Hex("FBC532"), wall = Hex("04101C"), frame = Hex("7D9EFA"),
                grid = new Color(0f, 0f, 0f, 0.11f), floorVignette = 0.11f, pieceGlow = 0f, cellLift = 0.16f,
                floorTexTint = Color.clear,
            },
            // 4 — Expert. Trench: colder, more prestigious. The violet is the first hue shift in
            // the whole game, so arriving here reads as leaving the ocean you knew.
            new LevelTheme {
                roomColors = new[] { Hex("263C7A"), Hex("2E4790"), Hex("1E3164"), Hex("38539E"), Hex("2A4386") },
                gutter = Hex("0A1230"),
                box = Hex("F2681E"), player = Hex("FFD34A"), wall = Hex("050818"), frame = Hex("A370FA"),
                grid = new Color(0f, 0f, 0f, 0.12f), floorVignette = 0.14f, pieceGlow = 0f, cellLift = 0.17f,
                floorTexTint = Color.clear,
            },
            // 5 — Master. The endgame: near-black, and the only fully saturated frame in the game.
            new LevelTheme {
                roomColors = new[] { Hex("2A2454"), Hex("332B66"), Hex("221D46"), Hex("3D3378"), Hex("2E2860") },
                gutter = Hex("06040F"),
                box = Hex("FF5E1A"), player = Hex("FFE066"), wall = Hex("030208"), frame = Hex("D10ACA"),
                grid = new Color(0f, 0f, 0f, 0.14f), floorVignette = 0.18f, pieceGlow = 0f, cellLift = 0.19f,
                floorTexTint = Color.clear,
            },
        };

        static string CreateMainMenuScene(Sprites spr, GameObject[] levels, Tiles t)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int levelCount = levels.Length;

            var menuCam = CreateCamera(true);   // orthographic — it physically flies into the real board on Start
            CreateEventSystem();
            CreateMusicPlayer();

            // world-space backdrop behind the board (same look as the game); a menu-only copy so the game scene is untouched
            var backdrop = BuildMenuBackdrop(menuCam, spr);

            var canvas = CreateCanvas("MenuCanvas");
            var ui = canvas.gameObject.AddComponent<MainMenuUI>();
            ui.unlockAllForTesting = false;
            // Shipping menu uses its approved full-screen artwork.  Never prebuild the legacy
            // world-space level preview that can spill across the menu at wide resolutions.
            ui.useStaticHomeArtwork = true;

            // the drifting squares sit in their OWN group, behind everything: the home text has to hide
            // when the level board opens, but this ambience should survive it.
            var bgRoot = MakeRect(canvas, "BackgroundRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var bgGroup = bgRoot.gameObject.AddComponent<CanvasGroup>();
            bgGroup.blocksRaycasts = false;

            // all home UI lives under a fade-able group so it dissolves as the camera dives into the board
            var homeRoot = MakeRect(canvas, "HomeRoot", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var homeGroup = homeRoot.gameObject.AddComponent<CanvasGroup>();
            ui.menuCam = menuCam;
            ui.backdrop = backdrop;
            ui.homeGroup = homeGroup;
            ui.backgroundGroup = bgGroup;

            // the real board's inputs — same prefabs / sprites / theme the game uses → identical render
            ui.levelPrefabs = levels;
            ui.floorPrefab = t.floor; ui.gridPrefab = t.grid; ui.wallPrefab = t.wall;
            ui.boxPrefab = t.box; ui.metaBoxPrefab = t.metaBox; ui.playerPrefab = t.player;
            ui.boxGoalPrefab = t.boxGoal; ui.playerGoalPrefab = t.playerGoal;
            ui.fxRing = spr.ringThick; ui.fxGlow = spr.glow; ui.fxVignette = spr.vignette; ui.fxCell = spr.cell;
            ui.boardThemes = ChapterThemes();

            BuildMenuBackground(bgRoot, spr);

            // ============================================ HOME screen (under homeRoot → fades during the dive)
            // "HAYOT'S" kicker above the logo
            AddTextShadow(MakeText(homeRoot, "Kicker", "H A Y O T ' S", 42, Hex("EAFBFF"),
                new Vector2(0, 332), new Vector2(1200, 60), FontStyle.Bold));

            // title logo — "PARAB[board]X": the O is a TRANSPARENT slot the real world board is framed into
            var titleGlow = UIImage(homeRoot, "TitleGlow", spr.glow, WithAlpha(Hex("38C0D8"), 0.42f), new Vector2(0, 205), new Vector2(1300, 560));
            var tgPulse = titleGlow.gameObject.AddComponent<UIPulse>();
            tgPulse.amplitude = 0.06f; tgPulse.speed = 1.4f;

            // auto-centered horizontal row so we don't have to guess the font's letter widths
            var titleRow = MakeRect(homeRoot, "TitleRow", new Vector2(0, 205), new Vector2(1200, 170));
            var hlg = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 4f;
            hlg.childControlWidth = true; hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var fitter = titleRow.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            TitleWord(titleRow, "TitlePARAB", "PARAB");
            var oSlotRT = MakeTitleBoardO(titleRow, spr, out var boardInner, out var oRingRT);   // transparent O slot; the world board is framed into it
            ui.boardInner = boardInner;
            ui.oSlot = oSlotRT;
            ui.oRing = oRingRT;
            TitleWord(titleRow, "TitleX", "X");

            // credits, like the reference
            AddTextShadow(MakeText(homeRoot, "Credit1", "a game by Nurhayot", 28, Hex("D6E8F4"),
                new Vector2(0, 74), new Vector2(1100, 38), FontStyle.Bold));
            // The scene baker below stores the Luxodd colours and final layout directly in the
            // generated scene, so these controls already look correct before entering Play Mode.
            ui.playButton   = MakePrompt(homeRoot, "PlayPrompt", "PLAY", new Vector2(-195, -150), new Vector2(350, 88), spr);
            // "Levels", not "Menu": it opens the progression map. The field has always been called
            // levelsButton and has always called OpenLevelBoard — only the label was lying.
            ui.levelsButton = MakePrompt(homeRoot, "LevelsPrompt", "LEVEL SELECT", new Vector2(195, -150), new Vector2(350, 88), spr);
            // no Quit on the title screen (matches the reference); MainMenuUI guards the null.

            BuildProgressionMap(canvas, spr, ui, levelCount);

            CreateFade(canvas);

            string path = SceneDir + "/MainMenu.unity";
            LuxoddArcadeUiSceneBaker.BakeScene(scene);
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        // ---------- level-select theming ----------------------------------------
        struct CatTheme
        {
            public string name, desc;
            public Color bg, accent, cardBase;
        }

        static CatTheme[] CategoryThemes() => new[]
        {
            new CatTheme { name = "COASTAL WATERS", desc = "Levels 1–10   ·   Beginner",
                           bg = Hex("0F4658"), accent = Hex("58E4E0"), cardBase = Hex("35BEC8") },
            new CatTheme { name = "CORAL REEF",     desc = "Levels 11–20   ·   Intermediate",
                           bg = Hex("082A46"), accent = Hex("3CC8EA"), cardBase = Hex("2E7EC4") },
            new CatTheme { name = "OCEAN ABYSS",    desc = "Levels 21–30   ·   Advanced",
                           bg = Hex("030E1C"), accent = Hex("34ECC6"), cardBase = Hex("0E2A46") },
        };

        // A full-screen overlay panel with a CanvasGroup + UIScreen (fade/scale transition).
        static Transform MakeOverlay(Transform canvas, string name, Color bg, out UIScreen screen)
        {
            var rt = MakeRect(canvas, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            rt.gameObject.AddComponent<CanvasGroup>();
            screen = rt.gameObject.AddComponent<UIScreen>();
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            return rt;
        }

        // ================================================= the progression map
        // Not a grid, and not a chapter list: ONE continuous route that snakes from level 1 to
        // level 30. Chapter 1 runs left→right, chapter 2 turns and runs right→left, chapter 3
        // turns again and runs to the final challenge — so the eye actually travels, and the
        // chapters read as legs of a journey rather than three separate shelves.
        //
        // The route itself is the progress bar: it's drawn as a trail of dots sampled along each
        // chapter's curve, and a leg only lights up once the level behind it is cleared. Your
        // progress IS the lit path.
        //
        // Each chapter's curve has its own identity — the wave grows in amplitude and frequency
        // as you descend (calm arc → 1.5 waves → a wilder double wave).
        static void BuildProgressionMap(Transform canvas, Sprites spr, MainMenuUI ui, int levelCount)
        {
            // 5 rows have to share the same canvas 3 used to. Measured: node 84 needs 172px of
            // amplitude clearance and overruns the Back button; node 72 over an 860px span leaves
            // 28px between rows, 16px above Back, and 103px between the closest two nodes anywhere.
            const float nodeSize = 72f, halfSpan = 720f, dotSize = 8f;
            const float rowSpan = 860f;
            const int dotsPerLink = 7;

            var themes = ChapterThemes();
            int per = MainMenuUI.PerCategory;
            int chapters = Mathf.Max(1, Mathf.CeilToInt(levelCount / (float)per));
            float rowGap = rowSpan / Mathf.Max(1, chapters);
            var baseY = new float[chapters];
            for (int c = 0; c < chapters; c++) baseY[c] = rowSpan * 0.5f - rowGap * 0.5f - c * rowGap;
            string[] chapterName = { "CHAPTER I", "CHAPTER II", "CHAPTER III", "CHAPTER IV", "CHAPTER V" };

            ui.levelButtons    = new Button[levelCount];
            ui.levelFills      = new Image[levelCount];
            ui.levelBorders    = new Image[levelCount];
            ui.levelNumbers    = new Text[levelCount];
            ui.levelChecks     = new GameObject[levelCount];
            ui.levelLocks      = new GameObject[levelCount];
            ui.levelHighlights = new GameObject[levelCount];
            ui.levelStars      = new GameObject[levelCount];
            ui.pathDots        = new Image[Mathf.Max(0, (levelCount - 1) * dotsPerLink)];
            ui.pathDotsPerLink = dotsPerLink;

            // a quiet scrim: the map floats in the world, it isn't a panel pasted over it
            var panel = MakeOverlay(canvas, "LevelBoardPanel", WithAlpha(Hex("03080F"), 0.90f), out var screen);
            ui.levelBoardScreen = screen;

            // Each chapter is a REGION: its own zone of colour, its own group, its own arrival.
            // A region owns its stretch of route, its nodes and its label, so the three chapters
            // read as places you travel between rather than three settings of one graph.
            // The map camera. Every region hangs off this one rect, so scaling and offsetting it
            // IS a camera push/pan — no second Unity camera, no render texture. The Back button
            // stays outside it so the UI never travels with the world.
            var mapCam = MakeRect(panel, "MapCam", Vector2.zero, new Vector2(1920f, 1080f));
            ui.mapCam = mapCam;

            var reveal = panel.gameObject.AddComponent<MapReveal>();
            reveal.regions = new CanvasGroup[chapters];
            reveal.regionRTs = new RectTransform[chapters];

            var regionRoot = new RectTransform[chapters];
            var pathLayers = new Transform[chapters];
            var glowLayers = new Transform[chapters];
            var nodeLayers = new Transform[chapters];
            for (int c = 0; c < chapters; c++)
            {
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                var rr = MakeRect(mapCam, "Region" + c, Vector2.zero, new Vector2(1920f, 1080f));
                regionRoot[c] = rr;
                reveal.regions[c] = rr.gameObject.AddComponent<CanvasGroup>();
                reveal.regionRTs[c] = rr;

                // the zone wash — a soft field of this chapter's colour behind its leg. It grows
                // stronger as you descend, so the final region is the most present on screen.
                UIImage(rr, "Zone", spr.glow, WithAlpha(th.frame, 0.05f + c * 0.025f),
                    new Vector2(0f, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)]), new Vector2(2000f, 460f));

                // draw order within a region: trail underneath, then hover glows, then nodes
                pathLayers[c] = MakeRect(rr, "Route", Vector2.zero, new Vector2(1920f, 1080f));
                glowLayers[c] = MakeRect(rr, "Glows", Vector2.zero, new Vector2(1920f, 1080f));
                nodeLayers[c] = MakeRect(rr, "Nodes", Vector2.zero, new Vector2(1920f, 1080f));
            }

            // ---- the route -------------------------------------------------------------
            for (int i = 0; i + 1 < levelCount; i++)
            {
                int ca = i / per, cb = (i + 1) / per;
                var th = themes[Mathf.Clamp(cb, 0, themes.Length - 1)];
                for (int d = 0; d < dotsPerLink; d++)
                {
                    float f = (d + 1f) / (dotsPerLink + 1f);
                    Vector2 p;
                    if (ca == cb)
                    {
                        // walk along this chapter's own curve, so the trail hugs the route
                        p = MapPos(ca, (i % per) + f, per, baseY[Mathf.Clamp(ca, 0, baseY.Length - 1)], halfSpan);
                    }
                    else
                    {
                        // the turn between chapters: bow it outward so the route visibly bends
                        // around, instead of dropping in a dead straight line
                        Vector2 a = MapPos(ca, per - 1, per, baseY[Mathf.Clamp(ca, 0, baseY.Length - 1)], halfSpan);
                        Vector2 b = MapPos(cb, 0, per, baseY[Mathf.Clamp(cb, 0, baseY.Length - 1)], halfSpan);
                        Vector2 ctrl = (a + b) * 0.5f + new Vector2(Mathf.Sign(a.x) * 90f, 0f);
                        p = Bezier(a, ctrl, b, f);
                    }
                    var dot = UIImage(pathLayers[Mathf.Clamp(cb, 0, chapters - 1)], "Dot" + i + "_" + d,
                        spr.disc, WithAlpha(th.frame, 0.12f), p, new Vector2(dotSize, dotSize));
                    ui.pathDots[i * dotsPerLink + d] = dot;
                }
            }

            // ---- chapter labels, sitting at each leg's starting node --------------------
            for (int c = 0; c < chapters; c++)
            {
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                Vector2 start = MapPos(c, 0, per, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)], halfSpan);
                bool rev = (c % 2) == 1;
                AddTextShadow(MakeText(regionRoot[c], "ChName" + c, chapterName[Mathf.Clamp(c, 0, chapterName.Length - 1)],
                    20, WithAlpha(Lighten(th.frame, 0.25f), 0.9f),
                    start + new Vector2(rev ? -110f : 110f, 76f), new Vector2(220f, 30f),
                    FontStyle.Bold, rev ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft));
            }

            // ---- the nodes -------------------------------------------------------------
            for (int i = 0; i < levelCount; i++)
            {
                int c = i / per;
                var th = themes[Mathf.Clamp(c, 0, themes.Length - 1)];
                Vector2 p = MapPos(c, i % per, per, baseY[Mathf.Clamp(c, 0, baseY.Length - 1)], halfSpan);
                float size = nodeSize;

                ui.levelButtons[i] = MakeMapNode(nodeLayers[Mathf.Clamp(c, 0, chapters - 1)],
                    glowLayers[Mathf.Clamp(c, 0, chapters - 1)], "Level" + (i + 1), i + 1, th, spr, p, size,
                    out ui.levelFills[i], out ui.levelBorders[i], out ui.levelNumbers[i],
                    out ui.levelLocks[i], out ui.levelChecks[i], out ui.levelHighlights[i], out ui.levelStars[i]);

            }

            // Chapter locking is already clear from dim, disabled nodes. Keep these compatibility
            // arrays for MainMenuUI, but do not add the old bar-and-padlock gate decoration.
            ui.chapterGates = new GameObject[chapters];
            ui.gateLeft = new RectTransform[chapters];
            ui.gateRight = new RectTransform[chapters];
            ui.gateLocks = new RectTransform[chapters];

            ui.progressFx = panel.gameObject.AddComponent<MapProgressFx>();
            ui.progressFx.burstGlow = spr.glow;
            ui.progressFx.burstSpark = spr.star;
            ui.progressFx.ringSprite = spr.cellRing;   // same ring the nodes wear, so it reads as the node itself leaving
            ui.unlockFx = panel.gameObject.AddComponent<ChapterUnlockFx>();
            ui.unlockFx.burstGlow = spr.glow;
            ui.unlockFx.burstSpark = spr.star;
            ui.unlockFx.ringSprite = spr.cellRing;
            ui.regionBaseY = baseY;

            // ================================================= the victory screen
            // The artifact. Four endings were built before it existed — all of them board effects,
            // which vanish the moment they finish. This is the thing that is still there when the
            // motion stops, and it sits over your completed route rather than replacing it.
            var gold = themes[chapters - 1].player;
            var fin = MakeRect(panel, "FinaleBanner", Vector2.zero, new Vector2(1920f, 1080f));
            var finBG = fin.gameObject.AddComponent<Image>();
            finBG.color = WithAlpha(Hex("03080F"), 0.72f);
            ui.finaleBanner = fin.gameObject.AddComponent<CanvasGroup>();

            // the trophy: a box, inside a box, inside a box — this game's shape, not a stock cup
            var badge = MakeRect(fin, "Badge", new Vector2(0f, 132f), new Vector2(190f, 190f));
            UIImage(badge, "BadgeGlow", spr.glow, WithAlpha(gold, 0.42f), Vector2.zero, new Vector2(400f, 400f))
                .gameObject.AddComponent<UIPulse>();
            // a ROUNDED-SQUARE frame, not a circle: the glyph inside is a square, and a square in a
            // circle puts its corners in a fight with the ring around it
            UIImage(badge, "BadgeRing", spr.cellRing, WithAlpha(gold, 0.38f), Vector2.zero, new Vector2(268f, 268f));
            UIImage(badge, "BadgeIcon", spr.trophy, gold, Vector2.zero, new Vector2(168f, 168f));
            ui.finaleBadge = badge;

            ui.finaleTitle = AddTextShadow(MakeText(fin, "FinaleTitle", "GAME COMPLETE", 64,
                Lighten(gold, 0.25f), new Vector2(0f, -14f), new Vector2(1200f, 84f), FontStyle.Bold));

            ui.finaleCount = AddTextShadow(MakeText(fin, "FinaleCount", "30 / 30 LEVELS COMPLETE", 30,
                Hex("EAF6FF"), new Vector2(0f, -76f), new Vector2(1200f, 40f), FontStyle.Bold));

            ui.finaleSub = AddTextShadow(MakeText(fin, "FinaleSub", "", 22, Hex("A8C2D4"),
                new Vector2(0f, -126f), new Vector2(1200f, 32f)));

            ui.finaleBanner.alpha = 0f;
            ui.finaleBanner.interactable = false;
            ui.finaleBanner.blocksRaycasts = false;

            ui.levelBoardBackButton = MakeButton(panel, "LBBack", "Back",
                new Vector2(0f, -466f), new Vector2(232f, 64f), 24, Hex("2A5570"));
            panel.gameObject.SetActive(false);
        }

        // A point on chapter c's route. k is the node index within the chapter and may be
        // fractional, which is how the trail samples the curve between two nodes.
        static Vector2 MapPos(int c, float k, int per, float baseY, float half)
        {
            float t = Mathf.Clamp01(k / (per - 1f));
            bool rev = (c % 2) == 1;                        // serpentine: every other leg reverses
            float x = -half + 2f * half * (rev ? 1f - t : t);
            // Tuned for 5 rows: at the old 30+14c a chapter-5 row swung 86px and collided with its
            // neighbours. 20+6c keeps the wander visible while every row stays in its lane.
            float amp = 20f + c * 6f;                       // later chapters wander further
            float freq = 1f + c * 0.4f;                     // ...and oscillate more
            float y = baseY + Mathf.Sin(t * freq * Mathf.PI * 2f) * amp;
            return new Vector2(x, y);
        }

        static Vector2 Bezier(Vector2 a, Vector2 ctrl, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * ctrl + t * t * b;
        }

        // One stop on the route. Same construction for all thirty, so size, stroke weight and
        // type scale can't drift; only the final node is scaled up, as the destination.
        static Button MakeMapNode(Transform parent, Transform glowLayer, string name, int number,
                                  LevelTheme th, Sprites spr, Vector2 pos, float size,
                                  out Image fill, out Image border, out Text numberText,
                                  out GameObject lockGO, out GameObject checkGO, out GameObject highlightGO,
                                  out GameObject starGO)
        {
            var s = new Vector2(size, size);

            var rt = MakeRect(parent, name, pos, s);
            fill = rt.gameObject.AddComponent<Image>();
            fill.sprite = spr.cell;
            fill.type = Image.Type.Simple;
            fill.color = (th.roomColors != null && th.roomColors.Length > 0) ? th.roomColors[0] : th.frame;

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = fill;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;   // locked is painted explicitly, not tinted
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            var hover = rt.gameObject.AddComponent<UIHoverScale>();
            hover.hover = 1.16f;
            hover.press = 0.90f;
            var hl = UIImage(rt, "LevelFocusSelector", spr.cellRing,
                Color.Lerp(th.frame, Color.white, 0.72f), Vector2.zero, new Vector2(116f, 116f));
            var selectorOutline = hl.gameObject.AddComponent<Outline>();
            selectorOutline.effectColor = WithAlpha(th.frame, 0.95f);
            selectorOutline.effectDistance = new Vector2(2.5f, -2.5f);
            var selectorShadow = hl.gameObject.AddComponent<Shadow>();
            selectorShadow.effectColor = WithAlpha(th.frame, 0.80f);
            selectorShadow.effectDistance = new Vector2(0f, -4f);
            var selectorPulse = hl.gameObject.AddComponent<UIPulse>();
            selectorPulse.amplitude = 0.06f;
            selectorPulse.speed = 3.5f;
            hl.gameObject.SetActive(false);
            hover.highlight = hl.gameObject;

            border = AddRing(rt, "Border", s, spr.cellRing, WithAlpha(th.frame, 0.55f)).GetComponent<Image>();
            numberText = MakeText(rt, "Number", number.ToString(), Mathf.RoundToInt(size * 0.36f), th.wall,
                Vector2.zero, s, FontStyle.Bold);

            var hi = AddRing(rt, "Current", s + new Vector2(20f, 20f), spr.cellRing, th.frame);
            hi.gameObject.AddComponent<UIPulse>();
            highlightGO = hi.gameObject;
            highlightGO.SetActive(false);

            // Completion is shown by the node's chapter colour. A second circular check badge made
            // the compact tile look crowded, so newly generated maps intentionally omit it.
            checkGO = null;

            // Perfect = solved at par. This is the only corner badge on a level tile.
            var st = MakeRect(rt, "Perfect", new Vector2(-size * 0.5f + 13f, size * 0.5f - 13f), new Vector2(30f, 30f));
            UIImage(st, "PerfectGlow", spr.glow, WithAlpha(th.player, 0.75f), Vector2.zero, new Vector2(46f, 46f));
            UIImage(st, "PerfectStar", spr.star, th.player, Vector2.zero, new Vector2(26f, 26f));
            starGO = st.gameObject;
            starGO.SetActive(false);

            var lk = MakeRect(rt, "Lock", Vector2.zero, new Vector2(28f, 28f));
            UIImage(lk, "LockIcon", spr.lockIcon, Lighten(th.gutter, 0.45f), Vector2.zero, new Vector2(28f, 28f));
            lockGO = lk.gameObject;
            lockGO.SetActive(false);

            hl.rectTransform.SetAsLastSibling();

            return btn;
        }

        static RectTransform AddRing(Transform parent, string name, Vector2 size, Sprite sprite, Color color)
        {
            var rt = MakeRect(parent, name, Vector2.zero, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
            return rt;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ---------- premium animated main-menu background ----------
        // Menu-only copy of the game's world backdrop (a child of the menu camera), so the game scene stays untouched.
        static CameraBackdrop BuildMenuBackdrop(Camera cam, Sprites spr)
        {
            var backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(cam.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, 20f);
            var bd = backdrop.AddComponent<CameraBackdrop>();
            bd.cam = cam;
            bd.glow1 = MakeWorldSprite(backdrop.transform, "Glow1", spr.glow, -200);
            bd.glow2 = MakeWorldSprite(backdrop.transform, "Glow2", spr.glow, -199);
            bd.vignette = MakeWorldSprite(backdrop.transform, "Vignette", spr.vignette, -198);
            bd.rays = new[]
            {
                MakeWorldSprite(backdrop.transform, "Ray0", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray1", spr.godRay, -197),
                MakeWorldSprite(backdrop.transform, "Ray2", spr.godRay, -197),
            };
            var bdThemes = CategoryThemes();
            bd.baseColors  = new[] { bdThemes[0].bg, bdThemes[1].bg, bdThemes[2].bg };
            bd.glowAColors = new[] { WithAlpha(bdThemes[0].accent, 0f), WithAlpha(bdThemes[1].accent, 0f), WithAlpha(bdThemes[2].accent, 0f) };   // flat background — no nebula wash
            bd.glowBColors = new[] { WithAlpha(bdThemes[0].cardBase, 0f), WithAlpha(bdThemes[1].cardBase, 0f), WithAlpha(Hex("1E7AA8"), 0f) };
            bd.rayColors   = new[] { WithAlpha(Hex("BFF4FF"), 0f), WithAlpha(Hex("9FE0FF"), 0f), WithAlpha(Hex("6FD8E0"), 0f) };   // no wave shafts anywhere — clean, non-decorative background
            bd.vignetteAlpha = 0f;
            cam.backgroundColor = bd.baseColors[0];
            bd.glow1.color = bd.glowAColors[0];
            bd.glow2.color = bd.glowBColors[0];
            bd.vignette.color = new Color(0f, 0f, 0f, bd.vignetteAlpha);
            bd.glow1.transform.localScale = Vector3.one * 20f;
            bd.glow2.transform.localScale = Vector3.one * 17f;
            bd.vignette.transform.localScale = new Vector3(26f, 15f, 1f);
            bd.glow1.transform.localPosition = new Vector3(-4f, 2.6f, 0f);
            bd.glow2.transform.localPosition = new Vector3(4.5f, -3f, 0f);
            for (int i = 0; i < bd.rays.Length; i++)
            {
                bd.rays[i].color = bd.rayColors[0];
                bd.rays[i].transform.localScale = new Vector3(9f, 34f, 1f);
                bd.rays[i].transform.localPosition = new Vector3((i - 1) * 7f, 4f, 0.05f);
                bd.rays[i].transform.localEulerAngles = new Vector3(0f, 0f, (i - 1) * 10f + 6f);
            }
            var menuPhoto = LoadMenuBgImage();
            if (menuPhoto != null)
            {
                bd.bgPhoto = MakeWorldSprite(backdrop.transform, "MenuPhoto", menuPhoto, -206);
                bd.bgPhoto.transform.localScale = new Vector3(30f, 18f, 1f);
            }
            return bd;
        }

        static void BuildMenuBackground(Transform canvas, Sprites spr)
        {
            // NOTE: NO opaque background image — the world-space board renders behind this canvas and must
            // show through the "O". The ocean colour + nebula atmosphere come from the world CameraBackdrop.

            // soft top-centre glow → a gentle radial lightening (semi-transparent, the board shows through)
            var topGlow = UIImage(canvas, "MenuTopGlow", spr.glow, WithAlpha(Hex("2E86B0"), 0.4f), new Vector2(0, 340), new Vector2(1800, 1200));
            topGlow.raycastTarget = false;

            // clearly-visible drifting SQUARES (like the reference's floating squares), in ocean tones
            var ambRT = MakeRect(canvas, "MenuParticles", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var amb = ambRT.gameObject.AddComponent<UIAmbient>();
            amb.sprite = spr.floorFill;
            amb.palette = new[] { Hex("3E6E90"), Hex("5A9CC0"), Hex("86D2E4"), Hex("B4E8F0") };
            amb.count = 32; amb.baseAlpha = 0.4f;
            amb.sizeRange = new Vector2(18f, 56f); amb.riseSpeed = 40f; amb.sway = 10f;
            amb.drift = true;   // float around within the screen, not stream up off the top

            // gentle depth vignette (content sits above this)
            var vigRT = MakeRect(canvas, "MenuVignette", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var vig = vigRT.gameObject.AddComponent<Image>();
            vig.sprite = spr.vignette; vig.color = new Color(0f, 0f, 0f, 0.24f); vig.raycastTarget = false;
        }

        static void MenuBlob(Transform canvas, Sprites spr, Color color, Vector2 pos, float size, float phase, float alpha)
        {
            var img = UIImage(canvas, "Blob", spr.glow, WithAlpha(color, alpha), pos, new Vector2(size, size));
            var bob = img.gameObject.AddComponent<Bobber>();
            bob.amplitude = 42f; bob.speed = 0.4f; bob.phase = phase;
            var pul = img.gameObject.AddComponent<UIPulse>();
            pul.amplitude = 0.07f; pul.speed = 0.45f;
        }

        // Loads a user-supplied menu background from Assets/Parabox/Art/MenuBG.(png|jpg),
        // forcing it to import as a Sprite. Returns null if none is present (procedural fallback).
        static Sprite LoadMenuBgImage()
        {
            string[] candidates = { Root + "/Art/MenuBG.png", Root + "/Art/MenuBG.jpg", Root + "/Art/MenuBG.jpeg" };
            foreach (var path in candidates)
            {
                string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                if (!File.Exists(abs)) continue;

                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite ||
                                    imp.spriteImportMode != SpriteImportMode.Single))
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) return sp;
            }
            return null;
        }

        // Optional custom font: the first .ttf/.otf in Assets/Parabox/Fonts restyles ALL text. Null => default UI font.
        static Font LoadGameFont()
        {
            string dir = Root + "/Fonts";
            if (!AssetDatabase.IsValidFolder(dir)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { dir }))
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null) return f;
            }
            return null;
        }

        // Optional in-game underwater background: Assets/Parabox/Art/GameBG.(png|jpg). Null => procedural scene.
        static Sprite LoadGameBgImage()
        {
            string[] candidates = { Root + "/Art/GameBG.png", Root + "/Art/GameBG.jpg", Root + "/Art/GameBG.jpeg" };
            foreach (var path in candidates)
            {
                string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                if (!File.Exists(abs)) continue;

                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite ||
                                    imp.spriteImportMode != SpriteImportMode.Single))
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) return sp;
            }
            return null;
        }

        // Optional background music. The generated neon arcade loop is preferred, while Music.*
        // and the older bundled tracks remain available as safe authoring fallbacks.
        static AudioClip LoadMusicClip()
        {
            // preferred names first
            string[] names =
            {
                "Resources/Music/NeonPuzzleParty",
                "NeonPuzzleParty",
                "Music",
                "music",
                "Flowerbed Fields - Zane Little Music",
                "Sci-Fi Puzzle In-Game 1 - MintoDog"
            };
            foreach (var n in names)
                foreach (var ext in new[] { ".ogg", ".mp3", ".wav" })
                {
                    string path = n.StartsWith("Resources/")
                        ? Root + "/" + n + ext
                        : Root + "/Audio/" + n + ext;
                    string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                    if (File.Exists(abs))
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip != null) return clip;
                    }
                }
            // fallback: use the first audio file of any name dropped in Assets/Parabox/Audio/
            string audioDir = Path.Combine(Application.dataPath, "Parabox/Audio");
            if (Directory.Exists(audioDir))
                foreach (var file in Directory.GetFiles(audioDir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ogg" || ext == ".mp3" || ext == ".wav" || ext == ".aiff" || ext == ".aif")
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Parabox/Audio/" + Path.GetFileName(file));
                        if (clip != null) return clip;
                    }
                }
            return null;
        }

        // Persistent looping music player (singleton, survives scene loads, follows the mute button).
        static void CreateMusicPlayer()
        {
            var go = new GameObject("MusicPlayer");
            go.AddComponent<AudioSource>();
            var mp = go.AddComponent<MusicPlayer>();
            mp.clip = LoadMusicClip();
        }

        // Optional real region icon: Assets/Parabox/Art/icon_{coastal|reef|abyss}.(png|jpg). Null => procedural.
        static Sprite LoadWorldIcon(int c)
        {
            string[] names = { "icon_coastal", "icon_reef", "icon_abyss" };
            if (c < 0 || c >= names.Length) return null;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            {
                string path = Root + "/Art/" + names[c] + ext;
                string abs = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
                if (!File.Exists(abs)) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp != null && (imp.textureType != TextureImporterType.Sprite ||
                                    imp.spriteImportMode != SpriteImportMode.Single))
                {
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sp != null) return sp;
            }
            return null;
        }

        static void MenuButtonGlow(Transform parent, Vector2 pos, Vector2 size, Color color, Sprites spr)
        {
            UIImage(parent, "BtnGlow", spr.glow, WithAlpha(color, 0.42f), pos, size + new Vector2(150f, 150f));
        }

        // The lit top face of a MakeButton (icons live here, above the gradient).
        static Transform BtnFace(Button b)
        {
            var f = b.transform.Find("Face");
            return f != null ? f : b.transform;
        }

        static void AddPlayIcon(Button b, Sprites spr)
        {
            UIImage(BtnFace(b), "Icon", spr.iconPlay, Color.white, new Vector2(-128f, 0f), new Vector2(38, 38));
        }

        static void AddGridIcon(Button b, Sprites spr)
        {
            var g = MakeRect(BtnFace(b), "Icon", new Vector2(-128f, 0f), new Vector2(42, 42));
            const float c = 15f, o = 9.5f;
            UIImage(g, "g0", spr.fill, Color.white, new Vector2(-o,  o), new Vector2(c, c));
            UIImage(g, "g1", spr.fill, Color.white, new Vector2( o,  o), new Vector2(c, c));
            UIImage(g, "g2", spr.fill, Color.white, new Vector2(-o, -o), new Vector2(c, c));
            UIImage(g, "g3", spr.fill, Color.white, new Vector2( o, -o), new Vector2(c, c));
        }

        static void AddPowerIcon(Button b, Sprites spr)
        {
            var g = MakeRect(BtnFace(b), "Icon", new Vector2(-78f, 0f), new Vector2(34, 34));
            UIImage(g, "ring", spr.ringCircle, Color.white, new Vector2(0f, -3f), new Vector2(30, 30));
            UIImage(g, "bar", spr.fill, Color.white, new Vector2(0f, 7f), new Vector2(5, 15));
        }

        // A premium mascot piece: soft drop shadow + top-lit 3D body + lighter rim outline.
        static void MascotPiece(Transform parent, string name, Vector2 pos, float size, Color color, Sprites spr)
        {
            var s = new Vector2(size, size);
            UIImage(parent, name + "Shadow", spr.glow, new Color(0f, 0f, 0f, 0.35f), pos + new Vector2(0f, -11f), s * 1.25f);
            UIImage(parent, name, spr.shaded, color, pos, s);
            UIImage(parent, name + "Rim", spr.ringThick, Lighten(color, 0.42f), pos, s);
        }

        // A mascot eye with a small catchlight highlight for a lively, premium look.
        static void MascotEye(Transform parent, string name, Vector2 pos, Sprites spr)
        {
            UIImage(parent, name, spr.fill, EyeColor, pos, new Vector2(13, 20));
            UIImage(parent, name + "Hi", spr.fill, new Color(1f, 1f, 1f, 0.85f), pos + new Vector2(3f, 5f), new Vector2(4.5f, 5.5f));
        }

        // Soft radial glow (white center -> transparent) — nebula backdrops and panel halos.
        static Sprite MakeGlowSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, maxr = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / maxr;
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "Glow", false, 0, FilterMode.Bilinear);
        }

        // Radial vignette (transparent center -> black edges) for premium depth.
        static Sprite MakeVignetteSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, maxr = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) / maxr;
                    float a = Mathf.Clamp01((d - 0.55f) / 0.45f); a *= a;
                    px[y * s + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "Vignette", false, 0, FilterMode.Bilinear);
        }

        // Right-pointing play triangle — the PLAY button icon.
        static Sprite MakePlaySprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float m = 0.22f * s;
            var vx = new float[] { m, m, s - m };
            var vy = new float[] { s - m, m, s * 0.5f };
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (PointInPoly(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS, vx, vy)) hits++;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "PlayIcon", false, 0, FilterMode.Bilinear);
        }

        // Tileable per-tier floor surface. mode: 0 grass mottle, 1 water ripples, 2 cracked rock.
        static Sprite MakeFloorTexSprite(string name, int mode)
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[s * s];
            const float TAU = 6.2831853f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float a;
                    if (mode == 0)        // grass: soft organic blotches
                    {
                        float n = Noise2(u, v) * 0.5f + 0.5f;
                        a = Mathf.Clamp01((n - 0.42f) * 2.4f);
                    }
                    else if (mode == 1)   // water: thin bright ripple crests
                    {
                        float w = Mathf.Sin(v * TAU * 7f + 0.7f * Mathf.Sin(u * TAU * 3f));
                        a = Mathf.Clamp01((w - 0.55f) * 2.6f);
                    }
                    else if (mode == 2)   // rock: thin dark cracks where the field crosses zero
                    {
                        float f = Noise2(u, v);
                        a = Mathf.Clamp01(1f - Mathf.Abs(f) * 7f);
                    }
                    else                  // stars: sparse bright points (cosmos)
                    {
                        float st = Mathf.Sin(u * TAU * 11f) * Mathf.Sin(v * TAU * 13f)
                                 + Mathf.Sin(u * TAU * 17f + 2f) * Mathf.Sin(v * TAU * 19f + 1f);
                        a = Mathf.Clamp01((st - 1.35f) * 6f);
                    }
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px); tex.Apply();

            string assetPath = SpriteDir + "/" + name + ".png";
            string absPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            File.WriteAllBytes(absPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = Res * 0.5f;   // one tile ~ 2 world cells (less repetitive)
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteBorder = Vector4.zero;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // Smooth tileable value field ~[-1,1] (integer frequencies => wraps seamlessly).
        static float Noise2(float u, float v)
        {
            const float TAU = 6.2831853f;
            float n = Mathf.Sin(u * TAU * 3f) * Mathf.Cos(v * TAU * 2f)
                    + 0.6f * Mathf.Sin(u * TAU * 5f + 1.3f) * Mathf.Cos(v * TAU * 4f + 0.7f)
                    + 0.4f * Mathf.Sin(u * TAU * 7f + 2.1f) * Mathf.Cos(v * TAU * 6f + 1.9f);
            return n * 0.5f;
        }

        // Up-pointing triangle — the Forest tree canopy icon.
        static Sprite MakeTriSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float m = 0.12f * s;
            var vx = new float[] { m, s - m, s * 0.5f };
            var vy = new float[] { m, m, s - m };
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (PointInPoly(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS, vx, vy)) hits++;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "TriIcon", false, 0, FilterMode.Bilinear);
        }

        // Teardrop (round bottom, pointed top) — Ocean droplet (flipped) & Volcano flame.
        static Sprite MakeDropSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = s * 0.5f, cy = 0.36f * s, r = 0.30f * s, topY = 0.94f * s;
            var vx = new float[] { cx - r, cx + r, cx };
            var vy = new float[] { cy, cy, topY };
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float fx = x + (sx + 0.5f) / SS, fy = y + (sy + 0.5f) / SS;
                            bool inC = (fx - cx) * (fx - cx) + (fy - cy) * (fy - cy) <= r * r;
                            if (inC || PointInPoly(fx, fy, vx, vy)) hits++;
                        }
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "DropIcon", false, 0, FilterMode.Bilinear);
        }

        static Color32 Col(Vector3 c, float a) =>
            new Color32((byte)(Mathf.Clamp01(c.x) * 255f), (byte)(Mathf.Clamp01(c.y) * 255f),
                        (byte)(Mathf.Clamp01(c.z) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));

        // Realistic shaded tree: brown trunk + a bushy green canopy lit from the top-left.
        static Sprite MakeTreeIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            var darkG = new Vector3(0.13f, 0.38f, 0.19f); var liteG = new Vector3(0.47f, 0.82f, 0.37f);
            var barkD = new Vector3(0.27f, 0.16f, 0.08f); var barkL = new Vector3(0.46f, 0.29f, 0.15f);
            float[] ccx = { 0.50f * s, 0.34f * s, 0.66f * s, 0.50f * s };
            float[] ccy = { 0.58f * s, 0.50f * s, 0.50f * s, 0.42f * s };
            float[] ccr = { 0.26f * s, 0.22f * s, 0.22f * s, 0.21f * s };
            float lx = 0.36f * s, ly = 0.70f * s;
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    int chits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float ux = x + (sx + 0.5f) / SS, uy = y + (sy + 0.5f) / SS;
                            for (int k = 0; k < 4; k++)
                            { float d0 = (ux - ccx[k]) * (ux - ccx[k]) + (uy - ccy[k]) * (uy - ccy[k]); if (d0 <= ccr[k] * ccr[k]) { chits++; break; } }
                        }
                    float canA = chits / (float)(SS * SS);
                    bool trunk = fx > 0.44f * s && fx < 0.56f * s && fy > 0.04f * s && fy < 0.34f * s;
                    if (canA > 0f)
                    {
                        float d = Mathf.Sqrt((fx - lx) * (fx - lx) + (fy - ly) * (fy - ly)) / (0.6f * s);
                        float t = Mathf.Clamp01(1f - d);
                        px[y * s + x] = Col(Vector3.Lerp(darkG, liteG, t * t), canA);
                    }
                    else if (trunk)
                    {
                        float t = Mathf.Clamp01((fx - 0.44f * s) / (0.12f * s));
                        px[y * s + x] = Col(Vector3.Lerp(barkD, barkL, t), 1f);
                    }
                    else px[y * s + x] = new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconTreeR", false, 0, FilterMode.Bilinear);
        }

        // Realistic glossy water droplet: blue gradient + a bright specular highlight.
        static Sprite MakeDropIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = s * 0.5f, cy = 0.36f * s, r = 0.30f * s, topY = 0.94f * s;
            var vx = new float[] { cx - r, cx + r, cx };
            var vy = new float[] { cy, cy, topY };
            var deep = new Vector3(0.10f, 0.34f, 0.72f); var lite = new Vector3(0.52f, 0.84f, 0.98f);
            float lx = 0.40f * s, ly = 0.30f * s;
            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float ux = x + (sx + 0.5f) / SS, uy = y + (sy + 0.5f) / SS;
                            bool inC = (ux - cx) * (ux - cx) + (uy - cy) * (uy - cy) <= r * r;
                            if (inC || PointInPoly(ux, uy, vx, vy)) hits++;
                        }
                    float a = hits / (float)(SS * SS);
                    if (a <= 0f) { px[y * s + x] = new Color32(0, 0, 0, 0); continue; }
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float d = Mathf.Sqrt((fx - lx) * (fx - lx) + (fy - ly) * (fy - ly)) / (0.62f * s);
                    float t = Mathf.Clamp01(1f - d);
                    var col = Vector3.Lerp(deep, lite, t * t);
                    float ds = Mathf.Sqrt((fx - 0.42f * s) * (fx - 0.42f * s) + (fy - 0.25f * s) * (fy - 0.25f * s));
                    col = Vector3.Lerp(col, Vector3.one, Mathf.Clamp01(1f - ds / (0.10f * s)) * 0.9f);
                    px[y * s + x] = Col(col, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconDropR", false, 0, FilterMode.Bilinear);
        }

        // Realistic glowing star: gold rays + a white-hot core.
        static Sprite MakeStarIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, rayLen = 0.46f * s, core = 0.13f * s;
            var gold = new Vector3(1f, 0.84f, 0.46f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Abs(x + 0.5f - c), dy = Mathf.Abs(y + 0.5f - c);
                    float ax = Mathf.Clamp01(1f - dx / rayLen) * Mathf.Clamp01(1f - dy / (0.03f * s + 0.10f * dx));
                    float ay = Mathf.Clamp01(1f - dy / rayLen) * Mathf.Clamp01(1f - dx / (0.03f * s + 0.10f * dy));
                    float ray = Mathf.Max(ax, ay);
                    float cr = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / core); cr *= cr;
                    float a = Mathf.Clamp01(Mathf.Max(ray, cr));
                    px[y * s + x] = Col(Vector3.Lerp(gold, Vector3.one, cr), a);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconStarR", false, 0, FilterMode.Bilinear);
        }

        // Ocean wave: three stacked blue wave bands with white foam crests.
        static Sprite MakeWaveIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            const float TAU = 6.2831853f;
            var cols = new[] { new Vector3(0.56f, 0.86f, 0.98f), new Vector3(0.30f, 0.64f, 0.92f), new Vector3(0.13f, 0.43f, 0.80f) };
            float[] baseY = { 0.64f, 0.47f, 0.30f };
            float[] amp = { 0.05f, 0.06f, 0.05f };
            float[] th = { 0.11f, 0.11f, 0.11f };
            float[] ph = { 0.0f, 1.3f, 2.6f };
            float[] freq = { 1.6f, 1.9f, 1.5f };
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, vv = (y + 0.5f) / s;
                    Color32 outp = new Color32(0, 0, 0, 0);
                    for (int i = 2; i >= 0; i--)
                    {
                        float surf = baseY[i] + amp[i] * Mathf.Sin(u * TAU * freq[i] + ph[i]);
                        float dist = vv - surf;                       // >0 above the wave surface
                        if (dist <= 0f && dist > -th[i])
                        {
                            float aa = Mathf.Min(Mathf.Clamp01(-dist / (2f / s)), Mathf.Clamp01((dist + th[i]) / (2f / s)));
                            float foam = Mathf.Clamp01((0.03f - Mathf.Abs(dist)) / 0.03f);
                            outp = Col(Vector3.Lerp(cols[i], Vector3.one, foam * 0.75f), aa);
                            break;
                        }
                    }
                    px[y * s + x] = outp;
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconWaveR", false, 0, FilterMode.Bilinear);
        }

        // distance from point (px,py) to segment a->b; out t = closest param along the segment.
        static float SegDist(float px, float py, float ax, float ay, float bx, float by, out float t)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float L2 = vx * vx + vy * vy;
            t = L2 <= 0f ? 0f : Mathf.Clamp01((wx * vx + wy * vy) / L2);
            float cx = ax + vx * t, cy = ay + vy * t;
            return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        // Branching coral: thick tapered tubes on a shared trunk, deep-pink base → light-coral tips.
        static Sprite MakeCoralIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            // ax, ay, bx, by, thickBase, thickTip  (normalized, y up)
            float[,] br = {
                { 0.50f, 0.04f, 0.50f, 0.42f, 0.075f, 0.055f },   // trunk
                { 0.50f, 0.30f, 0.30f, 0.60f, 0.055f, 0.032f },   // left lower
                { 0.30f, 0.60f, 0.25f, 0.82f, 0.032f, 0.020f },   // left tip
                { 0.50f, 0.36f, 0.70f, 0.62f, 0.055f, 0.032f },   // right lower
                { 0.70f, 0.62f, 0.76f, 0.84f, 0.032f, 0.020f },   // right tip
                { 0.50f, 0.42f, 0.50f, 0.86f, 0.045f, 0.022f },   // center
                { 0.50f, 0.24f, 0.38f, 0.44f, 0.035f, 0.020f },   // small left
                { 0.50f, 0.40f, 0.62f, 0.56f, 0.035f, 0.020f },   // small right
            };
            float[,] tips = { { 0.25f, 0.82f, 0.048f }, { 0.76f, 0.84f, 0.048f }, { 0.50f, 0.86f, 0.052f }, { 0.38f, 0.44f, 0.032f }, { 0.62f, 0.56f, 0.032f } };
            var baseC = new Vector3(0.80f, 0.24f, 0.44f);   // deep coral-pink
            var tipC  = new Vector3(1.00f, 0.60f, 0.50f);   // light coral
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float bestA = 0f, hgt = 0f;
                    for (int i = 0; i < br.GetLength(0); i++)
                    {
                        float t; float d = SegDist(u, v, br[i, 0], br[i, 1], br[i, 2], br[i, 3], out t);
                        float th = br[i, 4] + (br[i, 5] - br[i, 4]) * t;
                        float a = Mathf.Clamp01(0.5f + (th - d) * s / 1.5f);
                        if (a > bestA) { bestA = a; hgt = br[i, 1] + (br[i, 3] - br[i, 1]) * t; }
                    }
                    for (int i = 0; i < tips.GetLength(0); i++)
                    {
                        float d = Mathf.Sqrt((u - tips[i, 0]) * (u - tips[i, 0]) + (v - tips[i, 1]) * (v - tips[i, 1]));
                        float a = Mathf.Clamp01(0.5f + (tips[i, 2] - d) * s / 1.5f);
                        if (a > bestA) { bestA = a; hgt = tips[i, 1]; }
                    }
                    var col = Vector3.Lerp(baseC, tipC, Mathf.Clamp01((hgt - 0.10f) / 0.75f));
                    col = Vector3.Lerp(col, Vector3.one, Mathf.Clamp01((0.5f - u) * 0.28f));   // soft left highlight
                    px[y * s + x] = Col(col, bestA);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconCoral", false, 0, FilterMode.Bilinear);
        }

        // Bioluminescent jellyfish: translucent teal dome (bright at the crown) + wavy tentacles.
        static Sprite MakeJellyIconSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = 0.5f, cy = 0.56f, rx = 0.30f, ry = 0.30f;
            float[,] tent = { { 0.34f, 0.0f }, { 0.43f, 1.1f }, { 0.50f, 2.0f }, { 0.57f, 0.6f }, { 0.66f, 1.7f } };
            var domeTop = new Vector3(0.74f, 1.00f, 0.95f);
            var domeBot = new Vector3(0.24f, 0.80f, 0.80f);
            var tentC   = new Vector3(0.48f, 0.92f, 0.84f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float a = 0f; var col = domeBot;
                    if (v >= cy - 0.02f)
                    {
                        float ex = (u - cx) / rx, ey = (v - cy) / ry;
                        float r = Mathf.Sqrt(ex * ex + ey * ey);
                        float dome = Mathf.Clamp01(0.5f + (1f - r) * s * rx / 1.5f);
                        if (dome > a) { a = dome; col = Vector3.Lerp(domeBot, domeTop, Mathf.Clamp01((v - cy) / ry)); }
                    }
                    for (int i = 0; i < tent.GetLength(0); i++)
                    {
                        if (v >= 0.58f) continue;
                        float fall = Mathf.Clamp01((0.58f - v) / 0.46f);
                        float xoff = 0.035f * Mathf.Sin(v * 22f + tent[i, 1]) * fall;
                        float d = Mathf.Abs(u - (tent[i, 0] + xoff));
                        float th = 0.014f * (1f - 0.5f * fall);
                        float ta = Mathf.Clamp01(0.5f + (th - d) * s / 1.5f) * Mathf.Clamp01((v - 0.10f) / 0.10f) * (0.9f - 0.5f * fall);
                        if (ta > a) { a = ta; col = tentC; }
                    }
                    px[y * s + x] = Col(col, a);
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "IconJelly", false, 0, FilterMode.Bilinear);
        }

        // Underwater god-ray: a soft light shaft, bright at the top (surface) fanning + fading downward.
        static Sprite MakeGodRaySprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float hw = 0.06f + 0.28f * (1f - v);           // narrow at top, widening downward
                    float dx = Mathf.Abs(u - 0.5f);
                    float horiz = Mathf.Clamp01(1f - dx / hw); horiz *= horiz;
                    float vert = Mathf.SmoothStep(0f, 1f, v);      // bright at top, fades to nothing at bottom
                    float a = horiz * vert * 0.9f;
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "GodRay", false, 0, FilterMode.Bilinear);
        }

        // Solid filled circle (white, AA edge) — timer coin backing / soft shadow.
        static Sprite MakeDiscSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, R = 0.47f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(0.5f + (R - d));
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "Disc", false, 0, FilterMode.Bilinear);
        }

        // Circular ring / annulus (white, AA) — the timer track and its radial progress arc.
        static Sprite MakeRingCircleSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = s * 0.5f, ro = 0.47f * s, ri = 0.355f * s;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(0.5f + (ro - d)) * Mathf.Clamp01(0.5f + (d - ri));
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();
            return SaveTexAsSprite(tex, "RingCircle", false, 0, FilterMode.Bilinear);
        }

        // Filled 5-point star (white, tinted at runtime) — the Advanced category's icon.
        // The completion trophy: three of THIS GAME'S boxes, nested.
        //
        // The first version was hard-cornered squares sitting inside a circular ring, and it read
        // as wrong for two reasons that are the same reason: shape disagreement. The game's boxes
        // are ROUNDED squares (radiusFrac 0.22 — see `fill` and `shaded`), so a 90-degree glyph is
        // not this game's box; and a square glyph inside a CIRCLE has its corners fighting the ring
        // that frames it.
        //
        // So: the same rounded-square SDF the boxes themselves use, three rings deep, framed later
        // by a rounded-square ring rather than a circle. Every corner in the badge now agrees.
        static Sprite MakeTrophySprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];

            // half-extent of each ring, and its stroke — even steps, so it reads as deliberate
            float[] half  = { 0.440f, 0.290f, 0.140f };
            float[] thick = { 0.052f, 0.046f, 0.040f };
            const float BoxRadius = 0.22f;   // the boxes' own corner radius, as a fraction of the box
            const int SS = 4;                // the strokes are thin; 4x4 supersampling keeps them clean

            // signed distance to a rounded square of half-extent e (negative inside)
            float Sd(float x, float y, float e)
            {
                float r = BoxRadius * e * 2f;                  // radius scales with the ring, so all three match
                float dx = Mathf.Abs(x) - (e - r);
                float dy = Mathf.Abs(y) - (e - r);
                float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) +
                                           Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
            }

            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float fx = (x + (sx + 0.5f) / SS) / s - 0.5f;
                            float fy = (y + (sy + 0.5f) / SS) / s - 0.5f;

                            bool ink = Sd(fx, fy, 0.055f) < 0f;          // the innermost box: solid
                            for (int r = 0; r < half.Length && !ink; r++)
                            {
                                float d = Sd(fx, fy, half[r]);
                                if (d < 0f && d > -thick[r]) ink = true;  // a ring of that stroke
                            }
                            if (ink) hits++;
                        }
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
                }

            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Trophy", false, 0, FilterMode.Bilinear);
        }

        static Sprite MakeStarSprite()
        {
            int s = Res;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float cx = s * 0.5f, cy = s * 0.5f, R = 0.46f * s, r = 0.19f * s;

            var vx = new float[10];
            var vy = new float[10];
            for (int k = 0; k < 10; k++)
            {
                float ang = -Mathf.PI / 2f + k * Mathf.PI / 5f;
                float rad = (k % 2 == 0) ? R : r;
                vx[k] = cx + Mathf.Cos(ang) * rad;
                vy[k] = cy + Mathf.Sin(ang) * rad;
            }

            const int SS = 2;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (PointInPoly(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS, vx, vy)) hits++;
                    byte a = (byte)(255 * hits / (SS * SS));
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }

            tex.SetPixels32(px);
            tex.Apply();
            return SaveTexAsSprite(tex, "Star", false, 0, FilterMode.Bilinear);
        }

        static bool PointInPoly(float x, float y, float[] vx, float[] vy)
        {
            bool inside = false;
            int n = vx.Length, j = n - 1;
            for (int i = 0; i < n; i++)
            {
                if (((vy[i] > y) != (vy[j] > y)) &&
                    (x < (vx[j] - vx[i]) * (y - vy[i]) / (vy[j] - vy[i]) + vx[i]))
                    inside = !inside;
                j = i;
            }
            return inside;
        }

        // ================================================= scene building blocks
        static SpriteRenderer MakeWorldSprite(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        static Camera CreateCamera(bool ortho)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            go.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = ortho;
            cam.orthographicSize = 6f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG;

            var urpCam = go.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderPostProcessing = false;

            go.AddComponent<AudioListener>();
            return cam;
        }

        static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // The module ships with the default UI actions (Point/Click/Move/Submit) already wired,
            // so arrow-key navigation works once a button is focused (handled by MainMenuUI).
            go.AddComponent<InputSystemUIInputModule>();
        }

        static Transform CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Keep every generated 1920x1080 layout fully inside the viewport.  Expand adds safe
            // space on non-16:9 displays instead of cropping fixed-edge HUD and level-map content.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = 0f;
            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        // Full-screen black overlay that fades out on scene load (level transitions).
        // Added last so it draws on top of everything else on the canvas.
        static ScreenFade CreateFade(Transform canvas) => CreateFade(canvas, Color.black);
        static ScreenFade CreateFade(Transform canvas, Color color)
        {
            var rt = MakeRect(canvas, "ScreenFade", Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(color.r, color.g, color.b, 0f); // transparent in the editor;
            img.raycastTarget = false;             // ScreenFade sets alpha=1 at runtime, then fades out
            var fade = rt.gameObject.AddComponent<ScreenFade>();
            fade.image = img;
            return fade;
        }

        static RectTransform MakeRect(Transform parent, string name, Vector2 pos, Vector2 size,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin ?? new Vector2(0.5f, 0.5f);
            rt.anchorMax = anchorMax ?? new Vector2(0.5f, 0.5f);
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // Soft drop shadow so text stays legible over the busy world board / backdrop.
        static Text AddTextShadow(Text t)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.02f, 0.05f, 0.8f);
            sh.effectDistance = new Vector2(0f, -3f);
            return t;
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color,
            Vector2 pos, Vector2 dim, FontStyle style = FontStyle.Normal,
            TextAnchor align = TextAnchor.MiddleCenter,
            Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
        {
            var rt = MakeRect(parent, name, pos, dim, anchorMin, anchorMax, pivot);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = s_font != null ? s_font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize)
            => MakeButton(parent, name, label, pos, size, fontSize, ButtonCol);

        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize, Color color)
            => MakeButton(parent, name, label, pos, size, fontSize, Lighten(color, 0.16f), Darken(color, 0.20f));

        // Premium 3D gradient button: a lit top->bottom gradient face on a darker base lip.
        static Button MakeButton(Transform parent, string name, string label,
            Vector2 pos, Vector2 size, int fontSize, Color top, Color bottom)
        {
            var rt = MakeRect(parent, name, pos, size);
            var btn = rt.gameObject.AddComponent<Button>();
            var hover = rt.gameObject.AddComponent<UIHoverScale>();

            // bright glow behind the button, shown only while hovered or selected (obvious focus)
            var hl = UIImage(rt, "Highlight", s_glow != null ? s_glow : s_roundBtn,
                WithAlpha(Lighten(top, 0.45f), 0.9f), Vector2.zero, new Vector2(size.x + 140f, size.y + 140f));
            hl.gameObject.SetActive(false);
            hover.highlight = hl.gameObject;

            var round = s_roundBtn != null ? s_roundBtn
                : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // 3D base "lip" (darker, offset down) + a soft floating shadow — chunky & tactile
            var lipRT = MakeRect(rt, "Lip", new Vector2(0f, -7f), size);
            var lipImg = lipRT.gameObject.AddComponent<Image>();
            lipImg.sprite = round; lipImg.type = Image.Type.Sliced;
            lipImg.color = Darken(bottom, 0.32f); lipImg.raycastTarget = false;
            var sh = lipRT.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.42f);
            sh.effectDistance = new Vector2(0f, -7f);

            // lit top face with a vibrant top->bottom gradient — Button tints this
            var faceRT = MakeRect(rt, "Face", Vector2.zero, size);
            var face = faceRT.gameObject.AddComponent<Image>();
            face.sprite = round; face.type = Image.Type.Sliced; face.color = Color.white;
            var grad = faceRT.gameObject.AddComponent<UIGradient>();
            grad.top = top; grad.bottom = bottom;
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;

            // glossy top-light so the face reads as a rounded, lit surface
            if (s_gloss != null)
                UIImage(faceRT, "Gloss", s_gloss, new Color(1f, 1f, 1f, 0.10f), Vector2.zero, size);

            // Label weight: the typeface is fixed (one font ships in Assets/Parabox/Fonts), so the
            // presence has to come from treatment — a dark outline to bite against the lit face,
            // plus a drop shadow underneath it. Outline first so the shadow reads below both.
            var lbl = MakeText(faceRT, "Label", label, fontSize, Color.white, Vector2.zero, size, FontStyle.Bold);
            var lout = lbl.gameObject.AddComponent<Outline>();
            lout.effectColor = new Color(0f, 0.06f, 0.10f, 0.85f);
            lout.effectDistance = new Vector2(1.6f, -1.6f);
            var lsh = lbl.gameObject.AddComponent<Shadow>();
            lsh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            lsh.effectDistance = new Vector2(0f, -3f);
            return btn;
        }

        // A non-interactive rounded UI image (menu decoration / mascot).
        // A clean flat control button — rounded slate fill + thin accent outline + centered label. Clickable.
        // One half of the split "PARAB[board]X" title, styled like the original PARABOX logo.
        static void TitleWord(Transform parent, string name, string text)
        {
            var t = MakeText(parent, name, text, 108, Color.white, Vector2.zero, new Vector2(260, 150), FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var g = t.gameObject.AddComponent<UIGradient>();   // gradient fill (added before Shadow)
            g.top = Hex("5FE6EA"); g.bottom = Hex("2E86C4");
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(0f, -7f);
        }

        // The "O" in PARABOX is a TRANSPARENT slot in the logo. The real world-space board renders behind
        // the canvas and is framed by the menu camera to sit exactly in `inner` (its on-screen anchor).
        static RectTransform MakeTitleBoardO(Transform parent, Sprites spr, out RectTransform inner, out RectTransform ringRT)
        {
            var board = MakeRect(parent, "TitleBoardO", Vector2.zero, new Vector2(118, 118));
            var le = board.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 118f; le.preferredHeight = 118f;

            // The letter O itself. Without this the only outline was the world board's own frame,
            // which is sized to the LEVEL — so a wide level made the "O" read as a dash. This ring is
            // a fixed square, styled like the other letters (same gradient + drop shadow), so PARABOX
            // always reads correctly no matter what shape the level behind it is.
            var ring = UIImage(board, "ORing", spr.ringThick, Color.white, Vector2.zero, new Vector2(118, 118));
            var rg = ring.gameObject.AddComponent<UIGradient>();
            rg.top = Hex("5FE6EA"); rg.bottom = Hex("2E86C4");
            var rsh = ring.gameObject.AddComponent<Shadow>();
            rsh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            rsh.effectDistance = new Vector2(0f, -7f);

            // invisible spacer: reserves the O gap in the word and anchors the world board (via GetWorldCorners)
            var innerImg = UIImage(board, "OInner", null, new Color(0f, 0f, 0f, 0f), Vector2.zero, new Vector2(100, 100));
            inner = innerImg.rectTransform;
            ringRT = ring.rectTransform;
            return board;
        }

        // A big keycap-style title-screen button (label inside), clickable AND keyboard-navigable.
        static Button MakePrompt(Transform parent, string name, string label, Vector2 pos, Vector2 size, Sprites spr)
        {
            var hl = UIImage(parent, name + "Glow", s_glow, WithAlpha(Hex("59E1F0"), 0.85f), pos, size + new Vector2(80f, 80f));
            hl.gameObject.SetActive(false);

            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = WithAlpha(Hex("0B2A44"), 0.62f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.88f, 0.94f, 1f); colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f; btn.colors = colors;
            var hover = rt.gameObject.AddComponent<UIHoverScale>();
            hover.highlight = hl.gameObject;

            // glossy top sheen
            var gloss = UIImage(rt, "Gloss", spr.fill, Color.white, new Vector2(0f, size.y * 0.02f), size - new Vector2(8f, 8f));
            gloss.type = Image.Type.Sliced;
            var gg = gloss.gameObject.AddComponent<UIGradient>();
            gg.top = WithAlpha(Color.white, 0.12f); gg.bottom = WithAlpha(Color.white, 0f);

            // crisp cyan neon border — the keycap look
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(Hex("6FEAF2"), 0.95f));

            MakeText(rt, "Lbl", label, 34, Hex("EAFBFF"), Vector2.zero, size, FontStyle.Bold);
            return btn;
        }

        // A small keycap chip showing which keyboard key triggers a control button.
        static void MakeKeyHint(Transform parent, string key, Vector2 pos)
        {
            float w = 30f + Mathf.Max(0, key.Length - 1) * 13f;
            var size = new Vector2(w, 28f);
            // soft shadow beneath for a raised keycap
            UIImage(parent, "KeyShadow_" + key, s_glow, WithAlpha(Hex("020912"), 0.5f), pos + new Vector2(0f, -3f), size + new Vector2(6f, 8f));
            // keycap body with a two-tone gradient + neon edge
            var chip = UIImage(parent, "Key_" + key, s_roundBtn, Color.white, pos, size);
            chip.type = Image.Type.Sliced;
            var g = chip.gameObject.AddComponent<UIGradient>();
            g.top = Hex("17394F"); g.bottom = Hex("0A2236");
            var ol = chip.gameObject.AddComponent<Outline>();
            ol.effectColor = WithAlpha(Hex("46CEE0"), 0.7f);
            ol.effectDistance = new Vector2(1.2f, -1.2f);
            // glossy top sheen
            var gloss = UIImage(chip.transform, "KGloss", s_roundBtn, Color.white, Vector2.zero, size - new Vector2(4f, 4f));
            gloss.type = Image.Type.Sliced;
            var gg = gloss.gameObject.AddComponent<UIGradient>();
            gg.top = WithAlpha(Color.white, 0.18f); gg.bottom = WithAlpha(Color.white, 0f);
            MakeText(chip.transform, "K", key, 13, Hex("DFF4FA"), Vector2.zero, size, FontStyle.Bold);
        }

        static Button MakeFlatButton(Transform parent, string name, string label, int fontSize,
                                     Vector2 pos, Vector2 size, Color accent, Sprites spr)
        {
            // soft drop shadow for a raised, tactile feel (furthest back)
            UIImage(parent, name + "Shadow", spr.glow, WithAlpha(Hex("020912"), 0.5f),
                pos + new Vector2(0f, -9f), new Vector2(size.x + 4f, size.y + 14f));

            // bright glow behind the button, shown only while hovered or selected
            var hl = UIImage(parent, name + "HL", spr.glow, WithAlpha(Lighten(accent, 0.5f), 0.9f),
                pos, new Vector2(size.x + 62f, size.y + 62f));
            hl.gameObject.SetActive(false);

            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill; img.type = Image.Type.Sliced; img.color = Color.white;
            var grad = rt.gameObject.AddComponent<UIGradient>();   // premium two-tone fill
            grad.top = Hex("204E6A"); grad.bottom = Hex("0A2034");
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.80f, 0.86f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var hover = rt.gameObject.AddComponent<UIHoverScale>();
            hover.highlight = hl.gameObject;

            // glossy sheen fading down from the top edge
            var gloss = UIImage(rt, "Gloss", spr.fill, Color.white, new Vector2(0f, size.y * 0.02f), size - new Vector2(6f, 6f));
            gloss.type = Image.Type.Sliced;
            var gg = gloss.gameObject.AddComponent<UIGradient>();
            gg.top = WithAlpha(Color.white, 0.16f); gg.bottom = WithAlpha(Color.white, 0f);

            // neon outline: a faint halo ring plus a crisp bright edge
            AddRing(rt, "OutlineGlow", size + Vector2.one * 7f, spr.ringThin, WithAlpha(accent, 0.3f));
            AddRing(rt, "Outline", size, spr.ringThin, WithAlpha(accent, 0.95f));

            if (!string.IsNullOrEmpty(label))
            {
                var lbl = MakeText(rt, "Label", label, fontSize, Color.white, Vector2.zero, size, FontStyle.Bold);
                var sh = lbl.gameObject.AddComponent<Shadow>();
                sh.effectColor = WithAlpha(Hex("02121C"), 0.6f);
                sh.effectDistance = new Vector2(0f, -2f);
            }
            return btn;
        }

        static Image UIImage(Transform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var rt = MakeRect(parent, name, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // "Beaten" checkmark badge for a level button; hidden until MainMenuUI shows it.
        static GameObject MakeCheck(Transform button, Sprites spr)
        {
            var rt = MakeRect(button, "Check", new Vector2(32, 20), new Vector2(30, 30));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = spr.fill;
            img.color = Hex("34C759");
            img.raycastTarget = false;
            MakeText(rt, "Tick", "✓", 22, Color.white, Vector2.zero, new Vector2(30, 30), FontStyle.Bold);
            rt.gameObject.SetActive(false);
            return rt.gameObject;
        }

        // ================================================= color helpers
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);
    }
}
