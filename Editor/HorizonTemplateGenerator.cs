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
        private static string TEMPLATES_ROOT => GetPackageTemplatesRoot();
        private static string PRESETS_DIR => TEMPLATES_ROOT + "/Presets";
        private static string MODULES_DIR => TEMPLATES_ROOT + "/Modules";

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
                "Fog", "Fog_Clear", force, fp =>
                {
                    fp.renderMode = HorizonFogMode.AnalyticalAtmospheric;
                    fp.fogMode = FogMode.ExponentialSquared;

                    // Light, far atmospheric haze: the valley stays readable.
                    fp.dayColor = new Color(0.68f, 0.76f, 0.88f);
                    fp.nightColor = new Color(0.04f, 0.05f, 0.10f);
                    fp.dayZenithColor = new Color(0.46f, 0.56f, 0.70f);
                    fp.nightZenithColor = new Color(0.02f, 0.025f, 0.05f);

                    fp.density = 0.0003f;
                    fp.heightDensity = 0.0008f;
                    fp.heightFalloff = 0.03f;
                    fp.heightBaseOffset = 0f;
                    fp.startDistance = 20f;
                    fp.skyHorizonHeight = 0.14f;
                    fp.maxOpacity = 0.85f;
                    fp.skyboxBlendIntegrity = 1f;

                    fp.sunInscatterColor = new Color(0.95f, 0.92f, 0.85f);
                    fp.sunInscatterIntensity = 0.22f;
                    fp.sunInscatterPower = 8f;
                });

            var eClear = CreateModule<EffectsProfile>(
                "Effects", "Effects_Clear", force);

            var aClear = CreateModule<AudioProfile>(
                "Audio", "Audio_Clear", force, ap =>
                {
                    ap.rainVolume = 0.0f;
                    ap.windVolume = 0.18f;
                    ap.windGustStrength = 0.35f;
                });

            CreatePreset("Template_Clear", "Clear", force,
                lClear, sClear, cClear, moonDefault, fClear, eClear, aClear);

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
                "Fog", "Fog_Rain", force, fp =>
                {
                    fp.renderMode = HorizonFogMode.AnalyticalAtmospheric;
                    fp.fogMode = FogMode.ExponentialSquared;

                    fp.dayColor = new Color(0.34f, 0.39f, 0.45f);
                    fp.dayZenithColor = new Color(0.20f, 0.24f, 0.30f);
                    fp.nightColor = new Color(0.03f, 0.04f, 0.06f);
                    fp.nightZenithColor = new Color(0.015f, 0.02f, 0.035f);

                    fp.density = 0.0022f;
                    fp.heightDensity = 0.0085f;
                    fp.heightFalloff = 0.035f;
                    fp.heightBaseOffset = 0f;
                    fp.startDistance = 4f;
                    fp.skyHorizonHeight = 0.35f;
                    fp.maxOpacity = 0.96f;
                    fp.skyboxBlendIntegrity = 1f;

                    fp.sunInscatterColor = new Color(0.75f, 0.78f, 0.82f);
                    fp.sunInscatterIntensity = 0.14f;
                    fp.sunInscatterPower = 5f;
                });

            var eRain = CreateModule<EffectsProfile>(
                "Effects", "Effects_Rain", force, ep =>
                {
                    TryAssignPrefab(ep, "RainEffect");
                });

            var aRain = CreateModule<AudioProfile>(
                "Audio", "Audio_Rain", force, ap =>
                {
                    ap.rainVolume = 0.8f;
                    ap.windVolume = 0.18f;
                    ap.windGustStrength = 0.35f;
                });

            CreatePreset("Template_Rain", "Rain", force,
                lRain, skyOvercast, cStorm, moonDefault, fRain, eRain, aRain);

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
                "Fog", "Fog_Snow", force, fp =>
                {
                    fp.renderMode = HorizonFogMode.AnalyticalAtmospheric;
                    fp.fogMode = FogMode.ExponentialSquared;

                    // Cold, dense whiteout haze.
                    fp.dayColor = new Color(0.58f, 0.63f, 0.70f);
                    fp.dayZenithColor = new Color(0.40f, 0.45f, 0.52f);
                    fp.nightColor = new Color(0.05f, 0.06f, 0.08f);
                    fp.nightZenithColor = new Color(0.025f, 0.03f, 0.045f);

                    fp.density = 0.0045f;
                    fp.heightDensity = 0.006f;
                    fp.heightFalloff = 0.04f;
                    fp.heightBaseOffset = 0f;
                    fp.startDistance = 5f;
                    fp.skyHorizonHeight = 0.30f;
                    fp.maxOpacity = 0.95f;
                    fp.skyboxBlendIntegrity = 1f;

                    fp.sunInscatterColor = new Color(0.90f, 0.92f, 0.95f);
                    fp.sunInscatterIntensity = 0.20f;
                    fp.sunInscatterPower = 6f;
                });

            var eSnow = CreateModule<EffectsProfile>(
                "Effects", "Effects_Snow", force, ep =>
                {
                    TryAssignPrefab(ep, "SnowEffect");
                });

            var aSnow = CreateModule<AudioProfile>(
                "Audio", "Audio_Snow", force, ap =>
                {
                    ap.rainVolume = 0.0f;
                    ap.windVolume = 0.18f;
                    ap.windGustStrength = 0.35f;
                    ap.windPitch = 0.9f;
                });

            CreatePreset("Template_Snow", "Snow", force,
                lSnow, skyOvercast, cOvercast, moonDefault, fSnow, eSnow, aSnow);

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
            MoonProfile mp, FogProfile fp, EffectsProfile ep, AudioProfile ap)
        {
            string path = $"{PRESETS_DIR}/{name}.asset";
            WeatherProfile existing = AssetDatabase.LoadAssetAtPath<WeatherProfile>(path);

            if (existing != null && !force)
            {
                if (existing.audioProfile == null && ap != null)
                {
                    existing.audioProfile = ap;
                    EditorUtility.SetDirty(existing);
                }
                return;
            }

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
            preset.audioProfile = ap;

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
                $"{MODULES_DIR}/Effects",
                $"{MODULES_DIR}/Audio"
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

            // --- AUDIO CLIPS ---
            var rainExtStereo = AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherAudioGenerator.RAIN_EXT_STEREO_PATH);
            var rainExtMono   = AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherAudioGenerator.RAIN_EXT_MONO_PATH);
            var rainIntStereo = AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherAudioGenerator.RAIN_INT_STEREO_PATH);
            var rainRoofMono  = AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherAudioGenerator.RAIN_ROOF_MONO_PATH);
            var windBase      = AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherAudioGenerator.WIND_BASE_STEREO_PATH);
            var windGust      = AssetDatabase.LoadAssetAtPath<AudioClip>(WeatherAudioGenerator.WIND_GUST_STEREO_PATH);

            string[] audioGuids = AssetDatabase.FindAssets("t:AudioProfile", new[] { $"{MODULES_DIR}/Audio" });
            foreach (string guid in audioGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioProfile ap = AssetDatabase.LoadAssetAtPath<AudioProfile>(path);
                if (ap == null) continue;

                bool dirty = false;
                if (ap.rainExteriorStereoClip == null && rainExtStereo != null) { ap.rainExteriorStereoClip = rainExtStereo; dirty = true; }
                if (ap.rainExteriorMonoClip == null && rainExtMono != null) { ap.rainExteriorMonoClip = rainExtMono; dirty = true; }
                if (ap.rainInteriorStereoClip == null && rainIntStereo != null) { ap.rainInteriorStereoClip = rainIntStereo; dirty = true; }
                if (ap.rainRoofMonoClip == null && rainRoofMono != null) { ap.rainRoofMonoClip = rainRoofMono; dirty = true; }
                if (ap.windBaseStereoClip == null && windBase != null) { ap.windBaseStereoClip = windBase; dirty = true; }
                if (ap.windGustStereoClip == null && windGust != null) { ap.windGustStereoClip = windGust; dirty = true; }
                if (dirty) EditorUtility.SetDirty(ap);
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

        /// <summary>
        /// Returns the path to Internal/Templates inside the package folder.
        /// Works whether the package is in Packages/ (VCC/git) or Assets/ (embedded).
        /// </summary>
        private static string GetPackageTemplatesRoot()
        {
            string packageRoot = FindPackageRoot();
            if (!string.IsNullOrEmpty(packageRoot))
            {
                return packageRoot + "/Internal/Templates";
            }
            return "Assets/Horizon Weather & Time/Internal/Templates";
        }

        /// <summary>
        /// Regenerates Fog template assets in Internal/Templates when upgrading from legacy fog settings.
        /// </summary>
        public static void EnsureModernFogTemplates()
        {
            string fogRainPath = $"{MODULES_DIR}/Fog/Fog_Rain.asset";
            FogProfile existingRain = AssetDatabase.LoadAssetAtPath<FogProfile>(fogRainPath);
            if (existingRain != null &&
                Mathf.Approximately(existingRain.dayColor.r, 0.7f) &&
                Mathf.Approximately(existingRain.dayColor.g, 0.8f) &&
                Mathf.Approximately(existingRain.dayColor.b, 1.0f))
            {
                GenerateTemplates(true);
            }
        }
    }
}
#endif