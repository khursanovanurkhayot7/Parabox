using UnityEngine;
using UnityEngine.EventSystems;

namespace Parabox
{
    // A press-and-hold button for the on-screen d-pad: fires the instant it's pressed and keeps
    // firing while held, so tapping OR holding an arrow moves the player (like a real game pad).
    // Serialized onto the d-pad buttons by the prebuilt-UI generator; GameManager only assigns the
    // current movement callback when play begins.
    public class HoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action onFire;
        public float firstDelay = 0.26f;   // delay before hold-repeat kicks in
        public float repeatRate = 0.13f;    // repeat interval while held

        bool held;
        float nextFire;

        public void OnPointerDown(PointerEventData e)
        {
            held = true;
            onFire?.Invoke();                       // immediate move on press
            nextFire = Time.unscaledTime + firstDelay;
        }

        public void OnPointerUp(PointerEventData e) => held = false;
        public void OnPointerExit(PointerEventData e) => held = false;

        void Update()
        {
            if (held && onFire != null && Time.unscaledTime >= nextFire)
            {
                onFire();
                nextFire = Time.unscaledTime + repeatRate;
            }
        }
    }
}
