using UnityEngine;

namespace Parabox
{
    // Tier-tinted atmospheric backdrop behind the puzzle board: a solid base color on the
    // camera plus two soft nebula glows and a vignette, sized to fill the view every frame.
    // It lives as a child of the game camera (so it stays centered as the camera pans/zooms)
    // and configures its palette from the saved level's difficulty tier — matching the
    // Beginner / Intermediate / Advanced look of the level-select screens.
    public class CameraBackdrop : MonoBehaviour
    {
        public Camera cam;
        public SpriteRenderer glow1, glow2, vignette;
        public SpriteRenderer[] rays;   // underwater god-ray light shafts
        public SpriteRenderer bgPhoto;  // optional drop-in background image (fills the view)
        public Color[] baseColors;   // camera solid color, per tier
        public Color[] glowAColors;  // primary nebula glow, per tier
        public Color[] glowBColors;  // secondary nebula glow, per tier
        public Color[] rayColors;    // god-ray tint, per tier
        [Range(0f, 1f)] public float vignetteAlpha = 0f;

        const string LevelKey = "Parabox.Level";
        const int PerTier = 10;

        void Start()
        {
            int level = PlayerPrefs.GetInt(LevelKey, 0);
            int tier = (baseColors != null && baseColors.Length > 0)
                ? Mathf.Clamp(level / PerTier, 0, baseColors.Length - 1)
                : 0;
            Apply(tier);
        }

        public void Apply(int tier)
        {
            // when a real background photo is present, keep the procedural glows/rays subtle so the art shows
            bool photo = bgPhoto != null && bgPhoto.sprite != null;
            float atmos = photo ? 0.40f : 1f;
            if (cam != null && baseColors != null && tier < baseColors.Length)
                cam.backgroundColor = baseColors[tier];
            if (glow1 != null && glowAColors != null && tier < glowAColors.Length) glow1.color = Fade(glowAColors[tier], atmos);
            if (glow2 != null && glowBColors != null && tier < glowBColors.Length) glow2.color = Fade(glowBColors[tier], atmos);
            if (rays != null && rayColors != null && tier < rayColors.Length)
                foreach (var r in rays) if (r != null) r.color = Fade(rayColors[tier], photo ? 0.6f : 1f);
            // The vignette sprite reads as a second room-sized border once the camera zooms into a
            // board. Disable it outright; the solid backdrop is cleaner and keeps the grid dominant.
            if (vignette != null)
            {
                vignette.color = Color.clear;
                vignette.enabled = false;
            }
        }

        static Color Fade(Color c, float m) => new Color(c.r, c.g, c.b, c.a * m);

        void LateUpdate()
        {
            if (cam == null) return;
            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            float big = Mathf.Max(w, h);

            if (bgPhoto != null && bgPhoto.sprite != null)
            {
                var bs = bgPhoto.sprite.bounds.size;   // world size at scale 1
                if (bs.x > 0.0001f && bs.y > 0.0001f)
                    bgPhoto.transform.localScale = new Vector3(w / bs.x, h / bs.y, 1f);
                bgPhoto.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            }

            if (glow1 != null)
            {
                glow1.transform.localScale = Vector3.one * big * 1.5f;
                glow1.transform.localPosition = new Vector3(-w * 0.22f, h * 0.16f, 0f);
            }
            if (glow2 != null)
            {
                glow2.transform.localScale = Vector3.one * big * 1.25f;
                glow2.transform.localPosition = new Vector3(w * 0.24f, -h * 0.18f, 0f);
            }
            if (vignette != null)
                vignette.transform.localScale = new Vector3(w * 1.15f, h * 1.15f, 1f);

            if (rays != null)
            {
                float[] rx = { -0.30f, -0.02f, 0.28f };   // spread across the width
                float[] rz = { 12f, -6f, 16f };           // slight fan of angles
                for (int i = 0; i < rays.Length; i++)
                {
                    if (rays[i] == null) continue;
                    rays[i].transform.localScale = new Vector3(big * 0.42f, big * 1.7f, 1f);
                    rays[i].transform.localPosition = new Vector3(w * rx[i % 3], h * 0.12f, 0.05f);
                    rays[i].transform.localEulerAngles = new Vector3(0f, 0f, rz[i % 3]);
                }
            }
        }
    }
}
