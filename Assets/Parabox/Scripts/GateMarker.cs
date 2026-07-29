using UnityEngine;

namespace Parabox
{
    // Marks a gate cell — solid until its matching switch is held. heavy pairs it with weight
    // plates; otherwise with shell buttons. LevelParser turns these into PRoom.gate/heavyGate.
    public class GateMarker : MonoBehaviour
    {
        public int x;
        public int y;
        public bool heavy;
    }
}
