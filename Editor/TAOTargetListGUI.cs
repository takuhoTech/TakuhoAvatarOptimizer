using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// 対象の PhysBone の一覧を描画する。各行の左のラジオボタンで本体を選び、
    /// 一覧の最後の行のラジオボタンで、新しい PhysBone を生成して本体にするかを選ぶ。
    /// </summary>
    internal sealed class TAOTargetListGUI
    {
        const float RadioWidth = 20f;
        const float ListButtonWidth = 22f;
        const float NoticeIconWidth = 18f;
        const float LabelOffset = RadioWidth + 4f;

        static readonly FieldInfo LastControlIdField =
            typeof(EditorGUIUtility).GetField("s_LastControlID", BindingFlags.Static | BindingFlags.NonPublic);

        readonly UnityEditor.Editor _editor;
        readonly SerializedProperty _physBones;
        readonly SerializedProperty _mainPhysBone;
        readonly SerializedProperty _mainSource;

        public TAOTargetListGUI(UnityEditor.Editor editor, SerializedObject serializedObject)
        {
            _editor = editor;
            _physBones = serializedObject.FindProperty(nameof(TAOMergePhysBoneByConstraint.physBones));
            _mainPhysBone = serializedObject.FindProperty(nameof(TAOMergePhysBoneByConstraint.mainPhysBone));
            _mainSource = serializedObject.FindProperty(nameof(TAOMergePhysBoneByConstraint.mainSource));
        }

        bool IsGenerated => _mainSource.enumValueIndex == (int)TAOMainSource.Generated;

        // 本体の行の左端のラジオボタン。新規生成の行も同じ列にそろえる
        static Rect RadioRect(Rect row) => new Rect(row.x + 2f, row.y, RadioWidth, row.height);

        public void Draw(TAOMergePhysBoneByConstraint component, TAOInspectorCache cache)
        {
            AdoptMainIntoList();

            var header = EditorGUILayout.GetControlRect();
            var style = DragAndDrop.activeControlID == -10 ? EditorStyles.foldoutPreDrop : EditorStyles.foldout;
            var dragEvent = new Event(Event.current);
            _physBones.isExpanded = EditorGUI.Foldout(header, _physBones.isExpanded,
                new GUIContent(T("対象のPhysBone", "Target PhysBones"),
                    T("本体として残すPhysBoneと、Rotation Constraintに置き換わる追従PhysBoneを追加します。ドラッグ＆ドロップで追加できます",
                      "Add the PhysBone to keep as the main one and the follower PhysBones that become Rotation Constraints. You can drag and drop them here")),
                true, style);
            HandleDrop(header, GetLastControlId(), dragEvent);
            if (!_physBones.isExpanded) return;

            var mainIndex = IsGenerated ? -1 : FindMainIndex();
            for (var i = 0; i < _physBones.arraySize; i++)
            {
                if (DrawRow(i, mainIndex, component, cache)) break;
            }

            DrawAddRow();
            DrawGenerateRow();
        }

        // 本体が一覧に含まれていなければ、一覧に追加する
        void AdoptMainIntoList()
        {
            if (IsGenerated || _mainPhysBone.objectReferenceValue == null || FindMainIndex() >= 0) return;
            _physBones.arraySize++;
            _physBones.GetArrayElementAtIndex(_physBones.arraySize - 1).objectReferenceValue =
                _mainPhysBone.objectReferenceValue;
        }

        // 本体が一覧の何番目にあるか。無ければ -1
        int FindMainIndex()
        {
            var main = _mainPhysBone.objectReferenceValue;
            if (main == null) return -1;
            for (var i = 0; i < _physBones.arraySize; i++)
                if (_physBones.GetArrayElementAtIndex(i).objectReferenceValue == main) return i;
            return -1;
        }

        /// <summary>1 行を描画する。+ や - で一覧が変わったら true を返す。</summary>
        bool DrawRow(int index, int mainIndex, TAOMergePhysBoneByConstraint component, TAOInspectorCache cache)
        {
            var element = _physBones.GetArrayElementAtIndex(index);
            var isMain = index == mainIndex;
            var row = EditorGUILayout.GetControlRect();
            if (isMain) EditorGUI.DrawRect(row, TAOGUI.MainTint);

            var labelRect = new Rect(row.x + LabelOffset, row.y, EditorGUIUtility.labelWidth - LabelOffset, row.height);
            var previousColor = GUI.contentColor;
            if (isMain) GUI.contentColor = TAOGUI.MainColor;
            EditorGUI.LabelField(labelRect,
                isMain
                    ? new GUIContent(T("本体", "Main"),
                        T("このPhysBoneは残り、追従PhysBoneがこの動きに従います",
                          "This PhysBone is kept, and the follower PhysBones follow its motion"))
                    : new GUIContent(T("追従", "Follower")));
            GUI.contentColor = previousColor;

            if (!isMain && element.objectReferenceValue is VRCPhysBoneBase physBone)
            {
                DrawRowNotice(new Rect(labelRect.xMax - NoticeIconWidth, row.y, NoticeIconWidth, row.height),
                    component, cache, physBone);
            }

            var minus = new Rect(row.xMax - 2f - ListButtonWidth, row.y, ListButtonWidth, row.height);
            var plus = new Rect(minus.x - ListButtonWidth, row.y, ListButtonWidth, row.height);
            var fieldX = row.x + EditorGUIUtility.labelWidth;
            var fieldRect = new Rect(fieldX, row.y, plus.x - 2f - fieldX, row.height);

            var before = element.objectReferenceValue;
            EditorGUI.PropertyField(fieldRect, element, GUIContent.none);
            if (isMain && element.objectReferenceValue != before)
                _mainPhysBone.objectReferenceValue = element.objectReferenceValue;

            using (new EditorGUI.DisabledScope(element.objectReferenceValue == null))
            {
                var selected = GUI.Toggle(RadioRect(row), isMain,
                    new GUIContent("", T("この PhysBone を本体にします（追従せずに残ります）",
                        "Use this PhysBone as the main one (it is kept and does not follow)")),
                    EditorStyles.radioButton);
                if (selected && !isMain)
                {
                    _mainPhysBone.objectReferenceValue = element.objectReferenceValue;
                    _mainSource.enumValueIndex = (int)TAOMainSource.Existing;
                }
            }

            if (GUI.Button(plus, "+"))
            {
                _physBones.InsertArrayElementAtIndex(index + 1);
                _physBones.GetArrayElementAtIndex(index + 1).objectReferenceValue = null;
                return true;
            }

            if (GUI.Button(minus, "-"))
            {
                // 参照が入っている要素は1回目で参照を外し、2回目で要素を削除する
                if (isMain) _mainPhysBone.objectReferenceValue = null;
                if (element.objectReferenceValue != null)
                    element.objectReferenceValue = null;
                else
                    _physBones.DeleteArrayElementAtIndex(index);
                return true;
            }

            return false;
        }

        void DrawAddRow()
        {
            var row = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(
                new Rect(row.x + LabelOffset, row.y, EditorGUIUtility.labelWidth - LabelOffset, row.height),
                T("追加する要素", "Element to add"));
            var fieldRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y,
                row.width - EditorGUIUtility.labelWidth, row.height);

            var added = EditorGUI.ObjectField(fieldRect, GUIContent.none, null, typeof(VRCPhysBoneBase), true);
            if (added != null) AddUnique(added);
        }

        void DrawGenerateRow()
        {
            var generated = IsGenerated;
            var row = EditorGUILayout.GetControlRect();
            if (generated) EditorGUI.DrawRect(row, TAOGUI.MainTint);

            var tooltip = T("追従PhysBoneの平均的な位置に新しいPhysBoneを生成し、本体にします。追従PhysBoneはすべて削除されます",
                "Generate a new PhysBone at the average position of the follower PhysBones and use it as the main one. All follower PhysBones are removed");
            var previousColor = GUI.contentColor;
            if (generated) GUI.contentColor = TAOGUI.MainColor;
            EditorGUI.LabelField(
                new Rect(row.x + LabelOffset, row.y, row.width - LabelOffset, row.height),
                new GUIContent(T("新しいPhysBoneを生成して本体にする", "Generate a new PhysBone as the main one"), tooltip));
            GUI.contentColor = previousColor;

            var selected = GUI.Toggle(RadioRect(row), generated, new GUIContent("", tooltip),
                EditorStyles.radioButton);
            if (selected && !generated) _mainSource.enumValueIndex = (int)TAOMainSource.Generated;
        }

        // 階層やボーン数が本体と異なる行に、アイコンを表示する。理由はツールチップで確認する
        static void DrawRowNotice(Rect rect, TAOMergePhysBoneByConstraint component, TAOInspectorCache cache,
            VRCPhysBoneBase physBone)
        {
            var messages = new List<string>();
            var hasDifferentParent = !cache.HasSameParentAsMain(physBone);
            if (hasDifferentParent)
            {
                messages.Add(T("本体と階層（ルートの親）が異なるため、ワールド空間で追従します。",
                    "The hierarchy (parent of the root) differs from the main one, so it follows in world space."));
            }

            // 画面上のボーン数と同じく、先端のボーンを含む数で表示する。どちらが追従側かが分かるように書く
            var followerCount = cache.RotatingBoneCountOf(physBone);
            var mainCount = cache.MainRotatingBoneCount;
            if (mainCount > 0 && followerCount != mainCount)
            {
                var mainName = component.mainSource == TAOMainSource.Generated ? T("生成", "new") : T("本体", "main");
                messages.Add(T($"ボーン数（先端を含む）が異なります（追従 {followerCount + 1} → {mainName} {mainCount + 1}）",
                    $"The bone count (including the end bone) differs (follower {followerCount + 1} → {mainName} {mainCount + 1})"));
            }

            if (messages.Count == 0) return;
            var icon = EditorGUIUtility.IconContent(hasDifferentParent ? "console.warnicon.sml" : "console.infoicon.sml");
            GUI.Label(rect, new GUIContent(icon.image, string.Join("\n", messages)));
        }

        // 同じ PhysBone が既に一覧にあれば追加しない
        void AddUnique(Object physBone)
        {
            if (physBone == null) return;
            for (var i = 0; i < _physBones.arraySize; i++)
                if (_physBones.GetArrayElementAtIndex(i).objectReferenceValue == physBone) return;

            _physBones.arraySize++;
            _physBones.GetArrayElementAtIndex(_physBones.arraySize - 1).objectReferenceValue = physBone;
        }

        // AAO の PrefabSafeSet と同様に、見出し上へのドロップで追加する（GameObject は自身の PhysBone をすべて追加）
        void HandleDrop(Rect position, int controlId, Event e)
        {
            if (e.type == EventType.DragExited)
            {
                _editor.Repaint();
                return;
            }

            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!position.Contains(e.mousePosition) || !GUI.enabled) return;

            var accepted = false;
            foreach (var dragged in DragAndDrop.objectReferences)
            {
                var found = dragged is GameObject draggedObject ? draggedObject.GetComponents<VRCPhysBoneBase>()
                    : dragged is VRCPhysBoneBase draggedPhysBone ? new[] { draggedPhysBone }
                    : new VRCPhysBoneBase[0];
                if (found.Length == 0) continue;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform)
                {
                    foreach (var physBone in found) AddUnique(physBone);
                    accepted = true;
                    DragAndDrop.activeControlID = 0;
                }
                else
                {
                    DragAndDrop.activeControlID = controlId;
                }
            }

            if (!accepted) return;
            GUI.changed = true;
            DragAndDrop.AcceptDrag();
        }

        static int GetLastControlId()
            => LastControlIdField == null ? 0 : (int)LastControlIdField.GetValue(null);
    }
}
