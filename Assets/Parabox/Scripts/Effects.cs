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
        public static Sprite Glow;    // soft glow for the premium win celebration

        // Premium level-complete celebration from the board centre: shockwave + glow burst + streaks + flash.
        public static void Celebrate(Camera cam, Color[] palette) => CelebrateAt(cam, palette, null);

        // Same, but anchored to a real world point.
        //
        // Celebrate() put the burst at the CAMERA, which is not where the board is: CameraFollow
        // reserves 16% of the view at the top and 20% at the bottom for the HUD, so the camera sits
        // off-centre from the room by design. The shockwave therefore radiated from one point while
        // BoardWinFx's pulse radiated from another — two explosions with different origins, which
        // is exactly what reads as "strange" even when you can't name it.
        public static void CelebrateAt(Camera cam, Color[] palette, Vector3? worldPos)
        {
            if (Glow == null || cam == null) return;
            var go = new GameObject("WinFx");
            Vector3 at = worldPos ?? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
            go.transform.position = new Vector3(at.x, at.y, 0f);
            var fx = go.AddComponent<WinFx>();
            fx.glow = Glow;
            fx.ring = Ring;
            fx.palette = palette;
            fx.sizeScale = Mathf.Clamp(cam.orthographicSize / 4f, 0.7f, 1.7f);
            fx.order = 250;
        }

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

        // Four or five tiny square chips left at the push-off point of a normal grid move. They
        // travel mostly opposite the object, live for only a fraction of a second and stay behind
        // the piece, so movement gains a tactile edge without turning into a celebration effect.
        public static void MoveFragments(Transform parent, Vector3 localPos, Vector2 direction,
                                         Color color, int count, int order)
        {
            if (Piece == null || direction.sqrMagnitude < 0.001f || count <= 0) return;

            direction.Normalize();
            Vector2 side = new Vector2(-direction.y, direction.x);
            var host = new GameObject("MoveFragments").AddComponent<BurstAnim>();
            if (parent != null) host.transform.SetParent(parent, false);
            host.transform.localPosition = localPos - (Vector3)(direction * 0.22f);
            host.life = 0.30f;
            host.gravity = 0f;
            host.shrink = 0.76f;

            for (int i = 0; i < count; i++)
            {
                Color chip = Color.Lerp(color, Color.white, i == 0 ? 0.18f : 0.04f);
                chip.a = i == 0 ? 0.96f : 0.76f;
                var sr = NewPiece(host.transform, Piece, chip, order);
                float sc = Random.Range(0.075f, 0.125f);
                sr.transform.localScale = Vector3.one * sc;
                sr.transform.localPosition = (Vector3)(side * Random.Range(-0.20f, 0.20f));
                Vector2 velocity = -direction * Random.Range(0.52f, 0.92f)
                                   + side * Random.Range(-0.42f, 0.42f);
                host.Add(sr, velocity, sc, Random.Range(-180f, 180f));
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
