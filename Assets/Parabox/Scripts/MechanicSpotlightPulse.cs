using UnityEngine;

namespace Parabox
{
    // Small reusable presentation effect for first-mechanic lessons. It never owns gameplay state.
    public sealed class MechanicSpotlightPulse : MonoBehaviour
    {
        SpriteRenderer rendererRef;
        Vector3 baseScale;
        Color baseColour;
        float phase;

        void Awake()
        {
            rendererRef = GetComponent<SpriteRenderer>();
            baseScale = transform.localScale;
            baseColour = rendererRef != null ? rendererRef.color : Color.white;
            phase = transform.GetSiblingIndex() * 0.47f;
        }

        void Update()
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f + phase);
            transform.localScale = baseScale * Mathf.Lerp(0.92f, 1.08f, wave);
            if (rendererRef != null)
            {
                Color colour = baseColour;
                colour.a = baseColour.a * Mathf.Lerp(0.48f, 1f, wave);
                rendererRef.color = colour;
            }
        }
    }
}
