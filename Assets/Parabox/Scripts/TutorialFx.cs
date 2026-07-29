using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // The screen-space chrome for each chapter-opening cinematic onboarding.
    //
    // The tutorial "video" is the real board, rendered by the camera into a RenderTexture and shown
    // inside a raised, framed panel floating over a dimmed background — so it reads as a premium
    // presentation, not a flat overlay on gameplay. GameManager owns the TIMELINE (camera, the
    // self-playing solve, the RenderTexture); this owns the FRAME: the dim scrim, the elevated card
    // (soft shadow + polished frame + the video), one short line, and the three-option panel.
    //
    // It lives on its OWN canvas (above the HUD, own raycaster) so hiding the gameplay HUD during the
    // cinematic never touches it. Everything animates on unscaled time — the game is held still.
    public class TutorialFx : MonoBehaviour
    {
        [Header("Dim background")]
        public CanvasGroup scrimGroup;
        // MUST reach fully opaque: the camera is redirected into the RenderTexture while this is up,
        // so anything less than 1 lets uncleared screen show through behind the panel. The "dimmed
        // background" look comes from the scrim's dark colour + the soft glow behind the card, not
        // from partial transparency.
        public float scrimAlpha = 1f;

        [Header("Raised panel (the video card)")]
        public CanvasGroup panelGroup;
        public RectTransform panelRT;
        public RawImage videoImage;        // shows the board's RenderTexture

        [Header("Tutorial title")]
        public Text titleText;              // built automatically when older scenes have no title wired

        [Header("Caption — one short line")]
        public CanvasGroup captionGroup;
        public Text captionText;
        public float readTime = 2.0f;

        [Header("Choice panel")]
        public CanvasGroup choiceGroup;
        public RectTransform choiceRT;
        public Button againButton, tryButton, skipButton;

        Vector3 _panelHome = Vector3.one;
        Coroutine _panelCo;

        void Awake()
        {
            SimplifyPanelChrome();
            EnsureTitleBadge();

            // Inert by default. Only chapter openers run the cinematic, so nothing here may show or
            // eat a click on ordinary levels: scrim + panel invisible, caption + choice hidden.
            if (panelRT != null) _panelHome = panelRT.localScale;
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
            HideChoice();
        }

        // The title sits centred ABOVE the video frame, leaving the demonstrated board completely
        // unobstructed. It remains a child of panelRT so the same entrance/exit animation carries it
        // on chapter tutorials 1-5 and it can never leak onto ordinary gameplay. Reuse the project's
        // Fredoka font from the caption so it belongs to the existing UI.
        void EnsureTitleBadge()
        {
            if (panelRT == null) return;

            var existing = panelRT.Find("TutorialTitle");
            if (existing != null)
            {
                titleText = existing.GetComponentInChildren<Text>(true);
                if (titleText != null) titleText.text = "TUTORIAL";
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
            badgeImage.color = new Color(0.025f, 0.09f, 0.15f, 0.92f);
            var card = panelRT.Find("Card");
            if (card != null && card.TryGetComponent<Image>(out var cardImage))
            {
                badgeImage.sprite = cardImage.sprite;
                badgeImage.type = Image.Type.Sliced;
            }

            var badgeShadow = badge.AddComponent<Shadow>();
            badgeShadow.effectColor = new Color(0f, 0.02f, 0.05f, 0.65f);
            badgeShadow.effectDistance = new Vector2(0f, -4f);

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

            badgeRT.SetAsLastSibling();
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
        public void PanelIn()
        {
            if (_panelCo != null) StopCoroutine(_panelCo);
            _panelCo = StartCoroutine(PanelTo(1f, 1f, 0.5f, true));
        }

        // The exit: card and scrim fade away together, revealing the ready-to-play board behind.
        public Coroutine FadeOutAll()
        {
            if (captionGroup != null) captionGroup.alpha = 0f;
            if (_panelCo != null) StopCoroutine(_panelCo);
            _panelCo = StartCoroutine(PanelTo(0f, 0.96f, 0.5f, false));
            StartCoroutine(Fade(scrimGroup, scrimGroup != null ? scrimGroup.alpha : scrimAlpha, 0f, 0.5f));
            return _panelCo;
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
        }

        public void HideChoice()
        {
            if (choiceGroup == null) return;
            choiceGroup.alpha = 0f;
            choiceGroup.interactable = false;
            choiceGroup.blocksRaycasts = false;
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
