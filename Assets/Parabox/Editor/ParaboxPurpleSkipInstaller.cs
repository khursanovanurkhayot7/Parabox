#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // One-click scene authoring only. The installer never enters Play Mode: it creates or repairs
    // the serialized purple tutorial action and gives it the Luxodd cabinet number 6 while
    // preserving its approved centred position below the tutorial card.
    public static class ParaboxPurpleSkipInstaller
    {
        const string GameScenePath = "Assets/Parabox/Scenes/Game.unity";
        static readonly Color PurpleTop = Hex("B76CFF");
        static readonly Color PurpleBottom = Hex("5A2297");

        [MenuItem("Tools/Parabox/Install Purple Skip Button 6", priority = 3)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the tutorial Skip button.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            GameManager game = FindInScene<GameManager>(scene);
            TutorialFx tutorial = game != null && game.tutorialFx != null
                ? game.tutorialFx : FindInScene<TutorialFx>(scene);
            if (game == null || tutorial == null)
                throw new InvalidOperationException(
                    "Game.unity has no wired tutorial. Run Tools/Parabox/Generate Prebuilt UI first.");

            Transform tutorialRoot = tutorial.choiceRT != null
                ? tutorial.choiceRT.parent : tutorial.transform;
            Button skip = FindOrCreateSkip(tutorial, tutorialRoot);
            Text countdown = ConfigureButton(skip, tutorialRoot);
            tutorial.skipButton = skip;
            tutorial.skipCountdownText = countdown;
            skip.gameObject.SetActive(false);

            EditorUtility.SetDirty(skip);
            EditorUtility.SetDirty(tutorial);
            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Purple tutorial Skip installed: cabinet button 6, centred position and 10-second auto-continue.");
            EditorUtility.DisplayDialog("Purple Skip Button 6",
                "Installed successfully.\n\n"
                + "The purple SKIP action now shows number 6 and keeps its centred position below the tutorial card.\n\n"
                + "The final tutorial choice now counts down from 10 and continues automatically.\n\n"
                + "No Play Mode test was started.", "OK");
        }

        static Button FindOrCreateSkip(TutorialFx tutorial, Transform tutorialRoot)
        {
            Button skip = tutorial.skipButton;
            if (skip == null && tutorialRoot != null)
            {
                Transform existing = tutorialRoot.Find("TutorialSkip");
                if (existing != null) skip = existing.GetComponent<Button>();
            }

            if (skip == null)
            {
                if (tutorial.tryButton == null || tutorialRoot == null)
                    throw new InvalidOperationException("The tutorial Try button is missing; Skip cannot be authored.");
                GameObject clone = UnityEngine.Object.Instantiate(
                    tutorial.tryButton.gameObject, tutorialRoot, false);
                clone.name = "TutorialSkip";
                Undo.RegisterCreatedObjectUndo(clone, "Create purple tutorial Skip");
                skip = clone.GetComponent<Button>();
                if (skip == null)
                    throw new InvalidOperationException("The cloned tutorial action has no Button component.");
                skip.onClick = new Button.ButtonClickedEvent();
            }
            return skip;
        }

        static Text ConfigureButton(Button skip, Transform tutorialRoot)
        {
            RectTransform rect = skip.transform as RectTransform;
            if (rect == null)
                throw new InvalidOperationException("TutorialSkip has no RectTransform.");

            rect.SetParent(tutorialRoot, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -410f);
            rect.sizeDelta = new Vector2(310f, 72f);
            rect.SetAsLastSibling();

            Text label = FindLabel(skip);
            foreach (RectTransform child in skip.GetComponentsInChildren<RectTransform>(true))
            {
                if (child == rect || child.name == "ButtonNumberBadge") continue;
                if (child.name == "Face" || child.name == "Lip")
                    child.sizeDelta = rect.sizeDelta;
                else if (child.name == "Highlight")
                    child.sizeDelta = rect.sizeDelta + new Vector2(130f, 130f);
                else if (child.name == "Gloss")
                    child.sizeDelta = rect.sizeDelta - new Vector2(6f, 6f);
            }

            Graphic face = skip.targetGraphic != null ? skip.targetGraphic : skip.image;
            if (face != null)
            {
                face.raycastTarget = true;
                face.color = Color.white;
                UIGradient gradient = face.GetComponent<UIGradient>();
                if (gradient == null) gradient = face.gameObject.AddComponent<UIGradient>();
                gradient.top = PurpleTop;
                gradient.bottom = PurpleBottom;
                EditorUtility.SetDirty(gradient);
            }

            ColorBlock colors = skip.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.fadeDuration = 0.08f;
            skip.colors = colors;
            skip.navigation = new Navigation { mode = Navigation.Mode.None };

            if (label == null)
                throw new InvalidOperationException("TutorialSkip has no text label.");
            label.text = "SKIP TUTORIAL";
            label.fontSize = 20;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(30f, 0f);
            label.rectTransform.sizeDelta = new Vector2(220f, 72f);

            Transform existingBadge = skip.transform.Find("ButtonNumberBadge");
            GameObject badgeObject;
            if (existingBadge == null)
            {
                badgeObject = new GameObject("ButtonNumberBadge", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Outline));
                badgeObject.layer = skip.gameObject.layer;
                badgeObject.transform.SetParent(skip.transform, false);
                Undo.RegisterCreatedObjectUndo(badgeObject, "Create tutorial Skip number badge");
            }
            else badgeObject = existingBadge.gameObject;

            RectTransform badgeRect = (RectTransform)badgeObject.transform;
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0.5f, 0.5f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(-116f, 0f);
            badgeRect.sizeDelta = new Vector2(48f, 48f);
            Image badge = badgeObject.GetComponent<Image>();
            badge.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Parabox/Sprites/Disc.png");
            badge.type = Image.Type.Simple;
            badge.color = PurpleBottom;
            badge.raycastTarget = false;
            Outline outline = badgeObject.GetComponent<Outline>();
            if (outline == null) outline = badgeObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.90f, 0.78f, 1f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);

            Text number = badgeObject.GetComponentInChildren<Text>(true);
            if (number == null)
            {
                var numberObject = new GameObject("ButtonNumber", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text));
                numberObject.layer = skip.gameObject.layer;
                numberObject.transform.SetParent(badgeObject.transform, false);
                Undo.RegisterCreatedObjectUndo(numberObject, "Create tutorial Skip number");
                number = numberObject.GetComponent<Text>();
            }
            number.name = "ButtonNumber";
            number.font = label.font != null
                ? label.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            number.text = "6";
            number.fontSize = 26;
            number.fontStyle = FontStyle.Bold;
            number.alignment = TextAnchor.MiddleCenter;
            number.color = Color.white;
            number.raycastTarget = false;
            number.rectTransform.anchorMin = Vector2.zero;
            number.rectTransform.anchorMax = Vector2.one;
            number.rectTransform.offsetMin = Vector2.zero;
            number.rectTransform.offsetMax = Vector2.zero;
            badgeRect.SetAsLastSibling();

            Transform existingCountdown = skip.transform.Find("SkipCountdown");
            Text countdown;
            if (existingCountdown == null)
            {
                var countdownObject = new GameObject("SkipCountdown", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text), typeof(Outline));
                countdownObject.layer = skip.gameObject.layer;
                countdownObject.transform.SetParent(skip.transform, false);
                Undo.RegisterCreatedObjectUndo(countdownObject, "Create tutorial auto-continue timer");
                existingCountdown = countdownObject.transform;
                countdown = countdownObject.GetComponent<Text>();
            }
            else countdown = existingCountdown.GetComponent<Text>();
            if (countdown == null)
                countdown = existingCountdown.gameObject.AddComponent<Text>();

            countdown.font = label.font != null
                ? label.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            countdown.text = "AUTO-CONTINUE IN 10";
            countdown.fontSize = 18;
            countdown.fontStyle = FontStyle.Bold;
            countdown.alignment = TextAnchor.MiddleCenter;
            countdown.color = new Color(0.82f, 0.68f, 1f, 1f);
            countdown.raycastTarget = false;
            countdown.rectTransform.anchorMin = countdown.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            countdown.rectTransform.pivot = new Vector2(0.5f, 0f);
            countdown.rectTransform.anchoredPosition = new Vector2(0f, 10f);
            countdown.rectTransform.sizeDelta = new Vector2(310f, 30f);
            Outline countdownOutline = countdown.GetComponent<Outline>();
            if (countdownOutline == null) countdownOutline = countdown.gameObject.AddComponent<Outline>();
            countdownOutline.effectColor = new Color(0.01f, 0.02f, 0.06f, 0.90f);
            countdownOutline.effectDistance = new Vector2(1.5f, -1.5f);
            countdown.gameObject.SetActive(false);

            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(label);
            EditorUtility.SetDirty(badge);
            EditorUtility.SetDirty(outline);
            EditorUtility.SetDirty(number);
            EditorUtility.SetDirty(countdown);
            EditorUtility.SetDirty(countdownOutline);
            return countdown;
        }

        static Text FindLabel(Button button)
        {
            Text fallback = null;
            Text[] labels = button.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].name == "ButtonNumber") continue;
                if (fallback == null) fallback = labels[i];
                if (labels[i].name == "Label" || labels[i].name == "Lbl") return labels[i];
            }
            return fallback;
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

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
#endif
