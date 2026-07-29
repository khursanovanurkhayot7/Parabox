using UnityEngine;

namespace Parabox
{
    // Smoothly moves an entity's visual to its logical grid cell with premium, readable juice:
    //  - a critically-damped spring (SmoothDamp) → natural ease-IN and ease-OUT, a consistent pace,
    //    stays smooth when re-targeted rapidly (holding a direction), and always lands exactly on the grid,
    //  - a subtle squash in the travel direction as it pushes off,
    //  - a small settle "plop" the instant it arrives,
    //  - reparent grow/shrink when entering / leaving a box (the scale settle),
    //  - a springy bump when a move is blocked.
    public class EntityView : MonoBehaviour
    {
        // Heavier, more physical push: a longer spring time gives a clear ease-IN off the mark and a
        // longer ease-OUT into the cell — weight and momentum you can feel — while SmoothDamp keeps it
        // responsive (it starts on the first frame) and never slippery (it can't overshoot or drift).
        // Weight comes mostly from the push-off squash and the arrival settle (below), which add no
        // positional lag. moveSmoothTime is kept just under the held-move repeat interval (0.13s in
        // GameManager) so a HELD direction never trails more than a fraction of a cell behind the
        // grid — the "slippery" a longer time would cause. 0.10 reads heavier than the old 0.08
        // without that lag.
        public float moveSmoothTime = 0.10f;
        public float settleSpeed = 11f;
        public float squashSpeed = 13f;
        public float bumpSpeed = 17f;

        Vector3 curPos, targetLocalPos, vel;
        bool hasTarget, moving, lastHoriz = true;

        Vector3 settle = Vector3.one;   // reparent grow/shrink -> decays to one
        Vector2 squash = Vector2.one;   // push-off impulse     -> decays to one
        Vector3 bump = Vector3.zero;    // blocked nudge         -> decays to zero
        float land;                     // arrival settle pulse (1 -> 0)

        // A rock sinking into a trench: it slides onto the pit (SetTarget, as normal) while this
        // factor shrinks it to nothing, then the GameObject hides. Undo calls Unsink to bring it back.
        public float sinkSpeed = 4.5f;  // ~0.22s to vanish
        float sinkScale = 1f;
        bool sinking;

        public void Sink() { sinking = true; }

        public void Unsink()
        {
            if (!sinking && sinkScale >= 1f) return;   // nothing to restore — cheap no-op for normal entities
            sinking = false;
            sinkScale = 1f;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void SetTarget(Transform parent, Vector3 localPos, bool instant)
        {
            if (transform.parent != parent)
            {
                transform.SetParent(parent, true); // keep world pose across the reparent
                settle = transform.localScale;      // then animate scale back to one
                curPos = transform.localPosition;   // and position from where we landed
            }

            if (localPos != targetLocalPos)
            {
                Vector3 d = localPos - curPos;
                if (d != Vector3.zero) lastHoriz = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
                moving = true;
            }
            targetLocalPos = localPos;
            hasTarget = true;

            if (instant)
            {
                curPos = localPos; vel = Vector3.zero;
                settle = Vector3.one; squash = Vector2.one; bump = Vector3.zero; land = 0f;
                moving = false;
                transform.localPosition = localPos;
                transform.localScale = Vector3.one;
            }
        }

        public void Squash(Vector2 dir)
        {
            if (dir == Vector2.zero) return;
            lastHoriz = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);
            // a firmer push-off stretch in the travel axis — the object leans into the shove, which
            // is what sells "physical" rather than "slid across ice"
            squash = lastHoriz ? new Vector2(1.14f, 0.88f) : new Vector2(0.88f, 1.14f);
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

            // critically-damped spring: eases in from rest, eases out into the target, and carries
            // velocity across rapid re-targets so a held direction flows cell-to-cell without jerks.
            curPos = Vector3.SmoothDamp(curPos, targetLocalPos, ref vel, moveSmoothTime, Mathf.Infinity, dt);

            // land EXACTLY on the grid + fire a small settle the instant we arrive
            if (moving && (curPos - targetLocalPos).sqrMagnitude < 2e-5f && vel.sqrMagnitude < 2e-3f)
            {
                curPos = targetLocalPos; vel = Vector3.zero;
                moving = false;
                land = 1f;
            }

            settle = Vector3.Lerp(settle, Vector3.one, 1f - Mathf.Exp(-settleSpeed * dt));
            squash = Vector2.Lerp(squash, Vector2.one, 1f - Mathf.Exp(-squashSpeed * dt));
            bump = Vector3.Lerp(bump, Vector3.zero, 1f - Mathf.Exp(-bumpSpeed * dt));
            land = Mathf.Max(0f, land - dt * 4.5f);   // ~0.22s — a longer, clearer settle as it comes to rest

            // arrival "plop": a subtle squash across the travel axis, peaking mid-settle then easing
            // out — the object's weight lands into the cell. One rise-and-fall, never an oscillation,
            // so it settles rather than bounces.
            float s = Mathf.Sin(land * Mathf.PI) * 0.085f;
            float lx = lastHoriz ? 1f - s : 1f + s;
            float ly = lastHoriz ? 1f + s : 1f - s;

            if (sinking)
            {
                sinkScale = Mathf.Max(0f, sinkScale - dt * sinkSpeed);
                if (sinkScale <= 0.001f) { gameObject.SetActive(false); return; }   // fully sunk — hide
            }

            transform.localPosition = curPos + bump;
            transform.localScale = new Vector3(settle.x * squash.x * lx * sinkScale,
                                               settle.y * squash.y * ly * sinkScale, 1f);
        }
    }
}
