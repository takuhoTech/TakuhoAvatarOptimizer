using System.Linq;
using nadena.dev.ndmf;
using TakuhoTech.TAO;
using TakuhoTech.TAO.Editor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(TAOReorderGameObjectPlugin))]

namespace TakuhoTech.TAO.Editor
{
    internal class TAOReorderGameObjectPlugin : Plugin<TAOReorderGameObjectPlugin>
    {
        public override string QualifiedName => "com.takuho.tao.reorder-gameobject";
        public override string DisplayName => "TAO Reorder GameObject";

        protected override void Configure()
        {
            InPhase(BuildPhase.FirstChance)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Reorder Siblings By Order", ctx =>
                {
                    var components = ctx.AvatarRootObject
                        .GetComponentsInChildren<TAOReorderGameObject>(true);

                    // アバターのルートには並べ替える兄弟がいないので、対象にしない
                    var parents = components
                        .Select(c => c.transform)
                        .Where(t => t != ctx.AvatarRootTransform)
                        .Select(t => t.parent)
                        .Distinct()
                        .ToList();

                    foreach (var parent in parents) SortChildren(parent);

                    foreach (var component in components) Object.DestroyImmediate(component);
                });
        }

        /// <summary>
        /// 子全体を Order の昇順に並べ替える。OrderBy は安定ソートなので、Order が同じ子は元の並び順のままになる。
        /// </summary>
        static void SortChildren(Transform parent)
        {
            var sorted = Enumerable.Range(0, parent.childCount)
                .Select(i => parent.GetChild(i))
                .OrderBy(OrderOf)
                .ToList();

            for (var i = 0; i < sorted.Count; i++) sorted[i].SetSiblingIndex(i);
        }

        /// <summary>コンポーネントが付いていない GameObject は、DefaultOrder として扱う。</summary>
        static int OrderOf(Transform child)
        {
            var component = child.GetComponent<TAOReorderGameObject>();
            return component != null ? component.order : TAOReorderGameObject.DefaultOrder;
        }
    }
}
