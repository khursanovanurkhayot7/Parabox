using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    public sealed class ArcadeInputMappingConfigProvider : MonoBehaviour
    {
        [field: SerializeField]
        public ArcadeInputMappingConfig MappingConfig { get; private set; }
    }
}
