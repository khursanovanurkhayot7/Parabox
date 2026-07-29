using UnityEngine;

namespace Parabox
{
    // Persistent looping background music. Survives scene loads (menu <-> game) so the track
    // keeps playing seamlessly, and follows the global mute (M key / on-screen MUTE button).
    // A singleton: only the first instance lives; later scene copies destroy themselves.
    [RequireComponent(typeof(AudioSource))]
    public class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer Instance;

        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.45f;

        AudioSource src;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Sfx.Init();   // make sure the saved mute state is loaded

            src = GetComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D
            src.volume = volume;
            src.mute = Sfx.Muted;
            if (clip != null) src.Play();
        }

        void Update()
        {
            // stay in sync with the shared mute toggle
            if (src != null) src.mute = Sfx.Muted;
        }
    }
}
