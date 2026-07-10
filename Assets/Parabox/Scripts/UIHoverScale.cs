using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // Scales a UI element on hover / press for tactile buttons, with subtle sound feedback.
    // Sound + hover state are suppressed when the element is a non-interactable Selectable
    // (e.g. a locked level card), so locked items feel inert.
    public class UIHoverScale : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float hover = 1.08f;
        public float press = 0.94f;

        Vector3 baseScale = Vector3.one;
        float target = 1f;
        bool inside;
        Selectable selectable;

        void Awake()
        {
            baseScale = transform.localScale;
            selectable = GetComponent<Selectable>();
        }

        // Start slightly small so the Update lerp grows it in — a subtle entrance pop.
        void OnEnable() { target = 1f; transform.localScale = baseScale * 0.85f; }

        bool Active => selectable == null || selectable.interactable;

        public void OnPointerEnter(PointerEventData e)
        {
            inside = true;
            if (!Active) return;
            target = hover;
            Sfx.Hover();
        }

        public void OnPointerExit(PointerEventData e) { inside = false; target = 1f; }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Active) return;
            target = press;
            Sfx.Click();
        }

        public void OnPointerUp(PointerEventData e) { target = inside ? hover : 1f; }

        void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target,
                1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
        }
    }
}
