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
        [System.NonSerialized] public bool selectionChangesScale = true;
        public GameObject highlight;   // glow shown while hovered or selected (optional)
        public Outline focusOutline;
        public Color idleOutlineColor = new Color(0.275f, 0.808f, 0.878f, 0.35f);
        public Color focusOutlineColor = new Color(0.275f, 0.808f, 0.878f, 1f);

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
            if (focusOutline != null) focusOutline.effectColor = idleOutlineColor;
        }

        // Start slightly small so the Update spring grows it in — a subtle entrance pop.
        void OnEnable()
        {
            inside = false;
            pressed = false;
            selected = false;
            target = 1f;
            cur = 0.85f;
            vel = 0f;
            transform.localScale = baseScale * cur;
            RefreshSelectionFromEventSystem();
        }

        bool Active => selectable == null || selectable.interactable;
        bool Lit => Active && (inside || selected);
        bool Scaled => Active && (inside || (selected && selectionChangesScale));

        void Refresh()
        {
            if (!pressed) target = Scaled ? hover : 1f;
            if (highlight != null) highlight.SetActive(Lit);
            if (focusOutline != null)
                focusOutline.effectColor = Lit ? focusOutlineColor : idleOutlineColor;
        }

        public void ConfigureFocusOutline(Outline outline, Color idle, Color focused)
        {
            focusOutline = outline;
            idleOutlineColor = idle;
            focusOutlineColor = focused;
            Refresh();
        }

        // Arcade navigation invokes Button.onClick directly instead of travelling through
        // EventSystem's Submit handler. Keep the visual state sourced from the EventSystem itself
        // as well as its callbacks, so PLAY is visibly focused immediately and never loses its
        // glow when input modules or fast-enter Play Mode initialise in a different order.
        public void RefreshSelectionFromEventSystem()
        {
            bool eventSelected = EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == gameObject;
            if (selected == eventSelected) return;
            selected = eventSelected;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            // PLAY is selected by default, so it may already be resting at its focused scale when
            // the pointer arrives. Re-arm the spring from 1x in that case; mouse hover now produces
            // the same visible grow/bounce that LEVEL SELECT gets when the pointer enters it.
            bool wasAlreadyLit = Lit;
            inside = true;
            if (Active) Sfx.Hover();
            if (Active && wasAlreadyLit && !suspended)
            {
                cur = Mathf.Min(cur, 1f);
                vel = 0f;
                transform.localScale = baseScale * cur;
            }
            Refresh();
        }
        public void OnPointerExit(PointerEventData e)  { inside = false; Refresh(); }
        public void OnSelect(BaseEventData e)
        {
            selected = true;
            if (Active) Sfx.Hover();
            if (Active && !selectionChangesScale && !inside && !suspended)
            {
                // Home buttons stay the same resting size. Selection still gets a quick one-shot
                // bounce, then settles back to 1x while its outline/glow remains visibly focused.
                cur = 1f;
                vel = 5.5f;
                transform.localScale = baseScale;
            }
            Refresh();
        }
        public void OnDeselect(BaseEventData e)        { selected = false; Refresh(); }

        public void OnPointerDown(PointerEventData e)
        {
            if (!Active) return;
            pressed = true; target = press;
        }
        public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }

        void Update()
        {
            RefreshSelectionFromEventSystem();
            if (suspended) return;
            // Most menu/map nodes are idle most of the time. Once the spring has settled, avoid
            // running spring maths and writing the Transform every frame (notably 50 map nodes).
            if (Mathf.Abs(target - cur) < 0.0005f && Mathf.Abs(vel) < 0.0005f)
            {
                if (cur != target || vel != 0f)
                {
                    cur = target;
                    vel = 0f;
                    transform.localScale = baseScale * cur;
                }
                return;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // a hitch must not launch the spring
            float a = (target - cur) * Stiffness - vel * Damping;
            vel += a * dt;
            cur += vel * dt;
            transform.localScale = baseScale * cur;
        }
    }
}
