using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // The board failing, in WORLD space — the counterpart to WinFx, and deliberately heavier.
    //
    // Three staggered shockwaves tear out from the board's centre, a red flash washes the screen,
    // debris and light streaks blow outward, and the board itself comes apart: every floor cell and
    // every puzzle piece is detached, thrown, spun, and dragged down under gravity while draining to
    // black. It is not a shake and it is not an overlay — the level you were playing is destroyed.
    //
    // Self-destructs when finished. Runs on unscaled time (gameplay is frozen during this).
    public class BoardBreakFx : MonoBehaviour
    {
        public Sprite glow, ring;
        public Color[] palette;
        public float sizeScale = 1f;
        public int order = 250;

        class E { public Transform t; public SpriteRenderer sr; public Vector3 vel; public float age, life, baseScl, spinV; public bool streak; }
        class Shard { public Transform t; public SpriteRenderer sr; public Vector3 vel; public float spinV; public Color from; public Vector3 baseScl; }

        readonly List<E> es = new List<E>();
        readonly List<Shard> shards = new List<Shard>();
        readonly List<SpriteRenderer> waves = new List<SpriteRenderer>();
        SpriteRenderer flash;
        float t;
        const float Total = 2.2f;

        static Color A(Color c, float a) { c.a = a; return c; }
        static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

        // Kick the whole thing off. `pieces` are the board's cells and puzzle objects; they get
        // DETACHED first so a flying meta-box can't drag its nested room along with it.
        public static BoardBreakFx Play(Camera cam, Vector3 epicentre, IEnumerable<Transform> pieces, Sprite glow, Sprite ring, Color[] palette)
        {
            if (cam == null || glow == null) return null;
            var go = new GameObject("BoardBreakFx");
            go.transform.position = epicentre;
            var fx = go.AddComponent<BoardBreakFx>();
            fx.glow = glow;
            fx.ring = ring;
            fx.palette = (palette != null && palette.Length > 0)
                ? palette
                : new[] { new Color(1f, 0.42f, 0.34f), new Color(1f, 0.66f, 0.33f), new Color(0.62f, 0.70f, 0.80f) };
            fx.sizeScale = Mathf.Clamp(cam.orthographicSize / 4f, 0.7f, 1.9f);
            fx.order = 250;
            if (pieces != null) fx.Adopt(pieces, epicentre);
            return fx;
        }

        void Adopt(IEnumerable<Transform> pieces, Vector3 epicentre)
        {
            foreach (var p in pieces)
            {
                if (p == null) continue;
                var sr = p.GetComponent<SpriteRenderer>();
                Vector3 d = p.position - epicentre;
                if (d.sqrMagnitude < 0.0001f) d = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f);
                d.z = 0f;
                d.Normalize();

                // detach so a thrown parent can't drag its children into nonsense
                p.SetParent(null, true);

                // closer to the epicentre = harder kick, so the blast reads as radiating outward
                float dist = Vector3.Distance(p.position, epicentre);
                float force = Mathf.Lerp(10.5f, 3f, Mathf.Clamp01(dist / (4f * sizeScale)));   // harder blast
                shards.Add(new Shard
                {
                    t = p,
                    sr = sr,
                    vel = d * force * Random.Range(0.8f, 1.4f) + new Vector3(0f, Random.Range(2f, 5f), 0f),
                    spinV = Random.Range(-520f, 520f),
                    from = sr != null ? sr.color : Color.white,
                    baseScl = p.localScale,   // captured after detach — shrink from here so full-size pieces don't linger huge
                });
            }
        }

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

            // full-board flash
            flash = NewSR(glow, A(new Color(1f, 0.35f, 0.3f), 0f), order);
            flash.transform.localScale = Vector3.one * 16f * sizeScale;

            // three shockwaves, staggered — one ring reads as a ripple, three reads as a rupture
            for (int i = 0; i < 3; i++)
            {
                var sr = NewSR(ring != null ? ring : glow, A(palette[i % palette.Length], 0f), order + 1 + i);
                sr.transform.localScale = Vector3.one * 0.25f * sizeScale;
                waves.Add(sr);
            }

            // Debris started at 0.22-0.55 units — up to 0.7x a whole board TILE, i.e. "particles"
            // the size of the things they were meant to be blowing apart. Halved once (0.11-0.27),
            // still too big; halved again. At 0.06-0.15 a fragment is ~1/6 of a tile: unmistakably
            // debris, and the board stays the biggest thing on screen.
            for (int i = 0; i < 48; i++)   // debris
            {
                float ang = (i / 48f) * 6.2831853f + Random.Range(-0.3f, 0.3f);
                float sc = Random.Range(0.06f, 0.15f) * sizeScale;
                var sr = NewSR(glow, palette[Random.Range(0, palette.Length)], order + 5);
                sr.transform.localScale = Vector3.one * sc;
                es.Add(new E { t = sr.transform, sr = sr, vel = Dir(ang) * Random.Range(4f, 9.5f) * sizeScale,
                    life = Random.Range(0.9f, 1.5f), baseScl = sc, spinV = Random.Range(-260f, 260f), streak = false });
            }
            // Streaks ran 1.4-2.6 units on a 7-unit board — one streak crossed 42% of it. Halved
            // once, then halved again: at 0.36-0.70 a streak spans ~10% of the board and reads as
            // a spark thrown off the break rather than a beam laid across it.
            for (int i = 0; i < 14; i++)   // light streaks
            {
                float ang = (i / 14f) * 6.2831853f + Random.Range(-0.3f, 0.3f);
                float len = Random.Range(0.36f, 0.70f) * sizeScale;
                var sr = NewSR(glow, palette[Random.Range(0, palette.Length)], order + 5);
                sr.transform.localScale = new Vector3(len, 0.055f * sizeScale, 1f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg);
                es.Add(new E { t = sr.transform, sr = sr, vel = Dir(ang) * Random.Range(7f, 12f) * sizeScale,
                    life = Random.Range(0.7f, 1.05f), baseScl = len, spinV = 0f, streak = true });
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;

            for (int i = 0; i < waves.Count; i++)
            {
                float delay = i * 0.09f;
                float k = Mathf.Clamp01((t - delay) / 0.75f);
                if (k <= 0f) continue;
                float e = 1f - (1f - k) * (1f - k);   // ease-out: fast rupture, slow dissipation
                waves[i].transform.localScale = Vector3.one * Mathf.Lerp(0.25f, 13f, e) * sizeScale;
                waves[i].color = A(waves[i].color, (1f - k) * 0.9f);
            }

            if (flash != null)
            {
                float f = Mathf.Clamp01(t / 0.55f);
                flash.color = A(new Color(1f, 0.32f, 0.28f),
                    f < 0.16f ? Mathf.Lerp(0f, 0.78f, f / 0.16f) : Mathf.Lerp(0.78f, 0f, (f - 0.16f) / 0.84f));   // harder flash
            }

            for (int i = 0; i < es.Count; i++)
            {
                var e = es[i];
                if (e.age >= e.life) { if (e.sr.enabled) e.sr.enabled = false; continue; }
                e.age += dt;
                e.vel *= 1f / (1f + 2.2f * dt);
                e.t.localPosition += e.vel * dt;
                float k = Mathf.Clamp01(e.age / e.life);
                e.sr.color = A(e.sr.color, 1f - k * k);
                if (e.streak)
                    e.t.localScale = new Vector3(e.baseScl * Mathf.Lerp(1.15f, 0.25f, k), 0.055f * sizeScale * Mathf.Lerp(1f, 0.4f, k), 1f);
                else { e.t.Rotate(0f, 0f, e.spinV * dt); e.t.localScale = Vector3.one * e.baseScl * Mathf.Lerp(1f, 0.3f, k); }
            }

            // the board itself: thrown, spun, pulled down, SHRINKING, draining to black. The shrink
            // is the fix for "the pieces are too huge" — a full-size tile/box flying across the screen
            // reads as a giant object, not debris, so each piece scales down to a fragment as it goes.
            float sk = Mathf.Clamp01(t / 1.5f);
            float shrink = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(t / 0.85f));   // small fast — no lingering giants
            for (int i = 0; i < shards.Count; i++)
            {
                var s = shards[i];
                if (s.t == null) continue;
                s.vel += new Vector3(0f, -12f * dt, 0f);        // gravity
                s.vel *= 1f / (1f + 0.7f * dt);                  // air drag
                s.t.position += s.vel * dt;
                s.t.Rotate(0f, 0f, s.spinV * dt);
                s.t.localScale = s.baseScl * shrink;
                if (s.sr != null)
                    s.sr.color = Color.Lerp(s.from, new Color(0.02f, 0.03f, 0.05f, 0f), sk * sk);
            }

            if (t >= Total) Destroy(gameObject);
        }
    }
}
