using UnityEngine;

namespace Parabox
{
    // Marks a switch cell: a shell button (anything holds it) or, when heavy is set, a weight plate
    // that only a crate is heavy enough to hold. LevelParser turns these into PRoom.button/plate.
    public class SwitchMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public bool heavy;
    }
}
