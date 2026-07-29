using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // One-shot premium celebration burst: soft glowing dots, star sparkles, and light streaks fly
    // outward from the centre with smooth deceleration and a gradual fade-out. Fires whenever this
    // object becomes active (e.g. the win panel appears). Uses unscaled time so it runs while paused.
    [RequireComponent(typeof(RectTransform))]
    public class UIBurst : MonoBehaviour
    {
        public Sprite glowSprite;    // soft glow — dots + streaks
        public Sprite sparkSprite;   // star — sparkles
        public Color[] palette;
        public int count = 28;
        public float speed = 720f;
        public float drag = 2.0f;
        public float life = 1.15f;
        public float baseAlpha = 0.85f;

        RectTransform rt;
        enum Kind { Dot, Spark, Streak }
        class P { public RectTransform t; public Image img; public Vector2 vel; public float age, life, spin, spinV; public Kind kind; }
        readonly List<P> ps = new List<P>();

        void OnEnable() { Fire(); }

        void Fire()
        {
            rt = (RectTransform)transform;
            if (glowSprite == null || palette == null || palette.Length == 0) return;

            foreach (var old in ps) if (old != null && old.t != null) Destroy(old.t.gameObject);
            ps.Clear();

            for (int i = 0; i < count; i++)
            {
                Kind kind = (i % 4 == 0) ? Kind.Streak : (i % 4 == 1 ? Kind.Spark : Kind.Dot);
                var go = new GameObject("burst", typeof(RectTransform));
                var prt = (RectTransform)go.transform;
                prt.SetParent(rt, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.sprite = (kind == Kind.Spark && sparkSprite != null) ? sparkSprite : glowSprite;

                float ang = (i / (float)count) * 6.2831853f + Random.Range(-0.3f, 0.3f);
                float sp = speed * Random.Range(0.45f, 1.2f);
                var p = new P
                {
                    t = prt, img = img, kind = kind,
                    vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * sp,
                    age = 0f, life = life * Random.Range(0.75f, 1.2f),
                    spin = ang * Mathf.Rad2Deg, spinV = kind == Kind.Spark ? Random.Range(-160f, 160f) : 0f,
                };
                prt.anchoredPosition = Vector2.zero;
                if (kind == Kind.Streak)
                {
                    prt.sizeDelta = new Vector2(Random.Range(52f, 84f), Random.Range(9f, 15f));
                    prt.localRotation = Quaternion.Euler(0f, 0f, p.spin);   // stretch along its direction
                }
                else prt.sizeDelta = Vector2.one * Random.Range(13f, 23f);

                var c = palette[Random.Range(0, palette.Length)];
                img.color = new Color(c.r, c.g, c.b, baseAlpha);
                ps.Add(p);
            }
            enabled = true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool any = false;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (p.age >= p.life) { if (p.img.enabled) p.img.enabled = false; continue; }
                any = true;
                p.age += dt;
                p.vel *= 1f / (1f + drag * dt);   // exponential drag = smooth deceleration
                var pos = p.t.anchoredPosition + p.vel * dt;
                p.t.anchoredPosition = pos;

                float k = Mathf.Clamp01(p.age / p.life);
                var col = p.img.color; col.a = baseAlpha * (1f - k * k); p.img.color = col;   // gradual fade-out

                if (p.kind == Kind.Streak)
                    p.t.localScale = new Vector3(Mathf.Lerp(1.15f, 0.25f, k), Mathf.Lerp(1f, 0.5f, k), 1f);
                else
                {
                    p.spin += p.spinV * dt;
                    if (p.kind == Kind.Spark) p.t.localRotation = Quaternion.Euler(0f, 0f, p.spin);
                    p.t.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, k);
                }
            }
            if (!any) enabled = false;
        }
    }
}
