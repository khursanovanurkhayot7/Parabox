using UnityEngine;

namespace Parabox
{
    // Gentle up-and-down float (menu mascot, title).
    public class Bobber : MonoBehaviour
    {
        public float amplitude = 8f;
        public float speed = 1.6f;
        public float phase = 0f;

        Vector3 basePos;
        bool captured;

        void OnEnable()
        {
            if (!captured) { basePos = transform.localPosition; captured = true; }
        }

        void Update()
        {
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.unscaledTime * speed + phase) * amplitude);
        }
    }
}
