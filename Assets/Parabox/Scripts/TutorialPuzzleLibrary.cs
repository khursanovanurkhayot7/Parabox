using UnityEngine;

namespace Parabox
{
    // Resolves prebuilt tutorial mini-puzzles. Later chapters reuse the Level 1 foundation board.
    // Chapter I adds button/gate before Level 5. Chapter III introduces the premium portal before
    // Level 25 and keeps it active through the end of the chapter.
    public static class TutorialPuzzleLibrary
    {
        const string Root = "Parabox/Tutorials/";

        public static string ResourcePath(int levelIndex, MechanicCatalog.Id mechanic)
        {
            // Later chapter openers reuse the exact Level 1 foundation mini-board. Chapter V also
            // reuses the exact Chapter IV room-docking board before its own finale tutorial.
            if (levelIndex > 0 && levelIndex % 10 == 0
                && mechanic == MechanicCatalog.Id.Navigation)
                return Root + "Chapter_1";
            if (levelIndex == 40 && mechanic == MechanicCatalog.Id.NestedBoard)
                return Root + "Chapter_4";

            switch (levelIndex)
            {
                case 0 when mechanic == MechanicCatalog.Id.Navigation: return Root + "Chapter_1";
                case 4 when mechanic == MechanicCatalog.Id.FoundationMechanics:
                    return Root + "Chapter_1_Mechanics";
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
