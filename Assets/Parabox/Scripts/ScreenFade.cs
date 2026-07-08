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
            if (image != null)
            {
                var c = image.color; c.a = 1f; image.color = c;
                image.raycastTarget = true;
            }
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
