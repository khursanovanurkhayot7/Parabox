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
            "HEY! ONE MORE CHANCE!\nI ONLY GET ONE LIFE.\nUSE UNDO OR RESTART!";

        [Header("Lesson chrome")]
        public CanvasGroup group;
        public RectTransform card;
        public Button tryAgainButton;

        [Header("Legacy entrance references (kept hidden)")]
        public RectTransform entryRipple;
        public CanvasGroup entryRippleGroup;

        [Serializable]
        public struct SubtitleCue
        {
            [Min(0f)] public float startSeconds;
            public string text;
        }

        [Header("Voice subtitles")]
        public Text subtitleText;
        public CanvasGroup subtitleGroup;
        public SubtitleCue[] subtitleCues;

        [Header("Player dialogue")]
        public RectTransform player;
        public CanvasGroup playerGroup;
        public RectTransform speechBubble;
        public CanvasGroup speechGroup;
        public Text speechText;
        public RectTransform mouth;
        public RectTransform teachingArm;
        public RectTransform pointer;
        public Graphic teachingGraphic;
        public AudioSource voiceSource;
        public AudioClip voiceClip;
        [TextArea(2, 5)] public string spokenMessage = DefaultMessage;
        [Min(1f)] public float charactersPerSecond = 17f;

        Action onTryAgain;
        Coroutine entrance;
        bool accepted;
        bool messageComplete;
        int activeSubtitle = -1;
        Vector3 cardRest = Vector3.one;
        Vector2 playerRestPosition;
        Vector3 playerRestScale = Vector3.one;
        Quaternion playerRestRotation = Quaternion.identity;
        Vector3 bubbleRestScale = Vector3.one;
        Vector3 mouthRestScale = Vector3.one;
        Vector2 mouthRestPosition;
        Quaternion teachingArmRestRotation = Quaternion.identity;
        Quaternion pointerRestRotation = Quaternion.identity;
        Material gestureMaterial;
        static readonly int GestureAngleId = Shader.PropertyToID("_GestureAngle");
        static readonly int LeftStepId = Shader.PropertyToID("_LeftStep");
        static readonly int RightStepId = Shader.PropertyToID("_RightStep");
        static readonly int LeftPlantId = Shader.PropertyToID("_LeftPlant");
        static readonly int RightPlantId = Shader.PropertyToID("_RightPlant");
        static readonly int FreeArmAngleId = Shader.PropertyToID("_FreeArmAngle");
        static readonly int BodySwayId = Shader.PropertyToID("_BodySway");
        static readonly int BreathId = Shader.PropertyToID("_Breath");
        static readonly int ElbowId = Shader.PropertyToID("_ElbowAngle");
        static readonly int WristId = Shader.PropertyToID("_WristAngle");
        static readonly int FreeElbowId = Shader.PropertyToID("_FreeElbowAngle");
        static readonly int FreeWristId = Shader.PropertyToID("_FreeWristAngle");
        static readonly int EntranceLightId = Shader.PropertyToID("_EntranceLight");
        static readonly int HandOpenId = Shader.PropertyToID("_HandOpen");
        static readonly int HeldPointerId = Shader.PropertyToID("_HeldPointer");
        const float RelaxedFingerOpening = 0.22f;
        const float FadeInSeconds = 1.05f;
        // Keep the delivery clear for children and older players without sounding unnaturally slow.
        const float VoicePitch = 1.06f;
        const float TeachingHandLift = 0.18f;
        const float FreeHandLift = 0.14f;
        float performanceTime;
        float gestureBlend;
        SpeechGesture currentSpeechGesture;

        struct SpeechGesture
        {
            public float time, shoulder, elbow, wrist, freeShoulder, freeElbow, freeWrist, openness;
            public SpeechGesture(float t, float s, float e, float w, float fs, float fe, float fw,
                float open = RelaxedFingerOpening)
            { time = t; shoulder = s; elbow = e; wrist = w;
                freeShoulder = fs; freeElbow = fe; freeWrist = fw; openness = open; }
        }

        // Intentional preparation, stroke, hold and release for each spoken phrase.
        static readonly SpeechGesture[] SpeechGestures =
        {
            new SpeechGesture(0f, 0f, 0f, 0f, 0f, 0f, 0f),
            // "Hey!": a greeting held briefly before relaxing.
            new SpeechGesture(.32f, .065f, .13f, -.06f, .025f, .14f, -.025f, .92f),
            new SpeechGesture(.62f, .065f, .13f, -.06f, .025f, .14f, -.025f, .92f),
            new SpeechGesture(.98f, .005f, .025f, .01f, .008f, .035f, 0f, .40f),
            // "One more chance": prepare low, then offer the hand outwards.
            new SpeechGesture(1.30f, -.035f, .015f, .035f, .015f, .065f, .02f, .48f),
            new SpeechGesture(1.88f, .045f, .09f, -.045f, .038f, .19f, -.035f, 1f),
            new SpeechGesture(2.16f, .045f, .09f, -.045f, .038f, .19f, -.035f, 1f),
            new SpeechGesture(2.75f, 0f, .025f, 0f, .012f, .05f, 0f, .40f),
            // "I only get one life": a closer, personal emphasis with both elbows.
            new SpeechGesture(3.38f, -.045f, .24f, .045f, .045f, .31f, .035f, .65f),
            new SpeechGesture(3.85f, -.045f, .24f, .045f, .045f, .31f, .035f, .65f),
            new SpeechGesture(4.30f, .025f, .14f, -.035f, .028f, .20f, -.025f, .78f),
            new SpeechGesture(4.65f, .025f, .14f, -.035f, .028f, .20f, -.025f, .78f),
            new SpeechGesture(5.20f, 0f, .025f, 0f, .008f, .04f, 0f, .35f),
            // Distinct beats for "UNDO" and "RESTART", then settle completely.
            new SpeechGesture(5.82f, .075f, .12f, -.055f, .03f, .17f, -.025f, .94f),
            new SpeechGesture(6.10f, .075f, .12f, -.055f, .03f, .17f, -.025f, .94f),
            new SpeechGesture(6.35f, .005f, .065f, .015f, .018f, .09f, 0f, .50f),
            new SpeechGesture(6.80f, -.05f, .16f, .065f, .04f, .23f, .035f, .84f),
            new SpeechGesture(7.10f, -.05f, .16f, .065f, .04f, .23f, .035f, .84f),
            new SpeechGesture(7.78f, 0f, 0f, 0f, 0f, 0f, 0f)
        };

        public bool ReadyForTryIt => messageComplete && !accepted;

        void Awake()
        {
            FirstLifeLessonBoard.Prepare(this);
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
                mouthRestPosition = mouth.anchoredPosition;
                mouthRestScale = mouth.localScale;
                if (mouthRestScale.sqrMagnitude < 0.01f) mouthRestScale = Vector3.one;
            }
            if (teachingArm != null) teachingArmRestRotation = teachingArm.localRotation;
            if (pointer != null) pointerRestRotation = pointer.localRotation;
            if (teachingGraphic != null && teachingGraphic.material != null)
            {
                gestureMaterial = new Material(teachingGraphic.material)
                {
                    name = "PremiumTeacherGesture (Runtime)"
                };
                teachingGraphic.material = gestureMaterial;
                gestureMaterial.SetFloat(GestureAngleId, 0f);
                gestureMaterial.SetFloat(LeftStepId, 0f);
                gestureMaterial.SetFloat(RightStepId, 0f);
                gestureMaterial.SetVector(LeftPlantId, Vector4.zero);
                gestureMaterial.SetVector(RightPlantId, Vector4.zero);
                gestureMaterial.SetFloat(FreeArmAngleId, 0f);
                gestureMaterial.SetFloat(BodySwayId, 0f);
                gestureMaterial.SetFloat(BreathId, 0f);
                gestureMaterial.SetFloat(ElbowId, 0f);
                gestureMaterial.SetFloat(WristId, 0f);
                gestureMaterial.SetFloat(FreeElbowId, 0f);
                gestureMaterial.SetFloat(FreeWristId, 0f);
                gestureMaterial.SetFloat(EntranceLightId, 0f);
                gestureMaterial.SetFloat(HandOpenId, RelaxedFingerOpening);
                gestureMaterial.SetFloat(HeldPointerId, 0f);
            }
            if (subtitleGroup != null)
                PremiumSubtitlePanel.Apply(subtitleGroup.gameObject);
            if (pointer != null) pointer.gameObject.SetActive(false);
            if (voiceSource != null)
            {
                voiceSource.playOnAwake = false;
                voiceSource.loop = false;
                voiceSource.spatialBlend = 0f;
                voiceSource.ignoreListenerPause = true;
                voiceSource.clip = voiceClip;
                // Keep the delivery brisk but still clear enough for a first-time player.
                voiceSource.pitch = VoicePitch;
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

        void OnDestroy()
        {
            if (gestureMaterial != null) Destroy(gestureMaterial);
        }

        public void Show(Action callback)
        {
            onTryAgain = callback;
            accepted = false;
            messageComplete = false;
            performanceTime = 0f;
            gestureBlend = 0f;
            ResetSubtitles();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }
            if (speechText != null) speechText.text = string.Empty;
            if (tryAgainButton != null)
            {
                tryAgainButton.interactable = false;
                tryAgainButton.gameObject.SetActive(false);
            }
            if (voiceSource != null) voiceSource.Stop();
            CloseMouth();
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
            ResetSubtitles();
            HideEntryRipple();
            CloseMouth();
            RestoreTeachingGesture();
            if (tryAgainButton != null)
            {
                tryAgainButton.gameObject.SetActive(false);
            }
            if (speechText != null) speechText.text = spokenMessage;
            gameObject.SetActive(false);
        }

        IEnumerator AnimateConversation()
        {
            float elapsed = 0f;
            RestoreConversationTransforms();
            if (playerGroup != null) playerGroup.alpha = 0f;
            if (speechGroup != null) speechGroup.alpha = 0f;
            HideEntryRipple();

            // A soft, minimum-jerk fade at the final arms-down pose beside the board.
            // A restrained highlight catches the teacher's glossy cyan/pink finish, then
            // disappears before speech. No zoom, bounce or portal, even while paused.
            while (elapsed < FadeInSeconds && !messageComplete)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / FadeInSeconds);
                float opacity = EaseMinimumJerk(progress);
                if (playerGroup != null) playerGroup.alpha = opacity;
                if (speechGroup != null) speechGroup.alpha = opacity;
                if (gestureMaterial != null)
                    gestureMaterial.SetFloat(EntranceLightId,
                        Mathf.Sin(progress * Mathf.PI) * 0.14f);
                yield return null;
            }

            RestoreConversationTransforms();
            yield return new WaitForSecondsRealtime(0.16f);

            string message = string.IsNullOrWhiteSpace(spokenMessage)
                ? DefaultMessage : spokenMessage;
            bool hasVoice = voiceSource != null && voiceClip != null;
            float messageWeight = MessageTypingWeight(message);
            float narrationDuration = hasVoice ? voiceClip.length
                : messageWeight / Mathf.Max(1f, charactersPerSecond);
            if (hasVoice && !messageComplete)
            {
                voiceSource.clip = voiceClip;
                voiceSource.Play();
            }
            int count = 0;
            float nextCharacterAt = 0f;
            float fallbackClock = 0f;
            float narrationClock = 0f;
            char currentCharacter = ' ';
            while (!messageComplete)
            {
                bool voicePlaying = hasVoice && voiceSource.isPlaying;
                fallbackClock += Time.unscaledDeltaTime
                    * (hasVoice ? Mathf.Max(0.01f, voiceSource.pitch) : 1f);
                // Follow the actual clip playhead so subtitle timing cannot drift at low FPS.
                // The realtime fallback also keeps this retry usable if audio is unavailable.
                narrationClock = Mathf.Max(narrationClock,
                    voicePlaying ? voiceSource.time : fallbackClock);
                while (count < message.Length && narrationClock >= nextCharacterAt)
                {
                    currentCharacter = message[count];
                    count++;
                    if (speechText != null) speechText.text = message.Substring(0, count);
                    nextCharacterAt += CharacterTypingWeight(currentCharacter)
                        * narrationDuration / messageWeight;
                }
                UpdateSubtitle(narrationClock);
                AnimateTalkingMouth(currentCharacter);
                AnimateTeachingGesture(true, narrationClock);
                ApplyPlayerIdle();
                if (narrationClock >= narrationDuration && !voicePlaying)
                    CompleteMessage();
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
            if (voiceSource != null) voiceSource.Stop();
            // Keep the final advice readable after speech, including an early arcade confirm.
            UpdateSubtitle(float.MaxValue);
            if (subtitleGroup != null) subtitleGroup.alpha = 1f;
            if (speechText != null)
                speechText.text = string.IsNullOrWhiteSpace(spokenMessage)
                    ? DefaultMessage : spokenMessage;
            CloseMouth();
            if (tryAgainButton != null)
            {
                tryAgainButton.gameObject.SetActive(true);
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
            HideEntryRipple();
            RestoreTeachingGesture();
        }

        void HideEntryRipple()
        {
            if (entryRippleGroup != null) entryRippleGroup.alpha = 0f;
            if (entryRipple != null) entryRipple.gameObject.SetActive(false);
        }

        void ResetSubtitles()
        {
            activeSubtitle = -1;
            if (subtitleText != null) subtitleText.text = string.Empty;
            if (subtitleGroup != null) subtitleGroup.alpha = 0f;
        }

        void UpdateSubtitle(float clipTime)
        {
            if (subtitleText == null || subtitleCues == null || subtitleCues.Length == 0) return;
            int next = -1;
            for (int i = 0; i < subtitleCues.Length; i++)
                if (clipTime >= subtitleCues[i].startSeconds) next = i;
            if (next < 0) return;
            if (next != activeSubtitle)
            {
                activeSubtitle = next;
                subtitleText.text = subtitleCues[next].text;
                if (subtitleGroup != null) subtitleGroup.alpha = 0f;
            }
            if (subtitleGroup != null)
                subtitleGroup.alpha = Mathf.MoveTowards(subtitleGroup.alpha, 1f,
                    Time.unscaledDeltaTime * 10f);
        }

        void ApplyPlayerIdle()
        {
            if (player == null) return;
            // The hands and mouth carry the idle performance; leave both shoes on the floor.
            player.anchoredPosition = playerRestPosition;
            player.localRotation = playerRestRotation;
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

        void AnimateTeachingGesture(bool talking, float speechTime = 0f)
        {
            performanceTime += Time.unscaledDeltaTime;
            gestureBlend = Mathf.MoveTowards(gestureBlend, talking ? 1f : 0f,
                Time.unscaledDeltaTime * 2.5f);
            float t = performanceTime;
            // Follow the actual voice playhead and retain the last pose when interrupted.
            if (talking) currentSpeechGesture = SampleSpeechGesture(speechTime);
            // Both hands rise into a relaxed teaching stance as speech begins. The authored
            // phrase gestures still layer on top, and the arms ease down again after speaking.
            float arm = (currentSpeechGesture.shoulder + TeachingHandLift) * gestureBlend;
            float freeArm = (currentSpeechGesture.freeShoulder + FreeHandLift) * gestureBlend;
            float sway = Mathf.Sin(t * 1.05f) * 0.0035f * gestureBlend;
            float breath = (1f - Mathf.Cos(t * 1.5f)) * 0.003f * gestureBlend;
            if (gestureMaterial != null)
            {
                gestureMaterial.SetFloat(GestureAngleId, arm);
                gestureMaterial.SetFloat(FreeArmAngleId, freeArm);
                gestureMaterial.SetFloat(BodySwayId, sway);
                gestureMaterial.SetFloat(BreathId, breath);
                gestureMaterial.SetFloat(ElbowId, currentSpeechGesture.elbow * gestureBlend);
                gestureMaterial.SetFloat(WristId, currentSpeechGesture.wrist * gestureBlend);
                gestureMaterial.SetFloat(FreeElbowId, currentSpeechGesture.freeElbow * gestureBlend);
                gestureMaterial.SetFloat(FreeWristId, currentSpeechGesture.freeWrist * gestureBlend);
                gestureMaterial.SetFloat(HandOpenId,
                    Mathf.Lerp(RelaxedFingerOpening, currentSpeechGesture.openness, gestureBlend));
            }
            if (teachingArm != null)
                teachingArm.localRotation = Quaternion.Euler(0f, 0f, arm * Mathf.Rad2Deg)
                    * teachingArmRestRotation;
            if (mouth != null && teachingGraphic != null)
            {
                // Keep the live lip-sync overlay attached to the gently breathing torso.
                RectTransform portrait = teachingGraphic.rectTransform;
                Vector2 size = portrait.rect.size;
                if (teachingGraphic is Image image && image.preserveAspect && image.sprite != null)
                {
                    float aspect = image.sprite.rect.width / image.sprite.rect.height;
                    if (size.x / size.y > aspect) size.x = size.y * aspect;
                    else size.y = size.x / aspect;
                }
                float mouthUvY = 0.5f + (mouthRestPosition.y - portrait.anchoredPosition.y)
                    / Mathf.Max(1f, size.y);
                float weight = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.43f, 0.74f, mouthUvY));
                mouth.anchoredPosition = mouthRestPosition + new Vector2(
                    sway * weight * size.x, Mathf.Max(0f, mouthUvY - 0.45f) * breath * size.y);
            }
        }

        static SpeechGesture SampleSpeechGesture(float time)
        {
            for (int i = 1; i < SpeechGestures.Length; i++)
            {
                SpeechGesture a = SpeechGestures[i - 1], b = SpeechGestures[i];
                if (time > b.time) continue;
                float t = Mathf.InverseLerp(a.time, b.time, time);
                // Minimum-jerk interpolation eases into and out of deliberate holds.
                t = EaseMinimumJerk(t);
                return new SpeechGesture(time,
                    Mathf.Lerp(a.shoulder, b.shoulder, t), Mathf.Lerp(a.elbow, b.elbow, t),
                    Mathf.Lerp(a.wrist, b.wrist, t), Mathf.Lerp(a.freeShoulder, b.freeShoulder, t),
                    Mathf.Lerp(a.freeElbow, b.freeElbow, t), Mathf.Lerp(a.freeWrist, b.freeWrist, t),
                    Mathf.Lerp(a.openness, b.openness, t));
            }
            return SpeechGestures[SpeechGestures.Length - 1];
        }

        static float EaseMinimumJerk(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        void RestoreTeachingGesture()
        {
            if (gestureMaterial != null)
            {
                gestureMaterial.SetFloat(GestureAngleId, 0f);
                gestureMaterial.SetFloat(LeftStepId, 0f);
                gestureMaterial.SetFloat(RightStepId, 0f);
                gestureMaterial.SetVector(LeftPlantId, Vector4.zero);
                gestureMaterial.SetVector(RightPlantId, Vector4.zero);
                gestureMaterial.SetFloat(FreeArmAngleId, 0f);
                gestureMaterial.SetFloat(BodySwayId, 0f);
                gestureMaterial.SetFloat(BreathId, 0f);
                gestureMaterial.SetFloat(EntranceLightId, 0f);
                gestureMaterial.SetFloat(HandOpenId, RelaxedFingerOpening);
                gestureMaterial.SetFloat(HeldPointerId, 0f);
            }
            if (gestureMaterial != null)
            {
                gestureMaterial.SetFloat(ElbowId, 0f);
                gestureMaterial.SetFloat(WristId, 0f);
                gestureMaterial.SetFloat(FreeElbowId, 0f);
                gestureMaterial.SetFloat(FreeWristId, 0f);
            }
            currentSpeechGesture = default;
            if (mouth != null) mouth.anchoredPosition = mouthRestPosition;
            if (teachingArm != null) teachingArm.localRotation = teachingArmRestRotation;
            if (pointer != null) pointer.localRotation = pointerRestRotation;
        }

        void CloseMouth()
        {
            if (mouth == null) return;
            mouth.localScale = new Vector3(mouthRestScale.x,
                mouthRestScale.y * 0.24f, mouthRestScale.z);
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
