using System.Collections.Generic;
using UnityEngine;

namespace Parabox
{
    // Animates a set of particle pieces spawned by Fx.Burst / Fx.Confetti.
    public class BurstAnim : MonoBehaviour
    {
        public float life = 0.6f, gravity = 4f, shrink = 0.5f;
        float age;

        readonly List<Transform> ts = new List<Transform>();
        readonly List<SpriteRenderer> srs = new List<SpriteRenderer>();
        readonly List<Vector3> vels = new List<Vector3>();
        readonly List<float> baseScale = new List<float>();
        readonly List<float> spin = new List<float>();
        readonly List<Color> cols = new List<Color>();

        public void Add(SpriteRenderer sr, Vector3 vel, float scale, float spinDeg)
        {
            ts.Add(sr.transform); srs.Add(sr); vels.Add(vel);
            baseScale.Add(scale); spin.Add(spinDeg); cols.Add(sr.color);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            float f = Mathf.Clamp01(age / life);

            for (int i = 0; i < ts.Count; i++)
            {
                Vector3 v = vels[i];
                v.y -= gravity * dt;
                vels[i] = v;
                ts[i].localPosition += v * dt;
                ts[i].Rotate(0f, 0f, spin[i] * dt);
                ts[i].localScale = Vector3.one * baseScale[i] * (1f - shrink * f);
                Color c = cols[i];
                c.a = 1f - f * f;
                srs[i].color = c;
            }

            if (age >= life) Destroy(gameObject);
        }
    }
}
