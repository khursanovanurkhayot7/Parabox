using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    public static class ArcadeInput
    {
        public static bool GetButton(ArcadeButtonColor buttonColor)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKey(ArcadeUnityMapping.GetKeyCode(buttonColor));
#else
            return false;
#endif
        }

        public static bool GetButtonDown(ArcadeButtonColor buttonColor)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyDown(ArcadeUnityMapping.GetKeyCode(buttonColor));
#else
            return false;
#endif
        }

        public static bool GetButtonUp(ArcadeButtonColor buttonColor)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyUp(ArcadeUnityMapping.GetKeyCode(buttonColor));
#else
            return false;
#endif
        }

        public static float Horizontal
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return UnityEngine.Input.GetAxis("Horizontal");
#else
                return 0f;
#endif
            }
        }

        public static float Vertical
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return UnityEngine.Input.GetAxis("Vertical");
#else
                return 0f;
#endif
            }
        }
    }
}
