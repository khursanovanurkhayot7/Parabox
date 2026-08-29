using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Parabox
{
    // A single, prebuilt Level-1 safety lesson. GameManager owns when the grace is available;
    // this component presents it as a voiced character conversation and returns the confirmed
    // action. The player portrait and every UI reference are authored by the Edit-Mode installer.
    public sealed class FirstLifeLessonFx : MonoBehaviour
    {
        const string DefaultMessage =
            "ONE MORE CHANCE!\nI ONLY GET ONE LIFE.\nUSE UNDO OR RESTART!";

        [Header("Lesson chrome")]
        public CanvasGroup group;
        public RectTransform card;
        public Button tryAgainButton;

        [Header("Player dialogue")]
        public RectTransform player;
        public CanvasGroup playerGroup;
        public RectTransform speechBubble;
        public CanvasGroup speechGroup;
        public Text speechText;
        public RectTransform mouth;
        public RectTransform teachingArm;
        public RectTransform pointer;
        public AudioSource voiceSource;
        public AudioClip voiceClip;
        [TextArea(2, 5)] public string spokenMessage = DefaultMessage;
        [Min(1f)] public float charactersPerSecond = 17f;

        Action onTryAgain;
        Coroutine entrance;
        bool accepted;
        bool messageComplete;
        Vector3 cardRest = Vector3.one;
        Vector2 playerRestPosition;
        Vector3 playerRestScale = Vector3.one;
        Quaternion playerRestRotation = Quaternion.identity;
        Vector3 bubbleRestScale = Vector3.one;
        Vector3 mouthRestScale = Vector3.one;
        Quaternion teachingArmRestRotation = Quaternion.identity;
        Quaternion pointerRestRotation = Quaternion.identity;

        void Awake()
        {
            if (card != null)
            {
                cardRest = card.localScale;
                if (cardRest.sqrMagnitude < 0.01f) cardRest = Vector3.one;
            }
            if (player != null)
            {
                playerRestPosition = player.anchoredPosition;
                playerRestScale = player.localScale;
                if (playerRestScale.sqrMagnitude < 0.01f) playerRestScale = Vector3.one;
                playerRestRotation = player.localRotation;
            }
            if (speechBubble != null)
            {
                bubbleRestScale = speechBubble.localScale;
                if (bubbleRestScale.sqrMagnitude < 0.01f) bubbleRestScale = Vector3.one;
            }
            if (mouth != null)
            {
                mouthRestScale = mouth.localScale;
                if (mouthRestScale.sqrMagnitude < 0.01f) mouthRestScale = Vector3.one;
            }
            if (teachingArm != null) teachingArmRestRotation = teachingArm.localRotation;
            if (pointer != null) pointerRestRotation = pointer.localRotation;
            if (voiceSource != null)
            {
                voiceSource.playOnAwake = false;
                voiceSource.loop = false;
                voiceSource.spatialBlend = 0f;
                voiceSource.ignoreListenerPause = true;
                voiceSource.clip = voiceClip;
            }
            if (string.IsNullOrWhiteSpace(spokenMessage))
                spokenMessage = speechText != null && !string.IsNullOrWhiteSpace(speechText.text)
                    ? speechText.text : DefaultMessage;
            if (tryAgainButton != null)
            {
                tryAgainButton.onClick.RemoveListener(ChooseTryAgain);
                tryAgainButton.onClick.AddListener(ChooseTryAgain);
            }
        }

        public void Show(Action callback)
        {
            onTryAgain = callback;
            accepted = false;
            messageComplete = false;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }
            if (speechText != null) speechText.text = string.Empty;
            if (tryAgainButton != null) tryAgainButton.interactable = false;
            if (voiceSource != null) voiceSource.Stop();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (entrance != null) StopCoroutine(entrance);
            entrance = StartCoroutine(AnimateConversation());
        }

        // The first confirm finishes an unfinished sentence; the next one accepts the retry.
        // This keeps fast arcade input responsive without letting the teaching line disappear.
        public void Confirm()
        {
            if (!messageComplete)
            {
                CompleteMessage();
                return;
            }
            ChooseTryAgain();
        }

        void ChooseTryAgain()
        {
            if (accepted || !messageComplete) return;
            accepted = true;
            Action callback = onTryAgain;
            onTryAgain = null;
            DismissImmediate();
            callback?.Invoke();
        }

        public void DismissImmediate()
        {
            if (entrance != null) StopCoroutine(entrance);
            entrance = null;
            if (EventSystem.current != null && tryAgainButton != null
                && EventSystem.current.currentSelectedGameObject == tryAgainButton.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            if (group != null)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            if (playerGroup != null) playerGroup.alpha = 1f;
            if (speechGroup != null) speechGroup.alpha = 1f;
            if (card != null) card.localScale = cardRest;
            if (player != null)
            {
                player.anchoredPosition = playerRestPosition;
                player.localScale = playerRestScale;
                player.localRotation = playerRestRotation;
            }
            if (speechBubble != null) speechBubble.localScale = bubbleRestScale;
            if (voiceSource != null) voiceSource.Stop();
            CloseMouth();
            RestoreTeachingGesture();
            if (speechText != null) speechText.text = spokenMessage;
            gameObject.SetActive(false);
        }

        IEnumerator AnimateConversation()
        {
            const float introDuration = 0.62f;
            float elapsed = 0f;
            if (playerGroup != null) playerGroup.alpha = 0f;
            if (speechGroup != null) speechGroup.alpha = 0f;

            while (elapsed < introDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / introDuration);
                float cardT = EaseOutCubic(t);
                float playerT = EaseOutBack(t);
                float bubbleT = EaseOutBack(Mathf.Clamp01((t - 0.30f) / 0.70f));

                if (card != null)
                    card.localScale = cardRest * Mathf.Lerp(0.93f, 1f, cardT);
                if (player != null)
                {
                    player.anchoredPosition = Vector2.LerpUnclamped(
                        playerRestPosition + new Vector2(-190f, -34f), playerRestPosition, playerT);
                    player.localScale = playerRestScale
                        * Mathf.LerpUnclamped(0.42f, 1f, playerT);
                    player.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Lerp(-16f, 0f, cardT)) * playerRestRotation;
                }
                if (playerGroup != null) playerGroup.alpha = Mathf.Clamp01(t * 2.3f);
                if (speechBubble != null)
                    speechBubble.localScale = bubbleRestScale
                        * Mathf.LerpUnclamped(0.72f, 1f, bubbleT);
                if (speechGroup != null)
                    speechGroup.alpha = Mathf.Clamp01((t - 0.30f) / 0.34f);
                yield return null;
            }

            RestoreConversationTransforms();
            yield return new WaitForSecondsRealtime(0.08f);

            string message = string.IsNullOrWhiteSpace(spokenMessage)
                ? DefaultMessage : spokenMessage;
            float effectiveCharactersPerSecond = charactersPerSecond;
            if (voiceSource != null && voiceClip != null)
            {
                voiceSource.clip = voiceClip;
                voiceSource.Play();
                // Match the typing and mouth motion to the recorded line. Punctuation carries
                // extra weight, preserving the voice's natural pauses instead of rushing words.
                effectiveCharactersPerSecond = MessageTypingWeight(message)
                    / Mathf.Max(0.25f, voiceClip.length - 0.06f);
            }
            int count = 0;
            float timeUntilNextCharacter = 0f;
            char currentCharacter = ' ';
            while (!messageComplete)
            {
                timeUntilNextCharacter -= Time.unscaledDeltaTime;
                if (timeUntilNextCharacter <= 0f)
                {
                    if (count >= message.Length)
                    {
                        CompleteMessage();
                        yield return null;
                        continue;
                    }
                    currentCharacter = message[count];
                    count++;
                    if (speechText != null) speechText.text = message.Substring(0, count);
                    timeUntilNextCharacter = CharacterDelay(currentCharacter,
                        effectiveCharactersPerSecond);
                }
                AnimateTalkingMouth(currentCharacter);
                AnimateTeachingGesture(true);
                ApplyPlayerIdle();
                yield return null;
            }

            while (gameObject.activeSelf && !accepted)
            {
                ApplyPlayerIdle();
                CloseMouth();
                AnimateTeachingGesture(false);
                yield return null;
            }
            entrance = null;
        }

        void CompleteMessage()
        {
            if (messageComplete) return;
            messageComplete = true;
            if (speechText != null)
                speechText.text = string.IsNullOrWhiteSpace(spokenMessage)
                    ? DefaultMessage : spokenMessage;
            CloseMouth();
            if (tryAgainButton != null)
            {
                tryAgainButton.interactable = true;
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(tryAgainButton.gameObject);
            }
        }

        void RestoreConversationTransforms()
        {
            if (card != null) card.localScale = cardRest;
            if (player != null)
            {
                player.anchoredPosition = playerRestPosition;
                player.localScale = playerRestScale;
                player.localRotation = playerRestRotation;
            }
            if (speechBubble != null) speechBubble.localScale = bubbleRestScale;
            if (playerGroup != null) playerGroup.alpha = 1f;
            if (speechGroup != null) speechGroup.alpha = 1f;
            RestoreTeachingGesture();
        }

        void ApplyPlayerIdle()
        {
            if (player == null) return;
            float bob = Mathf.Sin(Time.unscaledTime * 3.1f) * 4f;
            player.anchoredPosition = playerRestPosition + new Vector2(0f, bob);
            player.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(Time.unscaledTime * 2.2f) * 1.4f) * playerRestRotation;
        }

        // Basic silent lip-sync: closed lips for pauses and B/M/P, round lips for O/U,
        // wider open shapes for vowels, and a smaller speaking shape for other consonants.
        void AnimateTalkingMouth(char character)
        {
            if (mouth == null) return;
            char upper = char.ToUpperInvariant(character);
            if (char.IsWhiteSpace(upper) || ".,!?:;".IndexOf(upper) >= 0)
            {
                CloseMouth();
                return;
            }

            float pulse = 0.86f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 19f)) * 0.14f;
            float width = 0.92f;
            float height = 0.48f;
            if ("BMP".IndexOf(upper) >= 0)
            {
                width = 1f;
                height = 0.18f;
            }
            else if ("OUQW".IndexOf(upper) >= 0)
            {
                width = 0.64f;
                height = 1.05f;
            }
            else if ("AEIY".IndexOf(upper) >= 0)
            {
                width = 1.10f;
                height = 0.78f;
            }
            mouth.localScale = new Vector3(mouthRestScale.x * width,
                mouthRestScale.y * height * pulse, mouthRestScale.z);
        }

        void AnimateTeachingGesture(bool talking)
        {
            float speed = talking ? 4.4f : 1.8f;
            float amount = talking ? 4.2f : 1.4f;
            float sweep = Mathf.Sin(Time.unscaledTime * speed) * amount;
            if (teachingArm != null)
                teachingArm.localRotation = Quaternion.Euler(0f, 0f, sweep * 0.55f)
                    * teachingArmRestRotation;
            if (pointer != null)
                pointer.localRotation = Quaternion.Euler(0f, 0f, sweep)
                    * pointerRestRotation;
        }

        void RestoreTeachingGesture()
        {
            if (teachingArm != null) teachingArm.localRotation = teachingArmRestRotation;
            if (pointer != null) pointer.localRotation = pointerRestRotation;
        }

        void CloseMouth()
        {
            if (mouth == null) return;
            mouth.localScale = new Vector3(mouthRestScale.x,
                mouthRestScale.y * 0.24f, mouthRestScale.z);
        }

        float CharacterDelay(char character, float rate)
        {
            return CharacterTypingWeight(character) / Mathf.Max(1f, rate);
        }

        static float MessageTypingWeight(string message)
        {
            float weight = 0f;
            for (int i = 0; i < message.Length; i++)
                weight += CharacterTypingWeight(message[i]);
            return Mathf.Max(1f, weight);
        }

        static float CharacterTypingWeight(char character)
        {
            if (character == '\n') return 4.2f;
            if (".!?".IndexOf(character) >= 0) return 5.2f;
            if (",:;".IndexOf(character) >= 0) return 3f;
            if (char.IsWhiteSpace(character)) return 0.55f;
            return 1f;
        }

        static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
