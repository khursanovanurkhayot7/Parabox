using UnityEngine;

namespace Parabox
{
    // Pops a UI element in from nothing after a delay, with a springy overshoot bounce.
    // Used on the win-screen stars so they cascade in one-by-one as a little celebration.
    public class StarPop : MonoBehaviour
    {
        public float delay = 0f;
        public float duration = 0.42f;

        Vector3 baseScale = Vector3.one;
        float t;

        void Awake()
        {
            if (transform.localScale != Vector3.zero) baseScale = transform.localScale;
        }

        void OnEnable()
        {
            t = -delay;
            transform.localScale = Vector3.zero;
        }

        void Update()
        {
            if (t >= duration) return;
            t += Time.unscaledDeltaTime;
            transform.localScale = baseScale * EaseOutBack(Mathf.Clamp01(t / duration));
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.9f, c3 = 1.9f + 1f;   // stronger overshoot = bouncier pop
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
