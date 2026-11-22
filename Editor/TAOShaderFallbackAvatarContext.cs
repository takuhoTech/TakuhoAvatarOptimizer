using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>メッシュが体のどこに付いているか。</summary>
    internal enum TAOFallbackRegion
    {
        Other,
        Head,

        /// <summary>頭と首を除く Humanoid ボーン（胴体・腕・脚）とその子。</summary>
        Body,
    }

    /// <summary>マテリアルが使われているメッシュのスロット。</summary>
    internal readonly struct TAOFallbackSlot
    {
        public readonly Mesh Mesh;
        public readonly int SubMesh;

        /// <summary>スキンメッシュのボーン。ボーンが無ければ null。</summary>
        public readonly Transform[] Bones;

        /// <summary>ボーンが無いときに、体のどこに付いているかを調べる Transform。</summary>
        public readonly Transform Anchor;

        public TAOFallbackSlot(Mesh mesh, int subMesh, Transform[] bones, Transform anchor)
        {
            Mesh = mesh;
            SubMesh = subMesh;
            Bones = bones;
            Anchor = anchor;
        }
    }

    /// <summary>
    /// アバターの中でマテリアルがどう使われているか。
    /// ビルド・シーンプレビュー・インスペクターで、同じ判定を使えるようにまとめる。
    /// </summary>
    internal sealed class TAOFallbackAvatarContext
    {
        static readonly List<TAOFallbackSlot> NoSlots = new List<TAOFallbackSlot>();

        readonly Transform _avatarRoot;
        readonly List<Material> _materials = new List<Material>();
        readonly Dictionary<Material, List<TAOFallbackSlot>> _slots = new Dictionary<Material, List<TAOFallbackSlot>>();
        readonly HashSet<Material> _surfaceMaterials = new HashSet<Material>();
        readonly HashSet<Material> _effectMaterials = new HashSet<Material>();

        // マテリアルを表示するレンダラー。アニメーションで差し替えるマテリアルは、差し替え先を確実には特定できないので _anywhere に入れる
        readonly Dictionary<Material, HashSet<Renderer>> _renderers = new Dictionary<Material, HashSet<Renderer>>();
        readonly HashSet<Material> _anywhere = new HashSet<Material>();

        // アニメーションで変化するマテリアルのプロパティ名。対象のレンダラーを特定できないものは _animatedEverywhere に入れる
        readonly Dictionary<Renderer, HashSet<string>> _animated = new Dictionary<Renderer, HashSet<string>>();
        readonly HashSet<string> _animatedEverywhere = new HashSet<string>();

        // Humanoid ボーンの部位
        readonly Dictionary<Transform, TAOFallbackRegion> _humanoid = new Dictionary<Transform, TAOFallbackRegion>();

        // 衣装のボーンは、ビルド前はアバターのボーンの子になっていないので、ボーンの名前でも部位を調べる
        readonly List<KeyValuePair<string, TAOFallbackRegion>> _humanoidNames =
            new List<KeyValuePair<string, TAOFallbackRegion>>();

        readonly Dictionary<Transform, TAOFallbackRegion> _regionCache = new Dictionary<Transform, TAOFallbackRegion>();

        public TAOFallbackAvatarContext(GameObject avatarRoot)
        {
            _avatarRoot = avatarRoot != null ? avatarRoot.transform : null;
            if (avatarRoot != null) ReadHumanoid(avatarRoot);
        }

        /// <summary>見つかった順のマテリアル。</summary>
        public IReadOnlyList<Material> Materials => _materials;

        /// <summary>顔のメッシュ（リップシンクやまばたきのメッシュ）で使われているマテリアル。</summary>
        public HashSet<Material> FaceMaterials { get; } = new HashSet<Material>();

        /// <summary>アニメーションで変化するマテリアルのプロパティ名（アバター全体）。</summary>
        public IEnumerable<string> AnimatedProperties
            => _animatedEverywhere.Concat(_animated.Values.SelectMany(names => names)).Distinct();

        /// <summary>ほかのツールがビルド中に複製したマテリアルから、複製元を求める。</summary>
        public Func<Material, Material> ResolveOriginal { get; set; } = material => material;

        public IReadOnlyList<TAOFallbackSlot> SlotsOf(Material material)
            => _slots.TryGetValue(material, out var slots) ? slots : NoSlots;

        /// <summary>
        /// アニメーションで変化するマテリアルのプロパティを登録する。
        /// マテリアルのプロパティのアニメーションは、対象のレンダラーのすべてのマテリアルに掛かる。
        /// 対象のレンダラーを特定できなければ、renderer は null。
        /// </summary>
        public void AddAnimatedProperty(string name, Renderer renderer)
        {
            if (renderer == null)
            {
                _animatedEverywhere.Add(name);
                return;
            }

            if (!_animated.TryGetValue(renderer, out var names)) _animated[renderer] = names = new HashSet<string>();
            names.Add(name);
        }

        /// <summary>
        /// マテリアルの表示に影響するプロパティが、アニメーションで変化するか。
        /// アニメーションで差し替えるマテリアルと、表示するレンダラーが分からないマテリアルは、
        /// アバターのすべてのアニメーションの影響を受けるものとして扱う。
        /// </summary>
        public bool IsAnimated(Material material, Func<string, bool> matches)
        {
            if (_animatedEverywhere.Any(matches)) return true;

            if (_anywhere.Contains(material) || !_renderers.TryGetValue(material, out var renderers))
                return _animated.Values.Any(names => names.Any(matches));

            foreach (var renderer in renderers)
                if (_animated.TryGetValue(renderer, out var names) && names.Any(matches)) return true;
            return false;
        }

        /// <summary>パーティクルやトレイルなど、メッシュ以外のレンダラーだけで使われているか。</summary>
        public bool IsEffectOnly(Material material)
            => _effectMaterials.Contains(material) && !_surfaceMaterials.Contains(material);

        public bool IsFace(Material material)
        {
            if (FaceMaterials.Contains(material)) return true;
            var original = ResolveOriginal(material);
            return original != null && FaceMaterials.Contains(original);
        }

        public Material OriginalOf(Material material) => ResolveOriginal(material);

        public void AddRenderer(Renderer renderer) => AddRenderer(renderer, renderer);

        /// <summary>
        /// source は元のレンダラー、displayed は実際に表示されるレンダラー（シーンプレビューではプロキシ）。
        /// マテリアルとメッシュは displayed から、ボーンは source から取る。
        /// </summary>
        public void AddRenderer(Renderer source, Renderer displayed)
        {
            var materials = displayed.sharedMaterials;
            var isSurface = source is SkinnedMeshRenderer || source is MeshRenderer;
            var mesh = isSurface ? MeshOf(displayed) : null;

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;

                Register(material);
                AddUser(material, source);
                if (isSurface)
                {
                    _surfaceMaterials.Add(material);
                    AddSlot(material, source, mesh, i);
                }
                else
                {
                    _effectMaterials.Add(material);
                }
            }
        }

        /// <summary>アニメーションでスロットに入るマテリアル。レンダラーが分からなければ renderer は null。</summary>
        public void AddAnimatedMaterial(Material material, Renderer renderer, int slotIndex)
        {
            if (material == null) return;
            Register(material);

            // アニメーションのパスからは、差し替え先のレンダラーを確実には特定できない
            _anywhere.Add(material);
            if (renderer == null) return;

            if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer)
            {
                _surfaceMaterials.Add(material);
                AddSlot(material, renderer, MeshOf(renderer), slotIndex);
            }
            else
            {
                _effectMaterials.Add(material);
            }
        }

        /// <summary>使われる場所が分からないマテリアル（ほかのコンポーネントが参照しているものなど）。</summary>
        public void AddMaterial(Material material)
        {
            if (material != null) Register(material);
        }

        void Register(Material material)
        {
            if (_slots.ContainsKey(material)) return;
            _slots[material] = new List<TAOFallbackSlot>();
            _materials.Add(material);
        }

        void AddUser(Material material, Renderer renderer)
        {
            if (!_renderers.TryGetValue(material, out var renderers))
                _renderers[material] = renderers = new HashSet<Renderer>();
            renderers.Add(renderer);
        }

        void AddSlot(Material material, Renderer source, Mesh mesh, int slotIndex)
        {
            if (mesh == null || mesh.subMeshCount == 0) return;

            // サブメッシュより多いスロットは、最後のサブメッシュをもう一度描画する
            var subMesh = Mathf.Clamp(slotIndex, 0, mesh.subMeshCount - 1);
            Transform[] bones = null;
            var anchor = source.transform;
            if (source is SkinnedMeshRenderer skinned)
            {
                var skinnedBones = skinned.bones;
                if (skinnedBones != null && skinnedBones.Length > 0) bones = skinnedBones;
                if (skinned.rootBone != null) anchor = skinned.rootBone;
            }

            _slots[material].Add(new TAOFallbackSlot(mesh, subMesh, bones, anchor));
        }

        /// <summary>
        /// Transform が体のどこに付いているか。Humanoid ボーンの子ならその部位、
        /// そうでなければ、Humanoid ボーンと同じ名前を含む親（衣装のボーン）の部位。
        /// </summary>
        public TAOFallbackRegion RegionOf(Transform transform)
        {
            if (transform == null) return TAOFallbackRegion.Other;
            if (_regionCache.TryGetValue(transform, out var region)) return region;

            if (!TryFindInParents(transform, current => _humanoid.TryGetValue(current, out var found)
                    ? found
                    : (TAOFallbackRegion?)null, out region))
            {
                TryFindInParents(transform, current => RegionByName(current.name), out region);
            }

            _regionCache[transform] = region;
            return region;
        }

        bool TryFindInParents(Transform transform, Func<Transform, TAOFallbackRegion?> find, out TAOFallbackRegion region)
        {
            for (var current = transform; current != null && current != _avatarRoot; current = current.parent)
            {
                var found = find(current);
                if (!found.HasValue) continue;
                region = found.Value;
                return true;
            }

            region = TAOFallbackRegion.Other;
            return false;
        }

        TAOFallbackRegion? RegionByName(string name)
        {
            foreach (var entry in _humanoidNames)
                if (name.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase) >= 0) return entry.Value;
            return null;
        }

        void ReadHumanoid(GameObject avatarRoot)
        {
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return;

            try
            {
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                {
                    var transform = animator.GetBoneTransform(bone);
                    if (transform != null) AddHumanoidBone(transform, bone);
                }
            }
            catch (Exception)
            {
                // Animator が初期化されていない場合は、下の名前による対応づけを使う
            }

            if (_humanoid.Count == 0) ReadHumanoidByName(avatarRoot, animator.avatar);

            // 長い名前から調べて、"Chest" より "UpperChest" を先に一致させる
            _humanoidNames.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        void ReadHumanoidByName(GameObject avatarRoot, Avatar avatar)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var transform in avatarRoot.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(transform.name)) byName[transform.name] = transform;

            foreach (var human in avatar.humanDescription.human)
            {
                var index = Array.IndexOf(HumanTrait.BoneName, human.humanName);
                if (index < 0 || !byName.TryGetValue(human.boneName, out var transform)) continue;
                AddHumanoidBone(transform, (HumanBodyBones)index);
            }
        }

        void AddHumanoidBone(Transform transform, HumanBodyBones bone)
        {
            var region = IsHeadBone(bone) ? TAOFallbackRegion.Head : TAOFallbackRegion.Body;
            _humanoid[transform] = region;

            // 短すぎる名前は、関係のないボーンにも一致してしまうので使わない
            if (transform.name.Length >= 3)
                _humanoidNames.Add(new KeyValuePair<string, TAOFallbackRegion>(transform.name, region));
        }

        static bool IsHeadBone(HumanBodyBones bone)
            => bone == HumanBodyBones.Neck || bone == HumanBodyBones.Head || bone == HumanBodyBones.LeftEye ||
               bone == HumanBodyBones.RightEye || bone == HumanBodyBones.Jaw;

        public static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            return renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
        }

        /// <summary>自身か親が EditorOnly のオブジェクトは、ビルドに含まれない。</summary>
        public static bool IsEditorOnly(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
                if (current.CompareTag("EditorOnly")) return true;
            return false;
        }

        /// <summary>リップシンクとまばたきに使うメッシュのマテリアルを集める。</summary>
        public static void CollectFaceMaterials(GameObject avatarRoot, HashSet<Material> into)
        {
            if (!avatarRoot.TryGetComponent<VRCAvatarDescriptor>(out var descriptor)) return;

            AddMaterialsOf(descriptor.VisemeSkinnedMesh, into);
            AddMaterialsOf(descriptor.customEyeLookSettings.eyelidsSkinnedMesh, into);
        }

        static void AddMaterialsOf(Renderer renderer, HashSet<Material> into)
        {
            if (renderer == null || IsEditorOnly(renderer.transform)) return;
            foreach (var material in renderer.sharedMaterials)
                if (material != null) into.Add(material);
        }

        /// <summary>"material._Color.r" のようなアニメーションのプロパティ名から、マテリアルのプロパティ名を取り出す。</summary>
        public static bool TryGetMaterialPropertyName(string animatedProperty, out string name)
        {
            const string prefix = "material.";
            name = null;
            if (animatedProperty == null || !animatedProperty.StartsWith(prefix)) return false;

            name = animatedProperty.Substring(prefix.Length);
            var dot = name.LastIndexOf('.');
            if (dot == name.Length - 2) name = name.Substring(0, dot);
            return name.Length > 0;
        }

        /// <summary>"m_Materials.Array.data[2]" のようなプロパティ名から、マテリアルのスロットの番号を取り出す。</summary>
        public static bool TryGetMaterialSlot(string property, out int slot)
        {
            const string prefix = "m_Materials.Array.data[";
            slot = 0;
            return property != null && property.StartsWith(prefix) && property.EndsWith("]") &&
                   int.TryParse(property.Substring(prefix.Length, property.Length - prefix.Length - 1), out slot);
        }

        /// <summary>
        /// シーン上のアバターから集める（インスペクターとシーンプレビュー用）。
        /// レンダラーのマテリアルに加えて、アニメーションやほかのコンポーネントが参照しているマテリアルも対象にする。
        /// displayed は、元のレンダラーから、実際に表示されるレンダラー（シーンプレビューのプロキシ）を求める。
        /// </summary>
        public static TAOFallbackAvatarContext FromScene(GameObject avatarRoot,
            Func<Renderer, Renderer> displayed = null)
        {
            var context = new TAOFallbackAvatarContext(avatarRoot);
            CollectFaceMaterials(avatarRoot, context.FaceMaterials);

            foreach (var renderer in avatarRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsEditorOnly(renderer.transform)) continue;
                context.AddRenderer(renderer, displayed != null ? displayed(renderer) : renderer);
            }

            context.ScanReferences(avatarRoot);
            return context;
        }

        /// <summary>アニメーションと、MonoBehaviour が参照しているマテリアルを集める。</summary>
        public void ScanReferences(GameObject avatarRoot)
        {
            var clips = new HashSet<AnimationClip>();
            var owners = new Dictionary<AnimationClip, Transform>();

            void AddClips(RuntimeAnimatorController controller, Transform owner)
            {
                if (controller == null) return;
                foreach (var clip in controller.animationClips)
                    if (clip != null && clips.Add(clip)) owners[clip] = owner;
            }

            foreach (var component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                // スクリプトが見つからないコンポーネントは null になる
                if (component == null || IsEditorOnly(component.transform)) continue;

                if (component is Animator animator)
                {
                    AddClips(animator.runtimeAnimatorController, animator.transform);
                    continue;
                }

                if (!(component is MonoBehaviour) || component is TAOOptimizeVRCShaderFallback) continue;

                // PhysBone や Constraint は数が多く、マテリアルを参照しない
                var typeNamespace = component.GetType().Namespace;
                if (typeNamespace != null && typeNamespace.StartsWith("VRC.") && typeNamespace.Contains("Dynamics"))
                    continue;

                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    var enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = property.propertyType == SerializedPropertyType.Generic ||
                                        property.propertyType == SerializedPropertyType.ManagedReference;
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                        var reference = property.objectReferenceValue;
                        if (reference is Material material) AddMaterial(material);
                        else if (reference is RuntimeAnimatorController controller) AddClips(controller, component.transform);
                        else if (reference is AnimationClip clip && clips.Add(clip)) owners[clip] = component.transform;
                    }
                }
            }

            foreach (var clip in clips) ScanClip(clip, owners[clip], avatarRoot.transform);
        }

        void ScanClip(AnimationClip clip, Transform owner, Transform avatarRoot)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!TryGetMaterialPropertyName(binding.propertyName, out var name)) continue;

                // パスの基準は、コントローラーを持つオブジェクトかアバターのルート。どちらか分からないので、両方を対象にする
                var found = false;
                foreach (var target in new[] { FindTarget(owner, binding.path), FindTarget(avatarRoot, binding.path) })
                {
                    if (target == null || !target.TryGetComponent<Renderer>(out var animated)) continue;
                    AddAnimatedProperty(name, animated);
                    found = true;
                }

                if (!found) AddAnimatedProperty(name, null);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                Renderer renderer = null;
                var hasSlot = TryGetMaterialSlot(binding.propertyName, out var slot);

                foreach (var keyframe in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                {
                    if (!(keyframe.value is Material material)) continue;

                    if (hasSlot && renderer == null)
                    {
                        var target = FindTarget(owner, binding.path);
                        if (target == null) target = FindTarget(avatarRoot, binding.path);
                        if (target != null && !IsEditorOnly(target)) target.TryGetComponent(out renderer);
                    }

                    AddAnimatedMaterial(material, hasSlot ? renderer : null, slot);
                }
            }
        }

        static Transform FindTarget(Transform from, string path)
            => string.IsNullOrEmpty(path) ? from : from.Find(path);
    }
}
