using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>追従 PhysBone の各ボーンに、本体のボーンへ追従する VRC Rotation Constraint を付ける。</summary>
    internal static class TAOConstraintBuilder
    {
        public sealed class FollowSettings
        {
            /// <summary>追従 PhysBone の、回転するボーンの数。</summary>
            public int FollowerRotatingBoneCount;

            /// <summary>本体のボーン列（先端を含む）。</summary>
            public List<Transform> MainChain;

            public List<Transform> IgnoredTransforms;
            public bool IgnoreFork;

            /// <summary>ワールド空間で解く。追従側の親が本体の親と異なるときに使う。</summary>
            public bool WorldSpace;

            public float Strength;
        }

        /// <summary>
        /// bone 以下の回転するボーンに、Constraint を付ける。
        /// 末端ボーンは回転せず（ウェイトも無く AAO に削除される）、Multi-Child Type が Ignore の分岐ボーンも動かないので、
        /// どちらにも付けず、分岐ボーンでは深さも進めない。
        /// </summary>
        public static void AddConstraints(Transform bone, int depth, FollowSettings follow)
        {
            var children = TAOPhysBoneTopology.ActiveChildren(bone, follow.IgnoredTransforms);
            var rotates = TAOPhysBoneTopology.Rotates(children.Count, follow.IgnoreFork);
            foreach (var child in children) AddConstraints(child, rotates ? depth + 1 : depth, follow);
            if (!rotates) return;

            var sources = SelectSources(depth, follow, out var worldSpace);
            AddRotationConstraint(bone, sources, worldSpace, follow.Strength);
        }

        /// <summary>depth 番目の回転ボーンが追従する、本体のボーンと重み。</summary>
        static List<(Transform Source, float Weight)> SelectSources(int depth, FollowSettings follow,
            out bool worldSpace)
        {
            worldSpace = follow.WorldSpace;
            var chain = follow.MainChain;
            var mainRotatingBoneCount = chain.Count - 1;
            var sources = new List<(Transform Source, float Weight)>();

            // 回転するボーンの数が同じなら、同じ深さのボーンに 1 対 1 で追従する
            if (follow.FollowerRotatingBoneCount == mainRotatingBoneCount)
            {
                sources.Add((chain[Mathf.Min(depth, mainRotatingBoneCount - 1)], 1f));
                return sources;
            }

            // 数が異なるときは、ルートと末端の相対的な角度が一致するように、
            // 追従側の最初の回転ボーンを本体の最初に、最後の回転ボーンを本体の最後に対応させ、
            // 間のボーンは本体の隣り合うボーンのワールド回転を重みで補間して追従する。
            // どちらも本体の回転を元にするので、ボーン数にもルートの親の向きにも依存しない
            worldSpace = true;
            var last = mainRotatingBoneCount - 1;
            var index = Mathf.Min(depth, follow.FollowerRotatingBoneCount - 1);
            var progress = follow.FollowerRotatingBoneCount == 1
                ? last
                : index * last / (float)(follow.FollowerRotatingBoneCount - 1);
            var lower = Mathf.Min(Mathf.FloorToInt(progress + 1e-4f), last);
            var fraction = progress - lower;

            if (fraction < 1e-3f || lower >= last)
            {
                sources.Add((chain[lower], 1f));
            }
            else
            {
                sources.Add((chain[lower], 1f - fraction));
                sources.Add((chain[lower + 1], fraction));
            }

            return sources;
        }

        static void AddRotationConstraint(Transform target, List<(Transform Source, float Weight)> sources,
            bool worldSpace, float strength)
        {
            if (target.GetComponent<VRCRotationConstraint>() != null) return;
            if (sources.Any(s => s.Source == target)) return;

            var constraint = target.gameObject.AddComponent<VRCRotationConstraint>();
            constraint.SolveInLocalSpace = !worldSpace;
            constraint.FreezeToWorld = false;
            constraint.GlobalWeight = strength;
            constraint.IsActive = true;

            // 出力 = ソース * オフセット となるよう、現在の姿勢が維持されるオフセットを計算する
            Quaternion sourceRotation, targetRotation;
            if (worldSpace)
            {
                sourceRotation = sources.Count == 1
                    ? sources[0].Source.rotation
                    : Quaternion.Slerp(sources[0].Source.rotation, sources[1].Source.rotation, sources[1].Weight);
                targetRotation = target.rotation;
            }
            else
            {
                sourceRotation = sources[0].Source.localRotation;
                targetRotation = target.localRotation;
            }

            constraint.RotationOffset = (Quaternion.Inverse(sourceRotation) * targetRotation).eulerAngles;
            constraint.RotationAtRest = targetRotation.eulerAngles;

            foreach (var (source, weight) in sources)
                constraint.Sources.Add(new VRCConstraintSource(source, weight));
            constraint.Locked = true;
        }
    }
}
