using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Smooth top->bottom vertex-color gradient for a UI Graphic (Image/Text).
    // Gives buttons a premium two-tone fill instead of a flat single color.
    public class UIGradient : BaseMeshEffect
    {
        public Color top = Color.white;
        public Color bottom = Color.white;

        static readonly List<UIVertex> verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            vh.GetUIVertexStream(verts);

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                float y = verts[i].position.y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float h = Mathf.Max(0.0001f, maxY - minY);

            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                float t = (v.position.y - minY) / h;     // 0 = bottom, 1 = top
                v.color = v.color * Color.Lerp(bottom, top, t);
                verts[i] = v;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
            verts.Clear();
        }
    }

    // Shared visual language for the flow buttons shown on the map, tutorials, win screen and
    // loss screen. The original Button objects remain intact, so their mouse/controller focus and
    // callbacks are preserved while every face matches the approved dark sci-fi BACK control.
    public static class ArcadeActionButtonStyle
    {
        static readonly Color FaceTop = Hex("17344A");
        static readonly Color FaceBottom = Hex("06131F");
        static readonly Color Lip = Hex("020A11");
        static readonly Color Cyan = Hex("24C8ED");
        static readonly Color Label = Hex("F4FBFF");

        public static void Apply(Button button, string label, int fontSize = 0)
        {
            if (button == null) return;

            button.interactable = true;
            button.transition = Selectable.Transition.ColorTint;

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            Image face = FindImage(button.transform, "Face");
            if (face == null) face = button.targetGraphic as Image;
            if (face == null) face = button.GetComponent<Image>();
            if (face != null)
            {
                face.color = Color.white;
                face.raycastTarget = true;
                face.canvasRenderer.cullTransparentMesh = false;
                button.targetGraphic = face;

                UIGradient gradient = face.GetComponent<UIGradient>();
                if (gradient == null) gradient = face.gameObject.AddComponent<UIGradient>();
                gradient.top = FaceTop;
                gradient.bottom = FaceBottom;

                Outline outline = face.GetComponent<Outline>();
                if (outline == null) outline = face.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.72f);
                outline.effectDistance = new Vector2(1.35f, -1.35f);
                outline.useGraphicAlpha = true;
            }

            TintImage(button.transform, "Lip", Lip);
            TintImage(button.transform, "Gloss", new Color(0.62f, 0.91f, 1f, 0.10f));
            TintImage(button.transform, "Outline", new Color(Cyan.r, Cyan.g, Cyan.b, 0.94f));
            TintImage(button.transform, "OutlineGlow", new Color(Cyan.r, Cyan.g, Cyan.b, 0.30f));
            TintImage(button.transform, "Frame", new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f));
            TintImage(button.transform, "Highlight", new Color(Cyan.r, Cyan.g, Cyan.b, 0.34f));

            Text text = FindLabel(button);
            if (text != null)
            {
                text.text = label;
                text.color = Label;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.supportRichText = true;
                text.raycastTarget = false;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 14;
                text.resizeTextMaxSize = fontSize > 0 ? fontSize : Mathf.Max(20, text.fontSize);
                if (fontSize > 0) text.fontSize = fontSize;

                Outline textOutline = text.GetComponent<Outline>();
                if (textOutline == null) textOutline = text.gameObject.AddComponent<Outline>();
                textOutline.effectColor = new Color(0f, 0.03f, 0.07f, 0.92f);
                textOutline.effectDistance = new Vector2(1.25f, -1.25f);
            }

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.72f, 0.82f, 0.88f, 1f);
            colors.disabledColor = new Color(0.35f, 0.42f, 0.48f, 0.72f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;

            UIHoverScale hover = button.GetComponent<UIHoverScale>();
            if (hover == null) hover = button.gameObject.AddComponent<UIHoverScale>();
            hover.hover = 1.045f;
            hover.press = 0.965f;
        }

        // The level-map BACK control used to be an invisible hotspot over a BACK face painted
        // into the map artwork. Showing the newer shared button style on that old hotspot exposed
        // its plain white root Image and left the smaller legacy artwork visible underneath.
        // Keep one large, real button: the root is click geometry only, while Face/Lip own every
        // visible pixel. The wider face completely replaces (and covers) the painted legacy face.
        public static void ApplyLevelMapBack(Button button)
        {
            if (button == null) return;

            Apply(button, "BACK", 30);

            RectTransform root = button.transform as RectTransform;
            if (root != null)
            {
                root.sizeDelta = new Vector2(520f, 122f);
                root.anchoredPosition = new Vector2(root.anchoredPosition.x, -440f);
            }

            Image rootHitArea = button.GetComponent<Image>();
            if (rootHitArea != null)
            {
                rootHitArea.sprite = null;
                rootHitArea.color = Color.clear;
                rootHitArea.raycastTarget = false;
            }

            ResizePart(button.transform, "Lip", new Vector2(500f, 106f), new Vector2(0f, -7f));
            ResizePart(button.transform, "Face", new Vector2(500f, 106f), Vector2.zero);
            ResizePart(button.transform, "Gloss", new Vector2(486f, 92f), Vector2.zero);
            ResizePart(button.transform, "Highlight", new Vector2(520f, 122f), Vector2.zero);

            Image face = FindImage(button.transform, "Face");
            if (face != null)
            {
                face.raycastTarget = true;
                button.targetGraphic = face;
            }
        }

        static Text FindLabel(Button button)
        {
            Text fallback = null;
            foreach (Text text in button.GetComponentsInChildren<Text>(true))
            {
                if (fallback == null) fallback = text;
                if (text.name == "Label" || text.name == "Lbl") return text;
            }
            return fallback;
        }

        static Image FindImage(Transform parent, string name)
        {
            foreach (Image image in parent.GetComponentsInChildren<Image>(true))
                if (image.name == name) return image;
            return null;
        }

        static void TintImage(Transform parent, string name, Color color)
        {
            Image image = FindImage(parent, name);
            if (image == null) return;
            image.color = color;
            image.raycastTarget = false;
        }

        static void ResizePart(Transform parent, string name, Vector2 size, Vector2 position)
        {
            Transform part = parent.Find(name);
            if (part == null)
            {
                foreach (RectTransform candidate in parent.GetComponentsInChildren<RectTransform>(true))
                    if (candidate.name == name) { part = candidate; break; }
            }

            RectTransform rect = part as RectTransform;
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
