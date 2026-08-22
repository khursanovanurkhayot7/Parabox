using System;
using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Live premium world-space graphics behind gameplay: perspective floor lines, recursive circuit
    // frames and sparse energy fragments. Everything is camera-relative, so it remains full-screen
    // through ordinary framing and recursive-room zooms.
    [DefaultExecutionOrder(990)]
    public sealed class FilmGridBackdrop : MonoBehaviour
    {
        // MainMenuApproved.png and GameBGApproved.png are the complete authored backgrounds.
        // Keep this legacy component only so older scenes deserialize safely; never generate
        // background GameObjects, textures or materials during Play Mode.
        static readonly bool AllowRuntimeGeneration = false;

        const int HorizontalCount = 9;
        const int RayCount = 13;
        const int FrameCount = 6;
        const int SideLineCount = 12;
        const int FragmentCount = 68;

        public Camera cam;

        readonly List<LineRenderer> horizontals = new List<LineRenderer>();
        readonly List<LineRenderer> rays = new List<LineRenderer>();
        readonly List<LineRenderer> frameGlows = new List<LineRenderer>();
        readonly List<LineRenderer> frames = new List<LineRenderer>();
        readonly List<LineRenderer> frameSweeps = new List<LineRenderer>();
        readonly List<LineRenderer> sideLines = new List<LineRenderer>();
        readonly List<Fragment> fragments = new List<Fragment>();
        Material lineMaterial;
        Texture2D lineTexture;
        Transform fxRoot;
        int chapter;
        bool menuOverlay;
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
            if (!AllowRuntimeGeneration || UsePregeneratedArtwork())
            {
                enabled = false;
                return;
            }
            if (cam == null) cam = GetComponentInParent<Camera>();
            Build();
        }

        void OnEnable()
        {
            if (!AllowRuntimeGeneration || UsePregeneratedArtwork())
            {
                enabled = false;
                return;
            }
            if (cam == null) cam = GetComponentInParent<Camera>();
            Build();
            FitToCamera(true);
        }

        void Build()
        {
            if (!AllowRuntimeGeneration || UsePregeneratedArtwork()) return;
            if (fxRoot != null) return;
            menuOverlay = gameObject.scene.name == "MainMenu";

            var root = new GameObject("AnimatedGridBackdropFX");
            fxRoot = root.transform;
            fxRoot.SetParent(transform, false);
            fxRoot.localPosition = new Vector3(0f, 0f, -0.15f);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader != null)
            {
                // A small feathered texture removes the hard vector cut at both ends of every
                // trace. Bilinear filtering keeps the neon linework soft and premium.
                lineTexture = new Texture2D(64, 1, TextureFormat.RGBA32, false, true)
                {
                    name = "Animated Backdrop Soft Line (Runtime)",
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
                    name = "Animated Backdrop Lines (Runtime)"
                };
                lineMaterial.mainTexture = lineTexture;
                lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            }

            for (int i = 0; i < HorizontalCount; i++)
                horizontals.Add(CreateLine("GridHorizontal_" + i, -204, false));
            for (int i = 0; i < RayCount; i++)
                rays.Add(CreateLine("GridRay_" + i, -204, false));
            for (int i = 0; i < FrameCount; i++)
            {
                frameGlows.Add(CreateLine("CircuitFrameGlow_" + i, -205, true));
                frames.Add(CreateLine("CircuitFrame_" + i, -203, true));
                frameSweeps.Add(CreateLine("CircuitFrameLightSweep_" + i, -201, false));
            }
            for (int i = 0; i < SideLineCount; i++)
                sideLines.Add(CreateLine("BackdropSideLine_" + i, -204, false));

            var random = new System.Random(147031);
            for (int i = 0; i < FragmentCount; i++)
            {
                Vector2 p = new Vector2(Lerp(-0.98f, 0.98f, random.NextDouble()),
                    Lerp(-0.96f, 0.96f, random.NextDouble()));
                // The menu artwork contains its title, diorama and buttons in the centre. Keep
                // overlay particles in the outer chamber so they animate the environment without
                // sparkling across readable content.
                if (menuOverlay && Mathf.Abs(p.x) < 0.88f && p.y > -0.80f && p.y < 0.84f)
                {
                    float side = p.x < 0f ? -1f : 1f;
                    p.x = side * Lerp(0.89f, 0.97f, random.NextDouble());
                }

                float angle = Lerp(-40f, 40f, random.NextDouble());
                if (random.NextDouble() < 0.22) angle += 90f;
                fragments.Add(new Fragment
                {
                    line = CreateLine("EnergyFragment_" + i, -202, false),
                    normalizedPosition = p,
                    direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad),
                        Mathf.Sin(angle * Mathf.Deg2Rad)),
                    // Mostly soft pinprick stars, with an occasional short technical energy dash.
                    length = i % 5 == 0
                        ? Lerp(0.014f, 0.038f, random.NextDouble())
                        : Lerp(0.002f, 0.006f, random.NextDouble()),
                    phase = Lerp(0f, Mathf.PI * 2f, random.NextDouble()),
                    violet = (i % 3) == 0
                });
            }

            // The menu's old artwork is now a luminance-cut foreground plate. Keep the complete
            // perspective chamber behind it, but promote only edge-safe moving details so the
            // animation is unmistakable without drawing across the logo, puzzle or buttons.
            if (menuOverlay)
            {
                // The perspective floor is excellent behind a gameplay board, but its long
                // horizontal strokes intersect the menu's baked PLAY and LEVEL SELECT artwork.
                // The menu uses only its animated outer chamber, sweeps and energy fragments.
                for (int i = 0; i < horizontals.Count; i++)
                    horizontals[i].enabled = false;
                for (int i = 0; i < rays.Count; i++)
                    rays[i].enabled = false;
                for (int i = 0; i < frameSweeps.Count; i++)
                    frameSweeps[i].sortingOrder = -201;
                for (int i = 0; i < sideLines.Count; i++)
                    sideLines[i].sortingOrder = -203;
                for (int i = 0; i < fragments.Count; i++)
                    fragments[i].line.sortingOrder = -202;
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
            // On the menu the authored logo/buttons plate is a foreground cutout. Put the full
            // live chamber behind it; dark pixels in that plate are blended away by CameraBackdrop.
            line.sortingOrder = sortingOrder + (menuOverlay ? -12 : 0);
            if (lineMaterial != null) line.sharedMaterial = lineMaterial;
            return line;
        }

        public void SetChapter(int value)
        {
            chapter = Mathf.Clamp(value, 0, 4);
            if (!AllowRuntimeGeneration || UsePregeneratedArtwork()) return;
            Build();
            ApplyPalette();
        }

        bool UsePregeneratedArtwork()
        {
            CameraBackdrop backdrop = GetComponent<CameraBackdrop>();
            return backdrop != null && backdrop.bgPhoto != null && backdrop.bgPhoto.sprite != null;
        }

        void ApplyPalette()
        {
            if (fxRoot == null) return;
            Color accent = ChapterAccent(chapter);
            Color cyan = Color.Lerp(accent, new Color(0.08f, 0.88f, 1f, 1f), 0.72f);
            Color violet = Color.Lerp(accent, new Color(0.52f, 0.31f, 1f, 1f), 0.68f);

            for (int i = 0; i < horizontals.Count; i++)
                SetColor(horizontals[i], i % 3 == 0 ? violet : cyan, menuOverlay ? 0.18f : 0.22f);
            for (int i = 0; i < rays.Count; i++)
                SetColor(rays[i], i % 4 == 0 ? violet : cyan, menuOverlay ? 0.15f : 0.18f);
            for (int i = 0; i < frames.Count; i++)
            {
                Color frameColor = i % 2 == 0 ? violet : cyan;
                SetColor(frameGlows[i], frameColor, menuOverlay ? 0.075f : 0.09f);
                SetColor(frames[i], frameColor,
                    menuOverlay ? 0.34f - i * 0.021f : 0.40f - i * 0.025f);
                SetColor(frameSweeps[i], Color.Lerp(frameColor, Color.white, 0.44f),
                    menuOverlay ? 0.72f : 0.84f);
            }
            for (int i = 0; i < sideLines.Count; i++)
                SetColor(sideLines[i], i % 2 == 0 ? cyan : violet,
                    menuOverlay ? 0.38f : 0.23f);
            for (int i = 0; i < fragments.Count; i++)
                SetColor(fragments[i].line, fragments[i].violet ? violet : cyan,
                    menuOverlay ? 0.50f : 0.58f);
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
            float thin = h * 0.00265f;

            // A converging floor grid occupies the lower third while the opaque puzzle board keeps
            // its own cells free from high-contrast decoration.
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
                line.widthMultiplier = thin * 0.84f;
            }

            for (int i = 0; i < rays.Count; i++)
            {
                float t = rays.Count <= 1 ? 0.5f : i / (float)(rays.Count - 1);
                float x = Mathf.Lerp(-w * 1.34f, w * 1.34f, t);
                LineRenderer line = rays[i];
                line.positionCount = 2;
                line.SetPosition(0, horizon);
                line.SetPosition(1, new Vector3(x, -h * 1.08f, 0f));
                line.widthMultiplier = thin * 0.72f;
            }

            // Six concentric beveled frames use the same geometry language as the puzzle board.
            // Their broad low-alpha copies provide a restrained halo without requiring post FX.
            for (int i = 0; i < frames.Count; i++)
            {
                // Main-menu frames hug the outer bezel. Gameplay frames can recurse toward the
                // board, but doing that on the menu would cut through its logo and two buttons.
                float inset = i * (menuOverlay ? 0.014f : 0.086f);
                float x = w * ((menuOverlay ? 0.985f : 0.96f) - inset);
                float top = h * ((menuOverlay ? 0.965f : 0.88f)
                    - inset * (menuOverlay ? 0.34f : 0.72f));
                float bottom = -h * ((menuOverlay ? 0.970f : 0.91f)
                    - inset * (menuOverlay ? 0.30f : 0.54f));
                float bevel = Mathf.Min(w, h) * (0.070f + i * 0.006f);
                SetBeveledFrame(frameGlows[i], x, top, bottom, bevel);
                SetBeveledFrame(frames[i], x, top, bottom, bevel);
                float coreWidth = thin * (1.18f - i * 0.055f);
                frameGlows[i].widthMultiplier = coreWidth * 4.8f;
                frames[i].widthMultiplier = coreWidth;
                frameSweeps[i].widthMultiplier = coreWidth * 2.25f;
            }

            // Short right-angle circuit details replace the former long diagonals. They match the
            // board edges and leave the outer field alive without making it visually sharp.
            int sideRows = Mathf.Max(1, SideLineCount / 2);
            for (int i = 0; i < sideLines.Count; i++)
            {
                bool left = i < sideRows;
                int row = i % sideRows;
                float t = sideRows <= 1 ? 0.5f : row / (float)(sideRows - 1);
                float y = Mathf.Lerp(h * 0.70f, -h * 0.63f, t);
                float sign = left ? -1f : 1f;
                float outerX = sign * w * 0.985f;
                float innerX = sign * w * (menuOverlay
                    ? 0.905f - (row % 2) * 0.012f
                    : 0.84f - (row % 2) * 0.035f);
                LineRenderer line = sideLines[i];
                line.positionCount = 3;
                line.SetPosition(0, new Vector3(outerX, y, 0f));
                line.SetPosition(1, new Vector3(innerX, y, 0f));
                line.SetPosition(2, new Vector3(innerX, y + (row % 2 == 0 ? -1f : 1f) * h * 0.055f, 0f));
                line.widthMultiplier = thin * 0.70f;
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
            float thin = h * 0.00265f;
            Color accent = ChapterAccent(chapter);
            Color cyan = Color.Lerp(accent, new Color(0.08f, 0.88f, 1f, 1f), 0.72f);
            Color violet = Color.Lerp(accent, new Color(0.52f, 0.31f, 1f, 1f), 0.68f);

            // The field drifts gently while the board stays perfectly stable.
            fxRoot.localPosition = new Vector3(
                Mathf.Sin(time * 0.24f) * h * 0.006f,
                Mathf.Cos(time * 0.19f) * h * 0.0045f,
                -0.15f);

            // Flowing perspective grid: rows continually emerge at the horizon and accelerate
            // toward the player. This is the clearest piece of backdrop motion.
            Vector3 horizon = new Vector3(0f, -h * 0.27f, 0f);
            for (int i = 0; i < horizontals.Count; i++)
            {
                if (menuOverlay) continue;
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
                    Mathf.Lerp(0.07f, menuOverlay ? 0.25f : 0.30f, flow) * edgeFade);
            }

            // Perspective rays gently fan left and right instead of remaining frozen.
            for (int i = 0; i < rays.Count; i++)
            {
                if (menuOverlay) continue;
                float u = rays.Count <= 1 ? 0.5f : i / (float)(rays.Count - 1);
                float sway = Mathf.Sin(time * 0.46f + i * 0.34f) * w * 0.036f;
                LineRenderer line = rays[i];
                line.SetPosition(0, horizon);
                line.SetPosition(1, new Vector3(Mathf.Lerp(-w * 1.34f, w * 1.34f, u) + sway,
                    -h * 1.08f, 0f));
                SetColor(line, i % 4 == 0 ? violet : cyan,
                    (menuOverlay ? 0.16f : 0.20f)
                        * (0.68f + 0.32f * Mathf.Sin(time * 0.62f + i * 0.37f)));
            }

            // Edge circuits illuminate in a staggered wave. Their geometry remains still so the
            // effect feels integrated with the board instead of drifting past it.
            int sideRows = Mathf.Max(1, SideLineCount / 2);
            for (int i = 0; i < sideLines.Count; i++)
            {
                int row = i % sideRows;
                LineRenderer line = sideLines[i];
                float wave = 0.5f + 0.5f * Mathf.Sin(time * 0.92f - row * 0.72f + (i < sideRows ? 0f : 1.1f));
                SetColor(line, i % 2 == 0 ? cyan : violet,
                    menuOverlay ? Mathf.Lerp(0.18f, 0.48f, wave) : Mathf.Lerp(0.10f, 0.36f, wave));
            }

            for (int i = 0; i < frames.Count; i++)
            {
                Color frameColor = i % 2 == 0 ? violet : cyan;
                float illumination = 0.5f + 0.5f * Mathf.Sin(time * 0.82f - i * 0.74f);
                float depthPulse = 1f + 0.007f * Mathf.Sin(time * 0.54f + i * 0.70f);
                Vector3 scale = new Vector3(depthPulse, depthPulse, 1f);
                float angle = Mathf.Sin(time * 0.28f + i) * 0.055f;
                frameGlows[i].transform.localScale = scale;
                frames[i].transform.localScale = scale;
                frameSweeps[i].transform.localScale = scale;
                frameGlows[i].transform.localEulerAngles = new Vector3(0f, 0f, angle);
                frames[i].transform.localEulerAngles = new Vector3(0f, 0f, angle);
                frameSweeps[i].transform.localEulerAngles = new Vector3(0f, 0f, angle);

                SetColor(frameGlows[i], frameColor, Mathf.Lerp(0.045f, 0.15f, illumination));
                SetColor(frames[i], frameColor,
                    Mathf.Lerp(0.27f - i * 0.015f, 0.52f - i * 0.020f, illumination));

                // A short bright segment travels around every frame, creating the clearly visible
                // premium "line lighting" requested without flashing the full screen.
                float sweep = Mathf.Repeat(time * (0.050f + i * 0.003f) + i * 0.143f, 1f);
                SetFrameSweep(frameSweeps[i], frames[i], sweep);
                SetColor(frameSweeps[i], Color.Lerp(frameColor, Color.white, 0.48f),
                    Mathf.Lerp(0.62f, 0.96f, illumination));

                if (menuOverlay)
                {
                    SetColor(frameGlows[i], frameColor,
                        Mathf.Lerp(0.035f, 0.12f, illumination));
                    SetColor(frames[i], frameColor,
                        Mathf.Lerp(0.22f - i * 0.012f, 0.44f - i * 0.017f, illumination));
                    SetColor(frameSweeps[i], Color.Lerp(frameColor, Color.white, 0.48f),
                        Mathf.Lerp(0.70f, 1.00f, illumination));
                }
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
                SetColor(f.line, f.violet ? violet : cyan,
                    menuOverlay ? Mathf.Lerp(0.20f, 0.62f, pulse) : Mathf.Lerp(0.16f, 0.68f, pulse));
            }

        }

        static void SetFrameSweep(LineRenderer sweep, LineRenderer frame, float centre)
        {
            const int Samples = 5;
            const float Span = 0.050f;
            sweep.positionCount = Samples;
            for (int i = 0; i < Samples; i++)
            {
                float offset = Mathf.Lerp(-Span, Span, i / (Samples - 1f));
                sweep.SetPosition(i, SampleLoop(frame, centre + offset));
            }
        }

        static Vector3 SampleLoop(LineRenderer line, float t)
        {
            int count = line != null ? line.positionCount : 0;
            if (count <= 0) return Vector3.zero;
            t = Mathf.Repeat(t, 1f);
            float scaled = t * count;
            int a = Mathf.FloorToInt(scaled) % count;
            int b = (a + 1) % count;
            return Vector3.Lerp(line.GetPosition(a), line.GetPosition(b), scaled - Mathf.Floor(scaled));
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
