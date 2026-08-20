#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Parabox.EditorTools
{
    // Designer-run authoring command. It creates and serializes project content only: it never
    // enters Play Mode and it never launches the campaign/UI validators. The designer performs
    // gameplay and visual QA after generation finishes.
    public static class ParaboxPublishContentGenerator
    {
        const string MenuPath = "Tools/Parabox/GENERATE Publish Content (No Play Mode)";
        const string TutorialMenuPath = "Tools/Parabox/GENERATE All Tutorials (No Play Mode)";

        [MenuItem(TutorialMenuPath, priority = 1)]
        public static void GenerateAllTutorials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Parabox Tutorial Generator",
                    "Exit Play Mode first. This command only runs in Edit Mode.", "OK");
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Parabox Tutorial Generator",
                    "Wait for Unity to finish compiling/importing, then run the command again.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("Generate all tutorials?",
                "This creates the five chapter tutorials and the independent NEW MECHANIC " +
                "examples. At runtime each chapter shows one chapter video, plus at most one " +
                "mechanic video at that chapter's first supported new rule. A chapter with no " +
                "supported new mechanic keeps only its one chapter video.\n\n" +
                "It will NOT enter Play Mode. In Edit Mode it solves and replays every tiny " +
                "tutorial before saving, so a route cannot stop without winning.",
                "GENERATE", "CANCEL");
            if (!confirmed) return;

            try
            {
                EditorUtility.DisplayProgressBar("Parabox", "Creating all prebuilt tutorials...", 0.5f);
                ParaboxSetupWizard.RegenerateTutorialMiniLevelsAuthoringOnly();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("Parabox: chapter and chapter-capped mechanic tutorials generated and solver-verified without Play Mode.");
                EditorUtility.DisplayDialog("Parabox Tutorial Generator",
                    "Finished. A chapter now shows one chapter video and no more than one " +
                    "NEW MECHANIC video. Every prebuilt mini-puzzle was solved and replayed " +
                    "before saving.\n\n" +
                    "No Play Mode test was run.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Parabox tutorial generation stopped",
                    exception.Message + "\n\nSee the Console for the full error.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(MenuPath, priority = 0)]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Parabox Content Generator",
                    "Exit Play Mode first. This command only runs in Edit Mode.", "OK");
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog("Parabox Content Generator",
                    "Wait for Unity to finish compiling/importing, then run the command again.", "OK");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            bool confirmed = EditorUtility.DisplayDialog("Generate publish content?",
                "This will rebuild the 50 level prefabs, all prebuilt tutorials, and the " +
                "serialized Main Menu/Game scene objects from the current authoring scripts. " +
                "Before writing any level, it checks the reviewed difficulty floors, unique " +
                "layouts, valid winning routes and that every placed mechanic is required by " +
                "the solution. NEW MECHANIC tutorials are tied to real first appearances.\n\n" +
                "It will NOT enter Play Mode or run the release validators.",
                "GENERATE", "CANCEL");
            if (!confirmed) return;

            try
            {
                EditorUtility.DisplayProgressBar("Parabox", "Creating Levels 1-50...", 0.15f);
                ParaboxSetupWizard.RegenerateDifficultyOrderedCampaignAuthoringOnly();

                EditorUtility.DisplayProgressBar("Parabox", "Creating all prebuilt tutorials...", 0.58f);
                ParaboxSetupWizard.RegenerateTutorialMiniLevelsAuthoringOnly();

                EditorUtility.DisplayProgressBar("Parabox", "Creating serialized scene and UI objects...", 0.82f);
                LuxoddArcadeUiSceneBaker.BakeAllSilent();

                EditorUtility.DisplayProgressBar("Parabox", "Saving generated content...", 0.96f);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("Parabox: publish content generation finished. No Play Mode or release validation was run.");
                EditorUtility.DisplayDialog("Parabox Content Generator",
                    "Generation finished.\n\n" +
                    "Created:\n" +
                    "- Levels 1-50 after difficulty, route and mechanic-use gates passed\n" +
                    "- Five chapter videos plus at most one NEW MECHANIC video per chapter\n" +
                    "- Main Menu and Game scene objects/UI\n\n" +
                    "No Play Mode test was run. You can now check the game yourself.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Parabox generation stopped",
                    exception.Message + "\n\nSee the Console for the full error.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(MenuPath, true)]
        static bool CanGenerate()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating;
        }

        [MenuItem(TutorialMenuPath, true)]
        static bool CanGenerateAllTutorials()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating;
        }
    }
}
#endif
