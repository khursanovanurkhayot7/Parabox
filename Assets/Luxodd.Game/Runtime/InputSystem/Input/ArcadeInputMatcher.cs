#if LUXODD_INPUT_SYSTEM
using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Luxodd.Game.Scripts.Input
{
    public static class ArcadeInputMatcher
    {
        public static bool IsMatchingButton(
            InputDevice device,
            ButtonControl button,
            int buttonIndex,
            ArcadeInputMappingConfig config,
            ArcadeButtonColor targetColor)
        {
            if (device == null || button == null || config == null)
            {
                return false;
            }

            ArcadeButtonBinding binding = GetBinding(config, targetColor);
            if (binding == null)
            {
                return false;
            }

            if (!IsMatchingDevice(device, config.DeviceMatcher))
            {
                return false;
            }

            if (IsPathMatch(button, binding))
            {
                return true;
            }

            if (IsControlNameAndIndexMatch(button, buttonIndex, binding))
            {
                return true;
            }

            if (IsControlNameMatch(button, binding))
            {
                return true;
            }

            if (IsDisplayNameMatch(button, binding))
            {
                return true;
            }

            if (IsIndexMatch(buttonIndex, binding))
            {
                return true;
            }

            return false;
        }

        public static bool IsMatchingDevice(InputDevice device, ArcadeDeviceMatcher matcher)
        {
            if (device == null)
            {
                return false;
            }

            if (matcher == null)
            {
                return true;
            }

            if (!IsLayoutMatch(device, matcher))
            {
                return false;
            }

            if (!IsDeviceNameMatch(device, matcher))
            {
                return false;
            }

            if (!IsVendorIdMatch(device, matcher))
            {
                return false;
            }

            if (!IsProductIdMatch(device, matcher))
            {
                return false;
            }

            return true;
        }

        public static ArcadeButtonBinding GetBinding(
            ArcadeInputMappingConfig config,
            ArcadeButtonColor buttonColor)
        {
            if (config == null || config.ButtonBindings == null)
            {
                return null;
            }

            for (int i = 0; i < config.ButtonBindings.Count; i++)
            {
                ArcadeButtonBinding binding = config.ButtonBindings[i];
                if (binding == null)
                {
                    continue;
                }

                if (binding.ButtonColor == buttonColor)
                {
                    return binding;
                }
            }

            return null;
        }

        private static bool IsPathMatch(ButtonControl button, ArcadeButtonBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.ExpectedControlPath))
            {
                return false;
            }

            return string.Equals(
                button.path,
                binding.ExpectedControlPath,
                StringComparison.Ordinal);
        }

        private static bool IsControlNameAndIndexMatch(
            ButtonControl button,
            int buttonIndex,
            ArcadeButtonBinding binding)
        {
            bool hasControlName = !string.IsNullOrWhiteSpace(binding.ExpectedControlName);
            bool hasButtonIndex = binding.ExpectedButtonIndex >= 0;

            if (!hasControlName || !hasButtonIndex)
            {
                return false;
            }

            return string.Equals(
                       button.name,
                       binding.ExpectedControlName,
                       StringComparison.OrdinalIgnoreCase) &&
                   buttonIndex == binding.ExpectedButtonIndex;
        }

        private static bool IsControlNameMatch(ButtonControl button, ArcadeButtonBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.ExpectedControlName))
            {
                return false;
            }

            return string.Equals(
                button.name,
                binding.ExpectedControlName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDisplayNameMatch(ButtonControl button, ArcadeButtonBinding binding)
        {
            if (string.IsNullOrWhiteSpace(binding.ExpectedDisplayName))
            {
                return false;
            }

            return string.Equals(
                button.displayName,
                binding.ExpectedDisplayName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIndexMatch(int buttonIndex, ArcadeButtonBinding binding)
        {
            if (binding.ExpectedButtonIndex < 0)
            {
                return false;
            }

            return buttonIndex == binding.ExpectedButtonIndex;
        }

        private static bool IsLayoutMatch(InputDevice device, ArcadeDeviceMatcher matcher)
        {
            if (string.IsNullOrWhiteSpace(matcher.ExpectedLayout))
            {
                return true;
            }

            return string.Equals(
                device.layout,
                matcher.ExpectedLayout,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDeviceNameMatch(InputDevice device, ArcadeDeviceMatcher matcher)
        {
            if (string.IsNullOrWhiteSpace(matcher.ExpectedDeviceNameContains))
            {
                return true;
            }

            string deviceName = device.displayName ?? string.Empty;

            return deviceName.IndexOf(
                matcher.ExpectedDeviceNameContains,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsVendorIdMatch(InputDevice device, ArcadeDeviceMatcher matcher)
        {
            if (string.IsNullOrWhiteSpace(matcher.ExpectedVendorId))
            {
                return true;
            }

            string deviceDescription = device.description.ToString();

            return deviceDescription.IndexOf(
                matcher.ExpectedVendorId,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsProductIdMatch(InputDevice device, ArcadeDeviceMatcher matcher)
        {
            if (string.IsNullOrWhiteSpace(matcher.ExpectedProductId))
            {
                return true;
            }

            string deviceDescription = device.description.ToString();

            return deviceDescription.IndexOf(
                matcher.ExpectedProductId,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
