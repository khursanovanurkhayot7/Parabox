using UnityEngine;

namespace Luxodd.Game.Example.Scripts
{
    public sealed class ArcadeInputVisualPresenter : MonoBehaviour
    {
        [Header("Action Buttons")]
        [SerializeField] private ArcadeButtonVisual _button1;
        [SerializeField] private ArcadeButtonVisual _button2;
        [SerializeField] private ArcadeButtonVisual _button3;
        [SerializeField] private ArcadeButtonVisual _button4;
        [SerializeField] private ArcadeButtonVisual _button5;
        [SerializeField] private ArcadeButtonVisual _button6;

        [Header("System Buttons")]
        [SerializeField] private ArcadeButtonVisual _menuButton;
        [SerializeField] private ArcadeButtonVisual _helpButton;

        public void SetButton1(bool value)
        {
            SetPressed(_button1, value);
        }

        public void SetButton1State(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_button1, isPressed, wasPressedThisFrame);
        }

        public void SetButton2(bool value)
        {
            SetPressed(_button2, value);
        }

        public void SetButton2State(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_button2, isPressed, wasPressedThisFrame);
        }

        public void SetButton3(bool value)
        {
            SetPressed(_button3, value);
        }

        public void SetButton3State(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_button3, isPressed, wasPressedThisFrame);
        }

        public void SetButton4(bool value)
        {
            SetPressed(_button4, value);
        }

        public void SetButton4State(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_button4, isPressed, wasPressedThisFrame);
        }

        public void SetButton5(bool value)
        {
            SetPressed(_button5, value);
        }

        public void SetButton5State(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_button5, isPressed, wasPressedThisFrame);
        }

        public void SetButton6(bool value)
        {
            SetPressed(_button6, value);
        }

        public void SetButton6State(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_button6, isPressed, wasPressedThisFrame);
        }

        public void SetMenu(bool value)
        {
            SetPressed(_menuButton, value);
        }

        public void SetMenuState(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_menuButton, isPressed, wasPressedThisFrame);
        }

        public void SetHelp(bool value)
        {
            SetPressed(_helpButton, value);
        }

        public void SetHelpState(bool isPressed, bool wasPressedThisFrame)
        {
            SetState(_helpButton, isPressed, wasPressedThisFrame);
        }

        public void PulseButton1()
        {
            Pulse(_button1);
        }

        public void PulseButton2()
        {
            Pulse(_button2);
        }

        public void PulseButton3()
        {
            Pulse(_button3);
        }

        public void PulseButton4()
        {
            Pulse(_button4);
        }

        public void PulseButton5()
        {
            Pulse(_button5);
        }

        public void PulseButton6()
        {
            Pulse(_button6);
        }

        public void PulseMenu()
        {
            Pulse(_menuButton);
        }

        public void PulseHelp()
        {
            Pulse(_helpButton);
        }

        private static void SetPressed(ArcadeButtonVisual target, bool value)
        {
            if (target == null)
            {
                return;
            }

            target.SetPressed(value);
        }

        private static void SetState(ArcadeButtonVisual target, bool isPressed, bool wasPressedThisFrame)
        {
            if (target == null)
            {
                return;
            }

            target.SetPressed(isPressed);

            if (wasPressedThisFrame)
            {
                target.TriggerPulse();
            }
        }

        private static void Pulse(ArcadeButtonVisual target)
        {
            if (target == null)
            {
                return;
            }

            target.TriggerPulse();
        }
    }
}
