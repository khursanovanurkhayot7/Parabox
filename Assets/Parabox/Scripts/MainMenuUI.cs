using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox
{
    public class MainMenuUI : MonoBehaviour
    {
        public Button playButton;
        public Button quitButton;
        public Button[] levelButtons;
        public GameObject[] levelChecks;   // per-level "beaten" badge, toggled here
        public Text progressLabel;

        const string LevelKey = "Parabox.Level";
        static string BestKey(int level) => "Parabox.Best." + level;

        void Start()
        {
            Sfx.Init();

            playButton.onClick.AddListener(() => StartLevel(FirstUnbeaten()));
            quitButton.onClick.AddListener(Quit);

            for (int i = 0; i < levelButtons.Length; i++)
            {
                int index = i;
                levelButtons[i].onClick.AddListener(() => StartLevel(index));
            }

            RefreshProgress();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.mKey.wasPressedThisFrame) Sfx.ToggleMute();
            if (kb.deleteKey.wasPressedThisFrame) ResetProgress();
        }

        // "PLAY" jumps to the first level you haven't beaten yet (or level 1).
        int FirstUnbeaten()
        {
            for (int i = 0; i < levelButtons.Length; i++)
                if (!PlayerPrefs.HasKey(BestKey(i))) return i;
            return 0;
        }

        void RefreshProgress()
        {
            int done = 0;
            for (int i = 0; i < levelButtons.Length; i++)
            {
                bool beaten = PlayerPrefs.HasKey(BestKey(i));
                if (beaten) done++;
                if (levelChecks != null && i < levelChecks.Length && levelChecks[i] != null)
                    levelChecks[i].SetActive(beaten);
            }

            if (progressLabel != null)
            {
                int total = levelButtons.Length;
                progressLabel.text = done >= total
                    ? "★  All levels complete!  ★"
                    : $"Completed {done} / {total}   —   Del to reset";
            }
        }

        void ResetProgress()
        {
            for (int i = 0; i < levelButtons.Length; i++)
                PlayerPrefs.DeleteKey(BestKey(i));
            PlayerPrefs.Save();
            RefreshProgress();
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
