using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Smooth top->bottom vertex-color gradient for a UI Graphic (Image/Text).
    // Gives buttons a premium two-tone fill instead of a flat single color.
    public class UIGradient : BaseMeshEffect
    {
        public Color top = Color.white;
        public Color bottom = Color.white;

        static readonly List<UIVertex> verts = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            vh.GetUIVertexStream(verts);

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                float y = verts[i].position.y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float h = Mathf.Max(0.0001f, maxY - minY);

            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                float t = (v.position.y - minY) / h;     // 0 = bottom, 1 = top
                v.color = v.color * Color.Lerp(bottom, top, t);
                verts[i] = v;
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(verts);
            verts.Clear();
        }
    }
}
