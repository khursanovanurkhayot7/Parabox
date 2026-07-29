using System.Collections.Generic;
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

            foreach (var tr in levelPrefab.GetComponentsInChildren<TrenchMarker>(true))
            {
                var room = RoomOf(model, tr);
                if (room.trench == null) room.trench = new bool[room.width, room.height];
                room.trench[tr.x, tr.y] = true;
            }

            foreach (var ic in levelPrefab.GetComponentsInChildren<IceMarker>(true))
            {
                var room = RoomOf(model, ic);
                if (room.ice == null) room.ice = new bool[room.width, room.height];
                room.ice[ic.x, ic.y] = true;
            }

            foreach (var cu in levelPrefab.GetComponentsInChildren<CurrentMarker>(true))
            {
                var room = RoomOf(model, cu);
                if (room.current == null) room.current = new Vector2Int[room.width, room.height];
                room.current[cu.x, cu.y] = new Vector2Int(cu.dx, cu.dy);
            }

            foreach (var ow in levelPrefab.GetComponentsInChildren<OneWayMarker>(true))
            {
                var room = RoomOf(model, ow);
                if (room.oneway == null) room.oneway = new Vector2Int[room.width, room.height];
                room.oneway[ow.x, ow.y] = new Vector2Int(ow.dx, ow.dy);
            }

            foreach (var cr in levelPrefab.GetComponentsInChildren<CrackMarker>(true))
            {
                var room = RoomOf(model, cr);
                if (room.cracked == null) room.cracked = new bool[room.width, room.height];
                room.cracked[cr.x, cr.y] = true;
            }

            foreach (var sw in levelPrefab.GetComponentsInChildren<SwitchMarker>(true))
            {
                var room = RoomOf(model, sw);
                if (sw.heavy)
                {
                    if (room.plate == null) room.plate = new bool[room.width, room.height];
                    room.plate[sw.x, sw.y] = true;
                }
                else
                {
                    if (room.button == null) room.button = new bool[room.width, room.height];
                    room.button[sw.x, sw.y] = true;
                }
            }

            foreach (var ga in levelPrefab.GetComponentsInChildren<GateMarker>(true))
            {
                var room = RoomOf(model, ga);
                if (ga.heavy)
                {
                    if (room.heavyGate == null) room.heavyGate = new bool[room.width, room.height];
                    room.heavyGate[ga.x, ga.y] = true;
                }
                else
                {
                    if (room.gate == null) room.gate = new bool[room.width, room.height];
                    room.gate[ga.x, ga.y] = true;
                }
            }

            var swaps = new List<(int rid, int x, int y)>();
            foreach (var tm in levelPrefab.GetComponentsInChildren<TerrainMarker>(true))
            {
                var room = RoomOf(model, tm);
                switch (tm.kind)
                {
                    case TerrainKind.Kelp:
                        if (room.kelp == null) room.kelp = new bool[room.width, room.height];
                        room.kelp[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Deep:
                        if (room.deep == null) room.deep = new bool[room.width, room.height];
                        room.deep[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Key:
                        if (room.key == null) room.key = new bool[room.width, room.height];
                        room.key[tm.x, tm.y] = true;
                        model.totalKeys++;
                        break;
                    case TerrainKind.Lock:
                        if (room.locked == null) room.locked = new bool[room.width, room.height];
                        room.locked[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Geyser:
                        if (room.geyser == null) room.geyser = new bool[room.width, room.height];
                        room.geyser[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Gap:
                        if (room.gap == null) room.gap = new bool[room.width, room.height];
                        room.gap[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Gravity:
                        if (room.gravity == null) room.gravity = new bool[room.width, room.height];
                        room.gravity[tm.x, tm.y] = true;
                        model.hasGravity = true;
                        break;
                    case TerrainKind.Rock:
                        if (room.rock == null) room.rock = new bool[room.width, room.height];
                        room.rock[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Toggle:
                        if (room.toggle == null) room.toggle = new bool[room.width, room.height];
                        room.toggle[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Latch:
                        if (room.latch == null) room.latch = new bool[room.width, room.height];
                        room.latch[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Pulse:
                        if (room.pulse == null) room.pulse = new bool[room.width, room.height];
                        room.pulse[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Updraft:
                        if (room.updraft == null) room.updraft = new bool[room.width, room.height];
                        room.updraft[tm.x, tm.y] = true;
                        model.hasGravity = true;
                        break;
                    case TerrainKind.Cage:
                        if (room.cage == null) room.cage = new bool[room.width, room.height];
                        room.cage[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Deflector:
                        if (room.deflector == null) room.deflector = new bool[room.width, room.height];
                        room.deflector[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Sand:
                        if (room.sand == null) room.sand = new bool[room.width, room.height];
                        room.sand[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Magnet:
                        if (room.magnet == null) room.magnet = new bool[room.width, room.height];
                        room.magnet[tm.x, tm.y] = true;
                        model.hasMagnet = true;
                        break;
                    case TerrainKind.Sticky:
                        if (room.sticky == null) room.sticky = new bool[room.width, room.height];
                        room.sticky[tm.x, tm.y] = true;
                        break;
                    case TerrainKind.Swap:
                        if (room.swap == null) room.swap = new bool[room.width, room.height];
                        room.swap[tm.x, tm.y] = true;
                        swaps.Add((room.id, tm.x, tm.y));
                        break;
                }
            }

            // Swap pads pair in the SAME stable (roomId, x, y) order the solver uses.
            swaps.Sort((a, b) => a.rid != b.rid ? a.rid.CompareTo(b.rid)
                                : a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            for (int i = 0; i + 1 < swaps.Count; i += 2)
            {
                var a = (swaps[i].rid, new Vector2Int(swaps[i].x, swaps[i].y));
                var b = (swaps[i + 1].rid, new Vector2Int(swaps[i + 1].x, swaps[i + 1].y));
                model.swapPair[a] = b;
                model.swapPair[b] = a;
            }

            // Whirlpools: collect every portal cell, mark it for rendering, then pair them in a
            // stable (roomId, x, y) order — the SAME order the Python solver uses — so a level that
            // the solver proved solvable links its portals identically in-game.
            var portals = new List<(int rid, int x, int y)>();
            foreach (var pt in levelPrefab.GetComponentsInChildren<PortalMarker>(true))
            {
                var room = RoomOf(model, pt);
                if (room.portal == null) room.portal = new bool[room.width, room.height];
                room.portal[pt.x, pt.y] = true;
                portals.Add((room.id, pt.x, pt.y));
            }
            portals.Sort((a, b) => a.rid != b.rid ? a.rid.CompareTo(b.rid)
                                  : a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            for (int i = 0; i + 1 < portals.Count; i += 2)
            {
                var a = (portals[i].rid, new Vector2Int(portals[i].x, portals[i].y));
                var b = (portals[i + 1].rid, new Vector2Int(portals[i + 1].x, portals[i + 1].y));
                model.portalPair[a] = b;
                model.portalPair[b] = a;
            }

            foreach (var g in levelPrefab.GetComponentsInChildren<GoalMarker>(true))
            {
                var room = RoomOf(model, g);
                var cell = new Vector2Int(g.x, g.y);
                if (g.colour > 0) room.colourGoals.Add((cell, g.colour));
                else if (g.forMirror) room.mirrorGoals.Add(cell);
                else if (g.forEcho) room.echoGoals.Add(cell);
                else if (g.forPlayer) room.playerGoals.Add(cell);
                else room.boxGoals.Add(cell);
            }

            foreach (var b in levelPrefab.GetComponentsInChildren<BoxMarker>(true))
            {
                var e = new PEntity
                {
                    isPlayer = false,
                    interiorRoomId = b.containsRoomId,
                    roomId = RoomOf(model, b).id,
                    pos = new Vector2Int(b.x, b.y),
                    colour = b.colour,
                    slick = b.slick,
                    boulder = b.boulder,
                    locking = b.locking,
                    fragile = b.fragile,
                    anchored = b.anchored
                };
                model.entities.Add(e);

                if (b.containsRoomId >= 0 && model.rooms.TryGetValue(b.containsRoomId, out var inner))
                    inner.containerBox = e;
            }

            // Two divers are possible now — you and your echo — so every PlayerMarker is read,
            // not just the first one found.
            foreach (var pm in levelPrefab.GetComponentsInChildren<PlayerMarker>(true))
            {
                var d = new PEntity
                {
                    isPlayer = !pm.isEcho && !pm.isMirror,
                    isEcho = pm.isEcho,
                    isMirror = pm.isMirror,
                    roomId = RoomOf(model, pm).id,
                    pos = new Vector2Int(pm.x, pm.y)
                };
                model.entities.Add(d);
                if (pm.isEcho) model.echo = d;
                else if (pm.isMirror) model.mirror = d;
                else model.player = d;
            }

            return model;
        }

        static PRoom RoomOf(LevelModel model, Component marker)
            => model.rooms[marker.GetComponentInParent<RoomMarker>(true).roomId];
    }
}
