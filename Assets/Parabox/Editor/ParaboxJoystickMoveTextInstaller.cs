#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Parabox.EditorTools
{
    // One-click scene adjustment for the small MOVE caption inside ArcadeJoystickHud.
    // It changes no joystick graphics, input, score HUD or other gameplay controls.
    public static class ParaboxJoystickMoveTextInstaller
    {
        const string GameScenePath = "Assets/Parabox/Scenes/Game.unity";

        [MenuItem("Tools/Parabox/Move Joystick MOVE Text Right", priority = 5)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before moving the joystick text.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            Transform joystick = FindNamedInScene(scene, "ArcadeJoystickHud");
            RectTransform action = joystick != null ? joystick.Find("Action") as RectTransform : null;
            if (action == null)
                throw new InvalidOperationException(
                    "ArcadeJoystickHud/Action is missing. Run Tools/Parabox/Generate Prebuilt UI first.");

            Undo.RecordObject(action, "Move joystick MOVE text right");
            action.anchoredPosition = new Vector2(67f, action.anchoredPosition.y);
            EditorUtility.SetDirty(action);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Joystick MOVE text moved right without changing the joystick control.");
            EditorUtility.DisplayDialog("Joystick MOVE Text",
                "The MOVE caption was moved 18 px to the right.\n\n"
                + "The JOYSTICK title, artwork and input behaviour were not changed.\n\n"
                + "No Play Mode test was started.", "OK");
        }

        static Transform FindNamedInScene(Scene scene, string objectName)
        {
            Transform[] candidates = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].gameObject.scene == scene
                    && candidates[i].name == objectName)
                    return candidates[i];
            return null;
        }
    }
}
#endif
