using UnityEngine;

namespace Parabox
{
    // Occasional eye blink for the player mascot.
    public class Blinker : MonoBehaviour
    {
        public Transform eyeL;
        public Transform eyeR;
        public float minGap = 2.4f;
        public float maxGap = 5.5f;
        public float blinkTime = 0.12f;

        Vector3 sL = Vector3.one, sR = Vector3.one;
        float timer;
        float blink = -1f;

        void Start()
        {
            if (eyeL) sL = eyeL.localScale;
            if (eyeR) sR = eyeR.localScale;
            timer = Random.Range(minGap, maxGap);
        }

        void Update()
        {
            if (blink >= 0f)
            {
                blink += Time.deltaTime / blinkTime;
                float open = Mathf.Max(0.05f, Mathf.Abs(Mathf.Cos(blink * Mathf.PI))); // 1 -> 0 -> 1
                if (eyeL) eyeL.localScale = new Vector3(sL.x, sL.y * open, sL.z);
                if (eyeR) eyeR.localScale = new Vector3(sR.x, sR.y * open, sR.z);
                if (blink >= 1f)
                {
                    blink = -1f;
                    if (eyeL) eyeL.localScale = sL;
                    if (eyeR) eyeR.localScale = sR;
                }
            }
            else
            {
                timer -= Time.deltaTime;
                if (timer <= 0f) { blink = 0f; timer = Random.Range(minGap, maxGap); }
            }
        }
    }
}
