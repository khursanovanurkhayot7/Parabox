using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Luxodd.Game;
using Luxodd.Game.Scripts.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace Parabox.EditorTools
{
    // Verifies the complete Luxodd cabinet contract plus the standard-gamepad fallback. The
    // Binding Editor is documentation only, so this audit checks the code mapping and the labels.
    public static class ParaboxLuxoddControlsValidator
    {
        const string BindingPath = "Assets/Luxodd.Game/Editor/DefaultAssets/ArcadeBindingAsset.asset";
        const string MenuPath = "Assets/Parabox/Scenes/MainMenu.unity";
        const string GamePath = "Assets/Parabox/Scenes/Game.unity";
        const string ReportName = "ParaboxLuxoddControlValidation.txt";

        static readonly ArcadeButtonColor[] Colors =
        {
            ArcadeButtonColor.Black, ArcadeButtonColor.Red, ArcadeButtonColor.Green,
            ArcadeButtonColor.Yellow, ArcadeButtonColor.Blue, ArcadeButtonColor.Purple,
            ArcadeButtonColor.Orange, ArcadeButtonColor.White
        };

        static readonly KeyCode[] CabinetKeys =
        {
            KeyCode.JoystickButton0, KeyCode.JoystickButton1, KeyCode.JoystickButton2,
            KeyCode.JoystickButton3, KeyCode.JoystickButton4, KeyCode.JoystickButton5,
            KeyCode.JoystickButton8, KeyCode.JoystickButton9
        };

        static readonly string[] Labels =
        {
            "Confirm", "Undo", "Restart", "Level Select", "Mute", "Skip",
            "Luxodd Help", "Back"
        };

        [MenuItem("Tools/Parabox/Validate Luxodd Controls")]
        public static void ValidateFromMenu()
        {
            ValidateSilent();
            EditorUtility.DisplayDialog("Parabox",
                "Luxodd cabinet and standard-gamepad controls passed validation.", "OK");
        }

        public static void ValidateSilent()
        {
            var problems = new List<string>();
            ValidateBindingAsset(problems);
            ValidateCabinetContract(problems);
            ValidateStandardGamepadContract(problems);
            ValidateOneGestureOneMove(problems);
            ValidateSceneControllerOwnership(MenuPath, problems);
            ValidateSceneControllerOwnership(GamePath, problems);

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string reportPath = Path.Combine(projectRoot, "Library", ReportName);
            string report = problems.Count == 0
                ? "failures=0\nPASS: Luxodd cabinet, standard gamepad, D-pad and UI ownership are valid.\n"
                : "failures=" + problems.Count + "\nFAIL:\n- " + string.Join("\n- ", problems) + "\n";
            File.WriteAllText(reportPath, report);

            if (problems.Count > 0)
                throw new InvalidDataException(string.Join("\n", problems));

            Debug.Log("Parabox Luxodd-control validation passed. Report: " + reportPath);
        }

        static void ValidateBindingAsset(List<string> problems)
        {
            ArcadeBindingAsset asset = AssetDatabase.LoadAssetAtPath<ArcadeBindingAsset>(BindingPath);
            if (asset == null)
            {
                problems.Add("ArcadeBindingAsset is missing.");
                return;
            }

            if (asset.Entries == null || asset.Entries.Length != Colors.Length)
            {
                problems.Add("ArcadeBindingAsset must contain exactly eight color entries.");
                return;
            }

            var seen = new HashSet<ArcadeButtonColor>();
            for (int i = 0; i < asset.Entries.Length; i++)
                if (!seen.Add(asset.Entries[i].ButtonColor))
                    problems.Add("ArcadeBindingAsset contains duplicate " + asset.Entries[i].ButtonColor + ".");

            for (int i = 0; i < Colors.Length; i++)
                if (asset.GetLabel(Colors[i]) != Labels[i])
                    problems.Add(Colors[i] + " label must be '" + Labels[i] + "'.");
        }

        static void ValidateCabinetContract(List<string> problems)
        {
            for (int i = 0; i < Colors.Length; i++)
                if (ArcadeUnityMapping.GetKeyCode(Colors[i]) != CabinetKeys[i])
                    problems.Add(Colors[i] + " must map to " + CabinetKeys[i] + ".");
        }

        static void ValidateStandardGamepadContract(List<string> problems)
        {
            MethodInfo mapper = typeof(ArcadeControls).GetMethod("ColorToGamepadButton",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (mapper == null)
            {
                problems.Add("ArcadeControls standard-gamepad mapper is missing.");
                return;
            }

            Gamepad gamepad = InputSystem.AddDevice<Gamepad>("Parabox Validation Gamepad");
            try
            {
                ButtonControl[] expected =
                {
                    gamepad.buttonSouth, gamepad.buttonWest, gamepad.buttonNorth,
                    gamepad.startButton, gamepad.leftShoulder, gamepad.rightShoulder,
                    gamepad.selectButton, gamepad.buttonEast
                };
                for (int i = 0; i < Colors.Length; i++)
                {
                    var actual = mapper.Invoke(null, new object[] { Colors[i], gamepad }) as ButtonControl;
                    if (actual != expected[i])
                        problems.Add(Colors[i] + " standard-gamepad mapping is incorrect.");
                }
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        static void ValidateOneGestureOneMove(List<string> problems)
        {
            MethodInfo consume = typeof(LuxoddArcadeAdapter).GetMethod("ConsumeMoveGesture",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (consume == null)
            {
                problems.Add("Luxodd one-gesture/one-move latch is missing.");
                return;
            }

            bool latched = false;
            bool Pulse(Vector2Int direction)
            {
                object[] values = { direction, latched };
                bool fired = (bool)consume.Invoke(null, values);
                latched = (bool)values[1];
                return fired;
            }

            if (!Pulse(Vector2Int.down))
                problems.Add("The first joystick tilt must emit one puzzle move.");
            for (int frame = 0; frame < 120; frame++)
                if (Pulse(Vector2Int.down))
                {
                    problems.Add("Holding the joystick emits extra puzzle moves.");
                    break;
                }
            if (Pulse(Vector2Int.right))
                problems.Add("Rotating a held joystick emits an extra puzzle move before neutral.");
            if (Pulse(Vector2Int.zero))
                problems.Add("Returning the joystick to neutral must not emit a move.");
            if (!Pulse(Vector2Int.down))
                problems.Add("A new tilt after neutral must emit the next puzzle move.");
        }

        static void ValidateSceneControllerOwnership(string path, List<string> problems)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                int systems = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                    {
                        systems++;
                        if (eventSystem.sendNavigationEvents)
                            problems.Add(Path.GetFileName(path)
                                + " allows duplicate Unity/Luxodd controller navigation.");
                    }
                if (systems != 1)
                    problems.Add(Path.GetFileName(path) + " must contain exactly one EventSystem.");
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
