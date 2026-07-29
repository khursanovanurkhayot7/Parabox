using UnityEngine;

namespace Parabox
{
    // Marks a whirlpool (portal) cell on a level prefab. LevelParser sorts these by (roomId, x, y),
    // pairs consecutive ones, and builds PRoom.portal + LevelModel.portalPair.
    public class PortalMarker : MonoBehaviour
    {
        public int x;
        public int y;
    }
}
