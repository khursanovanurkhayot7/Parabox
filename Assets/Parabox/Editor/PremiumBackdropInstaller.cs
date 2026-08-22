#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Parabox.Editor
{
    /// <summary>
    /// One-click scene installer for the pre-generated premium backgrounds. The complete artwork
    /// is stored in the scene and no background objects or materials are generated in Play Mode.
    /// </summary>
    public static class PremiumBackdropInstaller
    {
        const string MainMenuPath = "Assets/Parabox/Scenes/MainMenu.unity";
        const string GamePath = "Assets/Parabox/Scenes/Game.unity";
        const string GeneratedRootName = "AnimatedGridBackdropFX";

        [MenuItem("Tools/Parabox/INSTALL Pre-Generated Backgrounds", priority = 21)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Stop Play Mode",
                    "Stop Play Mode first, then run this command again.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string returnScene = SceneManager.GetActiveScene().path;
            InstallIntoScene(MainMenuPath, true);
            InstallIntoScene(GamePath, false);

            if (!string.IsNullOrEmpty(returnScene))
                EditorSceneManager.OpenScene(returnScene, OpenSceneMode.Single);
            else
                EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Pre-Generated Background Ready",
                "Static pre-generated backgrounds are installed in MainMenu and Game.\n\n" +
                "Play Mode will not create or animate background layers.",
                "OK");
        }

        static void InstallIntoScene(string scenePath, bool mainMenu)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Camera cam = FindCamera(scene);
            if (cam == null)
                throw new System.InvalidOperationException("No camera found in " + scenePath);

            cam.gameObject.SetActive(true);
            cam.enabled = true;
            cam.backgroundColor = new Color(0.0025f, 0.006f, 0.028f, 1f);

            Transform backdropTransform = cam.transform.Find("Backdrop");
            if (backdropTransform == null)
            {
                var backdropObject = new GameObject("Backdrop");
                backdropTransform = backdropObject.transform;
                backdropTransform.SetParent(cam.transform, false);
                backdropTransform.localPosition = new Vector3(0f, 0f, 20f);
            }

            backdropTransform.gameObject.SetActive(true);
            CameraBackdrop cameraBackdrop = backdropTransform.GetComponent<CameraBackdrop>();
            if (cameraBackdrop == null)
                cameraBackdrop = backdropTransform.gameObject.AddComponent<CameraBackdrop>();
            cameraBackdrop.enabled = true;
            cameraBackdrop.cam = cam;

            FilmGridBackdrop animatedBackdrop = backdropTransform.GetComponent<FilmGridBackdrop>();
            if (animatedBackdrop != null)
            {
                animatedBackdrop.enabled = false;
                animatedBackdrop.cam = cam;
            }

            // Runtime-created children must never be saved as stale scene objects. A fresh set is
            // generated from the current camera size every time the player enters the scene.
            Transform generated = backdropTransform.Find(GeneratedRootName);
            if (generated != null)
                Object.DestroyImmediate(generated.gameObject);

            if (mainMenu)
            {
                SpriteRenderer menuPhoto = FindSprite(scene, "MenuPhoto");
                if (menuPhoto == null)
                    throw new System.InvalidOperationException("MenuPhoto was not found in MainMenu.");

                menuPhoto.enabled = true;
                menuPhoto.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
                menuPhoto.sortingOrder = -206;
                cameraBackdrop.bgPhoto = menuPhoto;

                // Serialize a correct 16:9 cover scale too, so the teal matte cannot be visible
                // even before CameraBackdrop performs its first per-frame camera fit.
                if (menuPhoto.sprite != null)
                {
                    Vector2 size = menuPhoto.sprite.bounds.size;
                    float height = cam.orthographicSize * 2f;
                    float width = height * (16f / 9f);
                    float cover = Mathf.Max(width / size.x, height / size.y) * 1.035f;
                    menuPhoto.transform.localScale = new Vector3(cover, cover, 1f);
                    menuPhoto.transform.localPosition = new Vector3(0f, 0f, 0.1f);
                }
            }

            EditorUtility.SetDirty(cam);
            EditorUtility.SetDirty(cameraBackdrop);
            if (animatedBackdrop != null) EditorUtility.SetDirty(animatedBackdrop);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static Camera FindCamera(Scene scene)
        {
            Camera fallback = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                {
                    if (fallback == null) fallback = candidate;
                    if (candidate.CompareTag("MainCamera") || candidate.name == "Main Camera")
                        return candidate;
                }
            }
            return fallback;
        }

        static SpriteRenderer FindSprite(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SpriteRenderer candidate in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (candidate.name == objectName)
                        return candidate;
                }
            }
            return null;
        }
    }
}
#endif
