using UnityEngine;

namespace Parabox
{
    // Shared board-framing math. Both the gameplay camera (CameraFollow) and the menu fly-in use
    // this identical formula so the menu's final frame lands EXACTLY on the gameplay camera pose.
    public static class CameraFraming
    {
        public static void Compute(Vector3 roomCenter, float roomScale, int w, int h,
            float aspect, float padding, float topFrac, float bottomFrac,
            out Vector3 pos, out float size)
        {
            float halfW = w * roomScale * 0.5f;
            float halfH = h * roomScale * 0.5f;

            float usable = Mathf.Clamp(1f - topFrac - bottomFrac, 0.25f, 1f);
            float sizeH = halfH / usable;
            float sizeW = halfW / Mathf.Max(0.1f, aspect);
            size = Mathf.Max(sizeH, sizeW) * padding;

            float yOffset = (bottomFrac - topFrac) * size;
            pos = new Vector3(roomCenter.x, roomCenter.y - yOffset, roomCenter.z - 10f);
        }
    }
}
