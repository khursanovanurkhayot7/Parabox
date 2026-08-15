#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Parabox.EditorTools
{
    // Generates the complete arcade audio pack as imported WAV assets. Nothing is synthesized in
    // a player build: run this once in the Editor, then Unity serializes/compresses the files like
    // every other project asset.
    public static class ParaboxFunAudioBaker
    {
        const int MusicRate = 32000;
        const int SfxRate = 44100;
        const float Bpm = 140f;
        const string MusicPath = "Assets/Parabox/Resources/Music/NeonPuzzleParty.wav";
        const string SfxFolder = "Assets/Parabox/Resources/Sfx";
        const string RequestName = "ParaboxBuildFunAudio.request";
        const string ResultName = "ParaboxBuildFunAudio.result";
        static double nextPoll;

        [InitializeOnLoadMethod]
        static void RegisterRequestWatcher()
        {
            EditorApplication.update -= PollRequest;
            EditorApplication.update += PollRequest;
            PollRequest();
        }

        static void PollRequest()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5d;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;

            string root = Directory.GetParent(Application.dataPath).FullName;
            string request = Path.Combine(root, "Library", RequestName);
            if (!File.Exists(request)) return;
            File.Delete(request);

            try
            {
                BakeSilent();
                File.WriteAllText(Path.Combine(root, "Library", ResultName),
                    "success=1\nNeonPuzzleParty music and all movement/button voices were generated.\n");
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(root, "Library", ResultName),
                    "success=0\n" + exception + "\n");
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Parabox/Build Fun Audio Pack (Run This)", priority = 2)]
        public static void BakeFromMenu()
        {
            try
            {
                BakeSilent();
                EditorUtility.DisplayDialog("Parabox Fun Audio",
                    "Done. The upbeat neon-arcade loop, four movement voices, push voice, " +
                    "hover tick and button press are now prebuilt assets.", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Parabox Fun Audio failed", exception.Message, "OK");
            }
        }

        public static void BakeSilent()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MusicPath));
            Directory.CreateDirectory(SfxFolder);

            WriteMusic();
            for (int i = 0; i < 4; i++)
                WriteMono(SfxFolder + "/MoveVoice_0" + (i + 1) + ".wav", MakeMoveVoice(i), SfxRate);
            WriteMono(SfxFolder + "/PushVoice.wav", MakePushVoice(), SfxRate);
            WriteMono(SfxFolder + "/ButtonFun.wav", MakeButton(), SfxRate);
            WriteMono(SfxFolder + "/HoverFun.wav", MakeHover(), SfxRate);

            string notePath = "Assets/Parabox/Audio/FUN_AUDIO_README.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(notePath));
            File.WriteAllText(notePath,
                "Neon Puzzle Party audio pack\n" +
                "Original procedural composition and sound design generated for Hayot's Parabox.\n" +
                "BPM: 140. Loop length: 16 bars. No third-party samples are used.\n" +
                "Regenerate in Unity: Tools > Parabox > Build Fun Audio Pack (Run This).\n");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(MusicPath, true);
            for (int i = 0; i < 4; i++)
                ConfigureImporter(SfxFolder + "/MoveVoice_0" + (i + 1) + ".wav", false);
            ConfigureImporter(SfxFolder + "/PushVoice.wav", false);
            ConfigureImporter(SfxFolder + "/ButtonFun.wav", false);
            ConfigureImporter(SfxFolder + "/HoverFun.wav", false);
            AssetDatabase.SaveAssets();

            if (AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath) == null)
                throw new InvalidDataException("Unity could not import " + MusicPath + ".");
            Debug.Log("Parabox: fun audio pack generated as prebuilt assets.");
        }

        static void ConfigureImporter(string path, bool music)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;

            importer.forceToMono = !music;
            importer.loadInBackground = music;
            importer.ambisonic = false;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM,
                quality = music ? 0.72f : 1f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = true
            };
            importer.SaveAndReimport();
        }

        static void WriteMusic()
        {
            double beat = 60.0 / Bpm;
            int samples = (int)Math.Round(MusicRate * beat * 4.0 * 16.0);
            var left = new float[samples];
            var right = new float[samples];

            // D major: bright enough to feel playful, with a restrained sci-fi pulse that matches
            // the navy/cyan/violet presentation. I-vi-IV-V repeats, while the melody changes every
            // four bars so the loop has momentum rather than sounding like a menu drone.
            int[][] chords =
            {
                new[] { 62, 66, 69, 74 }, // D
                new[] { 59, 62, 66, 71 }, // Bm
                new[] { 55, 59, 62, 67 }, // G
                new[] { 57, 61, 64, 69 }, // A
            };
            int[] bass = { 38, 35, 31, 33 };
            int[][] melody =
            {
                new[] { 74, 78, 81, 78, 76, 74, 69, 71 },
                new[] { 71, 74, 78, 74, 73, 71, 69, 66 },
                new[] { 67, 71, 74, 79, 78, 74, 71, 69 },
                new[] { 69, 73, 76, 81, 78, 76, 73, 71 },
            };

            for (int bar = 0; bar < 16; bar++)
            {
                int progression = bar % 4;
                double barStart = bar * beat * 4.0;

                for (int b = 0; b < 4; b++)
                {
                    double at = barStart + b * beat;
                    AddKick(left, right, at, 0.66f);
                    if (b == 1 || b == 3) AddSnare(left, right, at, 0.34f, bar * 17 + b);
                    AddBass(left, right, at, beat * 0.72, Midi(bass[progression]), 0.19f);
                }

                for (int eighth = 0; eighth < 8; eighth++)
                {
                    double at = barStart + eighth * beat * 0.5;
                    AddHat(left, right, at, eighth % 2 == 0 ? 0.105f : 0.075f,
                        bar * 97 + eighth);

                    int chordTone = chords[progression][(eighth + bar) % 4] + 12;
                    AddPluck(left, right, at, beat * 0.34, Midi(chordTone), 0.075f,
                        eighth % 2 == 0 ? -0.48f : 0.48f);

                    // The first pass introduces the groove. Later passes answer it at a higher
                    // octave and add short rests, making the track build without becoming clutter.
                    if (bar >= 2 && !(bar >= 8 && eighth == 6))
                    {
                        int note = melody[(bar / 4) % melody.Length][eighth];
                        if (bar >= 12 && (eighth == 2 || eighth == 6)) note += 12;
                        AddLead(left, right, at, beat * 0.39, Midi(note), 0.12f,
                            (eighth - 3.5f) * 0.055f);
                    }
                }

                // A quick tom-like fill closes each phrase and makes the loop restart feel earned.
                if (bar % 4 == 3)
                {
                    AddKick(left, right, barStart + beat * 3.50, 0.34f);
                    AddKick(left, right, barStart + beat * 3.75, 0.27f);
                }
            }

            Normalize(left, right, 0.84f);
            WriteStereo(MusicPath, left, right, MusicRate);
        }

        static float[] MakeMoveVoice(int variant)
        {
            float duration = 0.105f + variant * 0.006f;
            int count = Mathf.RoundToInt(duration * SfxRate);
            var data = new float[count];
            float phase = 0f;
            float start = 350f + variant * 42f;
            float end = 680f + variant * 55f;
            for (int i = 0; i < count; i++)
            {
                float k = i / (float)(count - 1);
                float frequency = Mathf.Lerp(start, end, Smooth(k));
                phase += 2f * Mathf.PI * frequency / SfxRate;
                float attack = Mathf.Clamp01(k * 30f);
                float release = Mathf.Pow(1f - k, 2.1f);
                float vowel = Mathf.Sin(phase) + 0.34f * Mathf.Sin(phase * 2.02f)
                    + 0.13f * Mathf.Sin(phase * 3.97f);
                data[i] = vowel * attack * release * 0.40f;
            }
            return data;
        }

        static float[] MakePushVoice()
        {
            const float duration = 0.17f;
            int count = Mathf.RoundToInt(duration * SfxRate);
            var data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float k = i / (float)(count - 1);
                float frequency = Mathf.Lerp(300f, 150f, Smooth(k));
                phase += 2f * Mathf.PI * frequency / SfxRate;
                float env = Mathf.Clamp01(k * 24f) * Mathf.Pow(1f - k, 1.7f);
                float voice = Mathf.Sin(phase) + 0.38f * Mathf.Sin(phase * 0.5f);
                float thump = Mathf.Sin(2f * Mathf.PI * 68f * i / SfxRate)
                    * Mathf.Exp(-k * 8f);
                data[i] = (voice * 0.34f + thump * 0.24f) * env;
            }
            return data;
        }

        static float[] MakeButton()
        {
            const float duration = 0.105f;
            int count = Mathf.RoundToInt(duration * SfxRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SfxRate;
                float click = Noise(i + 9001) * Mathf.Exp(-t * 110f) * 0.24f;
                float first = Mathf.Sin(2f * Mathf.PI * 720f * t) * Mathf.Exp(-t * 35f);
                float local = Mathf.Max(0f, t - 0.028f);
                float second = t >= 0.028f
                    ? Mathf.Sin(2f * Mathf.PI * 1080f * local) * Mathf.Exp(-local * 38f) : 0f;
                data[i] = click + first * 0.28f + second * 0.24f;
            }
            return data;
        }

        static float[] MakeHover()
        {
            const float duration = 0.034f;
            int count = Mathf.RoundToInt(duration * SfxRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SfxRate;
                data[i] = Mathf.Sin(2f * Mathf.PI * 1180f * t) * Mathf.Exp(-t * 95f) * 0.22f;
            }
            return data;
        }

        static void AddLead(float[] l, float[] r, double start, double duration,
            float frequency, float gain, float pan)
        {
            AddTone(l, r, start, duration, frequency, gain, pan, 0);
        }

        static void AddPluck(float[] l, float[] r, double start, double duration,
            float frequency, float gain, float pan)
        {
            AddTone(l, r, start, duration, frequency, gain, pan, 1);
        }

        static void AddBass(float[] l, float[] r, double start, double duration,
            float frequency, float gain)
        {
            AddTone(l, r, start, duration, frequency, gain, 0f, 2);
        }

        static void AddTone(float[] l, float[] r, double start, double duration,
            float frequency, float gain, float pan, int timbre)
        {
            int first = Mathf.Max(0, Mathf.RoundToInt((float)(start * MusicRate)));
            int count = Mathf.RoundToInt((float)(duration * MusicRate));
            float phase = 0f;
            for (int n = 0; n < count && first + n < l.Length; n++)
            {
                float k = n / (float)Mathf.Max(1, count - 1);
                phase += 2f * Mathf.PI * frequency / MusicRate;
                float attack = Mathf.Clamp01(k * (timbre == 2 ? 18f : 45f));
                float release = Mathf.Pow(1f - k, timbre == 1 ? 2.4f : 1.25f);
                float sample;
                if (timbre == 1)
                    sample = Mathf.Sin(phase) + 0.45f * Mathf.Sin(phase * 2f)
                        + 0.20f * Mathf.Sin(phase * 3f);
                else if (timbre == 2)
                    sample = Mathf.Sin(phase) + 0.28f * Mathf.Sin(phase * 0.5f)
                        + 0.18f * Mathf.Sin(phase * 2f);
                else
                    sample = Mathf.Sin(phase) + 0.25f * Mathf.Sin(phase * 2f)
                        + 0.10f * Mathf.Sin(phase * 4f);
                Mix(l, r, first + n, sample * attack * release * gain, pan);
            }
        }

        static void AddKick(float[] l, float[] r, double start, float gain)
        {
            int first = Mathf.RoundToInt((float)(start * MusicRate));
            int count = Mathf.RoundToInt(0.19f * MusicRate);
            float phase = 0f;
            for (int n = 0; n < count && first + n < l.Length; n++)
            {
                float t = n / (float)MusicRate;
                float frequency = Mathf.Lerp(145f, 48f, Mathf.Clamp01(t / 0.12f));
                phase += 2f * Mathf.PI * frequency / MusicRate;
                Mix(l, r, first + n, Mathf.Sin(phase) * Mathf.Exp(-t * 19f) * gain, 0f);
            }
        }

        static void AddSnare(float[] l, float[] r, double start, float gain, int seed)
        {
            int first = Mathf.RoundToInt((float)(start * MusicRate));
            int count = Mathf.RoundToInt(0.13f * MusicRate);
            for (int n = 0; n < count && first + n < l.Length; n++)
            {
                float t = n / (float)MusicRate;
                float noise = Noise(n + seed * 193);
                float body = Mathf.Sin(2f * Mathf.PI * 185f * t) * 0.32f;
                Mix(l, r, first + n, (noise * 0.76f + body) * Mathf.Exp(-t * 27f) * gain, 0.08f);
            }
        }

        static void AddHat(float[] l, float[] r, double start, float gain, int seed)
        {
            int first = Mathf.RoundToInt((float)(start * MusicRate));
            int count = Mathf.RoundToInt(0.045f * MusicRate);
            float previous = 0f;
            for (int n = 0; n < count && first + n < l.Length; n++)
            {
                float t = n / (float)MusicRate;
                float noise = Noise(n + seed * 271);
                float high = noise - previous * 0.82f;
                previous = noise;
                Mix(l, r, first + n, high * Mathf.Exp(-t * 82f) * gain,
                    (seed & 1) == 0 ? -0.35f : 0.35f);
            }
        }

        static void Mix(float[] l, float[] r, int index, float sample, float pan)
        {
            if (index < 0 || index >= l.Length) return;
            float leftGain = Mathf.Sqrt((1f - Mathf.Clamp(pan, -1f, 1f)) * 0.5f);
            float rightGain = Mathf.Sqrt((1f + Mathf.Clamp(pan, -1f, 1f)) * 0.5f);
            l[index] += sample * leftGain;
            r[index] += sample * rightGain;
        }

        static void Normalize(float[] l, float[] r, float target)
        {
            float peak = 0f;
            for (int i = 0; i < l.Length; i++)
                peak = Mathf.Max(peak, Mathf.Abs(l[i]), Mathf.Abs(r[i]));
            if (peak < 0.00001f) return;
            float scale = target / peak;
            for (int i = 0; i < l.Length; i++)
            {
                l[i] = (float)Math.Tanh(l[i] * scale * 1.08f) / 1.08f;
                r[i] = (float)Math.Tanh(r[i] * scale * 1.08f) / 1.08f;
            }

            // An 8 ms zero crossing removes a PCM discontinuity at the WebGL loop boundary. It is
            // shorter than the kick transient, so the groove still lands immediately on beat one.
            int edge = Mathf.Min(Mathf.RoundToInt(MusicRate * 0.008f), l.Length / 2);
            for (int i = 0; i < edge; i++)
            {
                float fadeIn = i / (float)edge;
                float fadeOut = (edge - 1 - i) / (float)edge;
                l[i] *= fadeIn;
                r[i] *= fadeIn;
                int end = l.Length - edge + i;
                l[end] *= fadeOut;
                r[end] *= fadeOut;
            }
        }

        static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);
        static float Smooth(float value) => value * value * (3f - 2f * value);

        static float Noise(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                return (x / (float)uint.MaxValue) * 2f - 1f;
            }
        }

        static void WriteMono(string path, float[] data, int rate)
            => WriteWav(path, data, null, rate);

        static void WriteStereo(string path, float[] left, float[] right, int rate)
            => WriteWav(path, left, right, rate);

        static void WriteWav(string path, float[] left, float[] right, int rate)
        {
            bool stereo = right != null;
            int channels = stereo ? 2 : 1;
            int frames = left.Length;
            int bytes = frames * channels * 2;
            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write)))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + bytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(rate);
                writer.Write(rate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(bytes);
                for (int i = 0; i < frames; i++)
                {
                    writer.Write(ToPcm(left[i]));
                    if (stereo) writer.Write(ToPcm(right[i]));
                }
            }
        }

        static short ToPcm(float value)
            => (short)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * short.MaxValue);
    }
}
#endif
