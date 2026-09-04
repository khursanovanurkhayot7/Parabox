using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // One read-only presentation controller per board, shared by gameplay and tutorial replays.
    // A lit recess means the goal is occupied NOW, never that the piece is immovable.
    public sealed class GoalFeedbackFx : MonoBehaviour
    {
        public enum Kind { Cargo, Player, Echo, Mirror, Colour }

        public sealed class Target
        {
            public readonly GameObject root;
            public readonly int room;
            public readonly Vector2Int cell;
            public readonly Kind kind;
            public readonly int colour;
            public readonly Color accent;
            internal SpriteRenderer rim, glow, socket;
            internal Color emptyRim, emptyGlow, emptySocket;
            internal PremiumCargoSocket premium;
            // Occupying a goal must never replace the piece's visual identity. The arrival
            // animation supplies the confirmation; its final scale and centre stay unchanged.
            internal float seatScale = 1f;
            internal Vector2 seatOffset = Vector2.zero;
            internal bool filled;
            internal float fill, flash;

            public Target(GameObject root, int room, Vector2Int cell, Kind kind,
                Color accent, int colour = 0)
            {
                this.root = root;
                this.room = room;
                this.cell = cell;
                this.kind = kind;
                this.accent = accent;
                this.colour = colour;
            }
        }

        LevelModel model;
        Dictionary<PEntity, EntityView> views;
        List<Target> targets;
        readonly Dictionary<PEntity, Target> seated = new Dictionary<PEntity, Target>();

        public void Configure(LevelModel source, Dictionary<PEntity, EntityView> entityViews,
            List<Target> goalTargets, Sprite cellSprite)
        {
            model = source;
            views = entityViews;
            targets = goalTargets;
            foreach (Target target in targets) BuildTarget(target, cellSprite);
            Refresh(0f, true);
        }

        // Mirrors IsWon's five goal predicates, including colour-only matching. Never treats an
        // echo/mirror as cargo or lights a completed rim for the wrong-coloured piece.
        public static bool IsSatisfiedBy(PEntity entity, Kind kind, int colour = 0)
        {
            if (entity == null || entity.sunk) return false;
            switch (kind)
            {
                case Kind.Cargo: return entity.IsCrate;
                case Kind.Player: return entity.isPlayer;
                case Kind.Echo: return entity.isEcho;
                case Kind.Mirror: return entity.isMirror;
                case Kind.Colour: return entity.colour == colour;
                default: return false;
            }
        }

        void LateUpdate() => Refresh(Time.deltaTime, false);

        void Refresh(float dt, bool instant)
        {
            if (model == null || targets == null || views == null) return;
            seated.Clear();
            foreach (Target target in targets)
            {
                if (target.root == null) continue;
                PEntity occupant = model.EntityAt(target.room, target.cell);
                bool matches = IsSatisfiedBy(occupant, target.kind, target.colour);
                bool arrived = matches && (instant || !views.TryGetValue(occupant, out var view)
                    || view == null || view.ReadyForGoalSeat);
                if (arrived)
                {
                    // Keep the fitted size AND centre from one socket together, including
                    // overlapping goals; another target must not overwrite just the offset.
                    if (!seated.TryGetValue(occupant, out Target existing)
                        || target.seatScale < existing.seatScale)
                        seated[occupant] = target;
                }
                if (arrived && !target.filled && !instant) target.flash = 1f;
                if (!arrived) target.flash = 0f;
                target.filled = arrived;
                target.fill = instant ? (arrived ? 1f : 0f)
                    : Mathf.MoveTowards(target.fill, arrived ? 1f : 0f,
                        dt / (arrived ? 0.22f : 0.12f));
                target.flash = Mathf.Max(0f, target.flash - dt / 0.38f);
                PaintTarget(target);
            }

            // Compute the union first: moving directly between two goals (or overlapping goals)
            // must not let the old goal cancel the new goal's seating animation.
            foreach (var pair in views)
                if (pair.Value != null)
                {
                    bool occupied = seated.TryGetValue(pair.Key, out Target target);
                    pair.Value.SetGoalSeated(occupied, instant,
                        occupied ? target.seatScale : 1f,
                        occupied ? target.seatOffset : Vector2.zero);
                }
        }

        static void BuildTarget(Target target, Sprite cellSprite)
        {
            target.premium = target.root.GetComponent<PremiumCargoSocket>();
            target.rim = Part(target.root, "Ring");
            target.glow = Part(target.root, "Glow");
            target.socket = Part(target.root, "Socket");
            if (target.rim != null) target.emptyRim = target.rim.color;
            if (target.glow != null) target.emptyGlow = target.glow.color;
            if (target.socket != null) target.emptySocket = target.socket.color;
            // No completion badge, tick, corner lights or lock: the subtle socket light carries
            // the feedback, leaving the piece's face clean and visibly free to move.
        }

        static void PaintTarget(Target target)
        {
            float filled = Mathf.SmoothStep(0f, 1f, target.fill);
            if (target.premium != null) target.premium.SetGoalFill(filled);
            float pulse = Mathf.Sin(target.flash * Mathf.PI);
            Color light = Color.Lerp(target.accent, Color.white, 0.48f);
            light.a = 1f;
            if (target.rim != null)
                target.rim.color = Color.Lerp(target.emptyRim, light, filled);
            if (target.glow != null)
            {
                Color glow = light;
                glow.a = 0.28f + pulse * 0.12f;
                target.glow.color = Color.Lerp(target.emptyGlow, glow, filled);
            }
            if (target.socket != null)
                target.socket.color = Color.Lerp(target.emptySocket,
                    new Color(0.012f, 0.040f, 0.075f, 1f), filled);
        }

        static SpriteRenderer Part(GameObject root, string name)
        {
            Transform child = root.transform.Find(name);
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

    }
}
