using System;
using System.Collections.Generic;
using System.Text;
using Luxodd.Game.Scripts.Input;
using TMPro;
using UnityEngine;
#if LUXODD_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif
using UnityEngine.UI;

namespace Luxodd.Game.Example.Scripts
{
#if LUXODD_INPUT_SYSTEM
    public class ArcadeButtonMappingDiagnosticBehaviour : MonoBehaviour
    {
        [Serializable]
        private struct ButtonMappingResult
        {
            public ArcadeButtonColor ButtonColor;
            public string DeviceName;
            public string DeviceLayout;
            public string ControlName;
            public string DisplayName;
            public string Path;
            public int ButtonIndex;
        }
        
        [field: SerializeField] public bool ShouldUseMappingDiagnostic {get; private set;} = true;

        [Header("UI")]
        [SerializeField] private ControlExamplePanelHandler _panelHandler;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _resultText;

        [Header("Filtering")]
        [SerializeField] private bool _ignoreDpadButtons = true;
        [SerializeField] private bool _ignoreStickPressButtons = true;
        
        [Header("Input Stability")]
        [SerializeField] private float _pressDebounceDuration = 0.2f;

        private readonly ArcadeButtonColor[] _testOrder =
        {
            ArcadeButtonColor.Black,
            ArcadeButtonColor.Red,
            ArcadeButtonColor.Green,
            ArcadeButtonColor.Yellow,
            ArcadeButtonColor.Blue,
            ArcadeButtonColor.Purple,
            ArcadeButtonColor.Orange,
            ArcadeButtonColor.White
        };

        private readonly Dictionary<ArcadeButtonColor, ButtonMappingResult> _results = new();
        private readonly Dictionary<string, bool> _previousButtonStates = new();
        private readonly StringBuilder _builder = new(4096);

        private int _currentStepIndex;
        private ButtonMappingResult? _lastDetectedResult;
        
        private bool _isActive;
        
        private bool _isWaitingForRelease;
        private float _nextAllowedPressTime;
        private string _lastAcceptedButtonPath;
        private int _lastAcceptedButtonIndex = -1;

        public void Activate()
        {
            if (ShouldUseMappingDiagnostic == false)
            {
                return;
            }
            
            _isActive = true;
            ResetTest();
        }

        public void Deactivate()
        {
            _isActive = false;
        }
        
        private void Start()
        {
            Debug.Log($"[{DateTime.Now}][{GetType().Name}][{nameof(Start)}] OK, ShouldUseMappingDiagnostic: {ShouldUseMappingDiagnostic}");
            if (ShouldUseMappingDiagnostic)
            {
                ShowDiagnostic();
            }
            else
            {
                HideDiagnostic();
            }
            
            ResetTest();
        }

        private void Update()
        {
            if (!_isActive) return;
            
            UpdateRecordedToggles();

            if (_currentStepIndex >= _testOrder.Length)
            {
                Render();
                return;
            }

            if (_isWaitingForRelease)
            {
                if (!IsAnyRelevantButtonPressed())
                {
                    _isWaitingForRelease = false;
                }

                Render();
                return;
            }

            if (Time.unscaledTime < _nextAllowedPressTime)
            {
                Render();
                return;
            }

            DetectRawButtonPress();
            Render();
        }

        public void ResetTest()
        {
            _results.Clear();
            _previousButtonStates.Clear();
            _currentStepIndex = 0;
            _lastDetectedResult = null;

            _isWaitingForRelease = false;
            _nextAllowedPressTime = 0f;
            _lastAcceptedButtonPath = null;
            _lastAcceptedButtonIndex = -1;

            UpdateRecordedToggles();
            Render();
        }

        private void DetectRawButtonPress()
        {
            if (_currentStepIndex >= _testOrder.Length)
            {
                return;
            }

            var device = GetPreferredDevice();
            if (device == null)
            {
                return;
            }

            foreach (var control in device.allControls)
            {
                if (control is not ButtonControl button)
                {
                    continue;
                }

                if (ShouldIgnoreButton(button))
                {
                    continue;
                }

                var key = button.path;
                var isPressed = button.isPressed;
                var wasPressed = _previousButtonStates.TryGetValue(key, out var previousPressed) && previousPressed;

                _previousButtonStates[key] = isPressed;

                if (!isPressed || wasPressed)
                {
                    continue;
                }

                var buttonIndex = GetButtonIndex(device, button);

                if (IsDuplicatePress(button, buttonIndex))
                {
                    continue;
                }

                var currentColor = _testOrder[_currentStepIndex];
                var result = CreateResult(currentColor, device, button);

                _results[currentColor] = result;
                _lastDetectedResult = result;
                _currentStepIndex++;

                _lastAcceptedButtonPath = button.path;
                _lastAcceptedButtonIndex = buttonIndex;
                _nextAllowedPressTime = Time.unscaledTime + _pressDebounceDuration;
                _isWaitingForRelease = true;

                UpdateRecordedToggles();
                return;
            }
        }
        
        private bool IsAnyRelevantButtonPressed()
        {
            var device = GetPreferredDevice();
            if (device == null)
            {
                return false;
            }

            foreach (var control in device.allControls)
            {
                if (control is not ButtonControl button)
                {
                    continue;
                }

                if (ShouldIgnoreButton(button))
                {
                    continue;
                }

                if (button.isPressed)
                {
                    return true;
                }
            }

            return false;
        }

        private InputDevice GetPreferredDevice()
        {
            if (Joystick.current != null)
            {
                return Joystick.current;
            }

            if (Gamepad.current != null)
            {
                return Gamepad.current;
            }

            return null;
        }

        private ButtonMappingResult CreateResult(ArcadeButtonColor buttonColor, InputDevice device, ButtonControl button)
        {
            return new ButtonMappingResult
            {
                ButtonColor = buttonColor,
                DeviceName = device.displayName,
                DeviceLayout = device.layout,
                ControlName = button.name,
                DisplayName = button.displayName,
                Path = button.path,
                ButtonIndex = GetButtonIndex(device, button)
            };
        }

        private int GetButtonIndex(InputDevice device, ButtonControl targetButton)
        {
            var index = 0;

            foreach (var control in device.allControls)
            {
                if (control is not ButtonControl button)
                {
                    continue;
                }

                if (ShouldIgnoreButton(button))
                {
                    continue;
                }

                if (button == targetButton)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }
        
        private bool IsDuplicatePress(ButtonControl button, int buttonIndex)
        {
            if (!string.IsNullOrEmpty(_lastAcceptedButtonPath) &&
                string.Equals(_lastAcceptedButtonPath, button.path, StringComparison.Ordinal))
            {
                return true;
            }

            if (_lastAcceptedButtonIndex >= 0 && _lastAcceptedButtonIndex == buttonIndex)
            {
                return true;
            }

            return false;
        }

        private bool ShouldIgnoreButton(ButtonControl button)
        {
            if (_ignoreDpadButtons &&
                !string.IsNullOrEmpty(button.path) &&
                button.path.IndexOf("/dpad/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (_ignoreStickPressButtons && !string.IsNullOrEmpty(button.name))
            {
                if (string.Equals(button.name, "leftStickPress", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(button.name, "rightStickPress", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateRecordedToggles()
        {
            if (_panelHandler == null)
            {
                return;
            }

            foreach (var color in _testOrder)
            {
                _panelHandler.SetArcadeButtonColorToggleState(color, _results.ContainsKey(color));
            }
        }

        private void Render()
        {
            RenderStatusText();
            RenderResultText();
        }

        private void RenderStatusText()
        {
            if (_statusText == null)
            {
                return;
            }

            if (_currentStepIndex >= _testOrder.Length)
            {
                _statusText.text =
                    "Arcade Button Mapping Test\n\n" +
                    "Status: Completed\n" +
                    "All 8 buttons were recorded.";
                return;
            }

            var expectedColor = _testOrder[_currentStepIndex];

            if (_isWaitingForRelease)
            {
                _statusText.text =
                    "Arcade Button Mapping Test\n\n" +
                    $"Step: {_currentStepIndex}/{_testOrder.Length}\n" +
                    "Release button to continue...";
                return;
            }

            if (Time.unscaledTime < _nextAllowedPressTime)
            {
                _statusText.text =
                    "Arcade Button Mapping Test\n\n" +
                    $"Step: {_currentStepIndex + 1}/{_testOrder.Length}\n" +
                    "Waiting...";
                return;
            }

            _statusText.text =
                "Arcade Button Mapping Test\n\n" +
                $"Step: {_currentStepIndex + 1}/{_testOrder.Length}\n" +
                $"Press physical button: {expectedColor}";
        }

        private void RenderResultText()
        {
            if (_resultText == null)
            {
                return;
            }

            _builder.Clear();

            _builder.AppendLine("Last detected raw input:");
            _builder.AppendLine();

            if (_lastDetectedResult.HasValue)
            {
                AppendResult(_builder, _lastDetectedResult.Value);
            }
            else
            {
                _builder.AppendLine("No button press detected yet.");
            }

            _builder.AppendLine();
            _builder.AppendLine("Recorded mapping:");
            _builder.AppendLine();

            foreach (var color in _testOrder)
            {
                if (_results.TryGetValue(color, out var result))
                {
                    AppendResult(_builder, result);
                }
                else
                {
                    _builder.AppendLine($"{color}: not assigned");
                    _builder.AppendLine();
                }
            }

            _builder.AppendLine("Legacy reference:");
            _builder.AppendLine("Black  -> JoystickButton0");
            _builder.AppendLine("Red    -> JoystickButton1");
            _builder.AppendLine("Green  -> JoystickButton2");
            _builder.AppendLine("Yellow -> JoystickButton3");
            _builder.AppendLine("Blue   -> JoystickButton4");
            _builder.AppendLine("Purple -> JoystickButton5");
            _builder.AppendLine("Orange -> JoystickButton8");
            _builder.AppendLine("White  -> JoystickButton9");

            _resultText.text = _builder.ToString();
        }

        private static void AppendResult(StringBuilder builder, ButtonMappingResult result)
        {
            builder.AppendLine($"{result.ButtonColor}:");
            builder.AppendLine($"  Device Name: {Safe(result.DeviceName)}");
            builder.AppendLine($"  Device Layout: {Safe(result.DeviceLayout)}");
            builder.AppendLine($"  Control Name: {Safe(result.ControlName)}");
            builder.AppendLine($"  Display Name: {Safe(result.DisplayName)}");
            builder.AppendLine($"  Path: {Safe(result.Path)}");
            builder.AppendLine($"  Button Index: {result.ButtonIndex}");
            builder.AppendLine();
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        }

        private void ShowDiagnostic()
        {
            _statusText.gameObject.SetActive(true);
            _resultText.gameObject.SetActive(true);
            
            _statusText.transform.parent.gameObject.SetActive(true);
            _resultText.transform.parent.gameObject.SetActive(true);
        }

        private void HideDiagnostic()
        {
            _statusText.gameObject.SetActive(false);
            _resultText.gameObject.SetActive(false);
            
            _statusText.transform.parent.gameObject.SetActive(false);
            _resultText.transform.parent.gameObject.SetActive(false);
        }
    }
#else
#pragma warning disable 0414 // Serialized compatibility fields are intentionally inert without Input System.
    public class ArcadeButtonMappingDiagnosticBehaviour : MonoBehaviour
    {
        [field: SerializeField] public bool ShouldUseMappingDiagnostic { get; private set; }

        [Header("UI")]
        [SerializeField] private ControlExamplePanelHandler _panelHandler;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private TMP_Text _resultText;

        [Header("Filtering")]
        [SerializeField] private bool _ignoreDpadButtons = true;
        [SerializeField] private bool _ignoreStickPressButtons = true;

        [Header("Input Stability")]
        [SerializeField] private float _pressDebounceDuration = 0.2f;

        public void Activate()
        {
        }

        public void Deactivate()
        {
        }

        public void ResetTest()
        {
        }

        private void Start()
        {
            HideDiagnostic();
        }

        private void HideDiagnostic()
        {
            if (_statusText != null)
            {
                _statusText.gameObject.SetActive(false);
                if (_statusText.transform.parent != null)
                {
                    _statusText.transform.parent.gameObject.SetActive(false);
                }
            }

            if (_resultText != null)
            {
                _resultText.gameObject.SetActive(false);
                if (_resultText.transform.parent != null)
                {
                    _resultText.transform.parent.gameObject.SetActive(false);
                }
            }
        }
    }
#pragma warning restore 0414
#endif
}
