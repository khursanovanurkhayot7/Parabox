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

        [Header("Approved main-menu presentation")]
        [Tooltip("Uses the approved full-screen artwork instead of drawing the live board inside the title.")]
        public bool useStaticHomeArtwork = true;

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

        // Temporary playtest switch. Keep this in code instead of relying on a serialized Inspector
        // value: older copies of MainMenu.unity may still contain `false`, and OnEnable is also run
        // again whenever the player returns from gameplay. Set this back to false for the release
        // build so normal sequential progression is restored without touching saved progress.
        const bool UnlockAllLevelsForCurrentTestingBuild = true;

        public const int PerCategory = 10;

        const string LevelKey = "Parabox.Level";
        const int NewGameLevel = 0;
        const int CampaignLevelCount = 50;
        static string BestKey(int level) => "Parabox.Best." + level;

        // An arcade launch is a new player's run. Progress remains available while scenes change
        // inside that run, but stopping/reopening the game must never inherit the previous player's
        // unlocked route, best moves or score. Level 1 is the only initially available level.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeginFreshCampaignSession()
        {
            // Domain/scene reloads can happen inside one paid Luxodd session. Clearing campaign
            // data here made session recovery indistinguishable from Restart. The Luxodd token is
            // now the authority for detecting a genuinely new session; only transient UI flags
            // are safe to clear at process startup.
            PlayerPrefs.DeleteKey("Parabox.Seamless");
            PlayerPrefs.DeleteKey("Parabox.SeamlessOut");
            PlayerPrefs.DeleteKey("Parabox.FlyIn");
            PlayerPrefs.DeleteKey("Parabox.FinalRun");
            PlayerPrefs.Save();
        }

        // Public so the Luxodd shell hand-off can clear the finished player's run even when the
        // browser keeps this WebGL instance alive for the next cabinet player.
        public static void ResetCampaignSessionProgress()
        {
            for (int i = 0; i < CampaignLevelCount; i++)
            {
                PlayerPrefs.DeleteKey(GameManager.SessionClearKey(i));
                PlayerPrefs.DeleteKey(BestKey(i));
                PlayerPrefs.DeleteKey(GameManager.MechanicBriefingKey(i));
            }
            ScoreSystem.Reset(CampaignLevelCount);
            PlayerPrefs.SetInt(LevelKey, NewGameLevel);
            PlayerPrefs.DeleteKey("Parabox.Completed");
            PlayerPrefs.DeleteKey("Parabox.Seamless");
            PlayerPrefs.DeleteKey("Parabox.SeamlessOut");
            PlayerPrefs.DeleteKey("Parabox.OpenLevels");
            PlayerPrefs.DeleteKey("Parabox.JustBeat");
            PlayerPrefs.DeleteKey("Parabox.FlyIn");
            PlayerPrefs.DeleteKey("Parabox.FinalRun");
            PlayerPrefs.DeleteKey("Parabox.OutLevel");
            PlayerPrefs.DeleteKey("Parabox.FinalMoves");
            PlayerPrefs.Save();
        }

        int screen;   // 0 = home, 1 = the level board
        bool transitioning;

        // One hard auto-start countdown is shared by the title and level-select map. Navigation
        // never restarts it, and expiry always begins a fresh run from Level 1.
        const float MenuAutoStartSeconds = 30f;
        float _autoStartRemaining = MenuAutoStartSeconds;
        [SerializeField, HideInInspector] RectTransform _autoStartRoot;
        Vector2 _autoStartBasePosition;
        [SerializeField, HideInInspector] Text _autoStartLabel;
        [SerializeField, HideInInspector] Outline _autoStartOutline;
        [SerializeField, HideInInspector] Color _autoStartAccent;
        static readonly Color AutoStartWarning = new Color(0.96f, 0.25f, 0.22f, 1f);
        int _autoStartShown = -1;
        int _autoStartLayoutScreen = -1;
        bool _autoStartTriggered;
        bool _menuTimeoutReady;
        bool _autoStartWaitingForArm;
        int _autoStartArmVersion;
        long _autoStartDeadlineTimestamp;

        // The new five-panel map uses a clean artwork layer plus real Unity UI nodes. Keeping the
        // stateful parts native makes the completed fill, current ring, numbers and badges crisp and
        // correctly clipped at every resolution.
        Material _approvedMapProgressMaterial;
        Material _approvedLockedBlurMaterial;
        bool _approvedNativeMap;
        [SerializeField, HideInInspector]
        GameObject[] _approvedCompletedBadges = new GameObject[CampaignLevelCount];
        readonly float[] _approvedMapCompleted = new float[CampaignLevelCount];
        readonly float[] _approvedMapStates = new float[CampaignLevelCount];
        readonly float[] _approvedMapPathLit = new float[CampaignLevelCount];

        // live world-space board (rendered exactly like the game)
        LevelModel _model;
        readonly Dictionary<int, Transform> _roomRoots = new Dictionary<int, Transform>();
        readonly Dictionary<PEntity, EntityView> _views = new Dictionary<PEntity, EntityView>();
        Transform _boardRoot;
        int _startLevel;
        SpriteRenderer _boardGlow;
        float _glowBaseScale;

        void Awake()
        {
            // The approved title is rendered by a world-space SpriteRenderer, while PLAY,
            // LEVEL SELECT and the progression map are driven by this screen-space Canvas.
            // If the Canvas is accidentally saved at zero scale the title still looks perfect,
            // but every UI hit target collapses to zero pixels and the map appears impossible
            // to open. Keep the authored Canvas at a valid scale in both old and new scenes.
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.transform.localScale == Vector3.zero)
                canvas.transform.localScale = Vector3.one;
        }

        void OnEnable()
        {
            // The approved background is button-free. These are real Unity controls, not
            // invisible hotspots painted over artwork, so restore their visible faces every time
            // this screen is enabled. This also covers fast Enter Play Mode without scene reload.
            EnsureHomeActionButton(playButton, "PLAY", true);
            EnsureHomeActionButton(levelsButton, "LEVEL SELECT", false);

            // During the current difficulty playtest all 50 map nodes must remain selectable on
            // every menu visit. The normal completion visuals still use Beaten(), so unlocking a
            // level for testing does not falsely mark it as completed.
            unlockAllForTesting = UnlockAllLevelsForCurrentTestingBuild;
            // With fast Enter Play Mode this component can survive between runs. Display 30
            // immediately, then arm the real deadline on the first visible frame so editor reload
            // and scene activation time can never make the counter appear to start at 26.
            if (_menuTimeoutReady)
            {
                _autoStartWaitingForArm = true;
                _autoStartRemaining = MenuAutoStartSeconds;
                _autoStartShown = -1;
                _autoStartTriggered = false;
                if (_autoStartRoot != null) _autoStartRoot.gameObject.SetActive(true);
                PaintAutoStartTimer(30);
                int armVersion = ++_autoStartArmVersion;
                StartCoroutine(ArmMenuTimeoutOnVisibleFrame(armVersion));
            }
            if (mapCam != null)
            {
                Transform retiredMarker = mapCam.Find("ApprovedCurrentLevelMarker");
                if (retiredMarker != null) Destroy(retiredMarker.gameObject);
            }
            LuxoddGameService.ProgressLoaded += OnLuxoddProgressLoaded;
            HideChapterGates();
            HideFinalLevelDecoration();
        }

        void HideFinalLevelDecoration()
        {
            if (mapCam == null) return;
            Transform[] descendants = mapCam.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                string objectName = descendants[i].name;
                if (objectName == "FinalAura" || objectName == "FinalRing"
                    || objectName == "FinalLabel")
                    descendants[i].gameObject.SetActive(false);
            }
        }

        void OnDisable()
        {
            _autoStartArmVersion++;
            _autoStartWaitingForArm = false;
            LuxoddGameService.ProgressLoaded -= OnLuxoddProgressLoaded;
        }

        void OnDestroy()
        {
            if (_approvedMapProgressMaterial != null) Destroy(_approvedMapProgressMaterial);
            if (_approvedLockedBlurMaterial != null) Destroy(_approvedLockedBlurMaterial);
        }

        void Start()
        {
            Sfx.Init();
            HideMusicCredit();

            // PLAY and LEVEL SELECT used to be pictures painted into the menu background with
            // nearly invisible Button components laid over them. Keep their approved layout, but
            // render the supplied button artwork from the real Button hierarchy so hover, press,
            // controller focus and the complete rectangular hit area all belong to one object.
            EnsureHomeActionButton(playButton, "PLAY", true);
            EnsureHomeActionButton(levelsButton, "LEVEL SELECT", false);
            EnsureArtworkHitTarget(levelBoardBackButton);
            // The BACK face is painted into the full-screen map artwork. Keep its transparent
            // Unity hotspot above every decorative map layer so pointer clicks cannot be swallowed
            // by a later rebake or progress effect.
            if (levelBoardBackButton != null)
            {
                levelBoardBackButton.transform.SetAsLastSibling();
                levelBoardBackButton.gameObject.SetActive(true);
            }
            if (levelButtons != null)
                for (int i = 0; i < levelButtons.Length; i++)
                    EnsureArtworkHitTarget(levelButtons[i]);

            playButton.onClick.AddListener(BeginStart);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);
            if (levelsButton != null) levelsButton.onClick.AddListener(OpenLevelBoard);
            if (levelBoardBackButton != null) levelBoardBackButton.onClick.AddListener(CloseLevelBoard);

            for (int i = 0; i < levelButtons.Length; i++)
            {
                int index = i;
                if (levelButtons[i] != null) levelButtons[i].onClick.AddListener(() => TryStart(index));
            }

            BuildApprovedMapProgressLights();
            RefreshStates();
            RefreshGates();
            // The approved title artwork already contains the finished menu composition.  Do not
            // create a second puzzle board behind it: on a resized Game view that world-space board
            // can escape the old O slot and cover the whole menu.  The legacy animated preview is
            // still available only when the opt-in flag is deliberately disabled in the Inspector.
            if (useStaticHomeArtwork)
                RemoveLiveBoardPreview();
            else
            {
                BuildLiveBoard();
                ShowWorldBoard(true);
            }
            if (_autoStartRoot == null || _autoStartLabel == null || _autoStartOutline == null)
                Debug.LogError("[Parabox] Menu timer UI is not prebuilt. Run Tools/Parabox/Generate Prebuilt UI (Run This) before Play or Build.");
            // Arm the clock only after the expensive first-frame board/menu setup. Otherwise that
            // loading time is included in Unity's first delta and can consume most of the 30s.
            _menuTimeoutReady = true;
            BeginMenuTimeoutSession();

            // arriving out of the finale: catch the board mid-move and carry it out to the logo
            if (!useStaticHomeArtwork && PlayerPrefs.GetInt("Parabox.SeamlessOut", 0) == 1)
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

            // Static artwork has no world board to receive the old finale hand-off.  Clear stale
            // hand-off state so an earlier build cannot resurrect the removed preview.
            if (useStaticHomeArtwork && PlayerPrefs.GetInt("Parabox.SeamlessOut", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.SeamlessOut");
                PlayerPrefs.DeleteKey("Parabox.Seamless");
                PlayerPrefs.Save();
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
                    bool campaignComplete = isLast && AllLevelsBeaten();
                    bool opensChapter = !isLast && (beat % PerCategory) == PerCategory - 1
                                        && ch + 1 < Mathf.Max(1, Mathf.CeilToInt(levelButtons.Length / (float)PerCategory));
                    if (campaignComplete) StartCoroutine(PlayGameComplete());
                    else if (opensChapter) StartCoroutine(PlayChapterUnlock(ch));
                    else StartCoroutine(PlayProgress(beat));
                }
            }
        }

        static void EnsureArtworkHitTarget(Button button)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);
            button.interactable = true;

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null)
            {
                Debug.LogError("[Parabox] CanvasGroup is not prebuilt on " + button.name + ". Run the Prebuilt UI generator.");
                return;
            }
            if (group.alpha <= 0f) group.alpha = 0.001f;
            group.interactable = true;
            group.blocksRaycasts = true;

            // Older scenes use a small child named Face as Button.targetGraphic. The approved
            // artwork buttons are considerably larger, so clicking their outer area never reached
            // the Button. A nearly transparent root Image follows the complete Button rectangle:
            // it remains a reliable pointer target without drawing a white backing behind BACK.
            Image hitTarget = button.GetComponent<Image>();
            if (hitTarget == null)
            {
                Debug.LogError("[Parabox] Image hit target is not prebuilt on " + button.name + ". Run the Prebuilt UI generator.");
                return;
            }
            hitTarget.sprite = null;
            hitTarget.color = new Color(1f, 1f, 1f, 0.001f);
            hitTarget.raycastTarget = true;
            button.targetGraphic = hitTarget;

            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
                if (graphics[i] != null) graphics[i].raycastTarget = graphics[i] == hitTarget;
        }

        static void EnsureHomeActionButton(Button button, string label, bool isPlay)
        {
            if (button == null) return;
            button.gameObject.SetActive(true);
            button.interactable = true;
            button.transition = Selectable.Transition.ColorTint;

            // Keep both home actions low on the cabinet floor, with a small safe-area margin.
            // Enforcing this here also covers fast Enter Play Mode without a scene reload.
            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchoredPosition = new Vector2(isPlay ? -225f : 225f, -335f);
                buttonRect.sizeDelta = new Vector2(isPlay ? 345f : 370f, 112f);
            }

            CanvasGroup group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            Image face = button.GetComponent<Image>();
            if (face == null) face = button.gameObject.AddComponent<Image>();

            // Reuse the rounded sliced sprite already shipped in the button's Gloss child. This
            // replaces the old plain/transparent root with a proper rounded face without adding a
            // second texture or depending on the menu-background picture.
            Image gloss = FindHomeButtonImage(button.transform, "Gloss");
            if (gloss != null && gloss.sprite != null)
            {
                face.sprite = gloss.sprite;
                face.type = gloss.type;
            }
            face.color = Color.white;
            face.raycastTarget = true;
            face.canvasRenderer.cullTransparentMesh = false;
            button.targetGraphic = face;

            UIGradient gradient = face.GetComponent<UIGradient>();
            if (gradient == null) gradient = face.gameObject.AddComponent<UIGradient>();
            gradient.top = isPlay
                ? new Color(0.33333334f, 0.8627451f, 0.29803923f, 1f)
                : new Color(1f, 0.84705883f, 0.23921569f, 1f);
            gradient.bottom = isPlay
                ? new Color(0.08627451f, 0.52156866f, 0.21176471f, 1f)
                : new Color(0.9019608f, 0.60784316f, 0.04313726f, 1f);
            face.SetVerticesDirty();

            Text text = FindHomeButtonLabel(button);
            if (text != null)
            {
                text.text = label;
                text.raycastTarget = false;
                text.gameObject.SetActive(true);
            }

            EnsureHomeButtonIcon(button, text, isPlay);

            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
                if (graphics[i] != null) graphics[i].raycastTarget = graphics[i] == face;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.72f, 0.78f, 0.82f, 1f);
            colors.disabledColor = new Color(0.42f, 0.46f, 0.50f, 0.70f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            UIHoverScale hover = button.GetComponent<UIHoverScale>();
            if (hover == null) hover = button.gameObject.AddComponent<UIHoverScale>();
            hover.hover = 1.05f;
            hover.press = 0.965f;
        }

        static Image FindHomeButtonImage(Transform parent, string objectName)
        {
            Image[] images = parent.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i] != null && images[i].gameObject.name == objectName)
                    return images[i];
            return null;
        }

        static Text FindHomeButtonLabel(Button button)
        {
            Text fallback = null;
            Text[] labels = button.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null) continue;
                if (fallback == null) fallback = labels[i];
                if (labels[i].gameObject.name == "Lbl" || labels[i].gameObject.name == "Label")
                    return labels[i];
            }
            return fallback;
        }

        static void EnsureHomeButtonIcon(Button button, Text label, bool isPlay)
        {
            Transform existing = button.transform.Find("ButtonIcon");
            Text icon;
            if (existing != null)
            {
                icon = existing.GetComponent<Text>();
                if (icon == null) icon = existing.gameObject.AddComponent<Text>();
            }
            else
            {
                GameObject iconObject = new GameObject("ButtonIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                iconObject.layer = button.gameObject.layer;
                iconObject.transform.SetParent(button.transform, false);
                icon = iconObject.GetComponent<Text>();
            }

            RectTransform rect = icon.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(isPlay ? -112f : -132f, 0f);
            rect.sizeDelta = new Vector2(54f, 62f);

            icon.text = isPlay ? "\u25B6" : "\u25A6";
            icon.font = label != null && label.font != null
                ? label.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            icon.fontSize = isPlay ? 35 : 34;
            icon.fontStyle = FontStyle.Bold;
            icon.alignment = TextAnchor.MiddleCenter;
            icon.color = label != null ? label.color : Color.white;
            icon.raycastTarget = false;
            icon.gameObject.SetActive(true);
            icon.transform.SetAsLastSibling();
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
                if (!IsCurrentScreenSelection(selected))
                    ReselectCurrentScreen();
            }

            if (HandleArcadeMenuInput()) return;

            var kb = Keyboard.current;
            TickAutoStartTimer();
            if (kb == null || transitioning) return;
            if (kb.mKey.wasPressedThisFrame) Sfx.ToggleMute();
            // macOS labels Backspace as "Delete"; support both physical keys so the editor/test
            // reset shortcut works on a MacBook as well as a full keyboard.
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)
                ResetProgress();
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (screen == 1) CloseLevelBoard();
                else OpenLevelBoard();   // home: Esc opens the level board (the "Menu" prompt)
                return;
            }

            Vector2Int keyboardDirection = ReadMenuDirectionDown(kb);
            if (keyboardDirection != Vector2Int.zero)
            {
                MoveSelection(keyboardDirection);
                return;
            }

            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (!TryInvokeCurrentSelection())
                {
                    ReselectCurrentScreen();
                    TryInvokeCurrentSelection();
                }
            }
        }

        static Vector2Int ReadMenuDirectionDown(Keyboard keyboard)
        {
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
                return Vector2Int.up;
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
                return Vector2Int.down;
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                return Vector2Int.left;
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                return Vector2Int.right;
            return Vector2Int.zero;
        }

        void OnLuxoddProgressLoaded()
        {
            if (levelButtons == null || levelButtons.Length == 0) return;
            RefreshStates();
            RefreshGates();
            // Cloud progress may unlock later levels, but the title-screen game entry remains a
            // new run from Level 1. Explicit level-map selections are handled separately.
            int desiredLevel = Mathf.Clamp(PlayerPrefs.GetInt(LevelKey, NewGameLevel),
                0, levelButtons.Length - 1);
            if (useStaticHomeArtwork)
            {
                _startLevel = desiredLevel;
                RemoveLiveBoardPreview();
                if (!transitioning) ReselectCurrentScreen();
                return;
            }
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
        // explicit: stick/D-pad changes focus, Black activates it, Yellow opens the map and
        // White goes back. On a standard gamepad Luxodd maps Black=A and Red=B, so B is also a
        // contextual Back button outside gameplay. This keeps both primary gamepad buttons useful.
        bool HandleArcadeMenuInput()
        {
            var arcade = LuxoddArcadeAdapter.Instance;
            if (arcade == null || transitioning) return false;

            if (arcade.MuteDown)
            {
                // Luxodd buttons bypass Unity's EventSystem, so play the same prebuilt press used
                // by pointer/keyboard UI before muting the audio source.
                Sfx.Click();
                Sfx.ToggleMute();
                return true;
            }
            if (arcade.BackDown || arcade.UndoDown)
            {
                Sfx.Click();
                if (screen == 1) CloseLevelBoard();
                else Quit();
                return true;
            }
            if (arcade.LevelsDown)
            {
                Sfx.Click();
                if (screen == 0) OpenLevelBoard();
                else CloseLevelBoard();
                return true;
            }
            if (arcade.NavigationPulse && arcade.Direction != Vector2Int.zero)
            {
                MoveSelection(arcade.Direction);
                return true;
            }
            if (arcade.ConfirmDown)
            {
                // A screen transition can leave the EventSystem focused on a hidden button for
                // one frame. Previously the first cabinet press only repaired that focus, making
                // the player press Black twice. Repair it and submit the newly selected button on
                // the SAME press so cabinet input always feels immediate.
                if (!TryInvokeCurrentSelection())
                {
                    ReselectCurrentScreen();
                    TryInvokeCurrentSelection();
                }
                return true;
            }
            return false;
        }

        bool TryInvokeCurrentSelection()
        {
            if (EventSystem.current == null) return false;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (!IsCurrentScreenSelection(selected)) return false;
            Button button = selected != null ? selected.GetComponent<Button>() : null;
            if (button == null || !button.interactable || !button.gameObject.activeInHierarchy)
                return false;
            button.onClick.Invoke();
            return true;
        }

        void MoveSelection(Vector2Int direction)
        {
            if (EventSystem.current == null) return;
            GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
            Selectable selected = selectedObject != null ? selectedObject.GetComponent<Selectable>() : null;
            Selectable next = null;

            // The level map is an inward spiral. Unity's generic spatial navigation can jump
            // across nearby turns of that spiral, so level nodes follow their actual route
            // neighbours instead. This keeps arcade-stick navigation visually predictable.
            if (screen == 1 && selected is Button selectedButton
                && TryMoveAlongLevelRoute(selectedButton, direction))
                return;

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

        bool TryMoveAlongLevelRoute(Button selectedButton, Vector2Int direction)
        {
            if (levelButtons == null || selectedButton == null) return false;
            int index = System.Array.IndexOf(levelButtons, selectedButton);
            if (index < 0) return false;

            Vector2 wanted = ((Vector2)direction).normalized;
            Button best = null;
            float bestAlignment = 0.08f;
            int[] neighbourIndices = { index - 1, index + 1 };
            for (int i = 0; i < neighbourIndices.Length; i++)
            {
                int candidateIndex = neighbourIndices[i];
                if (candidateIndex < 0 || candidateIndex >= levelButtons.Length) continue;
                Button candidate = levelButtons[candidateIndex];
                if (candidate == null || !candidate.interactable || !candidate.gameObject.activeInHierarchy)
                    continue;

                Vector2 delta = (Vector2)candidate.transform.position - (Vector2)selectedButton.transform.position;
                if (delta.sqrMagnitude < 0.001f) continue;
                float alignment = Vector2.Dot(delta.normalized, wanted);
                if (alignment <= bestAlignment) continue;
                bestAlignment = alignment;
                best = candidate;
            }

            if (best != null)
            {
                Select(best);
                Sfx.Hover();
                return true;
            }

            // Down from the lower outer arc reaches the persistent Back button. All other
            // unmatched directions are consumed so the cursor never leaps to another orbit.
            RectTransform selectedRect = selectedButton.transform as RectTransform;
            if (direction.y < 0 && levelBoardBackButton != null && selectedRect != null
                && selectedRect.anchoredPosition.y < -220f)
            {
                Select(levelBoardBackButton);
                Sfx.Hover();
            }
            return true;
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

        // ---------------------------------------------------------- Luxodd 30-second menu auto-return
        void BeginMenuTimeoutSession()
        {
            _autoStartWaitingForArm = false;
            _autoStartDeadlineTimestamp = System.Diagnostics.Stopwatch.GetTimestamp()
                + (long)(MenuAutoStartSeconds * System.Diagnostics.Stopwatch.Frequency);
            _autoStartRemaining = MenuAutoStartSeconds;
            _autoStartShown = -1;
            _autoStartTriggered = false;
            _autoStartLayoutScreen = -1;

            if (_autoStartRoot == null) return;
            _autoStartRoot.gameObject.SetActive(true);
            if (_autoStartLabel != null) _autoStartLabel.rectTransform.localScale = Vector3.one;
            PaintAutoStartTimer(Mathf.CeilToInt(_autoStartRemaining));
        }

        System.Collections.IEnumerator ArmMenuTimeoutOnVisibleFrame(int armVersion)
        {
            yield return null;
            if (!isActiveAndEnabled || armVersion != _autoStartArmVersion) yield break;
            BeginMenuTimeoutSession();
        }

        void BuildAutoStartTimer()
        {
            if (homeGroup == null || _autoStartRoot != null) return;

            _autoStartAccent = new Color(0.12f, 0.86f, 1f, 1f);
            Color dark = new Color(0.012f, 0.045f, 0.09f, 0.96f);

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
            _autoStartOutline = outline;
            Color edge = _autoStartAccent; edge.a = 0.72f;
            outline.effectColor = edge;
            outline.effectDistance = new Vector2(2.4f, -2.4f);

            Font font = null;
            if (playButton != null)
            {
                var playLabel = playButton.GetComponentInChildren<Text>(true);
                if (playLabel != null) font = playLabel.font;
            }
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _autoStartLabel = MakeTimerText("Label", root.transform, font, 21, FontStyle.Bold,
                Vector2.zero, Vector2.one, new Vector2(14f, 7f), new Vector2(-14f, -3f));
            _autoStartLabel.text = "AUTO START IN 30 SECONDS";
            _autoStartLabel.color = Color.Lerp(_autoStartAccent, Color.white, 0.42f);
            var labelOutline = _autoStartLabel.gameObject.AddComponent<Outline>();
            labelOutline.effectColor = new Color(0f, 0.03f, 0.10f, 0.88f);
            labelOutline.effectDistance = new Vector2(1.2f, -1.2f);
            var labelShadow = _autoStartLabel.gameObject.AddComponent<Shadow>();
            labelShadow.effectColor = new Color(0f, 0f, 0f, 0.58f);
            labelShadow.effectDistance = new Vector2(0f, -2f);

            _autoStartRoot.SetAsLastSibling();
            LayoutAutoStartTimer();
            PaintAutoStartTimer(Mathf.CeilToInt(_autoStartRemaining));
        }

#if UNITY_EDITOR
        // Editor-only construction entry point used by the one-click generator. The player build
        // only updates these serialized objects; it never creates menu panels, badges or hit areas.
        public void PrebuildStaticUi()
        {
            EnsurePrebuiltHomeActionButton(playButton, "PLAY", true);
            EnsurePrebuiltHomeActionButton(levelsButton, "LEVEL SELECT", false);
            EnsurePrebuiltArtworkHitTarget(levelBoardBackButton);
            if (levelButtons != null)
                for (int i = 0; i < levelButtons.Length; i++)
                    EnsurePrebuiltArtworkHitTarget(levelButtons[i]);

            if (_autoStartRoot == null)
                BuildAutoStartTimer();

            if (_approvedCompletedBadges == null
                || _approvedCompletedBadges.Length != CampaignLevelCount)
                _approvedCompletedBadges = new GameObject[CampaignLevelCount];

            if (levelButtons != null)
                for (int i = 0; i < Mathf.Min(CampaignLevelCount, levelButtons.Length); i++)
                    EnsureApprovedCompletedBadge(i, levelButtons[i], ApprovedChapterAccent(i));

            foreach (Button button in GetComponentsInChildren<Button>(true))
                Sfx.AttachButton(button);
        }

        static void EnsurePrebuiltArtworkHitTarget(Button button)
        {
            if (button == null) return;
            if (button.GetComponent<CanvasGroup>() == null)
                button.gameObject.AddComponent<CanvasGroup>();
            if (button.GetComponent<Image>() == null)
                button.gameObject.AddComponent<Image>();
            EnsureArtworkHitTarget(button);
        }

        static void EnsurePrebuiltHomeActionButton(Button button, string label, bool isPlay)
        {
            if (button == null) return;
            if (button.GetComponent<CanvasGroup>() == null)
                button.gameObject.AddComponent<CanvasGroup>();
            if (button.GetComponent<Image>() == null)
                button.gameObject.AddComponent<Image>();
            EnsureHomeActionButton(button, label, isPlay);
        }
#endif

        public bool IsStaticUiPrebuilt
        {
            get
            {
                if (_autoStartRoot == null || _autoStartLabel == null || _autoStartOutline == null
                    || _approvedCompletedBadges == null
                    || _approvedCompletedBadges.Length != CampaignLevelCount)
                    return false;
                for (int i = 0; i < CampaignLevelCount; i++)
                    if (_approvedCompletedBadges[i] == null) return false;
                return true;
            }
        }

        void LayoutAutoStartTimer()
        {
            if (_autoStartRoot == null || _autoStartLayoutScreen == screen) return;
            _autoStartLayoutScreen = screen;

            // Occupy the original timer's cleared space at the very top-right. Keeping the same
            // position on Home and Level Select prevents the timer from jumping during navigation.
            _autoStartBasePosition = new Vector2(-38f, -8f);
            _autoStartRoot.anchoredPosition = _autoStartBasePosition;
            // Keep the timer compact.  The old cyan progress strip extended below this panel and
            // visually sat behind nearby menu/level buttons, so the timer is now text-only.
            _autoStartRoot.sizeDelta = new Vector2(420f, 64f);

            if (_autoStartLabel != null)
            {
                _autoStartLabel.fontSize = 19;
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
            CrispUiTypography.Polish(text);
            return text;
        }

        void TickAutoStartTimer()
        {
            if (!_menuTimeoutReady || _autoStartTriggered || _autoStartRoot == null
                || transitioning || _autoStartWaitingForArm) return;
            LayoutAutoStartTimer();

            // Gentle motion keeps the timer alive without distracting from the menu.
            float slowWave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.45f);
            _autoStartRoot.anchoredPosition = _autoStartBasePosition
                + Vector2.up * Mathf.Lerp(-1.5f, 1.5f, slowWave);

            long ticksLeft = _autoStartDeadlineTimestamp - System.Diagnostics.Stopwatch.GetTimestamp();
            _autoStartRemaining = Mathf.Max(0f,
                (float)(ticksLeft / (double)System.Diagnostics.Stopwatch.Frequency));
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
            StartLevel(NewGameLevel);
        }

        void PaintAutoStartTimer(int seconds)
        {
            int shown = Mathf.Clamp(seconds, 0, 99);
            if (_autoStartLabel != null && seconds != _autoStartShown)
            {
                _autoStartShown = seconds;
                _autoStartLabel.text = $"AUTO START IN {shown} SECOND{(shown == 1 ? "" : "S")}";

                bool warning = shown > 0 && shown <= 5;
                _autoStartLabel.color = warning
                    ? AutoStartWarning
                    : Color.Lerp(_autoStartAccent, Color.white, 0.42f);
                if (_autoStartOutline != null)
                {
                    Color edge = warning ? AutoStartWarning : _autoStartAccent;
                    edge.a = warning ? 0.92f : 0.72f;
                    _autoStartOutline.effectColor = edge;
                }
            }
        }

        // ---------------------------------------------------------- progression
        static bool Beaten(int i) => PlayerPrefs.HasKey(GameManager.SessionClearKey(i));
        bool Unlocked(int i)
        {
            if (unlockAllForTesting) return true;
            // A level opens only when the whole chain before it has been completed. This prevents
            // old test saves with scattered clears from opening later levels out of order.
            for (int previous = 0; previous < i; previous++)
                if (!Beaten(previous)) return false;
            return true;
        }

        bool AllLevelsBeaten()
        {
            if (levelButtons == null || levelButtons.Length == 0) return false;
            for (int i = 0; i < levelButtons.Length; i++)
                if (!Beaten(i)) return false;
            return true;
        }

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
            if (beaten < 0 || beaten >= levelButtons.Length) yield break;

            bool approvedProgress = _approvedNativeMap || _approvedMapProgressMaterial != null;

            // The approved five-chapter artwork uses its own tightly clipped shader animation.
            // Do not run the legacy burst/ring effect here: it spills outside the node and adds
            // visual noise. The clean sequence completes in 0.66 seconds.
            if (approvedProgress)
            {
                yield return PlayApprovedProgressLink(beaten);
                RefreshStates();
                int approvedNext = beaten + 1;
                if (approvedNext < levelButtons.Length && levelButtons[approvedNext] != null
                    && levelButtons[approvedNext].interactable)
                {
                    _startLevel = approvedNext;
                    Select(levelButtons[approvedNext]);
                }
                else SelectCurrentNode();
                yield break;
            }

            if (progressFx == null) yield break;

            var beatenRT = levelButtons[beaten] != null ? (RectTransform)levelButtons[beaten].transform : null;
            // Completion is already communicated by the tile colour and perfect-run star. Keep the
            // old serialized badge hidden so existing scenes lose the dark corner circle too.
            GameObject check = null;
            if (levelChecks != null && beaten < levelChecks.Length && levelChecks[beaten] != null)
                levelChecks[beaten].SetActive(false);

            int next = beaten + 1;
            RectTransform nextRT = null;
            GameObject nextRing = null;
            if (next < levelButtons.Length)
            {
                if (levelButtons[next] != null) nextRT = (RectTransform)levelButtons[next].transform;
                if (levelHighlights != null && next < levelHighlights.Length) nextRing = levelHighlights[next];
            }

            // No travelling route/line effect: completion lands on the cleared tile, then the
            // newly available tile wakes immediately.
            var road = new List<Image>();
            Color lit = Color.white;
            if (boardThemes != null && boardThemes.Length > 0)
            {
                var th = boardThemes[Mathf.Clamp(beaten / PerCategory, 0, boardThemes.Length - 1)];
                lit = WithA(th.frame, 0.85f);
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

        // Chapter locks are communicated by the dim nodes and disabled buttons. The old pair of
        // bars plus padlock crowded the route and chapter label, so legacy scene objects stay off.
        void RefreshGates()
        {
            HideChapterGates();
        }

        void HideChapterGates()
        {
            if (chapterGates == null) return;
            for (int c = 1; c < chapterGates.Length; c++)
                if (chapterGates[c] != null) chapterGates[c].SetActive(false);
        }

        // The chapter ceremony. `ch` is the chapter just COMPLETED; ch+1 is the one being opened.
        System.Collections.IEnumerator PlayChapterUnlock(int ch)
        {
            int next = ch + 1;
            if (unlockFx == null || next >= Mathf.CeilToInt(levelButtons.Length / (float)PerCategory))
                yield break;
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

            // Keep the chapter unlock motion, but never draw a travelling route line.
            var newRoad = new List<Image>();

            int firstIdx = next * PerCategory;
            RectTransform firstNode = (firstIdx < levelButtons.Length && levelButtons[firstIdx] != null)
                ? (RectTransform)levelButtons[firstIdx].transform : null;
            GameObject firstRing = (levelHighlights != null && firstIdx < levelHighlights.Length)
                ? levelHighlights[firstIdx] : null;

            float doneY = (regionBaseY != null && ch < regionBaseY.Length) ? regionBaseY[ch] : 0f;
            float newY  = (regionBaseY != null && next < regionBaseY.Length) ? regionBaseY[next] : 0f;

            yield return unlockFx.Play(mapCam, doneNodes, doneFills, Lighten(th.frame, 0.4f),
                null, null, null, null,
                null, newRoad, WithA(thNext.frame, 0.85f), firstNode, firstRing,
                doneY, newY);
        }

        // Finishing the game: the map replays your whole journey, then says so.
        System.Collections.IEnumerator PlayGameComplete()
        {
            // Defensive guard for stale transition flags or direct scene testing: the victory
            // ceremony is valid only after every shipped level has actually been cleared.
            if (unlockFx == null || !AllLevelsBeaten()) yield break;
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
                        pathDots[i].color = WithA(th.frame, 0.35f);
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
                null, nodes, fills, cols, PerCategory, finaleBanner, finaleBadge);
        }

        // The live world board — the one that becomes the O in the logo — sits in WORLD space,
        // behind the whole canvas. The map's scrim is 90% opaque, so the board was ghosting
        // through it: you could see a grid and a box floating behind the level nodes. Dimming the
        // scrim further would just hide it less; the board simply has no business being on screen
        // while the map is up, so it leaves.
        void ShowWorldBoard(bool on)
        {
            if (_boardRoot != null) _boardRoot.gameObject.SetActive(on && !useStaticHomeArtwork);
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

        bool IsCurrentScreenSelection(GameObject selected)
        {
            if (selected == null) return false;
            if (screen == 1) return IsLevelBoardSelection(selected);
            return (playButton != null && selected == playButton.gameObject)
                   || (levelsButton != null && selected == levelsButton.gameObject);
        }

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
            // Every level, including Level 50, uses the same immediate game-scene handoff.
            // The old Level-50-only map zoom exposed the title background while it moved the
            // oversized map and made the button appear not to enter gameplay.
            transitioning = true;   // guard against UI Submit and key polling both firing this frame
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
        // chapters had no identity at all. The chapter-coloured tile now says "done", while orange
        // marks the one level you're on.
        void RefreshStates()
        {
            int total = levelButtons.Length;
            int current = CurrentLevel();
            bool haveThemes = boardThemes != null && boardThemes.Length > 0;
            bool approvedMap = mapCam != null && mapCam.Find("ApprovedFiveChapterMap") != null;

            for (int i = 0; i < total; i++)
            {
                bool beaten = Beaten(i);
                bool unlocked = Unlocked(i);
                bool isCurrent = unlocked && !beaten && i == current;

                if (levelButtons[i] != null) levelButtons[i].interactable = unlocked;

                if (levelChecks != null && i < levelChecks.Length && levelChecks[i] != null)
                    levelChecks[i].SetActive(false);
                if (levelLocks != null && i < levelLocks.Length && levelLocks[i] != null)
                    levelLocks[i].SetActive(false);
                if (levelHighlights != null && i < levelHighlights.Length && levelHighlights[i] != null)
                    levelHighlights[i].SetActive(isCurrent);

                if (!haveThemes) continue;
                var th = boardThemes[Mathf.Clamp(i / PerCategory, 0, boardThemes.Length - 1)];
                Color cell = (th.roomColors != null && th.roomColors.Length > 0) ? th.roomColors[0] : th.frame;

                // Keep a virtually invisible pixel of alpha on the real Button graphic. Unity's
                // GraphicRaycaster culls a fully transparent CanvasGroup/Graphic, which made the
                // printed map nodes impossible to click even though testing mode unlocked them.
                Color approvedAccent = ApprovedChapterAccent(i);
                Color approvedLockedAccent = ApprovedLockedAccent(approvedAccent);
                Color face = approvedMap
                           ? (!unlocked
                               ? new Color(0.022f, 0.031f, 0.049f, 0.98f)
                               : isCurrent
                                   ? WithA(Lighten(approvedAccent, 0.28f), 1f)
                                   : beaten
                                       ? WithA(Lighten(approvedAccent, 0.10f), 1f)
                                       : WithA(approvedAccent, 0.72f))
                           : !unlocked ? Color.Lerp(th.gutter, Color.black, 0.28f)
                           : isCurrent ? th.box       // the only orange on the map
                           : beaten    ? th.frame     // this chapter's identity
                                       : cell;

                if (levelFills != null && i < levelFills.Length && levelFills[i] != null)
                {
                    levelFills[i].color = face;
                    if (approvedMap)
                        levelFills[i].material = !unlocked ? _approvedLockedBlurMaterial : null;
                }

                // one stroke weight everywhere; only its brightness tracks the state. A beaten node
                // is already the frame colour, so its border lifts to white instead — otherwise the
                // stroke would vanish into the face.
                if (levelBorders != null && i < levelBorders.Length && levelBorders[i] != null)
                {
                    levelBorders[i].color = approvedMap
                        ? !unlocked
                            ? WithA(approvedLockedAccent, 0.48f)
                            : isCurrent
                                ? WithA(Lighten(approvedAccent, 0.75f), 1f)
                                : WithA(Lighten(approvedAccent, 0.48f), 0.96f)
                        : !unlocked
                        ? WithA(Color.Lerp(th.gutter, Color.white, 0.14f), 1f)
                        : beaten    ? WithA(Lighten(th.frame, 0.55f), 0.95f)
                        : isCurrent ? WithA(th.frame, 0.95f)
                                    : WithA(th.frame, 0.5f);
                    if (approvedMap)
                        levelBorders[i].material = !unlocked ? _approvedLockedBlurMaterial : null;
                }

                // Numbers remain crisp in every state; only the locked frame and face are softened.
                if (levelNumbers != null && i < levelNumbers.Length && levelNumbers[i] != null)
                {
                    levelNumbers[i].enabled = true;
                    levelNumbers[i].text = (i + 1).ToString();
                    // The holographic map uses dark chapter tiles; bright numerals keep every
                    // unlocked stop readable against all five colours and match the concept art.
                    levelNumbers[i].color = approvedMap
                        ? (!unlocked
                            ? new Color(0.57f, 0.63f, 0.72f, 0.92f)
                            : Color.white)
                        : Color.Lerp(Color.white, th.frame, 0.12f);

                    if (approvedMap)
                    {
                        RectTransform numberRect = levelNumbers[i].rectTransform;
                        numberRect.anchoredPosition = Vector2.zero;
                        numberRect.sizeDelta = new Vector2(68f, 66f);
                        levelNumbers[i].fontSize = unlocked ? 25 : 20;
                    }
                }

                if (approvedMap && i < _approvedCompletedBadges.Length
                    && _approvedCompletedBadges[i] != null)
                    _approvedCompletedBadges[i].SetActive(beaten);

                if (approvedMap && levelHighlights != null && i < levelHighlights.Length
                    && levelHighlights[i] != null && isCurrent)
                {
                    Image ring = levelHighlights[i].GetComponent<Image>();
                    if (ring != null) ring.color = WithA(Lighten(approvedAccent, 0.72f), 0.98f);
                }

                // perfect = cleared at par. The par comes from the level prefab, so it can never
                // disagree with what the solver actually proved.
                bool perfect = beaten && Par(i) > 0 && PlayerPrefs.GetInt(BestKey(i), 9999) <= Par(i);
                if (levelStars != null && i < levelStars.Length && levelStars[i] != null)
                    levelStars[i].SetActive(!approvedMap && perfect);

                // Route-dot progress is retired; the level tiles themselves show all progression.
                if (pathDots != null && pathDotsPerLink > 0)
                    for (int d = 0; d < pathDotsPerLink; d++)
                    {
                        int idx = i * pathDotsPerLink + d;
                        if (idx < pathDots.Length && pathDots[idx] != null)
                            pathDots[idx].gameObject.SetActive(false);
                    }
            }

            RefreshApprovedMapProgressLights();

        }

        static Color ApprovedChapterAccent(int levelIndex)
        {
            switch (Mathf.Clamp(levelIndex / PerCategory, 0, 4))
            {
                case 0: return new Color(0.10f, 0.88f, 0.91f, 1f);
                case 1: return new Color(0.31f, 0.62f, 1.00f, 1f);
                case 2: return new Color(0.61f, 0.36f, 1.00f, 1f);
                case 3: return new Color(0.91f, 0.31f, 0.82f, 1f);
                default: return new Color(1.00f, 0.38f, 0.56f, 1f);
            }
        }

        static Color ApprovedLockedAccent(Color accent)
        {
            return Color.Lerp(accent, new Color(0.24f, 0.30f, 0.39f, 1f), 0.74f);
        }

        void EnsureApprovedCompletedBadge(int index, Button button, Color accent)
        {
            if (index < 0 || index >= _approvedCompletedBadges.Length || button == null) return;
            if (_approvedCompletedBadges[index] != null) return;
            Transform existing = button.transform.Find("CompletedBadge");
            if (existing != null)
            {
                _approvedCompletedBadges[index] = existing.gameObject;
                return;
            }
            if (Application.isPlaying)
            {
                Debug.LogError("[Parabox] CompletedBadge is not prebuilt for level " + (index + 1)
                    + ". Run the Prebuilt UI generator.");
                return;
            }

            GameObject badge = new GameObject("CompletedBadge", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            badge.transform.SetParent(button.transform, false);
            RectTransform badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0.5f, 0.5f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(24f, 24f);
            badgeRect.sizeDelta = new Vector2(20f, 20f);

            Image background = badge.GetComponent<Image>();
            background.raycastTarget = false;
            background.type = Image.Type.Simple;
            background.preserveAspect = false;
            if (levelFills != null && index < levelFills.Length && levelFills[index] != null)
                background.sprite = levelFills[index].sprite;
            background.color = WithA(Lighten(accent, 0.78f), 1f);

            Color checkColor = new Color(0.015f, 0.055f, 0.085f, 1f);
            CreateCheckStroke(badge.transform, "CheckShort", new Vector2(-3.2f, -1.2f),
                new Vector2(7.5f, 2.5f), -42f, checkColor);
            CreateCheckStroke(badge.transform, "CheckLong", new Vector2(2.1f, 0.6f),
                new Vector2(11f, 2.5f), 47f, checkColor);

            badge.SetActive(false);
            _approvedCompletedBadges[index] = badge;
        }

        static void CreateCheckStroke(Transform parent, string name, Vector2 position,
            Vector2 size, float rotation, Color color)
        {
            GameObject stroke = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            stroke.transform.SetParent(parent, false);
            RectTransform rect = stroke.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localEulerAngles = new Vector3(0f, 0f, rotation);
            Image image = stroke.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        void BuildApprovedMapProgressLights()
        {
            if (mapCam == null || levelButtons == null || levelButtons.Length == 0) return;
            Transform approvedMap = mapCam.Find("ApprovedFiveChapterMap");
            if (approvedMap == null) return;

            _approvedNativeMap = true;
            approvedMap.SetAsFirstSibling();

            // Retire the old map's decorative regions. Their node containers are reused, but the
            // old paths/panels would otherwise sit above the new artwork at mismatched positions.
            for (int r = 0; r < mapCam.childCount; r++)
            {
                Transform region = mapCam.GetChild(r);
                if (!region.name.StartsWith("Region")) continue;
                for (int c = 0; c < region.childCount; c++)
                {
                    Transform layer = region.GetChild(c);
                    layer.gameObject.SetActive(layer.name == "Nodes");
                }
            }

            // Remove the older generated overlay if a Play Mode domain reload left one alive.
            Transform old = mapCam.Find("ApprovedProgressLights");
            if (old != null) Destroy(old.gameObject);

            // The generated option-two artwork is deliberately neutral. State lighting, numbers
            // and completion badges are placed above it by the real level controls below.
            Transform artworkTransform = approvedMap.Find("Artwork");
            Image artwork = artworkTransform != null ? artworkTransform.GetComponent<Image>() : null;
            Sprite baseSprite = Resources.Load<Sprite>("UI/LevelMapOption2Base");
            if (artwork != null)
            {
                if (baseSprite != null) artwork.sprite = baseSprite;
                artwork.material = null;
                artwork.color = Color.white;
                artwork.raycastTarget = false;
            }
            Image duplicateRootArtwork = approvedMap.GetComponent<Image>();
            if (duplicateRootArtwork != null)
            {
                duplicateRootArtwork.material = null;
                duplicateRootArtwork.color = Color.clear;
                duplicateRootArtwork.raycastTarget = false;
            }
            if (_approvedMapProgressMaterial != null)
            {
                Destroy(_approvedMapProgressMaterial);
                _approvedMapProgressMaterial = null;
            }
            if (_approvedLockedBlurMaterial != null) Destroy(_approvedLockedBlurMaterial);
            Shader lockedBlur = Resources.Load<Shader>("Shaders/LockedNodeSoftBlur");
            _approvedLockedBlurMaterial = lockedBlur != null && lockedBlur.isSupported
                ? new Material(lockedBlur) { name = "Locked Node Soft Blur (Runtime)" }
                : null;
            if (_approvedLockedBlurMaterial != null)
                _approvedLockedBlurMaterial.SetFloat("_BlurSize", 1.15f);

            // Reuse the authored Unity nodes as the visible, interactive state layer. Disable the
            // legacy shadows/gradients/outlines so every fill is solid and cannot bloom outside.
            for (int i = 0; i < Mathf.Min(50, levelButtons.Length); i++)
            {
                Button button = levelButtons[i];
                if (button == null) continue;
                button.transition = Selectable.Transition.None;
                CanvasGroup group = button.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    Debug.LogError("[Parabox] Level-button CanvasGroup is not prebuilt for level "
                        + (i + 1) + ". Run the Prebuilt UI generator.");
                    continue;
                }
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;

                RectTransform node = button.transform as RectTransform;
                if (node != null)
                {
                    node.anchoredPosition = ApprovedMapNodePosition(i);
                    // The concept-art map contains an empty 76px node socket behind every real
                    // button.  A 74px live face left that socket peeking out on one side as a
                    // duplicate border.  The live control must fully cover the inert artwork.
                    node.sizeDelta = new Vector2(86f, 86f);
                    node.localScale = Vector3.one;
                }

                RemoveApprovedNodeLegacyEffects(button);

                if (levelFills != null && i < levelFills.Length && levelFills[i] != null)
                {
                    levelFills[i].type = Image.Type.Simple;
                    levelFills[i].preserveAspect = false;
                    levelFills[i].raycastTarget = true;
                }
                if (levelBorders != null && i < levelBorders.Length && levelBorders[i] != null)
                {
                    RectTransform border = levelBorders[i].rectTransform;
                    border.anchoredPosition = Vector2.zero;
                    // Keep one state-coloured stroke exactly on the enlarged live face.  This
                    // covers the baked socket instead of drawing a second offset frame around it.
                    border.sizeDelta = new Vector2(86f, 86f);
                    levelBorders[i].type = Image.Type.Sliced;
                    levelBorders[i].fillCenter = false;
                    levelBorders[i].raycastTarget = false;
                }
                if (levelHighlights != null && i < levelHighlights.Length
                    && levelHighlights[i] != null)
                {
                    RectTransform ring = levelHighlights[i].transform as RectTransform;
                    if (ring != null)
                    {
                        ring.anchoredPosition = Vector2.zero;
                        ring.sizeDelta = new Vector2(98f, 98f);
                        ring.localScale = Vector3.one;
                    }
                }

                if (levelLocks != null && i < levelLocks.Length && levelLocks[i] != null)
                    levelLocks[i].SetActive(false);
                EnsureApprovedCompletedBadge(i, button, ApprovedChapterAccent(i));

                var hover = button.GetComponent<UIHoverScale>();
                if (hover != null && hover.highlight != null)
                {
                    RectTransform highlight = hover.highlight.transform as RectTransform;
                    if (highlight != null)
                    {
                        highlight.anchoredPosition = ApprovedMapNodePosition(i);
                        highlight.sizeDelta = new Vector2(96f, 96f);
                    }
                    Image hoverImage = hover.highlight.GetComponent<Image>();
                    if (hoverImage != null)
                        hoverImage.color = WithA(Lighten(ApprovedChapterAccent(i), 0.55f), 0.34f);
                }
            }
        }

        static void RemoveApprovedNodeLegacyEffects(Button button)
        {
            if (button == null) return;

            // Explicitly neutralise the authored effects as well as disabling them.  Unity can
            // retain a previously generated UI mesh for part of a frame after a component is
            // disabled; clearing the offsets and dirtying the Graphic makes the cleanup reliable
            // with both normal and fast-enter Play Mode.
            BaseMeshEffect[] effects = button.GetComponents<BaseMeshEffect>();
            for (int e = 0; e < effects.Length; e++)
            {
                Shadow shadow = effects[e] as Shadow;
                if (shadow != null)
                {
                    shadow.effectDistance = Vector2.zero;
                    shadow.effectColor = Color.clear;
                }
                effects[e].enabled = false;
            }

            Graphic graphic = button.targetGraphic != null
                ? button.targetGraphic
                : button.GetComponent<Graphic>();
            if (graphic != null) graphic.SetVerticesDirty();
        }

        void RefreshApprovedMapProgressLights()
        {
            if (mapCam == null || mapCam.Find("ApprovedFiveChapterMap") == null || levelButtons == null)
                return;

            int current = CurrentLevel();
            int count = Mathf.Min(CampaignLevelCount, levelButtons.Length);
            for (int i = 0; i < count; i++)
            {
                Button button = levelButtons[i];
                if (button == null) continue;
                CanvasGroup group = button.GetComponent<CanvasGroup>();
                if (group != null) group.alpha = 1f;
                bool beaten = Beaten(i);
                bool unlocked = Unlocked(i);
                bool isCurrent = unlocked && !beaten && i == current;
                _approvedMapCompleted[i] = beaten ? 1f : 0f;
                _approvedMapStates[i] = !unlocked ? 0f : beaten ? 1f : isCurrent ? 2f : 0f;
                // Every completed node keeps the connection toward the next stop lit. This makes
                // the travelled route readable immediately when the map opens.
                _approvedMapPathLit[i] = beaten && i + 1 < count ? 1f : 0f;
            }

            if (_approvedMapProgressMaterial != null)
            {
                _approvedMapProgressMaterial.SetFloatArray("_Completed", _approvedMapCompleted);
                _approvedMapProgressMaterial.SetFloatArray("_States", _approvedMapStates);
                _approvedMapProgressMaterial.SetFloatArray("_PathLit", _approvedMapPathLit);
                _approvedMapProgressMaterial.SetFloat("_CurrentIndex", current < count ? current : -1f);
                _approvedMapProgressMaterial.SetFloat("_TravelLink", -1f);
                _approvedMapProgressMaterial.SetFloat("_TravelProgress", 0f);
                _approvedMapProgressMaterial.SetVector("_TravelEndpoints", Vector4.zero);
            }

        }

        void BeginApprovedProgressAnimation(int beaten)
        {
            if (_approvedMapProgressMaterial == null) return;
            int count = Mathf.Min(CampaignLevelCount, levelButtons.Length);
            for (int i = 0; i < count; i++)
            {
                bool wasBeaten = i != beaten && Beaten(i);
                _approvedMapStates[i] = wasBeaten ? 1f : i == beaten ? 2f : 0f;
                _approvedMapPathLit[i] = i + 1 < count && wasBeaten ? 1f : 0f;
            }
            _approvedMapProgressMaterial.SetFloatArray("_States", _approvedMapStates);
            _approvedMapProgressMaterial.SetFloatArray("_PathLit", _approvedMapPathLit);
            _approvedMapProgressMaterial.SetFloat("_CurrentIndex", beaten);
            _approvedMapProgressMaterial.SetFloat("_TravelLink", beaten);
            _approvedMapProgressMaterial.SetFloat("_TravelProgress", 0f);
            if (beaten >= 0 && beaten + 1 < count)
            {
                Vector2 from = ApprovedMapNodePosition(beaten);
                Vector2 to = ApprovedMapNodePosition(beaten + 1);
                _approvedMapProgressMaterial.SetVector("_TravelEndpoints", new Vector4(
                    (from.x + 960f) / 1920f, (from.y + 540f) / 1080f,
                    (to.x + 960f) / 1920f, (to.y + 540f) / 1080f));
            }
        }

        void ApprovedNodeCompleted(int beaten)
        {
            if (_approvedMapProgressMaterial == null || beaten < 0 || beaten >= CampaignLevelCount) return;
            _approvedMapStates[beaten] = 1f;
            _approvedMapProgressMaterial.SetFloatArray("_States", _approvedMapStates);
            _approvedMapProgressMaterial.SetFloat("_CurrentIndex", -1f);
        }

        void ApprovedRoadProgress(int beaten, float progress)
        {
            if (_approvedMapProgressMaterial == null || beaten < 0 || beaten >= CampaignLevelCount - 1) return;
            _approvedMapProgressMaterial.SetFloat("_TravelLink", beaten);
            _approvedMapProgressMaterial.SetFloat("_TravelProgress", Mathf.Clamp01(progress));
            if (progress >= 0.999f)
            {
                _approvedMapPathLit[beaten] = 1f;
                _approvedMapProgressMaterial.SetFloatArray("_PathLit", _approvedMapPathLit);
            }
        }

        void ApprovedNextActivated(int next)
        {
            if (_approvedMapProgressMaterial == null) return;
            if (next >= 0 && next < Mathf.Min(CampaignLevelCount, levelButtons.Length))
            {
                _approvedMapStates[next] = 2f;
                _approvedMapProgressMaterial.SetFloatArray("_States", _approvedMapStates);
                _approvedMapProgressMaterial.SetFloat("_CurrentIndex", next);
            }
            _approvedMapProgressMaterial.SetFloat("_TravelLink", -1f);
        }

        System.Collections.IEnumerator PlayApprovedProgressLink(int beaten)
        {
            if (_approvedNativeMap)
            {
                int count = Mathf.Min(CampaignLevelCount, levelButtons.Length);
                int next = beaten + 1;
                RefreshStates();
                Sfx.Ding();

                // Rewind the two changed nodes so the player watches the completed fill land, a
                // small light travel along the existing connection, and the next node wake. No
                // bloom or full-screen effect: the complete sequence stays local and lasts 0.68s.
                RectTransform beatenNode = beaten >= 0 && beaten < count && levelButtons[beaten] != null
                    ? levelButtons[beaten].transform as RectTransform : null;
                Image beatenFill = beaten >= 0 && beaten < count && levelFills != null
                    && beaten < levelFills.Length ? levelFills[beaten] : null;
                Image beatenBorder = beaten >= 0 && beaten < count && levelBorders != null
                    && beaten < levelBorders.Length ? levelBorders[beaten] : null;
                GameObject beatenBadge = beaten >= 0 && beaten < _approvedCompletedBadges.Length
                    ? _approvedCompletedBadges[beaten] : null;
                Image nextFill = next >= 0 && next < count && levelFills != null
                    && next < levelFills.Length ? levelFills[next] : null;
                Image nextBorder = next >= 0 && next < count && levelBorders != null
                    && next < levelBorders.Length ? levelBorders[next] : null;
                GameObject nextRing = next >= 0 && next < count && levelHighlights != null
                    && next < levelHighlights.Length ? levelHighlights[next] : null;
                Text nextNumber = next >= 0 && next < count && levelNumbers != null
                    && next < levelNumbers.Length ? levelNumbers[next] : null;

                Color beatenAccent = ApprovedChapterAccent(beaten);
                Color completedFace = WithA(Lighten(beatenAccent, 0.10f), 1f);
                Color completedBorder = WithA(Lighten(beatenAccent, 0.48f), 0.96f);
                Color activeFace = WithA(Lighten(beatenAccent, 0.28f), 1f);
                Color activeBorder = WithA(Lighten(beatenAccent, 0.75f), 1f);
                if (beatenFill != null) beatenFill.color = activeFace;
                if (beatenBorder != null) beatenBorder.color = activeBorder;
                if (beatenBadge != null) beatenBadge.SetActive(false);

                Color accent = ApprovedChapterAccent(Mathf.Clamp(next, 0, count - 1));
                Color lockedAccent = ApprovedLockedAccent(accent);
                Color lockedFace = new Color(0.022f, 0.031f, 0.049f, 0.98f);
                if (nextFill != null) nextFill.color = lockedFace;
                if (nextBorder != null) nextBorder.color = WithA(lockedAccent, 0.48f);
                if (nextRing != null) nextRing.SetActive(false);
                if (nextNumber != null)
                {
                    nextNumber.rectTransform.anchoredPosition = Vector2.zero;
                    nextNumber.rectTransform.sizeDelta = new Vector2(68f, 66f);
                    nextNumber.fontSize = 20;
                    nextNumber.color = new Color(0.57f, 0.63f, 0.72f, 0.92f);
                }

                float nativeT = 0f;
                const float nativeSettle = 0.16f;
                Vector3 beatenScale = beatenNode != null ? beatenNode.localScale : Vector3.one;
                while (nativeT < nativeSettle)
                {
                    nativeT += Time.unscaledDeltaTime;
                    float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nativeT / nativeSettle));
                    if (beatenFill != null) beatenFill.color = Color.Lerp(activeFace, completedFace, p);
                    if (beatenBorder != null) beatenBorder.color = Color.Lerp(activeBorder, completedBorder, p);
                    if (beatenNode != null)
                        beatenNode.localScale = beatenScale * (1f + Mathf.Sin(p * Mathf.PI) * 0.055f);
                    yield return null;
                }
                if (beatenNode != null) beatenNode.localScale = beatenScale;
                if (beatenBadge != null) beatenBadge.SetActive(true);

                nativeT = 0f;
                const float nativeTravel = 0.36f;
                RectTransform travelLight = CreateNativeProgressLight(beaten, next, beatenAccent,
                    out Vector2 travelFrom, out Vector2 travelTo);
                while (nativeT < nativeTravel)
                {
                    nativeT += Time.unscaledDeltaTime;
                    float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nativeT / nativeTravel));
                    if (nextFill != null)
                        nextFill.color = Color.Lerp(lockedFace,
                            WithA(Lighten(accent, 0.28f), 1f), p);
                    if (nextBorder != null)
                        nextBorder.color = Color.Lerp(WithA(lockedAccent, 0.48f),
                            WithA(Lighten(accent, 0.75f), 1f), p);
                    if (travelLight != null)
                    {
                        travelLight.anchoredPosition = Vector2.Lerp(travelFrom, travelTo, p)
                            + Vector2.up * (Mathf.Sin(p * Mathf.PI) * 10f);
                        travelLight.localScale = Vector3.one * (0.80f + Mathf.Sin(p * Mathf.PI) * 0.25f);
                        Image lightImage = travelLight.GetComponent<Image>();
                        if (lightImage != null)
                            lightImage.color = WithA(Lighten(beatenAccent, 0.72f),
                                Mathf.Sin(p * Mathf.PI) * 0.94f);
                    }
                    yield return null;
                }
                if (travelLight != null) Destroy(travelLight.gameObject);

                RefreshStates();
                if (next >= 0 && next < count) Sfx.Ding();
                nativeT = 0f;
                const float nativeActivate = 0.16f;
                RectTransform nextNode = next >= 0 && next < count && levelButtons[next] != null
                    ? levelButtons[next].transform as RectTransform : null;
                Vector3 nextScale = nextNode != null ? nextNode.localScale : Vector3.one;
                while (nativeT < nativeActivate)
                {
                    nativeT += Time.unscaledDeltaTime;
                    float p = Mathf.Clamp01(nativeT / nativeActivate);
                    if (nextNode != null)
                        nextNode.localScale = nextScale * (1f + Mathf.Sin(p * Mathf.PI) * 0.065f);
                    yield return null;
                }
                if (nextNode != null) nextNode.localScale = nextScale;
                yield break;
            }

            BeginApprovedProgressAnimation(beaten);
            ApprovedNodeCompleted(beaten);
            Sfx.Ding();
            float t = 0f;
            const float settle = 0.12f;
            while (t < settle) { t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            const float travel = 0.42f;
            while (t < travel)
            {
                t += Time.unscaledDeltaTime;
                ApprovedRoadProgress(beaten, Mathf.Clamp01(t / travel));
                yield return null;
            }
            ApprovedNextActivated(beaten + 1);
            if (beaten + 1 < Mathf.Min(CampaignLevelCount, levelButtons.Length)) Sfx.Ding();
            t = 0f;
            const float activate = 0.12f;
            while (t < activate) { t += Time.unscaledDeltaTime; yield return null; }
        }

        RectTransform CreateNativeProgressLight(int beaten, int next, Color accent,
            out Vector2 from, out Vector2 to)
        {
            from = to = Vector2.zero;
            if (mapCam == null || levelButtons == null || beaten < 0 || next < 0
                || beaten >= levelButtons.Length || next >= levelButtons.Length
                || levelButtons[beaten] == null || levelButtons[next] == null)
                return null;

            from = mapCam.InverseTransformPoint(levelButtons[beaten].transform.position);
            to = mapCam.InverseTransformPoint(levelButtons[next].transform.position);

            GameObject go = new GameObject("ProgressTravelLight", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(mapCam, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = from;
            rect.sizeDelta = new Vector2(13f, 13f);
            rect.SetAsLastSibling();

            Image image = go.GetComponent<Image>();
            image.sprite = fxCell != null ? fxCell
                : levelFills != null && beaten < levelFills.Length && levelFills[beaten] != null
                    ? levelFills[beaten].sprite : null;
            image.color = WithA(Lighten(accent, 0.72f), 0f);
            image.raycastTarget = false;
            image.preserveAspect = true;
            return rect;
        }

        static Vector2 ApprovedMapNodePosition(int index)
        {
            Vector2[] positions =
            {
                new Vector2(-837.1f,226.7f), new Vector2(-737.2f,226.7f), new Vector2(-635.0f,226.7f), new Vector2(-793.5f,75.2f), new Vector2(-683.3f,75.2f), new Vector2(-793.5f,-61.4f), new Vector2(-683.3f,-61.4f), new Vector2(-840.6f,-203.7f), new Vector2(-737.2f,-203.7f), new Vector2(-633.9f,-203.7f),
                new Vector2(-475.4f,226.7f), new Vector2(-369.8f,226.7f), new Vector2(-265.3f,226.7f), new Vector2(-428.3f,75.2f), new Vector2(-316.9f,75.2f), new Vector2(-428.3f,-61.4f), new Vector2(-316.9f,-61.4f), new Vector2(-477.7f,-203.7f), new Vector2(-370.9f,-203.7f), new Vector2(-265.3f,-203.7f),
                new Vector2(-110.2f,226.7f), new Vector2(-4.6f,226.7f), new Vector2(97.6f,226.7f), new Vector2(-60.9f,75.2f), new Vector2(50.5f,75.2f), new Vector2(-60.9f,-61.4f), new Vector2(49.4f,-61.4f), new Vector2(-111.4f,-203.7f), new Vector2(-6.9f,-203.7f), new Vector2(97.6f,-203.7f),
                new Vector2(254.9f,226.7f), new Vector2(359.4f,226.7f), new Vector2(462.8f,226.7f), new Vector2(299.7f,75.2f), new Vector2(413.4f,75.2f), new Vector2(299.7f,-61.4f), new Vector2(413.4f,-61.4f), new Vector2(252.6f,-203.7f), new Vector2(358.3f,-203.7f), new Vector2(462.8f,-203.7f),
                new Vector2(621.2f,226.7f), new Vector2(724.6f,226.7f), new Vector2(826.8f,226.7f), new Vector2(667.2f,75.2f), new Vector2(782.0f,75.2f), new Vector2(667.2f,-61.4f), new Vector2(782.0f,-61.4f), new Vector2(620.1f,-203.7f), new Vector2(724.6f,-203.7f), new Vector2(827.9f,-203.7f)
            };
            return positions[Mathf.Clamp(index, 0, positions.Length - 1)];
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
            {
                PlayerPrefs.DeleteKey(BestKey(i));
                PlayerPrefs.DeleteKey(GameManager.SessionClearKey(i));
                PlayerPrefs.DeleteKey(GameManager.MechanicBriefingKey(i));
            }
            ScoreSystem.Reset(levelButtons.Length);
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
            // Static artwork is the shipping presentation.  This guard is intentionally inside
            // the builder as well as at its call sites so no cloud-progress callback or future
            // menu refresh can accidentally recreate the oversized board.
            if (useStaticHomeArtwork) return;
            if (levelPrefabs == null || levelPrefabs.Length == 0 || floorPrefab == null || boardThemes == null || boardThemes.Length == 0) return;
            // Arriving out of the finale, the board MUST be the one the game just pulled away from —
            // same level, same geometry, same world position — or the hand-off is a cut instead of
            // a continuation. Any other time the title represents a fresh run from Level 1.
            int want = PlayerPrefs.GetInt("Parabox.SeamlessOut", 0) == 1
                ? PlayerPrefs.GetInt("Parabox.OutLevel", 0)
                : PlayerPrefs.GetInt(LevelKey, NewGameLevel);
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

        void RemoveLiveBoardPreview()
        {
            if (_boardRoot != null)
            {
                _boardRoot.gameObject.SetActive(false);
                Destroy(_boardRoot.gameObject);
            }
            _boardRoot = null;
            _boardGlow = null;
            _ambience = null;
            _model = null;
            _roomRoots.Clear();
            _views.Clear();
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

            // The approved title art already contains the complete, precisely placed diorama.
            // Keep it pixel-stable instead of drawing and drifting a second world-space preview
            // over the authored image. Gameplay and level-map loading still use the same model.
            if (useStaticHomeArtwork) return;

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
            // PLAY always begins the campaign at Level 1.
            transitioning = true;
            StartCoroutine(DiveIntoBoard(NewGameLevel));
        }

        // Physically fly the menu camera INTO the world board — from the O framing to the EXACT gameplay
        // framing — fading the UI out, then hand off to the game at that identical frame (seamless).
        System.Collections.IEnumerator DiveIntoBoard(int level)
        {
            Sfx.Ding();
            if (homeGroup != null) { homeGroup.interactable = false; homeGroup.blocksRaycasts = false; }
            if (useStaticHomeArtwork || menuCam == null || _model == null)
            {
                // No board was shown on the static menu, so this is a normal game entrance rather
                // than a seamless camera hand-off.
                LoadGame(level, false);
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
            // A previous build used this persisted flag for a special Level 50 entrance. Clear
            // it so existing browser saves cannot re-enable that obsolete path after this fix.
            PlayerPrefs.DeleteKey("Parabox.FinalRun");
            if (seamless) PlayerPrefs.SetInt("Parabox.Seamless", 1);   // menu already showed this board → no fade/fly-in
            else PlayerPrefs.SetInt("Parabox.FlyIn", 1);               // level-select: use the in-game fly-in
            PlayerPrefs.Save();                                        // commit the selected level before changing scenes
            LuxoddGameService.SyncProgress();
            SceneManager.LoadScene("Game");
        }

        void Quit()
        {
            LuxoddGameService.ReturnToSystem();
        }
    }
}
