using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Premium level-complete celebration in WORLD space, radiating from the board centre:
    // an expanding glowing shockwave, a radial burst of glowing dots, light streaks, and a soft
    // board-wide flash. Self-contained — destroys itself when finished. Uses unscaled time.
    public class WinFx : MonoBehaviour
    {
        public Sprite glow, ring;
        public Color[] palette;
        public float sizeScale = 1f;
        public int order = 250;

        class E { public Transform t; public SpriteRenderer sr; public Vector3 vel; public float age, life, baseScl, spinV; public bool streak; }
        readonly List<E> es = new List<E>();
        SpriteRenderer shock, flash;
        float t;
        const float Total = 1.4f;

        static Color A(Color c, float a) { c.a = a; return c; }
        static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

        SpriteRenderer NewSR(Sprite s, Color c, int ord)
        {
            var go = new GameObject("fx");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s; sr.color = c; sr.sortingOrder = ord;
            return sr;
        }

        void Start()
        {
            if (glow == null || palette == null || palette.Length == 0) { Destroy(gameObject); return; }

            flash = NewSR(glow, A(Color.white, 0f), order);
            flash.transform.localScale = Vector3.one * 11f * sizeScale;

            shock = NewSR(ring != null ? ring : glow, A(palette[0], 0.85f), order + 1);
            shock.transform.localScale = Vector3.one * 0.3f * sizeScale;

            for (int i = 0; i < 36; i++)   // glowing dots
            {
                float ang = (i / 36f) * 6.2831853f + Random.Range(-0.25f, 0.25f);
                float sc = Random.Range(0.26f, 0.5f) * sizeScale;
                var sr = NewSR(glow, palette[Random.Range(0, palette.Length)], order + 2);
                sr.transform.localScale = Vector3.one * sc;
                es.Add(new E { t = sr.transform, sr = sr, vel = Dir(ang) * Random.Range(3.4f, 7.2f) * sizeScale,
                    life = Random.Range(0.8f, 1.2f), baseScl = sc, spinV = Random.Range(-220f, 220f), streak = false });
            }
            for (int i = 0; i < 10; i++)   // light streaks
            {
                float ang = (i / 10f) * 6.2831853f + Random.Range(-0.3f, 0.3f);
                float len = Random.Range(1.1f, 1.9f) * sizeScale;
                var sr = NewSR(glow, palette[Random.Range(0, palette.Length)], order + 2);
                sr.transform.localScale = new Vector3(len, 0.16f * sizeScale, 1f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg);
                es.Add(new E { t = sr.transform, sr = sr, vel = Dir(ang) * Random.Range(6f, 9.5f) * sizeScale,
                    life = Random.Range(0.65f, 0.95f), baseScl = len, spinV = 0f, streak = true });
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;

            if (shock != null)
            {
                float sf = Mathf.Clamp01(t / 0.7f);
                shock.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 9f, 1f - (1f - sf) * (1f - sf)) * sizeScale;
                shock.color = A(shock.color, (1f - sf) * 0.85f);
            }
            if (flash != null)
            {
                float ff = Mathf.Clamp01(t / 0.5f);
                flash.color = A(Color.white, ff < 0.22f ? Mathf.Lerp(0f, 0.45f, ff / 0.22f) : Mathf.Lerp(0.45f, 0f, (ff - 0.22f) / 0.78f));
            }

            for (int i = 0; i < es.Count; i++)
            {
                var e = es[i];
                if (e.age >= e.life) { if (e.sr.enabled) e.sr.enabled = false; continue; }
                e.age += dt;
                e.vel *= 1f / (1f + 2.4f * dt);   // smooth drag
                e.t.localPosition += e.vel * dt;
                float k = Mathf.Clamp01(e.age / e.life);
                var c = e.sr.color; c.a = 1f - k * k; e.sr.color = c;
                if (e.streak)
                    e.t.localScale = new Vector3(e.baseScl * Mathf.Lerp(1.1f, 0.3f, k), 0.16f * sizeScale * Mathf.Lerp(1f, 0.5f, k), 1f);
                else { e.t.Rotate(0f, 0f, e.spinV * dt); e.t.localScale = Vector3.one * e.baseScl * Mathf.Lerp(1f, 0.35f, k); }
            }

            if (t >= Total) Destroy(gameObject);
        }
    }
}
