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
        // Tutorial RenderTextures shrink the board enough that the normal five-layer contour reads
        // as stacked stray lines. Tutorial boards request one quiet structural edge instead; normal
        // gameplay and the main-menu board keep their existing premium contour treatment.
        public bool simplifyBoundaryContours;
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
        // Crate colours 1..N. Cargo and its goal share both this hue and a matching embossed
        // identity mark, so Chapter V remains readable even when colour alone is hard to judge.
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
        // Every square gameplay object occupies the same visible footprint. The player, cargo,
        // recursive-room shell, player-container and their goals must align exactly when they sit
        // on neighbouring cells; using separate scales made the recursive room look like a larger
        // gameplay piece even though it still occupies only one logical cell.
        const float OptionOneObjectSize = 0.90f;
        const float NestedShellSize = OptionOneObjectSize;
        // Keep the live miniature at the proven readable fit. Its size is independent of the
        // shell's outer footprint, so normalising the square objects cannot enlarge or clip it.
        const float NestedPreviewFit = 0.72f;
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
        // The selected skin is the shared visual language for the complete campaign. The logical
        // grid still drives movement, but ordinary cells remain invisible: the room reads as one
        // continuous recessed surface inside a navy/cobalt/cyan cabinet frame.
        static bool UsesOptionOneSkin(BoardAssets a) => a != null;

        static readonly Color OptionOneFloor = new Color(0.025f, 0.075f, 0.190f, 1f);
        static readonly Color OptionOneWall = new Color(0.030f, 0.045f, 0.125f, 1f);
        static readonly Color OptionOneBevel = new Color(0.065f, 0.105f, 0.285f, 1f);
        static readonly Color OptionOneCyan = new Color(0.090f, 0.760f, 1.000f, 1f);
        static readonly Color OptionOneViolet = new Color(0.510f, 0.190f, 0.950f, 1f);
        // The level-select cards are also the chapter identity. Carry their exact colour family
        // into the controlled diver so the player can recognise the current world immediately:
        // cyan -> blue -> violet -> pink -> coral. Goals, eye rims, trails and landing effects all
        // consume BoardAssets.playerColor, so this single palette stays coherent everywhere.
        static readonly Color ChapterOnePlayer   = new Color(0.188235f, 0.890196f, 0.917647f, 1f); // #30E3EA
        static readonly Color ChapterTwoPlayer   = new Color(0.380392f, 0.658824f, 1.000000f, 1f); // #61A8FF
        static readonly Color ChapterThreePlayer = new Color(0.647059f, 0.423529f, 1.000000f, 1f); // #A56CFF
        static readonly Color ChapterFourPlayer  = new Color(0.917647f, 0.380392f, 0.839216f, 1f); // #EA61D6
        static readonly Color ChapterFivePlayer  = new Color(0.988235f, 0.443137f, 0.603922f, 1f); // #FC719A
        // Some neutral portal ornamentation deliberately keeps the original hot-magenta accent;
        // it is not a player body and therefore should not change identity between chapters.
        static readonly Color OptionOnePlayer = new Color(0.975f, 0.020f, 0.405f, 1f);
        static readonly Color OptionOnePlayerDark = new Color(0.105f, 0.025f, 0.090f, 1f);
        static readonly Color OptionOneBox = new Color(1.000f, 0.590f, 0.075f, 1f);
        static readonly Color OptionOneDepthBlue = new Color(0.035f, 0.145f, 0.360f, 1f);
        static readonly Color OptionOneDepthTeal = new Color(0.025f, 0.205f, 0.285f, 1f);
        static readonly Color OptionOneDepthIndigo = new Color(0.105f, 0.095f, 0.385f, 1f);
        static readonly Color OptionOneDepthViolet = new Color(0.205f, 0.070f, 0.345f, 1f);

        struct BoundaryEdge
        {
            public Vector2Int start;
            public Vector2Int end;
            public bool touchesOutside;

            public BoundaryEdge(Vector2Int start, Vector2Int end, bool touchesOutside)
            {
                this.start = start;
                this.end = end;
                this.touchesOutside = touchesOutside;
            }
        }

        // Builds the full world-space board under a fresh "LevelView" root and returns it. Fills the
        // supplied roomRoots / views dictionaries and snaps every piece into its cell (static board).
        public static Transform Render(LevelModel model, BoardAssets a,
            Dictionary<int, Transform> roomRoots, Dictionary<PEntity, EntityView> views,
            BoardTiles tiles = null)
        {
            ApplyChapterSkin(a);
            var worldRoot = new GameObject("LevelView").transform;
            var goalTargets = new List<GoalFeedbackFx.Target>();

            foreach (var room in model.rooms.Values)
            {
                var root = new GameObject("Room_" + room.id).transform;
                root.SetParent(worldRoot, false);
                roomRoots[room.id] = root;
                PaintRoom(root, room, a, tiles, model, goalTargets);
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

                // Player.prefab is also reused by echoes and the passive player bodies in Chapter
                // V. Only the controlled model.player receives the expressive move and eye cadence.
                bool controlledPlayer = object.ReferenceEquals(e, model.player);
                var blinker = go.GetComponent<Blinker>();
                if (blinker != null) blinker.enabled = controlledPlayer;

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
                        if (UsesOptionOneSkin(a))
                        {
                            body.localScale = Vector3.one * OptionOneObjectSize;
                            var leftEye = go.transform.Find("EyeL");
                            var rightEye = go.transform.Find("EyeR");
                            if (leftEye != null)
                            {
                                leftEye.localScale = new Vector3(0.16f, 0.22f, 1f);
                                leftEye.localPosition = new Vector3(-0.16f, 0.055f, 0f);
                            }
                            if (rightEye != null)
                            {
                                rightEye.localScale = new Vector3(0.16f, 0.22f, 1f);
                                rightEye.localPosition = new Vector3(0.16f, 0.055f, 0f);
                            }
                            AddEyeOutline(go, leftEye, "EyeOutlineL", diverC, a);
                            AddEyeOutline(go, rightEye, "EyeOutlineR", diverC, a);

                            var playerShadow = go.transform.Find("Shadow");
                            if (playerShadow != null)
                            {
                                playerShadow.localPosition = new Vector3(0.020f, -0.038f, 0f);
                                playerShadow.localScale = Vector3.one * 0.84f;
                                var shadowRenderer = playerShadow.GetComponent<SpriteRenderer>();
                                if (shadowRenderer != null)
                                    shadowRenderer.color = new Color(0f, 0f, 0f, ghost ? 0.07f : 0.13f);
                            }

                            AddSoftBevel(go, body, diverC, a);
                        }
                        // Echo/mirror actors keep a quiet halo so their identity is readable. The
                        // real player uses only the small pooled fragment trail configured below;
                        // it does not use the old full-body coloured halo that obscured tutorials.
                        if (ghost)
                            AddGlow(go, diverC, bsr.sortingOrder - 1, 1.2f, a);
                        view.ConfigureMotionFx(diverC,
                            Mathf.Max(OrderGoal + 1, bsr.sortingOrder - 1), false,
                            controlledPlayer, controlledPlayer);
                    }
                    if (!UsesOptionOneSkin(a))
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
                        if (UsesOptionOneSkin(a))
                        {
                            if (a.cellSprite != null) fsr.sprite = a.cellSprite;
                            fill.localScale = Vector3.one * OptionOneObjectSize;
                        }
                        AddGlow(go, crateC, fsr.sortingOrder - 1, 1.7f, a);
                        view.ConfigureMotionFx(crateC, Mathf.Max(OrderGoal + 1, fsr.sortingOrder - 1), true);
                    }

                    // Keep the inset face tied to the crate hue. The prefab's original amber
                    // panel looked pasted on when a level introduced sky/green crates; a slightly
                    // darker inset preserves the moulded, high-quality block construction in every
                    // colour while the existing gloss remains a neutral specular highlight.
                    var panel = go.transform.Find("Panel");
                    if (panel)
                    {
                        var psr = panel.GetComponent<SpriteRenderer>();
                        panel.localScale = Vector3.one * 0.58f;
                        if (psr != null)
                        {
                            psr.color = Darken(crateC, 0.14f);
                            psr.enabled = true;
                        }
                    }
                    var gloss = go.transform.Find("Gloss");
                    if (gloss != null)
                    {
                        var glossRenderer = gloss.GetComponent<SpriteRenderer>();
                        if (glossRenderer != null) glossRenderer.enabled = false;
                    }
                    var crateShadow = go.transform.Find("Shadow");
                    if (crateShadow != null)
                    {
                        crateShadow.localPosition = new Vector3(0.020f, -0.038f, 0f);
                        crateShadow.localScale = Vector3.one * 0.84f;
                        var shadowRenderer = crateShadow.GetComponent<SpriteRenderer>();
                        if (shadowRenderer != null)
                            shadowRenderer.color = new Color(0f, 0f, 0f, 0.13f);
                    }

                    if (UsesOptionOneSkin(a))
                        AddSoftBevel(go, fill, crateC, a);

                    // Plain cargo uses a quiet inset hatch. Chapter V colour cargo instead receives
                    // a semantic mark (coral sprig, sky wind or green leaf); its target receives the
                    // same mark below, making every pairing understandable without relying on hue.
                    if (a.cellSprite != null && !e.locking && !e.fragile && !e.boulder
                        && !e.slick && !e.anchored)
                    {
                        if (e.colour > 0)
                        {
                            AddColourIdentity(go.transform, a, e.colour, crateC, 60, false);
                        }
                        else
                        {
                            Color hatch = Lighten(crateC, 0.20f);
                            hatch.a = 0.58f;
                            Bar(go.transform, a.cellSprite, hatch, 60,
                                new Vector2(-0.14f, 0.14f), 0.39f, 0.040f, 45f);
                            Bar(go.transform, a.cellSprite, hatch, 60,
                                Vector2.zero, 0.39f, 0.040f, 45f);
                            Bar(go.transform, a.cellSprite, hatch, 60,
                                new Vector2(0.14f, -0.14f), 0.39f, 0.040f, 45f);
                        }
                    }
                    if (!UsesOptionOneSkin(a))
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

                if (e.anchored && e.interiorRoomId < 0 && a.cellSprite != null)   // chains = bolted cargo
                {
                    var dark = new Color(0.16f, 0.14f, 0.12f, 0.9f);
                    Bar(go.transform, a.cellSprite, dark, 62, new Vector2(0f, 0.34f), 0.86f, 0.09f, 0f);
                    Bar(go.transform, a.cellSprite, dark, 62, new Vector2(0f, -0.34f), 0.86f, 0.09f, 0f);
                    for (int k = -1; k <= 1; k += 2)
                        Bar(go.transform, a.cellSprite, dark, 62, new Vector2(k * 0.30f, 0f), 0.72f, 0.07f, 90f);
                }

                if (e.interiorRoomId >= 0)
                {
                    Color metaMotion = !e.anchored && UsesOptionOneSkin(a)
                        ? Lighten(OptionOneBox, 0.18f)
                        : Lighten(
                            RoomColor(a.roomColors, e.interiorRoomId),
                            UsesOptionOneSkin(a) ? 0.42f : 0.30f);
                    var metaFrame = go.transform.Find("Frame");
                    if (UsesOptionOneSkin(a))
                    {
                        // A recursive room is special in function, but it still occupies the same
                        // one-cell visual footprint as every other movable square.
                        if (metaFrame != null)
                            metaFrame.localScale = Vector3.one * NestedShellSize;
                        var metaBacking = go.transform.Find("Backing");
                        if (metaBacking != null)
                            metaBacking.localScale = Vector3.one * NestedShellSize;
                        var metaShadow = go.transform.Find("Shadow");
                        if (metaShadow != null)
                            metaShadow.localScale = Vector3.one * 0.82f;
                    }
                    var metaRenderer = metaFrame != null ? metaFrame.GetComponent<SpriteRenderer>() : null;
                    int order = metaRenderer != null ? metaRenderer.sortingOrder - 1 : OrderGoal + 1;
                    view.ConfigureMotionFx(metaMotion, Mathf.Max(OrderGoal + 1, order), true);

                    if (e.playerContainer)
                        ApplyPlayerContainerSkin(go, a, metaRenderer != null
                            ? metaRenderer.sortingOrder : 50);
                }

                if (e.interiorRoomId >= 1 && model.rooms.ContainsKey(e.interiorRoomId)
                    && nestedRooms.Add(e.interiorRoomId))
                {
                    Color interiorC = RoomColor(a.roomColors, e.interiorRoomId);
                    // Every movable recursive room is cargo: it can be pushed onto the same amber
                    // socket as an ordinary crate. Keep that object/goal identity consistent in
                    // every chapter; anchored enter-only rooms retain their depth colours.
                    Color shellC = e.anchored
                        ? NestedShellColor(e.interiorRoomId)
                        : MovableNestedShellColor(a.chapter);
                    Color shellAccent = e.anchored
                        ? NestedShellAccentColor(e.interiorRoomId)
                        : MovableNestedShellAccentColor(a.chapter);
                    var backing = go.transform.Find("Backing");
                    // Keep the backing: it is the coloured body of the recursive room. It renders
                    // below the live miniature and becomes the large, readable outer chamber when
                    // the camera enters the box.
                    if (backing && !e.playerContainer)
                    {
                        var backingRenderer = backing.GetComponent<SpriteRenderer>();
                        if (backingRenderer != null)
                        {
                            backingRenderer.enabled = true;
                            backingRenderer.color = shellC;
                        }
                    }
                    var shadow = go.transform.Find("Shadow");
                    if (shadow)
                    {
                        var shadowRenderer = shadow.GetComponent<SpriteRenderer>();
                        if (shadowRenderer != null) shadowRenderer.enabled = false;
                        if (e.playerContainer) shadow.gameObject.SetActive(false);
                    }
                    var frame = go.transform.Find("Frame");
                    if (frame)
                    {
                        var frameRenderer = frame.GetComponent<SpriteRenderer>();
                        if (frameRenderer != null)
                        {
                            // The prefab ring has heavy horizontal bands that made this special
                            // object resemble a bin. Its dedicated portal skin supplies the frame.
                            frameRenderer.enabled = !e.playerContainer;
                            if (e.playerContainer) frame.gameObject.SetActive(false);
                            if (!e.playerContainer)
                                frameRenderer.color = UsesOptionOneSkin(a)
                                    ? shellAccent
                                    : Lighten(interiorC, 0.30f);

                            if (!e.playerContainer && UsesOptionOneSkin(a))
                                AddNestedShellHighlights(go, a,
                                    shellAccent,
                                    frameRenderer.sortingOrder);
                        }
                    }

                    var innerRoom = model.rooms[e.interiorRoomId];
                    var innerRoot = roomRoots[e.interiorRoomId];
                    innerRoot.SetParent(go.transform, false);
                    // A player-container is presented as a centered portal cube. The live miniature
                    // sits inside that portal so it reads as an enterable space even at board scale.
                    float visibleFit = e.playerContainer
                        ? 0.32f
                        : UsesOptionOneSkin(a)
                            ? NestedPreviewFit
                            : InteriorFit;
                    float s = visibleFit / Mathf.Max(innerRoom.width, innerRoom.height);
                    innerRoot.localScale = Vector3.one * s;
                    innerRoot.localPosition = Vector3.zero;

                    // A recursive room may legally cross the edge anywhere its boundary cell is
                    // open. The old shell was a single opaque ring, so it painted a closed bezel
                    // over those real exits: the player could pass through a wall that still
                    // looked solid. Cut the shell from the room data itself. Sprite masks remove
                    // only the shell renderers, leaving the live floor and moving pieces visible
                    // while they travel through the opening. Extend the room floor through the
                    // cut as well; otherwise the parent-room colour shows through at the threshold
                    // and reads as a dark line across an otherwise open doorway.
                    if (!e.playerContainer && UsesOptionOneSkin(a))
                        AddNestedShellDoorways(go, a, innerRoom, s, interiorC,
                            e, model.rooms[e.roomId]);

                    // Option 1: preserve the live miniature and doorway while giving only the
                    // remaining square shell a restrained lower-right depth edge. Doorway shells
                    // deliberately disable their continuous frame, so they also skip this layer.
                    if (!e.playerContainer && UsesOptionOneSkin(a))
                    {
                        var visibleFrame = go.transform.Find("Frame");
                        var visibleFrameRenderer = visibleFrame != null
                            ? visibleFrame.GetComponent<SpriteRenderer>() : null;
                        if (visibleFrameRenderer != null && visibleFrameRenderer.enabled)
                            AddSoftFrameBevel(visibleFrame, visibleFrameRenderer, shellAccent);
                    }

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

            // Chapters II-III use room-boxes containing live miniature puzzles. Their shell draws a
            // high-order bezel, so the complete miniature needs one layer above it. Promote the
            // ROOM ROOT instead of adding a fixed offset to the renderers currently inside it.
            // The player begins Level 11 in the parent room and is reparented into this room later;
            // a static per-renderer offset left that newly-entered player behind the promoted floor,
            // making the player disappear. A SortingGroup on the room automatically includes every
            // actor that enters or leaves at runtime while preserving body/eye ordering.
            if (a.chapter >= 1 && a.chapter <= 2)
            {
                foreach (var e in model.entities)
                    if (e.interiorRoomId >= 1
                        && roomRoots.TryGetValue(e.interiorRoomId, out Transform nestedRoot))
                        PromoteNestedContents(nestedRoot);
            }

            if (goalTargets.Count > 0)
                worldRoot.gameObject.AddComponent<GoalFeedbackFx>()
                    .Configure(model, views, goalTargets, a.cellSprite);
            return worldRoot;
        }

        // Chapter V presents its anchored player-container as a portal cube rather than a face.
        // A centered cyan aperture exposes the real miniature room, while the bright pink shell
        // keeps its connection to the player. Everything here is presentation only.
        static void ApplyPlayerContainerSkin(GameObject box, BoardAssets a, int baseOrder)
        {
            if (box == null || a == null || a.cellSprite == null) return;

            var backing = box.transform.Find("Backing");
            if (backing != null)
            {
                backing.localScale = Vector3.one * OptionOneObjectSize;
                var sr = backing.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.enabled = true;
                    sr.color = OptionOnePlayer;
                }
            }

            var skin = new GameObject("PlayerContainerSkin").transform;
            skin.SetParent(box.transform, false);

            // One continuous aperture replaces the old eyes, lid-like top bar and side notches.
            // The dark under-frame separates the portal from the pink shell; the thinner cyan
            // frame above it matches the board architecture and clearly signals an entrance.
            const float portalHalfWidth = 0.220f;
            const float portalHalfHeight = 0.180f;
            const float underThickness = 0.080f;
            const float cyanThickness = 0.040f;
            Color portalUnder = Color.Lerp(OptionOnePlayer, OptionOnePlayerDark, 0.48f);

            // Four clean shell panels cover the nested board's structural bars outside the
            // aperture. This keeps the object solid pink and prevents the underlying miniature
            // architecture from recreating the discarded lid-and-base silhouette.
            const float shellHalfSize = 0.390f;
            float horizontalBand = shellHalfSize - portalHalfHeight;
            float verticalBand = shellHalfSize - portalHalfWidth;
            Bar(skin, a.cellSprite, OptionOnePlayer, baseOrder + 100,
                new Vector2(0f, portalHalfHeight + horizontalBand * 0.5f),
                shellHalfSize * 2f, horizontalBand, 0f);
            Bar(skin, a.cellSprite, OptionOnePlayer, baseOrder + 100,
                new Vector2(0f, -portalHalfHeight - horizontalBand * 0.5f),
                shellHalfSize * 2f, horizontalBand, 0f);
            Bar(skin, a.cellSprite, OptionOnePlayer, baseOrder + 100,
                new Vector2(-portalHalfWidth - verticalBand * 0.5f, 0f),
                portalHalfHeight * 2f, verticalBand, 90f);
            Bar(skin, a.cellSprite, OptionOnePlayer, baseOrder + 100,
                new Vector2(portalHalfWidth + verticalBand * 0.5f, 0f),
                portalHalfHeight * 2f, verticalBand, 90f);

            Bar(skin, a.cellSprite, portalUnder, baseOrder + 101,
                new Vector2(0f, portalHalfHeight), portalHalfWidth * 2f, underThickness, 0f);
            Bar(skin, a.cellSprite, portalUnder, baseOrder + 101,
                new Vector2(0f, -portalHalfHeight), portalHalfWidth * 2f, underThickness, 0f);
            Bar(skin, a.cellSprite, portalUnder, baseOrder + 101,
                new Vector2(-portalHalfWidth, 0f), portalHalfHeight * 2f, underThickness, 90f);
            Bar(skin, a.cellSprite, portalUnder, baseOrder + 101,
                new Vector2(portalHalfWidth, 0f), portalHalfHeight * 2f, underThickness, 90f);

            Color portalCyan = Lighten(OptionOneCyan, 0.12f);
            Bar(skin, a.cellSprite, portalCyan, baseOrder + 102,
                new Vector2(0f, portalHalfHeight), portalHalfWidth * 2f, cyanThickness, 0f);
            Bar(skin, a.cellSprite, portalCyan, baseOrder + 102,
                new Vector2(0f, -portalHalfHeight), portalHalfWidth * 2f, cyanThickness, 0f);
            Bar(skin, a.cellSprite, portalCyan, baseOrder + 102,
                new Vector2(-portalHalfWidth, 0f), portalHalfHeight * 2f, cyanThickness, 90f);
            Bar(skin, a.cellSprite, portalCyan, baseOrder + 102,
                new Vector2(portalHalfWidth, 0f), portalHalfHeight * 2f, cyanThickness, 90f);
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

        static void PromoteNestedContents(Transform nestedRoot)
        {
            if (nestedRoot == null) return;
            const int previewLayerOffset = 80;
            var group = nestedRoot.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (group == null)
                group = nestedRoot.gameObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
            group.sortingOrder = previewLayerOffset;
        }

        // A single rotated bar built from the 1x1 cell sprite. Every chevron, fracture line and
        // gate slat in the mechanic art is made of these, so no new sprite assets are needed.
        static GameObject Bar(Transform parent, Sprite s, Color c, int order,
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
            return go;
        }

        // A low-profile mechanic plate: enough lower-right depth to read as physical hardware,
        // with hairline edge lighting instead of a thick frame. Symbols are drawn above it by the
        // caller, so the same plate can support one-way arrows and the magnet without ambiguity.
        static void AddSlimMechanicPlate(Transform parent, BoardAssets a, string name,
                                         Color accent, int order, float size = 0.84f)
        {
            if (parent == null || a == null || a.cellSprite == null) return;

            Color faceColor = Color.Lerp(OptionOneFloor, accent, 0.13f);
            var depth = new GameObject(name + "Depth");
            depth.transform.SetParent(parent, false);
            depth.transform.localPosition = new Vector3(0.020f, -0.032f, 0f);
            depth.transform.localScale = Vector3.one * size;
            var depthRenderer = depth.AddComponent<SpriteRenderer>();
            depthRenderer.sprite = a.cellSprite;
            Color depthColor = Darken(accent, 0.64f);
            depthRenderer.color = new Color(depthColor.r, depthColor.g, depthColor.b, 0.70f);
            depthRenderer.sortingOrder = order;

            var face = new GameObject(name + "Face");
            face.transform.SetParent(parent, false);
            face.transform.localPosition = new Vector3(-0.006f, 0.008f, 0f);
            face.transform.localScale = Vector3.one * (size - 0.018f);
            var faceRenderer = face.AddComponent<SpriteRenderer>();
            faceRenderer.sprite = a.cellSprite;
            faceRenderer.color = new Color(faceColor.r, faceColor.g, faceColor.b, 0.94f);
            faceRenderer.sortingOrder = order + 1;

            float edge = size * 0.455f;
            Color highlight = Lighten(accent, 0.28f);
            highlight.a = 0.58f;
            GameObject top = Bar(parent, a.cellSprite, highlight, order + 2,
                new Vector2(-0.010f, edge), size * 0.64f, 0.018f, 0f);
            top.name = name + "TopLight";
            GameObject left = Bar(parent, a.cellSprite, highlight, order + 2,
                new Vector2(-edge, 0.006f), size * 0.58f, 0.016f, 90f);
            left.name = name + "LeftLight";

            Color shade = Darken(accent, 0.52f);
            shade.a = 0.54f;
            GameObject bottom = Bar(parent, a.cellSprite, shade, order + 2,
                new Vector2(0.012f, -edge), size * 0.64f, 0.020f, 0f);
            bottom.name = name + "BottomShade";
            GameObject right = Bar(parent, a.cellSprite, shade, order + 2,
                new Vector2(edge, -0.006f), size * 0.58f, 0.018f, 90f);
            right.name = name + "RightShade";
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

        // A shell button / weight plate. The ordinary button uses TWO linked identities:
        // an amber hatched socket matches the ordinary cargo that should be parked here, while
        // its thin green outer ring matches the green gate it opens. This removes the old visual
        // contradiction where an orange crate appeared to belong on a completely green target.
        static void PaintSwitches(Transform root, PRoom room, BoardAssets a,
                                  bool[,] cells, Color tint, bool heavy, LevelModel model)
        {
            if (cells == null || a.cellSprite == null) return;
            for (int cx = 0; cx < room.width; cx++)
                for (int cy = 0; cy < room.height; cy++)
                {
                    if (!cells[cx, cy]) continue;
                    var cell = new Vector2Int(cx, cy);
                    // A gate button may intentionally share a cargo target. Its premium goal
                    // carries both identities, so do not draw another ring and hatch underneath.
                    if (PremiumCargoSocket.ArtAvailable && PremiumCargoSocket.HasCargoGoal(room, cell))
                        continue;
                    var sw = new GameObject(heavy ? "Plate" : "Button");
                    sw.transform.SetParent(root, false);
                    sw.transform.localPosition = Cell(room, new Vector2Int(cx, cy));

                    Color socketTint = heavy ? tint : OptionOneBox;
                    if (PremiumCargoSocket.TryBuild(sw, a, model, room, cell, socketTint))
                        continue;

                    var bed = new GameObject("Recess");
                    bed.transform.SetParent(sw.transform, false);
                    bed.transform.localScale = Vector3.one * 0.90f;
                    var bsr = bed.AddComponent<SpriteRenderer>();
                    bsr.sprite = a.cellSprite;
                    bsr.color = Color.Lerp(new Color(0.02f, 0.055f, 0.12f, 1f), socketTint,
                        heavy ? 0.24f : 0.16f);
                    bsr.sortingOrder = OrderFloorCell + 1;

                    if (a.ringSprite != null)
                    {
                        var halo = new GameObject("SoftHalo");
                        halo.transform.SetParent(sw.transform, false);
                        halo.transform.localScale = Vector3.one * (heavy ? 0.86f : 0.84f);
                        var hsr = halo.AddComponent<SpriteRenderer>();
                        hsr.sprite = a.ringSprite;
                        hsr.color = new Color(tint.r, tint.g, tint.b, heavy ? 0.20f : 0.16f);
                        hsr.sortingOrder = OrderFloorCell + 2;

                        var ring = new GameObject(heavy ? "WeightRing" : "GateLinkRing");
                        ring.transform.SetParent(sw.transform, false);
                        ring.transform.localScale = Vector3.one * (heavy ? 0.76f : 0.78f);
                        var rsr = ring.AddComponent<SpriteRenderer>();
                        rsr.sprite = a.ringSprite;
                        rsr.color = Lighten(tint, heavy ? 0.08f : 0.04f);
                        rsr.sortingOrder = OrderFloorCell + 3;

                        if (!heavy)
                        {
                            var cargoRing = new GameObject("CargoMatchRing");
                            cargoRing.transform.SetParent(sw.transform, false);
                            cargoRing.transform.localScale = Vector3.one * 0.61f;
                            var cargoRingRenderer = cargoRing.AddComponent<SpriteRenderer>();
                            cargoRingRenderer.sprite = a.ringSprite;
                            cargoRingRenderer.color = Lighten(socketTint, 0.12f);
                            cargoRingRenderer.sortingOrder = OrderFloorCell + 4;
                        }
                    }

                    var core = new GameObject("Core");
                    core.transform.SetParent(sw.transform, false);
                    core.transform.localPosition = heavy ? new Vector3(0f, 0.10f, 0f) : Vector3.zero;
                    core.transform.localScale = Vector3.one * (heavy ? 0.36f : 0.40f);
                    var csr2 = core.AddComponent<SpriteRenderer>();
                    csr2.sprite = a.cellSprite;
                    csr2.color = heavy ? Darken(tint, 0.20f) : Darken(socketTint, 0.45f);
                    csr2.sortingOrder = OrderFloorCell + 5;

                    if (!heavy)
                    {
                        // Same three diagonal hatch marks as ordinary cargo: shape and colour now
                        // teach the pairing even before the Level 5 tutorial text is read.
                        Color hatch = Lighten(socketTint, 0.24f);
                        hatch.a = 0.92f;
                        Bar(sw.transform, a.cellSprite, hatch, OrderFloorCell + 6,
                            new Vector2(-0.12f, 0.12f), 0.30f, 0.038f, 45f);
                        Bar(sw.transform, a.cellSprite, hatch, OrderFloorCell + 6,
                            Vector2.zero, 0.30f, 0.038f, 45f);
                        Bar(sw.transform, a.cellSprite, hatch, OrderFloorCell + 6,
                            new Vector2(0.12f, -0.12f), 0.30f, 0.038f, 45f);
                    }

                    if (heavy)   // a cargo face plus down arrow = "place a crate here"
                    {
                        var dark = new Color(0.18f, 0.10f, 0.025f, 0.82f);
                        Bar(sw.transform, a.cellSprite, dark, OrderFloorCell + 5,
                            new Vector2(0f, 0.10f), 0.28f, 0.055f, 45f);
                        Bar(sw.transform, a.cellSprite, dark, OrderFloorCell + 5,
                            new Vector2(0f, 0.10f), 0.28f, 0.055f, -45f);

                        var down = Chevron(sw.transform, a.cellSprite, Lighten(tint, 0.26f),
                            OrderFloorCell + 5, Vector2Int.down, 0.38f);
                        down.name = "WeightArrow";
                        down.transform.localPosition = new Vector3(0f, -0.25f, 0f);
                        Bar(sw.transform, a.cellSprite, Lighten(tint, 0.26f),
                            OrderFloorCell + 5, new Vector2(0f, -0.34f), 0.30f, 0.045f, 0f);
                    }
                }
        }

        // Gates face across their corridor.  A north/south corridor gets posts on its left/right
        // edges; an east/west corridor gets posts above/below.  This is derived from the authored
        // neighbouring floor, so generated campaign boards and tutorial boards use the same rule.
        static bool GatePassageIsHorizontal(PRoom room, Vector2Int cell)
        {
            bool left  = GateNeighbourIsPassage(room, cell + Vector2Int.left);
            bool right = GateNeighbourIsPassage(room, cell + Vector2Int.right);
            bool down  = GateNeighbourIsPassage(room, cell + Vector2Int.down);
            bool up    = GateNeighbourIsPassage(room, cell + Vector2Int.up);

            // A complete opposing pair is the clearest corridor signal.
            bool horizontalPair = left && right;
            bool verticalPair = down && up;
            if (horizontalPair != verticalPair) return horizontalPair;

            // Corners and T-junctions fall back to whichever axis has more open neighbours.
            int horizontalOpen = (left ? 1 : 0) + (right ? 1 : 0);
            int verticalOpen = (down ? 1 : 0) + (up ? 1 : 0);
            if (horizontalOpen != verticalOpen) return horizontalOpen > verticalOpen;

            // Fully open/crossing cells keep the established north/south presentation.
            return false;
        }

        static bool GateNeighbourIsPassage(PRoom room, Vector2Int neighbour)
        {
            // A non-wall edge is a valid doorway into/out of a nested room.  Therefore the cell
            // just beyond that edge still counts as passage when choosing the frame orientation.
            if (!room.InBounds(neighbour)) return true;
            return !room.IsWall(neighbour) && !room.IsRock(neighbour) &&
                   !room.IsOpenTrench(neighbour) && !room.IsBroken(neighbour);
        }

        // A gate: a slatted slab that the game hides while its switch is held, over a permanent
        // frame that stays put — so an OPEN gate still reads as a gateway rather than plain floor.
        static void PaintGates(Transform root, PRoom room, BoardAssets a, bool[,] cells, Color tint,
                               Dictionary<(int, Vector2Int), GameObject> reg,
                               bool showLatchIndicator = false)
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
                    var fbed = new GameObject("Recess");
                    fbed.transform.SetParent(frame.transform, false);
                    fbed.transform.localScale = Vector3.one * 0.90f;
                    var fsr3 = fbed.AddComponent<SpriteRenderer>();
                    fsr3.sprite = a.cellSprite;
                    fsr3.color = Color.Lerp(new Color(0.015f, 0.045f, 0.10f, 1f), tint, 0.10f);
                    fsr3.sortingOrder = OrderFloorCell + 1;

                    bool horizontalPassage = GatePassageIsHorizontal(room, cell);

                    // Rotate the complete frame by a quarter turn for an east/west corridor.
                    // The posts remain parallel to movement and the closed slats cross movement.
                    Vector2 postA = horizontalPassage
                        ? new Vector2(0f, -0.39f) : new Vector2(-0.39f, 0f);
                    Vector2 postB = horizontalPassage
                        ? new Vector2(0f, 0.39f) : new Vector2(0.39f, 0f);
                    Vector2 capA = horizontalPassage
                        ? new Vector2(-0.30f, -0.39f) : new Vector2(-0.39f, 0.30f);
                    Vector2 capB = horizontalPassage
                        ? new Vector2(-0.30f, 0.39f) : new Vector2(0.39f, 0.30f);
                    float postAngle = horizontalPassage ? 0f : 90f;
                    float capAngle = horizontalPassage ? 90f : 0f;

                    Color rail = Darken(tint, 0.28f);
                    Bar(frame.transform, a.cellSprite, rail, OrderWall + 1,
                        postA, 0.78f, 0.105f, postAngle);
                    Bar(frame.transform, a.cellSprite, rail, OrderWall + 1,
                        postB, 0.78f, 0.105f, postAngle);
                    Bar(frame.transform, a.cellSprite, Lighten(tint, 0.18f), OrderWall + 2,
                        capA, 0.16f, 0.075f, capAngle);
                    Bar(frame.transform, a.cellSprite, Lighten(tint, 0.18f), OrderWall + 2,
                        capB, 0.16f, 0.075f, capAngle);

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

                    float slatAngle = horizontalPassage ? 90f : 0f;
                    for (int k = -1; k <= 1; k++)                // slats = a barrier, not a floor tile
                    {
                        Vector2 slatAt = horizontalPassage
                            ? new Vector2(k * 0.24f, 0f) : new Vector2(0f, k * 0.24f);
                        Bar(slab.transform, a.cellSprite, new Color(tint.r, tint.g, tint.b, 0.18f),
                            OrderWall + 1, slatAt, 0.76f, 0.18f, slatAngle);
                        Bar(slab.transform, a.cellSprite, Lighten(tint, 0.12f),
                            OrderWall + 3, slatAt, 0.68f, 0.085f, slatAngle);
                    }

                    if (showLatchIndicator)
                    {
                        var indicator = new GameObject("LatchIndicator");
                        indicator.transform.SetParent(slab.transform, false);
                        indicator.transform.localPosition = horizontalPassage
                            ? new Vector3(0f, -0.31f, 0f) : new Vector3(0.31f, 0f, 0f);
                        indicator.transform.localScale = Vector3.one * 0.22f;
                        var isr = indicator.AddComponent<SpriteRenderer>();
                        isr.sprite = a.cellSprite;
                        isr.color = new Color(0.16f, 0.09f, 0.025f, 0.96f);
                        isr.sortingOrder = OrderWall + 4;

                        var lamp = new GameObject("Lamp");
                        lamp.transform.SetParent(indicator.transform, false);
                        lamp.transform.localScale = horizontalPassage
                            ? new Vector3(0.62f, 0.34f, 1f) : new Vector3(0.34f, 0.62f, 1f);
                        var lsr = lamp.AddComponent<SpriteRenderer>();
                        lsr.sprite = a.cellSprite;
                        lsr.color = Lighten(tint, 0.30f);
                        lsr.sortingOrder = OrderWall + 5;
                    }

                    if (reg != null) reg[(room.id, cell)] = slab;
                }
        }

        enum PadMotif { ChevronUp, Sand, Sticky, Bars, Deflector, Poles, Swap }

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

                    int order;
                    if (motif == PadMotif.Poles)
                    {
                        // The magnet is real low-profile hardware, not another translucent floor
                        // stain. Its U silhouette remains the unique gameplay meaning.
                        AddSlimMechanicPlate(pad.transform, a, "MagnetPlate", tint,
                            OrderFloorCell + 2, 0.82f);
                        order = OrderFloorCell + 5;
                    }
                    else
                    {
                        var bed = new GameObject("Bed");
                        bed.transform.SetParent(pad.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var bsr = bed.AddComponent<SpriteRenderer>();
                        bsr.sprite = a.cellSprite;
                        bsr.color = new Color(tint.r, tint.g, tint.b, 0.26f);
                        bsr.sortingOrder = OrderFloorCell + 1;
                        order = OrderFloorCell + 2;
                    }
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
                        case PadMotif.Sand:           // loose grains: no traction while pushing
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(-0.18f, -0.10f), 0.22f, 0.09f, 0f);
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(0.14f, 0.06f), 0.26f, 0.09f, 0f);
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(-0.02f, 0.22f), 0.18f, 0.08f, 0f);
                            break;
                        case PadMotif.Sticky:         // adhesive cross: it holds your last direction
                            if (a.ringSprite != null)
                            {
                                var bond = new GameObject("AdhesiveRing");
                                bond.transform.SetParent(pad.transform, false);
                                bond.transform.localScale = Vector3.one * 0.56f;
                                var bondSr = bond.AddComponent<SpriteRenderer>();
                                bondSr.sprite = a.ringSprite;
                                bondSr.color = tint;
                                bondSr.sortingOrder = order;
                            }
                            Bar(pad.transform, a.cellSprite, Lighten(tint, 0.30f), order + 1,
                                Vector2.zero, 0.58f, 0.12f, 0f);
                            Bar(pad.transform, a.cellSprite, Lighten(tint, 0.30f), order + 1,
                                Vector2.zero, 0.58f, 0.12f, 90f);
                            break;
                        case PadMotif.Bars:           // a one-way trap for cargo
                            for (int k = -1; k <= 1; k++)
                                Bar(pad.transform, a.cellSprite, tint, order, new Vector2(k * 0.24f, 0f), 0.68f, 0.10f, 90f);
                            break;
                        case PadMotif.Deflector:      // a quarter turn clockwise
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(-0.06f, 0.16f), 0.34f, 0.10f, 0f);
                            Bar(pad.transform, a.cellSprite, tint, order, new Vector2(0.16f, -0.04f), 0.34f, 0.10f, 90f);
                            var head = Chevron(pad.transform, a.cellSprite, tint, order + 1, Vector2Int.down, 0.62f);
                            head.transform.localPosition = new Vector3(0.16f, -0.24f, 0f);
                            break;
                        case PadMotif.Swap:           // two opposite arrows: trade places with twin
                            var right = Chevron(pad.transform, a.cellSprite, tint, order,
                                Vector2Int.right, 0.58f);
                            right.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                            var left = Chevron(pad.transform, a.cellSprite, Lighten(tint, 0.38f), order,
                                Vector2Int.left, 0.58f);
                            left.transform.localPosition = new Vector3(0f, -0.18f, 0f);
                            break;
                        case PadMotif.Poles:          // it pulls
                            if (a.glowSprite != null)
                            {
                                var gl = new GameObject("Field");
                                gl.transform.SetParent(pad.transform, false);
                                gl.transform.localScale = Vector3.one * 0.92f;
                                var glr = gl.AddComponent<SpriteRenderer>();
                                glr.sprite = a.glowSprite;
                                glr.color = new Color(tint.r, tint.g, tint.b, 0.14f);
                                glr.sortingOrder = OrderFloorCell + 1;
                            }

                            Color magnetShadow = Darken(tint, 0.58f);
                            magnetShadow.a = 0.82f;
                            Vector2 magnetDepth = new Vector2(0.018f, -0.022f);
                            Bar(pad.transform, a.cellSprite, magnetShadow, order,
                                new Vector2(-0.18f, 0.02f) + magnetDepth, 0.40f, 0.115f, 90f);
                            Bar(pad.transform, a.cellSprite, magnetShadow, order,
                                new Vector2(0.18f, 0.02f) + magnetDepth, 0.40f, 0.115f, 90f);
                            Bar(pad.transform, a.cellSprite, magnetShadow, order,
                                new Vector2(0f, -0.18f) + magnetDepth, 0.46f, 0.115f, 0f);

                            Bar(pad.transform, a.cellSprite, tint, order + 1,
                                new Vector2(-0.18f, 0.02f), 0.40f, 0.105f, 90f);
                            Bar(pad.transform, a.cellSprite, tint, order + 1,
                                new Vector2(0.18f, 0.02f), 0.40f, 0.105f, 90f);
                            Bar(pad.transform, a.cellSprite, tint, order + 1,
                                new Vector2(0f, -0.18f), 0.46f, 0.105f, 0f);

                            Color poleLight = new Color(0.45f, 0.92f, 1f, 1f);
                            Bar(pad.transform, a.cellSprite, poleLight, order + 2,
                                new Vector2(-0.18f, 0.235f), 0.15f, 0.10f, 0f);
                            Bar(pad.transform, a.cellSprite, poleLight, order + 2,
                                new Vector2(0.18f, 0.235f), 0.15f, 0.10f, 0f);
                            Color magnetShine = Lighten(tint, 0.36f);
                            magnetShine.a = 0.58f;
                            Bar(pad.transform, a.cellSprite, magnetShine, order + 2,
                                new Vector2(-0.205f, 0.015f), 0.27f, 0.018f, 90f);
                            Bar(pad.transform, a.cellSprite, magnetShine, order + 2,
                                new Vector2(0.155f, 0.015f), 0.27f, 0.018f, 90f);
                            break;
                    }
                }
        }

        static void PaintRoom(Transform root, PRoom room, BoardAssets a, BoardTiles tiles,
                              LevelModel model, List<GoalFeedbackFx.Target> goalTargets)
        {
            Color floorC = RoomColor(a.roomColors, room.id);
            bool main = room.id == 0;
            bool optionOne = UsesOptionOneSkin(a);

            // The playable room sits INSIDE a physical cabinet-like tray. The selected skin uses
            // a dark structural plate, cobalt bevel, cyan inner rail and violet corner signatures.
            // The frame is deliberately only on the root room — recursive rooms already live
            // inside their meta-box frame, and the menu logo supplies its own "O" ring (hideFrame).
            if (main && !a.hideFrame && optionOne)
                PaintOptionOneCabinetFrame(root, room, a);

            if (main && !a.hideFrame && !optionOne)
            {
                Color coolWhite = new Color(0.96f, 0.98f, 1f, 1f);
                Color shell = optionOne ? a.wallColor
                                           : Color.Lerp(a.frameColor, coolWhite, 0.72f);
                Color bevel = optionOne ? a.wallColor
                                           : Color.Lerp(a.frameColor, coolWhite, 0.34f);

                // Chapter 1's perimeter wall IS the sculpted cabinet. Keep these backing panels
                // hidden beneath it so there is no second rectangular picture frame outside the
                // architecture. Later chapters retain the larger independent tray.
                float shadowPad = optionOne ? 0.24f : 0.92f;
                float shellPad = optionOne ? 0.02f : 0.76f;
                float bevelPad = optionOne ? 0.01f : 0.52f;
                float recessPad = optionOne ? -0.02f : 0.25f;

                // The ivory perimeter already separates the board from the background. A second
                // offset rectangle showed through as a black strip on the right and bottom edges,
                // especially on wide boards. Keep the tray shadow only for the later dark skins.
                if (!optionOne)
                    CreateSlicedPanel(root, a.floorPrefab, "BoardShadow",
                        new Vector2(room.width + shadowPad, room.height + shadowPad),
                        new Color(0.01f, 0.025f, 0.045f, 0.76f), -10,
                        new Vector2(0.07f, -0.09f));
                CreateSlicedPanel(root, a.floorPrefab, "RoomFrame",
                    new Vector2(room.width + shellPad, room.height + shellPad),
                    shell, -9, Vector2.zero);
                CreateSlicedPanel(root, a.floorPrefab, "FrameBevel",
                    new Vector2(room.width + bevelPad, room.height + bevelPad),
                    bevel, -8, new Vector2(0f, -0.015f));
                CreateSlicedPanel(root, a.floorPrefab, "InnerRecess",
                    new Vector2(room.width + recessPad, room.height + recessPad),
                    optionOne ? floorC : Darken(floorC, 0.74f),
                    -7, new Vector2(0f, -0.025f));

                // Asymmetric edge lighting gives the flat procedural panels a calm bevel: light
                // arrives from the upper left and falls away on the lower right.
                if (a.cellSprite != null && !optionOne)
                {
                    Color hi = new Color(1f, 1f, 1f, 0.78f);
                    Color lo = new Color(0.02f, 0.05f, 0.08f, 0.58f);
                    float edge = 0.315f;
                    float extra = 0.55f;
                    Bar(root, a.cellSprite, hi, -6,
                        new Vector2(-0.015f, room.height * 0.5f + edge), room.width + extra, 0.055f, 0f);
                    Bar(root, a.cellSprite, hi, -6,
                        new Vector2(-room.width * 0.5f - edge, 0.01f), room.height + extra, 0.050f, 90f);
                    Bar(root, a.cellSprite, lo, -6,
                        new Vector2(0.02f, -room.height * 0.5f - edge), room.width + extra, 0.060f, 0f);
                    Bar(root, a.cellSprite, lo, -6,
                        new Vector2(room.width * 0.5f + edge, -0.01f), room.height + extra, 0.055f, 90f);
                }
            }

            // The model still owns a normal logical grid; this is a visual-only representation.
            Color gutter = a.gutterColor.a > 0f ? a.gutterColor : Darken(floorC, 0.55f);
            if (!optionOne)
            {
                var floor = Object.Instantiate(a.floorPrefab, root);
                floor.transform.localPosition = Vector3.zero;
                var fsr = floor.GetComponent<SpriteRenderer>();
                fsr.drawMode = SpriteDrawMode.Sliced;
                fsr.size = new Vector2(room.width, room.height);
                fsr.color = Darken(gutter, 0.12f);
                fsr.sortingOrder = OrderFloorBase - 1;
            }

            // Paint the walkable silhouette as a small number of overlapping rectangles. The
            // overlap hides joins, so the result is a single calm surface rather than visible cells.
            if (optionOne)
                PaintOptionOneWalkableFloor(root, room, a, floorC);

            if (a.floorTex != null && a.floorTexTint.a > 0f)
            {
                var texGO = new GameObject("FloorTex");
                texGO.transform.SetParent(root, false);
                var tsr = texGO.AddComponent<SpriteRenderer>();
                tsr.sprite = a.floorTex;
                tsr.drawMode = SpriteDrawMode.Tiled;
                tsr.size = new Vector2(room.width, room.height);
                tsr.color = a.floorTexTint;
                tsr.sortingOrder = OrderFloorBase - 1;
            }

            // The room is one continuous floor. Grid coordinates remain fully functional in the
            // model, but ordinary cells have no visible borders, checker pattern, shadows or rings.
            // Only meaningful terrain (ice, switches, goals, hazards...) receives a cell-shaped mark.

            // No decorative "ambient cell": an unmarked floor area must never look like a tile.

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
                        bool nestedPreview = room.id > 0;
                        // Keep the shallow plate inside one cell, including recursive previews.
                        ow.transform.localScale = Vector3.one * (nestedPreview ? 1.12f : 1f);
                        AddSlimMechanicPlate(ow.transform, a, "OneWayPlate", amber,
                            OrderFloorCell + 1, nestedPreview ? 0.80f : 0.84f);

                        // A pair of stacked chevrons — unmistakably "this way only".
                        Color arrowColor = nestedPreview ? Lighten(amber, 0.16f) : amber;
                        Color arrowShadow = Darken(amber, 0.58f);
                        arrowShadow.a = 0.78f;
                        float arrowScale = nestedPreview ? 0.88f : 0.82f;
                        Vector3 first = new Vector3(d.x * 0.11f, d.y * 0.11f, 0f);
                        Vector3 second = new Vector3(-d.x * 0.11f, -d.y * 0.11f, 0f);

                        var shadow1 = Chevron(ow.transform, a.cellSprite, arrowShadow,
                            OrderFloorCell + 4, d, arrowScale);
                        shadow1.name = "ChevronDepthA";
                        shadow1.transform.localPosition = first + new Vector3(0.018f, -0.022f, 0f);
                        var shadow2 = Chevron(ow.transform, a.cellSprite, arrowShadow,
                            OrderFloorCell + 4, d, arrowScale);
                        shadow2.name = "ChevronDepthB";
                        shadow2.transform.localPosition = second + new Vector3(0.018f, -0.022f, 0f);

                        var c1 = Chevron(ow.transform, a.cellSprite, arrowColor,
                            OrderFloorCell + 5, d, arrowScale);
                        c1.name = "ChevronFaceA";
                        c1.transform.localPosition = first;
                        Color secondColor = Lighten(amber, 0.06f);
                        secondColor.a = nestedPreview ? 0.92f : 0.82f;
                        var c2 = Chevron(ow.transform, a.cellSprite, secondColor,
                            OrderFloorCell + 5, d, arrowScale);
                        c2.name = "ChevronFaceB";
                        c2.transform.localPosition = second;
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
            PaintSwitches(root, room, a, room.button, shellC, false, model);
            PaintSwitches(root, room, a, room.plate, plateC, true, model);
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
                        dsr.color = new Color(0.008f, 0.028f, 0.070f, 1f);
                        dsr.sortingOrder = OrderFloorCell + 1;

                        if (a.glowSprite != null)   // the sense of depth, not just a dark square
                        {
                            var vg2 = new GameObject("Depth");
                            vg2.transform.SetParent(dp.transform, false);
                            vg2.transform.localScale = Vector3.one * 0.86f;
                            var vsr2 = vg2.AddComponent<SpriteRenderer>();
                            vsr2.sprite = a.glowSprite;
                            vsr2.color = new Color(0f, 0f, 0f, 0.72f);
                            vsr2.sortingOrder = OrderFloorCell + 2;
                        }

                        // A restrained double lip communicates depth without turning the hollow
                        // into a bright portal. The centre remains almost black and visually below
                        // the walkable plane.
                        if (a.ringSprite != null)
                        {
                            var lip = new GameObject("DepthLip");
                            lip.transform.SetParent(dp.transform, false);
                            lip.transform.localScale = Vector3.one * 0.86f;
                            var lipSr = lip.AddComponent<SpriteRenderer>();
                            lipSr.sprite = a.ringSprite;
                            lipSr.color = new Color(0.18f, 0.40f, 0.52f, 0.62f);
                            lipSr.sortingOrder = OrderFloorCell + 3;

                            var inner = new GameObject("InnerDepth");
                            inner.transform.SetParent(dp.transform, false);
                            inner.transform.localScale = Vector3.one * 0.64f;
                            var innerSr = inner.AddComponent<SpriteRenderer>();
                            innerSr.sprite = a.ringSprite;
                            innerSr.color = new Color(0.03f, 0.14f, 0.25f, 0.48f);
                            innerSr.sortingOrder = OrderFloorCell + 4;
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

            // Breakable rock: a compact faceted block with a fracture through it, so it reads as
            // "hit this" rather than "wall". Registered because a crate shatters it into floor.
            if (room.rock != null && a.cellSprite != null)
            {
                Color stone = new Color(0.46f, 0.40f, 0.39f, 1f);
                for (int cx = 0; cx < room.width; cx++)
                    for (int cy = 0; cy < room.height; cy++)
                    {
                        if (!room.rock[cx, cy]) continue;
                        var cell = new Vector2Int(cx, cy);
                        var rk = new GameObject("Rock");
                        rk.transform.SetParent(root, false);
                        rk.transform.localPosition = Cell(room, cell);

                        var depth = new GameObject("RockDepth");
                        depth.transform.SetParent(rk.transform, false);
                        depth.transform.localPosition = new Vector3(0.026f, -0.040f, 0f);
                        depth.transform.localScale = Vector3.one * 0.80f;
                        var depthSr = depth.AddComponent<SpriteRenderer>();
                        depthSr.sprite = a.cellSprite;
                        depthSr.color = Darken(stone, 0.52f);
                        depthSr.sortingOrder = OrderWall;

                        var body = new GameObject("Body");
                        body.transform.SetParent(rk.transform, false);
                        body.transform.localPosition = new Vector3(-0.008f, 0.012f, 0f);
                        body.transform.localRotation = Quaternion.Euler(0f, 0f, -2f);
                        body.transform.localScale = Vector3.one * 0.78f;
                        var rsr = body.AddComponent<SpriteRenderer>();
                        rsr.sprite = a.cellSprite;
                        rsr.color = stone;
                        rsr.sortingOrder = OrderWall + 1;

                        if (a.ringSprite != null)
                        {
                            var bevel = new GameObject("StoneBevel");
                            bevel.transform.SetParent(rk.transform, false);
                            bevel.transform.localPosition = new Vector3(-0.008f, 0.012f, 0f);
                            bevel.transform.localRotation = Quaternion.Euler(0f, 0f, -2f);
                            bevel.transform.localScale = Vector3.one * 0.70f;
                            var bevelSr = bevel.AddComponent<SpriteRenderer>();
                            bevelSr.sprite = a.ringSprite;
                            bevelSr.color = Lighten(stone, 0.18f);
                            bevelSr.sortingOrder = OrderWall + 2;
                        }

                        var top = new GameObject("Lit");
                        top.transform.SetParent(rk.transform, false);
                        top.transform.localPosition = new Vector3(-0.04f, 0.326f, 0f);
                        top.transform.localRotation = Quaternion.Euler(0f, 0f, -2f);
                        top.transform.localScale = new Vector3(0.48f, 0.038f, 1f);
                        var tsr = top.AddComponent<SpriteRenderer>();
                        tsr.sprite = a.cellSprite;
                        Color rockLight = Lighten(stone, 0.34f);
                        rockLight.a = 0.72f;
                        tsr.color = rockLight;
                        tsr.sortingOrder = OrderWall + 3;

                        Color facetLight = Lighten(stone, 0.13f);
                        facetLight.a = 0.30f;
                        Bar(rk.transform, a.cellSprite, facetLight, OrderWall + 3,
                            new Vector2(-0.20f, 0.10f), 0.28f, 0.15f, -10f);
                        Color facetShade = Darken(stone, 0.16f);
                        facetShade.a = 0.34f;
                        Bar(rk.transform, a.cellSprite, facetShade, OrderWall + 3,
                            new Vector2(0.19f, -0.13f), 0.30f, 0.14f, 12f);

                        Color seam = new Color(0.12f, 0.09f, 0.08f, 0.85f);
                        Bar(rk.transform, a.cellSprite, seam, OrderWall + 5,
                            new Vector2(-0.03f, 0.11f), 0.46f, 0.046f, 72f);
                        Bar(rk.transform, a.cellSprite, seam, OrderWall + 5,
                            new Vector2(0.10f, -0.14f), 0.27f, 0.042f, 30f);
                        Bar(rk.transform, a.cellSprite, seam, OrderWall + 5,
                            new Vector2(-0.14f, -0.04f), 0.22f, 0.040f, -32f);

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

                        var bed = new GameObject("Recess");
                        bed.transform.SetParent(tg.transform, false);
                        bed.transform.localScale = Vector3.one * 0.90f;
                        var tbs = bed.AddComponent<SpriteRenderer>();
                        tbs.sprite = a.cellSprite;
                        tbs.color = new Color(0.018f, 0.050f, 0.110f, 1f);
                        tbs.sortingOrder = OrderFloorCell + 1;

                        if (a.ringSprite != null)
                        {
                            var track = new GameObject("ToggleTrack");
                            track.transform.SetParent(tg.transform, false);
                            track.transform.localScale = new Vector3(0.72f, 0.48f, 1f);
                            var trackSr = track.AddComponent<SpriteRenderer>();
                            trackSr.sprite = a.ringSprite;
                            trackSr.color = new Color(0.18f, 0.36f, 0.52f, 0.72f);
                            trackSr.sortingOrder = OrderFloorCell + 2;
                        }

                        var offLever = Bar(tg.transform, a.cellSprite, Darken(lever, 0.12f),
                            OrderFloorCell + 3, new Vector2(-0.04f, 0.04f), 0.50f, 0.14f, 62f);
                        offLever.name = "LeverOff";
                        var pivot = new GameObject("Pivot");
                        pivot.transform.SetParent(tg.transform, false);
                        pivot.transform.localPosition = new Vector3(-0.16f, -0.14f, 0f);
                        pivot.transform.localScale = Vector3.one * 0.16f;
                        var pivotSr = pivot.AddComponent<SpriteRenderer>();
                        pivotSr.sprite = a.cellSprite;
                        pivotSr.color = Lighten(lever, 0.14f);
                        pivotSr.sortingOrder = OrderFloorCell + 4;

                        var lit = new GameObject("Thrown");   // shown only once it is latched
                        lit.transform.SetParent(tg.transform, false);

                        var cover = new GameObject("StateCover");
                        cover.transform.SetParent(lit.transform, false);
                        cover.transform.localScale = new Vector3(0.62f, 0.40f, 1f);
                        var coverSr = cover.AddComponent<SpriteRenderer>();
                        coverSr.sprite = a.cellSprite;
                        coverSr.color = new Color(0.018f, 0.050f, 0.110f, 1f);
                        coverSr.sortingOrder = OrderFloorCell + 5;

                        Bar(lit.transform, a.cellSprite, Lighten(lever, 0.12f),
                            OrderFloorCell + 6, new Vector2(-0.05f, 0f), 0.48f, 0.14f, 0f);

                        var lamp = new GameObject("StateLamp");
                        lamp.transform.SetParent(lit.transform, false);
                        lamp.transform.localPosition = new Vector3(0.27f, 0f, 0f);
                        lamp.transform.localScale = new Vector3(0.11f, 0.20f, 1f);
                        var lsr2 = lamp.AddComponent<SpriteRenderer>();
                        lsr2.sprite = a.cellSprite;
                        lsr2.color = Lighten(lever, 0.30f);
                        lsr2.sortingOrder = OrderFloorCell + 7;
                        lit.SetActive(false);

                        if (tiles != null) tiles.toggleOn[(room.id, cell)] = lit;
                    }
            }

            // Latch gates wear the switch's amber; pulsing gates wear a cold cyan and blink.
            PaintGates(root, room, a, room.latch, new Color(1f, 0.86f, 0.42f, 1f),
                       tiles == null ? null : tiles.latches, true);
            PaintGates(root, room, a, room.pulse, new Color(0.46f, 0.90f, 0.96f, 1f),
                       tiles == null ? null : tiles.pulses);

            // Chapter-4 terrain. These are all "a tinted pad with a motif on it", so they share one
            // table-driven pass rather than nine near-identical blocks. Colour carries the meaning:
            // violet pulls, green lifts, sand is dun, cages are iron, deflectors and magnets glow.
            PaintPad(root, room, a, room.updraft,   new Color(0.55f, 0.95f, 0.66f, 1f), PadMotif.ChevronUp);
            PaintPad(root, room, a, room.sand,      new Color(0.86f, 0.78f, 0.56f, 1f), PadMotif.Sand);
            PaintPad(root, room, a, room.sticky,    new Color(0.95f, 0.62f, 0.86f, 1f), PadMotif.Sticky);
            PaintPad(root, room, a, room.cage,      new Color(0.62f, 0.66f, 0.74f, 1f), PadMotif.Bars);
            PaintPad(root, room, a, room.deflector, new Color(0.72f, 0.86f, 1f, 1f),    PadMotif.Deflector);
            PaintPad(root, room, a, room.magnet,    new Color(1f, 0.45f, 0.45f, 1f),    PadMotif.Poles);
            PaintPad(root, room, a, room.swap,      new Color(0.86f, 0.62f, 1f, 1f),    PadMotif.Swap);

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

            if (optionOne)
                PaintPlayableBoundaryContours(root, room, a, model);
            else
                for (int x = 0; x < room.width; x++)
                    for (int y = 0; y < room.height; y++)
                        if (room.wall[x, y])
                            PaintWall(root, room, x, y, a);

            foreach (var g in room.boxGoals)
            {
                var go = Object.Instantiate(a.boxGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                StyleGoal(go, a.boxColor, false, a, model, room, g);
                goalTargets.Add(new GoalFeedbackFx.Target(go, room.id, g,
                    GoalFeedbackFx.Kind.Cargo, a.boxColor));
            }
            foreach (var g in room.playerGoals)
            {
                var go = Object.Instantiate(a.playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                StyleGoal(go, a.playerColor, true, a);
                goalTargets.Add(new GoalFeedbackFx.Target(go, room.id, g,
                    GoalFeedbackFx.Kind.Player, a.playerColor));
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
                StyleGoal(go, mirrorC, true, a);
                goalTargets.Add(new GoalFeedbackFx.Target(go, room.id, g,
                    GoalFeedbackFx.Kind.Mirror, mirrorC));
            }
            foreach (var g in room.echoGoals)
            {
                var go = Object.Instantiate(a.playerGoalPrefab, root);
                go.transform.localPosition = Cell(room, g);
                SetOrder(go, OrderGoal);
                StyleGoal(go, echoC, true, a);
                goalTargets.Add(new GoalFeedbackFx.Target(go, room.id, g,
                    GoalFeedbackFx.Kind.Echo, echoC));
            }
            // A coloured goal wears exactly the hue of the one crate that satisfies it.
            foreach (var (cell, colour) in room.colourGoals)
            {
                var go = Object.Instantiate(a.boxGoalPrefab, root);
                go.transform.localPosition = Cell(room, cell);
                SetOrder(go, OrderGoal);
                Color gc = CrateColour(a.boxColor, colour);
                StyleGoal(go, gc, false, a, model, room, cell, colour);
                if (go.GetComponent<PremiumCargoSocket>() == null)
                    AddColourIdentity(go.transform, a, colour, gc, OrderGoal + 3, true);
                goalTargets.Add(new GoalFeedbackFx.Target(go, room.id, cell,
                    GoalFeedbackFx.Kind.Colour, gc, colour));
            }
        }

        static void PaintOptionOneCabinetFrame(Transform root, PRoom room, BoardAssets a)
        {
            Vector2 size = new Vector2(room.width, room.height);

            // Layered sliced panels reproduce the selected dark luxury cabinet without requiring
            // a level-specific bitmap. The opaque backing also makes void/wall areas read as a
            // deliberate recess instead of showing an accidental rectangle of the room artwork.
            CreateSlicedPanel(root, a.floorPrefab, "BoardShadow", size + Vector2.one * 0.72f,
                new Color(0.002f, 0.006f, 0.025f, 0.92f), -11,
                new Vector2(0f, -0.11f));
            CreateSlicedPanel(root, a.floorPrefab, "BoardOuterFrame", size + Vector2.one * 0.58f,
                OptionOneWall, -10, Vector2.zero);
            CreateSlicedPanel(root, a.floorPrefab, "BoardBevel", size + Vector2.one * 0.40f,
                OptionOneBevel, -9, Vector2.zero);
            CreateSlicedPanel(root, a.floorPrefab, "BoardCyanRail", size + Vector2.one * 0.20f,
                OptionOneCyan, -8, Vector2.zero);
            CreateSlicedPanel(root, a.floorPrefab, "BoardRecess", size + Vector2.one * 0.02f,
                Darken(OptionOneFloor, 0.28f), -7, Vector2.zero);

            // Four restrained violet diamonds are the frame's signature detail. They live outside
            // the playable surface and therefore cannot be mistaken for cells or mechanics.
            if (a.cellSprite == null) return;
            float x = room.width * 0.5f + 0.20f;
            float y = room.height * 0.5f + 0.20f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    Bar(root, a.cellSprite, OptionOneViolet, -6,
                        new Vector2(sx * x, sy * y), 0.20f, 0.20f, 45f);
        }

        static bool IsWall(PRoom room, int x, int y)
            => x >= 0 && y >= 0 && x < room.width && y < room.height && room.wall[x, y];

        static bool IsSolid(PRoom room, int x, int y)
            => x < 0 || y < 0 || x >= room.width || y >= room.height || room.wall[x, y];

        // Paint the logical floor as a small number of overlapping rectangles. There are no
        // visible cells and no checker pattern; the player only feels the grid while moving.
        // Greedy merging keeps the hierarchy and WebGL draw count low even on the largest boards.
        static void PaintOptionOneWalkableFloor(Transform root, PRoom room, BoardAssets a, Color floorColor)
        {
            // Nested-room shells deliberately use deep chapter colours. Their old walkable floor
            // used almost the same colour, so the route disappeared when the camera entered the
            // room. Lift only the playable surface; this keeps the shell premium and dark while
            // making every legal path readable at a glance.
            if (room.id > 0)
                floorColor = ReadableNestedPathColor(floorColor);

            var used = new bool[room.width, room.height];

            for (int y = 0; y < room.height; y++)
                for (int x = 0; x < room.width; x++)
                {
                    if (room.wall[x, y] || used[x, y]) continue;

                    int width = 1;
                    while (x + width < room.width
                           && !room.wall[x + width, y]
                           && !used[x + width, y])
                        width++;

                    int height = 1;
                    bool canGrow = true;
                    while (y + height < room.height && canGrow)
                    {
                        for (int ix = x; ix < x + width; ix++)
                            if (room.wall[ix, y + height] || used[ix, y + height])
                            {
                                canGrow = false;
                                break;
                            }
                        if (canGrow) height++;
                    }

                    for (int iy = y; iy < y + height; iy++)
                        for (int ix = x; ix < x + width; ix++)
                            used[ix, iy] = true;

                    Vector2 centre = new Vector2(
                        x + (width - 1) * 0.5f - (room.width - 1) * 0.5f,
                        y + (height - 1) * 0.5f - (room.height - 1) * 0.5f);
                    CreateSlicedPanel(root, a.floorPrefab, "WalkableFloor",
                        new Vector2(width + 0.08f, height + 0.08f), floorColor,
                        OrderFloorBase, centre);
                }
        }

        // Derive contours from the real playable-cell boundary. No wall cell is rendered. Nested
        // rooms are the one exception to a closed outline: their valid parent-facing exits are
        // physical doorways, so the rail must stop at those cells instead of drawing a misleading
        // line across them. Root rooms and unusable anchored sides remain fully closed.
        static void PaintPlayableBoundaryContours(Transform root, PRoom room, BoardAssets a,
                                                   LevelModel model)
        {
            var edges = new List<BoundaryEdge>();
            var outgoing = new Dictionary<Vector2Int, List<int>>();

            for (int x = 0; x < room.width; x++)
                for (int y = 0; y < room.height; y++)
                {
                    if (room.wall[x, y]) continue;

                    // Directed clockwise around solid space, leaving playable floor on the left.
                    if (IsSolid(room, x, y - 1))
                        AddBoundaryEdge(edges, outgoing,
                            new Vector2Int(x, y), new Vector2Int(x + 1, y), y == 0);
                    if (IsSolid(room, x + 1, y))
                        AddBoundaryEdge(edges, outgoing,
                            new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1),
                            x == room.width - 1);
                    if (IsSolid(room, x, y + 1))
                        AddBoundaryEdge(edges, outgoing,
                            new Vector2Int(x + 1, y + 1), new Vector2Int(x, y + 1),
                            y == room.height - 1);
                    if (IsSolid(room, x - 1, y))
                        AddBoundaryEdge(edges, outgoing,
                            new Vector2Int(x, y + 1), new Vector2Int(x, y), x == 0);
                }

            var used = new bool[edges.Count];
            for (int firstEdge = 0; firstEdge < edges.Count; firstEdge++)
            {
                if (used[firstEdge]) continue;

                var loopEdges = new List<BoundaryEdge>();
                BoundaryEdge edge = edges[firstEdge];
                Vector2Int loopStart = edge.start;
                Vector2Int incoming = edge.end - edge.start;
                loopEdges.Add(edge);
                used[firstEdge] = true;

                Vector2Int current = edge.end;
                int guard = edges.Count + 1;
                while (current != loopStart && guard-- > 0)
                {
                    int next = FindNextBoundaryEdge(current, incoming, outgoing, edges, used);
                    if (next < 0) break;

                    BoundaryEdge nextEdge = edges[next];
                    used[next] = true;
                    incoming = nextEdge.end - nextEdge.start;
                    current = nextEdge.end;
                    loopEdges.Add(nextEdge);
                }

                if (current != loopStart || loopEdges.Count < 4) continue;
                PaintBoundaryContour(root, room, a, loopEdges, model);
            }
        }

        static Vector2Int BoundaryOutward(BoundaryEdge edge)
        {
            Vector2Int tangent = edge.end - edge.start;
            return new Vector2Int(tangent.y, -tangent.x);
        }

        static bool IsNestedDoorway(BoundaryEdge edge, PRoom room, LevelModel model)
        {
            if (!edge.touchesOutside || room.containerBox == null || model == null) return false;
            if (!model.rooms.TryGetValue(room.containerBox.roomId, out PRoom parentRoom)) return false;
            Vector2Int direction = BoundaryOutward(edge);
            if (!HasUsableParentSide(room.containerBox, parentRoom, direction)) return false;

            // Existing advanced levels intentionally allow their established free-edge recursive
            // exits. Level 1 opts into the explicit doorway lesson, so only its real doorway edge
            // is removed from the contour.
            if (!model.useAuthoredDoorwayExits) return true;

            Vector2Int cell = direction.x != 0
                ? new Vector2Int(direction.x > 0 ? room.width - 1 : 0,
                    Mathf.Min(edge.start.y, edge.end.y))
                : new Vector2Int(Mathf.Min(edge.start.x, edge.end.x),
                    direction.y > 0 ? room.height - 1 : 0);
            return LevelModel.IsExitCell(room, cell, direction);
        }

        static void AddBoundaryEdge(List<BoundaryEdge> edges,
                                    Dictionary<Vector2Int, List<int>> outgoing,
                                    Vector2Int start, Vector2Int end, bool touchesOutside)
        {
            int index = edges.Count;
            edges.Add(new BoundaryEdge(start, end, touchesOutside));
            if (!outgoing.TryGetValue(start, out var list))
            {
                list = new List<int>(2);
                outgoing.Add(start, list);
            }
            list.Add(index);
        }

        static int FindNextBoundaryEdge(Vector2Int at, Vector2Int incoming,
                                        Dictionary<Vector2Int, List<int>> outgoing,
                                        List<BoundaryEdge> edges, bool[] used)
        {
            if (!outgoing.TryGetValue(at, out var candidates)) return -1;

            int best = -1;
            int bestTurn = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                int candidate = candidates[i];
                if (used[candidate]) continue;
                Vector2Int next = edges[candidate].end - edges[candidate].start;
                int cross = incoming.x * next.y - incoming.y * next.x;
                int dot = incoming.x * next.x + incoming.y * next.y;
                // Prefer a left turn at a diagonal-touch vertex, then straight, right and back.
                int turn = cross > 0 ? 3 : dot > 0 ? 2 : cross < 0 ? 1 : 0;
                if (turn <= bestTurn) continue;
                bestTurn = turn;
                best = candidate;
            }
            return best;
        }

        static void PaintBoundaryContour(Transform root, PRoom room, BoardAssets a,
                                         List<BoundaryEdge> edges, LevelModel model)
        {
            int doorway = -1;
            for (int i = 0; i < edges.Count; i++)
                if (IsNestedDoorway(edges[i], room, model))
                {
                    doorway = i;
                    break;
                }

            if (doorway < 0)
            {
                var closed = new List<Vector2Int>(edges.Count);
                bool touchesOutside = false;
                for (int i = 0; i < edges.Count; i++)
                {
                    closed.Add(edges[i].start);
                    touchesOutside |= edges[i].touchesOutside;
                }
                PaintBoundaryContourPath(root, room, a, closed, touchesOutside, true);
                return;
            }

            // Start immediately after a doorway so a run can never wrap across the list boundary.
            // Each skipped edge is exactly one authored boundary cell, leaving a clean threshold.
            var run = new List<Vector2Int>();
            int index = (doorway + 1) % edges.Count;
            for (int visited = 0; visited < edges.Count; visited++)
            {
                BoundaryEdge edge = edges[index];
                if (IsNestedDoorway(edge, room, model))
                {
                    PaintBoundaryContourPath(root, room, a, run, true, false);
                    run.Clear();
                }
                else
                {
                    if (run.Count == 0) run.Add(edge.start);
                    run.Add(edge.end);
                }
                index = (index + 1) % edges.Count;
            }
            PaintBoundaryContourPath(root, room, a, run, true, false);
        }

        static void PaintBoundaryContourPath(Transform root, PRoom room, BoardAssets a,
                                             List<Vector2Int> gridPoints, bool touchesOutside,
                                             bool loop)
        {
            if (gridPoints == null || gridPoints.Count < 2) return;
            var contour = new GameObject(touchesOutside ? "BoundaryWall" : "Wall");
            contour.transform.SetParent(root, false);

            Vector3[] points = new Vector3[gridPoints.Count];
            for (int i = 0; i < gridPoints.Count; i++)
                points[i] = new Vector3(
                    gridPoints[i].x - room.width * 0.5f,
                    gridPoints[i].y - room.height * 0.5f,
                    0f);

            Material material = BoundaryLineMaterial(a);
            if (a.simplifyBoundaryContours)
            {
                // Tutorials use one restrained structural edge. Keep it in the board's blue/cyan
                // family so the same near-black seam can never reappear on a lesson board.
                Color tutorialEdge = Color.Lerp(a.frameColor, OptionOneBevel, 0.45f);
                tutorialEdge.a = 0.76f;
                CreateContourLine(contour.transform, "TutorialBoundary", points, material,
                    tutorialEdge, 0.085f,
                    OrderWall + 1, loop);
                return;
            }

            // Do not draw the old black ContourShadow/NavyRail layers. They protruded past the
            // coloured boundary at open doorways and concave corners, producing the thin black
            // horizontal/vertical seams seen in multiple levels. The remaining glow, bevel and
            // cyan edge preserve a readable premium boundary without a black line artifact.
            CreateContourLine(contour.transform, "CyanGlow", points, material,
                new Color(a.frameColor.r, a.frameColor.g, a.frameColor.b, 0.22f),
                0.18f, OrderWall, loop);
            CreateContourLine(contour.transform, "CobaltBevel", points, material,
                OptionOneBevel, 0.105f, OrderWall + 2, loop);
            CreateContourLine(contour.transform, "CyanEdge", points, material,
                a.frameColor, 0.045f, OrderWall + 3, loop);
        }

        static Material BoundaryLineMaterial(BoardAssets a)
        {
            SpriteRenderer source = a.wallPrefab != null
                ? a.wallPrefab.GetComponentInChildren<SpriteRenderer>(true)
                : null;
            if (source == null && a.floorPrefab != null)
                source = a.floorPrefab.GetComponentInChildren<SpriteRenderer>(true);
            return source != null ? source.sharedMaterial : null;
        }

        static void CreateContourLine(Transform parent, string name, Vector3[] points,
                                      Material material, Color color, float width, int order,
                                      bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            if (material != null) line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCornerVertices = 3;
            line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.TransformZ;
            line.sortingOrder = order;
        }

        static void PaintChapterLightCells(Transform root, PRoom room, BoardAssets a)
        {
            if (a.cellSprite == null || a.ringSprite == null || room.width < 4 || room.height < 4)
                return;

            // One restrained ambient beacon is enough to keep the chamber alive. Real goals stay
            // brighter, so the decoration cannot be confused with something the player must use.
            Vector2Int accent = FindOpenLightCell(room, false);
            if (accent.x >= 0) CreateChapterLightCell(root, room, a, accent, 0f);
        }

        static Vector2Int FindOpenLightCell(PRoom room, bool highLeft)
        {
            int minX = 1, maxX = room.width - 2;
            int minY = 1, maxY = room.height - 2;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int step = 0; step <= Mathf.Max(room.width, room.height); step++)
                {
                    int x = highLeft ? minX + pass + step : maxX - pass - step;
                    int y = highLeft ? maxY - pass : minY + pass;
                    if (x < minX || x > maxX || y < minY || y > maxY) continue;
                    if (!room.wall[x, y]) return new Vector2Int(x, y);
                }
            }
            return new Vector2Int(-1, -1);
        }

        static void CreateChapterLightCell(Transform root, PRoom room, BoardAssets a,
                                           Vector2Int cell, float phase)
        {
            var beacon = new GameObject("AmbientBlueCell");
            beacon.transform.SetParent(root, false);
            beacon.transform.localPosition = Cell(room, cell);

            if (a.glowSprite != null)
            {
                var halo = new GameObject("SoftCyanHalo");
                halo.transform.SetParent(beacon.transform, false);
                halo.transform.localScale = Vector3.one * 1.28f;
                var hsr = halo.AddComponent<SpriteRenderer>();
                hsr.sprite = a.glowSprite;
                hsr.color = new Color(0.05f, 0.62f, 0.86f, 0.18f);
                hsr.sortingOrder = OrderFloorCell + 1;

                var breathe = halo.AddComponent<UIPulse>();
                breathe.amplitude = 0.035f;
                breathe.speed = 0.72f + phase * 0.025f;
            }

            var glass = new GameObject("LitGlass");
            glass.transform.SetParent(beacon.transform, false);
            glass.transform.localScale = Vector3.one * 0.72f;
            var gsr = glass.AddComponent<SpriteRenderer>();
            gsr.sprite = a.cellSprite;
            gsr.color = new Color(0.20f, 0.54f, 0.72f, 0.18f);
            gsr.sortingOrder = OrderFloorCell + 2;

            var cyanRing = new GameObject("CyanBloomRing");
            cyanRing.transform.SetParent(beacon.transform, false);
            cyanRing.transform.localScale = Vector3.one * 0.86f;
            var csr = cyanRing.AddComponent<SpriteRenderer>();
            csr.sprite = a.ringSprite;
            csr.color = new Color(0.08f, 0.70f, 0.92f, 0.32f);
            csr.sortingOrder = OrderFloorCell + 2;

            var whiteRing = new GameObject("WhiteCoreRing");
            whiteRing.transform.SetParent(beacon.transform, false);
            whiteRing.transform.localScale = Vector3.one * 0.72f;
            var wsr = whiteRing.AddComponent<SpriteRenderer>();
            wsr.sprite = a.ringSprite;
            wsr.color = new Color(0.58f, 0.82f, 0.94f, 0.60f);
            wsr.sortingOrder = OrderFloorCell + 3;

            // Four tiny corner flashes sharpen the silhouette at game-camera scale and keep the
            // cell readable against both the navy floor and the cyan architectural contour.
            Color spark = new Color(0.56f, 0.86f, 1f, 0.35f);
            const float corner = 0.29f;
            const float dash = 0.16f;
            const float thick = 0.035f;
            Bar(beacon.transform, a.cellSprite, spark, OrderFloorCell + 3,
                new Vector2(-corner, corner), dash, thick, 0f);
            Bar(beacon.transform, a.cellSprite, spark, OrderFloorCell + 3,
                new Vector2(corner, -corner), dash, thick, 0f);
        }

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
            bool left = IsWall(room, x - 1, y);
            bool right = IsWall(room, x + 1, y);
            bool boundary = x == 0 || y == 0 || x == room.width - 1 || y == room.height - 1;
            bool optionOne = UsesOptionOneSkin(a);
            Color wallFace = optionOne
                ? a.wallColor
                : boundary
                    ? Color.Lerp(a.frameColor, new Color(0.94f, 0.97f, 1f, 1f), 0.74f)
                    : Lighten(a.wallColor, 0.10f);

            // Keep every visual layer of one wall cell together. Besides making the hierarchy
            // clearer, this lets recursive-box previews hide perimeter walls without leaving their
            // shadows or highlight caps behind as black/grey fragments.
            var wallRoot = new GameObject(boundary ? "BoundaryWall" : "Wall").transform;
            wallRoot.SetParent(root, false);
            wallRoot.localPosition = p;

            // Cast a short, soft shadow only where the mass actually meets open floor. Option 1
            // is intentionally shallow: walls stay square architecture instead of tall blocks.
            if (!below && a.glowSprite != null)
            {
                var sh = new GameObject("WallShadow");
                sh.transform.SetParent(wallRoot, false);
                sh.transform.localPosition = new Vector3(0f, -0.405f, 0f);
                sh.transform.localScale = new Vector3(1.15f, 0.52f, 1f);
                var ssr = sh.AddComponent<SpriteRenderer>();
                ssr.sprite = a.glowSprite;
                ssr.color = new Color(0f, 0f, 0f, 0.28f);
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
                if (bsr != null) bsr.color = wallFace;
            }
            else
            {
                foreach (var sr in w.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = wallFace;
            }

            // A thin lit lip supplies depth without turning the square wall footprint into a
            // rectangle. It is deliberately less than half the height of the previous cap.
            if (!above && a.cellSprite != null)
            {
                var cap = new GameObject("WallCap");
                cap.transform.SetParent(wallRoot, false);
                cap.transform.localPosition = new Vector3(0f, 0.435f, 0f);
                cap.transform.localScale = new Vector3(0.92f, 0.085f, 1f);
                var csr = cap.AddComponent<SpriteRenderer>();
                csr.sprite = a.cellSprite;
                csr.color = Lighten(wallFace, boundary ? 0.34f : 0.28f);
                csr.sortingOrder = OrderWall + 1;
            }

            if (!left && a.cellSprite != null)
                Bar(wallRoot, a.cellSprite, Lighten(wallFace, 0.32f), OrderWall + 1,
                    new Vector2(-0.462f, 0f), 0.86f, 0.030f, 90f);

            if (!right && a.cellSprite != null)
                Bar(wallRoot, a.cellSprite, Darken(wallFace, 0.28f), OrderWall + 1,
                    new Vector2(0.462f, -0.008f), 0.86f, 0.034f, 90f);

            if (!below && a.cellSprite != null)
                Bar(wallRoot, a.cellSprite, Darken(wallFace, 0.34f), OrderWall + 1,
                    new Vector2(0f, -0.462f), 0.86f, 0.034f, 0f);
        }

        static GameObject CreateSlicedPanel(Transform parent, GameObject prefab, string name,
            Vector2 size, Color color, int order, Vector2 offset)
        {
            var panel = Object.Instantiate(prefab, parent);
            panel.name = name;
            panel.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            var sr = panel.GetComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = color;
            sr.sortingOrder = order;
            return panel;
        }

        // Goals are floor markings, but their coloured rim must still read as the SAME colour as
        // the object that belongs there. The old 22-24% player-goal alpha blended a cyan/blue goal
        // into the navy floor until it looked grey-purple. Keeping the socket translucent while
        // giving the rim and silhouette enough colour solves that mismatch on every campaign and
        // tutorial board without making a goal look like a second movable actor.
        static void StyleGoal(GameObject go, Color color, bool playerSilhouette, BoardAssets a,
            LevelModel model = null, PRoom room = null, Vector2Int cell = default, int colour = 0)
        {
            if (!playerSilhouette && room != null
                && PremiumCargoSocket.TryBuild(go, a, model, room, cell, color, colour)) return;
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                string part = sr.gameObject.name;
                if (UsesOptionOneSkin(a))
                {
                    if (playerSilhouette)
                    {
                        if (part == "Ring")
                            sr.transform.localScale = Vector3.one * OptionOneObjectSize;
                        else if (part == "Socket")
                            sr.transform.localScale = Vector3.one * (OptionOneObjectSize * 0.86f);
                        else if (part == "Glow")
                            sr.transform.localScale = Vector3.one * (OptionOneObjectSize * 1.08f);
                    }
                    else
                    {
                        // Keep every visible layer inside the exact same 0.90 footprint as the
                        // player. Using three different sizes made the coloured socket look tiny
                        // while its translucent surround looked oversized. Equal bounds make the
                        // yellow target read as one intentional, cell-sized gameplay element.
                        if (part == "Ring")
                            sr.transform.localScale = Vector3.one * OptionOneObjectSize;
                        else if (part == "Socket")
                            sr.transform.localScale = Vector3.one * OptionOneObjectSize;
                        else if (part == "Glow")
                            sr.transform.localScale = Vector3.one * OptionOneObjectSize;

                        // A translucent yellow socket over the cyan route blended into the dull
                        // green patch shown in the report. Keep the semantic cargo hue on the rim,
                        // but make the inset an opaque navy-orange material so it belongs to the
                        // cabinet palette at every room depth.
                        Color targetAccent = Darken(color, 0.10f);
                        if (part == "Glow")
                            sr.color = new Color(targetAccent.r, targetAccent.g,
                                targetAccent.b, 0.07f);
                        else if (part == "Socket")
                        {
                            Color inset = Color.Lerp(OptionOneFloor, targetAccent, 0.30f);
                            sr.color = new Color(inset.r, inset.g, inset.b, 0.96f);
                        }
                        else
                        {
                            Color rim = Lighten(targetAccent, 0.08f);
                            sr.color = new Color(rim.r, rim.g, rim.b, 0.94f);
                        }
                        continue;
                    }
                }
                float alpha = part == "Glow" ? (playerSilhouette ? 0.12f : 0.10f)
                    : part == "Socket" ? (playerSilhouette ? 0.20f : 0.36f)
                    : playerSilhouette ? 0.78f : 0.92f;
                sr.color = new Color(color.r, color.g, color.b, alpha);
            }

            if (!playerSilhouette || a.cellSprite == null) return;

            AddSoftGoalBevel(go, color);

            var silhouette = new GameObject("GoalSilhouette");
            silhouette.transform.SetParent(go.transform, false);
            silhouette.transform.localScale = Vector3.one * 0.64f;
            var body = silhouette.AddComponent<SpriteRenderer>();
            body.sprite = a.cellSprite;
            body.color = new Color(color.r, color.g, color.b, 0.54f);
            body.sortingOrder = OrderGoal + 1;

            Color eye = Color.Lerp(color, Color.white, 0.62f);
            eye.a = 0.88f;
            Bar(silhouette.transform, a.cellSprite, eye, OrderGoal + 2,
                new Vector2(-0.16f, 0.055f), 0.19f, 0.14f, 90f);
            Bar(silhouette.transform, a.cellSprite, eye, OrderGoal + 2,
                new Vector2(0.16f, 0.055f), 0.19f, 0.14f, 90f);
        }

        // Chapter V keeps one focused rule: match the three cargo families. These tiny embossed
        // marks turn abstract colours into familiar objects while preserving the premium cabinet
        // language: coral has a branching sprig, sky has wind streaks, and green has a leaf vein.
        // The identical mark is rendered on the corresponding floor socket.
        static void AddColourIdentity(Transform parent, BoardAssets a, int colour,
                                      Color baseColor, int order, bool goal)
        {
            if (parent == null || a == null || a.cellSprite == null || colour < 1 || colour > 3)
                return;

            var mark = new GameObject(colour == 1 ? "CoralMark"
                : colour == 2 ? "SkyMark" : "LeafMark");
            mark.transform.SetParent(parent, false);
            mark.transform.localPosition = new Vector3(0f, 0f, -0.01f);

            Color ink = Lighten(baseColor, goal ? 0.48f : 0.62f);
            ink.a = goal ? 0.82f : 0.94f;
            float thickness = goal ? 0.042f : 0.052f;

            if (colour == 1)
            {
                // Coral: one stem growing into two unmistakable branches.
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(0f, -0.02f), 0.42f, thickness, 90f);
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(-0.10f, 0.10f), 0.25f, thickness, 42f);
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(0.10f, 0.10f), 0.25f, thickness, 138f);
            }
            else if (colour == 2)
            {
                // Sky: three offset wind streaks, with a longer centre gust.
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(-0.055f, 0.13f), 0.34f, thickness, 0f);
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(0.025f, 0f), 0.50f, thickness, 0f);
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(-0.075f, -0.13f), 0.30f, thickness, 0f);
            }
            else
            {
                // Green: a diagonal leaf vein with two smaller veins.
                Bar(mark.transform, a.cellSprite, ink, order,
                    Vector2.zero, 0.48f, thickness, 48f);
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(-0.07f, 0.08f), 0.22f, thickness, 108f);
                Bar(mark.transform, a.cellSprite, ink, order,
                    new Vector2(0.08f, -0.07f), 0.22f, thickness, -18f);
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

        static Color ReadableNestedPathColor(Color c)
        {
            // A 7x7 room can be compressed into a single meta-box cell (Level 14). At that size
            // the old near-black route became only a few dark pixels. Use a luminous navy-cyan
            // path inside every recursive room so its actual walkable shape survives the preview.
            Color visible = Color.Lerp(c, OptionOneCyan, 0.46f);
            visible = Color.Lerp(visible, Color.white, 0.12f);
            visible.a = 1f;
            return visible;
        }

        // Nested shells stay in the same navy/indigo family as the cabinet. Depth is communicated
        // with a separate luminous rim, not by flooding the screen with a flat bright colour.
        static Color NestedShellColor(int roomId)
        {
            switch (Mathf.Abs(roomId) % 4)
            {
                case 1: return new Color(0.030f, 0.145f, 0.360f, 1f); // cobalt navy
                case 2: return new Color(0.020f, 0.205f, 0.285f, 1f); // deep teal
                case 3: return new Color(0.070f, 0.070f, 0.285f, 1f); // deep indigo
                default: return new Color(0.105f, 0.040f, 0.220f, 1f); // deep violet
            }
        }

        static Color MovableNestedShellColor(int chapter)
        {
            // A movable room-box satisfies an amber cargo socket, so its body must advertise the
            // same relationship. The chapter argument remains for API stability and clarity at
            // call sites; chapter identity belongs to the player, not to cargo semantics.
            return Darken(OptionOneBox, 0.46f);
        }

        static Color MovableNestedShellAccentColor(int chapter)
        {
            return Lighten(OptionOneBox, 0.10f);
        }

        static Color NestedShellAccentColor(int roomId)
        {
            switch (Mathf.Abs(roomId) % 4)
            {
                // The whole frame becomes room-sized during entry, so these are deliberately
                // deep cabinet colours. The narrow bevel bars below supply the bright light;
                // using a full-strength neon colour here made the enlarged shell look flat.
                case 1: return new Color(0.035f, 0.30f, 0.70f, 1f); // cobalt blue
                case 2: return new Color(0.020f, 0.40f, 0.48f, 1f); // deep cyan
                case 3: return new Color(0.120f, 0.20f, 0.65f, 1f); // indigo blue
                default: return new Color(0.200f, 0.08f, 0.55f, 1f); // cabinet violet
            }
        }

        static void AddNestedShellHighlights(GameObject box, BoardAssets a, Color color, int frameOrder)
        {
            if (box == null || a == null || a.cellSprite == null
                || box.transform.Find("NestedShellHighlightTop") != null) return;

            // Local bevel highlights provide visible light without a glow sprite. A sprite halo is
            // unsuitable here: when the camera enters the box, that one-cell halo is magnified to
            // screen size and becomes a flat colour wash. These narrow upper/left edges remain
            // attached to the shell at every recursive zoom depth.
            Color highlight = Color.Lerp(Color.Lerp(color, OptionOneCyan, 0.32f), Color.white, 0.28f);
            highlight.a = 0.88f;
            GameObject top = Bar(box.transform, a.cellSprite, highlight, frameOrder + 2,
                new Vector2(-0.025f, 0.405f), 0.58f, 0.018f, 0f);
            top.name = "NestedShellHighlightTop";
            GameObject left = Bar(box.transform, a.cellSprite, highlight, frameOrder + 2,
                new Vector2(-0.405f, 0.015f), 0.56f, 0.018f, 90f);
            left.name = "NestedShellHighlightLeft";
        }

        static void AddNestedShellDoorways(GameObject box, BoardAssets a, PRoom room,
                                           float roomScale, Color floorColor,
                                           PEntity container, PRoom parentRoom)
        {
            if (box == null || a == null || a.cellSprite == null || a.floorPrefab == null || room == null
                || box.transform.Find("NestedDoorways") != null) return;

            // Match the doorway bridge to the brighter nested-room route so the player can
            // visually follow the path while crossing into or out of the box.
            floorColor = ReadableNestedPathColor(floorColor);

            bool bottomDoor = HasUsableParentSide(container, parentRoom, Vector2Int.down)
                              && HasOpenBoundaryCell(room, false, false);
            bool topDoor = HasUsableParentSide(container, parentRoom, Vector2Int.up)
                           && HasOpenBoundaryCell(room, false, true);
            bool leftDoor = HasUsableParentSide(container, parentRoom, Vector2Int.left)
                            && HasOpenBoundaryCell(room, true, false);
            bool rightDoor = HasUsableParentSide(container, parentRoom, Vector2Int.right)
                             && HasOpenBoundaryCell(room, true, true);

            // SpriteMask support is material-dependent. On the cabinet material the old single
            // ring could therefore survive the mask and draw a bright bar straight across a real
            // doorway (most visibly over Chapter III's upward tutorial exit). Build the frame as
            // four authored, split sides instead. The missing segment is now real geometry, not a
            // rendering trick, so every recursive-room entrance stays visibly open in Editor and
            // WebGL builds.
            if (bottomDoor || topDoor || leftDoor || rightDoor)
                ReplaceNestedShellFrameWithSplitSides(box, a, room, roomScale,
                    bottomDoor, topDoor, leftDoor, rightDoor);

            // Only the recursive shell participates in these masks. Room art and entities keep
            // their normal renderers, so the diver remains visible as it crosses the threshold.
            SetOutsideMask(box.transform.Find("Backing"));
            SetOutsideMask(box.transform.Find("Frame"));
            SetOutsideMask(box.transform.Find("NestedShellHighlightTop"));
            SetOutsideMask(box.transform.Find("NestedShellHighlightLeft"));

            var doorwayRoot = new GameObject("NestedDoorways").transform;
            doorwayRoot.SetParent(box.transform, false);

            float halfRoomWidth = room.width * roomScale * 0.5f;
            float halfRoomHeight = room.height * roomScale * 0.5f;
            float shellOutside = NestedShellSize * 0.5f + 0.045f;
            const float floorOverlap = 0.065f;

            float horizontalDepth = shellOutside - halfRoomHeight + floorOverlap;
            float verticalDepth = shellOutside - halfRoomWidth + floorOverlap;
            float horizontalY = (shellOutside + halfRoomHeight - floorOverlap) * 0.5f;
            float verticalX = (shellOutside + halfRoomWidth - floorOverlap) * 0.5f;
            if (bottomDoor)
                CreateHorizontalDoorwayRuns(doorwayRoot, a, room, roomScale, floorColor,
                    false, -horizontalY, horizontalDepth);
            if (topDoor)
                CreateHorizontalDoorwayRuns(doorwayRoot, a, room, roomScale, floorColor,
                    true, horizontalY, horizontalDepth);
            if (leftDoor)
                CreateVerticalDoorwayRuns(doorwayRoot, a, room, roomScale, floorColor,
                    false, -verticalX, verticalDepth);
            if (rightDoor)
                CreateVerticalDoorwayRuns(doorwayRoot, a, room, roomScale, floorColor,
                    true, verticalX, verticalDepth);
        }

        static bool HasOpenBoundaryCell(PRoom room, bool vertical, bool highEdge)
        {
            if (room == null) return false;
            if (vertical)
            {
                int x = highEdge ? room.width - 1 : 0;
                for (int y = 0; y < room.height; y++)
                    if (!room.wall[x, y]) return true;
            }
            else
            {
                int y = highEdge ? room.height - 1 : 0;
                for (int x = 0; x < room.width; x++)
                    if (!room.wall[x, y]) return true;
            }
            return false;
        }

        static void ReplaceNestedShellFrameWithSplitSides(GameObject box, BoardAssets a,
                                                           PRoom room, float roomScale,
                                                           bool bottomDoor, bool topDoor,
                                                           bool leftDoor, bool rightDoor)
        {
            if (box == null || a == null || a.cellSprite == null || room == null
                || box.transform.Find("NestedSplitFrame") != null) return;

            Transform frame = box.transform.Find("Frame");
            var frameRenderer = frame != null ? frame.GetComponent<SpriteRenderer>() : null;
            if (frameRenderer == null) return;

            frameRenderer.enabled = false;

            // Do not generate the old split-frame rail. Its horizontal and vertical SpriteRenderer
            // bars overlapped at every corner, producing the thin doubled square and little cross
            // marks shown in the reference screenshot. Keep the backing, miniature room and real
            // doorway bridges; only this decorative line layer is removed on every level.
            DisableSprite(box.transform.Find("NestedShellHighlightTop"));
            DisableSprite(box.transform.Find("NestedShellHighlightLeft"));

            // Keep a marker so repeated setup calls do not try to construct the removed rail.
            var root = new GameObject("NestedSplitFrame").transform;
            root.SetParent(box.transform, false);
        }

        static void DisableSprite(Transform target)
        {
            if (target == null) return;
            var renderer = target.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

        static List<Vector2> DoorwayGaps(PRoom room, float roomScale,
                                         bool vertical, bool highEdge)
        {
            var gaps = new List<Vector2>();
            int count = vertical ? room.height : room.width;
            int fixedCoordinate = highEdge
                ? (vertical ? room.width - 1 : room.height - 1)
                : 0;
            int runStart = -1;
            for (int i = 0; i <= count; i++)
            {
                bool open = false;
                if (i < count)
                    open = vertical
                        ? !room.wall[fixedCoordinate, i]
                        : !room.wall[i, fixedCoordinate];
                if (open && runStart < 0) runStart = i;
                if (open || runStart < 0) continue;

                int runEnd = i - 1;
                int doorwayCell = (runStart + runEnd) / 2;
                float centre = (doorwayCell - (count - 1) * 0.5f) * roomScale;
                float halfGap = roomScale * 0.5f + 0.026f;
                gaps.Add(new Vector2(centre - halfGap, centre + halfGap));
                runStart = -1;
            }
            return gaps;
        }

        static void CreateSplitFrameSide(Transform parent, Sprite sprite, Color accent,
                                         int order, List<Vector2> gaps, bool vertical,
                                         float sideCoordinate, string sideName)
        {
            const float edge = 0.445f;
            const float minimumSegment = 0.012f;
            float cursor = -edge;
            int segment = 0;

            for (int i = 0; i <= gaps.Count; i++)
            {
                float end = i < gaps.Count ? Mathf.Clamp(gaps[i].x, -edge, edge) : edge;
                if (end - cursor > minimumSegment)
                {
                    float length = end - cursor;
                    float mid = (cursor + end) * 0.5f;
                    Vector2 position = vertical
                        ? new Vector2(sideCoordinate, mid)
                        : new Vector2(mid, sideCoordinate);

                    // These dimensions are deliberately narrow. Recursive camera zoom magnifies
                    // the shell along with the room, so normal one-cell wall thickness becomes a
                    // screen-sized rectangle at depth two or three.
                    GameObject shadow = Bar(parent, sprite, Darken(accent, 0.58f), order,
                        position, length + 0.010f, 0.038f, vertical ? 90f : 0f);
                    shadow.name = $"NestedFrame_{sideName}_{segment}_Shadow";
                    GameObject light = Bar(parent, sprite, accent, order + 1,
                        position, length + 0.004f, 0.014f, vertical ? 90f : 0f);
                    light.name = $"NestedFrame_{sideName}_{segment}_Light";
                    segment++;
                }

                if (i < gaps.Count)
                    cursor = Mathf.Max(cursor, Mathf.Clamp(gaps[i].y, -edge, edge));
            }
        }

        static bool HasUsableParentSide(PEntity container, PRoom parentRoom,
                                        Vector2Int direction)
        {
            // A movable recursive room can expose a currently blocked side after it is pushed, so
            // all of its authored boundary openings stay visible. An anchored room never changes
            // position: a parent wall or the outer boundary therefore makes that side permanently
            // impossible, and drawing a bridge there advertises a route the model can never take.
            if (container == null || parentRoom == null || !container.anchored) return true;
            Vector2Int adjacent = container.pos + direction;
            return parentRoom.InBounds(adjacent) && !parentRoom.wall[adjacent.x, adjacent.y];
        }

        // A boundary may expose several neighbouring cells. Stretching the shell bridge across
        // the whole run makes a miniature recursive room grow long bars that look like dozens of
        // separate exits. Represent each contiguous run with one compact threshold instead. The
        // model still owns the exact passable cells; this is only the shell's readable doorway
        // marker, and it remains centred on the authored opening.
        static void CreateHorizontalDoorwayRuns(Transform parent, BoardAssets a, PRoom room,
                                                float roomScale, Color floorColor, bool top,
                                                float localY, float depth)
        {
            int y = top ? room.height - 1 : 0;
            int runStart = -1;
            for (int x = 0; x <= room.width; x++)
            {
                bool open = x < room.width && !room.wall[x, y];
                if (open && runStart < 0) runStart = x;
                if (open || runStart < 0) continue;

                int runEnd = x - 1;
                int doorwayCell = (runStart + runEnd) / 2;
                float localX = (doorwayCell - (room.width - 1) * 0.5f) * roomScale;
                float span = roomScale + 0.012f;
                string edge = top ? "Top" : "Bottom";
                CreateDoorway(parent, a.floorPrefab, a.cellSprite, floorColor,
                    $"{edge}_{runStart}_{runEnd}", new Vector2(localX, localY), span, depth);
                runStart = -1;
            }
        }

        static void CreateVerticalDoorwayRuns(Transform parent, BoardAssets a, PRoom room,
                                              float roomScale, Color floorColor, bool right,
                                              float localX, float depth)
        {
            int x = right ? room.width - 1 : 0;
            int runStart = -1;
            for (int y = 0; y <= room.height; y++)
            {
                bool open = y < room.height && !room.wall[x, y];
                if (open && runStart < 0) runStart = y;
                if (open || runStart < 0) continue;

                int runEnd = y - 1;
                int doorwayCell = (runStart + runEnd) / 2;
                float localY = (doorwayCell - (room.height - 1) * 0.5f) * roomScale;
                float span = roomScale + 0.012f;
                string edge = right ? "Right" : "Left";
                CreateDoorway(parent, a.floorPrefab, a.cellSprite, floorColor,
                    $"{edge}_{runStart}_{runEnd}", new Vector2(localX, localY), depth, span);
                runStart = -1;
            }
        }

        static void SetOutsideMask(Transform target)
        {
            if (target == null) return;
            var sr = target.GetComponent<SpriteRenderer>();
            if (sr != null) sr.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
        }

        static void CreateDoorway(Transform parent, GameObject floorPrefab, Sprite maskSprite,
                                  Color floorColor, string suffix, Vector2 position,
                                  float width, float height)
        {
            // The logical boundary cell stops at the scaled miniature-room edge, while the orange
            // recursive shell continues to the edge of the gameplay piece. SpriteMask behaviour
            // varies with material/platform, so a mask alone can leave an orange strip across a
            // real doorway (especially in tutorial RenderTextures). Paint one narrow, flush bridge
            // with the exact same sliced floor prefab, material and tint used by WalkableFloor.
            // The old cellSprite bridge received the same colour value through a different sprite
            // texture, so it still rendered as a visibly separate blue rectangle. Keep the shared
            // floor panel on the floor-decoration layer: order 55 placed this blue
            // bridge above the player and cargo inside a SortingGroup, cutting moving objects in
            // half at every recursive threshold. OrderGoal - 1 still covers the shell backing and
            // ordinary floor, while every goal and gameplay entity remains fully visible above it.
            // Its span is exactly one authored boundary opening and its depth is only the distance
            // to the shell edge, so it cannot create the old square protrusion.
            if (floorPrefab != null && width > 0.001f && height > 0.001f)
            {
                CreateSlicedPanel(parent, floorPrefab, $"NestedDoorwayFloor_{suffix}",
                    new Vector2(width, height), floorColor, OrderGoal - 1, position);
            }

            var maskObject = new GameObject($"NestedDoorwayMask_{suffix}");
            maskObject.transform.SetParent(parent, false);
            maskObject.transform.localPosition = new Vector3(position.x, position.y, 0f);
            maskObject.transform.localScale = new Vector3(width, height, 1f);

            var mask = maskObject.AddComponent<SpriteMask>();
            mask.sprite = maskSprite;
            mask.alphaCutoff = 0.05f;
            mask.isCustomRangeActive = true;
            mask.frontSortingLayerID = 0;
            mask.frontSortingOrder = 200;
            mask.backSortingLayerID = 0;
            mask.backSortingOrder = -100;
        }

        // One continuous shallow-3D skin across all 50 levels. Progression comes from puzzle
        // design, never from replacing the approved board style with a different chapter palette.
        static void ApplyChapterSkin(BoardAssets a)
        {
            if (!UsesOptionOneSkin(a)) return;
            a.frameColor = OptionOneCyan;
            a.wallColor = OptionOneWall;
            a.roomColors = new[]
            {
                OptionOneFloor,
                OptionOneDepthBlue,
                OptionOneDepthTeal,
                OptionOneDepthIndigo,
                OptionOneDepthViolet,
            };
            a.gutterColor = OptionOneFloor;
            a.gridColor = Color.clear;
            a.playerColor = PlayerColorForChapter(a.chapter);
            a.boxColor = OptionOneBox;
            a.floorVignette = 0.018f;
            a.pieceGlow = 0.035f;
            a.cellLift = 0f;
            a.floorTexTint = Color.clear;
        }

        // Public for tutorial/demo renderers: their separate mini-board must use the same world
        // identity as the campaign board it introduces. Values outside the five-world campaign
        // are clamped so editor previews and stale saves remain deterministic.
        public static Color PlayerColorForChapter(int chapter)
        {
            switch (Mathf.Clamp(chapter, 0, 4))
            {
                case 0: return ChapterOnePlayer;
                case 1: return ChapterTwoPlayer;
                case 2: return ChapterThreePlayer;
                case 3: return ChapterFourPlayer;
                default: return ChapterFivePlayer;
            }
        }

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

        // The selected Option 1 treatment: a square face with only a hairline bevel and a tiny
        // lower-right depth reveal. All layers live under the entity root, so existing movement,
        // squash, goal seating and recursive reparent animation continue to affect one coherent
        // piece. This is presentation only; the logical one-cell footprint never changes.
        static void AddSoftBevel(GameObject piece, Transform face, Color color, BoardAssets a)
        {
            if (piece == null || face == null || a == null || a.cellSprite == null
                || piece.transform.Find("SoftBevelDepth") != null) return;

            var source = face.GetComponent<SpriteRenderer>();
            if (source == null || source.sprite == null) return;

            var depth = new GameObject("SoftBevelDepth");
            depth.transform.SetParent(piece.transform, false);
            depth.transform.localPosition = new Vector3(0.022f, -0.030f, 0f);
            depth.transform.localScale = Vector3.one * OptionOneObjectSize;
            var depthRenderer = depth.AddComponent<SpriteRenderer>();
            depthRenderer.sprite = source.sprite;
            depthRenderer.sharedMaterial = source.sharedMaterial;
            depthRenderer.sortingLayerID = source.sortingLayerID;
            depthRenderer.sortingOrder = source.sortingOrder - 1;
            Color depthColor = Darken(color, 0.50f);
            depthRenderer.color = new Color(depthColor.r, depthColor.g, depthColor.b, 0.82f);

            Color highlight = Lighten(color, 0.34f);
            highlight.a = 0.64f;
            GameObject top = Bar(piece.transform, a.cellSprite, highlight,
                source.sortingOrder + 1, new Vector2(-0.018f, 0.414f), 0.68f, 0.018f, 0f);
            top.name = "SoftBevelTop";
            GameObject left = Bar(piece.transform, a.cellSprite, highlight,
                source.sortingOrder + 1, new Vector2(-0.414f, 0.010f), 0.64f, 0.016f, 90f);
            left.name = "SoftBevelLeft";

            Color shade = Darken(color, 0.42f);
            shade.a = 0.58f;
            GameObject bottom = Bar(piece.transform, a.cellSprite, shade,
                source.sortingOrder + 1, new Vector2(0.018f, -0.414f), 0.68f, 0.020f, 0f);
            bottom.name = "SoftBevelBottom";
            GameObject right = Bar(piece.transform, a.cellSprite, shade,
                source.sortingOrder + 1, new Vector2(0.414f, -0.010f), 0.64f, 0.020f, 90f);
            right.name = "SoftBevelRight";
        }

        static void AddSoftFrameBevel(Transform frame, SpriteRenderer source, Color color)
        {
            if (frame == null || source == null || source.sprite == null
                || frame.Find("SoftFrameDepth") != null) return;

            var depth = new GameObject("SoftFrameDepth");
            depth.transform.SetParent(frame, false);
            depth.transform.localPosition = new Vector3(0.020f, -0.026f, 0f);
            var depthRenderer = depth.AddComponent<SpriteRenderer>();
            depthRenderer.sprite = source.sprite;
            depthRenderer.sharedMaterial = source.sharedMaterial;
            depthRenderer.sortingLayerID = source.sortingLayerID;
            depthRenderer.sortingOrder = source.sortingOrder - 1;
            Color shade = Darken(color, 0.52f);
            depthRenderer.color = new Color(shade.r, shade.g, shade.b, 0.78f);

            var light = new GameObject("SoftFrameHighlight");
            light.transform.SetParent(frame, false);
            light.transform.localPosition = new Vector3(-0.006f, 0.008f, 0f);
            var lightRenderer = light.AddComponent<SpriteRenderer>();
            lightRenderer.sprite = source.sprite;
            lightRenderer.sharedMaterial = source.sharedMaterial;
            lightRenderer.sortingLayerID = source.sortingLayerID;
            lightRenderer.sortingOrder = source.sortingOrder + 1;
            Color shine = Lighten(color, 0.28f);
            lightRenderer.color = new Color(shine.r, shine.g, shine.b, 0.20f);
        }

        static void AddSoftGoalBevel(GameObject goal, Color color)
        {
            if (goal == null || goal.transform.Find("SoftGoalDepth") != null) return;
            Transform ring = goal.transform.Find("Ring");
            var source = ring != null ? ring.GetComponent<SpriteRenderer>() : null;
            if (source == null || source.sprite == null) return;

            var depth = new GameObject("SoftGoalDepth");
            depth.transform.SetParent(goal.transform, false);
            depth.transform.localPosition = new Vector3(0.014f, -0.020f, 0f);
            depth.transform.localScale = ring.localScale;
            var depthRenderer = depth.AddComponent<SpriteRenderer>();
            depthRenderer.sprite = source.sprite;
            depthRenderer.sharedMaterial = source.sharedMaterial;
            depthRenderer.sortingLayerID = source.sortingLayerID;
            depthRenderer.sortingOrder = source.sortingOrder - 1;
            Color shade = Darken(color, 0.52f);
            depthRenderer.color = new Color(shade.r, shade.g, shade.b, 0.64f);

            var light = new GameObject("SoftGoalHighlight");
            light.transform.SetParent(goal.transform, false);
            light.transform.localPosition = new Vector3(-0.004f, 0.006f, 0f);
            light.transform.localScale = ring.localScale;
            var lightRenderer = light.AddComponent<SpriteRenderer>();
            lightRenderer.sprite = source.sprite;
            lightRenderer.sharedMaterial = source.sharedMaterial;
            lightRenderer.sortingLayerID = source.sortingLayerID;
            lightRenderer.sortingOrder = source.sortingOrder + 1;
            Color shine = Lighten(color, 0.30f);
            lightRenderer.color = new Color(shine.r, shine.g, shine.b, 0.18f);
        }

        static void AddEyeOutline(GameObject piece, Transform eye, string name, Color playerColor,
                                  BoardAssets a)
        {
            if (eye == null || a.ringSprite == null || piece.transform.Find(name) != null) return;
            var eyeRenderer = eye.GetComponent<SpriteRenderer>();
            if (eyeRenderer == null) return;

            // A complete soft outline, never a white eye glint. It makes the dark eyes readable at
            // cabinet distance while preserving the calm two-dot expression of the player.
            var outline = new GameObject(name);
            outline.transform.SetParent(piece.transform, false);
            outline.transform.localPosition = eye.localPosition;
            outline.transform.localScale = new Vector3(0.225f, 0.295f, 1f);
            var sr = outline.AddComponent<SpriteRenderer>();
            sr.sprite = a.ringSprite;
            sr.sharedMaterial = eyeRenderer.sharedMaterial;
            sr.sortingLayerID = eyeRenderer.sortingLayerID;
            sr.sortingOrder = eyeRenderer.sortingOrder;
            sr.color = Color.Lerp(playerColor, Color.white, 0.58f);
            eyeRenderer.sortingOrder = sr.sortingOrder + 1;
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
