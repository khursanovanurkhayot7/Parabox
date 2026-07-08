using UnityEngine;

namespace Parabox
{
    // Lightweight sprite-based particles. We avoid Unity's ParticleSystem so there
    // are no render-pipeline material/shader issues — plain SpriteRenderers render
    // correctly in URP and WebGL. The animating MonoBehaviours live in their own
    // files (BurstAnim.cs, RippleAnim.cs) so Unity can serialize them.
    public static class Fx
    {
        public static Sprite Piece;   // small rounded sprite for particles
        public static Sprite Ring;    // rounded outline for the ripple

        // A burst of pieces flying outward from a point (in a room's local space,
        // so it scales correctly inside nested boxes).
        public static void Burst(Transform parent, Vector3 localPos, Color[] palette,
                                 int count, float speed, float size, float gravity, float life, int order)
        {
            if (Piece == null) return;
            var host = new GameObject("Burst").AddComponent<BurstAnim>();
            if (parent != null) host.transform.SetParent(parent, false);
            host.transform.localPosition = localPos;
            host.transform.localScale = Vector3.one;
            host.life = life;
            host.gravity = gravity;
            host.shrink = 0.5f;

            for (int i = 0; i < count; i++)
            {
                var sr = NewPiece(host.transform, Piece, palette[i % palette.Length], order);
                float ang = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.35f, 0.35f);
                float sp = speed * Random.Range(0.55f, 1.15f);
                float sc = size * Random.Range(0.7f, 1.25f);
                sr.transform.localScale = Vector3.one * sc;
                host.Add(sr, new Vector3(Mathf.Cos(ang) * sp, Mathf.Sin(ang) * sp, 0f), sc, Random.Range(-360f, 360f));
            }
        }

        // A single ring that expands and fades — the "ding" when a target is filled.
        public static void Ripple(Transform parent, Vector3 localPos, Color color, int order)
        {
            if (Ring == null) return;
            var host = new GameObject("Ripple").AddComponent<RippleAnim>();
            if (parent != null) host.transform.SetParent(parent, false);
            host.transform.localPosition = localPos;
            var sr = NewPiece(host.transform, Ring, color, order);
            host.sr = sr;
            host.baseColor = color;
        }

        // Celebration confetti raining across the whole camera view.
        public static void Confetti(Camera cam, Color[] palette, int count, int order)
        {
            if (Piece == null || cam == null) return;
            var host = new GameObject("Confetti").AddComponent<BurstAnim>();
            host.transform.position = Vector3.zero;
            host.life = 2.7f;
            host.gravity = 3.5f;
            host.shrink = 0f;

            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            Vector3 c = cam.transform.position;

            for (int i = 0; i < count; i++)
            {
                var sr = NewPiece(host.transform, Piece, palette[i % palette.Length], order);
                float sc = Random.Range(0.12f, 0.26f);
                sr.transform.localScale = Vector3.one * sc;
                sr.transform.position = new Vector3(c.x + Random.Range(-w, w), c.y + h + Random.Range(0f, h), 0f);
                host.Add(sr, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-1.6f, -3.2f), 0f), sc, Random.Range(-540f, 540f));
            }
        }

        static SpriteRenderer NewPiece(Transform parent, Sprite sprite, Color color, int order)
        {
            var go = new GameObject("p");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
