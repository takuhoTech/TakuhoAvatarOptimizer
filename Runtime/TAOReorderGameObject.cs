using UnityEngine;
using VRC.SDKBase;

namespace TakuhoTech.TAO
{
    // 同じ親の下で、このGameObjectを最後の兄弟へ移動する
    [DisallowMultipleComponent]
    [AddComponentMenu("TAO/TAO Reorder GameObject")]
    public class TAOReorderGameObject : MonoBehaviour, IEditorOnly
    {
    }
}
