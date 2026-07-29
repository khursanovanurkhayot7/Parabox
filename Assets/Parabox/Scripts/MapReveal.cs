using System.Collections;
using UnityEngine;

namespace Parabox
{
    // Reveals the progression map one region at a time when the level select opens.
    //
    // The whole map appearing at once reads as a screen. Revealing it chapter by chapter, in
    // travel order, reads as a route being traced — you watch the journey lay itself out from
    // the starting area down to the final region, which is the whole point of a map.
    //
    // Unscaled time: this runs while the menu sits still.
    public class MapReveal : MonoBehaviour
    {
        public CanvasGroup[] regions;      // one per chapter, in travel order
        public RectTransform[] regionRTs;  // same order — each drifts up into place
        public float stagger = 0.13f;      // gap between one region arriving and the next
        public float dur = 0.42f;
        public float rise = 26f;           // how far each region travels as it lands

        Vector2[] home;
        Coroutine running;

        void Awake()
        {
            if (regionRTs == null) return;
            home = new Vector2[regionRTs.Length];
            for (int i = 0; i < regionRTs.Length; i++)
                if (regionRTs[i] != null) home[i] = regionRTs[i].anchoredPosition;
        }

        void OnEnable()
        {
            if (regions == null || regions.Length == 0) return;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            for (int i = 0; i < regions.Length; i++)
                if (regions[i] != null) regions[i].alpha = 0f;

            for (int i = 0; i < regions.Length; i++)
            {
                StartCoroutine(Land(i));
                float t = 0f;
                while (t < stagger) { t += Time.unscaledDeltaTime; yield return null; }
            }
            running = null;
        }

        IEnumerator Land(int i)
        {
            var g = regions[i];
            var rt = (regionRTs != null && i < regionRTs.Length) ? regionRTs[i] : null;
            Vector2 h = (home != null && i < home.Length) ? home[i] : Vector2.zero;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - Mathf.Pow(1f - k, 3f);   // ease-out: arrives quickly, settles softly
                if (g != null) g.alpha = e;
                if (rt != null) rt.anchoredPosition = new Vector2(h.x, h.y - rise * (1f - e));
                yield return null;
            }
            if (g != null) g.alpha = 1f;
            if (rt != null) rt.anchoredPosition = h;
        }
    }
}
