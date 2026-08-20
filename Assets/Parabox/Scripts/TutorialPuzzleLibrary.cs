using UnityEngine;

namespace Parabox
{
    // Resolves prebuilt tutorial mini-puzzles. Chapter boards are used at Levels 1/11/21/31/41;
    // at most one focused Mechanic_* board is used for the first supported new rule in each
    // chapter. Later new rules never create extra tutorial interruptions.
    public static class TutorialPuzzleLibrary
    {
        const string Root = "Parabox/Tutorials/";

        public static string ResourcePath(int levelIndex, MechanicCatalog.Id mechanic)
        {
            switch (levelIndex)
            {
                case 0 when mechanic == MechanicCatalog.Id.Navigation: return Root + "Chapter_1";
                case 10 when mechanic == MechanicCatalog.Id.NestedBoard: return Root + "Chapter_2";
                case 20 when mechanic == MechanicCatalog.Id.NestedBoard: return Root + "Chapter_3";
                case 30 when mechanic == MechanicCatalog.Id.NestedBoard: return Root + "Chapter_4";
                case 40 when mechanic == MechanicCatalog.Id.ColourCargo: return Root + "Chapter_5";
                default: return Root + "Mechanic_" + mechanic;
            }
        }

        public static GameObject Load(int levelIndex, MechanicCatalog.Id mechanic)
        {
            string path = ResourcePath(levelIndex, mechanic);
            return string.IsNullOrEmpty(path) ? null : Resources.Load<GameObject>(path);
        }

        public static bool Exists(int levelIndex, MechanicCatalog.Id mechanic)
            => Load(levelIndex, mechanic) != null;
    }
}
