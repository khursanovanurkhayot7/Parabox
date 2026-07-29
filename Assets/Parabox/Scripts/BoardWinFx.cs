using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // The board itself reacting to a win — the constructive counterpart to BoardBreakFx.
    //
    // Fx.Celebrate already throws a shockwave and particles from the board centre, but the BOARD
    // sat there inert underneath it. Here an energy pulse actually travels through the floor: each
    // cell lights and swells as the wavefront reaches it, then settles. Because the wave is driven
    // by distance from the centre, it reads as one force crossing the board rather than 40 tiles
    // blinking.
    //
    // The solved pieces then punch up on top of that — a box sitting on its goal is the thing the
    // player just achieved, so it gets the last beat.
    //
    // Self-destructs when finished. Unscaled time: gameplay is frozen through this.
    public class BoardWinFx : MonoBehaviour
    {
        class Cell { public Transform t; public SpriteRenderer sr; public float dist; public Color from; public Vector3 baseScale; public float settled; }
        class Piece { public Transform t; public float delay; public Vector3 baseScale; }

        readonly List<Cell> cells = new List<Cell>();
        readonly List<Piece> pieces = new List<Piece>();

        public Color pulse = Color.white;
        public float speed = 9f;        // world units per second the wavefront travels
        public float width = 1.6f;      // how wide the crest is — narrow reads as a snap, wide as a swell
        public float lift = 0.22f;      // how much a cell swells at the crest

        // How many wavefronts cross the board, and how far apart. More than one is how the finale
        // escalates WITHOUT spawning a second instance: two instances both adopt the same cells,
        // and the later one captures the earlier one's mid-pulse scale as its own baseline. That
        // compounds (1.22x -> 1.49x -> 1.82x) and, because each restores to its own corrupted
        // capture on destroy, leaves the board permanently swollen and half-lit. One instance owns
        // the cells; the waves live inside it.
        public int waves = 1;
        public float waveGap = 0.4f;

        // The board keeps what happened to it. Normally every cell is handed back exactly as it
        // was found; set this and the cells instead SETTLE into a transformed colour as the last
        // wave leaves. That is what "the board transforms after completion" means structurally —
        // the board is permanently changed by what you did, rather than flashing and reverting.
        public bool syncPieces;
        // Named transformBoard, not transform: a bare `transform` field hides Component.transform
        // (CS0108) and is a footgun — anyone reading boardWinFx.transform would get this bool instead
        // of the Transform.
        public bool transformBoard;
        public Color transformTo = Color.white;
        public float transformAmount = 0.5f;

        float t, maxDist;
        const float Tail = 0.9f;        // linger after the front leaves the board, so it settles

        public static BoardWinFx Play(Vector3 epicentre, IEnumerable<Transform> floorCells,
                                      IEnumerable<Transform> solvedPieces, Color pulseColour)
            => Play(epicentre, floorCells, solvedPieces, pulseColour, 1);

        public static BoardWinFx Play(Vector3 epicentre, IEnumerable<Transform> floorCells,
                                      IEnumerable<Transform> solvedPieces, Color pulseColour, int waveCount)
        {
            var go = new GameObject("BoardWinFx");
            go.transform.position = epicentre;
            var fx = go.AddComponent<BoardWinFx>();
            fx.pulse = pulseColour;
            fx.waves = Mathf.Max(1, waveCount);
            fx.Adopt(epicentre, floorCells, solvedPieces);
            return fx;
        }

        void Adopt(Vector3 epicentre, IEnumerable<Transform> floorCells, IEnumerable<Transform> solvedPieces)
        {
            if (floorCells != null)
                foreach (var c in floorCells)
                {
                    if (c == null) continue;
                    var sr = c.GetComponent<SpriteRenderer>();
                    if (sr == null) continue;
                    float d = Vector3.Distance(c.position, epicentre);
                    maxDist = Mathf.Max(maxDist, d);
                    cells.Add(new Cell { t = c, sr = sr, dist = d, from = sr.color, baseScale = c.localScale });
                }

            if (solvedPieces != null)
                foreach (var p in solvedPieces)
                {
                    if (p == null) continue;
                    // each solved piece fires as the wave reaches IT — the board tells you what you did
                    float d = Vector3.Distance(p.position, epicentre);
                    pieces.Add(new Piece { t = p, delay = d / Mathf.Max(0.01f, speed), baseScale = p.localScale });
                }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            float front = t * speed;

            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.t == null || c.sr == null) continue;
                // strongest of however many fronts are crossing — never additive, so N waves can
                // never push a cell past a single wave's peak
                float k = 0f;
                for (int w = 0; w < waves; w++)
                {
                    float f = (t - w * waveGap) * speed;
                    if (f < 0f) continue;
                    float kw = 1f - Mathf.Clamp01(Mathf.Abs(f - c.dist) / width);
                    if (kw > k) k = kw;
                }
                k = k * k * (3f - 2f * k);                       // smoothstep — no hard edge on the front

                // once a front has passed a cell, that cell stays changed — so the board converts
                // behind the wave instead of rippling back to how it was. THAT is the chain
                // reaction: you can see how far the transformation has spread at any instant.
                Color baseCol = c.from;
                if (transformBoard)
                {
                    float f0 = t * speed;                        // the leading front
                    if (f0 > c.dist) c.settled = Mathf.Min(1f, c.settled + Time.unscaledDeltaTime * 2.2f);
                    baseCol = Color.Lerp(c.from, transformTo, c.settled * transformAmount);
                }
                c.sr.color = Color.Lerp(baseCol, pulse, k * 0.85f);
                c.t.localScale = c.baseScale * (1f + lift * k);
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (p.t == null) continue;
                // syncPieces: every solved object reacts on the SAME beat instead of each waiting
                // for the front to reach it. Sequential reads as a ripple; together reads as the
                // whole board answering at once.
                float a = t - (syncPieces ? 0f : p.delay);
                if (a < 0f || a > 0.5f) continue;
                // a single punch up and back — the accomplishment, not a wobble
                float k = Mathf.Sin(Mathf.Clamp01(a / 0.5f) * Mathf.PI);
                p.t.localScale = p.baseScale * (1f + 0.28f * k);
            }

            float lastFront = (t - (waves - 1) * waveGap) * speed;
            if (lastFront > maxDist + width + Tail * speed)
            {
                // hand everything back as we found it — unless the board was transformed, in
                // which case the change is the point and it stays
                foreach (var c in cells)
                    if (c.t != null && c.sr != null)
                    {
                        c.sr.color = transformBoard ? Color.Lerp(c.from, transformTo, transformAmount) : c.from;
                        c.t.localScale = c.baseScale;
                    }
                foreach (var p in pieces)
                    if (p.t != null) p.t.localScale = p.baseScale;
                Destroy(gameObject);
            }
        }
    }
}
