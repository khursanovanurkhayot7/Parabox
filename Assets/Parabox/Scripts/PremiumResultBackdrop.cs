using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Resolution-independent circuit/grid ornament for the results board. It deliberately draws
    // no opaque fill: the GameManager's chapter-coloured glass remains the readable base layer.
    [AddComponentMenu("")]
    public sealed class PremiumResultBackdrop : MaskableGraphic
    {
        [SerializeField] Color accent = new Color(0.30f, 0.94f, 1f, 1f);
        [SerializeField] Color secondary = new Color(0.55f, 0.30f, 1f, 1f);

        public void SetPalette(Color primary, Color alternate)
        {
            accent = primary;
            secondary = alternate;
            color = Color.white;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float left = r.xMin + 19f;
            float right = r.xMax - 19f;
            float bottom = r.yMin + 19f;
            float top = r.yMax - 19f;

            Color grid = WithAlpha(accent, 0.055f);
            for (float y = bottom + 48f; y < top - 30f; y += 54f)
                AddLine(vh, new Vector2(left + 28f, y), new Vector2(right - 28f, y),
                    1f, grid);

            // A large faint hexagonal chamber links this screen visually to the recursive boards.
            Color chamber = WithAlpha(secondary, 0.065f);
            float hexX = Mathf.Min(238f, r.width * 0.34f);
            float hexY = Mathf.Min(218f, r.height * 0.36f);
            Vector2[] hex =
            {
                new Vector2(0f, hexY),
                new Vector2(hexX, hexY * 0.48f),
                new Vector2(hexX, -hexY * 0.48f),
                new Vector2(0f, -hexY),
                new Vector2(-hexX, -hexY * 0.48f),
                new Vector2(-hexX, hexY * 0.48f),
            };
            for (int i = 0; i < hex.Length; i++)
                AddLine(vh, hex[i], hex[(i + 1) % hex.Length], 1.35f, chamber);

            DrawCorner(vh, new Vector2(left, top), 1f, -1f);
            DrawCorner(vh, new Vector2(right, top), -1f, -1f);
            DrawCorner(vh, new Vector2(left, bottom), 1f, 1f);
            DrawCorner(vh, new Vector2(right, bottom), -1f, 1f);

            DrawCircuit(vh, new Vector2(left + 7f, top - 112f), 1f, -1f);
            DrawCircuit(vh, new Vector2(right - 7f, top - 112f), -1f, -1f);
            DrawCircuit(vh, new Vector2(left + 7f, bottom + 92f), 1f, 1f);
            DrawCircuit(vh, new Vector2(right - 7f, bottom + 92f), -1f, 1f);

            // Short premium rails frame the reward area without crossing any text.
            Color rail = WithAlpha(accent, 0.24f);
            AddLine(vh, new Vector2(-116f, top - 9f), new Vector2(116f, top - 9f), 2f, rail);
            AddLine(vh, new Vector2(-78f, bottom + 9f), new Vector2(78f, bottom + 9f),
                1.5f, WithAlpha(secondary, 0.19f));
        }

        void DrawCorner(VertexHelper vh, Vector2 corner, float xDirection, float yDirection)
        {
            Color bright = WithAlpha(accent, 0.54f);
            Color soft = WithAlpha(secondary, 0.24f);
            Vector2 xEnd = corner + new Vector2(58f * xDirection, 0f);
            Vector2 yEnd = corner + new Vector2(0f, 58f * yDirection);
            AddLine(vh, corner, xEnd, 3f, bright);
            AddLine(vh, corner, yEnd, 3f, bright);
            AddLine(vh, corner + new Vector2(9f * xDirection, 9f * yDirection),
                corner + new Vector2(42f * xDirection, 9f * yDirection), 1f, soft);
            AddDiamond(vh, corner, 4.5f, Color.Lerp(bright, Color.white, 0.18f));
        }

        void DrawCircuit(VertexHelper vh, Vector2 start, float xDirection, float yDirection)
        {
            Color line = WithAlpha(accent, 0.18f);
            Vector2 p1 = start + new Vector2(58f * xDirection, 0f);
            Vector2 p2 = p1 + new Vector2(24f * xDirection, 22f * yDirection);
            Vector2 p3 = p2 + new Vector2(54f * xDirection, 0f);
            AddLine(vh, start, p1, 1.5f, line);
            AddLine(vh, p1, p2, 1.5f, line);
            AddLine(vh, p2, p3, 1.5f, line);
            AddDiamond(vh, p3, 3.6f, WithAlpha(secondary, 0.34f));
        }

        static Color WithAlpha(Color value, float alpha)
        {
            value.a = alpha;
            return value;
        }

        static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color tint)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            AddQuad(vh, from - normal, to - normal, to + normal, from + normal, tint);
        }

        static void AddDiamond(VertexHelper vh, Vector2 centre, float radius, Color tint)
        {
            AddQuad(vh,
                centre + Vector2.up * radius,
                centre + Vector2.right * radius,
                centre + Vector2.down * radius,
                centre + Vector2.left * radius,
                tint);
        }

        static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color tint)
        {
            int start = vh.currentVertCount;
            Color32 colour = tint;
            vh.AddVert(a, colour, Vector2.zero);
            vh.AddVert(b, colour, Vector2.right);
            vh.AddVert(c, colour, Vector2.one);
            vh.AddVert(d, colour, Vector2.up);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
