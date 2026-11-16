using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Dynamics;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>変換による変化を、VRChat のパフォーマンス統計の名称で数える。</summary>
    internal static class TAOPerformanceStats
    {
        public readonly struct Entry
        {
            public string Label { get; }
            public int Delta { get; }

            public Entry(string label, int delta)
            {
                Label = label;
                Delta = delta;
            }
        }

        /// <summary>変化のある項目だけを返す。</summary>
        public static List<Entry> Calculate(TAOMergePhysBoneByConstraint component, TAOInspectorCache cache)
        {
            var followers = cache.Followers;
            var main = component.mainSource == TAOMainSource.Existing ? component.mainPhysBone : null;
            var all = main != null ? followers.Append(main).ToList() : followers;
            if (all.Count == 0) return new List<Entry>();

            var generatedBoneCount = component.mainSource == TAOMainSource.Generated ? cache.GeneratedBoneCount : 0;
            var transformsBefore = all.Sum(AffectedTransforms);
            var transformsAfter = main != null ? AffectedTransforms(main) : generatedBoneCount;

            var checksBefore = all.Sum(p => AffectedTransforms(p) * ColliderCount(p));
            var checksAfter = transformsAfter * cache.CollidersAfter;

            var entries = new List<Entry>
            {
                new Entry("PhysBone Components", 1 - all.Count),
                new Entry("PhysBone Transforms", transformsAfter - transformsBefore),
                new Entry("PhysBone Collision Check Count", checksAfter - checksBefore),
                new Entry("Constraint Count", followers.Sum(ConstraintCount)),
                new Entry("Constraint Depth", followers.Count == 0 ? 0 : followers.Max(ConstraintDepth)),
            };
            return entries.Where(e => e.Delta != 0).ToList();
        }

        static int ColliderCount(VRCPhysBoneBase physBone)
            => physBone.colliders == null ? 0 : physBone.colliders.Count(c => c != null);

        /// <summary>PhysBone が動かす Transform の数（ルートを含み、無視された Transform の配下を除く）。</summary>
        static int AffectedTransforms(VRCPhysBoneBase physBone)
            => AffectedTransforms(TAOPhysBoneTopology.RootOf(physBone), physBone.ignoreTransforms);

        static int AffectedTransforms(Transform transform, List<Transform> ignored)
            => 1 + TAOPhysBoneTopology.ActiveChildren(transform, ignored).Sum(child => AffectedTransforms(child, ignored));

        /// <summary>追加される Rotation Constraint の数（回転するボーンの数）。</summary>
        static int ConstraintCount(VRCPhysBoneBase physBone)
            => ConstraintCount(TAOPhysBoneTopology.RootOf(physBone), physBone.ignoreTransforms,
                TAOPhysBoneTopology.IgnoresFork(physBone));

        static int ConstraintCount(Transform transform, List<Transform> ignored, bool ignoreFork)
        {
            var children = TAOPhysBoneTopology.ActiveChildren(transform, ignored);
            var own = TAOPhysBoneTopology.Rotates(children.Count, ignoreFork) ? 1 : 0;
            return own + children.Sum(child => ConstraintCount(child, ignored, ignoreFork));
        }

        /// <summary>回転するボーンの経路上で、Constraint が付くボーンの数。分岐があるときは最も深い枝で数える。</summary>
        static int ConstraintDepth(VRCPhysBoneBase physBone)
            => ConstraintDepth(TAOPhysBoneTopology.RootOf(physBone), physBone.ignoreTransforms,
                TAOPhysBoneTopology.IgnoresFork(physBone));

        static int ConstraintDepth(Transform transform, List<Transform> ignored, bool ignoreFork)
        {
            var children = TAOPhysBoneTopology.ActiveChildren(transform, ignored);
            var deepest = children.Count == 0 ? 0 : children.Max(child => ConstraintDepth(child, ignored, ignoreFork));
            return TAOPhysBoneTopology.Rotates(children.Count, ignoreFork) ? deepest + 1 : deepest;
        }
    }
}
