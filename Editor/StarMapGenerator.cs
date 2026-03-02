using UnityEngine;
using UnityEditor;
using System.IO;
using System.Globalization;

namespace BlackHorizon.HorizonWeatherTime
{
    public class StarMapGenerator : EditorWindow
    {
        public enum ProjectionMode
        {
            Equatorial,
            Galactic
        }

        private TextAsset _csvFile;

        // --- Settings ---
        private ProjectionMode _projection = ProjectionMode.Equatorial;
        private int _resolutionWidth = 4096;
        private float _magThreshold = 6.5f;
        private float _starSize = 2.0f;
        private float _brightnessExp = 2.2f;

        // --- Adjustments ---
        [Range(0f, 1f)] private float _horizontalShift = 0.5f;
        private bool _mirrorHorizontal = true;
        private bool _saveAsEXR = true;

        // --- Debug ---
        private bool _drawDebugGuides = false;

        private const string SAVE_PATH_PNG = "Assets/Horizon Weather & Time/Textures/Sky/Horizon_StarMap_Gen.png";
        private const string SAVE_PATH_EXR = "Assets/Horizon Weather & Time/Textures/Sky/Horizon_StarMap_Gen.exr";

        private Vector3 _xGal, _yGal, _zGal; [MenuItem("Tools/Horizon/WeatherTime/Generate Star Map (HYG)")]
        public static void ShowWindow()
        {
            GetWindow<StarMapGenerator>("Star Map Gen");
        }

        private void OnGUI()
        {
            HorizonEditorUtils.DrawHorizonHeader("Star Map Generator", this);
            EditorGUILayout.Space();

            _csvFile = (TextAsset)EditorGUILayout.ObjectField("HYG Database (.txt)", _csvFile, typeof(TextAsset), false);

            EditorGUILayout.Space();
            GUILayout.Label("Projection & Orientation", EditorStyles.boldLabel);

            _projection = (ProjectionMode)EditorGUILayout.EnumPopup("Coordinate System", _projection);
            _mirrorHorizontal = EditorGUILayout.Toggle("Mirror Horizontal", _mirrorHorizontal);
            _horizontalShift = EditorGUILayout.Slider("Phase Shift", _horizontalShift, 0f, 1f);

            EditorGUILayout.Space();
            GUILayout.Label("Visuals", EditorStyles.boldLabel);

            _resolutionWidth = EditorGUILayout.IntPopup("Resolution", _resolutionWidth, new string[] { "2048", "4096", "8192" }, new int[] { 2048, 4096, 8192 });
            _magThreshold = EditorGUILayout.Slider("Magnitude Limit", _magThreshold, 4.0f, 50.0f);
            _starSize = EditorGUILayout.Slider("Max Star Size (Px)", _starSize, 1.0f, 10.0f);
            _brightnessExp = EditorGUILayout.Slider("Gamma Contrast", _brightnessExp, 1.0f, 5.0f);
            _saveAsEXR = EditorGUILayout.Toggle("Save as EXR (HDR)", _saveAsEXR);

            EditorGUILayout.Space();
            GUILayout.Label("Debug Tools", EditorStyles.boldLabel);
            _drawDebugGuides = EditorGUILayout.Toggle("Draw Debug Guides", _drawDebugGuides);

            if (_drawDebugGuides)
            {
                EditorGUILayout.HelpBox(
                    "🟦 Blue Band = Milky Way (Galactic Equator)\n" +
                    "🟥 Red Line = Celestial Equator\n" +
                    "🟩 Green Line = Prime Meridian (RA = 0h)\n" +
                    "🟨 Yellow Dot = East Marker (RA = 6h). Helps verify mirroring!", MessageType.Info);
            }

            EditorGUILayout.Space();

            if (_csvFile != null)
            {
                if (GUILayout.Button("Generate Star Map", GUILayout.Height(40)))
                {
                    GenerateMap();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Please assign 'hygdata_v3.txt' to generate.", MessageType.Warning);
            }
        }

        private void GenerateMap()
        {
            Texture2D tex = null;

            try
            {
                int width = _resolutionWidth;
                int height = width / 2;

                tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                Color[] pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0, 0, 0, 0);

                InitGalacticBasis();

                string[] lines = _csvFile.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                int count = 0;

                EditorUtility.DisplayProgressBar("Generating Stars", "Parsing HYG Database...", 0f);

                for (int i = 1; i < lines.Length; i++)
                {
                    if (i % 5000 == 0) EditorUtility.DisplayProgressBar("Generating Stars", $"Processing star {i}/{lines.Length}...", (float)i / lines.Length);

                    string line = lines[i];
                    string[] cols = line.Split(',');
                    if (cols.Length < 17) continue;

                    if (!float.TryParse(cols[13], NumberStyles.Any, CultureInfo.InvariantCulture, out float mag)) continue;
                    if (mag > _magThreshold) continue;

                    if (!float.TryParse(cols[7], NumberStyles.Any, CultureInfo.InvariantCulture, out float ra)) continue;
                    if (!float.TryParse(cols[8], NumberStyles.Any, CultureInfo.InvariantCulture, out float dec)) continue;

                    float ci = 0.0f;
                    if (!string.IsNullOrEmpty(cols[16])) float.TryParse(cols[16], NumberStyles.Any, CultureInfo.InvariantCulture, out ci);

                    float u = 0f, v = 0f;

                    if (_projection == ProjectionMode.Equatorial)
                    {
                        u = ra / 24.0f;
                        v = (dec + 90.0f) / 180.0f;
                    }
                    else
                    {
                        Vector2 gal = EquatorialToGalactic(ra, dec);
                        u = gal.x / 360.0f;
                        v = (gal.y + 90.0f) / 180.0f;
                    }

                    u = (u + _horizontalShift) % 1.0f;
                    if (u < 0) u += 1.0f;

                    if (_mirrorHorizontal) u = 1.0f - u;

                    int x = Mathf.FloorToInt(u * (width - 1));
                    int y = Mathf.FloorToInt(v * (height - 1));

                    Color starColor = BVToRGB(ci);

                    float visualIntensity = Mathf.Pow(Mathf.Max(0, (_magThreshold - mag) / _magThreshold), _brightnessExp);
                    float radius = Mathf.Lerp(0.8f, _starSize, visualIntensity);
                    float hdrMult = _saveAsEXR ? 10.0f : 1.0f;

                    DrawStar(pixels, width, height, x, y, radius, starColor, visualIntensity * hdrMult);
                    count++;
                }

                if (_drawDebugGuides)
                {
                    EditorUtility.DisplayProgressBar("Generating Stars", "Drawing Debug Guides...", 0.95f);
                    DrawDebugOverlay(pixels, width, height);
                }

                tex.SetPixels(pixels);
                tex.Apply();

                SaveTexture(tex);
                Debug.Log($"Generated {count} stars in {_projection} mode.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (tex != null) DestroyImmediate(tex);
            }
        }

        // --- DEBUG OVERLAY ---

        private void DrawDebugOverlay(Color[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u_base = (float)x / (width - 1);
                    float v_base = (float)y / (height - 1);

                    float u_unmirrored = _mirrorHorizontal ? 1.0f - u_base : u_base;

                    float u_unshifted = u_unmirrored - _horizontalShift;
                    if (u_unshifted < 0) u_unshifted += 1.0f;
                    if (u_unshifted >= 1.0f) u_unshifted -= 1.0f;

                    float raHours = 0, decDeg = 0, galB = 0;

                    if (_projection == ProjectionMode.Equatorial)
                    {
                        raHours = u_unshifted * 24.0f;
                        decDeg = v_base * 180.0f - 90.0f;
                        galB = EquatorialToGalactic(raHours, decDeg).y;
                    }
                    else
                    {
                        float galL = u_unshifted * 360.0f;
                        galB = v_base * 180.0f - 90.0f;
                        Vector2 eq = GalacticToEquatorial(galL, galB);
                        raHours = eq.x;
                        decDeg = eq.y;
                    }

                    bool draw = false;
                    Color overlay = Color.clear;

                    // 1. Milky Way Band (Blue)
                    if (Mathf.Abs(galB) < 1.5f) { overlay += new Color(0, 0, 2.0f, 1); draw = true; }

                    // 2. Celestial Equator (Red)
                    if (Mathf.Abs(decDeg) < 0.2f) { overlay += new Color(2.0f, 0, 0, 1); draw = true; }

                    // 3. Prime Meridian (Green)
                    if (raHours < 0.03f || raHours > 23.97f) { overlay += new Color(0, 2.0f, 0, 1); draw = true; }

                    // 4. East Marker (Yellow) at RA=6h
                    if (Mathf.Abs(decDeg) < 2.0f && Mathf.Abs(raHours - 6.0f) < 0.08f) { overlay += new Color(2.0f, 2.0f, 0, 1); draw = true; }

                    if (draw)
                    {
                        pixels[y * width + x] += overlay;
                    }
                }
            }
        }

        private void InitGalacticBasis()
        {
            Vector3 zG = DirFromRaDec(192.85948f, 27.12825f);
            Vector3 xG = DirFromRaDec(266.40510f, -28.93617f);

            _yGal = Vector3.Cross(zG, xG).normalized;
            _xGal = Vector3.Cross(_yGal, zG).normalized;
            _zGal = zG.normalized;
        }

        private Vector3 DirFromRaDec(float raDeg, float decDeg)
        {
            float r = raDeg * Mathf.Deg2Rad;
            float d = decDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(d) * Mathf.Cos(r), Mathf.Cos(d) * Mathf.Sin(r), Mathf.Sin(d));
        }

        private Vector2 EquatorialToGalactic(float raHours, float decDeg)
        {
            Vector3 eq = DirFromRaDec(raHours * 15.0f, decDeg);
            Vector3 gal = new Vector3(Vector3.Dot(eq, _xGal), Vector3.Dot(eq, _yGal), Vector3.Dot(eq, _zGal));
            float l = Mathf.Atan2(gal.y, gal.x) * Mathf.Rad2Deg;
            float b = Mathf.Asin(gal.z) * Mathf.Rad2Deg;
            if (l < 0) l += 360f;
            return new Vector2(l, b);
        }

        private Vector2 GalacticToEquatorial(float lDeg, float bDeg)
        {
            float l = lDeg * Mathf.Deg2Rad;
            float b = bDeg * Mathf.Deg2Rad;
            Vector3 gal = new Vector3(Mathf.Cos(b) * Mathf.Cos(l), Mathf.Cos(b) * Mathf.Sin(l), Mathf.Sin(b));
            Vector3 eq = gal.x * _xGal + gal.y * _yGal + gal.z * _zGal;
            float ra = Mathf.Atan2(eq.y, eq.x) * Mathf.Rad2Deg / 15.0f;
            float dec = Mathf.Asin(eq.z) * Mathf.Rad2Deg;
            if (ra < 0) ra += 24.0f;
            return new Vector2(ra, dec);
        }

        // --- RENDERING HELPERS ---

        private void DrawStar(Color[] pixels, int w, int h, int cx, int cy, float radius, Color col, float intensity)
        {
            int r = Mathf.CeilToInt(radius);
            float rSq = radius * radius;

            for (int dy = -r; dy <= r; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= h) continue;

                for (int dx = -r; dx <= r; dx++)
                {
                    int px = cx + dx;
                    if (px < 0) px += w;
                    else if (px >= w) px -= w;

                    float distSq = dx * dx + dy * dy;
                    if (distSq <= rSq)
                    {
                        float alpha = Mathf.Exp(-distSq / (rSq * 0.5f));
                        pixels[py * w + px] += col * intensity * alpha;
                    }
                }
            }
        }

        private Color BVToRGB(float bv)
        {
            bv = Mathf.Clamp(bv, -0.4f, 2.0f);

            float t = 4600 * (1 / (0.92f * bv + 1.7f) + 1 / (0.92f * bv + 0.62f));
            float temp = t / 100.0f;
            float r, g, b;

            if (temp <= 66) r = 255;
            else r = 329.698727446f * Mathf.Pow(temp - 60, -0.1332047592f);

            if (temp <= 66) g = 99.4708025861f * Mathf.Log(temp) - 161.1195681661f;
            else g = 288.1221695283f * Mathf.Pow(temp - 60, -0.0755148492f);

            if (temp >= 66) b = 255;
            else if (temp <= 19) b = 0;
            else b = 138.5177312231f * Mathf.Log(temp - 10) - 305.0447927307f;

            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f), 1.0f);
        }

        private void SaveTexture(Texture2D tex)
        {
            string path = _saveAsEXR ? SAVE_PATH_EXR : SAVE_PATH_PNG;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            byte[] bytes = _saveAsEXR ? tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP) : tex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);

            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            Debug.Log($"<b><color=#33FF33>[LOG]</color></b> Star Map Saved to {path}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
        }
    }
}