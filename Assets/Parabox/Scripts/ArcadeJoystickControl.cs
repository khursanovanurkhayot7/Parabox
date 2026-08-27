using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Parabox
{
    // Pointer/touch input for the large joystick card baked into the gameplay HUD. The arcade
    // adapter still owns physical cabinet input; this component gives the visible on-screen control
    // identical stable cardinal-direction behaviour in desktop and WebGL builds.
    [RequireComponent(typeof(RectTransform))]
    public sealed class ArcadeJoystickControl : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RectTransform handle;
        public Vector2 stickCentre = new Vector2(-52f, -22f);
        public float deadZone = 16f;
        public float directionSwitchBias = 8f;
        public float handleTravel = 9f;

        [NonSerialized] public Action<Vector2Int> onDirection;

        RectTransform rect;
        Vector2 handleHome;
        Vector2Int direction;
        Vector2Int lastFiredDirection;
        bool held;
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
            lastFiredDirection = Vector2Int.zero;
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

            Vector2 delta = local - stickCentre;
            Vector2Int next = StableDirectionFromDelta(
                delta, direction, deadZone, directionSwitchBias);
            direction = next;
            UpdateVisual(direction);

            if (direction == Vector2Int.zero)
            {
                lastFiredDirection = Vector2Int.zero;
                return;
            }
            // Holding one direction never repeats. Dragging decisively to another axis does fire,
            // matching rapid cabinet-stick turns without reacting to 45-degree pointer wobble.
            if (!held || direction == lastFiredDirection || onDirection == null) return;
            lastFiredDirection = direction;
            onDirection(direction);
        }

        public static Vector2Int DirectionFromDelta(Vector2 delta, float minimumDistance)
        {
            if (delta.sqrMagnitude < minimumDistance * minimumDistance) return Vector2Int.zero;
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? new Vector2Int(delta.x < 0f ? -1 : 1, 0)
                : new Vector2Int(0, delta.y < 0f ? -1 : 1);
        }

        public static Vector2Int StableDirectionFromDelta(Vector2 delta,
            Vector2Int currentDirection, float minimumDistance, float switchBias)
        {
            float ax = Mathf.Abs(delta.x);
            float ay = Mathf.Abs(delta.y);
            if (delta.sqrMagnitude < minimumDistance * minimumDistance)
                return Vector2Int.zero;

            Vector2Int candidate = DirectionFromDelta(delta, minimumDistance);
            if (currentDirection == Vector2Int.zero || candidate == currentDirection
                || candidate == -currentDirection)
                return candidate;

            float candidateStrength = candidate.x != 0 ? ax : ay;
            float currentStrength = currentDirection.x != 0 ? ax : ay;
            return candidateStrength >= currentStrength + Mathf.Max(0f, switchBias)
                ? candidate : currentDirection;
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
            direction = Vector2Int.zero;
            lastFiredDirection = Vector2Int.zero;
            ResetVisual();
        }
    }
}
