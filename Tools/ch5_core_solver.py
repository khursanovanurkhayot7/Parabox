#!/usr/bin/env python3
"""Small authoring solver for Parabox's core recursive-room rules.

This deliberately supports only the Chapter V vocabulary used by the authored
recursion boards: walls, the controlled player, ordinary cargo, goals, and
movable/anchored room boxes. Unity's exact C# solver remains the release proof;
this helper makes layout iteration fast before a prefab is generated.
"""

from __future__ import annotations

from collections import deque
from dataclasses import dataclass
import heapq
import itertools
import argparse
import ast
from pathlib import Path
from typing import Iterable


MOVES = (("U", (0, -1)), ("R", (1, 0)), ("D", (0, 1)), ("L", (-1, 0)))
ANCHORED = {"Q": 1, "U": 2, "V": 3}


@dataclass(frozen=True)
class Entity:
    kind: str
    interior: int = -1
    anchored: bool = False
    colour: int = 0


class CoreLevel:
    def __init__(self, rooms: list[list[str]]):
        self.rooms = rooms
        self.sizes = [(len(rows[0]), len(rows)) for rows in rooms]
        self.walls: list[set[tuple[int, int]]] = []
        self.box_goals: list[set[tuple[int, int]]] = []
        self.player_goals: list[set[tuple[int, int]]] = []
        self.colour_goals: list[dict[tuple[int, int], int]] = []
        self.entities: list[Entity] = []
        positions: list[tuple[int, int, int]] = []
        self.container_for_room: dict[int, int] = {}
        player_index = -1

        for room_id, rows in enumerate(rooms):
            width = len(rows[0])
            if any(len(row) != width for row in rows):
                raise ValueError(f"room {room_id} has ragged rows")
            walls: set[tuple[int, int]] = set()
            box_goals: set[tuple[int, int]] = set()
            player_goals: set[tuple[int, int]] = set()
            colour_goals: dict[tuple[int, int], int] = {}
            for y, row in enumerate(rows):
                for x, ch in enumerate(row):
                    if ch == "#":
                        walls.add((x, y))
                    elif ch == "x":
                        box_goals.add((x, y))
                    elif ch == "p":
                        player_goals.add((x, y))
                    elif ch == "j":
                        colour_goals[(x, y)] = 1
                    elif ch == "n":
                        colour_goals[(x, y)] = 2
                    elif ch == "z":
                        colour_goals[(x, y)] = 3
                    elif ch == "A":
                        colour_goals[(x, y)] = 1
                        self.entities.append(Entity("crate", colour=1))
                        positions.append((room_id, x, y))
                    elif ch == "C":
                        colour_goals[(x, y)] = 2
                        self.entities.append(Entity("crate", colour=2))
                        positions.append((room_id, x, y))
                    elif ch == "P":
                        player_index = len(self.entities)
                        self.entities.append(Entity("player"))
                        positions.append((room_id, x, y))
                    elif ch == "b":
                        self.entities.append(Entity("crate"))
                        positions.append((room_id, x, y))
                    elif ch == "J":
                        self.entities.append(Entity("crate", colour=1))
                        positions.append((room_id, x, y))
                    elif ch == "N":
                        self.entities.append(Entity("crate", colour=2))
                        positions.append((room_id, x, y))
                    elif ch == "Z":
                        self.entities.append(Entity("crate", colour=3))
                        positions.append((room_id, x, y))
                    elif "1" <= ch <= "9":
                        inner = int(ch)
                        entity_index = len(self.entities)
                        self.entities.append(Entity("meta", inner, False))
                        positions.append((room_id, x, y))
                        self.container_for_room[inner] = entity_index
                    elif ch in ANCHORED:
                        inner = ANCHORED[ch]
                        entity_index = len(self.entities)
                        self.entities.append(Entity("meta", inner, True))
                        positions.append((room_id, x, y))
                        self.container_for_room[inner] = entity_index
            self.walls.append(walls)
            self.box_goals.append(box_goals)
            self.player_goals.append(player_goals)
            self.colour_goals.append(colour_goals)

        if player_index < 0:
            raise ValueError("level has no player")
        if player_index != 0:
            # State is easier to inspect when the player is always entity zero.
            order = [player_index] + [i for i in range(len(self.entities)) if i != player_index]
            inverse = {old: new for new, old in enumerate(order)}
            self.entities = [self.entities[i] for i in order]
            positions = [positions[i] for i in order]
            self.container_for_room = {
                room: inverse[index] for room, index in self.container_for_room.items()
            }
        self.start = tuple(positions)

    def won(self, state: tuple[tuple[int, int, int], ...]) -> bool:
        for room_id, goals in enumerate(self.box_goals):
            for x, y in goals:
                if not any(
                    entity.kind in ("crate", "meta") and state[i] == (room_id, x, y)
                    for i, entity in enumerate(self.entities)
                ):
                    return False
        for room_id, goals in enumerate(self.player_goals):
            for x, y in goals:
                if state[0] != (room_id, x, y):
                    return False
        for room_id, goals in enumerate(self.colour_goals):
            for (x, y), colour in goals.items():
                if not any(
                    entity.kind == "crate" and entity.colour == colour
                    and state[i] == (room_id, x, y)
                    for i, entity in enumerate(self.entities)
                ):
                    return False
        return True

    def _entity_at(self, state: list[list[int]], room: int, x: int, y: int) -> int | None:
        for i, position in enumerate(state):
            if position == [room, x, y]:
                return i
        return None

    def _entry(self, room: int, dx: int, dy: int) -> tuple[int, int]:
        width, height = self.sizes[room]
        if dx == 1:
            return 0, height // 2
        if dx == -1:
            return width - 1, height // 2
        if dy == -1:
            return width // 2, height - 1
        return width // 2, 0

    def move(self, frozen: tuple[tuple[int, int, int], ...], dx: int, dy: int):
        state = [list(position) for position in frozen]
        guard: set[tuple[int, int, int, int]] = set()

        def attempt(entity_index: int, room: int, x: int, y: int) -> bool:
            key = (entity_index, room, x, y)
            if key in guard:
                return False
            guard.add(key)

            width, height = self.sizes[room]
            if x < 0 or y < 0 or x >= width or y >= height:
                container_index = self.container_for_room.get(room)
                if container_index is None:
                    return False
                parent, cx, cy = state[container_index]
                return attempt(entity_index, parent, cx + dx, cy + dy)

            if (x, y) in self.walls[room]:
                return False

            occupant = self._entity_at(state, room, x, y)
            if occupant is None:
                state[entity_index] = [room, x, y]
                return True
            if occupant == entity_index:
                return False

            occupied_entity = self.entities[occupant]
            if occupied_entity.anchored:
                if occupied_entity.interior < 0:
                    return False
                ex, ey = self._entry(occupied_entity.interior, dx, dy)
                return attempt(entity_index, occupied_entity.interior, ex, ey)

            if attempt(occupant, room, x + dx, y + dy):
                state[entity_index] = [room, x, y]
                return True

            if occupied_entity.interior >= 0:
                ex, ey = self._entry(occupied_entity.interior, dx, dy)
                return attempt(entity_index, occupied_entity.interior, ex, ey)
            return False

        room, x, y = state[0]
        if not attempt(0, room, x + dx, y + dy):
            return None
        return tuple(tuple(position) for position in state)

    def solve(self, max_depth: int = 100, node_limit: int = 4_000_000) -> str | None:
        if self.won(self.start):
            return ""
        queue = deque([(self.start, "")])
        seen = {self.start}
        nodes = 0
        while queue:
            state, route = queue.popleft()
            if len(route) >= max_depth:
                continue
            for code, (dx, dy) in MOVES:
                moved = self.move(state, dx, dy)
                if moved is None or moved in seen:
                    continue
                nodes += 1
                if nodes > node_limit:
                    return None
                next_route = route + code
                if self.won(moved):
                    return next_route
                seen.add(moved)
                queue.append((moved, next_route))
        return None

    def _heuristic(self, state: tuple[tuple[int, int, int], ...]) -> int:
        """Admissible lower bound for exact A*: one input may move a whole cargo train."""
        distances = [0]
        player_room, player_x, player_y = state[0]
        if self.player_goals[player_room]:
            distances.append(min(
                abs(player_x - gx) + abs(player_y - gy)
                for gx, gy in self.player_goals[player_room]
            ))

        for room_id, goals in enumerate(self.box_goals):
            for gx, gy in goals:
                candidates = [
                    abs(x - gx) + abs(y - gy)
                    for index, entity in enumerate(self.entities)
                    for room, x, y in (state[index],)
                    if room == room_id and entity.kind in ("crate", "meta")
                ]
                if candidates:
                    distances.append(min(candidates))

        for room_id, goals in enumerate(self.colour_goals):
            for (gx, gy), colour in goals.items():
                candidates = [
                    abs(x - gx) + abs(y - gy)
                    for index, entity in enumerate(self.entities)
                    for room, x, y in (state[index],)
                    if room == room_id and entity.kind == "crate" and entity.colour == colour
                ]
                if candidates:
                    distances.append(min(candidates))
        return max(distances)

    def solve_astar(self, max_depth: int = 100, node_limit: int = 8_000_000) -> str | None:
        if self.won(self.start):
            return ""
        serial = itertools.count()
        start_h = self._heuristic(self.start)
        queue = [(start_h, 0, next(serial), self.start)]
        best = {self.start: 0}
        parent: dict[tuple[tuple[int, int, int], ...], tuple[tuple[tuple[int, int, int], ...], str]] = {}
        nodes = 0
        while queue:
            _, distance, _, state = heapq.heappop(queue)
            if best.get(state) != distance:
                continue
            if self.won(state):
                route = []
                while state != self.start:
                    state, code = parent[state]
                    route.append(code)
                return "".join(reversed(route))
            if distance >= max_depth:
                continue
            for code, (dx, dy) in MOVES:
                moved = self.move(state, dx, dy)
                if moved is None:
                    continue
                next_distance = distance + 1
                if next_distance >= best.get(moved, 1 << 30):
                    continue
                nodes += 1
                if nodes > node_limit:
                    return None
                best[moved] = next_distance
                parent[moved] = (state, code)
                heapq.heappush(queue, (
                    next_distance + self._heuristic(moved), next_distance,
                    next(serial), moved
                ))
        return None

    def trace(self, route: str) -> Iterable[tuple[str, tuple[tuple[int, int, int], ...]]]:
        state = self.start
        yield "START", state
        lookup = dict(MOVES)
        for code in route:
            dx, dy = lookup[code]
            moved = self.move(state, dx, dy)
            if moved is None:
                raise ValueError(f"route blocked at {code}: {state}")
            state = moved
            yield code, state


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("definition", type=Path, help="Python literal containing list[list[str]]")
    parser.add_argument("--name", help="key when the definition file contains a dictionary")
    parser.add_argument("--max-depth", type=int, default=100)
    parser.add_argument("--astar", action="store_true")
    parser.add_argument("--trace", action="store_true")
    args = parser.parse_args()
    rooms = ast.literal_eval(args.definition.read_text())
    if isinstance(rooms, dict):
        if not args.name:
            raise ValueError("--name is required for a dictionary definition file")
        rooms = rooms[args.name]
    level = CoreLevel(rooms)
    route = level.solve_astar(args.max_depth) if args.astar else level.solve(args.max_depth)
    if route is None:
        print("UNSOLVED")
        return 1
    print(f"{len(route)} {route}")
    if args.trace:
        for code, state in level.trace(route):
            print(code, state)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
