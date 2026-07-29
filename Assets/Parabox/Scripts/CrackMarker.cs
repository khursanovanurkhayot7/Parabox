using UnityEngine;

namespace Parabox
{
    // Marks cracked coral — it collapses into a permanent hole once whoever started a move on it
    // leaves. LevelParser turns these into PRoom.cracked.
    public class CrackMarker : MonoBehaviour
    {
        public int x;
        public int y;
    }
}
