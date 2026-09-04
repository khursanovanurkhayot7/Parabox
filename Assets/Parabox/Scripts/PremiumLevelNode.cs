using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // Presentation only. The original Button, UIHoverScale, focus selector, completion badge,
    // navigation and hit rectangle remain the owners of input and selection.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image), typeof(Button))]
    public sealed class PremiumLevelNode : BaseMeshEffect,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public const float PushSeconds = .09f, HoldSeconds = .04f, ReleaseSeconds = .15f;
        Button button;
        Text number;
        Image oldBorder;
        PremiumMapPalette palette;
        bool configured, completed, inside, held, animating;
        float depth, focus, paintedDepth = -100f, paintedFocus = -100f;
        bool paintedInteractive;

        public static PremiumLevelNode Apply(Button control, Text caption, Image border, int chapter)
        {
            if (control == null || caption == null) return null;
            var finish = control.GetComponent<PremiumLevelNode>();
            if (finish == null) finish = control.gameObject.AddComponent<PremiumLevelNode>();
            finish.button = control;
            finish.number = caption;
            finish.oldBorder = border;
            finish.palette = PremiumMapPalette.ForChapter(chapter);
            // Only the retired flat border is suppressed. Never touch LevelFocusSelector,
            // its pulse/outline/shadow, the current-level ring, or UIHoverScale's settings.
            if (border != null && border != control.targetGraphic) border.enabled = false;
            Image face = control.GetComponent<Image>();
            face.sprite = null;
            face.type = Image.Type.Simple;
            face.material = null;
            face.color = Color.white;
            face.enabled = true;
            face.raycastTarget = true;
            face.canvasRenderer.cullTransparentMesh = false;
            caption.raycastTarget = false;
            foreach (BaseMeshEffect effect in caption.GetComponents<BaseMeshEffect>())
                effect.enabled = false;
            Shadow shadow = null;
            foreach (Shadow candidate in caption.GetComponents<Shadow>())
                if (!(candidate is Outline)) { shadow = candidate; break; }
            if (shadow == null) shadow = caption.gameObject.AddComponent<Shadow>();
            shadow.enabled = true;
            shadow.effectDistance = new Vector2(0f, -1.5f);
            shadow.effectColor = new Color(.015f, .035f, .075f, .65f);
            shadow.useGraphicAlpha = true;
            finish.configured = true;
            finish.enabled = true;
            finish.ResetPose();
            return finish;
        }

        bool Interactive => configured && button != null && button.IsActive() && button.IsInteractable();
        bool Selected => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

        public void SetCompleted(bool value)
        {
            completed = value;
            // MainMenuUI still calculates all states. Replace only its legacy flat surface tint.
            if (graphic != null) { graphic.color = Color.white; graphic.material = null; }
            if (oldBorder != null) oldBorder.enabled = false;
            paintedDepth = -100f;
            Paint();
        }

        protected override void OnEnable() { base.OnEnable(); if (configured) ResetPose(); }
        protected override void OnDisable() { if (configured) ResetPose(); base.OnDisable(); }
        void OnApplicationFocus(bool active) { if (!active) { inside = held = false; } }
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
            float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (!Interactive) inside = held = false;
            focus = Mathf.MoveTowards(focus, Interactive && (inside || Selected) ? 1f : 0f, dt / .12f);
            if (!animating) depth = Mathf.MoveTowards(depth, held ? 1f : 0f, dt / .07f);
            Paint();
        }
        void Paint()
        {
            if (!configured || graphic == null) return;
            if (Mathf.Abs(paintedDepth - depth) < .0001f && Mathf.Abs(paintedFocus - focus) < .0001f
                && paintedInteractive == Interactive) return;
            paintedDepth = depth; paintedFocus = focus; paintedInteractive = Interactive;
            number.rectTransform.anchoredPosition = new Vector2(0f, 5f - depth * 6f);
            number.color = Interactive ? Color.white : new Color(.58f, .66f, .76f, 1f);
            graphic.SetVerticesDirty();
        }

        public IEnumerator AnimateActivation()
        {
            if (!Interactive || animating) yield break;
            animating = true; held = false;
            float from = depth;
            for (float t = 0f; t < PushSeconds;)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f);
                depth = Mathf.Lerp(from, 1f, Mathf.SmoothStep(0f, 1f, t / PushSeconds));
                Paint(); yield return null;
            }
            for (float t = 0f; t < HoldSeconds;)
            { t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f); yield return null; }
            for (float t = 0f; t < ReleaseSeconds;)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 1f / 60f);
                float q = Mathf.Clamp01(t / ReleaseSeconds) - 1f;
                depth = -(2.15f * q * q * q + 1.15f * q * q);
                Paint(); yield return null;
            }
            depth = 0f; animating = false; Paint();
        }

        public void OnPointerEnter(PointerEventData e) { inside = true; }
        public void OnPointerExit(PointerEventData e) { inside = held = false; }
        public void OnPointerDown(PointerEventData e)
        { if (Interactive && !animating && e.button == PointerEventData.InputButton.Left) held = true; }
        public void OnPointerUp(PointerEventData e)
        { if (e.button == PointerEventData.InputButton.Left) held = false; }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || !configured || graphic == null) return;
            mesh.Clear();
            Rect bounds = graphic.rectTransform.rect;
            Vector2 c = bounds.center;
            float sx = bounds.width / 86f, sy = bounds.height / 86f;
            float y = 5f - depth * 6f;
            PremiumMapPalette p = palette;
            if (!Interactive) p = p.Dimmed();
            Color light = Color.Lerp(p.light, p.glow, focus * .28f);
            Color mid = Color.Lerp(p.mid, p.light, focus * .12f);
            // A fixed dark well and a thick sidewall. Only the raised face and live numeral move.
            Layer(mesh, c, sx, sy, 0f, -1f, 43f, 42f, 10f, C(2, 8, 18), C(1, 5, 12));
            Layer(mesh, c, sx, sy, 0f, -2f, 41.8f, 40f, 9f, p.deep, p.shadow);
            Layer(mesh, c, sx, sy, 0f, -3f, 40f, 37f, 8f, p.deep, p.mid);
            Layer(mesh, c, sx, sy, 0f, -1f, 40f, 37f, 8f, p.deep, p.shadow);
            Layer(mesh, c, sx, sy, 0f, y, 42f, 35.8f, 9f, p.glow, p.deep);
            Layer(mesh, c, sx, sy, 0f, y + .2f, 40.7f, 34.5f, 8f,
                Color.Lerp(p.glow, Color.white, .45f), p.mid);
            Layer(mesh, c, sx, sy, 0f, y - .7f, 39f, 32.8f, 7f, light, p.shadow);
            Layer(mesh, c, sx, sy, -.8f, y + 1f, 36.8f, 29.8f, 5.5f, light, p.deep);
            Layer(mesh, c, sx, sy, -.6f, y + .3f, 34.8f, 28f, 5f, mid, p.deep);
            Layer(mesh, c, sx, sy, -1f, y + 22f, 30f, 3.3f, 3f, C(255, 255, 255, 45), C(255, 255, 255, 4));
            Layer(mesh, c, sx, sy, 0f, y - 23f, 8f, 2.8f, 2.8f, p.shadow, p.mid);
            Color lamp = completed ? C(144, 255, 211) : C(3, 19, 32);
            Layer(mesh, c, sx, sy, 0f, y - 23f, 6.6f, 1.55f, 1.5f,
                completed ? C(223, 255, 241) : lamp, lamp);
            // Existing level-complete/unlock choreography temporarily tints the old Image.
            // Keep its dim-to-lit transition, without multiplying the chapter hue twice.
            Color source = graphic.color;
            float brightness = Mathf.Lerp(.22f, 1f, Mathf.Clamp01(source.maxColorComponent));
            Color tint = new Color(brightness, brightness, brightness, source.a);
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                var vertex = new UIVertex();
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.color = (Color)vertex.color * tint;
                mesh.SetUIVertex(vertex, i);
            }
        }
        static Color C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        static void Layer(VertexHelper mesh, Vector2 c, float sx, float sy, float x, float y,
            float hx, float hy, float radius, Color top, Color bottom)
        {
            PremiumMapMesh.Plate(mesh, new Rect(c.x + (x - hx) * sx, c.y + (y - hy) * sy,
                hx * 2f * sx, hy * 2f * sy), radius * Mathf.Min(sx, sy), top, bottom);
        }
    }

    public struct PremiumMapPalette
    {
        public Color glow, light, mid, deep, shadow;
        public static PremiumMapPalette ForChapter(int chapter)
        {
            switch (Mathf.Clamp(chapter, 0, 4))
            {
                case 0: return Make(0x82f9fa, 0x39d7e2, 0x169aad, 0x0b637c, 0x063448);
                case 1: return Make(0xa6dbff, 0x74bbff, 0x337dd1, 0x214782, 0x101f4c);
                case 2: return Make(0xdec2ff, 0xbd91fa, 0x8850c7, 0x4e277e, 0x291647);
                case 3: return Make(0xf9b9ef, 0xe780d9, 0xab43a5, 0x682765, 0x351238);
                default: return Make(0xffbcce, 0xfb7d9f, 0xc34c72, 0x7a2b4b, 0x3e142a);
            }
        }
        static PremiumMapPalette Make(int glow, int light, int mid, int deep, int shadow) =>
            new PremiumMapPalette { glow = Hex(glow), light = Hex(light), mid = Hex(mid), deep = Hex(deep), shadow = Hex(shadow) };
        static Color Hex(int hex) => new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
        public PremiumMapPalette Dimmed() => new PremiumMapPalette {
            glow = Color.Lerp(glow, Hex(0x384556), .83f), light = Hex(0x283c50),
            mid = Hex(0x17293c), deep = Hex(0x0b1728), shadow = Hex(0x030b15) };
    }

    // Small antialiased native UI meshes, using Unity's default UI material (including stencil
    // masks). No new shaders, raster backgrounds, textures, or per-frame material instances.
    internal static class PremiumMapMesh
    {
        const int Steps = 6, Count = 4 * (Steps + 1);
        public static void Plate(VertexHelper mesh, Rect r, float radius, Color top, Color bottom)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(r.width, r.height) * .5f);
            int first = mesh.currentVertCount;
            Add(mesh, r.center, Color.Lerp(bottom, top, .5f));
            for (int i = 0; i < Count; i++)
            {
                Vector2 v = Boundary(r, radius, i);
                Add(mesh, v, Shade(r, v, top, bottom));
            }
            for (int i = 0; i < Count; i++)
                mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % Count);
            // Half-unit fringe smooths corners at the map's authored 1920x1080 scale.
            Rect outer = new Rect(r.xMin - .45f, r.yMin - .45f, r.width + .9f, r.height + .9f);
            int edge = mesh.currentVertCount;
            for (int i = 0; i < Count; i++)
            {
                Vector2 v = Boundary(outer, radius + .45f, i);
                Color color = Shade(r, v, top, bottom); color.a = 0f;
                Add(mesh, v, color);
            }
            for (int i = 0; i < Count; i++)
            {
                int next = (i + 1) % Count;
                mesh.AddTriangle(first + 1 + i, edge + i, edge + next);
                mesh.AddTriangle(first + 1 + i, edge + next, first + 1 + next);
            }
        }
        static Color Shade(Rect r, Vector2 v, Color top, Color bottom) =>
            Color.Lerp(bottom, top, Mathf.Clamp01((v.y - r.yMin) / r.height));
        static Vector2 Boundary(Rect r, float radius, int index)
        {
            int corner = index / (Steps + 1), step = index % (Steps + 1);
            float x = corner == 0 || corner == 3 ? r.xMax - radius : r.xMin + radius;
            float y = corner < 2 ? r.yMax - radius : r.yMin + radius;
            float a = (corner * 90f + step * 90f / Steps) * Mathf.Deg2Rad;
            return new Vector2(x + Mathf.Cos(a) * radius, y + Mathf.Sin(a) * radius);
        }
        static void Add(VertexHelper mesh, Vector2 v, Color color) => mesh.AddVert(v, color, Vector2.zero);
        public static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            if (Mathf.Abs(a.x - b.x) < .01f)
                Plate(mesh, new Rect(a.x - width * .5f, Mathf.Min(a.y,b.y), width, Mathf.Abs(a.y-b.y)), width * .5f, color, color);
            else
                Plate(mesh, new Rect(Mathf.Min(a.x,b.x), a.y - width * .5f, Mathf.Abs(a.x-b.x), width), width * .5f, color, color);
        }
    }
}
