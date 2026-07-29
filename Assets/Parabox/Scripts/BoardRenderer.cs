using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Everything needed to draw a board — so the main menu and the game render the SAME
    // world-space board (the "O" in the logo becomes the real playable level).
    public class BoardAssets
    {
        public GameObject floorPrefab, gridPrefab, wallPrefab, boxGoalPrefab, playerGoalPrefab, boxPrefab, metaBoxPrefab, playerPrefab;
        public Sprite ringSprite, glowSprite, vignetteSprite, cellSprite;
        public Color[] roomColors;
        public Color boxColor, playerColor, wallColor, gridColor, frameColor, gutterColor;
        public float floorVignette, pieceGlow, cellLift;
        public int chapter;   // 0/1/2 → border thickness + glow strength step up per chapter
        // The menu draws the board inside the logo's "O" ring, which is its own outline. Without this
        // the board's frame (sized to the LEVEL) shows through as a second, level-shaped outline —
        // which is why the O looked like a wide dash instead of a letter.
        public bool hideFrame;
        public Sprite floorTex;
        public Color floorTexTint;
    }

    // The parts of a board whose look depends on live game state. The renderer registers them and
    // the game shows/hides them straight from the model every frame — so undo needs no special
    // handling at all: restore the model and the board follows.
    public class BoardTiles
    {
        public readonly Dictionary<(int, Vector2Int), GameObject> pits = new Dictionary<(int, Vector2Int), GameObject>();       // hidden once a rock bridges the gap
        public readonly Dictionary<(int, Vector2Int), GameObject> coral = new Dictionary<(int, Vector2Int), GameObject>();      // intact coral — hidden once it collapses
        public readonly Dictionary<(int, Vector2Int), GameObject> rubble = new Dictionary<(int, Vector2Int), GameObject>();     // the hole it leaves — shown once it does
        public readonly Dictionary<(int, Vector2Int), GameObject> gates = new Dictionary<(int, Vector2Int), GameObject>();      // the slab — hidden while a button is held
        public readonly Dictionary<(int, Vector2Int), GameObject> heavyGates = new Dictionary<(int, Vector2Int), GameObject>(); // ditto, for weight plates
        public readonly Dictionary<(int, Vector2Int), GameObject> pearls = new Dictionary<(int, Vector2Int), GameObject>();     // hidden once collected
        public readonly Dictionary<(int, Vector2Int), GameObject> locks = new Dictionary<(int, Vector2Int), GameObject>();      // hidden once every pearl is in
        public readonly Dictionary<(int, Vector2Int), GameObject> rocks = new Dictionary<(int, Vector2Int), GameObject>();      // hidden once a crate shatters it
        public readonly Dictionary<(int, Vector2Int), GameObject> latches = new Dictionary<(int, Vector2Int), GameObject>();    // hidden once a toggle is thrown
        public readonly Dictionary<(int, Vector2Int), GameObject> pulses = new Dictionary<(int, Vector2Int), GameObject>();     // hidden on the open beat
        public readonly Dictionary<(int, Vector2Int), GameObject> toggleOn = new Dictionary<(int, Vector2Int), GameObject>();   // the lit half of a switch
    }

    // Pure, gameplay-free board renderer lifted verbatim from GameManager (BuildView / PaintRoom /
    // the placement half of SyncViews) so it can run in ANY scene without input/timer/undo/win/HUD.
    public static class BoardRenderer
    {
        // Crate colours 1..N. A coloured crate and its goal are drawn in the same hue, which is
        // the entire explanation the player gets — and all they need.
        public static readonly Color[] CrateColours =
        {
            new Color(1f, 0.55f, 0.42f, 1f),   // 1 — coral
            new Color(0.55f, 0.82f, 1f, 1f),   // 2 — sky
            new Color(0.72f, 0.95f, 0.52f, 1f),// 3 — reef green
        };

        public static Color CrateColour(Color fallback, int colour)
            => colour <= 0 || colour > CrateColours.Length ? fallback : CrateColours[colour - 1];

        // strict design system — must mirror the wizard's CreateTilePrefabs values
        public const float ObjSize = 0.84f;    // every movable object + goal footprint
        public const float TileSize = 0.90f;   // board tile + wall
        // How much of a meta-box's cell the nested room spans.
        //
        // This must stay clear of the meta-box frame's inner opening — that frame draws at
        // OrderFrameInBox (50), on top of everything inside the room. The frame used ringThick,
        // whose opening is only 0.605 units, so it covered the room's edge cells and goals came
        // out as clipped "U"/"C" shapes.
        //
        // Shrinking the room to 0.58 "fixed" that and made things worse: the camera frames the
        // room, so a smaller room means a tighter zoom, and the fixed-size frame ring went from
        // 8.3% of the view to 22.4% — a grey band swallowing the board. The room was never the
        // problem. The frame is now ringBox (opening 0.740), which clears 0.72 with room to spare.
        const float InteriorFit = 0.72f;
        const int OrderFloorBase = 0, OrderFloorCell = 1, OrderWall = 2, OrderGoal = 4;

        // Builds the full world-space board under a fresh "LevelView" root and returns it. Fills the
        // supplied roomRoots / views dictionaries and snaps every piece into its cell (static board).
        public static Transform Render(LevelModel model, BoardAssets a,
            Dictionary<int, Transform> roomRoots, Dictionary<PEntity, EntityView> views,
            BoardTiles tiles = null)
        {
            var worldRoot = new GameObject("LevelView").transform;

            foreach (var room in model.rooms.Values)
            {
                var root = new GameObject("Room_" + room.id).transform;
                root.SetParent(worldRoot, false);
                roomRoots[room.id] = root;
                PaintRoom(root, room, a, tiles);
            }

            var nestedRooms = new HashSet<int>();
            foreach (var e in model.entities)
            {
                GameObject prefab = (e.isPlayer || e.isEcho)
                    ? a.playerPrefab
                    : (e.interiorRoomId >= 0 ? a.metaBoxPrefab : a.boxPrefab);

                var go = Object.Instantiate(prefab);
                var view = go.GetComponent<EntityView>();
                if (view == null) view = go.AddComponent<EntityView>();
                views[e] = view;

                if (e.isPlayer || e.isEcho || e.isMirror)
                {
                    // The echo is you, drawn as an afterimage: same silhouette, paler and cooler,
                    // so at a glance you can tell which diver you are actually steering.
                    Color diverC = e.isEcho
                        ? Color.Lerp(a.playerColor, new Color(0.72f, 0.80f, 1f, 1f), 0.55f)
                        : e.isMirror
                            ? Color.Lerp(a.playerColor, new Color(1f, 0.72f, 0.86f, 1f), 0.6f)
                            : a.playerColor;
                    bool ghost = e.isEcho || e.isMirror;
                    var body = go.transform.Find("Body");
                    if (body)
                    {
                        var bsr = body.GetComponent<SpriteRenderer>();
                        bsr.color = ghost ? new Color(diverC.r, diverC.g, diverC.b, 0.62f) : diverC;
                        AddGlow(go, diverC, bsr.sortingOrder - 1, ghost ? 1.2f : 1.7f, a);
                    }
                    AddRim(go, "Body", diverC, ObjSize, a);
                }
                else if (e.interiorRoomId < 0)
                {
                    Color crateC = CrateColour(a.boxColor, e.colour);
                    var fill = go.transform.Find("Sprite");
                    if (fill)
                    {
                        var fsr = fill.GetComponent<SpriteRenderer>();
                        fsr.color = crateC;
                        AddGlow(go, crateC, fsr.sortingOrder - 1, 1.7f, a);
                    }
                    AddRim(go, "Sprite", crateC, ObjSize, a);

                    if (e.locking && a.ringSprite != null)   // a keyed ring = "this one commits"
                    {
                        var key = new GameObject("Locking");
                        key.transform.SetParent(go.transform, false);
                        key.transform.localScale = Vector3.one * 0.52f;
                        var ksr = key.AddComponent<SpriteRenderer>();
                        ksr.sprite = a.ringSprite;
                        ksr.color = new Color(1f, 0.88f, 0.45f, 0.95f);
                        ksr.sortingOrder = 60;
                    }

                    if (e.fragile && a.cellSprite != null)   // a split down the middle = "one shove"
                    {
                        Bar(go.transform, a.cellSprite, new Color(0.15f, 0.10f, 0.10f, 0.8f), 60,
                            new Vector2(0.02f, 0f), 0.60f, 0.07f, 78f);
                    }

                    if (e.boulder && a.ringSprite != null)   // a banded ring = obvious mass
                    {
                        var band = new GameObject("Boulder");
                        band.transform.SetParent(go.transform, false);
                        band.transform.localScale = Vector3.one * 0.78f;
                        var bsr6 = band.AddComponent<SpriteRenderer>();
                        bsr6.sprite = a.ringSprite;
                        bsr6.color = new Color(0.20f, 0.16f, 0.14f, 0.85f);
                        bsr6.sortingOrder = 60;
                    }

                    if (e.slick && a.cellSprite != null)   // frost sheen = "this one will not stop"
                    {
                        var sheen = new GameObject("Slick");
                        sheen.transform.SetParent(go.transform, false);
                        sheen.transform.localScale = new Vector3(0.52f, 0.13f, 1f);
                        sheen.transform.localPosition = new Vector3(-0.05f, 0.16f, 0f);
                        var shs = sheen.AddComponent<SpriteRenderer>();
                        shs.sprite = a.cellSprite;
                        shs.color = new Color(1f, 1f, 1f, 0.75f);
                        shs.sortingOrder = 60;
                    }
                }

                if (e.anchored && a.cellSprite != null)   // chains = bolted down
                {
                    var dark = new Color(0.16f, 0.14f, 0.12f, 0.9f);
                    Bar(go.transform, a.cellSprite, dark, 62, new Vector2(0f, 0.34f), 0.86f, 0.09f, 0f);
                    Bar(go.transform, a.cellSprite, dark, 62, new Vector2(0f, -0.34f), 0.86f, 0.09f, 0f);
                    for (int k = -1; k <= 1; k += 2)
                        Bar(go.transform, a.cellSprite, dark, 62, new Vector2(k * 0.30f, 0f), 0.72f, 0.07f, 90f);
                }

                if (e.interiorRoomId >= 1 && model.rooms.ContainsKey(e.interiorRoomId)
                    && nestedRooms.Add(e.interiorRoomId))
                {
                    Color interiorC = RoomColor(a.roomColors, e.interiorRoomId);
                    var backing = go.transform.Find("Backing");
                    // The real nested room is the box's face. A second opaque backing remains
                    // visible wherever the room's aspect ratio does not fill the square, producing
                    // the four black bars seen around the preview. Remove that redundant layer.
                    if (backing)
                    {
                        var backingRenderer = backing.GetComponent<SpriteRenderer>();
                        if (backingRenderer != null) backingRenderer.enabled = false;
                    }
                    var shadow = go.transform.Find("Shadow");
                    if (shadow)
                    {
                        var shadowRenderer = shadow.GetComponent<SpriteRenderer>();
                        if (shadowRenderer != null) shadowRenderer.enabled = false;
                    }
                    var frame = go.transform.Find("Frame");
                    if (frame) frame.GetComponent<SpriteRenderer>().color = Lighten(interiorC, 0.3f);

                    var innerRoom = model.rooms[e.interiorRoomId];
                    var innerRoot = roomRoots[e.interiorRoomId];
                    innerRoot.SetParent(go.transform, false);
                    float s = InteriorFit / Mathf.Max(innerRoom.width, innerRoom.height);
                    innerRoot.localScale = Vector3.one * s;
                    innerRoot.localPosition = Vector3.zero;

                    // The meta-box frame already communicates the boundary. At preview scale, a
                    // full row of near-black perimeter wall cells merges into four heavy blocks
                    // around the tiny room. Hide only those perimeter-wall visuals; collision is
                    // enforced by the model and GameManager also keeps them hidden while playing.
                    SetBoundaryWallsVisible(innerRoot, false);
                }
            }

            // snap pieces to their cells (static board)
            foreach (var e in model.entities)
            {
                var room = model.rooms[e.roomId];
                views[e].SetTarget(roomRoots[e.roomId], Cell(room, e.pos), true);
            }

            return worldRoot;
        }

        static void SetBoundaryWallsVisible(Transform roomRoot, bool visible)
        {
            for (int i = 0; i < roomRoot.childCount; i++)
            {
                var child = roomRoot.GetChild(i);
                if (child.name != "BoundaryWall") continue;
                foreach (var sr in child.GetComponentsInChildren<SpriteRenderer>(true))
                    sr.enabled = visible;
            }
        }

        // A single rotated bar built from the 1x1 cell sprite. Every chevron, fracture line and
        // gate slat in the mechanic art is made of these, so no new sprite assets are needed.
        static void Bar(Transform parent, Sprite s, Color c, int order,
                        Vector2 mid, float len, float thick, float angle)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(mid.x, mid.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = new Vector3(len, thick, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingOrder = order;
        }

        static float DirAngle(Vector2Int d)
        {
            if (d == Vector2Int.up) return 90f;
            if (d == Vector2Int.left) return 180f;
            if (d == Vector2Int.down) return 270f;
            return 0f;                                  // right
        }

        // A ">"-shaped chevron pointing along `dir`, made of two angled bars.
        static GameObject Chevron(Transform parent, Sprite s, Color c, int order,
                                  Vector2Int dir, float scale)
        {
            var go = new GameObject("Chevron");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, DirAngle(dir));
            go.transform.localScale = Vector3.one * scale;
            Bar(go.transform, s, c, order, new Vector2(0f, 0.08f), 0.30f, 0.085f, -34f);
            Bar(go.transform, s, c, order, new Vector2(0f, -0.08f), 0.30f, 0.085f, 34f);
            return go;
        }

        // A shell button / weight plate: a recessed ring with a lit centre. The heavy variant gets
        // a chunkier ring and cross-bracing so "this one needs a crate" is visible, not a rule you
        // have to be told.
        static void PaintSwitches(Transform root, PRoom room, BoardAssets a,
                                  bool[,] cells, Color tint, bool heavy)
        {
            if (cells == null || a.cellSprite == null) return;
            for (int cx = 0; cx < room.width; cx++)
                for (int cy = 0; cy < room.height; cy++)
                {
                    if (!cells[cx, cy]) continue;
                    var sw = new GameObject(heavy ? "Plate" : "Button");
                    sw.transform.SetParent(root, false);
                    sw.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                    var bed = new GameObject("Bed");
                    bed.transform.SetParent(sw.transform, false);
                    bed.transform.localScale = Vector3.one * 0.90f;
                    var bsr = bed.AddComponent<SpriteRenderer>();
                    bsr.sprite = a.cellSprite;
                    bsr.color = new Color(tint.r, tint.g, tint.b, 0.16f);
                    bsr.sortingOrder = OrderFloorCell + 1;

                    if (a.ringSprite != null)
                    {
                        var ring = new GameObject("Ring");
                        ring.transform.SetParent(sw.transform, false);
                        ring.transform.localScale = Vector3.one * (heavy ? 0.80f : 0.66f);
                        var rsr = ring.AddComponent<SpriteRenderer>();
                        rsr.sprite = a.ringSprite;
                        rsr.color = tint;
                        rsr.sortingOrder = OrderFloorCell + 2;
                    }

                    var core = new GameObject("Core");
                    core.transform.SetParent(sw.transform, false);
                    core.transform.localScale = Vector3.one * (heavy ? 0.40f : 0.30f);
                    var csr2 = core.AddComponent<SpriteRenderer>();
                    csr2.sprite = a.cellSprite;
                    csr2.color = new Color(tint.r, tint.g, tint.b, 0.85f);
                    csr2.sortingOrder = OrderFloorCell + 3;

                    if (heavy)   // cross-bracing = "needs real weight"
                    {
                        var dark = new Color(0.10f, 0.07f, 0.02f, 0.55f);
                        Bar(sw.transform, a.cellSprite, dark, OrderFloorCell + 4,
                            Vector2.zero, 0.52f, 0.06f, 45f);
                        Bar(sw.transform, a.cellSprite, dark, OrderFloorCell + 4,
                            Vector2.zero, 0.52f, 0.06f, -45f);
                    }
                }
        }

        // A gate: a slatted slab that the game hides while its switch is held, over a permanent
        // frame that stays put — so an OPEN gate still reads as a gateway rather than plain floor.
        static void PaintGates(Transform root, PRoom room, BoardAssets a, bool[,] cells, Color tint,
                               Dictionary<(int, Vector2Int), GameObject> reg)
        {
            if (cells == null || a.cellSprite == null) return;
            for (int cx = 0; cx < room.width; cx++)
                for (int cy = 0; cy < room.height; cy++)
                {
                    if (!cells[cx, cy]) continue;
                    var cell = new Vector2Int(cx, cy);
                    var at = Cell(room, cell);

                    var frame = new GameObject("GateFrame");     // always visible
                    frame.transform.SetParent(root, false);
                    frame.transform.localPosition = at;
                    var fbed = new GameObject("Bed");
                    fbed.transform.SetParent(frame.transform, false);
                    fbed.transform.localScale = Vector3.one * 0.90f;
                    var fsr3 = fbed.AddComponent<SpriteRenderer>();
                    fsr3.sprite = a.cellSprite;
                    fsr3.color = new Color(tint.r, tint.g, tint.b, 0.14f);
                    fsr3.sortingOrder = OrderFloorCell + 1;

                    var slab = new GameObject("Gate");           // hidden while the switch is held
                    slab.transform.SetParent(root, false);
                    slab.transform.localPosition = at;

                    var body = new GameObject("Body");
                    body.transform.SetParent(slab.transform, false);
                    body.transform.localScale = Vector3.one * TileSize;
                    var bsr4 = body.AddComponent<SpriteRenderer>();
                    bsr4.sprite = a.cellSprite;
                    bsr4.color = Darken(tint, 0.42f);
                    bsr4.sortingOrder = OrderWall;

                    for (int k = -1; k <= 1; k++)                // slats = a barrier, not a floor tile
                        Bar(slab.transform, a.cellSprite, new Color(tint.r, tint.g, tint.b, 0.80f),
                            OrderWall + 1, new Vector2(0f, k * 0.24f), 0.74f, 0.11f, 0f);

                    if (reg != null) reg[(room.id, cell)] = slab;
                }
        }

        enum PadMotif { ChevronUp, Grains, Bars, Turn, Poles }

        // One tinted pad with a motif on it. Every chapter-4 terrain type is a variation of this,
        // so they all read as belonging to the same family while staying tellable apart.
        static void PaintPad(Transform root, PRoom room, BoardAssets a, bool[,] cells,
                             Color tint, PadMotif motif)
        {
            if (cells == null || a.cellSprite == null) return;
            for (int cx = 0; cx < room.width; cx++)
                for (int cy = 0; cy < room.height; cy++)
                {
                    if (!cells[cx, cy]) continue;
                    var pad = new GameObject(motif.ToString());
                    pad.transform.SetParent(root, false);
                    pad.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                    var bed = new GameObject("Bed");
                    bed.transform.SetParent(pad.transform, false);
                    bed.transform.localScale = Vector3.one * 0.90f;
                    var bsr = bed.AddComponent<SpriteRenderer>();
                    bsr.sprite = a.cellSprite;
                    bsr.color = new Color(tint.r, tint.g, tint.b, 0.26f);
                    bsr.sortingOrder = OrderFloorCell + 1;

                    int order = OrderFloorCell + 2;
                    switch (motif)
                    {
                        case PadMotif.ChevronUp:      // cargo rises here
                            for (int k = 0; k < 2; k++)
                            {
                                var ch = Chevron(pad.transform, a.cellSprite, tint, order, Vector2Int.up, 0.80f);
                                var dr = ch.AddComponent<Drifter>();
                                dr.dir = Vector2.up;
                                dr.phase = k * 0.5f;
                            }
                            break;
                        case PadMotif.Grains:         // loose, shifting footing
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(-0.18f, -0.10f), 0.22f, 0.09f, 0f);
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(0.14f, 0.06f), 0.26f, 0.09f, 0f);
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(-0.02f, 0.22f), 0.18f, 0.08f, 0f);
                            break;
                        case PadMotif.Bars:           // a one-way trap for cargo
                            for (int k = -1; k <= 1; k++)
                                Bar(pad.transform, a.cellSprite, tint, order, new Vector2(k * 0.24f, 0f), 0.68f, 0.10f, 90f);
                            break;
                        case PadMotif.Turn:           // a quarter turn clockwise
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(-0.06f, 0.16f), 0.34f, 0.10f, 0f);
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(0.16f, -0.04f), 0.34f, 0.10f, 90f);
                            var head = Chevron(pad.transform, a.cellSprite, tint, order + 1, Vector2Int.down, 0.62f);
                            head.transform.localPosition = new Vector3(0.16f, -0.24f, 0f);
                            break;
                        case PadMotif.Poles:          // it pulls
                            if (a.glowSprite != null)
                            {
                                var gl = new GameObject("Field");
                                gl.transform.SetParent(pad.transform, false);
                                gl.transform.localScale = Vector3.one * 1.25f;
                                var glr = gl.AddComponent<SpriteRenderer>();
                                glr.sprite = a.glowSprite;
                                glr.color = new Color(tint.r, tint.g, tint.b, 0.34f);
                                glr.sortingOrder = OrderFloorCell + 1;
                            }
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(0f, 0.14f), 0.46f, 0.16f, 0f);
                            Bar(pad.transform, a.cellSprite, Lighten(tint, 0.55f), order,
                                new Vector2(0f, -0.14f), 0.46f, 0.16f, 0f);
                            break;
                    }
                }
        }

        static void PaintRoom(Transform root, PRoom room, BoardAssets a, BoardTiles tiles)
        {
            Color floorC = RoomColor(a.roomColors, room.id);
            bool main = room.id == 0;
            const float rim = 0.07f;

            // Keep the room edge nearly invisible: the cell gutter already defines the playable
            // boundary. A large glow/shadow here expands into four soft bands when the camera zooms,
            // especially in the tutorial and inside nested rooms.
            if (main && !a.hideFrame)
            {
                var frameBase = Object.Instantiate(a.floorPrefab, root);
                frameBase.name = "RoomFrame";
                frameBase.transform.localPosition = Vector3.zero;
                var brsr = frameBase.GetComponent<SpriteRenderer>();
                brsr.drawMode = SpriteDrawMode.Sliced;
                brsr.size = new Vector2(room.width + rim * 2f, room.height + rim * 2f);
                brsr.color = Color.Lerp(Darken(floorC, 0.58f), a.frameColor, 0.16f);
                brsr.sortingOrder = OrderFloorBase - 1;
            }

            // the base is the DARK GUTTER the tiles sit on — it shows between cells as bold grid lines
            var floor = Object.Instantiate(a.floorPrefab, root);
            floor.transform.localPosition = Vector3.zero;
            var fsr = floor.GetComponent<SpriteRenderer>();
            fsr.drawMode = SpriteDrawMode.Sliced;
            fsr.size = new Vector2(room.width, room.height);
            fsr.color = a.gutterColor.a > 0f ? a.gutterColor : Darken(floorC, 0.55f);
            fsr.sortingOrder = OrderFloorBase;

            if (a.floorTex != null && a.floorTexTint.a > 0f)
            {
                var texGO = new GameObject("FloorTex");
                texGO.transform.SetParent(root, false);
                var tsr = texGO.AddComponent<SpriteRenderer>();
                tsr.sprite = a.floorTex;
                tsr.drawMode = SpriteDrawMode.Tiled;
                tsr.size = new Vector2(room.width, room.height);
                tsr.color = a.floorTexTint;
                tsr.sortingOrder = OrderFloorBase;
            }

            // BOLD grid: each cell is a solid, vibrant tile (the room's colour) sitting on the dark
            // gutter base — the dark gaps between tiles ARE the grid. Strong contrast, no texture.
            if (a.cellSprite != null)
            {
                Color cellCol = floorC;
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        var cellGO = new GameObject("Cell");
                        cellGO.transform.SetParent(root, false);
                        cellGO.transform.localPosition = Cell(room, new Vector2Int(cx, cy));
                        cellGO.transform.localScale = Vector3.one * 0.90f;   // inset → clean grid gutters + negative space
                        var csr = cellGO.AddComponent<SpriteRenderer>();
                        csr.sprite = a.cellSprite;
                        csr.color = cellCol;
                        csr.sortingOrder = OrderFloorCell;
                    }
            }
            else
            {
                var grid = Object.Instantiate(a.gridPrefab, root);
                grid.transform.localPosition = Vector3.zero;
                var gsr = grid.GetComponent<SpriteRenderer>();
                gsr.drawMode = SpriteDrawMode.Tiled;
                gsr.size = new Vector2(room.width, room.height);
                gsr.color = a.gridColor;
                gsr.sortingOrder = OrderFloorCell;
            }

            // Trench pits: a dark recessed tile over each trench cell, drawn ABOVE the bright floor
            // cell. Each is registered so the game can hide it the instant a rock fills the gap — the
            // bright cell beneath then reads as ordinary floor, which is exactly what a filled trench is.
            if (room.trench != null && a.cellSprite != null)
            {
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.trench[cx, cy]) continue;
                        var pit = new GameObject("Pit");
                        pit.transform.SetParent(root, false);
                        pit.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        var fill = new GameObject("PitFill");
                        fill.transform.SetParent(pit.transform, false);
                        fill.transform.localScale = Vector3.one * 0.90f;
                        var fsr2 = fill.AddComponent<SpriteRenderer>();
                        fsr2.sprite = a.cellSprite;
                        fsr2.color = new Color(0.02f, 0.04f, 0.07f, 1f);   // a near-black gap
                        fsr2.sortingOrder = OrderFloorCell + 1;

                        if (a.glowSprite != null)   // soft inner shadow → the gap reads as depth, not a flat dark tile
                        {
                            var sh = new GameObject("PitShadow");
                            sh.transform.SetParent(pit.transform, false);
                            sh.transform.localScale = Vector3.one * 0.80f;
                            var shr = sh.AddComponent<SpriteRenderer>();
                            shr.sprite = a.glowSprite;
                            shr.color = new Color(0f, 0f, 0f, 0.55f);
                            shr.sortingOrder = OrderFloorCell + 2;
                        }

                        if (tiles != null) tiles.pits[(room.id, new Vector2Int(cx, cy))] = pit;
                    }
            }

            // Ice: a pale frosted tile over the floor cell, so a slippery run is obvious at a glance.
            // Drawn above the floor cell and below entities, like the pit.
            if (room.ice != null && a.cellSprite != null)
            {
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.ice[cx, cy]) continue;
                        var ice = new GameObject("Ice");
                        ice.transform.SetParent(root, false);
                        ice.transform.localPosition = Cell(room, new Vector2Int(cx, cy));
                        ice.transform.localScale = Vector3.one * 0.90f;
                        var isr = ice.AddComponent<SpriteRenderer>();
                        isr.sprite = a.cellSprite;
                        isr.color = new Color(0.80f, 0.94f, 1f, 0.92f);   // frosted pale blue
                        isr.sortingOrder = OrderFloorCell + 1;

                        var sheen = new GameObject("IceSheen");           // a bright streak = "slippery"
                        sheen.transform.SetParent(ice.transform, false);
                        sheen.transform.localScale = new Vector3(0.62f, 0.16f, 1f);
                        sheen.transform.localPosition = new Vector3(-0.06f, 0.14f, 0f);
                        var ssr = sheen.AddComponent<SpriteRenderer>();
                        ssr.sprite = a.cellSprite;
                        ssr.color = new Color(1f, 1f, 1f, 0.55f);
                        ssr.sortingOrder = OrderFloorCell + 2;
                    }
            }

            // Currents: a tinted lane with chevrons drifting along it, so the flow direction reads
            // at a glance and, more importantly, reads as MOVING — a static arrow looks decorative.
            if (room.current != null && a.cellSprite != null)
            {
                Color lane = new Color(0.24f, 0.72f, 0.86f, 0.30f);
                Color arrow = new Color(0.78f, 0.98f, 1f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        var d = room.current[cx, cy];
                        if (d == Vector2Int.zero) continue;
                        var cell = new Vector2Int(cx, cy);

                        var lz = new GameObject("Current");
                        lz.transform.SetParent(root, false);
                        lz.transform.localPosition = Cell(room, cell);

                        var bed = new GameObject("Lane");
                        bed.transform.SetParent(lz.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var bsr2 = bed.AddComponent<SpriteRenderer>();
                        bsr2.sprite = a.cellSprite;
                        bsr2.color = lane;
                        bsr2.sortingOrder = OrderFloorCell + 1;

                        // Two chevrons half a cycle apart = a continuous stream through the cell.
                        for (int k = 0; k < 2; k++)
                        {
                            var ch2 = Chevron(lz.transform, a.cellSprite, arrow,
                                              OrderFloorCell + 2, d, 0.86f);
                            var dr = ch2.AddComponent<Drifter>();
                            dr.dir = new Vector2(d.x, d.y);
                            dr.phase = k * 0.5f;
                        }
                    }
            }

            // One-way flow: a bold static chevron plus a bar across the entry it refuses, so it
            // reads as a valve — clearly different from the soft, moving current lanes.
            if (room.oneway != null && a.cellSprite != null)
            {
                Color amber = new Color(1f, 0.78f, 0.34f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        var d = room.oneway[cx, cy];
                        if (d == Vector2Int.zero) continue;

                        var ow = new GameObject("OneWay");
                        ow.transform.SetParent(root, false);
                        ow.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        var bed = new GameObject("Bed");
                        bed.transform.SetParent(ow.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var bsr3 = bed.AddComponent<SpriteRenderer>();
                        bsr3.sprite = a.cellSprite;
                        bsr3.color = new Color(amber.r, amber.g, amber.b, 0.18f);
                        bsr3.sortingOrder = OrderFloorCell + 1;

                        // A pair of stacked chevrons — unmistakably "this way only".
                        var c1 = Chevron(ow.transform, a.cellSprite, amber, OrderFloorCell + 2, d, 1f);
                        c1.transform.localPosition = new Vector3(d.x * 0.13f, d.y * 0.13f, 0f);
                        var c2 = Chevron(ow.transform, a.cellSprite,
                                         new Color(amber.r, amber.g, amber.b, 0.45f),
                                         OrderFloorCell + 2, d, 1f);
                        c2.transform.localPosition = new Vector3(-d.x * 0.13f, -d.y * 0.13f, 0f);
                    }
            }

            // Cracked coral: an intact warm tile with fracture lines, plus the hole it leaves —
            // both registered so the game can swap them the instant the coral gives way.
            if (room.cracked != null && a.cellSprite != null)
            {
                Color coralC = new Color(0.90f, 0.52f, 0.44f, 0.95f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.cracked[cx, cy]) continue;
                        var cell = new Vector2Int(cx, cy);
                        var at = Cell(room, cell);

                        var intact = new GameObject("Coral");
                        intact.transform.SetParent(root, false);
                        intact.transform.localPosition = at;

                        var slab = new GameObject("Slab");
                        slab.transform.SetParent(intact.transform, false);
                        slab.transform.localScale = Vector3.one * 0.90f;
                        var ssr2 = slab.AddComponent<SpriteRenderer>();
                        ssr2.sprite = a.cellSprite;
                        ssr2.color = coralC;
                        ssr2.sortingOrder = OrderFloorCell + 1;

                        // Three fracture lines — the visual promise that this tile is about to go.
                        Color fissure = new Color(0.18f, 0.10f, 0.12f, 0.72f);
                        Bar(intact.transform, a.cellSprite, fissure, OrderFloorCell + 2,
                            new Vector2(-0.10f, 0.06f), 0.44f, 0.055f, 22f);
                        Bar(intact.transform, a.cellSprite, fissure, OrderFloorCell + 2,
                            new Vector2(0.14f, -0.12f), 0.30f, 0.050f, -38f);
                        Bar(intact.transform, a.cellSprite, fissure, OrderFloorCell + 2,
                            new Vector2(0.02f, 0.20f), 0.22f, 0.045f, -66f);

                        var hole = new GameObject("Rubble");
                        hole.transform.SetParent(root, false);
                        hole.transform.localPosition = at;
                        var hf = new GameObject("HoleFill");
                        hf.transform.SetParent(hole.transform, false);
                        hf.transform.localScale = Vector3.one * 0.90f;
                        var hsr = hf.AddComponent<SpriteRenderer>();
                        hsr.sprite = a.cellSprite;
                        hsr.color = new Color(0.02f, 0.04f, 0.07f, 1f);
                        hsr.sortingOrder = OrderFloorCell + 1;
                        if (a.glowSprite != null)
                        {
                            var hs = new GameObject("HoleShadow");
                            hs.transform.SetParent(hole.transform, false);
                            hs.transform.localScale = Vector3.one * 0.80f;
                            var hssr = hs.AddComponent<SpriteRenderer>();
                            hssr.sprite = a.glowSprite;
                            hssr.color = new Color(0f, 0f, 0f, 0.55f);
                            hssr.sortingOrder = OrderFloorCell + 2;
                        }
                        hole.SetActive(false);      // intact until the game says otherwise

                        if (tiles != null)
                        {
                            tiles.coral[(room.id, cell)] = intact;
                            tiles.rubble[(room.id, cell)] = hole;
                        }
                    }
            }

            // Switches and their gates share a colour so the link is readable without a legend:
            // green shells open green gates, amber plates open amber gates.
            Color shellC = new Color(0.42f, 0.95f, 0.62f, 1f);
            Color plateC = new Color(1f, 0.74f, 0.30f, 1f);
            PaintSwitches(root, room, a, room.button, shellC, false);
            PaintSwitches(root, room, a, room.plate, plateC, true);
            PaintGates(root, room, a, room.gate, shellC, tiles == null ? null : tiles.gates);
            PaintGates(root, room, a, room.heavyGate, plateC, tiles == null ? null : tiles.heavyGates);

            // Kelp: a dense frond thicket. Deliberately drawn as growth rather than a barrier,
            // because it stops crates but not the diver — it must not read as wall.
            if (room.kelp != null && a.cellSprite != null)
            {
                Color weed = new Color(0.18f, 0.62f, 0.36f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.kelp[cx, cy]) continue;
                        var kp = new GameObject("Kelp");
                        kp.transform.SetParent(root, false);
                        kp.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        var bed = new GameObject("Bed");
                        bed.transform.SetParent(kp.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var bsr5 = bed.AddComponent<SpriteRenderer>();
                        bsr5.sprite = a.cellSprite;
                        bsr5.color = new Color(weed.r, weed.g, weed.b, 0.30f);
                        bsr5.sortingOrder = OrderFloorCell + 1;

                        // Four fronds at alternating leans — organic, never a grid.
                        float[] xs = { -0.26f, -0.08f, 0.10f, 0.27f };
                        float[] tilt = { -13f, 9f, -7f, 14f };
                        for (int k = 0; k < xs.Length; k++)
                            Bar(kp.transform, a.cellSprite,
                                Color.Lerp(weed, Color.white, 0.10f + k * 0.09f), OrderFloorCell + 2,
                                new Vector2(xs[k], -0.04f), 0.62f, 0.11f, 90f + tilt[k]);
                    }
            }

            // Deep water: a dark hollow with no floor cell reading — you can swim it, a crate can't
            // survive it. Darker and softer-edged than a trench, which is a hard-lipped gap.
            if (room.deep != null && a.cellSprite != null)
            {
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.deep[cx, cy]) continue;
                        var dp = new GameObject("Deep");
                        dp.transform.SetParent(root, false);
                        dp.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        var fill = new GameObject("Fill");
                        fill.transform.SetParent(dp.transform, false);
                        fill.transform.localScale = Vector3.one * 0.90f;
                        var dsr = fill.AddComponent<SpriteRenderer>();
                        dsr.sprite = a.cellSprite;
                        dsr.color = new Color(0.02f, 0.11f, 0.22f, 1f);
                        dsr.sortingOrder = OrderFloorCell + 1;

                        if (a.glowSprite != null)   // the sense of depth, not just a dark square
                        {
                            var vg2 = new GameObject("Depth");
                            vg2.transform.SetParent(dp.transform, false);
                            vg2.transform.localScale = Vector3.one * 0.86f;
                            var vsr2 = vg2.AddComponent<SpriteRenderer>();
                            vsr2.sprite = a.glowSprite;
                            vsr2.color = new Color(0f, 0f, 0f, 0.6f);
                            vsr2.sortingOrder = OrderFloorCell + 2;
                        }
                    }
            }

            // Geysers: an upwelling ring — the launch pad. Bobbing, so it reads as pressure.
            if (room.geyser != null && a.ringSprite != null)
            {
                Color jet = new Color(0.72f, 0.95f, 1f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.geyser[cx, cy]) continue;
                        var gy = new GameObject("Geyser");
                        gy.transform.SetParent(root, false);
                        gy.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        if (a.glowSprite != null)
                        {
                            var gl2 = new GameObject("Plume");
                            gl2.transform.SetParent(gy.transform, false);
                            gl2.transform.localScale = Vector3.one * 1.05f;
                            var glr2 = gl2.AddComponent<SpriteRenderer>();
                            glr2.sprite = a.glowSprite;
                            glr2.color = new Color(jet.r, jet.g, jet.b, 0.34f);
                            glr2.sortingOrder = OrderFloorCell + 1;
                        }

                        var bub = new GameObject("Bubbles");
                        bub.transform.SetParent(gy.transform, false);
                        bub.AddComponent<Bobber>();
                        for (int r2 = 0; r2 < 3; r2++)
                        {
                            var ring = new GameObject("Ring" + r2);
                            ring.transform.SetParent(bub.transform, false);
                            ring.transform.localScale = Vector3.one * (0.34f + r2 * 0.20f);
                            var rr2 = ring.AddComponent<SpriteRenderer>();
                            rr2.sprite = a.ringSprite;
                            rr2.color = new Color(jet.r, jet.g, jet.b, 0.85f - r2 * 0.22f);
                            rr2.sortingOrder = OrderFloorCell + 2 + r2;
                        }
                    }
            }

            // Pearls and their locks share the same pale gold, so which key opens what is obvious
            // without a legend. Both are registered — collecting the last pearl clears every lock.
            Color pearlC = new Color(1f, 0.94f, 0.72f, 1f);
            if (room.key != null && a.cellSprite != null)
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.key[cx, cy]) continue;
                        var cell = new Vector2Int(cx, cy);
                        var pl = new GameObject("Pearl");
                        pl.transform.SetParent(root, false);
                        pl.transform.localPosition = Cell(room, cell);
                        pl.AddComponent<Bobber>();

                        if (a.glowSprite != null)
                        {
                            var g3 = new GameObject("Shine");
                            g3.transform.SetParent(pl.transform, false);
                            g3.transform.localScale = Vector3.one * 0.95f;
                            var g3r = g3.AddComponent<SpriteRenderer>();
                            g3r.sprite = a.glowSprite;
                            g3r.color = new Color(pearlC.r, pearlC.g, pearlC.b, 0.45f);
                            g3r.sortingOrder = OrderFloorCell + 1;
                        }
                        if (a.ringSprite != null)
                        {
                            var rg = new GameObject("Ring");
                            rg.transform.SetParent(pl.transform, false);
                            rg.transform.localScale = Vector3.one * 0.46f;
                            var rgr = rg.AddComponent<SpriteRenderer>();
                            rgr.sprite = a.ringSprite;
                            rgr.color = pearlC;
                            rgr.sortingOrder = OrderFloorCell + 3;
                        }
                        var core2 = new GameObject("Core");
                        core2.transform.SetParent(pl.transform, false);
                        core2.transform.localScale = Vector3.one * 0.30f;
                        var c2r = core2.AddComponent<SpriteRenderer>();
                        c2r.sprite = a.cellSprite;
                        c2r.color = pearlC;
                        c2r.sortingOrder = OrderFloorCell + 2;

                        if (tiles != null) tiles.pearls[(room.id, cell)] = pl;
                    }

            PaintGates(root, room, a, room.locked, pearlC, tiles == null ? null : tiles.locks);

            // Narrow gap: two heavy jaws pinching the cell almost shut. It has to read as "too
            // tight for me" rather than "wall", because a crate goes straight through it.
            if (room.gap != null && a.cellSprite != null)
            {
                Color jaw = new Color(0.62f, 0.66f, 0.74f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.gap[cx, cy]) continue;
                        var gp = new GameObject("Gap");
                        gp.transform.SetParent(root, false);
                        gp.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        for (int k = -1; k <= 1; k += 2)
                        {
                            var jawGO = new GameObject("Jaw");
                            jawGO.transform.SetParent(gp.transform, false);
                            jawGO.transform.localPosition = new Vector3(0f, k * 0.36f, 0f);
                            jawGO.transform.localScale = new Vector3(0.90f, 0.24f, 1f);
                            var jsr = jawGO.AddComponent<SpriteRenderer>();
                            jsr.sprite = a.cellSprite;
                            jsr.color = jaw;
                            jsr.sortingOrder = OrderWall;

                            var lip = new GameObject("Lip");     // a lit edge = a hard rim
                            lip.transform.SetParent(gp.transform, false);
                            lip.transform.localPosition = new Vector3(0f, k * 0.25f, 0f);
                            lip.transform.localScale = new Vector3(0.90f, 0.05f, 1f);
                            var lsr = lip.AddComponent<SpriteRenderer>();
                            lsr.sprite = a.cellSprite;
                            lsr.color = Lighten(jaw, 0.45f);
                            lsr.sortingOrder = OrderWall + 1;
                        }
                    }
            }

            // Gravity zone: chevrons drifting downward. Same visual grammar as the currents, which
            // is the point — this is a current that only cargo feels.
            if (room.gravity != null && a.cellSprite != null)
            {
                Color pull = new Color(0.64f, 0.48f, 0.92f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.gravity[cx, cy]) continue;
                        var gz = new GameObject("Gravity");
                        gz.transform.SetParent(root, false);
                        gz.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        var bed = new GameObject("Bed");
                        bed.transform.SetParent(gz.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var gbs = bed.AddComponent<SpriteRenderer>();
                        gbs.sprite = a.cellSprite;
                        gbs.color = new Color(pull.r, pull.g, pull.b, 0.22f);
                        gbs.sortingOrder = OrderFloorCell + 1;

                        for (int k = 0; k < 2; k++)
                        {
                            var ch3 = Chevron(gz.transform, a.cellSprite, pull,
                                              OrderFloorCell + 2, Vector2Int.down, 0.80f);
                            var dr2 = ch3.AddComponent<Drifter>();
                            dr2.dir = Vector2.down;
                            dr2.phase = k * 0.5f;
                        }
                    }
            }

            // Breakable rock: a chunky block with a fracture through it, so it reads as "hit this"
            // rather than "wall". Registered, because a crate turns it into open floor.
            if (room.rock != null && a.cellSprite != null)
            {
                Color stone = new Color(0.52f, 0.44f, 0.40f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.rock[cx, cy]) continue;
                        var cell = new Vector2Int(cx, cy);
                        var rk = new GameObject("Rock");
                        rk.transform.SetParent(root, false);
                        rk.transform.localPosition = Cell(room, cell);

                        var body = new GameObject("Body");
                        body.transform.SetParent(rk.transform, false);
                        body.transform.localScale = Vector3.one * TileSize;
                        var rsr = body.AddComponent<SpriteRenderer>();
                        rsr.sprite = a.cellSprite;
                        rsr.color = stone;
                        rsr.sortingOrder = OrderWall;

                        var top = new GameObject("Lit");        // a lit top face = mass
                        top.transform.SetParent(rk.transform, false);
                        top.transform.localPosition = new Vector3(0f, 0.30f, 0f);
                        top.transform.localScale = new Vector3(TileSize, 0.22f, 1f);
                        var tsr = top.AddComponent<SpriteRenderer>();
                        tsr.sprite = a.cellSprite;
                        tsr.color = Lighten(stone, 0.30f);
                        tsr.sortingOrder = OrderWall + 1;

                        Color seam = new Color(0.12f, 0.09f, 0.08f, 0.85f);
                        Bar(rk.transform, a.cellSprite, seam, OrderWall + 2, new Vector2(-0.06f, 0.02f), 0.50f, 0.07f, 74f);
                        Bar(rk.transform, a.cellSprite, seam, OrderWall + 2, new Vector2(0.10f, -0.18f), 0.28f, 0.06f, 28f);

                        if (tiles != null) tiles.rocks[(room.id, cell)] = rk;
                    }
            }

            // Toggle switch: a pad with a lever. The lit half is registered, so throwing the switch
            // visibly latches — the board tells you the state without a word of text.
            if (room.toggle != null && a.cellSprite != null)
            {
                Color lever = new Color(1f, 0.86f, 0.42f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.toggle[cx, cy]) continue;
                        var cell = new Vector2Int(cx, cy);
                        var tg = new GameObject("Toggle");
                        tg.transform.SetParent(root, false);
                        tg.transform.localPosition = Cell(room, cell);

                        var bed = new GameObject("Bed");
                        bed.transform.SetParent(tg.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var tbs = bed.AddComponent<SpriteRenderer>();
                        tbs.sprite = a.cellSprite;
                        tbs.color = new Color(lever.r, lever.g, lever.b, 0.18f);
                        tbs.sortingOrder = OrderFloorCell + 1;

                        Bar(tg.transform, a.cellSprite, Darken(lever, 0.35f), OrderFloorCell + 2,
                            Vector2.zero, 0.56f, 0.14f, 0f);

                        var lit = new GameObject("Thrown");   // shown only once it is latched
                        lit.transform.SetParent(tg.transform, false);
                        var lsr2 = lit.AddComponent<SpriteRenderer>();
                        lsr2.sprite = a.cellSprite;
                        lsr2.color = lever;
                        lit.transform.localScale = new Vector3(0.30f, 0.30f, 1f);
                        lsr2.sortingOrder = OrderFloorCell + 3;
                        lit.SetActive(false);

                        if (tiles != null) tiles.toggleOn[(room.id, cell)] = lit;
                    }
            }

            // Latch gates wear the switch's amber; pulsing gates wear a cold cyan and blink.
            PaintGates(root, room, a, room.latch, new Color(1f, 0.86f, 0.42f, 1f),
                       tiles == null ? null : tiles.latches);
            PaintGates(root, room, a, room.pulse, new Color(0.46f, 0.90f, 0.96f, 1f),
                       tiles == null ? null : tiles.pulses);

            // Chapter-4 terrain. These are all "a tinted pad with a motif on it", so they share one
            // table-driven pass rather than nine near-identical blocks. Colour carries the meaning:
            // violet pulls, green lifts, sand is dun, cages are iron, deflectors and magnets glow.
            PaintPad(root, room, a, room.updraft,   new Color(0.55f, 0.95f, 0.66f, 1f), PadMotif.ChevronUp);
            PaintPad(root, room, a, room.sand,      new Color(0.86f, 0.78f, 0.56f, 1f), PadMotif.Grains);
            PaintPad(root, room, a, room.sticky,    new Color(0.95f, 0.62f, 0.86f, 1f), PadMotif.Grains);
            PaintPad(root, room, a, room.cage,      new Color(0.62f, 0.66f, 0.74f, 1f), PadMotif.Bars);
            PaintPad(root, room, a, room.deflector, new Color(0.72f, 0.86f, 1f, 1f),    PadMotif.Turn);
            PaintPad(root, room, a, room.magnet,    new Color(1f, 0.45f, 0.45f, 1f),    PadMotif.Poles);
            PaintPad(root, room, a, room.swap,      new Color(0.86f, 0.62f, 1f, 1f),    PadMotif.Turn);

            // Whirlpool portals: a spinning target of concentric rings + a glow, drawn ABOVE the floor
            // cell and BELOW entities (so the player/box stands on it). Static — portals never change.
            if (room.portal != null && a.ringSprite != null)
            {
                Color pc = new Color(0.32f, 0.86f, 0.98f, 1f);   // whirlpool cyan
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.portal[cx, cy]) continue;
                        var portal = new GameObject("Portal");
                        portal.transform.SetParent(root, false);
                        portal.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                        if (a.glowSprite != null)
                        {
                            var gl = new GameObject("PortalGlow");
                            gl.transform.SetParent(portal.transform, false);
                            gl.transform.localScale = Vector3.one * 1.15f;
                            var glr = gl.AddComponent<SpriteRenderer>();
                            glr.sprite = a.glowSprite;
                            glr.color = new Color(pc.r, pc.g, pc.b, 0.35f);
                            glr.sortingOrder = OrderFloorCell + 1;
                        }

                        var rings = new GameObject("PortalRings");
                        rings.transform.SetParent(portal.transform, false);
                        rings.AddComponent<Spinner>();
                        for (int r = 0; r < 3; r++)
                        {
                            var ring = new GameObject("Ring" + r);
                            ring.transform.SetParent(rings.transform, false);
                            ring.transform.localScale = Vector3.one * (0.78f - r * 0.20f);
                            var rr = ring.AddComponent<SpriteRenderer>();
                            rr.sprite = a.ringSprite;
                            rr.color = Color.Lerp(pc, Color.white, r * 0.28f);
                            rr.sortingOrder = OrderFloorCell + 2 + r;
                        }
                    }
            }

            for (int x = 0; x < room.width; x++)
                for (int y = 0; y < room.height; y++)
                    if (room.wall[x, y])
                        PaintWall(root, room, x, y, a);

            foreach (var g in room.boxGoals)
            {
                var go = Object.Instantiate(a.boxGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = a.boxColor;
            }
            foreach (var g in room.playerGoals)
            {
                var go = Object.Instantiate(a.playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = a.playerColor;
            }
            // The echo's goal: the diver marker in the echo's own pale blue, so the pair reads as
            // "this one is for the other you".
            Color echoC = Color.Lerp(a.playerColor, new Color(0.72f, 0.80f, 1f, 1f), 0.55f);
            Color mirrorC = Color.Lerp(a.playerColor, new Color(1f, 0.72f, 0.86f, 1f), 0.6f);
            foreach (var g in room.mirrorGoals)
            {
                var go = Object.Instantiate(a.playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = mirrorC;
            }
            foreach (var g in room.echoGoals)
            {
                var go = Object.Instantiate(a.playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = echoC;
            }
            // A coloured goal wears exactly the hue of the one crate that satisfies it.
            foreach (var (cell, colour) in room.colourGoals)
            {
                var go = Object.Instantiate(a.boxGoalPrefab, root);
                go.transform.localPosition = Cell(room, cell);
                SetOrder(go, OrderGoal);
                Color gc = CrateColour(a.boxColor, colour);
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = gc;
            }
        }

        static bool IsWall(PRoom room, int x, int y)
            => x >= 0 && y >= 0 && x < room.width && y < room.height && room.wall[x, y];

        // Walls are the board's ARCHITECTURE, not objects sitting on it, and they're drawn to say so:
        //
        //  * full-bleed (scale 1, vs the floor cells' 0.90) so neighbouring walls merge into one
        //    continuous mass with no internal seams — a 4-cell divider reads as one slab, not four
        //    blocks. The floor keeps its grid gutters, so the contrast reads as masonry on tiling.
        //  * lit and shadowed PER MASS, not per cell: a stack throws a single shadow where it meets
        //    the floor and catches a single highlight on the face the light actually reaches. Doing
        //    this per-cell is what made walls look like scattered tiles.
        //  * no per-tile outline, for the same reason — the silhouette comes from the mass itself.
        static void PaintWall(Transform root, PRoom room, int x, int y, BoardAssets a)
        {
            Vector3 p = Cell(room, new Vector2Int(x, y));
            bool below = IsWall(room, x, y - 1);
            bool above = IsWall(room, x, y + 1);
            bool boundary = x == 0 || y == 0 || x == room.width - 1 || y == room.height - 1;

            // Keep every visual layer of one wall cell together. Besides making the hierarchy
            // clearer, this lets recursive-box previews hide perimeter walls without leaving their
            // shadows or highlight caps behind as black/grey fragments.
            var wallRoot = new GameObject(boundary ? "BoundaryWall" : "Wall").transform;
            wallRoot.SetParent(root, false);
            wallRoot.localPosition = p;

            // cast shadow only where the mass actually meets open floor
            if (!below && a.glowSprite != null)
            {
                var sh = new GameObject("WallShadow");
                sh.transform.SetParent(wallRoot, false);
                sh.transform.localPosition = new Vector3(0f, -0.34f, 0f);
                sh.transform.localScale = new Vector3(1.5f, 0.85f, 1f);
                var ssr = sh.AddComponent<SpriteRenderer>();
                ssr.sprite = a.glowSprite;
                ssr.color = new Color(0f, 0f, 0f, 0.5f);
                ssr.sortingOrder = OrderWall - 1;
            }

            var w = Object.Instantiate(a.wallPrefab, wallRoot);
            w.transform.localPosition = Vector3.zero;
            SetOrder(w, OrderWall);

            var body = w.transform.Find("Sprite");
            if (body != null)
            {
                body.localScale = Vector3.one;   // full-bleed → adjacent walls fuse into one mass
                var bsr = body.GetComponent<SpriteRenderer>();
                if (bsr != null) bsr.color = a.wallColor;
            }
            else
            {
                foreach (var sr in w.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = a.wallColor;
            }

            // the lit top face of the slab — gives the mass real thickness against the floor
            if (!above && a.cellSprite != null)
            {
                var cap = new GameObject("WallCap");
                cap.transform.SetParent(wallRoot, false);
                cap.transform.localPosition = new Vector3(0f, 0.38f, 0f);
                cap.transform.localScale = new Vector3(1f, 0.24f, 1f);
                var csr = cap.AddComponent<SpriteRenderer>();
                csr.sprite = a.cellSprite;
                csr.color = Lighten(a.wallColor, 0.34f);
                csr.sortingOrder = OrderWall + 1;
            }
        }

        public static Color RoomColor(Color[] roomColors, int roomId)
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

        static void AddRim(GameObject piece, string fillChild, Color fillColor, float scale, BoardAssets a)
        {
            if (a.ringSprite == null) return;
            var target = piece.transform.Find(fillChild);
            if (target == null) return;
            var tsr = target.GetComponent<SpriteRenderer>();
            if (tsr == null) return;
            var rim = new GameObject("Rim");
            rim.transform.SetParent(piece.transform, false);
            rim.transform.localScale = Vector3.one * scale;
            var rsr = rim.AddComponent<SpriteRenderer>();
            rsr.sprite = a.ringSprite;
            rsr.color = Lighten(fillColor, 0.45f);
            rsr.sortingOrder = tsr.sortingOrder + 1;
        }

        static void AddGlow(GameObject piece, Color color, int order, float scale, BoardAssets a)
        {
            if (a.glowSprite == null || a.pieceGlow <= 0f) return;
            var g = new GameObject("Glow");
            g.transform.SetParent(piece.transform, false);
            g.transform.localScale = Vector3.one * scale;
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = a.glowSprite;
            sr.color = new Color(color.r, color.g, color.b, a.pieceGlow);
            sr.sortingOrder = order;
        }
    }
}
