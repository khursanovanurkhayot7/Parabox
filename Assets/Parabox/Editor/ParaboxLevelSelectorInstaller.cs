#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // One-click scene authoring only. It creates a selector inside every level node so the focus
    // cannot disappear with the retired map glow layer. MainMenuUI still owns selection/input.
    public static class ParaboxLevelSelectorInstaller
    {
        const string MainMenuScenePath = "Assets/Parabox/Scenes/MainMenu.unity";
        const string RingSpritePath = "Assets/Parabox/Sprites/CellRing.png";

        [MenuItem("Tools/Parabox/Install Level Selection Selector", priority = 2)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing level selectors.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            MainMenuUI menu = FindInScene<MainMenuUI>(scene);
            if (menu == null || menu.levelButtons == null || menu.levelButtons.Length == 0)
                throw new InvalidOperationException(
                    "MainMenu.unity has no level buttons. Run Tools/Parabox/Generate Prebuilt UI first.");

            Sprite ringSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RingSpritePath);
            if (ringSprite == null)
                throw new InvalidOperationException("The selector ring sprite is missing from Assets/Parabox/Sprites.");

            int installed = 0;
            for (int i = 0; i < menu.levelButtons.Length; i++)
            {
                Button button = menu.levelButtons[i];
                if (button == null) continue;
                EnsureSelector(button, i, ringSprite);
                installed++;
            }

            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Parabox level selector installed on " + installed
                + " nodes: default focus, bright ring, pulse and scale feedback.");
            EditorUtility.DisplayDialog("Level Selection Selector",
                "Installed on " + installed + " level buttons.\n\n"
                + "The current joystick selection now has a bright chapter-colour ring, "
                + "pulse and scale animation.\n"
                + "The first available level is selected automatically when Level Selection opens.\n\n"
                + "No Play Mode test was started.", "OK");
        }

        static void EnsureSelector(Button button, int levelIndex, Sprite ringSprite)
        {
            Transform existing = button.transform.Find("LevelFocusSelector");
            GameObject selectorObject;
            if (existing == null)
            {
                selectorObject = new GameObject("LevelFocusSelector", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Shadow),
                    typeof(UIPulse));
                selectorObject.layer = button.gameObject.layer;
                selectorObject.transform.SetParent(button.transform, false);
                Undo.RegisterCreatedObjectUndo(selectorObject, "Create visible level selector");
            }
            else selectorObject = existing.gameObject;

            RectTransform selector = (RectTransform)selectorObject.transform;
            selector.anchorMin = selector.anchorMax = new Vector2(0.5f, 0.5f);
            selector.pivot = new Vector2(0.5f, 0.5f);
            selector.anchoredPosition = Vector2.zero;
            selector.sizeDelta = new Vector2(116f, 116f);
            selector.localScale = Vector3.one;
            selector.localRotation = Quaternion.identity;

            Color accent = FocusAccent(levelIndex);
            Image ring = selectorObject.GetComponent<Image>();
            ring.sprite = ringSprite;
            ring.type = Image.Type.Sliced;
            ring.color = Color.Lerp(accent, Color.white, 0.72f);
            ring.raycastTarget = false;

            Outline outline = selectorObject.GetComponent<Outline>();
            if (outline == null) outline = selectorObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.95f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            outline.useGraphicAlpha = true;

            Shadow shadow = null;
            foreach (Shadow candidate in selectorObject.GetComponents<Shadow>())
                if (candidate.GetType() == typeof(Shadow)) { shadow = candidate; break; }
            if (shadow == null) shadow = selectorObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(accent.r, accent.g, accent.b, 0.80f);
            shadow.effectDistance = new Vector2(0f, -4f);
            shadow.useGraphicAlpha = true;

            UIPulse pulse = selectorObject.GetComponent<UIPulse>();
            if (pulse == null) pulse = selectorObject.AddComponent<UIPulse>();
            pulse.amplitude = 0.06f;
            pulse.speed = 3.5f;

            Transform retiredPointer = selector.Find("SelectorPointer");
            if (retiredPointer != null)
                Undo.DestroyObjectImmediate(retiredPointer.gameObject);

            UIHoverScale hover = button.GetComponent<UIHoverScale>();
            if (hover == null) hover = button.gameObject.AddComponent<UIHoverScale>();
            if (hover.highlight != null && hover.highlight != selectorObject)
                hover.highlight.SetActive(false);
            hover.highlight = selectorObject;
            hover.hover = 1.16f;
            hover.press = 0.90f;

            selector.SetAsLastSibling();
            selectorObject.SetActive(false);
            EditorUtility.SetDirty(button);
            EditorUtility.SetDirty(hover);
            EditorUtility.SetDirty(ring);
            EditorUtility.SetDirty(outline);
            EditorUtility.SetDirty(shadow);
            EditorUtility.SetDirty(pulse);
        }

        static Color FocusAccent(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex / MainMenuUI.PerCategory, 0, 4))
            {
                case 0: return new Color(0.10f, 0.88f, 0.91f, 1f);
                case 1: return new Color(0.31f, 0.62f, 1.00f, 1f);
                case 2: return new Color(0.61f, 0.36f, 1.00f, 1f);
                case 3: return new Color(0.91f, 0.31f, 0.82f, 1f);
                default: return new Color(1.00f, 0.38f, 0.56f, 1f);
            }
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            T[] candidates = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].gameObject.scene == scene)
                    return candidates[i];
            return null;
        }
    }
}
#endif
