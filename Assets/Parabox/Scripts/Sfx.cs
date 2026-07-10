using UnityEngine;

namespace Parabox
{
    // Tiny procedural sound layer — soft blips synthesized at runtime, so there are
    // no audio assets to import. Low volume by design; press M to mute.
    public static class Sfx
    {
        const string MuteKey = "Parabox.Muted";
        const int Rate = 44100;

        static AudioSource src;
        static bool muted;
        static AudioClip move, push, blocked, ding, win, hover, click;

        public static bool Muted => muted;

        public static void Init()
        {
            if (src != null) return;

            var go = new GameObject("Sfx");
            Object.DontDestroyOnLoad(go);
            src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;

            muted = PlayerPrefs.GetInt(MuteKey, 0) == 1;

            move = Tone(320f, 0.06f, 0.16f);
            push = Tone(190f, 0.09f, 0.18f);
            blocked = Tone(110f, 0.10f, 0.15f);
            ding = Arp(new[] { 660f, 988f }, 0.09f, 0.20f);
            win = Arp(new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.22f);
            hover = Tone(880f, 0.025f, 0.05f);  // soft UI tick on hover
            click = Tone(560f, 0.05f, 0.11f);   // soft UI click
        }

        public static void ToggleMute()
        {
            muted = !muted;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
        }

        public static void Move() => Play(move);
        public static void Push() => Play(push);
        public static void Blocked() => Play(blocked);
        public static void Ding() => Play(ding);
        public static void Win() => Play(win);
        public static void Hover() => Play(hover);
        public static void Click() => Play(click);

        static void Play(AudioClip c)
        {
            if (src != null && !muted && c != null) src.PlayOneShot(c);
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
    }
}
