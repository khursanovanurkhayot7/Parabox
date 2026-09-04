using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // Presentation only: the existing Button, onClick listeners and menu transition guard own
    // activation. Texture artwork is never used as an invisible hotspot or a baked text label.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button), typeof(Image))]
    public sealed class PremiumMenuButton : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public const float Width = 344f, Height = 130f, CentreY = -355f, CentreOffset = 200f;
        public const float PushSeconds = 0.10f, HoldSeconds = 0.055f, ReleaseSeconds = 0.18f;
        static Sprite playArt, levelsArt;
        Button button;
        Image face;
        RectTransform rect;
        Text label;
        Outline focusEdge;
        Shadow labelDepth;
        Vector2 restPosition;
        Color accent;
        bool inside, held, animating, configured;
        float depth, focus;

        static Sprite LoadArt(bool isPlay)
        {
            if (isPlay && playArt != null) return playArt;
            if (!isPlay && levelsArt != null) return levelsArt;
            // Each texture has one tightly-framed sprite rectangle. Keeping its original PNG
            // and alpha avoids destructive trimming, while Unity imports just the button bounds.
            Sprite[] sprites = Resources.LoadAll<Sprite>(isPlay
                ? "UI/PremiumMenuPlay" : "UI/PremiumMenuLevels");
            Sprite sprite = sprites.Length > 0 ? sprites[0] : null;
            if (isPlay) playArt = sprite; else levelsArt = sprite;
            return sprite;
        }

        public static bool TryApply(Button control, Text text, string caption, bool isPlay)
        {
            if (control == null || text == null) return false;
            Sprite sprite = LoadArt(isPlay);
            if (sprite == null) return false;

            var fx = control.GetComponent<PremiumMenuButton>();
            if (fx == null) fx = control.gameObject.AddComponent<PremiumMenuButton>();
            fx.button = control;
            fx.rect = (RectTransform)control.transform;
            fx.face = control.GetComponent<Image>();
            fx.label = text;
            fx.accent = isPlay ? new Color(0.12f, 0.95f, 1f) : new Color(0.69f, 0.39f, 1f);

            UIHoverScale oldHover = control.GetComponent<UIHoverScale>();
            if (oldHover != null)
            {
                if (oldHover.highlight != null) oldHover.highlight.SetActive(false);
                oldHover.highlight = null;
                oldHover.focusOutline = null;
                oldHover.suspended = true;
                oldHover.enabled = false;
            }
            UIGradient oldGradient = control.GetComponent<UIGradient>();
            if (oldGradient != null) oldGradient.enabled = false;
            foreach (Shadow shadow in control.GetComponents<Shadow>()) shadow.enabled = false;
            // Retire old gloss, outlines and icons without deleting authored objects. Keep only
            // the live label; the approved sprite already includes its bevel, rim and sidewall.
            foreach (Graphic graphic in control.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = graphic == fx.face;
                if (graphic != fx.face && graphic != text) graphic.enabled = false;
            }

            // Below the central puzzle artwork, with a comfortable 120-unit bottom margin.
            // Centre anchors preserve this spacing on the reference frame at every aspect ratio.
            fx.rect.anchorMin = fx.rect.anchorMax = new Vector2(0.5f, 0.5f);
            fx.rect.pivot = new Vector2(0.5f, 0.5f);
            fx.rect.anchoredPosition = new Vector2(isPlay ? -CentreOffset : CentreOffset, CentreY);
            fx.rect.sizeDelta = new Vector2(Width, Height);
            fx.restPosition = fx.rect.anchoredPosition;
            fx.rect.localScale = Vector3.one;

            fx.face.sprite = sprite;
            fx.face.type = Image.Type.Simple;
            fx.face.preserveAspect = false;
            fx.face.color = Color.white;
            fx.face.enabled = true;
            fx.face.raycastTarget = true;
            fx.face.canvasRenderer.cullTransparentMesh = false;
            control.targetGraphic = fx.face;
            control.transition = Selectable.Transition.None;
            control.interactable = true;
            control.gameObject.SetActive(true);
            CanvasGroup group = control.GetComponent<CanvasGroup>();
            if (group == null) group = control.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = group.blocksRaycasts = true;

            StyleLabel(text, caption, isPlay);
            fx.labelDepth = PlainShadow(text.gameObject);
            fx.focusEdge = control.GetComponent<Outline>();
            if (fx.focusEdge == null) fx.focusEdge = control.gameObject.AddComponent<Outline>();
            fx.focusEdge.enabled = true;
            fx.focusEdge.effectDistance = new Vector2(1.2f, -1.2f);
            fx.focusEdge.useGraphicAlpha = true;
            fx.configured = true;
            fx.enabled = true;
            fx.ResetPose();
            return true;
        }

        static Shadow PlainShadow(GameObject root)
        {
            foreach (Shadow shadow in root.GetComponents<Shadow>())
                if (shadow.GetType() == typeof(Shadow)) return shadow;
            return root.AddComponent<Shadow>();
        }

        static void StyleLabel(Text text, string caption, bool isPlay)
        {
            text.gameObject.SetActive(true);
            text.enabled = true;
            text.text = caption;
            text.fontSize = isPlay ? 48 : 36;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = Color.white;
            text.raycastTarget = false;
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 9f);
            rt.sizeDelta = new Vector2(Width - 48f, 86f);
            rt.localScale = Vector3.one;
            text.transform.SetAsLastSibling();

            // Cream-white type with a fine dark keyline and a small extruded lower edge. It is
            // drawn at the Canvas resolution, so glyphs remain crisp instead of scaling a bitmap.
            var gradient = text.GetComponent<UIGradient>();
            if (gradient == null) gradient = text.gameObject.AddComponent<UIGradient>();
            gradient.enabled = true;
            gradient.top = Color.white;
            gradient.bottom = new Color(1f, 0.94f, 0.80f);
            var outline = text.GetComponent<Outline>();
            if (outline == null) outline = text.gameObject.AddComponent<Outline>();
            outline.enabled = true;
            outline.effectColor = isPlay ? new Color(0.01f, 0.17f, 0.075f, 0.9f)
                : new Color(0.24f, 0.12f, 0.015f, 0.9f);
            outline.effectDistance = new Vector2(0.75f, -0.75f);
            outline.useGraphicAlpha = true;
            Shadow depth = PlainShadow(text.gameObject);
            depth.enabled = true;
            depth.effectColor = isPlay ? new Color(0.005f, 0.08f, 0.035f, 0.95f)
                : new Color(0.16f, 0.065f, 0.005f, 0.95f);
            depth.effectDistance = new Vector2(0f, -3f);
            depth.useGraphicAlpha = true;
            text.SetVerticesDirty();
        }

        bool Interactive => configured && button != null && button.IsActive() && button.IsInteractable();
        bool Selected => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        void OnEnable() { if (configured) ResetPose(); }
        void OnDisable() { if (configured) ResetPose(); }
        void OnApplicationFocus(bool hasFocus) { if (!hasFocus) held = inside = false; }

        void ResetPose()
        {
            held = inside = animating = false;
            depth = 0f;
            focus = Selected ? 1f : 0f;
            Paint();
        }

        void Update()
        {
            if (!configured) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (!Interactive) held = inside = false;
            focus = Mathf.MoveTowards(focus, Interactive && (inside || Selected) ? 1f : 0f, dt / 0.12f);
            if (!animating) depth = Mathf.MoveTowards(depth, held ? 1f : 0f, dt / 0.085f);
            Paint();
        }

        void Paint()
        {
            if (rect == null || face == null) return;
            // Small nonuniform compression preserves the sculpted proportions; no giant shrink
            // or white flash. Moving the live text with the face makes the push feel mechanical.
            rect.anchoredPosition = restPosition + Vector2.down * (depth * 5f);
            rect.localScale = new Vector3(1f - depth * 0.018f, 1f - depth * 0.065f, 1f);
            face.color = !button.IsInteractable() ? new Color(0.52f, 0.56f, 0.60f, 0.7f)
                : Color.Lerp(Color.white, new Color(0.86f, 0.9f, 0.94f), Mathf.Clamp01(depth));
            if (focusEdge != null)
                focusEdge.effectColor = new Color(accent.r, accent.g, accent.b, focus * 0.68f);
            if (labelDepth != null)
                labelDepth.effectDistance = new Vector2(0f, -Mathf.Lerp(3f, 1f, Mathf.Clamp01(depth)));
        }

        // Reused for pointer click, Enter and direct arcade Button.onClick invocation. The menu
        // waits for this routine before changing screens, and its existing guard rejects repeats.
        public IEnumerator AnimateActivation()
        {
            if (!Interactive || animating) yield break;
            animating = true;
            held = false;
            float from = depth;
            for (float t = 0f; t < PushSeconds;)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f);
                depth = Mathf.Lerp(from, 1f, Mathf.SmoothStep(0f, 1f, t / PushSeconds));
                Paint();
                yield return null;
            }
            for (float t = 0f; t < HoldSeconds;)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f);
                yield return null;
            }
            for (float t = 0f; t < ReleaseSeconds;)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f);
                float k = Mathf.Clamp01(t / ReleaseSeconds);
                // Ease-out back gives a tiny spring return, finishing at the exact resting pose.
                float q = k - 1f;
                float eased = 1f + 2.15f * q * q * q + 1.15f * q * q;
                depth = 1f - eased;
                Paint();
                yield return null;
            }
            depth = 0f;
            animating = false;
            Paint();
        }

        public void OnPointerEnter(PointerEventData e) { inside = true; if (Interactive) Sfx.Hover(); }
        public void OnPointerExit(PointerEventData e) { inside = held = false; }
        public void OnPointerDown(PointerEventData e)
        {
            if (Interactive && !animating && e.button == PointerEventData.InputButton.Left) held = true;
        }
        public void OnPointerUp(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) held = false; }
        public void OnSelect(BaseEventData e) { if (Interactive) Sfx.Hover(); }
        public void OnDeselect(BaseEventData e) { held = false; }
    }
}
