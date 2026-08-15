using UnityEngine;

namespace Parabox
{
    // A movement-timed blink for the CONTROLLED player. GameManager sends the real successful
    // move number, so blocked input never counts and Undo/tutorial rewind remain deterministic.
    public class Blinker : MonoBehaviour
    {
        public Transform eyeL;
        public Transform eyeR;
        [Min(1)] public int stepsPerBlink = 2;
        [Min(0.05f)] public float blinkTime = 0.16f;
        [Range(0.02f, 0.2f)] public float closedEyeScale = 0.06f;

        Vector3 sL = Vector3.one, sR = Vector3.one;
        Transform outlineL, outlineR;
        Vector3 outlineScaleL = Vector3.one, outlineScaleR = Vector3.one;
        bool poseCached;
        float blink = -1f;

        void Start()
        {
            CacheOpenPose();
            ApplyOpenAmount(1f);
        }

        // Called only after TryMovePlayer succeeds. Passing the model's move count instead of
        // maintaining a second counter keeps cadence correct after Undo and tutorial replay.
        public void BlinkOnSuccessfulMove(int successfulMoveCount)
        {
            int cadence = Mathf.Max(1, stepsPerBlink);
            if (successfulMoveCount <= 0 || successfulMoveCount % cadence != 0) return;
            CacheOpenPose();
            blink = 0f;
            ApplyOpenAmount(1f);
        }

        // Tutorial playback uses the real actor, then rewinds the model. Restore the visual pose as
        // part of that hand-off so gameplay always begins with fully open eyes on move zero.
        public void ResetOpen()
        {
            CacheOpenPose();
            blink = -1f;
            ApplyOpenAmount(1f);
        }

        void CacheOpenPose()
        {
            if (!poseCached)
            {
                if (eyeL == null) eyeL = transform.Find("EyeL");
                if (eyeR == null) eyeR = transform.Find("EyeR");
                if (eyeL != null) sL = eyeL.localScale;
                if (eyeR != null) sR = eyeR.localScale;
                poseCached = true;
            }

            // The premium board skin adds these as siblings after the prefab is instantiated.
            // Discover them lazily and close them with the dark eye fills; otherwise two open rings
            // would remain visible while the pupils blinked.
            if (outlineL == null)
            {
                outlineL = transform.Find("EyeOutlineL");
                if (outlineL != null) outlineScaleL = outlineL.localScale;
            }
            if (outlineR == null)
            {
                outlineR = transform.Find("EyeOutlineR");
                if (outlineR != null) outlineScaleR = outlineR.localScale;
            }
        }

        void Update()
        {
            if (blink < 0f) return;

            blink = Mathf.Min(1f, blink + Time.unscaledDeltaTime / Mathf.Max(0.05f, blinkTime));
            float open = Mathf.Max(closedEyeScale,
                Mathf.Abs(Mathf.Cos(blink * Mathf.PI))); // open -> closed -> open
            ApplyOpenAmount(open);
            if (blink >= 1f)
            {
                blink = -1f;
                ApplyOpenAmount(1f);
            }
        }

        void ApplyOpenAmount(float open)
        {
            if (eyeL != null) eyeL.localScale = ScaleY(sL, open);
            if (eyeR != null) eyeR.localScale = ScaleY(sR, open);
            if (outlineL != null) outlineL.localScale = ScaleY(outlineScaleL, open);
            if (outlineR != null) outlineR.localScale = ScaleY(outlineScaleR, open);
        }

        static Vector3 ScaleY(Vector3 openScale, float amount)
            => new Vector3(openScale.x, openScale.y * amount, openScale.z);

        void OnDisable()
        {
            blink = -1f;
            if (poseCached) ApplyOpenAmount(1f);
        }
    }
}
