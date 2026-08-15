using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Ambient environment particles that drift around the puzzle board (leaves / bubbles /
    // embers), giving each difficulty tier its own living "world". Lives as a child of the
    // game camera (so it follows pan/zoom), renders behind the board, and self-selects its
    // environment from the saved level's tier. Pooled — a fixed set of sprites is recycled.
    public class AmbientParticles : MonoBehaviour
    {
        [System.Serializable]
        public class Config
        {
            public Sprite sprite;
            public Color[] palette;
            public int count = 34;
            public Vector2 sizeRange = new Vector2(0.12f, 0.34f);
            public float driftY = -1.0f;   // negative = fall (leaves), positive = rise (bubbles/embers)
            public float swayAmp = 0.5f;   // horizontal sine sway
            public float swaySpeed = 1.0f;
            public float spin = 40f;       // deg/sec (random per particle)
            public float baseAlpha = 0.5f;
            public bool flicker = false;   // embers pulse in brightness
            public int sortingOrder = -150; // behind the board (board min -50), above backdrop (-200)
        }

        public Camera cam;
        public Config[] tiers;

        const string LevelKey = "Parabox.Level";
        const int PerTier = 10;

        class P
        {
            public Transform t;
            public SpriteRenderer sr;
            public float vy, vx, spin, phase, baseA, baseScale;
        }
        readonly List<P> ps = new List<P>();
        Config cfg;
        int requestedChapter = -1;
        bool initialized;
        float referenceViewH = 6f;

        public void SetChapter(int chapter)
        {
            requestedChapter = Mathf.Clamp(chapter, 0, 4);
            if (initialized) Rebuild();
        }

        void Start()
        {
            if (cam == null) cam = GetComponentInParent<Camera>();
            initialized = true;
            Rebuild();
        }

        void Rebuild()
        {
            for (int i = 0; i < ps.Count; i++)
                if (ps[i].t != null) Destroy(ps[i].t.gameObject);
            ps.Clear();

            if (tiers == null || tiers.Length == 0) { enabled = false; return; }

            int chapter = requestedChapter >= 0
                ? requestedChapter
                : Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, 0) / PerTier, 0, 4);
            Config source = tiers[Mathf.Clamp(chapter, 0, tiers.Length - 1)];
            cfg = ChapterStyle(source, chapter);
            if (cfg == null || cfg.sprite == null || cfg.palette == null || cfg.palette.Length == 0)
            {
                enabled = false;
                return;
            }

            enabled = true;
            // The configured ranges are authored for the normal board framing. Nested rooms can
            // make the orthographic view many times smaller, so remember this baseline and keep
            // every mote the same apparent screen size while the camera travels recursively.
            referenceViewH = Mathf.Max(0.001f, ViewH);
            for (int i = 0; i < cfg.count; i++) Spawn(true);
        }

        // Five restrained identities, one for each chapter. The scene can keep its original three
        // sprite templates; later chapters reuse the small mote sprite with different motion and
        // colour, so atmosphere changes without putting noisy art over the puzzle.
        static Config ChapterStyle(Config source, int chapter)
        {
            if (source == null) return null;
            var style = new Config
            {
                sprite = source.sprite,
                sortingOrder = source.sortingOrder,
                spin = 0f,
            };

            switch (Mathf.Clamp(chapter, 0, 4))
            {
                case 0: // Foundations — calm cyan bubbles
                    style.palette = new[]
                    {
                        new Color(0.62f, 0.94f, 1f), new Color(0.37f, 0.88f, 0.91f),
                    };
                    style.count = 10; style.sizeRange = new Vector2(0.055f, 0.16f);
                    style.driftY = 0.22f; style.swayAmp = 0.10f; style.swaySpeed = 0.75f;
                    style.baseAlpha = 0.18f; style.flicker = false;
                    break;
                case 1: // Systems — more deliberate blue current
                    style.palette = new[]
                    {
                        new Color(0.35f, 0.68f, 1f), new Color(0.59f, 0.84f, 1f),
                    };
                    style.count = 12; style.sizeRange = new Vector2(0.045f, 0.14f);
                    style.driftY = 0.34f; style.swayAmp = 0.16f; style.swaySpeed = 1.05f;
                    style.baseAlpha = 0.20f; style.flicker = false;
                    break;
                case 2: // Synergy — slow violet bioluminescence
                    style.palette = new[]
                    {
                        new Color(0.64f, 0.43f, 1f), new Color(0.48f, 0.78f, 1f),
                    };
                    style.count = 14; style.sizeRange = new Vector2(0.035f, 0.11f);
                    style.driftY = 0.10f; style.swayAmp = 0.12f; style.swaySpeed = 0.72f;
                    style.baseAlpha = 0.24f; style.flicker = true;
                    break;
                case 3: // Mastery — sparse magenta dust descending
                    style.palette = new[]
                    {
                        new Color(0.93f, 0.34f, 0.84f), new Color(0.70f, 0.43f, 1f),
                    };
                    style.count = 15; style.sizeRange = new Vector2(0.030f, 0.095f);
                    style.driftY = -0.16f; style.swayAmp = 0.08f; style.swaySpeed = 0.62f;
                    style.baseAlpha = 0.25f; style.flicker = true;
                    break;
                default: // Convergence — cool cyan/violet energy inside the shared cabinet
                    style.palette = new[]
                    {
                        new Color(0.18f, 0.72f, 1f), new Color(0.55f, 0.34f, 1f),
                    };
                    style.count = 16; style.sizeRange = new Vector2(0.028f, 0.085f);
                    style.driftY = 0.055f; style.swayAmp = 0.06f; style.swaySpeed = 0.50f;
                    style.baseAlpha = 0.23f; style.flicker = true;
                    break;
            }
            return style;
        }

        float ViewH => cam != null ? cam.orthographicSize : 6f;
        float ViewW => ViewH * (cam != null ? cam.aspect : 1.6f);

        void Spawn(bool anywhere)
        {
            var go = new GameObject("p");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = cfg.sprite;
            sr.sortingOrder = cfg.sortingOrder;
            var p = new P { t = go.transform, sr = sr };
            Place(p, anywhere);
            ps.Add(p);
        }

        void Place(P p, bool anywhere)
        {
            float w = ViewW * 1.15f, h = ViewH * 1.25f;
            float size = Random.Range(cfg.sizeRange.x, cfg.sizeRange.y);
            p.baseA = cfg.baseAlpha * Random.Range(0.55f, 1f);
            var c = cfg.palette[Random.Range(0, cfg.palette.Length)];
            p.sr.color = new Color(c.r, c.g, c.b, p.baseA);
            p.baseScale = size;
            ScaleForCamera(p);
            p.t.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            p.spin = Random.Range(-cfg.spin, cfg.spin);
            p.phase = Random.Range(0f, 6.283f);
            p.vy = cfg.driftY * Random.Range(0.7f, 1.3f);
            p.vx = Random.Range(-0.12f, 0.12f);
            float y = anywhere ? Random.Range(-h, h) : (cfg.driftY < 0f ? h : -h);
            p.t.localPosition = new Vector3(Random.Range(-w, w), y, 0f);
        }

        void Update()
        {
            if (cfg == null) return;
            float dt = Time.deltaTime;
            float w = ViewW * 1.25f, h = ViewH * 1.4f;
            float tt = Time.time;

            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                ScaleForCamera(p);
                var pos = p.t.localPosition;
                pos.y += p.vy * dt;
                pos.x += (p.vx + Mathf.Sin(tt * cfg.swaySpeed + p.phase) * cfg.swayAmp) * dt;
                p.t.localPosition = pos;
                if (cfg.spin != 0f) p.t.Rotate(0f, 0f, p.spin * dt);

                if (cfg.flicker)
                {
                    var c = p.sr.color;
                    c.a = p.baseA * (0.55f + 0.45f * Mathf.Sin(tt * 6f + p.phase));
                    p.sr.color = c;
                }

                if ((cfg.driftY < 0f && pos.y < -h) || (cfg.driftY > 0f && pos.y > h) || Mathf.Abs(pos.x) > w)
                    Place(p, false);
            }
        }

        void ScaleForCamera(P p)
        {
            if (p == null || p.t == null) return;
            float cameraRatio = ViewH / Mathf.Max(0.001f, referenceViewH);
            p.t.localScale = Vector3.one * p.baseScale * cameraRatio;
        }
    }
}
