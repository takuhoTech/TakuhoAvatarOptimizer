using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// AAO Merge PhysBone と同様に、項目ごとに「自動 / 手動」を切り替えられる設定欄を描画する。
    /// 自動のときは取得元（PhysBone、またはまとめた設定）の値を読み取り専用で見せ、手動のときは settings の値を編集する。
    /// </summary>
    internal sealed class TAOPhysBoneSettingsGUI
    {
        const float CurveButtonWidth = 20f;
        const float HelpButtonWidth = 30f;

        readonly Dictionary<string, bool> _sectionFolds = new Dictionary<string, bool>();
        SerializedObject _sourceObject;
        UnityEngine.Object _sourcePhysBone;
        string _sourcePrefix = "";

        /// <summary>自動の値の取得元を、PhysBone にする。</summary>
        public void SetSource(VRCPhysBoneBase physBone)
        {
            _sourcePrefix = "";
            if (physBone != _sourcePhysBone || _sourceObject != null && _sourceObject.targetObject is TAOCombinedSettingsHolder)
            {
                _sourcePhysBone = physBone;
                _sourceObject = physBone != null ? new SerializedObject(physBone) : null;
            }
            else
            {
                _sourceObject?.Update();
            }
        }

        /// <summary>自動の値の取得元を、まとめた設定にする。prefix は設定までのプロパティパス。</summary>
        public void SetSource(SerializedObject combinedSettings, string prefix)
        {
            _sourcePhysBone = null;
            _sourceObject = combinedSettings;
            _sourcePrefix = prefix;
        }

        /// <param name="settings">手動の値を持つ TAOPhysBoneSettings のプロパティ。</param>
        /// <param name="transformExtras">Transform セクションの先頭に追加で描画する項目。</param>
        public void Draw(SerializedProperty settings, Action transformExtras = null)
        {
            var manualProperties = settings.FindPropertyRelative("manualProperties");

            // セクションに属さない項目（Version）は、セクションの外の先頭に表示する
            foreach (var property in TAOPhysBoneProperties.InSection(TAOPhysBoneProperties.NoSection))
                DrawProperty(property, settings, manualProperties);

            foreach (var section in TAOPhysBoneProperties.Sections)
            {
                var properties = TAOPhysBoneProperties.InSection(section.Name);
                if (properties.Count == 0 || !BeginSection(section)) continue;

                if (section.Name == "Transform") transformExtras?.Invoke();
                foreach (var property in properties) DrawProperty(property, settings, manualProperties);
                EditorGUI.indentLevel--;
            }
        }

        bool BeginSection(TAOPhysBoneSection section)
        {
            if (!_sectionFolds.TryGetValue(section.Name, out var open)) open = true;

            var rect = GUILayoutUtility.GetRect(EditorGUIUtility.fieldWidth, EditorGUIUtility.fieldWidth, 18f, 18f,
                EditorStyles.foldoutHeader);
            var buttonRect = new Rect(rect.xMax - HelpButtonWidth, rect.y, HelpButtonWidth, rect.height);
            var foldoutRect = new Rect(rect.x, rect.y,
                rect.width - HelpButtonWidth - EditorGUIUtility.standardVerticalSpacing, rect.height);

            open = EditorGUI.Foldout(foldoutRect, open, section.Name, EditorStyles.foldoutHeader);
            _sectionFolds[section.Name] = open;
            if (GUI.Button(buttonRect, "?"))
                Application.OpenURL("https://docs.vrchat.com/docs/physbones#" + section.DocTag);

            if (open) EditorGUI.indentLevel++;
            return open;
        }

        static int IndexOf(SerializedProperty manualProperties, string name)
        {
            for (var i = 0; i < manualProperties.arraySize; i++)
                if (manualProperties.GetArrayElementAtIndex(i).stringValue == name) return i;
            return -1;
        }

        SerializedProperty SourceProperty(string name) => _sourceObject?.FindProperty(_sourcePrefix + name);

        void DrawProperty(TAOPhysBoneProperty property, SerializedProperty settings,
            SerializedProperty manualProperties)
        {
            var isManual = IndexOf(manualProperties, property.Name) >= 0;
            var label = new GUIContent(property.Label);
            var valueProperty = isManual ? settings.FindPropertyRelative(property.Name) : SourceProperty(property.Name);

            var height = property.Kind == TAOPhysBonePropertyKind.Colliders && valueProperty != null
                ? EditorGUI.GetPropertyHeight(valueProperty, label, true)
                : EditorGUIUtility.singleLineHeight;
            var row = EditorGUILayout.GetControlRect(true, height);
            var popupRect = new Rect(row.xMax - TAOGUI.AutoPopupWidth, row.y, TAOGUI.AutoPopupWidth,
                EditorGUIUtility.singleLineHeight);
            var curveButtonRect = new Rect(popupRect.x - CurveButtonWidth - EditorGUIUtility.standardVerticalSpacing,
                row.y, CurveButtonWidth, EditorGUIUtility.singleLineHeight);
            var valueRect = new Rect(row.x, row.y,
                curveButtonRect.x - row.x - EditorGUIUtility.standardVerticalSpacing, row.height);

            var selected = TAOGUI.AutoManualPopup(popupRect, isManual);
            if (selected != (isManual ? 1 : 0))
            {
                SetManual(property, settings, manualProperties, selected == 1);
                return;
            }

            using (new EditorGUI.DisabledScope(!isManual))
            {
                if (valueProperty != null)
                    EditorGUI.PropertyField(valueRect, valueProperty, label, true);
                else
                    EditorGUI.LabelField(valueRect, label, new GUIContent("-"));
            }

            switch (property.Kind)
            {
                case TAOPhysBonePropertyKind.Curve:
                    DrawCurves(curveButtonRect, isManual,
                        new[] { CurveProperty(property.RelatedNames[0], settings, isManual) }, new[] { " " });
                    break;
                case TAOPhysBonePropertyKind.Vector3Curve:
                    DrawCurves(curveButtonRect, isManual,
                        property.RelatedNames.Select(n => CurveProperty(n, settings, isManual)).ToArray(),
                        new[] { "X", "Y", "Z" });
                    break;
                case TAOPhysBonePropertyKind.Permission:
                    DrawFilter(property, settings, isManual, valueProperty);
                    break;
            }
        }

        SerializedProperty CurveProperty(string name, SerializedProperty settings, bool isManual)
            => isManual ? settings.FindPropertyRelative(name) : SourceProperty(name);

        static bool HasKeys(SerializedProperty curve)
            => curve != null && curve.animationCurveValue != null && curve.animationCurveValue.length > 0;

        void DrawCurves(Rect buttonRect, bool editable, SerializedProperty[] curves, string[] labels)
        {
            var existing = curves.Where(c => c != null).ToArray();
            if (existing.Length == 0) return;
            var hasCurve = existing.Any(HasKeys);

            using (new EditorGUI.DisabledScope(!editable))
            {
                if (GUI.Button(buttonRect, hasCurve ? "X" : "C"))
                {
                    foreach (var curve in existing)
                        curve.animationCurveValue = hasCurve ? new AnimationCurve() : NewConstantCurve();
                }
            }

            if (!hasCurve) return;
            for (var i = 0; i < curves.Length; i++)
            {
                if (curves[i] == null) continue;
                var rect = EditorGUILayout.GetControlRect();
                rect.width -= TAOGUI.AutoPopupWidth + EditorGUIUtility.standardVerticalSpacing;
                using (new EditorGUI.DisabledScope(!editable))
                {
                    EditorGUI.BeginProperty(rect, null, curves[i]);
                    EditorGUI.BeginChangeCheck();
                    var value = EditorGUI.CurveField(rect, labels[i], curves[i].animationCurveValue, Color.cyan,
                        new Rect(0f, 0f, 1f, 1f));
                    if (EditorGUI.EndChangeCheck()) curves[i].animationCurveValue = value;
                    EditorGUI.EndProperty();
                }
            }
        }

        static AnimationCurve NewConstantCurve()
        {
            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0f, 1f));
            curve.AddKey(new Keyframe(1f, 1f));
            return curve;
        }

        void DrawFilter(TAOPhysBoneProperty property, SerializedProperty settings, bool isManual,
            SerializedProperty valueProperty)
        {
            // AdvancedBool.Other のときだけフィルタを表示する
            if (valueProperty == null || valueProperty.propertyType != SerializedPropertyType.Enum
                || valueProperty.enumValueIndex != 2) return;

            var filterName = property.RelatedNames[0];
            var filter = isManual ? settings.FindPropertyRelative(filterName) : SourceProperty(filterName);
            if (filter == null) return;

            using (new EditorGUI.DisabledScope(!isManual))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(filter.FindPropertyRelative("allowSelf"));
                EditorGUILayout.PropertyField(filter.FindPropertyRelative("allowOthers"));
                EditorGUI.indentLevel--;
            }
        }

        void SetManual(TAOPhysBoneProperty property, SerializedProperty settings,
            SerializedProperty manualProperties, bool manual)
        {
            if (!manual)
            {
                var index = IndexOf(manualProperties, property.Name);
                if (index >= 0) manualProperties.DeleteArrayElementAtIndex(index);
                return;
            }

            // 手動に切り替えたときは、自動の現在値から編集を始める
            foreach (var name in property.AllNames)
            {
                var from = SourceProperty(name);
                var to = settings.FindPropertyRelative(name);
                if (from != null && to != null) CopyValue(from, to);
            }

            manualProperties.arraySize++;
            manualProperties.GetArrayElementAtIndex(manualProperties.arraySize - 1).stringValue = property.Name;
        }

        static void CopyValue(SerializedProperty from, SerializedProperty to)
        {
            if (from.propertyType == SerializedPropertyType.Generic)
            {
                if (from.isArray)
                {
                    to.arraySize = from.arraySize;
                    for (var i = 0; i < from.arraySize; i++)
                        CopyValue(from.GetArrayElementAtIndex(i), to.GetArrayElementAtIndex(i));
                    return;
                }

                var source = from.Copy();
                var destination = to.Copy();
                var end = source.GetEndProperty();
                var first = true;
                while (true)
                {
                    var sourceMoved = source.NextVisible(first);
                    var destinationMoved = destination.NextVisible(first);
                    first = false;
                    if (!sourceMoved || !destinationMoved || SerializedProperty.EqualContents(source, end)) break;
                    CopyValue(source, destination);
                }

                return;
            }

            switch (from.propertyType)
            {
                case SerializedPropertyType.Integer: to.intValue = from.intValue; break;
                case SerializedPropertyType.Boolean: to.boolValue = from.boolValue; break;
                case SerializedPropertyType.Float: to.floatValue = from.floatValue; break;
                case SerializedPropertyType.String: to.stringValue = from.stringValue; break;
                case SerializedPropertyType.Enum: to.enumValueIndex = from.enumValueIndex; break;
                case SerializedPropertyType.Vector3: to.vector3Value = from.vector3Value; break;
                case SerializedPropertyType.AnimationCurve: to.animationCurveValue = from.animationCurveValue; break;
                case SerializedPropertyType.ObjectReference: to.objectReferenceValue = from.objectReferenceValue; break;
            }
        }
    }
}
