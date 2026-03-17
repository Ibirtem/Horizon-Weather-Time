#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace BlackHorizon.HorizonWeatherTime
{
    public static class WeatherBakeUtility
    {
        private const string CONTAINER_NAME = "_BakedWeatherData";
        private static bool _isBaking = false;

        private const string PROFILES_ROOT = "Assets/Horizon Weather & Time/Weather Profiles";
        private const string MODULES_DIR = PROFILES_ROOT + "/Modules";

        public static readonly (string folder, System.Type type)[] MODULE_TYPES =
        {
            ("Lighting", typeof(LightingProfile)),
            ("Sky",      typeof(SkyProfile)),
            ("Clouds",   typeof(CloudProfile)),
            ("Moon",     typeof(MoonProfile)),
            ("Fog",      typeof(FogProfile)),
            ("Effects",  typeof(EffectsProfile)),
        };

        public static Dictionary<string, List<ScriptableObject>> ScanModuleFolders()
        {
            var discoveredModules = new Dictionary<string, List<ScriptableObject>>();

            foreach (var (folder, type) in MODULE_TYPES)
            {
                string dir = $"{MODULES_DIR}/{folder}";
                var results = new List<ScriptableObject>();

                if (System.IO.Directory.Exists(dir))
                {
                    string[] guids = AssetDatabase.FindAssets($"t:{type.Name}", new[] { dir });
                    foreach (string guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        var module = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
                        if (module != null) results.Add(module);
                    }
                    results.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
                }
                discoveredModules[folder] = results;
            }
            return discoveredModules;
        }

        private const string MODULE_CONTAINER_NAME = "_BakedModules";

        /// <summary>
        /// Bakes all discovered modules. Completely generic — no per-type copy code.
        /// Adding a new module type only requires a new case in AssignModuleArray.
        /// </summary>
        public static void BakeModules(WeatherTimeSystem system, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            if (system == null || _isBaking) return;
            if (discoveredModules == null || discoveredModules.Count == 0) return;

            _isBaking = true;
            int totalBakedCount = 0; 

            try
            {
                Transform legacyContainer = system.transform.Find("_BakedWeatherData");
                if (legacyContainer != null) Object.DestroyImmediate(legacyContainer.gameObject);

                Transform container = system.transform.Find(MODULE_CONTAINER_NAME);
                if (container == null)
                {
                    var go = new GameObject(MODULE_CONTAINER_NAME);
                    go.transform.SetParent(system.transform);
                    go.transform.localPosition = Vector3.zero;
                    container = go.transform;
                }
                
                container.gameObject.hideFlags = HideFlags.None;

                for (int i = container.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(container.GetChild(i).gameObject);

                foreach (var kvp in discoveredModules)
                {
                    var array = BakeModuleList(container, kvp.Key, kvp.Value);
                    AssignModuleArray(system, kvp.Key, array);
                    
                    totalBakedCount += array.Length;
                }

                UpdatePresetMappings(system, discoveredModules);

                EditorUtility.SetDirty(system);
                AssetDatabase.SaveAssets();

                Debug.Log($"<b><color=#33FF33>[BAKE]</color></b> <color=white>Baked {totalBakedCount} modules across {discoveredModules.Count} categories.</color>", system);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"<b><color=#FF3333>[BAKE ERROR]</color></b> Critical error during baking: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _isBaking = false;
            }
        }

        /// <summary>
        /// Bakes a list of modules.
        /// </summary>
        private static BakedProfileData[] BakeModuleList(Transform container, string groupName, List<ScriptableObject> modules)
        {
            if (modules == null || modules.Count == 0) return new BakedProfileData[0];

            var groupObj = new GameObject($"_{groupName}");
            groupObj.transform.SetParent(container);
            groupObj.transform.localPosition = Vector3.zero;
            groupObj.hideFlags = HideFlags.HideInHierarchy;

            var resultList = new System.Collections.Generic.List<BakedProfileData>();

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] == null) continue;

                var child = new GameObject($"_{resultList.Count}_{modules[i].name}");
                child.transform.SetParent(groupObj.transform);
                child.transform.localPosition = Vector3.zero;
                child.hideFlags = HideFlags.HideInHierarchy;

                var baked = child.AddComponent<BakedProfileData>();
                baked.profileName = modules[i].name;
                baked.CopyFromModule(modules[i]);

                EditorUtility.SetDirty(baked);
                resultList.Add(baked);
            }

            return resultList.ToArray();
        }

        /// <summary>
        /// Routes a baked array to the correct field on WeatherTimeSystem.
        /// </summary>
        private static void AssignModuleArray(WeatherTimeSystem system,
            string folderName, BakedProfileData[] array)
        {
            switch (folderName)
            {
                case "Lighting": system.bakedLightingModules = array; break;
                case "Sky":      system.bakedSkyModules = array; break;
                case "Clouds":   system.bakedCloudModules = array; break;
                case "Moon":     system.bakedMoonModules = array; break;
                case "Fog":      system.bakedFogModules = array; break;
                case "Effects":  system.bakedEffectsModules = array; break;
            }
        }

        /// <summary>
        /// Checks if a rebake is needed for the new modular system.
        /// </summary>
        public static bool NeedsRebakeModules(WeatherTimeSystem system, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            if (system == null || discoveredModules == null) return false;

            if (!CompareCategoryCount(discoveredModules, "Lighting", system.bakedLightingModules)) return true;
            if (!CompareCategoryCount(discoveredModules, "Sky", system.bakedSkyModules)) return true;
            if (!CompareCategoryCount(discoveredModules, "Clouds", system.bakedCloudModules)) return true;
            if (!CompareCategoryCount(discoveredModules, "Moon", system.bakedMoonModules)) return true;
            if (!CompareCategoryCount(discoveredModules, "Fog", system.bakedFogModules)) return true;
            if (!CompareCategoryCount(discoveredModules, "Effects", system.bakedEffectsModules)) return true;

            if (system.presetToLighting == null || 
                (system.weatherProfilesList != null && system.presetToLighting.Length != system.weatherProfilesList.Length)) 
                return true;

            return false;
        }

        private static bool CompareCategoryCount(Dictionary<string, List<ScriptableObject>> discovered, string key, BakedProfileData[] baked)
        {
            int discoveredCount = discovered.TryGetValue(key, out var list) ? list.Count : 0;
            int bakedCount = baked != null ? baked.Length : 0;
            return discoveredCount == bakedCount;
        }

        private static void UpdatePresetMappings(WeatherTimeSystem system, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            int count = system.weatherProfilesList != null ? system.weatherProfilesList.Length : 0;
            
            system.presetToLighting = new int[count];
            system.presetToSky = new int[count];
            system.presetToCloud = new int[count];
            system.presetToMoon = new int[count];
            system.presetToFog = new int[count];
            system.presetToEffects = new int[count];

            for (int i = 0; i < count; i++)
            {
                var wp = system.weatherProfilesList[i] as WeatherProfile;
                if (wp != null)
                {
                    system.presetToLighting[i] = FindValidIndex("Lighting", wp.lightingProfile, discoveredModules);
                    system.presetToSky[i] = FindValidIndex("Sky", wp.skyProfile, discoveredModules);
                    system.presetToCloud[i] = FindValidIndex("Clouds", wp.cloudProfile, discoveredModules);
                    system.presetToMoon[i] = FindValidIndex("Moon", wp.moonProfile, discoveredModules);
                    system.presetToFog[i] = FindValidIndex("Fog", wp.fogProfile, discoveredModules);
                    system.presetToEffects[i] = FindValidIndex("Effects", wp.effectsProfile, discoveredModules);
                }
                else if (system.weatherProfilesList[i] != null)
                {
                    Debug.LogWarning($"<b><color=#FF9900>[WeatherBake]</color></b> weatherProfilesList[{i}] is not a WeatherProfile!");
                }
            }
        }

        private static int FindValidIndex(string folder, ScriptableObject module, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            if (module == null) return 0; 
            if (!discoveredModules.TryGetValue(folder, out var list)) return 0;

            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], module)) return i;
            }

            Debug.LogWarning($"<b><color=#FF3333>[BAKE ERROR]</color></b> Module '{module.name}' not found in {folder} folder! " +
                             "Ensure it is placed in the correct subfolder under Weather Profiles/Modules.");
            return 0;
        }

        private static int FindModuleIndex(string folder, ScriptableObject module, Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            if (module == null || !discoveredModules.ContainsKey(folder)) return 0;
            var list = discoveredModules[folder];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].name == module.name) return i;
            }
            return 0;
        }
    }
}
#endif