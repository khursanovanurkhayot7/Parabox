using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // The reward for finishing a level, played on the progression map.
    //
    // A level ends and you arrive here. Instead of the map simply being in its new state — which
    // shows you nothing, because you never saw the old one — it plays what changed: the node you
    // just cleared lands its completion, the road onward lights up dot by dot, and the next level
    // wakes up. You watch the route grow.
    //
    // Driven by MainMenuUI, which knows which level was beaten (PlayerPrefs "Parabox.JustBeat").
    // Unscaled time — the menu is static.
    public class MapProgressFx : MonoBehaviour
    {
        [Header("Node completion effect")]
        public Sprite burstGlow, burstSpark, ringSprite;

        [Header("Timing")]
        public float holdBefore = 0.35f;   // let the map settle before anything moves
        public float nodePunch = 0.42f;
        public float dotStep = 0.055f;     // per dot — this is the "travelling" feeling
        public float unlockDur = 0.5f;

        public IEnumerator Play(RectTransform beatenNode, GameObject beatenCheck,
                                IList<Image> road, Color roadLit,
                                RectTransform nextNode, GameObject nextRing,
                                Action nodeCompleted = null,
                                Action<float> roadProgress = null,
                                Action nextActivated = null)
        {
            float t = 0f;
            while (t < holdBefore) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 1. the node you just cleared lands ------------------------------------
            if (beatenCheck != null) beatenCheck.SetActive(true);
            nodeCompleted?.Invoke();
            Sfx.Ding();
            // the moment itself: a ring thrown off the node and a burst of light. Without this the
            // node merely got bigger — a scale-up is a state change, not an achievement.
            if (beatenNode != null)
            {
                SpawnRing(beatenNode, roadLit);
                SpawnBurst(beatenNode, roadLit);
            }
            if (beatenNode != null)
            {
                // take the scale off UIHoverScale for the duration, or it lerps our punch away
                var hs = beatenNode.GetComponent<UIHoverScale>();
                if (hs != null) hs.suspended = true;
                Vector3 b = beatenNode.localScale;
                t = 0f;
                while (t < nodePunch)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / nodePunch);
                    // overshoot and settle — the same motion the win title uses, so the two read as one event
                    beatenNode.localScale = b * (1f + 0.30f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.35f));
                    yield return null;
                }
                beatenNode.localScale = b;
                if (hs != null) hs.suspended = false;
            }

            // ---- 2. the road ahead lights, one dot at a time ---------------------------
            // Sequential, not all at once: the eye follows the light along the route and lands
            // on the next level. That travel IS the progression.
            if (road != null)
                for (int i = 0; i < road.Count; i++)
                {
                    if (road[i] != null) road[i].color = roadLit;
                    roadProgress?.Invoke((i + 1f) / Mathf.Max(1, road.Count));
                    t = 0f;
                    while (t < dotStep) { t += Time.unscaledDeltaTime; yield return null; }
                }
            if (road == null || road.Count == 0) roadProgress?.Invoke(1f);

            // ---- 3. the next level wakes up -------------------------------------------
            if (nextRing != null) nextRing.SetActive(true);
            nextActivated?.Invoke();
            if (nextNode != null)
            {
                Sfx.Ding();
                var hs2 = nextNode.GetComponent<UIHoverScale>();
                if (hs2 != null) hs2.suspended = true;
                Vector3 b = nextNode.localScale;
                t = 0f;
                while (t < unlockDur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / unlockDur);
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    nextNode.localScale = b * Mathf.Lerp(0.72f, 1f, e) * (1f + 0.16f * Mathf.Sin(k * Mathf.PI));
                    yield return null;
                }
                nextNode.localScale = b;
                if (hs2 != null) hs2.suspended = false;
            }
        }
    
        // A ring thrown off the node — the shape of something landing.
        void SpawnRing(RectTransform node, Color c)
        {
            if (ringSprite == null || node.parent == null) return;
            var go = new GameObject("CompleteRing", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            // sibling of the node, NOT a child: a child would inherit the node's punch and the
            // ring would pulse along with it instead of expanding cleanly away
            rt.SetParent(node.parent, false);
            rt.anchoredPosition = node.anchoredPosition;
            rt.sizeDelta = node.sizeDelta;
            var img = go.AddComponent<Image>();
            img.sprite = ringSprite; img.color = c; img.raycastTarget = false;
            StartCoroutine(RingOut(rt, img));
        }

        IEnumerator RingOut(RectTransform rt, Image img)
        {
            Color from = img.color;
            float t = 0f;
            const float dur = 0.55f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - k) * (1f - k);          // ease-out: leaves fast, dissipates slow
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 2.6f, e);
                img.color = new Color(from.r, from.g, from.b, from.a * (1f - k));
                yield return null;
            }
            Destroy(rt.gameObject);
        }

        // Light thrown outward. UIBurst fires on enable and cleans up after itself.
        void SpawnBurst(RectTransform node, Color c)
        {
            if (burstGlow == null || node.parent == null) return;
            var go = new GameObject("CompleteBurst", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(node.parent, false);
            rt.anchoredPosition = node.anchoredPosition;
            rt.sizeDelta = Vector2.one * 10f;
            var b = go.AddComponent<UIBurst>();
            b.glowSprite = burstGlow;
            b.sparkSprite = burstSpark;
            b.palette = new[] { c, Color.Lerp(c, Color.white, 0.5f), Color.white };
            b.count = 22;      // enough to read as a burst, few enough to stay clean on a map
            b.speed = 380f;
            b.life = 0.85f;
        }
    }
}
