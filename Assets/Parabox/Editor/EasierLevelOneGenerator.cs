#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    /// <summary>
    /// Manual authoring tool for replacing only Level 1 with a clearer beginner board.
    /// Nothing runs automatically and this script never enters Play Mode or tests the level.
    /// </summary>
    public static class EasierLevelOneGenerator
    {
        const string LevelPath = "Assets/Parabox/Prefabs/Levels/Level_1.prefab";
        const string PrefabRoot = "Assets/Parabox/Prefabs/";
        const string AuthoredSolution = "RRRURRDR";

        // One player, one ordinary crate, one crate goal and one player exit. The crate is the only
        // obstacle: after the straight delivery, the player takes the open upper lane to the exit.
        static readonly string[] Board =
        {
            "#########",
            "#.......#",
            "#P.b.x.p#",
            "#.......#",
            "#########",
        };

        static readonly Color RoomColor = HtmlColor("2E6FD8");

        [MenuItem("Tools/Parabox/Levels/Create Easier Level 1 (Straight Delivery)", priority = 1301)]
        public static void CreateEasierLevelOne()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Exit Play Mode",
                    "Exit Play Mode before creating Level 1.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Create easier Level 1?",
                    "This replaces only Level_1.prefab with the approved straight-delivery board. "
                    + "It does not enter Play Mode, test the game, or change Levels 2-50.",
                    "Create Level 1",
                    "Cancel"))
                return;

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
                BuildRoom(root.transform, tiles);
                EditorUtility.SetDirty(root);
                saved = PrefabUtility.SaveAsPrefabAsset(root, LevelPath) != null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (!saved)
                throw new InvalidOperationException("Unity could not save the easier Level 1 prefab.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            GameObject generated = AssetDatabase.LoadAssetAtPath<GameObject>(LevelPath);
            Selection.activeObject = generated;
            EditorGUIUtility.PingObject(generated);
            Debug.Log(
                "Created the easier Level 1 only. Route metadata: " + AuthoredSolution
                + ". Open Level_1.prefab and test it manually when ready.",
                generated);
        }

        static void ConfigureLevelMetadata(GameObject root)
        {
            root.name = "Level_1";
            ParaboxLevel level = root.GetComponent<ParaboxLevel>();
            if (level == null) level = root.AddComponent<ParaboxLevel>();

            var serializedLevel = new SerializedObject(level);
            SetString(serializedLevel, "levelName", "First Delivery");
            SetInt(serializedLevel, "par", AuthoredSolution.Length);
            SetFloat(serializedLevel, "difficultyRating", 1f);
            SetInt(serializedLevel, "designComplexity", 300);
            SetInt(serializedLevel, "chapter", 1);
            SetString(serializedLevel, "chapterName", "Foundations");
            SetString(serializedLevel, "chapterPhilosophy", "Learn one clear action at a time");
            SetInt(serializedLevel, "progressionRole", 0);
            SetString(
                serializedLevel,
                "mechanicFocus",
                "Push one crate along the obvious lane, then walk around it to the exit");
            SetBool(serializedLevel, "introducesMechanic", true);
            SetString(serializedLevel, "solution", AuthoredSolution);
            SetBool(serializedLevel, "preserveRuntimeLayoutProof", false);
            SetString(serializedLevel, "runtimeLayoutProof", string.Empty);
            serializedLevel.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildRoom(Transform root, TilePrefabs tiles)
        {
            int height = Board.Length;
            int width = Board[0].Length;
            var roomObject = new GameObject("Room_0");
            roomObject.transform.SetParent(root, false);
            roomObject.transform.localPosition = new Vector3(width * 0.5f, 0f, 0f);

            var room = roomObject.AddComponent<RoomMarker>();
            room.roomId = 0;
            room.width = width;
            room.height = height;

            GameObject floor = Instantiate(tiles.floor, roomObject.transform, Vector3.zero);
            ConfigurePanel(floor, width, height, RoomColor);
            GameObject border = Instantiate(tiles.border, roomObject.transform, Vector3.zero);
            ConfigurePanel(border, width, height, Color.Lerp(RoomColor, Color.white, 0.32f));

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    char symbol = Board[row][column];
                    if (symbol == '.') continue;
                    int x = column;
                    int y = height - 1 - row;
                    var position = new Vector3(
                        x - (width - 1) * 0.5f,
                        y - (height - 1) * 0.5f,
                        0f);

                    switch (symbol)
                    {
                        case '#': AddWall(tiles.wall, roomObject.transform, position, x, y); break;
                        case 'P': AddPlayer(tiles.player, roomObject.transform, position, x, y); break;
                        case 'b': AddBox(tiles.box, roomObject.transform, position, x, y); break;
                        case 'x': AddGoal(tiles.boxGoal, roomObject.transform, position, x, y, false); break;
                        case 'p': AddGoal(tiles.playerGoal, roomObject.transform, position, x, y, true); break;
                        default:
                            throw new InvalidOperationException(
                                "Unsupported easier Level 1 symbol '" + symbol + "'.");
                    }
                }
            }
        }

        static void AddWall(GameObject prefab, Transform parent, Vector3 position, int x, int y)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            WallMarker marker = instance.AddComponent<WallMarker>();
            marker.x = x;
            marker.y = y;
        }

        static void AddPlayer(GameObject prefab, Transform parent, Vector3 position, int x, int y)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            PlayerMarker marker = instance.AddComponent<PlayerMarker>();
            marker.x = x;
            marker.y = y;
            marker.isEcho = false;
            marker.isMirror = false;
        }

        static void AddBox(GameObject prefab, Transform parent, Vector3 position, int x, int y)
        {
            GameObject instance = Instantiate(prefab, parent, position);
            BoxMarker marker = instance.AddComponent<BoxMarker>();
            marker.x = x;
            marker.y = y;
            marker.containsRoomId = -1;
            marker.colour = 0;
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
            GoalMarker marker = instance.AddComponent<GoalMarker>();
            marker.x = x;
            marker.y = y;
            marker.forPlayer = forPlayer;
            marker.colour = 0;
        }

        static GameObject Instantiate(GameObject prefab, Transform parent, Vector3 localPosition)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Could not instantiate " + prefab.name + ".");
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        static void ConfigurePanel(GameObject panel, int width, int height, Color color)
        {
            SpriteRenderer renderer = panel.GetComponent<SpriteRenderer>();
            if (renderer == null)
                throw new InvalidOperationException(panel.name + " needs a SpriteRenderer.");
            renderer.size = new Vector2(width, height);
            renderer.color = color;
        }

        static void ClearExistingRooms(Transform root)
        {
            for (int index = root.childCount - 1; index >= 0; index--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(index).gameObject);
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
            int players = 0;
            int boxes = 0;
            int boxGoals = 0;
            int playerGoals = 0;
            int width = Board[0].Length;
            foreach (string row in Board)
            {
                if (row.Length != width)
                    throw new InvalidOperationException("The easier Level 1 board has uneven rows.");
                foreach (char symbol in row)
                {
                    if (symbol == 'P') players++;
                    else if (symbol == 'b') boxes++;
                    else if (symbol == 'x') boxGoals++;
                    else if (symbol == 'p') playerGoals++;
                }
            }
            if (players != 1 || boxes != 1 || boxGoals != 1 || playerGoals != 1)
                throw new InvalidOperationException(
                    "The easier Level 1 must contain exactly one player, box, box goal and exit.");
        }

        static Color HtmlColor(string hex)
        {
            if (!ColorUtility.TryParseHtmlString("#" + hex, out Color color))
                throw new InvalidOperationException("Invalid color: " + hex);
            return color;
        }

        static void SetString(SerializedObject target, string name, string value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property != null) property.stringValue = value;
        }

        static void SetInt(SerializedObject target, string name, int value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property != null) property.intValue = value;
        }

        static void SetFloat(SerializedObject target, string name, float value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property != null) property.floatValue = value;
        }

        static void SetBool(SerializedObject target, string name, bool value)
        {
            SerializedProperty property = target.FindProperty(name);
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
        }
    }
}
#endif
