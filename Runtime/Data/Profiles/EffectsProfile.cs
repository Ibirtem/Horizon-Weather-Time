using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Effects Profile", menuName = "Horizon/Profiles/Effects Profile")]
    public class EffectsProfile : ScriptableObject
    {
        [Header("Particle System")]
        [Tooltip("The GPU Particle prefab to instantiate. Can be null.")]
        public GameObject weatherEffectPrefab;

        [Header("Volume & Layout")]
        [Tooltip("The physical boundaries of the particle system (XYZ).")]
        public Vector3 volumeBounds = new Vector3(40f, 40f, 40f);

        [Tooltip("The size of the individual particles.")]
        [Range(0.01f, 0.5f)]
        public float particleSize = 0.05f;

        [Tooltip("The density of the particles (0 to 1). Values below 1 will cull vertices to save performance.")]
        [Range(0.0f, 1.0f)]
        public float weatherDensity = 1.0f;

        [Header("Spawning Rules")]
        [Tooltip("How high above the player the weather volume center should be.")]
        [Range(0f, 50f)]
        public float spawnHeightOffset = 15f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}