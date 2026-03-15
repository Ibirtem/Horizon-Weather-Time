#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Developer tool: generates default template .asset files for Clear/Rain/Snow presets.
    /// </summary>
    public static class HorizonTemplateGenerator
    {
        private const string TEMPLATES_ROOT = "Assets/Horizon Weather & Time/Internal/Templates";
        private const string PRESETS_DIR = TEMPLATES_ROOT + "/Presets";
        private const string MODULES_DIR = TEMPLATES_ROOT + "/Modules";

        // ================================================================
        // MENU ITEMS
        // ================================================================

        [MenuItem("Tools/Horizon/WeatherTime/Generate Default Templates")]
        public static void Generate()
        {
            GenerateTemplates(false);
        }

        [MenuItem("Tools/Horizon/WeatherTime/Force Regenerate All Templates")]
        public static void ForceRegenerate()
        {
            if (!EditorUtility.DisplayDialog(
                "Force Regenerate Templates",
                "This will OVERWRITE all template assets.\n" +
                "Any manual tweaks in Internal/Templates will be lost.\n\n" +
                "Continue?",
                "Overwrite", "Cancel")) return;

            GenerateTemplates(true);
        }

        // ================================================================
        // MAIN GENERATION LOGIC
        // ================================================================

        private static void GenerateTemplates(bool force)
        {
            EnsureDirectories();

            // -----------------------------------------------------------
            // SHARED MODULES
            // -----------------------------------------------------------

            var moonDefault = CreateModule<MoonProfile>(
                "Moon", "Moon_Default", force);

            var skyOvercast = CreateModule<SkyProfile>(
                "Sky", "Sky_Overcast", force, sp =>
                {
                    sp.turbidity = 10f;
                    sp.exposure = 7.5f;
                    sp.rayleigh = 0.5f;
                });

            // -----------------------------------------------------------
            // CLEAR
            // -----------------------------------------------------------

            var lClear = CreateModule<LightingProfile>(
                "Lighting", "Lighting_Clear", force);

            var sClear = CreateModule<SkyProfile>(
                "Sky", "Sky_Clear", force);

            var cClear = CreateModule<CloudProfile>(
                "Clouds", "Clouds_Clear", force);

            var fClear = CreateModule<FogProfile>(
                "Fog", "Fog_Clear", force);

            var eClear = CreateModule<EffectsProfile>(
                "Effects", "Effects_Clear", force);

            CreatePreset("Template_Clear", "Clear", force,
                lClear, sClear, cClear, moonDefault, fClear, eClear);

            // -----------------------------------------------------------
            // RAIN
            // -----------------------------------------------------------

            var lRain = CreateModule<LightingProfile>(
                "Lighting", "Lighting_Rain", force, lp =>
                {
                    lp.sunIntensity = 0.5f;
                    lp.sunColorZenith = new Color(0.6f, 0.65f, 0.7f);
                    lp.daySkyColor = new Color(0.3f, 0.35f, 0.4f);
                    lp.dayEquatorColor = new Color(0.25f, 0.3f, 0.35f);
                    lp.dayGroundColor = new Color(0.15f, 0.2f, 0.25f);
                });

            var cStorm = CreateModule<CloudProfile>(
                "Clouds", "Clouds_Storm", force, cp =>
                {
                    cp.coverage = 0.85f;
                    cp.density = 2.0f;
                    cp.baseColor = new Color(0.3f, 0.3f, 0.35f);
                    cp.shadowColor = new Color(0.1f, 0.1f, 0.15f);
                });

            var fRain = CreateModule<FogProfile>(
                "Fog", "Fog_Rain", force);

            var eRain = CreateModule<EffectsProfile>(
                "Effects", "Effects_Rain", force, ep =>
                {
                    TryAssignPrefab(ep, "RainEffect");
                });

            CreatePreset("Template_Rain", "Rain", force,
                lRain, skyOvercast, cStorm, moonDefault, fRain, eRain);

            // -----------------------------------------------------------
            // SNOW
            // -----------------------------------------------------------

            var lSnow = CreateModule<LightingProfile>(
                "Lighting", "Lighting_Snow", force, lp =>
                {
                    lp.sunColorZenith = new Color(0.8f, 0.85f, 0.95f);
                    lp.daySkyColor = new Color(0.5f, 0.6f, 0.7f);
                    lp.dayEquatorColor = new Color(0.6f, 0.65f, 0.7f);
                    lp.dayGroundColor = new Color(0.7f, 0.75f, 0.8f);
                });

            var cOvercast = CreateModule<CloudProfile>(
                "Clouds", "Clouds_Overcast", force);

            var fSnow = CreateModule<FogProfile>(
                "Fog", "Fog_Snow", force);

            var eSnow = CreateModule<EffectsProfile>(
                "Effects", "Effects_Snow", force, ep =>
                {
                    TryAssignPrefab(ep, "SnowEffect");
                });

            CreatePreset("Template_Snow", "Snow", force,
                lSnow, skyOvercast, cOvercast, moonDefault, fSnow, eSnow);

            // -----------------------------------------------------------
            // FINALIZE
            // -----------------------------------------------------------

            LinkTexturesToModules();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int presetCount = Directory.GetFiles(PRESETS_DIR, "*.asset").Length;
            int moduleCount = 0;
            foreach (var dir in Directory.GetDirectories(MODULES_DIR))
                moduleCount += Directory.GetFiles(dir, "*.asset").Length;

            Debug.Log($"<b><color=#33FF33>[TEMPLATES]</color></b> <color=white>Generated {presetCount} presets + {moduleCount} modules in {TEMPLATES_ROOT}</color>");

            EditorUtility.DisplayDialog("Template Generator",
                $"Generated {presetCount} presets + {moduleCount} modules.\n\n" +
                $"Location: {TEMPLATES_ROOT}\n\n" +
                "You can now tune their values in the Inspector.\n" +
                "These templates will be used as source for new user presets.",
                "OK");
        }

        // ================================================================
        // FACTORY METHODS
        // ================================================================

        /// <summary>
        /// Creates a sub-profile .asset in the templates folder.
        /// Non-force: skips if file exists (preserves manual tweaks).
        /// Force: overwrites with fresh C# defaults + initialize callback.
        /// </summary>
        private static T CreateModule<T>(string folder, string name, bool force,
            System.Action<T> initialize = null) where T : ScriptableObject
        {
            string dir = $"{MODULES_DIR}/{folder}";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string path = $"{dir}/{name}.asset";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);

            if (existing != null && !force) return existing;

            if (existing != null)
            {
                T fresh = ScriptableObject.CreateInstance<T>();
                initialize?.Invoke(fresh);
                EditorUtility.CopySerialized(fresh, existing);
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            T module = ScriptableObject.CreateInstance<T>();
            initialize?.Invoke(module);
            AssetDatabase.CreateAsset(module, path);
            return module;
        }

        /// <summary>
        /// Creates a master WeatherProfile .asset linking to sub-profiles.
        /// Non-force: skips if file exists.
        /// Force: overwrites name and all sub-profile references.
        /// </summary>
        private static void CreatePreset(string name, string displayName, bool force,
            LightingProfile lp, SkyProfile sp, CloudProfile cp,
            MoonProfile mp, FogProfile fp, EffectsProfile ep)
        {
            string path = $"{PRESETS_DIR}/{name}.asset";
            WeatherProfile existing = AssetDatabase.LoadAssetAtPath<WeatherProfile>(path);

            if (existing != null && !force) return;

            WeatherProfile preset = existing;
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<WeatherProfile>();
                AssetDatabase.CreateAsset(preset, path);
            }

            preset.profileName = displayName;
            preset.lightingProfile = lp;
            preset.skyProfile = sp;
            preset.cloudProfile = cp;
            preset.moonProfile = mp;
            preset.fogProfile = fp;
            preset.effectsProfile = ep;
            EditorUtility.SetDirty(preset);
        }

        /// <summary>
        /// Tries to assign a weather effect prefab by name.
        /// Prefabs are generated separately by CheckAndConfigureParticleAssets.
        /// If the prefab doesn't exist yet, the field stays null.
        /// </summary>
        private static void TryAssignPrefab(EffectsProfile ep, string prefabName)
        {
            string path = $"Assets/Horizon Weather & Time/Resources/Prefabs/{prefabName}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) ep.weatherEffectPrefab = prefab;
        }

        private static void EnsureDirectories()
        {
            string[] dirs =
            {
                TEMPLATES_ROOT,
                PRESETS_DIR,
                $"{MODULES_DIR}/Lighting",
                $"{MODULES_DIR}/Sky",
                $"{MODULES_DIR}/Clouds",
                $"{MODULES_DIR}/Moon",
                $"{MODULES_DIR}/Fog",
                $"{MODULES_DIR}/Effects"
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
        }

        /// <summary>
        /// Finds and assigns texture assets to all template modules that need them.
        /// </summary>
        private static void LinkTexturesToModules()
        {
            // --- CLOUD TEXTURES ---
            Texture3D cloudNoise3D = AssetDatabase.LoadAssetAtPath<Texture3D>(
                WeatherOptimizationGen.DEFAULT_CLOUD_NOISE_3D_PATH);
            Texture2D weatherMap = AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_WEATHER_MAP_PATH);
            Texture2D blueNoise = AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_BLUE_NOISE_PATH);
            Texture2D cirrusNoise = AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_CIRRUS_NOISE_PATH);
            Texture2D curlNoise = AssetDatabase.LoadAssetAtPath<Texture2D>(
                WeatherOptimizationGen.DEFAULT_CURL_NOISE_PATH);

            string[] cloudGuids = AssetDatabase.FindAssets("t:CloudProfile",
                new[] { $"{MODULES_DIR}/Clouds" });

            foreach (string guid in cloudGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CloudProfile cp = AssetDatabase.LoadAssetAtPath<CloudProfile>(path);
                if (cp == null) continue;

                bool dirty = false;
                if (cp.cloudNoiseTexture == null && cloudNoise3D != null)
                    { cp.cloudNoiseTexture = cloudNoise3D; dirty = true; }
                if (cp.weatherMapTexture == null && weatherMap != null)
                    { cp.weatherMapTexture = weatherMap; dirty = true; }
                if (cp.blueNoiseTexture == null && blueNoise != null)
                    { cp.blueNoiseTexture = blueNoise; dirty = true; }
                if (cp.cirrusNoiseTexture == null && cirrusNoise != null)
                    { cp.cirrusNoiseTexture = cirrusNoise; dirty = true; }
                if (cp.curlNoiseTexture == null && curlNoise != null)
                    { cp.curlNoiseTexture = curlNoise; dirty = true; }

                if (dirty) EditorUtility.SetDirty(cp);
            }

            string packageRoot = FindPackageRoot();
            string[] searchScope = packageRoot != null
                ? new[] { packageRoot }
                : null;

            // --- STAR CUBEMAPS ---
            Cubemap starsCube = FindAsset<Cubemap>("stars t:Cubemap", searchScope);
            Cubemap milkyWayCube = FindAsset<Cubemap>("milkyway t:Cubemap", searchScope);

            string[] skyGuids = AssetDatabase.FindAssets("t:SkyProfile",
                new[] { $"{MODULES_DIR}/Sky" });

            foreach (string guid in skyGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                SkyProfile sp = AssetDatabase.LoadAssetAtPath<SkyProfile>(path);
                if (sp == null) continue;

                bool dirty = false;
                if (sp.starsCubemap == null && starsCube != null)
                    { sp.starsCubemap = starsCube; dirty = true; }
                if (sp.milkyWayCubemap == null && milkyWayCube != null)
                    { sp.milkyWayCubemap = milkyWayCube; dirty = true; }

                if (dirty) EditorUtility.SetDirty(sp);
            }

            // --- MOON TEXTURE ---
            Texture2D moonTex = FindAsset<Texture2D>("lroc_color_poles t:Texture", searchScope);

            string[] moonGuids = AssetDatabase.FindAssets("t:MoonProfile",
                new[] { $"{MODULES_DIR}/Moon" });

            foreach (string guid in moonGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MoonProfile mp = AssetDatabase.LoadAssetAtPath<MoonProfile>(path);
                if (mp == null) continue;

                if (mp.moonTexture == null && moonTex != null)
                {
                    mp.moonTexture = moonTex;
                    EditorUtility.SetDirty(mp);
                }
            }
        }

        /// <summary>
        /// Finds the first asset matching a filter in the given folder.
        /// </summary>
        private static T FindAsset<T>(string filter, string[] searchFolders = null)
            where T : Object
        {
            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets(filter, searchFolders)
                : AssetDatabase.FindAssets(filter);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        /// <summary>
        /// Finds the package root by locating this script's own path.
        /// Works regardless of install method (Assets/, Packages/, VCC).
        /// </summary>
        private static string FindPackageRoot()
        {
            string[] guids = AssetDatabase.FindAssets("HorizonTemplateGenerator t:MonoScript");
            if (guids.Length > 0)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                // scriptPath = ".../Horizon Weather & Time/Editor/HorizonTemplateGenerator.cs"
                string editorDir = Path.GetDirectoryName(scriptPath);
                return Path.GetDirectoryName(editorDir);
            }
            return null;
        }
    }
}
#endif