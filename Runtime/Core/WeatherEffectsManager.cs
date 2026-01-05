using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    [ExecuteInEditMode]
#if UDONSHARP
    public class WeatherEffectsManager : UdonSharpBehaviour
#else
    public class WeatherEffectsManager : MonoBehaviour
#endif
    {
        private GameObject _currentEffectInstance;
        private Camera _mainCamera;
        private MaterialPropertyBlock _sharedPropertyBlock;
        private int SystemLightingColorID;

        private void Start()
        {
#if UDONSHARP
        SystemLightingColorID = VRCShader.PropertyToID("_SystemLightingColor");
#else
            SystemLightingColorID = Shader.PropertyToID("_SystemLightingColor");
#endif

            if (_sharedPropertyBlock == null) _sharedPropertyBlock = new MaterialPropertyBlock();
        }

        private void Awake()
        {
            _sharedPropertyBlock = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (_mainCamera == null) _mainCamera = Camera.main; 
                var sceneCam = UnityEditor.SceneView.lastActiveSceneView?.camera;
                Vector3 target = sceneCam != null ? sceneCam.transform.position : (_mainCamera != null ? _mainCamera.transform.position : Vector3.zero);
                UpdatePosition(target);
            }
#endif
        }

        private float _currentHeightOffset = 15f;

        public void SetHeightOffset(float offset) { _currentHeightOffset = offset; }

        public void UpdatePosition(Vector3 targetPos)
        {
            if (_currentEffectInstance != null)
            {
                _currentEffectInstance.transform.position = targetPos + Vector3.up * _currentHeightOffset;
                _currentEffectInstance.transform.rotation = Quaternion.identity;
            }
        }

        public void UpdateWeatherEffects(GameObject effectPrefab, float heightOffset)
        {
            GameObject desiredPrefab = effectPrefab;

            if (_currentEffectInstance != null)
            {
                if (desiredPrefab != null && _currentEffectInstance.name.StartsWith(desiredPrefab.name))
                {
                    if (heightOffset > 0) _currentEffectInstance.transform.localPosition = Vector3.up * heightOffset;
                    return;
                }
            }

            CleanupAllChildEffects();

            if (desiredPrefab != null)
            {
                var newInstance = Instantiate(desiredPrefab, Vector3.zero, Quaternion.identity, transform);
                newInstance.name = $"{desiredPrefab.name} (Instance)";
                _currentEffectInstance = newInstance;

                if (heightOffset > 0)
                {
                    newInstance.transform.localPosition = Vector3.up * heightOffset;
                }

                var mainParticleSystem = newInstance.GetComponentInChildren<ParticleSystem>();

#if !COMPILER_UDONSHARP && UNITY_EDITOR
                if (mainParticleSystem != null && mainParticleSystem.gameObject.name == "FallingSnow")
                {
                    var lppv = mainParticleSystem.GetComponent<LightProbeProxyVolume>();

                    if (lppv == null)
                    {
                        mainParticleSystem.gameObject.AddComponent<LightProbeProxyVolume>();
                    }
                }
#endif
            }
            else
            {
                _currentEffectInstance = null;
            }
        }

        public void UpdateEffectsLighting(Color globalLightColor)
        {
            if (_sharedPropertyBlock == null) _sharedPropertyBlock = new MaterialPropertyBlock();

            if (_currentEffectInstance == null) return;

            var renderers = _currentEffectInstance.GetComponentsInChildren<ParticleSystemRenderer>();

            if (renderers.Length > 0)
            {
                _sharedPropertyBlock.SetColor(SystemLightingColorID, globalLightColor);

                foreach (var psRenderer in renderers)
                {
                    psRenderer.SetPropertyBlock(_sharedPropertyBlock);
                }
            }
        }

        private void CleanupAllChildEffects()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                CleanupEffect(child);
            }
        }

        private void CleanupEffect(GameObject effectInstance)
        {
            if (effectInstance == null) return;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(effectInstance);
                return;
            }
#endif
            Destroy(effectInstance);
        }

    }
}