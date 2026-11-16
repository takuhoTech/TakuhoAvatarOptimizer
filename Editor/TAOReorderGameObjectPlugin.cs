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
                .Run("Move Objects To Last Sibling", ctx =>
                {
                    var components = ctx.AvatarRootObject
                        .GetComponentsInChildren<TAOReorderGameObject>(true);

                    foreach (var component in components)
                    {
                        var target = component.transform;
                        if (target.parent != null)
                            target.SetAsLastSibling();

                        Object.DestroyImmediate(component);
                    }
                });
        }
    }
}
