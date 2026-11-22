using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// Safety でシェーダーが無効化されたときの見た目を、シーン上で確認するためのプレビュー。
    /// ビルドと同じ設定を複製したマテリアルに書き込み、フォールバック先のシェーダーに差し替えて表示する。
    /// </summary>
    internal sealed class TAOShaderFallbackPreviewFilter : IRenderFilter
    {
        /// <summary>プレビューの切り替え。インスペクターのボタンと、NDMF のプレビュー設定から操作する。</summary>
        public static readonly TogglablePreviewNode Toggle = TogglablePreviewNode.Create(
            () => "TAO VRC Shader Fallback", null, false);

        public bool IsEnabled(ComputeContext context) => context.Observe(Toggle.IsEnabled);

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return Toggle;
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = ImmutableList.CreateBuilder<RenderGroup>();
            foreach (var root in context.GetAvatarRoots())
            {
                if (FindComponent(context, root) == null) continue;

                var renderers = context.GetComponentsInChildren<Renderer>(root, true)
                    .Where(renderer => (renderer is SkinnedMeshRenderer || renderer is MeshRenderer) &&
                                       !TAOFallbackAvatarContext.IsEditorOnly(renderer.transform))
                    .ToList();
                if (renderers.Count > 0) groups.Add(RenderGroup.For(renderers));
            }

            return groups.ToImmutable();
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            var node = new Node();
            try
            {
                node.Build(proxyPairs.ToList(), context);
            }
            catch (Exception exception)
            {
                // プレビューの失敗で、ほかのプレビューを止めない。作りかけのものは破棄して、何もしないノードを返す
                Debug.LogException(exception);
                node.Dispose();
                node = new Node();
            }

            return Task.FromResult<IRenderFilterNode>(node);
        }

        /// <summary>ビルドで使われるコンポーネント。EditorOnly のものは、ビルドに含まれないので除く。</summary>
        public static TAOOptimizeVRCShaderFallback FindComponent(ComputeContext context, GameObject avatarRoot)
        {
            return context.GetComponentsInChildren<TAOOptimizeVRCShaderFallback>(avatarRoot, true)
                .FirstOrDefault(component => component != null &&
                                             !TAOFallbackAvatarContext.IsEditorOnly(component.transform));
        }

        /// <summary>
        /// NDMF は、毎フレーム、プロキシのレンダラーを元のレンダラーの状態に戻してから OnFrame を呼ぶ。
        /// また、Instantiate に渡されるプロキシは準備用で、表示に使われるプロキシとは別のオブジェクトになる。
        /// そのため、プレビュー用のマテリアルは Instantiate で作っておき、OnFrame で毎フレーム差し替える。
        /// </summary>
        sealed class Node : IRenderFilterNode
        {
            // NDMF は、スクリプトの再読み込み（再コンパイルや再生の開始）のときにはノードを Dispose しない。
            // プレビュー用のマテリアルとテクスチャがメモリに残り続けないように、再読み込みの前に破棄する
            static readonly HashSet<Node> Live = new HashSet<Node>();

            static Node()
            {
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
                {
                    foreach (var node in Live.ToArray()) node.Dispose();
                };
            }

            // プロキシのマテリアル（前段のフィルターが差し替えたものを含む）から、プレビュー用のマテリアルへの対応
            readonly Dictionary<Material, Material> _previews = new Dictionary<Material, Material>();
            readonly List<Material> _created = new List<Material>();
            readonly List<Material> _slots = new List<Material>();
            TAOFallbackTextures _textures;
            GameObject _root;
            TAOOptimizeVRCShaderFallback _component;

            public RenderAspects WhatChanged { get; private set; } = RenderAspects.Material | RenderAspects.Texture;

            public void Build(List<(Renderer, Renderer)> pairs, ComputeContext context)
            {
                if (pairs.Count == 0) return;

                var rootTransform = RuntimeUtil.FindAvatarInParents(pairs[0].Item1.transform);
                if (rootTransform == null) return;
                _root = rootTransform.gameObject;

                _component = Observe(context);
                if (_component == null) return;
                var settings = TAOShaderFallbackSettings.From(_component);

                // インスペクターの一覧と同じ判定になるように、表示されていないメッシュも含めて調べる。
                // 表示されているメッシュのマテリアルは、前段のフィルターの結果が反映されたプロキシから読む
                var proxies = new Dictionary<Renderer, Renderer>();
                foreach (var (original, proxy) in pairs) proxies[original] = proxy;
                var avatar = TAOFallbackAvatarContext.FromScene(_root,
                    renderer => proxies.TryGetValue(renderer, out var displayed) ? displayed : renderer);
                avatar.ResolveOriginal =
                    material => ObjectRegistry.GetReference(material)?.Object as Material ?? material;

                var planner = new TAOFallbackPlanner(settings, avatar, new TAOFallbackSurfaceAnalyzer(avatar));
                Live.Add(this);
                _textures = new TAOFallbackTextures(true);

                var planned = new HashSet<Material>();
                foreach (var (_, proxy) in pairs)
                {
                    foreach (var material in proxy.sharedMaterials)
                    {
                        if (material == null || !planned.Add(material)) continue;

                        try
                        {
                            var preview = CreatePreview(planner.Plan(material));
                            if (preview != null) _previews[material] = preview;
                        }
                        catch (Exception exception)
                        {
                            // 1 つのマテリアルの失敗で、プレビュー全体を止めない
                            Debug.LogException(exception, material);
                        }
                    }
                }
            }

            // プレビューで再現しないマテリアルは null
            Material CreatePreview(TAOFallbackPlan plan)
            {
                var kind = TAOFallbackPreviewMaterials.DisplayedKind(plan, out var tag);
                if (kind == TAOFallbackKind.Unchanged || kind == TAOFallbackKind.Custom) return null;

                var processed = TAOFallbackWriter.Clone(plan.Material);
                Material preview;
                try
                {
                    TAOFallbackWriter.Apply(processed, plan, _textures);
                    preview = TAOFallbackPreviewMaterials.Create(processed, kind, tag);
                }
                catch
                {
                    Object.DestroyImmediate(processed);
                    throw;
                }

                if (preview != processed) Object.DestroyImmediate(processed);

                preview.hideFlags = HideFlags.HideAndDontSave;
                _created.Add(preview);

                // 後段のフィルターが、プレビュー用のマテリアルから元のマテリアルをたどれるようにする
                ObjectRegistry.RegisterReplacedObject(plan.Material, preview);
                return preview;
            }

            // コンポーネントの有無と設定を監視する。変わったら、NDMF がこのノードを作り直す
            TAOOptimizeVRCShaderFallback Observe(ComputeContext context)
            {
                var component = FindComponent(context, _root);
                if (component != null) context.Observe(component, c => TAOShaderFallbackSettings.From(c).Signature);
                return component;
            }

            /// <summary>
            /// ブレンドシェイプなど、マテリアルに関係しないものだけが変わったときは、作り直さずに使い続ける。
            /// 作り直すと、アバター全体のマテリアルとメッシュを調べ直すことになり、時間がかかるため。
            /// </summary>
            public Task<IRenderFilterNode> Refresh(IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context,
                RenderAspects updatedAspects)
            {
                // updatedAspects が 0 のときは、このノードが監視している設定が変わっている
                var isReusable = _component != null && updatedAspects != 0 &&
                                 (updatedAspects & ~RenderAspects.Shapes) == 0;

                // 使い続ける場合は、新しい context で設定を監視し直す
                if (!isReusable || Observe(context) != _component) return Task.FromResult<IRenderFilterNode>(null);

                WhatChanged = 0;
                return Task.FromResult<IRenderFilterNode>(this);
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (_previews.Count == 0) return;

                proxy.GetSharedMaterials(_slots);
                var changed = false;
                for (var i = 0; i < _slots.Count; i++)
                {
                    var material = _slots[i];
                    if (material == null || !_previews.TryGetValue(material, out var preview) || preview == null) continue;

                    _slots[i] = preview;
                    changed = true;
                }

                if (changed) proxy.SetSharedMaterials(_slots);
                _slots.Clear();
            }

            public void Dispose()
            {
                Live.Remove(this);

                foreach (var material in _created)
                    if (material != null) Object.DestroyImmediate(material);
                _created.Clear();
                _previews.Clear();

                _textures?.Dispose();
                _textures = null;
            }
        }
    }

    /// <summary>フォールバックしたときに VRChat が使うシェーダーで表示する、プレビュー用のマテリアルを作る。</summary>
    internal static class TAOFallbackPreviewMaterials
    {
        const string PreviewShader = "Hidden/TAO/ShaderFallbackPreview";

        static readonly string[] Passes =
        {
            "Always", "ForwardBase", "ForwardAdd", "Deferred", "ShadowCaster", "MotionVectors", "Vertex", "VertexLMRGBM",
            "VertexLM", "Meta",
        };

        /// <summary>
        /// Safety が有効のときに、VRChat がこのマテリアルの表示に使うフォールバック先。
        /// 変更しないマテリアルも、VRCFallback が設定されていれば（マテリアルでもシェーダーでも）、そのフォールバック先で表示される。
        /// そのまま表示されるマテリアルは Unchanged、プレビューで再現できないものは Custom を返す。
        /// </summary>
        public static TAOFallbackKind DisplayedKind(TAOFallbackPlan plan, out string tag)
        {
            if (plan.Changes)
            {
                tag = plan.Tag ?? "";
                return plan.Kind == TAOFallbackKind.Custom ? TAOFallbackKinds.FromTag(tag) : plan.Kind;
            }

            tag = plan.Info.Family == TAOFallbackFamily.BuiltIn ? "" : plan.Info.ExistingTag;
            return TAOFallbackKinds.FromTag(tag);
        }

        /// <summary>
        /// processed は、ビルドと同じ設定を書き込んだマテリアル。
        /// プレビューで再現できないフォールバック先の場合は、processed をそのまま返す。
        /// </summary>
        public static Material Create(Material processed, TAOFallbackKind kind, string tag)
        {
            switch (kind)
            {
                case TAOFallbackKind.ToonStandard:
                    return SwapShader(processed, TAOFallbackPlanner.ToonStandardShaderName);
                case TAOFallbackKind.ToonStandardOutline:
                    return SwapShader(processed, TAOFallbackPlanner.ToonStandardOutlineShaderName);
                case TAOFallbackKind.Standard:
                case TAOFallbackKind.StandardCutout:
                case TAOFallbackKind.StandardFade:
                case TAOFallbackKind.StandardTransparent:
                    return CreateStandard(processed, kind);
                case TAOFallbackKind.Custom:
                case TAOFallbackKind.Unchanged:
                    return processed;
                default:
                    return CreateSimple(processed, kind, tag ?? "");
            }
        }

        // VRChat と同じように、マテリアルの値を保ったままシェーダーを差し替える。同じ名前の値とキーワードが引き継がれる
        static Material SwapShader(Material processed, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) return processed;

            TAOFallbackWriter.RemoveUndeclaredTextures(processed);

            Material preview;
            using (TAOFallbackWriter.NoPropertyDrawers()) preview = new Material(processed) { shader = shader };

            preview.renderQueue = shader.renderQueue;
            foreach (var pass in Passes) preview.SetShaderPassEnabled(pass, true);
            return preview;
        }

        // Standard は、同じ名前の値（メインテクスチャ・色・Metallic・Smoothness など）を引き継いで表示する。描画モードはタグで決まる。
        // 発光やノーマルマップを VRChat が有効にするかどうかは分からないので、プレビューでは無効にする
        static Material CreateStandard(Material processed, TAOFallbackKind kind)
        {
            var shader = Shader.Find(TAOFallbackPlanner.StandardShaderName);
            if (shader == null) return processed;

            TAOFallbackWriter.RemoveUndeclaredTextures(processed);

            Material preview;
            using (TAOFallbackWriter.NoPropertyDrawers()) preview = new Material(processed) { shader = shader };
            foreach (var pass in Passes) preview.SetShaderPassEnabled(pass, true);
            preview.shaderKeywords = Array.Empty<string>();

            var isCutout = kind == TAOFallbackKind.StandardCutout;
            var isFade = kind == TAOFallbackKind.StandardFade;
            var isTransparent = kind == TAOFallbackKind.StandardTransparent;
            var isBlended = isFade || isTransparent;

            // Unity の Standard の Rendering Mode（Opaque・Cutout・Fade・Transparent）と同じ設定
            preview.SetOverrideTag("RenderType", isCutout ? "TransparentCutout" : isBlended ? "Transparent" : "");
            preview.SetFloat("_Mode", isCutout ? 1f : isFade ? 2f : isTransparent ? 3f : 0f);
            preview.SetFloat("_SrcBlend", (float)(isFade
                ? UnityEngine.Rendering.BlendMode.SrcAlpha
                : UnityEngine.Rendering.BlendMode.One));
            preview.SetFloat("_DstBlend", (float)(isBlended
                ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
                : UnityEngine.Rendering.BlendMode.Zero));
            preview.SetFloat("_ZWrite", isBlended ? 0f : 1f);

            if (isCutout) preview.EnableKeyword("_ALPHATEST_ON");
            if (isFade) preview.EnableKeyword("_ALPHABLEND_ON");
            if (isTransparent) preview.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            preview.renderQueue = isCutout ? 2450 : isBlended ? 3000 : -1;
            return preview;
        }

        // Toon・Unlit・Particle・Hidden は、メインテクスチャだけを使う単純なシェーダー
        static Material CreateSimple(Material processed, TAOFallbackKind kind, string tag)
        {
            var shader = Shader.Find(PreviewShader);
            if (shader == null) return processed;

            var preview = new Material(shader) { name = processed.name };
            if (TAOMaterialProps.DeclaresTexture(processed.shader, "_MainTex"))
            {
                preview.SetTexture("_MainTex", processed.GetTexture("_MainTex"));
                preview.SetTextureScale("_MainTex", processed.GetTextureScale("_MainTex"));
                preview.SetTextureOffset("_MainTex", processed.GetTextureOffset("_MainTex"));
            }

            preview.SetFloat("_Cutoff", TAOMaterialProps.Float(processed, "_Cutoff", 0.5f));

            var isLit = kind == TAOFallbackKind.Toon || kind == TAOFallbackKind.ToonCutout || kind == TAOFallbackKind.Matcap;
            var isCutout = kind == TAOFallbackKind.ToonCutout || kind == TAOFallbackKind.UnlitCutout;
            var isBlended = kind == TAOFallbackKind.UnlitTransparent;
            var isMultiply = kind == TAOFallbackKind.Particle;
            var doubleSided = isMultiply || tag.IndexOf("DoubleSided", StringComparison.OrdinalIgnoreCase) >= 0;

            preview.SetFloat("_TAOLit", isLit ? 1f : 0f);
            preview.SetFloat("_TAOCutout", isCutout ? 1f : 0f);
            preview.SetFloat("_TAOMultiply", isMultiply ? 1f : 0f);
            preview.SetFloat("_TAOHidden", kind == TAOFallbackKind.Hidden ? 1f : 0f);
            preview.SetFloat("_TAOCull", doubleSided ? 0f : 2f);
            preview.SetFloat("_TAOZWrite", isBlended || isMultiply ? 0f : 1f);

            // Particle は乗算（Blend Zero SrcColor）、Unlit Transparent は通常のアルファブレンド
            preview.SetFloat("_TAOSrcBlend", isMultiply ? 0f : isBlended ? 5f : 1f);
            preview.SetFloat("_TAODstBlend", isMultiply ? 3f : isBlended ? 10f : 0f);
            preview.renderQueue = isBlended || isMultiply ? 3000 : isCutout ? 2450 : 2000;
            return preview;
        }
    }
}
