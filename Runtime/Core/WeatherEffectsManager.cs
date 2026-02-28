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
        [Header("Static Scene References (Auto-Assigned)")]
        [Tooltip("The MeshRenderer that plays the weather effect (Snow/Rain).")]
        public MeshRenderer particleRenderer;

        [Tooltip("The Top-Down Orthographic Camera for roof occlusion.")]
        public Camera occlusionCamera;

        [Tooltip("The depth-only replacement shader for occlusion camera.")]
        public Shader depthReplacementShader;

        private MaterialPropertyBlock _sharedPropertyBlock;
        private int SystemLightingColorID;

        // Shader Property IDs
        private int BoundsID, ParticleSizeID, DensityID;
        private int OccTexID, OccCamPosID, OccOrthoSizeID, OccFarClipID;

        private Mesh _instanceMesh;

        private void Start()
        {
            SetupShaderIDs();
            SetupCameraShader();
        }

        private void Awake()
        {
            _sharedPropertyBlock = new MaterialPropertyBlock();
            SetupShaderIDs();
        }

        private void OnEnable()
        {
            SetupCameraShader();
        }

        private void OnDisable()
        {
            if (_instanceMesh != null)
            {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                DestroyImmediate(_instanceMesh);
#else
                Destroy(_instanceMesh);
#endif
                _instanceMesh = null;
            }
        }

        private void SetupShaderIDs()
        {
#if UDONSHARP
            SystemLightingColorID = VRCShader.PropertyToID("_SystemLightingColor");
            BoundsID = VRCShader.PropertyToID("_HorizonBounds");
            ParticleSizeID = VRCShader.PropertyToID("_HorizonParticleSize");
            DensityID = VRCShader.PropertyToID("_HorizonDensity");
            OccTexID = VRCShader.PropertyToID("_WeatherOcclusionTex");
            OccCamPosID = VRCShader.PropertyToID("_OcclusionCamPos");
            OccOrthoSizeID = VRCShader.PropertyToID("_OcclusionOrthoSize");
            OccFarClipID = VRCShader.PropertyToID("_OcclusionFarClip");
#else
            SystemLightingColorID = Shader.PropertyToID("_SystemLightingColor");
            BoundsID = Shader.PropertyToID("_HorizonBounds");
            ParticleSizeID = Shader.PropertyToID("_HorizonParticleSize");
            DensityID = Shader.PropertyToID("_HorizonDensity");
            OccTexID = Shader.PropertyToID("_WeatherOcclusionTex");
            OccCamPosID = Shader.PropertyToID("_OcclusionCamPos");
            OccOrthoSizeID = Shader.PropertyToID("_OcclusionOrthoSize");
            OccFarClipID = Shader.PropertyToID("_OcclusionFarClip");
#endif
        }

        private void SetupCameraShader()
        {
            if (occlusionCamera == null) return;

            if (depthReplacementShader != null)
            {
                occlusionCamera.backgroundColor = Color.white;
                occlusionCamera.clearFlags = CameraClearFlags.SolidColor;

                occlusionCamera.SetReplacementShader(depthReplacementShader, "RenderType");
            }
            else
            {
                Debug.LogWarning("[WeatherEffectsManager] Depth Replacement Shader is missing! Occlusion won't work.");
            }
        }

        private void LateUpdate()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Camera cam = Camera.main; 
                var sceneCam = UnityEditor.SceneView.lastActiveSceneView?.camera;
                Vector3 target = sceneCam != null ? sceneCam.transform.position : (cam != null ? cam.transform.position : Vector3.zero);
                UpdatePosition(target);
            }
#endif
        }

        private float _currentHeightOffset = 15f;

        public void SetHeightOffset(float offset) { _currentHeightOffset = offset; }

        private int _frameCounter = 0;
        public void UpdatePosition(Vector3 targetPos)
        {
            if (occlusionCamera != null)
            {
                occlusionCamera.transform.position = new Vector3(targetPos.x, targetPos.y + 60f, targetPos.z);
                occlusionCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                if (_frameCounter % 100 == 0)
                {
                    SetupCameraShader();
                }

                _frameCounter++;

                if (_frameCounter % 3 == 0)
                {
                    occlusionCamera.enabled = true;
                }
                else
                {
                    occlusionCamera.enabled = false;
                }
            }
        }

        public void UpdateWeatherEffects(GameObject effectPrefabSource, float heightOffset)
        {
            if (particleRenderer == null) return;

            if (effectPrefabSource == null)
            {
                particleRenderer.enabled = false;
                return;
            }

            Renderer sourceRenderer = effectPrefabSource.GetComponentInChildren<Renderer>();
            if (sourceRenderer != null)
            {
                particleRenderer.enabled = true;
                particleRenderer.sharedMaterial = sourceRenderer.sharedMaterial;

                MeshFilter sourceMf = effectPrefabSource.GetComponentInChildren<MeshFilter>();
                MeshFilter targetMf = particleRenderer.GetComponent<MeshFilter>();

                if (sourceMf != null && targetMf != null)
                {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
                    string targetInstanceName = sourceMf.sharedMesh.name + "_Instance";
                    if (_instanceMesh == null || _instanceMesh.name != targetInstanceName) 
                    {
                        if (_instanceMesh != null) DestroyImmediate(_instanceMesh);
                        
                        _instanceMesh = Instantiate(sourceMf.sharedMesh);
                        _instanceMesh.name = targetInstanceName;
                        targetMf.mesh = _instanceMesh;
                    }
#else
                    targetMf.sharedMesh = sourceMf.sharedMesh;
#endif
                }

                if (particleRenderer.gameObject.layer != 2) particleRenderer.gameObject.layer = 2;
            }
            else
            {
                particleRenderer.enabled = false;
            }
        }

        public void UpdateEffectsLighting(Color globalLightColor, Vector3 bounds, float particleSize, float density)
        {
            if (_sharedPropertyBlock == null) _sharedPropertyBlock = new MaterialPropertyBlock();
            if (particleRenderer == null || !particleRenderer.enabled) return;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (_instanceMesh != null)
            {
                _instanceMesh.bounds = new Bounds(Vector3.zero, bounds + Vector3.one * 5f);
            }
#endif

            _sharedPropertyBlock.SetColor(SystemLightingColorID, globalLightColor);
            _sharedPropertyBlock.SetVector(BoundsID, bounds);
            _sharedPropertyBlock.SetFloat(ParticleSizeID, particleSize);
            _sharedPropertyBlock.SetFloat(DensityID, density);

            if (occlusionCamera != null && occlusionCamera.targetTexture != null)
            {
                float requiredSize = Mathf.Max(bounds.x, bounds.z) * 0.6f;
                occlusionCamera.orthographicSize = requiredSize;

                _sharedPropertyBlock.SetTexture(OccTexID, occlusionCamera.targetTexture);
                _sharedPropertyBlock.SetVector(OccCamPosID, occlusionCamera.transform.position);
                _sharedPropertyBlock.SetFloat(OccOrthoSizeID, occlusionCamera.orthographicSize);
                _sharedPropertyBlock.SetFloat(OccFarClipID, occlusionCamera.farClipPlane);
            }

            particleRenderer.SetPropertyBlock(_sharedPropertyBlock);
        }
    }
}