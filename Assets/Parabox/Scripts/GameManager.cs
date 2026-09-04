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
        public Text winScoreValue;
        public Button nextButton;
        public Button menuButton;

        [Header("Prebuilt win auto-advance")]
        [Tooltip("Generated into Game.unity by Tools/Parabox/Generate Prebuilt UI (Run This).")]
        public RectTransform winCountdownRoot;
        public Text winCountdownLabel;
        public Image[] winCountdownBorder;

        [Header("Prebuilt score HUD")]
        [Tooltip("Generated into Game.unity by Tools/Parabox/Generate Prebuilt UI (Run This).")]
        public RectTransform scoreRoot;
        public Text scoreLabel;
        public Text levelPointsLabel;

        [Header("Premium live score HUD")]
        [Tooltip("Created by Tools/Parabox/Install Premium Score HUD Option 2.")]
        public Text premiumScoreValue;
        public Text premiumTimeValue;
        public Text premiumTimeDetail;
        public Text premiumMovesValue;
        public Text premiumMovesDetail;
        PremiumScoreRings premiumScoreRings;
        int lastScoreGain;
        int endOfRunTotalScore = -1;
        int undoCount;
        bool winControlsReady;
        ScoreSystem.Breakdown lastScoreBreakdown;
        ScoreSystem.Award lastScoreAward;
        int lastWinMoves;
        int lastWinMoveBest;
        bool lastWinWasMoveBest;
        Coroutine winAutoAdvanceRoutine;
        const float WinAutoAdvanceDuration = 5f;

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
        public FirstLifeLessonFx firstLifeLessonFx; // one free Level-1 retry that teaches recovery

        [Header("Prebuilt tutorial mini-games")]
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
        NestedBoxGuidanceFx nestedBoxGuidance;
        // FocusRoom used to traverse the complete recursive board after every move. Large late-game
        // boards contain hundreds of renderers, so that repeated hierarchy scan caused visible
        // stalls. The rendered hierarchy is immutable during a level; cache it once and skip the
        // visibility pass entirely while the active room/cinematic state has not changed.
        Renderer[] focusRenderers;
        int focusedRoomId = int.MinValue;
        bool focusedCinematic;
        int levelIndex;
        bool won;
        float timeLeft;
        float timeLimit;
        // Restart reloads this scene, so instance fields cannot carry the clock across it. Keep a
        // one-shot transfer in process memory: it survives a scene reload, but a genuinely new
        // Unity/Luxodd session still starts with the authored full allowance.
        static bool restartTimerPending;
        static int restartTimerLevel = -1;
        static float restartTimerRemaining;
        static float restartTimerCapturedAt;
        static bool restartTimerWasArmed;
        // A purchased Continue reloads the board, but remains in the same Luxodd level/session.
        static int continuedLevelReload = -1;
        // Level 1 grants one teaching retry per app/session. Static state survives the scene reload
        // used by that retry, while SubsystemRegistration resets it for a genuinely new launch.
        static bool firstLevelSecondChanceUsed;
        bool firstLifeLessonOpen;
        // Restarting a chapter opener must return straight to its puzzle instead of replaying the
        // tutorial the player has just watched. This is deliberately a one-reload suppression:
        // entering the level normally later still presents its tutorial.
        static bool restartTutorialSuppressionPending;
        static int restartTutorialSuppressionLevel = -1;
        bool suppressTutorialForThisLoad;
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
        int lastManualMoveFrame = -1;
        int lastTutorialMoveFrame = -1;
        bool suppressMirroredArcadeMove;
        GameObject mechanicSpotlightRoot;
        bool editorPreviewMode;
        [Header("Prebuilt finale")]
        public FinaleFx finaleOverlay;
        bool finaleSequenceActive;
        bool lossTransactionRequested;
        Canvas tutorialTimerCanvas;
        Vector2 gameplayTimerAnchoredPosition;
        bool gameplayTimerPositionCaptured;

        // Terminal means this attempt is over. Ended also includes the tutorial because player
        // input must stay locked while the demonstration is driving the screen; the countdown is
        // deliberately governed by Terminal instead so tutorial-video time can consume the clock.
        bool Terminal => won || timedUp || outOfMoves;
        bool Ended => Terminal || Tutoring;
        // The whole onboarding — cinematic AND the choice panel that follows it. While this is true
        // the board is not the player's. GameManager owns the flag (it owns the timeline), so there
        // is one source of truth rather than a reach into the UI component.
        bool Tutoring => cinematic;
        bool cinematic;
        // Lost, specifically — the win branch needs its own handling (Space = next level).
        bool Lost => timedUp || outOfMoves;
        int MovesLeft => Mathf.Max(0, moveLimit - model.MoveCount);

        // Every puzzle receives three recovery moves beyond its reviewed solution target. Level 14
        // receives seven after tester feedback identified a sudden difficulty spike; its displayed
        // target remains the truthful authored route while the player gets more room to recover.
        // Level 2 is reviewed as a 16-move solve (19 visible). Level 10 uses its authored
        // 19-move target plus the usual three recovery moves (22 visible), not the old 44-move budget.
        public static int MoveParForLevel(int levelIdx, int levelPar)
        {
            if (levelIdx == 1) return Mathf.Max(16, levelPar);
            return levelPar;
        }

        // Keep this shared method as the single source of truth for gameplay and campaign QA.
        public static int MoveLimitForLevel(int levelIdx, int levelPar)
        {
            int reviewedPar = MoveParForLevel(levelIdx, levelPar);
            int recoveryMoves = levelIdx == 13 ? 7 : 3;
            return reviewedPar > 0 ? reviewedPar + recoveryMoves : 999;
        }

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRestartTimerTransfer()
        {
            restartTimerPending = false;
            restartTimerLevel = -1;
            restartTimerRemaining = 0f;
            restartTimerCapturedAt = 0f;
            restartTimerWasArmed = false;
            continuedLevelReload = -1;
            restartTutorialSuppressionPending = false;
            restartTutorialSuppressionLevel = -1;
            TutorialsSeenThisPlaySession.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFirstLevelSecondChance()
        {
            firstLevelSecondChanceUsed = false;
        }

        void Awake()
        {
            EnsurePremiumScoreRings();
        }

        void Start()
        {
            RepairLevelPrefabReferencesInEditor();
            RepairGameplayCanvasScales();
            if (firstLifeLessonFx == null)
            {
                FirstLifeLessonFx[] lessons = UnityEngine.Object.FindObjectsByType<FirstLifeLessonFx>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (lessons.Length > 0) firstLifeLessonFx = lessons[0];
            }
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
            bool continuingCurrentLevel = continuedLevelReload == levelIndex;
            continuedLevelReload = -1;
            suppressTutorialForThisLoad = restartTutorialSuppressionPending
                && restartTutorialSuppressionLevel == levelIndex;
            // Consume the one-shot request immediately so it cannot suppress a later normal entry.
            restartTutorialSuppressionPending = false;
            restartTutorialSuppressionLevel = -1;
            ApplyLevelTheme(levelIndex / 10);   // Beginner / Intermediate / Advanced skin
            ConfigureChapterPresentation(levelIndex / 10);
            if (loseFx != null) loseFx.SetLeaderboardTheme(frameColor, gutterColor);
            model = LevelParser.Parse(levelPrefabs[levelIndex]);
            BuildView();
            SyncViews(true);
            // Arriving from level-select: fly the camera in from far out into the board.
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
                // Domain-reload-free Editor sessions can preserve runtime UnityEvent listeners.
                // Replace our own handlers before adding them so every tutorial action fires
                // exactly once, including the separately-authored purple Skip control.
                if (tutorialFx.againButton != null)
                {
                    tutorialFx.againButton.onClick.RemoveListener(TutorialWatchAgain);
                    tutorialFx.againButton.onClick.AddListener(TutorialWatchAgain);
                }
                if (tutorialFx.tryButton != null)
                {
                    tutorialFx.tryButton.onClick.RemoveListener(TutorialTryIt);
                    tutorialFx.tryButton.onClick.AddListener(TutorialTryIt);
                }
                if (tutorialFx.skipButton != null)
                {
                    tutorialFx.skipButton.onClick.RemoveListener(TutorialSkip);
                    tutorialFx.skipButton.onClick.AddListener(TutorialSkip);
                }
            }

            var levelInfo = levelPrefabs[levelIndex].GetComponent<ParaboxLevel>();
            par = levelInfo != null ? levelInfo.par : 0;

            // The campaign timer follows CampaignProgression's reviewed rule. Chapters I-IV use
            // their +3-second progression and tutorial allowances; Chapter V starts at 100
            // seconds on Level 41 and rises by two seconds through Level 50.
            timeLimit = TimeLimitForLevel(levelIndex, par);
            timeLeft = timeLimit;
            countdownArmed = false;
            RestoreTimerAfterRestart();
            // par == 0 means the prefab predates par data (wizard not re-run). Fall back to an
            // unrestrictive limit rather than handing the player an unwinnable level.
            moveLimit = MoveLimitForLevel(levelIndex, par);
            timedUp = false;
            // seamless menu-entry: the HUD is already "there" — skip the timer's scale/fade-in
            timerIntro = PlayerPrefs.GetInt("Parabox.Seamless", 0) == 1 ? IntroDur : 0f;

            tickBump = 0f;
            lastSecond = -1;
            int tier = Mathf.Clamp(levelIndex / 10, 0,
                (timerAccents != null && timerAccents.Length > 0) ? timerAccents.Length - 1 : 0);
            timerAccentCur = (timerAccents != null && timerAccents.Length > 0)
                ? timerAccents[tier] : new Color(1f, 0.62f, 0.37f, 1f);
            Transform scoreParent = timerRoot != null ? timerRoot.parent
                : movesLabel != null ? movesLabel.transform.parent : transform;
            BuildLevelPointsHud(scoreParent);
            BuildWinScoreReadout();
            BuildWinPlayerBadges();
            BuildWinCountdownUi();
            ConfigureScoreHudPresentation();
            ConfigureWinScorePresentation();
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
            MaybeTutorial();
            if (!Tutoring) ArmGameplayCountdown();
            if (!continuingCurrentLevel) LuxoddGameService.ReportLevelBegin(levelIndex);
        }

        #if UNITY_EDITOR
        // Editor-authoring only. The one-click prebuilder runs this while Game.unity is open in
        // edit mode; the resulting components and references are serialized into the scene.
        public void PrebuildStaticUi()
        {
            ArcadeActionButtonStyle.Apply(nextButton, "NEXT LEVEL", 24);
            if (menuButton != null)
            {
                menuButton.interactable = false;
                menuButton.gameObject.SetActive(false);
            }

            if (tutorialFx != null) tutorialFx.PrebuildStaticUi();
            if (loseFx != null) loseFx.PrebuildStaticUi();
            HideGameplaySoundButton();
            BuildScoreHud();
            BuildWinScoreReadout();
            BuildWinPlayerBadges();
            BuildWinCountdownUi();
            ConfigureScoreHudPresentation();
            ConfigureWinScorePresentation();

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
            // Gameplay uses more than one root Canvas (HUD, tutorial and terminal overlays).
            // Keep every Canvas in this scene on the same aspect-safe rule.  Match=0.5 can make
            // the 1920x1080 reference frame narrower than its authored content on a 16:10 screen,
            // which is why the top-right timer could be cut off even with a valid anchor.
            CanvasScaler[] scalers = UnityEngine.Object.FindObjectsByType<CanvasScaler>(
                FindObjectsInactive.Include);
            for (int i = 0; i < scalers.Length; i++)
            {
                CanvasScaler scaler = scalers[i];
                if (scaler == null || scaler.gameObject.scene != gameObject.scene) continue;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.matchWidthOrHeight = 0f;
            }

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
            HideGameplaySoundButton();
            // d-pad: one press/contact emits exactly one grid-cell move.
            WireHold(upButton,    Vector2Int.up);
            WireHold(downButton,  Vector2Int.down);
            WireHold(leftButton,  Vector2Int.left);
            WireHold(rightButton, Vector2Int.right);
            WireClick(undoButton, UiUndo);
            WireClick(restartButton, Restart);
            WireClick(hudMenuButton, GoToMenu);
            WireArcadeJoystick();
        }

        void HideGameplaySoundButton()
        {
            if (muteButton != null)
                muteButton.gameObject.SetActive(false);
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
            // Puzzle movement is one contact -> one cell. Holding an on-screen direction must not
            // silently spend extra moves while the player is still making the same gesture.
            h.repeatWhileHeld = false;
            h.onFire = () => UiMove(dir);
        }

        public void UiMove(Vector2Int dir)
        {
            if (Ended) return;
            RequestManualMove(dir);
        }

        // All manual gameplay inputs converge here. This closes the last double-dispatch gap
        // between the scene UI, Input System keyboard edges and Luxodd's legacy axis bridge.
        void RequestManualMove(Vector2Int dir)
        {
            if (dir == Vector2Int.zero || lastManualMoveFrame == Time.frameCount) return;
            lastManualMoveFrame = Time.frameCount;
            DoMove(dir);
        }

        public void UiUndo()
        {
            if (Ended) return;
            if (model.Undo())
            {
                undoCount++;
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

            Graphic face = muteButton != null ? muteButton.targetGraphic : null;
            if (face != null)
            {
                // The panel itself stays in the same dark glass family as the game background.
                // State is communicated by the icon, status word and a restrained edge tint.
                Color stateAccent = Sfx.Muted
                    ? new Color(1f, 0.40f, 0.49f, 1f)
                    : Color.Lerp(frameColor, Color.white, 0.14f);
                UIGradient gradient = face.GetComponent<UIGradient>();
                if (gradient != null)
                {
                    Color glassTop = Color.Lerp(new Color(0.035f, 0.075f, 0.16f, 1f),
                        frameColor, 0.16f);
                    Color glassBottom = Color.Lerp(Color.black, gutterColor, 0.46f);
                    glassBottom.a = 0.98f;
                    gradient.top = glassTop;
                    gradient.bottom = glassBottom;
                }
                face.color = Color.white;
                face.SetVerticesDirty();

                Outline outline = face.GetComponent<Outline>();
                if (outline != null)
                {
                    Color edge = Color.Lerp(frameColor, stateAccent, Sfx.Muted ? 0.36f : 0.72f);
                    edge.a = 0.92f;
                    outline.effectColor = edge;
                }
            }
        }

        // The old sound badge used sprite/text layers such as ")))" and "x". At gameplay scale
        // those layers could overlap the timer and read as a red Play button. Keep the replacement
        // code-native: dark background-matched glass, a real speaker glyph, and a clear ON/OFF
        // status centred directly below the timer.
        void ConfigurePremiumSoundControl()
        {
            if (muteButton == null || muteOnIcon == null || muteOffIcon == null) return;

            RectTransform muteRect = muteButton.transform as RectTransform;
            if (muteRect == null) return;

            if (timerRoot != null && timerRoot.parent != null && muteRect.parent != timerRoot.parent)
                muteRect.SetParent(timerRoot.parent, false);

            muteRect.anchorMin = muteRect.anchorMax = Vector2.one;
            muteRect.pivot = new Vector2(0.5f, 0.5f);
            muteRect.sizeDelta = new Vector2(88f, 88f);
            if (timerRoot != null)
            {
                float timerHeight = Mathf.Max(1f, timerRoot.rect.height);
                float controlHeight = muteRect.sizeDelta.y;
                muteRect.anchoredPosition = timerRoot.anchoredPosition
                    + Vector2.down * (timerHeight * 0.5f + 14f + controlHeight * 0.5f);
            }
            else
            {
                muteRect.anchoredPosition = new Vector2(-104f, -222f);
            }

            Image face = muteButton.targetGraphic as Image;
            if (face == null) face = muteButton.GetComponent<Image>();
            if (face != null)
            {
                face.enabled = true;
                face.color = Color.white;
                face.raycastTarget = true;
                face.type = Image.Type.Simple;
                face.preserveAspect = false;
                muteButton.targetGraphic = face;

                UIGradient gradient = face.GetComponent<UIGradient>();
                if (gradient == null) gradient = face.gameObject.AddComponent<UIGradient>();
                gradient.enabled = true;

                Shadow shadow = face.GetComponent<Shadow>();
                if (shadow == null) shadow = face.gameObject.AddComponent<Shadow>();
                shadow.enabled = true;
                shadow.effectColor = new Color(0f, 0.02f, 0.08f, 0.72f);
                shadow.effectDistance = new Vector2(0f, -7f);
                shadow.useGraphicAlpha = true;

                Outline outline = face.GetComponent<Outline>();
                if (outline == null) outline = face.gameObject.AddComponent<Outline>();
                // PremiumSoundDial owns the one clean visible rim. A second mesh outline creates
                // the stacked cyan rings visible at the bottom of the control.
                outline.enabled = false;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                outline.useGraphicAlpha = true;
            }

            BuildPremiumSoundState(muteOnIcon, false);
            BuildPremiumSoundState(muteOffIcon, true);
            muteButton.gameObject.SetActive(true);
        }

        void BuildPremiumSoundState(GameObject stateObject, bool muted)
        {
            if (stateObject == null) return;

            // Disable the legacy root glyph and every legacy child without destroying authored
            // scene data. This makes the repair safe for old scenes and repeatable after Restart.
            foreach (Graphic graphic in stateObject.GetComponents<Graphic>())
            {
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }
            for (int i = 0; i < stateObject.transform.childCount; i++)
            {
                Transform child = stateObject.transform.GetChild(i);
                if (child.name != "PremiumSoundState") child.gameObject.SetActive(false);
            }

            RectTransform state = EnsureSoundRect(stateObject.transform, "PremiumSoundState");
            state.anchorMin = Vector2.zero;
            state.anchorMax = Vector2.one;
            state.offsetMin = Vector2.zero;
            state.offsetMax = Vector2.zero;
            state.gameObject.SetActive(true);

            Transform oldBars = state.Find("WaveBars");
            Transform oldSlash = state.Find("MuteSlash");
            Transform oldStatus = state.Find("StatusLabel");
            Transform oldTopRail = state.Find("TopLightRail");
            Transform oldBottomRail = state.Find("BottomLightRail");
            Transform oldDivider = state.Find("Divider");
            Transform oldCaption = state.Find("SoundCaption");
            Transform oldDot = state.Find("StatusDot");
            if (oldBars != null) oldBars.gameObject.SetActive(false);
            if (oldSlash != null) oldSlash.gameObject.SetActive(false);
            if (oldStatus != null) oldStatus.gameObject.SetActive(false);
            if (oldTopRail != null) oldTopRail.gameObject.SetActive(false);
            if (oldBottomRail != null) oldBottomRail.gameObject.SetActive(false);
            if (oldDivider != null) oldDivider.gameObject.SetActive(false);
            if (oldCaption != null) oldCaption.gameObject.SetActive(false);
            if (oldDot != null) oldDot.gameObject.SetActive(false);

            Color accent = muted
                ? new Color(1f, 0.40f, 0.49f, 1f)
                : Color.Lerp(frameColor, Color.white, 0.14f);

            Image glow = EnsureSoundImage(state, "PremiumGlow");
            RectTransform glowRect = glow.rectTransform;
            glowRect.anchorMin = glowRect.anchorMax = glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(134f, 134f);
            glow.sprite = glowSprite;
            glow.preserveAspect = false;
            glow.enabled = glowSprite != null;
            Color glowColour = muted ? accent : Color.Lerp(frameColor, accent, 0.68f);
            glow.color = new Color(glowColour.r, glowColour.g, glowColour.b,
                muted ? 0.16f : 0.18f);
            glow.raycastTarget = false;
            glow.transform.SetAsFirstSibling();

            Color dialSecondary = Color.Lerp(frameColor,
                new Color(0.56f, 0.31f, 1f, 1f), 0.58f);
            PremiumSoundDial dial = EnsureSoundDial(state, "SoundDial");
            RectTransform dialRect = dial.rectTransform;
            dialRect.anchorMin = dialRect.anchorMax = dialRect.pivot
                = new Vector2(0.5f, 0.5f);
            dialRect.anchoredPosition = Vector2.zero;
            dialRect.sizeDelta = new Vector2(98f, 98f);
            dial.SetState(muted, frameColor, dialSecondary);
            dial.raycastTarget = false;

            PremiumSoundIcon glyph = EnsureSoundGlyph(state, "SoundGlyph");
            RectTransform glyphRect = glyph.rectTransform;
            glyphRect.anchorMin = glyphRect.anchorMax = glyphRect.pivot
                = new Vector2(0.5f, 0.5f);
            glyphRect.anchoredPosition = new Vector2(0f, 7f);
            glyphRect.sizeDelta = new Vector2(48f, 40f);
            glyph.SetState(muted, accent);
            glyph.raycastTarget = false;

            Font font = movesLabel != null && movesLabel.font != null
                ? movesLabel.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text stateLabel = EnsureSoundText(state, "StateLabel");
            RectTransform stateLabelRect = stateLabel.rectTransform;
            stateLabelRect.anchorMin = stateLabelRect.anchorMax = stateLabelRect.pivot
                = new Vector2(0.5f, 0.5f);
            stateLabelRect.anchoredPosition = new Vector2(7f, -27f);
            stateLabelRect.sizeDelta = new Vector2(34f, 18f);
            stateLabel.text = muted ? "OFF" : "ON";
            stateLabel.font = font;
            stateLabel.fontSize = 12;
            stateLabel.fontStyle = FontStyle.Bold;
            stateLabel.alignment = TextAnchor.MiddleCenter;
            stateLabel.color = Color.Lerp(accent, Color.white, 0.28f);
            stateLabel.supportRichText = false;
            stateLabel.raycastTarget = false;
            CrispUiTypography.Polish(stateLabel);

            Image statusDot = EnsureSoundImage(state, "StateLed");
            RectTransform dotRect = statusDot.rectTransform;
            dotRect.anchorMin = dotRect.anchorMax = dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.anchoredPosition = new Vector2(-15f, -27f);
            dotRect.sizeDelta = new Vector2(6f, 6f);
            statusDot.sprite = cellSprite;
            statusDot.color = accent;
            statusDot.raycastTarget = false;

            stateLabel.gameObject.SetActive(true);
            statusDot.gameObject.SetActive(true);
            dial.gameObject.SetActive(true);
            glyph.gameObject.SetActive(true);
            stateLabel.transform.SetAsLastSibling();
        }

        static RectTransform EnsureSoundRect(Transform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform));
            if (existing == null) item.transform.SetParent(parent, false);
            return item.transform as RectTransform;
        }

        static Image EnsureSoundImage(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null) item.transform.SetParent(parent, false);
            Image image = item.GetComponent<Image>();
            if (image == null) image = item.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        static Text EnsureSoundText(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            if (existing == null) item.transform.SetParent(parent, false);
            Text text = item.GetComponent<Text>();
            if (text == null) text = item.AddComponent<Text>();
            return text;
        }

        static PremiumSoundIcon EnsureSoundGlyph(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(PremiumSoundIcon));
            if (existing == null) item.transform.SetParent(parent, false);
            PremiumSoundIcon icon = item.GetComponent<PremiumSoundIcon>();
            if (icon == null) icon = item.AddComponent<PremiumSoundIcon>();
            return icon;
        }

        static PremiumSoundDial EnsureSoundDial(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(PremiumSoundDial));
            if (existing == null) item.transform.SetParent(parent, false);
            PremiumSoundDial dial = item.GetComponent<PremiumSoundDial>();
            if (dial == null) dial = item.AddComponent<PremiumSoundDial>();
            return dial;
        }

        // Cabinet mapping: stick=move, Black=confirm, Red=undo, Yellow=restart and Purple=skip
        // walkthrough. Green owns the one-time Level-1 TRY IT action; Blue and White do nothing.
        bool HandleArcadeInput()
        {
            var arcade = LuxoddArcadeAdapter.Instance;
            if (arcade == null) return false;

            // In the Editor, Luxodd's legacy axes can receive the same arrow/WASD press a frame
            // after the Input System. Keep swallowing that mirrored axis until both the key and
            // the smoothed legacy axis have returned to neutral. This is what closes the real
            // two/three-cell bug; a one-frame debounce alone is not enough for smoothed axes.
            if (suppressMirroredArcadeMove)
            {
                if (AnyMoveKeyHeld(Keyboard.current) || arcade.Direction != Vector2Int.zero)
                {
                    arcade.ClaimCurrentMoveGesture();
                    return true;
                }
                suppressMirroredArcadeMove = false;
            }

            if (Lost)
            {
                if (firstLifeLessonOpen && arcade.TryItDown
                    && firstLifeLessonFx != null && firstLifeLessonFx.ReadyForTryIt)
                {
                    Sfx.Click();
                    firstLifeLessonFx.Confirm();
                    return true;
                }
                // The keyboard path below has the same readiness gate. Let it handle
                // Enter/Space during this lesson; ordinary loss input remains swallowed.
                if (firstLifeLessonOpen) return false;
                // The leaderboard is informational and Luxodd owns the upcoming transaction.
                // The Level-1 lesson is the only pre-transaction exception; all other loss input
                // remains swallowed until the host returns a choice.
                return true;
            }

            if (finaleSequenceActive)
            {
                return finaleOverlay != null && finaleOverlay.HandleArcadeInput(arcade);
            }

            if (Tutoring)
            {
                if (CanSkipCurrentTutorial() && arcade.SkipDown)
                {
                    Sfx.Click();
                    TutorialSkip();
                    return true;
                }
                if (tutorialInteractive)
                {
                    if (arcade.RestartDown)
                    {
                        Sfx.Click();
                        BeginInteractiveTutorial();
                        return true;
                    }
                    if (arcade.UndoDown)
                    {
                        UndoInteractiveTutorial();
                        return true;
                    }
                    if (arcade.MovePulse && arcade.Direction != Vector2Int.zero)
                    {
                        MoveInteractiveTutorial(arcade.Direction);
                        return true;
                    }
                    // Consume the complete held gesture so one cabinet tilt remains one cell.
                    return arcade.Direction != Vector2Int.zero;
                }
                return HandleArcadeTutorialInput(arcade);
            }

            if (won)
            {
                if (winControlsReady && arcade.ConfirmDown)
                {
                    Sfx.Click();
                    NextLevel();
                }
                return true;
            }
            if (arcade.RestartDown) { Sfx.Click(); Restart(); return true; }
            if (arcade.UndoDown)
            {
                UiUndo();
                return true;
            }
            if (arcade.MovePulse && arcade.Direction != Vector2Int.zero)
            {
                RequestManualMove(arcade.Direction);
                return true;
            }
            // ArcadeControls' legacy Horizontal/Vertical axes also include keyboard arrows/WASD
            // in the Unity Editor. Claim the complete held gesture here, not just its first pulse,
            // otherwise the following frame falls through to the keyboard path and moves a second
            // cell for the same press.
            if (arcade.Direction != Vector2Int.zero)
                return true;
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
            UpdatePremiumScoreHud();

            TickTutorialProgressWatchdog();

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
                if (firstLifeLessonOpen && (kb.enterKey.wasPressedThisFrame
                    || kb.spaceKey.wasPressedThisFrame)
                    && firstLifeLessonFx != null && firstLifeLessonFx.ReadyForTryIt)
                {
                    Sfx.Click();
                    firstLifeLessonFx.Confirm();
                }
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
            // Gameplay uses press edges rather than held state, so a direction held while the
            // tutorial closes cannot spill into the puzzle as an unintended move.
            if (Tutoring)
            {
                if (CanSkipCurrentTutorial() && kb.tabKey.wasPressedThisFrame)
                {
                    TutorialSkip();
                    return;
                }
                if (tutorialInteractive)
                {
                    if (kb.rKey.wasPressedThisFrame)
                    {
                        BeginInteractiveTutorial();
                        return;
                    }
                    if (kb.zKey.wasPressedThisFrame)
                    {
                        UndoInteractiveTutorial();
                        return;
                    }
                    Vector2Int practiceDirection = ReadDirectionDown(kb);
                    if (practiceDirection != Vector2Int.zero)
                    {
                        ClaimKeyboardMoveGesture();
                        MoveInteractiveTutorial(practiceDirection);
                    }
                    return;
                }
                Vector2Int tutorialDirection = ReadDirectionDown(kb);
                if (tutorialDirection != Vector2Int.zero)
                    ClaimKeyboardMoveGesture();
                HandleTutorialChoiceInput(tutorialDirection,
                    tutorialDirection != Vector2Int.zero,
                    kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
                return;
            }

            if (won)
            {
                if (winControlsReady
                    && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
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
                    undoCount++;
                    Sfx.Undo();
                    SyncViews(false);
                    UpdateHud();
                }
                return;
            }

            // One physical/key press is exactly one logical move. A new cell requires release and
            // another press, matching the cabinet joystick's neutral -> tilt gesture contract.
            Vector2Int pressed = ReadDirectionDown(kb);
            if (pressed != Vector2Int.zero)
            {
                ClaimKeyboardMoveGesture();
                RequestManualMove(pressed);
            }
        }

        void ClaimKeyboardMoveGesture()
        {
            suppressMirroredArcadeMove = true;
            LuxoddArcadeAdapter.Instance?.ClaimCurrentMoveGesture();
        }

        static bool AnyMoveKeyHeld(Keyboard kb)
        {
            return kb != null &&
                (kb.wKey.isPressed || kb.upArrowKey.isPressed ||
                 kb.sKey.isPressed || kb.downArrowKey.isPressed ||
                 kb.aKey.isPressed || kb.leftArrowKey.isPressed ||
                 kb.dKey.isPressed || kb.rightArrowKey.isPressed);
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

            // Keep the clock armed after valid input. Gameplay normally arms it as soon as the
            // player receives control; this also safely covers upgraded scenes and old saves.
            ArmGameplayCountdown();

            SyncViews(false, dir);

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
                    if (GoalFeedbackFx.IsSatisfiedBy(e, GoalFeedbackFx.Kind.Cargo))
                        target[(room.id, g.x, g.y)] = false;
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
            nestedBoxGuidance = GetComponent<NestedBoxGuidanceFx>();
            if (nestedBoxGuidance == null)
                nestedBoxGuidance = gameObject.AddComponent<NestedBoxGuidanceFx>();
            nestedBoxGuidance.Configure(model, roomRoots, views, cellSprite, glowSprite);
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

        void SyncViews(bool instant, Vector2Int portalDirection = default)
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
                Transform targetParent = roomRoots[e.roomId];
                bool crossedRoomBoundary = moveStartPositions.TryGetValue(e, out var start)
                    && start.room != e.roomId;
                // Recursive cargo changes coordinate spaces exactly like the player. Using the
                // generic reparent animation made an UP push appear to fly sideways/outside the
                // chamber even though the model had correctly placed the cargo in its parent.
                // Give every cross-room entity the same cardinal hand-off animation so the visual
                // direction always matches the joystick direction and the logical grid state.
                if ((ReferenceEquals(e, model.player) || crossedRoomBoundary)
                    && v.transform.parent != targetParent
                    && portalDirection != Vector2Int.zero)
                    v.SetPortalTarget(targetParent, Cell(room, e.pos), portalDirection, instant);
                else
                    v.SetTarget(targetParent, Cell(room, e.pos), instant);
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
            int presentationRoomId = playerRoom.id;

            // In the finale, cargo can leave the room the player is still standing inside. The
            // old camera continued to frame only that inner room, so the correctly transferred
            // cargo appeared to float into black space with no destination. For that hand-off
            // frame, open the view to the immediate parent room. The player remains logically in
            // the child and the next input is unchanged; this only reveals the real playable cell
            // that accepted the cargo. A blocked parent side never reaches this branch because
            // LevelModel refuses that push.
            if (levelIndex >= 40 && portalDirection != Vector2Int.zero
                && moveStartPositions.TryGetValue(model.player, out var playerStart)
                && playerStart.room == model.player.roomId)
            {
                foreach (PEntity entity in model.entities)
                {
                    if (entity == null || entity.sunk || !entity.IsCrate
                        || entity.interiorRoomId >= 0
                        || !moveStartPositions.TryGetValue(entity, out var entityStart)
                        || entityStart.room == entity.roomId
                        || entityStart.room != playerStart.room
                        || !model.rooms.TryGetValue(entityStart.room, out PRoom sourceRoom)
                        || sourceRoom.containerBox == null
                        || sourceRoom.containerBox.roomId != entity.roomId)
                        continue;

                    presentationRoomId = entity.roomId;
                    break;
                }
            }

            PRoom presentationRoom = model.rooms[presentationRoomId];
            if (hiddenDiscovery != null) hiddenDiscovery.Refresh();
            FocusRoom(presentationRoomId);
            // The active nested room lives inside a larger coloured meta-box shell. Frame both,
            // not just the miniature room geometry, so the shell remains completely visible like
            // a playable room-container instead of becoming cropped cyan bands at screen edges.
            float nestedFraming = presentationRoomId == 0 ? 1f : 1.45f;
            cameraFollow.SetTargetRoom(roomRoots[presentationRoomId], presentationRoom.width,
                presentationRoom.height, instant, nestedFraming);
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
            if (owner != immediateBox) return false;

            // The named signal root is only a Transform. Its actual renderers are children named
            // ClosedSignal_Lamp, Glow, Housing, etc. Testing only the renderer's own name hid all
            // of them when entering a box. Keep the complete signal group on this room's shell,
            // but not lights belonging to a parent/sibling box outside the focused interior.
            for (Transform part = child; part != null && part != immediateBox; part = part.parent)
                if (part.name.StartsWith("NestedClosedSignal_")) return true;

            return child.name == "Frame" || child.name == "Backing"
                || child.name == "NestedShellHighlightTop"
                || child.name == "NestedShellHighlightLeft"
                || child.name.StartsWith("NestedDoorwayFloor_")
                || child.name.StartsWith("NestedDoorwayMask_");
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
            // Best-move records are still saved for scoring and the level map, but the gameplay
            // HUD shows only the information the player can act on right now.
            movesLabel.text = $"MOVES  {left}";
            movesLabel.color = left <= 3 ? TimerWarn : Color.white;
            UpdateScoreHud();
            UpdatePremiumScoreHud();
        }

        #if UNITY_EDITOR
        void BuildScoreHud()
        {
            Transform parent = timerRoot != null ? timerRoot.parent
                : movesLabel != null ? movesLabel.transform.parent : transform;

            if (scoreRoot != null && scoreLabel != null)
            {
                BuildLevelPointsHud(parent);
                return;
            }

            Transform existing = parent != null ? parent.Find("ScorePanel") : null;
            if (existing != null)
            {
                scoreRoot = existing as RectTransform;
                scoreLabel = existing.GetComponentInChildren<Text>(true);
                if (scoreRoot != null && scoreLabel != null)
                {
                    BuildLevelPointsHud(parent);
                    return;
                }
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
            BuildLevelPointsHud(parent);
        }
        #endif

        void BuildLevelPointsHud(Transform parent)
        {
            if (levelPointsLabel != null) return;
            Transform existing = parent != null ? parent.Find("LevelPointsText") : null;
            if (existing != null)
            {
                levelPointsLabel = existing.GetComponent<Text>();
                if (levelPointsLabel != null) return;
                DestroySupplementalUiObject(existing.gameObject);
            }

            var labelObject = new GameObject("LevelPointsText", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);
            levelPointsLabel = labelObject.GetComponent<Text>();
            levelPointsLabel.font = movesLabel != null && movesLabel.font != null
                ? movesLabel.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            levelPointsLabel.fontSize = 20;
            levelPointsLabel.fontStyle = FontStyle.Bold;
            levelPointsLabel.alignment = TextAnchor.MiddleCenter;
            levelPointsLabel.color = Color.Lerp(timerAccentCur, Color.white, 0.45f);
            levelPointsLabel.raycastTarget = false;
            levelPointsLabel.supportRichText = true;
            CrispUiTypography.Polish(levelPointsLabel);
        }

        void BuildWinScoreReadout()
        {
            if (winScoreValue != null || winPanel == null) return;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window == null) return;
            Transform existing = window.Find("WinScoreValue");
            if (existing != null)
            {
                winScoreValue = existing.GetComponent<Text>();
                if (winScoreValue != null) return;
                DestroySupplementalUiObject(existing.gameObject);
            }

            var scoreObject = new GameObject("WinScoreValue", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = (RectTransform)scoreObject.transform;
            rect.SetParent(window, false);
            winScoreValue = scoreObject.GetComponent<Text>();
            winScoreValue.font = winStats != null && winStats.font != null
                ? winStats.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            winScoreValue.fontSize = 48;
            winScoreValue.fontStyle = FontStyle.Bold;
            winScoreValue.alignment = TextAnchor.MiddleCenter;
            winScoreValue.color = new Color(1f, 0.91f, 0.65f, 1f);
            winScoreValue.raycastTarget = false;
            winScoreValue.supportRichText = false;
            CrispUiTypography.Polish(winScoreValue);
        }

        void BuildWinPlayerBadges()
        {
            if (winPanel == null || playerPrefab == null) return;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window == null) return;

            SpriteRenderer bodyRenderer = FindPlayerRenderer("Body");
            SpriteRenderer leftEyeRenderer = FindPlayerRenderer("EyeL");
            SpriteRenderer rightEyeRenderer = FindPlayerRenderer("EyeR");
            if (bodyRenderer == null || bodyRenderer.sprite == null) return;

            for (int i = 0; i < 3; i++)
            {
                RectTransform badge = window.Find("WinStar" + i) as RectTransform;
                if (badge == null) continue;

                Transform oldBody = badge.Find("Star");
                if (oldBody != null) oldBody.name = "PlayerBody";
                Image body = EnsureWinBadgeImage(badge, "PlayerBody", bodyRenderer.sprite,
                    Vector2.zero, new Vector2(74f, 74f));
                body.color = bodyRenderer.color;

                Transform shine = badge.Find("Shine");
                if (shine != null) shine.gameObject.SetActive(false);
                Image glow = badge.Find("Glow") != null
                    ? badge.Find("Glow").GetComponent<Image>() : null;
                if (glow != null)
                {
                    glow.color = new Color(1f, 0.28f, 0.60f, 0.46f);
                    glow.raycastTarget = false;
                }

                Image lightRing = EnsureWinBadgeImage(badge, "PlayerLightRing", ringSprite,
                    Vector2.zero, new Vector2(104f, 104f));
                lightRing.color = new Color(0.44f, 0.96f, 1f, 0f);
                lightRing.enabled = ringSprite != null;

                Image lightFlash = EnsureWinBadgeImage(badge, "PlayerLightFlash", bodyRenderer.sprite,
                    Vector2.zero, new Vector2(74f, 74f));
                lightFlash.color = new Color(1f, 1f, 1f, 0f);

                if (leftEyeRenderer != null && leftEyeRenderer.sprite != null)
                {
                    Image eye = EnsureWinBadgeImage(badge, "PlayerEyeL", leftEyeRenderer.sprite,
                        new Vector2(-13f, 5f), new Vector2(13f, 18f));
                    eye.color = leftEyeRenderer.color;
                }
                if (rightEyeRenderer != null && rightEyeRenderer.sprite != null)
                {
                    Image eye = EnsureWinBadgeImage(badge, "PlayerEyeR", rightEyeRenderer.sprite,
                        new Vector2(13f, 5f), new Vector2(13f, 18f));
                    eye.color = rightEyeRenderer.color;
                }

                if (glow != null) glow.transform.SetAsFirstSibling();
                lightRing.transform.SetSiblingIndex(Mathf.Min(1, badge.childCount - 1));
                body.transform.SetSiblingIndex(Mathf.Min(2, badge.childCount - 1));
                lightFlash.transform.SetSiblingIndex(Mathf.Min(3, badge.childCount - 1));
                Transform leftEye = badge.Find("PlayerEyeL");
                Transform rightEye = badge.Find("PlayerEyeR");
                if (leftEye != null) leftEye.SetAsLastSibling();
                if (rightEye != null) rightEye.SetAsLastSibling();

                CanvasGroup group = badge.GetComponent<CanvasGroup>();
                if (group == null) group = badge.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        // The result board owns its own auto-advance clock. Four thin filled images sit directly
        // on the outer neon frame and drain clockwise, while the label above the board keeps the
        // remaining seconds explicit without covering the frame. Everything is prebuilt into
        // Game.unity by the editor baker;
        // the runtime branch below is only an old-scene safety fallback.
        void BuildWinCountdownUi()
        {
            if (winPanel == null) return;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window == null) return;

            if (winCountdownRoot == null)
            {
                Transform existing = window.Find("WinCountdownBorder");
                if (existing != null) winCountdownRoot = existing as RectTransform;
            }
            if (winCountdownRoot == null)
            {
                var root = new GameObject("WinCountdownBorder", typeof(RectTransform));
                winCountdownRoot = (RectTransform)root.transform;
                winCountdownRoot.SetParent(window, false);
            }
            winCountdownRoot.anchorMin = winCountdownRoot.anchorMax
                = winCountdownRoot.pivot = new Vector2(0.5f, 0.5f);
            winCountdownRoot.anchoredPosition = Vector2.zero;
            winCountdownRoot.sizeDelta = new Vector2(720f, 620f);

            string[] names = { "TimerTop", "TimerRight", "TimerBottom", "TimerLeft" };
            Vector2[] positions =
            {
                new Vector2(0f, 322f), new Vector2(372f, 0f),
                new Vector2(0f, -322f), new Vector2(-372f, 0f),
            };
            Vector2[] sizes =
            {
                new Vector2(744f, 8f), new Vector2(8f, 644f),
                new Vector2(744f, 8f), new Vector2(8f, 644f),
            };
            if (winCountdownBorder == null || winCountdownBorder.Length != 4)
                winCountdownBorder = new Image[4];
            for (int i = 0; i < winCountdownBorder.Length; i++)
            {
                Image image = winCountdownBorder[i];
                if (image == null)
                {
                    Transform existing = winCountdownRoot.Find(names[i]);
                    if (existing != null) image = existing.GetComponent<Image>();
                }
                if (image == null)
                {
                    var item = new GameObject(names[i], typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(Image));
                    item.transform.SetParent(winCountdownRoot, false);
                    image = item.GetComponent<Image>();
                }
                winCountdownBorder[i] = image;
                RectTransform rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = positions[i];
                rect.sizeDelta = sizes[i];
                image.sprite = cellSprite != null ? cellSprite : glowSprite;
                image.type = Image.Type.Filled;
                image.fillMethod = i % 2 == 0
                    ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
                image.fillOrigin = i == 0 ? (int)Image.OriginHorizontal.Left
                    : i == 1 ? (int)Image.OriginVertical.Top
                    : i == 2 ? (int)Image.OriginHorizontal.Right
                    : (int)Image.OriginVertical.Bottom;
                image.fillAmount = 1f;
                image.color = Color.Lerp(frameColor, Color.white, 0.28f);
                image.raycastTarget = false;
            }

            if (winCountdownLabel == null)
            {
                Transform existing = winCountdownRoot.Find("CountdownLabel");
                if (existing != null) winCountdownLabel = existing.GetComponent<Text>();
            }
            if (winCountdownLabel == null)
            {
                var labelObject = new GameObject("CountdownLabel", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text));
                labelObject.transform.SetParent(winCountdownRoot, false);
                winCountdownLabel = labelObject.GetComponent<Text>();
                var outline = labelObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0.03f, 0.08f, 0.95f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            RectTransform labelRect = winCountdownLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot
                = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0f, 365f);
            labelRect.sizeDelta = new Vector2(430f, 50f);
            winCountdownLabel.font = winStats != null && winStats.font != null
                ? winStats.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            winCountdownLabel.fontSize = 32;
            winCountdownLabel.fontStyle = FontStyle.Bold;
            winCountdownLabel.alignment = TextAnchor.MiddleCenter;
            winCountdownLabel.color = Color.Lerp(frameColor, Color.white, 0.45f);
            winCountdownLabel.raycastTarget = false;
            winCountdownLabel.supportRichText = false;
            winCountdownLabel.text = "NEXT LEVEL IN 5";
            CrispUiTypography.Polish(winCountdownLabel);
            winCountdownLabel.transform.SetAsLastSibling();
            winCountdownRoot.gameObject.SetActive(false);
        }

        SpriteRenderer FindPlayerRenderer(string childName)
        {
            Transform child = playerPrefab != null ? playerPrefab.transform.Find(childName) : null;
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        static Image EnsureWinBadgeImage(RectTransform parent, string name, Sprite sprite,
            Vector2 position, Vector2 size)
        {
            Transform existing = parent.Find(name);
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null) item.transform.SetParent(parent, false);
            RectTransform rect = item.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = item.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
            item.SetActive(true);
            return image;
        }

        static void DestroySupplementalUiObject(GameObject item)
        {
            if (item == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) Object.DestroyImmediate(item);
            else Object.Destroy(item);
#else
            Object.Destroy(item);
#endif
        }

        void UpdateScoreHud()
        {
            int count = levelPrefabs != null ? levelPrefabs.Length : 0;
            int total = ScoreSystem.Total(count);
            int maximum = ScoreSystem.TotalMaximum(count);
            if (scoreLabel != null)
                scoreLabel.text = $"TOTAL POINTS  {total:n0} / {maximum:n0}";
            if (levelPointsLabel != null)
            {
                int levelBest = ScoreSystem.Best(levelIndex);
                int levelMaximum = ScoreSystem.MaxPoints(levelIndex);
                levelPointsLabel.text = $"LEVEL POINTS  {levelBest:n0} / {levelMaximum:n0}";
            }
        }

        void UpdatePremiumScoreHud()
        {
            if (premiumScoreValue == null && premiumTimeValue == null
                && premiumMovesValue == null) return;
            if (model == null) return;

            ScoreSystem.Breakdown live = ScoreSystem.Calculate(levelIndex,
                MoveParForLevel(levelIndex, par), model.MoveCount, moveLimit,
                timeLeft, timeLimit, undoCount);

            EnsurePremiumScoreRings();
            if (premiumScoreRings != null)
            {
                premiumScoreRings.SetBreakdown(live);
                return;
            }

            if (premiumScoreValue != null)
            {
                premiumScoreValue.text = live.runScore.ToString();
                premiumScoreValue.color = live.runScore <= 30 ? TimerWarn : Color.white;
            }
            if (premiumTimeValue != null)
            {
                premiumTimeValue.text = live.timePenalty > 0 ? $"TIME  −{live.timePenalty}" : "TIME";
                premiumTimeValue.color = live.timePenalty > 0 ? TimerWarn : Color.white;
            }
            if (premiumTimeDetail != null)
                premiumTimeDetail.text = $"-{ScoreSystem.TimePenaltyPerSecond} / SEC AFTER "
                    + ScoreSystem.TimeGraceSeconds;
            if (premiumMovesValue != null)
            {
                premiumMovesValue.text = live.movePenalty > 0 ? $"MOVES  −{live.movePenalty}" : "MOVES";
                premiumMovesValue.color = live.movePenalty > 0 ? TimerWarn : Color.white;
            }
            if (premiumMovesDetail != null)
                premiumMovesDetail.text = $"TARGET  {live.targetMoves}   USED  {live.usedMoves}";
        }

        // The premium panel is now the single score display. Keep only MOVES in this top strip so
        // LEVEL POINTS and TOTAL POINTS do not duplicate the same information in different words.
        void ConfigureScoreHudPresentation()
        {
            ConfigurePremiumScoreChipSpacing();
            EnsurePremiumScoreRings();

            if (movesLabel != null)
            {
                RectTransform movesRect = movesLabel.rectTransform;
                movesRect.anchorMin = movesRect.anchorMax = movesRect.pivot = new Vector2(0.5f, 1f);
                movesRect.anchoredPosition = new Vector2(0f, -96f);
                movesRect.sizeDelta = new Vector2(340f, 54f);
                movesLabel.fontSize = 28;
                movesLabel.fontStyle = FontStyle.Bold;
                movesLabel.alignment = TextAnchor.MiddleCenter;
            }

            if (levelPointsLabel != null)
                levelPointsLabel.gameObject.SetActive(false);
            if (scoreRoot != null)
                scoreRoot.gameObject.SetActive(false);

            if (levelPointsLabel != null)
            {
                RectTransform levelPointsRect = levelPointsLabel.rectTransform;
                levelPointsRect.anchorMin = levelPointsRect.anchorMax = levelPointsRect.pivot
                    = new Vector2(0.5f, 1f);
                levelPointsRect.anchoredPosition = new Vector2(0f, -96f);
                levelPointsRect.sizeDelta = new Vector2(290f, 40f);
                levelPointsLabel.fontSize = 20;
                levelPointsLabel.fontStyle = FontStyle.Bold;
                levelPointsLabel.alignment = TextAnchor.MiddleCenter;
                levelPointsLabel.color = Color.Lerp(timerAccentCur, Color.white, 0.45f);
                levelPointsLabel.lineSpacing = 1f;
                levelPointsLabel.supportRichText = false;
            }

            if (scoreRoot == null) return;
            scoreRoot.anchorMin = scoreRoot.anchorMax = scoreRoot.pivot = new Vector2(0.5f, 1f);
            scoreRoot.anchoredPosition = new Vector2(310f, -96f);
            scoreRoot.sizeDelta = new Vector2(330f, 40f);

            Image panel = scoreRoot.GetComponent<Image>();
            if (panel != null)
            {
                panel.enabled = false;
                panel.color = Color.clear;
                panel.raycastTarget = false;
                UIGradient gradient = panel.GetComponent<UIGradient>();
                if (gradient != null) gradient.enabled = false;
            }
            Shadow shadow = scoreRoot.GetComponent<Shadow>();
            if (shadow != null) shadow.enabled = false;
            Outline outline = scoreRoot.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
            if (scoreLabel != null)
            {
                scoreLabel.fontSize = 20;
                scoreLabel.fontStyle = FontStyle.Bold;
                scoreLabel.alignment = TextAnchor.MiddleCenter;
                scoreLabel.color = Color.Lerp(timerAccentCur, Color.white, 0.52f);
                scoreLabel.lineSpacing = 1f;
                scoreLabel.supportRichText = false;
                RectTransform textRect = scoreLabel.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
            }
            if (levelPointsLabel != null) levelPointsLabel.transform.SetAsLastSibling();
            scoreRoot.SetAsLastSibling();
        }

        void EnsurePremiumScoreRings()
        {
            if (premiumScoreRings != null && premiumScoreRings.IsBound) return;
            if (premiumScoreValue == null) return;
            Transform root = premiumScoreValue.transform;
            while (root != null && root.name != "PremiumScoreHudOption2") root = root.parent;
            if (root == null) return;
            premiumScoreRings = PremiumScoreRings.Apply(root as RectTransform, premiumScoreValue,
                premiumTimeValue, premiumTimeDetail, premiumMovesValue, premiumMovesDetail);
        }

        void ConfigurePremiumScoreChipSpacing()
        {
            Text source = premiumTimeValue != null ? premiumTimeValue
                : premiumMovesValue != null ? premiumMovesValue : premiumScoreValue;
            if (source == null) return;

            Transform premiumRoot = source.transform;
            while (premiumRoot != null && premiumRoot.name != "PremiumScoreHudOption2")
                premiumRoot = premiumRoot.parent;
            if (premiumRoot == null) return;

            ResizePremiumScoreChip(premiumRoot.Find("TimeChip") as RectTransform, -67f);
            ResizePremiumScoreChip(premiumRoot.Find("MovesChip") as RectTransform, -153f);
        }

        static void ResizePremiumScoreChip(RectTransform chip, float y)
        {
            if (chip == null) return;

            // Preserve the score disc and every existing style. Only make the two information
            // cards compact enough to leave a clean gutter before the cyan gameplay-board frame.
            const float width = 232f;
            const float height = 72f;
            const float border = 4f;
            chip.anchoredPosition = new Vector2(342f, y);
            chip.sizeDelta = new Vector2(width, height);

            SetPremiumScoreRect(chip.Find("Fill") as RectTransform,
                Vector2.zero, new Vector2(width - 8f, height - 8f));
            SetPremiumScoreRect(chip.Find("Main") as RectTransform,
                new Vector2(-2f, 13f), new Vector2(width - 42f, 30f));
            SetPremiumScoreRect(chip.Find("Detail") as RectTransform,
                new Vector2(-2f, -17f), new Vector2(width - 42f, 25f));
            SetPremiumScoreRect(chip.Find("BorderTop") as RectTransform,
                new Vector2(0f, height * 0.5f - border * 0.5f),
                new Vector2(width, border));
            SetPremiumScoreRect(chip.Find("BorderBottom") as RectTransform,
                new Vector2(0f, -height * 0.5f + border * 0.5f),
                new Vector2(width, border));
            SetPremiumScoreRect(chip.Find("BorderLeft") as RectTransform,
                new Vector2(-width * 0.5f + border * 0.5f, 0f),
                new Vector2(border, height));
            SetPremiumScoreRect(chip.Find("BorderRight") as RectTransform,
                new Vector2(width * 0.5f - border * 0.5f, 0f),
                new Vector2(border, height));
        }

        static void SetPremiumScoreRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // Keep the win board readable at arcade distance. The result title, this level's score,
        // campaign total and one primary action are enough; detailed bonus accounting belongs in
        // telemetry rather than on the completion screen.
        void ConfigureWinScorePresentation()
        {
            if (winPanel == null) return;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window != null)
            {
                window.sizeDelta = new Vector2(720f, 620f);
                ConfigureWinBoardBackground(window);
                RectTransform ring = window.Find("WinOutline") as RectTransform;
                if (ring != null) ring.sizeDelta = new Vector2(720f, 620f);
                RectTransform ring2 = window.Find("WinOutline2") as RectTransform;
                if (ring2 != null) ring2.sizeDelta = new Vector2(744f, 644f);
                for (int i = 0; i < 3; i++)
                {
                    RectTransform star = window.Find("WinStar" + i) as RectTransform;
                    if (star != null)
                    {
                        star.anchoredPosition = new Vector2((i - 1) * 84f, 252f);
                        star.sizeDelta = new Vector2(80f, 80f);
                    }
                }
                RectTransform glow = window.Find("BtnGlow") as RectTransform;
                if (glow != null) glow.anchoredPosition = new Vector2(0f, -230f);
            }

            if (winTitle != null)
            {
                winTitle.rectTransform.anchoredPosition = new Vector2(0f, 165f);
                winTitle.rectTransform.sizeDelta = new Vector2(670f, 76f);
                winTitle.fontSize = 50;
                winTitle.fontStyle = FontStyle.Bold;
                winTitle.alignment = TextAnchor.MiddleCenter;
                winTitle.supportRichText = false;
                winTitle.color = Color.white;
                UIGradient titleGradient = winTitle.GetComponent<UIGradient>();
                if (titleGradient == null)
                    titleGradient = winTitle.gameObject.AddComponent<UIGradient>();
                titleGradient.enabled = true;
                titleGradient.top = new Color(1f, 1f, 1f, 1f);
                titleGradient.bottom = new Color(0.34f, 0.92f, 1f, 1f);
                Shadow titleShadow = winTitle.GetComponent<Shadow>();
                if (titleShadow == null)
                    titleShadow = winTitle.gameObject.AddComponent<Shadow>();
                titleShadow.enabled = true;
                titleShadow.effectColor = new Color(0f, 0.16f, 0.28f, 0.52f);
                titleShadow.effectDistance = new Vector2(0f, -2f);
                titleShadow.useGraphicAlpha = true;
                winTitle.SetVerticesDirty();
                ConfigureWinTitleAccents(window);
            }
            if (winScoreValue != null)
            {
                winScoreValue.rectTransform.anchoredPosition = new Vector2(0f, 76f);
                winScoreValue.rectTransform.sizeDelta = new Vector2(670f, 58f);
                winScoreValue.fontSize = 40;
                winScoreValue.alignment = TextAnchor.MiddleCenter;
                winScoreValue.supportRichText = false;
                winScoreValue.color = new Color(1f, 0.91f, 0.65f, 1f);
            }
            if (winStats != null)
            {
                winStats.rectTransform.anchoredPosition = new Vector2(0f, -4f);
                winStats.rectTransform.sizeDelta = new Vector2(650f, 52f);
                winStats.fontSize = 25;
                winStats.alignment = TextAnchor.MiddleCenter;
                winStats.supportRichText = false;
                winStats.lineSpacing = 1f;
                winStats.horizontalOverflow = HorizontalWrapMode.Wrap;
                winStats.verticalOverflow = VerticalWrapMode.Truncate;
            }
            RectTransform nextRect = nextButton != null ? nextButton.transform as RectTransform : null;
            if (nextRect != null)
            {
                nextButton.gameObject.SetActive(true);
                nextRect.anchoredPosition = new Vector2(0f, -230f);
                nextRect.sizeDelta = new Vector2(310f, 82f);
            }
            if (menuButton != null)
            {
                menuButton.interactable = false;
                menuButton.gameObject.SetActive(false);
            }
            if (winCountdownRoot != null) winCountdownRoot.SetAsLastSibling();
        }

        // The results board should feel like part of the sci-fi room, not a flat blue card. Build
        // the background from the active chapter palette so it remains premium and readable in
        // every world: deep glass, two restrained light blooms and a code-drawn circuit surface.
        void ConfigureWinBoardBackground(RectTransform window)
        {
            if (window == null) return;

            Image panel = window.GetComponent<Image>();
            if (panel != null)
            {
                panel.enabled = true;
                panel.color = Color.white;
                panel.raycastTarget = false;
                UIGradient gradient = panel.GetComponent<UIGradient>();
                if (gradient == null) gradient = panel.gameObject.AddComponent<UIGradient>();
                gradient.enabled = true;
                Color glassTop = Color.Lerp(new Color(0.025f, 0.072f, 0.145f, 1f),
                    frameColor, 0.13f);
                Color glassBottom = Color.Lerp(new Color(0.008f, 0.018f, 0.052f, 1f),
                    gutterColor, 0.42f);
                glassBottom.a = 0.99f;
                gradient.top = glassTop;
                gradient.bottom = glassBottom;

                Shadow shadow = panel.GetComponent<Shadow>();
                if (shadow == null) shadow = panel.gameObject.AddComponent<Shadow>();
                shadow.enabled = true;
                shadow.effectColor = new Color(0f, 0.01f, 0.04f, 0.82f);
                shadow.effectDistance = new Vector2(0f, -12f);
                shadow.useGraphicAlpha = true;
                panel.SetVerticesDirty();
            }

            Image upperGlow = EnsureWinTitleDecoration(window, "ResultAmbientGlow",
                new Vector2(0f, 142f), new Vector2(650f, 370f));
            upperGlow.sprite = glowSprite;
            upperGlow.type = Image.Type.Simple;
            upperGlow.preserveAspect = false;
            upperGlow.enabled = glowSprite != null;
            upperGlow.color = new Color(frameColor.r, frameColor.g, frameColor.b, 0.115f);

            Image lowerGlow = EnsureWinTitleDecoration(window, "ResultLowerGlow",
                new Vector2(0f, -222f), new Vector2(530f, 220f));
            lowerGlow.sprite = glowSprite;
            lowerGlow.type = Image.Type.Simple;
            lowerGlow.preserveAspect = false;
            lowerGlow.enabled = glowSprite != null;
            Color purple = Color.Lerp(frameColor, new Color(0.57f, 0.28f, 1f, 1f), 0.62f);
            lowerGlow.color = new Color(purple.r, purple.g, purple.b, 0.075f);

            PremiumResultBackdrop pattern = EnsureResultBackdrop(window, "ResultBackdropPattern");
            RectTransform patternRect = pattern.rectTransform;
            patternRect.anchorMin = Vector2.zero;
            patternRect.anchorMax = Vector2.one;
            patternRect.offsetMin = new Vector2(12f, 12f);
            patternRect.offsetMax = new Vector2(-12f, -12f);
            pattern.SetPalette(frameColor, purple);
            pattern.raycastTarget = false;

            // Always keep decorative light below score text, badges and buttons.
            upperGlow.transform.SetAsFirstSibling();
            lowerGlow.transform.SetSiblingIndex(Mathf.Min(1, window.childCount - 1));
            pattern.transform.SetSiblingIndex(Mathf.Min(2, window.childCount - 1));
        }

        static PremiumResultBackdrop EnsureResultBackdrop(RectTransform parent, string name)
        {
            Transform existing = parent != null ? parent.Find(name) : null;
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(PremiumResultBackdrop));
            if (existing == null) item.transform.SetParent(parent, false);
            PremiumResultBackdrop backdrop = item.GetComponent<PremiumResultBackdrop>();
            if (backdrop == null) backdrop = item.AddComponent<PremiumResultBackdrop>();
            item.SetActive(true);
            return backdrop;
        }

        void ConfigureWinTitleAccents(RectTransform window)
        {
            if (window == null || winTitle == null) return;
            Image glow = EnsureWinTitleDecoration(window, "WinTitleGlow",
                new Vector2(0f, 165f), new Vector2(430f, 92f));
            glow.sprite = glowSprite;
            glow.type = Image.Type.Simple;
            glow.enabled = glowSprite != null;
            glow.color = new Color(0.22f, 0.90f, 1f, 0.12f);

            Image left = EnsureWinTitleDecoration(window, "WinTitleAccentLeft",
                new Vector2(-246f, 165f), new Vector2(104f, 3f));
            Image right = EnsureWinTitleDecoration(window, "WinTitleAccentRight",
                new Vector2(246f, 165f), new Vector2(104f, 3f));
            left.color = right.color = new Color(0.38f, 0.94f, 1f, 0.72f);

            int titleIndex = winTitle.transform.GetSiblingIndex();
            glow.transform.SetSiblingIndex(Mathf.Max(0, titleIndex - 1));
            left.transform.SetSiblingIndex(Mathf.Max(0, winTitle.transform.GetSiblingIndex()));
            right.transform.SetSiblingIndex(Mathf.Max(0, winTitle.transform.GetSiblingIndex()));
            winTitle.transform.SetAsLastSibling();
        }

        static Image EnsureWinTitleDecoration(RectTransform parent, string name,
            Vector2 position, Vector2 size)
        {
            Transform existing = parent.Find(name);
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null) item.transform.SetParent(parent, false);
            RectTransform rect = item.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = item.GetComponent<Image>();
            image.raycastTarget = false;
            item.SetActive(true);
            return image;
        }

        void UpdateWinPlayerBadges()
        {
            if (winPanel == null || lastScoreBreakdown.maxScore <= 0) return;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window == null) return;
            int earned = EarnedWinBadgeCount();
            Color liveBody = new Color(1f, 0.3725f, 0.6275f, 1f);
            Transform bodySource = playerPrefab != null ? playerPrefab.transform.Find("Body") : null;
            SpriteRenderer renderer = bodySource != null
                ? bodySource.GetComponent<SpriteRenderer>() : null;
            if (renderer != null) liveBody = renderer.color;

            for (int i = 0; i < 3; i++)
            {
                Transform badge = window.Find("WinStar" + i);
                if (badge == null) continue;
                bool lit = i < earned;
                Image body = badge.Find("PlayerBody") != null
                    ? badge.Find("PlayerBody").GetComponent<Image>() : null;
                if (body != null)
                    body.color = lit ? liveBody : new Color(0.22f, 0.31f, 0.40f, 0.58f);
                foreach (string eyeName in new[] { "PlayerEyeL", "PlayerEyeR" })
                {
                    Image eye = badge.Find(eyeName) != null
                        ? badge.Find(eyeName).GetComponent<Image>() : null;
                    if (eye != null)
                    {
                        Color eyeColor = eye.color;
                        eyeColor.a = lit ? 1f : 0.24f;
                        eye.color = eyeColor;
                    }
                }
                Transform glow = badge.Find("Glow");
                if (glow != null) glow.gameObject.SetActive(lit);
            }
        }

        int EarnedWinBadgeCount()
        {
            if (lastScoreBreakdown.maxScore <= 0) return 0;
            float ratio = lastScoreBreakdown.runScore / (float)lastScoreBreakdown.maxScore;
            return ratio >= 0.85f ? 3 : ratio >= 0.70f ? 2 : 1;
        }

        // The player badges are the level's rating, so they arrive as a reward rather than simply
        // being visible when the panel opens. Each one lands on its own beat, briefly catches a
        // white highlight and sends a cyan light ring through its pink halo.
        void PrepareWinPlayerBadgeReveal()
        {
            if (winPanel == null) return;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window == null) return;
            int earned = EarnedWinBadgeCount();

            for (int i = 0; i < 3; i++)
            {
                RectTransform badge = window.Find("WinStar" + i) as RectTransform;
                if (badge == null) continue;
                CanvasGroup group = badge.GetComponent<CanvasGroup>();
                if (group == null) group = badge.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
                badge.localScale = Vector3.one * 0.34f;
                badge.localRotation = Quaternion.Euler(0f, 0f, i % 2 == 0 ? -9f : 9f);

                bool lit = i < earned;
                Image glow = FindWinBadgeImage(badge, "Glow");
                if (glow != null)
                {
                    glow.gameObject.SetActive(lit);
                    glow.color = new Color(1f, 0.24f, 0.62f, 0f);
                    glow.rectTransform.localScale = Vector3.one * 0.62f;
                }
                Image ring = FindWinBadgeImage(badge, "PlayerLightRing");
                if (ring != null)
                {
                    ring.gameObject.SetActive(lit && ringSprite != null);
                    ring.color = new Color(0.44f, 0.96f, 1f, 0f);
                    ring.rectTransform.localScale = Vector3.one * 0.54f;
                }
                Image flash = FindWinBadgeImage(badge, "PlayerLightFlash");
                if (flash != null)
                {
                    flash.gameObject.SetActive(lit);
                    flash.color = new Color(1f, 1f, 1f, 0f);
                }
            }
        }

        System.Collections.IEnumerator AnimateWinPlayerBadgeReveal()
        {
            PrepareWinPlayerBadgeReveal();
            if (winPanel == null) yield break;
            RectTransform window = winPanel.transform.Find("Window") as RectTransform;
            if (window == null) yield break;

            int earned = EarnedWinBadgeCount();
            const float stagger = 0.16f;
            const float duration = 0.54f;
            float elapsed = 0f;
            float totalDuration = duration + stagger * 2f;

            while (elapsed < totalDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                for (int i = 0; i < 3; i++)
                {
                    RectTransform badge = window.Find("WinStar" + i) as RectTransform;
                    if (badge == null) continue;
                    float localTime = elapsed - i * stagger;
                    if (localTime < 0f) continue;
                    float p = Mathf.Clamp01(localTime / duration);
                    float settle = WinBadgeEaseOutBack(p);
                    bool lit = i < earned;

                    CanvasGroup group = badge.GetComponent<CanvasGroup>();
                    if (group != null) group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p * 2.6f));
                    badge.localScale = Vector3.one * Mathf.LerpUnclamped(0.34f, 1f, settle);
                    badge.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Lerp((i % 2 == 0 ? -9f : 9f), 0f, Mathf.SmoothStep(0f, 1f, p)));

                    Image glow = FindWinBadgeImage(badge, "Glow");
                    if (glow != null && lit)
                    {
                        float glowPulse = Mathf.Sin(p * Mathf.PI);
                        glow.color = new Color(1f, 0.24f, 0.62f,
                            Mathf.Lerp(0f, 0.46f, p) + glowPulse * 0.34f);
                        glow.rectTransform.localScale = Vector3.one
                            * Mathf.Lerp(0.62f, 1.10f, Mathf.SmoothStep(0f, 1f, p));
                    }

                    Image ring = FindWinBadgeImage(badge, "PlayerLightRing");
                    if (ring != null && lit)
                    {
                        float ringAlpha = Mathf.Sin(p * Mathf.PI) * 0.88f;
                        ring.color = new Color(0.44f, 0.96f, 1f, ringAlpha);
                        ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.54f, 1.42f, p);
                    }

                    Image flash = FindWinBadgeImage(badge, "PlayerLightFlash");
                    if (flash != null && lit)
                    {
                        float flashAlpha = Mathf.Sin(Mathf.Clamp01(p * 1.35f) * Mathf.PI) * 0.70f;
                        flash.color = new Color(1f, 1f, 1f, flashAlpha);
                    }
                }
                yield return null;
            }

            for (int i = 0; i < 3; i++)
            {
                RectTransform badge = window.Find("WinStar" + i) as RectTransform;
                if (badge == null) continue;
                CanvasGroup group = badge.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
                badge.localScale = Vector3.one;
                badge.localRotation = Quaternion.identity;
                bool lit = i < earned;

                Image glow = FindWinBadgeImage(badge, "Glow");
                if (glow != null)
                {
                    glow.gameObject.SetActive(lit);
                    glow.color = new Color(1f, 0.28f, 0.60f, lit ? 0.46f : 0f);
                    glow.rectTransform.localScale = Vector3.one;
                }
                Image ring = FindWinBadgeImage(badge, "PlayerLightRing");
                if (ring != null) ring.gameObject.SetActive(false);
                Image flash = FindWinBadgeImage(badge, "PlayerLightFlash");
                if (flash != null) flash.gameObject.SetActive(false);
            }
        }

        static Image FindWinBadgeImage(RectTransform badge, string childName)
        {
            Transform child = badge != null ? badge.Find(childName) : null;
            return child != null ? child.GetComponent<Image>() : null;
        }

        static float WinBadgeEaseOutBack(float value)
        {
            value = Mathf.Clamp01(value) - 1f;
            const float overshoot = 1.70158f;
            return 1f + (overshoot + 1f) * value * value * value
                + overshoot * value * value;
        }

        void Win()
        {
            won = true;
            if (winAutoAdvanceRoutine != null)
            {
                StopCoroutine(winAutoAdvanceRoutine);
                winAutoAdvanceRoutine = null;
            }
            if (winCountdownRoot != null) winCountdownRoot.gameObject.SetActive(false);
            winControlsReady = false;
            SetWinButtonsInteractable(false);

            int moves = model.MoveCount;
            int prevBest = PlayerPrefs.GetInt(BestKey(levelIndex), int.MaxValue);
            bool newBest = moves < prevBest;
            int best = Mathf.Min(moves, prevBest);
            lastWinMoves = moves;
            lastWinMoveBest = best;
            lastWinWasMoveBest = newBest;
            PlayerPrefs.SetInt(BestKey(levelIndex), best);
            PlayerPrefs.SetInt(SessionClearKey(levelIndex), 1);

            lastScoreBreakdown = ScoreSystem.Calculate(levelIndex,
                MoveParForLevel(levelIndex, par), moves, moveLimit,
                timeLeft, timeLimit, undoCount);
            int runScore = lastScoreBreakdown.runScore;
            ScoreSystem.Award scoreAward = ScoreSystem.RecordBest(
                levelIndex, runScore, levelPrefabs != null ? levelPrefabs.Length : 0);
            lastScoreAward = scoreAward;
            lastScoreGain = scoreAward.gained;
            // Freeze the exact total shown on the results screen. Submission uses this same
            // snapshot, so later PlayerPrefs/cloud activity cannot change one side of the flow.
            endOfRunTotalScore = scoreAward.total;
            PlayerPrefs.SetInt("Parabox.JustBeat", levelIndex);
            PlayerPrefs.Save();
            UpdateScoreHud();
            ReportLevelEndOnce();

            bool last = levelIndex >= levelPrefabs.Length - 1;
            bool allDone = AllLevelsBeaten();

            if (winTitle != null)
                winTitle.text = ScoreSystem.Rating(runScore, lastScoreBreakdown.maxScore);
            UpdateWinPlayerBadges();
            UpdateWinScoreText(0);

            Text nextLabel = nextButton != null
                ? nextButton.GetComponentInChildren<Text>(true) : null;
            if (nextLabel != null)
                nextLabel.text = last ? "PLAY AGAIN" : "NEXT LEVEL";

            // The campaign finale still gets its own full-screen ceremony, but the Level 50 points
            // are shown first so no leaderboard award is ever hidden from the player.
            if (last && allDone)
            {
                finaleSequenceActive = true;
                StartCoroutine(FinaleSequence());
                return;
            }

            StartCoroutine(WinSequence());
        }

        void SetWinButtonsInteractable(bool interactable)
        {
            if (nextButton != null) nextButton.interactable = interactable;
            if (menuButton != null)
            {
                menuButton.interactable = false;
                menuButton.gameObject.SetActive(false);
            }
        }

        void UpdateWinScoreText(int displayedScore)
        {
            if (winStats == null && winScoreValue == null) return;
            int levelCount = levelPrefabs != null ? levelPrefabs.Length : 0;
            int totalMaximum = ScoreSystem.TotalMaximum(levelCount);

            if (winScoreValue != null)
                winScoreValue.text = $"LEVEL SCORE  {displayedScore:n0} / {lastScoreBreakdown.maxScore:n0}";
            if (winStats != null)
                winStats.text = $"TOTAL SCORE  {lastScoreAward.total:n0} / {totalMaximum:n0}";
        }

        System.Collections.IEnumerator RevealScorePanel(bool autoCloseForFinale)
        {
            ConfigureWinScorePresentation();
            if (winCountdownRoot != null) winCountdownRoot.gameObject.SetActive(false);
            SetWinButtonsInteractable(false);
            winControlsReady = false;
            if (winPanel == null)
            {
                winControlsReady = !autoCloseForFinale;
                SetWinButtonsInteractable(winControlsReady);
                yield break;
            }

            winPanel.SetActive(true);
            winPanel.transform.SetAsLastSibling();
            UpdateWinScoreText(0);
            yield return AnimateWinPlayerBadgeReveal();

            const float countDuration = 1.20f;
            float t = 0f;
            int lastShown = -1;
            while (t < countDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / countDuration));
                int shown = Mathf.RoundToInt(lastScoreBreakdown.runScore * k);
                if (shown != lastShown)
                {
                    lastShown = shown;
                    UpdateWinScoreText(shown);
                }
                yield return null;
            }
            UpdateWinScoreText(lastScoreBreakdown.runScore);
            Sfx.Ding();

            if (autoCloseForFinale)
            {
                float hold = 0f;
                while (hold < 2.2f) { hold += Time.unscaledDeltaTime; yield return null; }
                winPanel.SetActive(false);
                yield break;
            }

            winControlsReady = true;
            SetWinButtonsInteractable(true);
            if (nextButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(nextButton.gameObject);
            StartWinAutoAdvance();
        }

        void StartWinAutoAdvance()
        {
            if (!won || !winControlsReady) return;
            if (winAutoAdvanceRoutine != null) StopCoroutine(winAutoAdvanceRoutine);
            winAutoAdvanceRoutine = StartCoroutine(WinAutoAdvanceCountdown());
        }

        System.Collections.IEnumerator WinAutoAdvanceCountdown()
        {
            float remaining = WinAutoAdvanceDuration;
            if (winCountdownRoot != null) winCountdownRoot.gameObject.SetActive(true);
            UpdateWinCountdownBorder(remaining);

            while (remaining > 0f && won && winControlsReady)
            {
                remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
                UpdateWinCountdownBorder(remaining);
                yield return null;
            }

            winAutoAdvanceRoutine = null;
            if (won && winControlsReady) NextLevel();
        }

        void UpdateWinCountdownBorder(float remaining)
        {
            float progress = Mathf.Clamp01(remaining / WinAutoAdvanceDuration);
            Color normal = Color.Lerp(frameColor, Color.white, 0.28f);
            Color warning = new Color(1f, 0.06f, 0.08f, 1f);
            Color colour = remaining <= 3f ? Color.Lerp(warning, Color.white, 0.12f) : normal;
            if (winCountdownBorder != null)
            {
                for (int i = 0; i < winCountdownBorder.Length; i++)
                {
                    Image segment = winCountdownBorder[i];
                    if (segment == null) continue;
                    segment.fillAmount = Mathf.Clamp01(progress * 4f - (3 - i));
                    segment.color = colour;
                }
            }
            if (winCountdownLabel != null)
            {
                int seconds = Mathf.CeilToInt(remaining);
                winCountdownLabel.text = seconds > 0
                    ? $"NEXT LEVEL IN {seconds}" : "STARTING NEXT LEVEL";
                winCountdownLabel.color = remaining <= 3f
                    ? Color.Lerp(warning, Color.white, 0.30f)
                    : Color.Lerp(frameColor, Color.white, 0.45f);
            }
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

            // ---- 4. make the reward unmistakable -----------------------------------------
            // Stay on the completed level and count every earned point before offering the centred
            // Next Level action and its visible border countdown.
            yield return RevealScorePanel(false);
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

            float e = 0f;                       // sit with the solved board for a beat
            while (e < 1.2f) { e += Time.unscaledDeltaTime; yield return null; }

            // Level 50 still awards ordinary level points. Show and count them before the larger
            // all-game finale takes ownership of the screen.
            yield return RevealScorePanel(true);

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
                totalMoves: TotalMovesAcrossRun(),
                totalPoints: ScoreSystem.Total(levelPrefabs.Length),
                maximumPoints: ScoreSystem.TotalMaximum(levelPrefabs.Length));
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
            if (!won || !winControlsReady) return;
            winControlsReady = false;
            SetWinButtonsInteractable(false);
            if (winCountdownRoot != null) winCountdownRoot.gameObject.SetActive(false);
            int next = (levelIndex + 1) % levelPrefabs.Length;
            PlayerPrefs.SetInt(LevelKey, next);
            PlayerPrefs.DeleteKey("Parabox.JustBeat");
            PlayerPrefs.Save();
            LuxoddGameService.SyncProgress();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // -------------------------------------------------- timer
        void ArmGameplayCountdown()
        {
            if (!GameplayCountdownEnabled || editorPreviewMode || Terminal || Tutoring
                || timeLeft <= 0f) return;
            countdownArmed = true;
        }

        void TickTimer()
        {
            if (Terminal || Tutoring || !countdownArmed) return;
            // Gameplay UI and arcade input use real time, so the timer must do the same. Unscaled
            // time keeps it moving if another presentation briefly changes Time.timeScale.
            float delta = Time.unscaledDeltaTime;
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0f) return;
            timeLeft = Mathf.Max(0f, timeLeft - delta);
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
                if (lastSecond >= 0 && !Terminal)
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
        bool danger = secs > 0 && secs <= 5 && !Terminal;
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
            if (Terminal) return;
            if (Tutoring) EndTutorialForTimeout();
            timedUp = true;
            ShowLose("TIME'S UP", "The clock ran out on this one.");
        }

        // A tutorial video can now spend the level allowance. If it reaches zero, tear down the
        // off-screen lesson cleanly before showing the normal loss presentation; otherwise the
        // cinematic canvas would remain above the TIME'S UP screen.
        void EndTutorialForTimeout()
        {
            if (_cine != null)
            {
                StopCoroutine(_cine);
                _cine = null;
            }
            countdownArmed = false;
            tutorialInteractive = false;
            tutorialExiting = false;
            ClearGoalGlow();
            ClearMechanicSpotlights();
            ReleaseRT();
            if (tutorialFx != null) tutorialFx.RevealGameplayImmediately();
            if (cameraFollow != null) cameraFollow.enabled = true;
            SetTutorialTimerPresentation(false);
            SetHud(1f, false);
            cinematic = false;
            tutorialSequence.Clear();
            tutorialSequenceIndex = 0;
            tutorialProgressDeadlineRealtime = -1f;
            tutorialWatchdogRecovering = false;
            if (model != null && model.player != null) FocusRoom(model.player.roomId);
        }

        void OutOfMoves()
        {
            if (Ended) return;
            outOfMoves = true;
            // leads with the counter at zero — the number that just killed you is the first thing to read
            ShowLose("OUT OF MOVES", $"0 / {moveLimit} MOVES LEFT");
        }

        // ============================================ retired chapter-start tutorial system
        //
        // A dedicated chapter cinematic, not an overlay on gameplay. The entire HUD is hidden and
        // one clean mini-puzzle introduces the chapter. It never plays the current board
        // or reads its solution. The end choice always enters the untouched campaign level;
        // players never practise inside the separate teaching board.
        public const string TutorialKey = "Parabox.Tutorial.Seen";
        // Kept for save migration and reset-menu compatibility. Tutorial playback itself is now
        // session-scoped: closing/reopening the website or restarting the Luxodd cabinet game starts
        // a fresh tutorial session, while returning to a level during one launch does not repeat it.
        const string MechanicBriefingPrefix = TutorialVideoVersion.MechanicSeenPrefix;
        // Chapter I now has an explicit pre-Level-1 presentation contract. Do not inherit the old
        // generic TutorialKey: previous builds could set it without showing the current mini-board.
        // This new key makes the corrected tutorial appear once, then suppresses normal replays.
        const string ChapterOneBriefingKey = "Parabox.MechanicBriefing.Chapter1.V14.Seen";
        public static string MechanicBriefingKey(int levelIndex)
        {
            int clamped = Mathf.Clamp(levelIndex, 0, 49);
            return clamped == 0 ? ChapterOneBriefingKey : MechanicBriefingPrefix + clamped;
        }
        static readonly HashSet<int> TutorialsSeenThisPlaySession = new HashSet<int>();
        Coroutine _cine;
        public int TutorialPlaybackSerial { get; private set; }
        GameObject _goalGlow;
        RenderTexture _rt;                      // the board renders into this; the panel shows it
        const int RTW = 1280, RTH = 720;        // 16:9 — matches the panel so the board isn't distorted
        float RTAspect => RTW / (float)RTH;
        LevelModel tutorialModel;
        Transform tutorialBoardRoot;
        readonly Dictionary<int, Transform> tutorialRoomRoots = new Dictionary<int, Transform>();
        readonly Dictionary<PEntity, EntityView> tutorialViews = new Dictionary<PEntity, EntityView>();
        BoardTiles tutorialTiles;
        Blinker tutorialPlayerBlinker;
        readonly List<MechanicCatalog.Id> tutorialSequence = new List<MechanicCatalog.Id>();
        readonly HashSet<string> missingTutorialWarnings = new HashSet<string>();
        int tutorialSequenceIndex;
        bool tutorialInteractive;
        float tutorialProgressDeadlineRealtime = -1f;
        bool tutorialWatchdogRecovering;
        Coroutine tutorialDecisionCountdown;
        float tutorialAutoContinueDeadline = -1f;
        float tutorialAutoContinueDuration;
        const float TutorialProgressTimeout = 20f;
        // Leave enough time for children, older players and slower readers to understand the
        // completed example before the cabinet continues automatically.
        const float TutorialChoiceGrace = 12f;
        const float TutorialStageOffset = 4096f;
        const float TutorialPlaybackRate = 0.45f;

        // The tutorial is a live solver replay, not an encoded movie. Expanding every beat by the
        // inverse rate gives a calm, readable presentation while gameplay and UI input remain at 1x.
        static float TutorialDuration(float seconds)
            => seconds / TutorialPlaybackRate;

        List<MechanicCatalog.Id> CurrentTutorialMechanics()
        {
            var lessons = new List<MechanicCatalog.Id>();
            if (TutorialAlreadySeenForCurrentLevel()) return lessons;

            // Chapter tutorials are fixed gates before their opener puzzle. Focused mechanic
            // tutorials still require genuine first-appearance evidence so ordinary levels and
            // reloads go straight to gameplay.
            var introductions = new HashSet<MechanicCatalog.Id>(
                MechanicCatalog.IntroductionsAt(levelPrefabs, levelIndex));
            foreach (MechanicCatalog.Id lesson in
                     MechanicCatalog.TutorialsAt(levelPrefabs, levelIndex))
            {
                if (MechanicCatalog.IsChapterTutorial(levelIndex, lesson)
                    || introductions.Contains(lesson))
                    lessons.Add(lesson);
            }
            return lessons;
        }

        bool TutorialAlreadySeenForCurrentLevel()
            => TutorialsSeenThisPlaySession.Contains(levelIndex);

        void MarkCurrentTutorialSeen()
            => TutorialsSeenThisPlaySession.Add(levelIndex);

        // Purple is the Luxodd cabinet's dedicated tutorial action. It must remain available for
        // chapter walkthroughs and first-appearance mechanic lessons alike.
        bool CanSkipCurrentTutorial() => true;

        // Tutorials run once, only where the current level genuinely introduces their mechanic.
        bool WillTutorial()
        {
            // A first-time Level 1 tutorial is a hard entry gate, including direct Editor preview
            // and a reload carrying restart suppression. Those shortcuts may skip ordinary repeat
            // presentations, but they must never make the player's first tutorial unreachable.
            bool chapterOneTutorialPending = levelIndex == 0
                && !TutorialAlreadySeenForCurrentLevel();
            if (editorPreviewMode && !chapterOneTutorialPending) return false;
            if (suppressTutorialForThisLoad && !chapterOneTutorialPending) return false;
            if (tutorialFx == null || tutorialFx.videoImage == null || tutorialBgCamera == null)
            {
                Debug.LogError("[Parabox] Real tutorial video references are missing from Game.unity.");
                return false;
            }

            List<MechanicCatalog.Id> lessons = CurrentTutorialMechanics();
            if (lessons.Count == 0 || string.IsNullOrWhiteSpace(MechanicCatalog.Lesson(lessons)))
                return false;
            foreach (MechanicCatalog.Id lesson in lessons)
            {
                if (TutorialPuzzleLibrary.Exists(levelIndex, lesson)) continue;
                string path = TutorialPuzzleLibrary.ResourcePath(levelIndex, lesson);
                if (missingTutorialWarnings.Add(path))
                    Debug.LogWarning($"[Parabox] Tutorial asset is not ready yet: {path}. "
                        + "Gameplay continues; the Edit-Mode repair will create it after Play Mode exits.");
                return false;
            }
            return true;
        }

        void MaybeTutorial()
        {
            if (!WillTutorial()) return;
            _cine = StartCoroutine(TutorialCinematic(true));
            // Do not mark this as seen merely because a coroutine was requested. Scene reloads or
            // an initialization failure could otherwise suppress a tutorial the player never saw.
            // CompleteTutorialExit records it only after the presentation has visibly finished.
        }

        void MarkTutorialProgress()
        {
            tutorialProgressDeadlineRealtime = Time.realtimeSinceStartup + TutorialProgressTimeout;
        }

        bool TutorialChoicesReady()
            => tutorialFx != null
               && tutorialFx.choiceGroup != null
               && tutorialFx.choiceGroup.interactable
               && tutorialFx.tryButton != null
               && tutorialFx.tryButton.gameObject.activeInHierarchy
               && tutorialFx.tryButton.interactable;

        void StartTutorialDecisionCountdown()
        {
            if (!TutorialChoicesReady()) return;
            // Give the player a complete twelve-second choice window only after REPEAT, SKIP and
            // TRY/NEXT are all visible and usable. This is intentionally generous for slower readers.
            StartTutorialSessionCountdown(TutorialChoiceGrace);
        }

        void StartTutorialSessionCountdown(float duration)
        {
            StopTutorialDecisionCountdown();
            tutorialAutoContinueDuration = Mathf.Max(TutorialChoiceGrace, duration);
            tutorialAutoContinueDeadline = Time.realtimeSinceStartup + tutorialAutoContinueDuration;
            tutorialDecisionCountdown = StartCoroutine(TutorialDecisionCountdown());
        }

        void StopTutorialDecisionCountdown()
        {
            if (tutorialDecisionCountdown != null)
            {
                StopCoroutine(tutorialDecisionCountdown);
                tutorialDecisionCountdown = null;
            }
            tutorialAutoContinueDeadline = -1f;
            tutorialAutoContinueDuration = 0f;
            if (tutorialFx != null)
            {
                tutorialFx.SetSkipCountdown(false, 0);
                tutorialFx.SetTutorialCountdown(false, 0f, 1f);
            }
        }

        System.Collections.IEnumerator TutorialDecisionCountdown()
        {
            while (Tutoring && !tutorialExiting)
            {
                float remaining = Mathf.Max(0f,
                    tutorialAutoContinueDeadline - Time.realtimeSinceStartup);
                if (tutorialFx != null)
                    tutorialFx.SetTutorialCountdown(true, remaining,
                        Mathf.Max(0.01f, tutorialAutoContinueDuration),
                        TutorialChoicesReady());
                // If an unexpectedly slow frame reaches zero before the replay finishes, hold at
                // zero until all three tutorial controls are ready instead of interrupting it.
                if (remaining <= 0f && TutorialChoicesReady()) break;
                yield return null;
            }

            bool shouldContinue = Tutoring && !tutorialExiting && TutorialChoicesReady();
            tutorialDecisionCountdown = null;
            tutorialAutoContinueDeadline = -1f;
            tutorialAutoContinueDuration = 0f;
            if (tutorialFx != null)
                tutorialFx.SetTutorialCountdown(false, 0f, 1f);
            if (shouldContinue) TutorialTryIt();
        }

        void TickTutorialProgressWatchdog()
        {
            if (!Tutoring || tutorialExiting || tutorialInteractive || tutorialWatchdogRecovering)
                return;
            if (TutorialChoicesReady()) return; // deliberately waiting for the player's decision
            if (tutorialProgressDeadlineRealtime < 0f)
            {
                MarkTutorialProgress();
                return;
            }
            if (Time.realtimeSinceStartup < tutorialProgressDeadlineRealtime) return;

            // A thrown coroutine exception, invalid demo route or missing transition must never
            // leave the opaque tutorial layer owning the game forever. Fail open to the untouched
            // campaign puzzle after a generous no-progress window.
            tutorialWatchdogRecovering = true;
            tutorialExiting = true;
            if (_cine != null)
            {
                StopCoroutine(_cine);
                _cine = null;
            }
            Debug.LogError("[Parabox] Tutorial stopped making progress; recovering to gameplay.");
            _cine = StartCoroutine(RecoverStalledTutorial());
        }

        System.Collections.IEnumerator RecoverStalledTutorial()
        {
            countdownArmed = false;
            tutorialInteractive = false;
            StopTutorialDecisionCountdown();
            if (tutorialFx != null)
            {
                tutorialFx.HideChoice();
                tutorialFx.HideSkip();
            }
            yield return TutorialExitToPlay();
            tutorialWatchdogRecovering = false;
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

        // The master timeline. A solver-proven mini-game runs only for a first-time mechanic.
        // Tutorials use separate models and render off-screen, so the campaign puzzle stays untouched.
        System.Collections.IEnumerator TutorialCinematic(bool resetSequence)
        {
            StopTutorialDecisionCountdown();
            TutorialPlaybackSerial++;
            cinematic = true;
            countdownArmed = false;
            tutorialInteractive = false;
            tutorialWatchdogRecovering = false;
            MarkTutorialProgress();
            if (resetSequence || tutorialSequence.Count == 0)
            {
                tutorialSequence.Clear();
                tutorialSequence.AddRange(CurrentTutorialMechanics());
                if (tutorialSequence.Count == 0)
                {
                    countdownArmed = false;
                    cinematic = false;
                    tutorialProgressDeadlineRealtime = -1f;
                    _cine = null;
                    yield break;
                }
                tutorialSequenceIndex = 0;
            }
            tutorialSequenceIndex = Mathf.Clamp(tutorialSequenceIndex, 0,
                Mathf.Max(0, tutorialSequence.Count - 1));
            if (tutorialFx != null)
                tutorialFx.SetPrimaryChoiceLabel(
                    tutorialSequenceIndex + 1 < tutorialSequence.Count);
            CleanupTutorialPuzzle();
            if (tutorialFx != null)
            {
                // Repeat can re-enter this timeline while the end-choice panel is still visible.
                // Reset every overlay synchronously before the first yielded frame so the replay
                // unmistakably starts as a video instead of appearing to ignore the button.
                tutorialFx.HideChoice();
                tutorialFx.HideCaptionImmediately();
                tutorialFx.HideMechanicDemoImmediately();
                tutorialFx.SetTitle("TUTORIAL");
                tutorialFx.HideMechanicBriefingImmediately();
                tutorialFx.SetNestedDoorLegendVisible(false);
                tutorialFx.ShowSkip();
            }
            if (tutorialFx == null || tutorialFx.videoImage == null || tutorialBgCamera == null)
            {
                countdownArmed = false;
                SetTutorialTimerPresentation(false);
                cinematic = false;
                if (tutorialFx != null) tutorialFx.HideSkip();
                Debug.LogError("[Parabox] The prebuilt real tutorial video is missing. "
                    + "Run Tools/Parabox/Generate Prebuilt UI (Run This).");
                yield break;
            }

            // Dedicate the display to the lesson while leaving the gameplay model untouched.
            if (hudReveal != null) hudReveal.StandDown();
            SetTutorialTimerPresentation(false);
            SetHud(0f, false);
            if (cameraFollow != null) cameraFollow.enabled = false;

            tutorialFx.CoverInstant();
            tutorialFx.SetVideo(null);
            StartTutorialCountdownFor(tutorialSequence[tutorialSequenceIndex]);
            tutorialFx.PanelIn(TutorialDuration(0.5f));
            yield return WaitU(TutorialDuration(0.6f));
            MarkTutorialProgress();

            yield return RunTutorialPuzzle(tutorialSequence[tutorialSequenceIndex]);
            MarkTutorialProgress();
            // Tutorial playback is outside gameplay, so the level clock remains paused here.
            countdownArmed = false;
            yield return tutorialFx.ShowChoice();
            if (!TutorialChoicesReady())
            {
                Debug.LogError("[Parabox] Tutorial choice controls were not ready; recovering to gameplay.");
                tutorialExiting = true;
                yield return TutorialExitToPlay();
                yield break;
            }
            tutorialProgressDeadlineRealtime = -1f;
            _cine = null;
            StartTutorialDecisionCountdown();
        }

        System.Collections.IEnumerator RunTutorialPuzzle(MechanicCatalog.Id mechanic)
        {
            GameObject prefab = TutorialPuzzleLibrary.Load(levelIndex, mechanic);
            if (prefab == null)
            {
                Debug.LogError($"[Parabox] Cannot play missing tutorial puzzle "
                    + TutorialPuzzleLibrary.ResourcePath(levelIndex, mechanic));
                yield break;
            }

            // Chapter 2's fixed room-doors skip the essential push-versus-enter rule. Show the
            // start of the existing, independent docking mini-board first, ending as soon as the
            // player enters. Neither tutorial prefab nor the campaign board is changed.
            if (NeedsParaBoxEntryPrimer(levelIndex, mechanic)
                && TryLoadParaBoxEntryPrimer(out GameObject entryPrefab, out string entryRoute))
            {
                yield return ReplayTutorialPuzzle(entryPrefab, mechanic, entryRoute, true);
                if (!cinematic || tutorialExiting) yield break;
            }

            ParaboxLevel info = prefab.GetComponent<ParaboxLevel>();
            string route = info != null ? info.solution : string.Empty;
            yield return ReplayTutorialPuzzle(prefab, mechanic, route, false);
        }

        System.Collections.IEnumerator ReplayTutorialPuzzle(GameObject prefab,
            MechanicCatalog.Id mechanic, string route, bool entryPrimer)
        {
            if (string.IsNullOrEmpty(route))
            {
                Debug.LogError($"[Parabox] Tutorial {prefab.name} has no solver-validated route.");
                yield break;
            }
            tutorialFx.SetTitle(entryPrimer ? "HOW TO ENTER A PARA BOX"
                : MechanicCatalog.TutorialTitle(levelIndex, mechanic));

            CleanupTutorialPuzzle();
            tutorialModel = LevelParser.Parse(prefab);
            tutorialTiles = new BoardTiles();
            BoardAssets tutorialAssets = BuildAssets();
            tutorialAssets.simplifyBoundaryContours = true;
            tutorialBoardRoot = BoardRenderer.Render(tutorialModel, tutorialAssets,
                tutorialRoomRoots, tutorialViews, tutorialTiles);
            tutorialBoardRoot.name = "TutorialMiniPuzzle";
            tutorialBoardRoot.position = new Vector3(TutorialStageOffset, TutorialStageOffset, 0f);
            AttachTutorialNestedBoxGuidance();
            tutorialPlayerBlinker = tutorialViews.TryGetValue(tutorialModel.player, out var playerView)
                && playerView != null
                ? playerView.GetComponent<Blinker>()
                : null;

            EnsureRT();
            var gameplayCamera = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            tutorialBgCamera.orthographic = true;
            tutorialBgCamera.clearFlags = CameraClearFlags.SolidColor;
            tutorialBgCamera.backgroundColor = new Color(0.005f, 0.015f, 0.055f, 1f);
            tutorialBgCamera.cullingMask = gameplayCamera != null ? gameplayCamera.cullingMask : ~0;
            tutorialBgCamera.targetTexture = _rt;
            tutorialBgCamera.enabled = true;
            tutorialFx.SetVideo(_rt);
            // The entry primer shows only one numbered instruction at a time.
            tutorialFx.SetNestedDoorLegendVisible(levelIndex >= 10 && !entryPrimer);
            bool chapterFiveWalkthrough = !entryPrimer && levelIndex == 40
                && mechanic == MechanicCatalog.Id.ColourCargo;
            string bundleLesson = MechanicCatalog.TutorialBundleLesson(levelIndex, mechanic);
            if (entryPrimer)
                ShowParaBoxEntryStep(1, TutorialDirection(route[0]));
            else if (chapterFiveWalkthrough)
                ShowChapterFiveTutorialStep(1);
            else
                tutorialFx.ShowCaptionPersistent(!string.IsNullOrEmpty(bundleLesson)
                    ? bundleLesson
                    : $"{MechanicCatalog.DisplayName(mechanic).ToUpperInvariant()}  •  {MechanicCatalog.Lesson(mechanic)}");

            SyncTutorialViews(true);
            SetTutorialCameraRoom(tutorialModel.player.roomId, true);
            yield return WaitU(TutorialDuration(entryPrimer ? 1.6f : 0.7f));

            var before = new Dictionary<PEntity, (int room, Vector2Int pos)>();
            int finaleLessonStep = chapterFiveWalkthrough ? 1 : 0;
            int finaleCargoBoundaryCrossings = 0;
            foreach (char command in route)
            {
                if (!cinematic || tutorialExiting || tutorialModel == null) yield break;

                before.Clear();
                foreach (PEntity entity in tutorialModel.entities)
                    before[entity] = (entity.roomId, entity.pos);

                int roomBefore = tutorialModel.player.roomId;
                bool gateWasOpen = chapterFiveWalkthrough && tutorialModel.GatesOpen();
                Vector2Int direction = TutorialDirection(command);
                if (direction == Vector2Int.zero || !tutorialModel.TryMovePlayer(direction))
                {
                    Debug.LogError($"[Parabox] Tutorial {prefab.name} route blocked at '{command}'.");
                    yield break;
                }
                MarkTutorialProgress();

                SyncTutorialViews(false, direction, before);
                Vector2 squashDirection = new Vector2(direction.x, direction.y);
                foreach (PEntity entity in tutorialModel.entities)
                {
                    if (!before.TryGetValue(entity, out var start)
                        || start.room != entity.roomId || start.pos != entity.pos)
                        tutorialViews[entity].Squash(squashDirection);
                    if (chapterFiveWalkthrough && entity.IsCrate && entity.interiorRoomId < 0
                        && entity.colour > 0 && before.TryGetValue(entity, out start)
                        && start.room != entity.roomId)
                        finaleCargoBoundaryCrossings++;
                }
                if (tutorialPlayerBlinker != null)
                    tutorialPlayerBlinker.BlinkOnSuccessfulMove(tutorialModel.MoveCount);

                int entryStep = entryPrimer
                    ? ParaBoxEntryStepAfterMove(tutorialModel, direction, roomBefore) : 0;
                if (entryPrimer) ShowParaBoxEntryStep(entryStep, direction);

                if (chapterFiveWalkthrough)
                {
                    int nextStep = finaleLessonStep;
                    if (tutorialModel.player.roomId == 1) nextStep = Mathf.Max(nextStep, 2);
                    if (tutorialModel.player.roomId == 2) nextStep = Mathf.Max(nextStep, 3);
                    if (finaleCargoBoundaryCrossings >= 2) nextStep = Mathf.Max(nextStep, 4);
                    if (!gateWasOpen && tutorialModel.GatesOpen()) nextStep = 5;
                    if (nextStep > finaleLessonStep)
                    {
                        finaleLessonStep = nextStep;
                        ShowChapterFiveTutorialStep(finaleLessonStep);
                    }
                }

                if (roomBefore != tutorialModel.player.roomId)
                    yield return MoveTutorialCameraToRoom(tutorialModel.player.roomId,
                        TutorialDuration(0.42f));
                else if (!entryPrimer)
                    yield return WaitU(TutorialDuration(0.34f));

                // Deliberately hold at wall contact, BEFORE the next identical input enters.
                // The player can read the caption and see that the box has stopped moving.
                if (entryPrimer && entryStep < 3)
                    yield return WaitU(TutorialDuration(ParaBoxEntryMoveHold(entryStep)));
            }

            if (!entryPrimer && !tutorialModel.IsWon())
            {
                Debug.LogError($"[Parabox] Tutorial {prefab.name} finished its route without winning.");
                yield break;
            }
            MarkTutorialProgress();
            yield return WaitU(TutorialDuration(entryPrimer ? 2f : 0.9f));
        }

        static bool NeedsParaBoxEntryPrimer(int levelIdx, MechanicCatalog.Id mechanic)
            => levelIdx == 10 && mechanic == MechanicCatalog.Id.NestedBoard;

        static bool TryLoadParaBoxEntryPrimer(out GameObject prefab, out string route)
        {
            prefab = Resources.Load<GameObject>("Parabox/Tutorials/Chapter_4");
            ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
            route = info != null
                ? FindParaBoxEntryRoute(LevelParser.Parse(prefab), info.solution) : string.Empty;
            return !string.IsNullOrEmpty(route);
        }

        // Derive the short prefix from the real rules, not from a hard-coded frame or move count.
        // It must show this same movable box being pushed, stopped by a wall, and then entered.
        static string FindParaBoxEntryRoute(LevelModel demo, string route)
        {
            if (demo == null || demo.player == null || string.IsNullOrEmpty(route))
                return string.Empty;
            PEntity pushedBox = null;
            for (int i = 0; i < route.Length; i++)
            {
                int roomBefore = demo.player.roomId;
                Vector2Int direction = TutorialDirection(route[i]);
                PEntity box = demo.EntityAt(roomBefore, demo.player.pos + direction);
                Vector2Int boxBefore = box != null ? box.pos : Vector2Int.zero;
                bool againstWall = ParaBoxAgainstWall(demo, box, direction);
                if (direction == Vector2Int.zero || !demo.TryMovePlayer(direction))
                    return string.Empty;
                if (box != null && box.interiorRoomId >= 0 && !box.anchored
                    && box.roomId == roomBefore && box.pos != boxBefore)
                    pushedBox = box;
                if (demo.player.roomId != roomBefore)
                    return box != null && box == pushedBox && againstWall
                        && demo.player.roomId == box.interiorRoomId
                        ? route.Substring(0, i + 1) : string.Empty;
            }
            return string.Empty;
        }

        static bool ParaBoxAgainstWall(LevelModel demo, PEntity box, Vector2Int direction)
        {
            if (demo == null || box == null || box.interiorRoomId < 0 || box.anchored
                || direction == Vector2Int.zero
                || !demo.rooms.TryGetValue(box.roomId, out PRoom room)) return false;
            Vector2Int stop = box.pos + direction;
            return room.InBounds(stop) && room.IsWall(stop);
        }

        static int ParaBoxEntryStepAfterMove(LevelModel demo, Vector2Int direction, int roomBefore)
        {
            if (demo.player.roomId != roomBefore) return 3;
            PEntity box = demo.EntityAt(demo.player.roomId, demo.player.pos + direction);
            return ParaBoxAgainstWall(demo, box, direction) ? 2 : 1;
        }

        static float ParaBoxEntryMoveHold(int step) => step == 2 ? 1.8f : 0.6f;

        void ShowParaBoxEntryStep(int step, Vector2Int direction)
        {
            if (tutorialFx == null) return;
            string arrow = direction == Vector2Int.left ? "LEFT"
                : direction == Vector2Int.up ? "UP"
                : direction == Vector2Int.down ? "DOWN" : "RIGHT";
            tutorialFx.ShowCaptionPersistent(step == 1
                ? $"1 / 3  •  PUSH BOX {arrow} TO THE WALL"
                : step == 2
                    ? $"2 / 3  •  PRESS {arrow} AGAIN TO ENTER"
                    : "3 / 3  •  INSIDE! CYAN GAP = DOORWAY");
        }

        float EstimateTutorialReplaySeconds(GameObject prefab, string route, bool entryPrimer = false)
        {
            // Includes the opening hold, every solver move, the solved-board hold and the final
            // button entrance. Parsing a separate model keeps the visible tutorial untouched.
            float authoredSeconds = entryPrimer ? 1.6f + 2f : 0.7f + 0.9f;
            LevelModel estimate = prefab != null ? LevelParser.Parse(prefab) : null;
            if (estimate == null || estimate.player == null)
                return TutorialDuration(authoredSeconds + Mathf.Max(0, route.Length) * 0.42f) + 0.5f;

            foreach (char command in route)
            {
                int roomBefore = estimate.player.roomId;
                Vector2Int direction = TutorialDirection(command);
                bool moved = direction != Vector2Int.zero && estimate.TryMovePlayer(direction);
                if (entryPrimer)
                {
                    int step = ParaBoxEntryStepAfterMove(estimate, direction, roomBefore);
                    authoredSeconds += moved && step == 3 ? 0.42f : ParaBoxEntryMoveHold(step);
                }
                else
                    authoredSeconds += moved && estimate.player.roomId != roomBefore ? 0.42f : 0.34f;
            }
            // Only the complete lesson has an end-choice animation; the primer flows straight
            // into the existing Chapter 2 board without adding a second countdown or button row.
            return TutorialDuration(authoredSeconds) + (entryPrimer ? 0f : 0.5f);
        }

        void StartTutorialCountdownFor(MechanicCatalog.Id mechanic)
        {
            GameObject prefab = TutorialPuzzleLibrary.Load(levelIndex, mechanic);
            ParaboxLevel info = prefab != null ? prefab.GetComponent<ParaboxLevel>() : null;
            string route = info != null ? info.solution : string.Empty;
            if (prefab == null || string.IsNullOrEmpty(route))
            {
                StartTutorialSessionCountdown(TutorialChoiceGrace);
                return;
            }

            float fullPresentation = TutorialDuration(0.6f)
                + EstimateTutorialReplaySeconds(prefab, route)
                + TutorialChoiceGrace;
            if (NeedsParaBoxEntryPrimer(levelIndex, mechanic)
                && TryLoadParaBoxEntryPrimer(out GameObject entryPrefab, out string entryRoute))
                fullPresentation += EstimateTutorialReplaySeconds(entryPrefab, entryRoute, true);
            StartTutorialSessionCountdown(fullPresentation);
        }

        void ShowChapterFiveTutorialStep(int step)
        {
            if (tutorialFx == null) return;
            switch (Mathf.Clamp(step, 1, 5))
            {
                case 1:
                    tutorialFx.ShowCaptionPersistent("1 / 5  •  ENTER THE BLUE ROOM");
                    break;
                case 2:
                    tutorialFx.ShowCaptionPersistent("2 / 5  •  ENTER THE INNER TEAL ROOM");
                    break;
                case 3:
                    tutorialFx.ShowCaptionPersistent("3 / 5  •  PUSH CORAL CARGO OUT THROUGH BOTH ROOMS");
                    break;
                case 4:
                    tutorialFx.ShowCaptionPersistent("4 / 5  •  PARK CORAL ON THE MATCHING BUTTON TARGET");
                    break;
                default:
                    tutorialFx.ShowCaptionPersistent("5 / 5  •  CROSS THE GATE AND FOLLOW THE ONE-WAY");
                    break;
            }
        }

        // Retained for editor-authored tutorial diagnostics. The player-facing TRY IT YOURSELF
        // action never calls this method: it starts the real campaign level behind the video.
        void BeginInteractiveTutorial()
        {
            if (tutorialSequenceIndex < 0 || tutorialSequenceIndex >= tutorialSequence.Count)
                return;
            if (_cine != null)
            {
                StopCoroutine(_cine);
                _cine = null;
            }

            MechanicCatalog.Id mechanic = tutorialSequence[tutorialSequenceIndex];
            GameObject prefab = TutorialPuzzleLibrary.Load(levelIndex, mechanic);
            if (prefab == null)
            {
                Debug.LogError($"[Parabox] Cannot practice missing tutorial puzzle "
                    + TutorialPuzzleLibrary.ResourcePath(levelIndex, mechanic));
                return;
            }

            tutorialExiting = false;
            tutorialInteractive = true;
            if (tutorialFx != null)
            {
                tutorialFx.HideChoice();
                tutorialFx.SetTitle(MechanicCatalog.TutorialTitle(levelIndex, mechanic));
            }
            CleanupTutorialPuzzle();
            tutorialModel = LevelParser.Parse(prefab);
            tutorialTiles = new BoardTiles();
            BoardAssets tutorialAssets = BuildAssets();
            tutorialAssets.simplifyBoundaryContours = true;
            tutorialBoardRoot = BoardRenderer.Render(tutorialModel, tutorialAssets,
                tutorialRoomRoots, tutorialViews, tutorialTiles);
            tutorialBoardRoot.name = "TutorialPracticePuzzle";
            tutorialBoardRoot.position = new Vector3(TutorialStageOffset, TutorialStageOffset, 0f);
            AttachTutorialNestedBoxGuidance();
            tutorialPlayerBlinker = tutorialViews.TryGetValue(tutorialModel.player, out var playerView)
                && playerView != null
                ? playerView.GetComponent<Blinker>()
                : null;

            EnsureRT();
            var gameplayCamera = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            tutorialBgCamera.orthographic = true;
            tutorialBgCamera.clearFlags = CameraClearFlags.SolidColor;
            tutorialBgCamera.backgroundColor = new Color(0.005f, 0.015f, 0.055f, 1f);
            tutorialBgCamera.cullingMask = gameplayCamera != null ? gameplayCamera.cullingMask : ~0;
            tutorialBgCamera.targetTexture = _rt;
            tutorialBgCamera.enabled = true;
            tutorialFx.SetVideo(_rt);
            tutorialFx.SetNestedDoorLegendVisible(levelIndex >= 10);
            tutorialFx.ShowCaptionPersistent("YOUR TURN  •  "
                + MechanicCatalog.TutorialBundleLesson(levelIndex, mechanic));
            SyncTutorialViews(true);
            SetTutorialCameraRoom(tutorialModel.player.roomId, true);
        }

        void MoveInteractiveTutorial(Vector2Int direction)
        {
            if (!tutorialInteractive || tutorialModel == null || direction == Vector2Int.zero)
                return;
            if (lastTutorialMoveFrame == Time.frameCount) return;
            lastTutorialMoveFrame = Time.frameCount;

            var before = new Dictionary<PEntity, (int room, Vector2Int pos)>();
            foreach (PEntity entity in tutorialModel.entities)
                before[entity] = (entity.roomId, entity.pos);
            int roomBefore = tutorialModel.player.roomId;
            if (!tutorialModel.TryMovePlayer(direction))
            {
                Sfx.Blocked();
                return;
            }

            Sfx.Move();
            SyncTutorialViews(false, direction, before);
            Vector2 squashDirection = new Vector2(direction.x, direction.y);
            foreach (PEntity entity in tutorialModel.entities)
            {
                if (!before.TryGetValue(entity, out var start)
                    || start.room != entity.roomId || start.pos != entity.pos)
                    tutorialViews[entity].Squash(squashDirection);
            }
            if (tutorialPlayerBlinker != null)
                tutorialPlayerBlinker.BlinkOnSuccessfulMove(tutorialModel.MoveCount);
            if (roomBefore != tutorialModel.player.roomId)
                SetTutorialCameraRoom(tutorialModel.player.roomId, true);

            if (!tutorialModel.IsWon()) return;
            tutorialInteractive = false;
            Sfx.Win();
            _cine = StartCoroutine(CompleteInteractiveTutorial());
        }

        void UndoInteractiveTutorial()
        {
            if (!tutorialInteractive || tutorialModel == null || !tutorialModel.Undo()) return;
            Sfx.Undo();
            if (tutorialPlayerBlinker != null) tutorialPlayerBlinker.ResetOpen();
            SyncTutorialViews(true);
            SetTutorialCameraRoom(tutorialModel.player.roomId, true);
        }

        System.Collections.IEnumerator CompleteInteractiveTutorial()
        {
            yield return WaitU(TutorialDuration(0.55f));
            tutorialSequenceIndex++;
            if (tutorialSequenceIndex < tutorialSequence.Count)
            {
                _cine = StartCoroutine(TutorialCinematic(false));
                yield break;
            }
            tutorialExiting = true;
            yield return TutorialExitToPlay();
        }

        static Vector2Int TutorialDirection(char command)
        {
            switch (command)
            {
                case 'U': return Vector2Int.up;
                case 'D': return Vector2Int.down;
                case 'L': return Vector2Int.left;
                case 'R': return Vector2Int.right;
                default: return Vector2Int.zero;
            }
        }

        void SyncTutorialViews(bool instant, Vector2Int portalDirection = default,
                               Dictionary<PEntity, (int room, Vector2Int pos)> before = null)
        {
            if (tutorialModel == null) return;
            foreach (PEntity entity in tutorialModel.entities)
            {
                if (!tutorialViews.TryGetValue(entity, out var view) || view == null) continue;
                if (entity.sunk)
                {
                    if (view.gameObject.activeSelf)
                    {
                        PRoom sinkRoom = tutorialModel.rooms[entity.roomId];
                        view.SetTarget(tutorialRoomRoots[entity.roomId], Cell(sinkRoom, entity.pos), instant);
                        if (instant) view.gameObject.SetActive(false);
                        else view.Sink();
                    }
                    continue;
                }
                if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
                view.Unsink();
                PRoom room = tutorialModel.rooms[entity.roomId];
                Transform targetParent = tutorialRoomRoots[entity.roomId];
                bool crossedRoomBoundary = before != null
                    && before.TryGetValue(entity, out var start) && start.room != entity.roomId;
                if ((ReferenceEquals(entity, tutorialModel.player) || crossedRoomBoundary)
                    && view.transform.parent != targetParent
                    && portalDirection != Vector2Int.zero)
                    view.SetPortalTarget(targetParent, Cell(room, entity.pos), portalDirection, instant);
                else
                    view.SetTarget(targetParent, Cell(room, entity.pos), instant);
            }

            // Keep the Chapter V walkthrough visually identical to the real game: when cargo
            // exits while the player is still inside, reveal the parent room that received it.
            if (levelIndex >= 40 && before != null
                && before.TryGetValue(tutorialModel.player, out var tutorialPlayerStart)
                && tutorialPlayerStart.room == tutorialModel.player.roomId)
            {
                foreach (PEntity entity in tutorialModel.entities)
                {
                    if (entity == null || entity.sunk || !entity.IsCrate
                        || entity.interiorRoomId >= 0
                        || !before.TryGetValue(entity, out var entityStart)
                        || entityStart.room == entity.roomId
                        || entityStart.room != tutorialPlayerStart.room
                        || !tutorialModel.rooms.TryGetValue(entityStart.room, out PRoom sourceRoom)
                        || sourceRoom.containerBox == null
                        || sourceRoom.containerBox.roomId != entity.roomId)
                        continue;

                    SetTutorialCameraRoom(entity.roomId, instant);
                    break;
                }
            }

            if (tutorialTiles == null) return;
            foreach (var kv in tutorialTiles.pits)
            {
                bool filled = tutorialModel.rooms.TryGetValue(kv.Key.Item1, out var room)
                    && room.filled.Contains(kv.Key.Item2);
                Show(kv.Value, !filled);
            }
            foreach (var kv in tutorialTiles.coral)
            {
                bool gone = tutorialModel.rooms.TryGetValue(kv.Key.Item1, out var room)
                    && room.IsBroken(kv.Key.Item2);
                Show(kv.Value, !gone);
                if (tutorialTiles.rubble.TryGetValue(kv.Key, out var hole)) Show(hole, gone);
            }
            bool gatesOpen = tutorialModel.GatesOpen();
            bool heavyGatesOpen = tutorialModel.HeavyGatesOpen();
            foreach (var kv in tutorialTiles.gates) Show(kv.Value, !gatesOpen);
            foreach (var kv in tutorialTiles.heavyGates) Show(kv.Value, !heavyGatesOpen);
            foreach (var kv in tutorialTiles.rocks)
            {
                bool gone = tutorialModel.rooms.TryGetValue(kv.Key.Item1, out var room)
                    && room.smashed.Contains(kv.Key.Item2);
                Show(kv.Value, !gone);
            }
            foreach (var kv in tutorialTiles.toggleOn) Show(kv.Value, tutorialModel.latched);
            foreach (var kv in tutorialTiles.latches) Show(kv.Value, !tutorialModel.latched);
            foreach (var kv in tutorialTiles.pulses) Show(kv.Value, tutorialModel.beat == 0);
            foreach (var kv in tutorialTiles.pearls)
                Show(kv.Value, !tutorialModel.collected.Contains(kv.Key));
            bool locksOpen = tutorialModel.LocksOpen();
            foreach (var kv in tutorialTiles.locks) Show(kv.Value, !locksOpen);
        }

        bool TutorialCameraPose(int roomId, out Vector3 position, out float size)
        {
            position = Vector3.zero;
            size = 1f;
            if (tutorialModel == null || !tutorialModel.rooms.TryGetValue(roomId, out var room))
                return false;
            if (!tutorialRoomRoots.TryGetValue(roomId, out var root) || root == null)
                return false;
            float padding = roomId == 0 ? 1.20f : 1.34f;
            CameraFraming.Compute(root.position, root.lossyScale.x, room.width, room.height,
                RTAspect, padding, 0.08f, 0.08f, out position, out size);
            return true;
        }

        void SetTutorialCameraRoom(int roomId, bool instant)
        {
            if (tutorialBgCamera == null
                || !TutorialCameraPose(roomId, out var position, out var size)) return;
            if (instant)
            {
                tutorialBgCamera.transform.position = position;
                tutorialBgCamera.orthographicSize = size;
            }
        }

        System.Collections.IEnumerator MoveTutorialCameraToRoom(int roomId, float duration)
        {
            if (tutorialBgCamera == null
                || !TutorialCameraPose(roomId, out var targetPosition, out var targetSize))
            {
                yield return WaitU(duration);
                yield break;
            }

            Vector3 startPosition = tutorialBgCamera.transform.position;
            float startSize = tutorialBgCamera.orthographicSize;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                tutorialBgCamera.transform.position = Vector3.Lerp(startPosition, targetPosition, t);
                tutorialBgCamera.orthographicSize = Mathf.Lerp(startSize, targetSize, t);
                yield return null;
            }
            tutorialBgCamera.transform.position = targetPosition;
            tutorialBgCamera.orthographicSize = targetSize;
        }

        void AttachTutorialNestedBoxGuidance()
        {
            if (tutorialBoardRoot == null || tutorialModel == null) return;
            NestedBoxGuidanceFx guidance =
                tutorialBoardRoot.GetComponent<NestedBoxGuidanceFx>();
            if (guidance == null)
                guidance = tutorialBoardRoot.gameObject.AddComponent<NestedBoxGuidanceFx>();
            guidance.Configure(tutorialModel, tutorialRoomRoots, tutorialViews,
                cellSprite, glowSprite);
        }

        void CleanupTutorialPuzzle()
        {
            if (tutorialBgCamera != null)
            {
                tutorialBgCamera.enabled = false;
                tutorialBgCamera.targetTexture = null;
                tutorialBgCamera.cullingMask = 0;
            }
            if (tutorialBoardRoot != null) Destroy(tutorialBoardRoot.gameObject);
            tutorialBoardRoot = null;
            tutorialModel = null;
            tutorialRoomRoots.Clear();
            tutorialViews.Clear();
            tutorialTiles = null;
            tutorialPlayerBlinker = null;
            if (tutorialFx != null) tutorialFx.SetVideo(null);
        }

        // Chosen: play the campaign level for real. Dispose the separate teaching board while the
        // scrim still covers the display, then reveal the untouched campaign puzzle underneath.
        System.Collections.IEnumerator TutorialExitToPlay()
        {
            countdownArmed = false;
            tutorialInteractive = false;
            tutorialProgressDeadlineRealtime = -1f;
            StopTutorialDecisionCountdown();
            if (tutorialFx != null)
            {
                tutorialFx.HideChoice();
                tutorialFx.HideSkip();
            }
            ClearGoalGlow();
            ClearMechanicSpotlights();
            CleanupTutorialPuzzle();

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
            SetTutorialTimerPresentation(false);

            CompleteTutorialExit();
        }

        void CompleteTutorialExit()
        {
            StopTutorialDecisionCountdown();
            // The tutorial is finished and the untouched campaign board is now under player
            // control. Start the gameplay clock immediately.
            countdownArmed = false;
            cinematic = false;                      // gameplay is live from here
            tutorialExiting = false;
            tutorialInteractive = false;
            tutorialSequence.Clear();
            tutorialSequenceIndex = 0;
            tutorialProgressDeadlineRealtime = -1f;
            tutorialWatchdogRecovering = false;
            _cine = null;
            FocusRoom(model.player.roomId);         // restore normal all-room visibility after close-up
            ArmGameplayCountdown();
            MarkCurrentTutorialSeen();
        }

        public void TutorialWatchAgain()
        {
            StopTutorialDecisionCountdown();
            tutorialExiting = false;
            tutorialInteractive = false;
            if (_cine != null) StopCoroutine(_cine);
            if (tutorialFx != null) tutorialFx.HideChoice();
            if (!TutorialRewind())
            {
                RecoverFromTutorialRewindFailure();
                return;
            }
            ClearGoalGlow();
            ClearMechanicSpotlights();
            // Replay only the tutorial currently on screen. In particular, REPEAT on the
            // NEW MECHANICS card must not jump back to a different chapter lesson.
            _cine = StartCoroutine(TutorialCinematic(false));
        }

        public void TutorialTryIt()
        {
            if (tutorialExiting) return;
            StopTutorialDecisionCountdown();
            Sfx.Ding();

            // The first card's primary action is NEXT. Only the second/final card hands control
            // to the untouched campaign level.
            if (tutorialSequenceIndex + 1 < tutorialSequence.Count)
            {
                tutorialSequenceIndex++;
                tutorialInteractive = false;
                if (_cine != null) StopCoroutine(_cine);
                if (tutorialFx != null) tutorialFx.HideChoice();
                _cine = StartCoroutine(TutorialCinematic(false));
                return;
            }

            // TRY IT YOURSELF always means the real campaign puzzle that caused this tutorial to
            // appear. The isolated teaching example is video-only and is never handed to the
            // player as a practice level.
            tutorialExiting = true;
            StartCoroutine(TutorialExitToPlay());
        }

        // Available on every tutorial through the visible purple action and Luxodd Purple/RB.
        public void TutorialSkip()
        {
            if (!Tutoring || tutorialExiting) return;
            StopTutorialDecisionCountdown();

            if (_cine != null)
            {
                StopCoroutine(_cine);
                _cine = null;
            }

            Sfx.Ding();
            tutorialInteractive = false;
            tutorialExiting = true;
            ClearMechanicSpotlights();
            StartCoroutine(TutorialExitToPlay());
        }

        // The campaign model is never moved by the tutorial. This guarded rewind remains a safety
        // net for scene upgrades and guarantees the puzzle always starts at move zero.
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
            countdownArmed = false;
            StopTutorialDecisionCountdown();
            ClearGoalGlow();
            ClearMechanicSpotlights();
            CleanupTutorialPuzzle();
            ReleaseRT();
            if (tutorialFx != null) tutorialFx.RevealGameplayImmediately();
            if (cameraFollow != null) cameraFollow.enabled = true;
            SetTutorialTimerPresentation(false);
            SetHud(1f, true);
            cinematic = false;
            tutorialInteractive = false;
            tutorialSequence.Clear();
            tutorialSequenceIndex = 0;
            tutorialProgressDeadlineRealtime = -1f;
            tutorialWatchdogRecovering = false;
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
            CleanupTutorialPuzzle();
            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            if (cam != null && cam.targetTexture == _rt) { cam.targetTexture = null; cam.ResetAspect(); }
            if (tutorialFx != null) tutorialFx.SetVideo(null);
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
        }

        // Leaving the scene mid-cinematic (Escape) unloads everything, but a RenderTexture is not
        // garbage-collected — release it explicitly so it can't leak across scene loads.
        void OnDestroy()
        {
            CleanupTutorialPuzzle();
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

        // The tutorial has its own screen-space canvas at sorting order 100, while the ordinary
        // timer lives inside the gameplay HUD at order 0. Promote only the timer above the lesson
        // and let its CanvasGroup ignore the hidden HUD parent, so the exact same countdown stays
        // visible without duplicating UI or state.
        void SetTutorialTimerPresentation(bool visible)
        {
            if (timerRoot == null || editorPreviewMode) return;

            timerRoot.gameObject.SetActive(GameplayCountdownEnabled);
            if (!gameplayTimerPositionCaptured)
            {
                gameplayTimerAnchoredPosition = timerRoot.anchoredPosition;
                gameplayTimerPositionCaptured = true;
            }
            // Skip now sits centred below the tutorial video, so the timer keeps its normal
            // gameplay position at the top-right during both the video and the final choices.
            timerRoot.anchoredPosition = gameplayTimerAnchoredPosition;
            if (timerCanvas != null)
            {
                timerCanvas.ignoreParentGroups = visible;
                timerCanvas.interactable = false;
                timerCanvas.blocksRaycasts = false;
            }

            // Do not use ?? here. Unity can return a destroyed/missing native component wrapper
            // that is not a CLR null but does compare equal to null through UnityEngine.Object.
            // Accessing that wrapper caused the MissingComponentException shown in the Console.
            if (tutorialTimerCanvas == null)
            {
                tutorialTimerCanvas = timerRoot.GetComponent<Canvas>();
                if (tutorialTimerCanvas == null)
                    tutorialTimerCanvas = timerRoot.gameObject.AddComponent<Canvas>();
            }
            if (tutorialTimerCanvas == null) return;

            tutorialTimerCanvas.overrideSorting = visible;
            tutorialTimerCanvas.sortingOrder = visible ? 110 : 0;
            if (visible) timerRoot.SetAsLastSibling();
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
            if (TryOfferFirstLevelSecondChance()) return;

            // A loss must sound at the moment the terminal state is announced.  Keeping this in
            // LoseFx made the cue depend on that optional component/coroutine being present and
            // running; levels without it could fail silently.  This is now the single call site
            // for both TIME UP and OUT OF MOVES.
            Sfx.Death();
            PlayLoseBoardLight();

            // A failed level earns no new level points, but the player must still see both that
            // result and the overall score which will be submitted at session end.
            int levelCount = levelPrefabs != null ? levelPrefabs.Length : 0;
            const int lostRunLevelScore = 0;
            endOfRunTotalScore = ScoreSystem.Total(levelCount);

            if (loseFx != null)
            {
                // Keep the board/model intact. After the five-second reading beat, Luxodd owns
                // the official Continue / Restart / End transaction choices.
                loseFx.SetScoreSummary(lostRunLevelScore, endOfRunTotalScore);
                loseFx.PlayGameOver(title, OpenLossSessionOptions);
                return;
            }
            // Old-scene safety: even without LoseFx, keep the same reading beat before opening
            // Luxodd's official session options.
            if (timeUpPanel != null) timeUpPanel.SetActive(true);
            StartCoroutine(OpenSessionOptionsAfterDelay(title, 5f));
        }

        bool TryOfferFirstLevelSecondChance()
        {
            if (levelIndex != 0 || editorPreviewMode || firstLevelSecondChanceUsed
                || firstLifeLessonFx == null)
                return false;

            firstLevelSecondChanceUsed = true;
            firstLifeLessonOpen = true;
            countdownArmed = false;
            lossTransactionRequested = false;

            // Keep the failed board visible behind the teaching card so the advice has context,
            // but do not report a terminal score or open the paid Luxodd loss transaction.
            Sfx.Death();
            PlayLoseBoardLight();
            firstLifeLessonFx.Show(AcceptFirstLevelSecondChance);
            return true;
        }

        public void AcceptFirstLevelSecondChance()
        {
            if (!firstLifeLessonOpen) return;
            firstLifeLessonOpen = false;
            if (firstLifeLessonFx != null) firstLifeLessonFx.DismissImmediate();

            // This is a fresh teaching attempt, not the ordinary RESTART action: refill the full
            // Level-1 timer/move allowance and suppress only the tutorial on this one reload.
            restartTimerPending = false;
            restartTimerLevel = -1;
            restartTimerRemaining = 0f;
            restartTimerCapturedAt = 0f;
            restartTimerWasArmed = false;
            restartTutorialSuppressionPending = true;
            restartTutorialSuppressionLevel = 0;
            timedUp = false;
            outOfMoves = false;
            countdownArmed = false;
            lossTransactionRequested = false;
            PlayerPrefs.SetInt(LevelKey, 0);
            PlayerPrefs.Save();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        System.Collections.IEnumerator OpenSessionOptionsAfterDelay(string reason, float delay)
        {
            Text legacyTitle = null;
            Text legacySub = null;
            if (timeUpPanel != null)
            {
                foreach (Text label in timeUpPanel.GetComponentsInChildren<Text>(true))
                {
                    if (label.name == "LoseTitle") legacyTitle = label;
                    else if (label.name == "LoseSub") legacySub = label;
                }
            }
            if (legacyTitle != null) legacyTitle.text = "GAME OVER";
            while (delay > 0f)
            {
                if (legacySub != null)
                    legacySub.text = LoseFx.ReturnMessage(reason, Mathf.CeilToInt(delay));
                delay -= Time.unscaledDeltaTime;
                yield return null;
            }
            OpenLossSessionOptions();
        }

        void OpenLossSessionOptions()
        {
            if (!Lost || lossTransactionRequested) return;
            lossTransactionRequested = true;
            LuxoddGameService.RequestLossTransaction(levelIndex, endOfRunTotalScore,
                ContinueCurrentSession, RestartCampaignSession);
        }

        // A paid Luxodd Restart is a new run, not a retry from the death position. Clear the
        // previous campaign, return to Level 1 with full resources, and allow its tutorial again.
        void RestartCampaignSession()
        {
            lossTransactionRequested = false;
            firstLevelSecondChanceUsed = false;
            ResetRestartTimerTransfer();
            MainMenuUI.ResetCampaignSessionProgress();
            PlayerPrefs.DeleteKey(EditorPreviewLevelKey);
            PlayerPrefs.SetInt(LevelKey, 0);
            PlayerPrefs.Save();
            LuxoddGameService.SyncProgress();
            SceneManager.LoadScene("Game");
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
            restartTutorialSuppressionPending = true;
            restartTutorialSuppressionLevel = levelIndex;
            PreserveTimerForRestart();
            ReportLevelEndOnce();
            ReloadCurrentLevel();
        }

        void PreserveTimerForRestart()
        {
            restartTimerPending = true;
            restartTimerLevel = levelIndex;
            restartTimerRemaining = Mathf.Max(0f, timeLeft);
            restartTimerCapturedAt = Time.realtimeSinceStartup;
            restartTimerWasArmed = countdownArmed;
        }

        void RestoreTimerAfterRestart()
        {
            if (!restartTimerPending) return;

            bool sameLevel = restartTimerLevel == levelIndex;
            float remaining = restartTimerRemaining;
            float capturedAt = restartTimerCapturedAt;
            bool wasArmed = restartTimerWasArmed;
            ResetRestartTimerTransfer(); // consume the transfer even if a different level loaded

            if (!sameLevel) return;

            // Once the player has started the clock, scene loading is part of the same arcade
            // attempt. Subtract it so repeated restarts cannot manufacture free playing time.
            if (wasArmed)
                remaining -= Mathf.Max(0f, Time.realtimeSinceStartup - capturedAt);

            timeLeft = Mathf.Clamp(remaining, 0f, timeLimit);
            countdownArmed = wasArmed;
        }

        // Continue buys a fresh attempt at THIS level, not a recovery at the death position.
        // Reloading uses Start's normal parser/view setup, resetting every room, crate, key,
        // terrain change and undo entry. Campaign scores/progress and the SDK session survive.
        void ContinueCurrentSession()
        {
            PrepareCurrentLevelContinue();
            ReloadCurrentLevel();
        }

        void PrepareCurrentLevelContinue()
        {
            // Unlike the in-game Restart shortcut, a transaction retry gets the full authored
            // timer. Discard only the old clock transfer, not session tutorial/score progress.
            restartTimerPending = false;
            restartTimerLevel = -1;
            restartTimerRemaining = 0f;
            restartTimerCapturedAt = 0f;
            restartTimerWasArmed = false;
            continuedLevelReload = levelIndex;
            restartTutorialSuppressionPending = true;
            restartTutorialSuppressionLevel = levelIndex;
            PlayerPrefs.SetInt(LevelKey, levelIndex);
            if (editorPreviewMode) PlayerPrefs.SetInt(EditorPreviewLevelKey, levelIndex);
            PlayerPrefs.Save();
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
            if (endOfRunTotalScore < 0)
                endOfRunTotalScore = ScoreSystem.Total(
                    levelPrefabs != null ? levelPrefabs.Length : 0);
            LuxoddGameService.ReportLevelEnd(levelIndex, endOfRunTotalScore);
        }

        // Menu always returns to the LEVEL BOARD, not the title screen — you came from a level,
        // so the useful place to land is the list you picked it from. Same flag ReturnToLevels uses.
        void GoToMenu() => ReturnToLevels();
    }
}
