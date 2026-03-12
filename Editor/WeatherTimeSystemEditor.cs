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
        private SerializedProperty _timeModeProp;
        private SerializedProperty _timeZoneOffsetProp;
        private SerializedProperty _sunTimeOfDayProp;
        private SerializedProperty _moonTimeOfDayProp;
        private SerializedProperty _timeSpeedProp;
        private SerializedProperty _simulateMoonPhaseProp;
        private SerializedProperty _moonPhaseProp;
        private SerializedProperty _lunarCycleDaysProp;

        private SerializedProperty _allowedWeatherProfilesProp;
        private SerializedProperty _currentProfileIndexProp;

        // --- ASTRONOMY PROPERTIES ---
        private SerializedProperty _latitudeProp;
        private SerializedProperty _axialTiltProp;
        private SerializedProperty _daysInYearProp;
        private SerializedProperty _dayOfYearProp;

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
        private bool _showAstronomy = false;
        private bool _showLayerOverrides = false;

        // VRChat State
        private bool _isVRChatProject = false;
        private bool _integrationActive = false;
        private string _statusMessage = "";

        private bool _lutMissing = false;

        private void OnEnable()
        {
            _target = (WeatherTimeSystem)target;

            // Linking Core Properties
            _timeModeProp = serializedObject.FindProperty("timeMode");
            _timeZoneOffsetProp = serializedObject.FindProperty("timeZoneOffset");
            _sunTimeOfDayProp = serializedObject.FindProperty("_sunTimeOfDay");
            _moonTimeOfDayProp = serializedObject.FindProperty("_moonTimeOfDay");
            _timeSpeedProp = serializedObject.FindProperty("timeSpeed");
            _simulateMoonPhaseProp = serializedObject.FindProperty("simulateMoonPhase");
            _moonPhaseProp = serializedObject.FindProperty("moonPhase");
            _lunarCycleDaysProp = serializedObject.FindProperty("lunarCycleDays");

            _latitudeProp = serializedObject.FindProperty("latitude");
            _axialTiltProp = serializedObject.FindProperty("axialTilt");
            _daysInYearProp = serializedObject.FindProperty("daysInYear");
            _dayOfYearProp = serializedObject.FindProperty("dayOfYear");

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

            // Initial check for LUT
            CheckLUTStatus();

            EditorApplication.delayCall += () => { if (_target != null) _target.Refresh(); };

            EditorApplication.delayCall += () =>
            {
                if (_target != null)
                {
                    if (WeatherBakeUtility.NeedsRebake(_target))
                    {
                        WeatherBakeUtility.BakeAllProfiles(_target);
                    }
                    _target.Refresh();
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. HEADER
            HorizonEditorUtils.DrawHorizonHeader("Weather & Time System", this);

            // 0. CRITICAL WARNINGS
            if (_lutMissing)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.color = new Color(1f, 0.5f, 0.5f);
                GUILayout.Label("⚠️ Missing Atmosphere Data", EditorStyles.boldLabel);
                GUI.color = Color.white;
                EditorGUILayout.HelpBox("The Atmosphere Optical Depth LUT is missing! The skybox will not render correctly.", MessageType.Error);

                if (GUILayout.Button("Regenerate Atmosphere LUT", GUILayout.Height(30)))
                {
                    GenerateAndAssignLUT();
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            // 2. SECTIONS
            DrawTimelineSection();
            DrawWeatherPresetsSection();
            DrawCoreModulesSection();

            // 3. FOOTER
            EditorGUILayout.Space(10);
            DrawVRChatStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void CheckLUTStatus()
        {
            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>(AtmosphereLUTBaker.LUT_PATH);

            _lutMissing = (lut == null);

            if (lut != null && _skyManagerProp.objectReferenceValue != null)
            {
                var manager = (SkyManager)_skyManagerProp.objectReferenceValue;
                var so = new SerializedObject(manager);
                var prop = so.FindProperty("transmittanceLUT");
                if (prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = lut;
                    so.ApplyModifiedProperties();
                }
            }
        }

        private void GenerateAndAssignLUT()
        {
            Texture2D tex = AtmosphereLUTBaker.GenerateLUT();

            if (tex == null)
            {
                Debug.LogError("<b><color=#FF3333>[ERROR]</color></b> [WeatherTimeSystem] Failed to generate Atmosphere LUT.");
                return;
            }

            if (_skyManagerProp.objectReferenceValue != null)
            {
                SkyManager manager = _skyManagerProp.objectReferenceValue as SkyManager;

                if (manager != null)
                {
                    SerializedObject so = new SerializedObject(manager);
                    SerializedProperty prop = so.FindProperty("transmittanceLUT");

                    if (prop != null)
                    {
                        prop.objectReferenceValue = tex;
                        so.ApplyModifiedProperties();
                    }
                }
            }

            _lutMissing = false;

            _target.Refresh();
            _target.ForceVisualUpdate();

            UnityEditor.SceneView.RepaintAll();
        }

        // --- SECTION DRAWERS ---

        private void DrawTimelineSection()
        {
            HorizonEditorUtils.DrawSectionHeader("TIMELINE & SIMULATION");

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // --- 1. TIME MANAGEMENT ---
            GUILayout.Label("Time Management", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_timeModeProp);

            int currentMode = _timeModeProp.enumValueIndex;
            // 0 = SyncWithSystemClock, 1 = SimulatedFlow, 2 = StaticManual

            if (currentMode == 0)
            {
                EditorGUILayout.PropertyField(_timeZoneOffsetProp);
                DrawTimeDebugInfo();
            }
            else if (currentMode == 1)
            {
                EditorGUILayout.PropertyField(_timeSpeedProp);
            }

            EditorGUILayout.Space(6);

            // --- 2. CELESTIAL CONTROL ---
            GUILayout.Label("Celestial Control", EditorStyles.boldLabel);

            GUI.enabled = currentMode != 0;
            EditorGUILayout.PropertyField(_sunTimeOfDayProp, new GUIContent("Sun Position"));
            GUI.enabled = true;

            if (currentMode != 0)
            {
                EditorGUILayout.PropertyField(_simulateMoonPhaseProp, new GUIContent("Simulate Moon Phase"));
                if (_simulateMoonPhaseProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_moonPhaseProp, new GUIContent("Current Phase"));
                    if (currentMode == 1)
                    {
                        EditorGUILayout.PropertyField(_lunarCycleDaysProp, new GUIContent("Lunar Cycle (Days)"));
                    }
                    EditorGUI.indentLevel--;
                }
            }

            bool lockMoon = currentMode == 0 || _simulateMoonPhaseProp.boolValue;
            EditorGUI.BeginDisabledGroup(lockMoon);
            EditorGUILayout.PropertyField(_moonTimeOfDayProp, new GUIContent("Moon Position"));
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            _showAstronomy = EditorGUILayout.Foldout(_showAstronomy, "Astronomy & Geography", true, EditorStyles.foldoutHeader);
            if (_showAstronomy)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(_latitudeProp, new GUIContent("Latitude (Deg)", "0 = Equator, 90 = North Pole. Affects sun/moon paths and star rotation."));

                const float EARTH_TILT = 23.44f;
                bool isTiltModified = Mathf.Abs(_axialTiltProp.floatValue - EARTH_TILT) > 0.01f;

                EditorGUILayout.BeginHorizontal();

                Color defaultColor = GUI.color;
                if (isTiltModified) GUI.color = new Color(1f, 0.92f, 0.75f);

                EditorGUILayout.PropertyField(_axialTiltProp, new GUIContent("Axial Tilt", "Seasonal tilt (Earth ~23.44). Affects day length and sun height."));

                GUI.color = defaultColor;

                if (isTiltModified)
                {
                    if (GUILayout.Button(new GUIContent("↺", "Reset to Earth Standard (23.44)"), GUILayout.Width(24), GUILayout.Height(18)))
                    {
                        _axialTiltProp.floatValue = EARTH_TILT;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                //  --- 3. DATE ---
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(_dayOfYearProp, new GUIContent("Day of Year"));
                GUILayout.Label($"/ {_daysInYearProp.floatValue:F0}", GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

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

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("🔨 Force Rebake All Profiles", GUILayout.Height(25)))
            {
                WeatherBakeUtility.BakeAllProfiles(_target);
                _target.Refresh();
            }

            var bakedProp = serializedObject.FindProperty("bakedProfiles");
            int bakedCount = bakedProp != null ? bakedProp.arraySize : 0;
            int profileCount = _target.weatherProfilesList != null ? _target.weatherProfilesList.Length : 0;

            Color statusColor = (bakedCount == profileCount && bakedCount > 0)
                ? new Color(0.5f, 1f, 0.5f)
                : new Color(1f, 0.7f, 0.4f);

            GUI.color = statusColor;
            GUILayout.Label($"Baked: {bakedCount}/{profileCount}", EditorStyles.miniLabel, GUILayout.Width(80));
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
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
            if (_target.timeMode == TimeMode.SyncWithSystemClock)
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
                    p.skyProfile.turbidity = 5f;
                });

            // 2. SNOW PRESET
            WeatherProfile snow = CheckAndCreatePreset("Default Snow", "Snow",
                (p) =>
                {
                    p.lightingProfile.sunColorZenith = new Color(0.8f, 0.85f, 0.95f);
                    p.lightingProfile.daySkyColor = new Color(0.5f, 0.6f, 0.7f);
                    p.lightingProfile.dayEquatorColor = new Color(0.6f, 0.65f, 0.7f);
                    p.lightingProfile.dayGroundColor = new Color(0.7f, 0.75f, 0.8f);
                    p.effectsProfile.weatherEffectPrefab = Resources.Load<GameObject>("Prefabs/SnowEffect");
                }, "Overcast", "Overcast");

            // 3. RAIN PRESET
            WeatherProfile rain = CheckAndCreatePreset("Default Rain", "Rain",
                (p) =>
                {
                    p.lightingProfile.sunIntensity = 0.5f;
                    p.lightingProfile.sunColorZenith = new Color(0.6f, 0.65f, 0.7f);
                    p.lightingProfile.daySkyColor = new Color(0.3f, 0.35f, 0.4f);
                    p.lightingProfile.dayEquatorColor = new Color(0.25f, 0.3f, 0.35f);
                    p.lightingProfile.dayGroundColor = new Color(0.15f, 0.2f, 0.25f);
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
            CheckLUTStatus();

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
                    preset.skyProfile.exposure = 7.5f;
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
            if (p.skyProfile == null)
            {
                return;
            }

            bool isDirty = false;
            string starsFolder = "Assets/Horizon Weather & Time/Textures/Sky";

            // 1. Search for Star Map Cubemap
            if (p.skyProfile.starsCubemap == null)
            {
                string[] guids = AssetDatabase.FindAssets("stars t:Cubemap", new[] { starsFolder });

                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);

                    if (cube != null)
                    {
                        p.skyProfile.starsCubemap = cube;
                        isDirty = true;
                        break;
                    }
                }
            }

            // 2. Search for Milky Way Cubemap
            if (p.skyProfile.milkyWayCubemap == null)
            {
                string[] guids = AssetDatabase.FindAssets("milkyway t:Cubemap", new[] { starsFolder });

                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);

                    if (cube != null)
                    {
                        p.skyProfile.milkyWayCubemap = cube;
                        isDirty = true;
                        break;
                    }
                }
            }

            // Restore default intensities if they are unset
            if (p.skyProfile.starsIntensity <= 0.001f)
            {
                p.skyProfile.starsIntensity = 1.0f;
                isDirty = true;
            }

            if (p.skyProfile.milkyWayIntensity <= 0.001f)
            {
                p.skyProfile.milkyWayIntensity = 1.0f;
                isDirty = true;
            }

            if (isDirty)
            {
                EditorUtility.SetDirty(p.skyProfile);
            }
        }

        // --- ASSET HELPERS ---

        private void CheckAndGenerateCloudTexture(List<WeatherProfile> profiles)
        {
            string cloudPath3D = WeatherOptimizationGen.DEFAULT_CLOUD_NOISE_3D_PATH;
            string weatherMapPath = WeatherOptimizationGen.DEFAULT_WEATHER_MAP_PATH;
            string blueNoisePath = WeatherOptimizationGen.DEFAULT_BLUE_NOISE_PATH;
            string cirrusPath = WeatherOptimizationGen.DEFAULT_CIRRUS_NOISE_PATH;

            Texture3D cloudTex3D = AssetDatabase.LoadAssetAtPath<Texture3D>(cloudPath3D);
            Texture2D weatherMapTex = AssetDatabase.LoadAssetAtPath<Texture2D>(weatherMapPath);
            Texture2D blueNoiseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(blueNoisePath);
            Texture2D cirrusTex = AssetDatabase.LoadAssetAtPath<Texture2D>(cirrusPath);

            if (cloudTex3D == null)
            {
                WeatherOptimizationGen.Generate3DCloudNoise(cloudPath3D);
                cloudTex3D = AssetDatabase.LoadAssetAtPath<Texture3D>(cloudPath3D);
            }
            if (weatherMapTex == null) weatherMapTex = WeatherOptimizationGen.GenerateWeatherMap(weatherMapPath);
            if (blueNoiseTex == null) blueNoiseTex = WeatherOptimizationGen.GenerateBlueNoise(blueNoisePath);
            if (cirrusTex == null) cirrusTex = WeatherOptimizationGen.GenerateCirrusTexture(cirrusPath);

            foreach (var p in profiles)
            {
                if (p != null && p.cloudProfile != null)
                {
                    bool isDirty = false;

                    if (p.cloudProfile.cloudNoiseTexture == null) { p.cloudProfile.cloudNoiseTexture = cloudTex3D; isDirty = true; }
                    if (p.cloudProfile.weatherMapTexture == null) { p.cloudProfile.weatherMapTexture = weatherMapTex; isDirty = true; }
                    if (p.cloudProfile.blueNoiseTexture == null) { p.cloudProfile.blueNoiseTexture = blueNoiseTex; isDirty = true; }
                    if (p.cloudProfile.cirrusNoiseTexture == null) { p.cloudProfile.cirrusNoiseTexture = cirrusTex; isDirty = true; }

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

        /// <summary>
        /// Final attempt to assign the generated Star Map cubemap asset by its specific default filename.
        /// </summary>
        /// <param name="p">The WeatherProfile to initialize.</param>
        private void CheckAndAssignDefaultStarsTexture(WeatherProfile p)
        {
            if (p.skyProfile == null || p.skyProfile.starsCubemap != null)
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("Horizon_StarMap_Gen t:Cubemap");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);

                if (cube != null)
                {
                    p.skyProfile.starsCubemap = cube;
                    EditorUtility.SetDirty(p.skyProfile);
                }
            }
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
            Texture2D snowTex = EnsureSnowflakeTexture();
            Mesh particleMesh = EnsureParticleMesh();
            Material particleMat = EnsureParticleMaterial(snowTex);

            if (_weatherEffectsManagerProp.objectReferenceValue != null)
            {
                var manager = (WeatherEffectsManager)_weatherEffectsManagerProp.objectReferenceValue;
                ConfigureEffectsManagerInstance(manager, particleMesh, particleMat);
            }

            UpdateSnowPrefab(particleMesh, particleMat);
        }

        // --- SUB-FUNCTIONS ---

        private Texture2D EnsureSnowflakeTexture()
        {
            string texPath = "Assets/Horizon Weather & Time/Textures/Horizon_SnowFlake_v1.png";
            string legacyPath = "Assets/Horizon Weather & Time/Textures/DefaultSnowflake.png";

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            if (tex == null)
            {
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(legacyPath) != null)
                    AssetDatabase.DeleteAsset(legacyPath);

                if (!Directory.Exists("Assets/Horizon Weather & Time/Textures"))
                    Directory.CreateDirectory("Assets/Horizon Weather & Time/Textures");

                tex = GenerateRealisticSnowFlake(128);

                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                AssetDatabase.Refresh();

                TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null)
                {
                    importer.alphaIsTransparency = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }
            return tex;
        }

        private Mesh EnsureParticleMesh()
        {
            string meshPath = "Assets/Horizon Weather & Time/Resources/Meshes/GPUParticleVolume.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = GenerateGPUParticleMesh(15000, meshPath);
            }
            return mesh;
        }

        private Material EnsureParticleMaterial(Texture2D texture)
        {
            string matPath = "Assets/Horizon Weather & Time/Materials/SnowParticle_Lit_Material.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader shader = Shader.Find("Horizon/GPU Particles");

            if (shader == null)
            {
                Debug.LogError("[WeatherTimeSystem] Shader 'Horizon/GPU Particles' missing!");
                return mat;
            }

            if (mat == null)
            {
                mat = new Material(shader);
                mat.SetTexture("_MainTex", texture);

                string dir = Path.GetDirectoryName(matPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                if (mat.shader != shader) mat.shader = shader;
                if (mat.GetTexture("_MainTex") != texture)
                {
                    mat.SetTexture("_MainTex", texture);
                    EditorUtility.SetDirty(mat);
                }
            }
            return mat;
        }

        private void ConfigureEffectsManagerInstance(WeatherEffectsManager manager, Mesh mesh, Material mat)
        {
            Transform root = manager.transform;
            Transform volumeTrans = root.Find("WeatherFX_Volume");

            if (volumeTrans == null)
            {
                GameObject volObj = new GameObject("WeatherFX_Volume");
                volObj.transform.SetParent(root);
                volObj.transform.localPosition = Vector3.zero;
                volumeTrans = volObj.transform;
            }

            if (volumeTrans.gameObject.layer != 2) volumeTrans.gameObject.layer = 2;

            MeshFilter mf = volumeTrans.GetComponent<MeshFilter>();
            if (mf == null) mf = volumeTrans.gameObject.AddComponent<MeshFilter>();
            if (mf.sharedMesh != mesh) mf.sharedMesh = mesh;

            MeshRenderer mr = volumeTrans.GetComponent<MeshRenderer>();
            if (mr == null) mr = volumeTrans.gameObject.AddComponent<MeshRenderer>();

            if (mr.sharedMaterial != mat) mr.sharedMaterial = mat;

            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            if (manager.particleRenderer != mr)
            {
                manager.particleRenderer = mr;
                EditorUtility.SetDirty(manager);
            }
        }

        private void UpdateSnowPrefab(Mesh mesh, Material mat)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/SnowEffect");
            if (prefab != null)
            {
                bool changed = false;

                var oldPs = prefab.GetComponentInChildren<ParticleSystem>();
                if (oldPs != null) { DestroyImmediate(oldPs.gameObject, true); changed = true; }

                var mf = prefab.GetComponent<MeshFilter>();
                if (mf == null) { mf = prefab.AddComponent<MeshFilter>(); changed = true; }
                if (mf.sharedMesh != mesh) { mf.sharedMesh = mesh; changed = true; }

                var mr = prefab.GetComponent<MeshRenderer>();
                if (mr == null) { mr = prefab.AddComponent<MeshRenderer>(); changed = true; }
                if (mr.sharedMaterial != mat) { mr.sharedMaterial = mat; changed = true; }

                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

                if (changed) EditorUtility.SetDirty(prefab);
            }
        }

        /// <summary>
        /// Generates a realistic "clump" snowflake using layered noise for a soft, fluffy look.
        /// </summary>
        private static Texture2D GenerateRealisticSnowFlake(int resolution)
        {
            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[resolution * resolution];
            float invRes = 1.0f / (resolution - 1);

            float seedX = UnityEngine.Random.Range(0f, 100f);
            float seedY = UnityEngine.Random.Range(0f, 100f);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = (x * invRes) * 2.0f - 1.0f;
                    float v = (y * invRes) * 2.0f - 1.0f;

                    float dist = Mathf.Sqrt(u * u + v * v);

                    float t = Mathf.InverseLerp(0.1f, 0.5f, dist);
                    float falloff = Mathf.SmoothStep(0f, 1f, t);

                    float circleMask = 1.0f - falloff;

                    if (circleMask <= 0.001f)
                    {
                        pixels[y * resolution + x] = Color.clear;
                        continue;
                    }

                    float n1 = Mathf.PerlinNoise(u * 2.5f + seedX, v * 2.5f + seedY);
                    float n2 = Mathf.PerlinNoise(u * 6.0f - seedX, v * 6.0f - seedY);
                    float n3 = Mathf.PerlinNoise(u * 12.0f + seedY, v * 12.0f + seedX);

                    float noise = n1 * 0.5f + n2 * 0.3f + n3 * 0.2f;
                    float alpha = circleMask * noise;

                    alpha = Mathf.Pow(alpha, 1.5f);
                    alpha *= 1.8f;

                    alpha = Mathf.Clamp01(alpha);

                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
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

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(AtmosphereLUTBaker.LUT_PATH) == null)
            {
                AtmosphereLUTBaker.GenerateLUT();
            }
        }
    }
}