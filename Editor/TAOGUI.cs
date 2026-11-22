using UnityEditor;
using UnityEngine;
using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>インスペクターで共通に使う描画の部品。</summary>
    internal static class TAOGUI
    {
        public const float AutoPopupWidth = 64f;

        // 本体の行の文字色と背景色
        public static readonly Color MainColor = new Color(0.55f, 0.85f, 1f);
        public static readonly Color MainTint = new Color(0.3f, 0.6f, 1f, 0.14f);

        static readonly Color AutoContentColor = new Color(0.55f, 0.85f, 1f);
        static readonly Color AutoBackgroundColor = new Color(0.5f, 0.75f, 1f);
        static readonly Color ManualContentColor = new Color(1f, 0.78f, 0.35f);
        static readonly Color ManualBackgroundColor = new Color(1f, 0.7f, 0.3f);

        public static void HorizontalLine()
        {
            EditorGUILayout.Space();
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorGUILayout.Space();
        }

        /// <summary>1 行を、入力欄と、右端の「自動 / 手動」ポップアップに分ける。</summary>
        public static void SplitFieldAndPopup(Rect row, out Rect fieldRect, out Rect popupRect)
        {
            popupRect = new Rect(row.xMax - AutoPopupWidth, row.y, AutoPopupWidth, row.height);
            fieldRect = new Rect(row.x, row.y, row.width - AutoPopupWidth - 2f, row.height);
        }

        /// <summary>
        /// 自動と手動を見分けやすいように、色を変えて表示する（自動=青、手動=橙）。
        /// 戻り値は選んだ項目（0=自動, 1=手動）。
        /// </summary>
        public static int AutoManualPopup(Rect rect, bool manual)
        {
            var indent = EditorGUI.indentLevel;
            var contentColor = GUI.contentColor;
            var backgroundColor = GUI.backgroundColor;
            EditorGUI.indentLevel = 0;
            GUI.contentColor = manual ? ManualContentColor : AutoContentColor;
            GUI.backgroundColor = manual ? ManualBackgroundColor : AutoBackgroundColor;

            var selected = EditorGUI.Popup(rect, manual ? 1 : 0, new[] { T("自動", "Auto"), T("手動", "Manual") });
            GUI.Label(rect, new GUIContent("", T("自動: 設定の取得元の値を使います\n手動: ここで指定した値を使います",
                "Auto: use the value of the settings source\nManual: use the value specified here")));

            GUI.contentColor = contentColor;
            GUI.backgroundColor = backgroundColor;
            EditorGUI.indentLevel = indent;
            return selected;
        }

        /// <summary>戻り値は、選択後に「自動」かどうか。</summary>
        public static bool AutoPopup(Rect rect, bool isAuto) => AutoManualPopup(rect, !isAuto) == 0;

        /// <summary>
        /// 選択肢の多いポップアップ。自動で決まった項目と、手動で指定した項目を見分けやすいように、
        /// 「自動 / 手動」のポップアップと同じ色で表示する。
        /// </summary>
        public static int ColoredPopup(Rect rect, int selected, string[] options, bool manual)
        {
            var indent = EditorGUI.indentLevel;
            var contentColor = GUI.contentColor;
            var backgroundColor = GUI.backgroundColor;
            EditorGUI.indentLevel = 0;
            GUI.contentColor = manual ? ManualContentColor : AutoContentColor;
            GUI.backgroundColor = manual ? ManualBackgroundColor : AutoBackgroundColor;

            selected = EditorGUI.Popup(rect, selected, options);

            GUI.contentColor = contentColor;
            GUI.backgroundColor = backgroundColor;
            EditorGUI.indentLevel = indent;
            return selected;
        }
    }
}
