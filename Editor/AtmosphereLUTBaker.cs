using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Static utility to bake the Optical Depth LUT for atmospheric scattering.
    /// This pre-calculates the density integrals for Rayleigh, Mie, and Ozone.
    /// Eliminates the O(N) inner loop in the shader, making the skybox rendering O(N) instead of O(N^2).
    /// </summary>
    public static class AtmosphereLUTBaker
    {
        public const string LUT_PATH = "Assets/Horizon Weather & Time/Textures/Sky/Horizon_Atmosphere_LUT.asset";

        // Physical constants matching the shader
        private const float PLANET_RADIUS = 6371000.0f;
        private const float ATMOSPHERE_THICKNESS = 100000.0f;
        private const float ATMOSPHERE_RADIUS = PLANET_RADIUS + ATMOSPHERE_THICKNESS;
        private const float RAYLEIGH_SCALE_HEIGHT = 8500.0f;
        private const float MIE_SCALE_HEIGHT = 1200.0f;
        private const float OZONE_CENTER = 25000.0f;
        private const float OZONE_WIDTH = 15000.0f;

        private const int RESOLUTION_U = 256;
        private const int RESOLUTION_V = 128;
        private const int STEPS = 512;

        public static Texture2D GenerateLUT()
        {
            Texture2D tex = new Texture2D(RESOLUTION_U, RESOLUTION_V, TextureFormat.RGBAFloat, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 0;

            Color[] pixels = new Color[RESOLUTION_U * RESOLUTION_V];

            for (int y = 0; y < RESOLUTION_V; y++)
            {
                float v = (float)y / (RESOLUTION_V - 1);
                float altitude = (v * v) * ATMOSPHERE_THICKNESS;

                for (int x = 0; x < RESOLUTION_U; x++)
                {
                    float u = (float)x / (RESOLUTION_U - 1);
                    float u_norm = u * 2.0f - 1.0f;
                    float cosTheta = Mathf.Sign(u_norm) * (u_norm * u_norm);

                    cosTheta = Mathf.Clamp(cosTheta, -1.0f, 1.0f);

                    pixels[y * RESOLUTION_U + x] = IntegrateOpticalDepth(altitude, cosTheta);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            SaveTexture(tex);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(LUT_PATH);
        }

        private static Color IntegrateOpticalDepth(float altitude, float cosTheta)
        {
            Vector3 origin = new Vector3(0, PLANET_RADIUS + altitude, 0);

            float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
            Vector3 dir = new Vector3(sinTheta, cosTheta, 0).normalized;

            float distToTop = RaySphereIntersect(origin, dir, ATMOSPHERE_RADIUS);

            float rayLength = distToTop;
            if (rayLength <= 0) return Color.black;

            float stepSize = rayLength / STEPS;
            double totalR = 0.0;
            double totalM = 0.0;
            double totalO = 0.0;

            for (int i = 0; i < STEPS; i++)
            {
                float t = (i + 0.5f) * stepSize;
                Vector3 pos = origin + dir * t;
                float h = pos.magnitude - PLANET_RADIUS;

                h = Mathf.Max(0, h);

                totalR += System.Math.Exp(-h / RAYLEIGH_SCALE_HEIGHT);
                totalM += System.Math.Exp(-h / MIE_SCALE_HEIGHT);
                totalO += System.Math.Max(0, 1.0 - System.Math.Abs(h - OZONE_CENTER) / OZONE_WIDTH);
            }

            return new Color((float)(totalR * stepSize), (float)(totalM * stepSize), (float)(totalO * stepSize), 1.0f);
        }

        private static float RaySphereIntersect(Vector3 org, Vector3 dir, float radius)
        {
            float b = Vector3.Dot(org, dir);
            float c = Vector3.Dot(org, org) - radius * radius;
            float d = b * b - c;
            if (d < 0) return -1;
            return -b + Mathf.Sqrt(d);
        }

        private static void SaveTexture(Texture2D tex)
        {
            string dir = Path.GetDirectoryName(LUT_PATH);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            AssetDatabase.CreateAsset(tex, LUT_PATH);

            AssetDatabase.ImportAsset(LUT_PATH, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();

            Debug.Log($"<b><color=#33FF33>[LOG]</color></b> [AtmosphereLUTBaker] Generated Optical Depth LUT at {LUT_PATH}");
        }
    }
}