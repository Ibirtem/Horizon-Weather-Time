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
            if (GUILayout.Button("Generate 3D Cloud Noise (Takes ~10-20 sec)", GUILayout.Height(30)))
            {
                Generate3DCloudNoise(DEFAULT_CLOUD_NOISE_3D_PATH);
            }

            if (GUILayout.Button("Generate Blue Noise (Multi-Pass)", GUILayout.Height(30)))
            {
                GenerateBlueNoise(DEFAULT_BLUE_NOISE_PATH);
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
        /// Creates liquid-like, organic swirling patterns instead of rigid noise.
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
                    // Blend Domain Warped Perlin (for general shape) with Worley (for internal structure)
                    float perlin = DomainWarpedFBM(u, v, 3f, ox, oy, 5, 0.12f);
                    float worley = WorleyFBM(u, v, 4, ox, 3);

                    float coverage = Mathf.Lerp(perlin, perlin * worley, 0.4f);
                    coverage = Remap01(coverage, 0.2f, 0.8f);

                    // G: Cloud Type (0 = stratus, 1 = tall cumulus)
                    // Low frequency variation across the sky
                    float cloudType = TileableFBM(u, v, 1.5f, -ox, -oy, 2);

                    // B: Erosion Mask (Detail variance)
                    // High frequency mask to determine where clouds should be wispy vs solid
                    float erosionMask = TileableFBM(u, v, 8f, ox * 2f, oy * 2f, 4);

                    // A: Local Density
                    // Correlated with coverage to ensure thicker clouds have denser cores
                    float densVar = TileableFBM(u, v, 4f, ox + 200f, oy + 200f, 3);
                    float density = Mathf.Lerp(0.5f, 1.0f, coverage * densVar);

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

            float[] rawP = new float[totalVoxels];
            float[] rawW4 = new float[totalVoxels];
            float[] rawW8 = new float[totalVoxels];
            float[] rawW16 = new float[totalVoxels];
            float[] rawW24 = new float[totalVoxels];

            float minP = float.MaxValue, maxP = float.MinValue;
            float minW4 = float.MaxValue, maxW4 = float.MinValue;
            float minW8 = float.MaxValue, maxW8 = float.MinValue;
            float minW16 = float.MaxValue, maxW16 = float.MinValue;
            float minW24 = float.MaxValue, maxW24 = float.MinValue;

            try
            {
                for (int z = 0; z < res; z++)
                {
                    EditorUtility.DisplayProgressBar("Generating 3D Cloud Noise",
                        $"Pass 1/2: Calculating Voxels... slice {z + 1}/{res}", (float)z / (res * 2));

                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int i = x + y * res + z * res * res;
                            float u = (float)x / res;
                            float v = (float)y / res;
                            float w = (float)z / res;

                            float p = TileablePerlin3D(u, v, w, 5);
                            float w4 = TileableWorley3D(u, v, w, 4, 111);
                            float w8 = TileableWorley3D(u, v, w, 8, 333);
                            float w16 = TileableWorley3D(u, v, w, 16, 444);
                            float w24 = TileableWorley3D(u, v, w, 24, 555);

                            rawP[i] = p; if (p < minP) minP = p; if (p > maxP) maxP = p;
                            rawW4[i] = w4; if (w4 < minW4) minW4 = w4; if (w4 > maxW4) maxW4 = w4;
                            rawW8[i] = w8; if (w8 < minW8) minW8 = w8; if (w8 > maxW8) maxW8 = w8;
                            rawW16[i] = w16; if (w16 < minW16) minW16 = w16; if (w16 > maxW16) maxW16 = w16;
                            rawW24[i] = w24; if (w24 < minW24) minW24 = w24; if (w24 > maxW24) maxW24 = w24;
                        }
                    }
                }

                for (int z = 0; z < res; z++)
                {
                    EditorUtility.DisplayProgressBar("Generating 3D Cloud Noise",
                        $"Pass 2/2: Normalizing and Packing... slice {z + 1}/{res}", 0.5f + (float)z / (res * 2));

                    for (int y = 0; y < res; y++)
                    {
                        for (int x = 0; x < res; x++)
                        {
                            int i = x + y * res + z * res * res;

                            float p = (rawP[i] - minP) / (maxP - minP);
                            float w4 = (rawW4[i] - minW4) / (maxW4 - minW4);
                            float w8 = (rawW8[i] - minW8) / (maxW8 - minW8);
                            float w16 = (rawW16[i] - minW16) / (maxW16 - minW16);
                            float w24 = (rawW24[i] - minW24) / (maxW24 - minW24);

                            float invW4 = 1.0f - w4;
                            float invW8 = 1.0f - w8;
                            float invW16 = 1.0f - w16;
                            float invW24 = 1.0f - w24;

                            float r = RemapClamped(p, invW4, 1.0f, 0.0f, 1.0f);

                            float g = invW8;
                            float b = invW16;
                            float a = invW24;

                            pixels[i] = new Color(r, g, b, a);
                        }
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply(true);

                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(tex, path);
                AssetDatabase.SaveAssets();

                Debug.Log($"<b><color=#33FF33>[LOG]</color></b> 3D Cloud Noise saved to {path} (MinMax Pass Applied)");
                EditorGUIUtility.PingObject(tex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
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