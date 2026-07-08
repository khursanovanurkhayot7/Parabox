using UnityEngine;

namespace Parabox
{
    // A pushable box. If containsRoomId >= 0 this is a meta-box whose
    // inside IS that room (the Patrick's Parabox mechanic).
    public class BoxMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public int containsRoomId = -1;
    }
}
