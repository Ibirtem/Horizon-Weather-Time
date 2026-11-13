using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    [CreateAssetMenu(fileName = "New Audio Profile", menuName = "Horizon/Profiles/Audio Profile")]
    public class AudioProfile : ScriptableObject
    {
        [Header("Master")]
        public bool enabled = true;

        [Range(0f, 2f)]
        public float masterVolume = 1.0f;

        [Header("Audio Clips (Auto-Generated)")]
        [Tooltip("Seamless stereo loop for open outdoor rain.")]
        public AudioClip rainExteriorStereoClip;

        [Tooltip("Seamless mono loop for directional outdoor rain through openings/windows.")]
        public AudioClip rainExteriorMonoClip;

        [Tooltip("Seamless stereo loop for wall-muffled indoor rain.")]
        public AudioClip rainInteriorStereoClip;

        [Tooltip("Seamless mono loop for rain pattering directly on a roof overhead.")]
        public AudioClip rainRoofMonoClip;

        [Tooltip("Seamless stereo loop for steady ambient wind.")]
        public AudioClip windBaseStereoClip;

        [Tooltip("Seamless stereo loop for wind gusts and storm howl.")]
        public AudioClip windGustStereoClip;

        [Header("Rain Settings")]
        [Range(0f, 1f)]
        public float rainVolume = 0.0f;

        [Tooltip("Muffled rain loudness inside rooms that share an exterior wall with the outdoors.")]
        [Range(0f, 1f)]
        public float rainInteriorScale = 0.45f;

        [Tooltip("Loudness of overhead roof pattering when directly under a single-story roof.")]
        [Range(0f, 1f)]
        public float rainRoofScale = 0.65f;

        [Range(0.5f, 1.5f)]
        public float rainPitch = 1.0f;

        [Header("Wind Settings")]
        [Range(0f, 1f)]
        public float windVolume = 0.2f;

        [Range(0f, 1f)]
        public float windGustStrength = 0.35f;

        [Range(0.05f, 1f)]
        public float windGustSpeed = 0.25f;

        [Tooltip("Muffled wind loudness when indoors.")]
        [Range(0f, 1f)]
        public float windInteriorScale = 0.25f;

        [Range(0.5f, 1.5f)]
        public float windPitch = 1.0f;

        [Header("Acoustics")]
        [Tooltip("Residual muffled loudness deep inside multi-room basements or bunkers.")]
        [Range(0f, 1f)]
        public float minBunkerScale = 0.05f;

        [Tooltip("Smoothing speed when transitioning between outdoor and indoor acoustics.")]
        [Range(0.5f, 15f)]
        public float acousticSmoothing = 3.5f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= WeatherProfile.InvokeGlobalUpdate;
            UnityEditor.EditorApplication.delayCall += WeatherProfile.InvokeGlobalUpdate;
        }
#endif
    }
}