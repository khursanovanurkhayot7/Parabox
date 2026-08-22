using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    /// <summary>
    /// Swaps only the authored Room hierarchies between Levels 11-16 and Levels 21-26.
    /// The prefab roots and every ParaboxLevel field stay in their original slots, so level
    /// number, chapter, name, par, timer/move-limit inputs and difficulty metadata never move.
    /// </summary>
    public static class ChapterTwoThreeGameplaySwap
    {
        const string LevelDirectory = "Assets/Parabox/Prefabs/Levels";

        sealed class RoomCopy
        {
            public GameObject gameObject;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public int siblingIndex;
        }

        [MenuItem(
            "Tools/Parabox/Levels/Swap Selected Chapter 2-3 Gameplay (11-16 <-> 21-26)",
            priority = 1322)]
        public static void SwapGameplayOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Chapter Gameplay Swap",
                    "Stop Play Mode first. This command edits prefab Room objects only.",
                    "OK");
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(
                    "Chapter Gameplay Swap",
                    "Unity is compiling or importing. Wait until it finishes, then run this command again.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Swap Chapter 2 and 3 gameplay?",
                    "This swaps only the puzzle Room objects: Level 11 <-> 21, 12 <-> 22, "
                    + "... 16 <-> 26. Levels 17-20 and 27-30 are not touched. Level numbers, "
                    + "chapter data, names, par, move limits, "
                    + "timers, difficulty and solutions stay in their current slots. "
                    + "Running this command a second time swaps the rooms back.",
                    "Swap Gameplay",
                    "Cancel"))
            {
                return;
            }

            PreflightAllPairs();

            try
            {
                AssetDatabase.DisallowAutoRefresh();
                for (int offset = 0; offset < 6; offset++)
                    SwapPair(11 + offset, 21 + offset);

                AssetDatabase.SaveAssets();
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
                AssetDatabase.Refresh();
            }

            GameObject levelTwentySix = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPath(26));
            Selection.activeObject = levelTwentySix;
            if (levelTwentySix != null) EditorGUIUtility.PingObject(levelTwentySix);

            EditorUtility.DisplayDialog(
                "Gameplay Swap Complete",
                "Only Room gameplay was swapped between Levels 11-16 and 21-26. Levels 17-20 "
                + "and 27-30 were not touched. All root metadata and timer/move settings stayed "
                + "in their original levels.",
                "OK");
            Debug.Log("Parabox: swapped gameplay Rooms for Level 11<->21 through Level 16<->26. "
                + "Levels 17-20 and 27-30 were not touched; no ParaboxLevel metadata was changed.");
        }

        static void PreflightAllPairs()
        {
            for (int levelNumber = 11; levelNumber <= 26; levelNumber++)
            {
                if (levelNumber >= 17 && levelNumber <= 20) continue;
                string path = LevelPath(levelNumber);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    throw new InvalidOperationException("Missing level prefab: " + path);
                if (prefab.GetComponent<ParaboxLevel>() == null)
                    throw new InvalidOperationException(path + " is missing ParaboxLevel metadata.");

                int roomCount = 0;
                foreach (Transform child in prefab.transform)
                    if (child.GetComponent<RoomMarker>() != null) roomCount++;
                if (roomCount == 0)
                    throw new InvalidOperationException(path + " has no direct Room objects to swap.");
            }
        }

        static void SwapPair(int chapterTwoLevel, int chapterThreeLevel)
        {
            string chapterTwoPath = LevelPath(chapterTwoLevel);
            string chapterThreePath = LevelPath(chapterThreeLevel);
            GameObject chapterTwoRoot = null;
            GameObject chapterThreeRoot = null;

            try
            {
                chapterTwoRoot = PrefabUtility.LoadPrefabContents(chapterTwoPath);
                chapterThreeRoot = PrefabUtility.LoadPrefabContents(chapterThreePath);
                if (chapterTwoRoot == null || chapterThreeRoot == null)
                    throw new InvalidOperationException(
                        $"Could not open Level {chapterTwoLevel} and Level {chapterThreeLevel}.");

                ParaboxLevel chapterTwoInfo = chapterTwoRoot.GetComponent<ParaboxLevel>();
                ParaboxLevel chapterThreeInfo = chapterThreeRoot.GetComponent<ParaboxLevel>();
                if (chapterTwoInfo == null || chapterThreeInfo == null)
                    throw new InvalidOperationException("A loaded level root lost its ParaboxLevel component.");

                // JSON is used only as an exact before/after assertion. It is never copied from
                // one level to another, so all level metadata remains byte-for-byte equivalent.
                string chapterTwoMetadata = EditorJsonUtility.ToJson(chapterTwoInfo);
                string chapterThreeMetadata = EditorJsonUtility.ToJson(chapterThreeInfo);

                List<RoomCopy> chapterTwoRooms = CloneDirectRooms(chapterTwoRoot);
                List<RoomCopy> chapterThreeRooms = CloneDirectRooms(chapterThreeRoot);
                if (chapterTwoRooms.Count == 0 || chapterThreeRooms.Count == 0)
                    throw new InvalidOperationException("Both prefabs must contain direct Room objects.");

                DestroyDirectRooms(chapterTwoRoot);
                DestroyDirectRooms(chapterThreeRoot);
                AttachRooms(chapterThreeRooms, chapterTwoRoot.transform);
                AttachRooms(chapterTwoRooms, chapterThreeRoot.transform);

                ValidateRoomIds(chapterTwoRoot, chapterTwoLevel);
                ValidateRoomIds(chapterThreeRoot, chapterThreeLevel);

                if (EditorJsonUtility.ToJson(chapterTwoInfo) != chapterTwoMetadata
                    || EditorJsonUtility.ToJson(chapterThreeInfo) != chapterThreeMetadata)
                {
                    throw new InvalidOperationException(
                        "The gameplay swap attempted to change root metadata and was stopped.");
                }

                if (PrefabUtility.SaveAsPrefabAsset(chapterTwoRoot, chapterTwoPath) == null)
                    throw new InvalidOperationException("Unity could not save " + chapterTwoPath);
                if (PrefabUtility.SaveAsPrefabAsset(chapterThreeRoot, chapterThreePath) == null)
                    throw new InvalidOperationException("Unity could not save " + chapterThreePath);
            }
            finally
            {
                if (chapterTwoRoot != null) PrefabUtility.UnloadPrefabContents(chapterTwoRoot);
                if (chapterThreeRoot != null) PrefabUtility.UnloadPrefabContents(chapterThreeRoot);
            }
        }

        static List<RoomCopy> CloneDirectRooms(GameObject sourceRoot)
        {
            var result = new List<RoomCopy>();
            foreach (Transform child in sourceRoot.transform)
            {
                if (child.GetComponent<RoomMarker>() == null) continue;

                GameObject clone = UnityEngine.Object.Instantiate(child.gameObject);
                clone.name = child.gameObject.name;
                result.Add(new RoomCopy
                {
                    gameObject = clone,
                    localPosition = child.localPosition,
                    localRotation = child.localRotation,
                    localScale = child.localScale,
                    siblingIndex = child.GetSiblingIndex(),
                });
            }
            result.Sort((left, right) => left.siblingIndex.CompareTo(right.siblingIndex));
            return result;
        }

        static void DestroyDirectRooms(GameObject root)
        {
            var rooms = new List<GameObject>();
            foreach (Transform child in root.transform)
                if (child.GetComponent<RoomMarker>() != null) rooms.Add(child.gameObject);
            foreach (GameObject room in rooms)
                UnityEngine.Object.DestroyImmediate(room);
        }

        static void AttachRooms(List<RoomCopy> rooms, Transform destination)
        {
            for (int index = 0; index < rooms.Count; index++)
            {
                RoomCopy room = rooms[index];
                room.gameObject.transform.SetParent(destination, false);
                room.gameObject.transform.localPosition = room.localPosition;
                room.gameObject.transform.localRotation = room.localRotation;
                room.gameObject.transform.localScale = room.localScale;
                room.gameObject.transform.SetSiblingIndex(
                    Mathf.Clamp(room.siblingIndex, 0, destination.childCount - 1));
            }
        }

        static void ValidateRoomIds(GameObject root, int levelNumber)
        {
            var roomIds = new HashSet<int>();
            foreach (Transform child in root.transform)
            {
                RoomMarker marker = child.GetComponent<RoomMarker>();
                if (marker == null) continue;
                if (!roomIds.Add(marker.roomId))
                    throw new InvalidOperationException(
                        $"Level {levelNumber} contains duplicate Room {marker.roomId} after the swap.");
            }

            if (!roomIds.Contains(0))
                throw new InvalidOperationException(
                    $"Level {levelNumber} has no outer Room_0 after the swap.");
            for (int roomId = 0; roomId < roomIds.Count; roomId++)
                if (!roomIds.Contains(roomId))
                    throw new InvalidOperationException(
                        $"Level {levelNumber} is missing Room {roomId} after the swap.");
        }

        static string LevelPath(int levelNumber)
            => LevelDirectory + "/Level_" + levelNumber + ".prefab";
    }
}
