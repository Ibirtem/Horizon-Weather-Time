using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Utility window to generate static mesh volumes for GPU-based particle systems.
    /// Creates a large mesh of quads where each vertex contains unique seed and ID data 
    /// used by shaders for procedural animation and positioning.
    /// </summary>
    public class WeatherParticleGen : EditorWindow
    {
        private int _particleCount = 20000;
        private const string SAVE_PATH = "Assets/Horizon Weather & Time/Resources/Meshes/GPUParticleVolume.asset";

        [MenuItem("Tools/Horizon/WeatherTime/Generate GPU Particle Mesh")]
        public static void ShowWindow()
        {
            GetWindow<WeatherParticleGen>("GPU Particle Gen");
        }

        private void OnGUI()
        {
            HorizonEditorUtils.DrawHorizonHeader("Particle Generator", this);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Generates a static Mesh asset containing quads for GPU particle rendering. " +
                                    "Each quad is assigned unique random data in UV1 for shader-side variation.", MessageType.Info);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _particleCount = EditorGUILayout.IntSlider("Particle Count", _particleCount, 1000, 60000);

            if (GUILayout.Button("Generate & Save Mesh", GUILayout.Height(30)))
            {
                GenerateMesh(_particleCount, SAVE_PATH);
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Constructs a Mesh containing the specified number of quads and saves it to the project.
        /// Quads are initialized at origin; their world-space positions are computed in the shader.
        /// </summary>
        /// <param name="count">Total number of quads (particles) to generate.</param>
        /// <param name="path">Project-relative path to save the .asset file.</param>
        private static void GenerateMesh(int count, string path)
        {
            Mesh mesh = new Mesh();
            mesh.name = $"GPU_Particles_{count}";
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            int vertexCount = count * 4;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uv0 = new Vector2[vertexCount];
            Vector4[] uv1 = new Vector4[vertexCount];
            int[] indices = new int[count * 6];

            for (int i = 0; i < count; i++)
            {
                int vIdx = i * 4;
                int iIdx = i * 6;

                vertices[vIdx + 0] = new Vector3(-0.5f, -0.5f, 0);
                vertices[vIdx + 1] = new Vector3(0.5f, -0.5f, 0);
                vertices[vIdx + 2] = new Vector3(-0.5f, 0.5f, 0);
                vertices[vIdx + 3] = new Vector3(0.5f, 0.5f, 0);

                uv0[vIdx + 0] = new Vector2(0, 0);
                uv0[vIdx + 1] = new Vector2(1, 0);
                uv0[vIdx + 2] = new Vector2(0, 1);
                uv0[vIdx + 3] = new Vector2(1, 1);

                Vector4 data = new Vector4(Random.value, Random.value, Random.value, (float)i / count);
                uv1[vIdx + 0] = data;
                uv1[vIdx + 1] = data;
                uv1[vIdx + 2] = data;
                uv1[vIdx + 3] = data;

                indices[iIdx + 0] = vIdx + 0;
                indices[iIdx + 1] = vIdx + 2;
                indices[iIdx + 2] = vIdx + 1;
                indices[iIdx + 3] = vIdx + 2;
                indices[iIdx + 4] = vIdx + 3;
                indices[iIdx + 5] = vIdx + 1;
            }

            mesh.vertices = vertices;
            mesh.uv = uv0;
            mesh.SetUVs(1, uv1);
            mesh.triangles = indices;

            mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000, 10000, 10000));

            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            Debug.Log($"<b><color=#33FF33>[LOG]</color></b> [ParticleGen] Generated mesh with {count} quads. Saved to: {path}");
        }
    }
}