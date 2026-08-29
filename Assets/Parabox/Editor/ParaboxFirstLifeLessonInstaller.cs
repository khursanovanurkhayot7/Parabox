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
        const string RootName = "FirstLifeLesson";
        static readonly Color NavyTop = Hex("12334D");
        static readonly Color NavyBottom = Hex("040B17");
        static readonly Color Cyan = Hex("42E7FF");
        static readonly Color Purple = Hex("9B58FF");
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
            AudioClip voiceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(VoiceClipPath);
            if (voiceClip == null)
                throw new InvalidOperationException(
                    "The offline lesson voice is missing at " + VoiceClipPath + ". Wait for Unity "
                    + "to finish importing, then run this command again.");

            RectTransform root = CreateRoot(parent);
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            FirstLifeLessonFx lesson = root.GetComponent<FirstLifeLessonFx>();

            CreateImage(root, "LessonGlow", Vector2.zero, new Vector2(1080f, 700f), glow,
                new Color(Purple.r, Purple.g, Purple.b, 0.24f), false);

            RectTransform card = CreateCard(root, fill);
            CreateImage(card, "TopAccent", new Vector2(0f, 244f), new Vector2(830f, 5f),
                null, Cyan, false);
            CreateText(card, "LessonLabel", font, "SECOND CHANCE", 18, FontStyle.Bold,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.82f), new Vector2(0f, 216f),
                new Vector2(360f, 32f));

            RectTransform player = CreatePlayerPortrait(card, game.playerPrefab, fill, glow,
                out CanvasGroup playerGroup, out RectTransform mouth,
                out RectTransform teachingArm, out RectTransform pointer);
            RectTransform speechBubble = CreateSpeechBubble(card, fill,
                out CanvasGroup speechGroup);
            CreateText(speechBubble, "Speaker", font, "PARABOX", 16, FontStyle.Bold, Cyan,
                new Vector2(-165f, 88f), new Vector2(120f, 28f));
            const string message =
                "ONE MORE CHANCE!\nI ONLY GET ONE LIFE.\nUSE UNDO OR RESTART!";
            Text speech = CreateText(speechBubble, "TypedMessage", font, message, 26,
                FontStyle.Bold, White, new Vector2(12f, -12f), new Vector2(390f, 154f));
            speech.alignment = TextAnchor.MiddleLeft;
            speech.lineSpacing = 1.08f;

            Button retry = CreateButton(card, font, fill);
            CreateText(card, "InputHint", font, "BLACK / ENTER  •  TRY AGAIN", 16,
                FontStyle.Bold, new Color(Cyan.r, Cyan.g, Cyan.b, 0.88f),
                new Vector2(134f, -218f), new Vector2(460f, 28f));

            lesson.group = group;
            lesson.card = card;
            lesson.tryAgainButton = retry;
            lesson.player = player;
            lesson.playerGroup = playerGroup;
            lesson.speechBubble = speechBubble;
            lesson.speechGroup = speechGroup;
            lesson.speechText = speech;
            lesson.mouth = mouth;
            lesson.teachingArm = teachingArm;
            lesson.pointer = pointer;
            lesson.voiceSource = root.GetComponent<AudioSource>();
            lesson.voiceClip = voiceClip;
            lesson.spokenMessage = message;
            lesson.charactersPerSecond = 17f;
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
                + "The premium teacher enters, speaks with synced mouth animation, and types a short UNDO / RESTART lesson.\n"
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
            dim.color = new Color(0.005f, 0.015f, 0.045f, 0.90f);
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

        static RectTransform CreatePlayerPortrait(RectTransform card, GameObject playerPrefab,
            Sprite fill, Sprite glow, out CanvasGroup group, out RectTransform mouth,
            out RectTransform teachingArm, out RectTransform pointer)
        {
            var go = new GameObject("TalkingPlayer", typeof(RectTransform), typeof(CanvasGroup));
            go.layer = card.gameObject.layer;
            go.transform.SetParent(card, false);
            RectTransform root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(-286f, 45f);
            root.sizeDelta = new Vector2(280f, 310f);
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

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
            var go = new GameObject("SpeechBubble", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup), typeof(Outline),
                typeof(Shadow), typeof(UIGradient));
            go.layer = card.gameObject.layer;
            go.transform.SetParent(card, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(142f, 56f);
            rect.sizeDelta = new Vector2(478f, 244f);

            Image face = go.GetComponent<Image>();
            face.sprite = fill;
            face.type = fill != null ? Image.Type.Sliced : Image.Type.Simple;
            face.color = Color.white;
            face.raycastTarget = false;
            UIGradient gradient = go.GetComponent<UIGradient>();
            gradient.top = Hex("30235B");
            gradient.bottom = Hex("120F2F");
            Outline outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.88f);
            outline.effectDistance = new Vector2(2f, -2f);
            Shadow shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(0f, -8f);
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

            Image tail = CreateImage(rect, "SpeechTail", new Vector2(-230f, -46f),
                new Vector2(38f, 38f), fill, Hex("1B1740"), false);
            tail.preserveAspect = false;
            tail.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tail.transform.SetAsFirstSibling();
            return rect;
        }

        static SpriteRenderer PlayerPart(GameObject playerPrefab, string name)
        {
            Transform child = playerPrefab != null ? playerPrefab.transform.Find(name) : null;
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        static Button CreateButton(RectTransform card, Font font, Sprite fill)
        {
            var go = new GameObject("TryLevelOneAgain", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(UIButtonSfx));
            go.layer = card.gameObject.layer;
            go.transform.SetParent(card, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(134f, -162f);
            rect.sizeDelta = new Vector2(420f, 78f);

            Image face = go.GetComponent<Image>();
            face.sprite = fill;
            face.type = fill != null ? Image.Type.Sliced : Image.Type.Simple;
            face.color = Color.white;
            face.raycastTarget = true;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = face;

            CreateText(rect, "Label", font, "TRY AGAIN", 24, FontStyle.Bold, White,
                Vector2.zero, rect.sizeDelta - new Vector2(24f, 12f));
            ArcadeActionButtonStyle.Apply(button, "TRY AGAIN", 24);
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
