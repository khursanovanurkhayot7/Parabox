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
        public static MusicPlayer Instance;

        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.22f;

        AudioSource src;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Keep the scene's original Flowerbed Fields loop. Both MainMenu and Game serialize
            // that same clip, so the persistent player carries it across scenes without a restart.
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
            src.volume = Mathf.MoveTowards(src.volume, volume, Time.unscaledDeltaTime * 1.8f);

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
