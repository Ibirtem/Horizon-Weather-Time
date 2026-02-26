using UnityEditor;
using UnityEngine;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// A helper class to bridge Editor interactions with the runtime system.
    /// Forces the WeatherTimeSystem to update in real-time when profiles are modified,
    /// bypassing Unity's unreliable OnValidate/delayCall behavior for ScriptableObjects.
    /// </summary>
    public static class ProfileUpdateHelper
    {
        public static void ForceUpdate()
        {
            var sys = Object.FindAnyObjectByType<WeatherTimeSystem>();
            if (sys != null)
            {
                sys.Editor_HotReloadProfile(null);
            }
        }
    }

    [CustomEditor(typeof(WeatherProfile))]
    public class WeatherProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }

    [CustomEditor(typeof(CloudProfile))]
    public class CloudProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }

    [CustomEditor(typeof(LightingProfile))]
    public class LightingProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }

    [CustomEditor(typeof(SkyProfile))]
    public class SkyProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }

    [CustomEditor(typeof(FogProfile))]
    public class FogProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }

    [CustomEditor(typeof(MoonProfile))]
    public class MoonProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }

    [CustomEditor(typeof(EffectsProfile))]
    public class EffectsProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            base.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) ProfileUpdateHelper.ForceUpdate();
        }
    }
}