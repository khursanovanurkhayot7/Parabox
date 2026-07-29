using UnityEngine;

namespace Parabox
{
    public class GoalMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public bool forPlayer;
        public bool forEcho;            // the shadow diver's own goal
        public bool forMirror;          // the mirror diver's own goal
        public int colour;              // > 0 = accepts only the crate wearing this colour
    }
}
