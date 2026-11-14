using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CustomEditor(typeof(SpatialAudioEmitter))]
    public class SpatialAudioEmitterEditor : Editor
    {
        private SpatialAudioEmitter _emitter;
        private double _lastRefresh;
        private double _lastRepaint;

        private void OnEnable()
        {
            _emitter = (SpatialAudioEmitter)target;

            bool sourceChanged = false;

            if (_emitter.sourceOverride == null)
            {
                AudioSource source = _emitter.GetComponent<AudioSource>();
                if (source == null) source = _emitter.GetComponentInChildren<AudioSource>(true);
                if (source != null)
                {
                    _emitter.sourceOverride = source;
                    sourceChanged = true;
                }
            }

            SpatialAudioRigUtility.EnsureSource(_emitter, false);
            if (SpatialAudioRigUtility.NeedsSourceUpdate(_emitter))
            {
                SpatialAudioRigUtility.ApplyEmitterSettings(_emitter);
            }

            if (sourceChanged) EditorUtility.SetDirty(_emitter);

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < 0.05) return;
            _lastRepaint = now;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            HorizonEditorUtils.DrawHorizonHeader("Spatial Audio Emitter", this);

            SpatialAudioSystem system = SpatialAudioRigUtility.FindOwningSystem(_emitter);
            RefreshVisuals(system);

            DrawStatus(system);
            SpatialAudioPreset preset = DrawPresetField();

            EditorGUILayout.Space(3f);

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                SpatialAudioRigUtility.ApplyEmitterSettings(_emitter);
                SceneView.RepaintAll();
            }

            DrawActions(system, preset);

            serializedObject.ApplyModifiedProperties();
        }

        private SpatialAudioPreset DrawPresetField()
        {
            SpatialAudioPreset preset = _emitter.presetAsset as SpatialAudioPreset;

            EditorGUI.BeginChangeCheck();
            preset = (SpatialAudioPreset)EditorGUILayout.ObjectField(
                new GUIContent("Preset", "Optional Spatial Audio Preset asset."),
                preset, typeof(SpatialAudioPreset), false);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_emitter, "Assign Spatial Audio Preset");
                _emitter.presetAsset = preset;
                EditorUtility.SetDirty(_emitter);
            }

            return preset;
        }

        private void DrawStatus(SpatialAudioSystem system)
        {
            if (system == null)
            {
                EditorGUILayout.HelpBox(
                    "This emitter is not under a SpatialAudioSystem. Add it as a child of a system to make it play.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Color color = SpatialAudioVisualizerGUI.GetEmitterColor(_emitter);
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(SpatialAudioVisualizerGUI.GetEmitterStateLabel(_emitter), EditorStyles.boldLabel);
            GUI.contentColor = previous;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Placement", SpatialAudioRigUtility.DescribePlacement(_emitter));
            EditorGUILayout.LabelField("Condition", SpatialAudioVisualizerGUI.ConditionName(_emitter.condition));
            EditorGUILayout.EndHorizontal();

            SpatialAudioVisualizerGUI.DrawMeter("Condition match", _emitter.ConditionFactor, color, null);
            SpatialAudioVisualizerGUI.DrawMeter("Distance gain", _emitter.DistanceGain, color, null);
            SpatialAudioVisualizerGUI.DrawMeter("Current volume", _emitter.CurrentVolume, color, null);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Target", EditorStyles.miniLabel, GUILayout.Width(46f));
            GUILayout.Label((_emitter.TargetVolume * 100f).ToString("F0") + "%", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (_emitter.IsExternallyDriven)
            {
                GUILayout.Label("Externally driven", EditorStyles.miniLabel);
            }
            else if (_emitter.clip != null)
            {
                GUILayout.Label(_emitter.clip.name, EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("No clip assigned", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            if (system != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Distance", _emitter.ResolvedDistance.ToString("F2") + " m");
                EditorGUILayout.LabelField("Attenuation", Mathf.RoundToInt(_emitter.DistanceGain * 100f) + "%");
                EditorGUILayout.EndHorizontal();
            }

            if (_emitter.spatialBlend <= 0.01f)
            {
                EditorGUILayout.HelpBox("2D bed: distance attenuation does not apply.", MessageType.Info);
            }
            else if (!_emitter.useDistanceAttenuation)
            {
                EditorGUILayout.HelpBox("Distance attenuation is disabled for this emitter.", MessageType.Info);
            }
            else if (_emitter.placement != SpatialAudioPlacementMode.FixedWorld)
            {
                EditorGUILayout.HelpBox(
                    "This placement follows the listener. Its distance is fixed by the orbit radius / vertical offset. Use Fixed World for a source that gets quieter as you walk away.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActions(SpatialAudioSystem system, SpatialAudioPreset preset)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Repair AudioSource"))
            {
                SpatialAudioRigUtility.EnsureSource(_emitter, true);
                SpatialAudioRigUtility.ApplyEmitterSettings(_emitter);
            }

            if (system != null && GUILayout.Button("Select System"))
            {
                Selection.activeGameObject = system.gameObject;
                EditorGUIUtility.PingObject(system.gameObject);
            }

            if (preset != null && GUILayout.Button("Apply Preset"))
            {
                SpatialAudioRigUtility.ApplyPreset(_emitter, preset);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RefreshVisuals(SpatialAudioSystem system)
        {
            if (system == null || Application.isPlaying || system.IsEditorPreview) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefresh < 0.2) return;
            _lastRefresh = now;

            system.EditorRefresh(SpatialAudioVisualizerGUI.GetPreviewListener(system));
        }

        private void OnSceneGUI()
        {
            if (_emitter == null) return;

            SpatialAudioSystem system = SpatialAudioRigUtility.FindOwningSystem(_emitter);
            if (system == null) return;

            Vector3 listener = system.ListenerPosition;
            Color color = SpatialAudioVisualizerGUI.GetEmitterColor(_emitter);

            if (_emitter.spatialBlend > 0.01f)
            {
                Handles.color = new Color(color.r, color.g, color.b, 0.55f);

                if (_emitter.placement == SpatialAudioPlacementMode.OrbitListener ||
                    _emitter.placement == SpatialAudioPlacementMode.OpeningDirection)
                {
                    Handles.DrawWireDisc(listener, Vector3.up, _emitter.orbitRadius);
                }

                Handles.DrawLine(listener, _emitter.ResolvedPosition);
                Handles.color = color;
                Handles.DrawSolidDisc(_emitter.ResolvedPosition, Vector3.up, 0.12f);
            }

            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
            labelStyle.normal.textColor = color;
            Handles.Label(_emitter.ResolvedPosition + Vector3.up * 0.3f,
                _emitter.name + "  " + Mathf.RoundToInt(_emitter.CurrentVolume * 100f) + "%",
                labelStyle);

            if (_emitter.placement == SpatialAudioPlacementMode.FixedWorld)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPosition = Handles.PositionHandle(_emitter.transform.position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_emitter.transform, "Move Spatial Audio Emitter");
                    _emitter.transform.position = newPosition;
                    SpatialAudioRigUtility.MarkSceneDirty(_emitter.gameObject);
                }
            }
        }
    }
}
