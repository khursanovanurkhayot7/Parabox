using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // Scales a UI element AND lights up a glow when it is hovered OR selected (keyboard / controller),
    // so the currently-focused button is always obvious. Sound + highlight are suppressed when the
    // element is a non-interactable Selectable (e.g. a locked level card), so locked items feel inert.
    public class UIHoverScale : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public float hover = 1.10f;
        public float press = 0.94f;
        public GameObject highlight;   // glow shown while hovered or selected (optional)

        // While true, something else owns this transform's scale (the map's progression
        // animation punches a node). Disabling the component instead would re-fire OnEnable on
        // the way back and snap the node to 0.85 — a visible pop in the middle of the reward.
        public bool suspended;

        Vector3 baseScale = Vector3.one;
        float target = 1f;
        bool inside, selected, pressed;
        Selectable selectable;

        // Spring state. The old motion was Lerp(current, target, 1-exp(-15dt)) — an exponential
        // approach with no curve: it reaches 90% in ~150ms and simply stops. Nothing in the real
        // world moves like that, which is why a hover read as a state change rather than a motion.
        // A light spring overshoots a few percent and settles, which is what "responsive" feels like.
        float cur = 1f, vel;
        const float Stiffness = 190f;
        const float Damping = 18f;      // ratio ~0.65 -> a small, deliberate overshoot

        void Awake()
        {
            baseScale = transform.localScale;
            selectable = GetComponent<Selectable>();
            if (highlight != null) highlight.SetActive(false);
        }

        // Start slightly small so the Update lerp grows it in — a subtle entrance pop.
        void OnEnable() { target = 1f; cur = 0.85f; vel = 0f; transform.localScale = baseScale * cur; }

        bool Active => selectable == null || selectable.interactable;
        bool Lit => Active && (inside || selected);

        void Refresh()
        {
            if (!pressed) target = Lit ? hover : 1f;
            if (highlight != null) highlight.SetActive(Lit);
        }

        public void OnPointerEnter(PointerEventData e) { inside = true; if (Active) Sfx.Hover(); Refresh(); }
        public void OnPointerExit(PointerEventData e)  { inside = false; Refresh(); }
        public void OnSelect(BaseEventData e)          { selected = true; if (Active) Sfx.Hover(); Refresh(); }
        public void OnDeselect(BaseEventData e)        { selected = false; Refresh(); }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Active) return;
            pressed = true; target = press;
        }
        public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }

        void Update()
        {
            if (suspended) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // a hitch must not launch the spring
            float a = (target - cur) * Stiffness - vel * Damping;
            vel += a * dt;
            cur += vel * dt;
            transform.localScale = baseScale * cur;
        }
    }
}
