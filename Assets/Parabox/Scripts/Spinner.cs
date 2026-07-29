using UnityEngine;

namespace Parabox
{
    // Slowly rotates its transform in the 2D plane — used for the whirlpool portal rings.
    public class Spinner : MonoBehaviour
    {
        public float degPerSec = 40f;

        void Update()
        {
            transform.Rotate(0f, 0f, degPerSec * Time.deltaTime);
        }
    }
}
