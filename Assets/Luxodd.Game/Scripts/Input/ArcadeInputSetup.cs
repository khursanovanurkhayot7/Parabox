using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    public class ArcadeInputSetup : MonoBehaviour
    {
        [SerializeField] private ArcadeInputConfigAsset _inputConfigAsset;
        [SerializeField] private ArcadeInputMappingConfig _mappingConfig;

        private void Awake()
        {
            ArcadeControls.SetInputMappingConfig(_mappingConfig);
        }
    }
}
