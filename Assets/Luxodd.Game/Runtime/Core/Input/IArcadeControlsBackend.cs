using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    public interface IArcadeControlsBackend
    {
        bool IsAvailable { get; }

        void SetInputMappingConfig(ArcadeInputMappingConfig config);

        bool TryGetButton(ArcadeButtonColor buttonColor, out bool result);

        bool TryGetButtonDown(ArcadeButtonColor buttonColor, out bool result);

        bool TryGetButtonUp(ArcadeButtonColor buttonColor, out bool result);

        bool TryGetJoystick(out Vector2 result);
    }
}
