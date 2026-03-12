#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEditor;

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