using UnityEngine;

namespace Parabox
{
    // On a SEAMLESS menu-entry, the board arrives first (matching the menu's last frame); this then
    // elegantly reveals the HUD a beat later — a soft fade plus the control bar sliding up — so the
    // controls/labels don't hard-pop. On a normal entry (restart / next level) the ScreenFade handles
    // the transition, so this does nothing.
    public class HudReveal : MonoBehaviour
    {
        public CanvasGroup group;    // faded 0 -> 1
        public RectTransform slide;  // optional: eases up from an offset (the control bar)
        public Vector2 fromOffset = new Vector2(0f, -55f);
        public float delay = 0.12f;
        public float duration = 0.45f;

        float t;
        Vector2 slideRest;
        bool _captured;

        // Capture the bar's resting position before anything can offset it, so StandDown() can put it
        // back regardless of whether Start() (which applies the offset) has run yet — Start ordering
        // between this and GameManager is undefined, and the level-1 cinematic stands this down.
        void Awake()
        {
            if (slide != null) { slideRest = slide.anchoredPosition; _captured = true; }
        }

        void Start()
        {
            if (PlayerPrefs.GetInt("Parabox.Seamless", 0) != 1) { enabled = false; return; }   // only on the dive
            if (group != null) group.alpha = 0f;
            if (slide != null) slide.anchoredPosition = slideRest + fromOffset;
            t = -delay;
        }

        // Step aside for the level-1 cinematic, which owns the HUD for its duration. Undo any offset
        // this applied (or would have) so the control bar isn't left shifted, then disable — the
        // cinematic fades the HUD in itself on the way into gameplay.
        public void StandDown()
        {
            if (_captured && slide != null) slide.anchoredPosition = slideRest;
            enabled = false;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
            float e = 1f - Mathf.Pow(1f - k, 3f);   // ease-out cubic
            if (group != null) group.alpha = e;
            if (slide != null) slide.anchoredPosition = Vector2.Lerp(slideRest + fromOffset, slideRest, e);
            if (k >= 1f) { if (group != null) group.alpha = 1f; enabled = false; }
        }
    }
}
