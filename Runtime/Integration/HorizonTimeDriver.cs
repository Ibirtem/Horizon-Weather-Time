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
        [UdonSynced(UdonSyncMode.None)] private int _syncedProfileIndex;

        private bool _isLocalOverride = false;
        private VRCPlayerApi _localPlayer;

        private void Start()
        {
            if (targetSystem == null) targetSystem = GetComponent<WeatherTimeSystem>();
            _localPlayer = Networking.LocalPlayer;

            if (targetSystem != null)
            {
                ApplyWeather(_syncedProfileIndex);
            }
        }

        public override void OnDeserialization()
        {
            if (!_isLocalOverride) ApplyWeather(_syncedProfileIndex);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject)) RequestSerialization();
        }

        public void SetGlobalWeather(int profileIndex)
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            _syncedProfileIndex = profileIndex;
            RequestSerialization();

            _isLocalOverride = false; 
            ApplyWeather(profileIndex);
        }

        public void SetLocalWeather(int profileIndex)
        {
            _isLocalOverride = true;
            ApplyWeather(profileIndex);
        }

        public void RevertToGlobalSync()
        {
            _isLocalOverride = false;
            ApplyWeather(_syncedProfileIndex);
        }

        private void ApplyWeather(int index)
        {
            if (targetSystem != null) targetSystem.SetWeatherProfile(index);
        }
    }
}
#else
namespace BlackHorizon.HorizonWeatherTime { public class HorizonTimeDriver : MonoBehaviour { } }
#endif