using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Optional transition overlay. Gameplay starts fully transparent: keeping a coloured image
    // over the world for even one frame made the cabinet look coral/blue in recursive close-ups.
    public class ScreenFade : MonoBehaviour
    {
        public Image image;
        public float duration = 0.45f;

        void Start()
        {
            // The approved sci-fi background is already the transition surface. Never start with
            // the legacy blue/coral colour panel in front of it; on deeply nested boards that
            // panel read as a permanent full-screen colour wash and hid the practical lights.
            if (image != null)
            {
                var c = image.color; c.a = 0f; image.color = c;
                image.raycastTarget = false;
            }
            enabled = false;
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
    }
}
