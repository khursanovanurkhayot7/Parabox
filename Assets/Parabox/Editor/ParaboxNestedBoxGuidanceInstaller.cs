#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Parabox.EditorTools
{
    /// <summary>
    /// Adds the clean nested-box closed-wall signal host to Game.unity in Edit Mode. The runtime
    /// component creates its own compact lamps; no level prefab or difficulty value is edited.
    /// </summary>
    public static class ParaboxNestedBoxGuidanceInstaller
    {
        const string GameScenePath = "Assets/Parabox/Scenes/Game.unity";

        [MenuItem("Tools/Parabox/Install Clean Nested-Box Door Signals", priority = 3)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Exit Play Mode before installing nested-box guidance.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException(
                    "Unity is compiling or importing. Wait until it finishes, then run this command again.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            GameManager game = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                game = root.GetComponentInChildren<GameManager>(true);
                if (game != null) break;
            }
            if (game == null)
                throw new InvalidOperationException(
                    "Game.unity has no GameManager. Run Tools/Parabox/Generate Prebuilt UI first.");

            NestedBoxGuidanceFx guidance = game.GetComponent<NestedBoxGuidanceFx>();
            if (guidance == null) guidance = Undo.AddComponent<NestedBoxGuidanceFx>(game.gameObject);

            EditorUtility.SetDirty(guidance);
            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Unity could not save Game.unity.");
            AssetDatabase.SaveAssets();

            const string result =
                "Clean nested-box door signals installed.\n\n"
                + "• Open sides keep the clean cyan doorway cut.\n"
                + "• Closed sides remain solid orange walls.\n"
                + "• A compact premium red lamp pulses on each closed wall, outside and inside the box.\n"
                + "• No arrows, X icons, ENTER/EXIT text or overlays are created.\n"
                + "• Levels, move limits, scoring and difficulty were not changed.\n\n"
                + "No tutorial was added and Play Mode was not started.";
            Debug.Log("[Parabox] " + result, guidance);
            EditorUtility.DisplayDialog("Nested-Box Guidance", result, "OK");
        }

        [MenuItem("Tools/Parabox/Validate Nested-Box Signal Visibility")]
        public static void ValidateSignalVisibility()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run signal visibility validation in Edit Mode.");

            var fixture = new GameObject("NestedSignalVisibilityValidation")
                { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                GameManager manager = fixture.AddComponent<GameManager>();
                Transform main = ValidationChild(fixture.transform, "Room_0");
                SpriteRenderer outsideFloor = ValidationSprite(main, "OutsideFloor");
                Transform box = ValidationBox(main, "OuterBox");
                Transform room = ValidationChild(box, "Room_1");
                Transform innerBox = ValidationBox(room, "InnerBox");
                Transform innerRoom = ValidationChild(innerBox, "Room_2");
                SpriteRenderer[] outerLights = ValidationLights(box);
                SpriteRenderer[] innerLights = ValidationLights(innerBox);
                Transform doorway = ValidationChild(box, "NestedDoorways");
                SpriteRenderer cyan = ValidationSprite(doorway, "NestedDoorwayFloor_Top");
                Transform openSignal = ValidationChild(box, "NestedClosedSignal_OpenTest");
                ValidationSprite(openSignal, "ClosedSignal_Lamp");
                openSignal.gameObject.SetActive(false);

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GameManager).GetField("boardRoot", flags).SetValue(manager, fixture.transform);
                var rooms = (Dictionary<int, Transform>)typeof(GameManager)
                    .GetField("roomRoots", flags).GetValue(manager);
                rooms.Add(0, main);
                rooms.Add(1, room);
                rooms.Add(2, innerRoom);
                MethodInfo focus = typeof(GameManager).GetMethod("FocusRoom", flags);
                foreach (int roomId in new[] { 1, 2, 1, 0 })
                {
                    focus.Invoke(manager, new object[] { roomId });
                    foreach (SpriteRenderer lamp in outerLights)
                        Require(lamp.enabled == (roomId != 2), "outer lamp visibility at room " + roomId);
                    foreach (SpriteRenderer lamp in innerLights)
                        Require(lamp.enabled, "inner lamp visibility at room " + roomId);
                    Require(outsideFloor.enabled == (roomId == 0), "parent-room isolation");
                    Require(cyan.enabled == (roomId != 2), "cyan doorway stays with its enclosing box");
                    Require(!openSignal.gameObject.activeSelf, "open sides must not gain red lights");
                }
                Debug.Log("Nested-box visibility passed: all four lamp sides survive entry/exit and "
                    + "two nesting depths; cyan doorways remain visible; unrelated parent art stays hidden.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fixture);
            }
        }

        static Transform ValidationChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        static SpriteRenderer ValidationSprite(Transform parent, string name)
            => ValidationChild(parent, name).gameObject.AddComponent<SpriteRenderer>();

        static Transform ValidationBox(Transform parent, string name)
        {
            Transform box = ValidationChild(parent, name);
            ValidationSprite(box, "Frame");
            ValidationSprite(box, "Backing");
            return box;
        }

        static SpriteRenderer[] ValidationLights(Transform box)
        {
            var lamps = new List<SpriteRenderer>();
            foreach (string side in new[] { "Top", "Bottom", "Left", "Right" })
            {
                Transform signal = ValidationChild(box, "NestedClosedSignal_" + side);
                foreach (string part in new[] { "Housing", "Rim", "Glow", "Lamp", "Highlight" })
                    lamps.Add(ValidationSprite(signal, "ClosedSignal_" + part));
            }
            return lamps.ToArray();
        }

        static void Require(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException("Nested-box visibility failed: " + message);
        }
    }
}
#endif
