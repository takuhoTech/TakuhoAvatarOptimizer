using UnityEditor;
using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    [CustomEditor(typeof(TAOReorderGameObject))]
    internal class TAOReorderGameObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawLanguagePopup();
            EditorGUILayout.HelpBox(T(
                "ビルド時に、このGameObjectを同じ親の下で最後の兄弟へ移動します。",
                "Moves this GameObject to the last sibling under its parent at build time."),
                MessageType.Info);
        }
    }
}
