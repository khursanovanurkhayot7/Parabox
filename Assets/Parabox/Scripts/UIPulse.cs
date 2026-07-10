using UnityEngine;

namespace Parabox
{
    // Gentle looping scale pulse — used to draw the eye to the current (playable) level.
    public class UIPulse : MonoBehaviour
    {
        public float amplitude = 0.05f;
        public float speed = 3.2f;

        Vector3 baseScale = Vector3.one;
        bool captured;

        void OnEnable()
        {
            if (!captured) { baseScale = transform.localScale; captured = true; }
        }

        void Update()
        {
            float s = 1f + Mathf.Sin(Time.unscaledTime * speed) * amplitude;
            transform.localScale = baseScale * s;
        }
    }
}
