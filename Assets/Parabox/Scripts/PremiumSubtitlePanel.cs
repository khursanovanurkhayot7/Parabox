using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Native UI geometry, not a generated picture. Crisp at any canvas scale, with the
    // existing live subtitle text and CanvasGroup controlling visibility and timing.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class PremiumSubtitlePanel : BaseMeshEffect
    {
        [SerializeField] bool lessonBoard;

        public static void Apply(GameObject panel, bool asLessonBoard = false)
        {
            if (panel == null) return;
            Image face = panel.GetComponent<Image>();
            if (face == null) return;
            foreach (BaseMeshEffect effect in panel.GetComponents<BaseMeshEffect>())
                if (!(effect is PremiumSubtitlePanel)) effect.enabled = false;
            Transform accent = panel.transform.Find("SubtitleAccent");
            if (accent != null) accent.gameObject.SetActive(false);
            face.sprite = null;
            face.type = Image.Type.Simple;
            face.color = Color.white;
            face.raycastTarget = false;
            PremiumSubtitlePanel finish = panel.GetComponent<PremiumSubtitlePanel>();
            if (finish == null) finish = panel.AddComponent<PremiumSubtitlePanel>();
            finish.lessonBoard = asLessonBoard;
            finish.enabled = true;
            face.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || graphic == null) return;
            mesh.Clear();
            Rect r = graphic.rectTransform.rect;
            if (r.width < 20f || r.height < 20f) return;
            float halfW = r.width * 0.5f;
            float halfH = r.height * 0.5f;
            Vector2 centre = r.center;
            if (lessonBoard)
            {
                DrawLessonBoard(mesh, centre, halfW, halfH);
                return;
            }
            // Recessed base, a slim machined rim, then a shaded inset glass face.
            Plate(mesh, centre + new Vector2(0f, -3f), halfW, halfH - 3f, 15f,
                C(15, 39, 62), C(3, 7, 19));
            Plate(mesh, centre + new Vector2(0f, 2f), halfW - 1f, halfH - 5f, 14f,
                C(83, 182, 207), C(62, 61, 120));
            Plate(mesh, centre + new Vector2(0f, 2f), halfW - 2.5f, halfH - 6.5f, 13f,
                C(26, 62, 87), C(12, 20, 43));
            Plate(mesh, centre + new Vector2(0f, 2f), halfW - 6f, halfH - 10f, 10f,
                C(17, 36, 57), C(6, 13, 29));

            // Restrained top bevel and recessed lower edge; no floating notch or pill.
            Stripe(mesh, centre, -halfW + 28f, halfW - 28f, halfH - 4f, 1.5f,
                C(0, 0, 0, 0), C(178, 242, 255, 205));
            Stripe(mesh, centre, -halfW + 32f, halfW - 32f, -halfH + 8f, 1f,
                C(0, 0, 0, 0), C(104, 130, 215, 110));
            // Small side lights echo the board's cyan/violet edges, outside the text area.
            Plate(mesh, centre + new Vector2(-halfW + 3.5f, 2f), 1.5f, 12f, 1.5f,
                C(170, 247, 255), C(41, 182, 222));
            Plate(mesh, centre + new Vector2(halfW - 3.5f, 2f), 1.5f, 12f, 1.5f,
                C(196, 184, 255), C(102, 90, 198));
        }

        void DrawLessonBoard(VertexHelper mesh, Vector2 c, float w, float h)
        {
            // The original cyan/violet dimensional frame, dark inset, corner studs and
            // gold ledge. Geometry stays inside the panel: no large black backing rectangle.
            Plate(mesh, c + new Vector2(0f, -5f), w - 1f, h - 6f, 30f,
                C(23, 65, 97), C(26, 13, 58));
            Plate(mesh, c + new Vector2(0f, 4f), w - 3f, h - 12f, 30f,
                C(91, 241, 255), C(117, 60, 222));
            Plate(mesh, c + new Vector2(0f, 4f), w - 8f, h - 17f, 27f,
                C(34, 121, 155), C(70, 30, 119));
            Plate(mesh, c + new Vector2(0f, 4f), w - 13f, h - 22f, 24f,
                C(87, 211, 239), C(75, 112, 169));
            Plate(mesh, c + new Vector2(0f, 4f), w - 15f, h - 24f, 23f,
                C(20, 52, 78), C(5, 12, 25));
            Stripe(mesh, c, -w + 43f, w - 43f, h - 12f, 2f,
                C(150, 229, 249, 30), C(230, 255, 255));
            // No speaker nameplate or its divider: keep the board face clear for dialogue.

            Plate(mesh, c + new Vector2(0f, -h + 4f), w * 0.60f, 3f, 2f,
                C(109, 65, 17), C(74, 37, 12));
            Plate(mesh, c + new Vector2(0f, -h + 9f), w * 0.60f, 5f, 3f,
                C(255, 238, 158), C(193, 121, 28));
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector2 p = c + new Vector2(x * (w - 15f), y * (h - 23f) + 4f);
                    Plate(mesh, p, 8f, 8f, 3f, C(27, 107, 144), C(17, 43, 77));
                    Plate(mesh, p + new Vector2(0f, 1f), 4f, 4f, 1.5f,
                        C(188, 253, 255), C(35, 205, 237));
                }
        }

        static Color C(byte r, byte g, byte b, byte a = 255)
            => new Color32(r, g, b, a);

        void Vertex(VertexHelper mesh, Vector2 point, Color tint)
        {
            Color colour = tint * graphic.color;
            mesh.AddVert(new Vector3(point.x, point.y, 0f), colour, Vector2.zero);
        }

        void Plate(VertexHelper mesh, Vector2 centre, float hx, float hy, float radius,
            Color top, Color bottom)
        {
            radius = Mathf.Min(radius, Mathf.Min(hx, hy));
            int start = mesh.currentVertCount;
            Vertex(mesh, centre, Color.Lerp(bottom, top, 0.5f));
            const int segments = 6;
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = corner == 0 || corner == 3 ? hx - radius : -hx + radius;
                float cy = corner < 2 ? hy - radius : -hy + radius;
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (corner * 90f + step * 90f / segments) * Mathf.Deg2Rad;
                    Vector2 p = new Vector2(cx + Mathf.Cos(angle) * radius,
                        cy + Mathf.Sin(angle) * radius);
                    Vertex(mesh, centre + p, Color.Lerp(bottom, top,
                        Mathf.InverseLerp(-hy, hy, p.y)));
                }
            }
            int count = 4 * (segments + 1);
            for (int i = 0; i < count; i++)
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
        }

        void Stripe(VertexHelper mesh, Vector2 centre, float left, float right, float y,
            float height, Color edge, Color middle)
        {
            int start = mesh.currentVertCount;
            for (int i = 0; i < 3; i++)
            {
                float x = Mathf.Lerp(left, right, i * 0.5f);
                Color tint = i == 1 ? middle : edge;
                Vertex(mesh, centre + new Vector2(x, y), tint);
                Vertex(mesh, centre + new Vector2(x, y + height), tint);
            }
            for (int i = 0; i < 2; i++)
            {
                int a = start + i * 2;
                mesh.AddTriangle(a, a + 1, a + 2);
                mesh.AddTriangle(a + 1, a + 3, a + 2);
            }
        }
    }
}
