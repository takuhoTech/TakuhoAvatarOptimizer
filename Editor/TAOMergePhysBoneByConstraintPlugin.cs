using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using TakuhoTech.TAO;
using TakuhoTech.TAO.Editor;
using UnityEngine;
using VRC.Dynamics;

[assembly: ExportsPlugin(typeof(TAOMergePhysBoneByConstraintPlugin))]

namespace TakuhoTech.TAO.Editor
{
    internal class TAOMergePhysBoneByConstraintPlugin : Plugin<TAOMergePhysBoneByConstraintPlugin>
    {
        public override string QualifiedName => "com.takuho.tao.merge-physbone-by-constraint";
        public override string DisplayName => "TAO Merge PhysBone By Constraint";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Merge PhysBones By Rotation Constraints", ctx =>
                {
                    var components = ctx.AvatarRootObject
                        .GetComponentsInChildren<TAOMergePhysBoneByConstraint>(true);

                    foreach (var component in components)
                    {
                        var current = component;
                        ErrorReport.WithContextObject(current, () => Merge(current));
                        Object.DestroyImmediate(current);
                    }
                });
        }

        /// <summary>追従 PhysBone が従う本体のボーン。</summary>
        sealed class MainBones
        {
            /// <summary>ボーン列（先端を含む）。</summary>
            public readonly List<Transform> Chain;

            public readonly Transform ParentOfRoot;

            /// <summary>既存の PhysBone を本体にする場合のルート。生成する場合は null。</summary>
            public readonly Transform ExistingRoot;

            public MainBones(List<Transform> chain, Transform parentOfRoot, Transform existingRoot)
            {
                Chain = chain;
                ParentOfRoot = parentOfRoot;
                ExistingRoot = existingRoot;
            }
        }

        static void Merge(TAOMergePhysBoneByConstraint component)
        {
            var followers = component.GetFollowers();
            if (followers.Count == 0)
            {
                TAOBuildReport.Warn(TAOBuildReport.NoFollowers);
                return;
            }

            var main = component.mainSource == TAOMainSource.Generated
                ? PrepareGeneratedMain(component, followers)
                : PrepareExistingMain(component);
            if (main == null) return;

            foreach (var follower in followers)
            {
                var root = TAOPhysBoneTopology.RootOf(follower);

                // 本体が追従PhysBoneの配下にあると循環するためスキップする
                if (main.ExistingRoot != null && main.ExistingRoot.IsChildOf(root))
                {
                    TAOBuildReport.Warn(TAOBuildReport.MainInsideFollower, follower);
                    continue;
                }

                var follow = new TAOConstraintBuilder.FollowSettings
                {
                    FollowerRotatingBoneCount = TAOPhysBoneTopology.RotatingBoneCount(follower),
                    MainChain = main.Chain,
                    IgnoredTransforms = follower.ignoreTransforms,
                    IgnoreFork = TAOPhysBoneTopology.IgnoresFork(follower),
                    WorldSpace = root.parent != main.ParentOfRoot,
                    Strength = component.followStrength,
                };
                TAOConstraintBuilder.AddConstraints(root, 0, follow);
                Object.DestroyImmediate(follower);
            }
        }

        static MainBones PrepareExistingMain(TAOMergePhysBoneByConstraint component)
        {
            var main = component.mainPhysBone;
            if (main == null)
            {
                TAOBuildReport.Warn(TAOBuildReport.NoMain);
                return null;
            }

            var chain = TAOPhysBoneTopology.BoneChain(main);
            if (chain.Count < 2)
            {
                TAOBuildReport.Warn(TAOBuildReport.MainHasNoRotatingBone, main);
                return null;
            }

            ApplyMainSettings(component, main);
            var root = TAOPhysBoneTopology.RootOf(main);
            return new MainBones(chain, root.parent, root);
        }

        /// <summary>
        /// 新しい PhysBone を生成して本体にする。生成する PhysBone が追従 PhysBone の配下になる場合は、
        /// 循環を避けるため、その追従 PhysBone を followers から外す。
        /// </summary>
        static MainBones PrepareGeneratedMain(TAOMergePhysBoneByConstraint component,
            List<VRCPhysBoneBase> followers)
        {
            Transform parent;
            while (true)
            {
                parent = component.ResolveGeneratedParent(followers);
                if (parent == null)
                {
                    TAOBuildReport.Warn(TAOBuildReport.GeneratedParentUnknown);
                    return null;
                }

                var nested = followers
                    .Where(f => parent.IsChildOf(TAOPhysBoneTopology.RootOf(f)))
                    .ToList();
                if (nested.Count == 0) break;

                foreach (var follower in nested) TAOBuildReport.Warn(TAOBuildReport.GeneratedInsideFollower, follower);
                followers.RemoveAll(nested.Contains);
                if (followers.Count == 0) return null;
            }

            var chain = TAOGeneratedPhysBoneBuilder.Build(component, followers, parent);
            if (chain == null)
            {
                TAOBuildReport.Warn(TAOBuildReport.NoRotatingBoneToGenerate);
                return null;
            }

            return new MainBones(chain, parent, null);
        }

        /// <summary>設定の取得元が自動生成なら、まとめた設定を本体に書き込み、そのあとで手動の項目を書き込む。</summary>
        static void ApplyMainSettings(TAOMergePhysBoneByConstraint component, VRCPhysBoneBase main)
        {
            if (component.EffectiveSettingsSource() == TAOSettingsSource.Generate)
            {
                TAOPhysBoneSettings
                    .Combine(component.GetSettingsSources(), component.MainRotatingBoneCount())
                    .ApplyTo(main);
            }

            component.settings.ApplyTo(main);
        }
    }
}
