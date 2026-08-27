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
            "Confirm", "Undo", "", "Restart", "", "Skip Tutorial",
            "Luxodd Help", ""
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
            ValidateJoystickGestures(problems);
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
            // Luxodd 2.x moved the controller mapping out of ArcadeControls and into its optional
            // Input System assembly. Resolve it by assembly-qualified name so this validator stays
            // compatible when that optional backend is disabled.
            System.Type mapperType = System.Type.GetType(
                "Luxodd.Game.Scripts.Input.StandardGamepadMapping, Luxodd.Game.InputSystem");
            MethodInfo mapper = mapperType?.GetMethod("GetButtonControl",
                BindingFlags.Public | BindingFlags.Static);
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
                    gamepad.buttonSouth, gamepad.buttonEast, gamepad.buttonWest,
                    gamepad.buttonNorth, gamepad.leftShoulder, gamepad.rightShoulder,
                    gamepad.selectButton, gamepad.startButton
                };
                for (int i = 0; i < Colors.Length; i++)
                {
                    var actual = mapper.Invoke(null, new object[] { gamepad, Colors[i] }) as ButtonControl;
                    if (actual != expected[i])
                        problems.Add(Colors[i] + " standard-gamepad mapping is incorrect.");
                }
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        static void ValidateJoystickGestures(List<string> problems)
        {
            MethodInfo quantize = typeof(LuxoddArcadeAdapter).GetMethod("QuantizeStable",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo consume = typeof(LuxoddArcadeAdapter).GetMethod("ConsumeMoveGesture",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (quantize == null || consume == null)
            {
                problems.Add("Luxodd stable joystick quantizer or movement latch is missing.");
                return;
            }

            bool latched = false;
            Vector2Int stableDirection = Vector2Int.zero;
            Vector2Int lastMoveDirection = Vector2Int.zero;
            bool Pulse(Vector2 rawStick)
            {
                stableDirection = (Vector2Int)quantize.Invoke(null,
                    new object[] { rawStick, stableDirection });
                object[] values = { stableDirection, rawStick, latched, lastMoveDirection };
                bool fired = (bool)consume.Invoke(null, values);
                latched = (bool)values[2];
                lastMoveDirection = (Vector2Int)values[3];
                return fired;
            }

            if (Pulse(new Vector2(0.18f, -0.18f)))
                problems.Add("Joystick movement fires inside the dead zone.");
            if (!Pulse(Vector2.down))
                problems.Add("The first joystick tilt must emit one puzzle move.");
            for (int frame = 0; frame < 120; frame++)
                if (Pulse(Vector2.down))
                {
                    problems.Add("Holding the joystick emits extra puzzle moves.");
                    break;
                }

            // Near-diagonal axis noise must retain the direction that already owns the gesture.
            if (Pulse(new Vector2(0.78f, -0.80f))
                || Pulse(new Vector2(0.80f, -0.78f))
                || stableDirection != Vector2Int.down)
                problems.Add("Diagonal joystick noise changes direction or emits a move.");

            // A decisive turn is a new grid gesture even when the player moves quickly around
            // the gate without pausing at neutral.
            if (!Pulse(Vector2.right) || stableDirection != Vector2Int.right)
                problems.Add("A rapid cardinal direction change must emit immediately.");
            if (Pulse(Vector2.right))
                problems.Add("Holding the changed direction emits an extra puzzle move.");
            if (!Pulse(Vector2.up) || stableDirection != Vector2Int.up)
                problems.Add("A second rapid cardinal direction change must remain responsive.");

            // Cabinet axes can briefly dip below the engage threshold without the player actually
            // releasing the stick. That noise must not re-arm movement.
            if (Pulse(new Vector2(0f, 0.45f)))
                problems.Add("Joystick threshold noise emits an extra puzzle move.");
            if (Pulse(Vector2.up))
                problems.Add("Joystick threshold noise re-arms movement before true neutral.");

            if (Pulse(Vector2.zero))
                problems.Add("Returning the joystick to neutral must not emit a move.");
            if (!Pulse(Vector2.left))
                problems.Add("A new tilt after neutral must emit the next puzzle move.");

            MethodInfo pointerQuantize = typeof(ArcadeJoystickControl).GetMethod(
                "StableDirectionFromDelta", BindingFlags.Public | BindingFlags.Static);
            if (pointerQuantize == null)
            {
                problems.Add("The on-screen joystick stable quantizer is missing.");
                return;
            }
            Vector2Int pointerDirection = (Vector2Int)pointerQuantize.Invoke(null,
                new object[] { new Vector2(40f, 39f), Vector2Int.zero, 16f, 8f });
            if (pointerDirection != Vector2Int.right)
                problems.Add("The on-screen joystick does not resolve its first diagonal predictably.");
            pointerDirection = (Vector2Int)pointerQuantize.Invoke(null,
                new object[] { new Vector2(39f, 40f), pointerDirection, 16f, 8f });
            if (pointerDirection != Vector2Int.right)
                problems.Add("The on-screen joystick jitters when a drag crosses the diagonal.");
            pointerDirection = (Vector2Int)pointerQuantize.Invoke(null,
                new object[] { new Vector2(10f, 50f), pointerDirection, 16f, 8f });
            if (pointerDirection != Vector2Int.up)
                problems.Add("The on-screen joystick ignores a decisive rapid direction change.");
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
                {
                    foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                    {
                        systems++;
                        if (eventSystem.sendNavigationEvents)
                            problems.Add(Path.GetFileName(path)
                                + " allows duplicate Unity/Luxodd controller navigation.");
                    }
                    foreach (HoldRepeatButton directionButton in
                             root.GetComponentsInChildren<HoldRepeatButton>(true))
                        if (directionButton.repeatWhileHeld)
                            problems.Add(Path.GetFileName(path) + "/" + directionButton.name
                                + " repeats puzzle movement while one pointer is held.");
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
