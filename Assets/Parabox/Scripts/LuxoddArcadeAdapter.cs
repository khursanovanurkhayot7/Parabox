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
        // Gameplay pulse: once for each deliberate cardinal tilt or direction change.
        public bool MovePulse { get; private set; }
        // UI pulse: menus may repeat while held so long lists remain convenient to navigate.
        public bool NavigationPulse { get; private set; }

        public bool ConfirmDown { get; private set; }  // Black
        public bool UndoDown { get; private set; }     // Red
        public bool BackDown { get; private set; }     // Red while a menu/selection screen owns input
        public bool RestartDown { get; private set; }  // Yellow
        public bool SkipDown { get; private set; }     // Purple (skip the active walkthrough)
        public bool SystemDown { get; private set; }   // Orange (Luxodd overlay owns the response)

        bool moveLatched;
        Vector2Int lastMoveDirection;
        Vector2Int lastNavigationDirection;
        float nextNavigationRepeat;

        // Cabinet sticks rarely return a perfectly stable zero. Using the same threshold to engage
        // and release lets a noisy axis bounce across that boundary while it is still held, which
        // turns one physical tilt into two or three logical moves. A wide hysteresis band fixes the
        // hardware behaviour without adding a timer or changing any puzzle mechanic: engage only
        // on a deliberate tilt, then stay latched until the stick is genuinely back at centre.
        const float MoveEngageThreshold = 0.58f;
        const float MoveReleaseThreshold = 0.22f;
        const float DirectionSwitchBias = 0.16f;

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
            Direction = QuantizeStable(Stick, Direction);
            MovePulse = false;
            NavigationPulse = false;

            // A held direction never frame-repeats. A deliberate turn to another cardinal direction
            // does emit immediately, so fast grid movement does not require a stop at neutral.
            MovePulse = ConsumeMoveGesture(Direction, Stick,
                ref moveLatched, ref lastMoveDirection);

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
            BackDown = UndoDown;
            RestartDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Yellow);
            SkipDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Purple);
            SystemDown = ArcadeControls.GetButtonDown(ArcadeButtonColor.Orange);
            // Green, Blue and White are deliberately not polled: they have no authored Parabox
            // action and must not trigger a hidden shortcut. Orange remains owned by Luxodd.
        }

        // Resolve diagonals with hysteresis. Once an axis owns the gesture, the other axis must be
        // clearly stronger before it can take over; small cabinet noise near 45 degrees therefore
        // cannot alternate left/right movement with up/down movement from frame to frame.
        static Vector2Int QuantizeStable(Vector2 value, Vector2Int currentDirection)
        {
            float ax = Mathf.Abs(value.x);
            float ay = Mathf.Abs(value.y);
            float strongest = Mathf.Max(ax, ay);
            if (strongest <= MoveReleaseThreshold) return Vector2Int.zero;
            if (currentDirection != Vector2Int.zero && strongest < MoveEngageThreshold)
                return currentDirection;
            if (currentDirection == Vector2Int.zero && strongest < MoveEngageThreshold)
                return Vector2Int.zero;

            Vector2Int candidate = ax >= ay
                ? new Vector2Int(value.x >= 0f ? 1 : -1, 0)
                : new Vector2Int(0, value.y >= 0f ? 1 : -1);
            if (currentDirection == Vector2Int.zero || candidate == currentDirection)
                return candidate;

            // Reversing on the same axis is always deliberate once the new side reaches engage.
            bool opposite = candidate == -currentDirection;
            if (opposite) return candidate;

            float candidateStrength = candidate.x != 0 ? ax : ay;
            float currentStrength = currentDirection.x != 0 ? ax : ay;
            return candidateStrength >= MoveEngageThreshold
                   && candidateStrength >= currentStrength + DirectionSwitchBias
                ? candidate : currentDirection;
        }

        // Kept as one small pure state transition so the editor audit can prove that held and noisy
        // input cannot repeat, while a deliberate cardinal turn remains immediately responsive.
        static bool ConsumeMoveGesture(Vector2Int direction, Vector2 rawStick, ref bool latched,
                                       ref Vector2Int lastDirection)
        {
            float strongestAxis = Mathf.Max(Mathf.Abs(rawStick.x), Mathf.Abs(rawStick.y));
            if (strongestAxis <= MoveReleaseThreshold)
            {
                latched = false;
                lastDirection = Vector2Int.zero;
                return false;
            }

            if (direction == Vector2Int.zero) return false;
            if (!latched)
            {
                latched = true;
                lastDirection = direction;
                return true;
            }

            // ClaimCurrentMoveGesture can latch before the mirrored legacy axis has risen.
            if (lastDirection == Vector2Int.zero)
            {
                lastDirection = direction;
                return false;
            }
            if (direction == lastDirection) return false;

            lastDirection = direction;
            return true;
        }

        // In the Unity Editor the Luxodd legacy axes can mirror the same arrow/WASD press that
        // GameManager already received from the Input System. The legacy axis often rises a frame
        // later, so a frame-only debounce cannot stop that second move. Claiming the gesture here
        // keeps the arcade path latched until the shared physical control returns to neutral.
        public void ClaimCurrentMoveGesture()
        {
            moveLatched = true;
            lastMoveDirection = Direction;
            MovePulse = false;
        }
    }
}
