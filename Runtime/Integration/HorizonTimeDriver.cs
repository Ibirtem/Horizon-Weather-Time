using UnityEngine;
using BlackHorizon.HorizonWeatherTime;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;

namespace BlackHorizon.HorizonWeatherTime
{
    [AddComponentMenu("Horizon/Horizon Time Driver (VRChat)")]
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class HorizonTimeDriver : UdonSharpBehaviour
    {
        [Header("References")]
        [Tooltip("Reference to the main Weather System.")]
        public WeatherTimeSystem targetSystem;

        [Header("Sync Settings")]
        [UdonSynced(UdonSyncMode.None)] private int _syncLighting;
        [UdonSynced(UdonSyncMode.None)] private int _syncSky;
        [UdonSynced(UdonSyncMode.None)] private int _syncCloud;
        [UdonSynced(UdonSyncMode.None)] private int _syncMoon;
        [UdonSynced(UdonSyncMode.None)] private int _syncFog;
        [UdonSynced(UdonSyncMode.None)] private int _syncEffects;

        private bool _isLocalOverride = false;
        private VRCPlayerApi _localPlayer;

        private void Start()
        {
            if (targetSystem == null) targetSystem = GetComponent<WeatherTimeSystem>();
            _localPlayer = Networking.LocalPlayer;

            if (targetSystem != null)
            {
                if (Networking.IsOwner(gameObject))
                {
                    _syncLighting = targetSystem.LightingIndex;
                    _syncSky = targetSystem.SkyIndex;
                    _syncCloud = targetSystem.CloudIndex;
                    _syncMoon = targetSystem.MoonIndex;
                    _syncFog = targetSystem.FogIndex;
                    _syncEffects = targetSystem.EffectsIndex;
                    RequestSerialization();
                    
                    ApplyWeather(_syncLighting, _syncSky, _syncCloud, _syncMoon, _syncFog, _syncEffects);
                }
            }
        }

        public override void OnDeserialization()
        {
            if (!_isLocalOverride) ApplyWeather(_syncLighting, _syncSky, _syncCloud, _syncMoon, _syncFog, _syncEffects);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject)) RequestSerialization();
        }

        public void SetGlobalWeather(int lighting, int sky, int cloud, int moon, int fog, int effects)
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            
            _syncLighting = lighting;
            _syncSky = sky;
            _syncCloud = cloud;
            _syncMoon = moon;
            _syncFog = fog;
            _syncEffects = effects;
            RequestSerialization();

            _isLocalOverride = false; 
            ApplyWeather(lighting, sky, cloud, moon, fog, effects);
        }

        public void SetLocalWeather(int lighting, int sky, int cloud, int moon, int fog, int effects)
        {
            _isLocalOverride = true;
            ApplyWeather(lighting, sky, cloud, moon, fog, effects);
        }

        public void RevertToGlobalSync()
        {
            _isLocalOverride = false;
            ApplyWeather(_syncLighting, _syncSky, _syncCloud, _syncMoon, _syncFog, _syncEffects);
        }

        private void ApplyWeather(int l, int s, int c, int m, int f, int e)
        {
            if (targetSystem != null) targetSystem.SetModuleStates(l, s, c, m, f, e);
        }
    }
}
#else
namespace BlackHorizon.HorizonWeatherTime { public class HorizonTimeDriver : MonoBehaviour { } }
#endif