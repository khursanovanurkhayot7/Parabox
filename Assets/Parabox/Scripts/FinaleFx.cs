using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Parabox
{
    // The end of the whole game — all fifty levels beaten. Not the win panel with an extra line
    // of text on it, which is what this replaced: finishing a fifty-level game and finishing
    // level 37 should not look the same.
    //
    // Its complete hierarchy is authored into Game.unity by the prebuilt-UI generator. Runtime
    // only fills the final tally, wires the two outcomes and plays the animation.
    //
    // The staging is the point. Each beat lands on a screen that has stopped moving:
    //   1  everything else goes; the screen sinks to open water
    //   2  a shaft of light finds you, and the sea starts rising past it
    //   3  the verdict, big, alone on the screen
    //   4  what you actually did — fifty levels, and the moves it cost you
    //   5  the name of the thing you finished, and whose it is
    //   6  the way out, last, so it is never competing with the moment
    //
    // Runs entirely on unscaled time: the game is frozen behind it and still has to animate.
    public class FinaleFx : MonoBehaviour
    {
        const float FadeIn = 1.1f;
        const int BubbleCount = 34;

        [SerializeField] CanvasGroup _group;
        [SerializeField] List<RectTransform> _bubbles = new List<RectTransform>();
        [SerializeField] List<float> _speed = new List<float>();
        [SerializeField] List<float> _drift = new List<float>();
        [SerializeField] List<float> _phase = new List<float>();
        [SerializeField] RectTransform _shaft;
        [SerializeField] Text _tallyText;
        float _t;
        [SerializeField] Button _againButton;
        [SerializeField] Button _levelsButton;
        bool _controlsReady;
        System.Action _onAgain;
        System.Action _onLevels;

        // Deep water, and the light coming down through it.
        static readonly Color Deep = new Color(0.016f, 0.055f, 0.098f, 1f);
        static readonly Color Cyan = new Color(0.435f, 0.918f, 0.949f, 1f);
        static readonly Color Warm = new Color(1f, 0.94f, 0.78f, 1f);

#if UNITY_EDITOR
        public static FinaleFx Prebuild(Canvas canvas, Font font, Sprite glow, Sprite cell)
        {
            var root = new GameObject("FinaleFx", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(canvas.transform, false);
            Stretch(root.GetComponent<RectTransform>());
            root.transform.SetAsLastSibling();          // above every other piece of HUD

            var fx = root.AddComponent<FinaleFx>();
            fx._group = root.GetComponent<CanvasGroup>();
            fx._group.alpha = 0f;
            fx._group.blocksRaycasts = true;
            fx.Compose(font, glow, cell);
            root.SetActive(false);
            return fx;
        }
#endif

        public bool IsFullyPrebuilt => _group != null && _tallyText != null
            && _againButton != null && _levelsButton != null
            && _bubbles != null && _bubbles.Count == BubbleCount;

        public void Play(System.Action onAgain, System.Action onLevels, int levels, int totalMoves)
        {
            if (!IsFullyPrebuilt)
            {
                Debug.LogError("[Parabox] Finale UI is not prebuilt. Run the Prebuilt UI generator.");
                return;
            }

            _onAgain = onAgain;
            _onLevels = onLevels;
            _tallyText.text = $"all {levels} levels solved\n{totalMoves:n0} moves in total";
            _againButton.onClick.AddListener(ChooseAgain);
            _levelsButton.onClick.AddListener(ChooseLevels);
            _controlsReady = false;
            _t = 0f;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _group.alpha = 0f;
            _group.blocksRaycasts = true;
            foreach (var n in new[] { "Verdict", "Rule", "Tally", "Title", "By", "Again", "Levels" })
            {
                Transform child = transform.Find(n);
                if (child != null) Alpha(child.gameObject, 0f);
            }
            StartCoroutine(Run());
        }

        void ChooseAgain() => _onAgain?.Invoke();
        void ChooseLevels() => _onLevels?.Invoke();

        void Compose(Font font, Sprite glow, Sprite cell)
        {
            // open water
            var scrim = Panel("Water", transform, Deep);
            Stretch(scrim);

            // the shaft of light from the surface, angled and soft
            if (glow != null)
            {
                var beam = Panel("Shaft", transform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.10f), glow);
                beam.anchorMin = beam.anchorMax = new Vector2(0.5f, 1f);
                beam.pivot = new Vector2(0.5f, 1f);
                beam.sizeDelta = new Vector2(760f, 1500f);
                beam.anchoredPosition = new Vector2(0f, 60f);
                beam.localRotation = Quaternion.Euler(0f, 0f, 9f);
                _shaft = beam;
            }

            // the sea rising past you
            if (glow != null)
                for (int i = 0; i < BubbleCount; i++)
                {
                    float r = 6f + (i * 37 % 23);
                    var b = Panel("Bubble", transform,
                                  new Color(1f, 1f, 1f, 0.05f + (i % 5) * 0.035f), glow);
                    b.sizeDelta = new Vector2(r * 2f, r * 2f);
                    b.anchorMin = b.anchorMax = new Vector2(0.5f, 0f);
                    b.anchoredPosition = new Vector2((i * 149 % 1000) - 500f, (i * 211 % 1200) - 100f);
                    _bubbles.Add(b);
                    _speed.Add(34f + (i * 53 % 60));
                    _drift.Add(9f + (i * 29 % 22));
                    _phase.Add(i * 0.7f);
                }

            // the verdict
            Label("Verdict", "THE DEEP IS YOURS", font, 76, FontStyle.Bold, Warm,
                  new Vector2(0f, 168f), 1000f);

            // a hairline under it, because the verdict needs a floor to sit on
            var rule = Panel("Rule", transform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f), cell);
            rule.anchorMin = rule.anchorMax = new Vector2(0.5f, 0.5f);
            rule.sizeDelta = new Vector2(300f, 2f);
            rule.anchoredPosition = new Vector2(0f, 122f);

            // what you actually did
            RectTransform tally = Label("Tally", "all 50 levels solved\n0 moves in total",
                font, 30, FontStyle.Normal, new Color(1f, 1f, 1f, 0.82f),
                new Vector2(0f, 56f), 900f);
            _tallyText = tally.GetComponent<Text>();

            // whose game this is
            Label("Title", "HAYOT'S PARABOX", font, 34, FontStyle.Bold,
                  new Color(Cyan.r, Cyan.g, Cyan.b, 0.95f), new Vector2(0f, -46f), 900f);
            Label("By", "by Nurhayot", font, 22, FontStyle.Normal,
                  new Color(1f, 1f, 1f, 0.5f), new Vector2(0f, -84f), 900f);

            // the way out
            _againButton = MakeButton("Again", "PLAY AGAIN", font, new Vector2(-155f, -168f), Cyan);
            _levelsButton = MakeButton("Levels", "LEVEL SELECT", font, new Vector2(155f, -168f),
                                       new Color(1f, 1f, 1f, 0.6f));

            // everything starts invisible; Run() brings each beat in
            foreach (var n in new[] { "Verdict", "Rule", "Tally", "Title", "By", "Again", "Levels" })
            {
                var t = transform.Find(n);
                if (t != null) Alpha(t.gameObject, 0f);
            }
        }

        IEnumerator Run()
        {
            // 1 — sink to open water
            yield return Fade(_group, 0f, 1f, FadeIn);
            yield return Wait(0.35f);

            // 2..6 — one beat at a time, each landing on a settled screen
            yield return Reveal("Verdict", 0.7f, 26f);
            yield return Reveal("Rule", 0.4f, 0f);
            yield return Wait(0.25f);
            yield return Reveal("Tally", 0.55f, 16f);
            yield return Wait(0.3f);
            yield return Reveal("Title", 0.5f, 12f);
            yield return Reveal("By", 0.45f, 8f);
            yield return Wait(0.4f);
            yield return Reveal("Again", 0.4f, 14f);
            yield return Reveal("Levels", 0.4f, 14f);
            _controlsReady = true;
            if (_againButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_againButton.gameObject);
        }

        public bool HandleArcadeInput(LuxoddArcadeAdapter arcade)
        {
            if (arcade == null) return false;
            return HandleChoiceInput(arcade.Direction, arcade.NavigationPulse, arcade.ConfirmDown,
                arcade.LevelsDown || arcade.BackDown || arcade.UndoDown);
        }

        public bool HandleKeyboardInput(Keyboard keyboard)
        {
            if (keyboard == null) return false;
            Vector2Int direction = Vector2Int.zero;
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
                direction = Vector2Int.left;
            else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
                direction = Vector2Int.right;
            else if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
                direction = Vector2Int.up;
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
                direction = Vector2Int.down;

            return HandleChoiceInput(direction, direction != Vector2Int.zero,
                keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame,
                keyboard.escapeKey.wasPressedThisFrame);
        }

        bool HandleChoiceInput(Vector2Int direction, bool movePulse, bool confirmDown, bool levelsDown)
        {
            // Swallow action presses while the finale is staging so the last level cannot be
            // accidentally reloaded before its choices have appeared.
            if (!_controlsReady)
                return confirmDown || levelsDown || movePulse;

            if (levelsDown)
            {
                if (_levelsButton != null) _levelsButton.onClick.Invoke();
                return true;
            }

            if (movePulse && direction != Vector2Int.zero)
            {
                Button target = SelectedButton() == _againButton ? _levelsButton : _againButton;
                if (target != null && EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(target.gameObject);
                Sfx.Hover();
                return true;
            }

            if (confirmDown)
            {
                Button selected = SelectedButton();
                if (selected == null) selected = _againButton;
                if (selected != null) selected.onClick.Invoke();
                return true;
            }
            return false;
        }

        Button SelectedButton()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            if (_againButton != null && selected == _againButton.gameObject) return _againButton;
            if (_levelsButton != null && selected == _levelsButton.gameObject) return _levelsButton;
            return null;
        }

        // Fade a child up while it rises the last few pixels into place — the same "arrive, don't
        // appear" motion the rest of the game's UI uses.
        IEnumerator Reveal(string child, float dur, float rise)
        {
            var t = transform.Find(child) as RectTransform;
            if (t == null) yield break;
            var to = t.anchoredPosition;
            var from = to - new Vector2(0f, rise);
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                float s = 1f - Mathf.Pow(1f - k, 3f);          // ease out cubic
                Alpha(t.gameObject, s);
                t.anchoredPosition = Vector2.LerpUnclamped(from, to, s);
                yield return null;
            }
            Alpha(t.gameObject, 1f);
            t.anchoredPosition = to;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;

            // the sea keeps rising; bubbles that leave the top come back from below
            for (int i = 0; i < _bubbles.Count; i++)
            {
                var p = _bubbles[i].anchoredPosition;
                p.y += _speed[i] * Time.unscaledDeltaTime;
                p.x += Mathf.Sin(_t * 0.8f + _phase[i]) * _drift[i] * Time.unscaledDeltaTime;
                if (p.y > 1300f) p.y = -120f;
                _bubbles[i].anchoredPosition = p;
            }

            // the shaft of light breathes, so the screen is never completely still
            if (_shaft != null)
            {
                var c = _shaft.GetComponent<Image>().color;
                c.a = 0.07f + Mathf.Sin(_t * 0.55f) * 0.035f;
                _shaft.GetComponent<Image>().color = c;
                _shaft.localRotation = Quaternion.Euler(0f, 0f, 9f + Mathf.Sin(_t * 0.4f) * 1.6f);
            }
        }

        // ---------------------------------------------------------------- building blocks
        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        static RectTransform Panel(string name, Transform parent, Color c, Sprite s = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.sprite = s;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        RectTransform Label(string name, string text, Font font, int size, FontStyle style,
                            Color c, Vector2 at, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = c;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(width, size * 2.4f);
            r.anchoredPosition = at;
            CrispUiTypography.Polish(t);
            return r;
        }

        Button MakeButton(string name, string text, Font font, Vector2 at, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(284f, 60f);
            r.anchoredPosition = at;

            var img = go.GetComponent<Image>();
            img.color = new Color(tint.r, tint.g, tint.b, 0.14f);

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            Stretch(label.GetComponent<RectTransform>());
            var t = label.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = 22;
            t.fontStyle = FontStyle.Bold;
            t.color = tint;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            CrispUiTypography.Polish(t);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            Sfx.AttachButton(btn);
            return btn;
        }

        static void Alpha(GameObject go, float a)
        {
            var g = go.GetComponent<CanvasGroup>();
            if (g == null) g = go.AddComponent<CanvasGroup>();
            g.alpha = a;
        }

        static IEnumerator Fade(CanvasGroup g, float from, float to, float dur)
        {
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(e / dur));
                yield return null;
            }
            g.alpha = to;
        }

        static IEnumerator Wait(float s)
        {
            float e = 0f;
            while (e < s) { e += Time.unscaledDeltaTime; yield return null; }
        }
    }
}
