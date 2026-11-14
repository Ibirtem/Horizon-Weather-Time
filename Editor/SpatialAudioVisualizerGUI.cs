using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Shared drawing helpers for the spatial audio inspector and visualizer window.
    ///
    /// The radar renders a top-down view of the listener, acoustic openings, and every
    /// emitter's resolved position. It is intentionally data-driven so the same drawing
    /// code works for weather stems and for hand-authored standalone emitters.
    /// </summary>
    public static class SpatialAudioVisualizerGUI
    {
        // ---------------------------------------------------------------
        // Palette
        // ---------------------------------------------------------------

        public static readonly Color BackgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        public static readonly Color PanelColor = new Color(0.14f, 0.15f, 0.18f, 1f);
        public static readonly Color GridColor = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color ListenerColor = new Color(0.25f, 0.62f, 1f, 1f);
        public static readonly Color AudibleColor = new Color(0.35f, 0.95f, 0.68f, 1f);
        public static readonly Color WaitingColor = new Color(1f, 0.72f, 0.30f, 1f);
        public static readonly Color SilentColor = new Color(0.58f, 0.60f, 0.66f, 1f);
        public static readonly Color OpeningColor = new Color(0.40f, 0.82f, 1f, 1f);
        public static readonly Color SkyColor = new Color(0.42f, 0.74f, 1f, 1f);
        public static readonly Color SidesColor = new Color(0.38f, 0.86f, 0.86f, 1f);
        public static readonly Color WallColor = new Color(1f, 0.66f, 0.36f, 1f);
        public static readonly Color RoofColor = new Color(0.80f, 0.55f, 1f, 1f);

        // ---------------------------------------------------------------
        // Cached resources
        // ---------------------------------------------------------------

        private static Texture2D _whiteTexture;
        private static Texture2D _discTexture;
        private static Texture2D _ringTexture;

        private static GUIStyle _tinyStyle;
        private static GUIStyle _tinyCenterStyle;
        private static GUIStyle _sectionStyle;

        private static Texture2D WhiteTexture
        {
            get
            {
                if (_whiteTexture == null)
                {
                    _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _whiteTexture.hideFlags = HideFlags.HideAndDontSave;
                    _whiteTexture.SetPixel(0, 0, Color.white);
                    _whiteTexture.Apply();
                }
                return _whiteTexture;
            }
        }

        private static Texture2D DiscTexture
        {
            get
            {
                if (_discTexture == null) _discTexture = CreateDiscTexture(64);
                return _discTexture;
            }
        }

        private static Texture2D RingTexture
        {
            get
            {
                if (_ringTexture == null) _ringTexture = CreateRingTexture(128);
                return _ringTexture;
            }
        }

        private static GUIStyle TinyStyle
        {
            get
            {
                if (_tinyStyle == null)
                {
                    _tinyStyle = new GUIStyle(EditorStyles.miniLabel);
                    _tinyStyle.fontSize = 9;
                    _tinyStyle.normal.textColor = new Color(0.78f, 0.80f, 0.85f, 1f);
                }
                return _tinyStyle;
            }
        }

        private static GUIStyle TinyCenterStyle
        {
            get
            {
                if (_tinyCenterStyle == null)
                {
                    _tinyCenterStyle = new GUIStyle(TinyStyle);
                    _tinyCenterStyle.alignment = TextAnchor.MiddleCenter;
                }
                return _tinyCenterStyle;
            }
        }

        private static GUIStyle SectionStyle
        {
            get
            {
                if (_sectionStyle == null)
                {
                    _sectionStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                    _sectionStyle.normal.textColor = new Color(0.70f, 0.73f, 0.78f, 1f);
                }
                return _sectionStyle;
            }
        }

        // ---------------------------------------------------------------
        // Public drawing API
        // ---------------------------------------------------------------

        /// <summary>
        /// Draws the full top-down radar for a system.
        /// </summary>
        public static void DrawRadar(Rect rect, SpatialAudioSystem system)
        {
            if (system == null) return;

            EditorGUI.DrawRect(rect, BackgroundColor);
            DrawBorder(rect, new Color(1f, 1f, 1f, 0.08f));

            bool has2DBeds = false;
            int emitterCount = system.emitters != null ? system.emitters.Length : 0;

            for (int i = 0; i < emitterCount; i++)
            {
                SpatialAudioEmitter emitter = system.emitters[i];
                if (emitter != null && emitter.spatialBlend <= 0.01f)
                {
                    has2DBeds = true;
                    break;
                }
            }

            float stripHeight = has2DBeds ? 28f : 0f;
            float bottomPadding = has2DBeds ? 8f : 0f;
            Rect mapRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f - stripHeight - bottomPadding);
            float radius = Mathf.Min(mapRect.width, mapRect.height) * 0.5f - 4f;

            if (radius < 20f) return;

            Vector2 center = mapRect.center;
            float range = Mathf.Max(1f, system.visualizerRange);

            DrawGrid(center, radius, range);
            DrawOpenSides(center, radius, system);
            DrawOpeningArrow(center, radius, range, system);
            DrawEmitterDots(center, radius, range, system);
            Draw2DBedStrip(rect, stripHeight, system);
            DrawListener(center, system);

            GUI.Label(new Rect(rect.xMax - 54f, rect.y + 6f, 50f, 12f), range.ToString("F0") + " m", TinyStyle);

            if (system.HasCeiling)
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 160f, 12f), "CEILING " + system.CeilingHeight.ToString("F1") + " m", TinyStyle);
            }
            else
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 160f, 12f), "OPEN SKY", TinyStyle);
            }

            if (system.HasTopRoof)
            {
                GUI.Label(new Rect(rect.x + 8f, rect.y + 18f, 160f, 12f), "ROOF " + system.TopRoofHeight.ToString("F1") + " m", TinyStyle);
            }
        }

        /// <summary>
        /// Draws the acoustic environment meters and compass row.
        /// </summary>
        public static void DrawEnvironment(SpatialAudioSystem system)
        {
            if (system == null) return;

            if (!system.enableAcousticProbes)
            {
                EditorGUILayout.HelpBox("Acoustic probes are disabled. Every emitter behaves as if it is outdoors.", MessageType.Info);
                return;
            }

            DrawMeter("Sky exposure", system.SkyExposure, SkyColor, null);
            DrawMeter("Lateral openness", system.LateralOpenness, SidesColor, null);
            DrawMeter("Wall transmission", system.WallTransmission, WallColor, null);
            DrawMeter("Roof exposure", system.RoofExposure, RoofColor, null);
            DrawMeter("Opening strength", system.OpeningStrength, OpeningColor, null);
            DrawMeter("Opening proximity", system.OpeningProximity, OpeningColor, null);

            EditorGUILayout.Space(2f);
            DrawCompassRow(system);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Ceiling", system.HasCeiling ? system.CeilingHeight.ToString("F1") + " m" : "none");
            EditorGUILayout.LabelField("Top roof", system.HasTopRoof ? system.TopRoofHeight.ToString("F1") + " m" : "none");
            EditorGUILayout.LabelField("Probes", system.probesPerFrame + "/" + system.ProbeCount + " per frame");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Opening probes", system.openingProbesPerFrame + " per frame");

            EditorGUILayout.LabelField("Opening depth", system.HasCeiling
                ? system.OpeningDepth.ToString("F1") + " m"
                : "outside");
        }

        /// <summary>
        /// Draws a single labelled meter row.
        /// </summary>
        public static void DrawMeter(string label, float value, Color color, string suffix)
        {
            value = Mathf.Clamp01(value);

            Rect row = EditorGUILayout.GetControlRect(false, 17f);
            GUI.Label(new Rect(row.x, row.y, 138f, row.height), label, EditorStyles.label);

            Rect bar = new Rect(row.x + 140f, row.y + 3f, Mathf.Max(10f, row.width - 190f), row.height - 6f);
            EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.30f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * value, bar.height), color);

            string text = (value * 100f).ToString("F0") + "%";
            if (!string.IsNullOrEmpty(suffix)) text += " " + suffix;
            GUI.Label(new Rect(bar.xMax + 4f, row.y, 46f, row.height), text, EditorStyles.miniLabel);
        }

        /// <summary>
        /// Draws the standard radar legend.
        /// </summary>
        public static void DrawLegend()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("●", ColoredStyle(AudibleColor), GUILayout.Width(12f));
            GUILayout.Label("Audible", EditorStyles.miniLabel);
            GUILayout.Space(8f);
            GUILayout.Label("●", ColoredStyle(WaitingColor), GUILayout.Width(12f));
            GUILayout.Label("Waiting for condition", EditorStyles.miniLabel);
            GUILayout.Space(8f);
            GUILayout.Label("●", ColoredStyle(SilentColor), GUILayout.Width(12f));
            GUILayout.Label("Silent", EditorStyles.miniLabel);
            GUILayout.Space(8f);
            GUILayout.Label("●", ColoredStyle(OpeningColor), GUILayout.Width(12f));
            GUILayout.Label("Opening", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Returns the listener position used by editor-time visualization.
        /// </summary>
        public static Vector3 GetPreviewListener(SpatialAudioSystem system)
        {
            if (system == null) return Vector3.zero;

            if (Application.isPlaying)
            {
                return system.ListenerPosition;
            }

            if (system.listenerOverride != null)
            {
                return system.listenerOverride.position;
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                return view.camera.transform.position;
            }

            return system.transform.position;
        }

        /// <summary>
        /// Resolves the most relevant system from the current selection.
        /// </summary>
        public static SpatialAudioSystem FindSystemFromSelection()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return null;

            SpatialAudioSystem system = selected.GetComponent<SpatialAudioSystem>();
            if (system != null) return system;

            WeatherAudioManager weatherAudio = selected.GetComponent<WeatherAudioManager>();
            if (weatherAudio != null && weatherAudio.audioSystem != null) return weatherAudio.audioSystem;

            SpatialAudioEmitter emitter = selected.GetComponent<SpatialAudioEmitter>();
            if (emitter != null) return SpatialAudioRigUtility.FindOwningSystem(emitter);

            system = selected.GetComponentInParent<SpatialAudioSystem>();
            if (system != null) return system;

            WeatherAudioManager weatherAudioInChildren = selected.GetComponentInChildren<WeatherAudioManager>(true);
            if (weatherAudioInChildren != null) return weatherAudioInChildren.audioSystem;

            return selected.GetComponentInChildren<SpatialAudioSystem>(true);
        }

        /// <summary>
        /// Color representing the emitter's current audible state.
        /// </summary>
        public static Color GetEmitterColor(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return SilentColor;

            if (emitter.CurrentVolume > 0.005f) return AudibleColor;

            if (!emitter.IsExternallyDriven && emitter.volume > 0.001f && emitter.ConditionFactor < 0.05f)
            {
                return WaitingColor;
            }

            return SilentColor;
        }

        public static string GetEmitterStateLabel(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return "Missing emitter";

            if (emitter.IsExternallyDriven)
            {
                return emitter.CurrentVolume > 0.005f ? "Driven / audible" : "Driven / silent";
            }

            if (emitter.CurrentVolume > 0.005f) return "Audible";

            if (emitter.volume > 0.001f && emitter.ConditionFactor < 0.05f)
            {
                return "Waiting for " + ConditionName(emitter.condition);
            }

            return "Silent";
        }

        public static string ConditionName(SpatialAudioCondition condition)
        {
            switch (condition)
            {
                case SpatialAudioCondition.Outdoors: return "outdoors";
                case SpatialAudioCondition.Indoors: return "indoors";
                case SpatialAudioCondition.UnderRoof: return "a roof";
                case SpatialAudioCondition.Opening: return "an opening";
                case SpatialAudioCondition.Sheltered: return "cover";
                default: return "always";
            }
        }

        // ---------------------------------------------------------------
        // Radar internals
        // ---------------------------------------------------------------

        private static void DrawGrid(Vector2 center, float radius, float range)
        {
            for (int i = 1; i <= 4; i++)
            {
                float ringRadius = radius * (i / 4f);
                DrawRing(center, ringRadius, GridColor);
            }

            DrawLine(center + new Vector2(-radius, 0f), center + new Vector2(radius, 0f), GridColor, 1f);
            DrawLine(center + new Vector2(0f, -radius), center + new Vector2(0f, radius), GridColor, 1f);

            GUI.Label(new Rect(center.x - 8f, center.y - radius - 15f, 16f, 12f), "N", TinyCenterStyle);
            GUI.Label(new Rect(center.x - 8f, center.y + radius + 3f, 16f, 12f), "S", TinyCenterStyle);
            GUI.Label(new Rect(center.x + radius + 3f, center.y - 6f, 16f, 12f), "E", TinyCenterStyle);
            GUI.Label(new Rect(center.x - radius - 19f, center.y - 6f, 16f, 12f), "W", TinyCenterStyle);

            GUI.Label(new Rect(center.x - 40f, center.y - radius * 0.5f - 14f, 80f, 12f), (range * 0.5f).ToString("F0") + " m", TinyCenterStyle);
            GUI.Label(new Rect(center.x - 40f, center.y - radius - 26f, 80f, 12f), range.ToString("F0") + " m", TinyCenterStyle);
        }

        private static void DrawOpenSides(Vector2 center, float radius, SpatialAudioSystem system)
        {
            int count = system.SectorCount;
            if (count <= 0) return;

            float range = Mathf.Max(1f, system.visualizerRange);

            for (int sector = 0; sector < count; sector++)
            {
                Vector3 dir3 = system.GetSectorDirection(sector);
                Vector2 dir = new Vector2(dir3.x, -dir3.z);

                if (system.GetSectorOpen(sector) > 0.5f)
                {
                    DrawLine(center, center + dir * radius * 0.94f, new Color(OpeningColor.r, OpeningColor.g, OpeningColor.b, 0.25f), 2f);
                }
                else
                {
                    float norm = Mathf.Clamp01(system.GetSectorWallDistance(sector) / range);
                    Vector2 hitPoint = center + dir * (radius * norm);
                    DrawLine(center, hitPoint, new Color(WallColor.r, WallColor.g, WallColor.b, 0.22f), 1.5f);
                    DrawDisc(hitPoint, 2.5f, new Color(WallColor.r, WallColor.g, WallColor.b, 0.75f));
                }
            }
        }

        private static void DrawOpeningArrow(Vector2 center, float radius, float range, SpatialAudioSystem system)
        {
            float strength = system.OpeningStrength;
            if (strength <= 0.02f) return;

            Vector3 opening = system.OpeningDirection;
            Vector2 direction = new Vector2(opening.x, -opening.z);
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            float normalizedDistance = system.HasCeiling && system.OpeningDepth > 0.01f
                ? Mathf.Clamp01(system.OpeningDepth / range)
                : Mathf.Lerp(0.2f, 0.72f, Mathf.Clamp01(strength));

            Vector2 tip = center + direction * radius * normalizedDistance;
            DrawLine(center, tip, OpeningColor, 2f);
            DrawDisc(tip, 4f, OpeningColor);

            string label = system.HasCeiling && system.OpeningDepth > 0.01f
                ? "OPENING " + system.OpeningDepth.ToString("F1") + " m"
                : "OPENING";
            GUI.Label(new Rect(tip.x + 5f, tip.y - 14f, 100f, 12f), label, TinyStyle);
        }

        private static void DrawEmitterDots(Vector2 center, float radius, float range, SpatialAudioSystem system)
        {
            if (system.emitters == null) return;

            Vector3 listener = system.ListenerPosition;
            Vector2 mouse = Event.current.mousePosition;
            bool canClick = Event.current.type == EventType.MouseDown && Event.current.button == 0;

            for (int i = 0; i < system.emitters.Length; i++)
            {
                SpatialAudioEmitter emitter = system.emitters[i];
                if (emitter == null || emitter.spatialBlend <= 0.01f) continue;

                Vector3 position = emitter.ResolvedPosition;
                Vector2 direction = new Vector2((position.x - listener.x) / range, (position.z - listener.z) / range);
                float distance = direction.magnitude;

                if (distance > 1f) direction /= distance;
                Vector2 point = center + direction * radius * Mathf.Min(distance, 1f);

                Color color = GetEmitterColor(emitter);
                DrawLine(center, point, new Color(color.r, color.g, color.b, 0.18f), 1f);

                if (Selection.activeGameObject == emitter.gameObject)
                {
                    DrawRing(point, 10f, new Color(1f, 1f, 1f, 0.8f));
                }

                if (Selection.activeGameObject == emitter.gameObject && emitter.maxDistance > 0.01f)
                {
                    float maxRadius = radius * Mathf.Clamp01(emitter.maxDistance / range);
                    DrawRing(point, maxRadius, new Color(color.r, color.g, color.b, 0.18f));
                }

                float dotRadius = Mathf.Lerp(3.5f, 8f, Mathf.Clamp01(emitter.CurrentVolume));
                if (emitter.CurrentVolume <= 0.005f)
                {
                    DrawRing(point, dotRadius + 1.5f, new Color(color.r, color.g, color.b, 0.9f));
                }
                else
                {
                    DrawDisc(point, dotRadius, color);
                }

                Rect hit = new Rect(point.x - 9f, point.y - 9f, 18f, 18f);
                bool hover = hit.Contains(mouse);

                {
                    string label = ShortName(emitter.name) + " " + Mathf.RoundToInt(emitter.CurrentVolume * 100f) + "%";
                    if (emitter.placement == SpatialAudioPlacementMode.AboveListener) label = "↑ " + label;

                    float labelY = emitter.placement == SpatialAudioPlacementMode.AboveListener ? point.y - 20f : point.y - 7f;

                    Color previous = GUI.contentColor;
                    GUI.contentColor = hover ? Color.white : color;
                    GUI.Label(new Rect(point.x + 10f, labelY, 150f, 12f), label, TinyStyle);
                    GUI.contentColor = previous;
                }

                if (hover && canClick)
                {
                    Selection.activeGameObject = emitter.gameObject;
                    Event.current.Use();
                }
            }
        }

        private static void Draw2DBedStrip(Rect rect, float stripHeight, SpatialAudioSystem system)
        {
            if (stripHeight <= 0f || system.emitters == null) return;

            int count = 0;
            for (int i = 0; i < system.emitters.Length; i++)
            {
                SpatialAudioEmitter emitter = system.emitters[i];
                if (emitter != null && emitter.spatialBlend <= 0.01f) count++;
            }
            if (count == 0) return;

            Rect strip = new Rect(rect.x + 8f, rect.yMax - stripHeight - 4f, rect.width - 16f, stripHeight - 4f);
            EditorGUI.DrawRect(strip, new Color(0f, 0f, 0f, 0.25f));
            GUI.Label(new Rect(strip.x, strip.y + 4f, 50f, 12f), "2D BEDS", SectionStyle);

            float startX = strip.x + 54f;
            float available = strip.xMax - startX;
            float pillWidth = Mathf.Max(44f, Mathf.Min(150f, available / count));

            Vector2 mouse = Event.current.mousePosition;
            bool canClick = Event.current.type == EventType.MouseDown && Event.current.button == 0;

            int index = 0;
            for (int i = 0; i < system.emitters.Length; i++)
            {
                SpatialAudioEmitter emitter = system.emitters[i];
                if (emitter == null || emitter.spatialBlend > 0.01f) continue;

                Rect pill = new Rect(startX + index * pillWidth, strip.y + 2f, pillWidth - 4f, strip.height - 4f);
                EditorGUI.DrawRect(pill, PanelColor);

                Color color = GetEmitterColor(emitter);
                EditorGUI.DrawRect(new Rect(pill.x, pill.y, pill.width * Mathf.Clamp01(emitter.CurrentVolume), pill.height),
                    new Color(color.r, color.g, color.b, 0.45f));

                if (Selection.activeGameObject == emitter.gameObject)
                {
                    DrawBorder(pill, new Color(1f, 1f, 1f, 0.65f));
                }

                GUI.Label(new Rect(pill.x + 4f, pill.y + 3f, pill.width - 8f, 12f),
                    ShortName(emitter.name) + " " + Mathf.RoundToInt(emitter.CurrentVolume * 100f) + "%", TinyStyle);

                if (pill.Contains(mouse) && canClick)
                {
                    Selection.activeGameObject = emitter.gameObject;
                    Event.current.Use();
                }

                index++;
            }
        }

        private static void DrawListener(Vector2 center, SpatialAudioSystem system)
        {
            DrawDisc(center, 6f, ListenerColor);
            DrawRing(center, 10f, new Color(1f, 1f, 1f, 0.7f));
            GUI.Label(new Rect(center.x + 12f, center.y + 2f, 90f, 12f), "LISTENER", TinyStyle);
        }

        private static void DrawCompassRow(SpatialAudioSystem system)
        {
            string[] names = { "N", "E", "S", "W" };

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Sides", SectionStyle, GUILayout.Width(42f));

            for (int side = 0; side < 4; side++)
            {
                bool open = system.GetSideOpen(side) > 0.5f;
                Color previous = GUI.contentColor;
                GUI.contentColor = open ? OpeningColor : SilentColor;
                GUILayout.Label(names[side] + (open ? " open" : " wall"), TinyStyle, GUILayout.Width(58f));
                GUI.contentColor = previous;
            }

            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------
        // Primitive drawing
        // ---------------------------------------------------------------

        private static void DrawDisc(Vector2 center, float radius, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), DiscTexture);
            GUI.color = previous;
        }

        private static void DrawRing(Vector2 center, float radius, Color color)
        {
            if (Event.current.type != EventType.Repaint || radius <= 0.1f) return;

            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), RingTexture);
            GUI.color = previous;
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            if (Event.current.type != EventType.Repaint) return;

            float distance = Vector2.Distance(start, end);
            if (distance < 0.01f) return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;

            float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, distance, width), WhiteTexture);

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static Vector2 CardinalToScreen(int side)
        {
            switch (side)
            {
                case 0: return new Vector2(0f, -1f); // north / +Z
                case 1: return new Vector2(1f, 0f);  // east / +X
                case 2: return new Vector2(0f, 1f);  // south / -Z
                default: return new Vector2(-1f, 0f); // west / -X
            }
        }

        private static GUIStyle ColoredStyle(Color color)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            return style;
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "(emitter)";
            if (name.Length <= 22) return name;
            return name.Substring(0, 20) + "…";
        }

        // ---------------------------------------------------------------
        // Texture generation
        // ---------------------------------------------------------------

        private static Texture2D CreateDiscTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / center;
                    float alpha = Mathf.Clamp01((1f - distance) / 0.35f);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateRingTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / center;
                    float alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - 0.94f) / 0.06f);
                    alpha = alpha * alpha * (3f - 2f * alpha);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
