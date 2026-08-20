#if LUXODD_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Luxodd.Game.Scripts.Input
{
    public static class StandardGamepadMapping
    {
        public static ButtonControl GetButtonControl(Gamepad gamepad, ArcadeButtonColor buttonColor)
        {
            if (gamepad == null)
            {
                return null;
            }

            return buttonColor switch
            {
                ArcadeButtonColor.Black => gamepad.buttonSouth,
                ArcadeButtonColor.Red => gamepad.buttonEast,
                ArcadeButtonColor.Green => gamepad.buttonWest,
                ArcadeButtonColor.Yellow => gamepad.buttonNorth,
                ArcadeButtonColor.Blue => gamepad.leftShoulder,
                ArcadeButtonColor.Purple => gamepad.rightShoulder,
                ArcadeButtonColor.Orange => gamepad.selectButton,
                ArcadeButtonColor.White => gamepad.startButton,
                _ => null
            };
        }
    }
}
#endif
