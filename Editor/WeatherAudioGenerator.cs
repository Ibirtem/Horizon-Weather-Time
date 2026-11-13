#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Editor utility that procedurally synthesizes seamless looping WAV audio stems
    /// for exterior/interior rain, roof pattering, and dynamic wind.
    /// </summary>
    public class WeatherAudioGenerator : EditorWindow
    {
        public const string AUDIO_DIR = "Assets/Horizon Weather & Time/Audio";
        public const string RAIN_EXT_STEREO_PATH = AUDIO_DIR + "/Horizon_Rain_Exterior_Stereo.wav";
        public const string RAIN_EXT_MONO_PATH   = AUDIO_DIR + "/Horizon_Rain_Exterior_Mono.wav";
        public const string RAIN_INT_STEREO_PATH = AUDIO_DIR + "/Horizon_Rain_Interior_Stereo.wav";
        public const string RAIN_ROOF_MONO_PATH  = AUDIO_DIR + "/Horizon_Rain_Roof_Mono.wav";
        public const string WIND_BASE_STEREO_PATH = AUDIO_DIR + "/Horizon_Wind_Base_Stereo.wav";
        public const string WIND_GUST_STEREO_PATH = AUDIO_DIR + "/Horizon_Wind_Gust_Stereo.wav";

        private const int SAMPLE_RATE = 44100;
        private const float DURATION_SEC = 8.0f;
        private const float CROSSFADE_SEC = 1.0f;

        [MenuItem("Tools/Horizon/WeatherTime/Generate Weather Audio")]
        public static void ShowWindow()
        {
            GetWindow<WeatherAudioGenerator>("Weather Audio Gen");
        }

        private void OnGUI()
        {
            HorizonEditorUtils.DrawHorizonHeader("Weather Audio Generator", this);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Generates 6 seamless looping WAV stems (44.1 kHz, 16-bit PCM) for exterior rain, " +
                "muffled interior rain, roof pattering, and dual-layer wind.", MessageType.Info);

            if (GUILayout.Button("Generate All Weather Audio Loops", GUILayout.Height(35)))
            {
                GenerateAllAudio();
            }
        }

        /**
         * Generates all missing or requested procedural weather audio files and configures AudioImporter flags.
         */
        public static void GenerateAllAudio()
        {
            if (!Directory.Exists(AUDIO_DIR)) Directory.CreateDirectory(AUDIO_DIR);

            try
            {
                EditorUtility.DisplayProgressBar("Generating Weather Audio", "Synthesizing Exterior Rain...", 0.1f);
                GenerateRainExteriorStereo(RAIN_EXT_STEREO_PATH);
                GenerateRainExteriorMono(RAIN_EXT_MONO_PATH);

                EditorUtility.DisplayProgressBar("Generating Weather Audio", "Synthesizing Interior Muffled Rain...", 0.4f);
                GenerateRainInteriorStereo(RAIN_INT_STEREO_PATH);

                EditorUtility.DisplayProgressBar("Generating Weather Audio", "Synthesizing Roof Patter...", 0.65f);
                GenerateRainRoofMono(RAIN_ROOF_MONO_PATH);

                EditorUtility.DisplayProgressBar("Generating Weather Audio", "Synthesizing Wind Stems...", 0.85f);
                GenerateWindBaseStereo(WIND_BASE_STEREO_PATH);
                GenerateWindGustStereo(WIND_GUST_STEREO_PATH);

                AssetDatabase.Refresh();
                Debug.Log($"<b><color=#33FF33>[AUDIO GEN]</color></b> Generated 6 seamless weather audio loops in {AUDIO_DIR}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void GenerateRainExteriorStereo(string path)
        {
            int loopSamples = Mathf.RoundToInt(SAMPLE_RATE * DURATION_SEC);
            int fadeSamples = Mathf.RoundToInt(SAMPLE_RATE * CROSSFADE_SEC);
            int total = loopSamples + fadeSamples;

            float[] left = new float[total];
            float[] right = new float[total];

            System.Random rng = new System.Random(101);
            PinkNoise pinkC = new PinkNoise();
            PinkNoise pinkL = new PinkNoise();
            PinkNoise pinkR = new PinkNoise();
            Biquad hpfL = Biquad.HighPass(SAMPLE_RATE, 220f, 0.707f);
            Biquad hpfR = Biquad.HighPass(SAMPLE_RATE, 220f, 0.707f);
            Biquad lpfL = Biquad.LowPass(SAMPLE_RATE, 5800f, 0.65f);
            Biquad lpfR = Biquad.LowPass(SAMPLE_RATE, 5800f, 0.65f);

            for (int i = 0; i < total; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float mod = 0.90f
                    + 0.06f * Mathf.Sin(t * 2f * Mathf.PI / 3.2f)
                    + 0.04f * Mathf.Sin(t * 2f * Mathf.PI / 1.9f + 1.1f);

                float center = pinkC.Next(rng) * 0.25f;
                float rawL = center + pinkL.Next(rng) * 0.75f;
                float rawR = center + pinkR.Next(rng) * 0.75f;

                left[i] = lpfL.Process(hpfL.Process(rawL)) * 0.85f * mod;
                right[i] = lpfR.Process(hpfR.Process(rawR)) * 0.85f * mod;
            }

            // Dense non-tonal micro-droplet noise bursts
            AddNoiseRainImpacts(left, right, rng, total, 4500, 700f, 4800f, 0.0005f, 0.0018f, 0.95f, 0.30f);
            AddNoiseRainImpacts(left, right, rng, total, 450, 260f, 850f, 0.0015f, 0.0032f, 0.75f, 0.20f);
            SaveSeamlessWav(path, left, right, loopSamples, fadeSamples, isStereo: true);
        }

        private static void GenerateRainExteriorMono(string path)
        {
            int loopSamples = Mathf.RoundToInt(SAMPLE_RATE * DURATION_SEC);
            int fadeSamples = Mathf.RoundToInt(SAMPLE_RATE * CROSSFADE_SEC);
            int total = loopSamples + fadeSamples;

            float[] mono = new float[total];
            System.Random rng = new System.Random(202);
            PinkNoise pink = new PinkNoise();
            Biquad hpf = Biquad.HighPass(SAMPLE_RATE, 240f, 0.707f);
            Biquad lpf = Biquad.LowPass(SAMPLE_RATE, 5600f, 0.65f);

            for (int i = 0; i < total; i++)
            {
                mono[i] = lpf.Process(hpf.Process(pink.Next(rng))) * 0.85f;
            }

            AddNoiseRainImpacts(mono, null, rng, total, 4200, 700f, 4600f, 0.0005f, 0.0018f, 0.95f, 0.28f);
            AddNoiseRainImpacts(mono, null, rng, total, 400, 260f, 850f, 0.0015f, 0.0032f, 0.75f, 0.18f);
            SaveSeamlessWav(path, mono, null, loopSamples, fadeSamples, isStereo: false);
        }

        private static void GenerateRainInteriorStereo(string path)
        {
            int loopSamples = Mathf.RoundToInt(SAMPLE_RATE * DURATION_SEC);
            int fadeSamples = Mathf.RoundToInt(SAMPLE_RATE * CROSSFADE_SEC);
            int total = loopSamples + fadeSamples;

            float[] left = new float[total];
            float[] right = new float[total];

            System.Random rng = new System.Random(303);
            PinkNoise pinkC = new PinkNoise();
            PinkNoise pinkL = new PinkNoise();
            PinkNoise pinkR = new PinkNoise();

            for (int i = 0; i < total; i++)
            {
                float center = pinkC.Next(rng) * 0.35f;
                left[i] = center + pinkL.Next(rng) * 0.65f;
                right[i] = center + pinkR.Next(rng) * 0.65f;
            }

            AddNoiseRainImpacts(left, right, rng, total, 1800, 350f, 1600f, 0.0008f, 0.0025f, 0.8f, 0.25f);

            // Simulate wall transmission loss (-36 dB/oct low-pass)
            Biquad hpfL = Biquad.HighPass(SAMPLE_RATE, 55f, 0.707f);
            Biquad hpfR = Biquad.HighPass(SAMPLE_RATE, 55f, 0.707f);
            Biquad lpf1L = Biquad.LowPass(SAMPLE_RATE, 340f, 0.65f);
            Biquad lpf2L = Biquad.LowPass(SAMPLE_RATE, 340f, 0.65f);
            Biquad lpf3L = Biquad.LowPass(SAMPLE_RATE, 340f, 0.65f);
            Biquad lpf1R = Biquad.LowPass(SAMPLE_RATE, 340f, 0.65f);
            Biquad lpf2R = Biquad.LowPass(SAMPLE_RATE, 340f, 0.65f);
            Biquad lpf3R = Biquad.LowPass(SAMPLE_RATE, 340f, 0.65f);

            for (int i = 0; i < total; i++)
            {
                left[i] = lpf3L.Process(lpf2L.Process(lpf1L.Process(hpfL.Process(left[i])))) * 0.9f;
                right[i] = lpf3R.Process(lpf2R.Process(lpf1R.Process(hpfR.Process(right[i])))) * 0.9f;
            }

            SaveSeamlessWav(path, left, right, loopSamples, fadeSamples, isStereo: true);
        }

        private static void GenerateRainRoofMono(string path)
        {
            int loopSamples = Mathf.RoundToInt(SAMPLE_RATE * DURATION_SEC);
            int fadeSamples = Mathf.RoundToInt(SAMPLE_RATE * CROSSFADE_SEC);
            int total = loopSamples + fadeSamples;

            float[] mono = new float[total];
            System.Random rng = new System.Random(404);
            PinkNoise pink = new PinkNoise();
            Biquad hpf = Biquad.HighPass(SAMPLE_RATE, 110f, 0.707f);
            Biquad lpf = Biquad.LowPass(SAMPLE_RATE, 1900f, 0.707f);

            for (int i = 0; i < total; i++)
            {
                mono[i] = lpf.Process(hpf.Process(pink.Next(rng))) * 0.65f;
            }

            // Crisp tactile roof-surface noise impacts without tonal ringing
            AddNoiseRainImpacts(mono, null, rng, total, 3200, 320f, 1600f, 0.0010f, 0.0030f, 1.15f, 0.35f);
            SaveSeamlessWav(path, mono, null, loopSamples, fadeSamples, isStereo: false);
        }

        private static void GenerateWindBaseStereo(string path)
        {
            int loopSamples = Mathf.RoundToInt(SAMPLE_RATE * DURATION_SEC);
            int fadeSamples = Mathf.RoundToInt(SAMPLE_RATE * CROSSFADE_SEC);
            int total = loopSamples + fadeSamples;

            float[] left = new float[total];
            float[] right = new float[total];

            System.Random rng = new System.Random(505);
            PinkNoise pinkL = new PinkNoise();
            PinkNoise pinkR = new PinkNoise();
            Biquad hpfL = Biquad.HighPass(SAMPLE_RATE, 40f, 0.707f);
            Biquad hpfR = Biquad.HighPass(SAMPLE_RATE, 40f, 0.707f);
            Biquad lpf1L = Biquad.LowPass(SAMPLE_RATE, 260f, 0.707f);
            Biquad lpf2L = Biquad.LowPass(SAMPLE_RATE, 260f, 0.707f);
            Biquad lpf1R = Biquad.LowPass(SAMPLE_RATE, 260f, 0.707f);
            Biquad lpf2R = Biquad.LowPass(SAMPLE_RATE, 260f, 0.707f);

            for (int i = 0; i < total; i++)
            {
                left[i] = lpf2L.Process(lpf1L.Process(hpfL.Process(pinkL.Next(rng)))) * 0.85f;
                right[i] = lpf2R.Process(lpf1R.Process(hpfR.Process(pinkR.Next(rng)))) * 0.85f;
            }

            SaveSeamlessWav(path, left, right, loopSamples, fadeSamples, isStereo: true);
        }

        private static void GenerateWindGustStereo(string path)
        {
            int loopSamples = Mathf.RoundToInt(SAMPLE_RATE * DURATION_SEC);
            int fadeSamples = Mathf.RoundToInt(SAMPLE_RATE * CROSSFADE_SEC);
            int total = loopSamples + fadeSamples;

            float[] left = new float[total];
            float[] right = new float[total];

            System.Random rng = new System.Random(606);
            PinkNoise pinkL = new PinkNoise();
            PinkNoise pinkR = new PinkNoise();

            // Low-Q resonant rush (avoids flute-like whistling)
            Biquad bpfLowL = Biquad.BandPass(SAMPLE_RATE, 170f, 1.6f);
            Biquad bpfLowR = Biquad.BandPass(SAMPLE_RATE, 178f, 1.6f);
            Biquad bpfMidL = Biquad.BandPass(SAMPLE_RATE, 340f, 2.1f);
            Biquad bpfMidR = Biquad.BandPass(SAMPLE_RATE, 355f, 2.1f);

            for (int i = 0; i < total; i++)
            {
                float nL = pinkL.Next(rng);
                float nR = pinkR.Next(rng);

                float phase = (float)i / SAMPLE_RATE;
                float swell = 0.65f + 0.35f * Mathf.Sin(phase * 2f * Mathf.PI / 4.0f);

                left[i] = (bpfLowL.Process(nL) * 0.65f + bpfMidL.Process(nL) * 0.35f) * swell;
                right[i] = (bpfLowR.Process(nR) * 0.65f + bpfMidR.Process(nR) * 0.35f) * swell;
            }

            SaveSeamlessWav(path, left, right, loopSamples, fadeSamples, isStereo: true);
        }

        /**
         * Injects ultra-short filtered white-noise bursts to synthesize organic raindrop impacts
         * without pitched sine-wave ringing.
         */
        private static void AddNoiseRainImpacts(
            float[] left, float[] right, System.Random rng,
            int totalSamples, int count, float minFreq, float maxFreq,
            float minDecaySec, float maxDecaySec, float q, float amp)
        {
            for (int d = 0; d < count; d++)
            {
                float decay = minDecaySec + (float)rng.NextDouble() * (maxDecaySec - minDecaySec);
                int len = Mathf.Clamp(Mathf.RoundToInt(SAMPLE_RATE * decay * 5f), 16, 512);
                int start = rng.Next(0, totalSamples - len - 1);

                float freqRatio = Mathf.Pow((float)rng.NextDouble(), 1.4f);
                float freq = minFreq + freqRatio * (maxFreq - minFreq);
                Biquad bpf = Biquad.BandPass(SAMPLE_RATE, freq, q);

                float pan = right != null ? (float)rng.NextDouble() : 0.5f;
                float sizeWeight = 0.15f + 0.85f * Mathf.Pow((float)rng.NextDouble(), 2.5f);
                float gainL = Mathf.Cos(pan * Mathf.PI * 0.5f) * amp * sizeWeight;
                float gainR = Mathf.Sin(pan * Mathf.PI * 0.5f) * amp * sizeWeight;

                for (int s = 0; s < len; s++)
                {
                    float t = (float)s / SAMPLE_RATE;
                    float env = Mathf.Exp(-t / decay);
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float val = bpf.Process(white * env);
                    left[start + s] += val * gainL;
                    if (right != null) right[start + s] += val * gainR;
                }
            }
        }

        private static void AddRainDroplets(
            float[] left, float[] right, System.Random rng,
            int totalSamples, int count, float minFreq, float maxFreq, float decaySec, float amp)
        {
            int len = Mathf.Max(16, Mathf.RoundToInt(SAMPLE_RATE * decaySec * 4f));
            for (int d = 0; d < count; d++)
            {
                int start = rng.Next(0, totalSamples - len - 1);
                float freq = minFreq + (float)rng.NextDouble() * (maxFreq - minFreq);
                float pan = right != null ? (float)rng.NextDouble() : 0.5f;
                float gainL = Mathf.Cos(pan * Mathf.PI * 0.5f) * amp;
                float gainR = Mathf.Sin(pan * Mathf.PI * 0.5f) * amp;
                float dropScale = 0.4f + 0.6f * (float)rng.NextDouble();

                for (int s = 0; s < len; s++)
                {
                    float t = (float)s / SAMPLE_RATE;
                    float env = Mathf.Exp(-t / decaySec);
                    float val = Mathf.Sin(2f * Mathf.PI * freq * t) * env * dropScale;
                    left[start + s] += val * gainL;
                    if (right != null) right[start + s] += val * gainR;
                }
            }
        }

        /**
         * Performs an equal-power cosine/sine crossfade on the overlap tail and writes a 16-bit PCM WAV file.
         */
        private static void SaveSeamlessWav(string path, float[] left, float[] right, int loopSamples, int fadeSamples, bool isStereo)
        {
            float[] outL = new float[loopSamples];
            float[] outR = isStereo ? new float[loopSamples] : null;

            float peak = 0.001f;
            for (int i = 0; i < loopSamples; i++)
            {
                if (i < fadeSamples)
                {
                    float t = (float)i / fadeSamples;
                    float wStart = Mathf.Sin(t * Mathf.PI * 0.5f);
                    float wEnd = Mathf.Cos(t * Mathf.PI * 0.5f);
                    outL[i] = left[i] * wStart + left[loopSamples + i] * wEnd;
                    if (isStereo) outR[i] = right[i] * wStart + right[loopSamples + i] * wEnd;
                }
                else
                {
                    outL[i] = left[i];
                    if (isStereo) outR[i] = right[i];
                }

                float absL = Mathf.Abs(outL[i]);
                if (absL > peak) peak = absL;
                if (isStereo)
                {
                    float absR = Mathf.Abs(outR[i]);
                    if (absR > peak) peak = absR;
                }
            }

            float norm = Mathf.Min(1.5f, 0.85f / peak);
            int channels = isStereo ? 2 : 1;
            int dataBytes = loopSamples * channels * 2;

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)channels);
                bw.Write(SAMPLE_RATE);
                bw.Write(SAMPLE_RATE * channels * 2);
                bw.Write((short)(channels * 2));
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(dataBytes);

                for (int i = 0; i < loopSamples; i++)
                {
                    short sL = (short)(Mathf.Clamp(outL[i] * norm, -1f, 1f) * 32767f);
                    bw.Write(sL);
                    if (isStereo)
                    {
                        short sR = (short)(Mathf.Clamp(outR[i] * norm, -1f, 1f) * 32767f);
                        bw.Write(sR);
                    }
                }
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null)
            {
                importer.forceToMono = !isStereo;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        private class PinkNoise
        {
            private float _b0, _b1, _b2, _b3, _b4, _b5, _b6;

            public float Next(System.Random rng)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                _b0 = 0.99886f * _b0 + white * 0.0555179f;
                _b1 = 0.99332f * _b1 + white * 0.0750759f;
                _b2 = 0.96900f * _b2 + white * 0.1538520f;
                _b3 = 0.86650f * _b3 + white * 0.3104856f;
                _b4 = 0.55000f * _b4 + white * 0.5329522f;
                _b5 = -0.7616f * _b5 - white * 0.0168980f;
                float pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362f;
                _b6 = white * 0.115926f;
                return pink * 0.11f;
            }
        }

        private struct Biquad
        {
            private float _a1, _a2, _b0, _b1, _b2, _x1, _x2, _y1, _y2;

            public static Biquad LowPass(float fs, float fc, float q)
            {
                float w0 = 2f * Mathf.PI * fc / fs;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float cosW = Mathf.Cos(w0);
                float a0 = 1f + alpha;
                return new Biquad
                {
                    _b0 = ((1f - cosW) * 0.5f) / a0,
                    _b1 = (1f - cosW) / a0,
                    _b2 = ((1f - cosW) * 0.5f) / a0,
                    _a1 = (-2f * cosW) / a0,
                    _a2 = (1f - alpha) / a0
                };
            }

            public static Biquad HighPass(float fs, float fc, float q)
            {
                float w0 = 2f * Mathf.PI * fc / fs;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float cosW = Mathf.Cos(w0);
                float a0 = 1f + alpha;
                return new Biquad
                {
                    _b0 = ((1f + cosW) * 0.5f) / a0,
                    _b1 = -(1f + cosW) / a0,
                    _b2 = ((1f + cosW) * 0.5f) / a0,
                    _a1 = (-2f * cosW) / a0,
                    _a2 = (1f - alpha) / a0
                };
            }

            public static Biquad BandPass(float fs, float fc, float q)
            {
                float w0 = 2f * Mathf.PI * fc / fs;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float cosW = Mathf.Cos(w0);
                float a0 = 1f + alpha;
                return new Biquad
                {
                    _b0 = alpha / a0,
                    _b1 = 0f,
                    _b2 = -alpha / a0,
                    _a1 = (-2f * cosW) / a0,
                    _a2 = (1f - alpha) / a0
                };
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }
        }
    }
}
#endif