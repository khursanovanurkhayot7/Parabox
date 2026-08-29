#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    // One reviewed Edit-Mode command for the tester's paired report. It rebuilds only Levels 5/6,
    // then repairs the serialized purple tutorial action in Game.unity. It never enters Play Mode.
    public static class ParaboxLevelFiveSixTutorialFixInstaller
    {
        [MenuItem("Tools/Parabox/Fix Levels 5-6 + Tutorial Skip", priority = 2)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Exit Play Mode before rebuilding Levels 5/6 and the tutorial Skip button.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException(
                    "Unity is compiling or importing. Wait until it finishes, then run the command again.");

            ParaboxSetupWizard.RegenerateLevelsFiveAndSixSilent();
            ParaboxPurpleSkipInstaller.Install();

            Debug.Log("Parabox: Level 5/6 differentiation and tutorial Skip repair completed "
                + "entirely in Edit Mode.");
        }
    }
}
#endif
