using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Generates Advanced Weather Maps & Blue Noise textures for volumetric cloud rendering.
    /// Implements industry-standard techniques (Perlin-Worley, Domain Warping) inspired by 
    /// Guerrilla Games' "Horizon Zero Dawn" cloud rendering approach.
    /// </summary>
    public class WeatherOptimizationGen : EditorWindow
    {
        public const string DEFAULT_WEATHER_MAP_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_WeatherMap_Gen.png";
        public const string DEFAULT_BLUE_NOISE_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_BlueNoise_Gen.png";

        [MenuItem("Tools/Horizon/WeatherTime/Generate Optimization Maps (AAA)")]
        public static void ShowWindow()
        {
            GetWindow<WeatherOptimizationGen>("Optimization Gen");
        }

        private void OnGUI()
        {
            GUILayout.Label("AAA Cloud Maps Generator", EditorStyles.boldLabel);
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