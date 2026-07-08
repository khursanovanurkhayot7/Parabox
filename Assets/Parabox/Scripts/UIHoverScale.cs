using UnityEngine;
using UnityEngine.EventSystems;

namespace Parabox
{
    // Scales a UI element on hover / press for tactile buttons.
    public class UIHoverScale : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float hover = 1.08f;
        public float press = 0.94f;

        Vector3 baseScale = Vector3.one;
        float target = 1f;
        bool inside;

        void Awake() { baseScale = transform.localScale; }
        void OnEnable() { target = 1f; transform.localScale = baseScale; }

        public void OnPointerEnter(PointerEventData e) { inside = true; target = hover; }
        public void OnPointerExit(PointerEventData e) { inside = false; target = 1f; }
        public void OnPointerDown(PointerEventData e) { target = press; }
        public void OnPointerUp(PointerEventData e) { target = inside ? hover : 1f; }

        void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target,
                1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
        }
    }
}
