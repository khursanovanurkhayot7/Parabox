using System.Collections;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // The UI half of the lose sequence. BoardBreakFx does the destruction in world space; this
    // times the screen around it.
    //
    // Order matters more than any single effect. The jolt and flash land on a still screen; then
    // the UI gets out of the way entirely for ~a second while the board is torn apart, because
    // dimming over the top of the blast would waste it; only once the wreckage is falling does
    // the verdict arrive; the options come last, one at a time, so the moment isn't competing
    // with a button.
    //
    // There is no window and no card. The message sits on the wreckage.
    //
    // Everything runs on unscaled time — the sequence freezes gameplay by design and still has
    // to animate while frozen.
    public class LoseFx : MonoBehaviour
    {
        [Header("Layers")]
        public CanvasGroup dim;            // full-screen darkening
        public Image pulse;                // full-screen flash — the impact
        public CanvasGroup titleGroup;
        public RectTransform titleRT;
        public Text titleText;
        public Text subText;
        public GameObject burst;           // UIBurst — fires when enabled

        [Header("Legacy options (hidden at runtime; keyboard controls remain)")]
        public CanvasGroup restartGroup;
        public RectTransform restartRT;
        public CanvasGroup levelsGroup;
        public RectTransform levelsRT;

        [Header("Leaderboard")]
        public LossLeaderboard leaderboard;
        public Color leaderboardAccent = new Color(0.27f, 0.88f, 0.85f, 1f);
        public Color leaderboardBacking = new Color(0.03f, 0.12f, 0.19f, 1f);

        [Header("World")]
        public CameraFollow shakeTarget;   // the board jolts via the camera

        [Header("Timing")]
        public float freeze = 0.20f;       // stunned beat before anything moves
        public float breakHold = 0.95f;    // let the board's destruction actually play out before dimming over it
        public float dimDur = 0.50f;
        public float titleDur = 0.42f;
        public float dimTo = 0.72f;        // not full black — the debris stays readable through it

        Coroutine running;
        Action afterLeaderboardDwell;

        void Awake()
        {
            BuildLeaderboard();
            // Keep the verdict/explanation intact in the left column. The old action buttons are
            // intentionally absent from this screen; R and Escape still work through GameManager.
            if (titleRT != null) titleRT.anchoredPosition += new Vector2(-300f, 0f);
            if (restartRT != null) restartRT.gameObject.SetActive(false);
            if (levelsRT != null) levelsRT.gameObject.SetActive(false);
        }

        public void SetLeaderboardTheme(Color accent, Color backing)
        {
            leaderboardAccent = accent;
            leaderboardBacking = backing;
            if (leaderboard != null) leaderboard.ApplyTheme(accent, backing);
        }

        void BuildLeaderboard()
        {
            if (leaderboard != null) return;
            Font font = titleText != null ? titleText.font : null;
            Sprite panelSprite = null;
            if (restartRT != null)
            {
                var button = restartRT.GetComponentInChildren<Button>(true);
                if (button != null && button.image != null) panelSprite = button.image.sprite;
            }
            leaderboard = LossLeaderboard.Create(transform, font, panelSprite,
                leaderboardAccent, leaderboardBacking);
        }

        public void Play(string title, string sub, Action afterFiveSeconds = null)
        {
            gameObject.SetActive(true);
            if (titleText != null) titleText.text = title;
            if (subText != null) subText.text = sub;
            afterLeaderboardDwell = afterFiveSeconds;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run());
        }

        void Prime()
        {
            if (dim != null) { dim.alpha = 0f; dim.blocksRaycasts = true; }
            if (pulse != null) pulse.color = new Color(pulse.color.r, pulse.color.g, pulse.color.b, 0f);
            if (titleGroup != null) titleGroup.alpha = 0f;
            if (leaderboard != null && leaderboard.Group != null)
            {
                leaderboard.Group.alpha = 0f;
                leaderboard.Group.interactable = false;
                leaderboard.Group.blocksRaycasts = false;
                leaderboard.Rect.localScale = Vector3.one * 0.94f;
            }
            foreach (var g in new[] { restartGroup, levelsGroup })
                if (g != null) { g.alpha = 0f; g.interactable = false; g.blocksRaycasts = false; }
            if (burst != null) burst.SetActive(false);
        }

        IEnumerator Run()
        {
            Prime();

            // ---- 1. impact: jolt + flash, on an otherwise still screen ----------------------
            // Shake(amplitude, decay): amplitude is the world-unit jolt, decay how fast it dies.
            // Was 0.42/4.5 — nearly doubled the amplitude for a heavy jolt, same decay so it snaps back.
            if (shakeTarget != null) shakeTarget.Shake(0.78f, 4.5f);
            StartCoroutine(Pulse());

            float t = 0f;
            while (t < freeze) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 2. hold: the board is being torn apart in world space right now ------------
            // Nothing from the UI happens here. Dimming over the top of the blast would waste it.
            t = 0f;
            while (t < breakHold) { t += Time.unscaledDeltaTime; yield return null; }

            // ---- 3. the wreckage drains --------------------------------------------------
            t = 0f;
            while (t < dimDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dimDur));
                if (dim != null) dim.alpha = k * dimTo;
                yield return null;
            }
            if (dim != null) dim.alpha = dimTo;

            // ---- 4. the verdict, with the particle burst on the beat -----------------------
            if (burst != null) burst.SetActive(true);
            t = 0f;
            while (t < titleDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / titleDur);
                float e = EaseOutBack(k);
                if (titleGroup != null) titleGroup.alpha = Mathf.Clamp01(k * 1.8f);
                if (titleRT != null)
                {
                    titleRT.localScale = Vector3.one * Mathf.Lerp(0.78f, 1f, e);
                    titleRT.anchoredPosition = new Vector2(titleRT.anchoredPosition.x, Mathf.Lerp(72f, 40f, e));
                }
                yield return null;
            }
            if (titleGroup != null) titleGroup.alpha = 1f;
            if (titleRT != null)
            {
                titleRT.localScale = Vector3.one;
                titleRT.anchoredPosition = new Vector2(titleRT.anchoredPosition.x, 40f);
            }

            // ---- 5. the top ten settles beside the verdict -------------------------------
            yield return RevealLeaderboard();

            // The result remains unobstructed for five full seconds before Luxodd owns the
            // screen with its transaction UI. Unscaled time keeps this reliable while gameplay
            // is in its terminal/frozen state.
            t = 0f;
            while (t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            Action callback = afterLeaderboardDwell;
            afterLeaderboardDwell = null;
            callback?.Invoke();

            running = null;
        }

        IEnumerator RevealLeaderboard()
        {
            if (leaderboard == null || leaderboard.Group == null) yield break;
            const float duration = 0.42f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                leaderboard.Group.alpha = e;
                leaderboard.Rect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
                yield return null;
            }
            leaderboard.Group.alpha = 1f;
            leaderboard.Rect.localScale = Vector3.one;
        }

        // Two quick flashes — one hard, one soft. A single flash reads as a glitch; two reads
        // as a deliberate beat.
        IEnumerator Pulse()
        {
            if (pulse == null) yield break;
            for (int i = 0; i < 2; i++)
            {
                float peak = i == 0 ? 0.60f : 0.28f;   // brighter impact flash
                float dur = i == 0 ? 0.20f : 0.26f;
                float t = 0f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    float a = Mathf.Sin(k * Mathf.PI) * peak;   // rise and fall in one stroke
                    var c = pulse.color; c.a = a; pulse.color = c;
                    yield return null;
                }
            }
            var f = pulse.color; f.a = 0f; pulse.color = f;
        }


        // overshoot-and-settle — the motion that gives a title weight
        static float EaseOutBack(float k)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float p = k - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
