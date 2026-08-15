using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox
{
    // One restrained typography treatment for every screen. The shipped Fredoka asset is already
    // SemiBold; asking Unity to synthesize Bold on top of it swells the glyphs, and combining that
    // with a four-copy Outline makes small labels visibly fuzzy. Keep the real font weight, retain
    // only a tight single shadow where one was authored, and snap screen-space canvases to pixels.
    public static class CrispUiTypography
    {
        const string FontFamilyToken = "Fredoka";
        static int pendingRenderPasses;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Apply(scene);
            // A few editor/runtime helpers finish wiring prebuilt UI during Start. Re-apply on the
            // first three renders so those labels receive the same treatment without a frame poll.
            pendingRenderPasses = 3;
        }

        static void OnWillRenderCanvases()
        {
            if (pendingRenderPasses <= 0) return;
            pendingRenderPasses--;
            Apply(SceneManager.GetActiveScene());
        }

        public static void Apply(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                    if (canvas.renderMode != RenderMode.WorldSpace) canvas.pixelPerfect = true;

                foreach (Text text in root.GetComponentsInChildren<Text>(true))
                    Polish(text);
            }
        }

        public static void Polish(Text text)
        {
            if (text == null) return;

            // This font file contains its real SemiBold outlines. Synthetic Bold adds a second,
            // offset stroke and is the main cause of the lumpy edges visible on "Level 50".
            if (text.font != null && text.font.name.IndexOf(
                    FontFamilyToken, StringComparison.OrdinalIgnoreCase) >= 0)
                text.fontStyle = FontStyle.Normal;

            text.alignByGeometry = true;

            // Unity UI Outline renders four displaced copies of every glyph. At responsive canvas
            // scales those offsets land between pixels and soften the whole word, so text uses its
            // native edge instead. Image/panel outlines are deliberately untouched.
            foreach (Outline outline in text.GetComponents<Outline>())
                outline.enabled = false;

            // Preserve authored depth with one precise copy rather than a wide blurred edge.
            foreach (Shadow shadow in text.GetComponents<Shadow>())
            {
                if (shadow is Outline) continue;
                Color color = shadow.effectColor;
                color.a = Mathf.Min(color.a, 0.52f);
                shadow.effectColor = color;
                shadow.effectDistance = new Vector2(0f, -1f);
                shadow.useGraphicAlpha = true;
            }
        }
    }
}
