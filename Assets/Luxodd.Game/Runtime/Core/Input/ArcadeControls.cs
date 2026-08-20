using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    /// <summary>
    /// Stable input facade shared by Luxodd's runtime assembly and its optional Input System
    /// backend. The backend is preferred; legacy Unity input is retained as a safe fallback.
    /// </summary>
    public static class ArcadeControls
    {
        private static IArcadeControlsBackend _backend;
        private static ArcadeInputMappingConfig _mappingConfig;

        public static void SetInputMappingConfig(ArcadeInputMappingConfig config)
        {
            _mappingConfig = config;
            _backend?.SetInputMappingConfig(config);
        }

        public static void RegisterBackend(IArcadeControlsBackend backend)
        {
            _backend = backend;

            if (_backend != null)
            {
                _backend.SetInputMappingConfig(_mappingConfig);
            }
        }

        public static bool GetButton(ArcadeButtonColor buttonColor)
        {
            return TryGetButtonFromBackend(buttonColor, out bool result)
                ? result
                : GetButtonLegacy(buttonColor);
        }

        public static bool GetButtonDown(ArcadeButtonColor buttonColor)
        {
            return TryGetButtonDownFromBackend(buttonColor, out bool result)
                ? result
                : GetButtonDownLegacy(buttonColor);
        }

        public static bool GetButtonUp(ArcadeButtonColor buttonColor)
        {
            return TryGetButtonUpFromBackend(buttonColor, out bool result)
                ? result
                : GetButtonUpLegacy(buttonColor);
        }

        public static Vector2 GetJoystick()
        {
            return TryGetJoystickFromBackend(out Vector2 result)
                ? result
                : GetJoystickLegacy();
        }

        private static bool TryGetButtonFromBackend(ArcadeButtonColor buttonColor, out bool result)
        {
            IArcadeControlsBackend backend = _backend;
            if (backend == null || !backend.IsAvailable)
            {
                result = false;
                return false;
            }

            return backend.TryGetButton(buttonColor, out result);
        }

        private static bool TryGetButtonDownFromBackend(ArcadeButtonColor buttonColor, out bool result)
        {
            IArcadeControlsBackend backend = _backend;
            if (backend == null || !backend.IsAvailable)
            {
                result = false;
                return false;
            }

            return backend.TryGetButtonDown(buttonColor, out result);
        }

        private static bool TryGetButtonUpFromBackend(ArcadeButtonColor buttonColor, out bool result)
        {
            IArcadeControlsBackend backend = _backend;
            if (backend == null || !backend.IsAvailable)
            {
                result = false;
                return false;
            }

            return backend.TryGetButtonUp(buttonColor, out result);
        }

        private static bool TryGetJoystickFromBackend(out Vector2 result)
        {
            IArcadeControlsBackend backend = _backend;
            if (backend == null || !backend.IsAvailable)
            {
                result = default;
                return false;
            }

            return backend.TryGetJoystick(out result);
        }

        private static bool GetButtonLegacy(ArcadeButtonColor buttonColor)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKey(GetLegacyKeyCode(buttonColor));
#else
            return false;
#endif
        }

        private static bool GetButtonDownLegacy(ArcadeButtonColor buttonColor)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyDown(GetLegacyKeyCode(buttonColor));
#else
            return false;
#endif
        }

        private static bool GetButtonUpLegacy(ArcadeButtonColor buttonColor)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyUp(GetLegacyKeyCode(buttonColor));
#else
            return false;
#endif
        }

        private static Vector2 GetJoystickLegacy()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            float horizontal = UnityEngine.Input.GetAxisRaw("Horizontal");
            float vertical = UnityEngine.Input.GetAxisRaw("Vertical");
            return new Vector2(horizontal, vertical);
#else
            return Vector2.zero;
#endif
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        private static KeyCode GetLegacyKeyCode(ArcadeButtonColor buttonColor)
        {
            switch (buttonColor)
            {
                case ArcadeButtonColor.Black:  return KeyCode.JoystickButton0;
                case ArcadeButtonColor.Red:    return KeyCode.JoystickButton1;
                case ArcadeButtonColor.Green:  return KeyCode.JoystickButton2;
                case ArcadeButtonColor.Yellow: return KeyCode.JoystickButton3;
                case ArcadeButtonColor.Blue:   return KeyCode.JoystickButton4;
                case ArcadeButtonColor.Purple: return KeyCode.JoystickButton5;
                case ArcadeButtonColor.Orange: return KeyCode.JoystickButton8;
                case ArcadeButtonColor.White:  return KeyCode.JoystickButton9;
                default:                       return KeyCode.None;
            }
        }
#endif
    }
}
