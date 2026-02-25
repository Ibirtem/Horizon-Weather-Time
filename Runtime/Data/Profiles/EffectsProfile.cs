using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Effects Profile", menuName = "Horizon/Profiles/Effects Profile")]
    public class EffectsProfile : ScriptableObject
    {
        [Header("Particle System")]
        [Tooltip("The particle system prefab to instantiate for this weather (e.g., rain, snow). Can be null.")]
        public GameObject weatherEffectPrefab;

        [Header("Spawning Rules")]
        [Tooltip("How high above the player the weather particles should start spawning from.")]
        [Range(5f, 50f)]
        public float spawnHeightOffset = 15f;

        [Tooltip("The maximum distance the raycast will check upwards for a ceiling. Should be greater than the spawn height.")]
        [Range(10f, 200f)]
        public float ceilingCheckDistance = 100f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (ceilingCheckDistance <= spawnHeightOffset)
            {
                Debug.LogWarning($"[EffectsProfile] Ceiling Check Distance ({ceilingCheckDistance}) must be greater than Spawn Height Offset ({spawnHeightOffset}) to function correctly.", this);
            }

            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}