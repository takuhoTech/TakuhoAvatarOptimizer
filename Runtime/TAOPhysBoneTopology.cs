using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Dynamics;

namespace TakuhoTech.TAO
{
    /// <summary>PhysBone が動かす Transform の構造（回転するボーンの経路など）を調べる。</summary>
    public static class TAOPhysBoneTopology
    {
        public static Transform RootOf(VRCPhysBoneBase physBone)
            => physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;

        public static bool IgnoresFork(VRCPhysBoneBase physBone)
            => physBone.multiChildType == VRCPhysBoneBase.MultiChildType.Ignore;

        /// <summary>Ignore Transforms に含まれない子。</summary>
        public static List<Transform> ActiveChildren(Transform transform, List<Transform> ignored)
        {
            var children = new List<Transform>();
            foreach (Transform child in transform)
                if (ignored == null || !ignored.Contains(child)) children.Add(child);
            return children;
        }

        /// <summary>
        /// 子へ向く形で回転するボーンか。末端ボーンは回転せず、Multi-Child Type が Ignore の分岐ボーンも動かない。
        /// </summary>
        public static bool Rotates(int activeChildCount, bool ignoreFork)
            => activeChildCount > 0 && !(ignoreFork && activeChildCount >= 2);

        /// <summary>
        /// 回転するボーンの経路（先端のボーンを含む）。
        /// Multi-Child Type が Ignore の分岐は最も長い枝を選び、それ以外は最初の子を辿る。
        /// </summary>
        public static List<Transform> BoneChain(VRCPhysBoneBase physBone)
            => LongestChain(RootOf(physBone), physBone.ignoreTransforms, IgnoresFork(physBone));

        /// <summary>先端のボーンを含むボーン数。</summary>
        public static int BoneCount(VRCPhysBoneBase physBone) => BoneChain(physBone).Count;

        /// <summary>回転するボーンの数（先端を除く）。</summary>
        public static int RotatingBoneCount(VRCPhysBoneBase physBone) => Mathf.Max(1, BoneCount(physBone) - 1);

        /// <summary>先端を含むボーン数の平均（四捨五入）。最小は 2。</summary>
        public static int AverageBoneCount(IEnumerable<VRCPhysBoneBase> physBones)
        {
            var counts = physBones.Where(p => p != null).Select(BoneCount).ToList();
            return counts.Count == 0 ? 2 : Mathf.Max(2, Mathf.RoundToInt((float)counts.Average()));
        }

        /// <summary>各 PhysBone のルートの親の共通祖先。無ければ null。</summary>
        public static Transform CommonParent(IEnumerable<VRCPhysBoneBase> physBones)
        {
            Transform common = null;
            foreach (var physBone in physBones)
            {
                if (physBone == null) continue;
                var parent = RootOf(physBone).parent;
                if (parent == null) return null;
                if (common == null)
                {
                    common = parent;
                    continue;
                }

                while (common != null && !parent.IsChildOf(common)) common = common.parent;
                if (common == null) return null;
            }

            return common;
        }

        static List<Transform> LongestChain(Transform transform, List<Transform> ignored, bool ignoreFork)
        {
            var children = ActiveChildren(transform, ignored);
            if (children.Count == 0) return new List<Transform> { transform };

            if (ignoreFork && children.Count >= 2)
            {
                return children
                    .Select(child => LongestChain(child, ignored, ignoreFork))
                    .OrderByDescending(chain => chain.Count)
                    .First();
            }

            var chain = LongestChain(children[0], ignored, ignoreFork);
            chain.Insert(0, transform);
            return chain;
        }
    }
}
