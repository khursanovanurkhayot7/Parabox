using System;
using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    [Serializable]
    public sealed class ArcadeDeviceMatcher
    {
        [field: SerializeField] public string ExpectedLayout { get; private set; }

        [field: SerializeField] public string ExpectedDeviceNameContains { get; private set; }

        [field: SerializeField] public string ExpectedVendorId { get; private set; }

        [field: SerializeField] public string ExpectedProductId { get; private set; }
    }
}
