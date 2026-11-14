using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Standalone, larger version of the spatial audio radar. Useful while placing
    /// emitters and openings, or when the system is not selected.
    /// </summary>
    public class SpatialAudioVisualizerWindow : EditorWindow
    {
        private SpatialAudioSystem _system;
        private Vector2 _scroll;
        private double _lastRefresh;
        private double _lastRepaint;

        private bool _showEnvironment = true;

        public static void Open(SpatialAudioSystem system)
        {
            SpatialAudioVisualizerWindow window = GetWindow<SpatialAudioVisualizerWindow>("Horizon Spatial Audio");
            window.minSize = new Vector2(420f, 520f);

            if (system != null) window._system = system;
            else window._system = SpatialAudioVisualizerGUI.FindSystemFromSelection();

            window.Show();
            window.Repaint();
        }

        private void OnEnable()
        {
            if (_system == null) _system = SpatialAudioVisualizerGUI.FindSystemFromSelection();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_system != null && _system.IsEditorPreview)
            {
                _system.SetEditorPreview(false);
                SpatialAudioPreviewDriver.Unregister(_system);
            }
        }

        private void OnSelectionChange()
        {
            if (_system == null) _system = SpatialAudioVisualizerGUI.FindSystemFromSelection();
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (_system == null) return;
            if (!Application.isPlaying && !_system.IsEditorPreview) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < 0.05) return;
            _lastRepaint = now;
            Repaint();
        }

        private void OnGUI()
        {
            HorizonEditorUtils.DrawHorizonHeader("Spatial Audio Visualizer", this);

            EditorGUILayout.BeginHorizontal();
            _system = (SpatialAudioSystem)EditorGUILayout.ObjectField("System", _system, typeof(SpatialAudioSystem), true);

            if (GUILayout.Button("Use Selection", GUILayout.Width(100f)))
            {
                _system = SpatialAudioVisualizerGUI.FindSystemFromSelection();
                Repaint();
            }

            if (GUILayout.Button("Find In Scene", GUILayout.Width(100f)))
            {
                SpatialAudioSystem[] systems = Object.FindObjectsOfType<SpatialAudioSystem>();
                if (systems.Length > 0) _system = systems[0];
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            if (_system == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a SpatialAudioSystem, a WeatherAudioManager, or a SpatialAudioEmitter to inspect it.",
                    MessageType.Info);
                return;
            }

            DrawPreviewBar();
            RefreshVisuals();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            float radarHeight = Mathf.Clamp(position.height * 0.50f, 300f, 520f);
            Rect radarRect = EditorGUILayout.GetControlRect(false, radarHeight);
            SpatialAudioVisualizerGUI.DrawRadar(radarRect, _system);
            SpatialAudioVisualizerGUI.DrawLegend();

            EditorGUILayout.Space(4f);

            _showEnvironment = EditorGUILayout.Foldout(_showEnvironment, "Acoustic Environment", true, EditorStyles.foldoutHeader);
            if (_showEnvironment)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                SpatialAudioVisualizerGUI.DrawEnvironment(_system);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4f);
            HorizonEditorUtils.DrawSectionHeader("EMITTERS");
            DrawEmitterTable();

            EditorGUILayout.EndScrollView();
        }

        private void DrawPreviewBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("Listener: " + _system.ListenerPosition.ToString("F1"), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (Application.isPlaying)
            {
                GUILayout.Label("LIVE", EditorStyles.miniBoldLabel, GUILayout.Width(40f));
            }
            else
            {
                bool preview = _system.IsEditorPreview;
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = preview ? new Color(1f, 0.55f, 0.35f) : new Color(0.55f, 0.8f, 1f);

                if (GUILayout.Button(preview ? "Stop Preview" : "Preview Audio", GUILayout.Width(105f)))
                {
                    if (preview)
                    {
                        _system.SetEditorPreview(false);
                        SpatialAudioPreviewDriver.Unregister(_system);
                    }
                    else
                    {
                        _system.SetEditorPreview(true);
                        SpatialAudioPreviewDriver.Register(_system);
                    }
                    SceneView.RepaintAll();
                }

                GUI.backgroundColor = old;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void RefreshVisuals()
        {
            if (Application.isPlaying || _system.IsEditorPreview) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefresh < 0.1) return;
            _lastRefresh = now;

            _system.EditorRefresh(SpatialAudioVisualizerGUI.GetPreviewListener(_system));
        }

        private void DrawEmitterTable()
        {
            if (_system.emitters == null || _system.emitters.Length == 0)
            {
                EditorGUILayout.HelpBox("This system has no emitters.", MessageType.Info);
                return;
            }

            for (int i = 0; i < _system.emitters.Length; i++)
            {
                SpatialAudioEmitter emitter = _system.emitters[i];
                if (emitter == null) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                Color color = SpatialAudioVisualizerGUI.GetEmitterColor(emitter);

                Rect dot = GUILayoutUtility.GetRect(10f, 16f);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(new Rect(dot.x + 1f, dot.y + 5f, 8f, 8f), color);
                }

                if (GUILayout.Button(emitter.name, EditorStyles.label, GUILayout.Width(170f)))
                {
                    Selection.activeGameObject = emitter.gameObject;
                    EditorGUIUtility.PingObject(emitter.gameObject);
                }

                if (GUILayout.Button(SpatialAudioRigUtility.DescribePlacement(emitter), EditorStyles.miniLabel, GUILayout.Width(150f)))
                {
                    Selection.activeGameObject = emitter.gameObject;
                }

                GUILayout.Label(SpatialAudioVisualizerGUI.GetEmitterStateLabel(emitter), EditorStyles.miniLabel, GUILayout.Width(135f));

                GUILayout.FlexibleSpace();

                Rect bar = GUILayoutUtility.GetRect(120f, 12f);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y + 4f, bar.width, 8f), new Color(0f, 0f, 0f, 0.35f));
                    EditorGUI.DrawRect(new Rect(bar.x, bar.y + 4f, bar.width * Mathf.Clamp01(emitter.CurrentVolume), 8f), color);
                }

                GUILayout.Label(Mathf.RoundToInt(emitter.CurrentVolume * 100f) + "%", EditorStyles.miniLabel, GUILayout.Width(36f));

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }
    }
}
