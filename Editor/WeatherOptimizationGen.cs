using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Generates Advanced Weather Maps & Blue Noise textures for volumetric cloud rendering.
    /// </summary>
    public class WeatherOptimizationGen : EditorWindow
    {
        public const string DEFAULT_WEATHER_MAP_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_WeatherMap_Gen.png";
        public const string DEFAULT_BLUE_NOISE_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_BlueNoise_Gen.png";
        public const string DEFAULT_CLOUD_NOISE_3D_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_CloudNoise3D_Gen.asset";
        public const string DEFAULT_CIRRUS_NOISE_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_CirrusNoise_Gen.png";

        [MenuItem("Tools/Horizon/WeatherTime/Generate Optimization Maps")]
        public static void ShowWindow()
        {
            GetWindow<WeatherOptimizationGen>("Optimization Gen");
        }

        private void OnGUI()
        {
            GUILayout.Label("Cloud Maps Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Generates a channel-packed Texture for Volumetric Control.\n\n" +
                                    "R: Coverage (Perlin-Worley + Domain Warp)\n" +
                                    "G: Cloud Type (Stratus -> Cumulonimbus)\n" +
                                    "B: Erosion Mask (High-frequency detail)\n" +
                                    "A: Density (Local density variation)", MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("Generate Weather Map (RGBA)", GUILayout.Height(30)))
            {
                GenerateWeatherMap(DEFAULT_WEATHER_MAP_PATH);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate 3D Cloud Noise (Takes ~15-30 sec)", GUILayout.Height(30)))
            {
                Generate3DCloudNoise(DEFAULT_CLOUD_NOISE_3D_PATH);
            }

            if (GUILayout.Button("Generate Blue Noise (Multi-Pass)", GUILayout.Height(30)))
            {
                GenerateBlueNoise(DEFAULT_BLUE_NOISE_PATH);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Cirrus Noise (Anisotropic)", GUILayout.Height(30)))
            {
                GenerateCirrusTexture(DEFAULT_CIRRUS_NOISE_PATH);
            }
        }

        #region Noise Math

        /// <summary>
        /// Remaps a value from a specific range [low, high] to [0, 1].
        /// </summary>
        private static float Remap01(float value, float low, float high) => Mathf.Clamp01((value - low) / (high - low));

        /// <summary>
        /// Generates tileable Perlin noise. Uses 4-corner sampling to ensure seamless wrapping.
        /// </summary>
        private static float TileablePerlin(float u, float v, float scale, float ox, float oy)
        {
            float x = u * scale + ox;
            float y = v * scale + oy;

            float n00 = Mathf.PerlinNoise(x, y);
            float n10 = Mathf.PerlinNoise(x - scale, y);
            float n01 = Mathf.PerlinNoise(x, y - scale);
            float n11 = Mathf.PerlinNoise(x - scale, y - scale);

            float su = u * u * (3f - 2f * u);
            float sv = v * v * (3f - 2f * v);

            return Mathf.Lerp(Mathf.Lerp(n00, n10, su), Mathf.Lerp(n01, n11, su), sv);
        }

        /// <summary>
        /// Fractal Brownian Motion (FBM) using Tileable Perlin noise.
        /// </summary>
        private static float TileableFBM(float u, float v, float baseScale, float ox, float oy, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float val = 0f, amp = 1f, maxVal = 0f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                val += amp * TileablePerlin(u, v, baseScale * freq, ox + i * 17.3f, oy + i * 31.7f);
                maxVal += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return val / maxVal;
        }

        /// <summary>
        /// Deterministic pseudo-random hash for Worley points.
        /// </summary>
        private static float Hash2D(int x, int y, float seed)
        {
            float n = Mathf.Sin(x * 127.1f + y * 311.7f + seed) * 43758.5453f;
            return n - Mathf.Floor(n);
        }

        /// <summary>
        /// Tileable Worley (Cellular) noise. Returns the F1 distance to the nearest point.
        /// Creates "bubbly" or "cauliflower-like" shapes.
        /// </summary>
        private static float TileableWorley(float u, float v, int cells, float seed)
        {
            float fu = u * cells;
            float fv = v * cells;

            int cx = Mathf.FloorToInt(fu);
            int cy = Mathf.FloorToInt(fv);

            float fx = fu - cx;
            float fy = fv - cy;

            float minDist = 10f;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = ((cx + dx) % cells + cells) % cells;
                    int ny = ((cy + dy) % cells + cells) % cells;

                    float px = Hash2D(nx, ny, seed);
                    float py = Hash2D(nx, ny, seed + 127.1f);

                    float distX = (dx + px) - fx;
                    float distY = (dy + py) - fy;

                    minDist = Mathf.Min(minDist, distX * distX + distY * distY);
                }
            }
            return Mathf.Sqrt(minDist);
        }

        /// <summary>
        /// Inverted Worley noise stacked in octaves. Crucial for the "billowy" cloud look.
        /// </summary>
        private static float WorleyFBM(float u, float v, int baseCells, float seed, int octaves)
        {
            float val = 0f, amp = 1f, maxVal = 0f;
            int cells = baseCells;

            for (int i = 0; i < octaves; i++)
            {
                float w = 1f - TileableWorley(u, v, cells, seed + i * 43.7f);
                val += amp * w;
                maxVal += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return val / maxVal;
        }

        /// <summary>
        /// Domain Warping: Distorts the UV coordinates of FBM with another FBM layer.
        /// </summary>
        private static float DomainWarpedFBM(float u, float v, float scale, float ox, float oy, int octaves, float warpStrength)
        {
            float wu = TileableFBM(u, v, scale * 0.7f, ox + 50f, oy + 50f, 3);
            float wv = TileableFBM(u, v, scale * 0.7f, ox + 100f, oy + 100f, 3);

            float nu = u + (wu - 0.5f) * warpStrength;
            float nv = v + (wv - 0.5f) * warpStrength;

            nu -= Mathf.Floor(nu);
            nv -= Mathf.Floor(nv);

            return TileableFBM(nu, nv, scale, ox, oy, octaves);
        }

        /// <summary>
        /// Generates tileable Perlin noise with independent X and Y scales (Anisotropic).
        /// </summary>
        private static float TileablePerlinAniso(float u, float v, float scaleX, float scaleY, float ox, float oy)
        {
            float x = u * scaleX + ox;
            float y = v * scaleY + oy;

            float n00 = Mathf.PerlinNoise(x, y);
            float n10 = Mathf.PerlinNoise(x - scaleX, y);
            float n01 = Mathf.PerlinNoise(x, y - scaleY);
            float n11 = Mathf.PerlinNoise(x - scaleX, y - scaleY);

            float su = u * u * (3f - 2f * u);
            float sv = v * v * (3f - 2f * v);

            return Mathf.Lerp(Mathf.Lerp(n00, n10, su), Mathf.Lerp(n01, n11, su), sv);
        }

        private static float TileableFBMAniso(float u, float v, float scaleX, float scaleY, float ox, float oy, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float val = 0f, amp = 1f, maxVal = 0f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                val += amp * TileablePerlinAniso(u, v, scaleX * freq, scaleY * freq, ox + i * 17.3f, oy + i * 31.7f);
                maxVal += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return val / maxVal;
        }

        private static float DomainWarpedFBMAniso(float u, float v, float scaleX, float scaleY, float ox, float oy, int octaves, float warpStrength)
        {
            float wu = TileableFBMAniso(u, v, scaleX * 0.7f, scaleY * 0.7f, ox + 50f, oy + 50f, 3);
            float wv = TileableFBMAniso(u, v, scaleX * 0.7f, scaleY * 0.7f, ox + 100f, oy + 100f, 3);

            float nu = u + (wu - 0.5f) * warpStrength;
            float nv = v + (wv - 0.5f) * warpStrength;

            nu -= Mathf.Floor(nu);
            nv -= Mathf.Floor(nv);

            return TileableFBMAniso(nu, nv, scaleX, scaleY, ox, oy, octaves);
        }

        private static float TileableRidgeAniso(float u, float v, float scaleX, float scaleY, float ox, float oy)
        {
            float n = TileablePerlinAniso(u, v, scaleX, scaleY, ox, oy);
            return 1.0f - Mathf.Abs(n * 2f - 1f);
        }

        /// <summary>
        /// Ridge FBM with previous-octave feedback (Mussgrave technique).
        /// </summary>
        private static float TileableRidgeFBMAniso(float u, float v, float scaleX, float scaleY,
            float ox, float oy, int octaves)
        {
            float val = 0f, amp = 1f, maxVal = 0f;
            float freqX = 1f, freqY = 1f;
            float prev = 1.0f;

            for (int i = 0; i < octaves; i++)
            {
                float r = TileableRidgeAniso(u, v,
                    scaleX * freqX, scaleY * freqY,
                    ox + i * 17.3f, oy + i * 31.7f);
                r *= r;
                val += amp * r * prev;
                prev = Mathf.Clamp01(r);
                maxVal += amp;
                amp *= 0.5f;
                freqX *= 2.0f;
                freqY *= 2.2f;
            }
            return val / maxVal;
        }

        /// <summary>
        /// Rotates UV coordinates around center (0.5, 0.5) by angle in radians.
        /// </summary>
        private static void RotateUV(float u, float v, float angle, out float ru, out float rv)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            ru = u * cos - v * sin;
            rv = u * sin + v * cos;
        }

        /// <summary>
        /// Generates an anisotropic, multi-directional noise pattern to simulate high-altitude cirrus filaments.
        /// Blends multiple domain-warped ridge fractals to create intersecting wind-sheared structures.
        /// </summary>
        /// <param name="octaves">Number of noise iterations.</param>
        /// <param name="warpStrength">Intensity of the UV distortion simulating atmospheric turbulence.</param>
        private static float MultiDirectionalCirrusCurl(float u, float v, float scaleX, float scaleY,
            float ox, float oy, int octaves, float warpStrength)
        {
            float l0 = CirrusWarpedRidgeFBM(u, v, scaleX, scaleY,
                ox, oy, octaves, warpStrength);

            float l1 = CirrusWarpedRidgeFBM(u, v, scaleY, scaleX,
                ox + 173.7f, oy + 173.7f, octaves, warpStrength);

            float l2 = CirrusWarpedRidgeFBM(v, u, scaleX * 0.8f, scaleY * 1.2f,
                ox + 347.4f, oy + 347.4f, octaves, warpStrength * 1.15f);

            float l3 = CirrusWarpedRidgeFBM(1.0f - v, u, scaleY * 1.1f, scaleX * 0.85f,
                ox + 521.1f, oy + 521.1f, octaves, warpStrength * 0.9f);

            float z1 = TileableFBM(u, v, 2.5f, ox + 300f, oy + 300f, 2);
            float z2 = TileableFBM(u, v, 2.5f, ox + 400f, oy + 400f, 2);
            float z3 = TileableFBM(u, v, 2.0f, ox + 500f, oy + 500f, 2);

            float w0 = Mathf.Clamp01(z1 * z3 * 3f);
            float w1 = Mathf.Clamp01((1f - z1) * z2 * 2.5f);
            float w2 = Mathf.Clamp01(z1 * (1f - z2) * 2.5f);
            float w3 = Mathf.Clamp01((1f - z1) * (1f - z3) * 3f);

            float wSum = w0 + w1 + w2 + w3 + 0.001f;

            return (l0 * w0 + l1 * w1 + l2 * w2 + l3 * w3) / wSum;
        }

        /// <summary>
        /// Double domain warping + ridge FBM = curling filaments.
        /// </summary>
        private static float CirrusWarpedRidgeFBM(float u, float v, float scaleX, float scaleY,
            float ox, float oy, int octaves, float warpStrength)
        {
            float w1u = TileableFBM(u, v, 3f, ox + 50f, oy + 50f, 3);
            float w1v = TileableFBM(u, v, 3f, ox + 100f, oy + 100f, 3);

            float nu = u + (w1u - 0.5f) * warpStrength;
            float nv = v + (w1v - 0.5f) * warpStrength;

            nu -= Mathf.Floor(nu);
            nv -= Mathf.Floor(nv);

            float w2u = TileableFBM(nu, nv, 5f, ox + 150f, oy + 150f, 3);
            float w2v = TileableFBM(nu, nv, 5f, ox + 200f, oy + 200f, 3);

            nu += (w2u - 0.5f) * warpStrength * 0.5f;
            nv += (w2v - 0.5f) * warpStrength * 0.5f;

            nu -= Mathf.Floor(nu);
            nv -= Mathf.Floor(nv);

            return TileableRidgeFBMAniso(nu, nv, scaleX, scaleY, ox, oy, octaves);
        }

        #endregion

        #region Generators

        /// <summary>
        /// Generates the master Weather Map (RGBA).
        /// </summary>
        public static Texture2D GenerateWeatherMap(string path)
        {
            int res = 512;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[res * res];
            float ox = Random.value * 100f;
            float oy = Random.value * 100f;

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / res;
                    float v = (float)y / res;

                    // R: Coverage
                    float perlin = DomainWarpedFBM(u, v, 6f, ox, oy, 6, 0.10f);

                    float worley = WorleyFBM(u, v, 10, ox, 3);

                    float coverage = Mathf.Lerp(perlin, perlin * worley, 0.55f);
                    coverage = Remap01(coverage, 0.12f, 0.72f);

                    // G: Cloud Type
                    float cloudType = TileableFBM(u, v, 5f, -ox, -oy, 3);
                    cloudType = Mathf.Lerp(0.2f, 0.8f, cloudType);

                    // B: Erosion Mask
                    float erosionMask = TileableFBM(u, v, 16f, ox * 2f, oy * 2f, 4);

                    // A: Local Density
                    float densVar = TileableFBM(u, v, 8f, ox + 200f, oy + 200f, 3);
                    float density = Mathf.Lerp(0.4f, 1.0f, coverage * 0.4f + densVar * 0.6f);

                    pixels[y * res + x] = new Color(coverage, cloudType, erosionMask, density);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return SaveTexture(tex, path, true);
        }

        /// <summary>
        /// Generates Blue Noise for dithering. Uses a multi-pass high-pass filter algorithm.
        /// </summary>
        public static Texture2D GenerateBlueNoise(string path)
        {
            int res = 64;
            Texture2D tex = new Texture2D(res, res, TextureFormat.R8, false);
            float[] current = new float[res * res];
            float[] buffer = new float[res * res];

            for (int i = 0; i < current.Length; i++) current[i] = Random.value;

            for (int iter = 0; iter < 4; iter++)
            {
                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float blurred = 0f;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = (x + dx + res) % res;
                                int ny = (y + dy + res) % res;
                                blurred += current[ny * res + nx];
                            }
                        }
                        blurred /= 9f;
                        buffer[y * res + x] = Mathf.Clamp01((current[y * res + x] - blurred) + 0.5f);
                    }
                }
                var tmp = current;
                current = buffer;
                buffer = tmp;
            }

            Color[] colors = new Color[res * res];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color(current[i], current[i], current[i], 1f);

            tex.SetPixels(colors);
            tex.Apply();
            return SaveTexture(tex, path, false);
        }

        /// <summary>
        /// Generates a 128x128x128 3D texture. R = Perlin-Worley, GBA = Worley (different frequencies).
        /// </summary>
        public static void Generate3DCloudNoise(string path)
        {
            int res = 128;
            int totalVoxels = res * res * res;
            Texture3D tex = new Texture3D(res, res, res, TextureFormat.RGBA32, true);
            Color[] pixels = new Color[totalVoxels];

            float[] rawPerlin1 = new float[totalVoxels];
            float[] rawPerlin2 = new float[totalVoxels];
            float[] rawPerlin3 = new float[totalVoxels];

            float[] rawW4 = new float[totalVoxels];
            float[] rawW8_carve = new float[totalVoxels];
            float[] rawW16_carve = new float[totalVoxels];

            float[] rawW8_erosion = new float[totalVoxels];
            float[] rawW16_erosion = new float[totalVoxels];
            float[] rawW24_erosion = new float[totalVoxels];

            float minP1 = float.MaxValue, maxP1 = float.MinValue;
            float minP2 = float.MaxValue, maxP2 = float.MinValue;
            float minP3 = float.MaxValue, maxP3 = float.MinValue;

            float minW4 = float.MaxValue, maxW4 = float.MinValue;
            float minW8c = float.MaxValue, maxW8c = float.MinValue;
            float minW16c = float.MaxValue, maxW16c = float.MinValue;

            float minW8e = float.MaxValue, maxW8e = float.MinValue;
            float minW16e = float.MaxValue, maxW16e = float.MinValue;
            float minW24e = float.MaxValue, maxW24e = float.MinValue;

            try
            {
                // =====================================================
                //  PASS 1: Generate raw noise values
                // =====================================================
                for (int z = 0; z < res; z++)
                {
                    EditorUtility.DisplayProgressBar("Generating 3D Cloud Noise",
                        $"Pass 1/2: Computing noise... slice {z + 1}/{res}",
                        (float)z / (res * 2));

                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int idx = x + y * res + z * res * res;
                            float u = (float)x / res;
                            float v = (float)y / res;
                            float w = (float)z / res;

                            // --- Perlin FBM octaves (3 octaves, periods 5, 10, 20) ---
                            float p1 = TileablePerlin3D(u, v, w, 5);
                            float p2 = TileablePerlin3D(u, v, w, 10);
                            float p3 = TileablePerlin3D(u, v, w, 20);

                            rawPerlin1[idx] = p1;
                            rawPerlin2[idx] = p2;
                            rawPerlin3[idx] = p3;

                            if (p1 < minP1) minP1 = p1; if (p1 > maxP1) maxP1 = p1;
                            if (p2 < minP2) minP2 = p2; if (p2 > maxP2) maxP2 = p2;
                            if (p3 < minP3) minP3 = p3; if (p3 > maxP3) maxP3 = p3;

                            // --- Worley for R-channel carving (3 octaves) ---
                            float worley4 = TileableWorley3D(u, v, w, 4, 111);
                            float worley8c = TileableWorley3D(u, v, w, 8, 222);
                            float worley16c = TileableWorley3D(u, v, w, 16, 277);

                            rawW4[idx] = worley4;
                            rawW8_carve[idx] = worley8c;
                            rawW16_carve[idx] = worley16c;

                            if (worley4 < minW4) minW4 = worley4; if (worley4 > maxW4) maxW4 = worley4;
                            if (worley8c < minW8c) minW8c = worley8c; if (worley8c > maxW8c) maxW8c = worley8c;
                            if (worley16c < minW16c) minW16c = worley16c; if (worley16c > maxW16c) maxW16c = worley16c;

                            // --- Worley for erosion G, B, A channels ---
                            float worley8e = TileableWorley3D(u, v, w, 8, 333);
                            float worley16e = TileableWorley3D(u, v, w, 16, 444);

                            float curlAmount = 0.04f;
                            float cu = u + (p1 - 0.5f) * curlAmount;
                            float cv = v + (p2 - 0.5f) * curlAmount;
                            float cw = w + (p3 - 0.5f) * curlAmount;
                            cu -= Mathf.Floor(cu);
                            cv -= Mathf.Floor(cv);
                            cw -= Mathf.Floor(cw);
                            float worley24e = TileableWorley3D(cu, cv, cw, 24, 555);

                            rawW8_erosion[idx] = worley8e;
                            rawW16_erosion[idx] = worley16e;
                            rawW24_erosion[idx] = worley24e;

                            if (worley8e < minW8e) minW8e = worley8e; if (worley8e > maxW8e) maxW8e = worley8e;
                            if (worley16e < minW16e) minW16e = worley16e; if (worley16e > maxW16e) maxW16e = worley16e;
                            if (worley24e < minW24e) minW24e = worley24e; if (worley24e > maxW24e) maxW24e = worley24e;
                        }
                    }
                }

                // =====================================================
                //  PASS 2: Normalize and compose channels
                // =====================================================
                for (int z = 0; z < res; z++)
                {
                    EditorUtility.DisplayProgressBar("Generating 3D Cloud Noise",
                        $"Pass 2/2: Normalizing & compositing... slice {z + 1}/{res}",
                        0.5f + (float)z / (res * 2));

                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int idx = x + y * res + z * res * res;

                            float p1 = Normalize(rawPerlin1[idx], minP1, maxP1);
                            float p2 = Normalize(rawPerlin2[idx], minP2, maxP2);
                            float p3 = Normalize(rawPerlin3[idx], minP3, maxP3);

                            float w4 = Normalize(rawW4[idx], minW4, maxW4);
                            float w8c = Normalize(rawW8_carve[idx], minW8c, maxW8c);
                            float w16c = Normalize(rawW16_carve[idx], minW16c, maxW16c);

                            float w8e = Normalize(rawW8_erosion[idx], minW8e, maxW8e);
                            float w16e = Normalize(rawW16_erosion[idx], minW16e, maxW16e);
                            float w24e = Normalize(rawW24_erosion[idx], minW24e, maxW24e);

                            // ============================================
                            //  R CHANNEL: Perlin-Worley base shape
                            // ============================================

                            float perlinFBM = p1 * 0.55f + p2 * 0.30f + p3 * 0.15f;

                            float invW4 = 1.0f - w4;
                            float invW8c = 1.0f - w8c;
                            float invW16c = 1.0f - w16c;

                            float worleyCarve = invW4 * 0.50f + invW8c * 0.30f + invW16c * 0.20f;

                            float r = RemapClamped(perlinFBM, 1.0f - worleyCarve, 1.0f, 0.0f, 1.0f);

                            // ============================================
                            //  G CHANNEL: Medium erosion (8 cells)
                            // ============================================
                            float g = 1.0f - w8e;

                            // ============================================
                            //  B CHANNEL: Fine erosion (16 cells)
                            // ============================================
                            float b = 1.0f - w16e;

                            // ============================================
                            //  A CHANNEL: Micro detail / wisp (24 cells, curl-distorted)
                            // ============================================
                            float a = 1.0f - w24e;

                            pixels[idx] = new Color(r, g, b, a);
                        }
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply(true);

                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(tex, path);
                AssetDatabase.SaveAssets();

                Debug.Log($"<b><color=#33FF33>[CloudNoise]</color></b> 3D texture saved: {path}\n" +
                        $"  R: Perlin FBM (3 oct) × Worley carve (3 oct) → fractal cloud shapes\n" +
                        $"  G: Inv Worley 8 cells → medium erosion / overcast shape\n" +
                        $"  B: Inv Worley 16 cells → fine erosion\n" +
                        $"  A: Inv Worley 24 cells (curl-distorted) → micro detail / wisps");

                EditorGUIUtility.PingObject(tex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Normalizes a value from [min, max] to [0, 1].
        /// </summary>
        private static float Normalize(float value, float min, float max)
        {
            if (Mathf.Approximately(max, min)) return 0.5f;
            return Mathf.Clamp01((value - min) / (max - min));
        }

        private static float RemapClamped(float value, float originalMin, float originalMax, float newMin, float newMax)
        {
            float t = Mathf.Clamp01((value - originalMin) / (originalMax - originalMin));
            return Mathf.Lerp(newMin, newMax, t);
        }

        private static Vector3 Hash3D(int x, int y, int z, int seed)
        {
            uint n1 = MurmurHash((uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed));
            uint n2 = MurmurHash(n1 ^ 0xDEADBEEF);
            uint n3 = MurmurHash(n2 ^ 0xCAFEBABE);

            return new Vector3(
                (n1 & 0xFFFF) / 65535f,
                (n2 & 0xFFFF) / 65535f,
                (n3 & 0xFFFF) / 65535f
            );
        }

        private static uint MurmurHash(uint n)
        {
            n = (n ^ (n >> 16)) * 0x85ebca6b;
            n = (n ^ (n >> 13)) * 0xc2b2ae35;
            return n ^ (n >> 16);
        }

        private static float TileableWorley3D(float x, float y, float z, int cells, int seed)
        {
            float px = x * cells, py = y * cells, pz = z * cells;
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py), cz = Mathf.FloorToInt(pz);
            float fx = px - cx, fy = py - cy, fz = pz - cz;
            float minDist = 10f;

            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = (cx + dx) % cells; if (nx < 0) nx += cells;
                        int ny = (cy + dy) % cells; if (ny < 0) ny += cells;
                        int nz = (cz + dz) % cells; if (nz < 0) nz += cells;

                        Vector3 rand = Hash3D(nx, ny, nz, seed);
                        float distX = dx + rand.x - fx;
                        float distY = dy + rand.y - fy;
                        float distZ = dz + rand.z - fz;

                        float distSq = distX * distX + distY * distY + distZ * distZ;
                        if (distSq < minDist) minDist = distSq;
                    }
            return Mathf.Sqrt(minDist);
        }

        private static float TileablePerlin3D(float x, float y, float z, int period)
        {
            float px = x * period, py = y * period, pz = z * period;
            int X = Mathf.FloorToInt(px), Y = Mathf.FloorToInt(py), Z = Mathf.FloorToInt(pz);
            float u = px - X, v = py - Y, w = pz - Z;

            float u5 = u * u * u * (u * (u * 6 - 15) + 10);
            float v5 = v * v * v * (v * (v * 6 - 15) + 10);
            float w5 = w * w * w * (w * (w * 6 - 15) + 10);

            int x0 = X % period; if (x0 < 0) x0 += period;
            int y0 = Y % period; if (y0 < 0) y0 += period;
            int z0 = Z % period; if (z0 < 0) z0 += period;
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;
            int z1 = (z0 + 1) % period;

            float n000 = Grad3D(HashInt(x0, y0, z0), u, v, w);
            float n100 = Grad3D(HashInt(x1, y0, z0), u - 1, v, w);
            float n010 = Grad3D(HashInt(x0, y1, z0), u, v - 1, w);
            float n110 = Grad3D(HashInt(x1, y1, z0), u - 1, v - 1, w);
            float n001 = Grad3D(HashInt(x0, y0, z1), u, v, w - 1);
            float n101 = Grad3D(HashInt(x1, y0, z1), u - 1, v, w - 1);
            float n011 = Grad3D(HashInt(x0, y1, z1), u, v - 1, w - 1);
            float n111 = Grad3D(HashInt(x1, y1, z1), u - 1, v - 1, w - 1);

            float nx00 = Mathf.Lerp(n000, n100, u5);
            float nx10 = Mathf.Lerp(n010, n110, u5);
            float nx01 = Mathf.Lerp(n001, n101, u5);
            float nx11 = Mathf.Lerp(n011, n111, u5);
            float nxy0 = Mathf.Lerp(nx00, nx10, v5);
            float nxy1 = Mathf.Lerp(nx01, nx11, v5);

            return Mathf.Lerp(nxy0, nxy1, w5) * 0.5f + 0.5f;
        }

        private static int HashInt(int x, int y, int z)
        {
            uint n = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
            n = (n ^ (n >> 16)) * 0x85ebca6b;
            n = (n ^ (n >> 13)) * 0xc2b2ae35;
            return (int)(n ^ (n >> 16));
        }

        private static float Grad3D(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        /// <summary>
        /// Creates spider-web like connectivity between Worley cell centers.
        /// </summary>
        private static float WorleyRidges(float u, float v, int cells, float seed)
        {
            u = u - Mathf.Floor(u);
            v = v - Mathf.Floor(v);

            float fu = u * cells, fv = v * cells;
            int cx = Mathf.FloorToInt(fu), cy = Mathf.FloorToInt(fv);
            float fx = fu - cx, fy = fv - cy;

            float minDist1 = 10f, minDist2 = 10f;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = ((cx + dx) % cells + cells) % cells;
                    int ny = ((cy + dy) % cells + cells) % cells;

                    float px = Hash2D(nx, ny, seed);
                    float py = Hash2D(nx, ny, seed + 127.1f);

                    float distX = (dx + px) - fx;
                    float distY = (dy + py) - fy;
                    float dist = Mathf.Sqrt(distX * distX + distY * distY);

                    if (dist < minDist1)
                    {
                        minDist2 = minDist1;
                        minDist1 = dist;
                    }
                    else if (dist < minDist2)
                    {
                        minDist2 = dist;
                    }
                }
            }

            float edge = minDist2 - minDist1;
            return 1.0f - Mathf.Clamp01(edge * 3.0f);
        }

        /// <summary>
        /// Multi-octave Worley ridges for spider-web structures.
        /// </summary>
        private static float WorleyRidgeFBM(float u, float v, int baseCells, float seed, int octaves)
        {
            float val = 0f, amp = 1f, maxVal = 0f;
            int cells = baseCells;
            for (int i = 0; i < octaves; i++)
            {
                val += amp * WorleyRidges(u, v, cells, seed + i * 43.7f);
                maxVal += amp;
                amp *= 0.45f;
                cells = (int)(cells * 1.8f);
            }
            return val / maxVal;
        }

        public static Texture2D GenerateCirrusTexture(string path)
        {
            int res = 512;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[res * res];
            float ox = Random.value * 100f;
            float oy = Random.value * 100f;

            float[] rawR = new float[res * res];
            float[] rawG = new float[res * res];
            float minR = float.MaxValue, maxR = float.MinValue;
            float minG = float.MaxValue, maxG = float.MinValue;

            for (int y = 0; y < res; y++)
            {
                EditorUtility.DisplayProgressBar("Generating Cirrus Noise",
                    $"Computing cirrus noise... {y + 1}/{res}", (float)y / res);

                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / res;
                    float v = (float)y / res;
                    int idx = y * res + x;

                    float wu = TileableFBM(u, v, 3f, ox + 50f, oy + 50f, 3);
                    float wv = TileableFBM(u, v, 3f, ox + 100f, oy + 100f, 3);
                    float warpedU = u + (wu - 0.5f) * 0.15f;
                    float warpedV = v + (wv - 0.5f) * 0.15f;
                    warpedU -= Mathf.Floor(warpedU);
                    warpedV -= Mathf.Floor(warpedV);

                    float patches = WorleyFBM(warpedU, warpedV, 5, ox + 10f, 3);

                    float web = WorleyRidgeFBM(warpedU, warpedV, 5, ox + 20f, 3);

                    float perlinVar = DomainWarpedFBM(u, v, 4f, ox, oy, 4, 0.18f);

                    float r = patches * 0.55f + web * 0.3f + perlinVar * 0.15f;

                    rawR[idx] = r;
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;

                    float g = MultiDirectionalCirrusCurl(u, v, 10f, 3f, ox + 700f, oy + 700f, 4, 0.28f);

                    rawG[idx] = g;
                    if (g < minG) minG = g;
                    if (g > maxG) maxG = g;
                }
            }

            EditorUtility.ClearProgressBar();

            float rRange = maxR - minR;
            float gRange = maxG - minG;

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / res;
                    float v = (float)y / res;
                    int idx = y * res + x;

                    float r = Remap01(rawR[idx], minR + rRange * 0.05f, maxR - rRange * 0.02f);
                    float g = Remap01(rawG[idx], minG + gRange * 0.08f, maxG - gRange * 0.03f);

                    float b = TileableFBM(u, v, 3f, ox + 200f, oy + 200f, 3);
                    b = Remap01(b, 0.15f, 0.85f);

                    pixels[idx] = new Color(r, g, b, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            Debug.Log($"<b><color=#33FF33>[CirrusGen]</color></b> Cirrus texture generated.");

            Texture2D savedTex = SaveTexture(tex, path, true);
            EditorGUIUtility.PingObject(savedTex);
            return savedTex;
        }

        #endregion

        #region I/O Utilities

        private static Texture2D SaveTexture(Texture2D tex, string path, bool isBilinear)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = isBilinear ? FilterMode.Bilinear : FilterMode.Point;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = isBilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        #endregion
    }
}