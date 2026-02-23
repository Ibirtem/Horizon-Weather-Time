using UnityEngine;
using UnityEditor;
using System.IO;

namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Shared styling and drawing logic for all Horizon modules.
    /// </summary>
    public static class HorizonEditorUtils
    {
        private static GUIStyle _headerTitleStyle;
        private static GUIStyle _headerVersionStyle;
        private static GUIStyle _headerSubtitleStyle;
        private static GUIStyle _sectionHeaderStyle;

        private static string _cachedVersion = null;

        // --- PUBLIC API ---

        public static void DrawHorizonHeader(string subtitle, Object scriptReferenceForPath)
        {
            InitStyles();

            // 1. Calculate Rects
            Rect boxRect = EditorGUILayout.GetControlRect(false, 60);

            // 2. Draw Background
            EditorGUI.DrawRect(boxRect, new Color(0.12f, 0.12f, 0.14f));

            // 3. Accent Line (Blue)
            Rect accentRect = new Rect(boxRect.x, boxRect.yMax - 2, boxRect.width, 2);
            EditorGUI.DrawRect(accentRect, new Color(0.2f, 0.6f, 1.0f));

            // 4. Text Content
            Rect contentRect = new Rect(boxRect.x + 12, boxRect.y + 8, boxRect.width - 24, boxRect.height - 10);

            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 24), "HORIZON", _headerTitleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24, contentRect.width, 20), subtitle.ToUpper(), _headerSubtitleStyle);

            // 5. Version
            if (_cachedVersion == null) _cachedVersion = GetVersion(scriptReferenceForPath);
            GUI.Label(contentRect, $"v{_cachedVersion}", _headerVersionStyle);

            EditorGUILayout.Space(4);
        }

        public static void DrawSectionHeader(string title)
        {
            InitStyles();
            EditorGUILayout.Space(12);

            Rect r = EditorGUILayout.GetControlRect(false, 24);

            GUI.Label(r, title, _sectionHeaderStyle);

            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 5, r.width, 1), new Color(1, 1, 1, 0.1f));

            EditorGUILayout.Space(4);
        }

        // --- INTERNAL LOGIC ---

        private static void InitStyles()
        {
            if (_headerTitleStyle != null) return;

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.9f, 0.9f, 0.95f) }
            };

            _headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
            };

            _headerVersionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 10,
                normal = { textColor = new Color(0.4f, 0.4f, 0.45f) }
            };

            _sectionHeaderStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
        }

        private static string GetVersion(Object scriptReference)
        {
            string path = null;

            if (scriptReference is ScriptableObject scriptableObj)
            {
                var monoScript = MonoScript.FromScriptableObject(scriptableObj);
                path = AssetDatabase.GetAssetPath(monoScript);
            }

            if (string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.GetAssetPath(scriptReference);
            }

            if (string.IsNullOrEmpty(path)) return "Dev (No Path)";

            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                if (packageInfo != null)
                {
                    return packageInfo.version;
                }
            }
            catch { }

            try
            {
                string directory = Path.GetDirectoryName(path);

                for (int i = 0; i < 4; i++)
                {
                    if (string.IsNullOrEmpty(directory)) break;

                    string jsonPath = Path.Combine(directory, "package.json");
                    if (File.Exists(jsonPath))
                    {
                        string json = File.ReadAllText(jsonPath);
                        const string token = "\"version\":";
                        if (json.Contains(token))
                        {
                            int index = json.IndexOf(token);
                            int startQuote = json.IndexOf("\"", index + token.Length);
                            if (startQuote != -1)
                            {
                                int endQuote = json.IndexOf("\"", startQuote + 1);
                                if (endQuote != -1)
                                {
                                    return json.Substring(startQuote + 1, endQuote - startQuote - 1);
                                }
                            }
                        }
                    }
                    directory = Path.GetDirectoryName(directory);
                }
            }
            catch { }

            return "Dev (Local)";
        }
    }
}