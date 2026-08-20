using UnityEngine;
using UnityEngine.EventSystems;

namespace Parabox
{
    // Direction button shared by the prebuilt on-screen controls. Gameplay explicitly disables
    // repeat so one pointer contact always means one cell; the optional repeat mode remains here
    // only for non-puzzle interfaces that may need it later.
    public class HoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action onFire;
        public bool repeatWhileHeld;
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
            if (repeatWhileHeld && held && onFire != null && Time.unscaledTime >= nextFire)
            {
                onFire();
                nextFire = Time.unscaledTime + repeatRate;
            }
        }
    }
}
