using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Parabox
{
    // Plays the origin film on the first gameplay entry of every launched app/Unity Play session.
    // The UI is built at runtime so the MP4 can stay in StreamingAssets without a fragile scene
    // VideoClip link.
    public sealed class OriginFilmPlayer : MonoBehaviour
    {
        public const string FileName = "HayotsParaboxOrigin.mp4";
        const float SkipDelay = 3f;
        const float PrepareTimeout = 15f;

        GameObject root;
        CanvasGroup group;
        RawImage screen;
        RectTransform filmRect;
        CanvasGroup filmGroup;
        RectTransform topCurtain;
        RectTransform bottomCurtain;
        RectTransform revealLine;
        CanvasGroup revealLineGroup;
        Image entranceFlash;
        Button skipButton;
        VideoPlayer video;
        bool finished;
        bool failed;

        public bool PlaybackStarted { get; private set; }

        public IEnumerator Play()
        {
            BuildUi();
            MusicPlayer.Instance?.SetCinematicDuck(true);

            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.waitForFirstFrame = true;
            video.skipOnDrop = true;
            video.renderMode = VideoRenderMode.APIOnly;
            video.audioOutputMode = VideoAudioOutputMode.Direct;
            video.controlledAudioTrackCount = 1;
            video.EnableAudioTrack(0, true);
            video.SetDirectAudioMute(0, Sfx.Muted);
            video.SetDirectAudioVolume(0, 1f);
            video.loopPointReached += OnFinished;
            video.errorReceived += OnError;

            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            video.url = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
            video.Prepare();

            float waited = 0f;
            while (!video.isPrepared && !failed && waited < PrepareTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!video.isPrepared || failed)
            {
                if (!failed) Debug.LogError("[OriginFilm] Timed out preparing " + video.url);
                yield return FadeOut();
                Cleanup();
                yield break;
            }

            screen.texture = video.texture;
            video.Play();
            PlaybackStarted = true;
            yield return AnimateEntrance();

            float elapsed = 0f;
            while (!finished && !failed)
            {
                elapsed += Time.unscaledDeltaTime;
                bool maySkip = elapsed >= SkipDelay;
                if (skipButton != null && skipButton.gameObject.activeSelf != maySkip)
                    skipButton.gameObject.SetActive(maySkip);

                if (maySkip && SkipPressed()) finished = true;
                yield return null;
            }

            if (video != null && video.isPlaying) video.Stop();
            yield return FadeOut();
            Cleanup();
        }

        bool SkipPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame
                || kb.enterKey.wasPressedThisFrame))
                return true;

            var arcade = LuxoddArcadeAdapter.Instance;
            return arcade != null && (arcade.BackDown || arcade.ConfirmDown);
        }

        void BuildUi()
        {
            root = new GameObject("OriginFilmCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            DontDestroyOnLoad(root);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            group = root.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;

            var bg = CreateStretch("Black", root.transform).gameObject.AddComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = true;

            filmRect = CreateStretch("Film", root.transform);
            screen = filmRect.gameObject.AddComponent<RawImage>();
            screen.color = Color.white;
            screen.raycastTarget = false;
            filmGroup = filmRect.gameObject.AddComponent<CanvasGroup>();
            filmGroup.alpha = 0f;
            filmGroup.blocksRaycasts = false;
            filmRect.localScale = Vector3.one * 0.965f;

            // A restrained cyan edge holds the movie like a premium cinema card while the
            // aperture opens. It becomes effectively invisible once the film fills the view.
            var edge = filmRect.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.08f, 0.82f, 1f, 0.55f);
            edge.effectDistance = new Vector2(2f, -2f);

            var fitter = filmRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;

            topCurtain = CreateCurtain("TopCurtain", root.transform, true);
            bottomCurtain = CreateCurtain("BottomCurtain", root.transform, false);

            var lineGo = new GameObject("CinemaRevealLine", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(Outline));
            revealLine = (RectTransform)lineGo.transform;
            revealLine.SetParent(root.transform, false);
            revealLine.anchorMin = revealLine.anchorMax = new Vector2(0.5f, 0.5f);
            revealLine.pivot = new Vector2(0.5f, 0.5f);
            revealLine.sizeDelta = new Vector2(1680f, 4f);
            revealLine.localScale = new Vector3(0f, 1f, 1f);
            var lineImage = lineGo.GetComponent<Image>();
            lineImage.color = new Color(0.15f, 0.9f, 1f, 1f);
            lineImage.raycastTarget = false;
            revealLineGroup = lineGo.GetComponent<CanvasGroup>();
            revealLineGroup.alpha = 0f;
            revealLineGroup.blocksRaycasts = false;
            var lineGlow = lineGo.GetComponent<Outline>();
            lineGlow.effectColor = new Color(0.42f, 0.2f, 1f, 0.8f);
            lineGlow.effectDistance = new Vector2(3f, -3f);

            entranceFlash = CreateStretch("EntranceGlow", root.transform).gameObject.AddComponent<Image>();
            entranceFlash.color = new Color(0.08f, 0.75f, 1f, 0f);
            entranceFlash.raycastTarget = false;

            skipButton = CreateSkipButton(root.transform);
            skipButton.onClick.AddListener(() => finished = true);
            skipButton.gameObject.SetActive(false);
        }

        static RectTransform CreateStretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        static RectTransform CreateCurtain(string name, Transform parent, bool top)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, top ? 0.5f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0.5f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            var image = go.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return rt;
        }

        IEnumerator AnimateEntrance()
        {
            const float duration = 1.05f;
            float elapsed = 0f;
            while (elapsed < duration && !failed)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float open = 1f - Mathf.Pow(1f - t, 3f);
                float filmIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.42f));

                if (filmGroup != null) filmGroup.alpha = filmIn;
                if (filmRect != null)
                    filmRect.localScale = Vector3.one * Mathf.Lerp(0.965f, 1f, open);

                float curtainScale = Mathf.Max(0f, 1f - open);
                if (topCurtain != null)
                    topCurtain.localScale = new Vector3(1f, curtainScale, 1f);
                if (bottomCurtain != null)
                    bottomCurtain.localScale = new Vector3(1f, curtainScale, 1f);

                float lineIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.34f));
                float lineOut = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((t - 0.36f) / 0.42f));
                if (revealLine != null)
                    revealLine.localScale = new Vector3(lineIn, 1f, 1f);
                if (revealLineGroup != null)
                    revealLineGroup.alpha = lineIn * lineOut;

                if (entranceFlash != null)
                {
                    float pulse = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.12f) / 0.62f));
                    entranceFlash.color = new Color(0.08f, 0.75f, 1f,
                        Mathf.Max(0f, pulse) * 0.11f);
                }
                yield return null;
            }

            if (filmGroup != null) filmGroup.alpha = 1f;
            if (filmRect != null) filmRect.localScale = Vector3.one;
            if (topCurtain != null) topCurtain.gameObject.SetActive(false);
            if (bottomCurtain != null) bottomCurtain.gameObject.SetActive(false);
            if (revealLine != null) revealLine.gameObject.SetActive(false);
            if (entranceFlash != null) entranceFlash.gameObject.SetActive(false);
        }

        static Button CreateSkipButton(Transform parent)
        {
            var go = new GameObject("SkipOriginFilm", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-42f, -42f);
            rt.sizeDelta = new Vector2(176f, 58f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.018f, 0.075f, 0.16f, 0.94f);
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0.86f, 1f, 0.82f);
            outline.effectDistance = new Vector2(2f, -2f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.72f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(0.35f, 0.8f, 0.92f, 1f);
            button.colors = colors;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>();
            label.text = "SKIP";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 22;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.9f, 0.98f, 1f, 1f);
            label.raycastTarget = false;
            return button;
        }

        IEnumerator FadeOut()
        {
            const float duration = 0.45f;
            float elapsed = 0f;
            float from = group != null ? group.alpha : 1f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (group != null)
                    group.alpha = Mathf.Lerp(from, 0f,
                        Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }
        }

        void OnFinished(VideoPlayer _) => finished = true;

        void OnError(VideoPlayer _, string message)
        {
            failed = true;
            Debug.LogError("[OriginFilm] " + message);
        }

        void Cleanup()
        {
            MusicPlayer.Instance?.SetCinematicDuck(false);
            if (video != null)
            {
                video.loopPointReached -= OnFinished;
                video.errorReceived -= OnError;
                Destroy(video);
                video = null;
            }
            if (root != null) Destroy(root);
            root = null;
        }

        void OnDestroy()
        {
            MusicPlayer.Instance?.SetCinematicDuck(false);
            if (video != null)
            {
                video.loopPointReached -= OnFinished;
                video.errorReceived -= OnError;
                Destroy(video);
                video = null;
            }
            if (root != null) Destroy(root);
        }
    }
}
