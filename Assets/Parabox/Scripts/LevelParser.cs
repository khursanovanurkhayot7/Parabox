using UnityEngine;

namespace Parabox
{
    // Builds a LevelModel by reading the marker components of a level prefab.
    // The prefab is only read — it is never instantiated or modified.
    public static class LevelParser
    {
        public static LevelModel Parse(GameObject levelPrefab)
        {
            var model = new LevelModel();

            foreach (var rm in levelPrefab.GetComponentsInChildren<RoomMarker>(true))
            {
                model.rooms[rm.roomId] = new PRoom
                {
                    id = rm.roomId,
                    width = rm.width,
                    height = rm.height,
                    wall = new bool[rm.width, rm.height]
                };
            }

            foreach (var w in levelPrefab.GetComponentsInChildren<WallMarker>(true))
                RoomOf(model, w).wall[w.x, w.y] = true;

            foreach (var g in levelPrefab.GetComponentsInChildren<GoalMarker>(true))
            {
                var room = RoomOf(model, g);
                if (g.forPlayer) room.playerGoals.Add(new Vector2Int(g.x, g.y));
                else room.boxGoals.Add(new Vector2Int(g.x, g.y));
            }

            foreach (var b in levelPrefab.GetComponentsInChildren<BoxMarker>(true))
            {
                var e = new PEntity
                {
                    isPlayer = false,
                    interiorRoomId = b.containsRoomId,
                    roomId = RoomOf(model, b).id,
                    pos = new Vector2Int(b.x, b.y)
                };
                model.entities.Add(e);

                if (b.containsRoomId >= 0 && model.rooms.TryGetValue(b.containsRoomId, out var inner))
                    inner.containerBox = e;
            }

            var pm = levelPrefab.GetComponentInChildren<PlayerMarker>(true);
            var p = new PEntity
            {
                isPlayer = true,
                roomId = RoomOf(model, pm).id,
                pos = new Vector2Int(pm.x, pm.y)
            };
            model.entities.Add(p);
            model.player = p;

            return model;
        }

        static PRoom RoomOf(LevelModel model, Component marker)
            => model.rooms[marker.GetComponentInParent<RoomMarker>(true).roomId];
    }
}
