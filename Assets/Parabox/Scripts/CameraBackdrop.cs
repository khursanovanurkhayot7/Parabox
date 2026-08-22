using UnityEngine;
using UnityEngine.Rendering;

namespace Parabox
{
    // Tier-tinted atmospheric backdrop behind the puzzle board: a solid base color on the
    // camera plus two soft nebula glows and a vignette, sized to fill the view every frame.
    // It lives as a child of the game camera (so it stays centered as the camera pans/zooms)
    // and configures its palette from the saved level's difficulty tier — matching the
    // Beginner / Intermediate / Advanced look of the level-select screens.
    // CameraFollow and the menu fly-in both change the orthographic size in LateUpdate. Run after
    // them so the photo is fitted to the final camera pose for this frame. Without an explicit
    // order, the Level 50 deep fly-in could render one frame with the old, much smaller backdrop
    // scale, exposing the vertical bands behind it; the same race was possible on every level.
    [DefaultExecutionOrder(1000)]
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

        FilmGridBackdrop filmGrid;

        const string LevelKey = "Parabox.Level";
        const int PerTier = 10;

        void Start()
        {
            if (cam == null) cam = GetComponentInParent<Camera>();
            EnsureFilmGrid();
            int level = PlayerPrefs.GetInt(LevelKey, 0);
            int tier = (baseColors != null && baseColors.Length > 0)
                ? Mathf.Clamp(level / PerTier, 0, baseColors.Length - 1)
                : 0;
            Apply(tier);
        }

        void OnEnable()
        {
            if (cam == null) cam = GetComponentInParent<Camera>();
            EnsureFilmGrid();
            // Never expose the old teal scene clear colour while the runtime background is being
            // constructed. This also makes the menu look correct on its very first rendered frame.
            if (Application.isPlaying && cam != null)
                cam.backgroundColor = new Color(0.0025f, 0.006f, 0.028f, 1f);
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
            RenderPipelineManager.beginCameraRendering += BeforeCameraRender;
            FitToCamera();
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
        }

        void BeforeCameraRender(ScriptableRenderContext context, Camera renderingCamera)
        {
            if (renderingCamera == cam) FitToCamera();
        }

        public void Apply(int tier)
        {
            int chapter = Mathf.Clamp(tier, 0, 4);
            EnsureFilmGrid();
            if (HasPregeneratedArtwork())
            {
                ApplyPregeneratedArtwork();
                return;
            }
            if (filmGrid != null) filmGrid.SetChapter(chapter);
            bool premiumStyle = filmGrid != null;
            bool menuArtwork = gameObject.scene.name == "MainMenu";
            Color accent = ChapterAccent(chapter);
            // When a real background photo is present, keep the procedural glows/rays subtle so
            // the art shows. Chapter V still needs enough cool light to separate its deep nested
            // shells from the cabinet, so it uses a slightly stronger (but never coloured-wash)
            // atmosphere.
            // The premium background is a live flat graphic, not a photograph. Hiding the legacy
            // art prevents its static grid from doubling the animated rows built at runtime.
            // Gameplay replaces its old static plate. The main menu's plate also contains the
            // logo, diorama and button faces, so keep it and render only edge-safe animated light
            // accents above it.
            if (bgPhoto != null) bgPhoto.enabled = menuArtwork || !premiumStyle;
            bool photo = bgPhoto != null && bgPhoto.enabled && bgPhoto.sprite != null;
            float atmos = photo ? 0.40f : 1f;

            Color baseColor = Configured(baseColors, chapter,
                Color.Lerp(new Color(0.003f, 0.012f, 0.032f, 1f), accent, 0.12f));
            // Existing early-chapter values remain the foundation, while a restrained chapter tint
            // makes all five environments identifiable even behind the shared chamber artwork.
            baseColor = Color.Lerp(baseColor, Color.Lerp(Color.black, accent, 0.19f), 0.30f);
            if (premiumStyle && !menuArtwork)
                baseColor = new Color(0.0025f, 0.006f, 0.028f, 1f);
            if (cam != null) cam.backgroundColor = baseColor;

            Color glowA = Configured(glowAColors, chapter, WithAlpha(accent, 0.16f));
            Color glowB = Configured(glowBColors, chapter,
                WithAlpha(Color.Lerp(accent, Color.white, 0.22f), 0.10f));
            Color ray = Configured(rayColors, chapter,
                WithAlpha(Color.Lerp(accent, Color.white, 0.34f), 0.085f));
            // Old scenes intentionally serialized transparent atmosphere. Treat that as "use the
            // new chapter default" rather than leaving Chapters I–III visually identical.
            if (glowA.a <= 0.001f) glowA = WithAlpha(accent, 0.16f);
            if (glowB.a <= 0.001f) glowB = WithAlpha(Color.Lerp(accent, Color.white, 0.22f), 0.10f);
            if (ray.a <= 0.001f) ray = WithAlpha(Color.Lerp(accent, Color.white, 0.34f), 0.085f);

            // The old Chapter-V preset was coral/rose. On the shared blue sci-fi cabinet it read
            // as a full-screen damage flash. Do not colour-grade the supplied cabinet photograph:
            // it already contains the correct cyan/violet practical lights. Recursive boxes add
            // their lighting locally, so the atmosphere sprites stay transparent in this chapter.
            if (chapter == 4)
            {
                baseColor = new Color(0.003f, 0.012f, 0.045f, 1f);
                if (cam != null) cam.backgroundColor = baseColor;
                glowA = new Color(0.055f, 0.30f, 0.72f, 0.078f);
                glowB = new Color(0.34f, 0.13f, 0.72f, 0.042f);
                ray = Color.clear;
            }

            if (premiumStyle)
            {
                // A clean navy field lets the live cyan/violet lines and particles provide the
                // light instead of layering on the scene's old nebula/god-ray sprites.
                baseColor = new Color(0.0025f, 0.006f, 0.028f, 1f);
                glowA = Color.clear;
                glowB = Color.clear;
                ray = Color.clear;
                if (cam != null) cam.backgroundColor = baseColor;
            }

            if (glow1 != null)
            {
                glow1.enabled = true;
                glow1.color = Fade(glowA, atmos);
            }
            if (glow2 != null)
            {
                glow2.enabled = true;
                glow2.color = Fade(glowB, atmos);
            }
            if (rays != null)
                foreach (var r in rays) if (r != null)
                {
                    r.enabled = !premiumStyle;
                    r.color = Fade(ray, photo ? 0.6f : 1f);
                }
            if (bgPhoto != null && bgPhoto.enabled && bgPhoto.sprite != null)
                bgPhoto.color = menuArtwork
                    ? Color.white
                    : chapter == 4
                    ? Color.white
                    : Color.Lerp(Color.white, accent, 0.055f);
            // A soft edge falloff blends the linework into the navy field and keeps attention on
            // the puzzle.
            if (vignette != null)
            {
                vignette.enabled = premiumStyle && !menuArtwork;
                vignette.color = premiumStyle && !menuArtwork
                    ? new Color(0f, 0.004f, 0.018f, 0.15f)
                    : Color.clear;
            }

            // Apply the material and camera-cover scale on the same frame. This is intentionally
            // repeated by LateUpdate because the menu fly-in can animate the camera afterwards.
            FitToCamera();
        }

        static Color Configured(Color[] colors, int chapter, Color fallback)
            => colors != null && chapter >= 0 && chapter < colors.Length
                ? colors[chapter] : fallback;

        static Color ChapterAccent(int chapter)
        {
            switch (Mathf.Clamp(chapter, 0, 4))
            {
                case 0: return new Color(0.10f, 0.88f, 0.91f, 1f);
                case 1: return new Color(0.31f, 0.62f, 1.00f, 1f);
                case 2: return new Color(0.61f, 0.36f, 1.00f, 1f);
                case 3: return new Color(0.91f, 0.31f, 0.82f, 1f);
                default: return new Color(0.24f, 0.58f, 1.00f, 1f);
            }
        }

        static Color WithAlpha(Color c, float alpha)
            => new Color(c.r, c.g, c.b, alpha);

        static Color Fade(Color c, float m) => new Color(c.r, c.g, c.b, c.a * m);

        bool HasPregeneratedArtwork()
        {
            return bgPhoto != null && bgPhoto.sprite != null;
        }

        void ApplyPregeneratedArtwork()
        {
            // MainMenuApproved.png and GameBGApproved.png already contain the complete visual.
            // Keep them unchanged in Play Mode: no runtime material, tint, glow or generated grid.
            bgPhoto.enabled = true;
            bgPhoto.color = Color.white;

            if (filmGrid != null) filmGrid.enabled = false;
            if (glow1 != null) glow1.enabled = false;
            if (glow2 != null) glow2.enabled = false;
            if (vignette != null) vignette.enabled = false;
            if (rays != null)
                foreach (SpriteRenderer ray in rays)
                    if (ray != null) ray.enabled = false;

            if (cam != null)
                cam.backgroundColor = new Color(0.0025f, 0.006f, 0.028f, 1f);
            FitToCamera();
        }

        void EnsureFilmGrid()
        {
            if (filmGrid == null) filmGrid = GetComponent<FilmGridBackdrop>();
            // Runtime-generated backgrounds are intentionally retired. Existing scenes retain the
            // component disabled for backwards compatibility, but it must never be auto-created.
            if (filmGrid != null)
            {
                filmGrid.cam = cam;
                if (HasPregeneratedArtwork()) filmGrid.enabled = false;
            }
        }

        void LateUpdate()
        {
            FitToCamera();
        }

        // Public for automated aspect-ratio QA and for any presentation camera that changes its
        // projection outside the normal frame loop (for example the tutorial RenderTexture).
        public void FitToCamera()
        {
            if (cam == null) return;
            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            float big = Mathf.Max(w, h);

            if (bgPhoto != null && bgPhoto.sprite != null)
            {
                var bs = bgPhoto.sprite.bounds.size;   // world size at scale 1
                if (bs.x > 0.0001f && bs.y > 0.0001f)
                {
                    // Cover the view without distorting the artwork. A non-16:9 cabinet crops a
                    // little at the edge instead of stretching the mechanical frame or cubes.
                    // A small menu overscan prevents a coloured matte from appearing because of
                    // rounding, Game-view toolbar resizing or the opening camera animation.
                    float overscan = gameObject.scene.name == "MainMenu" ? 1.035f : 1f;
                    float fit = Mathf.Max(w / bs.x, h / bs.y) * overscan;
                    bgPhoto.transform.localScale = new Vector3(fit, fit, 1f);
                }
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

            if (filmGrid != null) filmGrid.FitToCamera(false);
        }

#if UNITY_EDITOR
        // Used by the in-Editor regression hook to verify coverage on the exact frame that the
        // Level 50 fly-in expands the camera. Kept out of player builds.
        public bool CoversCameraView(float tolerance = 0.001f)
        {
            if (cam == null || bgPhoto == null || bgPhoto.sprite == null) return false;
            Vector3 spriteSize = bgPhoto.sprite.bounds.size;
            Vector3 scale = bgPhoto.transform.localScale;
            float actualWidth = spriteSize.x * Mathf.Abs(scale.x);
            float actualHeight = spriteSize.y * Mathf.Abs(scale.y);
            float requiredHeight = cam.orthographicSize * 2f;
            float requiredWidth = requiredHeight * cam.aspect;
            return actualWidth + tolerance >= requiredWidth
                && actualHeight + tolerance >= requiredHeight;
        }
#endif
    }
}
