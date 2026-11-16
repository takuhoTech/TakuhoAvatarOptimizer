using UnityEditor;

namespace TakuhoTech.TAO.Editor
{
    internal static class TAOLocalization
    {
        const string PrefKey = "TAO.EditorLanguage";

        public static readonly string[] Languages = { "日本語 (日本)", "English" };

        public static int Language
        {
            get => EditorPrefs.GetInt(PrefKey, 0);
            set => EditorPrefs.SetInt(PrefKey, value);
        }

        // 日本語, English
        public static string T(string ja, string en) => Language == 0 ? ja : en;

        public static void DrawLanguagePopup()
            => Language = EditorGUILayout.Popup("Editor Language", Language, Languages);
    }
}
