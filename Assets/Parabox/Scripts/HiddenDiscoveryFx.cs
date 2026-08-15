using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Rare, presentation-only discoveries.  The puzzle model is never changed: these veils only
    // control when a player gets to SEE an existing room or board region.  That keeps every stored
    // solution, collision rule and move limit intact while creating a clean "there is more here"
    // moment on a deliberately small selection of levels.
    public sealed class HiddenDiscoveryFx : MonoBehaviour
    {
        enum RevealTrigger
        {
            Proximity,
            EnterRoom,
            GatesOpen,
            LocksOpen,
            LatchOpen,
        }

        struct Spec
        {
            public int roomId;
            public int nestedRoomId;
            public RectInt area;
            public int revealDistance;
            public RevealTrigger trigger;

            public static Spec Region(int roomId, RectInt area, int distance)
                => new Spec
                {
                    roomId = roomId,
                    nestedRoomId = -1,
                    area = area,
                    revealDistance = distance,
                    trigger = RevealTrigger.Proximity,
                };

            public static Spec Nested(int roomId, RevealTrigger trigger)
                => new Spec
                {
                    roomId = -1,
                    nestedRoomId = roomId,
                    area = new RectInt(),
                    revealDistance = 0,
                    trigger = trigger,
                };
        }

        sealed class Veil
        {
            public Spec spec;
            public GameObject root;
            public readonly List<SpriteRenderer> covers = new List<SpriteRenderer>();
            public readonly List<Color> coverColors = new List<Color>();
            public readonly List<SpriteRenderer> hints = new List<SpriteRenderer>();
            public readonly List<Color> hintColors = new List<Color>();
            public bool revealed;
        }

        static Sprite solidMaskSprite;
        readonly List<Veil> veils = new List<Veil>();
        LevelModel model;
        Dictionary<int, Transform> roomRoots;
        Dictionary<PEntity, EntityView> views;
        Color[] roomColors;
        CameraFollow cameraFollow;
        int chapter;

        public void Configure(int levelIndex, LevelModel levelModel,
            Dictionary<int, Transform> roots, Dictionary<PEntity, EntityView> entityViews,
            Color[] colors, CameraFollow follow)
        {
            ClearVeils();
            model = levelModel;
            roomRoots = roots;
            views = entityViews;
            roomColors = colors;
            cameraFollow = follow;
            chapter = Mathf.Clamp(levelIndex / 10, 0, 4);

            foreach (Spec spec in SpecsFor(levelIndex))
            {
                Veil veil = spec.nestedRoomId >= 0
                    ? BuildNestedVeil(spec)
                    : BuildRegionVeil(spec);
                if (veil != null) veils.Add(veil);
            }

            if (veils.Count > 0)
                Debug.Log($"[Discovery] Level {levelIndex + 1}: {veils.Count} hidden discovery ready.");

            Refresh();
        }

        public void Refresh()
        {
            if (model == null || model.player == null) return;
            for (int i = 0; i < veils.Count; i++)
            {
                Veil veil = veils[i];
                if (!veil.revealed && ShouldReveal(veil.spec))
                    StartCoroutine(Reveal(veil));
            }
        }

        void Update()
        {
            // A hairline seam is the only clue. It breathes slowly enough to be noticed by a
            // curious player, but never reads as an arrow, objective marker or flashing hint.
            float breathe = 0.76f + Mathf.Sin(Time.unscaledTime * 1.65f) * 0.24f;
            for (int v = 0; v < veils.Count; v++)
            {
                Veil veil = veils[v];
                if (veil.revealed) continue;
                for (int i = 0; i < veil.hints.Count && i < veil.hintColors.Count; i++)
                {
                    if (veil.hints[i] == null) continue;
                    Color c = veil.hintColors[i];
                    c.a *= breathe;
                    veil.hints[i].color = c;
                }
            }
        }

        Veil BuildNestedVeil(Spec spec)
        {
            if (model == null || views == null
                || !model.rooms.TryGetValue(spec.nestedRoomId, out PRoom room)
                || room.containerBox == null
                || !views.TryGetValue(room.containerBox, out EntityView containerView)
                || containerView == null)
                return null;

            var veil = NewVeil(spec, "HiddenRoom_" + spec.nestedRoomId);
            veil.root.transform.SetParent(containerView.transform, false);

            // The frame remains as the only clue; every cell inside it is visually absent until
            // the player actually enters. This is a true hidden space, not a dimmed preview.
            AddCover(veil, veil.root.transform, Vector2.zero, new Vector2(0.70f, 0.70f),
                49, VeilColor(spec.nestedRoomId, 0.96f), "HiddenInterior");

            Color clue = HintColor(spec.nestedRoomId);
            AddHint(veil, veil.root.transform, new Vector2(0f, -0.342f),
                new Vector2(0.12f, 0.018f), 51, clue, "DiscoverySeam");

            return veil;
        }

        Veil BuildRegionVeil(Spec spec)
        {
            if (model == null || roomRoots == null
                || !model.rooms.TryGetValue(spec.roomId, out PRoom room)
                || !roomRoots.TryGetValue(spec.roomId, out Transform roomRoot)
                || roomRoot == null)
                return null;

            RectInt clipped = Clip(spec.area, room.width, room.height);
            // Never erase the board's outside silhouette. Leaving the perimeter wall visible makes
            // the secret read as architecture with something behind it, not a pasted rectangle.
            clipped = KeepPerimeter(clipped, room.width, room.height);
            if (clipped.width <= 0 || clipped.height <= 0) return null;
            spec.area = clipped;

            var veil = NewVeil(spec, "HiddenRegion_" + spec.roomId);
            veil.root.transform.SetParent(roomRoot, false);
            veil.root.transform.localPosition = Vector3.zero;

            // Mask every logical cell individually. There is no big rectangle, grid or darkened
            // node to announce the trick—the cells simply do not appear to exist yet.
            // A concealed corridor should read as part of the continuous wall architecture, not as
            // a black placeholder rectangle. It becomes navigable floor only during the reveal.
            Color hiddenCell = SampleWallColor(roomRoot, RegionVeilColor());
            for (int x = clipped.xMin; x < clipped.xMax; x++)
                for (int y = clipped.yMin; y < clipped.yMax; y++)
                    AddCover(veil, veil.root.transform, Cell(room, new Vector2Int(x, y)),
                        new Vector2(1.03f, 1.03f), 85, hiddenCell, $"HiddenCell_{x}_{y}");

            AddBoundaryHint(veil, room, clipped);

            return veil;
        }

        void AddBoundaryHint(Veil veil, PRoom room, RectInt area)
        {
            if (model == null || model.player == null || model.player.roomId != room.id) return;

            Vector2Int p = model.player.pos;
            Vector2 position;
            Vector2 size;
            if (p.x < area.xMin)
            {
                int y = Mathf.Clamp(p.y, area.yMin, area.yMax - 1);
                position = Cell(room, new Vector2Int(area.xMin, y)) + Vector3.left * 0.49f;
                size = new Vector2(0.026f, 0.28f);
            }
            else if (p.x >= area.xMax)
            {
                int y = Mathf.Clamp(p.y, area.yMin, area.yMax - 1);
                position = Cell(room, new Vector2Int(area.xMax - 1, y)) + Vector3.right * 0.49f;
                size = new Vector2(0.026f, 0.28f);
            }
            else if (p.y < area.yMin)
            {
                int x = Mathf.Clamp(p.x, area.xMin, area.xMax - 1);
                position = Cell(room, new Vector2Int(x, area.yMin)) + Vector3.down * 0.49f;
                size = new Vector2(0.28f, 0.026f);
            }
            else
            {
                int x = Mathf.Clamp(p.x, area.xMin, area.xMax - 1);
                position = Cell(room, new Vector2Int(x, area.yMax - 1)) + Vector3.up * 0.49f;
                size = new Vector2(0.28f, 0.026f);
            }

            AddHint(veil, veil.root.transform, position, size, 87,
                HintColor(room.id), "DiscoverySeam");
        }

        void AddCover(Veil veil, Transform parent, Vector2 position, Vector2 size,
            int order, Color color, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SolidMaskSprite();
            sr.sortingOrder = order;
            sr.color = color;
            veil.covers.Add(sr);
            veil.coverColors.Add(color);
        }

        void AddHint(Veil veil, Transform parent, Vector2 position, Vector2 size,
            int order, Color color, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SolidMaskSprite();
            sr.sortingOrder = order;
            sr.color = color;
            veil.hints.Add(sr);
            veil.hintColors.Add(color);
        }

        Veil NewVeil(Spec spec, string name)
        {
            return new Veil
            {
                spec = spec,
                root = new GameObject(name),
            };
        }

        static Sprite SolidMaskSprite()
        {
            if (solidMaskSprite != null) return solidMaskSprite;
            solidMaskSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            solidMaskSprite.name = "HiddenDiscoverySolidMask";
            solidMaskSprite.hideFlags = HideFlags.HideAndDontSave;
            return solidMaskSprite;
        }

        bool ShouldReveal(Spec spec)
        {
            // Entering the concealed room is always a fair fallback: no visual veil can ever hide
            // the space the player is currently using, even if its authored state trigger has not
            // fired yet.
            if (spec.nestedRoomId >= 0 && model.player.roomId == spec.nestedRoomId)
                return true;

            // A state-triggered region still reveals when the player physically reaches it. This
            // prevents presentation from ever hiding a usable cell if an unusual move order reaches
            // the secret before its intended switch/lock beat.
            if (spec.roomId >= 0 && model.player.roomId == spec.roomId
                && RegionDistance(spec) == 0)
                return true;

            switch (spec.trigger)
            {
                case RevealTrigger.EnterRoom:
                    return model.player.roomId == spec.nestedRoomId;
                case RevealTrigger.GatesOpen:
                    return model.GatesOpen() || model.HeavyGatesOpen();
                case RevealTrigger.LocksOpen:
                    return model.LocksOpen();
                case RevealTrigger.LatchOpen:
                    return model.latched;
                default:
                    if (model.player.roomId != spec.roomId) return false;
                    return RegionDistance(spec) <= spec.revealDistance;
            }
        }

        int RegionDistance(Spec spec)
        {
            Vector2Int p = model.player.pos;
            int dx = p.x < spec.area.xMin ? spec.area.xMin - p.x
                : p.x >= spec.area.xMax ? p.x - spec.area.xMax + 1 : 0;
            int dy = p.y < spec.area.yMin ? spec.area.yMin - p.y
                : p.y >= spec.area.yMax ? p.y - spec.area.yMax + 1 : 0;
            return dx + dy;
        }

        IEnumerator Reveal(Veil veil)
        {
            veil.revealed = true;
            Debug.Log("[Discovery] Hidden space revealed.");
            Sfx.RoomShift();
            if (cameraFollow != null) cameraFollow.Shake(0.035f, 11f);

            const float duration = 0.66f;
            float elapsed = 0f;
            while (elapsed < duration && veil.root != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                for (int i = 0; i < veil.covers.Count && i < veil.coverColors.Count; i++)
                {
                    // A short stagger makes the absent cells resolve into the board in a clean
                    // wave instead of fading one obvious rectangular overlay.
                    float delay = veil.covers.Count <= 1 ? 0f
                        : (i / (float)(veil.covers.Count - 1)) * 0.22f;
                    float cellT = Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((t - delay) / Mathf.Max(0.01f, 1f - delay)));
                    Color c = veil.coverColors[i];
                    c.a *= 1f - cellT;
                    veil.covers[i].color = c;
                }
                for (int i = 0; i < veil.hints.Count && i < veil.hintColors.Count; i++)
                {
                    if (veil.hints[i] == null) continue;
                    Color c = veil.hintColors[i];
                    c.a *= 1f - t;
                    veil.hints[i].color = c;
                }
                yield return null;
            }
            if (veil.root != null) veil.root.SetActive(false);
        }

        void ClearVeils()
        {
            StopAllCoroutines();
            for (int i = 0; i < veils.Count; i++)
                if (veils[i].root != null) Destroy(veils[i].root);
            veils.Clear();
        }

        Color VeilColor(int roomId, float darkness)
        {
            Color baseColor = roomColors != null && roomColors.Length > 0
                ? roomColors[Mathf.Abs(roomId) % roomColors.Length]
                : new Color(0.025f, 0.08f, 0.13f, 1f);
            Color c = Color.Lerp(baseColor, new Color(0.003f, 0.009f, 0.022f, 1f), darkness);
            c.a = 1f;
            return c;
        }

        Color HintColor(int roomId)
        {
            Color baseColor = roomColors != null && roomColors.Length > 0
                ? roomColors[Mathf.Abs(roomId) % roomColors.Length]
                : new Color(0.12f, 0.78f, 0.92f, 1f);
            Color c = Color.Lerp(baseColor, Color.white, 0.42f);
            c.a = 0.16f;
            return c;
        }

        Color RegionVeilColor()
        {
            Color[] walls =
            {
                new Color(0.07f, 0.34f, 0.67f, 1f),
                new Color(0.07f, 0.25f, 0.65f, 1f),
                new Color(0.22f, 0.11f, 0.58f, 1f),
                new Color(0.39f, 0.09f, 0.47f, 1f),
                new Color(0.48f, 0.08f, 0.28f, 1f),
            };
            return Color.Lerp(walls[Mathf.Clamp(chapter, 0, walls.Length - 1)], Color.white, 0.10f);
        }

        static Color SampleWallColor(Transform roomRoot, Color fallback)
        {
            // BoardRenderer may adjust its architectural palette independently of the serialized
            // level theme. Sampling an actual interior wall guarantees the veil fuses into that
            // mass instead of producing a slightly different rectangular patch.
            Transform[] descendants = roomRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (descendants[i].name != "Wall") continue;
                SpriteRenderer[] renderers = descendants[i].GetComponentsInChildren<SpriteRenderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                    if (renderers[r] != null && renderers[r].transform.name == "Sprite")
                        return renderers[r].color;
            }
            return fallback;
        }

        static Vector3 Cell(PRoom room, Vector2Int cell)
        {
            return new Vector3(cell.x - (room.width - 1) * 0.5f,
                cell.y - (room.height - 1) * 0.5f, 0f);
        }

        static RectInt Clip(RectInt area, int width, int height)
        {
            int xMin = Mathf.Clamp(area.xMin, 0, width);
            int yMin = Mathf.Clamp(area.yMin, 0, height);
            int xMax = Mathf.Clamp(area.xMax, xMin, width);
            int yMax = Mathf.Clamp(area.yMax, yMin, height);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        static RectInt KeepPerimeter(RectInt area, int width, int height)
        {
            int xMin = area.xMin == 0 ? 1 : area.xMin;
            int yMin = area.yMin == 0 ? 1 : area.yMin;
            int xMax = area.xMax == width ? width - 1 : area.xMax;
            int yMax = area.yMax == height ? height - 1 : area.yMax;
            return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
        }

        // Four restrained discoveries live before the recursion chapter. Chapter V deliberately
        // keeps every nested chamber visible from the start so players can plan across depths;
        // concealing a required child room would turn the new puzzles into guesswork.
        static IEnumerable<Spec> SpecsFor(int levelIndex)
        {
            switch (levelIndex)
            {
                case 11: // Level 12 — first small discovery
                    yield return Spec.Nested(1, RevealTrigger.EnterRoom);
                    break;
                case 22: // Level 23 — a narrow far-side panel
                    yield return Spec.Region(0, new RectInt(6, 0, 2, 7), 2);
                    break;
                case 28: // Level 29 — a second, slightly tighter discovery
                    yield return Spec.Region(0, new RectInt(5, 0, 2, 7), 2);
                    break;
                case 33: // Level 34 — reveal the deeper of two recursive spaces
                    yield return Spec.Nested(2, RevealTrigger.EnterRoom);
                    break;
            }
        }
    }
}
