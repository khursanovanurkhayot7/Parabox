using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // A real UI prop between the palm and curled fingers, not a baked teacher image.
    // Its grip follows the arm shader so it cannot slide away while he speaks.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class PremiumTeacherPointer : BaseMeshEffect
    {
        Vector2 grip, tip;
        Graphic heldPortrait;

        public Vector2 GripPosition => grip;
        public Vector2 TipPosition => tip;

        public void Follow(Graphic portrait, Vector3 worldTarget)
        {
            if (portrait == null || portrait.material == null) return;
            heldPortrait = portrait;
            portrait.material.SetFloat("_HeldPointer", 1f);
            Vector2 size = portrait.rectTransform.rect.size;
            if (size.x < 0.01f || size.y < 0.01f) return;
            if (portrait is Image image && image.preserveAspect && image.sprite != null)
            {
                float aspect = image.sprite.rect.width / image.sprite.rect.height;
                if (size.x / size.y > aspect) size.x = size.y * aspect;
                else size.y = size.x / aspect;
            }
            Vector2 uv = GripUv(portrait.material);
            Vector2 portraitPoint = portrait.rectTransform.rect.center + new Vector2(
                (uv.x - 0.5f) * size.x, (uv.y - 0.5f) * size.y);
            Vector2 next = transform.InverseTransformPoint(portrait.rectTransform.TransformPoint(portraitPoint));
            Vector2 target = transform.InverseTransformPoint(worldTarget);
            Vector2 portraitTarget = portrait.rectTransform.InverseTransformPoint(worldTarget);
            portraitTarget -= portrait.rectTransform.rect.center;
            portrait.material.SetVector("_PointerGripTip", new Vector4(uv.x, uv.y,
                0.5f + portraitTarget.x / size.x, 0.5f + portraitTarget.y / size.y));
            portrait.material.SetVector("_PointerCanvasSize", new Vector4(size.x, size.y, 0f, 0f));
            if ((next - grip).sqrMagnitude < 0.00001f && (target - tip).sqrMagnitude < 0.00001f) return;
            grip = next;
            tip = target;
            graphic.SetVerticesDirty();
        }

        public static Vector2 GripUv(Material material)
        {
            // Inside the thumb/finger fold, not the outer silhouette of the hand. The
            // prop-bearing hand stays curled even while the other hand opens to speak.
            // Seat the shaft deeper between the curled fingers instead of touching the glove edge.
            Vector2 p = new Vector2(0.086f, 0.399f);
            float handOpen = Mathf.Lerp(material.GetFloat("_HandOpen"), 0.22f,
                material.GetFloat("_HeldPointer"));
            float opening = Mathf.InverseLerp(0.22f, 1f, handOpen);
            p.y = 0.404f + (p.y - 0.404f) * (1f + opening * 0.20f);
            float fingerTip = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.345f, 0.404f, p.y));
            p.x = 0.060f + (p.x - 0.060f) * (1f + opening * fingerTip * 0.12f);
            p.y = 0.404f + (p.y - 0.404f) * Mathf.Lerp(1f, 0.60f, material.GetFloat("_HeldPointer"));
            p = UndoJoint(p, new Vector2(0.063f, 0.439f), material.GetFloat("_WristAngle"), 0.405f, 0.455f);
            p = UndoJoint(p, new Vector2(0.075f, 0.550f), material.GetFloat("_ElbowAngle"), 0.485f, 0.565f);
            p = Rotate(p, new Vector2(0.115f, 0.643f), -material.GetFloat("_GestureAngle"));
            p.x = 0.590f - p.x;
            p.x += material.GetFloat("_BodySway") * Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.43f, 0.74f, p.y));
            if (p.y > 0.45f) p.y = 0.45f + (p.y - 0.45f) * (1f + material.GetFloat("_Breath"));
            return p;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (heldPortrait != null && heldPortrait.material != null)
                heldPortrait.material.SetFloat("_HeldPointer", 0f);
        }

        static Vector2 UndoJoint(Vector2 point, Vector2 pivot, float angle, float low, float high)
        {
            Vector2 result = point;
            // The shader's joint weight depends on its pre-rotation Y coordinate. Fixed-point
            // inversion converges well within a pixel for the authored, restrained gestures.
            for (int i = 0; i < 8; i++)
            {
                float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(low, high, result.y));
                result = Rotate(point, pivot, -angle * weight);
            }
            return result;
        }

        static Vector2 Rotate(Vector2 point, Vector2 pivot, float angle)
        {
            Vector2 d = point - pivot;
            float s = Mathf.Sin(angle), c = Mathf.Cos(angle);
            return pivot + new Vector2(c * d.x - s * d.y, s * d.x + c * d.y);
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || graphic == null) return;
            mesh.Clear();
            Vector2 delta = tip - grip;
            if (delta.sqrMagnitude < 4f) return;
            Vector2 direction = delta.normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            // The short handle continues behind the grip, making the fingers visibly wrap it.
            Vector2 start = grip - direction * 24f;
            Ribbon(mesh, start + new Vector2(0.7f, -1f), tip + new Vector2(0.7f, -1f), 4.8f,
                C(79, 35, 9), C(120, 60, 16));
            Ribbon(mesh, start, tip, 3.6f, C(171, 80, 12), C(255, 213, 94));
            Ribbon(mesh, start + normal * 0.8f, tip + normal * 0.8f, 0.9f,
                C(255, 216, 107), C(255, 252, 208));
            // The portrait shader puts the shaft over the palm but under curled fingers,
            // compositing the glove once so it keeps the teacher's soft entrance opacity.
            Ribbon(mesh, grip - direction * 25f, grip + direction * 4f, 6f,
                C(25, 13, 42), C(72, 39, 91));
            Disc(mesh, tip + new Vector2(0.4f, -0.7f), 4.4f, C(95, 46, 8), C(128, 65, 9));
            Disc(mesh, tip, 3.5f, C(255, 228, 126), C(198, 111, 14));
            Disc(mesh, tip + new Vector2(-0.8f, 1f), 1.2f, C(255, 255, 225), C(255, 239, 157));
        }

        static Color C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        void Vertex(VertexHelper mesh, Vector2 p, Color color)
            => mesh.AddVert(new Vector3(p.x, p.y, 0f), color * graphic.color, Vector2.zero);

        void Ribbon(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color bottom, Color top)
        {
            Vector2 d = (b - a).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
            int first = mesh.currentVertCount;
            Vertex(mesh, a - n, bottom); Vertex(mesh, a + n, top);
            Vertex(mesh, b - n, bottom); Vertex(mesh, b + n, top);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 1, first + 3, first + 2);
        }

        void Disc(VertexHelper mesh, Vector2 centre, float radius, Color middle, Color edge)
        {
            int first = mesh.currentVertCount;
            const int segments = 16;
            Vertex(mesh, centre, middle);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vertex(mesh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, edge);
            }
            for (int i = 0; i < segments; i++)
                mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % segments);
        }
    }
}
