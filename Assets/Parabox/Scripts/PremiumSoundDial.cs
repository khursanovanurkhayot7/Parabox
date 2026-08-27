using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Segmented sci-fi dial surrounding the speaker glyph. It mirrors the circular countdown
    // language above it while remaining visually distinct from the timer itself.
    [AddComponentMenu("")]
    public sealed class PremiumSoundDial : MaskableGraphic
    {
        [SerializeField] bool muted;
        [SerializeField] Color primary = new Color(0.28f, 0.94f, 1f, 1f);
        [SerializeField] Color secondary = new Color(0.56f, 0.31f, 1f, 1f);

        public void SetState(bool isMuted, Color main, Color alternate)
        {
            muted = isMuted;
            primary = main;
            secondary = alternate;
            color = Color.white;
            if (muted) rectTransform.localRotation = Quaternion.identity;
            SetVerticesDirty();
        }

        void Update()
        {
            // A restrained instrument-like drift makes the dial feel alive without distracting
            // from the countdown above it. Muted state rests completely still.
            float angle = muted ? 0f : Mathf.Sin(Time.unscaledTime * 0.72f) * 2.2f;
            rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            // Exactly one rim. State already reads through the speaker slash, LED and ON/OFF text;
            // extra inner/status arcs only create stacked lines at the bottom. Muted uses the same
            // coral-red language as the slash, status LED and OFF label.
            Color rim = muted ? new Color(1f, 0.40f, 0.49f, 1f) : primary;
            AddArc(vh, 40f, 2.8f, 0f, 360f, 48, WithAlpha(rim, 0.88f));
        }

        static void AddArc(VertexHelper vh, float radius, float width, float fromDegrees,
            float toDegrees, int segments, Color tint)
        {
            float inner = radius - width * 0.5f;
            float outer = radius + width * 0.5f;
            Color32 colour = tint;
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Lerp(fromDegrees, toDegrees, i / (float)segments)
                    * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(fromDegrees, toDegrees, (i + 1) / (float)segments)
                    * Mathf.Deg2Rad;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                AddQuad(vh, d0 * inner, d1 * inner, d1 * outer, d0 * outer, colour);
            }
        }

        static Color WithAlpha(Color value, float alpha)
        {
            value.a = alpha;
            return value;
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
