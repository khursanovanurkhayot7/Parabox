using UnityEngine;

namespace Parabox
{
    // Marks a trench (gap) cell on a level prefab — LevelParser turns these into PRoom.trench.
    public class TrenchMarker : MonoBehaviour
    {
        public int x;
        public int y;
    }
}
