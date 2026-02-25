using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Moon Profile", menuName = "Horizon/Profiles/Moon Profile")]
    public class MoonProfile : ScriptableObject
    {
        [Tooltip("Texture for the moon disk (Equirectangular 2:1 projection supported).")]
        public Texture2D moonTexture;

        [Tooltip("Size of the moon in the sky.")]
        [Range(0.005f, 0.1f)]
        public float moonSize = 0.02f;

        [Tooltip("Visual tint of the moon disk.")]
        public Color moonColor = Color.white;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}