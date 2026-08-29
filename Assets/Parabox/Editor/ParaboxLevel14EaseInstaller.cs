#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    /// <summary>
    /// One Edit-Mode command that simplifies only Level 14. It removes the authored one-way
    /// restriction; LevelLayoutRebalancer separately exempts this level from injected arrows and
    /// button/gate dependencies. The recursive-room puzzle, its targets and its winning route stay
    /// untouched. This command never enters Play Mode and does not create a tutorial.
    /// </summary>
    public static class ParaboxLevel14EaseInstaller
    {
        const string LevelPath = "Assets/Parabox/Prefabs/Levels/Level_14.prefab";
        const int LevelIndex = 13;

        [MenuItem("Tools/Parabox/Levels/Make Level 14 Easier (No Tutorial)", priority = 1314)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Exit Play Mode before simplifying Level 14.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException(
                    "Unity is compiling or importing. Wait until it finishes, then run this command again.");

            GameObject root = PrefabUtility.LoadPrefabContents(LevelPath);
            if (root == null)
                throw new InvalidOperationException("Could not load " + LevelPath);

            int removedArrows = 0;
            bool saved = false;
            string proof = string.Empty;
            int proofMoves = 0;
            int moveLimit = 0;

            try
            {
                ParaboxLevel level = root.GetComponent<ParaboxLevel>();
                if (level == null)
                    throw new InvalidOperationException(
                        "Level_14.prefab is missing its ParaboxLevel component.");
                if (string.IsNullOrWhiteSpace(level.solution))
                    throw new InvalidOperationException(
                        "Level 14 has no stored winning route, so the easier layout cannot be proved safely.");

                int roomCount = root.GetComponentsInChildren<RoomMarker>(true).Length;
                int boxCount = root.GetComponentsInChildren<BoxMarker>(true).Length;
                int playerCount = root.GetComponentsInChildren<PlayerMarker>(true).Length;
                int goalCount = root.GetComponentsInChildren<GoalMarker>(true).Length;
                if (roomCount < 2 || boxCount < 1 || playerCount < 1 || goalCount < 1)
                    throw new InvalidOperationException(
                        "Level 14 is missing its recursive room, player, box, or target markers.");

                OneWayMarker[] arrows = root.GetComponentsInChildren<OneWayMarker>(true);
                for (int i = 0; i < arrows.Length; i++)
                {
                    OneWayMarker arrow = arrows[i];
                    if (arrow == null) continue;
                    GameObject holder = arrow.gameObject;
                    Component[] components = holder.GetComponents<Component>();
                    if (components.Length == 2) UnityEngine.Object.DestroyImmediate(holder);
                    else UnityEngine.Object.DestroyImmediate(arrow);
                    removedArrows++;
                }

                if (root.GetComponentsInChildren<OneWayMarker>(true).Length != 0)
                    throw new InvalidOperationException(
                        "Level 14 still contains an authored one-way marker after simplification.");

                // The runtime parser is the same model gameplay uses. Replaying the stored route
                // here verifies the easier board without entering Play Mode or launching the game.
                LevelModel model = LevelParser.Parse(root, LevelIndex);
                if (model.rebalanceOneWays != 0)
                    throw new InvalidOperationException(
                        "Runtime still injected one-way arrows into Level 14. Recompile scripts first.");
                if (model.curriculumReuses.Contains(MechanicCatalog.Id.ButtonGate))
                    throw new InvalidOperationException(
                        "Runtime still injected a button/gate dependency into Level 14.");

                proof = level.solution.Trim().ToUpperInvariant();
                for (int step = 0; step < proof.Length; step++)
                {
                    if (!TryDirection(proof[step], out Vector2Int direction))
                        throw new InvalidOperationException(
                            $"Level 14 has invalid route character '{proof[step]}' at move {step + 1}.");
                    if (!model.TryMovePlayer(direction))
                        throw new InvalidOperationException(
                            $"The stored Level 14 route is blocked at move {step + 1} ({proof[step]}).");
                }
                if (!model.IsWon())
                    throw new InvalidOperationException(
                        "The stored Level 14 route ends without completing every target.");

                proofMoves = model.MoveCount;
                moveLimit = GameManager.MoveLimitForLevel(LevelIndex, level.par);
                if (proofMoves > moveLimit)
                    throw new InvalidOperationException(
                        $"The Level 14 proof needs {proofMoves} moves but the limit is {moveLimit}.");

                // Removing the arrow must be the only structural prefab change.
                if (root.GetComponentsInChildren<RoomMarker>(true).Length != roomCount
                    || root.GetComponentsInChildren<BoxMarker>(true).Length != boxCount
                    || root.GetComponentsInChildren<PlayerMarker>(true).Length != playerCount
                    || root.GetComponentsInChildren<GoalMarker>(true).Length != goalCount)
                    throw new InvalidOperationException(
                        "A protected Level 14 room, player, box, or target changed unexpectedly.");

                EditorUtility.SetDirty(root);
                saved = PrefabUtility.SaveAsPrefabAsset(root, LevelPath) != null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (!saved)
                throw new InvalidOperationException(
                    "Unity could not save the easier Level 14 prefab.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPath);
            Selection.activeObject = generated;
            EditorGUIUtility.PingObject(generated);

            string result =
                $"Level 14 simplified in Edit Mode.\n\n"
                + $"Removed authored arrows: {removedArrows}\n"
                + "Runtime arrows: 0\n"
                + "Runtime button/gate dependency: 0\n"
                + $"Winning route: {proofMoves}/{moveLimit} moves\n\n"
                + "The recursive-room puzzle and all targets were preserved. No tutorial was added, "
                + "and Play Mode was not started.";
            Debug.Log("[Parabox] " + result, generated);
            EditorUtility.DisplayDialog("Level 14 Is Easier", result, "OK");
        }

        static bool TryDirection(char code, out Vector2Int direction)
        {
            switch (code)
            {
                case 'U': direction = Vector2Int.up; return true;
                case 'R': direction = Vector2Int.right; return true;
                case 'D': direction = Vector2Int.down; return true;
                case 'L': direction = Vector2Int.left; return true;
                default: direction = default; return false;
            }
        }
    }
}
#endif
