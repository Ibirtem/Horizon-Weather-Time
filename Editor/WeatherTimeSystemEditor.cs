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

                // --- MASTER PRESET (MACRO) ---
                EditorGUI.BeginChangeCheck();
                int newMaster = EditorGUILayout.Popup("Master Preset", _currentProfileIndexProp.intValue, profileNames);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyMasterPreset(newMaster);
                }

                // --- LAYER OVERRIDES (MIX & MATCH) ---
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
        // DEPENDENCY & ASSET CHECKS (MODULAR ARCHITECTURE)
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
            if (p.skyProfile.milkyWayIntensity <= 0f) { p.skyProfile.milkyWayIntensity = 1.0f; isDirty = true; }            if (isDirty) EditorUtility.SetDirty(p.skyProfile);
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

                        if (dist < maxRadius)
                        {

                            float t = dist / maxRadius;
                            alpha = 1.0f - t;

                            alpha = Mathf.Pow(alpha, 2.0f);
                        }

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
                Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Generated procedural snowflake texture.</color>");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Horizon Weather & Time/Materials/SnowParticle_Lit_Material.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Horizon/Lit Particle"));
                mat.SetTexture("_MainTex", tex);
                AssetDatabase.CreateAsset(mat, "Assets/Horizon Weather & Time/Materials/SnowParticle_Lit_Material.mat");
            }
            else if (mat.GetTexture("_MainTex") == null)
            {
                mat.SetTexture("_MainTex", tex);
                EditorUtility.SetDirty(mat);
            }

            var prefab = Resources.Load<GameObject>("Prefabs/SnowEffect");
            if (prefab != null)
            {
                var r = prefab.GetComponentInChildren<ParticleSystemRenderer>();
                if (r != null && r.sharedMaterial != mat)
                {
                    r.sharedMaterial = mat;
                    EditorUtility.SetDirty(prefab);
                }
            }

            // --- 2. RAIN ---
            string rainTexPath = "Assets/Horizon Weather & Time/Textures/DefaultRainDrop.png";
            Texture2D rainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(rainTexPath);

            if (rainTex == null || rainTex.width != 128)
            {
                if (!Directory.Exists("Assets/Horizon Weather & Time/Textures"))
                    Directory.CreateDirectory("Assets/Horizon Weather & Time/Textures");

                int res = 128;
                rainTex = new Texture2D(res, res, TextureFormat.ARGB32, false);
                Color[] pixels = new Color[res * res];

                Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
                float maxRadius = res * 0.45f;

                for (int y = 0; y < res; y++)
                {
                    for (int x = 0; x < res; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), center);
                        float alpha = 0f;

                        if (dist < maxRadius)
                        {
                            float t = dist / maxRadius;
                            float val = 1.0f - t;

                            val = Mathf.Pow(val, 3.0f);

                            alpha = val * 0.8f;
                        }

                        pixels[y * res + x] = new Color(0.8f, 0.8f, 0.8f, alpha);
                    }
                }

                rainTex.SetPixels(pixels);
                rainTex.Apply();

                File.WriteAllBytes(rainTexPath, rainTex.EncodeToPNG());
                AssetDatabase.Refresh();

                TextureImporter importer = AssetImporter.GetAtPath(rainTexPath) as TextureImporter;
                if (importer != null)
                {
                    importer.alphaIsTransparency = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }

                rainTex = AssetDatabase.LoadAssetAtPath<Texture2D>(rainTexPath);
                Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Generated procedural TRANSPARENT rain texture.</color>");
            }

            Material rainMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Horizon Weather & Time/Materials/RainParticle_Lit_Material.mat");
            if (rainMat == null)
            {
                rainMat = new Material(Shader.Find("Horizon/Lit Particle"));
                rainMat.SetTexture("_MainTex", rainTex);

                rainMat.SetColor("_Color", new Color(0.9f, 0.9f, 0.9f, 0.9f));

                AssetDatabase.CreateAsset(rainMat, "Assets/Horizon Weather & Time/Materials/RainParticle_Lit_Material.mat");
            }
            else
            {
                if (rainMat.GetTexture("_MainTex") != rainTex)
                {
                    rainMat.SetTexture("_MainTex", rainTex);
                    EditorUtility.SetDirty(rainMat);
                }
            }

            var rainPrefab = Resources.Load<GameObject>("Prefabs/RainEffect");
            if (rainPrefab != null)
            {
                var r = rainPrefab.GetComponentInChildren<ParticleSystemRenderer>();
                if (r != null && r.sharedMaterial != rainMat)
                {
                    r.sharedMaterial = rainMat;
                    EditorUtility.SetDirty(rainPrefab);
                }
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