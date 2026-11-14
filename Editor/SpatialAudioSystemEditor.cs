using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CustomEditor(typeof(SpatialAudioSystem))]
    public class SpatialAudioSystemEditor : Editor
    {
        private SpatialAudioSystem _target;

        private SerializedProperty _audioEnabledProp;
        private SerializedProperty _masterVolumeProp;
        private SerializedProperty _listenerOverrideProp;
        private SerializedProperty _trackMainCameraProp;
        private SerializedProperty _enableProbesProp;
        private SerializedProperty _verticalProbeDistanceProp;
        private SerializedProperty _lateralProbeDistanceProp;
        private SerializedProperty _singleRoofThicknessProp;
        private SerializedProperty _roofThicknessFalloffProp;
        private SerializedProperty _probesPerFrameProp;
        private SerializedProperty _occluderMaskProp;
        private SerializedProperty _acousticSmoothingProp;
        private SerializedProperty _roofSmoothingProp;
        private SerializedProperty _minimumIndoorGainProp;
        private SerializedProperty _openingProbeDistanceProp;
        private SerializedProperty _openingProbesPerFrameProp;
        private SerializedProperty _visualizerRangeProp;
        private SerializedProperty _showProbeRaysProp;
        private SerializedProperty _maxWallThicknessProp;

        private bool _showEnvironment = true;
        private bool _showSettings = false;

        private double _lastVisualRefresh;
        private double _lastRepaint;

        private readonly Dictionary<SpatialAudioEmitter, bool> _foldouts = new Dictionary<SpatialAudioEmitter, bool>();
        private readonly Dictionary<SpatialAudioEmitter, Editor> _emitterEditors = new Dictionary<SpatialAudioEmitter, Editor>();

        private void OnEnable()
        {
            _target = (SpatialAudioSystem)target;

            _audioEnabledProp = serializedObject.FindProperty("audioEnabled");
            _masterVolumeProp = serializedObject.FindProperty("masterVolume");
            _listenerOverrideProp = serializedObject.FindProperty("listenerOverride");
            _trackMainCameraProp = serializedObject.FindProperty("trackMainCamera");
            _enableProbesProp = serializedObject.FindProperty("enableAcousticProbes");
            _verticalProbeDistanceProp = serializedObject.FindProperty("verticalProbeDistance");
            _lateralProbeDistanceProp = serializedObject.FindProperty("lateralProbeDistance");
            _singleRoofThicknessProp = serializedObject.FindProperty("singleRoofMaxThickness");
            _roofThicknessFalloffProp = serializedObject.FindProperty("roofThicknessFalloff");
            _probesPerFrameProp = serializedObject.FindProperty("probesPerFrame");
            _occluderMaskProp = serializedObject.FindProperty("occluderMask");
            _acousticSmoothingProp = serializedObject.FindProperty("acousticSmoothing");
            _roofSmoothingProp = serializedObject.FindProperty("roofSmoothing");
            _minimumIndoorGainProp = serializedObject.FindProperty("minimumIndoorGain");
            _openingProbeDistanceProp = serializedObject.FindProperty("openingProbeDistance");
            _openingProbesPerFrameProp = serializedObject.FindProperty("openingProbesPerFrame");
            _visualizerRangeProp = serializedObject.FindProperty("visualizerRange");
            _showProbeRaysProp = serializedObject.FindProperty("showProbeRaysInSceneView");
            _maxWallThicknessProp = serializedObject.FindProperty("maxWallThickness");

            if (!Application.isPlaying)
            {
                SpatialAudioRigUtility.SyncEmitters(_target);
            }

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_target != null && _target.IsEditorPreview)
            {
                _target.SetEditorPreview(false);
                SpatialAudioPreviewDriver.Unregister(_target);
            }

            foreach (KeyValuePair<SpatialAudioEmitter, Editor> pair in _emitterEditors)
            {
                if (pair.Value != null) DestroyImmediate(pair.Value);
            }
            _emitterEditors.Clear();
        }

        private void OnEditorUpdate()
        {
            if (_target == null) return;
            if (!Application.isPlaying && !_target.IsEditorPreview) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < 0.05) return;
            _lastRepaint = now;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            HorizonEditorUtils.DrawHorizonHeader("Spatial Audio System", this);

            DrawToolbar();
            RefreshVisualsIfNeeded();

            EditorGUILayout.Space(2f);

            Rect radarRect = EditorGUILayout.GetControlRect(false, 270f);
            SpatialAudioVisualizerGUI.DrawRadar(radarRect, _target);
            SpatialAudioVisualizerGUI.DrawLegend();

            EditorGUILayout.Space(3f);

            _showEnvironment = EditorGUILayout.Foldout(_showEnvironment, "Acoustic Environment", true, EditorStyles.foldoutHeader);
            if (_showEnvironment)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                SpatialAudioVisualizerGUI.DrawEnvironment(_target);
                EditorGUILayout.EndVertical();
            }

            _showSettings = EditorGUILayout.Foldout(_showSettings, "System Settings", true, EditorStyles.foldoutHeader);
            if (_showSettings)
            {
                DrawSettings();
            }

            DrawEmitterList();

            serializedObject.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------
        // Header and live refresh
        // ---------------------------------------------------------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            Color previousColor = GUI.color;
            GUI.color = _target.audioEnabled ? new Color(0.6f, 1f, 0.7f) : new Color(1f, 0.55f, 0.55f);
            GUILayout.Label(_target.audioEnabled ? "● AUDIO ACTIVE" : "● AUDIO MUTED", EditorStyles.boldLabel, GUILayout.Width(120f));
            GUI.color = previousColor;

            GUILayout.Label("Emitters: " + CountEmitters(), EditorStyles.miniLabel, GUILayout.Width(70f));
            GUILayout.Label("Probes: " + _target.probesPerFrame + "/" + _target.ProbeCount, EditorStyles.miniLabel, GUILayout.Width(75f));
            GUILayout.FlexibleSpace();

            if (Application.isPlaying)
            {
                GUILayout.Label("LIVE", EditorStyles.miniBoldLabel, GUILayout.Width(38f));
            }
            else
            {
                bool preview = _target.IsEditorPreview;
                Color old = GUI.backgroundColor;
                GUI.backgroundColor = preview ? new Color(1f, 0.55f, 0.35f) : new Color(0.55f, 0.8f, 1f);

                if (GUILayout.Button(preview ? "Stop Preview" : "Preview Audio", GUILayout.Width(105f), GUILayout.Height(20f)))
                {
                    if (preview)
                    {
                        _target.SetEditorPreview(false);
                        SpatialAudioPreviewDriver.Unregister(_target);
                    }
                    else
                    {
                        _target.SetEditorPreview(true);
                        SpatialAudioPreviewDriver.Register(_target);
                    }
                    SceneView.RepaintAll();
                }

                GUI.backgroundColor = old;
            }

            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying && _target.IsEditorPreview)
            {
                EditorGUILayout.HelpBox("Preview uses the active Scene view camera as the listener. Audio starts only while preview is enabled.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void RefreshVisualsIfNeeded()
        {
            if (Application.isPlaying || _target.IsEditorPreview) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastVisualRefresh < 0.1) return;
            _lastVisualRefresh = now;

            _target.EditorRefresh(SpatialAudioVisualizerGUI.GetPreviewListener(_target));
        }

        private int CountEmitters()
        {
            if (_target.emitters == null) return 0;

            int count = 0;
            for (int i = 0; i < _target.emitters.Length; i++)
            {
                if (_target.emitters[i] != null) count++;
            }
            return count;
        }

        // ---------------------------------------------------------------
        // Settings
        // ---------------------------------------------------------------

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_audioEnabledProp, new GUIContent("Audio Enabled"));
            EditorGUILayout.PropertyField(_masterVolumeProp, new GUIContent("Master Volume"));

            EditorGUILayout.Space(4f);
            GUILayout.Label("Listener", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_listenerOverrideProp, new GUIContent("Listener Override"));
            EditorGUILayout.PropertyField(_trackMainCameraProp, new GUIContent("Track Main Camera"));

            EditorGUILayout.Space(4f);
            GUILayout.Label("Acoustic Probes", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableProbesProp, new GUIContent("Enable Probes"));
            EditorGUI.BeginDisabledGroup(!_target.enableAcousticProbes);
            EditorGUILayout.PropertyField(_probesPerFrameProp, new GUIContent("Probes Per Frame"));
            EditorGUILayout.PropertyField(_verticalProbeDistanceProp, new GUIContent("Vertical Distance"));
            EditorGUILayout.PropertyField(_lateralProbeDistanceProp, new GUIContent("Lateral Distance"));
            EditorGUILayout.PropertyField(_singleRoofThicknessProp, new GUIContent("Single Roof Thickness"));
            EditorGUILayout.PropertyField(_roofThicknessFalloffProp, new GUIContent("Roof Thickness Falloff"));
            EditorGUILayout.PropertyField(_acousticSmoothingProp, new GUIContent("Acoustic Smoothing"));
            EditorGUILayout.PropertyField(_roofSmoothingProp, new GUIContent("Roof Smoothing"));
            EditorGUILayout.PropertyField(_minimumIndoorGainProp, new GUIContent("Minimum Indoor Gain"));
            EditorGUILayout.PropertyField(_maxWallThicknessProp, new GUIContent("Max Wall Thickness"));
            EditorGUILayout.PropertyField(_openingProbeDistanceProp, new GUIContent("Opening Probe Distance"));
            EditorGUILayout.PropertyField(_openingProbesPerFrameProp, new GUIContent("Opening Probes Per Frame"));

            string[] layerNames = new string[32];
            for (int i = 0; i < 32; i++)
            {
                string layerName = LayerMask.LayerToName(i);
                layerNames[i] = string.IsNullOrEmpty(layerName) ? "Layer " + i : i + ": " + layerName;
            }

            EditorGUI.BeginChangeCheck();
            int mask = EditorGUILayout.MaskField(new GUIContent("Occluder Mask", "Layers treated as acoustic geometry."), _target.occluderMask, layerNames);
            if (EditorGUI.EndChangeCheck() && _occluderMaskProp != null)
            {
                _occluderMaskProp.intValue = mask;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4f);
            GUILayout.Label("Visualization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_visualizerRangeProp, new GUIContent("Radar Range (m)"));
            EditorGUILayout.PropertyField(_showProbeRaysProp, new GUIContent("Scene View Probe Rays"));

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Distance attenuation is applied per emitter by the system. Raw AudioSource curves are flattened to avoid double attenuation. Listener-following placements (orbit/overhead/opening) keep a fixed distance by design; use Fixed World for world-space falloff.",
                MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sync Rig"))
            {
                SpatialAudioRigUtility.SyncEmitters(_target);
                serializedObject.Update();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Open Full Visualizer"))
            {
                SpatialAudioVisualizerWindow.Open(_target);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------
        // Emitter list
        // ---------------------------------------------------------------

        private void DrawEmitterList()
        {
            HorizonEditorUtils.DrawSectionHeader("EMITTERS");

            DrawAddEmitterRow();

            if (_target.emitters == null || _target.emitters.Length == 0)
            {
                EditorGUILayout.HelpBox("No emitters yet. Add one above, or let the weather rig create its six stems automatically.", MessageType.Info);
                return;
            }

            int removeIndex = -1;

            for (int i = 0; i < _target.emitters.Length; i++)
            {
                SpatialAudioEmitter emitter = _target.emitters[i];

                if (emitter == null)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Missing emitter reference", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Remove Slot", GUILayout.Width(90f))) removeIndex = i;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }

                DrawEmitterCard(emitter, i, ref removeIndex);
            }

            if (removeIndex >= 0)
            {
                SpatialAudioRigUtility.RemoveEmitter(_target, removeIndex);
                serializedObject.Update();
                SceneView.RepaintAll();
            }
        }

        private void DrawAddEmitterRow()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("+ Add Emitter", GUILayout.Height(23f)))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Orbiting Source"), false, () => CreateEmitter("Orbiting Source", SpatialAudioPlacementMode.OrbitListener, SpatialAudioCondition.Always, true));
                menu.AddItem(new GUIContent("Roof Source"), false, () => CreateEmitter("Roof Source", SpatialAudioPlacementMode.AboveListener, SpatialAudioCondition.UnderRoof, true));
                menu.AddItem(new GUIContent("Opening Source"), false, () => CreateEmitter("Opening Source", SpatialAudioPlacementMode.OpeningDirection, SpatialAudioCondition.Opening, true));
                menu.AddItem(new GUIContent("Wall Source"), false, () => CreateEmitter("Wall Source", SpatialAudioPlacementMode.FixedWorld, SpatialAudioCondition.Always, true));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("2D Bed"), false, () => CreateEmitter("2D Bed", SpatialAudioPlacementMode.ListenerRelative, SpatialAudioCondition.Always, false));
                menu.ShowAsContext();
            }

            if (GUILayout.Button("Sync Rig", GUILayout.Width(80f), GUILayout.Height(23f)))
            {
                SpatialAudioRigUtility.SyncEmitters(_target);
                serializedObject.Update();
                SceneView.RepaintAll();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Click an emitter on the radar to select it.", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void CreateEmitter(string name, SpatialAudioPlacementMode placement, SpatialAudioCondition condition, bool is3D)
        {
            SpatialAudioRigUtility.CreateEmitter(_target, name, placement, condition, is3D);
            serializedObject.Update();
            SceneView.RepaintAll();
        }

        private void DrawEmitterCard(SpatialAudioEmitter emitter, int index, ref int removeIndex)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            Rect dotRect = GUILayoutUtility.GetRect(10f, 18f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(dotRect.x + 1f, dotRect.y + 5f, 8f, 8f), SpatialAudioVisualizerGUI.GetEmitterColor(emitter));
            }

            bool expanded;
            if (!_foldouts.TryGetValue(emitter, out expanded)) expanded = false;
            expanded = EditorGUILayout.Foldout(expanded, emitter.name, true);
            _foldouts[emitter] = expanded;

            GUILayout.FlexibleSpace();

            Rect barRect = GUILayoutUtility.GetRect(60f, 10f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y + 5f, barRect.width, 8f), new Color(0f, 0f, 0f, 0.35f));
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y + 5f, barRect.width * Mathf.Clamp01(emitter.CurrentVolume), 8f),
                    SpatialAudioVisualizerGUI.GetEmitterColor(emitter));
            }

            GUILayout.Label(Mathf.RoundToInt(emitter.CurrentVolume * 100f) + "%", EditorStyles.miniLabel, GUILayout.Width(34f));
            GUILayout.Label(SpatialAudioVisualizerGUI.GetEmitterStateLabel(emitter), EditorStyles.miniLabel, GUILayout.Width(130f));

            EditorGUILayout.EndHorizontal();

            if (expanded)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(SpatialAudioRigUtility.DescribePlacement(emitter) + "  ·  " + SpatialAudioVisualizerGUI.ConditionName(emitter.condition) + "  ·  " + emitter.ResolvedDistance.ToString("F1") + " m", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (emitter.clip != null)
                {
                    GUILayout.Label(emitter.clip.name, EditorStyles.miniLabel);
                }
                else
                {
                    GUILayout.Label(emitter.IsExternallyDriven ? "(external clip)" : "(no clip)", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();

                Editor emitterEditor = GetEmitterEditor(emitter);
                if (emitterEditor != null)
                {
                    emitterEditor.OnInspectorGUI();
                }

                if (EditorGUI.EndChangeCheck())
                {
                    SpatialAudioRigUtility.ApplyEmitterSettings(emitter);
                    SceneView.RepaintAll();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select", GUILayout.Width(60f)))
            {
                Selection.activeGameObject = emitter.gameObject;
                EditorGUIUtility.PingObject(emitter.gameObject);
            }
            if (GUILayout.Button("Ping Clip", GUILayout.Width(70f)) && emitter.clip != null)
            {
                EditorGUIUtility.PingObject(emitter.clip);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(65f)))
            {
                removeIndex = index;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private Editor GetEmitterEditor(SpatialAudioEmitter emitter)
        {
            Editor cached;
            if (_emitterEditors.TryGetValue(emitter, out cached) && cached != null) return cached;

            cached = Editor.CreateEditor(emitter);
            _emitterEditors[emitter] = cached;
            return cached;
        }

        // ---------------------------------------------------------------
        // Menu items
        // ---------------------------------------------------------------

        [MenuItem("GameObject/Horizon/Spatial Audio System", false, 11)]
        private static void CreateSpatialAudioSystem(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Horizon Spatial Audio");
            go.AddComponent<SpatialAudioSystem>();

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Horizon Spatial Audio System");

            Selection.activeObject = go;
        }

        [MenuItem("Tools/Horizon/Spatial Audio/Open Visualizer")]
        private static void OpenVisualizer()
        {
            SpatialAudioVisualizerWindow.Open(null);
        }
    }

    /// <summary>
    /// Drives editor-time audio preview outside Play Mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class SpatialAudioPreviewDriver
    {
        private static readonly List<SpatialAudioSystem> Systems = new List<SpatialAudioSystem>();
        private static double _lastTime;

        static SpatialAudioPreviewDriver()
        {
            EditorApplication.update += Update;
        }

        public static void Register(SpatialAudioSystem system)
        {
            if (system == null) return;
            if (!Systems.Contains(system)) Systems.Add(system);
            _lastTime = EditorApplication.timeSinceStartup;
        }

        public static void Unregister(SpatialAudioSystem system)
        {
            Systems.Remove(system);
        }

        private static void Update()
        {
            if (Systems.Count == 0) return;

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTime);
            _lastTime = now;

            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);

            bool anyAlive = false;

            for (int i = Systems.Count - 1; i >= 0; i--)
            {
                SpatialAudioSystem system = Systems[i];

                if (system == null || !system.IsEditorPreview || Application.isPlaying)
                {
                    Systems.RemoveAt(i);
                    continue;
                }

                system.EditorTick(dt, SpatialAudioVisualizerGUI.GetPreviewListener(system));
                anyAlive = true;
            }

            if (anyAlive)
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
