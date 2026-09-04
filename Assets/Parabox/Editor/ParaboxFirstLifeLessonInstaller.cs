#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Parabox.EditorTools
{
    // Creates the complete Level-1 second-chance lesson in Edit Mode. The installer touches only
    // this overlay and its GameManager reference; existing HUD, loss UI and gameplay stay intact.
    public static class ParaboxFirstLifeLessonInstaller
    {
        const string GameScenePath = "Assets/Parabox/Scenes/Game.unity";
        const string VoiceClipPath = "Assets/Parabox/Resources/Sfx/SecondChanceTeacher.wav";
        const string TeacherSpritePath = "Assets/Parabox/Resources/UI/PremiumTeacher3D.png";
        const string GestureShaderPath = "Assets/Parabox/Resources/Shaders/PremiumTeacherGesture.shader";
        const string GestureMaterialPath = "Assets/Parabox/Resources/UI/PremiumTeacherGesture.mat";
        const string RootName = "FirstLifeLesson";
        static readonly Color NavyTop = Hex("12334D");
        static readonly Color NavyBottom = Hex("040B17");
        static readonly Color Cyan = Hex("42E7FF");
        static readonly Color Purple = Hex("9B58FF");
        static readonly Color GreenTop = Hex("55DC4C");
        static readonly Color GreenBottom = Hex("168536");
        static readonly Color White = Hex("F4FCFF");

        [MenuItem("Tools/Parabox/Install Level 1 Second Chance Lesson", priority = 2)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the Level-1 lesson.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException(
                    "Unity is compiling or importing. Wait until it finishes, then run the command again.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            GameManager game = FindInScene<GameManager>(scene);
            if (game == null)
                throw new InvalidOperationException(
                    "Game.unity has no GameManager. Run Tools/Parabox/Generate Prebuilt UI first.");

            Transform parent = game.timeUpPanel != null ? game.timeUpPanel.transform.parent
                : game.winPanel != null ? game.winPanel.transform.parent : null;
            if (parent == null)
                throw new InvalidOperationException("The main gameplay Canvas could not be found.");

            Transform old = parent.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            Font font = game.levelLabel != null && game.levelLabel.font != null
                ? game.levelLabel.font
                : game.timerLabel != null && game.timerLabel.font != null
                    ? game.timerLabel.font
                    : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite fill = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Parabox/Sprites/Fill.png");
            Sprite glow = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Parabox/Sprites/Glow.png");
            Sprite teacherSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TeacherSpritePath);
            if (teacherSprite == null)
                throw new InvalidOperationException(
                    "The premium 3D teacher is missing at " + TeacherSpritePath + ". Wait for Unity "
                    + "to finish importing, then run this command again.");
            Material gestureMaterial = EnsureGestureMaterial();
            AudioClip voiceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(VoiceClipPath);
            if (voiceClip == null)
                throw new InvalidOperationException(
                    "The offline lesson voice is missing at " + VoiceClipPath + ". Wait for Unity "
                    + "to finish importing, then run this command again.");

            RectTransform root = CreateRoot(parent);
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            FirstLifeLessonFx lesson = root.GetComponent<FirstLifeLessonFx>();

            RectTransform player = CreatePlayerPortrait(root, game.playerPrefab, fill, glow,
                teacherSprite, gestureMaterial,
                out CanvasGroup playerGroup, out RectTransform mouth,
                out RectTransform teachingArm, out RectTransform pointer,
                out Graphic teachingGraphic);
            const string message =
                "HEY! ONE MORE CHANCE!\nI ONLY GET ONE LIFE.\nUSE UNDO OR RESTART!";
            Text subtitles = CreateSubtitles(root, font, fill, out CanvasGroup subtitleGroup);
            Button retry = CreateButton(root, font, fill, glow);

            lesson.group = group;
            lesson.card = null;
            lesson.tryAgainButton = retry;
            lesson.player = player;
            lesson.playerGroup = playerGroup;
            lesson.entryRipple = null;
            lesson.entryRippleGroup = null;
            lesson.subtitleText = subtitles;
            lesson.subtitleGroup = subtitleGroup;
            // Cue changes sit in the measured pauses of the existing 7.808-second voice clip.
            lesson.subtitleCues = new[]
            {
                new FirstLifeLessonFx.SubtitleCue
                    { startSeconds = 0f, text = "<color=#65EAFF>Hey!</color>" },
                new FirstLifeLessonFx.SubtitleCue
                    { startSeconds = 1.02f, text = "One more <color=#65EAFF>chance!</color>" },
                new FirstLifeLessonFx.SubtitleCue
                    { startSeconds = 2.94f, text = "I only get <color=#65EAFF>one life.</color>" },
                new FirstLifeLessonFx.SubtitleCue
                    { startSeconds = 5.42f, text = "Use <color=#65EAFF>UNDO</color> or <color=#65EAFF>RESTART!</color>" }
            };
            lesson.speechBubble = null;
            lesson.speechGroup = null;
            lesson.speechText = null;
            lesson.mouth = mouth;
            lesson.teachingArm = teachingArm;
            lesson.pointer = pointer;
            lesson.teachingGraphic = teachingGraphic;
            lesson.voiceSource = root.GetComponent<AudioSource>();
            lesson.voiceClip = voiceClip;
            lesson.spokenMessage = message;
            lesson.charactersPerSecond = 17f;
            FirstLifeLessonBoard.Prepare(lesson);
            game.firstLifeLessonFx = lesson;

            root.SetAsLastSibling();
            root.gameObject.SetActive(false);
            EditorUtility.SetDirty(lesson);
            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Level-1 second-chance lesson installed without entering Play Mode.");
            EditorUtility.DisplayDialog("Level 1 Second Chance",
                "Installed successfully.\n\n"
                + "The teacher fades in with the same relaxed pose, beside the 3D lesson board.\n"
                + "His phrases appear one by one at the upper screen centre; the green TRY IT stays below the board.\n"
                + "Press the GREEN arcade button or click TRY IT.\n"
                + "The first Level-1 loss then grants one fresh retry.\n"
                + "A later loss uses the normal Game Over flow.\n\n"
                + "No Play Mode test was started.", "OK");
        }

        static RectTransform CreateRoot(Transform parent)
        {
            var go = new GameObject(RootName, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(CanvasGroup), typeof(AudioSource),
                typeof(FirstLifeLessonFx));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Level-1 second-chance lesson");

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            Image dim = go.GetComponent<Image>();
            dim.color = new Color(0.005f, 0.015f, 0.045f, 0.72f);
            dim.raycastTarget = true;
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            AudioSource voice = go.GetComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            voice.volume = 0.92f;
            voice.ignoreListenerPause = true;
            return rect;
        }

        static RectTransform CreateCard(RectTransform root, Sprite fill)
        {
            var go = new GameObject("LessonCard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Outline), typeof(Shadow), typeof(UIGradient));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(900f, 510f);

            Image face = go.GetComponent<Image>();
            face.sprite = fill;
            face.type = fill != null ? Image.Type.Sliced : Image.Type.Simple;
            face.color = Color.white;
            face.raycastTarget = false;
            UIGradient gradient = go.GetComponent<UIGradient>();
            gradient.top = NavyTop;
            gradient.bottom = NavyBottom;
            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.90f);
            outline.effectDistance = new Vector2(2f, -2f);
            Shadow shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(0f, -12f);
            return rect;
        }

        static Text CreateSubtitles(RectTransform parent, Font font, Sprite fill,
            out CanvasGroup group)
        {
            // Keep narration above the teacher, with breathing room between the panel's
            // lower edge and the portrait. Reserve the former subtitle area for the action.
            Image strip = CreateImage(parent, "VoiceSubtitles", new Vector2(0f, 326f),
                new Vector2(740f, 88f), fill, Color.white, false);
            strip.type = fill != null ? Image.Type.Sliced : Image.Type.Simple;
            strip.preserveAspect = false;
            group = strip.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            PremiumSubtitlePanel.Apply(strip.gameObject);

            Text text = CreateText(strip.rectTransform, "SubtitleLine", font, string.Empty, 36,
                FontStyle.Normal, White, new Vector2(0f, 2f), new Vector2(668f, 62f));
            text.supportRichText = true;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 32;
            text.resizeTextMaxSize = 36;
            Shadow textShadow = text.gameObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0f, 0.01f, 0.03f, 0.50f);
            textShadow.effectDistance = new Vector2(0f, -1f);
            CrispUiTypography.Polish(text);
            return text;
        }

        static RectTransform CreatePlayerPortrait(RectTransform card, GameObject playerPrefab,
            Sprite fill, Sprite glow, Sprite teacherSprite, Material gestureMaterial,
            out CanvasGroup group,
            out RectTransform mouth,
            out RectTransform teachingArm, out RectTransform pointer,
            out Graphic teachingGraphic)
        {
            var go = new GameObject("TalkingPlayer", typeof(RectTransform), typeof(CanvasGroup));
            go.layer = card.gameObject.layer;
            go.transform.SetParent(card, false);
            RectTransform root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            // Preserve the approved body's screen-centred position. The transparent canvas
            // retains its original dimensions after removing the pointer.
            root.anchoredPosition = new Vector2(0f, 10f);
            root.sizeDelta = new Vector2(520f, 530f);
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;
            teachingGraphic = null;

            // The generated teacher is one clean, transparent 3D sprite. Keeping it as one
            // image preserves its lighting, proportions and premium finish. Only the mouth is
            // separate so FirstLifeLessonFx can continue syncing it to the typed narration.
            if (teacherSprite != null)
            {
                Image portrait = CreateImage(root, "PremiumTeacher3D", new Vector2(102f, 0f),
                    new Vector2(480f, 500f), teacherSprite, Color.white, false);
                portrait.preserveAspect = true;
                portrait.material = gestureMaterial;
                teachingGraphic = portrait;

                Image premiumMouthImage = CreateImage(root, "TalkingMouth", new Vector2(1f, 67f),
                    new Vector2(53f, 16f), fill, Hex("170D2B"), false);
                premiumMouthImage.preserveAspect = false;
                Outline premiumMouthOutline = premiumMouthImage.gameObject.AddComponent<Outline>();
                premiumMouthOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.34f);
                premiumMouthOutline.effectDistance = new Vector2(1f, -1f);
                mouth = premiumMouthImage.rectTransform;

                // The arm is isolated in the UI shader, allowing it to gesture independently
                // while preserving the exact approved single-sprite character artwork.
                teachingArm = null;
                pointer = null;
                return root;
            }

            // A two-layer aura separates the teacher from the card without bringing back the
            // large rotated rectangle that used to sit behind the portrait.
            Image outerHalo = CreateImage(root, "PlayerHaloOuter", new Vector2(0f, 4f),
                new Vector2(270f, 296f), glow,
                new Color(Purple.r, Purple.g, Purple.b, 0.31f), false);
            UIPulse pulse = outerHalo.gameObject.AddComponent<UIPulse>();
            pulse.amplitude = 0.035f;
            pulse.speed = 2.1f;
            CreateImage(root, "PlayerHaloInner", new Vector2(0f, 5f),
                new Vector2(224f, 250f), glow,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f), false);
            CreateImage(root, "PlayerHaloCore", new Vector2(0f, 4f),
                new Vector2(184f, 210f), glow,
                new Color(1f, 0.25f, 0.66f, 0.13f), false);
            Image groundShadow = CreateImage(root, "TeacherGroundShadow",
                new Vector2(0f, -150f), new Vector2(176f, 25f), glow,
                new Color(0.015f, 0.01f, 0.08f, 0.68f), false);
            groundShadow.preserveAspect = false;
            Image groundLight = CreateImage(root, "TeacherGroundLight",
                new Vector2(0f, -149f), new Vector2(142f, 4f), fill,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.46f), false);
            groundLight.preserveAspect = false;

            SpriteRenderer bodySource = PlayerPart(playerPrefab, "Body");
            SpriteRenderer leftEyeSource = PlayerPart(playerPrefab, "EyeL");
            SpriteRenderer rightEyeSource = PlayerPart(playerPrefab, "EyeR");
            Sprite bodySprite = bodySource != null && bodySource.sprite != null
                ? bodySource.sprite : fill;
            Color bodyColor = bodySource != null ? bodySource.color : Hex("F24F9A");
            bodyColor = Color.Lerp(bodyColor, Hex("F24F9A"), 0.16f);
            Color limbColor = Color.Lerp(bodyColor, Hex("9C236D"), 0.16f);
            Color shoeColor = Hex("12102E");
            Color glassesColor = Hex("281052");

            CreatePremiumPart(root, "LegL", new Vector2(-27f, -105f),
                new Vector2(25f, 66f), bodySprite, limbColor, -2f);
            CreatePremiumPart(root, "LegR", new Vector2(27f, -105f),
                new Vector2(25f, 66f), bodySprite, limbColor, 2f);
            CreatePremiumPart(root, "FootL", new Vector2(-42f, -139f),
                new Vector2(58f, 28f), bodySprite, shoeColor, 0f);
            CreatePremiumPart(root, "FootR", new Vector2(42f, -139f),
                new Vector2(58f, 28f), bodySprite, shoeColor, 0f);
            Image leftSole = CreateImage(root, "FootLAccent", new Vector2(-48f, -137f),
                new Vector2(28f, 4f), fill, new Color(Cyan.r, Cyan.g, Cyan.b, 0.36f), false);
            leftSole.preserveAspect = false;
            Image rightSole = CreateImage(root, "FootRAccent", new Vector2(36f, -137f),
                new Vector2(28f, 4f), fill, new Color(Cyan.r, Cyan.g, Cyan.b, 0.36f), false);
            rightSole.preserveAspect = false;

            CreatePremiumPart(root, "RelaxedArm", new Vector2(-81f, 8f),
                new Vector2(72f, 21f), bodySprite, limbColor, -13f);
            CreatePremiumPart(root, "RelaxedHand", new Vector2(-115f, 13f),
                new Vector2(34f, 34f), bodySprite, bodyColor, -5f);
            CreatePremiumPart(root, "RelaxedCuff", new Vector2(-97f, 9f),
                new Vector2(22f, 27f), bodySprite, glassesColor, -13f);

            teachingArm = CreatePremiumPart(root, "TeachingArm", new Vector2(53f, 12f),
                new Vector2(80f, 21f), bodySprite, limbColor, 10f);
            teachingArm.pivot = new Vector2(0f, 0.5f);
            teachingArm.anchoredPosition = new Vector2(53f, 12f);

            CreateImage(root, "PlayerBodyGlow", new Vector2(0f, 1f),
                new Vector2(174f, 204f), glow,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.19f), false);

            RectTransform body = CreatePremiumPart(root, "PlayerBody", new Vector2(0f, 1f),
                new Vector2(142f, 174f), bodySprite, bodyColor, 0f);
            Image bodyInnerLight = CreateImage(body, "BodyInnerLight", new Vector2(-22f, 14f),
                new Vector2(94f, 142f), glow, new Color(1f, 1f, 1f, 0.12f), false);
            bodyInnerLight.preserveAspect = false;
            Image bodySideShade = CreateImage(body, "BodySideShade", new Vector2(42f, -4f),
                new Vector2(58f, 146f), glow, new Color(0.15f, 0.03f, 0.24f, 0.14f), false);
            bodySideShade.preserveAspect = false;

            CreatePremiumPart(root, "TeachingCuff", new Vector2(94f, 19f),
                new Vector2(24f, 28f), bodySprite, glassesColor, 10f);

            Sprite eyeSprite = leftEyeSource != null && leftEyeSource.sprite != null
                ? leftEyeSource.sprite : fill;
            Color eyeColor = leftEyeSource != null ? leftEyeSource.color : Hex("071426");
            CreateImage(root, "TeacherLensGlowL", new Vector2(-29f, 25f),
                new Vector2(68f, 60f), glow,
                new Color(Purple.r, Purple.g, Purple.b, 0.22f), false);
            CreateImage(root, "TeacherLensGlowR", new Vector2(29f, 25f),
                new Vector2(68f, 60f), glow,
                new Color(Purple.r, Purple.g, Purple.b, 0.22f), false);
            Image lensL = CreateImage(root, "TeacherLensL", new Vector2(-29f, 25f),
                new Vector2(48f, 40f), fill, new Color(0.15f, 0.82f, 1f, 0.20f), false);
            lensL.preserveAspect = false;
            Image lensR = CreateImage(root, "TeacherLensR", new Vector2(29f, 25f),
                new Vector2(48f, 40f), fill, new Color(0.15f, 0.82f, 1f, 0.20f), false);
            lensR.preserveAspect = false;
            CreateImage(root, "PlayerEyeL", new Vector2(-27f, 25f), new Vector2(23f, 32f),
                eyeSprite, eyeColor, false);
            Sprite rightSprite = rightEyeSource != null && rightEyeSource.sprite != null
                ? rightEyeSource.sprite : eyeSprite;
            Color rightColor = rightEyeSource != null ? rightEyeSource.color : eyeColor;
            CreateImage(root, "PlayerEyeR", new Vector2(27f, 25f), new Vector2(23f, 32f),
                rightSprite, rightColor, false);
            Image eyeGlintL = CreateImage(root, "EyeGlintL", new Vector2(-31f, 32f),
                new Vector2(7f, 9f), fill, new Color(0.88f, 0.98f, 1f, 0.80f), false);
            eyeGlintL.preserveAspect = false;
            Image eyeGlintR = CreateImage(root, "EyeGlintR", new Vector2(23f, 32f),
                new Vector2(7f, 9f), fill, new Color(0.88f, 0.98f, 1f, 0.80f), false);
            eyeGlintR.preserveAspect = false;

            CreateGlassesFrame(root, "GlassesL", new Vector2(-29f, 25f), fill, glassesColor);
            CreateGlassesFrame(root, "GlassesR", new Vector2(29f, 25f), fill, glassesColor);
            Image bridge = CreateImage(root, "GlassesBridge", new Vector2(0f, 25f),
                new Vector2(20f, 7f), fill, glassesColor, false);
            bridge.preserveAspect = false;
            Image templeL = CreateImage(root, "GlassesTempleL", new Vector2(-61f, 25f),
                new Vector2(19f, 7f), fill, glassesColor, false);
            templeL.preserveAspect = false;
            Image templeR = CreateImage(root, "GlassesTempleR", new Vector2(61f, 25f),
                new Vector2(19f, 7f), fill, glassesColor, false);
            templeR.preserveAspect = false;
            Image hingeL = CreateImage(root, "GlassesHingeL", new Vector2(-62f, 25f),
                new Vector2(10f, 10f), bodySprite, Cyan, false);
            hingeL.preserveAspect = false;
            Image hingeR = CreateImage(root, "GlassesHingeR", new Vector2(62f, 25f),
                new Vector2(10f, 10f), bodySprite, Cyan, false);
            hingeR.preserveAspect = false;

            Image lensGlareL = CreateImage(root, "LensGlareL", new Vector2(-37f, 36f),
                new Vector2(21f, 4f), fill, new Color(Cyan.r, Cyan.g, Cyan.b, 0.48f), false);
            lensGlareL.preserveAspect = false;
            lensGlareL.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            Image lensGlareR = CreateImage(root, "LensGlareR", new Vector2(21f, 36f),
                new Vector2(21f, 4f), fill, new Color(Cyan.r, Cyan.g, Cyan.b, 0.48f), false);
            lensGlareR.preserveAspect = false;
            lensGlareR.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -12f);

            Image mouthImage = CreateImage(root, "TalkingMouth", new Vector2(0f, -31f),
                new Vector2(42f, 15f), fill, Hex("180D2C"), false);
            mouthImage.preserveAspect = false;
            Outline mouthOutline = mouthImage.gameObject.AddComponent<Outline>();
            mouthOutline.effectColor = new Color(1f, 0.33f, 0.58f, 0.42f);
            mouthOutline.effectDistance = new Vector2(1.2f, -1.2f);
            mouth = mouthImage.rectTransform;

            CreatePremiumPart(root, "TeacherBowLeft", new Vector2(-12f, -61f),
                new Vector2(29f, 18f), bodySprite, glassesColor, 28f);
            CreatePremiumPart(root, "TeacherBowRight", new Vector2(12f, -61f),
                new Vector2(29f, 18f), bodySprite, glassesColor, -28f);
            CreatePremiumPart(root, "TeacherBowKnot", new Vector2(0f, -61f),
                new Vector2(16f, 16f), bodySprite, Cyan, 0f);
            Image teacherBadgeGlow = CreateImage(root, "TeacherBadgeGlow",
                new Vector2(0f, -79f), new Vector2(25f, 25f), glow,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.42f), false);
            teacherBadgeGlow.preserveAspect = false;
            CreatePremiumPart(root, "TeacherBadge", new Vector2(0f, -79f),
                new Vector2(12f, 12f), bodySprite, Cyan, 45f);

            Image shine = CreateImage(root, "PlayerShine", new Vector2(-25f, 65f),
                new Vector2(58f, 9f), fill, new Color(1f, 1f, 1f, 0.23f), false);
            shine.preserveAspect = false;

            CreatePremiumPart(root, "TeachingHand", new Vector2(118f, 22f),
                new Vector2(37f, 37f), bodySprite, bodyColor, 4f);

            var pointerObject = new GameObject("TeachingPointer", typeof(RectTransform));
            pointerObject.layer = root.gameObject.layer;
            pointerObject.transform.SetParent(root, false);
            pointer = (RectTransform)pointerObject.transform;
            pointer.anchorMin = pointer.anchorMax = new Vector2(0.5f, 0.5f);
            pointer.pivot = new Vector2(0f, 0.5f);
            pointer.anchoredPosition = new Vector2(112f, 24f);
            pointer.sizeDelta = new Vector2(180f, 20f);
            pointer.localRotation = Quaternion.Euler(0f, 0f, 14f);

            Image pointerGlow = CreateImage(pointer, "PointerGlow", new Vector2(88f, 0f),
                new Vector2(178f, 17f), glow, new Color(1f, 0.64f, 0.18f, 0.20f), false);
            AnchorFromLeft(pointerGlow.rectTransform, new Vector2(88f, 0f));
            pointerGlow.preserveAspect = false;
            Image pointerShadow = CreateImage(pointer, "PointerShadow", new Vector2(86f, -2f),
                new Vector2(174f, 14f), bodySprite, Hex("160E2D"), false);
            AnchorFromLeft(pointerShadow.rectTransform, new Vector2(86f, -2f));
            pointerShadow.preserveAspect = false;
            Image pointerCore = CreateImage(pointer, "PointerCore", new Vector2(84f, 0f),
                new Vector2(168f, 9f), bodySprite, Hex("F6C453"), false);
            AnchorFromLeft(pointerCore.rectTransform, new Vector2(84f, 0f));
            pointerCore.preserveAspect = false;
            pointerCore.color = Color.white;
            UIGradient pointerGradient = pointerCore.gameObject.AddComponent<UIGradient>();
            pointerGradient.top = Hex("FFF2A5");
            pointerGradient.bottom = Hex("E59A22");
            Image pointerSpecular = CreateImage(pointer, "PointerSpecular",
                new Vector2(77f, 2f), new Vector2(132f, 2f), fill,
                new Color(1f, 1f, 1f, 0.62f), false);
            AnchorFromLeft(pointerSpecular.rectTransform, new Vector2(77f, 2f));
            pointerSpecular.preserveAspect = false;
            Image pointerGrip = CreateImage(pointer, "PointerGrip", new Vector2(14f, 0f),
                new Vector2(38f, 18f), bodySprite, glassesColor, false);
            AnchorFromLeft(pointerGrip.rectTransform, new Vector2(14f, 0f));
            pointerGrip.preserveAspect = false;
            Image pointerTipHalo = CreateImage(pointer, "PointerTipHalo", new Vector2(171f, 0f),
                new Vector2(31f, 31f), glow, new Color(Cyan.r, Cyan.g, Cyan.b, 0.34f), false);
            AnchorFromLeft(pointerTipHalo.rectTransform, new Vector2(171f, 0f));
            Image pointerTip = CreateImage(pointer, "PointerTip", new Vector2(171f, 0f),
                new Vector2(21f, 21f), bodySprite, Cyan, false);
            AnchorFromLeft(pointerTip.rectTransform, new Vector2(171f, 0f));
            pointerTip.preserveAspect = false;
            UIPulse tipPulse = pointerTip.gameObject.AddComponent<UIPulse>();
            tipPulse.amplitude = 0.075f;
            tipPulse.speed = 3.2f;
            return root;
        }

        static void AnchorFromLeft(RectTransform rect, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
        }

        static RectTransform CreatePremiumPart(RectTransform parent, string name,
            Vector2 position, Vector2 size, Sprite roundedSprite, Color color, float angle)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = position;
            root.sizeDelta = size;
            root.localRotation = Quaternion.Euler(0f, 0f, angle);

            Image outline = CreateImage(root, "Outline", new Vector2(0f, -2f),
                size + new Vector2(7f, 7f), roundedSprite, Hex("20103C"), false);
            outline.preserveAspect = false;
            Image face = CreateImage(root, "Face", Vector2.zero, size,
                roundedSprite, Color.white, false);
            face.preserveAspect = false;
            UIGradient faceGradient = face.gameObject.AddComponent<UIGradient>();
            faceGradient.top = Color.Lerp(color, Color.white, 0.20f);
            faceGradient.bottom = Color.Lerp(color, Color.black, 0.16f);

            float shortest = Mathf.Min(size.x, size.y);
            Image highlight = CreateImage(root, "Highlight",
                new Vector2(-size.x * 0.15f, size.y * 0.23f),
                new Vector2(Mathf.Max(10f, size.x * 0.42f),
                    Mathf.Clamp(shortest * 0.16f, 4f, 9f)), roundedSprite,
                new Color(1f, 1f, 1f, color.a * 0.20f), false);
            highlight.preserveAspect = false;
            return root;
        }

        static void CreateGlassesFrame(RectTransform root, string name, Vector2 centre,
            Sprite fill, Color color)
        {
            Image top = CreateImage(root, name + "Top", centre + new Vector2(0f, 23f),
                new Vector2(56f, 7f), fill, color, false);
            Image bottom = CreateImage(root, name + "Bottom", centre + new Vector2(0f, -23f),
                new Vector2(56f, 7f), fill, color, false);
            Image left = CreateImage(root, name + "Left", centre + new Vector2(-28f, 0f),
                new Vector2(7f, 46f), fill, color, false);
            Image right = CreateImage(root, name + "Right", centre + new Vector2(28f, 0f),
                new Vector2(7f, 46f), fill, color, false);
            top.preserveAspect = bottom.preserveAspect = false;
            left.preserveAspect = right.preserveAspect = false;
        }

        static RectTransform CreateSpeechBubble(RectTransform card, Sprite fill,
            out CanvasGroup group)
        {
            var go = new GameObject("LessonBoard", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(Outline),
                typeof(Shadow), typeof(UIGradient));
            go.layer = card.gameObject.layer;
            go.transform.SetParent(card, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(134f, 56f);
            rect.sizeDelta = new Vector2(478f, 244f);

            Image face = go.GetComponent<Image>();
            face.sprite = fill;
            face.type = fill != null ? Image.Type.Sliced : Image.Type.Simple;
            face.color = Color.white;
            face.raycastTarget = false;
            UIGradient gradient = go.GetComponent<UIGradient>();
            gradient.top = Hex("173B5A");
            gradient.bottom = Hex("050A16");
            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.88f);
            outline.effectDistance = new Vector2(2f, -2f);
            Shadow shadow = go.GetComponent<Shadow>();
            // Keep the board silhouette clean. The frame bevels provide the 3D depth without
            // placing a large black rectangle behind the artwork.
            shadow.effectColor = Color.clear;
            shadow.effectDistance = Vector2.zero;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // Layered depth and bevels make the board read as a solid premium object instead
            // of a flat speech panel. Every layer remains non-interactive UI artwork.
            Image boardFrame = CreateImage(rect, "BoardFrontFrame", Vector2.zero,
                new Vector2(478f, 244f), fill, Color.white, false);
            boardFrame.preserveAspect = false;
            UIGradient frameGradient = boardFrame.gameObject.AddComponent<UIGradient>();
            frameGradient.top = Hex("55F0FF");
            frameGradient.bottom = Hex("7139D6");
            Outline frameOutline = boardFrame.gameObject.AddComponent<Outline>();
            frameOutline.effectColor = new Color(0.56f, 0.92f, 1f, 0.96f);
            frameOutline.effectDistance = new Vector2(2f, -2f);

            Image innerDepth = CreateImage(rect, "BoardInnerDepth", new Vector2(0f, -5f),
                new Vector2(456f, 220f), fill, Hex("02040D"), false);
            innerDepth.preserveAspect = false;
            Image boardInset = CreateImage(rect, "BoardInset", new Vector2(0f, 0f),
                new Vector2(446f, 208f), fill, Color.white, false);
            boardInset.preserveAspect = false;
            UIGradient insetGradient = boardInset.gameObject.AddComponent<UIGradient>();
            insetGradient.top = Hex("163755");
            insetGradient.bottom = Hex("050B17");
            Outline insetOutline = boardInset.gameObject.AddComponent<Outline>();
            insetOutline.effectColor = new Color(0.22f, 0.80f, 1f, 0.62f);
            insetOutline.effectDistance = new Vector2(2f, -2f);

            Image topBevel = CreateImage(rect, "BoardTopBevel", new Vector2(0f, 105f),
                new Vector2(422f, 6f), fill, new Color(0.84f, 0.99f, 1f, 0.78f), false);
            topBevel.preserveAspect = false;
            Image leftBevel = CreateImage(rect, "BoardLeftBevel", new Vector2(-218f, 0f),
                new Vector2(6f, 194f), fill, new Color(Cyan.r, Cyan.g, Cyan.b, 0.56f), false);
            leftBevel.preserveAspect = false;
            Image rightShade = CreateImage(rect, "BoardRightShade", new Vector2(218f, -3f),
                new Vector2(7f, 194f), fill, new Color(0.06f, 0.02f, 0.16f, 0.72f), false);
            rightShade.preserveAspect = false;
            Image surfaceSheen = CreateImage(rect, "BoardSurfaceSheen", new Vector2(-74f, 74f),
                new Vector2(258f, 4f), fill, new Color(1f, 1f, 1f, 0.14f), false);
            surfaceSheen.preserveAspect = false;

            Image speakerPlate = CreateImage(rect, "SpeakerPlate", new Vector2(-165f, 88f),
                new Vector2(132f, 32f), fill, Color.white, false);
            speakerPlate.preserveAspect = false;
            UIGradient plateGradient = speakerPlate.gameObject.AddComponent<UIGradient>();
            plateGradient.top = Hex("284F72");
            plateGradient.bottom = Hex("10213A");
            Outline plateOutline = speakerPlate.gameObject.AddComponent<Outline>();
            plateOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.68f);
            plateOutline.effectDistance = new Vector2(1f, -1f);

            Image topRail = CreateImage(rect, "BoardTopRail", new Vector2(0f, 116f),
                new Vector2(414f, 4f), fill,
                new Color(0.86f, 0.99f, 1f, 0.98f), false);
            topRail.preserveAspect = false;
            Image bottomLedge = CreateImage(rect, "BoardBottomLedge", new Vector2(0f, -126f),
                new Vector2(322f, 13f), fill, Color.white, false);
            bottomLedge.preserveAspect = false;
            UIGradient ledgeGradient = bottomLedge.gameObject.AddComponent<UIGradient>();
            ledgeGradient.top = Hex("FFF09A");
            ledgeGradient.bottom = Hex("B96C16");
            Shadow ledgeShadow = bottomLedge.gameObject.AddComponent<Shadow>();
            ledgeShadow.effectColor = new Color(0f, 0f, 0f, 0.66f);
            ledgeShadow.effectDistance = new Vector2(0f, -4f);

            CreateBoardBolt(rect, fill, new Vector2(-221f, 103f));
            CreateBoardBolt(rect, fill, new Vector2(221f, 103f));
            CreateBoardBolt(rect, fill, new Vector2(-221f, -103f));
            CreateBoardBolt(rect, fill, new Vector2(221f, -103f));

            Image pointerTargetGlow = CreateImage(rect, "PointerTargetGlow",
                new Vector2(-236f, 15f), new Vector2(38f, 38f), fill,
                new Color(1f, 0.69f, 0.20f, 0.28f), false);
            pointerTargetGlow.preserveAspect = false;
            UIPulse targetPulse = pointerTargetGlow.gameObject.AddComponent<UIPulse>();
            targetPulse.amplitude = 0.12f;
            targetPulse.speed = 2.4f;
            Image pointerTarget = CreateImage(rect, "PointerTarget",
                new Vector2(-236f, 15f), new Vector2(15f, 15f), fill,
                Hex("FFD866"), false);
            pointerTarget.preserveAspect = false;
            Outline targetOutline = pointerTarget.gameObject.AddComponent<Outline>();
            targetOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.90f);
            targetOutline.effectDistance = new Vector2(2f, -2f);
            return rect;
        }

        static void CreateBoardBolt(RectTransform board, Sprite fill, Vector2 position)
        {
            Image boltShadow = CreateImage(board, "BoltShadow", position + new Vector2(2f, -3f),
                new Vector2(13f, 13f), fill, new Color(0f, 0f, 0f, 0.72f), false);
            boltShadow.preserveAspect = false;
            Image boltGlow = CreateImage(board, "BoltGlow", position,
                new Vector2(23f, 23f), fill,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.30f), false);
            boltGlow.preserveAspect = false;
            Image bolt = CreateImage(board, "Bolt", position,
                new Vector2(11f, 11f), fill, Hex("31DFFF"), false);
            bolt.preserveAspect = false;
            Image boltHighlight = CreateImage(board, "BoltHighlight",
                position + new Vector2(-2f, 2f), new Vector2(4f, 4f), fill,
                new Color(1f, 1f, 1f, 0.88f), false);
            boltHighlight.preserveAspect = false;
        }

        static SpriteRenderer PlayerPart(GameObject playerPrefab, string name)
        {
            Transform child = playerPrefab != null ? playerPrefab.transform.Find(name) : null;
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        static Button CreateButton(RectTransform card, Font font, Sprite fill, Sprite glow)
        {
            var go = new GameObject("TryItGreenArcadeButton", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(UIButtonSfx));
            go.layer = card.gameObject.layer;
            go.transform.SetParent(card, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            // The old subtitle slot is now the single, clearly separated primary action.
            rect.anchoredPosition = new Vector2(0f, -296f);
            rect.sizeDelta = new Vector2(390f, 84f);

            // The transparent root owns the hit area; the layered children create a physical,
            // illuminated green cabinet button without changing any surrounding lesson art.
            Image hitArea = go.GetComponent<Image>();
            hitArea.sprite = null;
            hitArea.color = new Color(1f, 1f, 1f, 0.002f);
            hitArea.raycastTarget = true;

            Image aura = CreateImage(rect, "GreenButtonAura", new Vector2(0f, -2f),
                new Vector2(410f, 108f), glow,
                new Color(GreenTop.r, GreenTop.g, GreenTop.b, 0.24f), false);
            aura.preserveAspect = false;
            UIPulse auraPulse = aura.gameObject.AddComponent<UIPulse>();
            auraPulse.amplitude = 0.035f;
            auraPulse.speed = 2.1f;

            Image lip = CreateImage(rect, "Lip", new Vector2(0f, -3f),
                new Vector2(372f, 74f), fill, Hex("06351C"), false);
            lip.preserveAspect = false;
            Outline lipOutline = lip.gameObject.AddComponent<Outline>();
            lipOutline.effectColor = new Color(0.04f, 0.12f, 0.08f, 0.96f);
            lipOutline.effectDistance = new Vector2(1f, -1f);

            Image face = CreateImage(rect, "Face", Vector2.zero,
                new Vector2(368f, 70f), fill, Color.white, false);
            face.preserveAspect = false;
            UIGradient faceGradient = face.gameObject.AddComponent<UIGradient>();
            faceGradient.top = GreenTop;
            faceGradient.bottom = GreenBottom;
            Outline faceOutline = face.gameObject.AddComponent<Outline>();
            faceOutline.effectColor = new Color(0.72f, 1f, 0.70f, 0.98f);
            faceOutline.effectDistance = new Vector2(2f, -2f);

            Image gloss = CreateImage(rect, "Gloss", new Vector2(-10f, 17f),
                new Vector2(330f, 25f), fill, new Color(1f, 1f, 1f, 0.19f), false);
            gloss.preserveAspect = false;
            Image edgeLight = CreateImage(rect, "EdgeLight", new Vector2(0f, 28f),
                new Vector2(326f, 4f), fill, new Color(0.90f, 1f, 0.90f, 0.76f), false);
            edgeLight.preserveAspect = false;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = new Color(0.70f, 0.84f, 0.70f, 1f);
            colours.disabledColor = new Color(0.38f, 0.46f, 0.38f, 0.72f);
            colours.fadeDuration = 0.07f;
            button.colors = colours;
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;

            Text label = CreateText(rect, "Label", font, "TRY IT", 28, FontStyle.Bold, White,
                new Vector2(0f, 2f), new Vector2(300f, 48f));
            Shadow labelShadow = label.gameObject.AddComponent<Shadow>();
            labelShadow.effectColor = new Color(0f, 0.10f, 0.03f, 0.76f);
            labelShadow.effectDistance = new Vector2(0f, -3f);
            UIHoverScale hover = go.AddComponent<UIHoverScale>();
            hover.hover = 1.045f;
            hover.press = 0.95f;
            return button;
        }

        static Image CreateImage(Transform parent, string name, Vector2 position, Vector2 size,
            Sprite sprite, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            image.preserveAspect = sprite != null;
            return image;
        }

        static Text CreateText(Transform parent, string name, Font font, string value, int size,
            FontStyle style, Color color, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Text), typeof(Outline));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            CrispUiTypography.Polish(text);
            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0.02f, 0.05f, 0.92f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        static Material EnsureGestureMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GestureShaderPath);
            if (shader == null)
                throw new InvalidOperationException(
                    "The premium teacher gesture shader is missing at " + GestureShaderPath
                    + ". Wait for Unity to finish importing, then run this command again.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(GestureMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "PremiumTeacherGesture" };
                AssetDatabase.CreateAsset(material, GestureMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetFloat("_GestureAngle", 0f);
            material.SetFloat("_LeftStep", 0f);
            material.SetFloat("_RightStep", 0f);
            material.SetVector("_LeftPlant", Vector4.zero);
            material.SetVector("_RightPlant", Vector4.zero);
            material.SetFloat("_FreeArmAngle", 0f);
            material.SetFloat("_BodySway", 0f);
            material.SetFloat("_Breath", 0f);
            material.SetFloat("_EntranceLight", 0f);
            material.SetFloat("_HandOpen", 0.22f);
            material.SetVector("_ArmPivot", new Vector4(0.46f, 0.66f, 0f, 0f));
            material.SetVector("_ArmBounds", new Vector4(0.455f, 0.56f, 1f, 0.75f));
            EditorUtility.SetDirty(material);
            return material;
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            T[] candidates = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].gameObject.scene == scene)
                    return candidates[i];
            return null;
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
#endif
