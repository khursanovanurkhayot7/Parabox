using UnityEngine;

namespace Parabox
{
    public class PlayerMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public bool isEcho;             // the shadow diver that copies the player's every move
        public bool isMirror;           // the diver that moves the OPPOSITE way to the player
    }
}
