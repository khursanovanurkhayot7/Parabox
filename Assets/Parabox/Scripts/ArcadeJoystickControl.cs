using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Parabox
{
    // Pointer/touch input for the large joystick card baked into the gameplay HUD. The arcade
    // adapter still owns physical cabinet input; this component gives the visible on-screen control
    // identical one-gesture/one-move behaviour in desktop and WebGL builds.
    [RequireComponent(typeof(RectTransform))]
    public sealed class ArcadeJoystickControl : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RectTransform handle;
        public Vector2 stickCentre = new Vector2(-52f, -22f);
        public float deadZone = 16f;
        public float handleTravel = 9f;

        [NonSerialized] public Action<Vector2Int> onDirection;

        RectTransform rect;
        Vector2 handleHome;
        Vector2Int direction;
        bool held;
        bool firedThisContact;
        bool cached;

        public void Configure(Action<Vector2Int> fire, RectTransform visualHandle)
        {
            onDirection = fire;
            if (visualHandle != null) handle = visualHandle;
            CacheReferences();
            ResetVisual();
        }

        void Awake() => CacheReferences();

        void OnDisable() => CancelInput();

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
            firedThisContact = false;
            UpdateDirection(eventData);
        }

        public void OnDrag(PointerEventData eventData) => UpdateDirection(eventData);

        public void OnPointerUp(PointerEventData eventData) => CancelInput();

        public void OnPointerExit(PointerEventData eventData) => CancelInput();

        void UpdateDirection(PointerEventData eventData)
        {
            CacheReferences();
            if (rect == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
                return;

            Vector2Int next = DirectionFromDelta(local - stickCentre, deadZone);
            direction = next;
            UpdateVisual(direction);

            // Even if the pointer wobbles between directions while held, this contact represents
            // one intended puzzle move. Release and press again to spend another move.
            if (!held || firedThisContact || direction == Vector2Int.zero || onDirection == null) return;
            firedThisContact = true;
            onDirection(direction);
        }

        public static Vector2Int DirectionFromDelta(Vector2 delta, float minimumDistance)
        {
            if (delta.sqrMagnitude < minimumDistance * minimumDistance) return Vector2Int.zero;
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? new Vector2Int(delta.x < 0f ? -1 : 1, 0)
                : new Vector2Int(0, delta.y < 0f ? -1 : 1);
        }

        void CacheReferences()
        {
            if (cached) return;
            rect = transform as RectTransform;
            if (handle == null)
            {
                Transform child = transform.Find("Ball");
                if (child != null) handle = child as RectTransform;
            }
            if (handle != null) handleHome = handle.anchoredPosition;
            cached = true;
        }

        void UpdateVisual(Vector2Int value)
        {
            if (handle == null) return;
            handle.anchoredPosition = handleHome + new Vector2(value.x, value.y * 0.55f) * handleTravel;
        }

        void ResetVisual()
        {
            if (handle != null) handle.anchoredPosition = handleHome;
        }

        void CancelInput()
        {
            held = false;
            firedThisContact = false;
            direction = Vector2Int.zero;
            ResetVisual();
        }
    }
}
