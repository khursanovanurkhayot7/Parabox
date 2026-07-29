using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Fades a full-screen black overlay out on scene start (clean level transitions).
    public class ScreenFade : MonoBehaviour
    {
        public Image image;
        public float duration = 0.45f;
        float t;

        void Start()
        {
            // seamless menu-entry: the menu already showed this exact board — no fade, no flash.
            if (PlayerPrefs.GetInt("Parabox.Seamless", 0) == 1)
            {
                if (image != null) { var c0 = image.color; c0.a = 0f; image.color = c0; image.raycastTarget = false; }
                enabled = false;
                return;
            }
            if (image != null)
            {
                var c = image.color; c.a = 1f; image.color = c;
                image.raycastTarget = true;
            }
        }

        // Fade TO black. The scene only ever faded IN, so leaving a scene was a hard cut: the
        // celebration's last frame snapped straight to the next scene's black. Every transition in
        // the game now has both halves.
        public System.Collections.IEnumerator FadeOut(float dur)
        {
            if (image == null) yield break;
            enabled = false;                 // stop the fade-in from fighting us
            image.raycastTarget = true;
            var c = image.color;
            float from = c.a;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(from, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur)));
                image.color = c;
                yield return null;
            }
            c.a = 1f;
            image.color = c;
        }

        void Update()
        {
            if (image == null) { enabled = false; return; }
            t += Time.unscaledDeltaTime / duration;
            var c = image.color;
            c.a = 1f - Mathf.Clamp01(t);
            image.color = c;
            if (t >= 1f) { image.raycastTarget = false; enabled = false; }
        }
    }
}
