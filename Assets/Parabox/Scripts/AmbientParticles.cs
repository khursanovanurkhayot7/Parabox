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

        class P { public Transform t; public SpriteRenderer sr; public float vy, vx, spin, phase, baseA; }
        readonly List<P> ps = new List<P>();
        Config cfg;

        void Start()
        {
            if (cam == null) cam = GetComponentInParent<Camera>();
            if (tiers == null || tiers.Length == 0) { enabled = false; return; }

            int level = PlayerPrefs.GetInt(LevelKey, 0);
            cfg = tiers[Mathf.Clamp(level / PerTier, 0, tiers.Length - 1)];
            if (cfg == null || cfg.sprite == null || cfg.palette == null || cfg.palette.Length == 0)
            {
                enabled = false;
                return;
            }

            for (int i = 0; i < cfg.count; i++) Spawn(true);
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
            p.t.localScale = Vector3.one * size;
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
    }
}
