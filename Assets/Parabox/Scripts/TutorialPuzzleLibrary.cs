using UnityEngine;

namespace Parabox
{
    // Resolves the prebuilt mini-puzzle used by a tutorial. Chapter openers deliberately have
    // their own boards, even when they refresh the same recursive-room rule: Levels 21, 31 and 41
    // therefore never replay one another and never borrow the campaign level being introduced.
    public static class TutorialPuzzleLibrary
    {
        const string Root = "Parabox/Tutorials/";

        public static string ResourcePath(int levelIndex, MechanicCatalog.Id mechanic)
        {
            switch (levelIndex)
            {
                case 0 when mechanic == MechanicCatalog.Id.Navigation: return Root + "Chapter_1";
                case 10 when mechanic == MechanicCatalog.Id.Mirror: return Root + "Chapter_2";
                case 20 when mechanic == MechanicCatalog.Id.NestedBoard: return Root + "Chapter_3";
                case 30 when mechanic == MechanicCatalog.Id.NestedBoard: return Root + "Chapter_4";
                case 40 when mechanic == MechanicCatalog.Id.NestedBoard: return Root + "Chapter_5";
                default: return Root + "Mechanic_" + mechanic;
            }
        }

        public static GameObject Load(int levelIndex, MechanicCatalog.Id mechanic)
            => Resources.Load<GameObject>(ResourcePath(levelIndex, mechanic));

        public static bool Exists(int levelIndex, MechanicCatalog.Id mechanic)
            => Load(levelIndex, mechanic) != null;
    }
}
