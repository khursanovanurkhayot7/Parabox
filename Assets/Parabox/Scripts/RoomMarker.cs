using UnityEngine;

namespace Parabox
{
    // One grid room inside a level. Room 0 is the main (outermost) room.
    public class RoomMarker : MonoBehaviour
    {
        public int roomId;
        public int width;
        public int height;
    }
}
