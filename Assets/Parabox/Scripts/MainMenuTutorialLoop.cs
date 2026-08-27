using System.Collections;
using UnityEngine;

namespace Parabox
{
    // Plays the already-authored mechanic vignette as the main-menu's quiet tutorial preview.
    // All graphics are serialized by the editor baker; runtime only moves the prebuilt pieces.
    public sealed class MainMenuTutorialLoop : MonoBehaviour
    {
        public MechanicDemoView demo;

        const string SkinKey = "Parabox.MenuPreviewSkin";
        int skinIndex;

        public bool IsPrebuilt => demo != null && demo.IsGameplayStylePrebuilt;

        IEnumerator Start()
        {
            // Let every prebuilt child finish Awake() before the first demonstration begins.
            yield return null;
            skinIndex = Mathf.Clamp(PlayerPrefs.GetInt(SkinKey, 0), 0, 2);
            while (isActiveAndEnabled)
            {
                if (demo != null) yield return PlayStyledDemo();
                yield return new WaitForSecondsRealtime(0.8f);
            }
        }

        IEnumerator PlayStyledDemo()
        {
            IEnumerator playback = demo.Play(MechanicCatalog.Id.Crate);
            while (playback.MoveNext())
            {
                ApplySkin();
                yield return playback.Current;
            }
            ApplySkin();
        }

        public void CycleSkin()
        {
            skinIndex = (skinIndex + 1) % 3;
            PlayerPrefs.SetInt(SkinKey, skinIndex);
            PlayerPrefs.Save();
            ApplySkin();
        }

        void ApplySkin()
        {
            if (demo == null) return;

            Color player;
            Color cargo;
            switch (skinIndex)
            {
                case 1:
                    player = new Color(0.45f, 0.96f, 0.78f, 1f);
                    cargo = new Color(1f, 0.28f, 0.69f, 1f);
                    break;
                case 2:
                    player = new Color(0.65f, 0.36f, 1f, 1f);
                    cargo = new Color(1f, 0.74f, 0.18f, 1f);
                    break;
                default:
                    player = new Color(0.09f, 0.76f, 1f, 1f);
                    cargo = new Color(1f, 0.59f, 0.075f, 1f);
                    break;
            }

            if (demo.playerImage != null) demo.playerImage.color = player;
            if (demo.cargoImage != null) demo.cargoImage.color = cargo;
            if (demo.goalImage != null)
                demo.goalImage.color = new Color(cargo.r, cargo.g, cargo.b, 0.72f);
        }

        void OnDisable()
        {
            if (demo != null) demo.HideImmediate();
        }
    }
}
