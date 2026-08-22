#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Parabox
{
    /// <summary>
    /// One-shot authoring tool for rebuilding Level 13 as a harder recursive-room puzzle.
    /// Nothing runs automatically: use the menu item, then test the generated prefab yourself.
    /// </summary>
    public static class Level13HarderGenerator
    {
        const string LevelPath = "Assets/Parabox/Prefabs/Levels/Level_13.prefab";
        const string PrefabRoot = "Assets/Parabox/Prefabs/";

        // Three linked Chapter 2 tasks: relay coral cargo through the room, dock the room on its
        // socket, then route the player to the separate exit.
        const string AuthoredSolution = "LLLLLLUUUUDDDDDDLLLLDLL";

        // Leave the runtime solution field empty for this custom board. LevelLayoutRebalancer uses
        // that field as permission to add several more cargo, gate and arrow dependencies. The
        // authored route above still sets par, but the playable board stays at exactly three tasks.
        const string RuntimeSolution = "";

        // A horizontally reversed relay gives Level 13 its own silhouette and command rhythm while
        // preserving Chapter 2's room-inside-a-box identity. The renderer hides ugly mini-previews.
        static readonly string[][] Rooms =
        {
            new[]
            {
                "#########",
                "#..#.#..#",
                "#..#.1JP#",
                "#..#....#",
                "#.j.x...#",
                "#p..#...#",
                "#########",
            },
            new[]
            {
                "###.###",
                "#.....#",
                "#.....#",
                ".......",
                "#.....#",
                "#.....#",
                "###.###",
            },
        };

        static readonly Color[] RoomColors =
        {
            HtmlColor("2E6FD8"),
            HtmlColor("C43F82"),
            HtmlColor("E0912F"),
        };

        [MenuItem("Tools/Parabox/Levels/Rebuild Level 13 (Chapter 2 - 3 Tasks)", priority = 1313)]
        public static void RebuildLevel13()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Level 13?",
                    "This replaces Level_13.prefab with a distinct Chapter 2 relay: move coral cargo, " +
                    "dock the room, and reach the exit. Level 12 and all other levels stay unchanged.",
                    "Rebuild Level 13",
                    "Cancel"))
            {
                return;
            }

            ValidateDefinition();
            TilePrefabs tiles = LoadTilePrefabs();
            GameObject root = PrefabUtility.LoadPrefabContents(LevelPath);
            if (root == null)
                throw new InvalidOperationException("Could not load " + LevelPath);

            bool saved = false;
            try
            {
                ClearExistingRooms(root.transform);
                ConfigureLevelMetadata(root);

                float offsetX = 0f;
                for (int roomId = 0; roomId < Rooms.Length; roomId++)
                    offsetX = BuildRoom(root.transform, roomId, Rooms[roomId], offsetX, tiles);

                EditorUtility.SetDirty(root);
                saved = PrefabUtility.SaveAsPrefabAsset(root, LevelPath) != null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (!saved)
                throw new InvalidOperationException("Unity could not save the rebuilt Level 13 prefab.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPath);
            Selection.activeObject = generated;
            EditorGUIUtility.PingObject(generated);

            Debug.Log(
                "Level 13 rebuilt with exactly 3 Chapter 2 tasks: relay cargo, dock Room 1, and reach the exit. " +
                "Its authored route is " + AuthoredSolution.Length +
                " moves; automatic cargo/gate/arrow tasks are disabled. Open Level_13.prefab and test it in Unity.",
                generated);
        }

        static void ConfigureLevelMetadata(GameObject root)
        {
            root.name = "Level_13";
            ParaboxLevel level = root.GetComponent<ParaboxLevel>();
            if (level == null) level = root.AddComponent<ParaboxLevel>();

            // SerializedProperty keeps this one script compatible with both project copies. The
            // older copy has only name/par/solution; the newer copy also has curriculum metadata.
            var serializedLevel = new SerializedObject(level);
            SetStringIfPresent(serializedLevel, "levelName", "Reverse Room Relay");
            SetIntIfPresent(serializedLevel, "par", AuthoredSolution.Length);
            SetFloatIfPresent(serializedLevel, "difficultyRating", 3.7f);
            SetIntIfPresent(serializedLevel, "designComplexity", 1580);
            SetIntIfPresent(serializedLevel, "chapter", 2);
            SetStringIfPresent(serializedLevel, "chapterName", "Inside the Box");
            SetStringIfPresent(
                serializedLevel,
                "chapterPhilosophy",
                "Enter rooms that are also boxes");
            SetIntIfPresent(serializedLevel, "progressionRole", 1); // Practice
            SetStringIfPresent(
                serializedLevel,
                "mechanicFocus",
                "Three tasks: relay coral cargo through a movable room, dock it, then reach the exit");
            SetBoolIfPresent(serializedLevel, "introducesMechanic", false);
            SetStringIfPresent(serializedLevel, "solution", RuntimeSolution);
            serializedLevel.ApplyModifiedPropertiesWithoutUndo();
        }

        static float BuildRoom(
            Transform root,
            int roomId,
            string[] rows,
            float offsetX,
            TilePrefabs tiles)
        {
            int height = rows.Length;
            int width = rows[0].Length;

            var roomObject = new GameObject("Room_" + roomId);
            roomObject.transform.SetParent(root, false);
            roomObject.transform.localPosition = new Vector3(offsetX + width * 0.5f, 0f, 0f);

            var room = roomObject.AddComponent<RoomMarker>();
            room.roomId = roomId;
            room.width = width;
            room.height = height;

            Color roomColor = RoomColors[roomId % RoomColors.Length];
            GameObject floor = Instantiate(tiles.floor, roomObject.transform, Vector3.zero);
            ConfigurePanel(floor, width, height, roomColor);

            GameObject border = Instantiate(tiles.border, roomObject.transform, Vector3.zero);
            ConfigurePanel(border, width, height, Color.Lerp(roomColor, Color.white, 0.32f));

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    char symbol = rows[row][column];
                    if (symbol == '.') continue;

                    int x = column;
                    int y = height - 1 - row;
                    var position = new Vector3(
                        x - (width - 1) * 0.5f,
                        y - (height - 1) * 0.5f,
                        0f);

                    switch (symbol)
                    {
                        case '#':
                            AddWall(tiles.wall, roomObject.transform, position, x, y);
                            break;
                        case 'P':
                            AddPlayer(tiles.player, roomObject.transform, position, x, y);
                            break;
                        case 'p':
                            AddGoal(tiles.playerGoal, roomObject.transform, position, x, y, true);
                            break;
                        case 'x':
                            AddGoal(tiles.boxGoal, roomObject.transform, position, x, y, false);
                            break;
                        case 'J':
                            AddColoredCargo(tiles.box, roomObject.transform, position, x, y, 1);
                            break;
                        case 'N':
                            AddColoredCargo(tiles.box, roomObject.transform, position, x, y, 2);
                            break;
                        case 'Z':
                            AddColoredCargo(tiles.box, roomObject.transform, position, x, y, 3);
                            break;
                        case 'j':
                            AddColoredGoal(tiles.boxGoal, roomObject.transform, position, x, y, 1);
                            break;
                        case 'n':
                            AddColoredGoal(tiles.boxGoal, roomObject.transform, position, x, y, 2);
                            break;
                        case 'z':
                            AddColoredGoal(tiles.boxGoal, roomObject.transform, position, x, y, 3);
                            break;
                        default:
                            if (symbol >= '1' && symbol <= '9')
                            {
                                AddRoomBox(
                                    tiles.metaBox,
                                    roomObject.transform,
                                    position,
                                    x,
                                    y,
                                    symbol - '0');
                                break;
                            }

                            throw new InvalidOperationException(
                                "Unsupported Level 13 symbol '" + symbol + "'.");
                    }
                }
            }

            return offsetX + width + 2f;
        }

        static void AddWall(GameObject prefab, Transform parent, Vector3 position, int x, int y)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            var marker = instance.AddComponent<WallMarker>();
            marker.x = x;
            marker.y = y;
        }

        static void AddPlayer(GameObject prefab, Transform parent, Vector3 position, int x, int y)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            var marker = instance.AddComponent<PlayerMarker>();
            marker.x = x;
            marker.y = y;
            marker.isEcho = false;
            marker.isMirror = false;
        }

        static void AddGoal(
            GameObject prefab,
            Transform parent,
            Vector3 position,
            int x,
            int y,
            bool forPlayer)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            var marker = instance.AddComponent<GoalMarker>();
            marker.x = x;
            marker.y = y;
            marker.forPlayer = forPlayer;
        }

        static void AddColoredCargo(
            GameObject prefab,
            Transform parent,
            Vector3 position,
            int x,
            int y,
            int colour)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            var marker = instance.AddComponent<BoxMarker>();
            marker.x = x;
            marker.y = y;
            marker.containsRoomId = -1;
            marker.colour = colour;
        }

        static void AddColoredGoal(
            GameObject prefab,
            Transform parent,
            Vector3 position,
            int x,
            int y,
            int colour)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            var marker = instance.AddComponent<GoalMarker>();
            marker.x = x;
            marker.y = y;
            marker.forPlayer = false;
            marker.colour = colour;
        }

        static void AddRoomBox(
            GameObject prefab,
            Transform parent,
            Vector3 position,
            int x,
            int y,
            int containedRoomId)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            var marker = instance.AddComponent<BoxMarker>();
            marker.x = x;
            marker.y = y;
            marker.containsRoomId = containedRoomId;

            var serializedMarker = new SerializedObject(marker);
            SetBoolIfPresent(serializedMarker, "anchored", false);
            SetBoolIfPresent(serializedMarker, "playerContainer", false);
            serializedMarker.ApplyModifiedPropertiesWithoutUndo();

            Color innerColor = RoomColors[containedRoomId % RoomColors.Length];
            TintChild(instance.transform, "Backing", Color.Lerp(innerColor, Color.black, 0.5f));
            TintChild(instance.transform, "Frame", Color.Lerp(innerColor, Color.white, 0.3f));
        }

        static void TintChild(Transform root, string childName, Color color)
        {
            Transform child = root.Find(childName);
            if (child == null) return;
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.color = color;
        }

        static void ConfigurePanel(GameObject panel, int width, int height, Color color)
        {
            SpriteRenderer renderer = panel.GetComponent<SpriteRenderer>();
            if (renderer == null)
                throw new InvalidOperationException(panel.name + " needs a SpriteRenderer.");

            renderer.size = new Vector2(width, height);
            renderer.color = color;
        }

        static GameObject Instantiate(GameObject prefab, Transform parent, Vector3 localPosition)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Could not instantiate " + prefab.name + ".");

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        static void ClearExistingRooms(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        static TilePrefabs LoadTilePrefabs()
        {
            return new TilePrefabs
            {
                floor = LoadPrefab("Floor.prefab"),
                border = LoadPrefab("Border.prefab"),
                wall = LoadPrefab("Wall.prefab"),
                player = LoadPrefab("Player.prefab"),
                playerGoal = LoadPrefab("PlayerGoal.prefab"),
                boxGoal = LoadPrefab("BoxGoal.prefab"),
                box = LoadPrefab("Box.prefab"),
                metaBox = LoadPrefab("MetaBox.prefab"),
            };
        }

        static GameObject LoadPrefab(string fileName)
        {
            string path = PrefabRoot + fileName;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Missing required prefab: " + path);
            return prefab;
        }

        static void ValidateDefinition()
        {
            if (string.IsNullOrEmpty(AuthoredSolution))
                throw new InvalidOperationException("The Level 13 authored route cannot be empty.");

            int players = 0;
            int playerGoals = 0;
            int roomGoals = 0;
            int movableRooms = 0;
            int coloredCargo = 0;
            int coloredGoals = 0;
            var referencedRooms = new bool[Rooms.Length];

            for (int roomId = 0; roomId < Rooms.Length; roomId++)
            {
                string[] rows = Rooms[roomId];
                if (rows == null || rows.Length == 0 || string.IsNullOrEmpty(rows[0]))
                    throw new InvalidOperationException("Room " + roomId + " is empty.");

                int width = rows[0].Length;
                for (int row = 0; row < rows.Length; row++)
                {
                    if (rows[row].Length != width)
                        throw new InvalidOperationException("Room " + roomId + " has uneven row widths.");

                    foreach (char symbol in rows[row])
                    {
                        if (symbol == 'P') players++;
                        else if (symbol == 'p') playerGoals++;
                        else if (symbol == 'x') roomGoals++;
                        else if (symbol == 'J' || symbol == 'N' || symbol == 'Z') coloredCargo++;
                        else if (symbol == 'j' || symbol == 'n' || symbol == 'z') coloredGoals++;
                        else if (symbol >= '1' && symbol <= '9')
                        {
                            int targetRoom = symbol - '0';
                            movableRooms++;
                            if (targetRoom <= 0 || targetRoom >= Rooms.Length)
                                throw new InvalidOperationException(
                                    "Room box " + targetRoom + " has no matching room definition.");
                            if (referencedRooms[targetRoom])
                                throw new InvalidOperationException(
                                    "Room " + targetRoom + " is referenced more than once.");
                            referencedRooms[targetRoom] = true;
                        }
                    }
                }
            }

            if (players != 1 || playerGoals != 1)
                throw new InvalidOperationException("Level 13 needs one player and one player goal.");
            if (coloredCargo != 1 || coloredGoals != 1)
                throw new InvalidOperationException("Level 13 needs one coral cargo and one matching goal.");
            if (movableRooms != 1 || roomGoals != 1 || Rooms.Length != 2)
                throw new InvalidOperationException("Level 13 needs one movable room and one room socket.");
            if (playerGoals + coloredGoals + roomGoals != 3)
                throw new InvalidOperationException("Level 13 must contain exactly three completion tasks.");
            if (!referencedRooms[1])
                throw new InvalidOperationException("Level 13's inner room is never placed on the board.");
        }

        static Color HtmlColor(string hex)
        {
            if (!ColorUtility.TryParseHtmlString("#" + hex, out Color color))
                throw new InvalidOperationException("Invalid color: " + hex);
            return color;
        }

        static void SetStringIfPresent(SerializedObject target, string propertyName, string value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null) property.stringValue = value;
        }

        static void SetIntIfPresent(SerializedObject target, string propertyName, int value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null) property.intValue = value;
        }

        static void SetFloatIfPresent(SerializedObject target, string propertyName, float value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null) property.floatValue = value;
        }

        static void SetBoolIfPresent(SerializedObject target, string propertyName, bool value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null) property.boolValue = value;
        }

        struct TilePrefabs
        {
            public GameObject floor;
            public GameObject border;
            public GameObject wall;
            public GameObject player;
            public GameObject playerGoal;
            public GameObject boxGoal;
            public GameObject box;
            public GameObject metaBox;
        }
    }
}
#endif
