using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Calm failure presentation over the intact board. Everything uses unscaled time because
    // gameplay is frozen while the leaderboard and Luxodd transaction are visible.
    public class LoseFx : MonoBehaviour
    {
        [Header("Layers")]
        public CanvasGroup dim;            // full-screen darkening
        public Image pulse;                // full-screen flash — the impact
        public CanvasGroup titleGroup;
        public RectTransform titleRT;
        public Text titleText;
        public Text subText;
        public GameObject burst;           // UIBurst — fires when enabled

        [Header("Player actions")]
        public CanvasGroup restartGroup;
        public RectTransform restartRT;
        public CanvasGroup levelsGroup;
        public RectTransform levelsRT;

        [Header("Leaderboard")]
        public LossLeaderboard leaderboard;
        public Sprite leaderboardPanelSprite;
        public Color leaderboardAccent = new Color(0.27f, 0.88f, 0.85f, 1f);
        public Color leaderboardBacking = new Color(0.03f, 0.12f, 0.19f, 1f);

        [Header("World")]
        public CameraFollow shakeTarget;   // one short controlled impact jolt on failure

        [Header("Timing")]
        public float freeze = 0.20f;       // stunned beat before anything moves
        public float breakHold = 0.95f;    // let the board's destruction actually play out before dimming over it
        public float dimDur = 0.50f;
        public float titleDur = 0.42f;
        public float dimTo = 0.72f;        // not full black — the debris stays readable through it
        public float transactionDelay = 5f; // visible GAME OVER countdown before returning to Luxodd

        Coroutine running;
        Coroutine countdownRunning;
        Vector2 titleRest;
        string returnReason;
        bool showReturnCountdown;
        float returnDeadlineRealtime;

        void Awake()
        {
            if (leaderboard == null || !leaderboard.IsFullyPrebuilt)
                Debug.LogError("[Parabox] Loss leaderboard is not prebuilt. Run Tools/Parabox/Generate Prebuilt UI (Run This) before Play or Build.");
            // Luxodd owns every post-loss choice. Legacy scene references are deliberately hidden
            // here as well as omitted by the scene generator so an older serialized Game scene
            // cannot bring the local CONTINUE / LEVELS row back.
            if (titleRT != null) titleRT.anchoredPosition += new Vector2(-300f, 0f);
            if (restartRT != null) restartRT.gameObject.SetActive(false);
            if (levelsRT != null) levelsRT.gameObject.SetActive(false);

            // Cache the authored resting positions once. The loss animation only moves each
            // element a few pixels around these points, so it cannot drift after repeated plays.
            if (titleRT != null)
            {
                titleRT.anchoredPosition = new Vector2(titleRT.anchoredPosition.x, 40f);
                titleRest = titleRT.anchoredPosition;
            }
        }

        public void SetLeaderboardTheme(Color accent, Color backing)
        {
            leaderboardAccent = accent;
            leaderboardBacking = backing;
            if (leaderboard != null) leaderboard.ApplyTheme(accent, backing);
        }

        public void SetScoreSummary(int levelScore, int totalScore)
        {
            if (leaderboard != null)
                leaderboard.SetPlayerScoreSummary(levelScore, totalScore);
        }

#if UNITY_EDITOR
        public void PrebuildStaticUi()
        {
            if (leaderboard == null)
                leaderboard = GetComponentInChildren<LossLeaderboard>(true);
            if (leaderboard != null && leaderboard.IsFullyPrebuilt) return;
            if (leaderboard != null)
            {
                UnityEngine.Object.DestroyImmediate(leaderboard.gameObject);
                leaderboard = null;
            }
            Font font = titleText != null ? titleText.font : null;
            Sprite panelSprite = leaderboardPanelSprite;
            if (panelSprite == null && restartRT != null)
            {
                var button = restartRT.GetComponentInChildren<Button>(true);
                if (button != null && button.image != null) panelSprite = button.image.sprite;
            }
            leaderboard = LossLeaderboard.Create(transform, font, panelSprite,
                leaderboardAccent, leaderboardBacking);
        }
#endif

        public void Play(string title, string sub, Action onTransactionReady)
        {
            showReturnCountdown = false;
            if (countdownRunning != null) StopCoroutine(countdownRunning);
            countdownRunning = null;
            gameObject.SetActive(true);
            if (titleText != null) titleText.text = title;
            if (subText != null) subText.text = sub;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run(onTransactionReady));
        }

        public void PlayGameOver(string reason, Action onReturnToArcade)
        {
            showReturnCountdown = true;
            returnReason = string.IsNullOrWhiteSpace(reason) ? "SESSION ENDED" : reason.Trim().ToUpperInvariant();
            gameObject.SetActive(true);
            if (titleText != null) titleText.text = "GAME OVER";
            returnDeadlineRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, transactionDelay);
            SetReturnCountdownText(Mathf.CeilToInt(Mathf.Max(1f, transactionDelay)));
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run(onReturnToArcade));
            if (countdownRunning != null) StopCoroutine(countdownRunning);
            countdownRunning = StartCoroutine(UpdateReturnCountdown());
        }

        public static string ReturnMessage(string reason, int seconds)
        {
            string clearReason = string.IsNullOrWhiteSpace(reason)
                ? "SESSION ENDED" : reason.Trim().ToUpperInvariant();
            return clearReason + "  •  RETURNING TO THE ARCADE IN "
                + Mathf.Max(1, seconds) + "...";
        }

        void SetReturnCountdownText(int seconds)
        {
            if (subText != null) subText.text = ReturnMessage(returnReason, seconds);
        }

        public void DismissImmediate()
        {
            if (running != null) StopCoroutine(running);
            StopAllCoroutines();
            running = null;
            countdownRunning = null;
            showReturnCountdown = false;

            if (dim != null)
            {
                dim.alpha = 0f;
                dim.interactable = false;
                dim.blocksRaycasts = false;
            }
            if (pulse != null)
            {
                Color color = pulse.color;
                color.a = 0f;
                pulse.color = color;
                pulse.raycastTarget = false;
            }
            if (titleGroup != null) titleGroup.alpha = 0f;
            if (restartGroup != null)
            {
                restartGroup.alpha = 0f;
                restartGroup.interactable = false;
                restartGroup.blocksRaycasts = false;
            }
            if (levelsGroup != null)
            {
                levelsGroup.alpha = 0f;
                levelsGroup.interactable = false;
                levelsGroup.blocksRaycasts = false;
            }
            if (leaderboard != null && leaderboard.Group != null)
            {
                leaderboard.Group.alpha = 0f;
                leaderboard.Group.interactable = false;
                leaderboard.Group.blocksRaycasts = false;
            }
            if (burst != null) burst.SetActive(false);
            gameObject.SetActive(false);
        }

        // Fast failure feedback. The board-light wave itself is started by GameManager so it can
        // reuse BoardWinFx, the same renderer used when a level is solved.
        public void PlayFast(Action onBoardLight, Action onComplete)
        {
            gameObject.SetActive(true);
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(RunFast(onBoardLight, onComplete));
        }

        IEnumerator RunFast(Action onBoardLight, Action onComplete)
        {
            Prime();

            float t = 0f;
            while (t < 0.15f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            onBoardLight?.Invoke();
            if (shakeTarget != null) shakeTarget.Shake(0.10f, 13f);

            const float responseDuration = 0.50f;
            t = 0f;
            while (t < responseDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / responseDuration);
                float red = Mathf.Sin(k * Mathf.PI) * 0.14f;
                if (pulse != null)
                {
                    Color c = pulse.color;
                    c.a = red;
                    pulse.color = c;
                }
                if (dim != null) dim.alpha = Mathf.SmoothStep(0f, 0.06f, k);
                yield return null;
            }

            if (pulse != null)
            {
                Color c = pulse.color;
                c.a = 0f;
                pulse.color = c;
            }
            if (dim != null) dim.alpha = 0f;
            running = null;
            onComplete?.Invoke();
        }

        void Prime()
        {
            if (dim != null) { dim.alpha = 0f; dim.blocksRaycasts = true; }
            if (pulse != null) pulse.color = new Color(pulse.color.r, pulse.color.g, pulse.color.b, 0f);
            if (titleGroup != null) titleGroup.alpha = 0f;
            if (leaderboard != null && leaderboard.Group != null)
            {
                leaderboard.Group.alpha = 0f;
                leaderboard.Group.interactable = false;
                leaderboard.Group.blocksRaycasts = false;
                leaderboard.Rect.localScale = Vector3.one * 0.94f;
            }
            if (restartGroup != null)
            {
                restartGroup.alpha = 0f;
                restartGroup.interactable = false;
                restartGroup.blocksRaycasts = false;
            }
            if (levelsGroup != null)
            {
                levelsGroup.alpha = 0f;
                levelsGroup.interactable = false;
                levelsGroup.blocksRaycasts = false;
            }
            if (burst != null) burst.SetActive(false);
        }

        IEnumerator Run(Action onTransactionReady)
        {
            Prime();

            // Give failure physical impact, like the win celebration, without returning to the
            // old board explosion. This is one short decaying jolt: strong enough to feel on an
            // arcade display, finished before the recovery buttons become interactive.
            if (shakeTarget != null) shakeTarget.Shake(0.18f, 9.5f);

            // One red wash lands on the same beat. The board itself stays completely intact:
            // no fragments, destruction or abrupt zoom.
            StartCoroutine(Pulse());

            float t = 0f;
            const float simpleDimDuration = 0.36f;
            while (t < simpleDimDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / simpleDimDuration));
                if (dim != null) dim.alpha = k * dimTo;
                yield return null;
            }
            if (dim != null) dim.alpha = dimTo;

            // The verdict makes a clear 54-pixel entrance. SmoothStep deliberately avoids the
            // rubbery overshoot that made the previous loss animation feel strange.
            t = 0f;
            while (t < titleDur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / titleDur));
                if (titleGroup != null) titleGroup.alpha = k;
                if (titleRT != null)
                {
                    titleRT.localScale = Vector3.one * Mathf.Lerp(0.76f, 1f, k);
                    titleRT.anchoredPosition = Vector2.Lerp(titleRest + Vector2.down * 54f,
                                                            titleRest, k);
                }
                yield return null;
            }
            if (titleGroup != null) titleGroup.alpha = 1f;
            if (titleRT != null)
            {
                titleRT.localScale = Vector3.one;
                titleRT.anchoredPosition = titleRest;
            }

            // ---- 5. the top ten settles beside the verdict -------------------------------
            yield return RevealLeaderboard();

            // Keep the end state explicit while the leaderboard remains readable. There are no
            // local actions: expiry returns ownership to the Luxodd arcade shell automatically.
            float delay = showReturnCountdown
                ? Mathf.Max(0f, returnDeadlineRealtime - Time.realtimeSinceStartup)
                : Mathf.Max(0f, transactionDelay);
            while (delay > 0f)
            {
                delay -= Time.unscaledDeltaTime;
                yield return null;
            }

            running = null;
            onTransactionReady?.Invoke();
        }

        IEnumerator UpdateReturnCountdown()
        {
            int displayedSecond = -1;
            while (showReturnCountdown)
            {
                float remaining = returnDeadlineRealtime - Time.realtimeSinceStartup;
                if (remaining <= 0f) break;
                int second = Mathf.Max(1, Mathf.CeilToInt(remaining));
                if (second != displayedSecond)
                {
                    displayedSecond = second;
                    SetReturnCountdownText(second);
                }
                yield return null;
            }
            countdownRunning = null;
        }

        IEnumerator RevealLeaderboard()
        {
            if (leaderboard == null || leaderboard.Group == null) yield break;
            const float duration = 0.38f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                leaderboard.Group.alpha = k;
                leaderboard.Rect.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, k);
                yield return null;
            }
            leaderboard.Group.alpha = 1f;
            leaderboard.Rect.localScale = Vector3.one;
        }

        // A single readable red wash: clearly animated, but still far below a hard white flash.
        IEnumerator Pulse()
        {
            if (pulse == null) yield break;
            const float duration = 0.54f;
            const float peak = 0.30f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float a = Mathf.Sin(k * Mathf.PI) * peak;
                var c = pulse.color;
                c.a = a;
                pulse.color = c;
                yield return null;
            }
            var f = pulse.color;
            f.a = 0f;
            pulse.color = f;
        }
    }
}
