using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // Native sculpted button surface. The existing Button, navigation, hit area and callback
    // stay intact; only its face and label move down into the fixed cabinet-style base.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image), typeof(Button))]
    public sealed class PremiumBackButton : BaseMeshEffect,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public const float PushSeconds = 0.09f, HoldSeconds = 0.04f, ReleaseSeconds = 0.16f;
        Button button;
        Text label;
        bool configured, inside, held, animating;
        float depth, focus;
        float paintedDepth = -100f, paintedFocus = -100f;
        bool paintedInteractive;

        public static void Apply(Button control, Text caption)
        {
            if (control == null || caption == null) return;
            Image face = control.GetComponent<Image>();
            if (face == null) face = control.gameObject.AddComponent<Image>();
            var finish = control.GetComponent<PremiumBackButton>();
            if (finish == null) finish = control.gameObject.AddComponent<PremiumBackButton>();
            finish.button = control;
            finish.label = caption;

            // Preserve authored objects and listeners, but retire the old flat face/gloss.
            foreach (Graphic old in control.GetComponentsInChildren<Graphic>(true))
            {
                old.raycastTarget = old == face;
                if (old != face && old != caption) old.enabled = false;
            }
            foreach (BaseMeshEffect effect in control.GetComponents<BaseMeshEffect>())
                if (effect != finish) effect.enabled = false;
            UIHoverScale oldHover = control.GetComponent<UIHoverScale>();
            if (oldHover != null)
            {
                if (oldHover.highlight != null) oldHover.highlight.SetActive(false);
                oldHover.highlight = null;
                oldHover.focusOutline = null;
                oldHover.suspended = true;
                oldHover.enabled = false;
            }
            control.transform.localScale = Vector3.one;
            face.sprite = null;
            face.type = Image.Type.Simple;
            face.color = Color.white;
            face.enabled = true;
            face.raycastTarget = true;
            face.canvasRenderer.cullTransparentMesh = false;
            control.targetGraphic = face;
            control.transition = Selectable.Transition.None;

            // Live Fredoka glyphs, not text painted into artwork. One tight shadow adds depth.
            caption.enabled = true;
            caption.gameObject.SetActive(true);
            caption.text = "BACK";
            caption.fontSize = 34;
            caption.fontStyle = FontStyle.Normal;
            caption.alignment = TextAnchor.MiddleCenter;
            caption.resizeTextForBestFit = false;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            caption.verticalOverflow = VerticalWrapMode.Truncate;
            caption.color = C(244, 253, 255);
            caption.raycastTarget = false;
            caption.transform.SetParent(control.transform, false);
            caption.transform.SetAsLastSibling();
            RectTransform textRect = caption.rectTransform;
            textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(172f, 60f);
            textRect.localScale = Vector3.one;
            foreach (BaseMeshEffect effect in caption.GetComponents<BaseMeshEffect>()) effect.enabled = false;
            Shadow shadow = null;
            foreach (Shadow candidate in caption.GetComponents<Shadow>())
                if (!(candidate is Outline)) { shadow = candidate; break; }
            if (shadow == null) shadow = caption.gameObject.AddComponent<Shadow>();
            shadow.enabled = true;
            shadow.effectColor = new Color(0.01f, 0.06f, 0.11f, 0.52f);
            shadow.effectDistance = new Vector2(0f, -1f);
            shadow.useGraphicAlpha = true;
            CrispUiTypography.Polish(caption);

            finish.configured = true;
            finish.enabled = true;
            finish.ResetPose();
        }

        bool Interactive => configured && button != null && button.IsActive() && button.IsInteractable();
        bool Selected => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        protected override void OnEnable() { base.OnEnable(); if (configured) ResetPose(); }
        protected override void OnDisable() { if (configured) ResetPose(); base.OnDisable(); }
        void OnApplicationFocus(bool active) { if (!active) held = inside = false; }

        void ResetPose()
        {
            inside = held = animating = false;
            depth = 0f;
            focus = Selected ? 1f : 0f;
            paintedDepth = -100f;
            Paint();
        }

        void Update()
        {
            if (!configured) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (!Interactive) inside = held = false;
            focus = Mathf.MoveTowards(focus, Interactive && (inside || Selected) ? 1f : 0f, dt / 0.12f);
            if (!animating) depth = Mathf.MoveTowards(depth, held ? 1f : 0f, dt / 0.08f);
            Paint();
        }

        void Paint()
        {
            if (!configured || graphic == null) return;
            bool interactive = Interactive;
            if (Mathf.Abs(paintedDepth - depth) < 0.0001f && Mathf.Abs(paintedFocus - focus) < 0.0001f
                && paintedInteractive == interactive) return;
            paintedDepth = depth;
            paintedFocus = focus;
            paintedInteractive = interactive;
            if (label != null)
            {
                label.rectTransform.anchoredPosition = new Vector2(14f, 6f - depth * 6f);
                label.color = interactive ? C(244, 253, 255) : C(133, 155, 172);
            }
            graphic.SetVerticesDirty();
        }

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
            { t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f); yield return null; }
            for (float t = 0f; t < ReleaseSeconds;)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f);
                float q = Mathf.Clamp01(t / ReleaseSeconds) - 1f;
                depth = -(2.15f * q * q * q + 1.15f * q * q);
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
        { if (Interactive && !animating && e.button == PointerEventData.InputButton.Left) held = true; }
        public void OnPointerUp(PointerEventData e)
        { if (e.button == PointerEventData.InputButton.Left) held = false; }
        public void OnSelect(BaseEventData e) { if (Interactive) Sfx.Hover(); }
        public void OnDeselect(BaseEventData e) { held = false; }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || graphic == null) return;
            mesh.Clear();
            Rect rect = graphic.rectTransform.rect;
            if (rect.width < 100f || rect.height < 60f) return;
            Vector2 c = rect.center;
            float w = rect.width * 0.5f, h = rect.height * 0.5f;
            float y = 6f - depth * 6f;
            // A fixed 18-unit sidewall gives the illuminated top a physical raised profile.
            Plate(mesh, c + new Vector2(0f, -6f), w - 9f, h - 7f, 22f, C(10, 48, 72), C(3, 12, 27));
            Plate(mesh, c + new Vector2(0f, -3f), w - 11f, h - 9f, 21f, C(30, 99, 131), C(9, 36, 64));
            Stripe(mesh, c, -w + 37f, w - 37f, -h + 6f, 1.6f, C(36, 118, 171, 0), C(47, 151, 213));
            Plate(mesh, c + new Vector2(0f, y), w - 10f, h - 13f, 21f,
                Color.Lerp(C(117, 244, 255), Color.white, focus * .32f), C(35, 148, 214));
            Plate(mesh, c + new Vector2(0f, y), w - 12f, h - 15f, 19f, C(44, 129, 167), C(10, 54, 101));
            Plate(mesh, c + new Vector2(0f, y + 1f), w - 17f, h - 21f, 15f, C(95, 197, 221), C(23, 88, 128));
            Plate(mesh, c + new Vector2(0f, y), w - 19f, h - 23f, 14f,
                Color.Lerp(C(38, 105, 143), C(54, 129, 170), focus), C(10, 38, 68));
            Stripe(mesh, c, -w + 42f, w - 42f, y + h - 16f, 1.5f,
                C(232, 255, 255, 60), C(241, 255, 255));
            Stripe(mesh, c, -w + 46f, w - 46f, y + 17f, 15f,
                C(184, 239, 255, 0), C(177, 239, 255, 23));
            Stripe(mesh, c, -w + 49f, w - 49f, y - h + 24f, 1f,
                C(83, 195, 227, 0), C(97, 209, 243, 155));
            // Recessed side details echo the map's circuit lighting; no extra badge or pill.
            foreach (int side in new[] { -1, 1 })
            {
                Vector2 p = c + new Vector2(side * (w - 14f), y);
                Plate(mesh, p, 2f, 11f, 1.5f, C(192, 253, 255), C(26, 168, 218));
            }
            Vector2 arrow = c + new Vector2(-74f, y);
            Chevron(mesh, arrow + new Vector2(0f, -1.5f), C(3, 24, 45));
            Chevron(mesh, arrow, C(171, 246, 255));
        }

        static Color C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        void Vertex(VertexHelper mesh, Vector2 p, Color colour)
        {
            if (configured && !Interactive) colour = colour * new Color(.55f, .61f, .67f, 1f);
            mesh.AddVert(new Vector3(p.x, p.y, 0f), colour * graphic.color, Vector2.zero);
        }

        void Plate(VertexHelper mesh, Vector2 centre, float hx, float hy, float radius, Color top, Color bottom)
        {
            radius = Mathf.Min(radius, Mathf.Min(hx, hy));
            int first = mesh.currentVertCount;
            Vertex(mesh, centre, Color.Lerp(bottom, top, .5f));
            const int steps = 8;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner == 0 || corner == 3 ? hx - radius : -hx + radius;
                float y = corner < 2 ? hy - radius : -hy + radius;
                for (int step = 0; step <= steps; step++)
                {
                    float angle = (corner * 90f + step * 90f / steps) * Mathf.Deg2Rad;
                    Vector2 p = new Vector2(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius);
                    Vertex(mesh, centre + p, Color.Lerp(bottom, top, Mathf.InverseLerp(-hy, hy, p.y)));
                }
            }
            const int count = 4 * (steps + 1);
            for (int i = 0; i < count; i++) mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
        }

        void Stripe(VertexHelper mesh, Vector2 c, float left, float right, float y, float height, Color edge, Color middle)
        {
            int first = mesh.currentVertCount;
            for (int i = 0; i < 3; i++)
            {
                float x = Mathf.Lerp(left, right, i * .5f);
                Color colour = i == 1 ? middle : edge;
                Vertex(mesh, c + new Vector2(x, y), colour);
                Vertex(mesh, c + new Vector2(x, y + height), colour);
            }
            for (int i = 0; i < 2; i++)
            {
                int v = first + i * 2;
                mesh.AddTriangle(v, v + 1, v + 2); mesh.AddTriangle(v + 1, v + 3, v + 2);
            }
        }

        void Chevron(VertexHelper mesh, Vector2 centre, Color colour)
        {
            int first = mesh.currentVertCount;
            Vector2[] points = { new Vector2(4f, 11f), new Vector2(-7f, 0f), new Vector2(4f, -11f),
                new Vector2(7f, -8f), new Vector2(-1f, 0f), new Vector2(7f, 8f) };
            foreach (Vector2 p in points) Vertex(mesh, centre + p, colour);
            mesh.AddTriangle(first, first + 1, first + 4); mesh.AddTriangle(first, first + 4, first + 5);
            mesh.AddTriangle(first + 1, first + 2, first + 3); mesh.AddTriangle(first + 1, first + 3, first + 4);
        }
    }
}
