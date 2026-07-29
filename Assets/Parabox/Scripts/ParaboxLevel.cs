using UnityEngine;

namespace Parabox
{
    // Root component of every level prefab.
    public class ParaboxLevel : MonoBehaviour
    {
        public string levelName = "Level";

        // The solver's optimal move count for this level (Tools/levelgen). The move limit is
        // derived from it at runtime, so the limit can never be set below what's achievable.
        public int par = 0;

        // The solver's actual optimal move sequence — "DDL", "RRURDDLD", ... The level-1 tutorial
        // replays this on the real board, so the demonstration is not a scripted mock-up: it is
        // the game playing itself, and it is provably a winning line.
        public string solution = "";
    }
}
