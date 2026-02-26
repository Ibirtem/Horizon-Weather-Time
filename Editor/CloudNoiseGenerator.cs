using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    public class CloudNoiseGenerator : EditorWindow
    {
        private int _resolution = 512;
        private int _scale = 4;

        public const string DEFAULT_NOISE_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_CloudNoise_Gen.png";

        [MenuItem("Tools/Horizon/WeatherTime/Generate Cloud Noise")]
        public static void ShowWindow()
        {
            GetWindow<CloudNoiseGenerator>("Cloud Noise Gen");
        }

        private void OnGUI()
        {
            GUILayout.Label("Volumetric Cloud Noise Generator", EditorStyles.boldLabel);

            _resolution = EditorGUILayout.IntPopup("Resolution", _resolution, new string[] { "256", "512", "1024", "2048" }, new int[] { 256, 512, 1024, 2048 });
            _scale = EditorGUILayout.IntSlider("Tile Scale", _scale, 2, 16);

            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Generates a channel-packed Texture for Volumetric Raymarching.\nR: Base Shape\nG: Erosion\nB: High Details\nA: Distortion", MessageType.Info);

            if (GUILayout.Button("Generate & Save"))
            {
                GenerateAndSaveTexture(_resolution, _scale, DEFAULT_NOISE_PATH);
            }
        }

        public static Texture2D GenerateAndSaveTexture(int resolution, int scale, string path)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[resolution * resolution];

            scale = Mathf.Max(2, scale);

            float invRes = 1.0f / resolution;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x * invRes;
                    float v = y * invRes;

                    // R: Base Shape (Low Frequency FBM)
                    float r = FractalNoise(u, v, scale, 3, 0.5f);

                    // G: Erosion (Medium Frequency)
                    float g = FractalNoise(u, v, scale * 2, 3, 0.5f);

                    // B: Detail (High Frequency)
                    float b = FractalNoise(u, v, scale * 4, 3, 0.5f);

                    // A: Extra (Very High Frequency for edges)
                    float a = FractalNoise(u, v, scale * 8, 2, 0.5f);

                    // Contrast adjustments
                    r = Map(r, 0.2f, 0.8f, 0f, 1f);

                    pixels[y * resolution + x] = new Color(r, g, b, a);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            SaveTexture(texture, path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // --- TILING NOISE MATH ---

        private static float Map(float val, float min1, float max1, float min2, float max2)
        {
            return min2 + (val - min1) * (max2 - min2) / (max1 - min1);
        }

        private static float FractalNoise(float u, float v, int scale, int octaves, float persistence)
        {
            float total = 0;
            float frequency = scale;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += TiledGradientNoise(u * frequency, v * frequency, frequency) * amplitude;

                maxValue += amplitude;

                amplitude *= persistence;
                frequency *= 2;
            }

            return (total / maxValue) * 0.5f + 0.5f;
        }

        private static float TiledGradientNoise(float x, float y, float period)
        {
            // Wrap coordinates based on period
            float xMod = x % period;
            float yMod = y % period;

            // Grid cell coordinates
            int xi = Mathf.FloorToInt(xMod);
            int yi = Mathf.FloorToInt(yMod);

            // Fractional part
            float xf = xMod - xi;
            float yf = yMod - yi;

            // Fade curves (Smoothstep/Perlin fade: 6t^5 - 15t^4 + 10t^3)
            float u = xf * xf * xf * (xf * (xf * 6 - 15) + 10);
            float v = yf * yf * yf * (yf * (yf * 6 - 15) + 10);

            int aa = Hash(xi % (int)period, yi % (int)period);
            int ab = Hash(xi % (int)period, (yi + 1) % (int)period);
            int ba = Hash((xi + 1) % (int)period, yi % (int)period);
            int bb = Hash((xi + 1) % (int)period, (yi + 1) % (int)period);

            float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);

            return Lerp(x1, x2, v);
        }

        private static float Lerp(float a, float b, float t) { return a + t * (b - a); }

        // Pseudo Random Hash
        private static int Hash(int x, int y)
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }

        // Gradient function
        private static float Grad(int hash, float x, float y)
        {
            int h = hash & 3;
            float u = h < 2 ? x : y;
            float v = h < 2 ? y : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? -2.0f * v : 2.0f * v);
        }

        private static void SaveTexture(Texture2D tex, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.SaveAndReimport();
            }
            Debug.Log($"<b><color=#33FF33>[LOG]</color></b> [CloudGenerator] Volumetric Noise saved to {path}");
        }
    }
}