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
    public class HorizonCloudVolume : UdonSharpBehaviour
#else
    public class HorizonCloudVolume : MonoBehaviour
#endif
    {
        [Header("Renderer Settings")]
        public Material volumeMaterial;
        public MeshRenderer volumeRenderer;

        public const string DEFAULT_CLOUD_MATERIAL_PATH = "Assets/Horizon Weather & Time/Materials/Horizon World Clouds.mat";

        [System.NonSerialized] private bool _isInitialized = false;

        // Shader Property IDs (Marked NonSerialized to prevent Unity zero-deserialization bug)
        [System.NonSerialized] private int _CloudAltitudeID, _CloudThicknessID, _CloudCoverageID, _CloudDensityID, _CloudScaleID;
        [System.NonSerialized] private int _CloudDetailID, _CloudWispID, _CloudScatterID, _ExposureID;
        [System.NonSerialized] private int _CloudColorID, _CloudShadowColorID, _SunPositionID, _MoonPositionID, _MoonColorID;
        [System.NonSerialized] private int _CloudNoise3DID, _WeatherMapTexID, _BlueNoiseTexID, _CurlNoiseTexID;
        [System.NonSerialized] private int _CloudWindID, _CloudTimeID;
        [System.NonSerialized] private int _TurbidityID;
        [System.NonSerialized] private int _RayleighID;
        [System.NonSerialized] private int _MieCoefficientID;
        [System.NonSerialized] private int _MieDirectionalGID;
        [System.NonSerialized] private int _TransmittanceLUTID;

        private void InitializeShaderIDs()
        {
#if UDONSHARP
            _CloudAltitudeID = VRCShader.PropertyToID("_CloudAltitude");
            _CloudThicknessID = VRCShader.PropertyToID("_CloudThickness");
            _CloudCoverageID = VRCShader.PropertyToID("_CloudCoverage");
            _CloudDensityID = VRCShader.PropertyToID("_CloudDensity");
            _CloudScaleID = VRCShader.PropertyToID("_CloudScale");
            _CloudDetailID = VRCShader.PropertyToID("_CloudDetail");
            _CloudWispID = VRCShader.PropertyToID("_CloudWisp");
            _CloudScatterID = VRCShader.PropertyToID("_CloudScatter");
            _ExposureID = VRCShader.PropertyToID("_Exposure");
            _CloudColorID = VRCShader.PropertyToID("_CloudColor");
            _CloudShadowColorID = VRCShader.PropertyToID("_CloudShadowColor");
            _SunPositionID = VRCShader.PropertyToID("_SunPosition");
            _MoonPositionID = VRCShader.PropertyToID("_MoonPosition");
            _MoonColorID = VRCShader.PropertyToID("_MoonColor");
            _CloudNoise3DID = VRCShader.PropertyToID("_CloudNoise3D");
            _WeatherMapTexID = VRCShader.PropertyToID("_WeatherMapTex");
            _BlueNoiseTexID = VRCShader.PropertyToID("_BlueNoiseTex");
            _CurlNoiseTexID = VRCShader.PropertyToID("_CurlNoiseTex");
            _CloudWindID = VRCShader.PropertyToID("_CloudWind");
            _CloudTimeID = VRCShader.PropertyToID("_CloudTime");
#else
            _CloudAltitudeID = Shader.PropertyToID("_CloudAltitude");
            _CloudThicknessID = Shader.PropertyToID("_CloudThickness");
            _CloudCoverageID = Shader.PropertyToID("_CloudCoverage");
            _CloudDensityID = Shader.PropertyToID("_CloudDensity");
            _CloudScaleID = Shader.PropertyToID("_CloudScale");
            _CloudDetailID = Shader.PropertyToID("_CloudDetail");
            _CloudWispID = Shader.PropertyToID("_CloudWisp");
            _CloudScatterID = Shader.PropertyToID("_CloudScatter");
            _ExposureID = Shader.PropertyToID("_Exposure");
            _CloudColorID = Shader.PropertyToID("_CloudColor");
            _CloudShadowColorID = Shader.PropertyToID("_CloudShadowColor");
            _SunPositionID = Shader.PropertyToID("_SunPosition");
            _MoonPositionID = Shader.PropertyToID("_MoonPosition");
            _MoonColorID = Shader.PropertyToID("_MoonColor");
            _CloudNoise3DID = Shader.PropertyToID("_CloudNoise3D");
            _WeatherMapTexID = Shader.PropertyToID("_WeatherMapTex");
            _BlueNoiseTexID = Shader.PropertyToID("_BlueNoiseTex");
            _CurlNoiseTexID = Shader.PropertyToID("_CurlNoiseTex");
            _CloudWindID = Shader.PropertyToID("_CloudWind");
            _CloudTimeID = Shader.PropertyToID("_CloudTime");

#endif

#if UDONSHARP
_TurbidityID = VRCShader.PropertyToID("_Turbidity");
_RayleighID = VRCShader.PropertyToID("_Rayleigh");
_MieCoefficientID = VRCShader.PropertyToID("_MieCoefficient");
_MieDirectionalGID = VRCShader.PropertyToID("_MieDirectionalG");
_TransmittanceLUTID = VRCShader.PropertyToID("_TransmittanceLUT");
#else
_TurbidityID = Shader.PropertyToID("_Turbidity");
_RayleighID = Shader.PropertyToID("_Rayleigh");
_MieCoefficientID = Shader.PropertyToID("_MieCoefficient");
_MieDirectionalGID = Shader.PropertyToID("_MieDirectionalG");
_TransmittanceLUTID = Shader.PropertyToID("_TransmittanceLUT");
#endif
            _isInitialized = true;
        }

private void Awake()
        {
            InitializeShaderIDs();
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            EnsureVolumeRenderer();
#endif
        }

        private void OnEnable()
        {
            InitializeShaderIDs();
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            EnsureVolumeRenderer();
#endif
        }

        private void LateUpdate()
        {
            Vector3 camPos = GetCameraPosition();
            if (volumeRenderer != null)
            {
                volumeRenderer.transform.position = camPos;
            }
        }

        /**
         * Resolves current viewpoint position for UdonSharp runtime (VRChat) and Unity Editor.
         *
         * @return World-space position of the local player head or active editor camera.
         */
        private Vector3 GetCameraPosition()
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
            if (Camera.main != null)
            {
                Camera.main.depthTextureMode |= DepthTextureMode.Depth;
                return Camera.main.transform.position;
            }
            if (UnityEditor.SceneView.lastActiveSceneView != null && UnityEditor.SceneView.lastActiveSceneView.camera != null)
            {
                return UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
            }
#endif
            return transform.position;
#endif
        }

        /**
         * Dispatches complete volumetric cloud parameters, textures, and lighting vectors to the active material.
         *
         * @param cloudTex 3D volumetric noise texture.
         * @param weatherMapTex 2D coverage and genus map.
         * @param blueNoiseTex Blue noise texture for dithering.
         * @param curlNoiseTex 2D curl noise vector field for atmospheric turbulence.
         * @param altitude Base altitude in kilometers.
         * @param scale Horizontal noise scale.
         * @param coverage Global coverage modifier [0.0, 1.0].
         * @param density Cloud mass optical density multiplier.
         * @param detail Erosion carving amount [0.0, 1.0].
         * @param wisp Fibrous edge wispiness amount [0.0, 1.0].
         * @param windOffset Pre-integrated horizontal wind offset vector.
         * @param cloudTime Pre-integrated cyclical UTC time value.
         * @param baseColor Direct diffuse sunlit color.
         * @param shadowColor Ambient multiple-scattering shadow color.
         * @param scatter Light absorption and forward scattering coefficient.
         * @param sunDir Direction vector pointing to the sun.
         * @param moonDir Direction vector pointing to the moon.
         * @param moonColor Diffuse visual color of the moon.
         */
        public void UpdateClouds(
            Texture3D cloudTex, Texture weatherMapTex, Texture blueNoiseTex, Texture2D curlNoiseTex,
            float altitude, float scale, float coverage, float density, float detail, float wisp,
            Vector2 windOffset, float cloudTime,
            Color baseColor, Color shadowColor, float scatter,
            Vector3 sunDir, Vector3 moonDir, Color moonColor,
            float rayleigh, float turbidity, float mieCoeff, float mieG, Texture lut) // Новые параметры
        {
            if (!_isInitialized) InitializeShaderIDs();
            if (volumeRenderer == null) return;
            Material mat = volumeRenderer.sharedMaterial;
            if (mat == null) return;

            mat.SetFloat(_CloudAltitudeID, altitude);
            mat.SetFloat(_CloudCoverageID, coverage);
            mat.SetFloat(_CloudDensityID, density);
            mat.SetFloat(_CloudScaleID, scale);
            mat.SetFloat(_CloudDetailID, detail);
            mat.SetFloat(_CloudWispID, wisp);
            mat.SetFloat(_CloudScatterID, scatter);

            mat.SetVector(_CloudWindID, windOffset);
            mat.SetFloat(_CloudTimeID, cloudTime);

            mat.SetColor(_CloudColorID, baseColor);
            mat.SetColor(_CloudShadowColorID, shadowColor);

            mat.SetVector(_SunPositionID, sunDir);
            mat.SetVector(_MoonPositionID, moonDir);
            mat.SetColor(_MoonColorID, moonColor);

            mat.SetFloat(_RayleighID, rayleigh);
            mat.SetFloat(_TurbidityID, turbidity);
            mat.SetFloat(_MieCoefficientID, mieCoeff);
            mat.SetFloat(_MieDirectionalGID, mieG);
            if (lut != null) mat.SetTexture(_TransmittanceLUTID, lut);

            if (cloudTex != null) mat.SetTexture(_CloudNoise3DID, cloudTex);
            if (weatherMapTex != null) mat.SetTexture(_WeatherMapTexID, weatherMapTex);
            if (blueNoiseTex != null) mat.SetTexture(_BlueNoiseTexID, blueNoiseTex);
            if (curlNoiseTex != null) mat.SetTexture(_CurlNoiseTexID, curlNoiseTex);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void EnsureVolumeMaterial()
        {
            if (volumeMaterial != null) return;

            Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(DEFAULT_CLOUD_MATERIAL_PATH);
            if (mat == null)
            {
                string dir = System.IO.Path.GetDirectoryName(DEFAULT_CLOUD_MATERIAL_PATH);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

                Shader shader = Shader.Find("Horizon/World Clouds");
                if (shader != null)
                {
                    mat = new Material(shader);
                    mat.name = "Horizon World Clouds";
                    UnityEditor.AssetDatabase.CreateAsset(mat, DEFAULT_CLOUD_MATERIAL_PATH);
                    UnityEditor.AssetDatabase.SaveAssets();
                    Debug.Log($"<b><color=#33FF33>[LOG]</color></b> [HorizonCloudVolume] Auto-generated material at {DEFAULT_CLOUD_MATERIAL_PATH}");
                }
            }

            if (mat != null)
            {
                volumeMaterial = mat;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }

        /**
         * Validates proxy geometry and auto-generates volume hull in Unity Editor.
         * Stripped to no-op at VRChat runtime.
         */
        public void EnsureVolumeRenderer()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            EnsureVolumeMaterial();

            Transform hullTrans = transform.Find("CloudVolume_Hull");
            if (hullTrans == null)
            {
                GameObject hullObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hullObj.name = "CloudVolume_Hull";
                hullObj.transform.SetParent(transform);
                hullObj.transform.localPosition = Vector3.zero;
                hullObj.transform.localScale = new Vector3(10f, 10f, 10f);

                var collider = hullObj.GetComponent<Collider>();
                if (collider != null) DestroyImmediate(collider);

                hullObj.layer = 2; // Ignore Raycast
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

                if (volumeMaterial != null && mr.sharedMaterial != volumeMaterial)
                {
                    mr.sharedMaterial = volumeMaterial;
                }

                volumeRenderer = mr;
            }
#endif
        }
#endif
    }
}