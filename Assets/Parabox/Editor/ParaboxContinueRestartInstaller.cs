#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // Run once from Tools/Parabox. This never enters Play Mode: it repairs the serialized Game
    // scene, creates the session-options status label when an older scene does not have one, and
    // guarantees that the Luxodd runtime prefab is available to show the official transactions.
    public static class ParaboxContinueRestartInstaller
    {
        const string GameScenePath = "Assets/Parabox/Scenes/Game.unity";
        const string SettingsFolder = "Assets/Parabox/Resources";
        const string SettingsPath = SettingsFolder + "/LuxoddParaboxSettings.asset";
        const string PluginPrefabPath = "Assets/Luxodd.Game/Prefabs/UnityPluginPrefab.prefab";

        [MenuItem("Tools/Parabox/Install Continue + Restart Flow", priority = 4)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing Continue / Restart.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            GameManager game = FindInScene<GameManager>(scene);
            if (game == null)
                throw new InvalidOperationException("Game.unity has no GameManager component.");

            LoseFx lose = game.loseFx != null ? game.loseFx : FindInScene<LoseFx>(scene);
            if (lose == null)
                throw new InvalidOperationException(
                    "Game.unity has no LoseFx object. Run Tools/Parabox/Generate Prebuilt UI first.");

            game.loseFx = lose;
            lose.transactionDelay = 5f;
            EnsureSessionOptionsLabel(lose);
            HideLegacyLocalActions(game, lose);
            EnsureLuxoddRuntimeSettings();

            EditorUtility.SetDirty(game);
            EditorUtility.SetDirty(lose);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Parabox Continue / Restart installed: GAME OVER opens Luxodd options after "
                + "5 seconds; Continue preserves the puzzle and Restart begins Level 1.");
            EditorUtility.DisplayDialog("Parabox Continue / Restart",
                "Installed successfully.\n\n"
                + "CONTINUE keeps the current puzzle state.\n"
                + "RESTART clears the run and starts Level 1.\n"
                + "END returns to the Luxodd arcade.\n\n"
                + "No Play Mode test was started.", "OK");
        }

        static void EnsureSessionOptionsLabel(LoseFx lose)
        {
            if (lose.subText == null)
            {
                Text[] labels = lose.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i].name != "LoseSub"
                        && labels[i].name != "GameOverSessionOptions") continue;
                    lose.subText = labels[i];
                    break;
                }
            }

            if (lose.subText == null)
            {
                Transform parent = lose.titleRT != null ? lose.titleRT : lose.transform;
                var item = new GameObject("GameOverSessionOptions", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text));
                RectTransform rect = (RectTransform)item.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -58f);
                rect.sizeDelta = new Vector2(720f, 56f);

                lose.subText = item.GetComponent<Text>();
                lose.subText.font = lose.titleText != null && lose.titleText.font != null
                    ? lose.titleText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                lose.subText.fontSize = 26;
                lose.subText.fontStyle = FontStyle.Bold;
                lose.subText.alignment = TextAnchor.MiddleCenter;
                lose.subText.color = new Color(0.78f, 0.91f, 0.96f, 1f);
                lose.subText.raycastTarget = false;
                lose.subText.supportRichText = false;
            }

            lose.subText.text = "CONTINUE / RESTART IN 5...";
            EditorUtility.SetDirty(lose.subText);
        }

        static void HideLegacyLocalActions(GameManager game, LoseFx lose)
        {
            HideButton(game.retryButton);
            HideButton(game.backToLevelsButton);

            if (lose.restartRT != null) lose.restartRT.gameObject.SetActive(false);
            if (lose.levelsRT != null) lose.levelsRT.gameObject.SetActive(false);
            DisableGroup(lose.restartGroup);
            DisableGroup(lose.levelsGroup);
        }

        static void HideButton(Button button)
        {
            if (button == null) return;
            button.interactable = false;
            button.gameObject.SetActive(false);
            EditorUtility.SetDirty(button);
        }

        static void DisableGroup(CanvasGroup group)
        {
            if (group == null) return;
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            EditorUtility.SetDirty(group);
        }

        static void EnsureLuxoddRuntimeSettings()
        {
            LuxoddParaboxSettings settings = AssetDatabase.LoadAssetAtPath<LuxoddParaboxSettings>(
                SettingsPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder(SettingsFolder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Parabox"))
                        throw new InvalidOperationException("Assets/Parabox is missing.");
                    AssetDatabase.CreateFolder("Assets/Parabox", "Resources");
                }
                settings = ScriptableObject.CreateInstance<LuxoddParaboxSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            GameObject pluginPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PluginPrefabPath);
            if (pluginPrefab == null)
                throw new InvalidOperationException("Luxodd runtime prefab is missing: " + PluginPrefabPath);
            settings.pluginPrefab = pluginPrefab;
            EditorUtility.SetDirty(settings);
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            T[] candidates = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].gameObject.scene == scene)
                    return candidates[i];
            return null;
        }
    }
}
#endif
