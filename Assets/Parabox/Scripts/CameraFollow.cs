using UnityEngine;

namespace Parabox
{
    // Frames the room the player is currently inside.
    // When the player enters/exits a box the camera smoothly zooms in/out.
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        public float padding = 1.25f;
        public float speed = 5f;

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
            Vector3 wanted = new Vector3(c.x, c.y, c.z - 10f);

            float halfW = roomW * s * 0.5f;
            float halfH = roomH * s * 0.5f;
            float size = Mathf.Max(halfH, halfW / Mathf.Max(0.1f, cam.aspect)) * padding;

            transform.position = Vector3.Lerp(transform.position, wanted, t);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, t);
        }
    }
}
