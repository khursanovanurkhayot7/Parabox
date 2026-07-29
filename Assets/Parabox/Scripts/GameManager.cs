using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

        // Built at runtime so existing scenes and regenerated scenes receive the score HUD without
        // requiring a manual Inspector pass.
        RectTransform scoreRoot;
        Text scoreLabel;
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
        static string BestKey(int level) => "Parabox.Best." + level;
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
        int levelIndex;
        bool won;
        Vector2Int lastHeld;
        float nextRepeat;
        float timeLeft;
        float timeLimit;
        bool timedUp;
        int par;
        int moveLimit;
        bool outOfMoves;
        bool levelEndReported;
        bool lossTransactionReady;

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
        bool demoMove;   // set only while the cinematic is driving DoMove itself
        // Lost, specifically — the win branch needs its own handling (Space = next level).
        bool Lost => timedUp || outOfMoves;
        int MovesLeft => Mathf.Max(0, moveLimit - model.MoveCount);

        // Slack over the solver's optimal, escalating by chapter: forgiving while you're still
        // learning the mechanic, demanding once you know it. Undo refunds a move (it pops the
        // model's undo stack), so experimenting inside the limit stays free.
        // Per chapter, five of them. Forgiving while you're learning the push, tightest in the
        // Master chapter — but never below 2, because a chapter-5 level is four rooms deep and a
        // single wasted probe move must not cost you the run.
        static int MoveSlack(int levelIdx)
        {
            switch (levelIdx / 10)
            {
                case 0: return 5;    // Easy
                case 1: return 4;    // Medium
                case 2: return 3;    // Hard
                case 3: return 3;    // Expert
                default: return 2;   // Master
            }
        }
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
            Sfx.Init();
            Fx.Piece = pieceSprite;
            Fx.Ring = ringSprite;
            Fx.Glow = glowSprite;

            levelIndex = Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 0), 0, levelPrefabs.Length - 1);
            ApplyLevelTheme(levelIndex / 10);   // Beginner / Intermediate / Advanced skin
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
                if (cameraFollow != null && !WillTutorial()) cameraFollow.PlayIntro();
            }

            // The final level arrives differently: a slower, further fly-in, so the board opens
            // out of the distance instead of simply being there. Same mechanism as every other
            // level — only the numbers change, which keeps it honest and impossible to desync.
            if (PlayerPrefs.GetInt("Parabox.FinalRun", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.FinalRun");
                if (cameraFollow != null)
                {
                    cameraFollow.introDur = 2.2f;        // vs 1.2 — it takes its time
                    cameraFollow.introZoomOut = 9f;      // vs 5 — it comes from further away
                    cameraFollow.PlayIntro();
                }
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
            timeLimit = par > 0 ? Mathf.Max(20f, par * 3f + 14f) : 10f + levelIndex;
            timeLeft = timeLimit;
            // par == 0 means the prefab predates par data (wizard not re-run). Fall back to an
            // unrestrictive limit rather than handing the player an unwinnable level.
            moveLimit = par > 0 ? par + MoveSlack(levelIndex) : 999;
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
            BuildScoreHud();
            UpdateHud();
            if (timeUpPanel != null) timeUpPanel.SetActive(false);
            if (retryButton != null) retryButton.onClick.AddListener(Restart);
            if (backToLevelsButton != null) backToLevelsButton.onClick.AddListener(ReturnToLevels);
            WireOnScreenControls();
            AnimateTimer();
            LuxoddGameService.ReportLevelBegin(levelIndex);
        }

        // On-screen buttons drive the exact same logic as the keyboard (touch / click support).
        void WireOnScreenControls()
        {
            // d-pad: press-and-hold to move (fires on press + repeats while held)
            WireHold(upButton,    Vector2Int.up);
            WireHold(downButton,  Vector2Int.down);
            WireHold(leftButton,  Vector2Int.left);
            WireHold(rightButton, Vector2Int.right);
            if (undoButton != null)    undoButton.onClick.AddListener(UiUndo);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (muteButton != null)    muteButton.onClick.AddListener(UiMute);
            if (hudMenuButton != null) hudMenuButton.onClick.AddListener(GoToMenu);
            UpdateMuteIcon();
        }

        void WireHold(Button b, Vector2Int dir)
        {
            if (b == null) return;
            var h = b.GetComponent<HoldRepeatButton>();
            if (h == null) h = b.gameObject.AddComponent<HoldRepeatButton>();
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
            if (model.Undo()) { Sfx.Undo(); SyncViews(false); UpdateHud(); }
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
        // Yellow/White=level select/back, Blue=mute. Orange remains owned by Luxodd.
        bool HandleArcadeInput()
        {
            var arcade = LuxoddArcadeAdapter.Instance;
            if (arcade == null) return false;

            if (arcade.MuteDown) { Sfx.ToggleMute(); UpdateMuteIcon(); }
            if (Lost)
            {
                // Luxodd owns retry/end decisions once the loss sequence begins. Black or Green
                // can reopen a popup that the player cancelled, but neither may bypass payment by
                // directly reloading the scene.
                if (lossTransactionReady && (arcade.ConfirmDown || arcade.RestartDown))
                {
                    RequestLossTransaction();
                    return true;
                }
                return false;
            }
            if (arcade.BackDown || arcade.LevelsDown)
            {
                GoToMenu();
                return true;
            }

            if (Tutoring) return false; // tutorial UI keeps normal EventSystem ownership
            if (won)
            {
                if (arcade.ConfirmDown) { NextLevel(); return true; }
                return false;
            }
            if (arcade.RestartDown) { Restart(); return true; }
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

        bool _flagsConsumed;

        void Update()
        {
            // clear the one-shot menu-entry flag once, on the first Update — i.e. AFTER every Start()
            // has read it (ScreenFade + this component read it in Start; order between Starts is undefined).
            if (!_flagsConsumed) { _flagsConsumed = true; PlayerPrefs.DeleteKey("Parabox.Seamless"); }

            TickTimer();
            AnimateTimer();

            if (HandleArcadeInput()) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.mKey.wasPressedThisFrame) { Sfx.ToggleMute(); UpdateMuteIcon(); }

            // A failed run cannot use Escape/R to avoid the Luxodd transaction. Enter, Space or R
            // only reopens a transaction popup after its five-second leaderboard dwell.
            if (Lost)
            {
                if (lossTransactionReady && (kb.enterKey.wasPressedThisFrame
                    || kb.spaceKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame))
                    RequestLossTransaction();
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
            if (Tutoring) { lastHeld = ReadDirection(kb); return; }

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

        void DoMove(Vector2Int dir)
        {
            var beforePos = CapturePositions();
            var beforeGoals = SatisfiedGoals();
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

            SyncViews(false);

            // Squash every entity that actually moved, in the move direction.
            var moveDir = new Vector2(dir.x, dir.y);
            bool pushedSomething = false;
            foreach (var e in model.entities)
            {
                if (!beforePos.TryGetValue(e, out var bp) || bp.room != e.roomId || bp.pos != e.pos)
                {
                    views[e].Squash(moveDir);
                    if (!e.isPlayer) pushedSomething = true;
                }
            }
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
            var playerBefore = beforePos[model.player];
            bool travelled = playerBefore.room != model.player.roomId
                             || Mathf.Abs(playerBefore.pos.x - model.player.pos.x)
                                + Mathf.Abs(playerBefore.pos.y - model.player.pos.y) > 1;

            if (impactEvent) Sfx.Impact();
            else if (collectedEvent) Sfx.Ding();
            else if (mechanicEvent) Sfx.Mechanic();
            if (travelled) Sfx.RoomShift();

            // Celebrate every target that just became satisfied.
            var afterGoals = SatisfiedGoals();
            foreach (var kv in afterGoals)
            {
                if (beforeGoals.ContainsKey(kv.Key)) continue;
                var parent = roomRoots[kv.Key.room];
                var localPos = Cell(model.rooms[kv.Key.room], new Vector2Int(kv.Key.x, kv.Key.y));
                Color col = kv.Value ? playerColor : boxColor;
                Fx.Burst(parent, localPos, new[] { col, Lighten(col, 0.35f) }, 12, 2.7f, 0.14f, 4.2f, 0.55f, OrderGoal + 60);
                Fx.Ripple(parent, localPos, col, OrderGoal + 59);
                Sfx.Ding();
            }

            UpdateHud();
            // The tutorial replays the winning line, so it reaches both of these. It is a
            // demonstration, not a run: it must not claim the level or spend the player's moves.
            // Everything above this point — squash, push sound, the goal burst — is exactly what
            // the demo exists to show, which is why it goes through DoMove rather than around it.
            if (demoMove) return;
            if (model.IsWon()) { Win(); return; }
            // solving ON the last move must still count as a win — hence the early return above
            if (MovesLeft <= 0) OutOfMoves();
        }

        Dictionary<PEntity, (int room, Vector2Int pos)> CapturePositions()
        {
            var d = new Dictionary<PEntity, (int room, Vector2Int pos)>();
            foreach (var e in model.entities) d[e] = (e.roomId, e.pos);
            return d;
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
        Dictionary<(int room, int x, int y), bool> SatisfiedGoals()
        {
            var d = new Dictionary<(int, int, int), bool>();
            foreach (var room in model.rooms.Values)
            {
                foreach (var g in room.boxGoals)
                {
                    var e = model.EntityAt(room.id, g);
                    if (e != null && !e.isPlayer) d[(room.id, g.x, g.y)] = false;
                }
                foreach (var g in room.playerGoals)
                {
                    var e = model.EntityAt(room.id, g);
                    if (e != null && e.isPlayer) d[(room.id, g.x, g.y)] = true;
                }
            }
            return d;
        }

        // -------------------------------------------------- view construction
        // Delegates to the shared BoardRenderer so the main menu renders the IDENTICAL board.
        Transform boardRoot;   // the rendered board — kept so the lose sequence can tear it apart

        void BuildView()
        {
            boardRoot = BoardRenderer.Render(model, BuildAssets(), roomRoots, views, tiles);
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
            FocusRoom(playerRoom.id);
            cameraFollow.SetTargetRoom(roomRoots[playerRoom.id], playerRoom.width, playerRoom.height, instant);
        }

        // Keep the complete recursive board rendered during room transitions. Only simplify the
        // miniature previews: their perimeter walls and opaque box backing are visual duplicates.
        // The camera may zoom toward the active room, but the enclosing board must never pop away.
        void FocusRoom(int roomId)
        {
            if (boardRoot == null || !roomRoots.TryGetValue(roomId, out var activeRoot) || activeRoot == null)
                return;

            roomRoots.TryGetValue(0, out var mainRoot);

            // During the cinematic only, isolate the room the demonstrated player entered so the
            // tiny action is readable inside the tutorial video. In real gameplay every room stays
            // rendered; entering a box must never make the outer board pop out of existence.
            bool tutorialCloseUp = cinematic && roomId != 0;
            foreach (var sr in boardRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                bool visible = !tutorialCloseUp || sr.transform.IsChildOf(activeRoot);

                // A nested room's perimeter is already enforced by the model. Its wall sprites
                // collapse into four oversized blocks when that room is enlarged for play, so keep
                // only the main board's perimeter visible. Internal wall sprites are unaffected.
                Transform owner = OwningRoomRoot(sr.transform);
                if (visible && owner != null && owner != mainRoot && IsBoundaryWall(sr.transform, owner))
                    visible = false;
                // Once a nested room is active, its ancestor meta-box frame and anchored marker
                // become a giant ring plus four dark bars over the play area. Hide only chrome on
                // enclosing boxes; boxes inside the active room stay fully readable.
                if (visible && IsEnclosingRecursiveBoxChrome(sr.transform, activeRoot))
                    visible = false;
                if (visible && IsRecursiveBoxBacking(sr.transform))
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

        static bool IsRecursiveBoxBacking(Transform child)
        {
            if (child == null || (child.name != "Backing" && child.name != "Shadow")) return false;
            var box = child.parent;
            return box != null && box.Find("Frame") != null && box.Find("Backing") != null;
        }

        static bool IsEnclosingRecursiveBoxChrome(Transform child, Transform activeRoot)
        {
            if (child == null || activeRoot == null) return false;
            if (child.name != "Frame" && child.name != "Bar") return false;
            var box = child.parent;
            return box != null && box.Find("Backing") != null && activeRoot.IsChildOf(box);
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

        void BuildScoreHud()
        {
            if (scoreRoot != null) return;
            Transform parent = timerRoot != null ? timerRoot.parent
                : movesLabel != null ? movesLabel.transform.parent : transform;

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
            scoreRoot.SetAsLastSibling();
        }

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

            var label = nextButton.GetComponentInChildren<Text>();
            if (label != null) label.text = last ? "Play Again" : "Next Level";

            // Finishing the whole game is not the same event as finishing a level, so it does not
            // get the same panel. The finale takes the screen over entirely; the ordinary win UI
            // never appears.
            if (last && allDone) { StartCoroutine(FinaleSequence()); return; }

            StartCoroutine(WinSequence(last));
        }

        // The completion, staged. The old version fired the burst on the same frame as the last
        // move and dropped a panel 0.55s later; the win was over before you registered it.
        //
        //   beat 1  a held breath — nothing moves, so the burst lands on a still screen
        //   beat 2  the burst + camera punch, and an energy pulse travelling through the board
        //   beat 3  you sit with the solved board while the pulse crosses it
        //   beat 4  hand off to the map, where the progression actually plays out
        System.Collections.IEnumerator WinSequence(bool last)
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

            // ---- 4. the finale gets its own, much longer ending -------------------------
            if (last)
            {
                yield return Finale();
                yield break;
            }

            // ---- 4b. out to the map ------------------------------------------------------
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

            var canvas = winPanel != null ? winPanel.GetComponentInParent<Canvas>()
                                          : FindAnyObjectByType<Canvas>();
            if (canvas == null) yield break;    // nothing to draw on — leave the board be

            var font = winTitle != null ? winTitle.font
                                        : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            FinaleFx.Build(canvas, font, glowSprite, cellSprite,
                           onAgain: () =>
                           {
                               for (int i = 0; i < levelPrefabs.Length; i++)
                                   PlayerPrefs.DeleteKey(BestKey(i));
                               PlayerPrefs.SetInt(LevelKey, 0);
                               PlayerPrefs.Save();
                               LuxoddGameService.SyncProgress();
                               SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                           },
                           onLevels: ReturnToLevels,
                           levels: levelPrefabs.Length,
                           totalMoves: TotalMovesAcrossRun());
        }

        bool AllLevelsBeaten()
        {
            for (int i = 0; i < levelPrefabs.Length; i++)
                if (!PlayerPrefs.HasKey(BestKey(i))) return false;
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
            if (Ended) return;
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f)
            {
                timeLeft = 0f;
                TimeUp();
            }
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

            bool danger = timeLeft <= 5f && !Ended;
            if (timerFill != null)
            {
                timerFill.fillAmount = timeLimit > 0f ? Mathf.Clamp01(timeLeft / timeLimit) : 0f;
                float danger01 = danger ? Mathf.Clamp01((5f - timeLeft) / 5f) : 0f;
                timerFill.color = Color.Lerp(timerAccentCur, TimerWarn, danger01);
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

        // ================================================= level-1 onboarding (cinematic)
        //
        // A dedicated cinematic, not an overlay on gameplay. The entire HUD is hidden, the screen is
        // letterboxed, and the camera does real choreography over the board while the level plays its
        // own solver-proven solve. Then three options; "Try It Yourself" retracts the letterbox,
        // reveals the HUD and settles the camera into the playable board — all in-scene, so gameplay
        // begins with no load and no cut. Runs on level 1 only, first time only.
        public const string TutorialKey = "Parabox.Tutorial.Seen";
        Coroutine _cine;
        GameObject _goalGlow;
        RenderTexture _rt;                      // the board renders into this; the panel shows it
        const int RTW = 1280, RTH = 720;        // 16:9 — matches the panel so the board isn't distorted
        float RTAspect => RTW / (float)RTH;

        // The level's proven-optimal move string, straight off the prefab (e.g. "DDL").
        string CurrentSolution()
        {
            var info = levelPrefabs[levelIndex].GetComponent<ParaboxLevel>();
            return info != null ? info.solution : "";
        }

        // The first level of every chapter is a TEACHING level: the cinematic plays it out so the
        // player SEES the new mechanic solved rather than reading about it. These openers are all
        // deliberately trivial (par 2-4), so demonstrating them gives nothing away. Plays every time
        // (the player can Skip / Try It Yourself), same as level 1.
        // Chapter openers only — levels 1, 11, 21, 31, 41.
        static bool IsTutorialLevel(int i) => i == 0 || i == 10 || i == 20 || i == 30 || i == 40;

        // The one line shown while that chapter's mechanic is demonstrated.
        static string TutorialLine(int i)
        {
            switch (i)
            {
                case 10: return "Currents carry you. Land on one and you keep going.";
                case 20: return "Push a rock into a gap to bridge it.";
                case 30: return "Step into a whirlpool — you come out its twin.";
                case 40: return "Some boxes have a room inside. You can go in.";
                default: return "Push the box onto the marker.";
            }
        }

        bool WillTutorial()
            => tutorialFx != null && IsTutorialLevel(levelIndex)
               && !string.IsNullOrEmpty(CurrentSolution());

        void MaybeTutorial()
        {
            if (!WillTutorial()) return;
            _cine = StartCoroutine(TutorialCinematic());
        }

        // The master timeline. Camera + board + panel, sequenced.
        System.Collections.IEnumerator TutorialCinematic()
        {
            cinematic = true;
            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            if (cam == null || !RootFraming(out var c, out var scale, out int w, out int h))
            {
                cinematic = false;                 // can't stage it — hand the board over unharmed
                if (tutorialFx != null) tutorialFx.HideChoice();
                yield break;
            }

            // dedicate the screen: suspend the HUD's own reveal, hide the HUD, take the camera
            if (hudReveal != null) hudReveal.StandDown();
            SetHud(0f, false);
            if (cameraFollow != null) cameraFollow.enabled = false;

            // This whole block runs SYNCHRONOUSLY inside Start() (StartCoroutine runs up to the first
            // yield immediately), i.e. BEFORE the first frame renders. So cover the screen instantly
            // and redirect the board into the RenderTexture right now: the full-screen game board is
            // never shown, and the tutorial panel opens DIRECTLY instead of "board appears → fades →
            // panel". The bg camera keeps the display fed; the opaque scrim hides the switch.
            if (tutorialFx != null) tutorialFx.CoverInstant();
            EnsureRT();
            cam.targetTexture = _rt;               // Unity sets cam.aspect to the RT's 16:9 automatically
            if (tutorialBgCamera != null) tutorialBgCamera.enabled = true;   // keep the DISPLAY fed
            if (tutorialFx != null) { tutorialFx.SetVideo(_rt); tutorialFx.PanelIn(); }

            // framings for the PANEL aspect, centred: wide to establish, then closer through the solve
            CameraFraming.Compute(c, scale, w, h, RTAspect, 1.5f, 0.12f, 0.12f, out var widePos, out var wideSize);
            CameraFraming.Compute(c, scale, w, h, RTAspect, 1.12f, 0.12f, 0.12f, out var closePos, out var closeSize);

            // the card's own entrance is the reveal, so the board simply starts on the wide shot
            cam.transform.position = widePos;
            cam.orthographicSize = wideSize;
            SpawnGoalGlow();                        // subtle breathing highlight on the objective
            yield return WaitU(0.6f);

            // one quiet line, then it plays itself
            if (tutorialFx != null) yield return tutorialFx.Caption(TutorialLine(levelIndex));

            // the solve: fire each proven move on a beat while the camera pushes in continuously,
            // so the two motions read as one gesture rather than a slideshow
            string sol = CurrentSolution();
            float gap = 0.62f;
            float solveDur = Mathf.Max(gap, sol.Length * gap);
            int establishingRoom = model.player.roomId;
            int fired = 0;
            float t = 0f;
            while (t < solveDur)
            {
                t += Time.unscaledDeltaTime;
                float g = Mathf.Clamp01(t / solveDur);
                while (fired < sol.Length && t >= fired * gap) TutorialStep(sol[fired++]);

                // Before the recursive entry, slowly push toward the whole board. Once the demo
                // enters a room-box, follow that room and enlarge it inside the tutorial panel.
                // This close-up is deliberately cinematic-only; CameraFollow retains the normal
                // full recursive hierarchy during actual gameplay.
                if (model.player.roomId != establishingRoom &&
                    TutorialRoomFraming(model.player.roomId, 1.08f, out var activePos, out var activeSize))
                {
                    float follow = 1f - Mathf.Exp(-5.5f * Time.unscaledDeltaTime);
                    cam.transform.position = Vector3.Lerp(cam.transform.position, activePos, follow);
                    cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, activeSize, follow);
                }
                else
                {
                    cam.transform.position = Vector3.Lerp(widePos, closePos, g);
                    cam.orthographicSize = Mathf.Lerp(wideSize, closeSize, g);
                }
                yield return null;
            }
            while (fired < sol.Length) TutorialStep(sol[fired++]);   // guard: never drop a move
            if (model.player.roomId != establishingRoom &&
                TutorialRoomFraming(model.player.roomId, 1.08f, out var finalPos, out var finalSize))
            {
                cam.transform.position = finalPos;
                cam.orthographicSize = finalSize;
            }
            else
            {
                cam.transform.position = closePos;
                cam.orthographicSize = closeSize;
            }

            yield return WaitU(1.2f);              // hold on the solved board
            if (tutorialFx != null) yield return tutorialFx.ShowChoice();
        }

        // One demonstrated move, through the SAME function the arrow keys call, so the cinematic
        // shows exactly the squash / push sound / goal burst the player is about to feel.
        bool TutorialStep(char c)
        {
            Vector2Int d = c == 'U' ? Vector2Int.up : c == 'D' ? Vector2Int.down
                         : c == 'L' ? Vector2Int.left : Vector2Int.right;
            int before = model.MoveCount;
            demoMove = true;
            try { DoMove(d); }
            finally { demoMove = false; }   // a throw must not leave Win() disabled for the session
            return model.MoveCount > before;
        }

        // Chosen: play the level for real. Hand the screen back to the camera (while the scrim is
        // still opaque, so the switch is invisible), reset the board, then dissolve the panel to
        // reveal the ready-to-play board — the panel closing IS the transition into gameplay.
        System.Collections.IEnumerator TutorialExitToPlay()
        {
            if (tutorialFx != null) tutorialFx.HideChoice();
            ClearGoalGlow();

            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            if (cam != null) { cam.targetTexture = null; cam.ResetAspect(); }   // draw to the SCREEN again
            if (tutorialBgCamera != null) tutorialBgCamera.enabled = false;      // the main camera has the display back
            TutorialRewind();                    // resets the board AND snaps the camera to the gameplay pose

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

            timeLeft = timeLimit;                   // the demo cost no clock; the player starts fresh
            cinematic = false;                      // gameplay is live from here
            PlayerPrefs.SetInt(TutorialKey, 1);     // once ever — set only on the way INTO play
            PlayerPrefs.Save();
            LuxoddGameService.SyncProgress();
        }

        public void TutorialWatchAgain()
        {
            if (_cine != null) StopCoroutine(_cine);
            if (tutorialFx != null) tutorialFx.HideChoice();
            TutorialRewind();
            ClearGoalGlow();
            _cine = StartCoroutine(TutorialCinematic());
        }

        public void TutorialTryIt() { Sfx.Ding(); StartCoroutine(TutorialExitToPlay()); }
        public void TutorialSkip()  { StartCoroutine(TutorialExitToPlay()); }

        // Put the board back exactly as it was, replaying backwards through the same undo the player's
        // Z key uses — so the board they play cannot differ from the one they watched.
        void TutorialRewind()
        {
            while (model.MoveCount > 0) model.Undo();
            SyncViews(true);
            UpdateHud();
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

            // the board comes apart in WORLD space, before any UI appears
            var cam = cameraFollow != null ? cameraFollow.GetComponent<Camera>() : Camera.main;
            BoardBreakFx.Play(cam, BoardCentre(), BoardShards(), glowSprite, ringSprite,
                new[] { new Color(1f, 0.42f, 0.34f), new Color(1f, 0.66f, 0.33f), new Color(0.62f, 0.70f, 0.80f) });

            if (loseFx != null)
            {
                loseFx.Play(title, sub, RequestLossTransaction);
                return;
            }
            // no LoseFx wired (wizard not re-run) — fall back to the old panel rather than
            // silently leaving the player on a dead board with no way out
            if (timeUpPanel != null) timeUpPanel.SetActive(true);
            StartCoroutine(RequestLossTransactionAfter(5f));
        }

        System.Collections.IEnumerator RequestLossTransactionAfter(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds) { elapsed += Time.unscaledDeltaTime; yield return null; }
            RequestLossTransaction();
        }

        void RequestLossTransaction()
        {
            if (!Lost) return;
            lossTransactionReady = true;
            int totalScore = ScoreSystem.Total(levelPrefabs != null ? levelPrefabs.Length : 0);
            LuxoddGameService.RequestLossTransaction(levelIndex, totalScore, ContinueCurrentSession);
        }

        // Paid Continue keeps the Luxodd game session alive but gives this puzzle a clean new
        // attempt at the same level. The shattered board cannot safely be reconstructed in place,
        // so a scene reload is the deterministic continuation owned by the game.
        void ContinueCurrentSession()
        {
            Sfx.Mechanic();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void ReturnToLevels()
        {
            ReportLevelEndOnce();
            PlayerPrefs.SetInt(OpenLevelsKey, 1);
            PlayerPrefs.Save();
            SceneManager.LoadScene("MainMenu");
        }

        void Restart()
        {
            // Guarded on Tutoring, NOT on Ended: the lose panel's Retry button also calls this, and
            // it fires exactly when Ended is true. The R key is already blocked during the demo, so
            // without this the on-screen button could interrupt it while the keyboard could not.
            if (Tutoring) return;
            ReportLevelEndOnce();
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
