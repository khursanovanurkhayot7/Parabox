using System;
using UnityEngine;

namespace Luxodd.Game.Scripts.Input
{
    [Serializable]
    public sealed class ArcadeButtonBinding
    {
        [field: SerializeField] public ArcadeButtonColor ButtonColor { get; private set; }

        [field: SerializeField] public string ExpectedControlName { get; private set; }

        [field: SerializeField] public string ExpectedDisplayName { get; private set; }

        [field: SerializeField] public int ExpectedButtonIndex { get; private set; } = -1;

        [field: SerializeField] public string ExpectedControlPath { get; private set; }
    }
}
