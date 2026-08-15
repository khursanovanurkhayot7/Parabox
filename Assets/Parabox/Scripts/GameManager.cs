using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Parabox
{
    // A per-difficulty-tier visual skin for the level. Gameplay is unchanged — only colors.
    [System.Serializable]
    public class LevelTheme
    {
        public Color[] roomColors;  // the CELL/tile color per room id (main room = 0)
        public Color gutter;        // the dark base the tiles sit on — shows between cells as bold grid lines
        public Color wall;
        public Color box;
        public Color player;
        public Color grid;
        public Color frame;          // board rim / neon frame (cyan, to match the title's O board)
        public float floorVignette;  // 0..1 inner-shadow depth on the floor
        public float pieceGlow;      // 0..1 neon glow halo behind boxes / player
        public float cellLift;       // 0..~0.16 how much lighter each grid cell is vs the floor (contrast)
        public Sprite floorTex;      // tileable surface pattern (grass / water / cracked rock)
        public Color floorTexTint;   // tint + alpha for the pattern overlay
    }

    public class GameManager : MonoBehaviour
    {
        [Header("Levels (prefabs, in order)")]
        public GameObject[] levelPrefabs;

        [Header("Tile prefabs")]
        public GameObject floorPrefab;
        public GameObject gridPrefab;
        public GameObject borderPrefab;
        public GameObject wallPrefab;
        public GameObject boxPrefab;
        public GameObject metaBoxPrefab;
        public GameObject playerPrefab;
        public GameObject boxGoalPrefab;
        public GameObject playerGoalPrefab;

        [Header("Room colors (index = room id)")]
        public Color[] roomColors;

        [Header("Per-tier level themes (index = level / 10)")]
        public LevelTheme[] levelThemes;

        [Header("Effect colors / sprites")]
        public Color boxColor = Color.white;
        public Color playerColor = Color.white;
        public Sprite pieceSprite;
        public Sprite ringSprite;
        public Sprite glowSprite;
        public Sprite vignetteSprite;
        public Sprite cellSprite;   // premium board-cell tile (soft top-lit square)

        [Header("Scene references")]
        public CameraFollow cameraFollow;
        public Text levelLabel;
        public Text movesLabel;
        public GameObject winPanel;
        public Text winTitle;
        public Text winStats;
        public Button nextButton;
        public Button menuButton;

        [Header("Prebuilt score HUD")]
        [Tooltip("Generated into Game.unity by Tools/Parabox/Generate Prebuilt UI (Run This).")]
        public RectTransform scoreRoot;
        public Text scoreLabel;
        int lastScoreGain;

        [Header("Timer")]
        public RectTransform timerRoot;
        public CanvasGroup timerCanvas;
        public Image timerFill;          // radial ring that depletes
        public Text timerLabel;
        public Color[] timerAccents;     // ring colour per difficulty tier
        public GameObject timeUpPanel;   // the lose panel (shared by both failure kinds)
        public Button retryButton;
        public Button backToLevelsButton;

        // Menu auto-start and gameplay pressure are separate: once a level begins, its own timer
        // follows puzzle complexity and grants extra reading time to chapter-opening lessons.
        // Running out of gameplay time is terminal; Luxodd Continue restores a complete fresh
        // allowance without changing the current puzzle state.
        const bool GameplayCountdownEnabled = true;

        [Header("Lose sequence")]
        public LoseFx loseFx;            // drives the freeze / jolt / dim / verdict / options beats
        public ScreenFade screenFade;    // used to fade OUT before leaving for the map

        [Header("Tutorial (level 1, first run only)")]
        public TutorialFx tutorialFx;      // the cinematic chrome (letterbox / caption / options)
        public CanvasGroup hudGroup;       // the ENTIRE gameplay HUD — hidden for the cinematic
        public HudReveal hudReveal;        // its seamless-entry reveal, suspended during the cinematic
        public Camera tutorialBgCamera;    // fills the display while the main camera renders to the panel

        [Header("On-screen controls")]
        public Button upButton, downButton, leftButton, rightButton;
        public Button undoButton, restartButton, muteButton, hudMenuButton;
        public GameObject muteOnIcon, muteOffIcon;   // toggled to show current audio state

        const string LevelKey = "Parabox.Level";
        const string OpenLevelsKey = "Parabox.OpenLevels";
        public const string EditorPreviewLevelKey = "Parabox.EditorPreviewLevel";
        static string BestKey(int level) => "Parabox.Best." + level;
        const string SessionClearPrefix = "Parabox.Session.Clear.";
        public static string SessionClearKey(int level) => SessionClearPrefix + level;
        const float InteriorFit = 0.72f; // interior room size relative to its box cell

        // Sorting orders. Entities (box 6, player 8, meta-box group 6) are baked into
        // the prefabs by the wizard; these are the room-decoration layers below them.
        const int OrderFloorBase = 0;   // floor; the frame base sits at OrderFloorBase - 1 (behind the inset floor)
        const int OrderFloorCell = 1;
        const int OrderWall = 2;
        const int OrderGoal = 4;

        LevelModel model;
        readonly Dictionary<int, Transform> roomRoots = new Dictionary<int, Transform>();
        readonly Dictionary<PEntity, EntityView> views = new Dictionary<PEntity, EntityView>();
        readonly BoardTiles tiles = new BoardTiles();
        readonly Dictionary<PEntity, (int room, Vector2Int pos)> moveStartPositions
            = new Dictionary<PEntity, (int room, Vector2Int pos)>();
        readonly Dictionary<(int room, int x, int y), bool> goalsBeforeMove
            = new Dictionary<(int room, int x, int y), bool>();
        readonly Dictionary<(int room, int x, int y), bool> goalsAfterMove
            = new Dictionary<(int room, int x, int y), bool>();
        HiddenDiscoveryFx hiddenDiscovery;
        // FocusRoom used to traverse the complete recursive board after every move. Large late-game
        // boards contain hundreds of renderers, so that repeated hierarchy scan caused visible
        // stalls. The rendered hierarchy is immutable during a level; cache it once and skip the
        // visibility pass entirely while the active room/cinematic state has not changed.
        Renderer[] focusRenderers;
        int focusedRoomId = int.MinValue;
        bool focusedCinematic;
        int levelIndex;
        bool won;
        Vector2Int lastHeld;
        float nextRepeat;
        float timeLeft;
        float timeLimit;
        // A level's allowance begins with the player's first successful move, not while the board
        // is arriving or the player is reading it. This is especially important at world changes,
        // where the presentation beat used to spend several seconds of World 2's clock.
        bool countdownArmed;
        bool timedUp;
        int par;
        int moveLimit;
        bool outOfMoves;
        bool levelEndReported;
        bool tutorialExiting;
        string mechanicBriefingSignature;
        GameObject mechanicSpotlightRoot;
        bool tutorialFromMainPlay;
        bool editorPreviewMode;
        [Header("Prebuilt finale")]
        public FinaleFx finaleOverlay;
        bool finaleSequenceActive;
        bool lossTransactionRequested;

        // Any terminal state — won, timed out, or out of moves. Every input guard tests this, so a
        // new failure kind can never accidentally leave the board still playable. The tutorial
        // counts too: while the game is demonstrating itself, the player's keys must do nothing
        // and the clock must not run.
        bool Ended => won || timedUp || outOfMoves || Tutoring;
        // The whole onboarding — cinematic AND the choice panel that follows it. While this is true
        // the board is not the player's: no input, no clock. GameManager owns the flag (it owns the
        // timeline), so there is one source of truth rather than a reach into the UI component.
        bool Tutoring => cinematic;
        bool cinematic;
        // Lost, specifically — the win branch needs its own handling (Space = next level).
        bool Lost => timedUp || outOfMoves;
        int MovesLeft => Mathf.Max(0, moveLimit - model.MoveCount);

        // Slack over the solver's optimal, removed in measured bands: forgiving while you're still
        // learning the vocabulary, demanding once you know it. Undo refunds the move itself so
        // difficulty comes from understanding the puzzle rather than from punishing experiments.
        // Each level now also has a clock-pressure mechanic, so the move allowance is deliberately
        // tighter: enough room to read a new rule, but not enough to brute-force the board. The
        // solver's par remains the floor, which guarantees the proven route always fits.
        static int MoveSlack(int levelIdx)
            => CampaignProgression.ForLevel(levelIdx).moveSlack;

        // Shared with the campaign validator so gameplay and automated QA can never drift onto
        // different timer or move-limit formulas.
        public static int MoveLimitForLevel(int levelIdx, int levelPar)
            => levelPar > 0 ? levelPar + MoveSlack(levelIdx) : 999;

        public static float TimeLimitForLevel(int levelIdx, int levelPar)
            => CampaignProgression.TimeLimit(levelIdx, levelPar);

        float timerIntro;
        float tickBump;
        int lastSecond = -1;
        Color timerAccentCur = new Color(1f, 0.62f, 0.37f, 1f);
        const float IntroDur = 0.5f;
        static readonly Color TimerWarn = new Color(0.96f, 0.36f, 0.34f, 1f);
        Color wallColor = new Color(0.078f, 0.102f, 0.157f, 1f);
        Color gridColor = new Color(0f, 0f, 0f, 0.12f);
        Color frameColor = new Color(0.435f, 0.918f, 0.949f, 1f);   // cyan board frame (matches the O board)
        Color gutterColor = new Color(0.07f, 0.20f, 0.29f, 1f);      // dark base between tiles
        float floorVignette;
        float pieceGlow;
        float cellLift = 0.10f;
        Sprite floorTex;
        Color floorTexTint = Color.clear;

        void Start()
        {
            RepairLevelPrefabReferencesInEditor();
            RepairGameplayCanvasScales();
            Sfx.Init();
            Fx.Piece = pieceSprite;
            Fx.Ring = ringSprite;
            Fx.Glow = glowSprite;

            // Editor visual-QA can request one exact board without disturbing or being overridden
            // by the player's normal saved campaign position. The key is consumed immediately.
            int editorPreviewLevel = PlayerPrefs.GetInt(EditorPreviewLevelKey, -1);
            editorPreviewMode = editorPreviewLevel >= 0;
            if (editorPreviewLevel >= 0)
            {
                PlayerPrefs.DeleteKey(EditorPreviewLevelKey);
                PlayerPrefs.Save();
            }
            levelIndex = Mathf.Clamp(editorPreviewLevel >= 0
                ? editorPreviewLevel
                : PlayerPrefs.GetInt(LevelKey, 0), 0, levelPrefabs.Length - 1);
            tutorialFromMainPlay = PlayerPrefs.GetInt(MainMenuUI.MainPlayTutorialKey, 0) == 1;
            if (tutorialFromMainPlay)
            {
                PlayerPrefs.DeleteKey(MainMenuUI.MainPlayTutorialKey);
                PlayerPrefs.Save();
            }
            ApplyLevelTheme(levelIndex / 10);   // Beginner / Intermediate / Advanced skin
            ConfigureChapterPresentation(levelIndex / 10);
            if (loseFx != null) loseFx.SetLeaderboardTheme(frameColor, gutterColor);
            model = LevelParser.Parse(levelPrefabs[levelIndex]);
            BuildView();
            SyncViews(true);
            // arriving from the main-menu "dive": fly the camera in from far out into the board.
            // The level-1 cinematic owns the camera from the first frame, so the normal fly-in is
            // suppressed there (the flag is still consumed so it can't leak into a later level).
            if (PlayerPrefs.GetInt("Parabox.FlyIn", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.FlyIn");
                if (cameraFollow != null && !WillTutorial())
                {
                    ConfigureChapterArrival(cameraFollow, levelIndex / 10);
                    cameraFollow.PlayIntro();
                }
            }

            // Older browser saves may still contain the retired Level-50-only entrance flag.
            // Consume it without changing the camera: Level 50 now enters exactly through the
            // same shared responsive path as the other 49 levels.
            if (PlayerPrefs.GetInt("Parabox.FinalRun", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.FinalRun");
            }
            UpdateHud();
            winPanel.SetActive(false);
            nextButton.onClick.AddListener(NextLevel);
            menuButton.onClick.AddListener(GoToMenu);
            if (tutorialFx != null)
            {
                if (tutorialFx.againButton != null) tutorialFx.againButton.onClick.AddListener(TutorialWatchAgain);
                if (tutorialFx.tryButton   != null) tutorialFx.tryButton.onClick.AddListener(TutorialTryIt);
                if (tutorialFx.skipButton  != null) tutorialFx.skipButton.onClick.AddListener(TutorialSkip);
            }

            var levelInfo = levelPrefabs[levelIndex].GetComponent<ParaboxLevel>();
            par = levelInfo != null ? levelInfo.par : 0;

            // The countdown scales with the PUZZLE, not the level number. It used to be
            // 10 + levelIndex, which was fine while every level sat near the same par — but pars
            // now run from 1 to 22, and that formula handed level 20 a 22-move puzzle and 29
            // seconds. Roughly three seconds a move plus a thinking cushion, and never under 20s,
            // so the clock is pressure rather than a dexterity test.
            timeLimit = TimeLimitForLevel(levelIndex, par);
            timeLeft = timeLimit;
            countdownArmed = false;
            // par == 0 means the prefab predates par data (wizard not re-run). Fall back to an
            // unrestrictive limit rather than handing the player an unwinnable level.
            moveLimit = MoveLimitForLevel(levelIndex, par);
            timedUp = false;
            // seamless menu-entry: the HUD is already "there" — skip the timer's scale/fade-in
            timerIntro = PlayerPrefs.GetInt("Parabox.Seamless", 0) == 1 ? IntroDur : 0f;

            // Last: the level-1 demonstration needs par, timeLimit and the wired buttons to exist
            // before it can borrow the board and hand it back untouched.
            MaybeTutorial();
            tickBump = 0f;
            lastSecond = -1;
            int tier = Mathf.Clamp(levelIndex / 10, 0,
                (timerAccents != null && timerAccents.Length > 0) ? timerAccents.Length - 1 : 0);
            timerAccentCur = (timerAccents != null && timerAccents.Length > 0)
                ? timerAccents[tier] : new Color(1f, 0.62f, 0.37f, 1f);
            if (scoreRoot == null || scoreLabel == null)
                Debug.LogError("[Parabox] Score HUD is not prebuilt. Run Tools/Parabox/Generate Prebuilt UI (Run This) before Play or Build.");
            UpdateHud();
            if (timeUpPanel != null) timeUpPanel.SetActive(false);
            // Luxodd owns the post-loss decision. Hide legacy serialized action holders so scenes
            // created before this flow was introduced cannot expose local Continue / Levels UI.
            HideLegacyLossAction(retryButton);
            HideLegacyLossAction(backToLevelsButton);
            WireOnScreenControls();
            if (timerRoot != null)
                timerRoot.gameObject.SetActive(GameplayCountdownEnabled && !editorPreviewMode);
            if (GameplayCountdownEnabled && !editorPreviewMode) AnimateTimer();
            LuxoddGameService.ReportLevelBegin(levelIndex);
        }

        #if UNITY_EDITOR
        // Editor-authoring only. The one-click prebuilder runs this while Game.unity is open in
        // edit mode; the resulting components and references are serialized into the scene.
        public void PrebuildStaticUi()
        {
            ArcadeActionButtonStyle.Apply(nextButton, "CONTINUE", 24);
            ArcadeActionButtonStyle.Apply(menuButton, "LEVELS", 24);

            if (tutorialFx != null) tutorialFx.PrebuildStaticUi();
            if (loseFx != null) loseFx.PrebuildStaticUi();
            BuildScoreHud();

            Canvas finaleCanvas = winPanel != null ? winPanel.GetComponentInParent<Canvas>() : null;
            if (finaleOverlay == null && finaleCanvas != null)
                finaleOverlay = finaleCanvas.GetComponentInChildren<FinaleFx>(true);
            if (finaleOverlay != null && !finaleOverlay.IsFullyPrebuilt)
            {
                Object.DestroyImmediate(finaleOverlay.gameObject);
                finaleOverlay = null;
            }
            if (finaleOverlay == null)
            {
                Font font = winTitle != null ? winTitle.font
                    : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (finaleCanvas != null)
                    finaleOverlay = FinaleFx.Prebuild(finaleCanvas, font, glowSprite, cellSprite);
            }

            foreach (Button button in GetComponentsInChildren<Button>(true))
                Sfx.AttachButton(button);

            AddPrebuiltHold(upButton);
            AddPrebuiltHold(downButton);
            AddPrebuiltHold(leftButton);
            AddPrebuiltHold(rightButton);

            Transform bar = upButton != null ? upButton.transform.parent
                : undoButton != null ? undoButton.transform.parent : null;
            Transform joystick = bar != null ? bar.Find("ArcadeJoystickHud") : null;
            if (joystick != null && joystick.GetComponent<ArcadeJoystickControl>() == null)
                joystick.gameObject.AddComponent<ArcadeJoystickControl>();
        }

        static void AddPrebuiltHold(Button button)
        {
            if (button != null && button.GetComponent<HoldRepeatButton>() == null)
                button.gameObject.AddComponent<HoldRepeatButton>();
        }
        #endif

        static void HideLegacyLossAction(Button button)
        {
            if (button == null) return;
            Transform holder = button.transform.parent;
            if (holder != null) holder.gameObject.SetActive(false);
            else button.gameObject.SetActive(false);
        }

        // When Unity's fast Play Mode skips a domain reload, an asset reimport can leave the
        // serialized prefab array pointing at destroyed in-memory objects even though the scene
        // YAML and prefab files are valid. Recover the assets by path before parsing a level. This
        // is editor-only and therefore adds no AssetDatabase dependency to WebGL builds.
        void RepairLevelPrefabReferencesInEditor()
        {
#if UNITY_EDITOR
            bool broken = levelPrefabs == null || levelPrefabs.Length != 50;
            if (!broken)
                for (int i = 0; i < levelPrefabs.Length; i++)
                    if (levelPrefabs[i] == null) { broken = true; break; }

            if (!broken) return;

            var recovered = new GameObject[50];
            for (int i = 0; i < recovered.Length; i++)
            {
                recovered[i] = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Parabox/Prefabs/Levels/Level_{i + 1}.prefab");
                if (recovered[i] == null)
                {
                    Debug.LogError($"Level prefab {i + 1} could not be recovered. Reimport the Levels folder.");
                    return;
                }
            }
            levelPrefabs = recovered;
            Debug.Log("[Parabox] Recovered 50 stale level prefab references.");
#endif
        }

        // A zero-scale root Canvas renders a permanently black frame during the tutorial because
        // the gameplay camera is intentionally redirected into the cinematic panel. Unity can
        // preserve that accidental Inspector state in the scene, so repair both UI roots before
        // any tutorial, fade, timer, or HUD setup runs.
        void RepairGameplayCanvasScales()
        {
            EnsureUsableCanvasScale(hudGroup != null ? hudGroup.transform : null);

            if (tutorialFx != null)
            {
                Canvas tutorialCanvas = tutorialFx.GetComponent<Canvas>();
                EnsureUsableCanvasScale(tutorialCanvas != null
                    ? tutorialCanvas.transform
                    : tutorialFx.transform);
            }
        }

        static void EnsureUsableCanvasScale(Transform canvasTransform)
        {
            if (canvasTransform == null) return;

            Vector3 scale = canvasTransform.localScale;
            const float MinScale = 0.0001f;
            if (Mathf.Abs(scale.x) < MinScale ||
                Mathf.Abs(scale.y) < MinScale ||
                Mathf.Abs(scale.z) < MinScale)
            {
                canvasTransform.localScale = Vector3.one;
            }
        }

        // On-screen buttons drive the exact same logic as the keyboard (touch / click support).
        void WireOnScreenControls()
        {
            // d-pad: press-and-hold to move (fires on press + repeats while held)
            WireHold(upButton,    Vector2Int.up);
            WireHold(downButton,  Vector2Int.down);
            WireHold(leftButton,  Vector2Int.left);
            WireHold(rightButton, Vector2Int.right);
            WireClick(undoButton, UiUndo);
            WireClick(restartButton, Restart);
            WireClick(muteButton, UiMute);
            WireClick(hudMenuButton, GoToMenu);
            WireArcadeJoystick();
            UpdateMuteIcon();
        }

        static void WireClick(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.interactable = true;
            if (button.targetGraphic != null) button.targetGraphic.raycastTarget = true;
            button.onClick.AddListener(action);
        }

        // The scene-baked joystick used to be presentation only. Give the exact visible control a
        // hit target and route tap/drag/hold input through UiMove, the same entry point used by the
        // hidden fallback d-pad and the physical Luxodd joystick.
        void WireArcadeJoystick()
        {
            Transform bar = upButton != null ? upButton.transform.parent
                : undoButton != null ? undoButton.transform.parent : null;
            if (bar == null) return;

            Transform joystick = bar.Find("ArcadeJoystickHud");
            if (joystick == null) return;

            Image hitTarget = joystick.GetComponent<Image>();
            if (hitTarget != null) hitTarget.raycastTarget = true;

            var control = joystick.GetComponent<ArcadeJoystickControl>();
            if (control == null)
            {
                Debug.LogError("[Parabox] ArcadeJoystickControl is not prebuilt. Run the Prebuilt UI generator.");
                return;
            }
            control.Configure(UiMove, joystick.Find("Ball") as RectTransform);
        }

        void WireHold(Button b, Vector2Int dir)
        {
            if (b == null) return;
            var h = b.GetComponent<HoldRepeatButton>();
            if (h == null)
            {
                Debug.LogError("[Parabox] HoldRepeatButton is not prebuilt on " + b.name + ". Run the Prebuilt UI generator.");
                return;
            }
            h.onFire = () => UiMove(dir);
        }

        public void UiMove(Vector2Int dir)
        {
            if (Ended) return;
            DoMove(dir);
        }

        public void UiUndo()
        {
            if (Ended) return;
            if (model.Undo())
            {
                Sfx.Undo();
                SyncViews(false);
                if (!Ended) UpdateHud();
            }
        }

        public void UiMute()
        {
            Sfx.ToggleMute();
            UpdateMuteIcon();
        }

        void UpdateMuteIcon()
        {
            if (muteOnIcon != null) muteOnIcon.SetActive(!Sfx.Muted);
            if (muteOffIcon != null) muteOffIcon.SetActive(Sfx.Muted);
        }

        // Cabinet mapping: stick=move, Black=confirm/retry, Red=undo, Green=restart,
        // Yellow/White=level select/back, Blue=mute, Purple=skip walkthrough. Orange remains Luxodd's.
        bool HandleArcadeInput()
        {
            var arcade = LuxoddArcadeAdapter.Instance;
            if (arcade == null) return false;

            if (arcade.MuteDown)
            {
                Sfx.Click();
                Sfx.ToggleMute();
                UpdateMuteIcon();
                return true;
            }
            if (Lost)
            {
                // The leaderboard is informational and Luxodd owns the upcoming transaction.
                // Swallow all gameplay/cabinet input until the host returns a choice.
                return true;
            }

            if (finaleSequenceActive)
            {
                return finaleOverlay != null && finaleOverlay.HandleArcadeInput(arcade);
            }

            if (Tutoring)
            {
                if (arcade.SkipDown)
                {
                    Sfx.Click();
                    TutorialSkip();
                    return true;
                }
                return HandleArcadeTutorialInput(arcade);
            }

            if (arcade.BackDown || arcade.LevelsDown)
            {
                Sfx.Click();
                GoToMenu();
                return true;
            }

            if (won)
            {
                if (arcade.ConfirmDown) { Sfx.Click(); NextLevel(); return true; }
                return false;
            }
            if (arcade.RestartDown) { Sfx.Click(); Restart(); return true; }
            if (arcade.UndoDown)
            {
                UiUndo();
                return true;
            }
            if (arcade.MovePulse && arcade.Direction != Vector2Int.zero)
            {
                DoMove(arcade.Direction);
                return true;
            }
            return false;
        }

        bool HandleArcadeTutorialInput(LuxoddArcadeAdapter arcade)
            => HandleTutorialChoiceInput(arcade.Direction, arcade.NavigationPulse, arcade.ConfirmDown);

        bool HandleTutorialChoiceInput(Vector2Int direction, bool movePulse, bool confirmDown)
        {
            if (tutorialFx == null || tutorialExiting)
                return false;

            if (tutorialFx.choiceGroup == null || !tutorialFx.choiceGroup.interactable)
                return false;

            var buttons = new[] { tutorialFx.againButton, tutorialFx.tryButton };
            int selected = 1;
            GameObject selectedObject = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i] != null && buttons[i].gameObject == selectedObject) selected = i;

            if (movePulse && direction != Vector2Int.zero)
            {
                int step = direction.x < 0 || direction.y > 0 ? -1 : 1;
                for (int tries = 0; tries < buttons.Length; tries++)
                {
                    selected = (selected + step + buttons.Length) % buttons.Length;
                    if (buttons[selected] == null || !buttons[selected].gameObject.activeInHierarchy
                        || !buttons[selected].interactable) continue;
                    if (EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(buttons[selected].gameObject);
                    Sfx.Hover();
                    break;
                }
                return true;
            }

            if (confirmDown)
            {
                Button button = selectedObject != null ? selectedObject.GetComponent<Button>() : null;
                if (button == null || !button.interactable) button = tutorialFx.tryButton;
                if (button != null) button.onClick.Invoke();
                return true;
            }
            return false;
        }

        bool _flagsConsumed;

        void Update()
        {
            // clear the one-shot menu-entry flag once, on the first Update — i.e. AFTER every Start()
            // has read it (ScreenFade + this component read it in Start; order between Starts is undefined).
            if (!_flagsConsumed) { _flagsConsumed = true; PlayerPrefs.DeleteKey("Parabox.Seamless"); }

            if (GameplayCountdownEnabled && !editorPreviewMode)
            {
                TickTimer();
                AnimateTimer();
            }

            if (HandleArcadeInput()) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.mKey.wasPressedThisFrame) { Sfx.ToggleMute(); UpdateMuteIcon(); }

            if (finaleSequenceActive)
            {
                if (finaleOverlay != null) finaleOverlay.HandleKeyboardInput(kb);
                return;
            }

            if (Lost)
            {
                // No local escape, retry, or level-select action is available after death.
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                GoToMenu();
                return;
            }

            // The tutorial is driving the board — the player's keys must not fight it. Escape and
            // mute are handled above and stay live, so they're never trapped in the demo.
            //
            // lastHeld keeps tracking reality even though nothing acts on it: if it froze at zero
            // while a key was held down, the first frame after the demo would read that key as a
            // fresh press and fire a move the player never asked for, on the board the tutorial
            // just promised to hand back untouched.
            if (Tutoring)
            {
                lastHeld = ReadDirection(kb);
                if (kb.tabKey.wasPressedThisFrame)
                {
                    TutorialSkip();
                    return;
                }
                Vector2Int tutorialDirection = ReadDirectionDown(kb);
                HandleTutorialChoiceInput(tutorialDirection,
                    tutorialDirection != Vector2Int.zero,
                    kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
                return;
            }

            if (won)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    NextLevel();
                return;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                Restart();
                return;
            }

            if (kb.zKey.wasPressedThisFrame)
            {
                if (model.Undo())
                {
                    Sfx.Undo();
                    SyncViews(false);
                    UpdateHud();
                }
                return;
            }

            // Movement with hold-to-repeat.
            Vector2Int held = ReadDirection(kb);
            if (held != lastHeld)
            {
                lastHeld = held;
                if (held != Vector2Int.zero)
                {
                    DoMove(held);
                    nextRepeat = Time.unscaledTime + 0.27f;
                }
            }
            else if (held != Vector2Int.zero && Time.unscaledTime >= nextRepeat)
            {
                DoMove(held);
                nextRepeat = Time.unscaledTime + 0.13f;
            }
        }

        static Vector2Int ReadDirection(Keyboard kb)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) return Vector2Int.up;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) return Vector2Int.down;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) return Vector2Int.left;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) return Vector2Int.right;
            return Vector2Int.zero;
        }

        static Vector2Int ReadDirectionDown(Keyboard kb)
        {
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) return Vector2Int.up;
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) return Vector2Int.down;
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) return Vector2Int.left;
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) return Vector2Int.right;
            return Vector2Int.zero;
        }

        void DoMove(Vector2Int dir)
        {
            CapturePositions(moveStartPositions);
            CaptureSatisfiedGoals(goalsBeforeMove);
            int beforeTerrain = TerrainChangeCount();
            int beforeSunk = SunkCount();
            int beforeCollected = model.collected.Count;
            bool beforeLatched = model.latched;
            bool beforeGates = model.GatesOpen();
            bool beforeHeavyGates = model.HeavyGatesOpen();
            bool beforeLocks = model.LocksOpen();

            if (!model.TryMovePlayer(dir))
            {
                views[model.player].BumpTo(new Vector3(dir.x, dir.y, 0f)); // spring into the wall
                Sfx.Blocked();
                return;
            }

            // Only a successful PLAYER move starts the gameplay clock.
            countdownArmed = true;

            SyncViews(false);

            // Squash every entity that actually moved, in the move direction.
            var moveDir = new Vector2(dir.x, dir.y);
            bool pushedSomething = false;
            foreach (var e in model.entities)
            {
                if (!moveStartPositions.TryGetValue(e, out var bp) || bp.room != e.roomId || bp.pos != e.pos)
                {
                    views[e].Squash(moveDir);
                    if (e.IsCrate) pushedSomething = true;
                }
            }
            // Exactly one blink after every second successful logical player move. Because this is
            // below TryMovePlayer's failure return, pushing into a wall never advances the cadence.
            if (controlledPlayerBlinker != null)
                controlledPlayerBlinker.BlinkOnSuccessfulMove(model.MoveCount);
            if (pushedSomething) Sfx.Push(); else Sfx.Move();

            // Sound the consequence, not every internal rule that participated in it. Priority
            // keeps one move from becoming a pile-up while still distinguishing the important
            // situations: destruction, collection, machinery, and recursive/long-distance travel.
            bool impactEvent = TerrainChangeCount() > beforeTerrain || SunkCount() > beforeSunk;
            bool collectedEvent = model.collected.Count > beforeCollected;
            bool mechanicEvent = model.latched != beforeLatched
                                 || model.GatesOpen() != beforeGates
                                 || model.HeavyGatesOpen() != beforeHeavyGates
                                 || model.LocksOpen() != beforeLocks;
            var playerBefore = moveStartPositions[model.player];
            bool travelled = playerBefore.room != model.player.roomId
                             || Mathf.Abs(playerBefore.pos.x - model.player.pos.x)
                                + Mathf.Abs(playerBefore.pos.y - model.player.pos.y) > 1;

            if (impactEvent) Sfx.Impact();
            else if (collectedEvent) Sfx.Ding();
            else if (mechanicEvent) Sfx.Mechanic();
            if (travelled) Sfx.RoomShift();

            // Celebrate every target that just became satisfied.
            CaptureSatisfiedGoals(goalsAfterMove);
            foreach (var kv in goalsAfterMove)
            {
                if (goalsBeforeMove.ContainsKey(kv.Key)) continue;
                var parent = roomRoots[kv.Key.room];
                var localPos = Cell(model.rooms[kv.Key.room], new Vector2Int(kv.Key.x, kv.Key.y));
                Color col = kv.Value ? playerColor : boxColor;
                Fx.Burst(parent, localPos, new[] { col, Lighten(col, 0.35f) }, 12, 2.7f, 0.14f, 4.2f, 0.55f, OrderGoal + 60);
                Fx.Ripple(parent, localPos, col, OrderGoal + 59);
                Sfx.Ding();
            }

            UpdateHud();
            if (model.IsWon()) { Win(); return; }
            if (timeLeft <= 0f) { TimeUp(); return; }
            // solving ON the last move must still count as a win — hence the early return above
            if (MovesLeft <= 0) OutOfMoves();
        }

        void CapturePositions(Dictionary<PEntity, (int room, Vector2Int pos)> target)
        {
            target.Clear();
            foreach (var e in model.entities) target[e] = (e.roomId, e.pos);
        }

        int TerrainChangeCount()
        {
            int total = 0;
            foreach (var room in model.rooms.Values)
                total += room.filled.Count + room.broken.Count + room.smashed.Count;
            return total;
        }

        int SunkCount()
        {
            int total = 0;
            foreach (var e in model.entities) if (e.sunk) total++;
            return total;
        }

        // Every currently-satisfied target, keyed by cell; value = true if it's a player goal.
        void CaptureSatisfiedGoals(Dictionary<(int room, int x, int y), bool> target)
        {
            target.Clear();
            foreach (var room in model.rooms.Values)
            {
                foreach (var g in room.boxGoals)
                {
                    var e = model.EntityAt(room.id, g);
                    if (e != null && !e.isPlayer) target[(room.id, g.x, g.y)] = false;
                }
                foreach (var g in room.playerGoals)
                {
                    var e = model.EntityAt(room.id, g);
                    if (e != null && e.isPlayer) target[(room.id, g.x, g.y)] = true;
                }
            }
        }

        // -------------------------------------------------- view construction
        // Delegates to the shared BoardRenderer so the main menu renders the IDENTICAL board.
        Transform boardRoot;   // the rendered board — kept so the lose sequence can tear it apart
        Blinker controlledPlayerBlinker;

        void BuildView()
        {
            // BoardRenderer applies the approved cabinet palette to this concrete asset set.
            // Keep the resulting colours as the gameplay source of truth too, so goal bursts,
            // focus effects and discovery presentation cannot drift back to serialized themes.
            BoardAssets renderedAssets = BuildAssets();
            boardRoot = BoardRenderer.Render(model, renderedAssets, roomRoots, views, tiles);
            roomColors = renderedAssets.roomColors;
            boxColor = renderedAssets.boxColor;
            playerColor = renderedAssets.playerColor;
            wallColor = renderedAssets.wallColor;
            gridColor = renderedAssets.gridColor;
            frameColor = renderedAssets.frameColor;
            gutterColor = renderedAssets.gutterColor;
            controlledPlayerBlinker = views.TryGetValue(model.player, out var playerView)
                && playerView != null
                ? playerView.GetComponent<Blinker>()
                : null;
            focusRenderers = boardRoot != null
                ? boardRoot.GetComponentsInChildren<Renderer>(true)
                : null;
            focusedRoomId = int.MinValue;
            hiddenDiscovery = GetComponent<HiddenDiscoveryFx>();
            if (hiddenDiscovery == null) hiddenDiscovery = gameObject.AddComponent<HiddenDiscoveryFx>();
            hiddenDiscovery.Configure(levelIndex, model, roomRoots, views, roomColors, cameraFollow);
        }

        BoardAssets BuildAssets() => new BoardAssets
        {
            floorPrefab = floorPrefab, gridPrefab = gridPrefab, wallPrefab = wallPrefab,
            boxGoalPrefab = boxGoalPrefab, playerGoalPrefab = playerGoalPrefab,
            boxPrefab = boxPrefab, metaBoxPrefab = metaBoxPrefab, playerPrefab = playerPrefab,
            ringSprite = ringSprite, glowSprite = glowSprite, vignetteSprite = vignetteSprite, cellSprite = cellSprite,
            roomColors = roomColors, boxColor = boxColor, playerColor = playerColor,
            wallColor = wallColor, gridColor = gridColor, frameColor = frameColor, gutterColor = gutterColor,
            floorVignette = floorVignette, pieceGlow = pieceGlow, cellLift = cellLift,
            chapter = levelIndex / 10,
            floorTex = floorTex, floorTexTint = floorTexTint,
        };

        void PaintRoom(Transform root, PRoom room)
        {
            Color floorC = RoomColor(room.id);
            const float rim = 0.09f;   // frame rim: the frame base sticks out this far behind the floor

            // Frame base — a rounded rect in the border colour, slightly LARGER than the floor and drawn
            // BEHIND it so it reads as a clean rim. Solid fill (never a hollow ring), so no edge artifacts.
            var frameBase = Instantiate(floorPrefab, root);
            frameBase.transform.localPosition = Vector3.zero;
            var brsr = frameBase.GetComponent<SpriteRenderer>();
            brsr.drawMode = SpriteDrawMode.Sliced;
            brsr.size = new Vector2(room.width + rim * 2f, room.height + rim * 2f);
            brsr.color = frameColor;   // cyan neon frame, like the title's O board
            brsr.sortingOrder = OrderFloorBase - 1;

            // Flat, continuous floor — one solid rounded rectangle at full room size.
            var floor = Instantiate(floorPrefab, root);
            floor.transform.localPosition = Vector3.zero;
            var fsr = floor.GetComponent<SpriteRenderer>();
            fsr.drawMode = SpriteDrawMode.Sliced;
            fsr.size = new Vector2(room.width, room.height);
            fsr.color = floorC;
            fsr.sortingOrder = OrderFloorBase;

            // per-tier floor surface pattern (tiled over the floor)
            if (floorTex != null && floorTexTint.a > 0f)
            {
                var texGO = new GameObject("FloorTex");
                texGO.transform.SetParent(root, false);
                var tsr = texGO.AddComponent<SpriteRenderer>();
                tsr.sprite = floorTex;
                tsr.drawMode = SpriteDrawMode.Tiled;
                tsr.size = new Vector2(room.width, room.height);
                tsr.color = floorTexTint;
                tsr.sortingOrder = OrderFloorBase;
            }

            // Hairline grid at FULL (integer) room size, so the lines land exactly on the cell
            // boundaries and every piece sits centered in its cell. The floor corners are barely
            // rounded (below), so the grid doesn't visibly overshoot them.
            var grid = Instantiate(gridPrefab, root);
            grid.transform.localPosition = Vector3.zero;
            var gsr = grid.GetComponent<SpriteRenderer>();
            gsr.drawMode = SpriteDrawMode.Tiled;
            gsr.size = new Vector2(room.width, room.height);
            gsr.color = gridColor;
            gsr.sortingOrder = OrderFloorCell;

            // Soft inner-shadow vignette over the floor for premium depth (per tier).
            if (vignetteSprite != null && floorVignette > 0f)
            {
                var vg = new GameObject("FloorVignette");
                vg.transform.SetParent(root, false);
                vg.transform.localScale = new Vector3(room.width, room.height, 1f);
                var vsr = vg.AddComponent<SpriteRenderer>();
                vsr.sprite = vignetteSprite;
                vsr.color = new Color(0f, 0f, 0f, floorVignette);
                vsr.sortingOrder = OrderFloorCell;
            }

            // Solid wall blocks.
            for (int x = 0; x < room.width; x++)
                for (int y = 0; y < room.height; y++)
                    if (room.wall[x, y])
                    {
                        var w = Instantiate(wallPrefab, root);
                        w.transform.localPosition = Cell(room, new Vector2Int(x, y));
                        SetOrder(w, OrderWall);
                        foreach (var sr in w.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = wallColor;
                        AddRim(w, "Sprite", wallColor, 0.95f); // beveled edge so walls read on dark tiers
                    }

            foreach (var g in room.boxGoals)
            {
                var go = Instantiate(boxGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = boxColor;
            }
            foreach (var g in room.playerGoals)
            {
                var go = Instantiate(playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = playerColor;
            }
        }

        void SyncViews(bool instant)
        {
            foreach (var e in model.entities)
            {
                var v = views[e];
                // A sunk rock slides onto the pit cell (e.pos was set to the trench) and shrinks away.
                // Undo un-sinks it (model restores the flag) and the else branch below brings it back.
                if (e.sunk)
                {
                    if (v.gameObject.activeSelf)
                    {
                        var sinkRoom = model.rooms[e.roomId];
                        v.SetTarget(roomRoots[e.roomId], Cell(sinkRoom, e.pos), instant);  // slide onto the pit
                        if (instant) v.gameObject.SetActive(false);   // rebuild/seamless: no animation
                        else v.Sink();                                 // animate the sink
                    }
                    continue;
                }
                if (!v.gameObject.activeSelf) v.gameObject.SetActive(true);
                v.Unsink();   // restore a rock that undo brought back (cheap no-op otherwise)
                var room = model.rooms[e.roomId];
                v.SetTarget(roomRoots[e.roomId], Cell(room, e.pos), instant);
            }

            // Terrain that changes as you play. All of it is read straight off the model, so undo
            // needs no special handling: restoring model state restores the board for free.

            // A filled trench is floor now — hide its pit (the bright cell beneath shows through).
            foreach (var kv in tiles.pits)
            {
                if (kv.Value == null) continue;
                bool filled = model.rooms.TryGetValue(kv.Key.Item1, out var rm) && rm.filled.Contains(kv.Key.Item2);
                Show(kv.Value, !filled);
            }

            // Collapsed coral: the cracked slab goes, the hole it left appears.
            foreach (var kv in tiles.coral)
            {
                bool gone = model.rooms.TryGetValue(kv.Key.Item1, out var rm) && rm.IsBroken(kv.Key.Item2);
                Show(kv.Value, !gone);
                if (tiles.rubble.TryGetValue(kv.Key, out var hole)) Show(hole, gone);
            }

            // Gates: the slab is only there while nothing is holding its switch down.
            bool open = model.GatesOpen(), heavyOpen = model.HeavyGatesOpen();
            foreach (var kv in tiles.gates) Show(kv.Value, !open);
            foreach (var kv in tiles.heavyGates) Show(kv.Value, !heavyOpen);

            // A shattered rock is open floor; a thrown switch stays lit and holds its latches open;
            // a pulsing gate blinks with the move count. All read straight off the model.
            foreach (var kv in tiles.rocks)
            {
                bool gone = model.rooms.TryGetValue(kv.Key.Item1, out var rr) && rr.smashed.Contains(kv.Key.Item2);
                Show(kv.Value, !gone);
            }
            foreach (var kv in tiles.toggleOn) Show(kv.Value, model.latched);
            foreach (var kv in tiles.latches) Show(kv.Value, !model.latched);
            foreach (var kv in tiles.pulses) Show(kv.Value, model.beat == 0);

            // Pearls vanish as they are collected; the last one springs every lock at once.
            foreach (var kv in tiles.pearls) Show(kv.Value, !model.collected.Contains(kv.Key));
            bool unlocked = model.LocksOpen();
            foreach (var kv in tiles.locks) Show(kv.Value, !unlocked);

            var playerRoom = model.rooms[model.player.roomId];
            if (hiddenDiscovery != null) hiddenDiscovery.Refresh();
            FocusRoom(playerRoom.id);
            // The active nested room lives inside a larger coloured meta-box shell. Frame both,
            // not just the miniature room geometry, so the shell remains completely visible like
            // a playable room-container instead of becoming cropped cyan bands at screen edges.
            float nestedFraming = playerRoom.id == 0 ? 1f : 1.45f;
            cameraFollow.SetTargetRoom(roomRoots[playerRoom.id], playerRoom.width,
                playerRoom.height, instant, nestedFraming);
        }

        // Keep the complete recursive board rendered during room transitions. Only simplify the
        // miniature previews: their perimeter walls and opaque box backing are visual duplicates.
        // The camera may zoom toward the active room, but the enclosing board must never pop away.
        void FocusRoom(int roomId)
        {
            if (boardRoot == null || !roomRoots.TryGetValue(roomId, out var activeRoot) || activeRoot == null)
                return;

            if (focusedRoomId == roomId && focusedCinematic == cinematic) return;
            focusedRoomId = roomId;
            focusedCinematic = cinematic;

            roomRoots.TryGetValue(0, out var mainRoot);

            // A nested room becomes the current playable board. Isolate it from the huge outer
            // board during the zoom, but retain its immediate Frame + Backing so the player always
            // sees the coloured box they entered. This is the visual grammar of a room inside a
            // room; ancestor boards otherwise become giant cyan strips around the close-up.
            bool nestedCloseUp = roomId != 0;
            Transform immediateBox = nestedCloseUp ? activeRoot.parent : null;
            if (focusRenderers == null)
                focusRenderers = boardRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var sr in focusRenderers)
            {
                if (sr == null) continue;
                bool visible = !nestedCloseUp
                    || sr.transform.IsChildOf(activeRoot)
                    || IsImmediateRecursiveBoxShell(sr.transform, immediateBox);

                // A nested room's perimeter is already enforced by the model. Its wall sprites
                // collapse into four oversized blocks when that room is enlarged for play, so keep
                // only the main board's perimeter visible. Internal wall sprites are unaffected.
                Transform owner = OwningRoomRoot(sr.transform);
                if (visible && owner != null && owner != mainRoot && IsBoundaryWall(sr.transform, owner))
                    visible = false;
                // The enclosing room shell stays visible during entry, so the player can always
                // understand that they are playing inside a coloured box. Only old decorative
                // chrome is removed; the backing and frame now form the active room's bezel.
                if (visible && IsEnclosingRecursiveBoxChrome(sr.transform, activeRoot))
                    visible = false;
                if (visible && IsPlayerContainerLegacyChrome(sr.transform))
                    visible = false;

                if (sr.enabled != visible) sr.enabled = visible;
            }
        }

        Transform OwningRoomRoot(Transform child)
        {
            for (var t = child; t != null && t != boardRoot; t = t.parent)
                if (t.name.StartsWith("Room_")) return t;
            return null;
        }

        static bool IsBoundaryWall(Transform child, Transform roomRoot)
        {
            for (var t = child; t != null && t != roomRoot; t = t.parent)
                if (t.name == "BoundaryWall") return true;
            return false;
        }

        static bool IsImmediateRecursiveBoxShell(Transform child, Transform immediateBox)
        {
            if (child == null || immediateBox == null) return false;
            Transform owner = RecursiveBoxOwner(child);
            return owner == immediateBox && (child.name == "Frame" || child.name == "Backing"
                || child.name == "NestedShellHighlightTop"
                || child.name == "NestedShellHighlightLeft"
                || child.name.StartsWith("NestedDoorwayFloor_")
                || child.name.StartsWith("NestedDoorwayMask_"));
        }

        // The special player-container owns a purpose-built portal skin. Its inherited meta-box
        // frame and drop shadow form the dark lid, side rails and base that made it look like a
        // trash can, so keep only those two legacy renderers hidden in normal room previews.
        static bool IsPlayerContainerLegacyChrome(Transform child)
        {
            if (child == null || (child.name != "Frame" && child.name != "Shadow")) return false;
            var box = RecursiveBoxOwner(child);
            return box != null && box.Find("PlayerContainerSkin") != null;
        }

        static bool IsEnclosingRecursiveBoxChrome(Transform child, Transform activeRoot)
        {
            if (child == null || activeRoot == null) return false;
            var box = RecursiveBoxOwner(child);
            if (box == null || !activeRoot.IsChildOf(box)) return false;

            // Preserve Frame + Backing as the coloured active-room bezel. Shadows, old anchor bars
            // and the legacy player-container decoration are exterior details that should not be
            // magnified over the playable interior.
            var skin = box.Find("PlayerContainerSkin");
            return child.name == "Shadow" || child.name == "Bar"
                || (skin != null && child.IsChildOf(skin));
        }

        static Transform RecursiveBoxOwner(Transform child)
        {
            for (var t = child != null ? child.parent : null; t != null; t = t.parent)
                if (t.Find("Frame") != null && t.Find("Backing") != null)
                    return t;
            return null;
        }

        // SetActive is not free when it churns a subtree, so only touch it on an actual change.
        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        Color RoomColor(int roomId)
        {
            if (roomColors == null || roomColors.Length == 0) return Color.gray;
            return roomColors[roomId % roomColors.Length];
        }

        static void SetOrder(GameObject go, int order)
        {
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                sr.sortingOrder = order;
        }

        static Vector3 Cell(PRoom room, Vector2Int p)
            => new Vector3(p.x - (room.width - 1) * 0.5f, p.y - (room.height - 1) * 0.5f, 0f);

        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);

        // Swaps the level palette to the current difficulty tier (gameplay unaffected).
        void ApplyLevelTheme(int tier)
        {
            if (levelThemes == null || levelThemes.Length == 0) return;
            var th = levelThemes[Mathf.Clamp(tier, 0, levelThemes.Length - 1)];
            if (th.roomColors != null && th.roomColors.Length > 0) roomColors = th.roomColors;
            boxColor = th.box;
            playerColor = th.player;
            wallColor = th.wall;
            gridColor = th.grid;
            if (th.frame.a > 0f) frameColor = th.frame;
            if (th.gutter.a > 0f) gutterColor = th.gutter;
            floorVignette = th.floorVignette;
            pieceGlow = th.pieceGlow;
            if (th.cellLift > 0.001f) cellLift = th.cellLift;
            floorTex = th.floorTex;
            floorTexTint = th.floorTexTint;
        }

        void ConfigureChapterPresentation(int chapter)
        {
            if (cameraFollow == null) return;

            CameraBackdrop chapterBackdrop = cameraFollow.GetComponentInChildren<CameraBackdrop>(true);
            if (chapterBackdrop != null) chapterBackdrop.Apply(chapter);

            AmbientParticles ambient = cameraFollow.GetComponentInChildren<AmbientParticles>(true);
            if (ambient != null)
            {
                // Pass the selected level explicitly: editor previews do not overwrite the player's
                // saved campaign level, and their atmosphere must still match the board on screen.
                ambient.SetChapter(chapter);
                ambient.gameObject.SetActive(true);
            }
        }

        static void ConfigureChapterArrival(CameraFollow follow, int chapter)
        {
            // The same clean zoom language is retained, but its pace/depth grows with the campaign:
            // calm foundation, deeper systems, then a deliberate final-chapter approach.
            switch (Mathf.Clamp(chapter, 0, 4))
            {
                case 0: follow.introDur = 0.88f; follow.introZoomOut = 3.6f; break;
                case 1: follow.introDur = 1.02f; follow.introZoomOut = 4.2f; break;
                case 2: follow.introDur = 1.16f; follow.introZoomOut = 5.0f; break;
                case 3: follow.introDur = 1.30f; follow.introZoomOut = 5.8f; break;
                default: follow.introDur = 1.46f; follow.introZoomOut = 6.7f; break;
            }
        }

        // A lightened rim outline around a piece's fill sprite — a premium, layered look.
        void AddRim(GameObject piece, string fillChild, Color fillColor, float scale)
        {
            if (ringSprite == null) return;
            var target = piece.transform.Find(fillChild);
            if (target == null) return;
            var tsr = target.GetComponent<SpriteRenderer>();
            if (tsr == null) return;
            var rim = new GameObject("Rim");
            rim.transform.SetParent(piece.transform, false);
            rim.transform.localScale = Vector3.one * scale;
            var rsr = rim.AddComponent<SpriteRenderer>();
            rsr.sprite = ringSprite;
            rsr.color = Lighten(fillColor, 0.45f);
            rsr.sortingOrder = tsr.sortingOrder + 1;
        }

        // A soft neon glow halo behind a piece (premium depth); intensity is per-tier.
        void AddGlow(GameObject piece, Color color, int order, float scale)
        {
            if (glowSprite == null || pieceGlow <= 0f) return;
            var g = new GameObject("Glow");
            g.transform.SetParent(piece.transform, false);
            g.transform.localScale = Vector3.one * scale;
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = glowSprite;
            sr.color = new Color(color.r, color.g, color.b, pieceGlow);
            sr.sortingOrder = order;
        }

        // -------------------------------------------------- HUD / flow
        void UpdateHud()
        {
            // Just the number. The level's name ("— Company", "— Rabbit Hole") added nothing the
            // player could act on and put a different word on screen every level.
            levelLabel.text = $"Level {levelIndex + 1}";

            // Moves REMAINING, not moves spent — the limit is the thing the player has to plan
            // against, so it's what the HUD counts down. It reddens as it runs out, matching the timer.
            int left = MovesLeft;
            string best = PlayerPrefs.HasKey(BestKey(levelIndex))
                ? $"     BEST {PlayerPrefs.GetInt(BestKey(levelIndex))}" : "";
            movesLabel.text = $"MOVES  {left}{best}";
            movesLabel.color = left <= 3 ? TimerWarn : Color.white;
            UpdateScoreHud();
        }

        #if UNITY_EDITOR
        void BuildScoreHud()
        {
            if (scoreRoot != null) return;
            Transform parent = timerRoot != null ? timerRoot.parent
                : movesLabel != null ? movesLabel.transform.parent : transform;

            Transform existing = parent != null ? parent.Find("ScorePanel") : null;
            if (existing != null)
            {
                scoreRoot = existing as RectTransform;
                scoreLabel = existing.GetComponentInChildren<Text>(true);
                if (scoreRoot != null && scoreLabel != null) return;
                Object.DestroyImmediate(existing.gameObject);
            }

            var root = new GameObject("ScorePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            scoreRoot = (RectTransform)root.transform;
            scoreRoot.SetParent(parent, false);
            scoreRoot.anchorMin = scoreRoot.anchorMax = new Vector2(0f, 1f);
            scoreRoot.pivot = new Vector2(0f, 1f);
            scoreRoot.anchoredPosition = new Vector2(34f, -34f);
            scoreRoot.sizeDelta = new Vector2(220f, 82f);

            var panel = root.GetComponent<Image>();
            Color backing = Color.Lerp(gutterColor, Color.black, 0.34f);
            backing.a = 0.94f;
            panel.color = backing;
            panel.raycastTarget = false;
            Button sourceButton = retryButton != null ? retryButton : menuButton;
            if (sourceButton != null && sourceButton.image != null && sourceButton.image.sprite != null)
            {
                panel.sprite = sourceButton.image.sprite;
                panel.type = Image.Type.Sliced;
            }

            var shadow = root.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.02f, 0.05f, 0.65f);
            shadow.effectDistance = new Vector2(0f, -5f);
            var outline = root.AddComponent<Outline>();
            Color edge = timerAccentCur; edge.a = 0.75f;
            outline.effectColor = edge;
            outline.effectDistance = new Vector2(2f, -2f);

            var labelObject = new GameObject("ScoreText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(scoreRoot, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 7f);
            labelRect.offsetMax = new Vector2(-12f, -7f);

            scoreLabel = labelObject.GetComponent<Text>();
            scoreLabel.font = movesLabel != null && movesLabel.font != null
                ? movesLabel.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            scoreLabel.fontSize = 28;
            scoreLabel.fontStyle = FontStyle.Bold;
            scoreLabel.alignment = TextAnchor.MiddleCenter;
            scoreLabel.color = Color.Lerp(timerAccentCur, Color.white, 0.36f);
            scoreLabel.raycastTarget = false;
            scoreLabel.supportRichText = true;
            scoreLabel.lineSpacing = 0.82f;
            CrispUiTypography.Polish(scoreLabel);
            scoreRoot.SetAsLastSibling();
        }
        #endif

        void UpdateScoreHud()
        {
            if (scoreLabel == null) return;
            int count = levelPrefabs != null ? levelPrefabs.Length : 0;
            int total = ScoreSystem.Total(count);
            string gain = won && lastScoreGain > 0
                ? $"\n<size=12>+{lastScoreGain:n0} THIS LEVEL</size>" : string.Empty;
            scoreLabel.text = $"<size=14>TOTAL SCORE</size>\n{total:n0}{gain}";
            if (scoreRoot != null) scoreRoot.sizeDelta = new Vector2(220f, gain.Length > 0 ? 94f : 82f);
        }

        void Win()
        {
            won = true;

            int moves = model.MoveCount;
            int prevBest = PlayerPrefs.GetInt(BestKey(levelIndex), int.MaxValue);
            bool newBest = moves < prevBest;
            int best = Mathf.Min(moves, prevBest);
            PlayerPrefs.SetInt(BestKey(levelIndex), best);
            PlayerPrefs.SetInt(SessionClearKey(levelIndex), 1);

            int runScore = ScoreSystem.Calculate(levelIndex, par, moves, moveLimit, timeLeft);
            ScoreSystem.Award scoreAward = ScoreSystem.RecordBest(
                levelIndex, runScore, levelPrefabs != null ? levelPrefabs.Length : 0);
            lastScoreGain = scoreAward.gained;
            PlayerPrefs.Save();
            UpdateScoreHud();
            ReportLevelEndOnce();

            bool last = levelIndex >= levelPrefabs.Length - 1;
            bool allDone = AllLevelsBeaten();

            winTitle.text = (last && allDone) ? "You Win!" : "Level Complete!";

            if (winStats != null)
            {
                winStats.text = newBest
                    ? $"Solved in {moves} moves — new best!"
                    : $"Solved in {moves} moves   (best {best})";
                winStats.text += scoreAward.gained > 0
                    ? $"\nScore +{scoreAward.gained:n0}     Total {scoreAward.total:n0}"
                    : $"\nLevel score {scoreAward.levelBest:n0}     Total {scoreAward.total:n0}";
                if (last && allDone)
                    winStats.text += "\nAll levels complete — thanks for playing!";
            }

            Text nextLabel = nextButton != null
                ? nextButton.GetComponentInChildren<Text>(true) : null;
            if (nextLabel != null)
                nextLabel.text = last ? "PLAY AGAIN" : "NEXT LEVEL";

            // Finishing the whole game is not the same event as finishing a level, so it does not
            // get the same panel. The finale takes the screen over entirely; the ordinary win UI
            // never appears.
            if (last && allDone)
            {
                finaleSequenceActive = true;
                StartCoroutine(FinaleSequence());
                return;
            }

            StartCoroutine(WinSequence());
        }

        // The completion, staged. The old version fired the burst on the same frame as the last
        // move and dropped a panel 0.55s later; the win was over before you registered it.
        //
        //   beat 1  a held breath — nothing moves, so the burst lands on a still screen
        //   beat 2  the burst + camera punch, and an energy pulse travelling through the board
        //   beat 3  you sit with the solved board while the pulse crosses it
        //   beat 4  hand off to the map, where the progression actually plays out
        System.Collections.IEnumerator WinSequence()
        {
            Sfx.Win();

            // ---- 1. freeze ---------------------------------------------------------------
            float t = 0f;
            while (t < 0.30f) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 2. the board reacts -----------------------------------------------------
            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            if (cam != null)
            {
                // anchored to the BOARD, not the camera: the camera is offset by the HUD reservation,
                // so celebrating at it put the shockwave somewhere the board isn't
                Fx.CelebrateAt(cam, WinPalette, BoardCentre());
                cam.orthographicSize *= 0.90f;      // punch-in; CameraFollow eases it back
            }
            // A normal win used to take BoardWinFx's defaults: one front at speed 9 with a 1.6-wide
            // crest, so each cell was lit for 1.6/9 = 0.18s — a flicker, gone before you saw it.
            // Slower and wider more than doubles that to 0.31s, two fronts mean the board rings
            // after the hit instead of blinking once, and syncing the pieces makes everything you
            // solved answer on the same beat. Still short of the finale, which keeps 4 fronts and
            // the permanent colour change as its own signature.
            var wfx = BoardWinFx.Play(BoardCentre(), FloorCells(), SolvedPieces(),
                                      Lighten(frameColor, 0.5f), 2);
            if (wfx != null)
            {
                wfx.speed = 7f;        // was 9 — slow enough to WATCH the front cross
                wfx.width = 2.2f;      // was 1.6 — each cell lit 0.31s instead of 0.18s
                wfx.lift = 0.30f;      // was 0.22 — a real swell
                wfx.waveGap = 0.45f;
                wfx.syncPieces = true; // every solved piece answers together
            }

            // ---- 3. let the pulse cross the board before anything covers it ---------------
            t = 0f;
            while (t < 1.5f) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 4. out to the map -------------------------------------------------------
            // Campaign completion is handled only by FinaleSequence after AllLevelsBeaten has
            // succeeded. Reaching Level 50 directly must never manufacture a completed campaign.
            // No panel and no Next button: the reward is watching your route light up, so the
            // map IS the completion screen. It opens focused on what you just unlocked.
            PlayerPrefs.SetInt("Parabox.JustBeat", levelIndex);
            PlayerPrefs.SetInt(OpenLevelsKey, 1);
            PlayerPrefs.Save();
            // NO fade-out. This used to run screenFade.FadeOut, which drives the Game scene's
            // board-blue overlay to FULL opacity — so finishing a level washed the whole screen
            // solid blue before the map arrived. It was added to avoid a "hard cut" that nobody
            // had complained about, and it cost more than it bought.
            SceneManager.LoadScene("MainMenu");
        }

        // Finishing the game.
        //
        // This took five attempts, and four of them tuned the same dial: 180 particles, then near
        // silence, then four fronts, then nothing but camera. All four were arguments about how
        // LOUD the board should be — and every one of them left the same hole, which nobody spotted
        // because we kept staring at the board: the game had no ARTIFACT for finishing it. No
        // trophy, no 30/30, no screen. Effects evaporate the instant they end. That is what
        // "forgettable" means.
        //
        // So this is all of it, in order: the board answers and stays transformed, the camera
        // carries it out of the level and into the logo without a cut (the dive, reversed — the
        // best moment in the game and it has no particles in it), the map honours the journey,
        // and then the thing you can actually point at.
        System.Collections.IEnumerator Finale()
        {
            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            PlayerPrefs.SetInt("Parabox.Completed", 1);

            // ---- 1. stay with it ---------------------------------------------------------
            float t = 0f;
            while (t < 0.5f) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 2. the board answers, and keeps the change ------------------------------
            // Every solved object reacts on the same beat; one burst of light from the board's
            // centre; four fronts cross it. Cells stay converted behind each front, so you watch
            // the transformation SPREAD rather than flash — and the board ends a different colour
            // than the one you played on. It is carried into the logo still transformed.
            Sfx.Win();
            if (cam != null) Fx.CelebrateAt(cam, WinPalette, BoardCentre());

            var fx = BoardWinFx.Play(BoardCentre(), FloorCells(), SolvedPieces(),
                                     Lighten(frameColor, 0.55f), 4);
            if (fx != null)
            {
                fx.speed = 7f;
                fx.width = 2.2f;
                fx.lift = 0.30f;
                fx.waveGap = 0.5f;
                fx.syncPieces = true;
                fx.transformBoard = true;
                fx.transformTo = Lighten(frameColor, 0.35f);
                fx.transformAmount = 0.55f;
            }
            t = 0f;
            while (t < 2.4f) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 3. the HUD steps back ----------------------------------------------------
            // Everything that says "you are playing" leaves first, so the pull-out is the board
            // alone. The dive fades the menu's UI on the way in; this fades the game's on the way
            // out — same idea, same direction of travel.
            var hud = new System.Collections.Generic.List<CanvasGroup>();
            if (timerCanvas != null) hud.Add(timerCanvas);
            var fades = new System.Collections.Generic.List<Graphic>();
            if (levelLabel != null) fades.Add(levelLabel);
            if (movesLabel != null) fades.Add(movesLabel);
            var startCols = new System.Collections.Generic.List<Color>();
            foreach (var g in fades) startCols.Add(g.color);

            // ---- 4. one continuous move out ----------------------------------------------
            if (cameraFollow != null) cameraFollow.enabled = false;   // it lerps the zoom back otherwise
            Sfx.Ding();

            Vector3 fromPos = cam != null ? cam.transform.position : Vector3.zero;
            float fromSize = cam != null ? cam.orthographicSize : 6f;
            Vector3 root = BoardCentre();
            // pull back and re-centre on the board itself — the gameplay pose is offset for the HUD,
            // and the HUD is leaving, so the board should end up centred as it goes
            Vector3 toPos = new Vector3(root.x, root.y, fromPos.z);
            float toSize = fromSize * 2.4f;

            float dur = 2.6f;
            t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = Mathf.SmoothStep(0f, 1f, k);      // no jerk at either end — it is one gesture
                if (cam != null)
                {
                    cam.transform.position = Vector3.Lerp(fromPos, toPos, e);
                    cam.orthographicSize = Mathf.Lerp(fromSize, toSize, e);
                }
                float a = 1f - Mathf.Clamp01(k / 0.4f);     // HUD gone in the first 40%
                foreach (var g in hud) if (g != null) g.alpha = a;
                for (int i = 0; i < fades.Count; i++)
                    if (fades[i] != null) fades[i].color = new Color(startCols[i].r, startCols[i].g, startCols[i].b, startCols[i].a * a);
                yield return null;
            }

            // ---- 5. hand the board over, mid-move ----------------------------------------
            // The menu picks up EXACTLY here: same level, same board, same camera pose. Because
            // nothing is moving at the hand-off, the scene change is invisible — which is the
            // whole trick the dive uses, pointed the other way.
            if (cam != null)
            {
                PlayerPrefs.SetFloat("Parabox.OutX", cam.transform.position.x);
                PlayerPrefs.SetFloat("Parabox.OutY", cam.transform.position.y);
                PlayerPrefs.SetFloat("Parabox.OutSize", cam.orthographicSize);
            }
            PlayerPrefs.SetInt("Parabox.OutLevel", levelIndex);
            PlayerPrefs.SetInt("Parabox.SeamlessOut", 1);
            PlayerPrefs.SetInt("Parabox.Seamless", 1);      // the menu's ScreenFade must not flash
            PlayerPrefs.SetInt("Parabox.JustBeat", levelIndex);
            PlayerPrefs.SetInt("Parabox.FinalMoves", model.MoveCount);
            PlayerPrefs.SetInt(OpenLevelsKey, 1);
            PlayerPrefs.Save();
            SceneManager.LoadScene("MainMenu");   // no fade: the frames match
        }

        // The board's floor cells — the pulse travels through these.
        List<Transform> FloorCells()
        {
            var list = new List<Transform>();
            if (boardRoot != null)
                foreach (var tr in boardRoot.GetComponentsInChildren<Transform>(true))
                    if (tr != null && tr.name == "Cell") list.Add(tr);
            return list;
        }

        // Boxes standing on a goal — what the player actually achieved.
        List<Transform> SolvedPieces()
        {
            var list = new List<Transform>();
            if (model == null) return list;
            foreach (var room in model.rooms.Values)
                foreach (var g in room.boxGoals)
                {
                    var e = model.EntityAt(room.id, g);
                    if (e != null && !e.isPlayer && views.TryGetValue(e, out var v) && v != null)
                        list.Add(v.transform);
                }
            if (model.player != null && views.TryGetValue(model.player, out var pv) && pv != null)
                list.Add(pv.transform);
            return list;
        }

        static readonly Color[] WinPalette =
        {
            new Color(0.27f, 0.88f, 0.85f),   // cyan
            new Color(0.38f, 0.72f, 0.96f),   // blue
            new Color(0.66f, 0.48f, 0.94f),   // purple
            new Color(1f, 1f, 1f),            // white
            new Color(0.40f, 0.93f, 0.90f),   // bright cyan
        };

        System.Collections.IEnumerator ShowWinPanelAfter(float delay)
        {
            float w = 0f;
            while (w < delay) { w += Time.unscaledDeltaTime; yield return null; }
            if (winPanel != null) winPanel.SetActive(true);
        }

        // Every level's best, added up — what the run actually cost you. Levels with no record
        // are skipped rather than counted as zero, so a partial save cannot flatter you.
        int TotalMovesAcrossRun()
        {
            int total = 0;
            for (int i = 0; i < levelPrefabs.Length; i++)
                total += PlayerPrefs.GetInt(BestKey(i), 0);
            return total;
        }

        // The end of the game. Let the board's own win effects land first — the last solve should
        // still feel like a solve — then hand the screen to FinaleFx and never take it back.
        System.Collections.IEnumerator FinaleSequence()
        {
            Sfx.Win();
            if (winPanel != null) winPanel.SetActive(false);

            float e = 0f;                       // sit with the solved board for a beat
            while (e < 1.2f) { e += Time.unscaledDeltaTime; yield return null; }

            if (hudGroup != null)               // the HUD has no business being here
            {
                float f = 0f;
                while (f < 0.4f)
                {
                    f += Time.unscaledDeltaTime;
                    hudGroup.alpha = 1f - Mathf.Clamp01(f / 0.4f);
                    yield return null;
                }
                hudGroup.alpha = 0f;
                hudGroup.blocksRaycasts = false;
            }

            if (finaleOverlay == null || !finaleOverlay.IsFullyPrebuilt)
            {
                Debug.LogError("[Parabox] Finale is not prebuilt. Run Tools/Parabox/Generate Prebuilt UI (Run This).");
                yield break;
            }

            finaleOverlay.Play(
                onAgain: () =>
                {
                    ResetCampaignForReplay();
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                },
                onLevels: () =>
                {
                    ResetCampaignForReplay();
                    ReturnToLevels();
                },
                levels: levelPrefabs.Length,
                totalMoves: TotalMovesAcrossRun());
        }

        // A completed campaign becomes a completely fresh run when the player leaves the finale:
        // level 1 available, levels 2-50 locked, no previous best moves and a zero total score.
        void ResetCampaignForReplay()
        {
            for (int i = 0; i < levelPrefabs.Length; i++)
            {
                PlayerPrefs.DeleteKey(SessionClearKey(i));
                PlayerPrefs.DeleteKey(BestKey(i));
            }
            ScoreSystem.Reset(levelPrefabs.Length);
            PlayerPrefs.SetInt(LevelKey, 0);
            PlayerPrefs.DeleteKey("Parabox.JustBeat");
            PlayerPrefs.DeleteKey("Parabox.FinalRun");
            PlayerPrefs.Save();
            LuxoddGameService.SyncProgress();
        }

        bool AllLevelsBeaten()
        {
            for (int i = 0; i < levelPrefabs.Length; i++)
                if (!PlayerPrefs.HasKey(SessionClearKey(i))) return false;
            return true;
        }

        void NextLevel()
        {
            if (!won) return;
            int next = (levelIndex + 1) % levelPrefabs.Length;
            PlayerPrefs.SetInt(LevelKey, next);
            PlayerPrefs.Save();
            LuxoddGameService.SyncProgress();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // -------------------------------------------------- timer
        void TickTimer()
        {
            if (Ended || !countdownArmed) return;
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f)
                TimeUp();
        }

        // All timer visuals in one place: intro fade + scale-in, a per-second "tick" pop,
        // the depleting radial ring, and a smooth (non-flashing) warm-up + gentle pulse
        // over the final 5 seconds.
        void AnimateTimer()
        {
            timerIntro = Mathf.Min(timerIntro + Time.unscaledDeltaTime, IntroDur);
            float introT = Mathf.Clamp01(timerIntro / IntroDur);
            if (timerCanvas != null) timerCanvas.alpha = introT;
            float introScale = Mathf.Lerp(0.55f, 1f, EaseOutBack(introT));

            int secs = Mathf.CeilToInt(Mathf.Max(0f, timeLeft));
            if (secs != lastSecond)
            {
                if (lastSecond >= 0 && !Ended)
                {
                    tickBump = 1f; // pop on each new second
                    if (secs > 0 && secs <= 5) Sfx.TimerWarning();
                }
                lastSecond = secs;
                if (timerLabel != null) timerLabel.text = secs.ToString();
            }
            tickBump = Mathf.MoveTowards(tickBump, 0f, Time.unscaledDeltaTime * 4f);
            if (timerLabel != null) timerLabel.transform.localScale = Vector3.one * (1f + tickBump * 0.16f);

        // Turn the visible countdown red as soon as it reaches 5 seconds.
        bool danger = secs > 0 && secs <= 5 && !Ended;
        if (timerLabel != null)
            timerLabel.color = danger ? TimerWarn : Color.white;
        if (timerFill != null)
        {
            timerFill.fillAmount = timeLimit > 0f ? Mathf.Clamp01(timeLeft / timeLimit) : 0f;
            timerFill.color = danger ? TimerWarn : timerAccentCur;
        }

            float pulse = danger
                ? 1f + Mathf.Sin(Time.unscaledTime * 4.2f) * 0.05f    // slow, smooth — never a flash
                : 1f + Mathf.Sin(Time.unscaledTime * 2.0f) * 0.012f;  // barely-there breathing
            if (timedUp) pulse = 1f;
            if (timerRoot != null) timerRoot.localScale = Vector3.one * introScale * pulse;
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = 2.70158f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        void TimeUp()
        {
            if (Ended) return;
            timedUp = true;
            ShowLose("TIME'S UP", "The clock ran out on this one.");
        }

        void OutOfMoves()
        {
            if (Ended) return;
            outOfMoves = true;
            // leads with the counter at zero — the number that just killed you is the first thing to read
            ShowLose("OUT OF MOVES", $"0 / {moveLimit} MOVES LEFT");
        }

        // ============================================ chapter-start onboarding (cinematic)
        //
        // A dedicated mechanic cinematic, not an overlay on gameplay. The entire HUD is hidden and
        // a small prebuilt vignette demonstrates only the new rule. It never plays the current board
        // or reads its solution. "Try It Yourself" then reveals the untouched puzzle in-scene.
        public const string TutorialKey = "Parabox.Tutorial.Seen";
        // The versioned prefix invalidates older solution replays and abstract rule cards. Existing
        // players receive each spoiler-free gameplay mini-board once after a presentation upgrade.
        const string MechanicBriefingPrefix = TutorialVideoVersion.MechanicSeenPrefix;
        public static string MechanicBriefingKey(int levelIndex)
            => MechanicBriefingPrefix + Mathf.Clamp(levelIndex, 0, 49);
        Coroutine _cine;
        public int TutorialPlaybackSerial { get; private set; }
        GameObject _goalGlow;
        RenderTexture _rt;                      // the board renders into this; the panel shows it
        const int RTW = 1280, RTH = 720;        // 16:9 — matches the panel so the board isn't distorted
        float RTAspect => RTW / (float)RTH;

        List<MechanicCatalog.Id> CurrentTutorialMechanics()
            => MechanicCatalog.TutorialsAt(levelPrefabs, levelIndex);

        // Every first-time rule receives a tutorial, and Levels 1/11/21/31/41 always open with a
        // compact chapter lesson. The stored signature makes each mini-board play once while a
        // changed curriculum or video format can introduce itself again. No puzzle solution is
        // read by this path.
        bool WillTutorial()
        {
            if (tutorialFx == null || tutorialFx.mechanicDemo == null) return false;
            List<MechanicCatalog.Id> lessons = CurrentTutorialMechanics();
            if (lessons.Count == 0 || string.IsNullOrWhiteSpace(MechanicCatalog.Lesson(lessons)))
                return false;
            string signature = MechanicCatalog.Signature(lessons);
            return PlayerPrefs.GetString(MechanicBriefingKey(levelIndex), string.Empty) != signature;
        }

        void MaybeTutorial()
        {
            if (WillTutorial())
            {
                mechanicBriefingSignature = MechanicCatalog.Signature(CurrentTutorialMechanics());
                _cine = StartCoroutine(TutorialCinematic());
            }
        }

        // Keep mechanic briefings visually clean. Earlier versions placed large cyan focus rings
        // over the player, arrows and targets; at preview scale those overlays looked like duplicate
        // lighting and obscured the underlying board art. The caption and untouched board already
        // explain the mechanic, so there is no additional spotlight layer.
        void CreateMechanicSpotlights(IReadOnlyList<MechanicCatalog.Id> introductions)
        {
            ClearMechanicSpotlights();
        }

        void ClearMechanicSpotlights()
        {
            if (mechanicSpotlightRoot != null) Destroy(mechanicSpotlightRoot);
            mechanicSpotlightRoot = null;
        }

        // The master timeline. It renders only the prebuilt rule vignette. The current board stays
        // frozen behind the scrim and its solution is never read, replayed or even partially shown.
        System.Collections.IEnumerator TutorialCinematic()
        {
            TutorialPlaybackSerial++;
            cinematic = true;
            if (tutorialFx != null)
            {
                // Repeat can re-enter this timeline while the end-choice panel is still visible.
                // Reset every overlay synchronously before the first yielded frame so the replay
                // unmistakably starts as a video instead of appearing to ignore the button.
                tutorialFx.HideChoice();
                tutorialFx.HideCaptionImmediately();
                tutorialFx.HideMechanicDemoImmediately();
                tutorialFx.SetTitle("NEW MECHANIC");
                tutorialFx.HideMechanicBriefingImmediately();
                tutorialFx.ShowSkip();
            }
            if (tutorialFx == null || tutorialFx.mechanicDemo == null)
            {
                cinematic = false;
                if (tutorialFx != null) tutorialFx.HideSkip();
                Debug.LogError("[Parabox] The prebuilt mechanic tutorial is missing. "
                    + "Run Tools/Parabox/Generate Prebuilt UI (Run This).");
                yield break;
            }

            // Dedicate the display to the lesson while leaving the gameplay model untouched.
            if (hudReveal != null) hudReveal.StandDown();
            SetHud(0f, false);
            if (cameraFollow != null) cameraFollow.enabled = false;

            tutorialFx.CoverInstant();
            tutorialFx.SetVideo(null);
            tutorialFx.PanelIn(tutorialFromMainPlay ? 0.8f : 0.5f);
            yield return WaitU(tutorialFromMainPlay ? 0.9f : 0.6f);

            List<MechanicCatalog.Id> introductions = CurrentTutorialMechanics();
            if (introductions.Count == 0)
                introductions.Add(MechanicCatalog.Id.Navigation);

            for (int i = 0; i < introductions.Count; i++)
            {
                tutorialFx.SetTitle(introductions.Count > 1
                    ? $"NEW MECHANIC  {i + 1}/{introductions.Count}"
                    : "NEW MECHANIC");
                yield return tutorialFx.PlayMechanicDemo(introductions[i]);
                if (i + 1 < introductions.Count)
                {
                    tutorialFx.HideMechanicDemoImmediately();
                    yield return WaitU(0.18f);
                }
            }

            yield return tutorialFx.ShowChoice();
        }

        // Chosen: play the level for real. Hand the screen back to the camera (while the scrim is
        // still opaque, so the switch is invisible), reset the board, then dissolve the panel to
        // reveal the ready-to-play board — the panel closing IS the transition into gameplay.
        System.Collections.IEnumerator TutorialExitToPlay()
        {
            if (tutorialFx != null)
            {
                tutorialFx.HideChoice();
                tutorialFx.HideSkip();
            }
            ClearGoalGlow();
            ClearMechanicSpotlights();

            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            if (cam != null) { cam.targetTexture = null; cam.ResetAspect(); }   // draw to the SCREEN again
            if (tutorialBgCamera != null) tutorialBgCamera.enabled = false;      // the main camera has the display back
            if (!TutorialRewind())
            {
                RecoverFromTutorialRewindFailure();
                yield break;
            }

            Coroutine fade = (tutorialFx != null) ? tutorialFx.FadeOutAll() : null;   // card + scrim dissolve
            float dur = 0.5f, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                SetHud(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur)), false);     // HUD fades in with it
                yield return null;
            }
            SetHud(1f, true);
            if (cameraFollow != null) cameraFollow.enabled = true;   // resumes at the gameplay pose
            ReleaseRT();

            CompleteTutorialExit();
        }

        void CompleteTutorialExit()
        {
            timeLeft = timeLimit;                   // the demo cost no clock; the player starts fresh
            countdownArmed = false;
            cinematic = false;                      // gameplay is live from here
            tutorialExiting = false;
            _cine = null;
            FocusRoom(model.player.roomId);         // restore normal all-room visibility after close-up
            if (!string.IsNullOrEmpty(mechanicBriefingSignature))
            {
                // Record the exact mechanic set only after the video closes and the player enters
                // play. A changed curriculum therefore replays the new lesson without ever using
                // puzzle progress as tutorial state.
                PlayerPrefs.SetString(MechanicBriefingKey(levelIndex), mechanicBriefingSignature);
                mechanicBriefingSignature = null;
            }
            PlayerPrefs.SetInt(TutorialKey, 1);
            PlayerPrefs.Save();
            LuxoddGameService.SyncProgress();
        }

        public void TutorialWatchAgain()
        {
            tutorialExiting = false;
            if (_cine != null) StopCoroutine(_cine);
            if (tutorialFx != null) tutorialFx.HideChoice();
            if (!TutorialRewind())
            {
                RecoverFromTutorialRewindFailure();
                return;
            }
            ClearGoalGlow();
            ClearMechanicSpotlights();
            _cine = StartCoroutine(TutorialCinematic());
        }

        public void TutorialTryIt()
        {
            if (tutorialExiting) return;
            tutorialExiting = true;
            Sfx.Ding();
            StartCoroutine(TutorialExitToPlay());
        }

        // Available for the complete duration of every walkthrough. Purple on a Luxodd cabinet
        // and RB on a standard gamepad call here.
        public void TutorialSkip()
        {
            if (!Tutoring || tutorialExiting) return;

            if (_cine != null)
            {
                StopCoroutine(_cine);
                _cine = null;
            }

            Sfx.Ding();
            tutorialExiting = true;
            ClearMechanicSpotlights();
            StartCoroutine(TutorialExitToPlay());
        }

        // Put the board back exactly as it was, replaying backwards through the same undo the player's
        // Z key uses — so the board they play cannot differ from the one they watched.
        bool TutorialRewind()
        {
            if (model == null) return false;

            // Never use an open-ended "while moves remain" loop on Unity's main thread. If an
            // authored mechanic ever leaves an undo snapshot inconsistent, the old loop could keep
            // the opaque tutorial cover on screen forever and even prevent the Editor Stop button
            // from running. The count captured here is the absolute maximum amount of work allowed.
            int maximumUndos = model.MoveCount;
            for (int i = 0; i < maximumUndos && model.MoveCount > 0; i++)
            {
                int before = model.MoveCount;
                if (!model.Undo() || model.MoveCount >= before)
                {
                    Debug.LogError("[Parabox] Tutorial rewind did not make progress; reloading the level safely.");
                    return false;
                }
            }
            if (model.MoveCount != 0)
            {
                Debug.LogError("[Parabox] Tutorial rewind exceeded its safety bound; reloading the level safely.");
                return false;
            }
            if (controlledPlayerBlinker != null) controlledPlayerBlinker.ResetOpen();
            SyncViews(true);
            UpdateHud();
            return true;
        }

        void RecoverFromTutorialRewindFailure()
        {
            ClearGoalGlow();
            ClearMechanicSpotlights();
            ReleaseRT();
            if (tutorialFx != null) tutorialFx.RevealGameplayImmediately();
            if (cameraFollow != null) cameraFollow.enabled = true;
            SetHud(1f, true);
            cinematic = false;
            if (model != null && model.player != null) FocusRoom(model.player.roomId);
            tutorialExiting = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // ---- cinematic helpers -------------------------------------------------------------------

        // The root board room's world framing inputs (its own room on level 1).
        bool RootFraming(out Vector3 center, out float scale, out int w, out int h)
        {
            center = Vector3.zero; scale = 1f; w = 1; h = 1;
            if (model == null || model.player == null) return false;
            if (!model.rooms.TryGetValue(model.player.roomId, out var room)) return false;
            if (!roomRoots.TryGetValue(room.id, out var root) || root == null) return false;
            center = root.position; scale = root.lossyScale.x; w = room.width; h = room.height;
            return true;
        }

        // Keep a nested/recursive room readable after the demonstrated player enters it.
        bool TutorialRoomFraming(int roomId, float padding, out Vector3 position, out float size)
        {
            position = Vector3.zero;
            size = 1f;
            if (model == null || !model.rooms.TryGetValue(roomId, out var room)) return false;
            if (!roomRoots.TryGetValue(roomId, out var root) || root == null) return false;
            CameraFraming.Compute(root.position, root.lossyScale.x, room.width, room.height,
                RTAspect, padding, 0.12f, 0.12f, out position, out size);
            return true;
        }

        // The board's video texture for the panel. Created once, reused across "Watch Again",
        // released when the player leaves the tutorial for good.
        void EnsureRT()
        {
            if (_rt != null) return;
            _rt = new RenderTexture(RTW, RTH, 16, RenderTextureFormat.Default) { name = "TutorialRT" };
            _rt.antiAliasing = 1;   // the board is soft procedural art; MSAA-on-RT buys little and can be fussy on WebGL
            _rt.Create();
        }

        void ReleaseRT()
        {
            if (tutorialBgCamera != null) tutorialBgCamera.enabled = false;
            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            if (cam != null && cam.targetTexture == _rt) { cam.targetTexture = null; cam.ResetAspect(); }
            if (tutorialFx != null) tutorialFx.SetVideo(null);
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }

        // Leaving the scene mid-cinematic (Escape) unloads everything, but a RenderTexture is not
        // garbage-collected — release it explicitly so it can't leak across scene loads.
        void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); _rt = null; }
        }

        static System.Collections.IEnumerator WaitU(float dur)
        {
            float t = 0f;
            while (t < dur) { t += Time.unscaledDeltaTime; yield return null; }
        }

        void SetHud(float alpha, bool interactive)
        {
            if (hudGroup == null) return;
            hudGroup.alpha = alpha;
            hudGroup.interactable = interactive;
            hudGroup.blocksRaycasts = interactive;
        }

        // The goal the box has to reach — a soft glow breathes behind it. Order 3 sits above the wall
        // and below the goal marker / box / player, so the box still visibly lands ON the goal.
        Transform FirstGoalMarker()
        {
            if (boardRoot == null) return null;
            foreach (var tr in boardRoot.GetComponentsInChildren<Transform>(true))
                if (tr != null && tr.name.StartsWith("BoxGoal")) return tr;
            return null;
        }

        void SpawnGoalGlow()
        {
            ClearGoalGlow();
            var g = FirstGoalMarker();
            if (g == null || glowSprite == null) return;
            _goalGlow = new GameObject("TutorialGlow");
            _goalGlow.transform.SetParent(g, false);
            _goalGlow.transform.localScale = Vector3.one * 1.7f;
            var sr = _goalGlow.AddComponent<SpriteRenderer>();
            sr.sprite = glowSprite;
            sr.color = Lighten(boxColor, 0.45f);
            sr.sortingOrder = 3;
            StartCoroutine(BreatheGlow(sr));
        }

        void ClearGoalGlow()
        {
            if (_goalGlow != null) Destroy(_goalGlow);   // BreatheGlow's `while (sr != null)` ends with it
            _goalGlow = null;
        }

        System.Collections.IEnumerator BreatheGlow(SpriteRenderer sr)
        {
            float t = 0f;
            while (sr != null)
            {
                t += Time.unscaledDeltaTime;
                float k = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
                sr.transform.localScale = Vector3.one * (1.7f * Mathf.Lerp(0.92f, 1.10f, k));
                var col = sr.color; col.a = Mathf.Lerp(0.28f, 0.68f, k); sr.color = col;
                yield return null;
            }
        }

                // Everything the board is made of: floor cells and puzzle pieces. These become the shards
        // the blast throws. EntityView is disabled first — it drives its transform every frame and
        // would otherwise snap each piece straight back onto its grid cell mid-explosion.
        List<Transform> BoardShards()
        {
            var list = new List<Transform>();
            foreach (var v in views.Values)
            {
                if (v == null) continue;
                v.enabled = false;
                list.Add(v.transform);
            }
            if (boardRoot != null)
                foreach (var tr in boardRoot.GetComponentsInChildren<Transform>(true))
                    if (tr != null && tr.name == "Cell") list.Add(tr);
            return list;
        }

        // World position of the room the player is in — the blast's epicentre.
        Vector3 BoardCentre()
        {
            if (model != null && roomRoots.TryGetValue(model.player.roomId, out var r) && r != null)
                return r.position;
            return boardRoot != null ? boardRoot.position : Vector3.zero;
        }

        void ShowLose(string title, string sub)
        {
            // A loss must sound at the moment the terminal state is announced.  Keeping this in
            // LoseFx made the cue depend on that optional component/coroutine being present and
            // running; levels without it could fail silently.  This is now the single call site
            // for both TIME UP and OUT OF MOVES.
            Sfx.Death();
            PlayLoseBoardLight();

            if (loseFx != null)
            {
                // Keep the board/model intact. LoseFx reveals the leaderboard, holds it for 3.5
                // seconds, then asks this manager to launch Luxodd's official transaction.
                loseFx.Play(title, sub, BeginLossTransaction);
                return;
            }
            // Old-scene safety: even without LoseFx, never expose local post-loss choices.
            if (timeUpPanel != null) timeUpPanel.SetActive(true);
            StartCoroutine(BeginLossTransactionAfterDelay(3.5f));
        }

        System.Collections.IEnumerator BeginLossTransactionAfterDelay(float delay)
        {
            while (delay > 0f)
            {
                delay -= Time.unscaledDeltaTime;
                yield return null;
            }
            BeginLossTransaction();
        }

        void BeginLossTransaction()
        {
            if (!Lost || lossTransactionRequested) return;
            lossTransactionRequested = true;
            int totalScore = ScoreSystem.Total(levelPrefabs != null ? levelPrefabs.Length : 0);
            LuxoddGameService.RequestLossTransaction(levelIndex, totalScore, ContinueCurrentSession);
        }

        void PlayLoseBoardLight()
        {
            // The win and lose reactions now share the same travelling cell-light system. A loss
            // uses one quick coral-red wave; a win remains brighter and uses two waves.
            StartCoroutine(ShakeBoardOnLose());
            var fx = BoardWinFx.Play(BoardCentre(), FloorCells(), null,
                new Color(1f, 0.30f, 0.32f, 1f), 1);
            if (fx == null) return;
            fx.speed = 11f;
            fx.width = 2.4f;
            fx.lift = 0.20f;
            fx.waveGap = 0.4f;
            fx.syncPieces = false;
        }

        System.Collections.IEnumerator ShakeBoardOnLose()
        {
            if (boardRoot == null) yield break;

            Vector3 restPosition = boardRoot.localPosition;
            Quaternion restRotation = boardRoot.localRotation;
            const float duration = 0.32f;
            const float distance = 0.10f;
            const float angle = 0.65f;
            float t = 0f;

            while (t < duration && boardRoot != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float strength = (1f - k) * (1f - k);
                float x = Mathf.Sin(k * Mathf.PI * 9f) * distance * strength;
                float y = Mathf.Sin(k * Mathf.PI * 13f + 0.7f) * distance * 0.45f * strength;
                float z = Mathf.Sin(k * Mathf.PI * 7f) * angle * strength;
                boardRoot.localPosition = restPosition + new Vector3(x, y, 0f);
                boardRoot.localRotation = restRotation * Quaternion.Euler(0f, 0f, z);
                yield return null;
            }

            if (boardRoot != null)
            {
                boardRoot.localPosition = restPosition;
                boardRoot.localRotation = restRotation;
            }
        }

        void ReturnToLevels()
        {
            LuxoddGameService.AbandonLossTransaction();
            ReportLevelEndOnce();
            PlayerPrefs.SetInt(OpenLevelsKey, 1);
            PlayerPrefs.Save();
            SceneManager.LoadScene("MainMenu");
        }

        void Restart()
        {
            // Restart remains available during ordinary play. Once a loss is terminal, recovery
            // belongs exclusively to the delayed Luxodd transaction.
            if (Tutoring) return;
            if (Lost) return; // loss recovery belongs exclusively to the Luxodd transaction
            ReportLevelEndOnce();
            ReloadCurrentLevel();
        }

        // Luxodd Continue keeps this attempt alive. The model, positions, move count and undo
        // history are intentionally untouched; only the terminal flags and failure overlay are
        // cleared so Continue cannot behave like Restart.
        void ContinueCurrentSession()
        {
            // Refill complete resources from the death position. The model and its undo stack are
            // deliberately not parsed, reloaded, or rewound, so every body, crate, terrain state,
            // collected key and room transition remains exactly where the player left it.
            timeLimit = TimeLimitForLevel(levelIndex, par);
            timeLeft = timeLimit;
            countdownArmed = false;
            moveLimit = model.MoveCount + MoveLimitForLevel(levelIndex, par);
            timedUp = false;
            outOfMoves = false;
            lossTransactionRequested = false;
            lastHeld = Vector2Int.zero;
            nextRepeat = 0f;
            tickBump = 0f;
            lastSecond = -1;

            if (timeUpPanel != null) timeUpPanel.SetActive(false);
            if (loseFx != null) loseFx.DismissImmediate();
            UpdateHud();
            AnimateTimer();
            Sfx.Mechanic();
        }

        void ReloadCurrentLevel()
        {
            Sfx.Mechanic();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void ReportLevelEndOnce()
        {
            if (levelEndReported) return;
            levelEndReported = true;
            int totalScore = ScoreSystem.Total(levelPrefabs != null ? levelPrefabs.Length : 0);
            LuxoddGameService.ReportLevelEnd(levelIndex, totalScore);
        }

        // Menu always returns to the LEVEL BOARD, not the title screen — you came from a level,
        // so the useful place to land is the list you picked it from. Same flag ReturnToLevels uses.
        void GoToMenu() => ReturnToLevels();
    }
}
