using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Rendering;
#endif

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Owns the inverted proxy cube that applies the analytical atmospheric fog to
    /// opaque scene geometry, and guarantees that the camera depth texture exists.
    /// The mesh itself is only a full-screen trigger: all shading happens in
    /// Shaders/HorizonAtmosphericFog.shader through HorizonFogCommon.cginc.
    /// </summary>
    [ExecuteInEditMode]
#if UDONSHARP
    public class HorizonFogVolume : UdonSharpBehaviour
#else
    public class HorizonFogVolume : MonoBehaviour
#endif
    {
        [Header("Renderer Settings")]
        [Tooltip("Material using the Horizon/Atmospheric Fog shader.")]
        public Material fogMaterial;

        [Tooltip("Renderer of the proxy hull. Auto-created in the Unity Editor.")]
        public MeshRenderer volumeRenderer;

        public const string DEFAULT_FOG_MATERIAL_PATH = "Assets/Horizon Weather & Time/Materials/Horizon Atmospheric Fog.mat";

#if UDONSHARP
        [System.NonSerialized] private bool _depthTextureRequested = false;
#endif

        private void Awake()
        {
            EnsureDepthTexture();
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            EnsureVolumeRenderer();
#endif
        }

        private void OnEnable()
        {
            EnsureDepthTexture();
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            EnsureVolumeRenderer();
#endif
        }

        private void LateUpdate()
        {
            if (volumeRenderer == null) return;

            volumeRenderer.transform.position = GetViewPosition();

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            // The scene view camera is created on demand, so re-check it every editor frame.
            EnsureDepthTexture();
#endif
        }

        /// <summary>
        /// Enables or disables the fog hull. When fog is off the renderer is fully
        /// disabled, so the pass costs exactly 0.00 ms instead of shading and discarding.
        /// </summary>
        /// <param name="active">True to render the atmospheric fog pass.</param>
        public void SetActiveState(bool active)
        {
            if (active) EnsureDepthTexture();

            if (volumeRenderer == null) return;
            if (volumeRenderer.enabled != active) volumeRenderer.enabled = active;
        }

        /// <summary>
        /// Requests a depth texture from the local screen camera so the fog pass can
        /// reconstruct world positions. UdonSharp uses the SDK camera API; plain Unity
        /// falls back to Camera.main plus the active SceneView camera.
        /// </summary>
        private void EnsureDepthTexture()
        {
#if UDONSHARP
            if (_depthTextureRequested) return;
            if (VRCCameraSettings.ScreenCamera == null) return;

            _depthTextureRequested = true;
            DepthTextureMode current = VRCCameraSettings.ScreenCamera.DepthTextureMode;
            VRCCameraSettings.ScreenCamera.DepthTextureMode = current | DepthTextureMode.Depth;
#else
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.depthTextureMode |= DepthTextureMode.Depth;
            }

#if UNITY_EDITOR
            if (UnityEditor.SceneView.lastActiveSceneView != null &&
                UnityEditor.SceneView.lastActiveSceneView.camera != null)
            {
                UnityEditor.SceneView.lastActiveSceneView.camera.depthTextureMode |= DepthTextureMode.Depth;
            }
#endif
#endif
        }

        /// <summary>
        /// Resolves the current viewpoint position for UdonSharp runtime (VRChat) and Unity Editor.
        /// </summary>
        /// <returns>World-space position of the local player head or active editor camera.</returns>
        private Vector3 GetViewPosition()
        {
#if UDONSHARP
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (Utilities.IsValid(localPlayer))
            {
                return localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            }
            return transform.position;
#else
#if UNITY_EDITOR
            if (!Application.isPlaying &&
                UnityEditor.SceneView.lastActiveSceneView != null &&
                UnityEditor.SceneView.lastActiveSceneView.camera != null)
            {
                return UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
            }
#endif
            if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }
            return transform.position;
#endif
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        /// <summary>
        /// Loads (or auto-generates) the shared atmospheric fog material asset.
        /// </summary>
        private void EnsureVolumeMaterial()
        {
            if (fogMaterial != null) return;

            Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(DEFAULT_FOG_MATERIAL_PATH);
            if (mat == null)
            {
                string dir = System.IO.Path.GetDirectoryName(DEFAULT_FOG_MATERIAL_PATH);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

                Shader shader = Shader.Find("Horizon/Atmospheric Fog");
                if (shader != null)
                {
                    mat = new Material(shader);
                    mat.name = "Horizon Atmospheric Fog";
                    UnityEditor.AssetDatabase.CreateAsset(mat, DEFAULT_FOG_MATERIAL_PATH);
                    UnityEditor.AssetDatabase.SaveAssets();
                    Debug.Log($"<b><color=#33FF33>[LOG]</color></b> [HorizonFogVolume] Auto-generated material at {DEFAULT_FOG_MATERIAL_PATH}");
                }
                else
                {
                    Debug.LogError("<b><color=#FF3333>[ERROR]</color></b> [HorizonFogVolume] Shader 'Horizon/Atmospheric Fog' not found!");
                }
            }

            if (mat != null)
            {
                fogMaterial = mat;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }

        /// <summary>
        /// Validates the proxy hull and auto-generates it in the Unity Editor.
        /// Stripped to a no-op at VRChat runtime.
        /// </summary>
        public void EnsureVolumeRenderer()
        {
            EnsureVolumeMaterial();

            Transform hullTrans = transform.Find("FogVolume_Hull");
            if (hullTrans == null)
            {
                GameObject hullObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hullObj.name = "FogVolume_Hull";
                hullObj.transform.SetParent(transform);
                hullObj.transform.localPosition = Vector3.zero;
                hullObj.transform.localScale = new Vector3(10f, 10f, 10f);

                var collider = hullObj.GetComponent<Collider>();
                if (collider != null) DestroyImmediate(collider);

                hullObj.layer = 2; // Ignore Raycast / depth-only exclusion layers
                hullTrans = hullObj.transform;
            }
            else
            {
                hullTrans.localScale = new Vector3(10f, 10f, 10f);
            }

            MeshRenderer mr = hullTrans.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                mr.localBounds = new Bounds(Vector3.zero, Vector3.one * 200000f);

                if (fogMaterial != null && mr.sharedMaterial != fogMaterial)
                {
                    mr.sharedMaterial = fogMaterial;
                }

                volumeRenderer = mr;
            }
        }
#endif
    }
}
