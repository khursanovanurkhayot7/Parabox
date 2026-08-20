using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // The origin film's visual language, rebuilt as live world-space graphics behind gameplay:
    // perspective floor lines, recursive circuit frames and sparse energy fragments. Everything is
    // camera-relative, so it remains full-screen through ordinary framing and recursive-room zooms.
    [DefaultExecutionOrder(990)]
    public sealed class FilmGridBackdrop : MonoBehaviour
    {
        const int HorizontalCount = 7;
        const int RayCount = 11;
        const int FrameCount = 4;
        const int SideLineCount = 12;
        const int FragmentCount = 58;

        public Camera cam;

        readonly List<LineRenderer> horizontals = new List<LineRenderer>();
        readonly List<LineRenderer> rays = new List<LineRenderer>();
        readonly List<LineRenderer> frames = new List<LineRenderer>();
        readonly List<LineRenderer> sideLines = new List<LineRenderer>();
        readonly List<Fragment> fragments = new List<Fragment>();
        Material lineMaterial;
        Texture2D lineTexture;
        Transform fxRoot;
        int chapter;
        float lastHeight = -1f;
        float lastAspect = -1f;

        sealed class Fragment
        {
            public LineRenderer line;
            public Vector2 normalizedPosition;
            public Vector2 direction;
            public float length;
            public float phase;
            public bool violet;
        }

        void Awake()
        {
            if (cam == null) cam = GetComponentInParent<Camera>();
            Build();
        }

        void OnEnable()
        {
            if (cam == null) cam = GetComponentInParent<Camera>();
            Build();
            FitToCamera(true);
        }

        void Build()
        {
            if (fxRoot != null) return;

            var root = new GameObject("OriginFilmBackdropFX");
            fxRoot = root.transform;
            fxRoot.SetParent(transform, false);
            fxRoot.localPosition = new Vector3(0f, 0f, -0.15f);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null)
            {
                // A small feathered texture removes the hard vector cut at both ends of every
                // trace. Bilinear filtering gives the same restrained softness visible in the MP4.
                lineTexture = new Texture2D(64, 1, TextureFormat.RGBA32, false, true)
                {
                    name = "Origin Film Soft Line (Runtime)",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                for (int x = 0; x < lineTexture.width; x++)
                {
                    float u = x / (lineTexture.width - 1f);
                    float edge = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 9f);
                    float alpha = edge * edge * (3f - 2f * edge);
                    lineTexture.SetPixel(x, 0, new Color(1f, 1f, 1f, alpha));
                }
                lineTexture.Apply(false, true);

                lineMaterial = new Material(shader)
                {
                    name = "Origin Film Backdrop Lines (Runtime)"
                };
                lineMaterial.mainTexture = lineTexture;
                lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            }

            for (int i = 0; i < HorizontalCount; i++)
                horizontals.Add(CreateLine("GridHorizontal_" + i, -204, false));
            for (int i = 0; i < RayCount; i++)
                rays.Add(CreateLine("GridRay_" + i, -204, false));
            for (int i = 0; i < FrameCount; i++)
                frames.Add(CreateLine("CircuitFrame_" + i, -203, true));
            for (int i = 0; i < SideLineCount; i++)
                sideLines.Add(CreateLine("FilmSideLine_" + i, -204, false));

            var random = new System.Random(147031);
            for (int i = 0; i < FragmentCount; i++)
            {
                Vector2 p = new Vector2(Lerp(-0.98f, 0.98f, random.NextDouble()),
                    Lerp(-0.96f, 0.96f, random.NextDouble()));

                float angle = Lerp(-40f, 40f, random.NextDouble());
                if (random.NextDouble() < 0.22) angle += 90f;
                fragments.Add(new Fragment
                {
                    line = CreateLine("EnergyFragment_" + i, -202, false),
                    normalizedPosition = p,
                    direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad),
                        Mathf.Sin(angle * Mathf.Deg2Rad)),
                    // The film mixes pinprick stars with short technical energy dashes.
                    // The film is mostly soft pinprick stars, with only an occasional short dash.
                    length = i % 5 == 0
                        ? Lerp(0.014f, 0.038f, random.NextDouble())
                        : Lerp(0.002f, 0.006f, random.NextDouble()),
                    phase = Lerp(0f, Mathf.PI * 2f, random.NextDouble()),
                    violet = (i % 3) == 0
                });
            }

            ApplyPalette();
        }

        LineRenderer CreateLine(string name, int sortingOrder, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fxRoot, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 4;
            line.numCornerVertices = loop ? 4 : 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.sortingOrder = sortingOrder;
            if (lineMaterial != null) line.sharedMaterial = lineMaterial;
            return line;
        }

        public void SetChapter(int value)
        {
            chapter = Mathf.Clamp(value, 0, 4);
            Build();
            ApplyPalette();
        }

        void ApplyPalette()
        {
            if (fxRoot == null) return;
            Color accent = ChapterAccent(chapter);
            Color cyan = Color.Lerp(accent, new Color(0.08f, 0.88f, 1f, 1f), 0.72f);
            Color violet = Color.Lerp(accent, new Color(0.52f, 0.31f, 1f, 1f), 0.68f);

            for (int i = 0; i < horizontals.Count; i++)
                SetColor(horizontals[i], i % 3 == 0 ? violet : cyan, 0.14f);
            for (int i = 0; i < rays.Count; i++)
                SetColor(rays[i], i % 4 == 0 ? violet : cyan, 0.12f);
            for (int i = 0; i < frames.Count; i++)
                SetColor(frames[i], i % 2 == 0 ? violet : cyan, 0.25f - i * 0.022f);
            for (int i = 0; i < sideLines.Count; i++)
                SetColor(sideLines[i], i % 2 == 0 ? cyan : violet, 0.16f);
            for (int i = 0; i < fragments.Count; i++)
                SetColor(fragments[i].line, fragments[i].violet ? violet : cyan, 0.42f);
        }

        static void SetColor(LineRenderer line, Color color, float alpha)
        {
            if (line == null) return;
            color.a = alpha;
            line.startColor = color;
            line.endColor = color;
        }

        void LateUpdate()
        {
            FitToCamera(false);
            Animate();
        }

        public void FitToCamera(bool force)
        {
            if (cam == null || fxRoot == null) return;
            float h = Mathf.Max(0.01f, cam.orthographicSize);
            float aspect = Mathf.Max(0.1f, cam.aspect);
            if (!force && Mathf.Abs(h - lastHeight) < 0.001f
                && Mathf.Abs(aspect - lastAspect) < 0.001f)
                return;
            lastHeight = h;
            lastAspect = aspect;
            float w = h * aspect;
            float thin = h * 0.0022f;

            // A converging floor grid occupies the lower third, matching the film while keeping the
            // actual puzzle cells free from high-contrast decoration.
            // Exact MP4 measurements put the horizon at roughly 64% down the 16:9 frame.
            Vector3 horizon = new Vector3(0f, -h * 0.27f, 0f);
            for (int i = 0; i < horizontals.Count; i++)
            {
                float t = (i + 1f) / horizontals.Count;
                float curve = t * t;
                float y = Mathf.Lerp(horizon.y, -h * 1.08f, curve);
                float reach = Mathf.Lerp(w * 0.18f, w * 1.28f, t);
                LineRenderer line = horizontals[i];
                line.positionCount = 2;
                line.SetPosition(0, new Vector3(-reach, y, 0f));
                line.SetPosition(1, new Vector3(reach, y, 0f));
                line.widthMultiplier = thin * 0.72f;
            }

            for (int i = 0; i < rays.Count; i++)
            {
                float t = rays.Count <= 1 ? 0.5f : i / (float)(rays.Count - 1);
                float x = Mathf.Lerp(-w * 1.34f, w * 1.34f, t);
                LineRenderer line = rays[i];
                line.positionCount = 2;
                line.SetPosition(0, horizon);
                line.SetPosition(1, new Vector3(x, -h * 1.08f, 0f));
                line.widthMultiplier = thin * 0.62f;
            }

            // Four beveled recursive frames echo the nested outlines in the origin film. Their
            // centre portions sit behind the board; the readable effect remains around its edges.
            for (int i = 0; i < frames.Count; i++)
            {
                float inset = i * 0.115f;
                float x = w * (0.96f - inset);
                float top = h * (0.88f - inset * 0.72f);
                float bottom = -h * (0.91f - inset * 0.60f);
                float bevel = Mathf.Min(w, h) * (0.075f + i * 0.008f);
                SetBeveledFrame(frames[i], x, top, bottom, bevel);
                frames[i].widthMultiplier = thin * (0.86f - i * 0.045f);
            }

            int sideRows = Mathf.Max(1, SideLineCount / 2);
            for (int i = 0; i < sideLines.Count; i++)
            {
                bool left = i < sideRows;
                int row = i % sideRows;
                float t = sideRows <= 1 ? 0.5f : row / (float)(sideRows - 1);
                float y = Mathf.Lerp(h * 0.88f, -h * 0.82f, t);
                float outerX = (left ? -1f : 1f) * w * 1.04f;
                float innerX = (left ? -1f : 1f) * w * 0.52f;
                LineRenderer line = sideLines[i];
                line.positionCount = 2;
                line.SetPosition(0, new Vector3(outerX, y + h * 0.16f, 0f));
                line.SetPosition(1, new Vector3(innerX, y - h * 0.16f, 0f));
                line.widthMultiplier = thin * 0.46f;
            }

            float small = Mathf.Min(w, h);
            for (int i = 0; i < fragments.Count; i++)
            {
                Fragment f = fragments[i];
                Vector3 centre = new Vector3(f.normalizedPosition.x * w,
                    f.normalizedPosition.y * h, 0f);
                Vector3 delta = (Vector3)(f.direction * (small * f.length));
                f.line.positionCount = 2;
                f.line.SetPosition(0, centre - delta * 0.5f);
                f.line.SetPosition(1, centre + delta * 0.5f);
                f.line.widthMultiplier = thin * (f.length < 0.01f ? 1.35f : 0.92f);
            }

        }

        static void SetBeveledFrame(LineRenderer line, float x, float top, float bottom, float bevel)
        {
            line.positionCount = 8;
            line.SetPosition(0, new Vector3(-x + bevel, top, 0f));
            line.SetPosition(1, new Vector3(x - bevel, top, 0f));
            line.SetPosition(2, new Vector3(x, top - bevel, 0f));
            line.SetPosition(3, new Vector3(x, bottom + bevel, 0f));
            line.SetPosition(4, new Vector3(x - bevel, bottom, 0f));
            line.SetPosition(5, new Vector3(-x + bevel, bottom, 0f));
            line.SetPosition(6, new Vector3(-x, bottom + bevel, 0f));
            line.SetPosition(7, new Vector3(-x, top - bevel, 0f));
        }

        void Animate()
        {
            if (fxRoot == null || lastHeight <= 0f) return;
            float time = Time.unscaledTime;
            float h = lastHeight;
            float w = h * Mathf.Max(0.1f, lastAspect);
            float thin = h * 0.0022f;
            Color accent = ChapterAccent(chapter);
            Color cyan = Color.Lerp(accent, new Color(0.08f, 0.88f, 1f, 1f), 0.72f);
            Color violet = Color.Lerp(accent, new Color(0.52f, 0.31f, 1f, 1f), 0.68f);

            // The MP4's field drifts almost imperceptibly while the board stays perfectly stable.
            fxRoot.localPosition = new Vector3(
                Mathf.Sin(time * 0.24f) * h * 0.006f,
                Mathf.Cos(time * 0.19f) * h * 0.0045f,
                -0.15f);

            // Flowing perspective grid: rows continually emerge at the horizon and accelerate
            // toward the player. This is the clearest piece of motion from the origin film.
            Vector3 horizon = new Vector3(0f, -h * 0.27f, 0f);
            for (int i = 0; i < horizontals.Count; i++)
            {
                float flow = Mathf.Repeat((i + 1f) / HorizontalCount + time * 0.088f, 1f);
                float curve = flow * flow;
                float y = Mathf.Lerp(horizon.y, -h * 1.08f, curve);
                float reach = Mathf.Lerp(w * 0.18f, w * 1.28f, flow);
                LineRenderer line = horizontals[i];
                line.SetPosition(0, new Vector3(-reach, y, 0f));
                line.SetPosition(1, new Vector3(reach, y, 0f));
                line.widthMultiplier = thin * Mathf.Lerp(0.48f, 0.92f, flow);
                float edgeFade = Mathf.Sin(flow * Mathf.PI);
                SetColor(line, i % 3 == 0 ? violet : cyan,
                    Mathf.Lerp(0.04f, 0.20f, flow) * edgeFade);
            }

            // Perspective rays gently fan left and right instead of remaining frozen.
            for (int i = 0; i < rays.Count; i++)
            {
                float u = rays.Count <= 1 ? 0.5f : i / (float)(rays.Count - 1);
                float sway = Mathf.Sin(time * 0.46f + i * 0.34f) * w * 0.036f;
                LineRenderer line = rays[i];
                line.SetPosition(0, horizon);
                line.SetPosition(1, new Vector3(Mathf.Lerp(-w * 1.34f, w * 1.34f, u) + sway,
                    -h * 1.08f, 0f));
                SetColor(line, i % 4 == 0 ? violet : cyan,
                    0.13f * (0.66f + 0.34f * Mathf.Sin(time * 0.62f + i * 0.37f)));
            }

            // Six long diagonal traces on each side are present throughout the film. They travel
            // slowly down the field, wrapping seamlessly from bottom back to top.
            int sideRows = Mathf.Max(1, SideLineCount / 2);
            for (int i = 0; i < sideLines.Count; i++)
            {
                bool left = i < sideRows;
                int row = i % sideRows;
                float travel = Mathf.Repeat(row / (float)sideRows + time * 0.036f, 1f);
                float y = Mathf.Lerp(h * 1.10f, -h * 1.10f, travel);
                float outerX = (left ? -1f : 1f) * w * 1.05f;
                float innerX = (left ? -1f : 1f) * w * 0.54f;
                LineRenderer line = sideLines[i];
                line.SetPosition(0, new Vector3(outerX, y + h * 0.16f, 0f));
                line.SetPosition(1, new Vector3(innerX, y - h * 0.16f, 0f));
                float fade = Mathf.Sin(travel * Mathf.PI);
                SetColor(line, i % 2 == 0 ? cyan : violet, fade * 0.19f);
            }

            for (int i = 0; i < frames.Count; i++)
            {
                float depthPulse = 1f + 0.012f * Mathf.Sin(time * 0.56f + i * 0.82f);
                frames[i].transform.localScale = new Vector3(depthPulse, depthPulse, 1f);
                frames[i].transform.localEulerAngles = new Vector3(0f, 0f,
                    Mathf.Sin(time * 0.31f + i) * 0.10f);
                SetColor(frames[i], i % 2 == 0 ? violet : cyan,
                    (0.27f - i * 0.022f) * (0.62f + 0.38f * Mathf.Sin(time * 0.62f + i)));
            }

            for (int i = 0; i < fragments.Count; i++)
            {
                Fragment f = fragments[i];
                float pulse = 0.55f + 0.45f * Mathf.Sin(time * 1.05f + f.phase);
                float travel = Mathf.Sin(time * (0.28f + (i % 5) * 0.018f) + f.phase);
                Vector2 sideDrift = f.direction * (h * 0.030f * travel);
                float verticalDrift = Mathf.Cos(time * 0.22f + f.phase) * h * 0.017f;
                f.line.transform.localPosition = new Vector3(sideDrift.x,
                    sideDrift.y + verticalDrift, 0f);
                SetColor(f.line, f.violet ? violet : cyan, Mathf.Lerp(0.11f, 0.52f, pulse));
            }

        }

        static float Lerp(float a, float b, double t) => a + (b - a) * (float)t;

        static Color ChapterAccent(int value)
        {
            switch (Mathf.Clamp(value, 0, 4))
            {
                case 0: return new Color(0.10f, 0.88f, 0.91f, 1f);
                case 1: return new Color(0.31f, 0.62f, 1f, 1f);
                case 2: return new Color(0.61f, 0.36f, 1f, 1f);
                case 3: return new Color(0.91f, 0.31f, 0.82f, 1f);
                default: return new Color(0.24f, 0.58f, 1f, 1f);
            }
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
            if (lineTexture != null) Destroy(lineTexture);
        }
    }
}
