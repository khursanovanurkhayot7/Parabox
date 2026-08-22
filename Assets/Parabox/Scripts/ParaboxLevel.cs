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

        // The intended player-facing difficulty on the 1-10 campaign curve. This is authored from
        // the requested checkpoints, not inferred from the timer or move allowance.
        [Range(1f, 10f)] public float difficultyRating = 1f;

        // Composite design score used to order boards: solution depth, mechanic vocabulary,
        // dependencies, interacting objects, targets and nested rooms all contribute.
        public int designComplexity = 0;

        [Header("Campaign curriculum")]
        [Range(1, 5)] public int chapter = 1;
        public string chapterName = "Foundations";
        public string chapterPhilosophy = "Learn";
        public CampaignProgression.LevelRole progressionRole = CampaignProgression.LevelRole.Teach;
        public string mechanicFocus = "Movement";
        public bool introducesMechanic = false;

        // The solver's actual optimal move sequence — "DDL", "RRURDDLD", ... The level-1 tutorial
        // replays this on the real board, so the demonstration is not a scripted mock-up: it is
        // the game playing itself, and it is provably a winning line.
        public string solution = "";

        // Solution repair may replace stale proof metadata without being allowed to rebalance the
        // board around a different route. When this lock is enabled, LevelParser keeps using the
        // pre-repair proof below for its route-safe runtime decoration. These fields are hidden
        // because they are a preservation contract, not level-design controls.
        [HideInInspector] public bool preserveRuntimeLayoutProof = false;
        [HideInInspector] public string runtimeLayoutProof = "";
    }
}
