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
    }

    public class PRoom
    {
        public int id;
        public int width;
        public int height;
        public bool[,] wall;
        public List<Vector2Int> boxGoals = new List<Vector2Int>();
        public List<Vector2Int> playerGoals = new List<Vector2Int>();
        public PEntity containerBox;    // the meta-box whose inside is this room (null = main room)

        public bool InBounds(Vector2Int p) => p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;
        public bool IsWall(Vector2Int p) => wall[p.x, p.y];
    }

    public class LevelModel
    {
        public readonly Dictionary<int, PRoom> rooms = new Dictionary<int, PRoom>();
        public readonly List<PEntity> entities = new List<PEntity>();
        public PEntity player;

        readonly Stack<List<(PEntity e, int room, Vector2Int pos)>> undoStack =
            new Stack<List<(PEntity, int, Vector2Int)>>();

        public int MoveCount => undoStack.Count;

        public PEntity EntityAt(int roomId, Vector2Int p)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (e.roomId == roomId && e.pos == p) return e;
            }
            return null;
        }

        public bool TryMovePlayer(Vector2Int dir)
        {
            var snapshot = Snapshot();
            var guard = new HashSet<(PEntity, int, Vector2Int)>();
            if (TryMoveInto(player, player.roomId, player.pos + dir, dir, guard))
            {
                undoStack.Push(snapshot);
                return true;
            }
            return false;
        }

        public bool Undo()
        {
            if (undoStack.Count == 0) return false;
            var snap = undoStack.Pop();
            foreach (var (e, room, pos) in snap)
            {
                e.roomId = room;
                e.pos = pos;
            }
            return true;
        }

        public bool IsWon()
        {
            foreach (var room in rooms.Values)
            {
                foreach (var g in room.boxGoals)
                {
                    var e = EntityAt(room.id, g);
                    if (e == null || e.isPlayer) return false;
                }
                foreach (var g in room.playerGoals)
                {
                    var e = EntityAt(room.id, g);
                    if (e == null || !e.isPlayer) return false;
                }
            }
            return true;
        }

        List<(PEntity, int, Vector2Int)> Snapshot()
        {
            var list = new List<(PEntity, int, Vector2Int)>(entities.Count);
            foreach (var e in entities) list.Add((e, e.roomId, e.pos));
            return list;
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

            if (room.IsWall(target)) return false;

            var occupant = EntityAt(roomId, target);
            if (occupant == e) return false;

            if (occupant == null)
            {
                e.roomId = roomId;
                e.pos = target;
                return true;
            }

            // 1) Try to push the occupant (chain pushes resolve recursively).
            if (TryMoveInto(occupant, roomId, target + dir, dir, guard))
            {
                e.roomId = roomId;
                e.pos = target;
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
