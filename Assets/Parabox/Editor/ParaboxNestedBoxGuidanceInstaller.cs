#if UNITY_EDITOR
using System;
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
                + "• A compact premium red lamp pulses on each closed wall.\n"
                + "• No arrows, X icons, ENTER/EXIT text or overlays are created.\n"
                + "• Levels, move limits, scoring and difficulty were not changed.\n\n"
                + "No tutorial was added and Play Mode was not started.";
            Debug.Log("[Parabox] " + result, guidance);
            EditorUtility.DisplayDialog("Nested-Box Guidance", result, "OK");
        }
    }
}
#endif
