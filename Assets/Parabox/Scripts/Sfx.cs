using UnityEngine;
using UnityEngine.UI;

namespace Parabox
{
    // Shared sound layer. The shipped movement voices, push voice, hover tick and arcade button
    // press are prebuilt by ParaboxFunAudioBaker; gameplay only selects and plays those assets.
    // Press M to mute everything.
    public static class Sfx
    {
        const string MuteKey = "Parabox.Muted";
        const int Rate = 44100;

        static AudioSource src;
        static bool muted;
        static AudioClip[] moveVoices;
        static AudioClip push, blocked, ding, win, death, hover, click;
        static AudioClip undo, roomShift, impact, mechanic, timerWarning;
        static float pushGain = 1f, moveGain = 1f, hoverGain = 1f;
        static float blockedGain = 1f, dingGain = 1f, winGain = 1f, deathGain = 1f, clickGain = 1f;
        static float undoGain = 1f, roomShiftGain = 1f, impactGain = 1f, mechanicGain = 1f, warningGain = 1f;
        static int moveVoiceIndex;
        static float lastMoveVoiceAt = -10f;

        public static bool Muted => muted;

        public static void Init()
        {
            if (src == null)
            {
                var go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = 1f;
                src.priority = 0;             // result/UI cues win if Unity has to virtualize audio
                src.ignoreListenerPause = true;

                muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;

                moveVoices = new[]
                {
                    Recorded("Sfx/MoveVoice_01"),
                    Recorded("Sfx/MoveVoice_02"),
                    Recorded("Sfx/MoveVoice_03"),
                    Recorded("Sfx/MoveVoice_04")
                };
                push = Recorded("Sfx/PushVoice");
                hover = Recorded("Sfx/HoverFun");
                if (AllMissing(moveVoices))
                    Debug.LogError("[Parabox] Prebuilt movement voices are missing. Run Tools/Parabox/Build Fun Audio Pack (Run This).");
                if (push == null || hover == null)
                    Debug.LogError("[Parabox] Prebuilt push/hover audio is missing. Run Tools/Parabox/Build Fun Audio Pack (Run This).");
                moveGain = 0.72f;
                pushGain = 0.76f;
                hoverGain = 0.46f;

                // These four short OGG files are CC0 and live under Resources/Sfx. The generated
                // fallbacks keep the game audible if somebody removes the asset folder later.
                ding = Recorded("Sfx/Confirm");
                if (ding != null) dingGain = 0.48f;
                else ding = Arp(new[] { 660f, 988f }, 0.09f, 0.20f);

                win = Recorded("Sfx/Win");
                if (win != null)
                {
                    win = Amplified(win, "WinLoud", 0.75f);
                    winGain = 0.90f;
                }
                else win = Arp(new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.26f);

                death = Recorded("Sfx/Death");
                if (death != null)
                {
                    // The source recording peaks at only ~0.16 (-16 dBFS), so source volume 1 was
                    // still buried by the music.  Normalize the actual samples once at startup.
                    death = Amplified(death, "DeathLoud", 0.85f);
                    deathGain = 1f;
                }
                else death = FallingImpact();

                click = Recorded("Sfx/ButtonFun");
                if (click == null) click = Recorded("Sfx/ButtonClick");
                if (click != null) clickGain = 0.68f;
                else click = Arp(new[] { 720f, 520f }, 0.028f, 0.18f);

                blocked = Recorded("Sfx/Blocked");
                if (blocked != null) blockedGain = 0.48f;
                else blocked = Tone(110f, 0.10f, 0.15f);

                undo = Recorded("Sfx/Undo");
                if (undo != null) undoGain = 0.48f;
                else undo = Arp(new[] { 620f, 420f }, 0.045f, 0.16f);

                roomShift = Recorded("Sfx/RoomShift");
                if (roomShift != null) roomShiftGain = 0.45f;
                else roomShift = Arp(new[] { 420f, 630f, 840f }, 0.055f, 0.15f);

                impact = Recorded("Sfx/Impact");
                if (impact != null) impactGain = 0.52f;
                else impact = Tone(84f, 0.16f, 0.22f);

                mechanic = Recorded("Sfx/Mechanic");
                if (mechanic != null) mechanicGain = 0.44f;
                else mechanic = Arp(new[] { 540f, 760f }, 0.04f, 0.14f);

                timerWarning = Recorded("Sfx/TimerWarning");
                if (timerWarning != null) warningGain = 0.38f;
                else timerWarning = Tone(920f, 0.09f, 0.14f);
            }

            // Sfx survives scene loads, but UI buttons do not. Re-scan every time a scene's
            // controller calls Init so pointer, keyboard and controller activation all sound.
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
                AttachButton(button);
        }

        // Editor prebuilders use this while authoring scenes. During Play Mode, missing components
        // are reported instead of being generated so every static button remains scene-backed.
        public static void AttachButton(Button button)
        {
            if (button == null || button.GetComponent<UIButtonSfx>() != null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                button.gameObject.AddComponent<UIButtonSfx>();
                return;
            }
#endif
            Debug.LogError("[Parabox] UIButtonSfx is not prebuilt on " + button.name
                + ". Run Tools/Parabox/Generate Prebuilt UI (Run This).");
        }

        public static void ToggleMute()
        {
            muted = !muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
            // The normal button click happens before this toggle. When unmuting it was therefore
            // suppressed; confirm the newly-audible state immediately instead of feeling broken.
            if (!muted) Play(click, clickGain);
        }

        // A previous local build could leave the cabinet permanently muted in PlayerPrefs, which
        // made a valid replacement music clip appear broken. Each music upgrade may call this
        // once with a new version key; normal player mute choices work normally after that run.
        public static void EnsureAudibleForMusicUpgrade(string versionKey)
        {
            if (string.IsNullOrEmpty(versionKey) || PlayerPrefs.GetInt(versionKey, 0) == 1) return;

            muted = false;
            PlayerPrefs.SetInt(MuteKey, 0);
            PlayerPrefs.SetInt(versionKey, 1);
            PlayerPrefs.Save();
            if (src != null) src.mute = false;
        }

        public static void Move() => PlayMovementVoice(moveGain);
        public static void Push() => Play(push, pushGain);
        public static void Blocked() => Play(blocked, blockedGain);
        public static void Ding() => Play(ding, dingGain);
        public static void Win() => Play(win, winGain);
        public static void Death()
        {
            Init();
            Play(death, deathGain);
            // A short impact under the longer failure recording keeps the cue unmistakable over
            // the music and the board-break animation without replacing the actual loss sound.
            Play(impact, Mathf.Max(0.72f, impactGain));
        }
        public static void Hover() => Play(hover, hoverGain);
        public static void Click() => Play(click, clickGain);
        public static void Undo() => Play(undo, undoGain);
        public static void RoomShift() => Play(roomShift, roomShiftGain);
        public static void Impact() => Play(impact, impactGain);
        public static void Mechanic() => Play(mechanic, mechanicGain);
        public static void TimerWarning() => Play(timerWarning, warningGain);

        static void PlayMovementVoice(float gain)
        {
            Init();
            // Successful held-repeat movement is intentionally allowed, but never stack two
            // voices inside the same input pulse/frame if multiple input paths are accidentally live.
            if (Time.unscaledTime - lastMoveVoiceAt < 0.045f || moveVoices == null) return;
            lastMoveVoiceAt = Time.unscaledTime;

            for (int attempts = 0; attempts < moveVoices.Length; attempts++)
            {
                int index = moveVoiceIndex++ % moveVoices.Length;
                if (moveVoices[index] == null) continue;
                Play(moveVoices[index], gain);
                return;
            }
        }

        static bool AllMissing(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return true;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return false;
            return true;
        }

        static void Play(AudioClip c, float gain = 1f)
        {
            if (src != null && !muted && c != null) src.PlayOneShot(c, gain);
        }

        // Resource clips in this project used to import with Preload Audio Data disabled.  Loading
        // their sample data here as well makes startup deterministic even if an importer setting is
        // changed later or a platform override disables preloading.
        static AudioClip Recorded(string path)
        {
            var clip = Resources.Load<AudioClip>(path);
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            return clip;
        }

        // Normalize a short result clip in memory.  AudioSource.volume cannot repair a quiet file
        // once it is already at 1, and PlayOneShot volumeScale is not a reliable gain control above
        // 1 on every target platform.  These clips are tiny, so a one-time copy is deterministic and
        // costs less memory than another music buffer.
        static AudioClip Amplified(AudioClip original, string name, float targetPeak)
        {
            if (original == null || original.samples <= 0 || original.channels <= 0) return original;

            var data = new float[original.samples * original.channels];
            if (!original.GetData(data, 0)) return original;

            float peak = 0f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak < 0.0001f || peak >= targetPeak) return original;

            float scale = targetPeak / peak;
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] * scale, -1f, 1f);

            var loud = AudioClip.Create(name, original.samples, original.channels, original.frequency, false);
            loud.SetData(data, 0);
            return loud;
        }

        // Single decaying sine tone.
        static AudioClip Tone(float freq, float dur, float vol)
        {
            int n = Mathf.Max(1, (int)(Rate * dur));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Exp(-t * 16f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * vol;
            }
            var clip = AudioClip.Create("tone", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Sequence of decaying tones (a little chime).
        static AudioClip Arp(float[] freqs, float noteDur, float vol)
        {
            int per = Mathf.Max(1, (int)(Rate * noteDur));
            int n = per * freqs.Length;
            var data = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * 11f);
                    data[k * per + i] = Mathf.Sin(2f * Mathf.PI * freqs[k] * t) * env * vol;
                }
            }
            var clip = AudioClip.Create("arp", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A falling synth voice ending in a low impact: unmistakably a failed run, without
        // needing a third-party voice clip or competing with the background track.
        static AudioClip FallingImpact()
        {
            const float dur = 0.62f;
            int n = (int)(Rate * dur);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float freq = Mathf.Lerp(360f, 72f, Mathf.SmoothStep(0f, 1f, k));
                phase += 2f * Mathf.PI * freq / Rate;
                float voice = Mathf.Sin(phase) + Mathf.Sin(phase * 0.5f) * 0.32f;
                float thud = Mathf.Sin(2f * Mathf.PI * 54f * t) * Mathf.Clamp01((t - 0.30f) * 12f);
                float env = (1f - Mathf.Exp(-t * 28f)) * Mathf.Exp(-t * 3.7f);
                data[i] = (voice * 0.19f + thud * 0.09f) * env;
            }
            var clip = AudioClip.Create("death", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

}
