using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Restores the board conversation on existing scenes as well as fresh installer output.
    // Slots own responsive placement; the approved portrait keeps all its local transforms,
    // material, relaxed hands and voice-timed gestures exactly as authored.
    [DisallowMultipleComponent]
    public sealed class FirstLifeLessonBoard : MonoBehaviour
    {
        RectTransform presentation, teacherSlot, boardSlot, actionSlot, captionSlot;
        Vector2 lastSize;
        FirstLifeLessonFx owner;
        bool stacked;

        public struct Layout
        {
            public Vector2 teacher, board, action, caption, captionSize, origin;
            public float scale;
            public bool stacked;
        }

        public static Layout MeasureLayout(Vector2 size)
        {
            bool stacked = size.x < size.y * 0.9f;
            return new Layout
            {
                stacked = stacked,
                teacher = stacked ? new Vector2(0f, 225f) : new Vector2(-300f, 0f),
                board = stacked ? new Vector2(0f, -208f) : new Vector2(185f, 50f),
                action = stacked ? new Vector2(0f, -452f) : new Vector2(185f, -184f),
                // Coordinates are local to the board face, never a screen subtitle bar.
                caption = new Vector2(0f, 38f),
                captionSize = new Vector2(464f, 172f),
                origin = new Vector2(0f, stacked ? -60f : -42f),
                scale = Mathf.Min(1f, Mathf.Min(Mathf.Max(1f, size.x - 64f) / (stacked ? 700f : 1000f),
                    Mathf.Max(1f, size.y - 64f) / (stacked ? 1180f : 740f)))
            };
        }

        public static void Prepare(FirstLifeLessonFx lesson)
        {
            if (lesson == null || lesson.player == null) return;
            FirstLifeLessonBoard layout = lesson.GetComponent<FirstLifeLessonBoard>();
            if (layout == null) layout = lesson.gameObject.AddComponent<FirstLifeLessonBoard>();
            layout.Build(lesson);
        }

        void Build(FirstLifeLessonFx lesson)
        {
            owner = lesson;
            presentation = RectChild(transform, "LessonPresentation", Vector2.zero, new Vector2(1000f, 620f));
            teacherSlot = RectChild(presentation, "TeacherSlot", Vector2.zero, new Vector2(520f, 530f));
            boardSlot = RectChild(presentation, "BoardSlot", Vector2.zero, new Vector2(560f, 330f));
            actionSlot = RectChild(presentation, "ActionSlot", Vector2.zero, new Vector2(390f, 84f));
            // The pointer reaches inside the board, so its shaft/tip must draw over the face.
            boardSlot.SetAsFirstSibling();

            // SetParent(false) retains the portrait's authored local pose and dimensions.
            lesson.player.SetParent(teacherSlot, false);

            // The final teacher presentation uses natural hand gestures without a pointer prop.
            // Retire a runtime pointer left by fast Enter Play Mode, but do not create a new one.
            Transform oldPointer = lesson.player.Find("GoldTeachingPointer");
            if (oldPointer != null) oldPointer.gameObject.SetActive(false);

            RectTransform board = RectChild(boardSlot, "LessonBoard", Vector2.zero, new Vector2(560f, 330f));
            Image face = board.GetComponent<Image>();
            if (face == null) face = board.gameObject.AddComponent<Image>();
            PremiumSubtitlePanel.Apply(board.gameObject, true);
            CanvasGroup boardGroup = board.GetComponent<CanvasGroup>();
            if (boardGroup == null) boardGroup = board.gameObject.AddComponent<CanvasGroup>();
            boardGroup.interactable = false;
            boardGroup.blocksRaycasts = false;

            captionSlot = RectChild(board, "BoardText", new Vector2(0f, 38f), new Vector2(464f, 172f));

            Text message = lesson.speechText != null ? lesson.speechText : lesson.subtitleText;
            if (message == null)
                message = RectChild(captionSlot, "Dialogue", Vector2.zero, Vector2.zero).gameObject.AddComponent<Text>();
            message.transform.SetParent(captionSlot, false);
            Centre(message.rectTransform, Vector2.zero, captionSlot.sizeDelta);
            if (message.font == null) message.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            message.fontSize = 32;
            message.fontStyle = FontStyle.Normal;
            // Fixed top-left alignment keeps the first letters stationary as more lines type in.
            message.alignment = TextAnchor.UpperLeft;
            message.lineSpacing = 1.10f;
            message.resizeTextForBestFit = false;
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.verticalOverflow = VerticalWrapMode.Truncate;
            message.supportRichText = true;
            message.raycastTarget = false;
            message.color = new Color32(244, 252, 255, 255);
            CrispUiTypography.Polish(message);
            CanvasGroup captionGroup = message.GetComponent<CanvasGroup>();
            if (captionGroup == null) captionGroup = message.gameObject.AddComponent<CanvasGroup>();
            captionGroup.alpha = 1f;
            captionGroup.interactable = false;
            captionGroup.blocksRaycasts = false;

            // Retire existing serialized/runtime nameplates without creating a replacement.
            Transform oldLabel = board.Find("SpeakerLabel");
            if (oldLabel != null) oldLabel.gameObject.SetActive(false);
            Transform oldTopDialogue = presentation.Find("TopDialogue");
            if (oldTopDialogue != null) oldTopDialogue.gameObject.SetActive(false);

            // Reconnect the existing cumulative typewriter. The subtitle cue path must not
            // replace earlier sentences while the original spokenMessage is being typed.
            if (lesson.subtitleGroup != null && lesson.subtitleGroup != boardGroup
                && lesson.subtitleGroup != captionGroup)
                lesson.subtitleGroup.gameObject.SetActive(false);
            if (lesson.speechBubble != null && lesson.speechBubble != board)
                lesson.speechBubble.gameObject.SetActive(false);
            lesson.subtitleText = null;
            lesson.subtitleGroup = null;
            lesson.speechBubble = board;
            lesson.speechGroup = boardGroup;
            lesson.speechText = message;

            if (lesson.tryAgainButton != null)
            {
                // Keep the existing real Button, callback, arcade binding and spring animation.
                lesson.tryAgainButton.transform.SetParent(actionSlot, false);
                ((RectTransform)lesson.tryAgainButton.transform).anchoredPosition = Vector2.zero;
                Transform buttonFace = lesson.tryAgainButton.transform.Find("Face");
                if (buttonFace != null)
                {
                    UIGradient gradient = buttonFace.GetComponent<UIGradient>();
                    if (gradient != null)
                    {
                        gradient.top = new Color32(85, 220, 76, 255);
                        gradient.bottom = new Color32(22, 133, 54, 255);
                    }
                }
            }
            RefreshLayout(true);
        }

        void LateUpdate()
        {
            RefreshLayout(false);
        }

        void RefreshLayout(bool force)
        {
            if (presentation == null) return;
            RectTransform root = transform as RectTransform;
            if (root == null) return;
            Vector2 size = root.rect.size;
            if (size.x < 1f || size.y < 1f) return;
            if (!force && (size - lastSize).sqrMagnitude < 0.01f) return;
            lastSize = size;
            Layout layout = MeasureLayout(size);
            stacked = layout.stacked;
            presentation.localScale = Vector3.one * layout.scale;
            presentation.anchoredPosition = layout.origin * layout.scale;
            teacherSlot.anchoredPosition = layout.teacher;
            boardSlot.anchoredPosition = layout.board;
            actionSlot.anchoredPosition = layout.action;
            captionSlot.anchoredPosition = layout.caption;
            captionSlot.sizeDelta = layout.captionSize;
            if (owner != null && owner.speechText != null)
                owner.speechText.rectTransform.sizeDelta = layout.captionSize;
        }

        static RectTransform RectChild(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Transform found = parent.Find(name);
            RectTransform rect = found as RectTransform;
            if (rect == null)
            {
                var child = new GameObject(name, typeof(RectTransform));
                child.layer = parent.gameObject.layer;
                rect = (RectTransform)child.transform;
                rect.SetParent(parent, false);
            }
            Centre(rect, position, size);
            return rect;
        }

        static void Centre(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
