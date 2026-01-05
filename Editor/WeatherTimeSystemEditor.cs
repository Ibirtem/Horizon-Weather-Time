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

        // System Refs
        private SerializedProperty _lightingManagerProp;
        private SerializedProperty _skyManagerProp;
        private SerializedProperty _weatherEffectsManagerProp;
        private SerializedProperty _reflectionManagerProp;

        // VRChat State
        private bool _isVRChatProject = false;
        private bool _integrationActive = false;
        private string _statusMessage = "";

        private void OnEnable()
        {
            _target = (WeatherTimeSystem)target;

            // Linking Properties
            _useRealTimeProp = serializedObject.FindProperty("useRealTime");
            _timeZoneOffsetProp = serializedObject.FindProperty("timeZoneOffset");
            _sunTimeOfDayProp = serializedObject.FindProperty("_sunTimeOfDay");
            _moonTimeOfDayProp = serializedObject.FindProperty("_moonTimeOfDay");
            _timeSpeedProp = serializedObject.FindProperty("timeSpeed");

            _allowedWeatherProfilesProp = serializedObject.FindProperty("weatherProfilesList");
            _currentProfileIndexProp = serializedObject.FindProperty("_currentProfileIndex");

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

            // 2. TIMELINE
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

            // 3. WEATHER
            HorizonEditorUtils.DrawSectionHeader("ATMOSPHERE & PROFILES");

            if (_target.weatherProfilesList != null && _target.weatherProfilesList.Length > 0)
            {
                var profileNames = _target.weatherProfilesList.Select(p => p != null ? p.name : " (None)").ToArray();

                EditorGUI.BeginChangeCheck();
                Rect r = EditorGUILayout.GetControlRect();
                EditorGUI.LabelField(new Rect(r.x, r.y, r.width * 0.4f, r.height), "Active Profile", EditorStyles.boldLabel);
                _currentProfileIndexProp.intValue = EditorGUI.Popup(new Rect(r.x + r.width * 0.4f, r.y, r.width * 0.6f, r.height), _currentProfileIndexProp.intValue, profileNames);
                if (EditorGUI.EndChangeCheck()) { serializedObject.ApplyModifiedProperties(); _target.Refresh(); }
            }
            else
            {
                EditorGUILayout.HelpBox("No Profiles Loaded! Add Weather Profiles below.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(_allowedWeatherProfilesProp, true);

            // 4. CORE
            HorizonEditorUtils.DrawSectionHeader("CORE MODULES");
            GUI.enabled = false;
            EditorGUILayout.PropertyField(_lightingManagerProp);
            EditorGUILayout.PropertyField(_skyManagerProp);
            EditorGUILayout.PropertyField(_weatherEffectsManagerProp);
            EditorGUILayout.PropertyField(_reflectionManagerProp);
            GUI.enabled = true;

            // Footer
            EditorGUILayout.Space(10);
            DrawVRChatStatus();

            serializedObject.ApplyModifiedProperties();
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

        // ================================================================
        // DEPENDENCY & ASSET CHECKS
        // ================================================================

        private void CheckAndConfigureDependencies()
        {
            var defaultProfiles = new List<WeatherProfile>();

            // 1. Clear
            WeatherProfile clearProfile = CheckAndCreateDefaultProfile();
            if (clearProfile != null)
            {
                defaultProfiles.Add(clearProfile);
                CheckAndAssignDefaultStarsTexture(clearProfile);
                CheckAndAssignDefaultMoonTexture(clearProfile);
            }

            // 2. Snow
            WeatherProfile snowProfile = CheckAndCreateDefaultSnowProfile();
            if (snowProfile != null)
            {
                defaultProfiles.Add(snowProfile);
                CheckAndAssignDefaultStarsTexture(snowProfile);
                CheckAndAssignDefaultMoonTexture(snowProfile);
            }

            // 3. Rain
            WeatherProfile rainProfile = CheckAndCreateDefaultRainProfile();
            if (rainProfile != null)
            {
                defaultProfiles.Add(rainProfile);
                CheckAndAssignDefaultStarsTexture(rainProfile);
                CheckAndAssignDefaultMoonTexture(rainProfile);
            }

            EnsureProfilesAreInAllowedList(defaultProfiles);

            CheckAndGenerateCloudTexture(defaultProfiles);

            CheckAndConfigureSkyboxMaterial();
            CheckAndConfigureParticleAssets();
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
                    Debug.Log($"<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Created default Skybox Material.</color>");
                }
            }
            skyboxMaterialProp.objectReferenceValue = existingMat;
            skyManagerSO.ApplyModifiedProperties();
        }

        private WeatherProfile CheckAndCreateDefaultProfile()
        {
            const string fullPath = "Assets/Horizon Weather & Time/Weather Profiles/Default Clear.asset";
            WeatherProfile p = AssetDatabase.LoadAssetAtPath<WeatherProfile>(fullPath);
            if (p == null)
            {
                if (!Directory.Exists("Assets/Horizon Weather & Time/Weather Profiles")) Directory.CreateDirectory("Assets/Horizon Weather & Time/Weather Profiles");
                p = CreateInstance<WeatherProfile>(); p.profileName = "Default Clear"; p.skySettings.exposure = 0.3f; p.skySettings.turbidity = 5f;
                AssetDatabase.CreateAsset(p, fullPath);
            }
            return p;
        }

        private WeatherProfile CheckAndCreateDefaultSnowProfile()
        {
            const string fullPath = "Assets/Horizon Weather & Time/Weather Profiles/Default Snow.asset";
            WeatherProfile p = AssetDatabase.LoadAssetAtPath<WeatherProfile>(fullPath);
            var prefab = Resources.Load<GameObject>("Prefabs/SnowEffect");
            if (p == null && prefab != null)
            {
                p = CreateInstance<WeatherProfile>(); p.name = "Default Snow"; p.profileName = "Default Snow"; p.lightSettings.sunColorZenith = new Color(0.8f, 0.85f, 0.95f); p.effectsSettings.weatherEffectPrefab = prefab;
                AssetDatabase.CreateAsset(p, fullPath);
            }
            return p;
        }

        private void CheckAndGenerateCloudTexture(List<WeatherProfile> profiles)
        {
            string path = CloudNoiseGenerator.DEFAULT_NOISE_PATH;
            Texture2D cloudTex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (cloudTex == null)
            {
                Debug.Log("<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Cloud Noise missing. Generating default...</color>");
                cloudTex = CloudNoiseGenerator.GenerateAndSaveTexture(512, 4, path);
            }

            foreach (var p in profiles)
            {
                if (p != null && p.cloudSettings.cloudNoiseTexture == null)
                {
                    p.cloudSettings.cloudNoiseTexture = cloudTex;
                    EditorUtility.SetDirty(p);
                    Debug.Log($"<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Auto-assigned Cloud Noise to profile '{p.name}'.</color>");
                }
            }
        }

        private void EnsureProfilesAreInAllowedList(List<WeatherProfile> profiles)
        {
            if (profiles.Count == 0) return;

            List<UnityEngine.Object> allowedList;
            if (_target.weatherProfilesList == null)
            {
                allowedList = new List<UnityEngine.Object>();
            }
            else
            {
                allowedList = new List<UnityEngine.Object>(_target.weatherProfilesList);
            }

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
            if (p.skySettings.starsSettings.starsTexture != null) return;
            string[] guids = AssetDatabase.FindAssets("starmap_horizon_8k t:Texture");
            if (guids.Length > 0) { p.skySettings.starsSettings.starsTexture = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guids[0])); EditorUtility.SetDirty(p); }
        }

        private void CheckAndAssignDefaultMoonTexture(WeatherProfile p)
        {
            if (p.moonSettings.moonTexture != null) return;

            string[] guids = AssetDatabase.FindAssets("lroc_color_poles_1k t:Texture");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                p.moonSettings.moonTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                EditorUtility.SetDirty(p);
                Debug.Log($"<b><color=#33FF33>[LOG]</color></b> <color=white>[WeatherTimeSystem] Auto-assigned Moon texture to profile '{p.name}'.</color>");
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

        private WeatherProfile CheckAndCreateDefaultRainProfile()
        {
            const string fullPath = "Assets/Horizon Weather & Time/Weather Profiles/Default Rain.asset";
            WeatherProfile p = AssetDatabase.LoadAssetAtPath<WeatherProfile>(fullPath);

            var prefab = Resources.Load<GameObject>("Prefabs/RainEffect");

            if (p == null)
            {
                if (!Directory.Exists("Assets/Horizon Weather & Time/Weather Profiles")) Directory.CreateDirectory("Assets/Horizon Weather & Time/Weather Profiles");

                p = CreateInstance<WeatherProfile>();
                p.name = "Default Rain";
                p.profileName = "Heavy Rain";

                p.lightSettings.sunIntensity = 0.5f;
                p.lightSettings.sunColorZenith = new Color(0.6f, 0.65f, 0.7f);
                p.lightSettings.dayAmbientColor = new Color(0.3f, 0.35f, 0.4f);

                p.cloudSettings.coverage = 0.85f;
                p.cloudSettings.density = 2.0f;
                p.cloudSettings.baseColor = new Color(0.3f, 0.3f, 0.35f);
                p.cloudSettings.shadowColor = new Color(0.1f, 0.1f, 0.15f);

                p.effectsSettings.weatherEffectPrefab = prefab;

                AssetDatabase.CreateAsset(p, fullPath);
            }
            else if (p.effectsSettings.weatherEffectPrefab == null && prefab != null)
            {
                p.effectsSettings.weatherEffectPrefab = prefab;
                EditorUtility.SetDirty(p);
            }

            return p;
        }

        // ================================================================
        // AUTO-SETUP FOR VRCHAT
        // ================================================================

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
    }
}