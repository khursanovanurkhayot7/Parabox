#if LUXODD_INPUT_SYSTEM
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Luxodd.Game.Scripts.Input
{
    public sealed class ArcadeControlsInputSystemBackend : IArcadeControlsBackend
    {
        private ArcadeInputMappingConfig _mappingConfig;

        public bool IsAvailable => true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterBackend()
        {
            ArcadeControls.RegisterBackend(new ArcadeControlsInputSystemBackend());
        }

        public void SetInputMappingConfig(ArcadeInputMappingConfig config)
        {
            _mappingConfig = config;
        }

        public bool TryGetButton(ArcadeButtonColor buttonColor, out bool result)
        {
            if (TryGetArcadeButtonState(buttonColor, ButtonReadMode.Pressed, out result))
            {
                return true;
            }

            if (TryGetStandardGamepadButtonState(buttonColor, ButtonReadMode.Pressed, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        public bool TryGetButtonDown(ArcadeButtonColor buttonColor, out bool result)
        {
            if (TryGetArcadeButtonState(buttonColor, ButtonReadMode.PressedThisFrame, out result))
            {
                return true;
            }

            if (TryGetStandardGamepadButtonState(buttonColor, ButtonReadMode.PressedThisFrame, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        public bool TryGetButtonUp(ArcadeButtonColor buttonColor, out bool result)
        {
            if (TryGetArcadeButtonState(buttonColor, ButtonReadMode.ReleasedThisFrame, out result))
            {
                return true;
            }

            if (TryGetStandardGamepadButtonState(buttonColor, ButtonReadMode.ReleasedThisFrame, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        public bool TryGetJoystick(out Vector2 result)
        {
            if (TryGetArcadeJoystick(out result))
            {
                return true;
            }

            if (TryGetStandardGamepadJoystick(out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        private enum ButtonReadMode
        {
            Pressed,
            PressedThisFrame,
            ReleasedThisFrame
        }

        private bool TryGetArcadeButtonState(
            ArcadeButtonColor buttonColor,
            ButtonReadMode readMode,
            out bool result)
        {
            result = false;

            InputDevice arcadeDevice = GetArcadeDevice();
            if (arcadeDevice == null)
            {
                return false;
            }

            ArcadeInputMappingConfig mappingConfig = _mappingConfig;
            if (mappingConfig == null)
            {
                return false;
            }

            int buttonIndex = 0;

            foreach (InputControl inputControl in arcadeDevice.allControls)
            {
                if (inputControl is not ButtonControl buttonControl)
                {
                    continue;
                }

                if (ShouldIgnoreButton(buttonControl))
                {
                    continue;
                }

                bool isMatchingButton = ArcadeInputMatcher.IsMatchingButton(
                    arcadeDevice,
                    buttonControl,
                    buttonIndex,
                    mappingConfig,
                    buttonColor);

                if (isMatchingButton)
                {
                    result = ReadButtonState(buttonControl, readMode);
                    return true;
                }

                buttonIndex++;
            }

            return false;
        }

        private bool TryGetStandardGamepadButtonState(
            ArcadeButtonColor buttonColor,
            ButtonReadMode readMode,
            out bool result)
        {
            result = false;

            Gamepad gamepad = GetStandardGamepad();
            if (gamepad == null)
            {
                return false;
            }

            ButtonControl buttonControl = StandardGamepadMapping.GetButtonControl(gamepad, buttonColor);
            if (buttonControl == null)
            {
                return false;
            }

            result = ReadButtonState(buttonControl, readMode);
            return true;
        }

        private bool TryGetArcadeJoystick(out Vector2 result)
        {
            result = default;

            InputDevice arcadeDevice = GetArcadeDevice();
            if (arcadeDevice == null)
            {
                return false;
            }

            if (arcadeDevice is Joystick joystick)
            {
                result = joystick.stick.ReadValue();
                return true;
            }

            if (arcadeDevice is Gamepad gamepad)
            {
                result = ReadGamepadDirection(gamepad);
                return true;
            }

            return false;
        }

        private bool TryGetStandardGamepadJoystick(out Vector2 result)
        {
            result = default;

            Gamepad gamepad = GetStandardGamepad();
            if (gamepad == null)
            {
                return false;
            }

            result = ReadGamepadDirection(gamepad);
            return true;
        }

        // A standard controller player naturally expects both the left stick and D-pad to move
        // through menus and puzzles. The package previously exposed only leftStick, which made a
        // connected controller appear unresponsive whenever the player used its D-pad.
        private static Vector2 ReadGamepadDirection(Gamepad gamepad)
        {
            Vector2 dpad = gamepad.dpad.ReadValue();
            return dpad.sqrMagnitude >= 0.01f ? dpad : gamepad.leftStick.ReadValue();
        }

        private InputDevice GetArcadeDevice()
        {
            ArcadeInputMappingConfig mappingConfig = _mappingConfig;
            if (mappingConfig == null)
            {
                return null;
            }

            ReadOnlyArray<InputDevice> devices = InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                InputDevice device = devices[i];
                if (device == null)
                {
                    continue;
                }

                if (ArcadeInputMatcher.IsMatchingDevice(device, mappingConfig.DeviceMatcher))
                {
                    return device;
                }
            }

            return null;
        }

        private Gamepad GetStandardGamepad()
        {
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return null;
            }

            InputDevice arcadeDevice = GetArcadeDevice();
            if (arcadeDevice != null && ReferenceEquals(gamepad, arcadeDevice))
            {
                return null;
            }

            return gamepad;
        }

        private static bool ReadButtonState(ButtonControl buttonControl, ButtonReadMode readMode)
        {
            return readMode switch
            {
                ButtonReadMode.Pressed => buttonControl.isPressed,
                ButtonReadMode.PressedThisFrame => buttonControl.wasPressedThisFrame,
                ButtonReadMode.ReleasedThisFrame => buttonControl.wasReleasedThisFrame,
                _ => false
            };
        }

        private static bool ShouldIgnoreButton(ButtonControl buttonControl)
        {
            if (buttonControl == null)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(buttonControl.path) &&
                buttonControl.path.IndexOf("/dpad/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(buttonControl.name))
            {
                return false;
            }

            return string.Equals(buttonControl.name, "leftStickPress", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(buttonControl.name, "rightStickPress", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
