using Luxodd.Game.Scripts.Input;
using UnityEngine;

namespace Luxodd.Game.Example.Scripts
{
    public sealed class ControlExampleDemoBinder : MonoBehaviour
    {
        [Header("Presenters")]
        [SerializeField] private JoystickOverlayPresenter _joystickOverlayPresenter;
        [SerializeField] private ArcadeInputVisualPresenter _inputVisualPresenter;

        [Header("Joystick")]
        [SerializeField] private bool _clampJoystickMagnitude = true;

        [Header("Button Mapping")]
        [SerializeField] private ArcadeButtonColor _button1Color = ArcadeButtonColor.Black;
        [SerializeField] private ArcadeButtonColor _button2Color = ArcadeButtonColor.Red;
        [SerializeField] private ArcadeButtonColor _button3Color = ArcadeButtonColor.Green;
        [SerializeField] private ArcadeButtonColor _button4Color = ArcadeButtonColor.Yellow;
        [SerializeField] private ArcadeButtonColor _button5Color = ArcadeButtonColor.Blue;
        [SerializeField] private ArcadeButtonColor _button6Color = ArcadeButtonColor.Purple;
        [SerializeField] private ArcadeButtonColor _menuButtonColor = ArcadeButtonColor.Orange;
        [SerializeField] private ArcadeButtonColor _helpButtonColor = ArcadeButtonColor.White;

        private void OnEnable()
        {
            ApplyCurrentInputState();
        }

        private void Update()
        {
            ApplyCurrentInputState();
        }

        private void OnDisable()
        {
            ResetVisualState();
        }

        private void ApplyCurrentInputState()
        {
            Vector2 joystick = ArcadeControls.GetJoystick();
            if (_clampJoystickMagnitude)
            {
                joystick = Vector2.ClampMagnitude(joystick, 1.0f);
            }

            if (_joystickOverlayPresenter != null)
            {
                _joystickOverlayPresenter.SetVector(joystick);
            }

            if (_inputVisualPresenter == null)
            {
                return;
            }

            _inputVisualPresenter.SetButton1State(
                ArcadeControls.GetButton(_button1Color),
                ArcadeControls.GetButtonDown(_button1Color));
            _inputVisualPresenter.SetButton2State(
                ArcadeControls.GetButton(_button2Color),
                ArcadeControls.GetButtonDown(_button2Color));
            _inputVisualPresenter.SetButton3State(
                ArcadeControls.GetButton(_button3Color),
                ArcadeControls.GetButtonDown(_button3Color));
            _inputVisualPresenter.SetButton4State(
                ArcadeControls.GetButton(_button4Color),
                ArcadeControls.GetButtonDown(_button4Color));
            _inputVisualPresenter.SetButton5State(
                ArcadeControls.GetButton(_button5Color),
                ArcadeControls.GetButtonDown(_button5Color));
            _inputVisualPresenter.SetButton6State(
                ArcadeControls.GetButton(_button6Color),
                ArcadeControls.GetButtonDown(_button6Color));
            _inputVisualPresenter.SetMenuState(
                ArcadeControls.GetButton(_menuButtonColor),
                ArcadeControls.GetButtonDown(_menuButtonColor));
            _inputVisualPresenter.SetHelpState(
                ArcadeControls.GetButton(_helpButtonColor),
                ArcadeControls.GetButtonDown(_helpButtonColor));
        }

        private void ResetVisualState()
        {
            if (_joystickOverlayPresenter != null)
            {
                _joystickOverlayPresenter.SetVector(Vector2.zero);
            }

            if (_inputVisualPresenter == null)
            {
                return;
            }

            _inputVisualPresenter.SetButton1(false);
            _inputVisualPresenter.SetButton2(false);
            _inputVisualPresenter.SetButton3(false);
            _inputVisualPresenter.SetButton4(false);
            _inputVisualPresenter.SetButton5(false);
            _inputVisualPresenter.SetButton6(false);
            _inputVisualPresenter.SetMenu(false);
            _inputVisualPresenter.SetHelp(false);
        }
    }
}
