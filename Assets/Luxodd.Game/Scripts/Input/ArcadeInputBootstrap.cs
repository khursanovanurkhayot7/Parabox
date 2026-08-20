using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    public sealed class ArcadeInputBootstrap : MonoBehaviour
    {
        [field: SerializeField]
        public ArcadeInputMappingConfigProvider ConfigProvider { get; private set; }

        private void Awake()
        {
            ArcadeInputMappingConfigProvider configProvider = ConfigProvider;
            if (configProvider == null)
            {
                return;
            }

            ArcadeInputMappingConfig mappingConfig = configProvider.MappingConfig;
            if (mappingConfig == null)
            {
                return;
            }

            ArcadeControls.SetInputMappingConfig(mappingConfig);
        }
    }
}
