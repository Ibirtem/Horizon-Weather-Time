using UnityEditor;
using UnityEngine;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BlackHorizon.HorizonWeatherTime
{
    [CustomEditor(typeof(WeatherTimeSystem))]
    public class WeatherTimeSystemEditor : Editor
    {
        private WeatherTimeSystem _target;

        // --- PROPERTIES ---
        private SerializedProperty _useRealTimeProp;
        private SerializedProperty _timeZoneOffsetProp;
        private SerializedProperty _sunTimeOfDayProp;
        private SerializedProperty _moonTimeOfDayProp;
        private SerializedProperty _timeSpeedProp;
        private SerializedProperty _allowedWeatherProfilesProp;
        private SerializedProperty _currentProfileIndexProp;

        // --- INDEPENDENT LAYER PROPERTIES ---
        private SerializedProperty _lightingIndexProp;
        private SerializedProperty _skyIndexProp;
        private SerializedProperty _cloudIndexProp;
        private SerializedProperty _moonIndexProp;
        private SerializedProperty _fogIndexProp;
        private SerializedProperty _effectsIndexProp;

        // System Refs
        private SerializedProperty _lightingManagerProp;
        private SerializedProperty _skyManagerProp;
        private SerializedProperty _weatherEffectsManagerProp;
        private SerializedProperty _reflectionManagerProp;

        // UI State
        private bool _showLayerOverrides = false;

        // VRChat State
        private bool _isVRChatProject = false;
        private bool _integrationActive = false;
        private string _statusMessage = "";

        private void OnEnable()
        {
            _target = (WeatherTimeSystem)target;

            // Linking Core Properties
            _useRealTimeProp = serializedObject.FindProperty("useRealTime");
            _timeZoneOffsetProp = serializedObject.FindProperty("timeZoneOffset");
            _sunTimeOfDayProp = serializedObject.FindProperty("_sunTimeOfDay");
            _moonTimeOfDayProp = serializedObject.FindProperty("_moonTimeOfDay");
            _timeSpeedProp = serializedObject.FindProperty("timeSpeed");

            _allowedWeatherProfilesProp = serializedObject.FindProperty("weatherProfilesList");
            _currentProfileIndexProp = serializedObject.FindProperty("_currentProfileIndex");

            // Linking Independent Layers
            _lightingIndexProp = serializedObject.FindProperty("_lightingIndex");
            _skyIndexProp = serializedObject.FindProperty("_skyIndex");
            _cloudIndexProp = serializedObject.FindProperty("_cloudIndex");
            _moonIndexProp = serializedObject.FindProperty("_moonIndex");
            _fogIndexProp = serializedObject.FindProperty("_fogIndex");
            _effectsIndexProp = serializedObject.FindProperty("_effectsIndex");

            // Modules
            _lightingManagerProp = serializedObject.FindProperty("_lightingManager");
            _skyManagerProp = serializedObject.FindProperty("_skyManager");
            _weatherEffectsManagerProp = serializedObject.FindProperty("_weatherEffectsManager");
            _reflectionManagerProp = serializedObject.FindProperty("_reflectionManager");

            // Init Logic
            CheckAndConfigureDependencies();
            CheckAndAutoSetupVRChatIntegration();

            EditorApplication.delayCall += () => { if (_target != null) _target.Refresh(); };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. HEADER
            HorizonEditorUtils.DrawHorizonHeader("Weather & Time System", this);

            // 2. SECTIONS
            DrawTimelineSection();
            DrawWeatherPresetsSection();
            DrawCoreModulesSection();

            // 3. FOOTER
            EditorGUILayout.Space(10);
            DrawVRChatStatus();

            serializedObject.ApplyModifiedProperties();
        }

        // --- SECTION DRAWERS ---

        private void DrawTimelineSection()
        {
            HorizonEditorUtils.DrawSectionHeader("TIMELINE & SIMULATION");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(_useRealTimeProp);

            if (_target.useRealTime)
            {
                EditorGUILayout.PropertyField(_timeZoneOffsetProp);
                DrawTimeDebugInfo();
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("simulateMoonPhase"));
                EditorGUILayout.PropertyField(_timeSpeedProp);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            GUI.enabled = !_target.useRealTime;
            EditorGUILayout.PropertyField(_sunTimeOfDayProp, new GUIContent("Sun Position"));

            bool lockMoon = _target.useRealTime || _target.simulateMoonPhase;
            EditorGUI.BeginDisabledGroup(lockMoon);
            EditorGUILayout.PropertyField(_moonTimeOfDayProp, new GUIContent("Moon Position"));
            EditorGUI.EndDisabledGroup();

            GUI.enabled = true;

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _target.Refresh();
            }
        }

        private void DrawWeatherPresetsSection()
        {
            HorizonEditorUtils.DrawSectionHeader("WEATHER PRESETS & LAYERS");

            if (_target.weatherProfilesList != null && _target.weatherProfilesList.Length > 0)
            {
                var profileNames = _target.weatherProfilesList.Select(p => p != null ? p.name : " (None)").ToArray();
                ValidateIndices(profileNames.Length);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // --- MASTER PRESET ---
                EditorGUI.BeginChangeCheck();
                int newMaster = EditorGUILayout.Popup("Master Preset", _currentProfileIndexProp.intValue, profileNames);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyMasterPreset(newMaster);
                }

                // --- LAYER OVERRIDES ---
                EditorGUILayout.Space(2);

                GUILayout.Label("Layer Overrides", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();

                DrawLayerDropdown("Lighting", _lightingIndexProp, profileNames);
                DrawLayerDropdown("Sky & Stars", _skyIndexProp, profileNames);
                DrawLayerDropdown("Clouds", _cloudIndexProp, profileNames);
                DrawLayerDropdown("Moon", _moonIndexProp, profileNames);
                DrawLayerDropdown("Fog", _fogIndexProp, profileNames);
                DrawLayerDropdown("Effects", _effectsIndexProp, profileNames);

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    _target.Refresh();
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }
            else
            {
                EditorGUILayout.HelpBox("No Profiles Loaded! Add Weather Profiles below.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(_allowedWeatherProfilesProp, new GUIContent("Loaded Profiles List"), true);
        }

        private void DrawCoreModulesSection()
        {
            HorizonEditorUtils.DrawSectionHeader("CORE MODULES");
            GUI.enabled = false;
            EditorGUILayout.PropertyField(_lightingManagerProp);
            EditorGUILayout.PropertyField(_skyManagerProp);
            EditorGUILayout.PropertyField(_weatherEffectsManagerProp);
            EditorGUILayout.PropertyField(_reflectionManagerProp);
            GUI.enabled = true;
        }

        // --- HELPERS ---

        private void ValidateIndices(int length)
        {
            int maxIdx = Mathf.Max(0, length - 1);

            ClampProp(_currentProfileIndexProp, maxIdx);
            ClampProp(_lightingIndexProp, maxIdx);
            ClampProp(_skyIndexProp, maxIdx);
            ClampProp(_cloudIndexProp, maxIdx);
            ClampProp(_moonIndexProp, maxIdx);
            ClampProp(_fogIndexProp, maxIdx);
            ClampProp(_effectsIndexProp, maxIdx);
        }

        private static void ClampProp(SerializedProperty prop, int max)
        {
            int clamped = Mathf.Clamp(prop.intValue, 0, max);
            if (prop.intValue != clamped)
            {
                prop.intValue = clamped;
            }
        }

        private void ApplyMasterPreset(int newIndex)
        {
            _currentProfileIndexProp.intValue = newIndex;
            _lightingIndexProp.intValue = newIndex;
            _skyIndexProp.intValue = newIndex;
            _cloudIndexProp.intValue = newIndex;
            _moonIndexProp.intValue = newIndex;
            _fogIndexProp.intValue = newIndex;
            _effectsIndexProp.intValue = newIndex;

            serializedObject.ApplyModifiedProperties();
            _target.SetWeatherProfile(newIndex);
            EditorUtility.SetDirty(_target);
            _target.Refresh();
        }

        /// <summary>
        /// Draws a dropdown for a specific layer.
        /// </summary>
        private void DrawLayerDropdown(string label, SerializedProperty indexProp, string[] options)
        {
            int[] indices = Enumerable.Range(0, options.Length).ToArray();
            bool isOverride = indexProp.intValue != _currentProfileIndexProp.intValue;

            EditorGUILayout.BeginHorizontal();

            Color originalColor = GUI.color;
            if (isOverride) GUI.color = new Color(1f, 0.92f, 0.75f);

            indexProp.intValue = EditorGUILayout.IntPopup(label, indexProp.intValue, options, indices);

            GUI.color = originalColor;

            if (isOverride)
            {
                if (GUILayout.Button("↺", GUILayout.Width(24), GUILayout.Height(18)))
                {
                    indexProp.intValue = _currentProfileIndexProp.intValue;
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTimeDebugInfo()
        {
            if (_target.useRealTime)
            {
                DateTime currentUtc = DateTime.UtcNow;
                DateTime instanceTime = currentUtc.AddHours(_target.timeZoneOffset);
                EditorGUILayout.HelpBox($"Local Simulation Time: {instanceTime:HH:mm:ss}", MessageType.None);
            }
        }

        private void DrawVRChatStatus()
        {
            if (_isVRChatProject)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label("VRChat Integration", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(_integrationActive ? "ACTIVE" : "INACTIVE", _integrationActive ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        // ================================================================
        // DEPENDENCY & ASSET CHECKS
        // ================================================================

        private const string PROFILES_ROOT = "Assets/Horizon Weather & Time/Weather Profiles";
        private const string PRESETS_DIR = PROFILES_ROOT + "/Presets";
        private const string MODULES_DIR = PROFILES_ROOT + "/Modules";

        private void CheckAndConfigureDependencies()
        {
            var defaultPresets = new List<WeatherProfile>();

            // 1. CLEAR PRESET
            WeatherProfile clear = CheckAndCreatePreset("Default Clear", "Clear",
                (p) =>
                {
                    p.skyProfile.exposure = 0.3f;
                    p.skyProfile.turbidity = 5f;
                });

            // 2. SNOW PRESET
            WeatherProfile snow = CheckAndCreatePreset("Default Snow", "Snow",
                (p) =>
                {
                    p.lightingProfile.sunColorZenith = new Color(0.8f, 0.85f, 0.95f);
                    p.effectsProfile.weatherEffectPrefab = Resources.Load<GameObject>("Prefabs/SnowEffect");
                }, "Overcast", "Overcast");

            // 3. RAIN PRESET
            WeatherProfile rain = CheckAndCreatePreset("Default Rain", "Rain",
                (p) =>
                {
                    p.lightingProfile.sunIntensity = 0.5f;
                    p.lightingProfile.sunColorZenith = new Color(0.6f, 0.65f, 0.7f);
                    p.lightingProfile.dayAmbientColor = new Color(0.3f, 0.35f, 0.4f);
                    p.cloudProfile.coverage = 0.85f;
                    p.cloudProfile.density = 2.0f;
                    p.cloudProfile.baseColor = new Color(0.3f, 0.3f, 0.35f);
                    p.cloudProfile.shadowColor = new Color(0.1f, 0.1f, 0.15f);
                    p.effectsProfile.weatherEffectPrefab = Resources.Load<GameObject>("Prefabs/RainEffect");
                }, "Overcast", "Storm");

            if (clear != null) defaultPresets.Add(clear);
            if (snow != null) defaultPresets.Add(snow);
            if (rain != null) defaultPresets.Add(rain);

            EnsureProfilesAreInAllowedList(defaultPresets);
            CheckAndGenerateCloudTexture(defaultPresets);
            CheckAndConfigureSkyboxMaterial();
            CheckAndConfigureParticleAssets();
            CheckAndConfigureOcclusionCamera();

            AssetDatabase.SaveAssets();
        }

        // --- FACTORY METHODS ---

        private T GetOrCreateModule<T>(string moduleFolder, string assetName) where T : ScriptableObject
        {
            string dir = $"{MODULES_DIR}/{moduleFolder}";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = $"{dir}/{assetName}.asset";
            T module = AssetDatabase.LoadAssetAtPath<T>(path);

            if (module == null)
            {
                module = CreateInstance<T>();
                AssetDatabase.CreateAsset(module, path);
            }
            return module;
        }

        private WeatherProfile CheckAndCreatePreset(string presetName, string weatherType, Action<WeatherProfile> onInitialize = null, string skyType = null, string cloudType = null)
        {
            if (!Directory.Exists(PRESETS_DIR)) Directory.CreateDirectory(PRESETS_DIR);

            string path = $"{PRESETS_DIR}/{presetName}.asset";
            WeatherProfile preset = AssetDatabase.LoadAssetAtPath<WeatherProfile>(path);

            if (preset == null)
            {
                preset = CreateInstance<WeatherProfile>();
                preset.profileName = presetName;

                skyType = skyType ?? weatherType;
                cloudType = cloudType ?? weatherType;

                preset.lightingProfile = GetOrCreateModule<LightingProfile>("Lighting", $"Lighting_{weatherType}");
                preset.skyProfile = GetOrCreateModule<SkyProfile>("Sky", $"Sky_{skyType}");
                preset.cloudProfile = GetOrCreateModule<CloudProfile>("Clouds", $"Clouds_{cloudType}");
                preset.moonProfile = GetOrCreateModule<MoonProfile>("Moon", "Moon_Default");
                preset.fogProfile = GetOrCreateModule<FogProfile>("Fog", $"Fog_{weatherType}");
                preset.effectsProfile = GetOrCreateModule<EffectsProfile>("Effects", $"Effects_{weatherType}");

                if (skyType == "Overcast")
                {
                    preset.skyProfile.turbidity = 10f;
                    preset.skyProfile.exposure = 0.15f;
                    preset.skyProfile.rayleigh = 0.5f;
                }

                onInitialize?.Invoke(preset);

                EditorUtility.SetDirty(preset.lightingProfile);
                EditorUtility.SetDirty(preset.skyProfile);
                EditorUtility.SetDirty(preset.cloudProfile);
                EditorUtility.SetDirty(preset.moonProfile);
                EditorUtility.SetDirty(preset.fogProfile);
                EditorUtility.SetDirty(preset.effectsProfile);

                AssetDatabase.CreateAsset(preset, path);
                EditorUtility.SetDirty(preset);

                CheckAndAssignDeepSpaceAssets(preset);
                CheckAndAssignDefaultMoonTexture(preset);
            }

            return preset;
        }

        /// <summary>
        /// Automatically finds and assigns Star and Milky Way textures based on naming conventions.
        /// </summary>
        private void CheckAndAssignDeepSpaceAssets(WeatherProfile p)
        {
            if (p.skyProfile == null) return;

            bool isDirty = false;
            string starsFolder = "Assets/Horizon Weather & Time/Runtime/Textures/Sky";
            if (p.skyProfile.starsTexture == null)
            {
                string[] guids = AssetDatabase.FindAssets("stars t:Texture2D", new[] { starsFolder });
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    if (tex != null && tex is Texture2D && !(tex is Cubemap))
                    {
                        p.skyProfile.starsTexture = tex;
                        isDirty = true;
                        break;
                    }
                }
            }

            if (p.skyProfile.milkyWayTexture == null)
            {
                string[] guids = AssetDatabase.FindAssets("milkyway t:Texture2D", new[] { starsFolder });
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    if (tex != null && tex is Texture2D && !(tex is Cubemap))
                    {
                        p.skyProfile.milkyWayTexture = tex;
                        isDirty = true;
                        break;
                    }
                }
            }

            if (p.skyProfile.starsIntensity <= 0.01f) { p.skyProfile.starsIntensity = 1.0f; isDirty = true; }
            if (p.skyProfile.starsIntensity <= 0f) { p.skyProfile.starsIntensity = 1.0f; isDirty = true; }
            if (p.skyProfile.milkyWayIntensity <= 0f) { p.skyProfile.milkyWayIntensity = 1.0f; isDirty = true; }
            if (isDirty) EditorUtility.SetDirty(p.skyProfile);
        }

        // --- ASSET HELPERS ---

        private void CheckAndGenerateCloudTexture(List<WeatherProfile> profiles)
        {
            string cloudPath = CloudNoiseGenerator.DEFAULT_NOISE_PATH;
            string weatherMapPath = WeatherOptimizationGen.DEFAULT_WEATHER_MAP_PATH;
            string blueNoisePath = WeatherOptimizationGen.DEFAULT_BLUE_NOISE_PATH;

            Texture2D cloudTex = AssetDatabase.LoadAssetAtPath<Texture2D>(cloudPath);
            Texture2D weatherMapTex = AssetDatabase.LoadAssetAtPath<Texture2D>(weatherMapPath);
            Texture2D blueNoiseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(blueNoisePath);

            if (cloudTex == null) cloudTex = CloudNoiseGenerator.GenerateAndSaveTexture(512, 4, cloudPath);
            if (weatherMapTex == null) weatherMapTex = WeatherOptimizationGen.GenerateWeatherMap(weatherMapPath);
            if (blueNoiseTex == null) blueNoiseTex = WeatherOptimizationGen.GenerateBlueNoise(blueNoisePath);

            foreach (var p in profiles)
            {
                if (p != null && p.cloudProfile != null)
                {
                    bool isDirty = false;

                    if (p.cloudProfile.cloudNoiseTexture == null) { p.cloudProfile.cloudNoiseTexture = cloudTex; isDirty = true; }
                    if (p.cloudProfile.weatherMapTexture == null) { p.cloudProfile.weatherMapTexture = weatherMapTex; isDirty = true; }
                    if (p.cloudProfile.blueNoiseTexture == null) { p.cloudProfile.blueNoiseTexture = blueNoiseTex; isDirty = true; }

                    if (isDirty) EditorUtility.SetDirty(p.cloudProfile);
                }
            }
        }

        private void EnsureProfilesAreInAllowedList(List<WeatherProfile> profiles)
        {
            if (profiles.Count == 0) return;

            List<UnityEngine.Object> allowedList;
            if (_target.weatherProfilesList == null) allowedList = new List<UnityEngine.Object>();
            else allowedList = new List<UnityEngine.Object>(_target.weatherProfilesList);

            bool listModified = false;
            foreach (var profile in profiles)
            {
                if (profile != null && !allowedList.Contains(profile))
                {
                    allowedList.Add(profile);
                    listModified = true;
                }
            }

            if (listModified)
            {
                _target.weatherProfilesList = allowedList.ToArray();
                EditorUtility.SetDirty(_target);
                _target.Refresh();
            }
        }

        private void CheckAndAssignDefaultStarsTexture(WeatherProfile p)
        {
            if (p.skyProfile.starsTexture != null) return;
            string[] guids = AssetDatabase.FindAssets("starmap_horizon_8k t:Texture");
            if (guids.Length > 0) { p.skyProfile.starsTexture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guids[0])); EditorUtility.SetDirty(p.skyProfile); }
        }

        private void CheckAndAssignDefaultMoonTexture(WeatherProfile p)
        {
            if (p.moonProfile.moonTexture != null) return;

            string[] guids = AssetDatabase.FindAssets("lroc_color_poles_1k t:Texture");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);

                p.moonProfile.moonTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                EditorUtility.SetDirty(p.moonProfile);
            }
        }

        private void CheckAndConfigureSkyboxMaterial()
        {
            if (_skyManagerProp.objectReferenceValue == null) return;
            SkyManager skyManager = (SkyManager)_skyManagerProp.objectReferenceValue;
            SerializedObject skyManagerSO = new SerializedObject(skyManager);
            SerializedProperty skyboxMaterialProp = skyManagerSO.FindProperty("skyboxMaterial");
            if (skyboxMaterialProp.objectReferenceValue != null) return;

            const string fullPath = "Assets/Horizon Weather & Time/Materials/Horizon Skybox.mat";
            Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(fullPath);
            if (existingMat == null)
            {
                if (!Directory.Exists("Assets/Horizon Weather & Time/Materials")) Directory.CreateDirectory("Assets/Horizon Weather & Time/Materials");
                Shader shader = Shader.Find("Horizon/Procedural Skybox");
                if (shader != null)
                {
                    existingMat = new Material(shader);
                    AssetDatabase.CreateAsset(existingMat, fullPath);
                }
            }

            if (existingMat != null)
            {
                skyboxMaterialProp.objectReferenceValue = existingMat;
                skyManagerSO.ApplyModifiedProperties();
            }
        }

        private void CheckAndConfigureParticleAssets()
        {
            string texPath = "Assets/Horizon Weather & Time/Textures/DefaultSnowflake.png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            if (tex == null)
            {
                if (!Directory.Exists("Assets/Horizon Weather & Time/Textures"))
                    Directory.CreateDirectory("Assets/Horizon Weather & Time/Textures");

                int res = 64;
                tex = new Texture2D(res, res, TextureFormat.ARGB32, false);
                Color[] pixels = new Color[res * res];
                float center = res * 0.5f;
                float maxRadius = res * 0.45f;

                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        float alpha = 0f;
                        if (dist < maxRadius) { alpha = 1.0f - (dist / maxRadius); alpha = Mathf.Pow(alpha, 2.0f); }
                        pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                AssetDatabase.Refresh();

                TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null)
                {
                    importer.alphaIsTransparency = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }

            string meshPath = "Assets/Horizon Weather & Time/Resources/Meshes/GPUParticleVolume.asset";
            Mesh gpuParticleMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (gpuParticleMesh == null)
            {
                gpuParticleMesh = GenerateGPUParticleMesh(15000, meshPath);
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Horizon Weather & Time/Materials/SnowParticle_Lit_Material.mat");
            if (mat == null)
            {
                Shader gpuParticleShader = Shader.Find("Horizon/GPU Particles");
                if (gpuParticleShader == null)
                {
                    Debug.LogError("[WeatherTimeSystem] Shader 'Horizon/GPU Particles' not found! Cannot create material.");
                    return;
                }

                mat = new Material(gpuParticleShader);
                mat.SetTexture("_MainTex", tex);
                AssetDatabase.CreateAsset(mat, "Assets/Horizon Weather & Time/Materials/SnowParticle_Lit_Material.mat");
            }
            else if (mat.shader == null || mat.shader.name != "Horizon/GPU Particles")
            {
                Shader gpuParticleShader = Shader.Find("Horizon/GPU Particles");
                if (gpuParticleShader != null)
                {
                    mat.shader = gpuParticleShader;
                    mat.SetTexture("_MainTex", tex);
                    EditorUtility.SetDirty(mat);
                }
                else
                {
                    Debug.LogError("[WeatherTimeSystem] Shader 'Horizon/GPU Particles' not found! Cannot assign shader.");
                }
            }

            if (_weatherEffectsManagerProp.objectReferenceValue != null)
            {
                var effectsManager = (WeatherEffectsManager)_weatherEffectsManagerProp.objectReferenceValue;
                Transform root = effectsManager.transform;

                Transform volumeTrans = root.Find("WeatherFX_Volume");
                if (volumeTrans == null)
                {
                    GameObject volObj = new GameObject("WeatherFX_Volume");
                    volObj.transform.SetParent(root);
                    volObj.transform.localPosition = Vector3.zero;
                    volumeTrans = volObj.transform;
                }

                MeshFilter mf = volumeTrans.GetComponent<MeshFilter>();
                if (mf == null) mf = volumeTrans.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = gpuParticleMesh;

                MeshRenderer mr = volumeTrans.GetComponent<MeshRenderer>();
                if (mr == null) mr = volumeTrans.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                volumeTrans.gameObject.layer = 2;

                effectsManager.particleRenderer = mr;
                EditorUtility.SetDirty(effectsManager);
            }

            var prefab = Resources.Load<GameObject>("Prefabs/SnowEffect");
            if (prefab != null)
            {
                bool changed = false;
                var oldPs = prefab.GetComponentInChildren<ParticleSystem>();
                if (oldPs != null) { DestroyImmediate(oldPs.gameObject, true); changed = true; }

                var mf = prefab.GetComponent<MeshFilter>();
                if (mf == null) { mf = prefab.AddComponent<MeshFilter>(); changed = true; }
                if (mf.sharedMesh != gpuParticleMesh) { mf.sharedMesh = gpuParticleMesh; changed = true; }

                var mr = prefab.GetComponent<MeshRenderer>();
                if (mr == null) { mr = prefab.AddComponent<MeshRenderer>(); changed = true; }
                if (mr.sharedMaterial != mat) { mr.sharedMaterial = mat; changed = true; }

                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                if (changed) EditorUtility.SetDirty(prefab);
            }
        }

        private Mesh GenerateGPUParticleMesh(int count, string path)
        {
            Mesh mesh = new Mesh();
            mesh.name = $"GPU_Particles_Volume";
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

                float seedX = UnityEngine.Random.value;
                float seedY = UnityEngine.Random.value;
                float seedZ = UnityEngine.Random.value;
                float normalizedID = (float)i / count;

                Vector4 data = new Vector4(seedX, seedY, seedZ, normalizedID);
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

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        private void CheckAndConfigureOcclusionCamera()
        {
            if (_weatherEffectsManagerProp.objectReferenceValue == null) return;
            var effectsManager = (WeatherEffectsManager)_weatherEffectsManagerProp.objectReferenceValue;

            RenderTexture rt = GetOrUpdateOcclusionRT();

            Camera cam = GetOrUpdateOcclusionCamera(effectsManager);

            ConfigureCameraSettings(effectsManager, cam, rt);
        }

        private RenderTexture GetOrUpdateOcclusionRT()
        {
            string rtPath = "Assets/Horizon Weather & Time/Resources/Textures/WeatherOcclusion_RT.renderTexture";
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);

            if (rt == null || rt.format != RenderTextureFormat.RHalf)
            {
                string dir = Path.GetDirectoryName(rtPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (rt != null)
                {
                    rt.Release();
                    rt.format = RenderTextureFormat.RHalf;
                    rt.Create();
                    EditorUtility.SetDirty(rt);
                    Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Fixed Occlusion RT format to RHalf.</color>");
                }
                else
                {
                    rt = new RenderTexture(512, 512, 24, RenderTextureFormat.RHalf);
                    rt.name = "WeatherOcclusion_RT";
                    rt.wrapMode = TextureWrapMode.Clamp;
                    rt.filterMode = FilterMode.Bilinear;

                    AssetDatabase.CreateAsset(rt, rtPath);
                    Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Generated Occlusion RT (RHalf).</color>");
                }
                AssetDatabase.SaveAssets();
            }
            return rt;
        }

        private Camera GetOrUpdateOcclusionCamera(WeatherEffectsManager manager)
        {
            if (manager.occlusionCamera != null) return manager.occlusionCamera;

            Camera existingCam = manager.GetComponentInChildren<Camera>();
            if (existingCam != null)
            {
                manager.occlusionCamera = existingCam;
                return existingCam;
            }

            GameObject camObj = new GameObject("Weather Occlusion Camera");
            camObj.transform.SetParent(manager.transform);

            camObj.transform.localPosition = new Vector3(0, 50f, 0);
            camObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Camera newCam = camObj.AddComponent<Camera>();
            newCam.orthographic = true;
            newCam.orthographicSize = 25f;
            newCam.nearClipPlane = 0.3f;
            newCam.farClipPlane = 120f;
            newCam.depth = -100;
            newCam.allowHDR = false;
            newCam.allowMSAA = false;

            int uiLayer = LayerMask.NameToLayer("UI");
            int transparentFxLayer = LayerMask.NameToLayer("TransparentFX");
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

            int excludeMask = 0;
            if (uiLayer >= 0) excludeMask |= (1 << uiLayer);
            if (transparentFxLayer >= 0) excludeMask |= (1 << transparentFxLayer);
            if (ignoreRaycastLayer >= 0) excludeMask |= (1 << ignoreRaycastLayer);
            excludeMask |= (1 << 2);

            newCam.cullingMask = ~excludeMask;

            manager.occlusionCamera = newCam;
            EditorUtility.SetDirty(manager);

            Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Created Occlusion Camera.</color>");
            return newCam;
        }

        private void ConfigureCameraSettings(WeatherEffectsManager manager, Camera cam, RenderTexture rt)
        {
            bool isDirty = false;

            if (cam.clearFlags != CameraClearFlags.SolidColor || cam.backgroundColor != Color.white)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.white;
                isDirty = true;
            }

            if (cam.targetTexture != rt)
            {
                cam.targetTexture = rt;
                isDirty = true;
            }

            if (manager.depthReplacementShader == null)
            {
                Shader depthShader = Shader.Find("Hidden/Horizon/DepthOnly");
                if (depthShader != null)
                {
                    manager.depthReplacementShader = depthShader;
                    isDirty = true;
                    Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Assigned DepthOnly shader reference.</color>");
                }
                else
                {
                    Debug.LogError("<b><color=#FF3333>[ERROR]</color></b> <color=white>[WeatherTimeSystem] Shader 'Hidden/Horizon/DepthOnly' not found! Please ensure the file exists.</color>");
                }
            }

            if (!cam.enabled)
            {
                cam.enabled = true;
                isDirty = true;
            }

            if (isDirty)
            {
                EditorUtility.SetDirty(cam);
                EditorUtility.SetDirty(manager);
            }
        }

        // --- VRCHAT INTEGRATION ---

        private void CheckAndAutoSetupVRChatIntegration()
        {
            _integrationActive = false;
            _statusMessage = "Inactive";

            Type udonBehaviourType = Type.GetType("VRC.Udon.UdonBehaviour, VRC.Udon");
            if (udonBehaviourType == null)
            {
                _isVRChatProject = false;
                return;
            }
            _isVRChatProject = true;

            string[] guids = AssetDatabase.FindAssets("HorizonTimeDriver_Asset");
            if (guids.Length == 0)
            {
                _statusMessage = "Asset missing in Package";
                return;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            UnityEngine.Object programAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);

            if (programAsset == null)
            {
                _statusMessage = "Could not load Asset";
                return;
            }

            Component[] existingUdons = _target.GetComponents(udonBehaviourType);
            Component driverUdon = null;

            FieldInfo programSourceField = udonBehaviourType.GetField("programSource");

            if (programSourceField != null)
            {
                foreach (var udon in existingUdons)
                {
                    var source = programSourceField.GetValue(udon) as UnityEngine.Object;
                    if (source == programAsset || (source != null && source.name == programAsset.name))
                    {
                        driverUdon = udon;
                        break;
                    }
                }
            }

            if (driverUdon == null && programSourceField != null)
            {
                Debug.Log($"<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Connecting VRChat Integration...</color>", _target);

                driverUdon = _target.gameObject.AddComponent(udonBehaviourType);
                programSourceField.SetValue(driverUdon, programAsset);

                FieldInfo serializedAssetField = udonBehaviourType.GetField("serializedProgramAsset");
                if (serializedAssetField != null)
                {
                    serializedAssetField.SetValue(driverUdon, programAsset);
                }
            }

            if (driverUdon != null)
            {
                _integrationActive = true;
                _statusMessage = "ACTIVE (Driver Connected)";
                TryLinkReferences(driverUdon);
            }
            else
            {
                _statusMessage = "Error: Could not access Udon fields";
            }
        }

        private void TryLinkReferences(Component udon)
        {
            try
            {
                Type usEditorUtility = Type.GetType("UdonSharpEditor.UdonSharpEditorUtility, UdonSharp.Editor");
                if (usEditorUtility != null)
                {
                    MethodInfo getProxy = usEditorUtility.GetMethod("GetProxyBehaviour", BindingFlags.Public | BindingFlags.Static);
                    if (getProxy != null)
                    {
                        var proxy = getProxy.Invoke(null, new object[] { udon });
                        if (proxy != null)
                        {
                            Type proxyType = proxy.GetType();
                            FieldInfo targetSysField = proxyType.GetField("targetSystem");

                            if (targetSysField != null)
                            {
                                var currentVal = targetSysField.GetValue(proxy) as WeatherTimeSystem;
                                if (currentVal != _target)
                                {
                                    targetSysField.SetValue(proxy, _target);

                                    MethodInfo copyBack = usEditorUtility.GetMethod("CopyProxyToUdon", BindingFlags.Public | BindingFlags.Static);
                                    if (copyBack != null)
                                    {
                                        Type enumType = Type.GetType("UdonSharp.UdonSharpScriptExecutionSettings, UdonSharp.Editor");
                                        if (enumType != null)
                                        {
                                            object noneValue = Enum.ToObject(enumType, 0);
                                            copyBack.Invoke(null, new object[] { proxy, noneValue });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { /* Ignoring auto-linking errors */ }
        }

        // ================================================================
        // SCENE CREATION
        // ================================================================
        [MenuItem("GameObject/Horizon/Weather Time System", false, 10)]
        private static void CreateWeatherTimeSystem(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Horizon Weather & Time");

            go.AddComponent<WeatherTimeSystem>();

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            Undo.RegisterCreatedObjectUndo(go, "Create Horizon Weather Time System");

            Selection.activeObject = go;
        }
    }
}