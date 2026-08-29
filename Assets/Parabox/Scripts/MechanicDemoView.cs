using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // A small, self-contained animated rule card used by every first-appearance tutorial.
    // It deliberately contains no LevelModel and cannot read a level solution: the player sees
    // one cause-and-effect example, while the real puzzle remains untouched behind the scrim.
    public sealed class MechanicDemoView : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform player;
        public RectTransform cargo;
        public RectTransform second;
        public RectTransform mechanicTile;
        public RectTransform gate;
        public RectTransform goal;
        public RectTransform outerRoom;
        public RectTransform innerRoom;
        public RectTransform boardSurface;
        public RectTransform[] floorCells;
        public RectTransform[] wallCells;
        public Text mechanicGlyph;
        public Text gateGlyph;
        public Text cargoGlyph;
        public Text secondGlyph;
        public Text nameText;
        public Text ruleText;
        public Image playerImage;
        public Image cargoImage;
        public Image secondImage;
        public Image mechanicImage;
        public Image gateImage;
        public Image goalImage;

        static readonly Color Navy = new Color(0.018f, 0.055f, 0.145f, 1f);
        static readonly Color Cell = new Color(0.035f, 0.145f, 0.34f, 0.78f);
        static readonly Color Cyan = new Color(0.09f, 0.76f, 1f, 1f);
        static readonly Color Violet = new Color(0.51f, 0.19f, 0.95f, 1f);
        static readonly Color Pink = new Color(0.975f, 0.02f, 0.405f, 1f);
        static readonly Color Orange = new Color(1f, 0.59f, 0.075f, 1f);
        static readonly Color Coral = new Color(1f, 0.42f, 0.34f, 1f);
        static readonly Color Sky = new Color(0.46f, 0.82f, 1f, 1f);

        const int GridWidth = 9;
        const int GridHeight = 5;
        const float CellStep = 74f;

        // Release builds must use the editor-authored mini-board. Keeping this test public lets the
        // build guard reject an old Game.unity instead of silently falling back to the abstract
        // seven-square diagram that used to ship here.
        public bool IsGameplayStylePrebuilt => boardSurface != null
            && floorCells != null && floorCells.Length == GridWidth * GridHeight
            && wallCells != null && wallCells.Length == GridWidth * GridHeight;

        void Awake()
        {
            // Existing serialized tutorial boards are upgraded at runtime too, so a player never
            // needs a regenerated scene just to see the closed-side language.
            EnsureNestedRoomClosedLights();
            HideImmediate();
        }

        public void HideImmediate()
        {
            if (group != null)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        public IEnumerator Play(MechanicCatalog.Id id)
        {
            ResetDemo(id);
            if (group != null) group.alpha = 1f;
            yield return Wait(0.35f);
            yield return PlayPuzzleApproach(id);

            switch (id)
            {
                case MechanicCatalog.Id.Navigation:
                    yield return Move(player, P(-2), P(2), 1.35f);
                    yield return Pulse(goal, 1.18f, 0.38f);
                    break;

                case MechanicCatalog.Id.Crate:
                    Show(cargo, P(-1)); Show(goal, P(1));
                    yield return MovePair(player, P(-2), P(0), cargo, P(-1), P(1), 1.45f);
                    yield return Pulse(goal, 1.18f, 0.38f);
                    break;

                case MechanicCatalog.Id.OneWay:
                    Tile("ARROW  >", Cyan, 0);
                    yield return Move(player, P(-2), P(2), 1.25f);
                    break;

                case MechanicCatalog.Id.Current:
                    Tile("CURRENT  >>", Cyan, 0);
                    yield return Move(player, P(-2), P(0), 0.75f);
                    yield return Move(player, P(0), P(2), 0.55f);
                    break;

                case MechanicCatalog.Id.Geyser:
                    Tile("GEYSER  ^", Sky, 0);
                    yield return Move(player, P(-2), P(0), 0.72f);
                    yield return Jump(player, P(0), P(2), 0.8f);
                    break;

                case MechanicCatalog.Id.Trench:
                    Tile("TRENCH", new Color(0.01f, 0.02f, 0.055f, 1f), 0);
                    Show(cargo, P(-2));
                    yield return Move(cargo, P(-2), P(0), 0.8f);
                    mechanicImage.color = Cyan;
                    mechanicGlyph.text = "BRIDGE";
                    Show(player, P(-2));
                    yield return Move(player, P(-2), P(2), 1.1f);
                    break;

                case MechanicCatalog.Id.NarrowGap:
                    Barrier("SMALL GAP", Violet, 0);
                    Show(cargo, P(-2));
                    yield return Move(cargo, P(-2), P(2), 0.95f);
                    Show(player, P(-2));
                    yield return Bump(player, P(-2), P(-0.7f));
                    break;

                case MechanicCatalog.Id.BreakableRock:
                    Barrier("ROCK", Coral, 0); Show(cargo, P(-2));
                    yield return Move(cargo, P(-2), P(-0.15f), 0.8f);
                    yield return Fade(gate, 1f, 0f, 0.28f);
                    yield return Move(cargo, P(-0.15f), P(1), 0.45f);
                    break;

                case MechanicCatalog.Id.ButtonGate:
                case MechanicCatalog.Id.HeavyPlateGate:
                    Tile(id == MechanicCatalog.Id.HeavyPlateGate ? "HEAVY PLATE" : "BUTTON", Cyan, -1);
                    Barrier("GATE", Violet, 1); Show(cargo, P(-2)); Show(player, P(0));
                    yield return Move(cargo, P(-2), P(-1), 0.7f);
                    yield return Fade(gate, 1f, 0.1f, 0.35f);
                    yield return Move(player, P(0), P(2), 0.9f);
                    break;

                case MechanicCatalog.Id.SlidingCargo:
                    Tile("ICE", Sky, 0); Show(cargo, P(-2)); Show(player, P(-3));
                    yield return MovePair(player, P(-3), P(-2), cargo, P(-2), P(-1), 0.38f);
                    yield return Move(cargo, P(-1), P(2), 0.62f);
                    break;

                case MechanicCatalog.Id.NestedBoard:
                    yield return EnterRoom(false, false);
                    break;

                case MechanicCatalog.Id.Ice:
                    Tile("ICE", Sky, 0);
                    yield return Move(player, P(-2), P(-1), 0.35f);
                    yield return Move(player, P(-1), P(2), 0.72f);
                    break;

                case MechanicCatalog.Id.ToggleLatch:
                    Tile("TOGGLE", Cyan, -1); Barrier("LATCH", Violet, 1);
                    yield return Move(player, P(-2), P(-1), 0.62f);
                    mechanicImage.color = Violet;
                    yield return Fade(gate, 1f, 0.08f, 0.34f);
                    yield return Move(player, P(-1), P(2), 0.85f);
                    break;

                case MechanicCatalog.Id.Pulse:
                    Barrier("PULSE", Violet, 0);
                    yield return Fade(gate, 1f, 0.12f, 0.35f);
                    yield return Fade(gate, 0.12f, 1f, 0.35f);
                    yield return Fade(gate, 1f, 0.12f, 0.35f);
                    yield return Move(player, P(-2), P(2), 0.9f);
                    break;

                case MechanicCatalog.Id.Boulder:
                    Show(cargo, P(0)); cargoGlyph.text = "HEAVY"; Show(player, P(-3));
                    yield return Move(player, P(-3), P(-2), 0.32f);
                    yield return Move(player, P(-2), P(-1), 0.32f);
                    yield return MovePair(player, P(-1), P(0), cargo, P(0), P(1), 0.48f);
                    break;

                case MechanicCatalog.Id.DeepWater:
                    Tile("DEEP WATER", new Color(0.02f, 0.28f, 0.52f, 1f), 0);
                    yield return Move(player, P(-2), P(2), 1f);
                    Show(cargo, P(-2));
                    yield return Move(cargo, P(-2), P(0), 0.7f);
                    yield return Sink(cargo, 0.55f);
                    break;

                case MechanicCatalog.Id.GravityWell:
                    Tile("GRAVITY", Violet, 0); Show(cargo, P(0, 1));
                    yield return Move(player, P(-2), P(-1), 0.55f);
                    yield return Move(cargo, P(0, 1), P(0, -1), 0.75f);
                    break;

                case MechanicCatalog.Id.Updraft:
                    Tile("UPDRAFT  ^", Cyan, 0); Show(cargo, P(0, -1));
                    yield return Move(cargo, P(0, -1), P(0, 1), 0.75f);
                    break;

                case MechanicCatalog.Id.StickyFloor:
                    Tile("STICKY", Coral, 0);
                    yield return Move(player, P(-2), P(0), 0.75f);
                    yield return Move(player, P(0), P(1), 0.45f);
                    break;

                case MechanicCatalog.Id.Cage:
                    Barrier("CAGE", Violet, 0); Show(cargo, P(-2));
                    yield return Move(cargo, P(-2), P(0), 0.8f);
                    cargoGlyph.text = "LOCKED";
                    yield return Bump(cargo, P(0), P(-0.45f));
                    break;

                case MechanicCatalog.Id.KeyLock:
                    Tile("PEARL", Sky, -1); Barrier("LOCK", Orange, 1);
                    yield return Move(player, P(-2), P(-1), 0.6f);
                    yield return Fade(mechanicTile, 1f, 0f, 0.25f);
                    yield return Fade(gate, 1f, 0.08f, 0.35f);
                    yield return Move(player, P(-1), P(2), 0.85f);
                    break;

                case MechanicCatalog.Id.Mirror:
                    Show(second, P(2, -0.7f)); secondImage.color = Sky; secondGlyph.text = "EYES";
                    player.anchoredPosition = P(-2, 0.7f);
                    yield return MovePair(player, P(-2, 0.7f), P(-1, 0.7f),
                        second, P(2, -0.7f), P(1, -0.7f), 0.75f);
                    break;

                case MechanicCatalog.Id.Kelp:
                    Barrier("KELP", new Color(0.2f, 0.8f, 0.45f, 1f), 0);
                    yield return Move(player, P(-2), P(2), 0.95f);
                    Show(cargo, P(-2));
                    yield return Bump(cargo, P(-2), P(-0.7f));
                    break;

                case MechanicCatalog.Id.CrackedFloor:
                    Tile("CRACKED", Coral, 0);
                    yield return Move(player, P(-2), P(1), 0.95f);
                    yield return Fade(mechanicTile, 1f, 0f, 0.32f);
                    break;

                case MechanicCatalog.Id.Magnet:
                    Tile("MAGNET", Violet, 1); Show(cargo, P(-1, 1));
                    yield return Move(player, P(-2, -1), P(-1, -1), 0.55f);
                    yield return Move(cargo, P(-1, 1), P(1, 1), 0.72f);
                    break;

                case MechanicCatalog.Id.Echo:
                    Show(second, P(-2, -0.8f)); secondImage.color = Sky; secondGlyph.text = "ECHO";
                    player.anchoredPosition = P(-2, 0.8f);
                    yield return MovePair(player, P(-2, 0.8f), P(1, 0.8f),
                        second, P(-2, -0.8f), P(1, -0.8f), 1.05f);
                    break;

                case MechanicCatalog.Id.Sand:
                    Tile("SAND", Orange, -1); Show(cargo, P(0)); player.anchoredPosition = P(-1);
                    yield return BumpPair(player, P(-1), P(-0.65f), cargo, P(0), P(0.35f));
                    break;

                case MechanicCatalog.Id.LockingCargo:
                    Show(cargo, P(-1)); Show(goal, P(1));
                    yield return Move(cargo, P(-1), P(1), 0.9f);
                    cargoGlyph.text = "LOCKED";
                    yield return Pulse(cargo, 1.14f, 0.35f);
                    break;

                case MechanicCatalog.Id.ColourCargo:
                    cargoImage.color = Coral; Show(cargo, P(-1));
                    goalImage.color = new Color(Coral.r, Coral.g, Coral.b, 0.42f); Show(goal, P(1));
                    yield return Move(cargo, P(-1), P(1), 0.9f);
                    yield return Pulse(goal, 1.16f, 0.35f);
                    break;

                case MechanicCatalog.Id.Portal:
                    Tile("PORTAL A", Violet, -1); Show(second, P(2)); secondImage.color = Violet;
                    secondGlyph.text = "PORTAL B";
                    yield return Move(player, P(-2), P(-1), 0.58f);
                    yield return Fade(player, 1f, 0f, 0.18f);
                    player.anchoredPosition = P(2);
                    yield return Fade(player, 0f, 1f, 0.22f);
                    break;

                case MechanicCatalog.Id.Deflector:
                    Tile("TURN  >", Cyan, 0); player.anchoredPosition = P(0, -1);
                    yield return Move(player, P(0, -1), P(0), 0.55f);
                    yield return Move(player, P(0), P(2), 0.7f);
                    break;

                case MechanicCatalog.Id.Swap:
                    Tile("SWAP A", Violet, -1); Show(second, P(1)); secondImage.color = Sky;
                    secondGlyph.text = "SWAP B"; Show(cargo, P(-1));
                    yield return SwapPieces(cargo, second, 0.85f);
                    break;

                case MechanicCatalog.Id.MultiStageRecursion:
                    yield return EnterRoom(true, false);
                    break;

                case MechanicCatalog.Id.ChamberChain:
                    yield return EnterRoom(true, true);
                    break;

                case MechanicCatalog.Id.PlayerBodies:
                    Show(second, P(0)); secondImage.color = Pink; secondGlyph.text = "EYES";
                    Show(goal, P(2));
                    yield return MovePair(player, P(-2), P(0), second, P(0), P(2), 1.15f);
                    yield return Pulse(goal, 1.16f, 0.35f);
                    break;
            }

            yield return Wait(0.75f);
        }

        void ResetDemo(MechanicCatalog.Id id)
        {
            ConfigureMiniBoard(id);
            if (nameText != null) nameText.text = "NEW MECHANIC  •  " + MechanicCatalog.DisplayName(id);
            if (ruleText != null) ruleText.text = MechanicCatalog.Lesson(id);

            ResetPiece(player, playerImage, P(-2), Pink, Vector3.one);
            ResetPiece(cargo, cargoImage, P(-1), Orange, Vector3.one);
            ResetPiece(second, secondImage, P(1), Sky, Vector3.one);
            ResetPiece(mechanicTile, mechanicImage, P(0), Cyan, Vector3.one);
            ResetPiece(gate, gateImage, P(0), Violet, Vector3.one);
            ResetPiece(goal, goalImage, P(2), new Color(Cyan.r, Cyan.g, Cyan.b, 0.36f), Vector3.one);
            ResetPiece(outerRoom, null, P(0), Violet, Vector3.one);
            ResetPiece(innerRoom, null, P(0), Cyan, Vector3.one);

            if (cargoGlyph != null) cargoGlyph.text = "";
            if (secondGlyph != null) secondGlyph.text = "";
            if (mechanicGlyph != null) mechanicGlyph.text = "";
            if (gateGlyph != null) gateGlyph.text = "";

            Hide(cargo); Hide(second); Hide(mechanicTile); Hide(gate); Hide(goal);
            Hide(outerRoom); Hide(innerRoom);
            Show(player, P(-2));
        }

        // Every lesson uses a small authored board silhouette. These are presentation layouts, not
        // campaign puzzles: they contain no ParaboxLevel, cannot award progress, and never read the
        // current level or its solution. The asymmetric wall banks turn the old straight diagram
        // into a readable mini-puzzle while leaving the mechanic's demonstration lane unobstructed.
        void ConfigureMiniBoard(MechanicCatalog.Id id)
        {
            if (!IsGameplayStylePrebuilt) return;

            for (int i = 0; i < floorCells.Length; i++)
            {
                RectTransform cell = floorCells[i];
                if (cell == null) continue;
                cell.gameObject.SetActive(true);
                Image image = cell.GetComponent<Image>();
                if (image != null)
                {
                    int x = i % GridWidth;
                    int y = i / GridWidth;
                    float lift = ((x + y) & 1) == 0 ? 0.012f : 0f;
                    image.color = new Color(Cell.r + lift, Cell.g + lift, Cell.b + lift, 0.34f);
                }
            }

            for (int i = 0; i < wallCells.Length; i++)
                if (wallCells[i] != null) wallCells[i].gameObject.SetActive(false);

            // A continuous five-by-nine room boundary. The centre three rows are the playable
            // miniature; selected interior braces give each mechanic family its own readable room.
            for (int x = -4; x <= 4; x++)
            {
                SetWall(x, -2);
                SetWall(x, 2);
            }
            for (int y = -1; y <= 1; y++)
            {
                SetWall(-4, y);
                SetWall(4, y);
            }

            bool recursive = id == MechanicCatalog.Id.NestedBoard
                || id == MechanicCatalog.Id.MultiStageRecursion
                || id == MechanicCatalog.Id.ChamberChain;
            if (recursive)
            {
                // A staggered chamber mouth: the diver turns into the room instead of travelling
                // down the same horizontal strip used by ordinary mechanic cards.
                SetWall(-3, 0); SetWall(-3, 1); SetWall(-2, 1);
                SetWall(2, -1); SetWall(3, -1);
                return;
            }

            if (id == MechanicCatalog.Id.Mirror || id == MechanicCatalog.Id.Echo)
            {
                // Two clear parallel lanes make opposite/copy movement readable at a glance.
                SetWall(-3, 0); SetWall(0, 0); SetWall(3, 0);
                SetWall(0, 1); SetWall(0, -1);
                return;
            }

            if (id == MechanicCatalog.Id.Boulder)
            {
                // Keep the three-cell run-up visible while closing the tempting upper shortcut.
                SetWall(-3, 1); SetWall(-2, 1); SetWall(-1, 1);
                SetWall(2, -1); SetWall(3, -1);
                return;
            }

            // Shared S-bend used to approach the mechanic. Each family receives one additional
            // brace, so its example board and route silhouette do not look copied from the last.
            SetWall(-3, 0); SetWall(-3, 1); SetWall(-2, 1);
            SetWall(2, -1); SetWall(3, -1);
            switch (((int)id) % 5)
            {
                case 0: SetWall(3, 1); break;
                case 1: SetWall(-1, -1); break;
                case 2: SetWall(1, 1); break;
                case 3: SetWall(3, 0); break;
                default: SetWall(1, -1); break;
            }
        }

        // Most demonstrations begin with two genuine grid moves around the left wall bank. The
        // mechanic then resolves from the familiar (-2,0) staging cell used by the authored action
        // below. Mechanics whose rule itself depends on a special two-lane or run-up formation keep
        // their bespoke opening and never snap through this shared route.
        IEnumerator PlayPuzzleApproach(MechanicCatalog.Id id)
        {
            if (id == MechanicCatalog.Id.Mirror
                || id == MechanicCatalog.Id.Echo
                || id == MechanicCatalog.Id.Magnet
                || id == MechanicCatalog.Id.Sand
                || id == MechanicCatalog.Id.Boulder
                || id == MechanicCatalog.Id.Deflector)
                yield break;

            Vector2 start = P(-3, -1);
            Vector2 corner = P(-2, -1);
            Vector2 staging = P(-2, 0);
            player.anchoredPosition = start;
            yield return Move(player, start, corner, 0.26f);
            yield return Move(player, corner, staging, 0.26f);
        }

        void SetWall(int x, int y)
        {
            int ix = x + GridWidth / 2;
            int iy = y + GridHeight / 2;
            if (ix < 0 || ix >= GridWidth || iy < 0 || iy >= GridHeight) return;
            int index = iy * GridWidth + ix;
            if (wallCells[index] != null) wallCells[index].gameObject.SetActive(true);
        }

        IEnumerator EnterRoom(bool cargoCrosses, bool chain)
        {
            Show(outerRoom, P(0));
            outerRoom.localScale = Vector3.one * 0.94f;
            if (chain)
            {
                innerRoom.gameObject.SetActive(true);
                innerRoom.anchoredPosition = new Vector2(10f, -8f);
                innerRoom.localScale = Vector3.one * 0.48f;
            }
            yield return Move(player, P(-2), P(-0.92f), 0.62f);
            yield return Move(player, P(-0.92f), P(0), 0.34f);
            yield return Scale(player, 1f, 0.30f, 0.28f);
            yield return Scale(outerRoom, 0.94f, chain ? 2.45f : 2.25f, 0.52f);
            player.anchoredPosition = chain ? P(0.25f, 0.28f) : P(0.22f, 0.15f);
            yield return Scale(player, 0.30f, chain ? 0.46f : 0.58f, 0.26f);
            if (cargoCrosses)
            {
                Show(cargo, chain ? P(-0.35f, -0.28f) : P(-0.30f, -0.18f));
                cargo.localScale = Vector3.one * (chain ? 0.38f : 0.50f);
                yield return Move(cargo, cargo.anchoredPosition, P(1.55f), 0.9f);
                yield return Scale(cargo, cargo.localScale.x, 1f, 0.45f);
            }
            else
            {
                yield return Wait(0.48f);
                yield return Scale(player, player.localScale.x, 1f, 0.42f);
                yield return Move(player, player.anchoredPosition, P(1.6f), 0.55f);
            }
        }

        void Tile(string label, Color color, float x)
        {
            Show(mechanicTile, P(x));
            mechanicImage.color = color;
            mechanicGlyph.text = label;
        }

        void Barrier(string label, Color color, float x)
        {
            Show(gate, P(x));
            gateImage.color = color;
            gateGlyph.text = label;
        }

        static Vector2 P(float x, float y = 0f) => new Vector2(x * CellStep, y * CellStep);

        static void ResetPiece(RectTransform rect, Image image, Vector2 position, Color color, Vector3 scale)
        {
            if (rect == null) return;
            rect.gameObject.SetActive(true);
            rect.anchoredPosition = position;
            rect.localScale = scale;
            SetAlpha(rect, 1f);
            if (image != null) image.color = color;
        }

        static void Show(RectTransform rect, Vector2 position)
        {
            if (rect == null) return;
            rect.gameObject.SetActive(true);
            rect.anchoredPosition = position;
            SetAlpha(rect, 1f);
        }

        static void Hide(RectTransform rect)
        {
            if (rect != null) rect.gameObject.SetActive(false);
        }

        static void SetAlpha(RectTransform rect, float alpha)
        {
            if (rect == null) return;
            CanvasGroup cg = rect.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = alpha;
        }

        static IEnumerator Move(RectTransform rect, Vector2 from, Vector2 to, float duration)
        {
            if (rect == null) yield break;
            rect.anchoredPosition = from;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                rect.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                yield return null;
            }
            rect.anchoredPosition = to;
        }

        static IEnumerator MovePair(RectTransform a, Vector2 a0, Vector2 a1,
                                    RectTransform b, Vector2 b0, Vector2 b1, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                if (a != null) a.anchoredPosition = Vector2.LerpUnclamped(a0, a1, e);
                if (b != null) b.anchoredPosition = Vector2.LerpUnclamped(b0, b1, e);
                yield return null;
            }
            if (a != null) a.anchoredPosition = a1;
            if (b != null) b.anchoredPosition = b1;
        }

        static IEnumerator Bump(RectTransform rect, Vector2 home, Vector2 blocked)
        {
            yield return Move(rect, home, blocked, 0.28f);
            yield return Move(rect, blocked, home, 0.25f);
        }

        static IEnumerator BumpPair(RectTransform a, Vector2 a0, Vector2 a1,
                                    RectTransform b, Vector2 b0, Vector2 b1)
        {
            yield return MovePair(a, a0, a1, b, b0, b1, 0.28f);
            yield return MovePair(a, a1, a0, b, b1, b0, 0.25f);
        }

        static IEnumerator Fade(RectTransform rect, float from, float to, float duration)
        {
            if (rect == null) yield break;
            CanvasGroup cg = rect.GetComponent<CanvasGroup>();
            if (cg == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            cg.alpha = to;
        }

        static IEnumerator Scale(RectTransform rect, float from, float to, float duration)
        {
            if (rect == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration)));
                rect.localScale = Vector3.one * s;
                yield return null;
            }
            rect.localScale = Vector3.one * to;
        }

        static IEnumerator Pulse(RectTransform rect, float scale, float duration)
        {
            yield return Scale(rect, 1f, scale, duration * 0.5f);
            yield return Scale(rect, scale, 1f, duration * 0.5f);
        }

        static IEnumerator Jump(RectTransform rect, Vector2 from, Vector2 to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                Vector2 position = Vector2.Lerp(from, to, p);
                position.y += Mathf.Sin(p * Mathf.PI) * 72f;
                rect.anchoredPosition = position;
                yield return null;
            }
            rect.anchoredPosition = to;
        }

        static IEnumerator Sink(RectTransform rect, float duration)
        {
            Vector2 from = rect.anchoredPosition;
            Vector2 to = from + Vector2.down * 80f;
            CanvasGroup cg = rect.GetComponent<CanvasGroup>();
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                rect.anchoredPosition = Vector2.Lerp(from, to, p);
                if (cg != null) cg.alpha = 1f - p;
                yield return null;
            }
        }

        static IEnumerator SwapPieces(RectTransform a, RectTransform b, float duration)
        {
            Vector2 a0 = a.anchoredPosition, b0 = b.anchoredPosition;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                Vector2 ap = Vector2.Lerp(a0, b0, p);
                Vector2 bp = Vector2.Lerp(b0, a0, p);
                ap.y += Mathf.Sin(p * Mathf.PI) * 42f;
                bp.y -= Mathf.Sin(p * Mathf.PI) * 42f;
                a.anchoredPosition = ap;
                b.anchoredPosition = bp;
                yield return null;
            }
            a.anchoredPosition = b0;
            b.anchoredPosition = a0;
        }

        static IEnumerator Wait(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        void EnsureNestedRoomClosedLights()
        {
            EnsureRoomClosedLights(outerRoom);
            EnsureRoomClosedLights(innerRoom);
        }

        // The room vignette has one open cyan doorway on its right. Put a small premium red lamp
        // on the other three sides so the tutorial demonstrates the exact same closed-side rule as
        // gameplay. These are indicators only; they never participate in puzzle logic.
        static void EnsureRoomClosedLights(RectTransform room)
        {
            if (room == null) return;
            Image roomImage = room.GetComponent<Image>();
            Sprite sprite = roomImage != null ? roomImage.sprite : null;
            float width = Mathf.Max(1f, room.sizeDelta.x);
            float height = Mathf.Max(1f, room.sizeDelta.y);
            float unit = Mathf.Clamp(Mathf.Min(width, height) * 0.18f, 8f, 13f);

            CreateRoomLamp(room, "ClosedLampTop", sprite,
                new Vector2(0f, height * 0.43f), unit);
            CreateRoomLamp(room, "ClosedLampBottom", sprite,
                new Vector2(0f, -height * 0.43f), unit);
            CreateRoomLamp(room, "ClosedLampLeft", sprite,
                new Vector2(-width * 0.43f, 0f), unit);
        }

        static void CreateRoomLamp(RectTransform room, string name, Sprite sprite,
            Vector2 position, float unit)
        {
            Transform existing = room.Find(name);
            RectTransform root;
            if (existing != null && existing is RectTransform existingRect)
                root = existingRect;
            else
            {
                var rootObject = new GameObject(name, typeof(RectTransform));
                rootObject.transform.SetParent(room, false);
                root = (RectTransform)rootObject.transform;
            }

            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = position;
            root.sizeDelta = Vector2.one * (unit * 2.45f);
            root.localScale = Vector3.one;
            root.SetAsLastSibling();

            UIPulse pulse = root.GetComponent<UIPulse>();
            if (pulse == null) pulse = root.gameObject.AddComponent<UIPulse>();
            pulse.amplitude = 0.09f;
            pulse.speed = 3.15f;

            RoomLampImage(root, "Glow", sprite, Vector2.zero, Vector2.one * (unit * 2.35f),
                new Color(1f, 0.10f, 0.17f, 0.34f));
            Image housing = RoomLampImage(root, "Housing", sprite, Vector2.zero,
                Vector2.one * (unit * 1.42f), new Color(0.28f, 0.045f, 0.07f, 1f));
            Outline rim = housing.GetComponent<Outline>();
            if (rim == null) rim = housing.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(0.78f, 0.10f, 0.17f, 1f);
            rim.effectDistance = new Vector2(1f, -1f);
            rim.useGraphicAlpha = true;
            RoomLampImage(root, "Core", sprite, Vector2.zero, Vector2.one * (unit * 0.84f),
                new Color(1f, 0.16f, 0.23f, 1f));
            RoomLampImage(root, "Highlight", sprite,
                new Vector2(-unit * 0.15f, unit * 0.18f), Vector2.one * (unit * 0.25f),
                new Color(1f, 0.88f, 0.90f, 0.96f));
        }

        static Image RoomLampImage(Transform parent, string name, Sprite sprite, Vector2 position,
            Vector2 size, Color color)
        {
            Transform existing = parent.Find(name);
            RectTransform rect;
            if (existing != null && existing is RectTransform existingRect)
                rect = existingRect;
            else
            {
                var imageObject = new GameObject(name, typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                imageObject.transform.SetParent(parent, false);
                rect = (RectTransform)imageObject.transform;
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = rect.GetComponent<Image>();
            if (image == null) image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

#if UNITY_EDITOR
        public static MechanicDemoView Prebuild(RectTransform videoRoot, Font font)
        {
            if (videoRoot == null) return null;
            Transform old = videoRoot.Find("MechanicDemo");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            Sprite softSprite = FindSoftSprite(videoRoot);

            RectTransform root = Rect(videoRoot, "MechanicDemo", Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            Image background = root.gameObject.AddComponent<Image>();
            background.color = Navy;
            background.raycastTarget = false;
            CanvasGroup rootGroup = root.gameObject.AddComponent<CanvasGroup>();

            // This is a real mini-board silhouette, authored and serialized into Game.unity. The
            // runtime only toggles its already-existing walls and moves its already-existing pieces.
            RectTransform shadow = Panel(root, "BoardShadow", new Vector2(0f, -18f),
                new Vector2(828f, 408f), new Color(0f, 0.012f, 0.05f, 0.76f), softSprite);
            AddOutline(shadow, new Color(0f, 0.02f, 0.08f, 0.82f), 8f);

            RectTransform board = Panel(root, "GameplayMiniBoard", new Vector2(0f, -8f),
                new Vector2(816f, 400f), new Color(0.025f, 0.08f, 0.205f, 1f), softSprite);
            AddOutline(board, new Color(Cyan.r, Cyan.g, Cyan.b, 0.92f), 5f);
            RectTransform boardGlow = Panel(board, "NeonInset", Vector2.zero,
                new Vector2(788f, 372f), new Color(0.015f, 0.05f, 0.145f, 1f), softSprite);
            AddOutline(boardGlow, new Color(0.06f, 0.39f, 0.67f, 0.88f), 3f);

            RectTransform surface = Panel(boardGlow, "BoardSurface", Vector2.zero,
                new Vector2(724f, 374f), new Color(0.025f, 0.075f, 0.19f, 1f), softSprite);
            AddOutline(surface, new Color(0.035f, 0.145f, 0.36f, 0.92f), 2f);

            // Small violet corner bolts match the Chapter-V recursive-board cabinet.
            CornerBolt(board, new Vector2(-386f, 178f), 45f, softSprite);
            CornerBolt(board, new Vector2(386f, 178f), -45f, softSprite);
            CornerBolt(board, new Vector2(-386f, -178f), 135f, softSprite);
            CornerBolt(board, new Vector2(386f, -178f), -135f, softSprite);

            var view = root.gameObject.AddComponent<MechanicDemoView>();
            view.group = rootGroup;
            view.boardSurface = surface;
            view.floorCells = new RectTransform[GridWidth * GridHeight];
            view.wallCells = new RectTransform[GridWidth * GridHeight];
            for (int y = -GridHeight / 2; y <= GridHeight / 2; y++)
            for (int x = -GridWidth / 2; x <= GridWidth / 2; x++)
            {
                int index = (y + GridHeight / 2) * GridWidth + (x + GridWidth / 2);
                RectTransform cell = Panel(surface, "Floor_" + index, P(x, y),
                    new Vector2(CellStep + 0.8f, CellStep + 0.8f), Cell, null);
                view.floorCells[index] = cell;
            }

            // Walls are authored in a separate pass so every active wall is above every floor.
            // This removes hairline seams caused by neighbouring floor rectangles interleaving with
            // the continuous boundary silhouette.
            for (int y = -GridHeight / 2; y <= GridHeight / 2; y++)
            for (int x = -GridWidth / 2; x <= GridWidth / 2; x++)
            {
                int index = (y + GridHeight / 2) * GridWidth + (x + GridWidth / 2);
                RectTransform wall = Panel(surface, "Wall_" + index, P(x, y),
                    new Vector2(CellStep + 1.2f, CellStep + 1.2f),
                    new Color(0.030f, 0.045f, 0.125f, 1f), null);
                RectTransform bevel = Panel(wall, "Bevel", new Vector2(0f, CellStep * 0.42f),
                    new Vector2(CellStep + 1.2f, 4f), new Color(0.065f, 0.105f, 0.285f, 0.72f), null);
                bevel.GetComponent<Image>().raycastTarget = false;
                view.wallCells[index] = wall;
            }

            view.nameText = Label(root, "MechanicName", "NEW MECHANIC", font, 30,
                new Vector2(0f, 248f), new Vector2(1060f, 48f), new Color(0.77f, 0.97f, 1f, 1f));
            view.ruleText = Label(root, "Rule", "", font, 23,
                new Vector2(0f, -251f), new Vector2(1050f, 54f), Color.white);

            view.goal = Goal(surface, "Goal", softSprite, out view.goalImage);
            view.mechanicTile = Piece(surface, "MechanicTile", new Vector2(66f, 66f),
                out view.mechanicImage, out view.mechanicGlyph, font, "", softSprite, false);
            view.gate = Piece(surface, "Gate", new Vector2(68f, 74f),
                out view.gateImage, out view.gateGlyph, font, "", softSprite, false);
            view.cargo = Piece(surface, "Cargo", new Vector2(66f, 66f),
                out view.cargoImage, out view.cargoGlyph, font, "", softSprite, true);
            view.second = Piece(surface, "Second", new Vector2(66f, 66f),
                out view.secondImage, out view.secondGlyph, font, "", softSprite, true);
            view.player = Player(surface, font, softSprite, out view.playerImage);

            view.outerRoom = RoomFrame(surface, "OuterRoom", new Vector2(68f, 68f),
                new Color(0.035f, 0.145f, 0.36f, 1f), softSprite);
            view.innerRoom = RoomFrame(view.outerRoom, "InnerRoom", new Vector2(42f, 42f),
                new Color(0.105f, 0.095f, 0.385f, 1f), softSprite);
            view.outerRoom.SetAsLastSibling();
            view.innerRoom.SetAsLastSibling();
            view.mechanicTile.SetAsLastSibling();
            view.gate.SetAsLastSibling();
            view.goal.SetAsLastSibling();
            view.cargo.SetAsLastSibling();
            view.second.SetAsLastSibling();
            view.player.SetAsLastSibling();
            view.HideImmediate();
            return view;
        }

        static Sprite FindSoftSprite(RectTransform source)
        {
            Transform cursor = source;
            while (cursor != null)
            {
                Image image = cursor.GetComponent<Image>();
                if (image != null && image.sprite != null) return image.sprite;
                cursor = cursor.parent;
            }

            // The video itself is a RawImage, so its parent chain may not contain the rounded
            // nine-slice even though the same tutorial canvas already uses it for cards/buttons.
            // Reuse that serialized art asset instead of generating a texture at runtime.
            Canvas canvas = source.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Image[] images = canvas.GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                    if (images[i] != null && images[i].sprite != null
                        && images[i].sprite.border.sqrMagnitude > 0.01f)
                        return images[i].sprite;
                for (int i = 0; i < images.Length; i++)
                    if (images[i] != null && images[i].sprite != null)
                        return images[i].sprite;
            }
            return null;
        }

        static RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size,
                                   Color color, Sprite sprite)
        {
            RectTransform rect = Rect(parent, name, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            return rect;
        }

        static void AddOutline(RectTransform rect, Color color, float distance)
        {
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        static void CornerBolt(RectTransform parent, Vector2 position, float rotation, Sprite sprite)
        {
            RectTransform bolt = Panel(parent, "CornerBolt", position, new Vector2(28f, 28f), Violet, sprite);
            bolt.localEulerAngles = new Vector3(0f, 0f, rotation);
            bolt.SetAsLastSibling();
        }

        static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static RectTransform Piece(Transform parent, string name, Vector2 size,
                                   out Image image, out Text text, Font font, string label,
                                   Sprite sprite, bool cargoStyle)
        {
            RectTransform rect = Rect(parent, name, Vector2.zero, size);
            image = rect.gameObject.AddComponent<Image>();
            image.color = Orange;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            rect.gameObject.AddComponent<CanvasGroup>();
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.02f, 0.08f, 0.9f);
            outline.effectDistance = new Vector2(3f, -3f);

            if (cargoStyle)
            {
                RectTransform inset = Panel(rect, "Inset", Vector2.zero, size * 0.58f,
                    new Color(0.01f, 0.025f, 0.08f, 0.34f), sprite);
                for (int i = -1; i <= 1; i++)
                {
                    RectTransform hatch = Panel(inset, "Hatch", new Vector2(i * 12f, -i * 3f),
                        new Vector2(4f, 38f), new Color(1f, 1f, 1f, 0.30f), null);
                    hatch.localEulerAngles = new Vector3(0f, 0f, -45f);
                }
            }

            text = Label(rect, "Label", label, font, 16, Vector2.zero, size - new Vector2(6f, 6f), Color.white);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = 16;
            return rect;
        }

        static RectTransform Player(Transform parent, Font font, Sprite sprite, out Image image)
        {
            RectTransform rect = Rect(parent, "Player", Vector2.zero, new Vector2(66f, 66f));
            image = rect.gameObject.AddComponent<Image>();
            image.color = Pink;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            rect.gameObject.AddComponent<CanvasGroup>();
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.07f, 0.01f, 0.07f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);

            for (int i = -1; i <= 1; i += 2)
            {
                RectTransform eyeOutline = Panel(rect, i < 0 ? "EyeOutlineL" : "EyeOutlineR",
                    new Vector2(i * 14f, 1f), new Vector2(17f, 28f),
                    new Color(1f, 0.67f, 0.83f, 1f), sprite);
                Panel(eyeOutline, "Eye", Vector2.zero, new Vector2(9f, 20f),
                    new Color(0.105f, 0.025f, 0.09f, 1f), sprite);
            }
            return rect;
        }

        static RectTransform Goal(Transform parent, string name, Sprite sprite, out Image image)
        {
            RectTransform rect = Rect(parent, name, Vector2.zero, new Vector2(66f, 66f));
            image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.22f);
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            rect.gameObject.AddComponent<CanvasGroup>();
            AddOutline(rect, new Color(Cyan.r, Cyan.g, Cyan.b, 0.96f), 3f);
            RectTransform core = Panel(rect, "TargetCore", Vector2.zero, new Vector2(38f, 38f),
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.10f), sprite);
            AddOutline(core, new Color(0.55f, 0.94f, 1f, 0.74f), 2f);
            return rect;
        }

        static RectTransform RoomFrame(Transform parent, string name, Vector2 size, Color color, Sprite sprite)
        {
            RectTransform shell = Panel(parent, name, Vector2.zero, size, color, sprite);
            shell.gameObject.AddComponent<CanvasGroup>();
            AddOutline(shell, new Color(0f, 0.025f, 0.09f, 0.96f), 4f);

            RectTransform cyanFrame = Panel(shell, "CyanFrame", Vector2.zero, size * 0.82f,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.96f), sprite);
            RectTransform aperture = Panel(cyanFrame, "Aperture", Vector2.zero, size * 0.66f,
                new Color(0.018f, 0.055f, 0.145f, 1f), sprite);
            AddOutline(aperture, new Color(0.01f, 0.025f, 0.08f, 1f), 2f);

            // A visible open doorway makes it clear this is a room that can be entered, not a
            // decorative purple obstacle. The opening is part of the prebuilt shell artwork.
            RectTransform door = Panel(shell, "Doorway", new Vector2(size.x * 0.43f, 0f),
                new Vector2(size.x * 0.20f, size.y * 0.30f),
                new Color(0.018f, 0.055f, 0.145f, 1f), null);
            door.SetAsLastSibling();
            EnsureRoomClosedLights(shell);
            return shell;
        }

        static Text Label(Transform parent, string name, string value, Font font, int size,
                          Vector2 position, Vector2 dimensions, Color color)
        {
            RectTransform rect = Rect(parent, name, position, dimensions);
            Text text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.02f, 0.06f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }
#endif
    }
}
