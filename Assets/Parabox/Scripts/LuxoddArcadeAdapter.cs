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
        // Gameplay pulse: exactly once per neutral -> tilted joystick gesture.
        public bool MovePulse { get; private set; }
        // UI pulse: menus may repeat while held so long lists remain convenient to navigate.
        public bool NavigationPulse { get; private set; }

        public bool ConfirmDown { get; private set; }  // Black
        public bool UndoDown { get; private set; }     // Red
        public bool RestartDown { get; private set; }  // Green
        public bool LevelsDown { get; private set; }   // Yellow
        public bool MuteDown { get; private set; }     // Blue
        public bool SkipDown { get; private set; }     // Purple (skip the active walkthrough)
        public bool SystemDown { get; private set; }   // Orange (Luxodd overlay owns the response)
        public bool BackDown { get; private set; }     // White

        bool moveLatched;
        Vector2Int lastNavigationDirection;
        float nextNavigationRepeat;

        // Cabinet sticks rarely return a perfectly stable zero. Using the same threshold to engage
        // and release lets a noisy axis bounce across that boundary while it is still held, which
        // turns one physical tilt into two or three logical moves. A wide hysteresis band fixes the
        // hardware behaviour without adding a timer or changing any puzzle mechanic: engage only
        // on a deliberate tilt, then stay latched until the stick is genuinely back at centre.
        const float MoveEngageThreshold = 0.58f;
        const float MoveReleaseThreshold = 0.22f;

        // The Luxodd prefab normally creates this adapter. Keep a standalone fallback so cabinet
        // input still works when networking or the runtime prefab is unavailable.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void EnsureInputAdapter()
        {
            if (Instance != null) return;
            var root = new GameObject("ParaboxControllerInput");
            DontDestroyOnLoad(root);
            root.AddComponent<LuxoddArcadeAdapter>();
        }

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
            // ArcadeControls already handles both the cabinet HID joystick and its SDK-defined
            // gamepad fallback. Reading Gamepad again here caused a single physical press to map to
            // two different actions, so this adapter keeps Luxodd as the only mapping authority.
            Stick = ArcadeControls.GetJoystick();
            Direction = Quantize(Stick, MoveEngageThreshold);
            MovePulse = false;
            NavigationPulse = false;

            // A puzzle move is a discrete arcade gesture, never a frame/time repeat. Once the
            // joystick leaves neutral, gameplay stays latched until it returns to neutral. This
            // prevents one held direction from silently spending several moves.
            MovePulse = ConsumeMoveGesture(Direction, Stick, ref moveLatched);

            // Menu focus is non-destructive, so it retains a deliberate hold repeat independent
            // from gameplay's one-tilt/one-move latch.
            if (Direction != lastNavigationDirection)
            {
                lastNavigationDirection = Direction;
                if (Direction != Vector2Int.zero)
                {
                    NavigationPulse = true;
                    nextNavigationRepeat = Time.unscaledTime + 0.32f;
                }
            }
            else if (Direction != Vector2Int.zero && Time.unscaledTime >= nextNavigationRepeat)
            {
                NavigationPulse = true;
                nextNavigationRepeat = Time.unscaledTime + 0.16f;
            }

            ConfirmDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Black);
            UndoDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Red);
            RestartDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Green);
            LevelsDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Yellow);
            MuteDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Blue);
            SkipDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Purple);
            SystemDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Orange);
            BackDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.White);
            // Purple belongs to walkthrough Skip. Orange is observed for diagnostics only; the
            // Luxodd system overlay remains the sole owner of its response.
        }

        static Vector2Int Quantize(Vector2 value, float threshold)
        {
            float ax = Mathf.Abs(value.x);
            float ay = Mathf.Abs(value.y);
            if (Mathf.Max(ax, ay) < threshold) return Vector2Int.zero;
            return ax >= ay
                ? new Vector2Int(value.x >= 0f ? 1 : -1, 0)
                : new Vector2Int(0, value.y >= 0f ? 1 : -1);
        }

        // Kept as one small pure state transition so the editor audit can prove that held, rotated
        // and noisy input cannot spend extra moves without a real return to neutral.
        static bool ConsumeMoveGesture(Vector2Int direction, Vector2 rawStick, ref bool latched)
        {
            float strongestAxis = Mathf.Max(Mathf.Abs(rawStick.x), Mathf.Abs(rawStick.y));
            if (latched)
            {
                if (strongestAxis <= MoveReleaseThreshold)
                    latched = false;
                return false;
            }

            if (direction == Vector2Int.zero) return false;
            latched = true;
            return true;
        }

        // In the Unity Editor the Luxodd legacy axes can mirror the same arrow/WASD press that
        // GameManager already received from the Input System. The legacy axis often rises a frame
        // later, so a frame-only debounce cannot stop that second move. Claiming the gesture here
        // keeps the arcade path latched until the shared physical control returns to neutral.
        public void ClaimCurrentMoveGesture()
        {
            moveLatched = true;
            MovePulse = false;
        }
    }
}
