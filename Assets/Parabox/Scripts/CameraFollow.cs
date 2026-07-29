using UnityEngine;

namespace Parabox
{
    // Frames the room the player is currently inside.
    // When the player enters/exits a box the camera smoothly zooms in/out.
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        public float padding = 1.05f;
        public float speed = 5f;
        [Range(0f, 0.4f)] public float topFrac = 0.16f;     // screen fraction reserved at top for the HUD title
        [Range(0f, 0.4f)] public float bottomFrac = 0.20f;  // screen fraction reserved at bottom for the controls

        [Header("Start fly-in (menu → game)")]
        public float introDur = 1.2f;
        public float introZoomOut = 5f;    // camera starts this many× zoomed out, then eases into the board

        Camera cam;
        Transform roomRoot;
        int roomW;
        int roomH;
        float introT = -1f;   // < 0 = not playing the intro

        // Shake is layered on top of the follow pose rather than driven into it, so a shake can
        // never drag the framing off the board — `settled` is always the real, unshaken target.
        Vector3 settled;
        float shakeAmp;
        float shakeDecay = 5f;

        public void Shake(float amplitude, float decay = 5f)
        {
            shakeAmp = amplitude;
            shakeDecay = decay;
        }

        void Awake()
        {
            cam = GetComponent<Camera>();
        }

        public void SetTargetRoom(Transform root, int width, int height, bool instant)
        {
            roomRoot = root;
            roomW = width;
            roomH = height;
            if (instant) Apply(1f);
        }

        // Cinematic zoom from far out into the board — called when entering the game from the main menu.
        public void PlayIntro() { introT = 0f; }
        public bool IntroPlaying => introT >= 0f;

        void LateUpdate()
        {
            if (roomRoot == null) return;
            ComputeTarget(out Vector3 wanted, out float size);

            if (introT >= 0f)
            {
                introT += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(introT / Mathf.Max(0.01f, introDur));
                float e = 1f - Mathf.Pow(1f - k, 3f);   // ease-out cubic: fast then gently settle
                // start centered on the board, far out; ease to the EXACT gameplay framing
                Vector3 from = new Vector3(roomRoot.position.x, roomRoot.position.y, wanted.z);
                transform.position = Vector3.Lerp(from, wanted, e);
                cam.orthographicSize = Mathf.Lerp(size * introZoomOut, size, e);
                if (k >= 1f) introT = -1f;   // hand off to normal follow at the exact target — seamless
                return;
            }

            Apply(1f - Mathf.Exp(-speed * Time.deltaTime));

            if (shakeAmp > 0.0001f)
            {
                shakeAmp *= Mathf.Exp(-shakeDecay * Time.unscaledDeltaTime);
                Vector2 o = Random.insideUnitCircle * shakeAmp;
                transform.position = settled + new Vector3(o.x, o.y, 0f);
            }
        }

        void Apply(float t)
        {
            ComputeTarget(out Vector3 wanted, out float size);
            if (shakeAmp <= 0.0001f) settled = transform.position;   // not shaking → the live pose IS settled
            settled = Vector3.Lerp(settled, wanted, t);
            transform.position = settled;
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, t);
        }

        void ComputeTarget(out Vector3 wanted, out float size)
        {
            CameraFraming.Compute(roomRoot.position, roomRoot.lossyScale.x, roomW, roomH,
                cam.aspect, padding, topFrac, bottomFrac, out wanted, out size);
        }
    }
}
