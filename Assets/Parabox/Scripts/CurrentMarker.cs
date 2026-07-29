using UnityEngine;

namespace Parabox
{
    // Marks a current cell — after landing here you are carried one cell along (dx,dy), and that
    // becomes your new travel direction. LevelParser turns these into PRoom.current.
    public class CurrentMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public int dx;
        public int dy;
    }
}
