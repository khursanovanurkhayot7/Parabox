using UnityEngine;

namespace Parabox
{
    // Marks a one-way flow cell — it may only be ENTERED while travelling along (dx,dy). Leaving
    // is unrestricted. LevelParser turns these into PRoom.oneway.
    public class OneWayMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public int dx;
        public int dy;
    }
}
