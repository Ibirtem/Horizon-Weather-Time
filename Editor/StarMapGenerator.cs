using UnityEngine;
using UnityEditor;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Editor utility for generating high-accuracy Star Maps from the HYG Database.
    /// </summary>
    public class StarMapGenerator : EditorWindow
    {
        public enum ProjectionMode
        {
            Equatorial,
            Galactic
        }

        private TextAsset _csvFile;

        [Header("Projection Settings")]
        private bool _useCubemap = true;
        private ProjectionMode _projection = ProjectionMode.Equatorial;
        private int _resolutionWidth = 4096;

        [Header("Visuals")]
        private float _magThreshold = 6.5f;
        private float _starSize = 2.0f;
        private float _brightnessExp = 2.2f;
        private bool _saveAsEXR = true;

        [Header("Star Color Tuning")]
        [Range(0f, 3f)] private float _colorSaturation = 1.0f;
        [Range(-0.5f, 0.5f)] private float _colorTemperatureShift = 0.0f;

        [Header("Adjustments")]
        [Range(0f, 1f)] private float _horizontalShift = 0.5f;
        private bool _mirrorHorizontal = true;
        private bool _mirrorVertical = false;

        [Header("Constellations")]
        private bool _drawConstellations = true;
        private float _constellationLineWidth = 1.5f;
        private float _constellationLineIntensity = 0.15f;
        private Color _constellationLineColor = new Color(0.3f, 0.5f, 1.0f, 1.0f);
        private float _constellationMatchRadius = 1.5f;

        [Header("Debug")]
        private bool _drawDebugGuides = false;

        private const string SAVE_PATH_PNG = "Assets/Horizon Weather & Time/Textures/Sky/Horizon_StarMap_Gen.png";
        private const string SAVE_PATH_EXR = "Assets/Horizon Weather & Time/Textures/Sky/Horizon_StarMap_Gen.exr";

        private Vector3 _xGal, _yGal, _zGal;

        private struct CatalogStar
        {
            public float ra;
            public float dec;
            public float mag;
            public float ci;
        }

        private List<CatalogStar> _catalogStars;

        /// <summary>
        /// Grid mapping for Unity's standard Horizontal Cross (4x3) Cubemap layout.
        /// </summary>
        private static readonly Vector2Int[] FaceToGrid = new Vector2Int[]
        {
            new Vector2Int(2, 1), // 0: +X (Right)
            new Vector2Int(0, 1), // 1: -X (Left)
            new Vector2Int(1, 0), // 2: +Y (Up)
            new Vector2Int(1, 2), // 3: -Y (Down)
            new Vector2Int(1, 1), // 4: +Z (Forward)
            new Vector2Int(3, 1)  // 5: -Z (Back)
        };

        [MenuItem("Tools/Horizon/WeatherTime/Generate Star Map (HYG)")]
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

            _useCubemap = EditorGUILayout.Toggle("Render as Cubemap (4x3 Cross)", _useCubemap);

            _mirrorHorizontal = EditorGUILayout.Toggle("Mirror Horizontal", _mirrorHorizontal);
            _mirrorVertical = EditorGUILayout.Toggle("Mirror Vertical", _mirrorVertical);
            _horizontalShift = EditorGUILayout.Slider("Phase Shift (RA)", _horizontalShift, 0f, 1f);

            if (!_useCubemap)
            {
                _projection = (ProjectionMode)EditorGUILayout.EnumPopup("Coordinate System", _projection);
            }

            EditorGUILayout.Space();
            GUILayout.Label("Visuals", EditorStyles.boldLabel);

            _resolutionWidth = EditorGUILayout.IntPopup("Resolution", _resolutionWidth,
                new string[] { "2048", "4096", "8192" }, new int[] { 2048, 4096, 8192 });
            _magThreshold = EditorGUILayout.Slider("Magnitude Limit", _magThreshold, 4.0f, 50.0f);
            _starSize = EditorGUILayout.Slider("Max Star Size (Px)", _starSize, 1.0f, 10.0f);
            _brightnessExp = EditorGUILayout.Slider("Gamma Contrast", _brightnessExp, 1.0f, 5.0f);
            _saveAsEXR = EditorGUILayout.Toggle("Save as EXR (HDR)", _saveAsEXR);

            EditorGUILayout.Space();
            GUILayout.Label("Star Color Tuning", EditorStyles.boldLabel);
            _colorSaturation = EditorGUILayout.Slider("Color Saturation", _colorSaturation, 0f, 3f);
            _colorTemperatureShift = EditorGUILayout.Slider("Temperature Shift (B-V)", _colorTemperatureShift, -0.5f, 0.5f);

            EditorGUILayout.Space();
            GUILayout.Label("Constellation Lines", EditorStyles.boldLabel);
            _drawConstellations = EditorGUILayout.Toggle("Draw Constellations", _drawConstellations);

            if (_drawConstellations)
            {
                _constellationLineWidth = EditorGUILayout.Slider("Line Width (Px)", _constellationLineWidth, 0.5f, 5.0f);
                _constellationLineIntensity = EditorGUILayout.Slider("Line Intensity", _constellationLineIntensity, 0.01f, 1.0f);
                _constellationLineColor = EditorGUILayout.ColorField("Line Color", _constellationLineColor);
                _constellationMatchRadius = EditorGUILayout.Slider("Star Match Radius (°)", _constellationMatchRadius, 0.5f, 5.0f);
            }

            EditorGUILayout.Space();
            GUILayout.Label("Debug Tools", EditorStyles.boldLabel);
            _drawDebugGuides = EditorGUILayout.Toggle("Draw Debug Guides", _drawDebugGuides);

            if (_drawDebugGuides)
            {
                EditorGUILayout.HelpBox(
                    "🟦 Blue Band = Milky Way (Galactic Equator)\n" +
                    "🟥 Red Line = Celestial Equator\n" +
                    "🟩 Green Line = Prime Meridian (RA = 0h)\n" +
                    "🟨 Yellow Dot = East Marker (RA = 6h)", MessageType.Info);
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

        /// <summary>
        /// Converts Celestial Coordinates (RA/Dec) into a Unity World-Space Direction.
        /// </summary>
        private Vector3 CelestialDirToUnityDir(CelestialPoint p)
        {
            return CelestialDirToUnityDir(p.ra, p.dec);
        }

        private Vector3 CelestialDirToUnityDir(float raHours, float decDeg)
        {
            float shiftedRA = (raHours + _horizontalShift * 24.0f) % 24.0f;
            if (shiftedRA < 0) shiftedRA += 24.0f;

            float raRad = shiftedRA * 15.0f * Mathf.Deg2Rad;
            float decRad = decDeg * Mathf.Deg2Rad;

            if (_mirrorHorizontal) raRad = -raRad;
            if (_mirrorVertical) decRad = -decRad;

            float x = Mathf.Cos(decRad) * Mathf.Sin(raRad);
            float y = Mathf.Sin(decRad);
            float z = Mathf.Cos(decRad) * Mathf.Cos(raRad);

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Projects a 3D direction onto a specific face of a 4x3 Horizontal Cross texture.
        /// Returns Vector3(U, V, FaceIndex).
        /// </summary>
        private Vector3 GetCubeFaceUV(Vector3 dir)
        {
            float absX = Mathf.Abs(dir.x);
            float absY = Mathf.Abs(dir.y);
            float absZ = Mathf.Abs(dir.z);
            int face = 0;
            float u = 0, v = 0, ma = 0;

            if (absX >= absY && absX >= absZ)
            {
                face = dir.x > 0 ? 0 : 1; // +X, -X
                ma = absX; u = dir.x > 0 ? -dir.z : dir.z; v = dir.y;
            }
            else if (absY >= absX && absY >= absZ)
            {
                face = dir.y > 0 ? 2 : 3; // +Y, -Y
                ma = absY; u = dir.x; v = dir.y > 0 ? -dir.z : dir.z;
            }
            else
            {
                face = dir.z > 0 ? 4 : 5; // +Z, -Z
                ma = absZ; u = dir.z > 0 ? dir.x : -dir.x; v = dir.y;
            }
            return new Vector3((u / ma + 1f) * 0.5f, (v / ma + 1f) * 0.5f, face);
        }

        /// <summary>
        /// Main generation loop. Parses the database and routes drawing to the selected projection.
        /// </summary>
        private void GenerateMap()
        {
            if (_csvFile == null) return;

            Texture2D tex = null;
            try
            {
                int width, height, faceRes = 0;

                if (_useCubemap)
                {
                    faceRes = _resolutionWidth / 4;
                    width = faceRes * 4;
                    height = faceRes * 3;
                }
                else
                {
                    width = _resolutionWidth;
                    height = width / 2;
                }

                tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                Color[] pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0, 0, 0, 0);

                InitGalacticBasis();

                string[] lines = _csvFile.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                _catalogStars = new List<CatalogStar>(lines.Length);

                EditorUtility.DisplayProgressBar("Generating Stars", "Parsing HYG Database...", 0f);

                float hdrMult = _saveAsEXR ? 10.0f : 1.0f;

                for (int i = 1; i < lines.Length; i++)
                {
                    string[] cols = lines[i].Split(',');
                    if (cols.Length < 17) continue;

                    if (!float.TryParse(cols[13], NumberStyles.Any, CultureInfo.InvariantCulture, out float mag)) continue;
                    if (!float.TryParse(cols[7], NumberStyles.Any, CultureInfo.InvariantCulture, out float ra)) continue;
                    if (!float.TryParse(cols[8], NumberStyles.Any, CultureInfo.InvariantCulture, out float dec)) continue;

                    if (mag < -2.0f) continue;

                    float ci = 0.0f;
                    if (!string.IsNullOrEmpty(cols[16]))
                        float.TryParse(cols[16], NumberStyles.Any, CultureInfo.InvariantCulture, out ci);

                    if (mag <= 7.0f)
                        _catalogStars.Add(new CatalogStar { ra = ra, dec = dec, mag = mag, ci = ci });

                    if (mag > _magThreshold) continue;

                    float visualIntensity = Mathf.Pow(Mathf.Max(0, (_magThreshold - mag) / _magThreshold), _brightnessExp);
                    float radius = Mathf.Lerp(0.8f, _starSize, visualIntensity);
                    Color baseStarColor = BVToRGB(ci + _colorTemperatureShift);
                    Color starColor = AdjustStarSaturation(baseStarColor, _colorSaturation) * visualIntensity * hdrMult;

                    if (_useCubemap)
                    {
                        Vector3 dir = CelestialDirToUnityDir(ra, dec);
                        DrawPointCubemap(pixels, width, height, faceRes, dir, radius, starColor);
                    }
                    else
                    {
                        RaDecToUV(ra, dec, out float u, out float v, out float poleComp);
                        DrawStar(pixels, width, height, Mathf.FloorToInt(u * (width - 1)), Mathf.FloorToInt(v * (height - 1)), radius, starColor, 1.0f, dec);
                    }

                    if (i % 10000 == 0)
                        EditorUtility.DisplayProgressBar("Generating Stars", $"Processing {i}/{lines.Length}...", (float)i / lines.Length);
                }

                if (_drawConstellations)
                {
                    EditorUtility.DisplayProgressBar("Generating Stars", "Drawing Constellations...", 0.9f);
                    Color lineCol = _constellationLineColor * _constellationLineIntensity * hdrMult;
                    foreach (var constellation in GetAllConstellationDefs())
                    {
                        foreach (var seg in constellation.segments)
                        {
                            if (_useCubemap)
                                DrawLineCubemap(pixels, width, height, faceRes, seg.a, seg.b, lineCol);
                            else
                                DrawConstellationLine(pixels, width, height, seg.a, seg.b, lineCol);
                        }
                    }
                }

                tex.SetPixels(pixels);
                tex.Apply();
                SaveTexture(tex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (tex != null) DestroyImmediate(tex);
                _catalogStars = null;
            }
        }

        /// <summary>
        /// Draws a single Gaussian point (Star) onto the Cubemap grid.
        /// </summary>
        private void DrawPointCubemap(Color[] pixels, int texW, int texH, int faceRes, Vector3 dir, float radius, Color color)
        {
            Vector3 faceUV = GetCubeFaceUV(dir);
            Vector2Int gridPos = FaceToGrid[(int)faceUV.z];

            float pxCenter = gridPos.x * faceRes + faceUV.x * (faceRes - 1);
            float pyCenter = gridPos.y * faceRes + faceUV.y * (faceRes - 1);

            int r = Mathf.CeilToInt(radius + 1);
            float rSqBase = radius * radius;

            int xMin = Mathf.Max(0, Mathf.FloorToInt(pxCenter - r));
            int xMax = Mathf.Min(texW - 1, Mathf.CeilToInt(pxCenter + r));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(pyCenter - r));
            int yMax = Mathf.Min(texH - 1, Mathf.CeilToInt(pyCenter + r));

            for (int py = yMin; py <= yMax; py++)
            {
                float dy = py - pyCenter;
                for (int px = xMin; px <= xMax; px++)
                {
                    float dx = px - pxCenter;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= rSqBase * 4.0f)
                    {
                        float alpha = Mathf.Exp(-distSq / (rSqBase * 0.5f + 0.0001f));
                        pixels[py * texW + px] += color * alpha;
                    }
                }
            }
        }

        /// <summary>
        /// Draws constellation lines between two celestial points on the Cubemap using Slerp.
        /// </summary>
        private void DrawLineCubemap(Color[] pixels, int texW, int texH, int faceRes, CelestialPoint a, CelestialPoint b, Color color)
        {
            Vector3 dirA = CelestialDirToUnityDir(a);
            Vector3 dirB = CelestialDirToUnityDir(b);

            float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dirA, dirB), -1f, 1f));

            int steps = Mathf.CeilToInt(angle * Mathf.Rad2Deg * 10.0f);

            steps = Mathf.Max(1, steps);

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 dir = Vector3.Slerp(dirA, dirB, t);

                DrawPointCubemap(pixels, texW, texH, faceRes, dir, _constellationLineWidth * 0.5f, color);
            }
        }

        private void RaDecToUV(float raHours, float decDeg, out float u, out float v, out float poleCompensation)
        {
            u = raHours / 24.0f;
            v = 1.0f - (decDeg + 90.0f) / 180.0f;

            u = (u + _horizontalShift) % 1.0f;
            if (u < 0) u += 1.0f;
            if (_mirrorHorizontal) u = 1.0f - u;
            if (_mirrorVertical) v = 1.0f - v;

            float absDecRad = Mathf.Abs(decDeg) * Mathf.Deg2Rad;
            float cosDec = Mathf.Cos(absDecRad);
            poleCompensation = 1.0f / Mathf.Max(cosDec, 0.35f);
        }

        // =====================================================================
        //  CONSTELLATION DATA STRUCTURES
        // =====================================================================

        private struct CelestialPoint
        {
            public float ra;
            public float dec;

            public CelestialPoint(float raHours, float decDegrees)
            {
                ra = raHours;
                dec = decDegrees;
            }
        }

        private struct ConstellationSegment
        {
            public CelestialPoint a;
            public CelestialPoint b;

            public ConstellationSegment(float ra1, float dec1, float ra2, float dec2)
            {
                a = new CelestialPoint(ra1, dec1);
                b = new CelestialPoint(ra2, dec2);
            }
        }

        private struct ConstellationDef
        {
            public string name;
            public List<ConstellationSegment> segments;
        }

        // =====================================================================
        //  CONSTELLATION VALIDATION & DRAWING
        // =====================================================================

        /// <summary>
        /// Finds the nearest catalog star to the given RA/Dec within the match radius.
        /// Returns the snapped position, or false if no match found.
        /// </summary>
        private bool FindCatalogStar(CelestialPoint target, out CelestialPoint snapped)
        {
            snapped = target;
            float bestDist = float.MaxValue;
            bool found = false;

            Vector3 targetDir = CelestialToDir(target);
            float cosLimit = Mathf.Cos(_constellationMatchRadius * Mathf.Deg2Rad);

            for (int i = 0; i < _catalogStars.Count; i++)
            {
                var cs = _catalogStars[i];
                Vector3 csDir = CelestialToDir(new CelestialPoint(cs.ra, cs.dec));
                float cosAngle = Vector3.Dot(targetDir, csDir);

                if (cosAngle > cosLimit && cosAngle > bestDist)
                {
                    bestDist = cosAngle;
                    snapped = new CelestialPoint(cs.ra, cs.dec);
                    found = true;
                }
            }

            return found;
        }

        private void DrawAllConstellations(Color[] pixels, int width, int height)
        {
            List<ConstellationDef> allConstellations = GetAllConstellationDefs();

            float hdrMult = _saveAsEXR ? 5.0f : 1.0f;
            Color lineColor = _constellationLineColor * _constellationLineIntensity * hdrMult;

            int totalDrawn = 0;

            for (int c = 0; c < allConstellations.Count; c++)
            {
                var constellation = allConstellations[c];

                if (c % 10 == 0)
                    EditorUtility.DisplayProgressBar("Drawing Constellations",
                        $"{constellation.name} ({c}/{allConstellations.Count})...",
                        0.85f + 0.1f * ((float)c / allConstellations.Count));

                foreach (var seg in constellation.segments)
                {
                    DrawConstellationLine(pixels, width, height, seg.a, seg.b, lineColor);
                }

                totalDrawn++;
            }

            Debug.Log($"<b><color=#33FF33>[Constellations]</color></b> Drawn: {totalDrawn}");
        }

        // =====================================================================
        //  LINE DRAWING
        // =====================================================================

        private Vector2 CelestialToPixel(CelestialPoint p, int width, int height)
        {
            float u, v, poleComp;
            RaDecToUV(p.ra, p.dec, out u, out v, out poleComp);
            return new Vector2(u * (width - 1), v * (height - 1));
        }

        /// <summary>
        /// Draws a great-circle line between two celestial points using slerp interpolation.
        /// Line width compensates for polar compression.
        /// </summary>
        private void DrawConstellationLine(Color[] pixels, int width, int height,
            CelestialPoint a, CelestialPoint b, Color color)
        {
            Vector3 dirA = CelestialToDir(a);
            Vector3 dirB = CelestialToDir(b);

            float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dirA, dirB), -1f, 1f));
            int steps = Mathf.Max(8, Mathf.CeilToInt(angle * Mathf.Rad2Deg * 2.0f));

            Vector2 prevPx = CelestialToPixel(a, width, height);

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 dir = Vector3.Slerp(dirA, dirB, t).normalized;

                float dec = Mathf.Asin(dir.z) * Mathf.Rad2Deg;
                float ra = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg / 15.0f;
                if (ra < 0) ra += 24.0f;

                CelestialPoint cp = new CelestialPoint(ra, dec);
                Vector2 curPx = CelestialToPixel(cp, width, height);

                float dx = Mathf.Abs(curPx.x - prevPx.x);
                if (dx < width * 0.5f)
                {
                    float cosDec = Mathf.Cos(Mathf.Abs(dec) * Mathf.Deg2Rad);
                    float hStretch = 1.0f / Mathf.Max(cosDec, 0.1f);
                    DrawLineOnPixels(pixels, width, height, prevPx, curPx, color,
                        _constellationLineWidth, hStretch);
                }

                prevPx = curPx;
            }
        }

        private Vector3 CelestialToDir(CelestialPoint p)
        {
            float raDeg = p.ra * 15.0f;
            float r = raDeg * Mathf.Deg2Rad;
            float d = p.dec * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(d) * Mathf.Cos(r), Mathf.Cos(d) * Mathf.Sin(r), Mathf.Sin(d));
        }

        /// <summary>
        /// Draws an anti-aliased line segment on the pixel array with optional
        /// horizontal stretch for equirectangular pole compensation.
        /// </summary>
        private void DrawLineOnPixels(Color[] pixels, int width, int height,
            Vector2 from, Vector2 to, Color color, float lineWidth, float hStretch = 1.0f)
        {
            float dist = Vector2.Distance(from, to);
            int pixelSteps = Mathf.Max(1, Mathf.CeilToInt(dist * 2.0f));
            float halfWidth = lineWidth * 0.5f;
            int iHalfWidthX = Mathf.CeilToInt(halfWidth * hStretch) + 1;
            int iHalfWidthY = Mathf.CeilToInt(halfWidth) + 1;

            for (int i = 0; i <= pixelSteps; i++)
            {
                float t = (float)i / pixelSteps;
                float cx = Mathf.Lerp(from.x, to.x, t);
                float cy = Mathf.Lerp(from.y, to.y, t);

                for (int dy = -iHalfWidthY; dy <= iHalfWidthY; dy++)
                {
                    for (int dx = -iHalfWidthX; dx <= iHalfWidthX; dx++)
                    {
                        int px = Mathf.RoundToInt(cx) + dx;
                        int py = Mathf.RoundToInt(cy) + dy;

                        if (py < 0 || py >= height) continue;
                        if (px < 0) px += width;
                        else if (px >= width) px -= width;

                        float ndx = dx / hStretch;
                        float distToCenter = Mathf.Sqrt(ndx * ndx + dy * dy);

                        Vector2 stretchedPoint = new Vector2(
                            Mathf.RoundToInt(cx) + ndx,
                            py
                        );
                        float distToLine = PointToSegmentDistance(stretchedPoint,
                            new Vector2(from.x / hStretch, from.y),
                            new Vector2(to.x / hStretch, to.y));

                        if (distToCenter <= halfWidth)
                        {
                            float alpha = Mathf.Clamp01(1.0f - (distToCenter / halfWidth));
                            alpha = alpha * alpha;
                            pixels[py * width + px] += color * alpha;
                        }
                    }
                }
            }
        }

        private float PointToSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        // =====================================================================
        //  Coordinates: RA in hours, Dec in degrees (J2000).
        // =====================================================================

        private List<ConstellationDef> GetAllConstellationDefs()
        {
            List<ConstellationDef> defs = new List<ConstellationDef>();

            void Add(string name, ConstellationSegment[] segs)
            {
                defs.Add(new ConstellationDef
                {
                    name = name,
                    segments = new List<ConstellationSegment>(segs)
                });
            }

            // =================================================================
            // URSA MINOR (Little Dipper)
            // =================================================================
            Add("Ursa Minor", new[] {
                new ConstellationSegment(2.530f, 89.264f, 17.537f, 86.586f),  // Polaris -> δ
                new ConstellationSegment(17.537f, 86.586f, 16.766f, 82.037f), // δ -> ε
                new ConstellationSegment(16.766f, 82.037f, 15.734f, 77.795f), // ε -> ζ
                new ConstellationSegment(15.734f, 77.795f, 16.292f, 75.755f), // ζ -> η
                new ConstellationSegment(16.292f, 75.755f, 15.345f, 71.834f), // η -> γ Pherkad
                new ConstellationSegment(15.345f, 71.834f, 14.845f, 74.156f), // γ -> β Kochab
                new ConstellationSegment(14.845f, 74.156f, 15.734f, 77.795f), // β -> ζ
            });

            // =================================================================
            // URSA MAJOR (Big Dipper + body)
            // =================================================================
            Add("Ursa Major", new[] {
                new ConstellationSegment(11.062f, 61.751f, 11.031f, 56.382f), // Dubhe -> Merak
                new ConstellationSegment(11.031f, 56.382f, 11.897f, 53.695f), // Merak -> Phecda
                new ConstellationSegment(11.897f, 53.695f, 12.257f, 57.033f), // Phecda -> Megrez
                new ConstellationSegment(12.257f, 57.033f, 11.062f, 61.751f), // Megrez -> Dubhe
                new ConstellationSegment(12.257f, 57.033f, 12.900f, 55.960f), // Megrez -> Alioth
                new ConstellationSegment(12.900f, 55.960f, 13.399f, 54.925f), // Alioth -> Mizar
                new ConstellationSegment(13.399f, 54.925f, 13.792f, 49.313f), // Mizar -> Alkaid
            });

            // =================================================================
            // CASSIOPEIA
            // =================================================================
            Add("Cassiopeia", new[] {
                new ConstellationSegment(0.153f, 59.150f, 0.675f, 56.537f),   // Caph -> Schedar
                new ConstellationSegment(0.675f, 56.537f, 0.945f, 60.717f),   // Schedar -> γ
                new ConstellationSegment(0.945f, 60.717f, 1.430f, 60.235f),   // γ -> Ruchbah
                new ConstellationSegment(1.430f, 60.235f, 1.907f, 63.670f),   // Ruchbah -> ε
            });

            // =================================================================
            // DRACO
            // =================================================================
            Add("Draco", new[] {
                new ConstellationSegment(17.943f, 51.489f, 17.507f, 52.301f), // Eltanin -> Rastaban
                new ConstellationSegment(17.507f, 52.301f, 17.338f, 55.184f), // Rastaban -> ν
                new ConstellationSegment(17.338f, 55.184f, 17.892f, 56.873f), // ν -> ξ
                new ConstellationSegment(17.892f, 56.873f, 17.943f, 51.489f), // ξ -> Eltanin (head)
                new ConstellationSegment(17.892f, 56.873f, 19.209f, 67.662f), // ξ -> δ
                new ConstellationSegment(19.209f, 67.662f, 19.803f, 70.268f), // δ -> ε
                new ConstellationSegment(19.803f, 70.268f, 19.261f, 73.356f), // ε -> τ
                new ConstellationSegment(19.261f, 73.356f, 18.351f, 72.733f), // τ -> χ
                new ConstellationSegment(18.351f, 72.733f, 17.146f, 65.715f), // χ -> ζ
                new ConstellationSegment(17.146f, 65.715f, 16.400f, 61.514f), // ζ -> η
                new ConstellationSegment(16.400f, 61.514f, 16.031f, 58.565f), // η -> θ
                new ConstellationSegment(16.031f, 58.565f, 15.415f, 58.966f), // θ -> ι
                new ConstellationSegment(15.415f, 58.966f, 14.073f, 64.376f), // ι -> Thuban
                new ConstellationSegment(14.073f, 64.376f, 12.558f, 69.788f), // Thuban -> κ
                new ConstellationSegment(12.558f, 69.788f, 11.523f, 69.331f), // κ -> λ
            });

            // =================================================================
            // CEPHEUS
            // =================================================================
            Add("Cepheus", new[] {
                new ConstellationSegment(21.310f, 62.585f, 21.478f, 70.561f), // Alderamin -> β
                new ConstellationSegment(21.478f, 70.561f, 23.656f, 77.632f), // β -> Errai
                new ConstellationSegment(23.656f, 77.632f, 22.828f, 66.200f), // Errai -> ι
                new ConstellationSegment(22.828f, 66.200f, 22.486f, 58.415f), // ι -> δ
                new ConstellationSegment(22.486f, 58.415f, 22.181f, 58.201f), // δ -> ζ
                new ConstellationSegment(22.181f, 58.201f, 21.310f, 62.585f), // ζ -> Alderamin
            });

            // =================================================================
            // ORION
            // =================================================================
            Add("Orion", new[] {
                new ConstellationSegment(5.919f, 7.407f, 5.419f, 6.350f),     // Betelgeuse -> Bellatrix
                new ConstellationSegment(5.419f, 6.350f, 5.533f, -0.299f),    // Bellatrix -> Mintaka
                new ConstellationSegment(5.533f, -0.299f, 5.603f, -1.202f),   // Mintaka -> Alnilam
                new ConstellationSegment(5.603f, -1.202f, 5.679f, -1.943f),   // Alnilam -> Alnitak
                new ConstellationSegment(5.679f, -1.943f, 5.796f, -9.670f),   // Alnitak -> Saiph
                new ConstellationSegment(5.796f, -9.670f, 5.242f, -8.202f),   // Saiph -> Rigel
                new ConstellationSegment(5.242f, -8.202f, 5.533f, -0.299f),   // Rigel -> Mintaka
                new ConstellationSegment(5.919f, 7.407f, 5.679f, -1.943f),    // Betelgeuse -> Alnitak
            });

            // =================================================================
            // LEO
            // =================================================================
            Add("Leo", new[] {
                new ConstellationSegment(10.139f, 11.967f, 10.122f, 16.763f), // Regulus -> η
                new ConstellationSegment(10.122f, 16.763f, 10.333f, 19.842f), // η -> Algieba
                new ConstellationSegment(10.333f, 19.842f, 10.278f, 23.417f), // Algieba -> ζ
                new ConstellationSegment(10.278f, 23.417f, 9.879f, 26.007f),  // ζ -> μ
                new ConstellationSegment(9.879f, 26.007f, 9.764f, 23.774f),   // μ -> ε
                new ConstellationSegment(9.764f, 23.774f, 10.122f, 16.763f),  // ε -> η (sickle)
                new ConstellationSegment(10.139f, 11.967f, 11.237f, 15.430f), // Regulus -> θ
                new ConstellationSegment(11.237f, 15.430f, 11.235f, 20.524f), // θ -> Zosma
                new ConstellationSegment(11.235f, 20.524f, 11.818f, 14.572f), // Zosma -> Denebola
                new ConstellationSegment(11.237f, 15.430f, 11.818f, 14.572f), // θ -> Denebola
            });

            // =================================================================
            // SCORPIUS
            // =================================================================
            Add("Scorpius", new[] {
                new ConstellationSegment(16.490f, -26.432f, 16.353f, -25.593f), // Antares -> σ
                new ConstellationSegment(16.353f, -25.593f, 16.006f, -22.622f), // σ -> Dschubba
                new ConstellationSegment(16.006f, -22.622f, 16.091f, -19.806f), // Dschubba -> Graffias
                new ConstellationSegment(16.490f, -26.432f, 16.598f, -28.216f), // Antares -> τ
                new ConstellationSegment(16.598f, -28.216f, 16.836f, -34.293f), // τ -> ε
                new ConstellationSegment(16.836f, -34.293f, 16.865f, -38.048f), // ε -> μ¹
                new ConstellationSegment(16.865f, -38.048f, 16.897f, -42.362f), // μ¹ -> ζ¹
                new ConstellationSegment(16.897f, -42.362f, 17.202f, -43.239f), // ζ¹ -> η
                new ConstellationSegment(17.202f, -43.239f, 17.622f, -42.998f), // η -> Sargas
                new ConstellationSegment(17.622f, -42.998f, 17.793f, -40.127f), // Sargas -> ι¹
                new ConstellationSegment(17.793f, -40.127f, 17.560f, -37.104f), // ι¹ -> Shaula
                new ConstellationSegment(17.560f, -37.104f, 17.530f, -37.296f), // Shaula -> Lesath
            });

            // =================================================================
            // CYGNUS (Northern Cross)
            // =================================================================
            Add("Cygnus", new[] {
                new ConstellationSegment(20.690f, 45.280f, 20.370f, 40.257f), // Deneb -> Sadr
                new ConstellationSegment(20.370f, 40.257f, 19.938f, 35.083f), // Sadr -> η
                new ConstellationSegment(19.938f, 35.083f, 19.512f, 27.960f), // η -> Albireo
                new ConstellationSegment(20.370f, 40.257f, 19.750f, 45.131f), // Sadr -> δ
                new ConstellationSegment(20.370f, 40.257f, 20.770f, 33.970f), // Sadr -> Gienah
            });

            // =================================================================
            // LYRA
            // =================================================================
            Add("Lyra", new[] {
                new ConstellationSegment(18.616f, 38.784f, 18.746f, 37.605f), // Vega -> ζ
                new ConstellationSegment(18.746f, 37.605f, 18.835f, 33.363f), // ζ -> Sheliak
                new ConstellationSegment(18.835f, 33.363f, 18.982f, 32.690f), // Sheliak -> Sulafat
                new ConstellationSegment(18.982f, 32.690f, 18.908f, 36.899f), // Sulafat -> δ²
                new ConstellationSegment(18.908f, 36.899f, 18.746f, 37.605f), // δ² -> ζ
            });

            // =================================================================
            // AQUILA (The Eagle)
            // =================================================================
            Add("Aquila", new[] {
                new ConstellationSegment(19.846f, 8.868f, 19.771f, 10.613f),  // Altair -> Tarazed
                new ConstellationSegment(19.846f, 8.868f, 19.922f, 6.407f),   // Altair -> Alshain
                new ConstellationSegment(19.771f, 10.613f, 19.090f, 13.863f), // Tarazed -> ζ
                new ConstellationSegment(19.922f, 6.407f, 19.874f, 1.006f),   // Alshain -> η
                new ConstellationSegment(19.874f, 1.006f, 20.188f, -0.822f),  // η -> θ
                new ConstellationSegment(19.874f, 1.006f, 19.425f, 3.115f),   // η -> δ
            });

            // =================================================================
            // TAURUS
            // =================================================================
            Add("Taurus", new[] {
                new ConstellationSegment(4.599f, 16.510f, 4.478f, 15.962f),   // Aldebaran -> θ²
                new ConstellationSegment(4.478f, 15.962f, 4.330f, 15.628f),   // θ² -> γ
                new ConstellationSegment(4.330f, 15.628f, 4.382f, 17.543f),   // γ -> δ¹
                new ConstellationSegment(4.382f, 17.543f, 4.477f, 19.181f),   // δ¹ -> ε
                new ConstellationSegment(4.599f, 16.510f, 5.627f, 21.143f),   // Aldebaran -> ζ
                new ConstellationSegment(4.477f, 19.181f, 5.438f, 28.608f),   // ε -> Elnath
                new ConstellationSegment(5.627f, 21.143f, 5.438f, 28.608f),   // ζ -> Elnath
            });

            // =================================================================
            // GEMINI
            // =================================================================
            Add("Gemini", new[] {
                new ConstellationSegment(7.577f, 31.888f, 7.755f, 28.026f),   // Castor -> Pollux
                new ConstellationSegment(7.755f, 28.026f, 7.741f, 24.398f),   // Pollux -> κ
                new ConstellationSegment(7.741f, 24.398f, 7.336f, 21.982f),   // κ -> δ
                new ConstellationSegment(7.336f, 21.982f, 7.068f, 20.570f),   // δ -> ζ
                new ConstellationSegment(7.068f, 20.570f, 6.628f, 16.399f),   // ζ -> Alhena
                new ConstellationSegment(7.577f, 31.888f, 6.732f, 25.131f),   // Castor -> Mebsuta
                new ConstellationSegment(6.732f, 25.131f, 6.383f, 22.514f),   // Mebsuta -> Tejat
                new ConstellationSegment(6.383f, 22.514f, 6.248f, 22.507f),   // Tejat -> η
            });

            // =================================================================
            // VIRGO
            // =================================================================
            Add("Virgo", new[] {
                new ConstellationSegment(13.420f, -11.161f, 13.166f, -5.539f), // Spica -> θ
                new ConstellationSegment(13.166f, -5.539f, 12.694f, -1.449f),  // θ -> Porrima
                new ConstellationSegment(12.694f, -1.449f, 12.926f, 3.397f),   // Porrima -> δ
                new ConstellationSegment(12.926f, 3.397f, 13.036f, 10.959f),   // δ -> Vindemiatrix
                new ConstellationSegment(12.694f, -1.449f, 12.332f, -0.667f),  // Porrima -> η
                new ConstellationSegment(12.332f, -0.667f, 11.845f, 1.765f),   // η -> Zavijava
                new ConstellationSegment(13.420f, -11.161f, 13.578f, -0.596f), // Spica -> ζ
                new ConstellationSegment(13.578f, -0.596f, 12.926f, 3.397f),   // ζ -> δ
            });

            // =================================================================
            // SAGITTARIUS (Teapot)
            // =================================================================
            Add("Sagittarius", new[] {
                new ConstellationSegment(18.403f, -34.385f, 18.350f, -29.828f), // Kaus Aust -> δ
                new ConstellationSegment(18.350f, -29.828f, 18.466f, -25.421f), // δ -> Kaus Bor
                new ConstellationSegment(18.466f, -25.421f, 18.761f, -26.991f), // Kaus Bor -> φ
                new ConstellationSegment(18.761f, -26.991f, 18.921f, -26.297f), // φ -> Nunki
                new ConstellationSegment(18.921f, -26.297f, 19.116f, -27.670f), // Nunki -> τ
                new ConstellationSegment(19.116f, -27.670f, 19.043f, -29.880f), // τ -> ζ
                new ConstellationSegment(19.043f, -29.880f, 18.403f, -34.385f), // ζ -> Kaus Aust
                new ConstellationSegment(18.921f, -26.297f, 19.043f, -29.880f), // Nunki -> ζ
                new ConstellationSegment(18.350f, -29.828f, 18.097f, -30.424f), // δ -> Alnasl
            });

            // =================================================================
            // CANIS MAJOR
            // =================================================================
            Add("Canis Major", new[] {
                new ConstellationSegment(6.752f, -16.716f, 6.378f, -17.956f),  // Sirius -> Mirzam
                new ConstellationSegment(6.752f, -16.716f, 7.140f, -26.393f),  // Sirius -> Wezen
                new ConstellationSegment(7.140f, -26.393f, 7.402f, -29.303f),  // Wezen -> Aludra
                new ConstellationSegment(7.140f, -26.393f, 6.977f, -28.972f),  // Wezen -> Adhara
                new ConstellationSegment(6.977f, -28.972f, 7.030f, -27.935f),  // Adhara -> σ
            });

            // =================================================================
            // CANIS MINOR
            // =================================================================
            Add("Canis Minor", new[] {
                new ConstellationSegment(7.655f, 5.225f, 7.452f, 8.290f),      // Procyon -> Gomeisa
            });

            // =================================================================
            // PEGASUS (Great Square)
            // =================================================================
            Add("Pegasus", new[] {
                new ConstellationSegment(23.079f, 15.205f, 23.063f, 28.083f),  // Markab -> Scheat
                new ConstellationSegment(23.079f, 15.205f, 0.220f, 15.184f),   // Markab -> Algenib
                new ConstellationSegment(0.220f, 15.184f, 0.140f, 29.091f),    // Algenib -> Alpheratz
                new ConstellationSegment(0.140f, 29.091f, 23.063f, 28.083f),   // Alpheratz -> Scheat
                new ConstellationSegment(23.079f, 15.205f, 21.736f, 9.875f),   // Markab -> Enif
            });

            // =================================================================
            // ANDROMEDA
            // =================================================================
            Add("Andromeda", new[] {
                new ConstellationSegment(0.140f, 29.091f, 0.656f, 30.861f),    // Alpheratz -> δ
                new ConstellationSegment(0.656f, 30.861f, 1.163f, 35.621f),    // δ -> Mirach
                new ConstellationSegment(1.163f, 35.621f, 2.065f, 42.330f),    // Mirach -> Almach
            });

            // =================================================================
            // PERSEUS
            // =================================================================
            Add("Perseus", new[] {
                new ConstellationSegment(3.405f, 49.861f, 3.715f, 47.788f),    // Mirfak -> δ
                new ConstellationSegment(3.715f, 47.788f, 3.964f, 40.010f),    // δ -> ε
                new ConstellationSegment(3.964f, 40.010f, 3.902f, 31.884f),    // ε -> ζ
                new ConstellationSegment(3.405f, 49.861f, 3.080f, 53.506f),    // Mirfak -> γ
                new ConstellationSegment(3.080f, 53.506f, 3.136f, 40.957f),    // γ -> Algol
                new ConstellationSegment(3.136f, 40.957f, 3.086f, 38.840f),    // Algol -> ρ
                new ConstellationSegment(3.405f, 49.861f, 2.843f, 55.896f),    // Mirfak -> η
            });

            // =================================================================
            // AURIGA
            // =================================================================
            Add("Auriga", new[] {
                new ConstellationSegment(5.278f, 45.998f, 5.992f, 44.947f),    // Capella -> Menkalinan
                new ConstellationSegment(5.278f, 45.998f, 5.033f, 43.823f),    // Capella -> ε
                new ConstellationSegment(5.033f, 43.823f, 5.041f, 41.076f),    // ε -> ζ
                new ConstellationSegment(5.041f, 41.076f, 4.950f, 33.166f),    // ζ -> ι
                new ConstellationSegment(5.992f, 44.947f, 5.995f, 37.213f),    // Menkalinan -> θ
                new ConstellationSegment(5.995f, 37.213f, 4.950f, 33.166f),    // θ -> ι
                new ConstellationSegment(4.950f, 33.166f, 5.438f, 28.608f),    // ι -> Elnath (shared)
            });

            // =================================================================
            // BOÖTES
            // =================================================================
            Add("Boötes", new[] {
                new ConstellationSegment(14.261f, 19.183f, 14.750f, 27.074f),  // Arcturus -> Izar
                new ConstellationSegment(14.750f, 27.074f, 15.258f, 33.315f),  // Izar -> δ
                new ConstellationSegment(14.261f, 19.183f, 13.911f, 18.398f),  // Arcturus -> Muphrid
                new ConstellationSegment(14.261f, 19.183f, 14.530f, 30.371f),  // Arcturus -> ρ
                new ConstellationSegment(14.530f, 30.371f, 14.535f, 38.308f),  // ρ -> Seginus
                new ConstellationSegment(14.535f, 38.308f, 15.032f, 40.391f),  // Seginus -> Nekkar
                new ConstellationSegment(15.258f, 33.315f, 15.032f, 40.391f),  // δ -> Nekkar
            });

            // =================================================================
            // CORONA BOREALIS
            // =================================================================
            Add("Corona Borealis", new[] {
                new ConstellationSegment(15.578f, 26.715f, 15.464f, 29.106f),  // Alphecca -> β
                new ConstellationSegment(15.578f, 26.715f, 15.713f, 26.296f),  // Alphecca -> γ
                new ConstellationSegment(15.464f, 29.106f, 15.549f, 31.359f),  // β -> θ
                new ConstellationSegment(15.713f, 26.296f, 15.827f, 26.068f),  // γ -> δ
                new ConstellationSegment(15.827f, 26.068f, 15.959f, 26.878f),  // δ -> ε
            });

            // =================================================================
            // HERCULES
            // =================================================================
            Add("Hercules", new[] {
                new ConstellationSegment(16.503f, 21.490f, 16.688f, 31.603f),  // Kornephoros -> ζ
                new ConstellationSegment(16.688f, 31.603f, 16.715f, 38.922f),  // ζ -> η
                new ConstellationSegment(16.715f, 38.922f, 17.251f, 36.809f),  // η -> π
                new ConstellationSegment(17.251f, 36.809f, 17.005f, 30.926f),  // π -> ε
                new ConstellationSegment(17.005f, 30.926f, 16.688f, 31.603f),  // ε -> ζ (keystone)
                new ConstellationSegment(16.503f, 21.490f, 17.244f, 14.390f),  // Kornephoros -> Rasalgethi
                new ConstellationSegment(17.244f, 14.390f, 17.251f, 24.839f),  // Rasalgethi -> δ
                new ConstellationSegment(17.251f, 24.839f, 17.005f, 30.926f),  // δ -> ε
            });

            // =================================================================
            // OPHIUCHUS
            // =================================================================
            Add("Ophiuchus", new[] {
                new ConstellationSegment(17.582f, 12.560f, 16.961f, 9.375f),   // Rasalhague -> κ
                new ConstellationSegment(16.961f, 9.375f, 16.619f, -10.567f),  // κ -> ζ
                new ConstellationSegment(16.619f, -10.567f, 17.173f, -15.725f),// ζ -> η
                new ConstellationSegment(17.582f, 12.560f, 17.725f, 4.567f),   // Rasalhague -> Cebalrai
                new ConstellationSegment(17.725f, 4.567f, 17.985f, -9.774f),   // Cebalrai -> ν
                new ConstellationSegment(17.985f, -9.774f, 17.173f, -15.725f), // ν -> η
                new ConstellationSegment(16.961f, 9.375f, 16.239f, -3.694f),   // κ -> Yed Prior
                new ConstellationSegment(16.239f, -3.694f, 16.305f, -4.692f),  // Yed Prior -> Yed Post
            });

            // =================================================================
            // LIBRA
            // =================================================================
            Add("Libra", new[] {
                new ConstellationSegment(14.848f, -16.042f, 15.283f, -9.383f), // α² -> β
                new ConstellationSegment(15.283f, -9.383f, 15.592f, -14.790f), // β -> γ
                new ConstellationSegment(15.592f, -14.790f, 14.848f, -16.042f),// γ -> α²
                new ConstellationSegment(15.283f, -9.383f, 15.068f, -25.282f), // β -> σ
            });

            // =================================================================
            // ARIES
            // =================================================================
            Add("Aries", new[] {
                new ConstellationSegment(2.120f, 23.463f, 1.911f, 20.808f),    // Hamal -> Sheratan
                new ConstellationSegment(1.911f, 20.808f, 1.892f, 19.294f),    // Sheratan -> Mesarthim
            });

            // =================================================================
            // PISCES
            // =================================================================
            Add("Pisces", new[] {
                new ConstellationSegment(1.525f, 15.346f, 1.756f, 9.158f),     // η -> ο
                new ConstellationSegment(1.756f, 9.158f, 2.034f, 2.764f),      // ο -> Alrescha
                new ConstellationSegment(23.666f, 5.626f, 23.466f, 6.379f),    // ι -> θ
                new ConstellationSegment(23.466f, 6.379f, 23.286f, 3.282f),    // θ -> γ
                new ConstellationSegment(23.286f, 3.282f, 23.449f, 1.256f),    // γ -> κ
            });

            // =================================================================
            // AQUARIUS
            // =================================================================
            Add("Aquarius", new[] {
                new ConstellationSegment(21.526f, -5.571f, 22.096f, -0.320f),  // Sadalsuud -> Sadalmelik
                new ConstellationSegment(22.096f, -0.320f, 22.281f, -7.783f),  // Sadalmelik -> θ
                new ConstellationSegment(22.281f, -7.783f, 22.877f, -7.580f),  // θ -> λ
                new ConstellationSegment(22.877f, -7.580f, 22.911f, -15.821f), // λ -> Skat
            });

            // =================================================================
            // CAPRICORNUS
            // =================================================================
            Add("Capricornus", new[] {
                new ConstellationSegment(20.294f, -12.509f, 20.350f, -14.781f),// Algedi -> Dabih
                new ConstellationSegment(20.350f, -14.781f, 20.768f, -25.271f),// Dabih -> ψ
                new ConstellationSegment(20.768f, -25.271f, 20.863f, -26.919f),// ψ -> ω
                new ConstellationSegment(20.863f, -26.919f, 21.099f, -25.006f),// ω -> 24
                new ConstellationSegment(21.099f, -25.006f, 21.444f, -22.411f),// 24 -> ζ
                new ConstellationSegment(21.444f, -22.411f, 21.784f, -16.127f),// ζ -> Deneb Algedi
                new ConstellationSegment(21.784f, -16.127f, 21.668f, -16.662f),// Deneb Algedi -> Nashira
            });

            // =================================================================
            // CANCER
            // =================================================================
            Add("Cancer", new[] {
                new ConstellationSegment(8.975f, 11.858f, 8.745f, 18.154f),    // Acubens -> δ
                new ConstellationSegment(8.745f, 18.154f, 8.721f, 21.469f),    // δ -> Asellus Bor
                new ConstellationSegment(8.975f, 11.858f, 8.275f, 9.186f),     // Acubens -> Al Tarf
            });

            // =================================================================
            // CENTAURUS
            // =================================================================
            Add("Centaurus", new[] {
                new ConstellationSegment(14.660f, -60.835f, 14.064f, -60.373f),// Rigil Kent -> Hadar
                new ConstellationSegment(14.064f, -60.373f, 13.665f, -53.466f),// Hadar -> ε
                new ConstellationSegment(13.665f, -53.466f, 13.926f, -47.288f),// ε -> ζ
                new ConstellationSegment(13.926f, -47.288f, 14.592f, -42.158f),// ζ -> η
                new ConstellationSegment(14.592f, -42.158f, 14.111f, -36.370f),// η -> Menkent
                new ConstellationSegment(13.665f, -53.466f, 12.139f, -50.722f),// ε -> δ
                new ConstellationSegment(12.139f, -50.722f, 12.692f, -48.960f),// δ -> γ
            });

            // =================================================================
            // CRUX (Southern Cross)
            // =================================================================
            Add("Crux", new[] {
                new ConstellationSegment(12.443f, -63.099f, 12.519f, -57.113f),// Acrux -> Gacrux
                new ConstellationSegment(12.795f, -59.689f, 12.252f, -58.749f),// Mimosa -> δ
            });

            // =================================================================
            // ERIDANUS (simplified)
            // =================================================================
            Add("Eridanus", new[] {
                new ConstellationSegment(1.629f, -57.237f, 2.971f, -40.305f),  // Achernar -> θ¹
                new ConstellationSegment(2.971f, -40.305f, 2.851f, -21.004f),  // θ¹ -> τ²
                new ConstellationSegment(2.851f, -21.004f, 3.967f, -13.508f),  // τ² -> Zaurak
                new ConstellationSegment(3.967f, -13.508f, 3.721f, -9.763f),   // Zaurak -> δ
                new ConstellationSegment(3.721f, -9.763f, 3.549f, -9.458f),    // δ -> ε
                new ConstellationSegment(3.549f, -9.458f, 5.131f, -5.086f),    // ε -> Cursa
            });

            // =================================================================
            // CORVUS
            // =================================================================
            Add("Corvus", new[] {
                new ConstellationSegment(12.264f, -17.542f, 12.498f, -16.515f),// Gienah -> Algorab
                new ConstellationSegment(12.498f, -16.515f, 12.573f, -23.397f),// Algorab -> Kraz
                new ConstellationSegment(12.573f, -23.397f, 12.169f, -22.620f),// Kraz -> ε
                new ConstellationSegment(12.169f, -22.620f, 12.264f, -17.542f),// ε -> Gienah
            });

            // =================================================================
            // HYDRA (simplified main chain)
            // =================================================================
            Add("Hydra", new[] {
                new ConstellationSegment(9.460f, -8.659f, 9.664f, -1.143f),    // Alphard -> ι
                new ConstellationSegment(9.664f, -1.143f, 9.239f, -2.314f),    // ι -> θ
                new ConstellationSegment(9.239f, -2.314f, 8.923f, 5.946f),     // θ -> ζ
                new ConstellationSegment(8.923f, 5.946f, 8.780f, 6.419f),      // ζ -> ε
                new ConstellationSegment(8.780f, 6.419f, 8.628f, 5.704f),      // ε -> δ
                new ConstellationSegment(9.460f, -8.659f, 10.827f, -16.194f),  // Alphard -> ν
                new ConstellationSegment(10.827f, -16.194f, 10.176f, -12.354f),// ν -> λ
            });

            // =================================================================
            // PISCIS AUSTRINUS
            // =================================================================
            Add("Piscis Austrinus", new[] {
                new ConstellationSegment(22.961f, -29.622f, 22.932f, -32.540f),// Fomalhaut -> δ
                new ConstellationSegment(22.932f, -32.540f, 22.876f, -32.876f),// δ -> γ
                new ConstellationSegment(22.876f, -32.876f, 22.525f, -32.346f),// γ -> β
                new ConstellationSegment(22.961f, -29.622f, 22.678f, -27.044f),// Fomalhaut -> ε
            });

            // =================================================================
            // GRUS
            // =================================================================
            Add("Grus", new[] {
                new ConstellationSegment(22.137f, -46.961f, 22.711f, -46.885f),// Alnair -> β
                new ConstellationSegment(22.137f, -46.961f, 21.899f, -37.365f),// Alnair -> γ
                new ConstellationSegment(22.711f, -46.885f, 22.488f, -43.496f),// β -> δ¹
                new ConstellationSegment(21.899f, -37.365f, 22.488f, -43.496f),// γ -> δ¹
            });

            // =================================================================
            // TRIANGULUM
            // =================================================================
            Add("Triangulum", new[] {
                new ConstellationSegment(1.885f, 29.579f, 2.159f, 34.987f),    // α -> β
                new ConstellationSegment(2.159f, 34.987f, 2.289f, 33.847f),    // β -> γ
                new ConstellationSegment(2.289f, 33.847f, 1.885f, 29.579f),    // γ -> α
            });

            // =================================================================
            // TRIANGULUM AUSTRALE
            // =================================================================
            Add("Triangulum Australe", new[] {
                new ConstellationSegment(16.811f, -69.028f, 15.919f, -63.430f),// Atria -> β
                new ConstellationSegment(15.919f, -63.430f, 15.315f, -68.679f),// β -> γ
                new ConstellationSegment(15.315f, -68.679f, 16.811f, -69.028f),// γ -> Atria
            });

            // =================================================================
            // CARINA
            // =================================================================
            Add("Carina", new[] {
                new ConstellationSegment(6.399f, -52.696f, 9.220f, -69.717f),  // Canopus -> Miaplacidus
                new ConstellationSegment(9.220f, -69.717f, 10.229f, -70.038f), // Miaplacidus -> ω
                new ConstellationSegment(10.229f, -70.038f, 10.716f, -64.394f),// ω -> θ
                new ConstellationSegment(6.399f, -52.696f, 7.946f, -52.982f),  // Canopus -> χ
                new ConstellationSegment(7.946f, -52.982f, 8.375f, -59.510f),  // χ -> Avior
                new ConstellationSegment(8.375f, -59.510f, 9.285f, -59.275f),  // Avior -> ι
                new ConstellationSegment(9.285f, -59.275f, 10.716f, -64.394f), // ι -> θ
            });

            // =================================================================
            // VELA
            // =================================================================
            Add("Vela", new[] {
                new ConstellationSegment(8.159f, -47.337f, 8.745f, -54.709f),  // Regor -> δ
                new ConstellationSegment(8.745f, -54.709f, 9.368f, -55.011f),  // δ -> κ
                new ConstellationSegment(9.368f, -55.011f, 9.133f, -43.433f),  // κ -> Suhail
                new ConstellationSegment(9.133f, -43.433f, 8.159f, -47.337f),  // Suhail -> Regor
            });

            // =================================================================
            // PUPPIS
            // =================================================================
            Add("Puppis", new[] {
                new ConstellationSegment(8.059f, -40.003f, 8.126f, -24.304f),  // Naos -> ρ
                new ConstellationSegment(8.059f, -40.003f, 7.286f, -37.097f),  // Naos -> π
                new ConstellationSegment(7.649f, -43.196f, 6.832f, -50.615f),  // ν -> τ
            });

            // =================================================================
            // SAGITTA
            // =================================================================
            Add("Sagitta", new[] {
                new ConstellationSegment(19.979f, 19.492f, 19.789f, 18.534f),  // γ -> δ
                new ConstellationSegment(19.789f, 18.534f, 19.668f, 18.014f),  // δ -> Sham
                new ConstellationSegment(19.789f, 18.534f, 19.684f, 17.476f),  // δ -> β
            });

            // =================================================================
            // DELPHINUS
            // =================================================================
            Add("Delphinus", new[] {
                new ConstellationSegment(20.660f, 15.912f, 20.626f, 14.595f),  // Sualocin -> Rotanev
                new ConstellationSegment(20.626f, 14.595f, 20.724f, 15.075f),  // Rotanev -> δ
                new ConstellationSegment(20.724f, 15.075f, 20.777f, 16.124f),  // δ -> γ
                new ConstellationSegment(20.777f, 16.124f, 20.660f, 15.912f),  // γ -> Sualocin
                new ConstellationSegment(20.554f, 11.303f, 20.626f, 14.595f),  // ε -> Rotanev
            });

            // =================================================================
            // LEPUS
            // =================================================================
            Add("Lepus", new[] {
                new ConstellationSegment(5.545f, -17.822f, 5.471f, -20.759f),  // Arneb -> Nihal
                new ConstellationSegment(5.545f, -17.822f, 5.216f, -16.206f),  // Arneb -> μ
                new ConstellationSegment(5.216f, -16.206f, 5.091f, -22.371f),  // μ -> ε
                new ConstellationSegment(5.091f, -22.371f, 5.471f, -20.759f),  // ε -> Nihal
                new ConstellationSegment(5.545f, -17.822f, 5.856f, -20.879f),  // Arneb -> δ
            });

            // =================================================================
            // COLUMBA
            // =================================================================
            Add("Columba", new[] {
                new ConstellationSegment(5.661f, -34.074f, 5.849f, -35.768f),  // Phact -> β
                new ConstellationSegment(5.661f, -34.074f, 5.520f, -35.471f),  // Phact -> ε
            });

            // =================================================================
            // CANES VENATICI
            // =================================================================
            Add("Canes Venatici", new[] {
                new ConstellationSegment(12.934f, 38.318f, 12.562f, 41.358f),  // Cor Caroli -> Chara
            });

            // =================================================================
            // SERPENS CAPUT
            // =================================================================
            Add("Serpens", new[] {
                new ConstellationSegment(15.737f, 6.425f, 15.580f, 10.539f),   // Unukalhai -> δ
                new ConstellationSegment(15.580f, 10.539f, 15.769f, 15.422f),  // δ -> β
                new ConstellationSegment(15.769f, 15.422f, 15.940f, 15.661f),  // β -> γ
                new ConstellationSegment(15.737f, 6.425f, 15.846f, 4.477f),    // Unukalhai -> ε
            });

            // =================================================================
            // LUPUS
            // =================================================================
            Add("Lupus", new[] {
                new ConstellationSegment(14.699f, -47.388f, 14.976f, -43.134f),// α -> β
                new ConstellationSegment(14.976f, -43.134f, 15.356f, -40.648f),// β -> δ
                new ConstellationSegment(15.356f, -40.648f, 15.586f, -41.167f),// δ -> γ
                new ConstellationSegment(15.586f, -41.167f, 15.378f, -44.690f),// γ -> ε
                new ConstellationSegment(15.378f, -44.690f, 14.699f, -47.388f),// ε -> α
            });

            // =================================================================
            // ARA
            // =================================================================
            Add("Ara", new[] {
                new ConstellationSegment(17.531f, -49.876f, 17.422f, -55.530f),// α -> β
                new ConstellationSegment(17.422f, -55.530f, 17.424f, -56.378f),// β -> γ
                new ConstellationSegment(17.531f, -49.876f, 16.977f, -55.990f),// α -> ζ
                new ConstellationSegment(16.977f, -55.990f, 16.829f, -53.160f),// ζ -> ε¹
            });

            // =================================================================
            // PAVO
            // =================================================================
            Add("Pavo", new[] {
                new ConstellationSegment(20.428f, -56.735f, 20.145f, -66.182f),// Peacock -> δ
                new ConstellationSegment(20.145f, -66.182f, 20.749f, -66.203f),// δ -> β
                new ConstellationSegment(20.428f, -56.735f, 20.749f, -66.203f),// Peacock -> β
            });

            // =================================================================
            // PHOENIX
            // =================================================================
            Add("Phoenix", new[] {
                new ConstellationSegment(0.438f, -42.306f, 1.102f, -46.718f),  // Ankaa -> β
                new ConstellationSegment(1.102f, -46.718f, 1.473f, -43.319f),  // β -> γ
                new ConstellationSegment(0.438f, -42.306f, 0.158f, -45.747f),  // Ankaa -> ε
            });

            // =================================================================
            // CRATER
            // =================================================================
            Add("Crater", new[] {
                new ConstellationSegment(10.996f, -18.299f, 11.194f, -22.826f),// Alkes -> β
                new ConstellationSegment(11.194f, -22.826f, 11.415f, -17.684f),// β -> γ
                new ConstellationSegment(11.415f, -17.684f, 11.322f, -14.778f),// γ -> δ
                new ConstellationSegment(11.322f, -14.778f, 10.996f, -18.299f),// δ -> Alkes
            });

            // =================================================================
            // CORONA AUSTRALIS
            // =================================================================
            Add("Corona Australis", new[] {
                new ConstellationSegment(19.158f, -37.905f, 19.167f, -39.341f),// α -> β
                new ConstellationSegment(19.167f, -39.341f, 19.139f, -40.497f),// β -> δ
                new ConstellationSegment(19.107f, -37.063f, 19.158f, -37.905f),// γ -> α
            });

            // =================================================================
            // MUSCA
            // =================================================================
            Add("Musca", new[] {
                new ConstellationSegment(12.620f, -69.136f, 12.771f, -68.108f),// α -> β
                new ConstellationSegment(12.771f, -68.108f, 13.038f, -71.549f),// β -> δ
                new ConstellationSegment(13.038f, -71.549f, 12.542f, -72.133f),// δ -> γ
                new ConstellationSegment(12.542f, -72.133f, 12.620f, -69.136f),// γ -> α
            });

            // =================================================================
            // SCUTUM
            // =================================================================
            Add("Scutum", new[] {
                new ConstellationSegment(18.587f, -8.244f, 18.786f, -4.748f),  // α -> β
                new ConstellationSegment(18.786f, -4.748f, 18.706f, -9.053f),  // β -> δ
            });

            // =================================================================
            // VULPECULA
            // =================================================================
            Add("Vulpecula", new[] {
                new ConstellationSegment(19.478f, 24.665f, 19.893f, 24.080f),  // α -> 13
            });

            // =================================================================
            // CETUS
            // =================================================================
            Add("Cetus", new[] {
                new ConstellationSegment(0.727f, -17.987f, 0.324f, -8.824f),   // Diphda -> ι
                new ConstellationSegment(0.324f, -8.824f, 1.143f, -10.182f),   // ι -> η
                new ConstellationSegment(1.143f, -10.182f, 1.400f, -8.183f),   // η -> θ
                new ConstellationSegment(3.038f, 4.090f, 2.722f, 3.236f),      // Menkar -> γ
                new ConstellationSegment(2.722f, 3.236f, 2.658f, 0.329f),      // γ -> δ
                new ConstellationSegment(2.658f, 0.329f, 2.322f, -2.978f),     // δ -> Mira
                new ConstellationSegment(2.322f, -2.978f, 0.727f, -17.987f),   // Mira -> Diphda
            });

            // =================================================================
            // FORNAX
            // =================================================================
            Add("Fornax", new[] {
                new ConstellationSegment(3.201f, -28.987f, 2.818f, -32.406f),  // Dalim -> β
                new ConstellationSegment(2.818f, -32.406f, 2.073f, -29.297f),  // β -> ν
            });

            // =================================================================
            // SCULPTOR
            // =================================================================
            Add("Sculptor", new[] {
                new ConstellationSegment(0.977f, -29.358f, 23.815f, -28.131f), // α -> δ
                new ConstellationSegment(23.815f, -28.131f, 23.313f, -32.532f),// δ -> γ
            });

            // =================================================================
            // HYDRUS
            // =================================================================
            Add("Hydrus", new[] {
                new ConstellationSegment(1.980f, -61.570f, 0.429f, -77.254f),  // α -> β
                new ConstellationSegment(0.429f, -77.254f, 3.787f, -74.239f),  // β -> γ
                new ConstellationSegment(3.787f, -74.239f, 1.980f, -61.570f),  // γ -> α
            });

            // =================================================================
            // TUCANA
            // =================================================================
            Add("Tucana", new[] {
                new ConstellationSegment(22.309f, -60.260f, 23.290f, -58.236f),// α -> γ
                new ConstellationSegment(23.290f, -58.236f, 0.526f, -62.958f), // γ -> β¹
                new ConstellationSegment(0.526f, -62.958f, 0.334f, -64.875f),  // β¹ -> ζ
            });

            // =================================================================
            // LACERTA (simplified)
            // =================================================================
            Add("Lacerta", new[] {
                new ConstellationSegment(22.522f, 50.283f, 22.393f, 52.229f),  // α -> β
                new ConstellationSegment(22.393f, 52.229f, 22.409f, 49.476f),  // β -> 4
            });

            // =================================================================
            // MONOCEROS (simplified)
            // =================================================================
            Add("Monoceros", new[] {
                new ConstellationSegment(7.687f, -9.551f, 6.481f, -7.033f),    // α -> β
                new ConstellationSegment(6.481f, -7.033f, 6.247f, -6.275f),    // β -> γ
            });

            // =================================================================
            // DORADO
            // =================================================================
            Add("Dorado", new[] {
                new ConstellationSegment(4.567f, -55.045f, 5.560f, -62.490f),  // α -> β
                new ConstellationSegment(5.560f, -62.490f, 5.746f, -65.735f),  // β -> δ
                new ConstellationSegment(4.567f, -55.045f, 4.267f, -51.487f),  // α -> γ
            });

            // =================================================================
            // PICTOR
            // =================================================================
            Add("Pictor", new[] {
                new ConstellationSegment(6.803f, -61.941f, 5.788f, -51.067f),  // α -> β
                new ConstellationSegment(6.803f, -61.941f, 5.831f, -56.167f),  // α -> γ
            });

            // =================================================================
            // RETICULUM
            // =================================================================
            Add("Reticulum", new[] {
                new ConstellationSegment(4.240f, -62.474f, 3.737f, -64.807f),  // α -> β
                new ConstellationSegment(3.737f, -64.807f, 3.979f, -61.400f),  // β -> δ
                new ConstellationSegment(3.979f, -61.400f, 4.275f, -59.302f),  // δ -> ε
                new ConstellationSegment(4.275f, -59.302f, 4.240f, -62.474f),  // ε -> α
            });

            // =================================================================
            // VOLANS
            // =================================================================
            Add("Volans", new[] {
                new ConstellationSegment(9.041f, -66.396f, 8.429f, -66.137f),  // α -> β
                new ConstellationSegment(8.429f, -66.137f, 8.132f, -68.617f),  // β -> ε
                new ConstellationSegment(8.132f, -68.617f, 7.281f, -67.957f),  // ε -> δ
                new ConstellationSegment(7.281f, -67.957f, 7.146f, -70.499f),  // δ -> γ²
            });

            // =================================================================
            // INDUS
            // =================================================================
            Add("Indus", new[] {
                new ConstellationSegment(20.626f, -47.292f, 20.913f, -58.454f),// α -> β
                new ConstellationSegment(20.626f, -47.292f, 21.331f, -53.449f),// α -> θ
            });

            return defs;
        }

        // =====================================================================
        //  DEBUG OVERLAY
        // =====================================================================

        private void DrawDebugOverlay(Color[] pixels, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u_base = (float)x / (width - 1);
                    float v_base = (float)y / (height - 1);

                    float u_unmirrored = _mirrorHorizontal ? 1.0f - u_base : u_base;
                    float v_unmirrored = _mirrorVertical ? 1.0f - v_base : v_base;

                    float u_unshifted = u_unmirrored - _horizontalShift;
                    if (u_unshifted < 0) u_unshifted += 1.0f;
                    if (u_unshifted >= 1.0f) u_unshifted -= 1.0f;

                    float raHours = 0, decDeg = 0, galB = 0;

                    if (_projection == ProjectionMode.Equatorial)
                    {
                        raHours = u_unshifted * 24.0f;
                        decDeg = 90.0f - v_unmirrored * 180.0f;
                        galB = EquatorialToGalactic(raHours, decDeg).y;
                    }
                    else
                    {
                        float galL = u_unshifted * 360.0f;
                        galB = 90.0f - v_unmirrored * 180.0f;
                        Vector2 eq = GalacticToEquatorial(galL, galB);
                        raHours = eq.x;
                        decDeg = eq.y;
                    }

                    bool draw = false;
                    Color overlay = Color.clear;

                    if (Mathf.Abs(galB) < 1.5f) { overlay += new Color(0, 0, 2.0f, 1); draw = true; }
                    if (Mathf.Abs(decDeg) < 0.2f) { overlay += new Color(2.0f, 0, 0, 1); draw = true; }
                    if (raHours < 0.03f || raHours > 23.97f) { overlay += new Color(0, 2.0f, 0, 1); draw = true; }
                    if (Mathf.Abs(decDeg) < 2.0f && Mathf.Abs(raHours - 6.0f) < 0.08f)
                    {
                        overlay += new Color(2.0f, 2.0f, 0, 1); draw = true;
                    }

                    if (draw)
                    {
                        pixels[y * width + x] += overlay;
                    }
                }
            }
        }

        // =====================================================================
        //  COORDINATE SYSTEMS
        // =====================================================================

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
            Vector3 gal = new Vector3(
                Vector3.Dot(eq, _xGal), Vector3.Dot(eq, _yGal), Vector3.Dot(eq, _zGal));
            float l = Mathf.Atan2(gal.y, gal.x) * Mathf.Rad2Deg;
            float b = Mathf.Asin(gal.z) * Mathf.Rad2Deg;
            if (l < 0) l += 360f;
            return new Vector2(l, b);
        }

        private Vector2 GalacticToEquatorial(float lDeg, float bDeg)
        {
            float l = lDeg * Mathf.Deg2Rad;
            float b = bDeg * Mathf.Deg2Rad;
            Vector3 gal = new Vector3(
                Mathf.Cos(b) * Mathf.Cos(l), Mathf.Cos(b) * Mathf.Sin(l), Mathf.Sin(b));
            Vector3 eq = gal.x * _xGal + gal.y * _yGal + gal.z * _zGal;
            float ra = Mathf.Atan2(eq.y, eq.x) * Mathf.Rad2Deg / 15.0f;
            float dec = Mathf.Asin(eq.z) * Mathf.Rad2Deg;
            if (ra < 0) ra += 24.0f;
            return new Vector2(ra, dec);
        }

        // =====================================================================
        //  RENDERING HELPERS
        // =====================================================================

        /// <summary>
        /// Draws a star as a gaussian dot, horizontally stretched near poles
        /// to compensate for equirectangular compression.
        /// </summary>
        private void DrawStar(Color[] pixels, int w, int h, int cx, int cy,
            float radius, Color col, float intensity, float decDeg)
        {
            float cosDec = Mathf.Cos(decDeg * Mathf.Deg2Rad);
            float hStretch = 1.0f / Mathf.Max(cosDec, 0.1f);

            int rx = Mathf.CeilToInt(radius * hStretch);
            int ry = Mathf.CeilToInt(radius);
            float rSqBase = radius * radius;

            for (int dy = -ry; dy <= ry; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= h) continue;

                for (int dx = -rx; dx <= rx; dx++)
                {
                    int px = cx + dx;
                    if (px < 0) px += w;
                    else if (px >= w) px -= w;

                    float ndx = dx / hStretch;
                    float distSq = ndx * ndx + dy * dy;
                    if (distSq <= rSqBase)
                    {
                        float alpha = Mathf.Exp(-distSq / (rSqBase * 0.5f));
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

        /// <summary>
        /// Adjusts the saturation of a star color by lerping between
        /// its perceptual luminance (grayscale) and the original color.
        /// Values > 1 boost saturation, < 1 desaturate.
        /// </summary>
        private Color AdjustStarSaturation(Color color, float saturation)
        {
            float gray = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return new Color(
                Mathf.Max(0f, Mathf.LerpUnclamped(gray, color.r, saturation)),
                Mathf.Max(0f, Mathf.LerpUnclamped(gray, color.g, saturation)),
                Mathf.Max(0f, Mathf.LerpUnclamped(gray, color.b, saturation)),
                1.0f
            );
        }

        private void SaveTexture(Texture2D tex)
        {
            string path = _saveAsEXR ? SAVE_PATH_EXR : SAVE_PATH_PNG;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            byte[] bytes = _saveAsEXR
                ? tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP)
                : tex.EncodeToPNG();
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