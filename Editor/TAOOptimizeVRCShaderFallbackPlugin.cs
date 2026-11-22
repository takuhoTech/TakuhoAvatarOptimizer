using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using TakuhoTech.TAO;
using TakuhoTech.TAO.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(TAOOptimizeVRCShaderFallbackPlugin))]

namespace TakuhoTech.TAO.Editor
{
    internal class TAOOptimizeVRCShaderFallbackPlugin : Plugin<TAOOptimizeVRCShaderFallbackPlugin>
    {
        const string AvatarOptimizer = "com.anatawa12.avatar-optimizer";

        public override string QualifiedName => "com.takuho.tao.optimize-vrc-shader-fallback";
        public override string DisplayName => "TAO Optimize VRC Shader Fallback";

        /// <summary>コンポーネントを削除したあとも、設定を最後のパスまで持ち運ぶ。</summary>
        sealed class State
        {
            public TAOShaderFallbackSettings Settings;
            public readonly HashSet<Material> FaceMaterials = new HashSet<Material>();
        }

        protected override void Configure()
        {
            // AAO は、知らないコンポーネントが残っていると警告を出す。設定を読み取って、先にコンポーネントを削除しておく
            InPhase(BuildPhase.Resolving).Run("Read VRC Shader Fallback Settings", ReadSettings);

            // AAO は、シェーダーが使わないマテリアルの値を取り除く。フォールバック専用の値は、そのあとで書き込む
            InPhase(BuildPhase.Optimizing)
                .AfterPlugin(AvatarOptimizer)
                .Run("Optimize VRC Shader Fallback", Optimize)
                .PreviewingWith(new TAOShaderFallbackPreviewFilter());
        }

        static void ReadSettings(BuildContext context)
        {
            var all = context.AvatarRootObject.GetComponentsInChildren<TAOOptimizeVRCShaderFallback>(true);
            var components = all.Where(c => !TAOFallbackAvatarContext.IsEditorOnly(c.transform)).ToList();

            if (components.Count > 0)
            {
                var component = components[0];
                if (components.Count > 1) TAOBuildReport.Warn(TAOBuildReport.MultipleShaderFallbacks, component);

                var state = context.GetState<State>();
                state.Settings = TAOShaderFallbackSettings.From(component);

                // 顔のメッシュは、ほかのメッシュと結合される前に調べておく
                TAOFallbackAvatarContext.CollectFaceMaterials(context.AvatarRootObject, state.FaceMaterials);
            }

            foreach (var component in all) Object.DestroyImmediate(component);
        }

        static void Optimize(BuildContext context)
        {
            var state = context.GetState<State>();
            if (state.Settings == null) return;

            // アニメーションを調べる準備（NDMF の AnimatorServicesContext）は、アニメーターを作り直すので時間がかかる。
            // コンポーネントの無いアバターのビルドに影響しないように、必要なときだけここで有効にする
            var animator = context.ActivateExtensionContextRecursive<AnimatorServicesContext>();
            try
            {
                Process(context, state, animator);
            }
            finally
            {
                // NDMF は、このパスのあとでは何も有効になっていないものとして、以降のパスを実行する
                context.DeactivateAllExtensionContexts();
            }
        }

        static void Process(BuildContext context, State state, AnimatorServicesContext animator)
        {
            var settings = state.Settings;
            var avatar = new TAOFallbackAvatarContext(context.AvatarRootObject) { ResolveOriginal = OriginalOf };
            avatar.FaceMaterials.UnionWith(state.FaceMaterials);
            CollectMaterials(context, animator, avatar);

            var planner = new TAOFallbackPlanner(settings, avatar, new TAOFallbackSurfaceAnalyzer(avatar));
            var replaced = new Dictionary<Material, Material>();
            var counts = new SortedDictionary<TAOFallbackKind, int>();

            using (var textures = new TAOFallbackTextures(false))
            {
                foreach (var material in avatar.Materials)
                {
                    Material clone = null;
                    try
                    {
                        var plan = planner.Plan(material);
                        if (!plan.Changes) continue;

                        // 元のマテリアルは変更せず、複製に書き込む
                        clone = TAOFallbackWriter.Clone(material);
                        TAOFallbackWriter.Apply(clone, plan, textures);
                        ObjectRegistry.RegisterReplacedObject(material, clone);
                        replaced[material] = clone;

                        counts.TryGetValue(plan.Kind, out var count);
                        counts[plan.Kind] = count + 1;
                    }
                    catch (Exception exception)
                    {
                        // 1 つのマテリアルの失敗で、ビルド全体を止めない。そのマテリアルは元のまま使う
                        if (clone != null && !replaced.ContainsKey(material)) Object.DestroyImmediate(clone);
                        Debug.LogWarning($"[TAO] Failed to set the shader fallback of {material.name}: {exception}", material);
                    }
                }
            }

            if (replaced.Count == 0) return;
            ReplaceMaterials(context, animator, replaced);

            Debug.Log("[TAO] Optimize VRC Shader Fallback: " +
                      string.Join(", ", counts.Select(entry => $"{TAOFallbackKinds.Label(entry.Key)} {entry.Value}")));
        }

        // ほかのツールがビルド中に複製したマテリアルから、複製元を求める
        static Material OriginalOf(Material material)
            => ObjectRegistry.GetReference(material)?.Object as Material ?? material;

        static void CollectMaterials(BuildContext context, AnimatorServicesContext animator, TAOFallbackAvatarContext avatar)
        {
            foreach (var renderer in context.AvatarRootObject.GetComponentsInChildren<Renderer>(true))
                if (!TAOFallbackAvatarContext.IsEditorOnly(renderer.transform)) avatar.AddRenderer(renderer);

            // アニメーションで差し替えるマテリアル
            foreach (var (binding, value) in animator.AnimationIndex.GetPPtrReferencedObjectsWithBinding)
            {
                if (!(value is Material material)) continue;

                Renderer renderer = null;
                var hasSlot = TAOFallbackAvatarContext.TryGetMaterialSlot(binding.propertyName, out var slot);
                if (hasSlot)
                {
                    var target = animator.ObjectPathRemapper.GetObjectForPath(binding.path);
                    if (target != null) target.TryGetComponent(out renderer);
                }

                avatar.AddAnimatedMaterial(material, renderer, slot);
            }

            // アニメーションで変化するマテリアルのプロパティ
            var avatarRoot = context.AvatarRootTransform;
            var controllers = animator.ControllerContext.Controllers;
            var targets = new Dictionary<(Transform, string), Renderer>();
            foreach (var key in controllers.Keys.ToList())
            {
                if (!controllers.TryGetValue(key, out var controller) || controller == null) continue;

                // 子の Animator のアニメーションは、アバターのルートではなく、その Animator からのパスで書かれている
                var origin = key is Animator child && child != null && child.transform != avatarRoot
                    ? child.transform
                    : null;

                foreach (var node in controller.AllReachableNodes())
                {
                    if (!(node is VirtualClip clip)) continue;
                    foreach (var binding in clip.GetFloatCurveBindings())
                    {
                        if (!TAOFallbackAvatarContext.TryGetMaterialPropertyName(binding.propertyName, out var name)) continue;
                        avatar.AddAnimatedProperty(name, FindAnimatedRenderer(animator, origin, binding.path, targets));
                    }
                }
            }
        }

        // アニメーションの対象のレンダラー。見つからなければ null
        static Renderer FindAnimatedRenderer(AnimatorServicesContext animator, Transform origin, string path,
            Dictionary<(Transform, string), Renderer> cache)
        {
            if (cache.TryGetValue((origin, path), out var renderer)) return renderer;

            Transform target;
            if (origin != null)
            {
                target = string.IsNullOrEmpty(path) ? origin : origin.Find(path);
            }
            else
            {
                var found = animator.ObjectPathRemapper.GetObjectForPath(path);
                target = found != null ? found.transform : null;
            }

            if (target != null) target.TryGetComponent(out renderer);
            cache[(origin, path)] = renderer;
            return renderer;
        }

        static void ReplaceMaterials(BuildContext context, AnimatorServicesContext animator,
            Dictionary<Material, Material> replaced)
        {
            foreach (var renderer in context.AvatarRootObject.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null || !replaced.TryGetValue(materials[i], out var clone)) continue;
                    materials[i] = clone;
                    changed = true;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            animator.AnimationIndex.RewriteObjectCurves(value =>
                value is Material material && replaced.TryGetValue(material, out var clone) ? clone : value);
        }
    }
}
