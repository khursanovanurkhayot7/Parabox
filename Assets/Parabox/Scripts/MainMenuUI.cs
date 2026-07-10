using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox
{
    // Main menu + a three-tier level-selection flow:
    //   Home  ->  Category select (Beginner / Intermediate / Advanced)  ->  that tier's 10 levels.
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Home")]
        public Button playButton;
        public Button levelsButton;
        public Button quitButton;

        [Header("Category select")]
        public UIScreen categorySelectScreen;
        public Button[] categoryButtons;       // one per category
        public Text[] categoryProgress;        // "x / 10" per category
        public GameObject[] categoryLocks;     // shown when a category isn't reachable yet
        public Button categoryBackButton;

        [Header("Level grids (one screen per category)")]
        public UIScreen[] gridScreens;
        public Button[] gridBackButtons;

        [Header("Levels (all 30, indexed globally)")]
        public Button[] levelButtons;
        public GameObject[] levelChecks;
        public GameObject[] levelLocks;
        public GameObject[] levelHighlights;

        [Header("Testing")]
        [Tooltip("When ON, every level is playable regardless of progress (no locks). Turn OFF to ship with gated progression.")]
        public bool unlockAllForTesting = true;

        public const int PerCategory = 10;

        const string LevelKey = "Parabox.Level";
        static string BestKey(int level) => "Parabox.Best." + level;

        int screen;   // 0 = home, 1 = category select, 2 = a level grid
        int openCat;

        void Start()
        {
            Sfx.Init();

            playButton.onClick.AddListener(() => StartLevel(FirstUnbeaten()));
            quitButton.onClick.AddListener(Quit);
            if (levelsButton != null) levelsButton.onClick.AddListener(OpenCategories);
            if (categoryBackButton != null) categoryBackButton.onClick.AddListener(CloseCategories);

            if (categoryButtons != null)
                for (int c = 0; c < categoryButtons.Length; c++)
                {
                    int cat = c;
                    if (categoryButtons[c] != null) categoryButtons[c].onClick.AddListener(() => OpenCategory(cat));
                }

            if (gridBackButtons != null)
                for (int c = 0; c < gridBackButtons.Length; c++)
                    if (gridBackButtons[c] != null) gridBackButtons[c].onClick.AddListener(BackToCategories);

            for (int i = 0; i < levelButtons.Length; i++)
            {
                int index = i;
                if (levelButtons[i] != null) levelButtons[i].onClick.AddListener(() => TryStart(index));
            }

            RefreshStates();

            // returning from a "Time's Up" screen jumps straight to that level's tier grid
            if (PlayerPrefs.GetInt("Parabox.OpenLevels", 0) == 1)
            {
                PlayerPrefs.DeleteKey("Parabox.OpenLevels");
                PlayerPrefs.Save();
                int lvl = PlayerPrefs.GetInt(LevelKey, 0);
                int tier = (gridScreens != null && gridScreens.Length > 0)
                    ? Mathf.Clamp(lvl / PerCategory, 0, gridScreens.Length - 1) : 0;
                OpenCategory(tier);
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.mKey.wasPressedThisFrame) Sfx.ToggleMute();
            if (kb.deleteKey.wasPressedThisFrame) ResetProgress();
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (screen == 2) BackToCategories();
                else if (screen == 1) CloseCategories();
            }
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
        void OpenCategories()
        {
            Sfx.Ding();
            RefreshStates();
            screen = 1;
            if (categorySelectScreen != null) categorySelectScreen.Show();
        }

        void CloseCategories()
        {
            Sfx.Click();
            screen = 0;
            if (categorySelectScreen != null) categorySelectScreen.Hide();
        }

        void OpenCategory(int c)
        {
            Sfx.Ding();
            openCat = c;
            RefreshStates();
            screen = 2;
            if (categorySelectScreen != null) categorySelectScreen.Hide();
            if (gridScreens != null && c < gridScreens.Length && gridScreens[c] != null) gridScreens[c].Show();
        }

        void BackToCategories()
        {
            Sfx.Click();
            screen = 1;
            if (gridScreens != null && openCat < gridScreens.Length && gridScreens[openCat] != null) gridScreens[openCat].Hide();
            if (categorySelectScreen != null) categorySelectScreen.Show();
        }

        void TryStart(int index)
        {
            if (!Unlocked(index)) { Sfx.Blocked(); return; }
            StartLevel(index);
        }

        // ---------------------------------------------------------- state paint
        void RefreshStates()
        {
            int total = levelButtons.Length;
            int current = CurrentLevel();

            for (int i = 0; i < total; i++)
            {
                bool beaten = Beaten(i);
                bool unlocked = Unlocked(i);
                bool isCurrent = unlocked && !beaten && i == current;

                if (levelChecks != null && i < levelChecks.Length && levelChecks[i] != null)
                    levelChecks[i].SetActive(beaten);
                if (levelLocks != null && i < levelLocks.Length && levelLocks[i] != null)
                    levelLocks[i].SetActive(!unlocked);
                if (levelHighlights != null && i < levelHighlights.Length && levelHighlights[i] != null)
                    levelHighlights[i].SetActive(isCurrent);
                if (levelButtons[i] != null) levelButtons[i].interactable = unlocked;
            }

            if (categoryProgress != null)
                for (int c = 0; c < categoryProgress.Length; c++)
                {
                    int done = 0;
                    for (int k = 0; k < PerCategory; k++)
                    {
                        int idx = c * PerCategory + k;
                        if (idx < total && Beaten(idx)) done++;
                    }
                    if (categoryProgress[c] != null)
                        categoryProgress[c].text = done >= PerCategory ? "Complete  ★" : done + " / " + PerCategory;

                    // a category is "locked" until the previous tier's last level is beaten
                    bool catLocked = !unlockAllForTesting && c > 0 && !Beaten(c * PerCategory - 1);
                    if (categoryLocks != null && c < categoryLocks.Length && categoryLocks[c] != null)
                        categoryLocks[c].SetActive(catLocked);
                }
        }

        void ResetProgress()
        {
            for (int i = 0; i < levelButtons.Length; i++)
                PlayerPrefs.DeleteKey(BestKey(i));
            PlayerPrefs.Save();
            RefreshStates();
        }

        void StartLevel(int index)
        {
            Sfx.Ding();
            PlayerPrefs.SetInt(LevelKey, index);
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
