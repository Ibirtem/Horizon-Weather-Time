using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    public class SnowNoiseGenerator : EditorWindow
    {
        private int _resolution = 1024;
        private int _scale = 8;
        private float _contrast = 1.2f;

        public const string DEFAULT_SNOW_NOISE_PATH = "Assets/Horizon Weather & Time/Textures/Horizon_SnowNoise_Gen.png";

        [MenuItem("Tools/Horizon/WeatherTime/Generate Snow Noise")]
        public static void ShowWindow()
        {
            GetWindow<SnowNoiseGenerator>("Snow Noise Gen");
        }

        private void OnGUI()
        {
            GUILayout.Label("Snow Coverage Map Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _resolution = EditorGUILayout.IntPopup("Resolution", _resolution, new string[] { "512", "1024", "2048" }, new int[] { 512, 1024, 2048 });
            _scale = EditorGUILayout.IntSlider("Pattern Scale", _scale, 2, 20);
            _contrast = EditorGUILayout.Slider("Contrast", _contrast, 0.5f, 2.0f);

            GUILayout.Space(10);
            EditorGUILayout.HelpBox("Generates a texture to control snow accumulation.\n" +
                                    "Dark areas = Snow appears last.\n" +
                                    "White areas = Snow appears first (drifts).", MessageType.Info);

            if (GUILayout.Button("Generate & Save"))
            {
                GenerateAndSaveTexture(_resolution, _scale, _contrast, DEFAULT_SNOW_NOISE_PATH);
            }
        }

        public static void GenerateAndSaveTexture(int resolution, int scale, float contrast, string path)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            Color[] pixels = new Color[resolution * resolution];

            float invRes = 1.0f / resolution;

            // Offset to avoid symmetry with cloud noise if used together
            float seedX = Random.Range(0f, 100f);
            float seedY = Random.Range(0f, 100f);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x * invRes;
                    float v = y * invRes;

                    // Layer 1: Base Drifts (Low Frequency)
                    float n1 = FractalNoise(u + seedX, v + seedY, scale, 3, 0.5f);

                    // Layer 2: Surface Texture (High Frequency)
                    float n2 = FractalNoise(u + seedX, v + seedY, scale * 4, 3, 0.5f);

                    // Combine: 70% Base, 30% Detail
                    float final = n1 * 0.7f + n2 * 0.3f;

                    // Apply Contrast
                    final = (final - 0.5f) * contrast + 0.5f;
                    final = Mathf.Clamp01(final);

                    pixels[y * resolution + x] = new Color(final, final, final, 1.0f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            SaveTexture(texture, path);
        }

        // --- MATH UTILS ---

        private static float FractalNoise(float u, float v, int scale, int octaves, float persistence)
        {
            float total = 0;
            float frequency = scale;
            float amplitude = 1;
            float maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Mathf.PerlinNoise(u * frequency, v * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= 2;
            }

            return total / maxValue;
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
                importer.sRGBTexture = false; // Linear for math data
                importer.mipMapsPreserveCoverage = true;
                importer.streamingMipmaps = true;
                importer.SaveAndReimport();
            }
            Debug.Log($"<b><color=#33FF33>[LOG]</color></b> [SnowGenerator] Texture saved to {path}");
        }
    }
}