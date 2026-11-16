using UnityEngine;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// 自動で作る設定を SerializedObject として表示するための入れ物。
    /// 本体の設定の「自動」の値を、読み取り専用で見せるのに使う。
    /// </summary>
    internal sealed class TAOCombinedSettingsHolder : ScriptableObject
    {
        public TAOPhysBoneSettings settings = new TAOPhysBoneSettings();
    }
}
