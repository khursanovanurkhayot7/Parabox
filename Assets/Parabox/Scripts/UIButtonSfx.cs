using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Kept in its own matching source file so Unity can serialize this component into scenes.
    // Button.onClick covers mouse/touch release as well as keyboard/controller Submit. Keeping
    // this separate from hover animation prevents a pointer click from sounding twice.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class UIButtonSfx : MonoBehaviour
    {
        Button button;

        void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(Play);
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(Play);
        }

        void Play()
        {
            if (button != null && button.interactable) Sfx.Click();
        }
    }
}
