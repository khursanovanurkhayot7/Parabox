using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    /// <summary>
    /// Keeps recursive boxes readable without covering the puzzle with labels or direction icons.
    /// The authored shell/doorway renderer remains the source of truth: an orange uninterrupted
    /// edge is a wall and a cyan cut in that edge is a doorway. This component adds only a small,
    /// pulsing red status lamp to a side that cannot currently be crossed.
    ///
    /// Presentation only: it never changes room data, collision, moves, score or difficulty.
    /// </summary>
    public class NestedBoxGuidanceFx : MonoBehaviour
    {
        sealed class ClosedSideSignal
        {
            public Transform root;
            public SpriteRenderer glow;
            public SpriteRenderer housing;
            public SpriteRenderer glass;
            public SpriteRenderer lamp;
            public SpriteRenderer highlight;
            public float phase;
        }

        static readonly Vector2Int[] InwardDirections =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
        };

        static readonly Color HousingShadow = new Color(0.015f, 0.025f, 0.050f, 0.78f);
        static readonly Color Housing = Hex("101A2A");
        static readonly Color HousingEdge = Hex("3A5068");
        static readonly Color Recess = Hex("210B14");
        static readonly Color GlassLow = Hex("941426");
        static readonly Color GlassHigh = Hex("C82036");
        static readonly Color LampLow = Hex("D9273C");
        static readonly Color LampHigh = Hex("FF5868");
        static readonly Color Highlight = Hex("FFD7DA");
        static readonly Color EnvironmentGlint = new Color(0.24f, 0.78f, 1f, 0.34f);

        const int GlowOrder = 104;
        const int ShadowOrder = 105;
        const int HousingOrder = 106;
        const int BevelOrder = 107;
        const int RecessOrder = 108;
        const int GlassOrder = 109;
        const int LampOrder = 110;
        const int HighlightOrder = 111;

        LevelModel model;
        Dictionary<PEntity, EntityView> views;
        Sprite cellSprite;
        Sprite glowSprite;
        readonly Dictionary<PEntity, ClosedSideSignal[]> signals =
            new Dictionary<PEntity, ClosedSideSignal[]>();
        readonly List<ClosedSideSignal> allSignals = new List<ClosedSideSignal>();
        int lastStateSignature = int.MinValue;
        bool configured;

        public void Configure(LevelModel levelModel, Dictionary<int, Transform> renderedRooms,
                              Dictionary<PEntity, EntityView> renderedViews,
                              Sprite guideCellSprite, Sprite guideGlowSprite)
        {
            ClearSignals();
            model = levelModel;
            views = renderedViews;
            cellSprite = guideCellSprite;
            glowSprite = guideGlowSprite;

            if (model == null || views == null || cellSprite == null)
            {
                configured = false;
                return;
            }

            foreach (PEntity box in model.entities)
            {
                if (box == null || box.interiorRoomId < 0
                    || !model.rooms.ContainsKey(box.interiorRoomId)
                    || !views.TryGetValue(box, out EntityView view) || view == null)
                    continue;

                var boxSignals = new ClosedSideSignal[InwardDirections.Length];
                for (int i = 0; i < InwardDirections.Length; i++)
                    boxSignals[i] = CreateClosedSignal(view.transform, InwardDirections[i],
                        allSignals.Count * 0.73f);
                signals[box] = boxSignals;
            }

            configured = signals.Count > 0;
            lastStateSignature = int.MinValue;
            if (configured) RefreshSignals();
        }

        void Update()
        {
            if (!configured || model == null) return;
            int signature = StateSignature();
            if (signature != lastStateSignature)
            {
                lastStateSignature = signature;
                RefreshSignals();
            }
            AnimateSignals();
        }

        void RefreshSignals()
        {
            foreach (var pair in signals)
            {
                PEntity box = pair.Key;
                ClosedSideSignal[] boxSignals = pair.Value;
                for (int i = 0; i < InwardDirections.Length; i++)
                {
                    ClosedSideSignal signal = boxSignals[i];
                    if (signal == null || signal.root == null) continue;

                    // A clean cyan doorway is already drawn by BoardRenderer. Do not add another
                    // icon on open sides; show the red hardware only on a real closed wall.
                    bool closed = !SideHasUsableDoorway(box, InwardDirections[i]);
                    signal.root.gameObject.SetActive(closed);
                }
            }
        }

        bool SideHasUsableDoorway(PEntity box, Vector2Int inward)
        {
            if (box == null || box.sunk
                || !model.rooms.TryGetValue(box.roomId, out PRoom parent)
                || !model.rooms.TryGetValue(box.interiorRoomId, out PRoom inner))
                return false;

            // The parent approach cell and at least one matching inner boundary cell must both
            // accept the same movement. Checking the complete boundary avoids the old centre-cell
            // assumption, which could mark an off-centre doorway as closed.
            Vector2Int approachCell = box.pos - inward;
            if (PlayerTerrainBlocks(parent, approachCell, inward)) return false;

            if (inward == Vector2Int.right || inward == Vector2Int.left)
            {
                int x = inward == Vector2Int.right ? 0 : inner.width - 1;
                for (int y = 0; y < inner.height; y++)
                    if (!PlayerTerrainBlocks(inner, new Vector2Int(x, y), inward)) return true;
                return false;
            }

            int boundaryY = inward == Vector2Int.up ? 0 : inner.height - 1;
            for (int x = 0; x < inner.width; x++)
                if (!PlayerTerrainBlocks(inner, new Vector2Int(x, boundaryY), inward)) return true;
            return false;
        }

        bool PlayerTerrainBlocks(PRoom room, Vector2Int cell, Vector2Int travelDirection)
        {
            if (room == null || !room.InBounds(cell) || room.IsWall(cell)
                || room.IsBroken(cell) || room.IsOpenTrench(cell)
                || room.IsRock(cell) || room.IsGap(cell))
                return true;

            Vector2Int oneWay = room.OneWay(cell);
            if (oneWay != Vector2Int.zero && oneWay != travelDirection) return true;
            if (room.gate != null && room.gate[cell.x, cell.y] && !model.GatesOpen()) return true;
            if (room.heavyGate != null && room.heavyGate[cell.x, cell.y]
                && !model.HeavyGatesOpen()) return true;
            if (room.IsLocked(cell) && !model.LocksOpen()) return true;
            if (room.IsLatch(cell) && !model.latched) return true;
            if (room.IsPulse(cell) && model.beat == 0) return true;
            return false;
        }

        ClosedSideSignal CreateClosedSignal(Transform boxView, Vector2Int inward, float phase)
        {
            var root = new GameObject("NestedClosedSignal_" + SideName(inward)).transform;
            root.SetParent(boxView, false);
            root.localPosition = SidePosition(inward);

            bool horizontalWall = inward.y != 0;
            // Option 1: a slim recessed beacon. Its overall footprint remains matched to the
            // original box-edge marker; depth comes from shallow nested layers rather than a
            // thick frame, so it stays premium and readable at the tutorial's small scale.
            Vector2 shadowSize = OrientedSize(horizontalWall, 0.156f, 0.084f);
            Vector2 housingSize = OrientedSize(horizontalWall, 0.150f, 0.078f);
            Vector2 bevelSize = OrientedSize(horizontalWall, 0.134f, 0.064f);
            Vector2 recessSize = OrientedSize(horizontalWall, 0.119f, 0.052f);
            Vector2 glassSize = OrientedSize(horizontalWall, 0.108f, 0.042f);
            Vector2 lampSize = OrientedSize(horizontalWall, 0.084f, 0.030f);
            Vector2 highlightSize = OrientedSize(horizontalWall, 0.050f, 0.005f);
            Vector2 glowSize = OrientedSize(horizontalWall, 0.210f, 0.130f);
            Vector2 glintSize = OrientedSize(horizontalWall, 0.066f, 0.0035f);
            Vector2 shadowOffset = new Vector2(0.006f, -0.007f);

            var signal = new ClosedSideSignal { root = root, phase = phase };
            if (glowSprite != null)
                signal.glow = AddSprite(root, "ClosedSignal_Glow", glowSprite,
                    new Color(LampHigh.r, LampHigh.g, LampHigh.b, 0.12f), GlowOrder,
                    Vector2.zero, glowSize);

            AddSprite(root, "ClosedSignal_Shadow", cellSprite, HousingShadow,
                ShadowOrder, shadowOffset, shadowSize);
            signal.housing = AddSprite(root, "ClosedSignal_Housing", cellSprite, Housing,
                HousingOrder, Vector2.zero, housingSize);
            AddSprite(root, "ClosedSignal_Rim", cellSprite, HousingEdge,
                BevelOrder, Vector2.zero, bevelSize);
            AddSprite(root, "ClosedSignal_Recess", cellSprite, Recess,
                RecessOrder, Vector2.zero, recessSize);
            signal.glass = AddSprite(root, "ClosedSignal_Glass", cellSprite, GlassLow,
                GlassOrder, Vector2.zero, glassSize);

            signal.lamp = AddSprite(root, "ClosedSignal_Lamp", cellSprite, LampLow,
                LampOrder, Vector2.zero, lampSize);
            Vector2 highlightOffset = horizontalWall
                ? new Vector2(-0.010f, 0.009f)
                : new Vector2(-0.009f, 0.010f);
            signal.highlight = AddSprite(root, "ClosedSignal_Highlight", cellSprite, Highlight,
                HighlightOrder, highlightOffset, highlightSize);
            Vector2 glintOffset = horizontalWall
                ? new Vector2(-0.018f, 0.033f)
                : new Vector2(-0.033f, 0.018f);
            AddSprite(root, "ClosedSignal_EnvironmentGlint", cellSprite, EnvironmentGlint,
                HighlightOrder, glintOffset, glintSize);

            allSignals.Add(signal);
            return signal;
        }

        void AnimateSignals()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < allSignals.Count; i++)
            {
                ClosedSideSignal signal = allSignals[i];
                if (signal.root == null || !signal.root.gameObject.activeSelf) continue;

                // Keep motion restrained: the layered glass supplies the 3D readability, while a
                // slow low-amplitude pulse keeps the closed-wall state noticeable without flashing.
                float wave = Mathf.Sin(now * 2.8f + signal.phase) * 0.5f + 0.5f;
                float pulse = Mathf.SmoothStep(0f, 1f, wave);
                signal.root.localScale = Vector3.one * Mathf.Lerp(0.99f, 1.025f, pulse);

                if (signal.glass != null)
                    signal.glass.color = Color.Lerp(GlassLow, GlassHigh, pulse);
                if (signal.lamp != null)
                    signal.lamp.color = Color.Lerp(LampLow, LampHigh, pulse);
                if (signal.highlight != null)
                {
                    Color highlight = Highlight;
                    highlight.a = Mathf.Lerp(0.46f, 0.82f, pulse);
                    signal.highlight.color = highlight;
                }
                if (signal.glow != null)
                {
                    Color glow = signal.glow.color;
                    glow.a = Mathf.Lerp(0.08f, 0.22f, pulse);
                    signal.glow.color = glow;
                }
            }
        }

        void ClearSignals()
        {
            for (int i = 0; i < allSignals.Count; i++)
            {
                ClosedSideSignal signal = allSignals[i];
                if (signal.root == null) continue;
                signal.root.gameObject.SetActive(false);
                Destroy(signal.root.gameObject);
            }
            allSignals.Clear();
            signals.Clear();
            configured = false;
        }

        int StateSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + model.MoveCount;
                hash = hash * 31 + model.beat;
                hash = hash * 31 + (model.latched ? 1 : 0);
                hash = hash * 31 + (model.GatesOpen() ? 1 : 0);
                hash = hash * 31 + (model.HeavyGatesOpen() ? 1 : 0);
                hash = hash * 31 + (model.LocksOpen() ? 1 : 0);
                foreach (PEntity box in model.entities)
                {
                    if (box.interiorRoomId < 0) continue;
                    hash = hash * 31 + box.roomId;
                    hash = hash * 31 + box.pos.x;
                    hash = hash * 31 + box.pos.y;
                    hash = hash * 31 + (box.sunk ? 1 : 0);
                }
                return hash;
            }
        }

        static Vector3 SidePosition(Vector2Int inward)
            => new Vector3(-inward.x * 0.43f, -inward.y * 0.43f, 0f);

        static Vector2 OrientedSize(bool horizontal, float longAxis, float shortAxis)
            => horizontal ? new Vector2(longAxis, shortAxis) : new Vector2(shortAxis, longAxis);

        static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Color color,
                                        int sortingOrder, Vector2 position, Vector2 scale)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = new Vector3(position.x, position.y, 0f);
            item.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        static string SideName(Vector2Int inward)
        {
            if (inward == Vector2Int.up) return "Bottom";
            if (inward == Vector2Int.right) return "Left";
            if (inward == Vector2Int.down) return "Top";
            return "Right";
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
