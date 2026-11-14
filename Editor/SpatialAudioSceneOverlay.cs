using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Scene view overlay for spatial audio. Draws the listener, probe rays, opening
    /// direction, emitter positions, and volume labels for the selected system.
    /// </summary>
    [InitializeOnLoad]
    internal static class SpatialAudioSceneOverlay
    {
        private const string PREF_ENABLED = "HorizonWT_SpatialAudioOverlay";
        private const string PREF_ALL = "HorizonWT_SpatialAudioOverlayAll";

        private static bool Enabled
        {
            get { return EditorPrefs.GetBool(PREF_ENABLED, true); }
            set { EditorPrefs.SetBool(PREF_ENABLED, value); }
        }

        private static bool DrawAllSystems
        {
            get { return EditorPrefs.GetBool(PREF_ALL, false); }
            set { EditorPrefs.SetBool(PREF_ALL, value); }
        }

        static SpatialAudioSceneOverlay()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Tools/Horizon/Spatial Audio/Toggle Scene View Overlay")]
        private static void ToggleOverlay()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Horizon/Spatial Audio/Overlay: Draw All Systems")]
        private static void ToggleDrawAll()
        {
            DrawAllSystems = !DrawAllSystems;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Horizon/Spatial Audio/Overlay: Draw All Systems", true)]
        private static bool ToggleDrawAllValidate()
        {
            Menu.SetChecked("Tools/Horizon/Spatial Audio/Overlay: Draw All Systems", DrawAllSystems);
            return true;
        }

        [MenuItem("Tools/Horizon/Spatial Audio/Toggle Scene View Overlay", true)]
        private static bool ToggleOverlayValidate()
        {
            Menu.SetChecked("Tools/Horizon/Spatial Audio/Toggle Scene View Overlay", Enabled);
            return true;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!Enabled) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            if (DrawAllSystems)
            {
                SpatialAudioSystem[] systems = Object.FindObjectsOfType<SpatialAudioSystem>();
                for (int i = 0; i < systems.Length; i++) DrawSystem(systems[i]);
            }
            else
            {
                SpatialAudioSystem system = ResolveSelectedSystem();
                if (system != null) DrawSystem(system);
            }
        }

        private static SpatialAudioSystem ResolveSelectedSystem()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return null;

            SpatialAudioSystem system = selected.GetComponentInParent<SpatialAudioSystem>();
            if (system != null) return system;

            WeatherAudioManager weatherAudio = selected.GetComponentInParent<WeatherAudioManager>();
            if (weatherAudio != null) return weatherAudio.audioSystem;

            WeatherAudioManager weatherAudioInChildren = selected.GetComponentInChildren<WeatherAudioManager>(true);
            if (weatherAudioInChildren != null) return weatherAudioInChildren.audioSystem;

            return SpatialAudioVisualizerGUI.FindSystemFromSelection();
        }

        private static void DrawSystem(SpatialAudioSystem system)
        {
            if (system == null) return;

            Vector3 listener = system.ListenerPosition;

            // Listener
            Handles.color = SpatialAudioVisualizerGUI.ListenerColor;
            Handles.DrawSolidDisc(listener, Vector3.up, 0.18f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(listener, Vector3.up, 0.30f);

            // Opening arrow
            if (system.OpeningStrength > 0.02f)
            {
                Vector3 direction = system.OpeningDirection;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    direction.Normalize();

                    float length = system.HasCeiling && system.OpeningDepth > 0.1f
                        ? system.OpeningDepth
                        : 2f;

                    Vector3 end = listener + direction * length;

                    Handles.color = SpatialAudioVisualizerGUI.OpeningColor;
                    Handles.DrawAAPolyLine(3f, listener, end);
                    Handles.DrawLine(end, end - direction * 0.3f + Vector3.Cross(Vector3.up, direction) * 0.18f);
                    Handles.DrawLine(end, end - direction * 0.3f - Vector3.Cross(Vector3.up, direction) * 0.18f);

                    string openingLabel = system.HasCeiling && system.OpeningDepth > 0.1f
                        ? "OPENING " + system.OpeningDepth.ToString("F1") + " m  " + Mathf.RoundToInt(system.OpeningProximity * 100f) + "%"
                        : "OPENING " + Mathf.RoundToInt(system.OpeningStrength * 100f) + "%";

                    Handles.Label(end + Vector3.up * 0.18f, openingLabel);
                }
            }

            // Probe rays
            if (system.showProbeRaysInSceneView && system.enableAcousticProbes)
            {
                for (int i = 0; i < system.ProbeCount; i++)
                {
                    Vector3 origin = system.GetProbeOrigin(i);
                    Vector3 end = system.GetProbeEnd(i);
                    if (end == Vector3.zero && origin == Vector3.zero) continue;

                    bool open = system.GetProbeOpen(i) > 0.5f;
                    SpatialAudioProbeKind kind = (SpatialAudioProbeKind)system.GetProbeKind(i);

                    Color color;
                    if (kind == SpatialAudioProbeKind.Wall || kind == SpatialAudioProbeKind.OutsideWall)
                    {
                        color = open ? new Color(0.3f, 0.9f, 0.9f, 0.45f) : new Color(1f, 0.45f, 0.35f, 0.35f);
                    }
                    else
                    {
                        color = open ? new Color(0.45f, 0.75f, 1f, 0.45f) : new Color(1f, 0.6f, 0.3f, 0.35f);
                    }

                    Handles.color = color;
                    Handles.DrawAAPolyLine(1.5f, origin, end);
                }
            }

            // Emitters
            if (system.emitters != null)
            {
                int twoDStack = 0;

                for (int i = 0; i < system.emitters.Length; i++)
                {
                    SpatialAudioEmitter emitter = system.emitters[i];
                    if (emitter == null) continue;

                    Color color = SpatialAudioVisualizerGUI.GetEmitterColor(emitter);
                    Vector3 position = emitter.ResolvedPosition;

                    Handles.color = new Color(color.r, color.g, color.b, 0.25f);
                    Handles.DrawAAPolyLine(1.5f, listener, position);

                    if (emitter.spatialBlend > 0.01f)
                    {
                        Handles.color = new Color(color.r, color.g, color.b, 0.12f);
                        Vector3 orbitCenter = new Vector3(listener.x, listener.y + emitter.verticalOffset, listener.z);
                        Handles.DrawWireDisc(orbitCenter, Vector3.up, Mathf.Max(0.05f, emitter.orbitRadius));

                        if (Selection.activeGameObject == emitter.gameObject)
                        {
                            Handles.DrawWireDisc(position, Vector3.up, Mathf.Max(0.05f, emitter.maxDistance));
                        }
                    }

                    Handles.color = color;
                    Handles.SphereHandleCap(0, position, Quaternion.identity, 0.22f, EventType.Repaint);

                    GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
                    style.normal.textColor = color;
                    string label = emitter.name + "  " + Mathf.RoundToInt(emitter.CurrentVolume * 100f) + "%";

                    Vector3 labelPosition = position + Vector3.up * 0.32f;

                    if (emitter.spatialBlend <= 0.01f)
                    {
                        label = "[2D] " + label;
                        labelPosition = listener + Vector3.up * (0.42f + twoDStack * 0.20f);
                        twoDStack++;
                    }

                    Handles.Label(labelPosition, label, style);
                }
            }
        }
    }
}
