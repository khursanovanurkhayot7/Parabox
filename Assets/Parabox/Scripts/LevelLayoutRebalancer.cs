using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Adds clean physical restrictions to every shipped board without editing the source prefabs.
    // This keeps the ASCII level definitions readable and means regenerating assets cannot erase
    // the rebalance. Main-menu previews and gameplay both use LevelParser, so they see one layout.
    public static class LevelLayoutRebalancer
    {
        public static float TargetRatingForLevel(int levelIndex)
            => CampaignProgression.RatingFor(levelIndex);

        // Deliberate cross-mechanic dependency beats. These are sparse by design: a newly taught
        // rule stays readable, then selected later boards ask the player to reuse crate planning
        // while solving the board's headline mechanic. The values are maximums; every inserted
        // objective is replay-tested against the authored winning line before it is retained.
        public static int DependencyBudgetForLevel(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 0, 49))
            {
                // Chapter I: one surprise after the basic push language is understood.
                case 5: case 9:
                    return 1;

                // Chapter II: practice old cargo knowledge inside the new system puzzles.
                case 13: case 17: case 19:
                    return 1;

                // Chapter III: the reported difficulty dips receive an explicit dependency beat.
                // Level 30 is the chapter synthesis board, so it carries two commitments.
                case 21: case 22: case 24: case 26: case 27: case 28:
                    return 1;
                case 29:
                    return 2;

                // Chapter IV: mastery boards carry two interdependent commitments where space
                // permits, while the remaining boards keep their authored focal mechanic clean.
                case 31: case 34:
                    return 1;
                case 35: case 37: case 38: case 39:
                    return 2;

                // Chapter V is a continuous synthesis ladder, not ten copies of the same chamber.
                // The budget rises gradually so every level asks for more sequencing than the one
                // before it while the proven solution remains the source of truth.
                case 40:
                    return 1;
                case 41: case 42: case 43: case 44: case 45:
                    return 2;
                case 46: case 47: case 48:
                    return 3;
                case 49:
                    return 4;
                default:
                    return 0;
            }
        }

        // After one-way movement is taught on Level 24, selected combination/mastery boards may
        // reuse it. Chokes are placed only on cells the proven solution always enters from the same
        // direction, so they create planning constraints without introducing a guessing trap.
        public static int LearnedOneWayBudgetForLevel(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex, 0, 49))
            {
                case 24: case 26: case 27:
                    return 1;
                case 29:
                    return 3;
                case 31: case 35:
                    return 1;
                case 34: case 37: case 39:
                    return 2;
                case 40: case 41: case 42:
                    return 1;
                case 45: case 46: case 47: case 48:
                    return 2;
                case 49:
                    return 3;
                default:
                    return 0;
            }
        }

        struct Candidate
        {
            public int room;
            public Vector2Int cell;
            public int routeDistance;
            public int score;
        }

        enum HazardKind
        {
            Trench,
            Deep,
            Cracked,
            Rock,
            Cage,
            Sticky,
            Sand,
        }

        sealed class Trace
        {
            public readonly HashSet<(int room, Vector2Int cell)> protectedCells =
                new HashSet<(int, Vector2Int)>();
            public readonly HashSet<(int room, Vector2Int cell)> initialOccupants =
                new HashSet<(int, Vector2Int)>();
            public readonly Dictionary<(int room, Vector2Int cell), HashSet<int>> arrivals =
                new Dictionary<(int, Vector2Int), HashSet<int>>();
            public readonly List<RouteStep> playerSteps = new List<RouteStep>();
            public readonly Dictionary<(int room, Vector2Int cell), int> playerFirstVisit =
                new Dictionary<(int, Vector2Int), int>();
            public readonly HashSet<(int room, Vector2Int cell)> playerVisited =
                new HashSet<(int, Vector2Int)>();
            public readonly List<EntityRouteStep> entitySteps = new List<EntityRouteStep>();
            public readonly Dictionary<PEntity, (int room, Vector2Int cell, bool sunk)> finalPositions =
                new Dictionary<PEntity, (int, Vector2Int, bool)>();
            public bool valid;
        }

        struct RouteStep
        {
            public int index;
            public int room;
            public int toRoom;
            public Vector2Int from;
            public Vector2Int to;
            public Vector2Int direction;
        }

        struct EntityRouteStep
        {
            public int index;
            public PEntity entity;
            public int room;
            public int toRoom;
            public Vector2Int from;
            public Vector2Int to;
        }

        public static void Apply(LevelModel model, int levelIndex, string solution)
        {
            if (model == null || model.player == null || levelIndex < 0 || levelIndex >= 50
                || string.IsNullOrEmpty(solution)) return;

            Trace trace = TraceSolution(model, solution);
            if (!trace.valid) return; // the campaign validator reports the broken source route

            // Intentionally no procedural additions. The previous pass raised difficulty by
            // scattering walls, arrows, hazards and objectives onto a valid route. That made the
            // board busier, not smarter. The new campaign's difficulty is authored in its mechanic
            // relationships (and, in the finale, its nested chambers) and is verified by the
            // stored solution above.
            model.rebalanceWalls = 0;
            model.rebalanceOneWays = 0;
            model.rebalanceHazards = 0;
            model.rebalanceObjectives = 0;
            model.curriculumReuses.Clear();
        }

        static Trace TraceSolution(LevelModel model, string solution)
        {
            var trace = new Trace();
            trace.playerVisited.Add((model.player.roomId, model.player.pos));
            trace.playerFirstVisit[(model.player.roomId, model.player.pos)] = -1;
            foreach (var entity in model.entities)
            {
                var key = (entity.roomId, entity.pos);
                trace.initialOccupants.Add(key);
                Protect(trace, entity.roomId, entity.pos);
            }

            int completedMoves = 0;
            for (int step = 0; step < solution.Length; step++)
            {
                if (!TryDirection(solution[step], out var direction)) break;
                int playerRoom = model.player.roomId;
                Vector2Int playerCell = model.player.pos;
                var before = Capture(model);
                if (!model.TryMovePlayer(direction)) break;
                completedMoves++;

                trace.playerSteps.Add(new RouteStep
                {
                    index = step,
                    room = playerRoom,
                    toRoom = model.player.roomId,
                    from = playerCell,
                    to = model.player.pos,
                    direction = direction,
                });
                var playerKey = (model.player.roomId, model.player.pos);
                trace.playerVisited.Add(playerKey);
                if (!trace.playerFirstVisit.ContainsKey(playerKey))
                    trace.playerFirstVisit[playerKey] = step;

                foreach (var entity in model.entities)
                {
                    var old = before[entity];
                    if (old.room == entity.roomId && old.cell == entity.pos && old.sunk == entity.sunk)
                        continue;

                    Protect(trace, old.room, old.cell);
                    Protect(trace, entity.roomId, entity.pos);
                    if (old.room == entity.roomId)
                        ProtectSegment(trace, old.room, old.cell, entity.pos);

                    Vector2Int arrival = direction;
                    if (old.room == entity.roomId)
                    {
                        Vector2Int delta = entity.pos - old.cell;
                        if (delta.x == 0 && delta.y != 0) arrival = new Vector2Int(0, delta.y > 0 ? 1 : -1);
                        else if (delta.y == 0 && delta.x != 0) arrival = new Vector2Int(delta.x > 0 ? 1 : -1, 0);
                        else if (delta.x != 0 && delta.y != 0) continue; // a curved settle path: don't constrain its end
                    }
                    RecordArrival(trace, entity.roomId, entity.pos, arrival);
                    trace.entitySteps.Add(new EntityRouteStep
                    {
                        index = step,
                        entity = entity,
                        room = old.room,
                        toRoom = entity.roomId,
                        from = old.cell,
                        to = entity.pos,
                    });
                }
            }

            trace.valid = completedMoves == solution.Length && model.IsWon();
            foreach (PEntity entity in model.entities)
                trace.finalPositions[entity] = (entity.roomId, entity.pos, entity.sunk);
            while (model.MoveCount > 0) model.Undo();
            return trace;
        }

        static void AddCurriculumRehearsals(LevelModel model, int levelIndex, string solution,
                                            Trace trace)
        {
            foreach (MechanicCatalog.Id mechanic in MechanicCatalog.RehearsalsAt(levelIndex))
            {
                bool retained;
                switch (mechanic)
                {
                    case MechanicCatalog.Id.Geyser:
                        retained = TryPlayerTerrain(model, solution, trace, mechanic, 503);
                        break;
                    case MechanicCatalog.Id.Ice:
                        retained = TryPlayerTerrain(model, solution, trace, mechanic, 509,
                            requireStraightContinuation: true);
                        break;
                    case MechanicCatalog.Id.StickyFloor:
                        retained = TryPlayerTerrain(model, solution, trace, mechanic, 521,
                            requireStraightContinuation: true);
                        break;
                    case MechanicCatalog.Id.NarrowGap:
                        retained = TryCargoTerrain(model, solution, trace, mechanic, 523);
                        break;
                    case MechanicCatalog.Id.GravityWell:
                        retained = TryCargoTerrain(model, solution, trace, mechanic, 541);
                        if (retained) model.hasGravity = true;
                        break;
                    case MechanicCatalog.Id.Updraft:
                        retained = TryCargoTerrain(model, solution, trace, mechanic, 547);
                        break;
                    case MechanicCatalog.Id.SlidingCargo:
                        retained = TryCargoVariant(model, solution, trace, mechanic);
                        break;
                    case MechanicCatalog.Id.LockingCargo:
                        retained = TryCargoVariant(model, solution, trace, mechanic);
                        break;
                    case MechanicCatalog.Id.ColourCargo:
                        retained = TryColourCargo(model, solution, trace);
                        break;
                    case MechanicCatalog.Id.Magnet:
                        retained = TryMagnet(model, solution, trace, levelIndex);
                        break;
                    case MechanicCatalog.Id.OneWay:
                        retained = TryOneWay(model, solution, trace, levelIndex);
                        break;
                    default:
                        retained = false;
                        break;
                }

                if (retained) model.curriculumReuses.Add(mechanic);
            }
        }

        static bool TryPlayerTerrain(LevelModel model, string solution, Trace trace,
                                     MechanicCatalog.Id mechanic, int salt,
                                     bool requireStraightContinuation = false)
        {
            var candidates = new List<RouteStep>();
            for (int i = 0; i < trace.playerSteps.Count; i++)
            {
                RouteStep step = trace.playerSteps[i];
                if (step.room != step.toRoom || step.to - step.from != step.direction) continue;
                if (!model.rooms.TryGetValue(step.room, out PRoom room)) continue;
                if (!room.InBounds(step.to) || HasFeature(model, room, step.to)) continue;
                if (trace.initialOccupants.Contains((step.room, step.to))) continue;
                if (requireStraightContinuation)
                {
                    if (i + 1 >= trace.playerSteps.Count
                        || trace.playerSteps[i + 1].direction != step.direction) continue;
                }
                candidates.Add(step);
            }
            candidates.Sort((a, b) => StableHash(salt, a.room, a.to.x, a.to.y)
                .CompareTo(StableHash(salt, b.room, b.to.x, b.to.y)));

            foreach (RouteStep step in candidates)
            {
                PRoom room = model.rooms[step.room];
                SetTerrain(room, step.to, mechanic, true);
                if (SolutionStillWins(model, solution)) return true;
                SetTerrain(room, step.to, mechanic, false);
            }
            return false;
        }

        static bool TryCargoTerrain(LevelModel model, string solution, Trace trace,
                                    MechanicCatalog.Id mechanic, int salt)
        {
            var candidates = new List<EntityRouteStep>();
            // Prefer the final cargo target. This creates an honest interaction (the crate must
            // enter the field/gap), while nearby architecture can arrest gravity/updraft motion.
            // It is especially readable for NarrowGap: cargo visibly occupies a cell the diver
            // never uses.
            foreach (var pair in trace.finalPositions)
            {
                PEntity entity = pair.Key;
                var final = pair.Value;
                if (entity == null || !entity.IsCrate || entity.interiorRoomId >= 0 || final.sunk
                    || !model.rooms.TryGetValue(final.room, out PRoom room)
                    || !room.boxGoals.Contains(final.cell)
                    || HasFeatureIgnoringBoxGoal(model, room, final.cell)
                    || trace.playerVisited.Contains((final.room, final.cell))) continue;
                candidates.Add(new EntityRouteStep
                {
                    index = int.MaxValue,
                    entity = entity,
                    room = final.room,
                    toRoom = final.room,
                    from = final.cell,
                    to = final.cell,
                });
            }
            foreach (EntityRouteStep step in trace.entitySteps)
            {
                if (step.entity == null || !step.entity.IsCrate || step.entity.interiorRoomId >= 0
                    || step.entity.sunk || step.room != step.toRoom) continue;
                if (!model.rooms.TryGetValue(step.toRoom, out PRoom room)
                    || !room.InBounds(step.to) || HasFeature(model, room, step.to)) continue;
                // Narrow gaps teach a genuine cargo-only route: never put one on a cell used by
                // the diver. Other cargo fields also stay readable by following a moved crate.
                if (trace.playerVisited.Contains((step.toRoom, step.to))) continue;
                candidates.Add(step);
            }
            candidates.Sort((a, b) => StableHash(salt, a.toRoom, a.to.x, a.to.y)
                .CompareTo(StableHash(salt, b.toRoom, b.to.x, b.to.y)));

            foreach (EntityRouteStep step in candidates)
            {
                PRoom room = model.rooms[step.toRoom];
                SetTerrain(room, step.to, mechanic, true);
                bool previousGravity = model.hasGravity;
                if (mechanic == MechanicCatalog.Id.GravityWell) model.hasGravity = true;
                Vector2Int supportDirection = Vector2Int.zero;
                if (step.index == int.MaxValue)
                {
                    if (mechanic == MechanicCatalog.Id.GravityWell)
                        supportDirection = Vector2Int.down;
                    else if (mechanic == MechanicCatalog.Id.Updraft)
                        supportDirection = Vector2Int.up;
                    else if (mechanic == MechanicCatalog.Id.NarrowGap)
                    {
                        // A narrow gap continues cargo in its arrival direction. Support the far
                        // side of a target so the crate visibly enters the gap and comes to rest.
                        for (int i = trace.entitySteps.Count - 1; i >= 0; i--)
                        {
                            EntityRouteStep route = trace.entitySteps[i];
                            if (route.entity != step.entity || route.toRoom != step.toRoom
                                || route.to != step.to) continue;
                            Vector2Int delta = route.to - route.from;
                            supportDirection = new Vector2Int(
                                delta.x == 0 ? 0 : (delta.x > 0 ? 1 : -1),
                                delta.y == 0 ? 0 : (delta.y > 0 ? 1 : -1));
                            break;
                        }
                    }
                }

                Vector2Int support = step.to + supportDirection;
                bool addedSupport = supportDirection != Vector2Int.zero && room.InBounds(support)
                                    && !HasFeature(model, room, support);
                if (addedSupport) room.wall[support.x, support.y] = true;
                if (SolutionStillWins(model, solution)) return true;
                if (addedSupport)
                {
                    room.wall[support.x, support.y] = false;
                    // Some authored boards already provide a support farther along the settle
                    // path. Do not make the helper wall mandatory if the mechanic works cleanly
                    // without it.
                    if (SolutionStillWins(model, solution)) return true;
                }
                model.hasGravity = previousGravity;
                SetTerrain(room, step.to, mechanic, false);
            }
            return false;
        }

        static bool HasFeatureIgnoringBoxGoal(LevelModel model, PRoom room, Vector2Int cell)
        {
            bool removed = room.boxGoals.Remove(cell);
            bool result = HasFeature(model, room, cell);
            if (removed) room.boxGoals.Add(cell);
            return result;
        }

        static bool TryCargoVariant(LevelModel model, string solution, Trace trace,
                                    MechanicCatalog.Id mechanic)
        {
            var candidates = new List<PEntity>();
            foreach (EntityRouteStep step in trace.entitySteps)
                if (step.entity != null && step.entity.IsCrate && step.entity.interiorRoomId < 0
                    && !candidates.Contains(step.entity)) candidates.Add(step.entity);

            foreach (PEntity crate in candidates)
            {
                bool old = mechanic == MechanicCatalog.Id.SlidingCargo ? crate.slick : crate.locking;
                if (old) return true;
                if (mechanic == MechanicCatalog.Id.SlidingCargo) crate.slick = true;
                else crate.locking = true;
                if (SolutionStillWins(model, solution)) return true;
                if (mechanic == MechanicCatalog.Id.SlidingCargo) crate.slick = old;
                else crate.locking = old;
            }
            return false;
        }

        static bool TryColourCargo(LevelModel model, string solution, Trace trace)
        {
            foreach (PEntity crate in model.entities)
            {
                if (!crate.IsCrate || crate.interiorRoomId >= 0 || crate.colour > 0
                    || !trace.finalPositions.TryGetValue(crate, out var final) || final.sunk
                    || !model.rooms.TryGetValue(final.room, out PRoom room)
                    || !room.boxGoals.Contains(final.cell)) continue;

                room.boxGoals.Remove(final.cell);
                room.colourGoals.Add((final.cell, 1));
                crate.colour = 1;
                if (SolutionStillWins(model, solution)) return true;
                crate.colour = 0;
                room.colourGoals.Remove((final.cell, 1));
                room.boxGoals.Add(final.cell);
            }
            return false;
        }

        static bool TryMagnet(LevelModel model, string solution, Trace trace, int levelIndex)
        {
            var candidates = new List<Candidate>();
            foreach (PRoom room in model.rooms.Values)
                for (int x = 1; x < room.width - 1; x++)
                    for (int y = 1; y < room.height - 1; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (HasFeature(model, room, cell)) continue;
                        bool aligned = false;
                        foreach (EntityRouteStep step in trace.entitySteps)
                            if (step.entity != null && step.entity.IsCrate && step.toRoom == room.id
                                && (step.to.x == x || step.to.y == y)) { aligned = true; break; }
                        if (!aligned) continue;
                        candidates.Add(new Candidate
                        {
                            room = room.id, cell = cell, routeDistance = 0,
                            score = StableHash(levelIndex + 557, room.id, x, y),
                        });
                    }
            SortCandidates(candidates);
            foreach (Candidate candidate in candidates)
            {
                PRoom room = model.rooms[candidate.room];
                Ensure(ref room.magnet, room)[candidate.cell.x, candidate.cell.y] = true;
                bool old = model.hasMagnet;
                model.hasMagnet = true;
                if (SolutionStillWins(model, solution)) return true;
                model.hasMagnet = old;
                room.magnet[candidate.cell.x, candidate.cell.y] = false;
            }
            return false;
        }

        static bool TryOneWay(LevelModel model, string solution, Trace trace, int levelIndex)
        {
            foreach (var pair in trace.arrivals)
            {
                if (pair.Value.Count != 1) continue;
                PRoom room = model.rooms[pair.Key.room];
                Vector2Int cell = pair.Key.cell;
                if (!room.InBounds(cell) || HasFeature(model, room, cell)) continue;
                int code = -1;
                foreach (int value in pair.Value) { code = value; break; }
                Vector2Int direction = DecodeDirection(code);
                if (direction == Vector2Int.zero) continue;
                if (room.oneway == null) room.oneway = new Vector2Int[room.width, room.height];
                room.oneway[cell.x, cell.y] = direction;
                if (SolutionStillWins(model, solution)) return true;
                room.oneway[cell.x, cell.y] = Vector2Int.zero;
            }
            return false;
        }

        static void SetTerrain(PRoom room, Vector2Int cell, MechanicCatalog.Id mechanic, bool value)
        {
            switch (mechanic)
            {
                case MechanicCatalog.Id.Geyser:
                    Ensure(ref room.geyser, room)[cell.x, cell.y] = value; break;
                case MechanicCatalog.Id.Ice:
                    Ensure(ref room.ice, room)[cell.x, cell.y] = value; break;
                case MechanicCatalog.Id.StickyFloor:
                    Ensure(ref room.sticky, room)[cell.x, cell.y] = value; break;
                case MechanicCatalog.Id.NarrowGap:
                    Ensure(ref room.gap, room)[cell.x, cell.y] = value; break;
                case MechanicCatalog.Id.GravityWell:
                    Ensure(ref room.gravity, room)[cell.x, cell.y] = value; break;
                case MechanicCatalog.Id.Updraft:
                    Ensure(ref room.updraft, room)[cell.x, cell.y] = value; break;
            }
        }

        static void AddMandatoryRouteObjectives(LevelModel model, int levelIndex, string solution,
                                                Trace trace, int budget)
        {
            if (budget <= 0) return;

            var candidates = new List<RouteStep>();
            foreach (RouteStep step in trace.playerSteps)
            {
                // Settling, portals and room transitions can move farther than one cell.  Those
                // mechanics already carry enough cognitive load; only plain one-cell steps are
                // safe places for a transparent crate dependency.
                if (step.room != step.toRoom) continue;
                if (step.to - step.from != step.direction) continue;
                if (!model.rooms.TryGetValue(step.room, out PRoom room)) continue;
                if (!trace.playerFirstVisit.TryGetValue((step.room, step.to), out int first)
                    || first != step.index) continue;

                Vector2Int parking = step.to + step.direction;
                if (!room.InBounds(parking)) continue;
                // A later revisit is allowed. The replay check below is the authority: if the
                // parked crate blocks a required return route, this candidate is rejected. This
                // opens useful multi-stage candidates that the old conservative filter discarded.
                if (trace.initialOccupants.Contains((step.room, step.to))) continue;
                if (HasNonPlayerFeature(model, room, step.to)) continue;
                if (HasFeature(model, room, parking)) continue;

                candidates.Add(step);
            }

            // Later steps are preferable: a failed push then costs more planning, while the
            // deterministic hash prevents every board from placing its objective in the same
            // visual corner.
            candidates.Sort((a, b) =>
            {
                int late = b.index.CompareTo(a.index);
                return late != 0 ? late
                    : StableHash(levelIndex + 401, a.room, a.to.x, a.to.y)
                        .CompareTo(StableHash(levelIndex + 401, b.room, b.to.x, b.to.y));
            });

            int added = 0;
            foreach (RouteStep step in candidates)
            {
                if (added >= budget) break;
                PRoom room = model.rooms[step.room];
                Vector2Int parking = step.to + step.direction;
                if (model.EntityAt(step.room, step.to) != null || HasFeature(model, room, parking))
                    continue;

                var crate = new PEntity { roomId = step.room, pos = step.to };
                model.entities.Add(crate);
                room.boxGoals.Add(parking);

                if (SolutionStillWins(model, solution))
                {
                    added++;
                    continue;
                }

                room.boxGoals.Remove(parking);
                model.entities.Remove(crate);
            }
            model.rebalanceObjectives += added;
        }

        static bool SolutionStillWins(LevelModel model, string solution)
        {
            int completed = 0;
            for (int i = 0; i < solution.Length; i++)
            {
                if (!TryDirection(solution[i], out Vector2Int direction)
                    || !model.TryMovePlayer(direction)) break;
                completed++;
            }

            bool won = completed == solution.Length && model.IsWon();
            while (model.MoveCount > 0) model.Undo();
            return won;
        }

        static bool HasNonPlayerFeature(LevelModel model, PRoom room, Vector2Int cell)
        {
            bool wasPlayerGoal = room.playerGoals.Remove(cell);
            bool occupied = HasFeature(model, room, cell);
            if (wasPlayerGoal) room.playerGoals.Add(cell);
            return occupied;
        }

        static Dictionary<PEntity, (int room, Vector2Int cell, bool sunk)> Capture(LevelModel model)
        {
            var result = new Dictionary<PEntity, (int, Vector2Int, bool)>(model.entities.Count);
            foreach (var entity in model.entities)
                result[entity] = (entity.roomId, entity.pos, entity.sunk);
            return result;
        }

        static void Protect(Trace trace, int room, Vector2Int cell)
            => trace.protectedCells.Add((room, cell));

        static void ProtectSegment(Trace trace, int room, Vector2Int from, Vector2Int to)
        {
            if (from.x != to.x && from.y != to.y) return;
            Vector2Int step = new Vector2Int(
                from.x == to.x ? 0 : (to.x > from.x ? 1 : -1),
                from.y == to.y ? 0 : (to.y > from.y ? 1 : -1));
            var cell = from;
            Protect(trace, room, cell);
            while (cell != to)
            {
                cell += step;
                Protect(trace, room, cell);
            }
        }

        static void RecordArrival(Trace trace, int room, Vector2Int cell, Vector2Int direction)
        {
            int code = DirectionCode(direction);
            if (code < 0) return;
            var key = (room, cell);
            if (!trace.arrivals.TryGetValue(key, out var directions))
            {
                directions = new HashSet<int>();
                trace.arrivals[key] = directions;
            }
            directions.Add(code);
        }

        static void AddWallFormation(LevelModel model, int levelIndex, string solution, int budget, Trace trace)
        {
            var candidates = new List<Candidate>();
            foreach (var pair in model.rooms)
            {
                PRoom room = pair.Value;
                // Never close a boundary cell: nested-room entry and exit points live there.
                for (int x = 1; x < room.width - 1; x++)
                    for (int y = 1; y < room.height - 1; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (trace.protectedCells.Contains((room.id, cell))) continue;
                        if (HasFeature(model, room, cell)) continue;
                        candidates.Add(new Candidate
                        {
                            room = room.id,
                            cell = cell,
                            routeDistance = DistanceToRoute(trace, room.id, cell),
                            score = StableHash(levelIndex, room.id, x, y),
                        });
                    }
            }
            SortCandidates(candidates);

            int added = 0;
            var available = new HashSet<(int, Vector2Int)>();
            foreach (var candidate in candidates) available.Add((candidate.room, candidate.cell));

            bool verticalMirror = (levelIndex & 1) == 0;
            foreach (var candidate in candidates)
            {
                if (added >= budget) break;
                var key = (candidate.room, candidate.cell);
                if (!available.Remove(key)) continue;
                PRoom room = model.rooms[candidate.room];
                room.wall[candidate.cell.x, candidate.cell.y] = true;
                if (SolutionStillWins(model, solution))
                    added++;
                else
                {
                    room.wall[candidate.cell.x, candidate.cell.y] = false;
                    continue;
                }

                if (added >= budget) break;
                var mirror = verticalMirror
                    ? new Vector2Int(room.width - 1 - candidate.cell.x, candidate.cell.y)
                    : new Vector2Int(candidate.cell.x, room.height - 1 - candidate.cell.y);
                var mirrorKey = (candidate.room, mirror);
                if (mirror != candidate.cell && available.Remove(mirrorKey))
                {
                    room.wall[mirror.x, mirror.y] = true;
                    if (SolutionStillWins(model, solution))
                        added++;
                    else
                        room.wall[mirror.x, mirror.y] = false;
                }
            }
            model.rebalanceWalls = added;
        }

        static void AddDirectionalChokes(LevelModel model, int levelIndex, int budget, Trace trace)
        {
            var candidates = new List<Candidate>();
            foreach (var pair in trace.arrivals)
            {
                if (pair.Value.Count != 1) continue; // every visit must approach the same way
                int roomId = pair.Key.room;
                Vector2Int cell = pair.Key.cell;
                PRoom room = model.rooms[roomId];
                if (cell.x <= 0 || cell.x >= room.width - 1 || cell.y <= 0 || cell.y >= room.height - 1)
                    continue;
                if (trace.initialOccupants.Contains((roomId, cell))) continue;
                if (HasFeature(model, room, cell)) continue;
                candidates.Add(new Candidate
                {
                    room = roomId,
                    cell = cell,
                    routeDistance = 0,
                    score = StableHash(levelIndex + 97, roomId, cell.x, cell.y),
                });
            }
            SortCandidates(candidates);

            int added = 0;
            foreach (var candidate in candidates)
            {
                if (added >= budget) break;
                PRoom room = model.rooms[candidate.room];
                if (HasFeature(model, room, candidate.cell)) continue;
                int code = -1;
                foreach (int value in trace.arrivals[(candidate.room, candidate.cell)]) { code = value; break; }
                Vector2Int direction = DecodeDirection(code);
                if (direction == Vector2Int.zero) continue;
                if (room.oneway == null) room.oneway = new Vector2Int[room.width, room.height];
                room.oneway[candidate.cell.x, candidate.cell.y] = direction;
                added++;
            }
            model.rebalanceOneWays = added;
        }

        static bool HasAuthoredOneWay(LevelModel model)
        {
            foreach (PRoom room in model.rooms.Values)
            {
                if (room.oneway == null) continue;
                for (int x = 0; x < room.width; x++)
                    for (int y = 0; y < room.height; y++)
                        if (room.oneway[x, y] != Vector2Int.zero) return true;
            }
            return false;
        }

        static bool TryFocusedHazard(LevelModel model, out HazardKind kind)
        {
            foreach (PRoom room in model.rooms.Values)
            {
                if (HasAny(room.trench)) { kind = HazardKind.Trench; return true; }
                if (HasAny(room.deep))    { kind = HazardKind.Deep; return true; }
                if (HasAny(room.cracked)) { kind = HazardKind.Cracked; return true; }
                if (HasAny(room.rock))    { kind = HazardKind.Rock; return true; }
                if (HasAny(room.cage))    { kind = HazardKind.Cage; return true; }
                if (HasAny(room.sticky))  { kind = HazardKind.Sticky; return true; }
                if (HasAny(room.sand))    { kind = HazardKind.Sand; return true; }
            }
            kind = default;
            return false;
        }

        static bool HasAny(bool[,] cells)
        {
            if (cells == null) return false;
            for (int x = 0; x < cells.GetLength(0); x++)
                for (int y = 0; y < cells.GetLength(1); y++)
                    if (cells[x, y]) return true;
            return false;
        }

        static void AddPenaltyHazards(LevelModel model, int levelIndex, int budget, Trace trace,
                                      HazardKind? focusedKind = null)
        {
            if (budget <= 0) return;

            var candidates = new List<Candidate>();
            foreach (var pair in model.rooms)
            {
                PRoom room = pair.Value;
                // Boundary cells are reserved for nested-room entry and exit. The most useful
                // challenge cells are one step from the winning route: they are visible choices,
                // not decoration hidden in an irrelevant corner.
                for (int x = 1; x < room.width - 1; x++)
                    for (int y = 1; y < room.height - 1; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (trace.protectedCells.Contains((room.id, cell))) continue;
                        if (trace.initialOccupants.Contains((room.id, cell))) continue;
                        if (HasFeature(model, room, cell)) continue;
                        candidates.Add(new Candidate
                        {
                            room = room.id,
                            cell = cell,
                            routeDistance = DistanceToRoute(trace, room.id, cell),
                            score = StableHash(levelIndex + 211, room.id, x, y),
                        });
                    }
            }
            SortCandidates(candidates);

            int added = 0;
            foreach (var candidate in candidates)
            {
                if (added >= budget) break;
                PRoom room = model.rooms[candidate.room];
                if (HasFeature(model, room, candidate.cell)) continue;
                PlaceHazard(room, candidate.cell,
                    focusedKind.HasValue ? focusedKind.Value : HazardFor(levelIndex, added));
                added++;
            }
            model.rebalanceHazards = added;
        }

        // Hazards never appear before their authored teaching level: trench L12, deep L16,
        // cracked coral L19, rock L23, cage L33, sticky L34, and sand L37. Later chapters mix the
        // full learned vocabulary, which is where the campaign becomes a mastery test.
        static HazardKind HazardFor(int levelIndex, int ordinal)
        {
            HazardKind[] palette;
            if (levelIndex < 15) palette = new[] { HazardKind.Trench };
            else if (levelIndex < 18) palette = new[] { HazardKind.Deep, HazardKind.Trench };
            else if (levelIndex < 22) palette = new[] { HazardKind.Cracked, HazardKind.Deep, HazardKind.Trench };
            else if (levelIndex < 32) palette = new[] { HazardKind.Rock, HazardKind.Cracked, HazardKind.Deep, HazardKind.Trench };
            else if (levelIndex < 33) palette = new[] { HazardKind.Cage, HazardKind.Rock, HazardKind.Deep };
            else if (levelIndex < 36) palette = new[] { HazardKind.Sticky, HazardKind.Cage, HazardKind.Rock, HazardKind.Deep };
            else palette = new[]
            {
                HazardKind.Sand, HazardKind.Sticky, HazardKind.Cage, HazardKind.Rock,
                HazardKind.Cracked, HazardKind.Deep, HazardKind.Trench,
            };
            return palette[(levelIndex + ordinal) % palette.Length];
        }

        static void PlaceHazard(PRoom room, Vector2Int cell, HazardKind kind)
        {
            switch (kind)
            {
                case HazardKind.Trench:
                    Ensure(ref room.trench, room)[cell.x, cell.y] = true;
                    break;
                case HazardKind.Deep:
                    Ensure(ref room.deep, room)[cell.x, cell.y] = true;
                    break;
                case HazardKind.Cracked:
                    Ensure(ref room.cracked, room)[cell.x, cell.y] = true;
                    break;
                case HazardKind.Rock:
                    Ensure(ref room.rock, room)[cell.x, cell.y] = true;
                    break;
                case HazardKind.Cage:
                    Ensure(ref room.cage, room)[cell.x, cell.y] = true;
                    break;
                case HazardKind.Sticky:
                    Ensure(ref room.sticky, room)[cell.x, cell.y] = true;
                    break;
                case HazardKind.Sand:
                    Ensure(ref room.sand, room)[cell.x, cell.y] = true;
                    break;
            }
        }

        static bool[,] Ensure(ref bool[,] cells, PRoom room)
        {
            if (cells == null) cells = new bool[room.width, room.height];
            return cells;
        }

        static int DistanceToRoute(Trace trace, int roomId, Vector2Int cell)
        {
            int best = int.MaxValue;
            foreach (var routeCell in trace.protectedCells)
            {
                if (routeCell.Item1 != roomId) continue;
                int distance = Mathf.Abs(routeCell.Item2.x - cell.x) + Mathf.Abs(routeCell.Item2.y - cell.y);
                if (distance < best) best = distance;
            }
            return best;
        }

        static void SortCandidates(List<Candidate> candidates)
        {
            candidates.Sort((a, b) =>
            {
                int distance = a.routeDistance.CompareTo(b.routeDistance);
                return distance != 0 ? distance : a.score.CompareTo(b.score);
            });
        }

        static bool HasFeature(LevelModel model, PRoom room, Vector2Int cell)
        {
            if (room.IsWall(cell) || model.EntityAt(room.id, cell) != null) return true;
            if (room.boxGoals.Contains(cell) || room.playerGoals.Contains(cell)
                || room.echoGoals.Contains(cell) || room.mirrorGoals.Contains(cell)) return true;
            foreach (var goal in room.colourGoals) if (goal.cell == cell) return true;

            return (room.trench != null && room.trench[cell.x, cell.y])
                   || (room.portal != null && room.portal[cell.x, cell.y])
                   || (room.ice != null && room.ice[cell.x, cell.y])
                   || (room.current != null && room.current[cell.x, cell.y] != Vector2Int.zero)
                   || (room.oneway != null && room.oneway[cell.x, cell.y] != Vector2Int.zero)
                   || (room.cracked != null && room.cracked[cell.x, cell.y])
                   || (room.button != null && room.button[cell.x, cell.y])
                   || (room.gate != null && room.gate[cell.x, cell.y])
                   || (room.plate != null && room.plate[cell.x, cell.y])
                   || (room.heavyGate != null && room.heavyGate[cell.x, cell.y])
                   || (room.kelp != null && room.kelp[cell.x, cell.y])
                   || (room.deep != null && room.deep[cell.x, cell.y])
                   || (room.key != null && room.key[cell.x, cell.y])
                   || (room.locked != null && room.locked[cell.x, cell.y])
                   || (room.geyser != null && room.geyser[cell.x, cell.y])
                   || (room.gap != null && room.gap[cell.x, cell.y])
                   || (room.gravity != null && room.gravity[cell.x, cell.y])
                   || (room.rock != null && room.rock[cell.x, cell.y])
                   || (room.toggle != null && room.toggle[cell.x, cell.y])
                   || (room.latch != null && room.latch[cell.x, cell.y])
                   || (room.pulse != null && room.pulse[cell.x, cell.y])
                   || (room.updraft != null && room.updraft[cell.x, cell.y])
                   || (room.cage != null && room.cage[cell.x, cell.y])
                   || (room.deflector != null && room.deflector[cell.x, cell.y])
                   || (room.sand != null && room.sand[cell.x, cell.y])
                   || (room.magnet != null && room.magnet[cell.x, cell.y])
                   || (room.sticky != null && room.sticky[cell.x, cell.y])
                   || (room.swap != null && room.swap[cell.x, cell.y]);
        }

        static int StableHash(int level, int room, int x, int y)
        {
            unchecked
            {
                int hash = (level + 1) * 73856093;
                hash ^= (room + 3) * 19349663;
                hash ^= (x + 11) * 83492791;
                hash ^= (y + 17) * 265443576;
                return hash & int.MaxValue;
            }
        }

        static bool TryDirection(char move, out Vector2Int direction)
        {
            switch (move)
            {
                case 'U': direction = Vector2Int.up; return true;
                case 'D': direction = Vector2Int.down; return true;
                case 'L': direction = Vector2Int.left; return true;
                case 'R': direction = Vector2Int.right; return true;
                default: direction = Vector2Int.zero; return false;
            }
        }

        static int DirectionCode(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return 0;
            if (direction == Vector2Int.right) return 1;
            if (direction == Vector2Int.down) return 2;
            if (direction == Vector2Int.left) return 3;
            return -1;
        }

        static Vector2Int DecodeDirection(int code)
        {
            switch (code)
            {
                case 0: return Vector2Int.up;
                case 1: return Vector2Int.right;
                case 2: return Vector2Int.down;
                case 3: return Vector2Int.left;
                default: return Vector2Int.zero;
            }
        }
    }
}
