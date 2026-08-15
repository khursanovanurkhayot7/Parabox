using Luxodd.Game.Scripts.Input;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Luxodd.Game
{
    public static class ArcadeControls
    {
        
        public static ArcadeInputConfigAsset Config { get; set; }

        public static bool GetButton(ArcadeButtonColor buttonColor)
        {
#if ENABLE_INPUT_SYSTEM
            if (IsNewInputActive())
                return GetButton_New(buttonColor);
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return GetButton_Legacy(buttonColor);
#else
            return false;
#endif
        }

        public static bool GetButtonDown(ArcadeButtonColor buttonColor)
        {
#if ENABLE_INPUT_SYSTEM
            if (IsNewInputActive())
                return GetButtonDown_New(buttonColor);
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return GetButtonDown_Legacy(buttonColor);
#else
            return false;
#endif
        }

        public static bool GetButtonUp(ArcadeButtonColor buttonColor)
        {
#if ENABLE_INPUT_SYSTEM
            if (IsNewInputActive())
                return GetButtonUp_New(buttonColor);
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return GetButtonUp_Legacy(buttonColor);
#else
            return false;
#endif
        }

        /// <summary>
        /// Returns stick axes as ArcadeStick (Vector2 internally).
        /// Uses Input System if enabled/active; otherwise uses Legacy Input Manager axes.
        /// </summary>
        public static ArcadeStick GetStick()
        {
            var config = Config;

            var deadZone = config ? config.DeadZone : 0.15f;
            var invertX = config && config.InvertX;
            var invertY = config && config.InvertY;

            Vector2 raw;

#if ENABLE_INPUT_SYSTEM
            if (IsNewInputActive())
            {
                raw = GetStick_New();
            }
            else
#endif
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                raw = GetStick_Legacy(config);
#else
                raw = Vector2.zero;
#endif
            }

            if (invertX) raw.x *= -1f;
            if (invertY) raw.y *= -1f;

            var deadZoneVector = ApplyDeadZone(raw, deadZone);
            return new ArcadeStick(deadZoneVector.x, deadZoneVector.y);
        }


        // Legacy Input Manager implementation

#if ENABLE_LEGACY_INPUT_MANAGER
        private static bool GetButton_Legacy(ArcadeButtonColor buttonColor) =>
            UnityEngine.Input.GetKey(ArcadeUnityMapping.GetKeyCode(buttonColor));

        private static bool GetButtonDown_Legacy(ArcadeButtonColor buttonColor) =>
            UnityEngine.Input.GetKeyDown(ArcadeUnityMapping.GetKeyCode(buttonColor));

        private static bool GetButtonUp_Legacy(ArcadeButtonColor buttonColor) =>
            UnityEngine.Input.GetKeyUp(ArcadeUnityMapping.GetKeyCode(buttonColor));

        private static Vector2 GetStick_Legacy(ArcadeInputConfigAsset config)
        {
            var xAxis = config ? config.HorizontalAxisName : "Horizontal";
            var yAxis = config ? config.VerticalAxisName : "Vertical";

            var xValue = SafeGetAxis_Legacy(xAxis);
            var yValue = SafeGetAxis_Legacy(yAxis);

            return new Vector2(xValue, yValue);
        }

        private static float SafeGetAxis_Legacy(string axisName)
        {
            try
            {
                return UnityEngine.Input.GetAxis(axisName);
            }
            catch
            {
                return 0f;
            }
        }
#endif


        // New Input System implementation (Joystick preferred; Gamepad fallback)
        
#if ENABLE_INPUT_SYSTEM
        /// <summary>
        /// Detects whether the Input System is active in runtime.
        /// In Unity 6 with Active Input Handling = "Input System Package (New)",
        /// calling UnityEngine.Input will throw, so we route through InputSystem.
        /// </summary>
        private static bool IsNewInputActive()
        {
            // In projects where Input System is enabled, InputSystem.settings is non-null.
            return InputSystem.settings != null;
        }

        private static bool GetButton_New(ArcadeButtonColor buttonColor)
        {
            var joystick = MapColorToJoystickButton(buttonColor, GetArcadeJoystick());
            var gamepad = ColorToGamepadButton(buttonColor, Gamepad.current);
            return (joystick != null && joystick.isPressed)
                || (gamepad != null && gamepad.isPressed);
        }

        private static bool GetButtonDown_New(ArcadeButtonColor buttonColor)
        {
            var joystick = MapColorToJoystickButton(buttonColor, GetArcadeJoystick());
            var gamepad = ColorToGamepadButton(buttonColor, Gamepad.current);
            return (joystick != null && joystick.wasPressedThisFrame)
                || (gamepad != null && gamepad.wasPressedThisFrame);
        }

        private static bool GetButtonUp_New(ArcadeButtonColor buttonColor)
        {
            var joystick = MapColorToJoystickButton(buttonColor, GetArcadeJoystick());
            var gamepad = ColorToGamepadButton(buttonColor, Gamepad.current);
            return (joystick != null && joystick.wasReleasedThisFrame)
                || (gamepad != null && gamepad.wasReleasedThisFrame);
        }

        private static Vector2 GetStick_New()
        {
            // Read both device families. A development machine may have an idle HID joystick and
            // an active standard gamepad connected at the same time; choosing Joystick.current
            // unconditionally made the gamepad appear dead in that setup.
            Vector2 strongest = Vector2.zero;
            var js = GetArcadeJoystick();
            if (js != null)
                strongest = js.stick.ReadValue();

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 leftStick = pad.leftStick.ReadValue();
                Vector2 dpad = pad.dpad.ReadValue();
                Vector2 gamepad = dpad.sqrMagnitude > leftStick.sqrMagnitude ? dpad : leftStick;
                if (gamepad.sqrMagnitude > strongest.sqrMagnitude) strongest = gamepad;
            }

            return strongest;
        }

        private static Joystick GetArcadeJoystick()
        {
            // Often arcade controllers show up as Joystick (HID).
            return Joystick.current;
        }

        /// <summary>
        /// Returns the fixed Luxodd physical button on a HID/generic joystick.
        /// </summary>
        private static ButtonControl MapColorToJoystickButton(ArcadeButtonColor color, Joystick joystick)
        {
            if (joystick == null) return null;
            var index = ColorToJoystickButtonIndex(color);
            return index >= 0
                ? joystick.TryGetChildControl<ButtonControl>($"button{index}")
                : null;
        }

        /// <summary>
        /// We are use this mapping:
        /// Black=0, Red=1, Green=2, Yellow=3, Blue=4, Purple=5, Orange=8, White=9
        /// </summary>
        private static int ColorToJoystickButtonIndex(ArcadeButtonColor color)
        {
            return color switch
            {
                ArcadeButtonColor.Black  => 0,
                ArcadeButtonColor.Red    => 1,
                ArcadeButtonColor.Green  => 2,
                ArcadeButtonColor.Yellow => 3,
                ArcadeButtonColor.Blue   => 4,
                ArcadeButtonColor.Purple => 5,
                ArcadeButtonColor.Orange => 8,
                ArcadeButtonColor.White  => 9,
                _ => -1
            };
        }

        /// <summary>
        /// Standard-gamepad fallback. It keeps the same named Parabox actions while using a
        /// conventional, non-overlapping layout: A confirm, B back, X undo, Y restart,
        /// Start level-select, LB mute, RB skip walkthrough, Select Luxodd help.
        /// </summary>
        private static ButtonControl ColorToGamepadButton(ArcadeButtonColor color, Gamepad pad)
        {
            if (pad == null) return null;
            return color switch
            {
                ArcadeButtonColor.Black  => pad.buttonSouth, // A / Cross
                ArcadeButtonColor.White  => pad.buttonEast,  // B / Circle
                ArcadeButtonColor.Red    => pad.buttonWest,  // X / Square
                ArcadeButtonColor.Green  => pad.buttonNorth, // Y / Triangle
                ArcadeButtonColor.Yellow => pad.startButton,
                ArcadeButtonColor.Blue   => pad.leftShoulder,
                ArcadeButtonColor.Purple => pad.rightShoulder,
                ArcadeButtonColor.Orange => pad.selectButton,

                _ => null
            };
        }
#endif
        
        // Helpers

        private static Vector2 ApplyDeadZone(Vector2 input, float deadZone)
        {
            if (deadZone <= 0f) return input;

            var magnitude = input.magnitude;
            if (magnitude < deadZone) return Vector2.zero;

            var scaled = (magnitude - deadZone) / (1f - deadZone);
            return input.normalized * Mathf.Clamp01(scaled);
        }
    }
}
