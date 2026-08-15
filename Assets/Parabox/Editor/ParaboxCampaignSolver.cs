using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    // Editor-only proof search used while authoring the campaign. It drives the exact runtime
    // LevelModel and undoes every explored move, so generated routes cannot disagree with the
    // shipped movement rules. Nothing in this file is included in a player build.
    public static class ParaboxCampaignSolver
    {
        static readonly (char code, Vector2Int direction)[] Moves =
        {
            ('U', Vector2Int.up),
            ('R', Vector2Int.right),
            ('D', Vector2Int.down),
            ('L', Vector2Int.left)
        };

        public static string FindShortest(GameObject prefab, int minimumDepth, int maximumDepth,
                                          int nodeLimit = 1500000)
        {
            LevelModel model = LevelParser.Parse(prefab);
            if (model.player == null) return null;
            if (model.IsWon()) return string.Empty;

            int nodes = 0;
            for (int depth = Mathf.Max(1, minimumDepth); depth <= maximumDepth; depth++)
            {
                var seen = new Dictionary<string, int>(4096, StringComparer.Ordinal);
                var route = new StringBuilder(depth);
                if (Search(model, depth, route, seen, ref nodes, nodeLimit))
                    return route.ToString();
                if (nodes >= nodeLimit) break;
            }
            return null;
        }

        static bool Search(LevelModel model, int remaining, StringBuilder route,
                           Dictionary<string, int> seen, ref int nodes, int nodeLimit)
        {
            if (model.IsWon()) return true;
            if (remaining == 0 || ++nodes > nodeLimit) return false;

            string key = StateKey(model);
            if (seen.TryGetValue(key, out int previousRemaining) && previousRemaining >= remaining)
                return false;
            seen[key] = remaining;

            for (int i = 0; i < Moves.Length; i++)
            {
                var move = Moves[i];
                if (!model.TryMovePlayer(move.direction)) continue;
                route.Append(move.code);
                if (Search(model, remaining - 1, route, seen, ref nodes, nodeLimit)) return true;
                route.Length--;
                model.Undo();
                if (nodes >= nodeLimit) return false;
            }
            return false;
        }

        static string StateKey(LevelModel model)
        {
            var key = new StringBuilder(128 + model.entities.Count * 14);
            for (int i = 0; i < model.entities.Count; i++)
            {
                PEntity entity = model.entities[i];
                key.Append(entity.roomId).Append(',').Append(entity.pos.x).Append(',')
                   .Append(entity.pos.y).Append(',').Append(entity.sunk ? '1' : '0').Append(';');
            }
            key.Append('|').Append(model.latched ? '1' : '0').Append(',').Append(model.beat)
               .Append(',').Append(model.hasLast ? '1' : '0').Append(',')
               .Append(model.lastDir.x).Append(',').Append(model.lastDir.y)
               .Append(',').Append(model.hasForced ? '1' : '0').Append(',')
               .Append(model.forcedDir.x).Append(',').Append(model.forcedDir.y);

            foreach (var roomPair in model.rooms)
            {
                PRoom room = roomPair.Value;
                AppendCells(key, 'f', room.id, room.filled);
                AppendCells(key, 'b', room.id, room.broken);
                AppendCells(key, 'r', room.id, room.smashed);
            }
            foreach (var collected in model.collected)
                key.Append("k:").Append(collected.Item1).Append(',').Append(collected.Item2.x)
                   .Append(',').Append(collected.Item2.y).Append(';');
            return key.ToString();
        }

        static void AppendCells(StringBuilder key, char family, int room,
                                HashSet<Vector2Int> cells)
        {
            foreach (Vector2Int cell in cells)
                key.Append(family).Append(':').Append(room).Append(',').Append(cell.x)
                   .Append(',').Append(cell.y).Append(';');
        }

        public static string SolveAndStore(string prefabPath, int minimumDepth, int maximumDepth)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                string solution = FindShortest(root, minimumDepth, maximumDepth);
                if (solution == null)
                    throw new InvalidOperationException(
                        $"No solution found for {prefabPath} between {minimumDepth} and {maximumDepth} moves.");

                ParaboxLevel info = root.GetComponent<ParaboxLevel>();
                info.solution = solution;
                info.par = solution.Length;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return solution;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
