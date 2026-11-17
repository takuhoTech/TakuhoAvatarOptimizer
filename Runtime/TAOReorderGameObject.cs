using UnityEngine;
using VRC.SDKBase;

namespace TakuhoTech.TAO
{
    /// <summary>
    /// 同じ親を持つ子 GameObject 全体を、Order の昇順（小さいほど上）に並べ替える。
    /// このコンポーネントが付いていない GameObject は Order = DefaultOrder として扱い、Order が同じ場合は元の並び順を維持する。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("TAO/TAO Reorder GameObject")]
    public class TAOReorderGameObject : MonoBehaviour, IEditorOnly
    {
        /// <summary>Order の初期値。コンポーネントが付いていない GameObject の Order としても使う。</summary>
        public const int DefaultOrder = 50;

        /// <summary>並び順。小さいほど上、大きいほど下に配置する。</summary>
        public int order = DefaultOrder;
    }
}
