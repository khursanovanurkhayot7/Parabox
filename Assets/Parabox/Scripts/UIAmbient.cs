using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Floating UI light-motes for a premium animated menu background. Spawns soft Image dots
    // that drift upward with a gentle sway and recycle at the top. Sized to its own
    // (full-screen) RectTransform. Uses unscaled time so it runs on menus regardless of pause.
    [RequireComponent(typeof(RectTransform))]
    public class UIAmbient : MonoBehaviour
    {
        public Sprite sprite;
        public Color[] palette;
        public int count = 26;
        public Vector2 sizeRange = new Vector2(6f, 22f);
        public float riseSpeed = 24f;
        public float sway = 16f;
        public float baseAlpha = 0.30f;

        RectTransform rt;
        class P { public RectTransform t; public Image img; public float vx, phase, baseA, speed; }
        readonly List<P> ps = new List<P>();

        void Start()
        {
            rt = (RectTransform)transform;
            if (sprite == null || palette == null || palette.Length == 0) { enabled = false; return; }
            for (int i = 0; i < count; i++) Spawn(true);
        }

        Vector2 Size => rt.rect.size;

        void Spawn(bool anywhere)
        {
            var go = new GameObject("mote", typeof(RectTransform));
            var prt = (RectTransform)go.transform;
            prt.SetParent(rt, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            var p = new P { t = prt, img = img };
            Place(p, anywhere);
            ps.Add(p);
        }

        void Place(P p, bool anywhere)
        {
            var s = Size;
            float sz = Random.Range(sizeRange.x, sizeRange.y);
            p.t.sizeDelta = new Vector2(sz, sz);
            p.baseA = baseAlpha * Random.Range(0.5f, 1f);
            var c = palette[Random.Range(0, palette.Length)];
            p.img.color = new Color(c.r, c.g, c.b, p.baseA);
            p.speed = riseSpeed * Random.Range(0.6f, 1.4f);
            p.vx = Random.Range(-4f, 4f);
            p.phase = Random.Range(0f, 6.283f);
            float y = anywhere ? Random.Range(-s.y * 0.5f, s.y * 0.5f) : -s.y * 0.5f - sz;
            p.t.anchoredPosition = new Vector2(Random.Range(-s.x * 0.5f, s.x * 0.5f), y);
        }

        void Update()
        {
            var s = Size;
            if (s.x <= 0f) return;
            float dt = Time.unscaledDeltaTime;
            float tt = Time.unscaledTime;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                var pos = p.t.anchoredPosition;
                pos.y += p.speed * dt;
                pos.x += (p.vx + Mathf.Sin(tt * 0.6f + p.phase) * sway) * dt;
                p.t.anchoredPosition = pos;
                if (pos.y > s.y * 0.5f + 40f) Place(p, false);
            }
        }
    }
}
