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

        Camera cam;
        Transform roomRoot;
        int roomW;
        int roomH;

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

        void LateUpdate()
        {
            if (roomRoot != null)
                Apply(1f - Mathf.Exp(-speed * Time.deltaTime));
        }

        void Apply(float t)
        {
            float s = roomRoot.lossyScale.x;
            Vector3 c = roomRoot.position;

            float halfW = roomW * s * 0.5f;
            float halfH = roomH * s * 0.5f;

            // Fit the board into the MIDDLE band only, leaving room top (title) + bottom (controls),
            // so the world board never slides under the screen-space HUD.
            float usable = Mathf.Clamp(1f - topFrac - bottomFrac, 0.25f, 1f);
            float sizeH = halfH / usable;
            float sizeW = halfW / Mathf.Max(0.1f, cam.aspect);
            float size = Mathf.Max(sizeH, sizeW) * padding;

            // Shift the view so the board centers in that band (extra bottom room → board rides a bit higher).
            float yOffset = (bottomFrac - topFrac) * size;
            Vector3 wanted = new Vector3(c.x, c.y - yOffset, c.z - 10f);

            transform.position = Vector3.Lerp(transform.position, wanted, t);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, t);
        }
    }
}
