using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox
{
    // Main menu + level selection:
    //   Home  ->  the progression map (one route snaking from level 1 to level 30).
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Home")]
        public Button playButton;
        public Button levelsButton;
        public Button quitButton;

        [Header("Live board — the O IS the real next level, in world space")]
        public RectTransform boardInner;   // the O's inner rect: the on-screen anchor the world board is framed into
        public RectTransform oSlot;        // the whole O (ring + inner) — scaled for the entrance
        public RectTransform oRing;        // just the ring — spins as it lands
        public Camera menuCam;             // orthographic; flies into the board on Start
        public CameraBackdrop backdrop;    // world backdrop behind the board (matches the game's)
        public CanvasGroup homeGroup;      // all home UI; fades out during the dive
        public CanvasGroup backgroundGroup; // the drifting squares — outlive the home text, fade with the dive
        public GameObject[] levelPrefabs;  // the level prefabs, to parse + render the real board
        public LevelTheme[] boardThemes;   // one per chapter/tier — same as the game's levelThemes
        public GameObject floorPrefab, gridPrefab, wallPrefab, boxPrefab, metaBoxPrefab, playerPrefab, boxGoalPrefab, playerGoalPrefab;
        public Sprite fxRing, fxGlow, fxVignette, fxCell;

        [Header("Progression map")]
        public UIScreen levelBoardScreen;
        public Button levelBoardBackButton;

        [Header("Route stops (all 30, indexed globally)")]
        public Button[] levelButtons;
        public Image[] levelFills;          // the node face — carries the state colour
        public Image[] levelBorders;        // the node's stroke — one weight across every node
        public Text[] levelNumbers;
        public GameObject[] levelChecks;
        public GameObject[] levelLocks;
        public GameObject[] levelHighlights;
        public GameObject[] levelStars;     // "perfect" badge — solved in par

        [Header("The route")]
        public Image[] pathDots;            // the trail between nodes; lights up behind you as you go
        public int pathDotsPerLink = 7;
        public MapProgressFx progressFx;    // plays what CHANGED when you arrive from a win

        [Header("Chapter gates")]
        public RectTransform mapCam;        // every region hangs off this — moving it IS the map camera
        public ChapterUnlockFx unlockFx;
        public GameObject[] chapterGates;   // [c] = the barrier shutting chapter c off (index 0 unused)
        public RectTransform[] gateLeft, gateRight, gateLocks;
        public float[] regionBaseY;         // each region's y, so the camera knows where to look

        [Header("Game complete")]
        public CanvasGroup finaleBanner;
        public RectTransform finaleBadge;   // the trophy — lands before the words
        public Text finaleTitle, finaleCount, finaleSub;

        [Header("Testing")]
        [Tooltip("When ON, every level is playable regardless of progress (no locks). Turn OFF to ship with gated progression.")]
        public bool unlockAllForTesting = true;

        public const int PerCategory = 10;

        const string LevelKey = "Parabox.Level";
        const int NewGameLevel = 0;
        static string BestKey(int level) => "Parabox.Best." + level;

        // A new application session always begins from Level 1. This deliberately preserves
        // scores/unlocks, so the level-select map can still be used to revisit unlocked levels.
        // Clearing the one-shot transition flags also prevents a previous interrupted session
        // from reopening the map or resuming a later-level camera transition on launch.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetFreshLaunchLevel()
        {
            PlayerPrefs.SetInt(LevelKey, NewGameLevel);
            PlayerPrefs.DeleteKey("Parabox.Seamless");
            PlayerPrefs.DeleteKey("Parabox.SeamlessOut");
            PlayerPrefs.DeleteKey("Parabox.OpenLevels");
            PlayerPrefs.DeleteKey("Parabox.JustBeat");
            PlayerPrefs.DeleteKey("Parabox.FlyIn");
            PlayerPrefs.DeleteKey("Parabox.FinalRun");
            PlayerPrefs.Save();
        }

        int screen;   // 0 = home, 1 = the level board
        bool transitioning;

        // Main-menu auto start. One countdown follows the player from the title to the level map;
        // at zero it always launches level 1, regardless of saved progress.
        const float MenuAutoStartSeconds = 45f;
        float _autoStartRemaining = MenuAutoStartSeconds;
        RectTransform _autoStartRoot;
        RectTransform _autoStartBar;
        Text _autoStartLabel;
        Color _autoStartAccent;
        int _autoStartShown = -1;
        int _autoStartLayoutScreen = -1;
        bool _autoStartTriggered;

        // live world-space board (rendered exactly like the game)
        LevelModel _model;
        readonly Dictionary<int, Transform> _roomRoots = new Dictionary<int, Transform>();
        readonly Dictionary<PEntity, EntityView> _views = new Dictionary<PEntity, EntityView>();
        Transform _boardRoot;
        int _startLevel;
        SpriteRenderer _boardGlow;
        float _glowBaseScale;
        RectTransform _logoEmblem;

        void OnEnable()
        {
            LuxoddGameService.ProgressLoaded += OnLuxoddProgressLoaded;
        }

        void OnDisable()
        {
            LuxoddGameService.ProgressLoaded -= OnLuxoddProgressLoaded;
        }

        void Start()
        {
            Sfx.Init();
            HideMusicCredit();
            BuildLogoEmblem();

            playButton.onClick.AddListener(BeginStart);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (levelsButton != null) levelsButton.onClick.AddListener(OpenLevelBoard);
            if (levelBoardBackButton != null) levelBoardBackButton.onClick.AddListener(CloseLevelBoard);

            for (int i = 0; i < levelButtons.Length; i++)
            {
                int index = i;
                if (levelButtons[i] != null) levelButtons[i].onClick.AddListener(() => TryStart(index));
            }

            RefreshStates();
            RefreshGates();
            BuildLiveBoard();   // render the real next level in world space (the O) + set the backdrop tier
            BuildAutoStartTimer();

            // arriving out of the finale: catch the board mid-move and carry it out to the logo
            if (PlayerPrefs.GetInt("Parabox.SeamlessOut", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.SeamlessOut");
                PlayerPrefs.Save();
                // NB: "Parabox.Seamless" is deliberately NOT deleted here. ScreenFade reads it in
                // its own Start(), and Start() order between components is undefined — deleting it
                // here could run first and leave ScreenFade fading up from black, which is exactly
                // the flash this whole ending exists to avoid. The coroutine clears it a frame later.
                StartCoroutine(RiseOutOfBoard());
                return;                                      // the outro owns the screen from here
            }

            StartCoroutine(OEntrance());   // the O spins into the logo on a normal open

            // arriving from a loss, or from a win — either way the map opens on the level in question
            if (PlayerPrefs.GetInt("Parabox.OpenLevels", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.OpenLevels");
                PlayerPrefs.Save();
                OpenLevelBoard();
                // ...and if it was a win, play the progression rather than just showing the result
                int beat = PlayerPrefs.GetInt("Parabox.JustBeat", -1);
                if (beat >= 0)
                {
                    PlayerPrefs.DeleteKey("Parabox.JustBeat");
                    PlayerPrefs.Save();
                    // finishing the last level of a chapter opens the next one — that is a bigger
                    // event than a level, and gets the bigger sequence
                    int ch = beat / PerCategory;
                    bool isLast = beat == levelButtons.Length - 1;
                    bool opensChapter = !isLast && (beat % PerCategory) == PerCategory - 1
                                        && ch + 1 < Mathf.Max(1, Mathf.CeilToInt(levelButtons.Length / (float)PerCategory));
                    if (isLast) StartCoroutine(PlayGameComplete());
                    else if (opensChapter) StartCoroutine(PlayChapterUnlock(ch));
                    else StartCoroutine(PlayProgress(beat));
                }
            }
        }

        // Hide the old track credit even when Unity has retained an older in-memory copy of
        // MainMenu.unity while scripts were edited externally.
        void HideMusicCredit()
        {
            Text[] labels = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                Text label = labels[i];
                if (label == null || label.gameObject.name != "Credit2") continue;
                label.gameObject.SetActive(false);
            }
        }

        // A separate high-contrast emblem gives the game a recognizable mark at icon size while
        // preserving the animated PARAB[board]X wordmark. It is created from Resources so older
        // scene copies and regenerated scenes receive the logo without an Inspector migration.
        void BuildLogoEmblem()
        {
            if (homeGroup == null || _logoEmblem != null) return;
            Sprite emblem = Resources.Load<Sprite>("Logo/ParaboxLogoEmblem");
            if (emblem == null)
            {
                Debug.LogWarning("Parabox logo emblem could not be loaded from Resources/Logo.");
                return;
            }

            var go = new GameObject("ParaboxLogoEmblem", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            _logoEmblem = (RectTransform)go.transform;
            _logoEmblem.SetParent(homeGroup.transform, false);
            _logoEmblem.anchorMin = _logoEmblem.anchorMax = new Vector2(0.5f, 0.5f);
            _logoEmblem.pivot = new Vector2(0.5f, 0.5f);
            _logoEmblem.anchoredPosition = new Vector2(-720f, 205f);
            _logoEmblem.sizeDelta = new Vector2(180f, 180f);

            var image = go.GetComponent<Image>();
            image.sprite = emblem;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.02f, 0.08f, 0.82f);
            shadow.effectDistance = new Vector2(0f, -8f);
            _logoEmblem.SetAsLastSibling();
        }

        // The O arrives: the whole slot scales up with an overshoot while the ring spins into place.
        // Scaling the SLOT (not just the ring) also carries the live board, because LateUpdate frames
        // the board into the slot's inner rect every frame — so the board grows with its own letter.
        // It never scales to 0: ComputeFarPose bails on a sub-pixel rect, which would snap the camera.
        System.Collections.IEnumerator OEntrance()
        {
            if (oSlot == null) yield break;
            const float dur = 0.75f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                const float c1 = 1.70158f, c3 = c1 + 1f;      // ease-out-back: overshoots, then settles
                float p = k - 1f;
                float back = 1f + c3 * p * p * p + c1 * p * p;
                float spin = 1f - Mathf.Pow(1f - k, 3f);      // ease-out-cubic for the rotation
                oSlot.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, back);
                if (oRing != null) oRing.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-180f, 0f, spin));
                yield return null;
            }
            oSlot.localScale = Vector3.one;
            if (oRing != null) oRing.localRotation = Quaternion.identity;
        }

        void Update()
        {
            // Keep focus on the screen that is actually visible.  A scene change can leave the
            // hidden home PLAY button selected for a frame; submitting that stale selection was
            // the path that reopened level 1 from the level-2 map.
            if (EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected == null || (screen == 1 && !IsLevelBoardSelection(selected)))
                    ReselectCurrentScreen();
            }

            TickAutoStartTimer();

            if (HandleArcadeMenuInput()) return;

            var kb = Keyboard.current;
            if (kb == null || transitioning) return;
            if (kb.mKey.wasPressedThisFrame) Sfx.ToggleMute();
            if (kb.deleteKey.wasPressedThisFrame) ResetProgress();
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (screen == 1) CloseLevelBoard();
                else OpenLevelBoard();   // home: Esc opens the level board (the "Menu" prompt)
            }
        }

        void OnLuxoddProgressLoaded()
        {
            if (levelButtons == null || levelButtons.Length == 0) return;
            RefreshStates();
            RefreshGates();
            // Cloud progress may unlock later levels, but the title-screen game entry remains a
            // new run from Level 1. Explicit level-map selections are handled separately.
            int desiredLevel = NewGameLevel;
            // Keep the title's live-board preview aligned with the Level-1 start destination.
            if (!transitioning && _boardRoot != null && desiredLevel != _startLevel)
            {
                Destroy(_boardRoot.gameObject);
                _boardRoot = null;
                _model = null;
                _ambience = null;
                _boardGlow = null;
                _roomRoots.Clear();
                _views.Clear();
                BuildLiveBoard();
                ShowWorldBoard(screen == 0);
            }
            else if (_boardRoot == null)
            {
                _startLevel = desiredLevel;
            }
            if (!transitioning) ReselectCurrentScreen();
        }

        // Luxodd does not inject cabinet controls into Unity's EventSystem, so menu navigation is
        // explicit: stick changes focus, Black activates it, Yellow opens the map and White goes
        // back. This keeps the same UI usable with mouse/keyboard while making the arcade build
        // fully operable without either one.
        bool HandleArcadeMenuInput()
        {
            var arcade = LuxoddArcadeAdapter.Instance;
            if (arcade == null || transitioning) return false;

            if (arcade.MuteDown)
            {
                Sfx.ToggleMute();
                return true;
            }
            if (arcade.BackDown)
            {
                if (screen == 1) CloseLevelBoard();
                return true;
            }
            if (arcade.LevelsDown)
            {
                if (screen == 0) OpenLevelBoard();
                else CloseLevelBoard();
                return true;
            }
            if (arcade.MovePulse && arcade.Direction != Vector2Int.zero)
            {
                MoveSelection(arcade.Direction);
                return true;
            }
            if (arcade.ConfirmDown)
            {
                var selected = EventSystem.current != null
                    ? EventSystem.current.currentSelectedGameObject : null;
                var button = selected != null ? selected.GetComponent<Button>() : null;
                if (button != null && button.interactable && button.gameObject.activeInHierarchy)
                    button.onClick.Invoke();
                else
                    ReselectCurrentScreen();
                return true;
            }
            return false;
        }

        void MoveSelection(Vector2Int direction)
        {
            if (EventSystem.current == null) return;
            GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
            Selectable selected = selectedObject != null ? selectedObject.GetComponent<Selectable>() : null;
            Selectable next = null;

            if (selected != null)
            {
                if (direction.x < 0) next = selected.FindSelectableOnLeft();
                else if (direction.x > 0) next = selected.FindSelectableOnRight();
                else if (direction.y > 0) next = selected.FindSelectableOnUp();
                else if (direction.y < 0) next = selected.FindSelectableOnDown();
            }

            if (next == null || !next.IsInteractable() || !next.gameObject.activeInHierarchy)
                next = FindNearestSelectable(selected, direction);

            if (next != null)
            {
                Select(next);
                Sfx.Hover();
            }
        }

        Selectable FindNearestSelectable(Selectable current, Vector2Int direction)
        {
            var candidates = new List<Selectable>();
            if (screen == 0)
            {
                if (playButton != null) candidates.Add(playButton);
                if (levelsButton != null) candidates.Add(levelsButton);
                if (quitButton != null) candidates.Add(quitButton);
            }
            else
            {
                if (levelButtons != null)
                    for (int i = 0; i < levelButtons.Length; i++)
                        if (levelButtons[i] != null) candidates.Add(levelButtons[i]);
                if (levelBoardBackButton != null) candidates.Add(levelBoardBackButton);
            }

            if (current == null)
            {
                ReselectCurrentScreen();
                return null;
            }

            Vector2 origin = current.transform.position;
            Vector2 wanted = direction;
            Selectable best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Selectable candidate = candidates[i];
                if (candidate == null || candidate == current || !candidate.IsInteractable()
                    || !candidate.gameObject.activeInHierarchy) continue;

                Vector2 delta = (Vector2)candidate.transform.position - origin;
                float forward = Vector2.Dot(delta, wanted);
                if (forward <= 0.01f) continue;
                float sideways = Mathf.Abs(delta.x * wanted.y - delta.y * wanted.x);
                float score = forward + sideways * 2.5f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = candidate;
            }
            return best;
        }

        // ---------------------------------------------------------- main-menu 45-second auto start
        void BuildAutoStartTimer()
        {
            if (homeGroup == null || _autoStartRoot != null) return;

            _autoStartAccent = _theme != null ? _theme.frame : new Color(0.44f, 0.90f, 0.95f, 1f);
            Color dark = _theme != null ? Color.Lerp(_theme.gutter, Color.black, 0.24f)
                                        : new Color(0.025f, 0.09f, 0.15f, 1f);
            dark.a = 0.94f;

            var root = new GameObject("AutoStartTimer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _autoStartRoot = (RectTransform)root.transform;
            // Parent to the shared Canvas root, not HomeRoot: the same timer remains visible on the
            // level map. LayoutAutoStartTimer makes the map version smaller.
            _autoStartRoot.SetParent(homeGroup.transform.parent, false);
            _autoStartRoot.anchorMin = _autoStartRoot.anchorMax = new Vector2(1f, 1f);
            _autoStartRoot.pivot = new Vector2(1f, 1f);

            var panel = root.GetComponent<Image>();
            panel.raycastTarget = false;
            panel.color = dark;
            if (playButton != null && playButton.image != null && playButton.image.sprite != null)
            {
                panel.sprite = playButton.image.sprite;
                panel.type = Image.Type.Sliced;
            }

            var shadow = root.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0.02f, 0.05f, 0.65f);
            shadow.effectDistance = new Vector2(0f, -5f);

            var outline = root.AddComponent<Outline>();
            Color edge = _autoStartAccent; edge.a = 0.72f;
            outline.effectColor = edge;
            outline.effectDistance = new Vector2(2f, -2f);

            Font font = null;
            if (playButton != null)
            {
                var playLabel = playButton.GetComponentInChildren<Text>(true);
                if (playLabel != null) font = playLabel.font;
            }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _autoStartLabel = MakeTimerText("Label", root.transform, font, 21, FontStyle.Bold,
                Vector2.zero, Vector2.one, new Vector2(14f, 7f), new Vector2(-14f, -3f));
            _autoStartLabel.text = "AUTO START IN 45 SECONDS";
            _autoStartLabel.color = Color.Lerp(_autoStartAccent, Color.white, 0.42f);

            var bar = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _autoStartBar = (RectTransform)bar.transform;
            _autoStartBar.SetParent(root.transform, false);
            _autoStartBar.anchorMin = new Vector2(0.5f, 0f);
            _autoStartBar.anchorMax = new Vector2(0.5f, 0f);
            _autoStartBar.pivot = new Vector2(0.5f, 0f);
            var barImage = bar.GetComponent<Image>();
            barImage.raycastTarget = false;
            barImage.color = _autoStartAccent;

            _autoStartRoot.SetAsLastSibling();
            LayoutAutoStartTimer();
            PaintAutoStartTimer(Mathf.CeilToInt(_autoStartRemaining));
        }

        void LayoutAutoStartTimer()
        {
            if (_autoStartRoot == null || _autoStartLayoutScreen == screen) return;
            _autoStartLayoutScreen = screen;
            bool map = screen == 1;

            _autoStartRoot.anchoredPosition = map ? new Vector2(-34f, -26f) : new Vector2(-52f, -44f);
            _autoStartRoot.sizeDelta = map ? new Vector2(210f, 56f) : new Vector2(320f, 70f);

            if (_autoStartLabel != null)
            {
                _autoStartLabel.fontSize = map ? 14 : 21;
            }
            if (_autoStartBar != null)
            {
                _autoStartBar.anchoredPosition = new Vector2(0f, map ? 4f : 5f);
                _autoStartBar.sizeDelta = new Vector2(map ? 172f : 270f, map ? 3f : 4f);
            }
        }

        static Text MakeTimerText(string name, Transform parent, Font font, int size, FontStyle style,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        void TickAutoStartTimer()
        {
            if (_autoStartTriggered || _autoStartRoot == null || transitioning) return;
            LayoutAutoStartTimer();

            _autoStartRemaining = Mathf.Max(0f, _autoStartRemaining - Time.unscaledDeltaTime);
            int seconds = Mathf.CeilToInt(_autoStartRemaining);
            PaintAutoStartTimer(seconds);

            if (seconds <= 5 && _autoStartLabel != null)
            {
                float pulse = 1f + (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f)) * 0.08f;
                _autoStartLabel.rectTransform.localScale = Vector3.one * pulse;
            }
            else if (_autoStartLabel != null) _autoStartLabel.rectTransform.localScale = Vector3.one;

            if (_autoStartRemaining > 0f) return;
            _autoStartTriggered = true;
            transitioning = true;
            _autoStartRoot.gameObject.SetActive(false);
            StartCoroutine(DiveIntoBoard(NewGameLevel));
        }

        void PaintAutoStartTimer(int seconds)
        {
            if (_autoStartLabel == null || seconds == _autoStartShown) return;
            _autoStartShown = seconds;
            int shown = Mathf.Clamp(seconds, 0, 99);
            _autoStartLabel.text = $"AUTO START IN {shown} SECOND{(shown == 1 ? "" : "S")}";
        }

        // ---------------------------------------------------------- progression
        static bool Beaten(int i) => PlayerPrefs.HasKey(BestKey(i));
        bool Unlocked(int i) => unlockAllForTesting || i == 0 || Beaten(i - 1);

        int FirstUnbeaten()
        {
            for (int i = 0; i < levelButtons.Length; i++)
                if (!Beaten(i)) return i;
            return 0;
        }

        int CurrentLevel()
        {
            for (int i = 0; i < levelButtons.Length; i++)
                if (!Beaten(i)) return i;
            return levelButtons.Length;
        }

        // ---------------------------------------------------------- navigation
        void OpenLevelBoard()
        {
            Sfx.Ding();
            RefreshStates();
            screen = 1;
            RefreshGates();
            ResetMapCam();     // the map ALWAYS opens framed on the whole journey
            ShowHome(false);   // the map owns the screen — the title must not ghost through the scrim
            ShowWorldBoard(false);
            if (levelBoardScreen != null) levelBoardScreen.Show();
            SelectCurrentNode();
            // UIScreen activates/fades during this frame.  Re-assert focus on the next frame after
            // the EventSystem has discarded the hidden home button, otherwise Enter can submit the
            // old PLAY button and reopen level 1 even though the map is showing level 2.
            StartCoroutine(FocusCurrentNodeNextFrame());
        }

        System.Collections.IEnumerator FocusCurrentNodeNextFrame()
        {
            yield return null;
            if (screen == 1) SelectCurrentNode();
        }

        // The map camera home pose: whole route, dead centre, no zoom.
        void ResetMapCam()
        {
            if (mapCam == null) return;
            mapCam.anchoredPosition = Vector2.zero;
            mapCam.localScale = Vector3.one;
        }

        // Deadline for whichever map animation is allowed to own the camera right now.
        float _mapCeremonyUntil;

        void ClaimMapCam(float seconds) => _mapCeremonyUntil = Time.unscaledTime + seconds;

        // Self-healing map camera.
        //
        // Three sequences drive this rect — the chapter-unlock ceremony (1.28x), the final approach
        // to level 50 (2.1x) and the game-complete lap (2.1x) — and each is *supposed* to hand it
        // back at the end. Resetting on open wasn't enough: measured on a real screenshot, the map
        // sat at scale 1.29, offset (0,-444), which is exactly step 1 of the chapter-unlock
        // ceremony (-Focus(344) * 1.28). The ceremony re-zoomed AFTER the open-reset and then died
        // partway — silently, no exception in the log — so it never reached the pull-back.
        //
        // Rather than keep hunting for which step dies, this makes the failure impossible to see:
        // each sequence CLAIMS the camera for a bounded window, and once that window lapses the map
        // takes its camera back. Any animation that dies, throws, or is interrupted self-corrects.
        void HealMapCam()
        {
            if (screen != 1 || mapCam == null) return;              // only while the map is up
            if (Time.unscaledTime < _mapCeremonyUntil) return;      // an animation legitimately owns it
            if (mapCam.anchoredPosition == Vector2.zero && Mathf.Approximately(mapCam.localScale.x, 1f)) return;
            ResetMapCam();
        }

        void CloseLevelBoard()
        {
            screen = 0;
            // NOT StopAllCoroutines() here: that would also kill DiveIntoBoard and RiseOutOfBoard,
            // which load scenes. A ceremony left running is harmless — it ends on MoveCam(..., 1f)
            // anyway, and OpenLevelBoard resets the pose next time regardless.
            ResetMapCam();
            if (levelBoardScreen != null) levelBoardScreen.Hide();
            ShowWorldBoard(true);
            ShowHome(true);
            SelectHome();
        }

        // The second half of the ending — the dive's mirror image.
        //
        // The game pulled the camera out of the board you just solved and stopped. We pick up at
        // that exact pose, on that exact board, so the scene change lands on a frame where nothing
        // has moved: invisible, the same trick the dive uses inbound. Then one continuous move
        // carries the board out to where the logo's O sits, and the title assembles around it.
        //
        // The board you finished the game on becomes the O in the title screen. That is the whole
        // effect, and there isn't a particle in it.
        System.Collections.IEnumerator RiseOutOfBoard()
        {
            transitioning = true;                            // LateUpdate must not fight us for the camera
            ShowHome(false);
            if (backgroundGroup != null) backgroundGroup.alpha = 0f;
            if (_ambience != null) _ambience.weight = 0f;    // the board is still settling; no idle float yet

            Vector3 fromPos = new Vector3(PlayerPrefs.GetFloat("Parabox.OutX", 0f),
                                         PlayerPrefs.GetFloat("Parabox.OutY", 0f), -10f);
            float fromSize = PlayerPrefs.GetFloat("Parabox.OutSize", 6f);
            if (menuCam != null)
            {
                menuCam.transform.position = fromPos;        // frame 1 == the game's last frame
                menuCam.orthographicSize = fromSize;
            }
            yield return null;                               // let the canvas lay out before we measure the O
            // safe now: every Start() has run, so ScreenFade has already seen the flag and stood down
            PlayerPrefs.DeleteKey("Parabox.Seamless");
            PlayerPrefs.Save();

            float t = 0f;
            while (t < 0.7f) { t += Time.unscaledDeltaTime; yield return null; }   // a beat, still moving out

            // one continuous move: the board travels from where you left it into the logo
            float dur = 2.8f;
            t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = Mathf.SmoothStep(0f, 1f, k);
                ComputeFarPose(out var toPos, out var toSize);   // recomputed live: the O's rect is authoritative
                if (menuCam != null)
                {
                    menuCam.transform.position = Vector3.Lerp(fromPos, toPos, e);
                    menuCam.orthographicSize = Mathf.Lerp(fromSize, toSize, e);
                }
                // the title assembles around the board as it arrives, rather than being there first
                if (homeGroup != null) homeGroup.alpha = Mathf.Clamp01((k - 0.45f) / 0.45f);
                if (backgroundGroup != null) backgroundGroup.alpha = Mathf.Clamp01((k - 0.45f) / 0.45f);
                yield return null;
            }

            ShowHome(true);
            if (backgroundGroup != null) backgroundGroup.alpha = 1f;
            if (_ambience != null) _ambience.weight = 1f;
            transitioning = false;                           // hand the camera back to the idle drift

            float w = 0f;
            while (w < 0.9f) { w += Time.unscaledDeltaTime; yield return null; }   // let it breathe

            // ...and only now, the journey
            PlayerPrefs.DeleteKey("Parabox.OpenLevels");
            PlayerPrefs.DeleteKey("Parabox.JustBeat");
            PlayerPrefs.Save();
            OpenLevelBoard();
            yield return PlayGameComplete();
        }

        // Show what changed: the cleared node lands, the road onward lights dot by dot, the next
        // level wakes. RefreshStates has already painted the END state, so this rewinds the parts
        // that are about to animate — otherwise there'd be nothing to watch.
        System.Collections.IEnumerator PlayProgress(int beaten)
        {
            if (progressFx == null || beaten < 0 || beaten >= levelButtons.Length) yield break;

            var beatenRT = levelButtons[beaten] != null ? (RectTransform)levelButtons[beaten].transform : null;
            var check = (levelChecks != null && beaten < levelChecks.Length) ? levelChecks[beaten] : null;

            int next = beaten + 1;
            RectTransform nextRT = null;
            GameObject nextRing = null;
            if (next < levelButtons.Length)
            {
                if (levelButtons[next] != null) nextRT = (RectTransform)levelButtons[next].transform;
                if (levelHighlights != null && next < levelHighlights.Length) nextRing = levelHighlights[next];
            }

            // the leg from the cleared level to the next one
            var road = new List<Image>();
            Color lit = Color.white;
            if (pathDots != null && pathDotsPerLink > 0 && boardThemes != null && boardThemes.Length > 0)
            {
                var th = boardThemes[Mathf.Clamp(beaten / PerCategory, 0, boardThemes.Length - 1)];
                lit = WithA(th.frame, 0.85f);
                for (int d = 0; d < pathDotsPerLink; d++)
                {
                    int idx = beaten * pathDotsPerLink + d;
                    if (idx < pathDots.Length && pathDots[idx] != null)
                    {
                        pathDots[idx].color = WithA(th.frame, 0.12f);   // rewind: dark, so it can light
                        road.Add(pathDots[idx]);
                    }
                }
            }
            if (check != null) check.SetActive(false);
            if (nextRing != null) nextRing.SetActive(false);

            yield return progressFx.Play(beatenRT, check, road, lit, nextRT, nextRing);

            // The progress animation ends on the newly unlocked node, so keyboard/controller
            // focus and the home PLAY fallback must end there too.
            RefreshStates();
            if (next < levelButtons.Length && levelButtons[next] != null && levelButtons[next].interactable)
            {
                _startLevel = next;
                Select(levelButtons[next]);
            }
            else SelectCurrentNode();
        }

        // A gate stays shut until the chapter behind it is finished, so the barrier is visible
        // (and meaningful) for the whole chapter before it up and breaks.
        void RefreshGates()
        {
            if (chapterGates == null) return;
            for (int c = 1; c < chapterGates.Length; c++)
            {
                if (chapterGates[c] == null) continue;
                bool open = unlockAllForTesting || Beaten(c * PerCategory - 1);
                chapterGates[c].SetActive(!open);
            }
        }

        // The chapter ceremony. `ch` is the chapter just COMPLETED; ch+1 is the one being opened.
        System.Collections.IEnumerator PlayChapterUnlock(int ch)
        {
            int next = ch + 1;
            if (unlockFx == null || chapterGates == null || next >= chapterGates.Length) { yield break; }
            ClaimMapCam(12f);   // the ceremony runs ~5s; past this the map takes its camera back

            var th = boardThemes[Mathf.Clamp(ch, 0, boardThemes.Length - 1)];
            var thNext = boardThemes[Mathf.Clamp(next, 0, boardThemes.Length - 1)];

            // the finished chapter's nodes, in route order — the wave runs along them
            var doneNodes = new List<RectTransform>();
            var doneFills = new List<Image>();
            for (int k = 0; k < PerCategory; k++)
            {
                int i = ch * PerCategory + k;
                if (i >= levelButtons.Length || levelButtons[i] == null) continue;
                doneNodes.Add((RectTransform)levelButtons[i].transform);
                doneFills.Add(levelFills != null && i < levelFills.Length ? levelFills[i] : null);
            }

            // the new chapter's first stretch of road
            var newRoad = new List<Image>();
            int link = next * PerCategory;           // road leaving the new chapter's first node
            if (pathDots != null)
                for (int d = 0; d < pathDotsPerLink; d++)
                {
                    int idx = link * pathDotsPerLink + d;
                    if (idx < pathDots.Length && pathDots[idx] != null) newRoad.Add(pathDots[idx]);
                }

            int firstIdx = next * PerCategory;
            RectTransform firstNode = (firstIdx < levelButtons.Length && levelButtons[firstIdx] != null)
                ? (RectTransform)levelButtons[firstIdx].transform : null;
            GameObject firstRing = (levelHighlights != null && firstIdx < levelHighlights.Length)
                ? levelHighlights[firstIdx] : null;

            // the gate must be SHUT when the sequence starts, whatever RefreshStates decided
            chapterGates[next].SetActive(true);

            float doneY = (regionBaseY != null && ch < regionBaseY.Length) ? regionBaseY[ch] : 0f;
            float newY  = (regionBaseY != null && next < regionBaseY.Length) ? regionBaseY[next] : 0f;

            yield return unlockFx.Play(mapCam, doneNodes, doneFills, Lighten(th.frame, 0.4f),
                (RectTransform)chapterGates[next].transform,
                gateLeft != null && next < gateLeft.Length ? gateLeft[next] : null,
                gateRight != null && next < gateRight.Length ? gateRight[next] : null,
                gateLocks != null && next < gateLocks.Length ? gateLocks[next] : null,
                null, newRoad, WithA(thNext.frame, 0.85f), firstNode, firstRing,
                doneY, newY);
        }

        // Finishing the game: the map replays your whole journey, then says so.
        System.Collections.IEnumerator PlayGameComplete()
        {
            if (unlockFx == null) yield break;
            ClaimMapCam(20f);   // the victory lap runs ~8s

            var nodes = new List<RectTransform>();
            var fills = new List<Image>();
            for (int i = 0; i < levelButtons.Length; i++)
            {
                nodes.Add(levelButtons[i] != null ? (RectTransform)levelButtons[i].transform : null);
                fills.Add(levelFills != null && i < levelFills.Length ? levelFills[i] : null);
            }
            var cols = new List<Color>();
            for (int c = 0; c < (boardThemes != null ? boardThemes.Length : 0); c++) cols.Add(boardThemes[c].frame);

            // rewind the route so the relight has something to draw over
            if (pathDots != null)
                for (int i = 0; i < pathDots.Length; i++)
                    if (pathDots[i] != null)
                    {
                        var th = boardThemes[Mathf.Clamp(i / (pathDotsPerLink * PerCategory), 0, boardThemes.Length - 1)];
                        pathDots[i].color = WithA(th.frame, 0.12f);
                    }

            if (finaleTitle != null) finaleTitle.text = "GAME COMPLETE";

            int total = levelButtons.Length;
            int perfect = 0, totalMoves = 0;
            for (int i = 0; i < total; i++)
            {
                if (!Beaten(i)) continue;
                int best = PlayerPrefs.GetInt(BestKey(i), 0);
                totalMoves += best;
                if (Par(i) > 0 && best <= Par(i)) perfect++;
            }
            if (finaleCount != null) finaleCount.text = $"{total} / {total} LEVELS COMPLETE";
            if (finaleSub != null)
                finaleSub.text = $"{perfect} solved in par     ·     {totalMoves} moves across the whole game";

            var last = levelButtons.Length - 1;
            yield return unlockFx.PlayGameComplete(
                mapCam,
                levelButtons[last] != null ? (RectTransform)levelButtons[last].transform : null,
                regionBaseY != null && regionBaseY.Length > 0 ? regionBaseY[regionBaseY.Length - 1] : 0f,
                pathDots, nodes, fills, cols, PerCategory, finaleBanner, finaleBadge);
        }

        // The live world board — the one that becomes the O in the logo — sits in WORLD space,
        // behind the whole canvas. The map's scrim is 90% opaque, so the board was ghosting
        // through it: you could see a grid and a box floating behind the level nodes. Dimming the
        // scrim further would just hide it less; the board simply has no business being on screen
        // while the map is up, so it leaves.
        void ShowWorldBoard(bool on)
        {
            if (_boardRoot != null) _boardRoot.gameObject.SetActive(on);
        }

        void ShowHome(bool on)
        {
            if (homeGroup == null) return;
            homeGroup.alpha = on ? 1f : 0f;
            homeGroup.interactable = on;
            homeGroup.blocksRaycasts = on;
        }

        // --- keyboard / controller: keep a button focused on each screen so arrow keys can navigate ---
        void Select(Selectable s)
        {
            if (s != null && s.gameObject.activeInHierarchy && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        void SelectHome() => Select(playButton);

        bool IsLevelBoardSelection(GameObject selected)
        {
            if (selected == null) return false;
            if (levelBoardBackButton != null && selected == levelBoardBackButton.gameObject) return true;
            if (levelButtons == null) return false;
            for (int i = 0; i < levelButtons.Length; i++)
                if (levelButtons[i] != null && selected == levelButtons[i].gameObject) return true;
            return false;
        }

        // Land on the level you'd actually play next, so the board opens where your eye already is.
        void SelectCurrentNode()
        {
            int cur = CurrentLevel();
            if (cur < levelButtons.Length && levelButtons[cur] != null && levelButtons[cur].interactable)
            {
                _startLevel = cur;
                Select(levelButtons[cur]);
                return;
            }
            for (int i = 0; i < levelButtons.Length; i++)
                if (levelButtons[i] != null && levelButtons[i].interactable)
                {
                    _startLevel = i;
                    Select(levelButtons[i]);
                    return;
                }
            Select(levelBoardBackButton);
        }

        void ReselectCurrentScreen()
        {
            if (screen == 0) SelectHome();
            else SelectCurrentNode();
        }

        void TryStart(int index)
        {
            if (transitioning) return;
            if (!Unlocked(index)) { Sfx.Blocked(); return; }
            // the last level is approached, not just opened
            if (index == levelButtons.Length - 1 && mapCam != null && levelButtons[index] != null)
            { StartCoroutine(FinalApproach(index)); return; }
            transitioning = true;   // guard against UI Submit and key polling both firing this frame
            StartLevel(index);
        }

        // The walk up to the final challenge. Every other level opens the instant you click it;
        // this one makes you arrive. The map pushes in on the node until it fills the view and
        // holds there — a beat of nothing but the destination — before the level loads. The pause
        // IS the effect: anticipation is time, not particles.
        System.Collections.IEnumerator FinalApproach(int index)
        {
            transitioning = true;
            ClaimMapCam(6f);    // pushes in on node 50, then loads the level
            Sfx.Ding();

            var node = (RectTransform)levelButtons[index].transform;
            Vector2 focus = node.anchoredPosition;
            Vector2 fromPos = mapCam.anchoredPosition;
            float fromZ = mapCam.localScale.x;
            const float zoom = 2.1f, dur = 1.15f;

            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - Mathf.Pow(1f - k, 3f);          // ease-out: rushes in, settles
                float z = Mathf.Lerp(fromZ, zoom, e);
                mapCam.anchoredPosition = Vector2.Lerp(fromPos, -focus * zoom, e);
                mapCam.localScale = new Vector3(z, z, 1f);
                // everything else drains away, so only the destination is left
                if (levelBoardScreen != null)
                {
                    var cg = levelBoardScreen.GetComponent<CanvasGroup>();
                    if (cg != null) cg.alpha = 1f;
                }
                yield return null;
            }

            t = 0f;
            while (t < 0.45f) { t += Time.unscaledDeltaTime; yield return null; }   // the held beat

            PlayerPrefs.SetInt("Parabox.FinalRun", 1);   // the level itself opens differently
            StartLevel(index);
        }

        // ---------------------------------------------------------- state paint
        static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // Lighten exists in GameManager, BoardRenderer and the wizard — but private to each, so
        // none of them is reachable from here. Local copy rather than a fourth cross-class hop.
        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);

        // Every node state is drawn from that chapter's REAL LevelTheme, so the map speaks the same
        // colour language as the boards:
        //   locked    = the board's dark gutter (a cell that isn't lit yet)
        //   playable  = a live floor cell
        //   beaten    = the CHAPTER'S FRAME colour — its identity
        //   current   = box orange, the only orange on the map: "you are here"
        //
        // Beaten used to be box-orange in every chapter, on the reasoning that it looked like a box
        // resting on its goal. But the box is orange in all five chapters (their hues span 12
        // degrees), so a map with everything beaten was a field of identical orange squares and the
        // chapters had no identity at all. The tick badge already says "done"; the colour is better
        // spent saying WHERE you are. Orange now marks the one level you're on.
        void RefreshStates()
        {
            int total = levelButtons.Length;
            int current = CurrentLevel();
            bool haveThemes = boardThemes != null && boardThemes.Length > 0;

            for (int i = 0; i < total; i++)
            {
                bool beaten = Beaten(i);
                bool unlocked = Unlocked(i);
                bool isCurrent = unlocked && !beaten && i == current;

                if (levelButtons[i] != null) levelButtons[i].interactable = unlocked;

                if (levelChecks != null && i < levelChecks.Length && levelChecks[i] != null)
                    levelChecks[i].SetActive(beaten);
                if (levelLocks != null && i < levelLocks.Length && levelLocks[i] != null)
                    levelLocks[i].SetActive(!unlocked);
                if (levelHighlights != null && i < levelHighlights.Length && levelHighlights[i] != null)
                    levelHighlights[i].SetActive(isCurrent);

                if (!haveThemes) continue;
                var th = boardThemes[Mathf.Clamp(i / PerCategory, 0, boardThemes.Length - 1)];
                Color cell = (th.roomColors != null && th.roomColors.Length > 0) ? th.roomColors[0] : th.frame;

                Color face = !unlocked ? Color.Lerp(th.gutter, Color.black, 0.28f)
                           : isCurrent ? th.box       // the only orange on the map
                           : beaten    ? th.frame     // this chapter's identity
                                       : cell;

                if (levelFills != null && i < levelFills.Length && levelFills[i] != null)
                    levelFills[i].color = face;

                // one stroke weight everywhere; only its brightness tracks the state. A beaten node
                // is already the frame colour, so its border lifts to white instead — otherwise the
                // stroke would vanish into the face.
                if (levelBorders != null && i < levelBorders.Length && levelBorders[i] != null)
                    levelBorders[i].color = !unlocked
                        ? WithA(Color.Lerp(th.gutter, Color.white, 0.14f), 1f)
                        : beaten    ? WithA(Lighten(th.frame, 0.55f), 0.95f)
                        : isCurrent ? WithA(th.frame, 0.95f)
                                    : WithA(th.frame, 0.5f);

                // the number only reads on a lit face — a locked node shows its glyph instead
                if (levelNumbers != null && i < levelNumbers.Length && levelNumbers[i] != null)
                {
                    levelNumbers[i].enabled = unlocked;
                    levelNumbers[i].color = th.wall;
                }

                // perfect = cleared at par. The par comes from the level prefab, so it can never
                // disagree with what the solver actually proved.
                bool perfect = beaten && Par(i) > 0 && PlayerPrefs.GetInt(BestKey(i), 9999) <= Par(i);
                if (levelStars != null && i < levelStars.Length && levelStars[i] != null)
                    levelStars[i].SetActive(perfect);

                // the leg of the route BEHIND this node lights once it's cleared — the trail you
                // have walked is lit, the road ahead is dim. The path IS the progress bar.
                if (pathDots != null && pathDotsPerLink > 0)
                    for (int d = 0; d < pathDotsPerLink; d++)
                    {
                        int idx = i * pathDotsPerLink + d;
                        if (idx < pathDots.Length && pathDots[idx] != null)
                            pathDots[idx].color = WithA(th.frame, beaten ? 0.85f : 0.12f);
                    }
            }

        }

        // The level's optimal move count, straight off its prefab.
        int Par(int i)
        {
            if (levelPrefabs == null || i < 0 || i >= levelPrefabs.Length || levelPrefabs[i] == null) return 0;
            var info = levelPrefabs[i].GetComponent<ParaboxLevel>();
            return info != null ? info.par : 0;
        }

        void ResetProgress()
        {
            for (int i = 0; i < levelButtons.Length; i++)
                PlayerPrefs.DeleteKey(BestKey(i));
            ScoreSystem.Reset(levelButtons.Length);
            // A fresh start has to include the first-run demonstration, or "reset" quietly means
            // "reset everything except the one thing only a new player sees".
            PlayerPrefs.DeleteKey(GameManager.TutorialKey);
            PlayerPrefs.Save();
            RefreshStates();
            RefreshGates();
            LuxoddGameService.SyncProgress();
        }

        // Render the title entry level as a world-space board (identical to gameplay) at world origin.
        LevelTheme _theme;

        void BuildLiveBoard()
        {
            if (levelPrefabs == null || levelPrefabs.Length == 0 || floorPrefab == null || boardThemes == null || boardThemes.Length == 0) return;
            // Arriving out of the finale, the board MUST be the one the game just pulled away from —
            // same level, same geometry, same world position — or the hand-off is a cut instead of
            // a continuation. Any other time the title represents a fresh run from Level 1.
            int want = PlayerPrefs.GetInt("Parabox.SeamlessOut", 0) == 1
                ? PlayerPrefs.GetInt("Parabox.OutLevel", 0)
                : NewGameLevel;
            _startLevel = Mathf.Clamp(want, 0, levelPrefabs.Length - 1);
            _theme = boardThemes[Mathf.Clamp(_startLevel / PerCategory, 0, boardThemes.Length - 1)];   // this level's chapter
            _model = LevelParser.Parse(levelPrefabs[_startLevel]);
            _boardRoot = BoardRenderer.Render(_model, BuildAssets(), _roomRoots, _views);
            if (backdrop != null) backdrop.Apply(_startLevel / PerCategory);   // atmosphere = the tier we'll load

            BuildAmbience();

            // soft cyan glow behind the board — it pulses on the menu (idle), hidden once the board fills the screen
            if (fxGlow != null)
            {
                var g = new GameObject("BoardIdleGlow");
                g.transform.SetParent(_boardRoot, false);
                g.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                _boardGlow = g.AddComponent<SpriteRenderer>();
                _boardGlow.sprite = fxGlow;
                _boardGlow.color = new Color(0.44f, 0.92f, 0.95f, 0.5f);
                _boardGlow.sortingOrder = -20;   // behind the board floor, in front of the backdrop
                float span = FramedRoom != null ? Mathf.Max(FramedRoom.width, FramedRoom.height) : 6f;
                _glowBaseScale = span * 1.7f;
                g.transform.localScale = Vector3.one * _glowBaseScale;
            }
        }

        MenuAmbience _ambience;

        // Idle life on the live board: the boxes float, and a light crawls across it. Both are
        // scaled by MenuAmbience.weight, which the dive drives to 0 — see DiveIntoBoard.
        void BuildAmbience()
        {
            if (_boardRoot == null || _model == null) return;

            var boxes = new List<Transform>();
            foreach (var e in _model.entities)
                if (!e.isPlayer && _views.TryGetValue(e, out var v) && v != null)
                    boxes.Add(v.transform);

            _ambience = _boardRoot.gameObject.AddComponent<MenuAmbience>();
            _ambience.pieces = boxes.ToArray();

            if (fxGlow != null && FramedRoom != null)
            {
                var sw = new GameObject("LightSweep");
                sw.transform.SetParent(_boardRoot, false);
                sw.transform.localPosition = new Vector3(0f, 0f, 0.2f);
                // a tall narrow band: it reads as light crossing the board, not a blob drifting over it
                sw.transform.localScale = new Vector3(FramedRoom.width * 0.35f, FramedRoom.height * 1.6f, 1f);
                var sr = sw.AddComponent<SpriteRenderer>();
                sr.sprite = fxGlow;
                sr.color = new Color(1f, 1f, 1f, 0f);
                sr.sortingOrder = 30;              // over the floor, under nothing that matters
                _ambience.sweep = sr;
                _ambience.sweepSpan = FramedRoom.width * 0.75f;
            }
        }

        BoardAssets BuildAssets() => new BoardAssets
        {
            floorPrefab = floorPrefab, gridPrefab = gridPrefab, wallPrefab = wallPrefab,
            boxGoalPrefab = boxGoalPrefab, playerGoalPrefab = playerGoalPrefab,
            boxPrefab = boxPrefab, metaBoxPrefab = metaBoxPrefab, playerPrefab = playerPrefab,
            ringSprite = fxRing, glowSprite = fxGlow, vignetteSprite = fxVignette, cellSprite = fxCell,
            roomColors = _theme.roomColors, boxColor = _theme.box, playerColor = _theme.player,
            wallColor = _theme.wall, gridColor = _theme.grid, frameColor = _theme.frame, gutterColor = _theme.gutter,
            floorVignette = _theme.floorVignette, pieceGlow = _theme.pieceGlow, cellLift = _theme.cellLift,
            chapter = _startLevel / PerCategory,
            floorTex = _theme.floorTex, floorTexTint = _theme.floorTexTint,
            hideFrame = true,   // the logo's O ring is the outline here — see BoardAssets.hideFrame
        };

        Transform FramedRoot => (_model != null && _roomRoots.TryGetValue(_model.player.roomId, out var r)) ? r : null;
        PRoom FramedRoom => (_model != null && _model.rooms.TryGetValue(_model.player.roomId, out var rm)) ? rm : null;

        bool _bdApplied;

        // Keep the world board glued inside the O whenever we're idle on the menu (and across resizes/WebGL).
        void LateUpdate()
        {
            // set the backdrop tier once, after all Start()s — so CameraBackdrop.Start's stale value can't win
            if (!_bdApplied && backdrop != null && _model != null) { _bdApplied = true; backdrop.Apply(_startLevel / PerCategory); }

            HealMapCam();

            if (transitioning || menuCam == null || _model == null) return;
            ComputeFarPose(out var pos, out var size);

            // gentle idle — camera-based float + breathe (never touches the board, so the dive stays pixel-exact)
            float tt = Time.unscaledTime;
            Vector3 rest = pos;
            pos.x += Mathf.Sin(tt * 0.55f) * size * 0.012f;
            pos.y += Mathf.Sin(tt * 0.80f + 1.3f) * size * 0.018f;
            size *= 1f + Mathf.Sin(tt * 0.70f) * 0.010f;
            menuCam.transform.position = pos;
            menuCam.orthographicSize = size;

            // Parallax. The backdrop is a CHILD of the camera, so without this it travels 1:1 and
            // the background is effectively nailed to the screen — every layer moving together is
            // exactly what reads as flat. Pushing it back against the drift makes it lag, so the
            // board and the background move at different rates and the screen gains depth.
            if (backdrop != null)
            {
                Vector3 drift = pos - rest;
                var bp = backdrop.transform.localPosition;
                backdrop.transform.localPosition = new Vector3(-drift.x * 0.55f, -drift.y * 0.55f, bp.z);
            }

            // soft glow pulse + subtle border shimmer
            if (_boardGlow != null)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(tt * 1.2f);
                var c = _boardGlow.color; c.a = Mathf.Lerp(0.32f, 0.62f, p); _boardGlow.color = c;
                _boardGlow.transform.localScale = Vector3.one * _glowBaseScale * (1f + 0.05f * Mathf.Sin(tt * 1.0f));
            }
        }

        // Camera pose that fits the whole board inside the O's on-screen rect (board sits "in the O").
        //
        // This used to fit the room's HEIGHT and nothing else, which was wrong twice over:
        //
        //   * width was never checked, so a 7x5 room drew 1.4x wider than tall and spilled out
        //     sideways past the letters — the O read as a wide rectangle;
        //   * it measured the ROOM, but BoardRenderer draws a bezel and frame AROUND it
        //     (rim = 0.35 + chapter*0.05, bezel = rim + 0.07 on every side), which is another
        //     ~1.2 units — so the thing on screen was always bigger than the thing measured.
        //
        // Measured, a chapter-4 board rendered 163x123px inside a 100x100 slot. Now both axes are
        // fitted, against the board's REAL drawn extent, and the tighter of the two wins.
        void ComputeFarPose(out Vector3 pos, out float size)
        {
            pos = new Vector3(0f, 0f, -10f); size = 6f;
            var fr = FramedRoom; var root = FramedRoot;
            if (fr == null || root == null || boardInner == null || menuCam == null) return;

            var cr = new Vector3[4];
            boardInner.GetWorldCorners(cr);              // overlay canvas → world corners ARE screen pixels
            Vector2 centerPx = (Vector2)(cr[0] + cr[2]) * 0.5f;
            float widthPx = cr[2].x - cr[0].x;
            float heightPx = cr[1].y - cr[0].y;
            if (heightPx < 1f || widthPx < 1f) return;

            // the board's real drawn extent: the room plus the bezel BoardRenderer paints around it
            int ch = Mathf.Clamp(_startLevel / PerCategory, 0, 4);
            float pad = (0.35f + ch * 0.05f + 0.07f) * 2f;
            float boardW = fr.width + pad;
            float boardH = fr.height + pad;

            float sw = Screen.width, sh = Mathf.Max(1f, Screen.height);
            float aspect = sw / sh;
            Vector3 rc = root.position;

            // orthographicSize is HALF the view height in world units, so pixels-per-unit is
            // sh / (2*size) on both axes. Solve each axis for size and take the larger — the
            // larger size is the wider view, i.e. the one that keeps the board inside the slot.
            float sizeForH = boardH * sh / (2f * heightPx);
            float sizeForW = boardW * sh / (2f * widthPx);
            size = Mathf.Max(sizeForH, sizeForW);

            float vpx = centerPx.x / sw - 0.5f;
            float vpy = centerPx.y / sh - 0.5f;
            pos = new Vector3(rc.x - vpx * 2f * size * aspect, rc.y - vpy * 2f * size, rc.z - 10f);
        }

        // The EXACT gameplay camera pose (same formula the game's CameraFollow uses) for this level.
        void ComputeGameplayPose(out Vector3 pos, out float size)
        {
            pos = new Vector3(0f, 0f, -10f); size = 6f;
            var fr = FramedRoom; var root = FramedRoot;
            if (fr == null || root == null || menuCam == null) return;
            CameraFraming.Compute(root.position, root.lossyScale.x, fr.width, fr.height,
                menuCam.aspect, 1.05f, 0.16f, 0.20f, out pos, out size);
        }

        void BeginStart()
        {
            if (transitioning) return;
            transitioning = true;
            StartCoroutine(DiveIntoBoard(NewGameLevel));
        }

        // Physically fly the menu camera INTO the world board — from the O framing to the EXACT gameplay
        // framing — fading the UI out, then hand off to the game at that identical frame (seamless).
        System.Collections.IEnumerator DiveIntoBoard(int level)
        {
            Sfx.Ding();
            if (homeGroup != null) { homeGroup.interactable = false; homeGroup.blocksRaycasts = false; }
            if (menuCam == null || _model == null) { LoadGame(level, true); yield break; }

            // Level 1 opens with the tutorial cinematic, which covers the screen the INSTANT the game
            // loads. Flying the board in first would just show a board that's about to be hidden — so
            // for level 1 skip the dive entirely and fade straight out, and the tutorial appears
            // directly instead of "board flies in → gets covered → tutorial".
            if (level == 0)
            {
                float ft = 0f, fdur = 0.3f;
                while (ft < fdur)
                {
                    ft += Time.unscaledDeltaTime;
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ft / fdur));
                    if (homeGroup != null) homeGroup.alpha = a;
                    if (backgroundGroup != null) backgroundGroup.alpha = a;
                    yield return null;
                }
                if (homeGroup != null) homeGroup.alpha = 0f;
                if (backgroundGroup != null) backgroundGroup.alpha = 0f;
                LoadGame(level, true);
                yield break;
            }

            Vector3 fromPos = menuCam.transform.position;   // continue from wherever the idle drift left the camera
            float fromSize = menuCam.orthographicSize;
            ComputeGameplayPose(out var toPos, out var toSize);
            float ambFrom = _ambience != null ? _ambience.weight : 0f;
            Vector3 bpFrom = backdrop != null ? backdrop.transform.localPosition : Vector3.zero;

            float t = 0f, dur = 1.15f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                float e = 1f - Mathf.Pow(1f - k, 3f);   // ease-out cubic
                menuCam.transform.position = Vector3.Lerp(fromPos, toPos, e);
                menuCam.orthographicSize = Mathf.Lerp(fromSize, toSize, e);
                // settle the idle out FAST and early: by the hand-off every piece must sit exactly
                // on its cell, or the game's first frame shows them all snapping into place
                if (_ambience != null) _ambience.weight = Mathf.Lerp(ambFrom, 0f, Mathf.Clamp01(k / 0.25f));
                // same for the parallax: LateUpdate stops running once `transitioning` is set, so
                // the backdrop would freeze at whatever offset it happened to hold and jump when
                // the game's own backdrop takes over at zero
                if (backdrop != null)
                {
                    var bp = backdrop.transform.localPosition;
                    backdrop.transform.localPosition = Vector3.Lerp(
                        new Vector3(bpFrom.x, bpFrom.y, bp.z), new Vector3(0f, 0f, bp.z),
                        Mathf.Clamp01(k / 0.25f));
                }
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.5f));
                if (homeGroup != null) homeGroup.alpha = fade;
                if (backgroundGroup != null) backgroundGroup.alpha = fade;   // squares must not survive into the game
                yield return null;
            }
            if (_ambience != null) _ambience.weight = 0f;   // exact, not nearly
            if (backdrop != null)
            {
                var bp = backdrop.transform.localPosition;
                backdrop.transform.localPosition = new Vector3(0f, 0f, bp.z);
            }
            menuCam.transform.position = toPos;      // land on the EXACT gameplay pose
            menuCam.orthographicSize = toSize;
            if (homeGroup != null) homeGroup.alpha = 0f;
            if (backgroundGroup != null) backgroundGroup.alpha = 0f;
            LoadGame(level, true);
        }

        void StartLevel(int index) { Sfx.Ding(); LoadGame(index, false); }

        void LoadGame(int index, bool seamless)
        {
            PlayerPrefs.SetInt(LevelKey, index);
            if (seamless) PlayerPrefs.SetInt("Parabox.Seamless", 1);   // menu already showed this board → no fade/fly-in
            else PlayerPrefs.SetInt("Parabox.FlyIn", 1);               // level-select: use the in-game fly-in
            PlayerPrefs.Save();                                        // commit the selected level before changing scenes
            SceneManager.LoadScene("Game");
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
