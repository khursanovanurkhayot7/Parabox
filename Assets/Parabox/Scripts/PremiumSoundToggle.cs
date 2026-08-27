using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Scene-backed sound control used on the main screen.  The editor prebuilder authors every
    // visual child; a player build only switches the two serialized states and never constructs UI.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button), typeof(Image))]
    public sealed class PremiumSoundToggle : MonoBehaviour
    {
        [SerializeField, HideInInspector] Button button;
        [SerializeField, HideInInspector] GameObject soundOnState;
        [SerializeField, HideInInspector] GameObject soundOffState;
        bool lastMuted;

        static readonly Color Cyan = new Color(0.435f, 0.918f, 0.949f, 1f);
        static readonly Color Coral = new Color(1f, 0.40f, 0.49f, 1f);
        static readonly Color Purple = new Color(0.56f, 0.31f, 1f, 1f);
        static readonly Color GlassTop = new Color(0.035f, 0.075f, 0.16f, 1f);
        static readonly Color GlassBottom = new Color(0.01f, 0.035f, 0.075f, 0.99f);

        public bool IsFullyPrebuilt
        {
            get
            {
                return button != null && soundOnState != null && soundOffState != null
                    && soundOnState.GetComponentInChildren<PremiumSoundIcon>(true) != null
                    && soundOnState.GetComponentInChildren<PremiumSoundDial>(true) != null
                    && soundOffState.GetComponentInChildren<PremiumSoundIcon>(true) != null
                    && soundOffState.GetComponentInChildren<PremiumSoundDial>(true) != null;
            }
        }

        void Awake()
        {
            button = button != null ? button : GetComponent<Button>();
        }

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Sfx.Init();
            button = button != null ? button : GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveListener(Toggle);
                button.onClick.AddListener(Toggle);
            }
            Refresh();
        }

        void OnDisable()
        {
            if (button != null) button.onClick.RemoveListener(Toggle);
        }

        void Update()
        {
            // M can also change the shared state through MainMenuUI.
            if (lastMuted != Sfx.Muted) Refresh();
        }

        public void Toggle()
        {
            Sfx.ToggleMute();
            Refresh();
        }

        public void Refresh()
        {
            lastMuted = Sfx.Muted;
            if (soundOnState != null) soundOnState.SetActive(!lastMuted);
            if (soundOffState != null) soundOffState.SetActive(lastMuted);
        }

#if UNITY_EDITOR
        public void PrebuildStaticUi(Sprite discSprite, Sprite glowSprite, Sprite cellSprite,
            Font displayFont)
        {
            button = GetComponent<Button>();
            Image face = GetComponent<Image>();
            if (button == null || face == null) return;

            face.sprite = discSprite;
            face.type = Image.Type.Simple;
            face.preserveAspect = false;
            face.color = Color.white;
            face.raycastTarget = true;
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;

            UIGradient gradient = face.GetComponent<UIGradient>();
            if (gradient == null) gradient = face.gameObject.AddComponent<UIGradient>();
            gradient.enabled = true;
            gradient.top = GlassTop;
            gradient.bottom = GlassBottom;

            Shadow shadow = face.GetComponent<Shadow>();
            if (shadow == null) shadow = face.gameObject.AddComponent<Shadow>();
            shadow.enabled = true;
            shadow.effectColor = new Color(0f, 0.01f, 0.04f, 0.76f);
            shadow.effectDistance = new Vector2(0f, -7f);
            shadow.useGraphicAlpha = true;

            Outline outline = face.GetComponent<Outline>();
            if (outline == null) outline = face.gameObject.AddComponent<Outline>();
            // PremiumSoundDial draws the one intentional rim; a second outline looks like debris.
            outline.enabled = false;

            soundOnState = EnsureStateRoot("SoundOnState");
            soundOffState = EnsureStateRoot("SoundOffState");
            BuildState(soundOnState, false, glowSprite, cellSprite, displayFont);
            BuildState(soundOffState, true, glowSprite, cellSprite, displayFont);
            soundOnState.SetActive(true);
            soundOffState.SetActive(false);
        }

        GameObject EnsureStateRoot(string objectName)
        {
            Transform existing = transform.Find(objectName);
            GameObject state = existing != null ? existing.gameObject
                : new GameObject(objectName, typeof(RectTransform));
            if (existing == null) state.transform.SetParent(transform, false);
            RectTransform rect = state.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return state;
        }

        static void BuildState(GameObject root, bool muted, Sprite glowSprite, Sprite cellSprite,
            Font displayFont)
        {
            RectTransform state = EnsureRect(root.transform, "PremiumSoundState");
            state.anchorMin = Vector2.zero;
            state.anchorMax = Vector2.one;
            state.offsetMin = Vector2.zero;
            state.offsetMax = Vector2.zero;

            Color accent = muted ? Coral : Color.Lerp(Cyan, Color.white, 0.14f);

            Image glow = EnsureImage(state, "PremiumGlow");
            SetCentred(glow.rectTransform, Vector2.zero, new Vector2(134f, 134f));
            glow.sprite = glowSprite;
            glow.preserveAspect = false;
            glow.enabled = glowSprite != null;
            Color glowColour = muted ? accent : Color.Lerp(Cyan, accent, 0.68f);
            glow.color = new Color(glowColour.r, glowColour.g, glowColour.b,
                muted ? 0.16f : 0.18f);
            glow.raycastTarget = false;
            glow.transform.SetAsFirstSibling();

            PremiumSoundDial dial = EnsureDial(state, "SoundDial");
            SetCentred(dial.rectTransform, Vector2.zero, new Vector2(98f, 98f));
            dial.SetState(muted, Cyan, Color.Lerp(Cyan, Purple, 0.58f));
            dial.raycastTarget = false;

            PremiumSoundIcon glyph = EnsureGlyph(state, "SoundGlyph");
            SetCentred(glyph.rectTransform, new Vector2(0f, 7f), new Vector2(48f, 40f));
            glyph.SetState(muted, accent);
            glyph.raycastTarget = false;

            Text label = EnsureText(state, "StateLabel");
            SetCentred(label.rectTransform, new Vector2(7f, -27f), new Vector2(34f, 18f));
            label.text = muted ? "OFF" : "ON";
            label.font = displayFont != null ? displayFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 12;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.Lerp(accent, Color.white, 0.28f);
            label.supportRichText = false;
            label.raycastTarget = false;
            CrispUiTypography.Polish(label);

            Image led = EnsureImage(state, "StateLed");
            SetCentred(led.rectTransform, new Vector2(-15f, -27f), new Vector2(6f, 6f));
            led.sprite = cellSprite;
            led.color = accent;
            led.raycastTarget = false;

            state.gameObject.SetActive(true);
            label.transform.SetAsLastSibling();
        }

        static void SetCentred(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static RectTransform EnsureRect(Transform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            GameObject item = existing != null ? existing.gameObject
                : new GameObject(objectName, typeof(RectTransform));
            if (existing == null) item.transform.SetParent(parent, false);
            return item.transform as RectTransform;
        }

        static Image EnsureImage(RectTransform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            GameObject item = existing != null ? existing.gameObject : new GameObject(objectName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null) item.transform.SetParent(parent, false);
            Image image = item.GetComponent<Image>();
            if (image == null) image = item.AddComponent<Image>();
            return image;
        }

        static Text EnsureText(RectTransform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            GameObject item = existing != null ? existing.gameObject : new GameObject(objectName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            if (existing == null) item.transform.SetParent(parent, false);
            Text text = item.GetComponent<Text>();
            if (text == null) text = item.AddComponent<Text>();
            return text;
        }

        static PremiumSoundIcon EnsureGlyph(RectTransform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            GameObject item = existing != null ? existing.gameObject : new GameObject(objectName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(PremiumSoundIcon));
            if (existing == null) item.transform.SetParent(parent, false);
            PremiumSoundIcon icon = item.GetComponent<PremiumSoundIcon>();
            if (icon == null) icon = item.AddComponent<PremiumSoundIcon>();
            return icon;
        }

        static PremiumSoundDial EnsureDial(RectTransform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            GameObject item = existing != null ? existing.gameObject : new GameObject(objectName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(PremiumSoundDial));
            if (existing == null) item.transform.SetParent(parent, false);
            PremiumSoundDial dial = item.GetComponent<PremiumSoundDial>();
            if (dial == null) dial = item.AddComponent<PremiumSoundDial>();
            return dial;
        }
#endif
    }
}
