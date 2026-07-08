using UnityEngine;

namespace Parabox
{
    // Expanding, fading ring spawned by Fx.Ripple.
    public class RippleAnim : MonoBehaviour
    {
        public SpriteRenderer sr;
        public Color baseColor;
        public float life = 0.45f;
        float age;

        void Update()
        {
            age += Time.deltaTime;
            float f = Mathf.Clamp01(age / life);
            transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.5f, f);
            if (sr != null)
            {
                Color c = baseColor;
                c.a = (1f - f) * 0.9f;
                sr.color = c;
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
