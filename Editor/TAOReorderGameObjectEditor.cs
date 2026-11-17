using UnityEditor;
using UnityEngine;
using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    [CustomEditor(typeof(TAOReorderGameObject))]
    internal class TAOReorderGameObjectEditor : UnityEditor.Editor
    {
        const int DefaultOrder = TAOReorderGameObject.DefaultOrder;

        SerializedProperty _order;

        void OnEnable() => _order = serializedObject.FindProperty(nameof(TAOReorderGameObject.order));

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawLanguagePopup();
            EditorGUILayout.PropertyField(_order, new GUIContent(T("Order（並び順）", "Order"),
                T("小さいほど上、大きいほど下に配置されます",
                  "Smaller values are placed higher, larger values lower")));
            EditorGUILayout.HelpBox(T(
                "ビルド時に、同じ親を持つ子GameObject全体を、Orderの昇順（小さいほど上、大きいほど下）に並べ替えます。\n" +
                $"・コンポーネントが付いていないGameObjectは、Order = {DefaultOrder}として扱います。\n" +
                "・Orderが同じ場合は、元の並び順を維持します。",
                "At build time, sorts all child GameObjects under the same parent by Order, in ascending order (smaller values are placed higher, larger values lower).\n" +
                $"- GameObjects without this component are treated as Order = {DefaultOrder}.\n" +
                "- GameObjects with the same Order keep their original order."),
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
