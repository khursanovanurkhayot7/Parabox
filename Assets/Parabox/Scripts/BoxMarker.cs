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
        public int colour;              // 0 = ordinary crate; > 0 = must land on ITS colour of goal
        public bool slick;              // keeps sliding once shoved
        public bool boulder;            // only shifts if the pusher was already moving that way
        public bool locking;            // sets in stone the moment it reaches a mark
        public bool fragile;            // survives one shove, shatters on the second
        public bool anchored;           // a meta-box that can be entered but never pushed
    }
}
