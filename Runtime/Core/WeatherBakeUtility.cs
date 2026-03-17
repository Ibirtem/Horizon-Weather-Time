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

        public static void BakeAllProfiles(WeatherTimeSystem system)
        {
            if (system == null || _isBaking) return;
            if (system.weatherProfilesList == null || system.weatherProfilesList.Length == 0)
            {
                ClearContainer(system);
                return;
            }

            _isBaking = true;

            try
            {
                int count = system.weatherProfilesList.Length;

                Transform container = system.transform.Find(CONTAINER_NAME);
                if (container == null)
                {
                    var go = new GameObject(CONTAINER_NAME);
                    go.transform.SetParent(system.transform);
                    go.transform.localPosition = Vector3.zero;
                    container = go.transform;
                }
                container.gameObject.hideFlags = HideFlags.HideInHierarchy;

                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(container.GetChild(i).gameObject);
                }

                var bakedArray = new BakedProfileData[count];

                for (int i = 0; i < count; i++)
                {
                    var wp = system.weatherProfilesList[i] as WeatherProfile;

                    var child = new GameObject($"_{i}");
                    child.transform.SetParent(container);
                    child.transform.localPosition = Vector3.zero;
                    child.hideFlags = HideFlags.HideInHierarchy;

                    var baked = child.AddComponent<BakedProfileData>();

                    if (wp != null)
                    {
                        baked.CopyFromProfile(wp);
                    }

                    bakedArray[i] = baked;
                }

                system.bakedProfiles = bakedArray;
                EditorUtility.SetDirty(system);

                Debug.Log($"<b><color=#33FF33>[BAKE]</color></b> <color=white>Baked {count} profiles.</color>", system);
            }
            finally
            {
                _isBaking = false;
            }
        }

        private const string MODULE_CONTAINER_NAME = "_BakedModules";

        /// <summary>
        /// Bakes all discovered modules. Completely generic — no per-type copy code.
        /// Adding a new module type only requires a new case in AssignModuleArray.
        /// </summary>
        public static void BakeModules(WeatherTimeSystem system,
            Dictionary<string, List<ScriptableObject>> discoveredModules)
        {
            if (system == null || _isBaking) return;
            if (discoveredModules == null || discoveredModules.Count == 0) return;

            _isBaking = true;

            try
            {
                Transform container = system.transform.Find(MODULE_CONTAINER_NAME);
                if (container == null)
                {
                    var go = new GameObject(MODULE_CONTAINER_NAME);
                    go.transform.SetParent(system.transform);
                    go.transform.localPosition = Vector3.zero;
                    container = go.transform;
                }
                container.gameObject.hideFlags = HideFlags.HideInHierarchy;

                for (int i = container.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(container.GetChild(i).gameObject);

                int total = 0;

                foreach (var kvp in discoveredModules)
                {
                    var array = BakeModuleList(container, kvp.Key, kvp.Value);
                    AssignModuleArray(system, kvp.Key, array);
                    total += kvp.Value.Count;
                }

                EditorUtility.SetDirty(system);

                Debug.Log($"<b><color=#33FF33>[BAKE]</color></b> <color=white>Baked {total} modules across {discoveredModules.Count} categories.</color>", system);
            }
            finally
            {
                _isBaking = false;
            }
        }

        /// <summary>
        /// Bakes a list of modules.
        /// </summary>
        private static BakedProfileData[] BakeModuleList(
            Transform container, string groupName,
            List<ScriptableObject> modules)
        {
            if (modules == null || modules.Count == 0)
                return new BakedProfileData[0];

            var groupObj = new GameObject($"_{groupName}");
            groupObj.transform.SetParent(container);
            groupObj.transform.localPosition = Vector3.zero;
            groupObj.hideFlags = HideFlags.HideInHierarchy;

            var result = new BakedProfileData[modules.Count];

            for (int i = 0; i < modules.Count; i++)
            {
                var child = new GameObject($"_{i}_{modules[i].name}");
                child.transform.SetParent(groupObj.transform);
                child.transform.localPosition = Vector3.zero;
                child.hideFlags = HideFlags.HideInHierarchy;

                var baked = child.AddComponent<BakedProfileData>();
                baked.profileName = modules[i].name;
                baked.CopyFromModule(modules[i]);

                result[i] = baked;
            }

            return result;
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

        public static void RebakeSingleProfile(WeatherTimeSystem system, int index)
        {
            if (system == null || _isBaking) return;
            if (system.bakedProfiles == null) return;
            if (index < 0 || index >= system.bakedProfiles.Length) return;

            var baked = system.bakedProfiles[index];
            if (baked == null) return;

            if (system.weatherProfilesList == null || index >= system.weatherProfilesList.Length) return;

            var wp = system.weatherProfilesList[index] as WeatherProfile;
            if (wp != null)
            {
                baked.CopyFromProfile(wp);
                EditorUtility.SetDirty(baked);
            }
        }

        private static void ClearContainer(WeatherTimeSystem system)
        {
            Transform container = system.transform.Find(CONTAINER_NAME);
            if (container != null)
            {
                Object.DestroyImmediate(container.gameObject);
            }
            system.bakedProfiles = null;
            EditorUtility.SetDirty(system);
        }

        /// <summary>
        /// Checks if a rebake is actually needed (count mismatch or missing data).
        /// </summary>
        public static bool NeedsRebake(WeatherTimeSystem system)
        {
            if (system == null) return false;

            if (system.weatherProfilesList == null) 
                return system.bakedProfiles != null && system.bakedProfiles.Length > 0;

            if (system.bakedProfiles == null) return true;
            if (system.bakedProfiles.Length != system.weatherProfilesList.Length) return true;

            for (int i = 0; i < system.bakedProfiles.Length; i++)
            {
                if (system.bakedProfiles[i] == null) return true;
            }

            return false;
        }
    }
}
#endif