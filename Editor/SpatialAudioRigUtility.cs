using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Shared editor rigging for spatial audio: creating emitters, wiring AudioSources,
    /// keeping the system's emitter array in sync, and configuring VRChat spatialization.
    /// </summary>
    public static class SpatialAudioRigUtility
    {
        private static Type _cachedVRCSpatialAudioType;
        private static bool _vrcSpatialAudioTypeSearched;

        // -----------------------------------------------------------------
        // Emitter creation
        // -----------------------------------------------------------------

        /// <summary>
        /// Creates a new emitter under a system and appends it to the system's list.
        /// </summary>
        public static SpatialAudioEmitter CreateEmitter(
            SpatialAudioSystem system, string objectName,
            SpatialAudioPlacementMode placement, SpatialAudioCondition condition, bool is3D)
        {
            if (system == null) return null;

            GameObject go = new GameObject(objectName);
            go.transform.SetParent(system.transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            Undo.RegisterCreatedObjectUndo(go, "Create Spatial Audio Emitter");

            SpatialAudioEmitter emitter = go.AddComponent<SpatialAudioEmitter>();
            ApplyDefaults(emitter, placement, condition, is3D);

            EnsureSource(emitter, true);
            ApplyEmitterSettings(emitter);

            AppendEmitter(system, emitter);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            return emitter;
        }

        /// <summary>
        /// Finds an emitter child by name or creates it. Used by the weather rig migration.
        /// </summary>
        public static SpatialAudioEmitter EnsureEmitter(
            Transform parent, string objectName,
            SpatialAudioPlacementMode placement, SpatialAudioCondition condition, bool is3D)
        {
            if (parent == null) return null;

            Transform child = parent.Find(objectName);
            GameObject go;

            if (child == null)
            {
                go = new GameObject(objectName);
                go.transform.SetParent(parent);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
            }
            else
            {
                go = child.gameObject;
            }

            SpatialAudioEmitter emitter = go.GetComponent<SpatialAudioEmitter>();
            if (emitter == null)
            {
                emitter = go.AddComponent<SpatialAudioEmitter>();
                ApplyDefaults(emitter, placement, condition, is3D);
            }

            EnsureSource(emitter, false);
            ApplyEmitterSettings(emitter);

            return emitter;
        }

        private static void ApplyDefaults(
            SpatialAudioEmitter emitter,
            SpatialAudioPlacementMode placement,
            SpatialAudioCondition condition,
            bool is3D)
        {
            if (emitter == null) return;

            emitter.loop = true;
            emitter.volume = 1f;
            emitter.pitch = 1f;
            emitter.placement = placement;
            emitter.condition = condition;
            emitter.offset = Vector3.zero;
            emitter.orbitRadius = 2.5f;
            emitter.verticalOffset = placement == SpatialAudioPlacementMode.OpeningDirection ? 0f : 1.4f;
            emitter.useTransformDirection = true;
            emitter.useOpeningDepthDistance = placement == SpatialAudioPlacementMode.OpeningDirection;
            emitter.useSystemCeilingHeight = placement == SpatialAudioPlacementMode.AboveListener;
            emitter.minimumGain = 0f;
            emitter.responseSharpness = 1f;
            emitter.volumeSmoothing = 8f;

            emitter.spatialBlend = is3D ? 1f : 0f;
            emitter.panStereo = 0f;
            emitter.useDistanceAttenuation = is3D;
            emitter.minDistance = is3D ? 1f : 0f;

            if (!is3D)
            {
                emitter.maxDistance = 1f;
            }
            else if (placement == SpatialAudioPlacementMode.AboveListener)
            {
                emitter.maxDistance = 30f;
            }
            else if (placement == SpatialAudioPlacementMode.OpeningDirection)
            {
                emitter.maxDistance = 20f;
            }
            else
            {
                emitter.maxDistance = 16f;
            }
            emitter.rolloffMode = AudioRolloffMode.Linear;
            emitter.spread = is3D ? 80f : 0f;
            emitter.dopplerLevel = 0f;
        }

        // -----------------------------------------------------------------
        // AudioSource wiring
        // -----------------------------------------------------------------

        /// <summary>
        /// Ensures an AudioSource exists on the emitter and applies its spatial settings.
        /// </summary>
        public static AudioSource EnsureSource(SpatialAudioEmitter emitter, bool registerUndo)
        {
            if (emitter == null) return null;

            AudioSource source = emitter.sourceOverride;
            if (source == null) source = emitter.GetComponent<AudioSource>();

#if UNITY_EDITOR
            // Support existing rigs where the AudioSource lives on a child of the emitter.
            if (source == null)
            {
                source = emitter.GetComponentInChildren<AudioSource>(true);
                if (source != null) emitter.sourceOverride = source;
            }
#endif

            if (source == null)
            {
                if (registerUndo)
                {
                    source = Undo.AddComponent<AudioSource>(emitter.gameObject);
                }
                else
                {
                    source = emitter.gameObject.AddComponent<AudioSource>();
                }
            }

            if (source != null && emitter.sourceOverride != source)
            {
                emitter.sourceOverride = source;
                EditorUtility.SetDirty(emitter);
            }

            emitter.EnsureSource();
            return source;
        }

        /// <summary>
        /// Resolves the AudioSource used by an emitter, preferring the explicit override.
        /// </summary>
        public static AudioSource GetEmitterSource(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return null;
            if (emitter.sourceOverride != null) return emitter.sourceOverride;
            return emitter.GetComponent<AudioSource>();
        }

        /// <summary>
        /// Returns true when the AudioSource settings do not match the emitter, so the
        /// rig can be repaired lazily instead of dirtying the scene on every selection.
        /// </summary>
        public static bool NeedsSourceUpdate(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return false;

            AudioSource source = GetEmitterSource(emitter);
            if (source == null) return true;

            if (source.playOnAwake) return true;
            if (source.mute) return true;
            if (source.loop != emitter.loop) return true;
            if (!Mathf.Approximately(source.spatialBlend, emitter.spatialBlend)) return true;
            if (!Mathf.Approximately(source.dopplerLevel, emitter.dopplerLevel)) return true;
            if (!Mathf.Approximately(source.spread, emitter.spread)) return true;
            if (!Mathf.Approximately(source.panStereo, emitter.panStereo)) return true;
            if (source.rolloffMode != AudioRolloffMode.Linear) return true;
            if (source.spatialize != (emitter.spatialBlend > 0.001f)) return true;

            if (emitter.spatialBlend > 0.001f)
            {
                if (!Mathf.Approximately(source.minDistance, 1000f)) return true;
                if (!Mathf.Approximately(source.maxDistance, 1001f)) return true;
            }
            else
            {
                if (!Mathf.Approximately(source.minDistance, 0f)) return true;
                if (!Mathf.Approximately(source.maxDistance, 1f)) return true;
            }

            return false;
        }

        /// <summary>
        /// Applies emitter fields to its AudioSource and configures VRChat spatial audio.
        /// </summary>
        public static void ApplyEmitterSettings(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return;

            AudioSource source = GetEmitterSource(emitter);
            if (source == null) return;

            source.playOnAwake = false;
            source.mute = false;
            source.loop = emitter.loop;
            source.volume = 0f;
            source.dopplerLevel = emitter.dopplerLevel;
            source.spatialBlend = emitter.spatialBlend;
            source.spread = emitter.spread;
            source.panStereo = emitter.panStereo;

            // The spatial system owns distance attenuation. Flatten the raw AudioSource
            // curve so Unity/VRChat do not apply a second distance attenuation.
            source.rolloffMode = AudioRolloffMode.Linear;
            if (emitter.spatialBlend > 0.001f)
            {
                source.minDistance = 1000f;
                source.maxDistance = 1001f;
            }
            else
            {
                source.minDistance = 0f;
                source.maxDistance = 1f;
            }

            source.spatialize = emitter.spatialBlend > 0.001f;

            ConfigureVRCSpatialAudioIfPresent(source.gameObject, emitter.spatialBlend > 0.001f);

            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(emitter);
            MarkSceneDirty(emitter.gameObject);
        }

        // -----------------------------------------------------------------
        // System emitter array
        // -----------------------------------------------------------------

        /// <summary>
        /// Rebuilds the system's serialized emitter array from its children and repairs
        /// every AudioSource in the process.
        /// </summary>
        public static void SyncEmitters(SpatialAudioSystem system)
        {
            if (system == null) return;

            SpatialAudioEmitter[] found = system.GetComponentsInChildren<SpatialAudioEmitter>(true);
            List<SpatialAudioEmitter> ordered = new List<SpatialAudioEmitter>();

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;

                // Only include emitters rooted under this system to avoid grabbing a
                // nested, independently managed system.
                SpatialAudioSystem owner = FindOwningSystem(found[i]);
                if (owner != null && owner != system) continue;

                ordered.Add(found[i]);
            }

            bool changed = false;

            using (SerializedObject so = new SerializedObject(system))
            {
                so.Update();
                SerializedProperty arrayProp = so.FindProperty("emitters");

                if (arrayProp != null)
                {
                    if (arrayProp.arraySize != ordered.Count)
                    {
                        changed = true;
                    }
                    else
                    {
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue != ordered[i])
                            {
                                changed = true;
                                break;
                            }
                        }
                    }

                    if (changed)
                    {
                        arrayProp.arraySize = ordered.Count;
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
                        }
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                EnsureSource(ordered[i], false);
                if (NeedsSourceUpdate(ordered[i]))
                {
                    ApplyEmitterSettings(ordered[i]);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(system);
                MarkSceneDirty(system.gameObject);
            }
        }

        public static void AppendEmitter(SpatialAudioSystem system, SpatialAudioEmitter emitter)
        {
            if (system == null || emitter == null) return;

            using (SerializedObject so = new SerializedObject(system))
            {
                so.Update();
                SerializedProperty arrayProp = so.FindProperty("emitters");
                if (arrayProp != null)
                {
                    int index = arrayProp.arraySize;
                    arrayProp.arraySize = index + 1;
                    arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = emitter;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(system);
        }

        public static void RemoveEmitter(SpatialAudioSystem system, int index)
        {
            if (system == null) return;

            SpatialAudioEmitter emitter = null;

            using (SerializedObject so = new SerializedObject(system))
            {
                so.Update();
                SerializedProperty arrayProp = so.FindProperty("emitters");
                if (arrayProp != null && index >= 0 && index < arrayProp.arraySize)
                {
                    emitter = arrayProp.GetArrayElementAtIndex(index).objectReferenceValue as SpatialAudioEmitter;
                    arrayProp.DeleteArrayElementAtIndex(index);

                    // Unity's array deletion can leave a null slot; delete again if needed.
                    if (index < arrayProp.arraySize &&
                        arrayProp.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    {
                        arrayProp.DeleteArrayElementAtIndex(index);
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(system);

            if (emitter != null)
            {
                Undo.DestroyObjectImmediate(emitter.gameObject);
            }

            SyncEmitters(system);
        }

        /// <summary>
        /// Finds the nearest SpatialAudioSystem above an emitter.
        /// </summary>
        public static SpatialAudioSystem FindOwningSystem(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return null;

            Transform current = emitter.transform.parent;
            while (current != null)
            {
                SpatialAudioSystem system = current.GetComponent<SpatialAudioSystem>();
                if (system != null) return system;
                current = current.parent;
            }

            return null;
        }

        // -----------------------------------------------------------------
        // Presets
        // -----------------------------------------------------------------

        public static void ApplyPreset(SpatialAudioEmitter emitter, SpatialAudioPreset preset)
        {
            if (emitter == null || preset == null) return;

            Undo.RecordObject(emitter, "Apply Spatial Audio Preset");
            preset.ApplyTo(emitter);
            EditorUtility.SetDirty(emitter);
            ApplyEmitterSettings(emitter);
            MarkSceneDirty(emitter.gameObject);
        }

        // -----------------------------------------------------------------
        // VRChat spatial audio support
        // -----------------------------------------------------------------

        /// <summary>
        /// Resolves the concrete non-abstract VRCSpatialAudioSource component type.
        /// </summary>
        public static Type GetVRCSpatialAudioType()
        {
            if (_vrcSpatialAudioTypeSearched) return _cachedVRCSpatialAudioType;
            _vrcSpatialAudioTypeSearched = true;

            _cachedVRCSpatialAudioType =
                Type.GetType("VRC.SDK3.Components.VRCSpatialAudioSource, VRCSDK3") ??
                Type.GetType("VRC.SDK3.Components.VRCSpatialAudioSource, VRC.SDK3");

            if (_cachedVRCSpatialAudioType == null || _cachedVRCSpatialAudioType.IsAbstract)
            {
                _cachedVRCSpatialAudioType = null;
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type candidate = assembly.GetType("VRC.SDK3.Components.VRCSpatialAudioSource");
                    if (candidate != null && !candidate.IsAbstract)
                    {
                        _cachedVRCSpatialAudioType = candidate;
                        break;
                    }
                }
            }

            return _cachedVRCSpatialAudioType;
        }

        public static void ConfigureVRCSpatialAudioIfPresent(GameObject go, bool enableSpatialization)
        {
            Type vrcSpatialType = GetVRCSpatialAudioType();
            if (vrcSpatialType == null || vrcSpatialType.IsAbstract) return;

            Component component = go.GetComponent(vrcSpatialType);
            if (component == null) component = go.AddComponent(vrcSpatialType);
            if (component == null) return;

            SerializedObject so = new SerializedObject(component);
            SerializedProperty gainProp = so.FindProperty("Gain");
            SerializedProperty nearProp = so.FindProperty("Near");
            SerializedProperty farProp = so.FindProperty("Far");
            SerializedProperty curveProp = so.FindProperty("UseAudioSourceVolumeCurve");
            SerializedProperty spatProp = so.FindProperty("EnableSpatialization");

            if (gainProp != null) gainProp.floatValue = 0f;
            if (nearProp != null) nearProp.floatValue = 0f;
            if (farProp != null) farProp.floatValue = 10000f;
            if (curveProp != null) curveProp.boolValue = true;
            if (spatProp != null) spatProp.boolValue = enableSpatialization;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // -----------------------------------------------------------------
        // Misc
        // -----------------------------------------------------------------

        public static void MarkSceneDirty(GameObject go)
        {
            if (go == null || Application.isPlaying) return;
            if (go.scene.IsValid())
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }

        /// <summary>
        /// Human-readable name for a weather stem role. Used for new emitter objects,
        /// the weather inspector summary, and name normalization. Add new roles here
        /// when extending the stem enum.
        /// </summary>
        public static string GetStemDisplayName(WeatherAudioStemType type)
        {
            switch (type)
            {
                case WeatherAudioStemType.RainExterior: return "Rain - Exterior";
                case WeatherAudioStemType.RainInterior: return "Rain - Interior";
                case WeatherAudioStemType.RainOpening: return "Rain - Opening";
                case WeatherAudioStemType.RainRoof: return "Rain - Roof";
                case WeatherAudioStemType.WindBase: return "Wind - Base";
                case WeatherAudioStemType.WindGust: return "Wind - Gust";
                default: return "Weather Stem";
            }
        }

        /// <summary>
        /// GameObject name for a new weather stem. Kept separate from the display name
        /// so future naming conventions can change without touching the mix logic.
        /// </summary>
        public static string GetStemObjectName(WeatherAudioStemType type)
        {
            return GetStemDisplayName(type);
        }

        /// <summary>
        /// True for names generated by this package. User-renamed objects are left alone.
        /// </summary>
        public static bool IsGeneratedStemName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return false;

            return objectName.StartsWith("Weather Audio ") ||
                   objectName.StartsWith("Audio_Rain_") ||
                   objectName.StartsWith("Audio_Wind_");
        }

        /// <summary>
        /// Renames an auto-generated stem object to its role name. Custom names are kept.
        /// </summary>
        public static void NormalizeStemName(SpatialAudioEmitter emitter, WeatherAudioStemType type)
        {
            if (emitter == null) return;
            if (!IsGeneratedStemName(emitter.gameObject.name)) return;

            string desired = GetStemObjectName(type);
            if (emitter.gameObject.name == desired) return;

            Undo.RecordObject(emitter.gameObject, "Rename Weather Audio Stem");
            emitter.gameObject.name = desired;
            EditorUtility.SetDirty(emitter.gameObject);
        }

        /// <summary>
        /// Human-readable placement description used by inspector diagnostics.
        /// </summary>
        public static string DescribePlacement(SpatialAudioEmitter emitter)
        {
            if (emitter == null) return "(none)";

            switch (emitter.placement)
            {
                case SpatialAudioPlacementMode.ListenerRelative:
                    return "Listener relative";

                case SpatialAudioPlacementMode.OrbitListener:
                    return "Orbit @ " + emitter.orbitRadius.ToString("F1") + " m";

                case SpatialAudioPlacementMode.AboveListener:
                    return emitter.useSystemCeilingHeight
                        ? "Overhead (ceiling height)"
                        : "Overhead +" + emitter.verticalOffset.ToString("F1") + " m";

                case SpatialAudioPlacementMode.OpeningDirection:
                    return emitter.useOpeningDepthDistance
                        ? "Opening direction (system depth)"
                        : "Opening direction @ " + emitter.orbitRadius.ToString("F1") + " m";

                case SpatialAudioPlacementMode.FixedWorld:
                    return "Fixed world position";

                default:
                    return emitter.placement.ToString();
            }
        }
    }
}
