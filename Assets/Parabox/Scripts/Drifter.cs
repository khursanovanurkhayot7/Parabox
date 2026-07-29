using UnityEngine;

namespace Parabox
{
    // Slides a sprite along a direction and fades it in and out at the ends, on a loop — the
    // drifting chevrons that show which way a current is flowing. Give siblings different
    // phases so the arrows travel in a staggered stream rather than in lockstep.
    public class Drifter : MonoBehaviour
    {
        public Vector2 dir = Vector2.right;
        public float travel = 0.46f;
        public float period = 1.7f;
        public float phase;
        public float peakAlpha = 0.8f;

        Vector3 _origin;
        SpriteRenderer[] _srs;

        void Awake()
        {
            _origin = transform.localPosition;
            _srs = GetComponentsInChildren<SpriteRenderer>(true);
        }

        void Update()
        {
            float t = Mathf.Repeat(Time.time / Mathf.Max(0.01f, period) + phase, 1f);
            transform.localPosition = _origin + (Vector3)(dir.normalized * ((t - 0.5f) * travel));
            float a = Mathf.Sin(t * Mathf.PI) * peakAlpha;   // invisible at both ends of the run
            for (int i = 0; i < _srs.Length; i++)
            {
                var c = _srs[i].color;
                c.a = a;
                _srs[i].color = c;
            }
        }
    }
}
