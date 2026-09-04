#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // Creates the approved Progress Rings score presentation (legacy Option 2 menu entry).
    // It deliberately does not replace,
    // move or rewire any existing gameplay HUD, timer, board, buttons or scoring behaviour.
    public static class ParaboxPremiumScoreHudInstaller
    {
        const string GameScenePath = "Assets/Parabox/Scenes/Game.unity";
        const string HudName = "PremiumScoreHudOption2";
        const string DiscPath = "Assets/Parabox/Sprites/Disc.png";
        const string RingPath = "Assets/Parabox/Sprites/RingThin.png";
        const string GlowPath = "Assets/Parabox/Sprites/Glow.png";

        static readonly Color NavyTop = Hex("12324A");
        static readonly Color NavyBottom = Hex("030C18");
        static readonly Color ChipTop = Hex("102A40");
        static readonly Color ChipBottom = Hex("040D19");
        static readonly Color Cyan = Hex("3DE9FF");
        static readonly Color Blue = Hex("4A8DFF");
        static readonly Color Violet = Hex("9B55FF");
        static readonly Color White = Hex("F3FBFF");
        static readonly Color Muted = Hex("A9C3D4");
        static readonly Color Coral = Hex("FF6C62");

        [MenuItem("Tools/Parabox/Install Premium Score HUD Option 2", priority = 4)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the premium score HUD.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            GameManager game = FindInScene<GameManager>(scene);
            if (game == null)
                throw new InvalidOperationException(
                    "Game.unity has no GameManager. Run Tools/Parabox/Generate Prebuilt UI first.");

            Transform parent = game.hudGroup != null ? game.hudGroup.transform
                : game.scoreRoot != null ? game.scoreRoot.parent
                : game.timerRoot != null ? game.timerRoot.parent : null;
            if (parent == null)
                throw new InvalidOperationException("The gameplay HUD parent could not be found.");

            Transform old = parent.Find(HudName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            Sprite disc = RequireSprite(DiscPath);
            Sprite ring = RequireSprite(RingPath);
            Sprite glow = RequireSprite(GlowPath);
            Font font = game.movesLabel != null && game.movesLabel.font != null
                ? game.movesLabel.font
                : game.levelLabel != null && game.levelLabel.font != null
                    ? game.levelLabel.font
                    : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            RectTransform root = CreateRoot(parent);
            CreateScoreCore(root, font, disc, ring, glow);
            CreateInfoChip(root, "TimeChip", new Vector2(342f, -67f), font,
                Cyan, "TIME", "0 / 10s");
            CreateInfoChip(root, "MovesChip", new Vector2(342f, -153f), font,
                Violet, "MOVES", "0 / 13");
            root.SetAsLastSibling();

            game.premiumScoreValue = RequireText(root, "ScoreValue");
            game.premiumTimeValue = RequireText(root, "TimeChip/Main");
            game.premiumTimeDetail = RequireText(root, "TimeChip/Detail");
            game.premiumMovesValue = RequireText(root, "MovesChip/Main");
            game.premiumMovesDetail = RequireText(root, "MovesChip/Detail");
            PremiumScoreRings.Apply(root, game.premiumScoreValue, game.premiumTimeValue,
                game.premiumTimeDetail, game.premiumMovesValue, game.premiumMovesDetail);
            EditorUtility.SetDirty(game);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Premium Progress Rings score HUD installed in the gameplay upper-left corner.");
            EditorUtility.DisplayDialog("Premium Score HUD — Progress Rings",
                "Installed successfully in Game.unity.\n\n"
                + "Only the new upper-left score presentation was created.\n"
                + "Existing gameplay, board, timer, buttons and score logic were not changed.\n\n"
                + "No Play Mode test was started.", "OK");
        }

        static RectTransform CreateRoot(Transform parent)
        {
            GameObject go = new GameObject(HudName, typeof(RectTransform), typeof(CanvasGroup));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create premium score HUD Option 2");

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(34f, -36f);
            rect.sizeDelta = new Vector2(505f, 215f);
            rect.localScale = Vector3.one;

            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            return rect;
        }

        static void CreateScoreCore(RectTransform root, Font font, Sprite disc, Sprite ring,
            Sprite glow)
        {
            Image halo = CreateImage("ScoreHalo", root, new Vector2(102f, -106f),
                new Vector2(214f, 214f), glow, new Color(Cyan.r, Cyan.g, Cyan.b, 0.24f), true);
            UIPulse pulse = halo.gameObject.AddComponent<UIPulse>();
            pulse.amplitude = 0.035f;
            pulse.speed = 2.2f;

            CreateRoundedScoreFill(root, new Vector2(102f, -106f), disc);

            Image outerRing = CreateImage("OuterRing", root, new Vector2(102f, -106f),
                new Vector2(204f, 204f), ring, new Color(Cyan.r, Cyan.g, Cyan.b, 0.96f), true);
            AddOutline(outerRing.gameObject, new Color(Blue.r, Blue.g, Blue.b, 0.78f),
                new Vector2(2f, -2f));

            Image innerRing = CreateImage("InnerRing", root, new Vector2(102f, -106f),
                new Vector2(126f, 126f), ring, new Color(Violet.r, Violet.g, Violet.b, 0.72f), true);
            innerRing.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Spinner diamondSpin = innerRing.gameObject.AddComponent<Spinner>();
            diamondSpin.degPerSec = 18f;

            Text title = CreateText("ScoreTitle", root, font, "SCORE", 20, FontStyle.Bold,
                White, new Vector2(102f, -73f), new Vector2(130f, 30f), true);
            CreateText("ScoreValue", root, font, "100", 52, FontStyle.Bold, White,
                new Vector2(102f, -119f), new Vector2(150f, 64f), true);

        }

        static void CreateInfoChip(RectTransform root, string name, Vector2 position, Font font,
            Color accent, string main, string detail)
        {
            GameObject chipObject = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            chipObject.layer = root.gameObject.layer;
            chipObject.transform.SetParent(root, false);
            RectTransform chip = (RectTransform)chipObject.transform;
            chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
            chip.pivot = new Vector2(0.5f, 0.5f);
            chip.anchoredPosition = position;
            // Keep a visible gutter between these chips and the gameplay board. The old 274px
            // cards reached into the cyan board frame at narrower arcade/browser aspects.
            chip.sizeDelta = new Vector2(232f, 72f);

            Image face = chipObject.GetComponent<Image>();
            face.sprite = null;
            face.type = Image.Type.Simple;
            face.color = Color.clear;
            face.raycastTarget = false;
            Shadow shadow = chipObject.GetComponent<Shadow>();
            shadow.effectColor = Color.clear;
            shadow.effectDistance = Vector2.zero;
            shadow.useGraphicAlpha = true;

            // The border is now a flat rectangle, so its fill must also be rectangular. Keeping a
            // capsule here left black wedges in all four corners despite the full-width centre.
            Image fill = CreateImage("Fill", chip, Vector2.zero,
                new Vector2(224f, 64f), null, Color.white);
            AddChipGradient(fill, accent);

            CreateFlatBorder(chip, accent);

            Text mainText = CreateText("Main", chip, font, main, 20, FontStyle.Bold, White,
                new Vector2(-2f, 13f), new Vector2(190f, 30f));
            mainText.alignment = TextAnchor.MiddleLeft;

            Text detailText = CreateText("Detail", chip, font, detail, 15, FontStyle.Bold,
                name == "TimeChip" ? Coral : Muted,
                new Vector2(-2f, -17f), new Vector2(190f, 25f));
            detailText.alignment = TextAnchor.MiddleLeft;
        }

        static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size,
            Sprite sprite, Color color, bool fromTopLeft = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = fromTopLeft
                ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = sprite != null;
            return image;
        }

        static Text CreateText(string name, Transform parent, Font font, string value, int size,
            FontStyle style, Color color, Vector2 position, Vector2 dimensions,
            bool fromTopLeft = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = fromTopLeft
                ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            CrispUiTypography.Polish(text);
            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0.005f, 0.015f, 0.035f, 0.90f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        static void AddOutline(GameObject target, Color color, Vector2 distance)
        {
            Outline outline = target.GetComponent<Outline>();
            if (outline == null) outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        static void AddChipGradient(Image image, Color accent)
        {
            UIGradient gradient = image.gameObject.AddComponent<UIGradient>();
            gradient.top = Color.Lerp(ChipTop, accent, 0.09f);
            gradient.bottom = ChipBottom;
        }

        static void CreateRoundedScoreFill(RectTransform root, Vector2 centre, Sprite disc)
        {
            // Union two rectangles and four circular corners into a rounded square. This reaches
            // underneath the cyan frame instead of leaving the old circular black corner gaps.
            Color fill = Color.Lerp(NavyTop, NavyBottom, 0.46f);
            const float cornerOffset = 72f;
            CreateImage("ScoreFillHorizontal", root, centre, new Vector2(200f, 144f),
                null, fill, true);
            CreateImage("ScoreFillVertical", root, centre, new Vector2(144f, 200f),
                null, fill, true);
            CreateImage("ScoreFillTopLeft", root,
                centre + new Vector2(-cornerOffset, cornerOffset), new Vector2(56f, 56f),
                disc, fill, true);
            CreateImage("ScoreFillTopRight", root,
                centre + new Vector2(cornerOffset, cornerOffset), new Vector2(56f, 56f),
                disc, fill, true);
            CreateImage("ScoreFillBottomLeft", root,
                centre + new Vector2(-cornerOffset, -cornerOffset), new Vector2(56f, 56f),
                disc, fill, true);
            CreateImage("ScoreFillBottomRight", root,
                centre + new Vector2(cornerOffset, -cornerOffset), new Vector2(56f, 56f),
                disc, fill, true);
        }

        static void CreateFlatBorder(RectTransform chip, Color accent)
        {
            Color line = new Color(accent.r, accent.g, accent.b, 0.88f);
            const float thickness = 4f;
            float halfWidth = chip.sizeDelta.x * 0.5f;
            float halfHeight = chip.sizeDelta.y * 0.5f;

            CreateImage("BorderTop", chip, new Vector2(0f, halfHeight - thickness * 0.5f),
                new Vector2(chip.sizeDelta.x, thickness), null, line);
            CreateImage("BorderBottom", chip, new Vector2(0f, -halfHeight + thickness * 0.5f),
                new Vector2(chip.sizeDelta.x, thickness), null, line);
            CreateImage("BorderLeft", chip, new Vector2(-halfWidth + thickness * 0.5f, 0f),
                new Vector2(thickness, chip.sizeDelta.y), null, line);
            CreateImage("BorderRight", chip, new Vector2(halfWidth - thickness * 0.5f, 0f),
                new Vector2(thickness, chip.sizeDelta.y), null, line);
        }

        static void AddShadow(GameObject target, Color color, Vector2 distance)
        {
            Shadow shadow = null;
            Shadow[] shadows = target.GetComponents<Shadow>();
            for (int i = 0; i < shadows.Length; i++)
                if (shadows[i].GetType() == typeof(Shadow)) { shadow = shadows[i]; break; }
            if (shadow == null) shadow = target.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        static Sprite RequireSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Missing score HUD sprite: " + path);
            return sprite;
        }

        static Text RequireText(Transform root, string path)
        {
            Transform item = root.Find(path);
            Text text = item != null ? item.GetComponent<Text>() : null;
            if (text == null)
                throw new InvalidOperationException("Premium score HUD text is missing: " + path);
            return text;
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
