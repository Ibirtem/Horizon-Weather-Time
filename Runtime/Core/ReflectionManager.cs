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
    public class ReflectionManager : UdonSharpBehaviour
#else
    public class ReflectionManager : MonoBehaviour
#endif
    {
        [Header("Configuration")]
        [Tooltip("How often (in seconds) the reflection probe should refresh.")]
        [Range(0.1f, 10.0f)]
        public float updateInterval = 2.0f;

        [Tooltip("Reference to the Realtime Reflection Probe.")]
        public ReflectionProbe mainReflectionProbe;

        private float _timer;

        private void OnEnable()
        {
            if (mainReflectionProbe == null)
            {
                mainReflectionProbe = GetComponentInChildren<ReflectionProbe>();
            }
        }

        public void ManualUpdate()
        {
            if (mainReflectionProbe != null)
            {
                mainReflectionProbe.RenderProbe();
            }
        }

        private void Update()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying)
            {
                 return;
            }
#endif

            _timer += Time.deltaTime;

            if (_timer >= updateInterval)
            {
                _timer = 0f;
                ManualUpdate();
            }
        }

        public void EnsureProbeExists()
        {
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (mainReflectionProbe == null)
            {
                mainReflectionProbe = GetComponentInChildren<ReflectionProbe>();
                
                if (mainReflectionProbe == null)
                {
                    GameObject probeObj = new GameObject("Global Reflection Probe");
                    probeObj.transform.SetParent(transform);
                    probeObj.transform.localPosition = Vector3.zero;
                    mainReflectionProbe = probeObj.AddComponent<ReflectionProbe>();
                    
                    mainReflectionProbe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
                    mainReflectionProbe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
                    mainReflectionProbe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
                    
                    mainReflectionProbe.importance = 1;
                    mainReflectionProbe.boxProjection = false;
                    mainReflectionProbe.size = new Vector3(5000, 5000, 5000);
                    mainReflectionProbe.resolution = 128; 
                    
                    mainReflectionProbe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.Skybox;

                    mainReflectionProbe.cullingMask = 0; 
                }
            }
#endif
        }
    }
}