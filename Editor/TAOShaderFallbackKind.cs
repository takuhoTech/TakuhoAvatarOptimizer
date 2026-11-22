using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>マテリアルに設定するフォールバックの種類。</summary>
    internal enum TAOFallbackKind
    {
        ToonStandard,
        ToonStandardOutline,
        Toon,
        ToonCutout,
        Unlit,
        UnlitCutout,
        UnlitTransparent,

        /// <summary>Unity の Standard シェーダー。光と、マテリアルの色・不透明度が反映される。</summary>
        Standard,
        StandardCutout,

        /// <summary>Standard の Fade。光の影響を受けるアルファブレンド。</summary>
        StandardFade,

        /// <summary>Standard の Transparent。乗算済みアルファ（ガラスのように、反射は薄くならない）。</summary>
        StandardTransparent,
        Matcap,
        Particle,
        Hidden,

        /// <summary>VRCFallback タグを文字列で直接指定したもの。</summary>
        Custom,

        /// <summary>マテリアルを変更しない。</summary>
        Unchanged,
    }

    /// <summary>日本語と英語の組。表示するときに、選択中の言語の文字列を取り出す。</summary>
    internal readonly struct TAOText
    {
        public readonly string Ja;
        public readonly string En;

        public TAOText(string ja, string en)
        {
            Ja = ja;
            En = en;
        }

        public string Text => T(Ja, En);
    }

    internal static class TAOFallbackKinds
    {
        public const string TagName = "VRCFallback";

        /// <summary>インスペクターで結果を並べる順。</summary>
        public static readonly TAOFallbackKind[] DisplayOrder =
        {
            TAOFallbackKind.ToonStandard,
            TAOFallbackKind.ToonStandardOutline,
            TAOFallbackKind.Toon,
            TAOFallbackKind.ToonCutout,
            TAOFallbackKind.Unlit,
            TAOFallbackKind.UnlitCutout,
            TAOFallbackKind.UnlitTransparent,
            TAOFallbackKind.Standard,
            TAOFallbackKind.StandardCutout,
            TAOFallbackKind.StandardFade,
            TAOFallbackKind.StandardTransparent,
            TAOFallbackKind.Matcap,
            TAOFallbackKind.Particle,
            TAOFallbackKind.Hidden,
            TAOFallbackKind.Custom,
            TAOFallbackKind.Unchanged,
        };

        public static bool IsToonStandard(TAOFallbackKind kind)
            => kind == TAOFallbackKind.ToonStandard || kind == TAOFallbackKind.ToonStandardOutline;

        public static bool IsStandard(TAOFallbackKind kind)
            => kind == TAOFallbackKind.Standard || kind == TAOFallbackKind.StandardCutout ||
               kind == TAOFallbackKind.StandardFade || kind == TAOFallbackKind.StandardTransparent;

        /// <summary>光の影響を受けないフォールバック先。光の影響を受けるものと混ぜると、暗い場所で光って見える。</summary>
        public static bool IsUnlit(TAOFallbackKind kind)
            => kind == TAOFallbackKind.Unlit || kind == TAOFallbackKind.UnlitCutout ||
               kind == TAOFallbackKind.UnlitTransparent;

        /// <summary>マテリアルの色（_Color）を使うフォールバック先。ほかは、メインテクスチャの色だけを使う。</summary>
        public static bool UsesMaterialColor(TAOFallbackKind kind) => IsToonStandard(kind) || IsStandard(kind);

        /// <summary>メインテクスチャのアルファで、抜いたり透かしたりするフォールバック先。</summary>
        public static bool UsesTextureAlpha(TAOFallbackKind kind)
            => kind == TAOFallbackKind.ToonCutout || kind == TAOFallbackKind.UnlitCutout ||
               kind == TAOFallbackKind.UnlitTransparent || kind == TAOFallbackKind.Particle ||
               kind == TAOFallbackKind.StandardCutout || kind == TAOFallbackKind.StandardFade ||
               kind == TAOFallbackKind.StandardTransparent;

        /// <summary>メインテクスチャを表示するフォールバック先。</summary>
        public static bool UsesMainTexture(TAOFallbackKind kind)
            => kind != TAOFallbackKind.Matcap && kind != TAOFallbackKind.Hidden && kind != TAOFallbackKind.Custom &&
               kind != TAOFallbackKind.Unchanged;

        public static string Label(TAOFallbackKind kind)
        {
            switch (kind)
            {
                case TAOFallbackKind.ToonStandard: return "Toon Standard";
                case TAOFallbackKind.ToonStandardOutline: return "Toon Standard Outline";
                case TAOFallbackKind.Toon: return "Toon";
                case TAOFallbackKind.ToonCutout: return "Toon Cutout";
                case TAOFallbackKind.Unlit: return "Unlit";
                case TAOFallbackKind.UnlitCutout: return "Unlit Cutout";
                case TAOFallbackKind.UnlitTransparent: return "Unlit Transparent";
                case TAOFallbackKind.Standard: return "Standard";
                case TAOFallbackKind.StandardCutout: return "Standard Cutout";
                case TAOFallbackKind.StandardFade: return "Standard Fade";
                case TAOFallbackKind.StandardTransparent: return "Standard Transparent";
                case TAOFallbackKind.Matcap: return "Matcap";
                case TAOFallbackKind.Particle: return "Particle";
                case TAOFallbackKind.Hidden: return "Hidden";
                case TAOFallbackKind.Custom: return T("カスタム", "Custom");
                default: return T("変更なし", "Unchanged");
            }
        }

        /// <summary>
        /// VRCFallback タグ。両面描画（DoubleSided）は Toon だけが対応している。
        /// toonstandard と toonstandardoutline は、ほかのタグと組み合わせられない。
        /// 一覧に無い名前は Standard になるので、Standard は「Standard」と描画モードの名前を組み合わせる
        /// （lilToon や Poiyomi の設定画面が書き込むタグと同じ）。
        /// </summary>
        public static string Tag(TAOFallbackKind kind, bool doubleSided)
        {
            switch (kind)
            {
                case TAOFallbackKind.ToonStandard: return "toonstandard";
                case TAOFallbackKind.ToonStandardOutline: return "toonstandardoutline";
                case TAOFallbackKind.Toon: return doubleSided ? "ToonDoubleSided" : "Toon";
                case TAOFallbackKind.ToonCutout: return doubleSided ? "ToonCutoutDoubleSided" : "ToonCutout";
                case TAOFallbackKind.Unlit: return "Unlit";
                case TAOFallbackKind.UnlitCutout: return "UnlitCutout";
                case TAOFallbackKind.UnlitTransparent: return "UnlitTransparent";
                case TAOFallbackKind.Standard: return "Standard";
                case TAOFallbackKind.StandardCutout: return "StandardCutout";
                case TAOFallbackKind.StandardFade: return "StandardFade";
                case TAOFallbackKind.StandardTransparent: return "StandardTransparent";
                case TAOFallbackKind.Matcap: return "Matcap";
                case TAOFallbackKind.Particle: return "Particle";
                case TAOFallbackKind.Hidden: return "Hidden";
                default: return null;
            }
        }

        public static TAOFallbackKind FromTarget(TAOFallbackTarget target)
        {
            switch (target)
            {
                case TAOFallbackTarget.ToonStandard: return TAOFallbackKind.ToonStandard;
                case TAOFallbackTarget.ToonStandardOutline: return TAOFallbackKind.ToonStandardOutline;
                case TAOFallbackTarget.Toon: return TAOFallbackKind.Toon;
                case TAOFallbackTarget.ToonCutout: return TAOFallbackKind.ToonCutout;
                case TAOFallbackTarget.Unlit: return TAOFallbackKind.Unlit;
                case TAOFallbackTarget.UnlitCutout: return TAOFallbackKind.UnlitCutout;
                case TAOFallbackTarget.UnlitTransparent: return TAOFallbackKind.UnlitTransparent;
                case TAOFallbackTarget.Standard: return TAOFallbackKind.Standard;
                case TAOFallbackTarget.StandardCutout: return TAOFallbackKind.StandardCutout;
                case TAOFallbackTarget.StandardFade: return TAOFallbackKind.StandardFade;
                case TAOFallbackTarget.StandardTransparent: return TAOFallbackKind.StandardTransparent;
                case TAOFallbackTarget.Matcap: return TAOFallbackKind.Matcap;
                case TAOFallbackTarget.Particle: return TAOFallbackKind.Particle;
                case TAOFallbackTarget.Hidden: return TAOFallbackKind.Hidden;
                case TAOFallbackTarget.Custom: return TAOFallbackKind.Custom;
                default: return TAOFallbackKind.Unchanged;
            }
        }

        /// <summary>
        /// タグの文字列から、VRChat が選ぶフォールバックの種類を求める。
        /// 一覧に無い名前は Standard になる。再現できないもの（VertexLit・Sprite）は Custom を返す。
        /// https://creators.vrchat.com/avatars/shader-fallback-system/
        /// </summary>
        public static TAOFallbackKind FromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return TAOFallbackKind.Unchanged;

            var lower = tag.Replace(" ", "").ToLowerInvariant();
            if (lower == "toonstandard") return TAOFallbackKind.ToonStandard;
            if (lower == "toonstandardoutline") return TAOFallbackKind.ToonStandardOutline;
            if (lower.Contains("hidden")) return TAOFallbackKind.Hidden;

            var cutout = lower.Contains("cutout");
            var fade = lower.Contains("fade");
            var blended = fade || lower.Contains("transparent");

            // Toon には半透明が無く、Unlit/Transparent になる
            if (lower.Contains("toon") && !lower.Contains("mobiletoon"))
                return blended ? TAOFallbackKind.UnlitTransparent : cutout ? TAOFallbackKind.ToonCutout : TAOFallbackKind.Toon;
            if (lower.Contains("unlit"))
                return blended ? TAOFallbackKind.UnlitTransparent : cutout ? TAOFallbackKind.UnlitCutout : TAOFallbackKind.Unlit;
            if (lower.Contains("particle")) return TAOFallbackKind.Particle;
            if (lower.Contains("matcap")) return TAOFallbackKind.Matcap;
            if (lower.Contains("mobiletoon")) return TAOFallbackKind.Toon;
            if (lower.Contains("vertexlit") || lower.Contains("sprite")) return TAOFallbackKind.Custom;

            return fade ? TAOFallbackKind.StandardFade
                : blended ? TAOFallbackKind.StandardTransparent
                : cutout ? TAOFallbackKind.StandardCutout
                : TAOFallbackKind.Standard;
        }
    }
}
