using Luxodd.Game;
using Luxodd.Game.Scripts.Input;
using UnityEngine;

namespace Parabox
{
    // Hardware boundary for Parabox. Gameplay and menu code consume named game actions from this
    // adapter and never depend on joystick button indices or cabinet wiring.
    [DefaultExecutionOrder(-5000)]
    public class LuxoddArcadeAdapter : MonoBehaviour
    {
        public static LuxoddArcadeAdapter Instance { get; private set; }

        public Vector2 Stick { get; private set; }
        public Vector2Int Direction { get; private set; }
        public bool MovePulse { get; private set; }

        public bool ConfirmDown { get; private set; }  // Black
        public bool UndoDown { get; private set; }     // Red
        public bool RestartDown { get; private set; }  // Green
        public bool LevelsDown { get; private set; }   // Yellow
        public bool MuteDown { get; private set; }     // Blue
        public bool BackDown { get; private set; }     // White

        Vector2Int lastDirection;
        float nextRepeat;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            Stick = ArcadeControls.GetStick().Vector;
            Direction = Quantize(Stick);
            MovePulse = false;

            if (Direction != lastDirection)
            {
                lastDirection = Direction;
                if (Direction != Vector2Int.zero)
                {
                    MovePulse = true;
                    nextRepeat = Time.unscaledTime + 0.28f;
                }
            }
            else if (Direction != Vector2Int.zero && Time.unscaledTime >= nextRepeat)
            {
                MovePulse = true;
                nextRepeat = Time.unscaledTime + 0.14f;
            }

            ConfirmDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Black);
            UndoDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Red);
            RestartDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Green);
            LevelsDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Yellow);
            MuteDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Blue);
            BackDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.White);
            // Orange is reserved for the Luxodd system overlay; Purple remains available later.
        }

        static Vector2Int Quantize(Vector2 value)
        {
            const float threshold = 0.52f;
            float ax = Mathf.Abs(value.x);
            float ay = Mathf.Abs(value.y);
            if (Mathf.Max(ax, ay) < threshold) return Vector2Int.zero;
            return ax >= ay
                ? new Vector2Int(value.x >= 0f ? 1 : -1, 0)
                : new Vector2Int(0, value.y >= 0f ? 1 : -1);
        }
    }
}
