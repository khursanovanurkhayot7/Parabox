using UnityEngine;

namespace Parabox
{
    // A fade + slight-scale transition for a full-screen UI panel (menu <-> level select).
    // Show() activates the panel and eases it in; Hide() eases out then deactivates.
    // Uses unscaled time so it works regardless of Time.timeScale.
    [RequireComponent(typeof(CanvasGroup))]
    public class UIScreen : MonoBehaviour
    {
        public float duration = 0.28f;
        public float fromScale = 0.96f;

        CanvasGroup cg;
        RectTransform rt;
        float t;        // 0 = hidden, 1 = fully shown
        int dir;        // +1 easing in, -1 easing out, 0 idle

        void Awake()
        {
            cg = GetComponent<CanvasGroup>();
            rt = (RectTransform)transform;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (cg == null) cg = GetComponent<CanvasGroup>();
            if (rt == null) rt = (RectTransform)transform;
            t = 0f;
            dir = 1;
            cg.alpha = 0f;
            // The screen is already the active destination. Keeping it non-interactable for the
            // first half of the fade made a quick first click disappear even though the button was
            // visibly arriving. Accept input immediately; the transition owner still prevents
            // duplicate scene loads.
            cg.interactable = true;
            cg.blocksRaycasts = true;
            rt.localScale = Vector3.one * fromScale;
            enabled = true;
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            dir = -1;
            enabled = true;
        }

        void Update()
        {
            if (dir == 0) return;

            t = Mathf.Clamp01(t + dir * Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
            float e = t * t * (3f - 2f * t); // smoothstep

            cg.alpha = e;
            cg.interactable = dir > 0 ? t > 0f : t > 0.5f;
            cg.blocksRaycasts = t > 0.01f;
            rt.localScale = Vector3.one * Mathf.Lerp(fromScale, 1f, e);

            if (dir > 0 && t >= 1f) { dir = 0; enabled = false; }
            else if (dir < 0 && t <= 0f) { dir = 0; gameObject.SetActive(false); enabled = false; }
        }
    }
}
