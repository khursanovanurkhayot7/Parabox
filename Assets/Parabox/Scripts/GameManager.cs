using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox
{
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

        [Header("Effect colors / sprites")]
        public Color boxColor = Color.white;
        public Color playerColor = Color.white;
        public Sprite pieceSprite;
        public Sprite ringSprite;

        [Header("Scene references")]
        public CameraFollow cameraFollow;
        public Text levelLabel;
        public Text movesLabel;
        public GameObject winPanel;
        public Text winTitle;
        public Text winStats;
        public Button nextButton;
        public Button menuButton;

        const string LevelKey = "Parabox.Level";
        static string BestKey(int level) => "Parabox.Best." + level;
        const float InteriorFit = 0.72f; // interior room size relative to its box cell

        // Sorting orders. Entities (box 6, player 8, meta-box group 6) are baked into
        // the prefabs by the wizard; these are the room-decoration layers below them.
        const int OrderFloorBase = 0;
        const int OrderFloorCell = 1;
        const int OrderWall = 2;
        const int OrderBorder = 3;
        const int OrderGoal = 4;

        LevelModel model;
        readonly Dictionary<int, Transform> roomRoots = new Dictionary<int, Transform>();
        readonly Dictionary<PEntity, EntityView> views = new Dictionary<PEntity, EntityView>();
        int levelIndex;
        bool won;
        Vector2Int lastHeld;
        float nextRepeat;

        void Start()
        {
            Sfx.Init();
            Fx.Piece = pieceSprite;
            Fx.Ring = ringSprite;

            levelIndex = Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 0), 0, levelPrefabs.Length - 1);
            model = LevelParser.Parse(levelPrefabs[levelIndex]);
            BuildView();
            SyncViews(true);
            UpdateHud();
            winPanel.SetActive(false);
            nextButton.onClick.AddListener(NextLevel);
            menuButton.onClick.AddListener(GoToMenu);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                GoToMenu();
                return;
            }

            if (kb.mKey.wasPressedThisFrame) Sfx.ToggleMute();

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
            if (model.IsWon()) Win();
        }

        Dictionary<PEntity, (int room, Vector2Int pos)> CapturePositions()
        {
            var d = new Dictionary<PEntity, (int room, Vector2Int pos)>();
            foreach (var e in model.entities) d[e] = (e.roomId, e.pos);
            return d;
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
        void BuildView()
        {
            var worldRoot = new GameObject("LevelView").transform;

            foreach (var room in model.rooms.Values)
            {
                var root = new GameObject("Room_" + room.id).transform;
                root.SetParent(worldRoot, false);
                roomRoots[room.id] = root;
                PaintRoom(root, room);
            }

            var nestedRooms = new HashSet<int>();
            foreach (var e in model.entities)
            {
                GameObject prefab = e.isPlayer
                    ? playerPrefab
                    : (e.interiorRoomId >= 0 ? metaBoxPrefab : boxPrefab);

                var go = Instantiate(prefab);
                var view = go.GetComponent<EntityView>();
                if (view == null) view = go.AddComponent<EntityView>();
                views[e] = view;

                // A meta-box carries its interior room as a scaled-down child so the
                // nesting and the recursive zoom happen automatically. Its frame color
                // matches the room it contains (like the real game).
                // Guard: never nest the main room (id 0) or a room already nested — that
                // would create a transform-parenting cycle (the infinity case) and throw.
                if (e.interiorRoomId >= 1 && model.rooms.ContainsKey(e.interiorRoomId)
                    && nestedRooms.Add(e.interiorRoomId))
                {
                    Color interiorC = RoomColor(e.interiorRoomId);
                    var backing = go.transform.Find("Backing");
                    if (backing) backing.GetComponent<SpriteRenderer>().color = Darken(interiorC, 0.5f);
                    var frame = go.transform.Find("Frame");
                    if (frame) frame.GetComponent<SpriteRenderer>().color = Lighten(interiorC, 0.3f);

                    var innerRoom = model.rooms[e.interiorRoomId];
                    var innerRoot = roomRoots[e.interiorRoomId];
                    innerRoot.SetParent(go.transform, false);
                    float s = InteriorFit / Mathf.Max(innerRoom.width, innerRoom.height);
                    innerRoot.localScale = Vector3.one * s;
                    innerRoot.localPosition = Vector3.zero;
                }
            }
        }

        void PaintRoom(Transform root, PRoom room)
        {
            Color floorC = RoomColor(room.id);

            // Flat, continuous floor — one solid rounded rectangle (no per-cell plates).
            var floor = Instantiate(floorPrefab, root);
            floor.transform.localPosition = Vector3.zero;
            var fsr = floor.GetComponent<SpriteRenderer>();
            fsr.drawMode = SpriteDrawMode.Sliced;
            fsr.size = new Vector2(room.width, room.height);
            fsr.color = floorC;
            fsr.sortingOrder = OrderFloorBase;

            // Hairline grid drawn over the floor — thin lines on the cell boundaries.
            var grid = Instantiate(gridPrefab, root);
            grid.transform.localPosition = Vector3.zero;
            var gsr = grid.GetComponent<SpriteRenderer>();
            gsr.drawMode = SpriteDrawMode.Tiled;
            gsr.size = new Vector2(room.width, room.height);
            gsr.sortingOrder = OrderFloorCell;

            // Solid wall blocks.
            for (int x = 0; x < room.width; x++)
                for (int y = 0; y < room.height; y++)
                    if (room.wall[x, y])
                    {
                        var w = Instantiate(wallPrefab, root);
                        w.transform.localPosition = Cell(room, new Vector2Int(x, y));
                        SetOrder(w, OrderWall);
                    }

            // Border frame around the room edge.
            var border = Instantiate(borderPrefab, root);
            border.transform.localPosition = Vector3.zero;
            var brd = border.GetComponent<SpriteRenderer>();
            brd.drawMode = SpriteDrawMode.Sliced;
            brd.size = new Vector2(room.width, room.height);
            brd.color = Lighten(floorC, 0.34f);
            brd.sortingOrder = OrderBorder;

            foreach (var g in room.boxGoals)
            {
                var go = Instantiate(boxGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
            }
            foreach (var g in room.playerGoals)
            {
                var go = Instantiate(playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
            }
        }

        void SyncViews(bool instant)
        {
            foreach (var e in model.entities)
            {
                var room = model.rooms[e.roomId];
                views[e].SetTarget(roomRoots[e.roomId], Cell(room, e.pos), instant);
            }

            var playerRoom = model.rooms[model.player.roomId];
            cameraFollow.SetTargetRoom(roomRoots[playerRoom.id], playerRoom.width, playerRoom.height, instant);
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

        // -------------------------------------------------- HUD / flow
        void UpdateHud()
        {
            var info = levelPrefabs[levelIndex].GetComponent<ParaboxLevel>();
            string name = info != null ? info.levelName : "";
            levelLabel.text = $"Level {levelIndex + 1} — {name}";

            if (PlayerPrefs.HasKey(BestKey(levelIndex)))
                movesLabel.text = $"Moves: {model.MoveCount}    Best: {PlayerPrefs.GetInt(BestKey(levelIndex))}";
            else
                movesLabel.text = $"Moves: {model.MoveCount}";
        }

        void Win()
        {
            won = true;

            int moves = model.MoveCount;
            int prevBest = PlayerPrefs.GetInt(BestKey(levelIndex), int.MaxValue);
            bool newBest = moves < prevBest;
            int best = Mathf.Min(moves, prevBest);
            PlayerPrefs.SetInt(BestKey(levelIndex), best);
            PlayerPrefs.Save();

            bool last = levelIndex >= levelPrefabs.Length - 1;
            bool allDone = AllLevelsBeaten();

            winTitle.text = (last && allDone) ? "You Win!" : "Level Complete!";

            if (winStats != null)
            {
                winStats.text = newBest
                    ? $"Solved in {moves} moves — new best!"
                    : $"Solved in {moves} moves   (best {best})";
                if (last && allDone)
                    winStats.text += "\nAll levels complete — thanks for playing!";
            }

            var label = nextButton.GetComponentInChildren<Text>();
            if (label != null) label.text = last ? "Play Again" : "Next Level";
            winPanel.SetActive(true);

            Fx.Confetti(Camera.main, roomColors, (last && allDone) ? 130 : 64, 300);
            Sfx.Win();
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
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void GoToMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }
    }
}
