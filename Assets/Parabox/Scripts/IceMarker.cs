using UnityEngine;

namespace Parabox
{
    // Marks a slippery (ice) cell on a level prefab — LevelParser turns these into PRoom.ice.
    public class IceMarker : MonoBehaviour
    {
        public int x;
        public int y;
    }
}
