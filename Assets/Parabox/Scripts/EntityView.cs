using UnityEngine;

namespace Parabox
{
    // Smoothly moves an entity's visual to its logical grid cell with restrained, readable juice:
    //  - a visual-only anticipation lean before a fresh move (it never delays control),
    //  - a critically-damped spring that stays responsive when a direction is held,
    //  - subtle squash/stretch through push-off and a tiny landing rebound,
    //  - a short player-coloured rectangle trail plus tiny pooled fragments on tile entry,
    //  - reparent grow/shrink when entering / leaving a box (the scale settle),
    //  - a springy bump when a move is blocked.
    public class EntityView : MonoBehaviour
    {
        // Kept well below the 0.13s held-input repeat so the visuals never lag behind the model.
        public float moveSmoothTime = 0.072f;
        public float anticipationDuration = 0.050f;
        public float landingDuration = 0.200f;
        public float settleSpeed = 14f;
        public float squashSpeed = 18f;
        public float bumpSpeed = 14f;

        // The player needs a clearer silhouette change than cargo on a large arcade display.
        // These values affect only the visual transform; logical movement and collision stay exact.
        public float playerMoveStretch = 1.52f;
        public float playerCrossSquash = 0.78f;
        public float cargoMoveStretch = 1.06f;
        public float cargoCrossSquash = 0.95f;

        Vector3 curPos, targetLocalPos, vel;
        bool hasTarget, moving, lastHoriz = true;

        Vector3 settle = Vector3.one;   // reparent grow/shrink -> decays to one
        Vector2 squash = Vector2.one;   // push-off impulse     -> decays to one
        Vector3 bump = Vector3.zero;    // blocked nudge         -> decays to zero
        float roll;                     // directional lean      -> decays to zero
        float land;                     // arrival settle pulse (1 -> 0)
        float anticipationLeft;
        Vector2 moveDirection = Vector2.right;

        bool motionHeavier;
        bool controlledPlayerMotion;
        SpriteRenderer motionGlow;
        Color motionGlowBase;
        bool hasPortalTravelDirection;
        Vector2 portalTravelDirection;

        // Five pre-created sprites form a short trail of tiny player-coloured rectangles. They are
        // pooled once per actor (never allocated per move), then stamped behind the departing body.
        // This keeps the movement readable on an arcade display without bringing back the large,
        // distracting full-player afterimages that previously hurt both clarity and WebGL timing.
        // Board goals render at order 4. Echoes must stay at 5 or above so they remain visible,
        // while the live player body (order 8) still reads as the clear foreground subject.
        const int OrderSafeTrail = 5;
        const int StepEchoCount = 6;
        const float StepEchoDuration = 0.32f;
        readonly SpriteRenderer[] stepEchoes = new SpriteRenderer[StepEchoCount];
        readonly float[] stepEchoLife = new float[StepEchoCount];
        readonly float[] stepEchoAlpha = new float[StepEchoCount];
        bool stepEchoEnabled;
        Color stepEchoColor;

        // A tiny, reusable arrival burst. These are ordinary SpriteRenderers instead of a runtime
        // ParticleSystem so WebGL never allocates or instantiates objects while the player moves.
        const int TileSparkCount = 5;
        const float TileSparkDuration = 0.24f;
        readonly SpriteRenderer[] tileSparks = new SpriteRenderer[TileSparkCount];
        readonly Vector3[] tileSparkVelocity = new Vector3[TileSparkCount];
        readonly float[] tileSparkLife = new float[TileSparkCount];
        readonly float[] tileSparkAlpha = new float[TileSparkCount];
        readonly float[] tileSparkSize = new float[TileSparkCount];

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

        public void ConfigureMotionFx(Color color, int sortingOrder, bool heavier,
                                      bool enableStepEcho = false,
                                      bool isControlledPlayer = false)
        {
            motionHeavier = heavier;
            controlledPlayerMotion = isControlledPlayer && !heavier;
            // Player/box prefabs can retain an older serialized Smooth Time value even after the
            // script defaults improve. Set the runtime presentation values explicitly so every
            // generated level gets the same visible, responsive movement without prefab-by-prefab
            // editing. These affect only the transform animation, never logical grid movement.
            if (!heavier)
            {
                moveSmoothTime = 0.072f;
                anticipationDuration = 0.050f;
                landingDuration = 0.200f;
                // The active diver gets the wide, expressive Patrick's-Parabox-style silhouette.
                // Echoes and passive player bodies retain a restrained deformation so the player
                // remains the unmistakable focus, while cargo keeps its separate heavy tuning.
                playerMoveStretch = controlledPlayerMotion ? 1.52f : 1.12f;
                playerCrossSquash = controlledPlayerMotion ? 0.78f : 0.92f;
                squashSpeed = controlledPlayerMotion ? 12.5f : 18f;
            }
            Transform glow = transform.Find("Glow");
            motionGlow = glow != null ? glow.GetComponent<SpriteRenderer>() : null;
            if (motionGlow != null) motionGlowBase = motionGlow.color;
            stepEchoEnabled = enableStepEcho && controlledPlayerMotion;
            stepEchoColor = color;
            if (stepEchoEnabled)
            {
                CreateStepEchoes(sortingOrder);
                CreateTileSparks(sortingOrder + 1);
            }
        }

        void CreateTileSparks(int sortingOrder)
        {
            Transform bodyTransform = transform.Find("Body");
            var source = bodyTransform != null ? bodyTransform.GetComponent<SpriteRenderer>() : null;
            if (source == null) return;

            Color sparkColor = Color.Lerp(stepEchoColor, Color.white, 0.12f);
            for (int i = 0; i < TileSparkCount; i++)
            {
                if (tileSparks[i] != null) continue;
                var spark = new GameObject("TileSpark" + i);
                spark.transform.SetParent(transform, false);
                var sr = spark.AddComponent<SpriteRenderer>();
                sr.sprite = source.sprite;
                sr.sharedMaterial = source.sharedMaterial;
                sr.sortingLayerID = source.sortingLayerID;
                sr.sortingOrder = sortingOrder;
                sr.color = new Color(sparkColor.r, sparkColor.g, sparkColor.b, 0f);
                sr.enabled = false;
                tileSparks[i] = sr;
            }
        }

        void CreateStepEchoes(int sortingOrder)
        {
            Transform bodyTransform = transform.Find("Body");
            var source = bodyTransform != null ? bodyTransform.GetComponent<SpriteRenderer>() : null;
            if (source == null) { stepEchoEnabled = false; return; }

            for (int i = 0; i < StepEchoCount; i++)
            {
                if (stepEchoes[i] != null) continue;
                var echo = new GameObject("MoveEcho" + i);
                echo.transform.SetParent(transform, false);
                echo.transform.localScale = Vector3.one * 0.1f;
                var sr = echo.AddComponent<SpriteRenderer>();
                sr.sprite = source.sprite;
                sr.sharedMaterial = source.sharedMaterial;
                sr.sortingLayerID = source.sortingLayerID;
                // Keep every fragment behind the player but above the board architecture. The old
                // order dropped the last echoes beneath goals/walls, which made the entire trail
                // appear missing on several layouts.
                sr.sortingOrder = Mathf.Max(OrderSafeTrail, sortingOrder - i / 2);
                sr.color = new Color(stepEchoColor.r, stepEchoColor.g, stepEchoColor.b, 0f);
                sr.enabled = false;
                stepEchoes[i] = sr;
            }
        }

        void SyncStepEchoParent(Transform parent)
        {
            if (!stepEchoEnabled) return;
            for (int i = 0; i < StepEchoCount; i++)
            {
                if (stepEchoes[i] == null) continue;
                stepEchoes[i].transform.SetParent(parent, false);
                stepEchoes[i].enabled = false;
                stepEchoLife[i] = 0f;
            }
            for (int i = 0; i < TileSparkCount; i++)
            {
                if (tileSparks[i] == null) continue;
                tileSparks[i].transform.SetParent(parent, false);
                tileSparks[i].enabled = false;
                tileSparkLife[i] = 0f;
            }
        }

        void StampStepEchoes()
        {
            if (!stepEchoEnabled) return;
            Vector2 side = new Vector2(-moveDirection.y, moveDirection.x);

            for (int i = 0; i < StepEchoCount; i++)
            {
                var sr = stepEchoes[i];
                if (sr == null) continue;
                float t = i / (float)(StepEchoCount - 1);
                float behind = 0.035f + t * 0.48f;
                float sideOffset = i == 0 ? 0f : ((i & 1) == 0 ? -1f : 1f) * (0.022f + t * 0.024f);
                sr.transform.localPosition = curPos - (Vector3)moveDirection * behind
                    + (Vector3)side * sideOffset;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, ((i & 1) == 0 ? -1f : 1f) * t * 7f);

                // Long edge follows the move direction; later fragments become smaller and softer.
                float length = Mathf.Lerp(0.34f, 0.11f, t);
                float thickness = Mathf.Lerp(0.16f, 0.055f, t);
                sr.transform.localScale = lastHoriz
                    ? new Vector3(length, thickness, 1f)
                    : new Vector3(thickness, length, 1f);
                float alpha = Mathf.Lerp(0.72f, 0.18f, t);
                sr.color = new Color(stepEchoColor.r, stepEchoColor.g, stepEchoColor.b, alpha);
                stepEchoAlpha[i] = alpha;
                stepEchoLife[i] = StepEchoDuration * Mathf.Lerp(1f, 0.62f, t);
                sr.enabled = true;
            }
        }

        void BurstTileEntry(Vector3 tilePosition)
        {
            if (!stepEchoEnabled) return;

            Color sparkColor = Color.Lerp(stepEchoColor, Color.white, 0.12f);
            Vector2 side = new Vector2(-moveDirection.y, moveDirection.x);
            for (int i = 0; i < TileSparkCount; i++)
            {
                var sr = tileSparks[i];
                if (sr == null) continue;

                float sideSign = (i & 1) == 0 ? -1f : 1f;
                float row = i < 2 ? 0.92f : 0.58f;
                Vector2 velocity = -moveDirection * row + side * sideSign * (i < 2 ? 0.86f : 0.52f);
                tileSparkVelocity[i] = new Vector3(velocity.x, velocity.y, 0f);
                tileSparkLife[i] = TileSparkDuration * (i < 2 ? 1f : 0.78f);
                tileSparkAlpha[i] = i < 2 ? 0.50f : 0.32f;
                tileSparkSize[i] = i < 2 ? 0.085f : 0.055f;

                sr.transform.localPosition = tilePosition - (Vector3)moveDirection * 0.25f
                    + (Vector3)side * sideSign * (i < 2 ? 0.21f : 0.13f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, sideSign * (12f + i * 7f));
                sr.transform.localScale = lastHoriz
                    ? new Vector3(tileSparkSize[i] * 1.65f, tileSparkSize[i], 1f)
                    : new Vector3(tileSparkSize[i], tileSparkSize[i] * 1.65f, 1f);
                sr.color = new Color(sparkColor.r, sparkColor.g, sparkColor.b, tileSparkAlpha[i]);
                sr.enabled = true;
            }
        }

        void ClearStepEchoes()
        {
            for (int i = 0; i < StepEchoCount; i++)
            {
                stepEchoLife[i] = 0f;
                if (stepEchoes[i] != null) stepEchoes[i].enabled = false;
            }
            for (int i = 0; i < TileSparkCount; i++)
            {
                tileSparkLife[i] = 0f;
                if (tileSparks[i] != null) tileSparks[i].enabled = false;
            }
        }

        void UpdateStepEchoes(float dt)
        {
            if (!stepEchoEnabled) return;
            for (int i = 0; i < StepEchoCount; i++)
            {
                var sr = stepEchoes[i];
                if (sr == null || !sr.enabled) continue;
                stepEchoLife[i] = Mathf.Max(0f, stepEchoLife[i] - dt);
                if (stepEchoLife[i] <= 0f) { sr.enabled = false; continue; }
                float life = Mathf.Clamp01(stepEchoLife[i] / StepEchoDuration);
                Color c = sr.color;
                c.a = stepEchoAlpha[i] * life * life;
                sr.color = c;
            }

            for (int i = 0; i < TileSparkCount; i++)
            {
                var sr = tileSparks[i];
                if (sr == null || !sr.enabled) continue;
                tileSparkLife[i] = Mathf.Max(0f, tileSparkLife[i] - dt);
                if (tileSparkLife[i] <= 0f) { sr.enabled = false; continue; }

                float life = Mathf.Clamp01(tileSparkLife[i] / TileSparkDuration);
                sr.transform.localPosition += tileSparkVelocity[i] * dt;
                float sparkScale = tileSparkSize[i] * Mathf.Lerp(0.45f, 1f, life);
                sr.transform.localScale = lastHoriz
                    ? new Vector3(sparkScale * 1.65f, sparkScale, 1f)
                    : new Vector3(sparkScale, sparkScale * 1.65f, 1f);
                Color c = sr.color;
                c.a = tileSparkAlpha[i] * life * life;
                sr.color = c;
            }
        }

        // A recursive-room transfer changes both parent transform and scale. Preserving the old
        // world pose across that reparent can convert a horizontal move into a local position above
        // or below the doorway. Supply the logical input direction for every recursive entity
        // transfer so the hand-off remains visually cardinal: RIGHT always travels right into the
        // new cell, LEFT travels left, and the vertical directions behave the same way. This is
        // required for cargo as well as the player; both can cross a room boundary in one push.
        public void SetPortalTarget(Transform parent, Vector3 localPos, Vector2 direction,
                                    bool instant)
        {
            hasPortalTravelDirection = !instant && direction.sqrMagnitude > 0.001f;
            portalTravelDirection = hasPortalTravelDirection ? direction.normalized : Vector2.zero;
            SetTarget(parent, localPos, instant);
            hasPortalTravelDirection = false;
            portalTravelDirection = Vector2.zero;
        }

        public void SetTarget(Transform parent, Vector3 localPos, bool instant)
        {
            bool parentChanged = transform.parent != parent;
            if (parentChanged)
            {
                transform.SetParent(parent, true); // keep world pose across the reparent
                // Deep recursive rooms can differ by more than 100x in world scale. Keeping that
                // ratio verbatim makes the player/crate fill the whole screen for a frame and can
                // carry it through the HUD while the camera catches up. Preserve the direction of
                // travel, but bound the visual hand-off to a short portal-sized move.
                Vector3 inheritedScale = transform.localScale;
                settle = new Vector3(
                    Mathf.Clamp(inheritedScale.x, 0.58f, 1.68f),
                    Mathf.Clamp(inheritedScale.y, 0.58f, 1.68f),
                    1f);
                transform.localScale = settle;

                if (hasPortalTravelDirection)
                {
                    // Begin just outside the destination cell, on the side opposite travel. This
                    // is long enough to read as entering/exiting a recursive room but short enough
                    // to stay inside the doorway while the camera performs its portal zoom.
                    const float portalLeadDistance = 0.72f;
                    moveDirection = portalTravelDirection;
                    lastHoriz = Mathf.Abs(moveDirection.x) >= Mathf.Abs(moveDirection.y);
                    curPos = localPos - (Vector3)portalTravelDirection * portalLeadDistance;
                    transform.localPosition = curPos;
                }
                else
                {
                    curPos = transform.localPosition;
                    Vector3 transition = curPos - localPos;
                    const float maxTransitionDistance = 1.35f;
                    if (transition.sqrMagnitude > maxTransitionDistance * maxTransitionDistance)
                    {
                        curPos = localPos + transition.normalized * maxTransitionDistance;
                        transform.localPosition = curPos;
                    }
                }
                SyncStepEchoParent(parent);
            }

            if (localPos != targetLocalPos || parentChanged)
            {
                Vector3 d = localPos - curPos;
                bool wasMoving = moving;
                if (d != Vector3.zero)
                {
                    lastHoriz = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
                    moveDirection = new Vector2(d.x, d.y).normalized;
                }
                moving = true;

                if (!instant)
                {
                    StampStepEchoes();
                    // A newly-started move gets a two-frame visual lean. A held direction is already
                    // in motion, so it keeps flowing instead of repeatedly pausing between cells.
                    anticipationLeft = wasMoving ? 0f : anticipationDuration;
                    // Keep normal movement allocation-free. The previous stronger-animation pass
                    // created several temporary GameObjects on every grid step; rapid movement and
                    // multi-object mechanics caused visible garbage-collection stalls in WebGL and
                    // on lower-memory Macs. Squash, lean, landing recoil and glow still provide the
                    // stronger feedback without per-step allocations.
                }
            }
            targetLocalPos = localPos;
            hasTarget = true;

            if (instant)
            {
                curPos = localPos; vel = Vector3.zero;
                settle = Vector3.one; squash = Vector2.one; bump = Vector3.zero; roll = 0f;
                land = 0f; anticipationLeft = 0f;
                moving = false;
                ClearStepEchoes();
                transform.localPosition = localPos;
                transform.localScale = Vector3.one;
                transform.localRotation = Quaternion.identity;
                if (motionGlow != null) motionGlow.color = motionGlowBase;
            }
        }

        public void Squash(Vector2 dir)
        {
            if (dir == Vector2.zero) return;
            lastHoriz = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);
            moveDirection = dir.normalized;
            // The diver briefly stretches along travel and compresses across it. Cargo deforms less
            // so it still reads as weighty and rigid. Both return to an exact 1x scale immediately.
            float stretch = motionHeavier ? cargoMoveStretch : playerMoveStretch;
            float compress = motionHeavier ? cargoCrossSquash : playerCrossSquash;
            squash = lastHoriz ? new Vector2(stretch, compress) : new Vector2(compress, stretch);
            float leanSign = Mathf.Abs(dir.x) > 0.01f ? -Mathf.Sign(dir.x) : Mathf.Sign(dir.y);
            roll = leanSign * (motionHeavier ? 3.2f : 5.5f);
        }

        public void BumpTo(Vector3 localDir)
        {
            if (localDir == Vector3.zero) return;
            bump = localDir.normalized * (motionHeavier ? 0.22f : 0.25f);
            float leanSign = Mathf.Abs(localDir.x) > 0.01f
                ? -Mathf.Sign(localDir.x) : Mathf.Sign(localDir.y);
            roll = leanSign * (motionHeavier ? 4.5f : 5.5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateStepEchoes(dt);
            if (!hasTarget) return;

            bool anticipating = anticipationLeft > 0f;
            if (anticipating)
                anticipationLeft = Mathf.Max(0f, anticipationLeft - dt);

            // Anticipation is only a silhouette/lean cue. Logical input and visual translation begin
            // immediately, preserving the fast retry and held-direction feel of a premium puzzler.
            curPos = Vector3.SmoothDamp(curPos, targetLocalPos, ref vel,
                moveSmoothTime, Mathf.Infinity, dt);

            // land EXACTLY on the grid + fire a small settle the instant we arrive
            if (moving && (curPos - targetLocalPos).sqrMagnitude < 2e-5f && vel.sqrMagnitude < 2e-3f)
            {
                curPos = targetLocalPos; vel = Vector3.zero;
                moving = false;
                land = 1f;
                BurstTileEntry(targetLocalPos);
            }

            settle = Vector3.Lerp(settle, Vector3.one, 1f - Mathf.Exp(-settleSpeed * dt));
            squash = Vector2.Lerp(squash, Vector2.one, 1f - Mathf.Exp(-squashSpeed * dt));
            bump = Vector3.Lerp(bump, Vector3.zero, 1f - Mathf.Exp(-bumpSpeed * dt));
            roll = Mathf.Lerp(roll, 0f, 1f - Mathf.Exp(-bumpSpeed * 0.78f * dt));
            land = Mathf.Max(0f, land - dt / Mathf.Max(0.01f, landingDuration));

            // Anticipation gently compresses in the travel axis and leans back by only 0.025 units.
            float anticipation01 = anticipationDuration > 0f
                ? Mathf.Clamp01(1f - anticipationLeft / anticipationDuration) : 1f;
            float anticipationPulse = anticipating ? Mathf.Sin(anticipation01 * Mathf.PI) : 0f;
            float anticipateScale = anticipationPulse * (motionHeavier ? 0.022f : 0.055f);
            float ax = lastHoriz ? 1f - anticipateScale : 1f + anticipateScale * 0.65f;
            float ay = lastHoriz ? 1f + anticipateScale * 0.65f : 1f - anticipateScale;

            // A heavier, two-beat landing makes each grid step legible on a large arcade display.
            // Position still resolves exactly to the model's cell after the short recoil.
            float landProgress = 1f - land;
            float impactStrength = motionHeavier ? 0.032f : 0.075f;
            float reboundStrength = motionHeavier ? 0.012f : 0.028f;
            float impact = Mathf.Sin(landProgress * Mathf.PI) * land * impactStrength;
            float rebound = Mathf.Sin(landProgress * Mathf.PI * 2f) * land * reboundStrength;
            float lx = (lastHoriz ? 1f - impact : 1f + impact) * (1f + rebound);
            float ly = (lastHoriz ? 1f + impact : 1f - impact) * (1f + rebound);
            Vector3 anticipationOffset = -(Vector3)moveDirection * (anticipationPulse * 0.035f);
            Vector3 landingOffset = -(Vector3)moveDirection
                * (Mathf.Sin(landProgress * Mathf.PI) * land * (motionHeavier ? 0.022f : 0.045f));

            if (sinking)
            {
                sinkScale = Mathf.Max(0f, sinkScale - dt * sinkSpeed);
                if (sinkScale <= 0.001f)
                {
                    gameObject.SetActive(false);
                    return;
                }   // fully sunk — hide
            }

            transform.localPosition = curPos + bump + anticipationOffset + landingOffset;
            transform.localScale = new Vector3(settle.x * squash.x * ax * lx * sinkScale,
                                               settle.y * squash.y * ay * ly * sinkScale, 1f);
            transform.localRotation = Quaternion.Euler(0f, 0f, roll);

            // The existing premium halo briefly brightens during push-off and landing. This reuses
            // the same sprite/material, so the stronger feedback remains WebGL-safe.
            if (motionGlow != null)
            {
                float energy = Mathf.Max(anticipationPulse, moving ? 0.55f : 0f, land * 0.85f);
                Color glow = motionGlowBase;
                glow.a = Mathf.Min(0.70f, motionGlowBase.a * Mathf.Lerp(1f, 4.0f, energy));
                motionGlow.color = glow;
            }
        }

        void OnDestroy()
        {
            // Echoes are siblings during play; clean them if an entity is removed independently of
            // the board root. Destroying the complete level remains safe because Unity ignores
            // children that are already scheduled for destruction.
            for (int i = 0; i < StepEchoCount; i++)
                if (stepEchoes[i] != null)
                    Destroy(stepEchoes[i].gameObject);
            for (int i = 0; i < TileSparkCount; i++)
                if (tileSparks[i] != null)
                    Destroy(tileSparks[i].gameObject);
        }
    }
}
