using UnityEngine;

namespace Parabox
{
    // Idle life for the menu's live board.
    //
    // The camera already drifts and breathes, and the player mascot blinks — but the boxes sat
    // perfectly frozen, which is what reads as "lifeless": a still object next to a moving one
    // looks broken rather than calm. This floats them, slowly, each on its own phase so the board
    // never pulses as one block. A light also crawls across the board on a long cycle, so the
    // lighting is never quite the same twice.
    //
    // Two things make this safe:
    //
    //  * It runs in LateUpdate and OFFSETS whatever EntityView wrote in Update, rather than owning
    //    the position. EntityView stays the source of truth; nothing accumulates.
    //  * `weight` scales everything to nothing. The dive drives it to 0 before the hand-off, so the
    //    last menu frame has every piece sitting EXACTLY on its cell — which is the whole reason
    //    the menu→game transition is seamless. Idle motion that survived into the load would show
    //    up as every box twitching at the moment the game starts.
    public class MenuAmbience : MonoBehaviour
    {
        public Transform[] pieces;          // boxes on the menu board (the player has its own blink)
        public SpriteRenderer sweep;        // a soft band of light crawling across the board
        public float bobAmp = 0.028f;       // world units — deliberately below "did that move?"
        public float bobSpeed = 0.85f;
        public float sweepPeriod = 16f;     // one crossing every 16s: present, never rhythmic
        public float sweepSpan = 9f;
        public float sweepAlpha = 0.07f;

        [Range(0f, 1f)] public float weight = 1f;   // 0 = perfectly settled (the dive drives this)

        void LateUpdate()
        {
            float t = Time.unscaledTime;

            if (pieces != null)
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i] == null) continue;
                    // own phase per piece, so they drift independently instead of breathing in unison
                    float y = Mathf.Sin(t * bobSpeed + i * 1.7f) * bobAmp * weight;
                    pieces[i].localPosition += new Vector3(0f, y, 0f);
                }

            if (sweep != null)
            {
                float k = Mathf.Repeat(t / Mathf.Max(0.01f, sweepPeriod), 1f);
                var p = sweep.transform.localPosition;
                sweep.transform.localPosition = new Vector3(Mathf.Lerp(-sweepSpan, sweepSpan, k), p.y, p.z);
                // fade in and out at the edges of the crossing — a band that pops in is a flash
                float edge = Mathf.Sin(k * Mathf.PI);
                var c = sweep.color;
                c.a = sweepAlpha * edge * edge * weight;
                sweep.color = c;
            }
        }
    }
}
