using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // A sharp, resolution-independent speaker glyph for the gameplay sound control. Building the
    // mesh in UI space avoids emoji/font substitutions and keeps ON/OFF readable in WebGL.
    [AddComponentMenu("")]
    public sealed class PremiumSoundIcon : MaskableGraphic
    {
        [SerializeField] bool muted;

        public void SetState(bool isMuted, Color accent)
        {
            muted = isMuted;
            color = accent;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Color32 tint = color;

            // Speaker body and flared cone.
            AddQuad(vh,
                new Vector2(-21f, -7f), new Vector2(-13f, -7f),
                new Vector2(-13f, 7f), new Vector2(-21f, 7f), tint);
            AddQuad(vh,
                new Vector2(-13f, -7f), new Vector2(-3f, -15f),
                new Vector2(-3f, 15f), new Vector2(-13f, 7f), tint);

            if (muted)
            {
                // Keep every OFF cue in one clear colour language: speaker, slash, LED, label
                // and outer rim all use the same coral-red accent.
                Color32 slash = color;
                AddLine(vh, new Vector2(-22f, 16f), new Vector2(21f, -16f), 4.5f, slash);
                return;
            }

            // Two clean sound arcs; segmented quads stay crisp at every Canvas scale.
            AddArc(vh, new Vector2(-2f, 0f), 11f, 2.8f, -54f, 54f, 7, tint);
            Color outer = Color.Lerp(color, Color.white, 0.34f);
            AddArc(vh, new Vector2(-2f, 0f), 19f, 2.8f, -54f, 54f, 9, outer);
        }

        static void AddArc(VertexHelper vh, Vector2 centre, float radius, float width,
            float fromDegrees, float toDegrees, int segments, Color32 tint)
        {
            float inner = radius - width * 0.5f;
            float outer = radius + width * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Lerp(fromDegrees, toDegrees, i / (float)segments)
                    * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(fromDegrees, toDegrees, (i + 1) / (float)segments)
                    * Mathf.Deg2Rad;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                AddQuad(vh, centre + d0 * inner, centre + d1 * inner,
                    centre + d1 * outer, centre + d0 * outer, tint);
            }
        }

        static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color32 tint)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            AddQuad(vh, from - normal, to - normal, to + normal, from + normal, tint);
        }

        static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color32 tint)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, tint, Vector2.zero);
            vh.AddVert(b, tint, Vector2.right);
            vh.AddVert(c, tint, Vector2.one);
            vh.AddVert(d, tint, Vector2.up);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
