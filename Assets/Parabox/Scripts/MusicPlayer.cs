using System.Collections;
using UnityEngine;

namespace Parabox
{
    // Persistent looping background music. Survives scene loads (menu <-> game) so the track
    // keeps playing seamlessly, and follows the global mute (M key / on-screen MUTE button).
    // A singleton: only the first instance lives; later scene copies destroy themselves.
    [RequireComponent(typeof(AudioSource))]
    public class MusicPlayer : MonoBehaviour
    {
        const string FunMusicResource = "Music/NeonPuzzleParty";
        public static MusicPlayer Instance;

        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.45f;

        AudioSource src;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Prefer the generated, project-owned neon arcade loop. Keeping the serialized clip as
            // a fallback means an older scene still opens safely before the Editor audio baker runs.
            AudioClip funClip = Resources.Load<AudioClip>(FunMusicResource);
            if (funClip != null) clip = funClip;

            // This release replaces the previous music track. Clear the stale saved mute once so
            // existing test cabinets hear it; later mute choices still persist normally.
            Sfx.EnsureAudibleForMusicUpgrade("Parabox.AudioUpgrade.NeonPuzzleParty.v1");
            Sfx.Init();   // make sure the saved mute state is loaded

            src = GetComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D
            src.volume = volume;
            src.mute = Sfx.Muted;
            StartCoroutine(StartWhenReady());
        }

        void Update()
        {
            // stay in sync with the shared mute toggle
            if (src == null) return;
            src.mute = Sfx.Muted;

            // Browsers and some arcade machines can briefly suspend their audio context while a
            // scene starts. Recover automatically once audio is allowed instead of leaving the
            // whole session silent after one failed first-frame Play call.
            if (!src.mute && !src.isPlaying && clip != null
                && clip.loadState == AudioDataLoadState.Loaded)
                src.Play();
        }

        IEnumerator StartWhenReady()
        {
            if (clip == null)
            {
                Debug.LogWarning("[Parabox] Background music clip is missing.");
                yield break;
            }

            src.clip = clip;
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();

            float remaining = 10f;
            while (clip.loadState == AudioDataLoadState.Loading && remaining > 0f)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (clip.loadState == AudioDataLoadState.Loaded)
                src.Play();
            else
                Debug.LogWarning($"[Parabox] Background music failed to load: {clip.name}.");
        }
    }
}
