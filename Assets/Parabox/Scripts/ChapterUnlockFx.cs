using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Opening a new chapter — the biggest moment on the map, and deliberately staged like one.
    //
    // A normal level completion lights one node and one stretch of road. This does something the
    // player has never seen: the map itself moves. The camera pushes in on the region you just
    // finished, a wave of light runs the length of it and slams into the gate that has been
    // sitting there since level 1, the gate breaks open, a whole new region fades up beyond it,
    // and the camera travels through into territory that did not exist a moment ago.
    //
    // The gate is the point. It has been visible and shut the entire time you played chapter 1,
    // so breaking it means something — you can't celebrate removing a barrier the player never
    // knew was there.
    //
    // Unscaled time: the menu is static throughout.
    public class ChapterUnlockFx : MonoBehaviour
    {
        [Header("Break effect")]
        public Sprite burstGlow, burstSpark, ringSprite;

        [Header("Timing")]
        public float freeze = 0.30f;
        public float focusDur = 0.60f;
        public float waveStep = 0.045f;    // per node — this is the wave travelling
        public float shakeDur = 0.35f;
        public float breakDur = 0.55f;
        public float revealDur = 0.70f;
        public float travelDur = 0.85f;
        public float settleDur = 0.60f;
        public float zoom = 1.28f;         // how far the camera pushes in on a region

        // Where a region sits on the map, so the camera can frame it.
        public static Vector2 Focus(float baseY) => new Vector2(0f, baseY);

        public IEnumerator Play(
            RectTransform mapCam,
            IList<RectTransform> doneNodes, IList<Image> doneFills, Color waveColour,
            RectTransform gate, RectTransform gateL, RectTransform gateR, RectTransform gateLock,
            CanvasGroup newRegion, IList<Image> newRoad, Color roadLit, RectTransform firstNode, GameObject firstRing,
            float doneY, float newY)
        {
            // the new region must not be on screen before we open the gate
            if (newRegion != null) { newRegion.alpha = 0f; }
            if (firstRing != null) firstRing.SetActive(false);
            if (newRoad != null) foreach (var d in newRoad) if (d != null) d.color = new Color(roadLit.r, roadLit.g, roadLit.b, 0.12f);

            float t = 0f;
            while (t < freeze) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 1. push in on the chapter you just finished ---------------------------
            yield return MoveCam(mapCam, Focus(doneY), zoom, focusDur);

            // ---- 2. a wave of light runs the length of the region, toward the gate ------
            Sfx.Ding();
            for (int i = 0; i < (doneNodes != null ? doneNodes.Count : 0); i++)
            {
                if (doneFills != null && i < doneFills.Count && doneFills[i] != null)
                    StartCoroutine(Flash(doneFills[i], waveColour));
                if (doneNodes[i] != null) StartCoroutine(Punch(doneNodes[i], 0.18f, 0.26f));
                t = 0f;
                while (t < waveStep) { t += Time.unscaledDeltaTime; yield return null; }
            }

            // ---- 3. the wave hits the gate: it shakes, then gives ----------------------
            if (gate != null)
            {
                Vector2 home = gate.anchoredPosition;
                t = 0f;
                while (t < shakeDur)
                {
                    t += Time.unscaledDeltaTime;
                    float amp = 7f * (1f - t / shakeDur);
                    gate.anchoredPosition = home + new Vector2(Random.Range(-amp, amp), Random.Range(-amp, amp));
                    yield return null;
                }
                gate.anchoredPosition = home;
            }

            // ---- 4. it breaks: the lock bursts, the halves are thrown apart -------------
            // The break is the moment the whole sequence exists for. Sliding two bars apart is a
            // state change; debris and a shockwave leaving the gate is a barrier giving way.
            Sfx.Win();
            if (gate != null)
            {
                Shockwave(gate, waveColour, 5.5f, 0.7f);
                Burst(gate, waveColour, 40, 700f, 1.05f);
            }
            t = 0f;
            Vector2 lHome = gateL != null ? gateL.anchoredPosition : Vector2.zero;
            Vector2 rHome = gateR != null ? gateR.anchoredPosition : Vector2.zero;
            while (t < breakDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / breakDur);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                if (gateL != null)
                {
                    gateL.anchoredPosition = lHome + new Vector2(-150f * e, 0f);
                    gateL.localRotation = Quaternion.Euler(0f, 0f, -38f * e);
                    SetAlpha(gateL, 1f - e);
                }
                if (gateR != null)
                {
                    gateR.anchoredPosition = rHome + new Vector2(150f * e, 0f);
                    gateR.localRotation = Quaternion.Euler(0f, 0f, 38f * e);
                    SetAlpha(gateR, 1f - e);
                }
                if (gateLock != null)
                {
                    gateLock.localScale = Vector3.one * (1f + 1.4f * e);   // the lock blows outward
                    SetAlpha(gateLock, 1f - e);
                }
                yield return null;
            }
            if (gate != null) gate.gameObject.SetActive(false);

            // ---- 5. the new region fades up beyond the broken gate ----------------------
            t = 0f;
            while (t < revealDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / revealDur));
                if (newRegion != null) newRegion.alpha = k;
                yield return null;
            }
            if (newRegion != null) newRegion.alpha = 1f;

            // ---- 6. travel into it ------------------------------------------------------
            yield return MoveCam(mapCam, Focus(newY), zoom, travelDur);

            // ---- 7. its road lights, and the first level of the new area wakes ----------
            if (newRoad != null)
                for (int i = 0; i < newRoad.Count; i++)
                {
                    if (newRoad[i] != null) newRoad[i].color = roadLit;
                    t = 0f;
                    while (t < 0.05f) { t += Time.unscaledDeltaTime; yield return null; }
                }
            if (firstRing != null) firstRing.SetActive(true);
            if (firstNode != null)
            {
                Sfx.Ding();
                Shockwave(firstNode, roadLit, 3f, 0.55f);
                Burst(firstNode, roadLit, 24, 420f, 0.85f);
                yield return Punch(firstNode, 0.42f, 0.34f);
            }

            // ---- 8. pull back so the whole journey is visible again ---------------------
            yield return MoveCam(mapCam, Vector2.zero, 1f, settleDur);
        }

        // The map camera: the map lives under one rect, so scaling and offsetting it IS a camera.
        // To hold world point p at screen centre at zoom z, the rect sits at -p * z.
        IEnumerator MoveCam(RectTransform cam, Vector2 focus, float z, float dur)
        {
            if (cam == null) yield break;
            Vector2 fromPos = cam.anchoredPosition;
            float fromZ = cam.localScale.x;
            Vector2 toPos = -focus * z;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = k * k * (3f - 2f * k);   // smoothstep: no jerk at either end
                cam.anchoredPosition = Vector2.Lerp(fromPos, toPos, e);
                float s = Mathf.Lerp(fromZ, z, e);
                cam.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            cam.anchoredPosition = toPos;
            cam.localScale = new Vector3(z, z, 1f);
        }

        IEnumerator Punch(RectTransform rt, float dur, float amount)
        {
            var hs = rt.GetComponent<UIHoverScale>();
            if (hs != null) hs.suspended = true;      // or it lerps the punch away
            Vector3 b = rt.localScale;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                rt.localScale = b * (1f + amount * Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI));
                yield return null;
            }
            rt.localScale = b;
            if (hs != null) hs.suspended = false;
        }

        IEnumerator Flash(Image img, Color to)
        {
            Color from = img.color;
            float t = 0f;
            while (t < 0.34f)
            {
                t += Time.unscaledDeltaTime;
                img.color = Color.Lerp(from, to, Mathf.Sin(Mathf.Clamp01(t / 0.34f) * Mathf.PI));
                yield return null;
            }
            img.color = from;
        }

        static void SetAlpha(RectTransform rt, float a)
        {
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true))
            {
                var c = g.color; c.a = a; g.color = c;
            }
        }
    
        // A ring thrown off a point, expanding away and dissipating. Parented as a SIBLING so it
        // never inherits the shake or punch of the thing that threw it.
        void Shockwave(RectTransform from, Color c, float grow, float dur)
        {
            if (ringSprite == null || from.parent == null) return;
            var go = new GameObject("Shockwave", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(from.parent, false);
            rt.anchoredPosition = from.anchoredPosition;
            rt.sizeDelta = new Vector2(70f, 70f);
            var img = go.AddComponent<Image>();
            img.sprite = ringSprite; img.color = c; img.raycastTarget = false;
            StartCoroutine(RingOut(rt, img, grow, dur));
        }

        IEnumerator RingOut(RectTransform rt, Image img, float grow, float dur)
        {
            Color from = img.color;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - (1f - k) * (1f - k);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, grow, e);
                img.color = new Color(from.r, from.g, from.b, from.a * (1f - k));
                yield return null;
            }
            Destroy(rt.gameObject);
        }

        // UIBurst fires on enable and cleans itself up.
        void Burst(RectTransform at, Color c, int count, float speed, float life)
        {
            if (burstGlow == null || at.parent == null) return;
            var go = new GameObject("Burst", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(at.parent, false);
            rt.anchoredPosition = at.anchoredPosition;
            rt.sizeDelta = Vector2.one * 10f;
            var b = go.AddComponent<UIBurst>();
            b.glowSprite = burstGlow;
            b.sparkSprite = burstSpark;
            b.palette = new[] { c, Color.Lerp(c, Color.white, 0.5f), Color.white };
            b.count = count;
            b.speed = speed;
            b.life = life;
        }
    
        // ================================================= finishing the game
        // The victory lap. Not a bigger level-complete — a different thing entirely: the camera
        // starts tight on the final node and pulls all the way back while the route relights from
        // level 1 forward, so you watch your own journey redraw itself beneath you, chapter by
        // chapter, and end on the whole map at once. The screen only speaks after that.
        public IEnumerator PlayGameComplete(
            RectTransform mapCam, RectTransform finalNode, float finalY,
            IList<Image> allDots, IList<RectTransform> allNodes, IList<Image> allFills,
            IList<Color> chapterColour, int per,
            CanvasGroup banner, RectTransform badge)
        {
            if (banner != null) banner.alpha = 0f;

            // ---- 1. tight on the last node, where you just were -------------------------
            if (mapCam != null)
            {
                mapCam.localScale = new Vector3(2.1f, 2.1f, 1f);
                mapCam.anchoredPosition = -(finalNode != null ? finalNode.anchoredPosition : Vector2.zero) * 2.1f;
            }
            float t = 0f;
            while (t < 0.4f) { t += Time.unscaledDeltaTime; yield return null; }

            // The last node announces itself — a ring and a burst, because this is the arrival
            // point of thirty levels. What we do NOT do is scatter confetti: every particle here
            // leaves from the one node that matters, so the effect points at something.
            if (finalNode != null)
            {
                Sfx.Win();
                Color fc = chapterColour.Count > 0 ? chapterColour[chapterColour.Count - 1] : Color.white;
                Shockwave(finalNode, fc, 5f, 0.85f);
                Burst(finalNode, fc, 30, 560f, 0.95f);
                StartCoroutine(Punch(finalNode, 0.5f, 0.34f));
            }
            t = 0f;
            while (t < 0.7f) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 2. pull back while the whole route relights, 1 -> 30 -------------------
            // The pull-back and the trace run TOGETHER: the journey redraws at exactly the rate
            // the map opens up, so by the time you can see all thirty levels the line has just
            // finished arriving at the one you're sitting on. This is the whole ending — one long
            // unhurried gesture, not an effect.
            StartCoroutine(MoveCam(mapCam, Vector2.zero, 1f, 3.4f));

            int n = allNodes != null ? allNodes.Count : 0;
            for (int i = 0; i < n; i++)
            {
                int ch = Mathf.Clamp(i / Mathf.Max(1, per), 0, Mathf.Max(0, chapterColour.Count - 1));
                Color c = chapterColour.Count > 0 ? chapterColour[ch] : Color.white;

                // Each chapter is marked as the wave enters it: a ring off its first node, so the
                // three chapters read as three distinct legs being honoured rather than thirty
                // undifferentiated nodes lighting up.
                if (i % Mathf.Max(1, per) == 0 && allNodes[i] != null)
                {
                    Shockwave(allNodes[i], c, 3.6f, 0.8f);
                    Sfx.Ding();
                }

                if (allFills != null && i < allFills.Count && allFills[i] != null)
                    StartCoroutine(Flash(allFills[i], Color.Lerp(c, Color.white, 0.5f)));
                if (allNodes[i] != null) StartCoroutine(Punch(allNodes[i], 0.3f, 0.26f));

                // the road behind this node lights with it, so the line is continuous
                if (allDots != null)
                    for (int d = 0; d < 7; d++)
                    {
                        int idx = i * 7 + d;
                        if (idx < allDots.Count && allDots[idx] != null)
                            allDots[idx].color = new Color(c.r, c.g, c.b, 0.9f);
                    }

                t = 0f;
                while (t < 0.1f) { t += Time.unscaledDeltaTime; yield return null; }   // 30 x 0.1 = 3.0s, matched to the pull-back
            }

            t = 0f;
            while (t < 0.8f) { t += Time.unscaledDeltaTime; yield return null; }   // let it rest before the words

            // ---- 3. the artifact ---------------------------------------------------------
            // The trophy lands FIRST and alone, with weight — it overshoots and settles like
            // something set down. The words follow it. An award that fades up with its own caption
            // is a label; an award that arrives and is then named is a moment.
            if (badge != null) badge.localScale = Vector3.zero;
            if (banner != null)
            {
                float d = 0.85f;
                t = 0f;
                while (t < d)
                {
                    t += Time.unscaledDeltaTime;
                    banner.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (d * 0.6f)));
                    yield return null;
                }
                banner.alpha = 1f;
                banner.interactable = true;
                banner.blocksRaycasts = true;
            }

            if (badge != null)
            {
                Sfx.Win();
                Shockwave(badge, chapterColour.Count > 0 ? chapterColour[chapterColour.Count - 1] : Color.white, 4.5f, 0.9f);
                float d = 0.75f;
                t = 0f;
                while (t < d)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / d);
                    // overshoot-and-settle: the motion that gives an object mass
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    float pk = k - 1f;
                    float e = 1f + c3 * pk * pk * pk + c1 * pk * pk;
                    badge.localScale = Vector3.one * e;
                    yield return null;
                }
                badge.localScale = Vector3.one;
            }
        }
    }
}
