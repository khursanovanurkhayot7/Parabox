using System.Collections.Generic;
using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    [CreateAssetMenu(
        fileName = "ArcadeInputMappingConfig",
        menuName = "Luxodd Unity Plugin/Input/Arcade Input Mapping Config")]
    public sealed class ArcadeInputMappingConfig : ScriptableObject
    {
        [field: SerializeField] public ArcadeDeviceMatcher DeviceMatcher { get; private set; }

        [field: SerializeField] public List<ArcadeButtonBinding> ButtonBindings { get; private set; } = new();
    }
}
