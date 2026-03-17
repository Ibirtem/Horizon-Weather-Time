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

        private Dictionary<string, List<ScriptableObject>> _cachedDiscoveredModules;
        private bool _modulesCacheDirty = true;

        private Dictionary<string, List<ScriptableObject>> GetDiscoveredModules()
        {
            if (_modulesCacheDirty || _cachedDiscoveredModules == null)
            {
                _cachedDiscoveredModules = WeatherBakeUtility.ScanModuleFolders();
                _modulesCacheDirty = false;
            }
            return _cachedDiscoveredModules;
        }

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
            CheckSkyManagerInternalTextures();

            EditorApplication.delayCall += () =>
            {
                if (_target != null)
                {
                    var discovered = WeatherBakeUtility.ScanModuleFolders();
                    bool needsModuleBake = WeatherBakeUtility.NeedsRebakeModules(_target, discovered);

                    if (needsModuleBake)
                    {
                        WeatherBakeUtility.BakeModules(_target, discovered);
                    }

                    _target.Refresh();

                    UnityEditor.SceneView.RepaintAll();
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var discoveredModules = GetDiscoveredModules();

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
            DrawWeatherPresetsSection(discoveredModules);
            DrawCoreModulesSection();

            // 3. FOOTER
            EditorGUILayout.Space(10);
            DrawVRChatStatus();

            serializedObject.ApplyModifiedProperties();
        }

        private void CheckSkyManagerInternalTextures()
        {
            if (_skyManagerProp.objectReferenceValue == null) return;

            var manager = (SkyManager)_skyManagerProp.objectReferenceValue;
            var so = new SerializedObject(manager);

            // --- Transmittance LUT ---
            var lutProp = so.FindProperty("transmittanceLUT");
            var lut = AssetDatabase.LoadAssetAtPath<Texture2D>(AtmosphereLUTBaker.LUT_PATH);

            _lutMissing = (lut == null);

            if (lut != null && lutProp.objectReferenceValue == null)
            {
                lutProp.objectReferenceValue = lut;
            }

            // --- Twinkle Noise 3D ---
            var twinkleProp = so.FindProperty("twinkleNoiseTex");
            if (twinkleProp != null && twinkleProp.objectReferenceValue == null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture3D>(
                    WeatherOptimizationGen.DEFAULT_TWINKLE_NOISE_3D_PATH);
                if (tex != null)
                {
                    twinkleProp.objectReferenceValue = tex;
                }
            }

            so.ApplyModifiedProperties();
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
                UnityEditor.SceneView.RepaintAll();
            }
        }

        private void DrawWeatherPresetsSection(Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            HorizonEditorUtils.DrawSectionHeader("WEATHER PRESETS & LAYERS");

            // --- MASTER PRESET ---
            if (_target.weatherProfilesList != null && _target.weatherProfilesList.Length > 0)
            {
                var presetNames = _target.weatherProfilesList
                    .Select(p => p != null ? p.name : "(None)").ToArray();

                ValidateIndices(presetNames.Length, discoveredModules);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();
                int newMaster = EditorGUILayout.Popup("Master Preset", _currentProfileIndexProp.intValue, presetNames);
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyMasterPreset(newMaster, discoveredModules);
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }

            // --- PER-MODULE LAYER OVERRIDES ---
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Layer Overrides", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            DrawModuleDropdown("Lighting", _lightingIndexProp, "Lighting", discoveredModules);
            DrawModuleDropdown("Sky & Stars", _skyIndexProp, "Sky", discoveredModules);
            DrawModuleDropdown("Clouds", _cloudIndexProp, "Clouds", discoveredModules);
            DrawModuleDropdown("Moon", _moonIndexProp, "Moon", discoveredModules);
            DrawModuleDropdown("Fog", _fogIndexProp, "Fog", discoveredModules);
            DrawModuleDropdown("Effects", _effectsIndexProp, "Effects", discoveredModules);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                _target.Refresh();
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_allowedWeatherProfilesProp, new GUIContent("Loaded Profiles List"), true);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("🔨 Force Rebake All", GUILayout.Height(25)))
            {
                _modulesCacheDirty = true;
                WeatherBakeUtility.BakeModules(_target, GetDiscoveredModules());
                _target.Refresh();
            }

            int profileCount = _target.weatherProfilesList != null ? _target.weatherProfilesList.Length : 0;
            int moduleCount = 0;
            foreach (var kvp in discoveredModules) moduleCount += kvp.Value.Count;

            bool isBaked = _target.bakedLightingModules != null && _target.bakedLightingModules.Length > 0;
            GUI.color = isBaked ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.7f, 0.4f);
            GUILayout.Label($"P:{profileCount} M:{moduleCount}", EditorStyles.miniLabel, GUILayout.Width(100));
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

        private void ValidateIndices(int presetCount, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            ClampProp(_currentProfileIndexProp, Mathf.Max(0, presetCount - 1));

            foreach (var (folder, _) in WeatherBakeUtility.MODULE_TYPES)
            {
                int moduleMax = discoveredModules.ContainsKey(folder)
                    ? Mathf.Max(0, discoveredModules[folder].Count - 1)
                    : 0;

                SerializedProperty prop = GetIndexPropForFolder(folder);
                if (prop != null) ClampProp(prop, moduleMax);
            }
        }

        private SerializedProperty GetIndexPropForFolder(string folder)
        {
            switch (folder)
            {
                case "Lighting": return _lightingIndexProp;
                case "Sky": return _skyIndexProp;
                case "Clouds": return _cloudIndexProp;
                case "Moon": return _moonIndexProp;
                case "Fog": return _fogIndexProp;
                case "Effects": return _effectsIndexProp;
                default: return null;
            }
        }

        private static void ClampProp(SerializedProperty prop, int max)
        {
            int clamped = Mathf.Clamp(prop.intValue, 0, max);
            if (prop.intValue != clamped)
            {
                prop.intValue = clamped;
            }
        }

        private void ApplyMasterPreset(int newIndex, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            _currentProfileIndexProp.intValue = newIndex;

            if (_target.weatherProfilesList != null && newIndex < _target.weatherProfilesList.Length)
            {
                var wp = _target.weatherProfilesList[newIndex] as WeatherProfile;
                if (wp != null)
                {
                    _lightingIndexProp.intValue = FindModuleIndex("Lighting", wp.lightingProfile, discoveredModules);
                    _skyIndexProp.intValue = FindModuleIndex("Sky", wp.skyProfile, discoveredModules);
                    _cloudIndexProp.intValue = FindModuleIndex("Clouds", wp.cloudProfile, discoveredModules);
                    _moonIndexProp.intValue = FindModuleIndex("Moon", wp.moonProfile, discoveredModules);
                    _fogIndexProp.intValue = FindModuleIndex("Fog", wp.fogProfile, discoveredModules);
                    _effectsIndexProp.intValue = FindModuleIndex("Effects", wp.effectsProfile, discoveredModules);
                }
            }
            serializedObject.ApplyModifiedProperties();
            _target.Refresh();
        }

        /// <summary>
        /// Finds the index of a module in the discovered list by matching asset name.
        /// </summary>
        private int FindModuleIndex(string folder, ScriptableObject module, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            if (module == null || !discoveredModules.ContainsKey(folder)) return 0;
            var list = discoveredModules[folder];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].name == module.name) return i;
            }
            return 0;
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

        /// <summary>
        /// Draws a dropdown that lists discovered modules of a specific type.
        /// Shows module asset names, not preset names.
        /// </summary>
        private void DrawModuleDropdown(string label, SerializedProperty indexProp, string folder, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            string[] names = new[] { "(None)" };
            if (discoveredModules.ContainsKey(folder) && discoveredModules[folder].Count > 0)
            {
                names = discoveredModules[folder].Select(m => m != null ? m.name : "(None)").ToArray();
            }

            int maxIndex = Mathf.Max(0, names.Length - 1);
            if (indexProp.intValue > maxIndex) indexProp.intValue = 0;

            indexProp.intValue = EditorGUILayout.Popup(label, indexProp.intValue, names);
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

        private void ScanAndSyncProfiles()
        {
            if (!Directory.Exists(PRESETS_DIR)) return;

            string[] guids = AssetDatabase.FindAssets("t:WeatherProfile", new[] { PRESETS_DIR });
            var foundProfiles = new List<UnityEngine.Object>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                WeatherProfile profile = AssetDatabase.LoadAssetAtPath<WeatherProfile>(path);
                if (profile != null) foundProfiles.Add(profile);
            }

            foundProfiles.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            bool changed = false;
            if (_target.weatherProfilesList == null || _target.weatherProfilesList.Length != foundProfiles.Count)
            {
                changed = true;
            }
            else
            {
                for (int i = 0; i < foundProfiles.Count; i++)
                {
                    if (_target.weatherProfilesList[i] != foundProfiles[i]) { changed = true; break; }
                }
            }

            if (changed)
            {
                _target.weatherProfilesList = foundProfiles.ToArray();
                EditorUtility.SetDirty(_target);
                _modulesCacheDirty = true;
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
            ScanAndSyncProfiles();

            EnsureProceduralTextures();
            EnsureDefaultPresets();

            CheckAndConfigureSkyboxMaterial();
            CheckAndConfigureParticleAssets();
            CheckAndConfigureOcclusionCamera();
            CheckSkyManagerInternalTextures();

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Copies default weather presets from internal templates.
        /// If templates don't exist yet, triggers their generation first.
        /// </summary>
        private List<WeatherProfile> EnsureDefaultPresets()
        {
            string templatesPresetDir = GetPackageTemplatesPath("Presets");
            if (string.IsNullOrEmpty(templatesPresetDir)
                || !Directory.Exists(templatesPresetDir)
                || Directory.GetFiles(templatesPresetDir, "*.asset").Length == 0)
            {
                HorizonTemplateGenerator.Generate();
                templatesPresetDir = GetPackageTemplatesPath("Presets");
            }

            var defaultPresets = new List<WeatherProfile>();

            WeatherProfile clear = CopyTemplateIfMissing("Template_Clear", "Default Clear");
            WeatherProfile rain = CopyTemplateIfMissing("Template_Rain", "Default Rain");
            WeatherProfile snow = CopyTemplateIfMissing("Template_Snow", "Default Snow");

            if (clear != null) defaultPresets.Add(clear);
            if (rain != null) defaultPresets.Add(rain);
            if (snow != null) defaultPresets.Add(snow);

            return defaultPresets;
        }

        /// <summary>
        /// Copies a template preset and all its sub-modules into the user's working directory.
        /// Skips if the output preset already exists (preserves user edits).
        /// </summary>
        private WeatherProfile CopyTemplateIfMissing(string templateName, string outputName)
        {
            string outputPath = $"{PRESETS_DIR}/{outputName}.asset";
            WeatherProfile existing = AssetDatabase.LoadAssetAtPath<WeatherProfile>(outputPath);
            if (existing != null) return existing;

            string templateDir = GetPackageTemplatesPath("Presets");
            string templatePath = $"{templateDir}/{templateName}.asset";
            WeatherProfile template = AssetDatabase.LoadAssetAtPath<WeatherProfile>(templatePath);
            if (template == null)
            {
                Debug.LogWarning($"<b><color=#FF9900>[WARNING]</color></b> <color=white>[WeatherTimeSystem] Template '{templateName}' not found at {templatePath}</color>");
                return null;
            }

            if (!Directory.Exists(PRESETS_DIR)) Directory.CreateDirectory(PRESETS_DIR);
            if (!Directory.Exists(MODULES_DIR)) Directory.CreateDirectory(MODULES_DIR);

            LightingProfile lp = CopyModuleIfNeeded(template.lightingProfile, "Lighting");
            SkyProfile sp = CopyModuleIfNeeded(template.skyProfile, "Sky");
            CloudProfile cp = CopyModuleIfNeeded(template.cloudProfile, "Clouds");
            MoonProfile mp = CopyModuleIfNeeded(template.moonProfile, "Moon");
            FogProfile fp = CopyModuleIfNeeded(template.fogProfile, "Fog");
            EffectsProfile ep = CopyModuleIfNeeded(template.effectsProfile, "Effects");

            WeatherProfile preset = ScriptableObject.CreateInstance<WeatherProfile>();
            preset.profileName = outputName;
            preset.lightingProfile = lp;
            preset.skyProfile = sp;
            preset.cloudProfile = cp;
            preset.moonProfile = mp;
            preset.fogProfile = fp;
            preset.effectsProfile = ep;

            AssetDatabase.CreateAsset(preset, outputPath);
            EditorUtility.SetDirty(preset);

            return preset;
        }

        /// <summary>
        /// Copies a single sub-module asset from templates to user's working directory.
        /// If a module with the same name already exists, returns the existing one.
        /// </summary>
        private T CopyModuleIfNeeded<T>(T source, string folder) where T : ScriptableObject
        {
            if (source == null) return null;

            string dir = $"{MODULES_DIR}/{folder}";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string assetName = source.name;
            if (string.IsNullOrEmpty(assetName))
            {
                string sourcePath = AssetDatabase.GetAssetPath(source);
                assetName = Path.GetFileNameWithoutExtension(sourcePath);
            }
            if (string.IsNullOrEmpty(assetName)) assetName = $"{folder}_Unknown";

            string outputPath = $"{dir}/{assetName}.asset";
            T existing = AssetDatabase.LoadAssetAtPath<T>(outputPath);

            if (existing != null)
            {
                FillGeneratedTextures(existing);
                return existing;
            }

            T copy = ScriptableObject.CreateInstance<T>();
            EditorUtility.CopySerialized(source, copy);
            AssetDatabase.CreateAsset(copy, outputPath);

            FillGeneratedTextures(copy);

            return copy;
        }

        /// <summary>
        /// Fills null texture fields in user module copies with generated Assets/ textures.
        /// Package textures (moon, stars) come from templates via CopySerialized.
        /// This handles ONLY textures that are generated into Assets/ and thus
        /// cannot be referenced by package templates.
        /// </summary>
        private void FillGeneratedTextures(ScriptableObject module)
        {
            if (module == null) return;

            if (module is CloudProfile cp)
            {
                bool dirty = false;

                if (cp.cloudNoiseTexture == null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture3D>(
                        WeatherOptimizationGen.DEFAULT_CLOUD_NOISE_3D_PATH);
                    if (tex != null) { cp.cloudNoiseTexture = tex; dirty = true; }
                }
                if (cp.weatherMapTexture == null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        WeatherOptimizationGen.DEFAULT_WEATHER_MAP_PATH);
                    if (tex != null) { cp.weatherMapTexture = tex; dirty = true; }
                }
                if (cp.blueNoiseTexture == null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        WeatherOptimizationGen.DEFAULT_BLUE_NOISE_PATH);
                    if (tex != null) { cp.blueNoiseTexture = tex; dirty = true; }
                }
                if (cp.cirrusNoiseTexture == null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        WeatherOptimizationGen.DEFAULT_CIRRUS_NOISE_PATH);
                    if (tex != null) { cp.cirrusNoiseTexture = tex; dirty = true; }
                }
                if (cp.curlNoiseTexture == null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        WeatherOptimizationGen.DEFAULT_CURL_NOISE_PATH);
                    if (tex != null) { cp.curlNoiseTexture = tex; dirty = true; }
                }

                if (dirty) EditorUtility.SetDirty(cp);
                return;
            }

            if (module is EffectsProfile ep)
            {
                if (ep.weatherEffectPrefab == null)
                {
                    string nameLower = ep.name.ToLowerInvariant();
                    string prefabName = null;

                    if (nameLower.Contains("rain")) prefabName = "RainEffect";
                    else if (nameLower.Contains("snow")) prefabName = "SnowEffect";

                    if (prefabName != null)
                    {
                        string path = $"Assets/Horizon Weather & Time/Resources/Prefabs/{prefabName}.prefab";
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab != null)
                        {
                            ep.weatherEffectPrefab = prefab;
                            EditorUtility.SetDirty(ep);
                        }
                    }
                }
                return;
            }
        }

        // --- ASSET HELPERS ---

        /// <summary>
        /// Ensures all procedural textures exist on disk.
        /// </summary>
        private void EnsureProceduralTextures()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture3D>(
                WeatherOptimizationGen.DEFAULT_CLOUD_NOISE_3D_PATH) == null)
            {
                WeatherOptimizationGen.Generate3DCloudNoise(
                    WeatherOptimizationGen.DEFAULT_CLOUD_NOISE_3D_PATH);
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_WEATHER_MAP_PATH) == null)
            {
                WeatherOptimizationGen.GenerateWeatherMap(
                    WeatherOptimizationGen.DEFAULT_WEATHER_MAP_PATH);
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_BLUE_NOISE_PATH) == null)
            {
                WeatherOptimizationGen.GenerateBlueNoise(
                    WeatherOptimizationGen.DEFAULT_BLUE_NOISE_PATH);
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_CIRRUS_NOISE_PATH) == null)
            {
                WeatherOptimizationGen.GenerateCirrusTexture(
                    WeatherOptimizationGen.DEFAULT_CIRRUS_NOISE_PATH);
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_CURL_NOISE_PATH) == null)
            {
                WeatherOptimizationGen.GenerateCurlNoise(
                    WeatherOptimizationGen.DEFAULT_CURL_NOISE_PATH);
            }

            if (AssetDatabase.LoadAssetAtPath<Texture3D>(
                WeatherOptimizationGen.DEFAULT_TWINKLE_NOISE_3D_PATH) == null)
            {
                WeatherOptimizationGen.GenerateTwinkleNoise3D(
                    WeatherOptimizationGen.DEFAULT_TWINKLE_NOISE_3D_PATH);
            }
        }

        /// <summary>
        /// Finds the Internal/Templates path inside the package, regardless of install method.
        /// </summary>
        private static string GetPackageTemplatesPath(string subfolder = null)
        {
            string[] guids = AssetDatabase.FindAssets("HorizonTemplateGenerator t:MonoScript");
            if (guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                string editorDir = Path.GetDirectoryName(scriptPath);
                string packageRoot = Path.GetDirectoryName(editorDir);
                string templatesRoot = packageRoot + "/Internal/Templates";

                if (!string.IsNullOrEmpty(subfolder))
                    return templatesRoot + "/" + subfolder;

                return templatesRoot;
            }
            return null;
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
            Mesh particleMesh = EnsureParticleMesh();

            Texture2D snowTex = EnsureSnowflakeTexture();
            Vector4 snowWind = new Vector4(1f, -1.5f, 0.5f, 0f);
            Material snowMat = EnsureParticleMaterial(
                snowTex, "SnowParticle_Lit_Material.mat",
                stretch: 1.0f, defaultWind: snowWind,
                color: new Color(1f, 1f, 1f, 0.9f),
                particleSize: 0.03f);
            UpdateEffectPrefab("SnowEffect", particleMesh, snowMat);

            Texture2D rainTex = EnsureRaindropTexture();
            Vector4 rainWind = new Vector4(0.5f, -12.0f, 0.2f, 0f);
            Material rainMat = EnsureParticleMaterial(
                rainTex, "RainParticle_Lit_Material.mat",
                stretch: 5.0f, defaultWind: rainWind,
                color: new Color(0.7f, 0.78f, 0.88f, 0.3f),
                particleSize: 0.015f);
            UpdateEffectPrefab("RainEffect", particleMesh, rainMat);

            if (_weatherEffectsManagerProp.objectReferenceValue != null)
            {
                var manager = (WeatherEffectsManager)_weatherEffectsManagerProp.objectReferenceValue;
                if (manager.particleRenderer == null)
                {
                    ConfigureEffectsManagerInstance(manager, particleMesh, snowMat);
                }
            }
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

        private Material EnsureParticleMaterial(
            Texture2D texture, string matName,
            float stretch, Vector4 defaultWind,
            Color color, float particleSize)
        {
            string matPath = $"Assets/Horizon Weather & Time/Materials/{matName}";
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
                mat.SetFloat("_Stretch", stretch);
                mat.SetVector("_Wind", defaultWind);
                mat.SetColor("_Color", color);
                mat.SetFloat("_HorizonParticleSize", particleSize);

                string dir = Path.GetDirectoryName(matPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                bool isDirty = false;
                if (mat.shader != shader) { mat.shader = shader; isDirty = true; }
                if (mat.GetTexture("_MainTex") != texture) { mat.SetTexture("_MainTex", texture); isDirty = true; }
                if (!Mathf.Approximately(mat.GetFloat("_Stretch"), stretch)) { mat.SetFloat("_Stretch", stretch); isDirty = true; }
                if (!Mathf.Approximately(mat.GetFloat("_HorizonParticleSize"), particleSize)) { mat.SetFloat("_HorizonParticleSize", particleSize); isDirty = true; }
                if (mat.GetColor("_Color") != color) { mat.SetColor("_Color", color); isDirty = true; }
                if (mat.GetVector("_Wind").y == 0f) { mat.SetVector("_Wind", defaultWind); isDirty = true; }

                if (isDirty) EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        private void UpdateEffectPrefab(string prefabName, Mesh mesh, Material mat)
        {
            string dir = "Assets/Horizon Weather & Time/Resources/Prefabs";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = $"{dir}/{prefabName}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            GameObject tempObj = null;
            if (prefab == null)
            {
                tempObj = new GameObject(prefabName);
                prefab = PrefabUtility.SaveAsPrefabAsset(tempObj, path);
                DestroyImmediate(tempObj);
            }

            using (var editingScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                var root = editingScope.prefabContentsRoot;

                var oldPs = root.GetComponent<ParticleSystem>();
                if (oldPs != null) { DestroyImmediate(oldPs, true); }

                var mf = root.GetComponent<MeshFilter>();
                if (mf == null) mf = root.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = root.GetComponent<MeshRenderer>();
                if (mr == null) mr = root.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;

                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }
        }

        private Texture2D EnsureRaindropTexture()
        {
            string texPath = "Assets/Horizon Weather & Time/Textures/Horizon_Raindrop_v1.png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            if (tex == null)
            {
                if (!Directory.Exists("Assets/Horizon Weather & Time/Textures"))
                    Directory.CreateDirectory("Assets/Horizon Weather & Time/Textures");

                tex = GenerateRealisticRaindrop(64);

                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                AssetDatabase.Refresh();

                TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null)
                {
                    importer.alphaIsTransparency = true;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.mipmapEnabled = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }
            return tex;
        }

        private static Texture2D GenerateRealisticRaindrop(int resolution)
        {
            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[resolution * resolution];
            float invRes = 1.0f / (resolution - 1);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = (x * invRes) * 2.0f - 1.0f;
                    float v = (y * invRes) * 2.0f - 1.0f;

                    float dist = Mathf.Sqrt(u * u + v * v);
                    float alpha = 1.0f - Mathf.SmoothStep(0.1f, 0.8f, dist);

                    if (alpha < 0.005f)
                    {
                        pixels[y * resolution + x] = Color.clear;
                        continue;
                    }

                    pixels[y * resolution + x] = new Color(alpha, alpha, alpha, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
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