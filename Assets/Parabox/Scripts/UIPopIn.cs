using UnityEngine;

namespace Parabox
{
    // Pops a panel in with a little overshoot when it becomes visible.
    public class UIPopIn : MonoBehaviour
    {
        public float duration = 0.34f;

        Vector3 target = Vector3.one;
        float t;
        bool haveTarget;

        void Awake() { target = transform.localScale; haveTarget = true; }

        void OnEnable()
        {
            if (!haveTarget) { target = transform.localScale; haveTarget = true; }
            t = 0f;
            transform.localScale = target * 0.7f;
        }

        void Update()
        {
            if (t >= 1f) return;
            t += Time.unscaledDeltaTime / duration;
            float e = EaseOutBack(Mathf.Clamp01(t));
            transform.localScale = target * Mathf.Lerp(0.7f, 1f, e);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = 1.70158f + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
