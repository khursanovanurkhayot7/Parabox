using UnityEngine;

namespace Parabox
{
    // Smoothly moves an entity's visual to its logical grid cell, with juice:
    //  - reparenting between rooms of different scale gives the grow/shrink when
    //    entering or leaving a box (the "settle" scale),
    //  - a squash-and-stretch impulse in the move direction,
    //  - a springy "bump" nudge when a move is blocked.
    public class EntityView : MonoBehaviour
    {
        public float posSpeed = 14f;
        public float settleSpeed = 12f;
        public float squashSpeed = 15f;
        public float bumpSpeed = 17f;

        Vector3 curPos;                 // settled local position (bump added on top)
        Vector3 targetLocalPos;
        bool hasTarget;

        Vector3 settle = Vector3.one;   // reparent grow/shrink -> decays to one
        Vector2 squash = Vector2.one;   // move impulse         -> decays to one
        Vector3 bump = Vector3.zero;    // blocked nudge         -> decays to zero

        public void SetTarget(Transform parent, Vector3 localPos, bool instant)
        {
            if (transform.parent != parent)
            {
                transform.SetParent(parent, true); // keep world pose across the reparent
                settle = transform.localScale;      // then animate scale back to one
                curPos = transform.localPosition;   // and position from where we landed
            }

            targetLocalPos = localPos;
            hasTarget = true;

            if (instant)
            {
                curPos = localPos;
                settle = Vector3.one;
                squash = Vector2.one;
                bump = Vector3.zero;
                transform.localPosition = localPos;
                transform.localScale = Vector3.one;
            }
        }

        public void Squash(Vector2 dir)
        {
            if (dir == Vector2.zero) return;
            squash = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
                ? new Vector2(1.16f, 0.86f)
                : new Vector2(0.86f, 1.16f);
        }

        public void BumpTo(Vector3 localDir)
        {
            if (localDir == Vector3.zero) return;
            bump = localDir.normalized * 0.16f;
        }

        void Update()
        {
            if (!hasTarget) return;
            float dt = Time.deltaTime;

            curPos = Vector3.Lerp(curPos, targetLocalPos, 1f - Mathf.Exp(-posSpeed * dt));
            settle = Vector3.Lerp(settle, Vector3.one, 1f - Mathf.Exp(-settleSpeed * dt));
            squash = Vector2.Lerp(squash, Vector2.one, 1f - Mathf.Exp(-squashSpeed * dt));
            bump = Vector3.Lerp(bump, Vector3.zero, 1f - Mathf.Exp(-bumpSpeed * dt));

            transform.localPosition = curPos + bump;
            transform.localScale = new Vector3(settle.x * squash.x, settle.y * squash.y, 1f);
        }
    }
}
