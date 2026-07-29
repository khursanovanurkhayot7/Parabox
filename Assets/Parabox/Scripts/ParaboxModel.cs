using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Pure logical state — no Unity scene objects in here.

    public class PEntity
    {
        public bool isPlayer;
        public int interiorRoomId = -1; // >= 0 means this box contains that room
        public int roomId;              // which room the entity currently stands in
        public Vector2Int pos;          // grid cell inside that room
        public bool sunk;               // a box that fell into a trench — off the board, hidden, immovable
        public bool isEcho;             // the shadow diver: copies the player's move, one step behind
        public int colour;              // 0 = an ordinary crate; > 0 = must land on ITS colour of goal
        public bool slick;              // a crate that keeps sliding once shoved
        public bool boulder;            // only shifts if the pusher was already moving that way
        public bool isMirror;           // the diver that moves the OPPOSITE way to you
        public bool locking;            // sets in stone the moment it reaches a mark
        public bool fragile;            // survives one shove, shatters on the second
        public bool anchored;           // a meta-box bolted down: enterable, but never pushable

        // Crates and meta-boxes. The player and the echo are divers, not cargo — this is what
        // separates "can be pushed / counts as weight / satisfies a crate goal" from "cannot".
        public bool IsCrate => !isPlayer && !isEcho && !isMirror;
    }

    public class PRoom
    {
        public int id;
        public int width;
        public int height;
        public bool[,] wall;
        public bool[,] trench;          // a gap; a box pushed in sinks and fills it (null = no trenches)
        public bool[,] portal;          // whirlpool cells — entering one relocates you to its pair (null = none)
        public bool[,] ice;             // slippery cells — you keep sliding while standing on one (null = none)
        public Vector2Int[,] current;   // carry direction per cell; zero = not a current (null = none)
        public Vector2Int[,] oneway;    // the ONLY direction this cell may be entered from; zero = free
        public bool[,] cracked;         // coral that collapses into a hole once its rider leaves
        public bool[,] button;          // anything standing here opens every gate
        public bool[,] gate;            // solid unless some button is held
        public bool[,] plate;           // only a CRATE here opens every heavyGate
        public bool[,] heavyGate;       // solid unless some plate is held
        public bool[,] kelp;            // a thicket only the diver slips through
        public bool[,] deep;            // the diver swims over; a crate pushed in is lost for good
        public bool[,] key;             // pearls the diver collects by coming to rest on them
        public bool[,] locked;          // solid until every pearl on the board has been collected
        public bool[,] geyser;          // landing here launches you two cells on, over the middle
        public bool[,] gap;             // too narrow for a diver; a crate slides straight through
        public bool[,] gravity;         // a crate left standing here sinks until something holds it
        public bool[,] rock;            // a crate shoved into it shatters both; a diver just bounces
        public bool[,] toggle;          // step on one and every latch gate flips, and stays flipped
        public bool[,] latch;           // solid until a toggle has been stepped on
        public bool[,] pulse;           // open on every other move
        public bool[,] updraft;         // cargo left standing here floats upward
        public bool[,] cage;            // a crate can be pushed in, but never back out
        public bool[,] deflector;       // turns you a quarter turn clockwise and sends you on
        public bool[,] sand;            // no purchase: you may walk it, but not push from it
        public bool[,] magnet;          // drags crates on its row/column one cell closer each move
        public bool[,] sticky;          // leaving one forces your next move to repeat that way
        public bool[,] swap;            // step on one and you trade places with its twin
        public readonly HashSet<Vector2Int> smashed = new HashSet<Vector2Int>();  // rock already broken
        public readonly HashSet<Vector2Int> filled = new HashSet<Vector2Int>();  // trench cells now bridged
        public readonly HashSet<Vector2Int> broken = new HashSet<Vector2Int>();  // coral already collapsed
        public List<Vector2Int> boxGoals = new List<Vector2Int>();
        public List<Vector2Int> playerGoals = new List<Vector2Int>();
        public List<Vector2Int> echoGoals = new List<Vector2Int>();
        public List<Vector2Int> mirrorGoals = new List<Vector2Int>();
        // A coloured goal accepts exactly one crate: the one wearing its colour.
        public readonly List<(Vector2Int cell, int colour)> colourGoals = new List<(Vector2Int, int)>();
        public PEntity containerBox;    // the meta-box whose inside is this room (null = main room)

        public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;
        public bool IsWall(Vector2Int p) => wall[p.x, p.y];
        // An OPEN trench blocks like a wall until a box fills it; a FILLED one is ordinary floor.
        public bool IsOpenTrench(Vector2Int p) => trench != null && trench[p.x, p.y] && !filled.Contains(p);
        public bool IsIce(Vector2Int p) => ice != null && ice[p.x, p.y];
        public bool IsCracked(Vector2Int p) => cracked != null && cracked[p.x, p.y];
        public bool IsBroken(Vector2Int p) => broken.Contains(p);
        public bool IsButton(Vector2Int p) => button != null && button[p.x, p.y];
        public bool IsPlate(Vector2Int p) => plate != null && plate[p.x, p.y];
        public Vector2Int Current(Vector2Int p) => current == null ? Vector2Int.zero : current[p.x, p.y];
        public Vector2Int OneWay(Vector2Int p) => oneway == null ? Vector2Int.zero : oneway[p.x, p.y];
        public bool IsKelp(Vector2Int p) => kelp != null && kelp[p.x, p.y];
        public bool IsDeep(Vector2Int p) => deep != null && deep[p.x, p.y];
        public bool IsKey(Vector2Int p) => key != null && key[p.x, p.y];
        public bool IsLocked(Vector2Int p) => locked != null && locked[p.x, p.y];
        public bool IsGeyser(Vector2Int p) => geyser != null && geyser[p.x, p.y];
        public bool IsGap(Vector2Int p) => gap != null && gap[p.x, p.y];
        public bool IsGravity(Vector2Int p) => gravity != null && gravity[p.x, p.y];
        public bool IsToggle(Vector2Int p) => toggle != null && toggle[p.x, p.y];
        public bool IsLatch(Vector2Int p) => latch != null && latch[p.x, p.y];
        public bool IsPulse(Vector2Int p) => pulse != null && pulse[p.x, p.y];
        public bool IsUpdraft(Vector2Int p) => updraft != null && updraft[p.x, p.y];
        public bool IsCage(Vector2Int p) => cage != null && cage[p.x, p.y];
        public bool IsDeflector(Vector2Int p) => deflector != null && deflector[p.x, p.y];
        public bool IsSand(Vector2Int p) => sand != null && sand[p.x, p.y];
        public bool IsSticky(Vector2Int p) => sticky != null && sticky[p.x, p.y];
        public bool IsSwap(Vector2Int p) => swap != null && swap[p.x, p.y];
        // An INTACT rock blocks like a wall; a smashed one is ordinary floor.
        public bool IsRock(Vector2Int p) => rock != null && rock[p.x, p.y] && !smashed.Contains(p);
    }

    public class LevelModel
    {
        public readonly Dictionary<int, PRoom> rooms = new Dictionary<int, PRoom>();
        public readonly List<PEntity> entities = new List<PEntity>();
        public PEntity player;

        // Whirlpool links: entering the key cell relocates the entity to the value cell. Both
        // directions are stored. Built by LevelParser (pairs sorted by roomId,x,y — the SAME order
        // the solver uses, so which end links to which is deterministic). Static: no undo state.
        public readonly Dictionary<(int, Vector2Int), (int, Vector2Int)> portalPair =
            new Dictionary<(int, Vector2Int), (int, Vector2Int)>();

        // Swap pads pair up the same stable way whirlpools do, so which pad links to which is
        // deterministic and matches the Python solver's swap_pair exactly.
        public readonly Dictionary<(int, Vector2Int), (int, Vector2Int)> swapPair =
            new Dictionary<(int, Vector2Int), (int, Vector2Int)>();

        // Pearls picked up so far, and how many the level started with. Every lock springs at
        // once the moment the last one is collected.
        public readonly HashSet<(int, Vector2Int)> collected = new HashSet<(int, Vector2Int)>();
        public int totalKeys;
        public PEntity echo;            // the shadow diver, or null if this level has none
        public PEntity mirror;          // the diver that moves the opposite way, or null
        public bool hasMagnet;          // set by LevelParser — skips the whole magnet pass otherwise

        // Fragile crates that have already spent their one shove, and the direction a sticky floor
        // is demanding of the next move. Both are snapshotted, so undo restores them for free.
        public readonly HashSet<PEntity> shoved = new HashSet<PEntity>();
        public bool hasForced;
        public Vector2Int forcedDir;

        // A quarter turn clockwise: right -> down -> left -> up. Deflectors use it, and so does
        // the Python solver's turn_cw; keep the two identical or a level the solver proved will
        // steer differently in the game.
        static Vector2Int TurnCw(Vector2Int d) => new Vector2Int(d.y, -d.x);
        public bool hasGravity;         // set by LevelParser — skips the whole gravity pass otherwise

        // The rest of the state a move can change. Every one of these is captured by MoveSnap, so
        // undo restores them for free — miss one and undo silently desyncs the board from the model.
        public bool latched;            // has a toggle switch been thrown?
        public int beat;                // 3-beat clock for pulsing gates; shut on beat 0

        // Three beats, not two, and deliberately so: the board is a grid, so the parity of your
        // move count is fixed by which cell you stand on. On a two-beat cycle a pulsing gate is
        // therefore always open when you reach it, or never — it degenerates into a wall or a
        // floor tile. Three is coprime with the grid's two-colouring, so burning a move genuinely
        // changes what you can walk through. Must match PULSE_CYCLE in the Python solver.
        public const int PulseCycle = 3;
        public bool hasLast;            // false until the first move — nothing has been charged yet
        public Vector2Int lastDir;      // the previous move's direction, for boulders

        public bool LocksOpen() => totalKeys > 0 && collected.Count == totalKeys;

        // A move snapshot: every entity's position + sunk flag, plus the two per-room terrain sets
        // that a move can change (bridged trenches, collapsed coral). Undo restores all of them, so
        // sinking a rock — or dropping the coral behind you — is fully reversible.
        class MoveSnap
        {
            public List<(PEntity e, int room, Vector2Int pos, bool sunk)> ents;
            public List<(PRoom room, Vector2Int[] filled, Vector2Int[] broken)> tiles;
            public (int, Vector2Int)[] keys;
            public List<(PRoom room, Vector2Int[] smashed)> rocks;
            public bool latched;
            public int beat;
            public bool hasLast;
            public Vector2Int lastDir;
            public PEntity[] shoved;
            public bool hasForced;
            public Vector2Int forcedDir;
        }
        readonly Stack<MoveSnap> undoStack = new Stack<MoveSnap>();

        public int MoveCount => undoStack.Count;

        public PEntity EntityAt(int roomId, Vector2Int p)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (e.sunk) continue;   // sunk boxes are off the board — they never occupy a cell
                if (e.roomId == roomId && e.pos == p) return e;
            }
            return null;
        }

        public bool TryMovePlayer(Vector2Int dir)
        {
            // A sticky floor dictates your next move; anything else is refused outright.
            if (hasForced && dir != forcedDir) return false;

            var snapshot = Snapshot();
            var startRoom = player.roomId;
            var startPos = player.pos;
            var guard = new HashSet<(PEntity, int, Vector2Int)>();
            if (!TryMoveInto(player, player.roomId, player.pos + dir, dir, guard)) return false;

            // THE ORDER OF WHAT FOLLOWS IS THE CONTRACT. The Python solver's try_move does exactly
            // the same four things in exactly this sequence; change one and the two engines part
            // company, which is how an impossible level ships.
            //
            // 1) The echo copies the move. It is allowed to fail — a blocked echo simply stays put
            //    — and it moves AFTER you, so it sees where you ended up and can never displace you.
            if (echo != null && !echo.sunk)
                TryMoveInto(echo, echo.roomId, echo.pos + dir, dir,
                            new HashSet<(PEntity, int, Vector2Int)>());

            // 2) The mirror diver moves the OPPOSITE way. Like the echo it may fail harmlessly, and
            //    it goes after the echo so the order of the three divers is fixed and reproducible.
            if (mirror != null && !mirror.sunk)
                TryMoveInto(mirror, mirror.roomId, mirror.pos - dir, -dir,
                            new HashSet<(PEntity, int, Vector2Int)>());

            // 3) Gravity, once everything that was going to move has moved.
            ApplyGravity();

            // 4) Magnets, last of all, so they act on where everything finally came to rest.
            ApplyMagnets();

            // 5) Cracked coral collapses behind you: a cracked cell someone STARTED this move on, and
            // that nobody is standing on now, becomes a permanent hole. Resolved once at the end of
            // the move rather than mid-recursion, so the rule stays simple — and identical to the
            // Python solver, which is what makes a level it proved solvable stay solvable here.
            foreach (var (e, room, pos, sunk) in snapshot.ents)
            {
                if (sunk) continue;                              // already off the board
                var r = rooms[room];
                if (!r.IsCracked(pos)) continue;
                if (e.roomId == room && e.pos == pos) continue;   // never moved off it
                if (EntityAt(room, pos) != null) continue;        // someone else took the cell
                r.broken.Add(pos);
            }

            // Pearls are picked up by the diver, and only where it comes to rest — one merely
            // skimmed over mid-slide stays on the board.
            if (!player.sunk && rooms[player.roomId].IsKey(player.pos))
                collected.Add((player.roomId, player.pos));

            // A toggle latches when the diver comes to rest on one it did not start the move on —
            // so standing still on a switch does not flip it back and forth.
            if (!player.sunk && rooms[player.roomId].IsToggle(player.pos)
                && (player.roomId != startRoom || player.pos != startPos))
                latched = !latched;

            // Leaving a sticky floor commits you: your next move must repeat this direction.
            hasForced = rooms[startRoom].IsSticky(startPos)
                        && (player.roomId != startRoom || player.pos != startPos);
            forcedDir = dir;

            beat = (beat + 1) % PulseCycle;   // the pulse ticks on every move that happened
            hasLast = true;
            lastDir = dir;

            undoStack.Push(snapshot);
            return true;
        }

        public bool Undo()
        {
            if (undoStack.Count == 0) return false;
            var snap = undoStack.Pop();
            foreach (var (e, room, pos, sunk) in snap.ents)
            {
                e.roomId = room;
                e.pos = pos;
                e.sunk = sunk;
            }
            foreach (var (room, filled, broken) in snap.tiles)
            {
                room.filled.Clear();
                foreach (var c in filled) room.filled.Add(c);
                room.broken.Clear();
                foreach (var c in broken) room.broken.Add(c);
            }
            collected.Clear();
            foreach (var k in snap.keys) collected.Add(k);
            foreach (var (room, rock) in snap.rocks)
            {
                room.smashed.Clear();
                foreach (var c in rock) room.smashed.Add(c);
            }
            latched = snap.latched;
            beat = snap.beat;
            hasLast = snap.hasLast;
            lastDir = snap.lastDir;
            shoved.Clear();
            foreach (var x in snap.shoved) shoved.Add(x);
            hasForced = snap.hasForced;
            forcedDir = snap.forcedDir;
            return true;
        }

        public bool IsWon()
        {
            foreach (var room in rooms.Values)
            {
                foreach (var g in room.boxGoals)
                {
                    var e = EntityAt(room.id, g);
                    if (e == null || !e.IsCrate) return false;
                }
                foreach (var g in room.playerGoals)
                {
                    var e = EntityAt(room.id, g);
                    if (e == null || !e.isPlayer) return false;
                }
                foreach (var g in room.echoGoals)
                {
                    var e = EntityAt(room.id, g);
                    if (e == null || !e.isEcho) return false;
                }
                foreach (var g in room.mirrorGoals)
                {
                    var e = EntityAt(room.id, g);
                    if (e == null || !e.isMirror) return false;
                }
                // A coloured goal accepts exactly one crate: the one wearing its colour.
                foreach (var (cell, colour) in room.colourGoals)
                {
                    var e = EntityAt(room.id, cell);
                    if (e == null || e.colour != colour) return false;
                }
            }
            return true;
        }

        // Crates standing in a gravity zone sink one cell at a time until something holds them up.
        // Divers swim and are unaffected. Runs to a fixed point in entity-index order, so a stack
        // settles the same way every time — and the same way the Python solver settles it.
        // Every magnet drags each crate sharing its row or column one cell closer, over and over
        // until nothing more will move. Same lowest-first ordering as gravity, and for the same
        // reason: the two engines build their entity lists differently.
        void ApplyMagnets()
        {
            if (!hasMagnet) return;
            bool moved = true;
            var order = new List<PEntity>();
            while (moved)
            {
                moved = false;
                order.Clear();
                order.AddRange(entities);
                order.Sort((a, b) => a.roomId != b.roomId ? a.roomId.CompareTo(b.roomId)
                                   : a.pos.y != b.pos.y ? a.pos.y.CompareTo(b.pos.y)
                                   : a.pos.x.CompareTo(b.pos.x));
                foreach (var e in order)
                {
                    if (!e.IsCrate || e.sunk) continue;
                    var room = rooms[e.roomId];
                    if (room.magnet == null) continue;
                    for (int mx = 0; mx < room.width && !moved; mx++)
                        for (int my = 0; my < room.height; my++)
                        {
                            if (!room.magnet[mx, my]) continue;
                            if (mx == e.pos.x && my == e.pos.y) continue;
                            Vector2Int step;
                            if (mx == e.pos.x) step = new Vector2Int(0, my > e.pos.y ? 1 : -1);
                            else if (my == e.pos.y) step = new Vector2Int(mx > e.pos.x ? 1 : -1, 0);
                            else continue;
                            if (CanLand(e, room, e.pos + step, step))
                            {
                                e.pos += step;
                                moved = true;
                                break;
                            }
                        }
                }
            }
        }

        void ApplyGravity()
        {
            if (!hasGravity) return;
            bool moved = true;
            var order = new List<PEntity>();
            while (moved)
            {
                moved = false;
                // LOWEST CRATE FIRST, by (room, y, x) — never by list index. The two engines build
                // their entity lists in different orders (the Python solver scans the grid; this
                // side adds every crate before every diver), and a crate dropping off a weight
                // plate can slam a gate shut halfway through a cascade. Sorting by position is the
                // only order both engines can agree on.
                order.Clear();
                order.AddRange(entities);
                order.Sort((a, b) => a.roomId != b.roomId ? a.roomId.CompareTo(b.roomId)
                                   : a.pos.y != b.pos.y ? a.pos.y.CompareTo(b.pos.y)
                                   : a.pos.x.CompareTo(b.pos.x));
                for (int i = 0; i < order.Count; i++)
                {
                    var e = order[i];
                    if (!e.IsCrate || e.sunk) continue;
                    var room = rooms[e.roomId];
                    Vector2Int step;
                    if (room.IsGravity(e.pos)) step = Vector2Int.down;        // sinks
                    else if (room.IsUpdraft(e.pos)) step = Vector2Int.up;     // floats
                    else continue;
                    var to = e.pos + step;
                    if (CanLand(e, room, to, step))
                    {
                        e.pos = to;
                        moved = true;
                    }
                }
            }
        }

        MoveSnap Snapshot()
        {
            var snap = new MoveSnap
            {
                ents = new List<(PEntity, int, Vector2Int, bool)>(entities.Count),
                tiles = new List<(PRoom, Vector2Int[], Vector2Int[])>(),
                keys = new (int, Vector2Int)[collected.Count],
                rocks = new List<(PRoom, Vector2Int[])>(),
                latched = latched,
                beat = beat,
                hasLast = hasLast,
                lastDir = lastDir,
                shoved = new PEntity[shoved.Count],
                hasForced = hasForced,
                forcedDir = forcedDir
            };
            shoved.CopyTo(snap.shoved);
            collected.CopyTo(snap.keys);
            foreach (var room in rooms.Values)
                if (room.rock != null)
                {
                    var r = new Vector2Int[room.smashed.Count];
                    room.smashed.CopyTo(r);
                    snap.rocks.Add((room, r));
                }
            foreach (var e in entities) snap.ents.Add((e, e.roomId, e.pos, e.sunk));
            foreach (var room in rooms.Values)
                if (room.trench != null || room.cracked != null)
                {
                    var f = new Vector2Int[room.filled.Count];
                    room.filled.CopyTo(f);
                    var b = new Vector2Int[room.broken.Count];
                    room.broken.CopyTo(b);
                    snap.tiles.Add((room, f, b));
                }
            return snap;
        }

        // Is this crate standing on a mark that accepts it? Used by locking crates, which set in
        // stone once they arrive — so IsWon and this must agree about what counts.
        static bool OnGoal(PEntity e, PRoom room, Vector2Int cell)
        {
            if (room.boxGoals.Contains(cell)) return true;
            foreach (var (c, colour) in room.colourGoals)
                if (c == cell && e.colour == colour) return true;
            return false;
        }

        // Gates are passable while ANY entity — diver or crate — stands on a shell button.
        public bool GatesOpen()
        {
            foreach (var e in entities)
            {
                if (e.sunk) continue;                           // off the board, holds nothing down
                if (rooms[e.roomId].IsButton(e.pos)) return true;
            }
            return false;
        }

        // Heavy gates want real weight: only a crate holds a plate down, never the diver.
        public bool HeavyGatesOpen()
        {
            foreach (var e in entities)
            {
                if (e.sunk || !e.IsCrate) continue;
                if (rooms[e.roomId].IsPlate(e.pos)) return true;
            }
            return false;
        }

        // Terrain that refuses entry to `target` while travelling in `dir`. Shared by the push path
        // and the settle path so the two can never disagree. Trenches are deliberately NOT here: a
        // box IS allowed in (it sinks), so that stays a special case in TryMoveInto.
        bool TerrainBlocks(PRoom room, Vector2Int target, Vector2Int dir, PEntity mover)
        {
            if (room.IsWall(target)) return true;
            if (room.IsBroken(target)) return true;             // collapsed coral — now a hole
            var ow = room.OneWay(target);
            if (ow != Vector2Int.zero && ow != dir) return true; // swimming against the flow
            if (room.gate != null && room.gate[target.x, target.y] && !GatesOpen()) return true;
            if (room.heavyGate != null && room.heavyGate[target.x, target.y] && !HeavyGatesOpen())
                return true;
            if (room.IsLocked(target) && !LocksOpen()) return true;
            if (room.IsKelp(target) && !mover.isPlayer) return true;  // a thicket only a diver slips through
            if (room.IsGap(target) && !mover.IsCrate) return true;    // too narrow for a diver
            if (room.IsLatch(target) && !latched) return true;         // nobody has thrown the switch
            if (room.IsPulse(target) && beat == 0) return true;        // shut on this beat
            return false;
        }

        // After landing, keep being carried: ice keeps you going the way you were already
        // travelling, a current sends you the way IT points (and that becomes your new direction),
        // and a geyser launches you two cells on — over whatever is in the middle. Settling is plain
        // movement: it never pushes, sinks or warps. The seen-set stops two currents facing each
        // other from ping-ponging forever. Mirrors the solver's _settle exactly, so a level it
        // proved solvable stays solvable in game.
        void Settle(PEntity e, Vector2Int dir)
        {
            var seen = new HashSet<(int, Vector2Int)>();
            while (true)
            {
                var room = rooms[e.roomId];
                if (!seen.Add((e.roomId, e.pos))) return;       // already been here — a loop

                Vector2Int step;
                int hop;
                if (room.IsGeyser(e.pos)) { step = dir; hop = 2; }   // thrown clear over the next cell
                else if (room.IsIce(e.pos)) { step = dir; hop = 1; }
                else if (room.Current(e.pos) != Vector2Int.zero) { step = room.Current(e.pos); hop = 1; }
                else if (room.IsDeflector(e.pos)) { step = TurnCw(dir); hop = 1; }  // spun a quarter turn
                else if (room.IsGap(e.pos)) { step = dir; hop = 1; }  // too tight to rest in
                else if (e.slick) { step = dir; hop = 1; }            // needs no ice of its own
                else return;                                          // solid ground — stop

                var dest = e.pos + step * hop;
                if (!CanLand(e, room, dest, step)) return;
                if (hop == 2)   // a hop of 2 sails over the middle cell, but not through solid rock
                {
                    var mid = e.pos + step;
                    if (!room.InBounds(mid)) return;
                    if (TerrainBlocks(room, mid, step, e)) return;
                }
                e.pos = dest;
                dir = step;
            }
        }

        // Is `cell` somewhere `e` can come to rest, arriving along `dir`? Used by the settle path
        // only — landing never pushes, so an occupied cell is simply a stop.
        bool CanLand(PEntity e, PRoom room, Vector2Int cell, Vector2Int dir)
        {
            if (!room.InBounds(cell)) return false;
            if (TerrainBlocks(room, cell, dir, e)) return false;
            if (room.IsOpenTrench(cell)) return false;
            if (room.IsDeep(cell) && !e.isPlayer) return false;  // settling never sinks a crate
            if (room.IsRock(cell)) return false;                 // settling never smashes; only a push does
            if (EntityAt(room.id, cell) != null) return false;
            return true;
        }

        // Attempts to move entity e onto cell `target` of room `roomId`, moving in `dir`.
        // Resolution order (like Patrick's Parabox): push > enter; leaving room bounds = exit.
        bool TryMoveInto(PEntity e, int roomId, Vector2Int target, Vector2Int dir,
                         HashSet<(PEntity, int, Vector2Int)> guard)
        {
            if (!guard.Add((e, roomId, target))) return false; // cycle protection

            var room = rooms[roomId];

            // Stepping outside the room: exit through the meta-box that contains it.
            if (!room.InBounds(target))
            {
                var container = room.containerBox;
                if (container == null) return false; // main room edge — blocked
                return TryMoveInto(e, container.roomId, container.pos + dir, dir, guard);
            }

            if (TerrainBlocks(room, target, dir, e)) return false;

            // An open trench: the player can't cross the gap, but a box pushed in sinks — filling the
            // gap and being consumed — so the pusher advances into its old cell. Mirrors the solver.
            if (room.IsOpenTrench(target))
            {
                if (e.isPlayer) return false;
                room.filled.Add(target);
                e.sunk = true;
                e.roomId = roomId;
                e.pos = target;   // where it sank — the view animates down here, then hides
                return true;
            }

            // Breakable rock: a crate driven into it shatters both — the rock opens, the crate is
            // spent. Divers just bounce off. Same shape as the trench branch: the mover is consumed
            // and the pusher advances into the cell it came from.
            if (room.IsRock(target))
            {
                if (!e.IsCrate) return false;
                room.smashed.Add(target);
                e.sunk = true;
                e.roomId = roomId;
                e.pos = target;
                return true;
            }

            // Deep water: the diver swims across, but a crate pushed in is LOST — no bridge, no
            // second chance. Same shape as the trench branch above, minus the filling.
            if (room.IsDeep(target) && !e.isPlayer)
            {
                e.sunk = true;
                e.roomId = roomId;
                e.pos = target;   // where it went under — the view animates down here, then hides
                return true;
            }

            // A swap pad trades you with whoever stands on its twin. Resolved before the ordinary
            // occupant logic, because the cell you step onto may be empty while the swap still
            // happens at the far end.
            if (swapPair.TryGetValue((roomId, target), out var twin)
                && EntityAt(roomId, target) == null)
            {
                var other = EntityAt(twin.Item1, twin.Item2);
                if (other != null)
                {
                    other.roomId = roomId;
                    other.pos = target;
                    e.roomId = twin.Item1;
                    e.pos = twin.Item2;
                    Settle(e, dir);
                    return true;
                }
            }

            var occupant = EntityAt(roomId, target);
            if (occupant == e) return false;

            if (occupant == null)
            {
                // Whirlpool: entering an empty portal relocates the entity to its pair — unless the
                // pair is occupied (nothing can emerge there), in which case the move is blocked.
                if (portalPair.TryGetValue((roomId, target), out var pair))
                {
                    if (EntityAt(pair.Item1, pair.Item2) != null) return false;
                    e.roomId = pair.Item1;
                    e.pos = pair.Item2;
                    Settle(e, dir);
                    return true;
                }
                e.roomId = roomId;
                e.pos = target;
                Settle(e, dir);
                return true;
            }

            // Loose sand gives you nothing to brace against: you may walk off it, but you may not
            // shove anything while standing on it. Checked on the PUSHER's current cell.
            if (rooms[e.roomId].IsSand(e.pos)) return false;

            // A fragile crate takes exactly one shove. The second breaks it where it stands, and
            // the pusher advances into the space — resolved here so it reads like the trench branch.
            if (occupant.IsCrate && occupant.fragile && shoved.Contains(occupant))
            {
                occupant.sunk = true;
                e.roomId = roomId;
                e.pos = target;
                Settle(e, dir);
                return true;
            }

            // A crate in a cage is in for good, and a locking crate sets the moment it reaches its
            // mark. Both are refusals to be pushed, checked on the OCCUPANT rather than the target.
            if (occupant.IsCrate)
            {
                var oroom = rooms[occupant.roomId];
                if (oroom.IsCage(occupant.pos)) return false;
                if (occupant.locking && OnGoal(occupant, oroom, occupant.pos)) return false;
            }

            // A boulder needs a run-up: it only shifts if the move BEFORE this one was already
            // going the same way. Checked before the push, so a failed charge costs nothing.
            if (occupant.boulder && (!hasLast || lastDir != dir)) return false;

            // An anchored box is bolted down: skip straight to trying to climb inside it.
            if (occupant.anchored)
            {
                if (occupant.interiorRoomId < 0) return false;
                var anchoredInner = rooms[occupant.interiorRoomId];
                return TryMoveInto(e, anchoredInner.id, EntryCell(anchoredInner, dir), dir, guard);
            }

            // 1) Try to push the occupant (chain pushes resolve recursively).
            if (TryMoveInto(occupant, roomId, target + dir, dir, guard))
            {
                if (occupant.fragile) shoved.Add(occupant);
                e.roomId = roomId;
                e.pos = target;
                Settle(e, dir);
                return true;
            }

            // 2) Push failed — if the occupant is a meta-box, try to enter it.
            if (occupant.interiorRoomId >= 0)
            {
                var inner = rooms[occupant.interiorRoomId];
                return TryMoveInto(e, inner.id, EntryCell(inner, dir), dir, guard);
            }

            return false;
        }

        // Cell where an entity appears when entering a room while moving in `dir`.
        static Vector2Int EntryCell(PRoom room, Vector2Int dir)
        {
            if (dir == Vector2Int.right) return new Vector2Int(0, room.height / 2);
            if (dir == Vector2Int.left) return new Vector2Int(room.width - 1, room.height / 2);
            if (dir == Vector2Int.up) return new Vector2Int(room.width / 2, 0);
            return new Vector2Int(room.width / 2, room.height - 1);
        }
    }
}
