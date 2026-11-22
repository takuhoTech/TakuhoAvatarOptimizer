using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>ビルド時の問題を、NDMF のエラーレポートに出す。レポートからは対象のコンポーネントへ移動できる。</summary>
    internal static class TAOBuildReport
    {
        const string Prefix = "tao.merge-physbone.";

        public const string NoFollowers = Prefix + "no-followers";
        public const string NoMain = Prefix + "no-main";
        public const string MainHasNoRotatingBone = Prefix + "main-has-no-rotating-bone";
        public const string MainInsideFollower = Prefix + "main-inside-follower";
        public const string GeneratedParentUnknown = Prefix + "generated-parent-unknown";
        public const string GeneratedInsideFollower = Prefix + "generated-inside-follower";
        public const string NoRotatingBoneToGenerate = Prefix + "no-rotating-bone-to-generate";

        public const string MultipleShaderFallbacks = "tao.shader-fallback.multiple-components";

        static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>();
        static readonly Dictionary<string, string> English = new Dictionary<string, string>();

        static readonly Localizer Localizer = new Localizer("ja-JP", () => new List<(string, Func<string, string>)>
        {
            ("ja-JP", key => Japanese.TryGetValue(key, out var text) ? text : null),
            ("en-US", key => English.TryGetValue(key, out var text) ? text : null),
        });

        static TAOBuildReport()
        {
            Add(NoFollowers,
                "追従PhysBoneがありません",
                "対象のPhysBoneに、本体以外のPhysBoneを追加してください。",
                "There are no follower PhysBones",
                "Add PhysBones other than the main one to the target PhysBones.");
            Add(NoMain,
                "本体のPhysBoneが選択されていません",
                "対象のPhysBoneの左のボタンで、本体を選んでください。",
                "No main PhysBone is selected",
                "Select the main PhysBone with the button on the left of the target PhysBones.");
            Add(MainHasNoRotatingBone,
                "本体のPhysBoneに回転するボーンがないため、スキップしました",
                "{0} には、子を持つボーンがありません。",
                "Skipped because the main PhysBone has no rotating bone",
                "{0} has no bone with a child.");
            Add(MainInsideFollower,
                "本体が追従PhysBoneの配下にあるため、このPhysBoneをスキップしました",
                "本体が {0} の配下にあります。循環を避けるため、変換しません。",
                "Skipped a follower PhysBone because the main one is under it",
                "The main PhysBone is under {0}. It is not converted to avoid a cycle.");
            Add(GeneratedParentUnknown,
                "生成するPhysBoneの親を決められないため、スキップしました",
                "追従PhysBoneのルートの親に共通の祖先がありません。生成先の親を手動で指定してください。",
                "Skipped because the parent of the new PhysBone could not be decided",
                "The roots of the follower PhysBones have no common ancestor. Specify the parent manually.");
            Add(GeneratedInsideFollower,
                "生成するPhysBoneが追従PhysBoneの配下になるため、このPhysBoneを除外しました",
                "{0} は、生成するPhysBoneの親を含むため、変換しません。",
                "Excluded a follower PhysBone because the new PhysBone would be under it",
                "{0} contains the parent of the new PhysBone, so it is not converted.");
            Add(NoRotatingBoneToGenerate,
                "回転するボーンを持つ追従PhysBoneがないため、PhysBoneを生成できませんでした",
                null,
                "Could not generate a PhysBone because no follower PhysBone has a rotating bone",
                null);
            Add(MultipleShaderFallbacks,
                "TAO Optimize VRC Shader Fallback が複数あります",
                "{0} の設定だけを使います。",
                "There are multiple TAO Optimize VRC Shader Fallback components",
                "Only the settings of {0} are used.");
        }

        /// <summary>ビルドは止めずに、NDMF のレポートへ表示する。args の Unity オブジェクトは、レポートから選択できる。</summary>
        public static void Warn(string key, params object[] args)
            => ErrorReport.ReportError(Localizer, ErrorSeverity.NonFatal, key, args);

        static void Add(string key, string japaneseTitle, string japaneseDetails, string englishTitle,
            string englishDetails)
        {
            Japanese[key] = japaneseTitle;
            English[key] = englishTitle;
            if (japaneseDetails != null) Japanese[key + ":description"] = japaneseDetails;
            if (englishDetails != null) English[key + ":description"] = englishDetails;
        }
    }
}
