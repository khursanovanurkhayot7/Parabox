using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // The screen-space chrome for each chapter-opening cinematic onboarding.
    //
    // The tutorial video is a real, separate, prebuilt mini-puzzle shown inside a raised panel.
    // It uses the gameplay model and premium board renderer, but never borrows the current campaign
    // puzzle or its solver route. GameManager owns the timeline; this owns the
    // FRAME: the dim scrim, the elevated card
    // (soft shadow + polished frame + the video), one short line, the two-option panel, and a
    // dedicated purple Skip action centred below the card for the complete tutorial, including the
    // final Repeat/Try choice and first-appearance mechanic lessons. First-appearance
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

        [Header("Nested-box door legend — Chapter 2 onward")]
        public CanvasGroup nestedDoorLegendGroup;
        public Text nestedDoorLegendText;

        [Header("Tutorial duration countdown")]
        public RectTransform tutorialCountdownRoot;
        public Text tutorialCountdownLabel;
        public Image[] tutorialCountdownBorder;
        bool tutorialCountdownReady;

        [Header("Legacy text briefing (prebuilt fallback, hidden)")]
        public CanvasGroup briefingGroup;
        public RectTransform briefingRT;
        public Text briefingTitleText;
        public Text briefingText;

        [Header("Choice panel")]
        public CanvasGroup choiceGroup;
        public RectTransform choiceRT;
        public Button againButton, tryButton;
        public Button skipButton;                   // prebuilt outside choiceGroup; Purple / button 6
        public Text skipCountdownText;              // prebuilt above Skip; final choice timeout

        Vector3 _panelHome = Vector3.one;
        Coroutine _panelCo;
        Coroutine _captionCo;
        const float CaptionFadeInDuration = 1.0f;

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
            EnsureNestedDoorLegend();
            PolishTutorialCopyLayout();
            EnsureTutorialCountdown();
            SetTutorialCountdown(false, 0f, 1f);
            SetNestedDoorLegendVisible(false);
            HideMechanicBriefingImmediately();
            if (mechanicDemo != null) mechanicDemo.HideImmediate();
            HideChoice();
            ConfigureTutorialChoices();
            HideSkip();
        }

#if UNITY_EDITOR
        // Called only by the editor generator. Nothing in this method runs in a player build; the
        // title, chrome, button styles and navigation are saved directly in Game.unity.
        public void PrebuildStaticUi()
        {
            SimplifyPanelChrome();
            EnsureTitleBadge();
            EnsureNestedDoorLegend();
            ConfigureTutorialChoices();
            if (videoImage != null)
                mechanicDemo = MechanicDemoView.Prebuild(videoImage.rectTransform,
                    captionText != null ? captionText.font : null);
            PolishTutorialCopyLayout();
            EnsureTutorialCountdown();
            SetTutorialCountdown(false, 0f, 1f);
        }
#endif

        // Repeat plus TRY IT YOURSELF remain the two end choices. TRY IT YOURSELF always enters
        // the real campaign level behind the video. The separate purple button-6 Skip stays visible
        // centred below the card so every tutorial state has the same Luxodd escape action.
        void ConfigureTutorialChoices()
        {
            // At the final choice the persistent purple Skip occupies the middle slot. Repeat and
            // Try sit either side with generous gaps, matching their visible left-to-right order.
            if (choiceRT != null) choiceRT.sizeDelta = new Vector2(1100f, 120f);

            if (againButton != null)
            {
                RectTransform rect = againButton.transform as RectTransform;
                if (rect != null)
                {
                    rect.anchoredPosition = new Vector2(-350f, 0f);
                    rect.sizeDelta = new Vector2(260f, 72f);
                }
                Navigation nav = againButton.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = null;
                nav.selectOnRight = tryButton;
                againButton.navigation = nav;
            }

            if (tryButton != null)
            {
                RectTransform rect = tryButton.transform as RectTransform;
                if (rect != null)
                {
                    rect.anchoredPosition = new Vector2(350f, 0f);
                    rect.sizeDelta = new Vector2(290f, 72f);
                }
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
                ConfigureSkipButton();
                HideSkip();
            }
        }

        // The same serialized button advances from the first chapter video to the second, then
        // enters the real campaign level. No runtime replacement button is created.
        public void SetPrimaryChoiceLabel(bool hasNextVideo)
        {
            if (tryButton == null) return;
            Text label = tryButton.GetComponentInChildren<Text>(true);
            if (label != null) label.text = hasNextVideo ? "NEXT" : "TRY IT YOURSELF";
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

        // Chapter II introduces recursive rooms. Keep their wall language visible in every later
        // tutorial as one compact in-frame card: a real pulsing lamp followed by a short rule.
        void EnsureNestedDoorLegend()
        {
            if (panelRT == null) return;

            Transform existing = panelRT.Find("NestedDoorLegend");
            if (existing != null)
            {
                nestedDoorLegendGroup = GetOrAdd<CanvasGroup>(existing.gameObject);
                StyleNestedDoorLegend(existing as RectTransform);
                return;
            }

            var legend = new GameObject("NestedDoorLegend", typeof(RectTransform),
                typeof(CanvasGroup));
            RectTransform rect = (RectTransform)legend.transform;
            rect.SetParent(panelRT, false);
            nestedDoorLegendGroup = legend.GetComponent<CanvasGroup>();
            StyleNestedDoorLegend(rect);
            nestedDoorLegendGroup.alpha = 0f;
            nestedDoorLegendGroup.interactable = false;
            nestedDoorLegendGroup.blocksRaycasts = false;
        }

        void StyleNestedDoorLegend(RectTransform rect)
        {
            if (rect == null) return;

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // VideoPanel is 1100 x 620. Keep a full visual gutter between the board's lower
            // cyan edge and even the legend's drop shadow so the two cards never look attached.
            rect.anchoredPosition = new Vector2(0f, -280f);
            rect.sizeDelta = new Vector2(780f, 40f);

            // Upgrade the old root-level text object in place so existing scenes do not need to be
            // regenerated. The root becomes a clean container and the new children provide depth.
            Text legacyText = rect.GetComponent<Text>();
            if (legacyText != null) legacyText.enabled = false;
            foreach (Shadow legacyEffect in rect.GetComponents<Shadow>())
                legacyEffect.enabled = false;

            Sprite cardSprite = null;
            Transform card = panelRT.Find("Card");
            if (card != null && card.TryGetComponent<Image>(out Image cardImage))
                cardSprite = cardImage.sprite;

            Image background = EnsureLegendImage(rect, "LegendBackground", cardSprite,
                Color.white, Vector2.zero, rect.sizeDelta);
            background.type = cardSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            UIGradient gradient = GetOrAdd<UIGradient>(background.gameObject);
            gradient.top = new Color(0.055f, 0.16f, 0.25f, 0.98f);
            gradient.bottom = new Color(0.012f, 0.035f, 0.075f, 0.98f);
            Outline frame = GetOrAdd<Outline>(background.gameObject);
            frame.effectColor = new Color(0.26f, 0.90f, 1f, 0.82f);
            frame.effectDistance = new Vector2(1.6f, -1.6f);
            frame.useGraphicAlpha = true;
            Shadow cardShadow = null;
            foreach (Shadow candidate in background.GetComponents<Shadow>())
                if (!(candidate is Outline)) { cardShadow = candidate; break; }
            if (cardShadow == null) cardShadow = background.gameObject.AddComponent<Shadow>();
            cardShadow.effectColor = new Color(0f, 0f, 0.02f, 0.76f);
            cardShadow.effectDistance = new Vector2(0f, -5f);
            cardShadow.useGraphicAlpha = true;
            background.transform.SetAsFirstSibling();

            RectTransform lampRoot = EnsureLegendRect(rect, "RedLamp", new Vector2(-344f, 0f),
                new Vector2(34f, 34f));
            UIPulse lampPulse = GetOrAdd<UIPulse>(lampRoot.gameObject);
            lampPulse.amplitude = 0.085f;
            lampPulse.speed = 3.1f;
            EnsureLegendImage(lampRoot, "Glow", cardSprite,
                new Color(1f, 0.12f, 0.20f, 0.30f), Vector2.zero, new Vector2(34f, 34f));
            Image housing = EnsureLegendImage(lampRoot, "Housing", cardSprite,
                new Color(0.30f, 0.055f, 0.085f, 1f), Vector2.zero, new Vector2(27f, 27f));
            Outline housingEdge = GetOrAdd<Outline>(housing.gameObject);
            housingEdge.effectColor = new Color(0.75f, 0.12f, 0.20f, 1f);
            housingEdge.effectDistance = new Vector2(1.3f, -1.3f);
            EnsureLegendImage(lampRoot, "Core", cardSprite,
                new Color(1f, 0.18f, 0.25f, 1f), Vector2.zero, new Vector2(16f, 16f));
            EnsureLegendImage(lampRoot, "Highlight", cardSprite,
                new Color(1f, 0.88f, 0.90f, 0.95f), new Vector2(-3f, 4f), new Vector2(5f, 5f));

            Transform textTransform = rect.Find("LegendText");
            if (textTransform == null)
            {
                var textObject = new GameObject("LegendText", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text), typeof(Outline), typeof(Shadow));
                textObject.transform.SetParent(rect, false);
                textTransform = textObject.transform;
            }
            RectTransform textRect = (RectTransform)textTransform;
            textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = new Vector2(22f, 0f);
            textRect.sizeDelta = new Vector2(680f, 34f);
            nestedDoorLegendText = GetOrAdd<Text>(textTransform.gameObject);

            nestedDoorLegendText.text =
                "<color=#FF6570>RED</color> = CLOSED"
                + "   •   <color=#42E7FF>CYAN GAP</color> = OPEN";
            nestedDoorLegendText.font = captionText != null
                ? captionText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            nestedDoorLegendText.fontSize = 20;
            nestedDoorLegendText.fontStyle = FontStyle.Bold;
            nestedDoorLegendText.alignment = TextAnchor.MiddleCenter;
            nestedDoorLegendText.color = Color.white;
            nestedDoorLegendText.supportRichText = true;
            nestedDoorLegendText.resizeTextForBestFit = true;
            nestedDoorLegendText.resizeTextMinSize = 16;
            nestedDoorLegendText.resizeTextMaxSize = 20;
            nestedDoorLegendText.horizontalOverflow = HorizontalWrapMode.Wrap;
            nestedDoorLegendText.verticalOverflow = VerticalWrapMode.Truncate;
            nestedDoorLegendText.raycastTarget = false;

            Outline outline = nestedDoorLegendText.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(0f, 0.025f, 0.075f, 0.98f);
                outline.effectDistance = new Vector2(1.4f, -1.4f);
                outline.useGraphicAlpha = true;
            }
            Shadow glow = null;
            foreach (Shadow candidate in nestedDoorLegendText.GetComponents<Shadow>())
                if (!(candidate is Outline))
                {
                    glow = candidate;
                    break;
                }
            if (glow != null)
            {
                glow.effectColor = new Color(1f, 0.24f, 0.32f, 0.34f);
                glow.effectDistance = new Vector2(0f, -2f);
                glow.useGraphicAlpha = true;
            }
            CrispUiTypography.Polish(nestedDoorLegendText);
            rect.SetAsLastSibling();
        }

        void PolishTutorialCopyLayout()
        {
            // Keep the current instruction in a compact top-centre card, fully inside the frame.
            if (captionGroup != null && captionGroup.transform is RectTransform captionRect)
            {
                captionRect.anchorMin = captionRect.anchorMax = captionRect.pivot =
                    new Vector2(0.5f, 0.5f);
                // Leave a deliberate gap above the board frame. The card's drop shadow extends
                // below its rectangle, so the previous position still appeared glued to the cyan
                // top edge even though the bounds barely cleared it.
                captionRect.anchoredPosition = new Vector2(0f, 260f);
                captionRect.sizeDelta = new Vector2(760f, 54f);
                StyleCaptionCard(captionRect);
            }
            StyleComfortableCopy(captionText, new Vector2(710f, 44f), 22);

            if (mechanicDemo != null && mechanicDemo.ruleText != null)
            {
                RectTransform ruleRect = mechanicDemo.ruleText.rectTransform;
                ruleRect.anchoredPosition = new Vector2(0f, 260f);
                StyleComfortableCopy(mechanicDemo.ruleText, new Vector2(710f, 44f), 22);
            }
        }

        void StyleCaptionCard(RectTransform rect)
        {
            Sprite cardSprite = null;
            Transform card = panelRT != null ? panelRT.Find("Card") : null;
            if (card != null && card.TryGetComponent<Image>(out Image cardImage))
                cardSprite = cardImage.sprite;

            GetOrAdd<CanvasRenderer>(rect.gameObject).cullTransparentMesh = false;
            Image background = GetOrAdd<Image>(rect.gameObject);
            background.sprite = cardSprite;
            background.type = cardSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            background.color = Color.white;
            background.raycastTarget = false;

            UIGradient gradient = GetOrAdd<UIGradient>(rect.gameObject);
            gradient.top = new Color(0.060f, 0.18f, 0.28f, 0.98f);
            gradient.bottom = new Color(0.010f, 0.035f, 0.080f, 0.98f);

            Outline frame = GetOrAdd<Outline>(rect.gameObject);
            frame.effectColor = new Color(0.26f, 0.90f, 1f, 0.88f);
            frame.effectDistance = new Vector2(1.6f, -1.6f);
            frame.useGraphicAlpha = true;

            Shadow depth = null;
            foreach (Shadow candidate in rect.GetComponents<Shadow>())
                if (!(candidate is Outline)) { depth = candidate; break; }
            if (depth == null) depth = rect.gameObject.AddComponent<Shadow>();
            depth.effectColor = new Color(0f, 0f, 0.02f, 0.72f);
            depth.effectDistance = new Vector2(0f, -5f);
            depth.useGraphicAlpha = true;
        }

        static void StyleComfortableCopy(Text text, Vector2 size, int maxFontSize)
        {
            if (text == null) return;
            text.rectTransform.sizeDelta = size;
            text.fontSize = maxFontSize;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 17;
            text.resizeTextMaxSize = maxFontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 0.95f;
        }

        static RectTransform EnsureLegendRect(Transform parent, string name, Vector2 position,
            Vector2 size)
        {
            Transform existing = parent.Find(name);
            RectTransform rect;
            if (existing != null && existing is RectTransform existingRect)
                rect = existingRect;
            else
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                rect = (RectTransform)go.transform;
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image EnsureLegendImage(Transform parent, string name, Sprite sprite, Color color,
            Vector2 position, Vector2 size)
        {
            RectTransform rect = EnsureLegendRect(parent, name, position, size);
            CanvasRenderer renderer = GetOrAdd<CanvasRenderer>(rect.gameObject);
            renderer.cullTransparentMesh = false;
            Image image = GetOrAdd<Image>(rect.gameObject);
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        public void SetNestedDoorLegendVisible(bool visible)
        {
            EnsureNestedDoorLegend();
            if (nestedDoorLegendGroup == null) return;
            nestedDoorLegendGroup.alpha = visible ? 1f : 0f;
            nestedDoorLegendGroup.interactable = false;
            nestedDoorLegendGroup.blocksRaycasts = false;
        }

        void EnsureTutorialCountdown()
        {
            if (panelRT == null) return;
            if (tutorialCountdownReady && tutorialCountdownRoot != null
                && tutorialCountdownLabel != null && tutorialCountdownBorder != null
                && tutorialCountdownBorder.Length == 4)
                return;
            if (tutorialCountdownRoot == null)
            {
                Transform existing = panelRT.Find("TutorialCountdownBorder");
                if (existing != null) tutorialCountdownRoot = existing as RectTransform;
            }
            if (tutorialCountdownRoot == null)
            {
                var rootObject = new GameObject("TutorialCountdownBorder", typeof(RectTransform));
                rootObject.transform.SetParent(panelRT, false);
                tutorialCountdownRoot = (RectTransform)rootObject.transform;
            }

            tutorialCountdownRoot.anchorMin = tutorialCountdownRoot.anchorMax =
                tutorialCountdownRoot.pivot = new Vector2(0.5f, 0.5f);
            tutorialCountdownRoot.anchoredPosition = Vector2.zero;
            Vector2 panelSize = panelRT.sizeDelta;
            if (panelSize.x < 100f || panelSize.y < 100f) panelSize = new Vector2(1100f, 620f);
            tutorialCountdownRoot.sizeDelta = panelSize;

            string[] names = { "TimerTop", "TimerRight", "TimerBottom", "TimerLeft" };
            Vector2 half = panelSize * 0.5f;
            Vector2[] positions =
            {
                new Vector2(0f, half.y + 4f), new Vector2(half.x + 4f, 0f),
                new Vector2(0f, -half.y - 4f), new Vector2(-half.x - 4f, 0f)
            };
            Vector2[] sizes =
            {
                new Vector2(panelSize.x + 8f, 7f), new Vector2(7f, panelSize.y + 8f),
                new Vector2(panelSize.x + 8f, 7f), new Vector2(7f, panelSize.y + 8f)
            };
            Sprite timerSprite = null;
            Transform panelCard = panelRT.Find("Card");
            if (panelCard != null && panelCard.TryGetComponent<Image>(out Image panelCardImage))
                timerSprite = panelCardImage.sprite;
            if (tutorialCountdownBorder == null || tutorialCountdownBorder.Length != 4)
                tutorialCountdownBorder = new Image[4];
            for (int i = 0; i < tutorialCountdownBorder.Length; i++)
            {
                Image segment = EnsureLegendImage(tutorialCountdownRoot, names[i], timerSprite,
                    new Color(0.26f, 0.90f, 1f, 0.94f), positions[i], sizes[i]);
                segment.type = Image.Type.Filled;
                segment.fillMethod = i % 2 == 0
                    ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
                segment.fillOrigin = i == 0 ? (int)Image.OriginHorizontal.Left
                    : i == 1 ? (int)Image.OriginVertical.Top
                    : i == 2 ? (int)Image.OriginHorizontal.Right
                    : (int)Image.OriginVertical.Bottom;
                segment.fillAmount = 1f;
                tutorialCountdownBorder[i] = segment;
            }

            Transform labelTransform = tutorialCountdownRoot.Find("CountdownLabel");
            if (labelTransform == null)
            {
                var labelObject = new GameObject("CountdownLabel", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Text), typeof(Outline));
                labelObject.transform.SetParent(tutorialCountdownRoot, false);
                labelTransform = labelObject.transform;
            }
            RectTransform labelRect = (RectTransform)labelTransform;
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.pivot = new Vector2(1f, 0.5f);
            // The tutorial title owns the centre above the frame; the countdown gets a clear
            // top-right position. Anchor its right edge to the frame instead of using a fixed
            // centre offset, so every aspect ratio keeps the label close to the outer edge.
            labelRect.anchoredPosition = new Vector2(-22f, half.y + 48f);
            labelRect.sizeDelta = new Vector2(270f, 44f);
            tutorialCountdownLabel = GetOrAdd<Text>(labelTransform.gameObject);
            tutorialCountdownLabel.font = captionText != null
                ? captionText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tutorialCountdownLabel.fontSize = 25;
            tutorialCountdownLabel.fontStyle = FontStyle.Bold;
            tutorialCountdownLabel.alignment = TextAnchor.MiddleRight;
            tutorialCountdownLabel.color = new Color(0.77f, 0.96f, 1f, 1f);
            tutorialCountdownLabel.raycastTarget = false;
            tutorialCountdownLabel.supportRichText = false;
            tutorialCountdownLabel.resizeTextForBestFit = true;
            tutorialCountdownLabel.resizeTextMinSize = 18;
            tutorialCountdownLabel.resizeTextMaxSize = 25;
            Outline labelOutline = GetOrAdd<Outline>(tutorialCountdownLabel.gameObject);
            labelOutline.effectColor = new Color(0f, 0.025f, 0.08f, 0.96f);
            labelOutline.effectDistance = new Vector2(1.5f, -1.5f);
            CrispUiTypography.Polish(tutorialCountdownLabel);
            tutorialCountdownRoot.SetAsLastSibling();
            tutorialCountdownReady = true;
        }

        public void SetTutorialCountdown(bool visible, float remaining, float duration,
            bool choicePhase = false)
        {
            EnsureTutorialCountdown();
            if (tutorialCountdownRoot == null) return;
            tutorialCountdownRoot.gameObject.SetActive(visible);
            if (!visible) return;

            float safeDuration = Mathf.Max(0.01f, duration);
            float progress = Mathf.Clamp01(remaining / safeDuration);
            Color normal = new Color(0.26f, 0.90f, 1f, 0.94f);
            Color warning = new Color(1f, 0.08f, 0.12f, 1f);
            Color colour = remaining <= 4f ? warning : normal;
            if (tutorialCountdownBorder != null)
            {
                for (int i = 0; i < tutorialCountdownBorder.Length; i++)
                {
                    Image segment = tutorialCountdownBorder[i];
                    if (segment == null) continue;
                    segment.fillAmount = Mathf.Clamp01(progress * 4f - (3 - i));
                    segment.color = colour;
                }
            }
            if (tutorialCountdownLabel != null)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
                tutorialCountdownLabel.text = seconds > 0
                    ? (choicePhase ? "AUTO-CONTINUE IN " + seconds : "TUTORIAL  " + seconds)
                    : (choicePhase ? "STARTING GAME" : "STARTING LEVEL");
                tutorialCountdownLabel.color = remaining <= 4f
                    ? new Color(1f, 0.26f, 0.30f, 1f)
                    : new Color(0.77f, 0.96f, 1f, 1f);
            }
            tutorialCountdownRoot.SetAsLastSibling();
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
            if (videoImage == null) return;
            videoImage.texture = tex;
            videoImage.color = tex != null
                ? Color.white
                : new Color(0.018f, 0.055f, 0.145f, 1f);
            // The old abstract diagram remains serialized only so existing scenes do not lose a
            // component reference. A real gameplay RenderTexture is now the only visible lesson.
            if (tex != null) HideMechanicDemoImmediately();
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
            HideCaptionImmediately();
            SetNestedDoorLegendVisible(false);
            SetTutorialCountdown(false, 0f, 1f);
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
            HideCaptionImmediately();
            SetNestedDoorLegendVisible(false);
            SetTutorialCountdown(false, 0f, 1f);
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
            StopCaptionFade();
            if (captionText != null) captionText.text = text;
            PrepareCaptionFadeIn();
            yield return Fade(captionGroup, 0f, 1f, CaptionFadeInDuration);
            yield return Wait(readTime);
            yield return Fade(captionGroup, 1f, 0f, 0.3f);
        }

        // ---- choice ------------------------------------------------------------------------------

        public IEnumerator ShowChoice()
        {
            ShowSkip();
            if (choiceGroup == null) yield break;
            choiceGroup.alpha = 1f;
            choiceGroup.interactable = false;
            choiceGroup.blocksRaycasts = false;

            CanvasGroup repeatGroup = againButton != null
                ? GetOrAdd<CanvasGroup>(againButton.gameObject) : null;
            CanvasGroup tryGroup = tryButton != null
                ? GetOrAdd<CanvasGroup>(tryButton.gameObject) : null;
            SetChoiceButtonVisible(repeatGroup, false);
            SetChoiceButtonVisible(tryGroup, false);
            // Skip has already been standing in the centre throughout the lesson. The two new
            // decisions now arrive one after another around it instead of covering it.
            if (againButton != null)
                yield return RevealChoiceButton(againButton, repeatGroup, 0.24f);
            if (tryButton != null)
                yield return RevealChoiceButton(tryButton, tryGroup, 0.24f);

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
            if (againButton != null)
                SetChoiceButtonVisible(againButton.GetComponent<CanvasGroup>(), false);
            if (tryButton != null)
                SetChoiceButtonVisible(tryButton.GetComponent<CanvasGroup>(), false);
        }

        static IEnumerator RevealChoiceButton(Button button, CanvasGroup group, float duration)
        {
            if (button == null) yield break;
            RectTransform rect = button.transform as RectTransform;
            Vector2 home = rect != null ? rect.anchoredPosition : Vector2.zero;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
                if (group != null) group.alpha = t;
                if (rect != null)
                    rect.anchoredPosition = home + new Vector2(0f, -22f * (1f - t));
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = home;
            SetChoiceButtonVisible(group, true);
        }

        static void SetChoiceButtonVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        void ConfigureSkipButton()
        {
            if (skipButton == null) return;
            RectTransform rect = skipButton.transform as RectTransform;
            if (rect != null)
            {
                Transform tutorialRoot = choiceRT != null ? choiceRT.parent : rect.parent;
                if (tutorialRoot != null && rect.parent != tutorialRoot)
                    rect.SetParent(tutorialRoot, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, -410f);
                rect.sizeDelta = new Vector2(310f, 72f);
                // The video card blocks raycasts while it is visible. Keep Skip as the last
                // sibling so the button that the player can see is also the object receiving it.
                rect.SetAsLastSibling();
            }
            if (skipButton.targetGraphic != null)
                skipButton.targetGraphic.raycastTarget = true;
            Transform numberBadge = skipButton.transform.Find("ButtonNumberBadge");
            if (numberBadge != null)
                numberBadge.gameObject.SetActive(false);
            Text numberText = numberBadge != null
                ? numberBadge.GetComponentInChildren<Text>(true) : null;
            if (numberText != null)
            {
                numberText.text = string.Empty;
                numberText.gameObject.SetActive(false);
            }
            Text label = null;
            Text fallback = null;
            foreach (Text candidate in skipButton.GetComponentsInChildren<Text>(true))
            {
                if (numberBadge != null && candidate.transform.IsChildOf(numberBadge)) continue;
                if (fallback == null) fallback = candidate;
                if (candidate.name == "Label" || candidate.name == "Lbl")
                {
                    label = candidate;
                    break;
                }
            }
            if (label == null) label = fallback;
            if (label != null)
            {
                // Hide the cabinet number in both current and older scenes. The physical
                // Luxodd button-6 binding remains unchanged; this is presentation only.
                label.text = "SKIP TUTORIAL";
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                // Button 6 is intentionally hidden, so reserve no space for its old badge.
                // Equal zero offsets place the label at the true visual centre of the face.
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                label.alignment = TextAnchor.MiddleCenter;
            }
            Navigation nav = skipButton.navigation;
            nav.mode = Navigation.Mode.None;
            skipButton.navigation = nav;
        }

        // Skip is prebuilt and serialized by the editor generator. Runtime only positions it
        // centred below the card and changes visibility; it never constructs walkthrough UI.
        public void ShowSkip()
        {
            if (skipButton == null) return;
            ConfigureSkipButton();
            skipButton.gameObject.SetActive(true);
            skipButton.interactable = true;
            skipButton.transform.SetAsLastSibling();
            CanvasGroup inputGroup = skipButton.GetComponent<CanvasGroup>();
            if (inputGroup != null)
            {
                inputGroup.alpha = 1f;
                inputGroup.interactable = true;
                inputGroup.blocksRaycasts = true;
            }
            SetSkipCountdown(false, 0);
        }

        public void HideSkip()
        {
            if (skipButton == null) return;
            SetSkipCountdown(false, 0);
            if (EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == skipButton.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            skipButton.interactable = false;
            skipButton.gameObject.SetActive(false);
        }

        public void SetSkipCountdown(bool visible, int seconds)
        {
            if (skipCountdownText == null) return;
            skipCountdownText.gameObject.SetActive(visible);
            skipCountdownText.text = visible
                ? "AUTO-CONTINUE IN " + Mathf.Max(0, seconds) : string.Empty;
            // Match the other arcade countdowns: the final three seconds become urgent red.
            skipCountdownText.color = visible && seconds <= 3
                ? new Color(1f, 0.30f, 0.30f, 1f)
                : new Color(0.82f, 0.68f, 1f, 1f);
        }

        public void HideCaptionImmediately()
        {
            StopCaptionFade();
            if (captionGroup != null) captionGroup.alpha = 0f;
        }

        public void ShowCaptionPersistent(string text)
        {
            StopCaptionFade();
            if (captionText != null) captionText.text = text;
            if (captionGroup == null) return;
            PrepareCaptionFadeIn();
            _captionCo = StartCoroutine(FadePersistentCaptionIn());
        }

        void PrepareCaptionFadeIn()
        {
            if (captionGroup == null) return;
            captionGroup.alpha = 0f;
            captionGroup.interactable = false;
            captionGroup.blocksRaycasts = false;
        }

        IEnumerator FadePersistentCaptionIn()
        {
            yield return Fade(captionGroup, 0f, 1f, CaptionFadeInDuration);
            _captionCo = null;
        }

        void StopCaptionFade()
        {
            if (_captionCo == null) return;
            StopCoroutine(_captionCo);
            _captionCo = null;
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
