using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // The screen-space chrome for each chapter-opening cinematic onboarding.
    //
    // The tutorial video is a small prebuilt mechanic vignette shown inside a raised, framed panel.
    // It never borrows the current puzzle or its solver route, so onboarding can explain a rule
    // without revealing how that level is beaten. GameManager owns the timeline; this owns the
    // FRAME: the dim scrim, the elevated card
    // (soft shadow + polished frame + the video), one short line, the two-option panel, and a
    // dedicated Skip action that remains available throughout every walkthrough. First-appearance
    // mechanic lessons use this same video card; the older text briefing remains serialized only
    // for scene compatibility and is kept hidden.
    //
    // It lives on its OWN canvas (above the HUD, own raycaster) so hiding the gameplay HUD during the
    // cinematic never touches it. Everything animates on unscaled time — the game is held still.
    public class TutorialFx : MonoBehaviour
    {
        [Header("Dim background")]
        public CanvasGroup scrimGroup;
        // Keep a little of the presentation background visible. Besides looking less abrupt, this
        // guarantees that a broken or missing video frame can never leave the whole display as an
        // indistinguishable permanent black screen.
        public float scrimAlpha = 0.86f;

        [Header("Raised panel (the video card)")]
        public CanvasGroup panelGroup;
        public RectTransform panelRT;
        public RawImage videoImage;        // dark backing behind the prebuilt mechanic vignette
        public MechanicDemoView mechanicDemo;

        [Header("Tutorial title")]
        public Text titleText;              // serialized by the editor prebuilder

        [Header("Caption — one short line")]
        public CanvasGroup captionGroup;
        public Text captionText;
        public float readTime = 2.0f;

        [Header("Legacy text briefing (prebuilt fallback, hidden)")]
        public CanvasGroup briefingGroup;
        public RectTransform briefingRT;
        public Text briefingTitleText;
        public Text briefingText;

        [Header("Choice panel")]
        public CanvasGroup choiceGroup;
        public RectTransform choiceRT;
        public Button againButton, tryButton;
        public Button skipButton;                   // prebuilt outside choiceGroup; Purple / RB

        Vector3 _panelHome = Vector3.one;
        Coroutine _panelCo;

        void Awake()
        {
            // Inert by default. Only chapter openers run the cinematic, so nothing here may show or
            // eat a click on ordinary levels: scrim + panel invisible, caption + choice hidden.
            if (panelRT != null)
            {
                _panelHome = panelRT.localScale;
                if (Mathf.Abs(_panelHome.x) < 0.0001f || Mathf.Abs(_panelHome.y) < 0.0001f)
                {
                    _panelHome = Vector3.one;
                    panelRT.localScale = _panelHome;
                }
            }
            if (scrimGroup != null)
            {
                scrimGroup.alpha = 0f;
                // CanvasGroup.blocksRaycasts defaults to TRUE — an alpha-0 scrim on a sortingOrder-100
                // canvas would otherwise swallow every click on every level. It never needs to block:
                // input is gated by GameManager during the cinematic, and the choice buttons carry
                // their own raycasts.
                scrimGroup.blocksRaycasts = false;
            }
            SetPanel(0f, 0.9f);
            if (captionGroup != null) captionGroup.alpha = 0f;
            HideMechanicBriefingImmediately();
            if (mechanicDemo != null) mechanicDemo.HideImmediate();
            HideChoice();
            HideSkip();
        }

#if UNITY_EDITOR
        // Called only by the editor generator. Nothing in this method runs in a player build; the
        // title, chrome, button styles and navigation are saved directly in Game.unity.
        public void PrebuildStaticUi()
        {
            SimplifyPanelChrome();
            EnsureTitleBadge();
            ConfigureTutorialChoices();
            if (videoImage != null)
                mechanicDemo = MechanicDemoView.Prebuild(videoImage.rectTransform,
                    captionText != null ? captionText.font : null);
        }
#endif

        // Repeat/Try remain the two end choices. Skip is a separate always-available walkthrough
        // action, so controller users never have to navigate away from those final choices.
        void ConfigureTutorialChoices()
        {
            if (choiceRT != null) choiceRT.sizeDelta = new Vector2(760f, 120f);

            if (againButton != null)
            {
                RectTransform rect = againButton.transform as RectTransform;
                if (rect != null) rect.anchoredPosition = new Vector2(-180f, 0f);
                Navigation nav = againButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = null;
                nav.selectOnRight = tryButton;
                againButton.navigation = nav;
            }

            if (tryButton != null)
            {
                RectTransform rect = tryButton.transform as RectTransform;
                if (rect != null) rect.anchoredPosition = new Vector2(180f, 0f);
                Navigation nav = tryButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = againButton;
                nav.selectOnRight = null;
                tryButton.navigation = nav;
            }

            ArcadeActionButtonStyle.Apply(againButton, "REPEAT", 22);
            ArcadeActionButtonStyle.Apply(tryButton, "TRY IT YOURSELF", 20);
            if (skipButton != null)
            {
                Navigation nav = skipButton.navigation;
                nav.mode = Navigation.Mode.None;
                skipButton.navigation = nav;
                HideSkip();
            }
        }

        // The title sits centred ABOVE the video frame, leaving the demonstrated board completely
        // unobstructed. It remains a child of panelRT so the same entrance/exit animation carries it
        // on chapter tutorials 1-5 and it can never leak onto ordinary gameplay. It is deliberately
        // text-only: the old dark rounded badge looked like a detached second panel.
        void EnsureTitleBadge()
        {
            if (panelRT == null) return;

            var existing = panelRT.Find("TutorialTitle");
            if (existing != null)
            {
                titleText = existing.GetComponentInChildren<Text>(true);
                if (titleText != null) titleText.text = "TUTORIAL";
                RemoveTitleBackground(existing);
                PositionTitle((RectTransform)existing);
                existing.SetAsLastSibling();
                return;
            }

            var badge = new GameObject("TutorialTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var badgeRT = (RectTransform)badge.transform;
            badgeRT.SetParent(panelRT, false);
            PositionTitle(badgeRT);

            var badgeImage = badge.GetComponent<Image>();
            badgeImage.raycastTarget = false;
            badgeImage.color = Color.clear;
            badgeImage.enabled = false;
            var card = panelRT.Find("Card");
            if (card != null && card.TryGetComponent<Image>(out var cardImage))
            {
                badgeImage.sprite = cardImage.sprite;
                badgeImage.type = Image.Type.Sliced;
            }

            var badgeShadow = badge.AddComponent<Shadow>();
            badgeShadow.effectColor = new Color(0f, 0.02f, 0.05f, 0.65f);
            badgeShadow.effectDistance = new Vector2(0f, -4f);
            badgeShadow.enabled = false;

            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var labelRT = (RectTransform)label.transform;
            labelRT.SetParent(badgeRT, false);
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(18f, 6f);
            labelRT.offsetMax = new Vector2(-18f, -6f);

            titleText = label.GetComponent<Text>();
            titleText.text = "TUTORIAL";
            titleText.font = captionText != null ? captionText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 32;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = new Color(0.82f, 0.98f, 1f, 1f);
            titleText.raycastTarget = false;
            titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = label.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.04f, 0.07f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            CrispUiTypography.Polish(titleText);

            badgeRT.SetAsLastSibling();
        }

        static void RemoveTitleBackground(Transform titleRoot)
        {
            if (titleRoot == null) return;
            if (titleRoot.TryGetComponent<Image>(out var background))
            {
                background.color = Color.clear;
                background.raycastTarget = false;
                background.enabled = false;
            }
            if (titleRoot.TryGetComponent<Shadow>(out var shadow))
                shadow.enabled = false;
        }

        static void PositionTitle(RectTransform badgeRT)
        {
            badgeRT.anchorMin = badgeRT.anchorMax = new Vector2(0.5f, 1f);
            badgeRT.pivot = new Vector2(0.5f, 0.5f);
            badgeRT.anchoredPosition = new Vector2(0f, 58f);
            badgeRT.sizeDelta = new Vector2(300f, 64f);
        }

        // The video already has a clear rectangular edge. Two bright rings plus a large card shadow
        // made that edge look like several unrelated borders, particularly at smaller Game-view
        // scales. Keep one quiet hairline and remove the redundant outer ring.
        void SimplifyPanelChrome()
        {
            if (panelRT == null) return;

            var outer = panelRT.Find("FrameOuter");
            if (outer != null) outer.gameObject.SetActive(false);

            var frame = panelRT.Find("Frame");
            if (frame != null && frame.TryGetComponent<Image>(out var image))
            {
                var c = image.color;
                c.a = Mathf.Min(c.a, 0.28f);
                image.color = c;
            }

            var shadow = panelRT.Find("Shadow");
            if (shadow != null && shadow.TryGetComponent<Image>(out var shadowImage))
            {
                var c = shadowImage.color;
                c.a = Mathf.Min(c.a, 0.18f);
                shadowImage.color = c;
            }
        }

        void SetPanel(float alpha, float scale)
        {
            if (panelGroup != null)
            {
                panelGroup.alpha = alpha;
                panelGroup.interactable = false;   // the video is not interactive; the buttons are separate
                panelGroup.blocksRaycasts = alpha > 0.5f;
            }
            if (panelRT != null) panelRT.localScale = _panelHome * scale;
        }

        public void SetVideo(Texture tex)
        {
            if (videoImage != null) videoImage.texture = tex;
        }

        public IEnumerator PlayMechanicDemo(MechanicCatalog.Id id)
        {
            if (videoImage != null)
            {
                videoImage.texture = null;
                videoImage.color = new Color(0.018f, 0.055f, 0.145f, 1f);
            }
            if (mechanicDemo == null)
            {
                // A stale scene is still safe and spoiler-free: show the rule as a caption, never
                // fall back to the current level board or its stored solution.
                yield return Caption(MechanicCatalog.Lesson(id));
                yield break;
            }
            yield return mechanicDemo.Play(id);
        }

        public void HideMechanicDemoImmediately()
        {
            if (mechanicDemo != null) mechanicDemo.HideImmediate();
        }

        public void SetTitle(string title)
        {
            if (titleText != null)
                titleText.text = string.IsNullOrWhiteSpace(title) ? "TUTORIAL" : title;
        }

        // Step 1 of the entrance: dim the screen to black BEFORE the camera is redirected into the
        // RenderTexture. The camera is still drawing the board to the screen while this runs, so the
        // fade is over the live board — no uncleared-screen garbage is ever visible.
        public Coroutine ScrimIn()
        {
            return StartCoroutine(Fade(scrimGroup, scrimGroup != null ? scrimGroup.alpha : 0f, scrimAlpha, 0.45f));
        }

        // Cover the screen fully opaque THIS frame, no fade. Called in the cinematic's synchronous
        // setup (before the first frame renders) so the full-screen game board is never shown — the
        // tutorial opens directly instead of "board appears, then fades out, then the panel".
        public void CoverInstant()
        {
            if (scrimGroup != null) scrimGroup.alpha = scrimAlpha;
        }

        // Step 2: the raised card rises and fades in over the (now opaque) scrim.
        public void PanelIn(float duration = 0.5f)
        {
            if (_panelCo != null) StopCoroutine(_panelCo);
            // Always begin transparent so entering from the main-menu PLAY button is a true
            // fade-in even if this component survived a fast Play Mode reload.
            SetPanel(0f, 0.94f);
            _panelCo = StartCoroutine(PanelTo(1f, 1f, Mathf.Max(0.01f, duration), true));
        }

        // The exit: card and scrim fade away together, revealing the ready-to-play board behind.
        public Coroutine FadeOutAll()
        {
            if (captionGroup != null) captionGroup.alpha = 0f;
            // Reveal the live camera immediately. The card can still dissolve gracefully, but a
            // stalled animation or a zero-sized Editor Game view can no longer hold a black cover
            // over gameplay and make it look frozen.
            if (scrimGroup != null)
            {
                scrimGroup.alpha = 0f;
                scrimGroup.blocksRaycasts = false;
            }
            if (_panelCo != null) StopCoroutine(_panelCo);
            _panelCo = StartCoroutine(PanelTo(0f, 0.96f, 0.5f, false));
            return _panelCo;
        }

        public void RevealGameplayImmediately()
        {
            if (_panelCo != null) StopCoroutine(_panelCo);
            _panelCo = null;
            if (scrimGroup != null)
            {
                scrimGroup.alpha = 0f;
                scrimGroup.blocksRaycasts = false;
            }
            SetPanel(0f, 1f);
            if (captionGroup != null) captionGroup.alpha = 0f;
            HideMechanicDemoImmediately();
            HideMechanicBriefingImmediately();
            HideChoice();
            HideSkip();
        }

        IEnumerator PanelTo(float alpha, float scale, float dur, bool interactiveAtEnd)
        {
            float a0 = panelGroup != null ? panelGroup.alpha : 0f;
            float s0 = panelRT != null ? panelRT.localScale.x / Mathf.Max(0.0001f, _panelHome.x) : 1f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);   // ease-out
                if (panelGroup != null) panelGroup.alpha = Mathf.Lerp(a0, alpha, e);
                if (panelRT != null) panelRT.localScale = _panelHome * Mathf.Lerp(s0, scale, e);
                yield return null;
            }
            if (panelGroup != null)
            {
                panelGroup.alpha = alpha;
                panelGroup.blocksRaycasts = interactiveAtEnd && alpha > 0.5f;
            }
            if (panelRT != null) panelRT.localScale = _panelHome * scale;
        }

        // ---- caption -----------------------------------------------------------------------------

        public IEnumerator Caption(string text)
        {
            if (captionText != null) captionText.text = text;
            yield return Fade(captionGroup, 0f, 1f, 0.35f);
            yield return Wait(readTime);
            yield return Fade(captionGroup, 1f, 0f, 0.3f);
        }

        // ---- choice ------------------------------------------------------------------------------

        public IEnumerator ShowChoice()
        {
            if (choiceGroup == null) yield break;
            float dur = 0.5f, t = 0f;
            Vector2 home = choiceRT != null ? choiceRT.anchoredPosition : Vector2.zero;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                choiceGroup.alpha = e;
                if (choiceRT != null) choiceRT.anchoredPosition = new Vector2(home.x, home.y - 24f * (1f - e));
                yield return null;
            }
            choiceGroup.alpha = 1f;
            if (choiceRT != null) choiceRT.anchoredPosition = home;
            choiceGroup.interactable = true;
            choiceGroup.blocksRaycasts = true;
            if (tryButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(tryButton.gameObject);
        }

        public void HideChoice()
        {
            if (choiceGroup == null) return;
            choiceGroup.alpha = 0f;
            choiceGroup.interactable = false;
            choiceGroup.blocksRaycasts = false;
        }

        // Skip is prebuilt and serialized by the editor generator. Runtime only changes the
        // visibility/interactability of that existing object; it never constructs walkthrough UI.
        public void ShowSkip()
        {
            if (skipButton == null) return;
            skipButton.gameObject.SetActive(true);
            skipButton.interactable = true;
        }

        public void HideSkip()
        {
            if (skipButton == null) return;
            if (EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == skipButton.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            skipButton.interactable = false;
            skipButton.gameObject.SetActive(false);
        }

        public void HideCaptionImmediately()
        {
            if (captionGroup != null) captionGroup.alpha = 0f;
        }

        // Unlike the cinematic caption, this panel is not a child of the hidden video card. It is
        // serialized directly under the tutorial canvas, so the first-appearance lesson is visible
        // before the obstacle's first playable frame.
        public void ShowMechanicBriefingImmediately(string lesson)
        {
            if (briefingText != null) briefingText.text = lesson;
            if (briefingTitleText != null) briefingTitleText.text = "NEW MECHANIC";
            if (briefingGroup == null) return;
            briefingGroup.alpha = 1f;
            briefingGroup.interactable = false;
            briefingGroup.blocksRaycasts = false;
        }

        public IEnumerator HoldThenHideMechanicBriefing()
        {
            if (briefingGroup == null) yield break;
            yield return Wait(Mathf.Max(1.8f, readTime));
            yield return Fade(briefingGroup, briefingGroup.alpha, 0f, 0.28f);
            briefingGroup.interactable = false;
            briefingGroup.blocksRaycasts = false;
        }

        public void HideMechanicBriefingImmediately()
        {
            if (briefingGroup == null) return;
            briefingGroup.alpha = 0f;
            briefingGroup.interactable = false;
            briefingGroup.blocksRaycasts = false;
        }

        // ---- shared ------------------------------------------------------------------------------

        static IEnumerator Wait(float dur)
        {
            float t = 0f;
            while (t < dur) { t += Time.unscaledDeltaTime; yield return null; }
        }

        static IEnumerator Fade(CanvasGroup g, float from, float to, float dur)
        {
            if (g == null) yield break;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur)));
                yield return null;
            }
            g.alpha = to;
        }
    }
}
