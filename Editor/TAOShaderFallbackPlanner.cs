using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>Toon Standard の Shadow Ramp の作り方。プリセットか、lilToon の影の設定から生成したもの。</summary>
    internal sealed class TAOFallbackRampSpec
    {
        /// <summary>
        /// 直接光に対する環境光の比の想定。
        /// lilToon は影の色を光に掛けるが、Toon Standard は影の色に環境光を足すので、同じ影の色では影が薄くなる。
        /// 環境光がこの比で当たっているときに、影の濃さが lilToon と同じになるように、影の色を濃くする。
        /// </summary>
        public const float DefaultAmbient = 0.75f;

        public bool Generated;

        /// <summary>生成しないときに使う、Toon Standard の影のプリセット。</summary>
        public TAOFallbackShadow Preset = TAOFallbackShadow.Flat;

        // 以下は生成するときの値。色はリニアで、2 影と 3 影のアルファは影の濃さ
        public float Strength = 1f;
        public Color Color1 = Color.white;
        public Color Color2 = Color.white;
        public Color Color3 = Color.white;
        public float Border1, Blur1, Border2, Blur2, Border3, Blur3;
        public Color BorderColor = Color.black;
        public float BorderRange;

        /// <summary>lilToon の「コントラスト」。影の色にメインカラーを掛ける割合。</summary>
        public float MainStrength;

        public float Ambient = DefaultAmbient;

        public static TAOFallbackRampSpec FromPreset(TAOFallbackShadow preset)
            => new TAOFallbackRampSpec { Preset = preset };

        /// <summary>同じ内容の Ramp を 1 枚にまとめるためのキー。</summary>
        public string Key
        {
            get
            {
                if (!Generated) return "preset:" + Preset;
                return string.Join("|", new[]
                {
                    Strength, Color1.r, Color1.g, Color1.b, Color2.r, Color2.g, Color2.b, Color2.a,
                    Color3.r, Color3.g, Color3.b, Color3.a, Border1, Blur1, Border2, Blur2, Border3, Blur3,
                    BorderColor.r, BorderColor.g, BorderColor.b, BorderRange, MainStrength, Ambient,
                }.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// Toon Standard の Shadow Tint。lilToon の「コントラスト」で影に掛かるメインカラーを、
        /// Toon Standard の「影の色にメインカラーを掛ける」設定で再現する。
        /// </summary>
        public float ShadowAlbedo
        {
            get
            {
                if (!Generated) return 0.5f;
                if (MainStrength <= 0f) return 0f;
                var shade = Mathf.Clamp01((Color1.r + Color1.g + Color1.b) / 3f);
                var rest = 1f - shade * (1f - MainStrength);
                return rest > 1e-4f ? Mathf.Clamp01(shade * MainStrength / rest) : 1f;
            }
        }

        /// <summary>
        /// 光の当たり具合 x（0: 光の反対側, 1: 光の正面）での明るさ。リニアの値。
        /// lilToon の影の計算（lilGetShading）のうち、メインカラーに依存しない部分を取り出したもの。
        /// </summary>
        public Color Evaluate(float x)
        {
            var shadow1 = Tooning(x, Border1, Blur1, 0f);
            var shadow2 = Tooning(x, Border2, Blur2, 0f);
            var shadow3 = Tooning(x, Border3, Blur3, 0f);
            var border = Tooning(x, Border1, Blur1, BorderRange);

            var lit = Mathf.Lerp(1f, shadow1, Strength);
            var blend2 = Color2.a * (1f - shadow2);
            var blend3 = Color3.a * (1f - shadow3);

            var result = Color.white;
            for (var channel = 0; channel < 3; channel++)
            {
                var shade = Mathf.Lerp(Mathf.Lerp(Color1[channel], Color2[channel], blend2), Color3[channel], blend3);
                shade = Mathf.Min(shade * (1f - MainStrength), 1f);

                // 影の境界の色（グラデーション）
                var borderBlend = Mathf.Clamp01(border * BorderColor[channel]);
                shade = Mathf.Lerp(shade, 1f, borderBlend);

                var value = Mathf.Lerp(shade, 1f, lit);
                result[channel] = Mathf.Clamp01(value * (1f + Ambient) - Ambient);
            }

            return result;
        }

        // lilToon の lilTooningScale（アンチエイリアスを除く）
        static float Tooning(float value, float border, float blur, float borderRange)
        {
            var min = Mathf.Clamp01(border - blur * 0.5f - borderRange);
            var max = Mathf.Clamp01(border + blur * 0.5f);
            return Mathf.Clamp01((value - min) / Mathf.Max(max - min, 1e-4f));
        }
    }

    /// <summary>lilToon のメインカラーと透明度を、メインテクスチャに焼き込む指定。</summary>
    internal sealed class TAOFallbackBakeSpec
    {
        public Material Source;
        public bool ToneCorrection;
        public bool Layer2;
        public bool Layer3;

        /// <summary>メインカラーの色も焼き込む。焼き込んだあとのマテリアルの色は、白にする。</summary>
        public bool BakeColor;

        /// <summary>透明度（色のアルファとアルファマスク）を、メインテクスチャのアルファに焼き込む。</summary>
        public bool BakeAlpha;

        /// <summary>
        /// アルファマスクの設定を残す。「置き換え」のアルファマスクは、通常の表示でメインテクスチャのアルファを使わないので、
        /// 設定を残したまま焼き込めば、通常の表示は変わらない。輪郭線もアルファマスクを使うので、外すと輪郭線が変わる。
        /// </summary>
        public bool KeepAlphaMask;

        public bool HasColorPart => ToneCorrection || Layer2 || Layer3 || BakeColor;
    }

    /// <summary>焼き込みに関わるマテリアルの設定と、焼き込めるかどうか。lilToon（Lite を除く）のマテリアルにだけ作る。</summary>
    internal sealed class TAOFallbackBakeCheck
    {
        public bool ToneCorrection;
        public bool Layer2;
        public bool Layer3;

        /// <summary>マテリアルの色が白ではない。</summary>
        public bool Tinted;

        /// <summary>lilToon のアルファマスク（0: なし, 1: 置き換え, 2: 乗算, 3: 加算, 4: 減算）。</summary>
        public int AlphaMaskMode;

        /// <summary>色のアルファかアルファマスクで、透け方を変えている。</summary>
        public bool HasAlphaSettings;

        /// <summary>メインカラーも透明度も焼き込めない理由。</summary>
        public TAOText? CommonObstacle;

        /// <summary>メインカラーを焼き込めない理由。焼き込めるなら null。</summary>
        public TAOText? ColorObstacle;

        /// <summary>透明度を焼き込めない理由。焼き込めるなら null。</summary>
        public TAOText? AlphaObstacle;

        /// <summary>メインテクスチャの色だけでは、元の色にならない。</summary>
        public bool NeedsColor => ToneCorrection || Layer2 || Layer3 || Tinted;
    }

    /// <summary>Toon Standard にフォールバックしたときにだけ使われる値。</summary>
    internal sealed class TAOFallbackToonStandardPlan
    {
        public bool WriteCulling;
        public int Culling = (int)CullMode.Back;

        /// <summary>null なら、_Ramp を変更しない。</summary>
        public TAOFallbackRampSpec Ramp;

        public float ShadowBoost;
        public float ShadowAlbedo = 0.5f;
        public float MinBrightness;
        public bool LimitBrightness = true;

        public bool WriteEmission;
        public float EmissionStrength;
        public int EmissionUV;

        public bool NormalMap;

        public bool HueShift;
        public float HueShiftAngle;

        public bool Outline;
        public float OutlineThickness;
    }

    /// <summary>Standard にフォールバックしたときにだけ使われる値。</summary>
    internal sealed class TAOFallbackStandardPlan
    {
        /// <summary>Standard の Rendering Mode（0: Opaque, 1: Cutout, 2: Fade, 3: Transparent）。</summary>
        public int Mode;

        /// <summary>光沢と映り込みを無くす。lilToon の見た目は、Standard の光沢とは合わないため。</summary>
        public bool Matte;
    }

    /// <summary>1 つのマテリアルに設定する内容。</summary>
    internal sealed class TAOFallbackPlan
    {
        public readonly TAOFallbackMaterialInfo Info;
        public TAOFallbackKind Kind = TAOFallbackKind.Unchanged;

        /// <summary>設定する VRCFallback タグ。変更しない場合は null。</summary>
        public string Tag;

        /// <summary>マテリアルごとの指定。指定が無ければ Auto。</summary>
        public TAOFallbackTarget Target = TAOFallbackTarget.Auto;

        public TAOFallbackToonStandardPlan ToonStandard;
        public TAOFallbackStandardPlan Standard;
        public TAOFallbackBakeSpec Bake;

        /// <summary>無効になっている発光の色を、黒にする。</summary>
        public bool BlackenEmission;

        /// <summary>焼き込めるかどうか。lilToon（Lite を除く）以外は null。</summary>
        public TAOFallbackBakeCheck Baking;

        /// <summary>フォールバック先を決めるために測った結果。測っていなければ null。</summary>
        public TAOFallbackSurfaceStats Stats;

        /// <summary>再現の誤差を、後ろが肌の場合で比べた（顔に重ねて表示するもの）。</summary>
        public bool ComparedOnSkin;

        /// <summary>半透明の分類で候補にした、アルファブレンドのフォールバック先。候補に無ければ Unchanged。</summary>
        public TAOFallbackKind BlendCandidate = TAOFallbackKind.Unchanged;

        /// <summary>アルファブレンドのフォールバック先で、元の表示と同じ色と透け方になるか（焼き込みを含む）。</summary>
        public bool BlendIsFaithful;

        /// <summary>補足。</summary>
        public readonly List<TAOText> Notes = new List<TAOText>();

        /// <summary>フォールバックしたときに、見た目が元から大きく変わる理由。</summary>
        public readonly List<TAOText> Warnings = new List<TAOText>();

        public TAOFallbackPlan(TAOFallbackMaterialInfo info) => Info = info;

        public Material Material => Info.Material;
        public bool Changes => Kind != TAOFallbackKind.Unchanged;
        public bool HasWarning => Warnings.Count > 0;

        public string NoteText => string.Join("\n", Warnings.Concat(Notes).Select(note => note.Text));

        public void Note(string ja, string en) => Add(Notes, new TAOText(ja, en));

        public void Note(TAOText note) => Add(Notes, note);

        public void Warn(string ja, string en) => Add(Warnings, new TAOText(ja, en));

        static void Add(List<TAOText> list, TAOText note)
        {
            if (!list.Any(existing => existing.Ja == note.Ja)) list.Add(note);
        }
    }

    /// <summary>マテリアルごとに、フォールバック先と、フォールバックしたときにだけ使われる値を決める。</summary>
    internal sealed class TAOFallbackPlanner
    {
        public const string BakerShaderName = "Hidden/ltsother_baker";
        public const string AlphaBakerShaderName = "Hidden/TAO/ShaderFallbackAlphaBake";
        public const string ToonStandardShaderName = "VRChat/Mobile/Toon Standard";
        public const string ToonStandardOutlineShaderName = "VRChat/Mobile/Toon Standard (Outline)";
        public const string StandardShaderName = "Standard";

        public static readonly Vector4 DefaultHSVG = new Vector4(0f, 1f, 1f, 1f);

        // 影を生成できないマテリアルに使う、Toon Standard の影
        const TAOFallbackShadow DefaultShadow = TAOFallbackShadow.RealisticVerySoft;

        // 半透明のマテリアルを、面として扱う条件
        const float BodyFraction = 0.5f;
        const float BodySurfaceAlpha = 0.1f;
        const float SolidSurfaceAlpha = 0.5f;

        // 面全体の不透明度がこれより低いマテリアルは、元の表示でもほとんど見えていない
        const float InvisibleCoverage = 0.02f;

        // 面として見える半透明のうち、見えている部分の不透明度がこれ以上のものは、不透明として扱う
        const float OpaqueSurfaceAlpha = 0.9f;

        // 面として見える半透明を、不透明ではなく半透明のフォールバック先にするのは、再現の誤差が不透明の半分以下のときだけ。
        // Particle は下の色を暗くすることしかできず、明るい色の衣類は消えたように見えるので、不透明を優先する
        const float TranslucentAdvantage = 0.5f;

        // 重ねて表示するものを非表示にするのは、表示しないほうが、はっきりと元の表示に近いとき
        const float HiddenRatio = 1.25f;
        const float HiddenMargin = 0.003f;

        // メインテクスチャのアルファと元の表示の差がこれ以下なら、焼き込まなくても同じように透ける・抜ける
        const float AlphaTolerance = 0.02f;
        const float CutoutTolerance = 0.005f;

        // Toon Standard にはカットアウトが無いので、抜けの多いマテリアルだけ Toon Cutout にする
        const float CutoutHoles = 0.25f;
        const float CutoutWrong = 0.02f;

        // Toon Standard の輪郭線は光の影響を受けないので、明るい色の輪郭線は暗い場所で光って見える
        const float OutlineLuminance = 0.15f;
        const float OutlineMask = 0.5f;

        // 光の影響を受けないマテリアルとして扱う、lilToon の「明るさの下限」
        const float UnlitMinLimit = 0.9f;

        const float EmissionMask = 0.5f;

        /// <summary>
        /// マテリアルを分類するときに使う、フォールバック先の組。
        /// 光の影響を受けるもの（Toon Standard・Toon）と受けないもの（Unlit）を混ぜると、暗い場所で一部だけ光って見えるので、
        /// 1 つのマテリアルの分類では、同じ系統のフォールバック先だけを使う。
        /// </summary>
        sealed class Route
        {
            /// <summary>不透明のフォールバック先。シェーダー自体の VRCFallback に任せる場合は Unchanged。</summary>
            public TAOFallbackKind Opaque = TAOFallbackKind.Unchanged;

            public TAOFallbackKind Cutout;

            /// <summary>アルファブレンドのフォールバック先。Toon Standard と Toon には無いので、Unchanged。</summary>
            public TAOFallbackKind Blend = TAOFallbackKind.Unchanged;

            public bool HasBlend => Blend != TAOFallbackKind.Unchanged;
            public bool KeepsShaderTag => Opaque == TAOFallbackKind.Unchanged;
        }

        readonly TAOShaderFallbackSettings _settings;
        readonly TAOFallbackAvatarContext _avatar;
        readonly TAOFallbackSurfaceAnalyzer _surfaces;

        public TAOFallbackPlanner(TAOShaderFallbackSettings settings, TAOFallbackAvatarContext avatar,
            TAOFallbackSurfaceAnalyzer surfaces)
        {
            _settings = settings;
            _avatar = avatar;
            _surfaces = surfaces;
        }

        public TAOFallbackPlan Plan(Material material)
        {
            var info = TAOFallbackMaterialAnalyzer.Analyze(material);
            var plan = new TAOFallbackPlan(info) { Baking = CheckBake(info) };

            var specified = _settings.FindOverride(material, _avatar.OriginalOf(material));
            if (specified != null)
            {
                plan.Target = specified.target;
                if (specified.target == TAOFallbackTarget.Keep)
                {
                    plan.Note("指定により、このマテリアルは変更しません。", "This material is left unchanged as specified.");
                    return plan;
                }

                if (specified.target == TAOFallbackTarget.Custom)
                {
                    var tag = (specified.customTag ?? "").Trim();
                    if (tag.Length == 0)
                    {
                        plan.Warn("カスタムのタグが入力されていないため、変更しません。",
                            "Left unchanged because the custom tag is empty.");
                        return plan;
                    }

                    plan.Kind = TAOFallbackKind.Custom;
                    plan.Tag = tag;
                    return plan;
                }

                Complete(plan, TAOFallbackKinds.FromTarget(specified.target));
                NoteSpecified(plan);
                return plan;
            }

            if (info.Family == TAOFallbackFamily.BuiltIn)
            {
                plan.Note("Unity や VRChat SDK のシェーダーは、Safety が有効でもそのまま表示されます。",
                    "Unity and VRChat SDK shaders are displayed as they are even when Safety is enabled.");
                return plan;
            }

            if (info.ExistingTag.Length > 0 && info.ExistingTagOnMaterial && !_settings.OverwriteExisting)
            {
                plan.Note($"「設定済みのフォールバック先を上書き」がオフのため、VRCFallback（{info.ExistingTag}）をそのまま使います。",
                    $"Kept because the overwrite option is off: the VRCFallback ({info.ExistingTag}) is used as it is.");
                return plan;
            }

            var route = RouteOf(plan);
            if (route == null) return plan;

            var kind = Classify(plan, route);
            if (TAOFallbackKinds.IsUnlit(kind) && IsUnlitMaterial(info))
            {
                plan.Note("光の影響を受けない設定のため、Unlit にフォールバックします。",
                    "Falls back to Unlit because the material is set not to be affected by light.");
            }

            Complete(plan, kind);
            return plan;
        }

        void Complete(TAOFallbackPlan plan, TAOFallbackKind kind)
        {
            plan.Kind = kind;
            plan.Tag = TAOFallbackKinds.Tag(kind, plan.Info.DoubleSided);
            if (kind == TAOFallbackKind.Unchanged) return;

            PlanBake(plan);
            if (TAOFallbackKinds.IsToonStandard(kind)) plan.ToonStandard = PlanToonStandard(plan);
            else if (TAOFallbackKinds.IsStandard(kind)) plan.Standard = PlanStandard(plan);
        }

        // マテリアルごとに指定したフォールバック先についての補足
        void NoteSpecified(TAOFallbackPlan plan)
        {
            var info = plan.Info;
            if (TAOFallbackKinds.IsUnlit(plan.Kind) && _settings.DefaultFallback != TAOFallbackShader.Unlit &&
                !IsUnlitMaterial(info))
            {
                plan.Warn("光の影響を受けないフォールバック先のため、暗い場所ではこのマテリアルだけ光って見えます。",
                    "This fallback is not affected by light, so only this material glows in dark places.");
            }

            if (TAOFallbackKinds.IsStandard(plan.Kind) && info.IsLilToon)
            {
                plan.Note("Standard は lilToon と光の当たり方が違うため、ほかの部分と明るさが合わないことがあります。",
                    "Standard is lit differently from lilToon, so its brightness may not match the other parts.");
            }

            NoteStandardBlend(plan);
        }

        /// <summary>
        /// VRChat は、Standard にフォールバックするときに、ブレンドの設定（Src Blend・Dst Blend）もマテリアルから引き継ぐ。
        /// Fade は Src Alpha、Transparent は One（乗算済みアルファ）で合成するので、マテリアルの設定と合わないほうを指定した場合は、
        /// 合うほうを案内する。lilToon の半透明は One で合成するので、Transparent が合う。
        /// </summary>
        static void NoteStandardBlend(TAOFallbackPlan plan)
        {
            var isFade = plan.Kind == TAOFallbackKind.StandardFade;
            if (!isFade && plan.Kind != TAOFallbackKind.StandardTransparent) return;

            var material = plan.Info.Material;
            if (!plan.Info.IsTranslucent || !material.HasProperty("_SrcBlend")) return;

            var source = (BlendMode)Mathf.RoundToInt(material.GetFloat("_SrcBlend"));
            if (isFade && source == BlendMode.One)
            {
                plan.Note("このマテリアルの合成の設定（Src Blend = One）は、Standard Transparent と同じです。Standard Fade で明るく表示される場合は、Standard Transparent を指定してください。",
                    "The blend setting of this material (Src Blend = One) is the same as Standard Transparent. If it looks too bright with Standard Fade, specify Standard Transparent.");
            }
            else if (!isFade && source == BlendMode.SrcAlpha)
            {
                plan.Note("このマテリアルの合成の設定（Src Blend = Src Alpha）は、Standard Fade と同じです。Standard Transparent で暗く表示される場合は、Standard Fade を指定してください。",
                    "The blend setting of this material (Src Blend = Src Alpha) is the same as Standard Fade. If it looks too dark with Standard Transparent, specify Standard Fade.");
            }
        }

        // ---- フォールバック先の分類 ----

        Route RouteOf(TAOFallbackPlan plan)
        {
            var info = plan.Info;

            // シェーダー自体に VRCFallback が書かれている場合（Poiyomi など）は、そのシェーダーのすべてのマテリアルが
            // 同じフォールバック先になる。不透明のフォールバック先はシェーダーやマテリアルの設定に任せて、
            // 設定が描画モードに合っていないマテリアルだけ、決め直す
            if (info.Family == TAOFallbackFamily.Generic && info.ShaderTag.Length > 0)
            {
                var route = RouteOfExistingTag(info);
                if (route == null) plan.Note(KeptTagNote(info));
                return route;
            }

            if (IsUnlitMaterial(info) || _settings.DefaultFallback == TAOFallbackShader.Unlit)
            {
                return new Route
                {
                    Opaque = TAOFallbackKind.Unlit,
                    Cutout = TAOFallbackKind.UnlitCutout,
                    Blend = TAOFallbackKind.UnlitTransparent,
                };
            }

            return new Route
            {
                Opaque = _settings.DefaultFallback == TAOFallbackShader.Toon ? TAOFallbackKind.Toon : TAOFallbackKind.ToonStandard,
                Cutout = TAOFallbackKind.ToonCutout,
            };
        }

        /// <summary>
        /// 設定済みの VRCFallback（マテリアルに無ければシェーダーのもの）が、マテリアルの描画モードに合っていない場合の分類。
        /// 合っている場合は null。不透明のフォールバック先は設定済みのものに任せ、カットアウトと半透明だけを、
        /// 同じ系統のフォールバック先にする。
        /// </summary>
        static Route RouteOfExistingTag(TAOFallbackMaterialInfo info)
        {
            var kind = TAOFallbackKinds.FromTag(info.ExistingTag);
            var isOpaque = kind == TAOFallbackKind.Standard || kind == TAOFallbackKind.Toon || kind == TAOFallbackKind.Unlit ||
                           kind == TAOFallbackKind.Matcap || TAOFallbackKinds.IsToonStandard(kind);
            var isCutout = kind == TAOFallbackKind.StandardCutout || kind == TAOFallbackKind.ToonCutout ||
                           kind == TAOFallbackKind.UnlitCutout;

            var isMismatched = info.IsTranslucent || info.Mode == TAOFallbackSourceMode.Effect ? isOpaque || isCutout
                : info.Mode == TAOFallbackSourceMode.Cutout && isOpaque;
            if (!isMismatched) return null;

            if (TAOFallbackKinds.IsStandard(kind))
            {
                return new Route
                {
                    Cutout = TAOFallbackKind.StandardCutout,
                    Blend = info.Premultiplied ? TAOFallbackKind.StandardTransparent : TAOFallbackKind.StandardFade,
                };
            }

            if (TAOFallbackKinds.IsUnlit(kind))
                return new Route { Cutout = TAOFallbackKind.UnlitCutout, Blend = TAOFallbackKind.UnlitTransparent };

            return new Route { Cutout = TAOFallbackKind.ToonCutout };
        }

        static bool IsUnlitMaterial(TAOFallbackMaterialInfo info)
            => info.IsLilToon && (TAOMaterialProps.Float(info.Material, "_AsUnlit") >= 0.5f ||
                                  TAOMaterialProps.Float(info.Material, "_LightMinLimit", 0.05f) >= UnlitMinLimit);

        static TAOText KeptTagNote(TAOFallbackMaterialInfo info)
        {
            return info.ExistingTagOnMaterial
                ? new TAOText($"設定済みの VRCFallback（{info.ExistingTag}）をそのまま使います。",
                    $"The existing VRCFallback ({info.ExistingTag}) is kept.")
                : new TAOText($"シェーダーに設定されている VRCFallback（{info.ExistingTag}）をそのまま使います。",
                    $"The VRCFallback specified by the shader ({info.ExistingTag}) is used as it is.");
        }

        TAOFallbackKind Classify(TAOFallbackPlan plan, Route route)
        {
            var info = plan.Info;

            if (info.Invisible || info.Mode == TAOFallbackSourceMode.FakeShadow ||
                info.Mode == TAOFallbackSourceMode.OutlineOnly || info.Mode == TAOFallbackSourceMode.FurOnly)
            {
                plan.Note("フォールバック先のシェーダーでは再現できない補助的な描画のため、非表示にします。",
                    "Hidden because this auxiliary rendering cannot be reproduced by the fallback shaders.");
                return TAOFallbackKind.Hidden;
            }

            if (info.Mode == TAOFallbackSourceMode.Effect || _avatar.IsEffectOnly(info.Material))
            {
                plan.Note("エフェクト用のマテリアルのため、非表示にします。", "Hidden because this is an effect material.");
                return TAOFallbackKind.Hidden;
            }

            if (info.IsTranslucent)
            {
                var translucent = ClassifyTranslucent(plan, route);
                if (translucent.HasValue) return translucent.Value;
            }

            return ClassifyOpaque(plan, route);
        }

        /// <summary>半透明のマテリアルのフォールバック先。不透明として扱う場合は null。</summary>
        TAOFallbackKind? ClassifyTranslucent(TAOFallbackPlan plan, Route route)
        {
            var info = plan.Info;
            var stats = Measure(plan);

            if (info.HasUnreadAlphaMask)
            {
                plan.Note("このシェーダーのアルファマスクは読み取れないため、不透明度はメインテクスチャと色のアルファから測っています。",
                    "The alpha mask of this shader cannot be read, so the opacity is measured from the alpha of the main texture and the color.");
            }

            if (stats.Valid && stats.Coverage < InvisibleCoverage)
            {
                plan.Note("元の表示でもほとんど見えていないため、非表示にします。",
                    "Hidden because it is almost invisible even in the original appearance.");
                return TAOFallbackKind.Hidden;
            }

            var isSurface = IsSurface(info, stats);
            var opacity = stats.Valid ? stats.VisibleAlpha : Mathf.Clamp01(info.Color.a);
            if (isSurface && opacity >= OpaqueSurfaceAlpha)
            {
                plan.Note("半透明ですが、ほぼ不透明の面として見えるため、不透明にフォールバックします。",
                    "Translucent, but falls back to opaque because it is seen as an almost opaque surface.");
                return null;
            }

            return route.HasBlend
                ? ClassifyBlend(plan, route, stats, isSurface)
                : ClassifyMultiply(plan, route, stats, isSurface);
        }

        // アルファブレンドのあるフォールバック先（Unlit Transparent・Standard Fade）。透け方が合っていれば、元の表示と同じ合成になる
        TAOFallbackKind? ClassifyBlend(TAOFallbackPlan plan, Route route, TAOFallbackSurfaceStats stats, bool isSurface)
        {
            // Standard は、色と色のアルファもマテリアルから受け取る
            var isFaithful = TAOFallbackKinds.UsesMaterialColor(route.Blend) || IsFaithful(plan, stats);
            plan.BlendCandidate = route.Blend;
            plan.BlendIsFaithful = isFaithful;
            if (isFaithful || !stats.HasErrors) return route.Blend;

            if (isSurface)
            {
                if (stats.Error(TAOFallbackCandidate.BlendRaw) <= TranslucentAdvantage * RouteError(plan, route, stats))
                    return route.Blend;

                plan.Warn("透け方をメインテクスチャだけでは再現できないため、不透明にフォールバックします。透け感は再現されません。",
                    "Falls back to opaque because the transparency cannot be reproduced by the main texture alone. The sheerness is not reproduced.");
                NoteBakeObstacles(plan);
                return null;
            }

            if (IsBetterShown(plan, stats, TAOFallbackCandidate.BlendRaw)) return route.Blend;

            plan.Warn("透け方をメインテクスチャだけでは再現できないため、非表示にします。",
                "Hidden because the transparency cannot be reproduced by the main texture alone.");
            NoteBakeObstacles(plan);
            return TAOFallbackKind.Hidden;
        }

        // アルファブレンドの無いフォールバック先（Toon Standard・Toon）。半透明は、下の色を暗くする Particle（乗算合成）で表す
        TAOFallbackKind? ClassifyMultiply(TAOFallbackPlan plan, Route route, TAOFallbackSurfaceStats stats, bool isSurface)
        {
            if (!stats.HasErrors) return isSurface ? (TAOFallbackKind?)null : TAOFallbackKind.Particle;

            var isFaithful = IsFaithful(plan, stats);
            var candidate = isFaithful ? TAOFallbackCandidate.ParticleBaked : TAOFallbackCandidate.ParticleRaw;

            if (isSurface)
            {
                if (stats.Error(candidate) <= TranslucentAdvantage * RouteError(plan, route, stats))
                {
                    plan.Note("透けて見える暗い色の面のため、下の色を暗くする Particle にフォールバックします。",
                        "Falls back to Particle, which darkens what is behind it, because this is a sheer surface in a dark color.");
                    return TAOFallbackKind.Particle;
                }

                plan.Warn("透けて見える面ですが、Particle（下の色を暗くする合成）では色が薄くなって消えたように見えるため、不透明にフォールバックします。透け感は再現されません。",
                    "A sheer surface, but Particle (which only darkens what is behind it) would make it look faded or missing, so it falls back to opaque. The sheerness is not reproduced.");
                if (!isFaithful) NoteBakeObstacles(plan);
                return null;
            }

            if (IsBetterShown(plan, stats, candidate)) return TAOFallbackKind.Particle;

            plan.Warn("Particle（下の色を暗くする合成）では、表示しない場合よりも元の表示から離れるため、非表示にします。",
                "Hidden because Particle (which only darkens what is behind it) would look further from the original than not displaying it.");
            if (!isFaithful) NoteBakeObstacles(plan);
            return TAOFallbackKind.Hidden;
        }

        /// <summary>
        /// 重ねて表示するものを、候補のフォールバック先で表示したほうがよいか。
        /// 表示しない場合よりも、はっきりと元の表示から離れるときだけ、表示しない。
        /// 顔に重ねるもの（表情や頬の赤みなど）は、後ろが肌なので、明るい背景で比べる。
        /// </summary>
        bool IsBetterShown(TAOFallbackPlan plan, TAOFallbackSurfaceStats stats, TAOFallbackCandidate candidate)
        {
            var onSkin = _avatar.IsFace(plan.Material);
            plan.ComparedOnSkin = onSkin;
            return stats.Error(candidate, onSkin) <= stats.Error(TAOFallbackCandidate.Hidden, onSkin) * HiddenRatio + HiddenMargin;
        }

        /// <summary>
        /// メインテクスチャだけで、元の色と透け方になるか。メインカラーと透明度を焼き込む場合を含む。
        /// 半透明のフォールバック先（Particle・Unlit Transparent）は、マテリアルの色・色のアルファ・アルファマスクを使わない。
        /// </summary>
        bool IsFaithful(TAOFallbackPlan plan, TAOFallbackSurfaceStats stats)
        {
            var info = plan.Info;
            var isTinted = plan.Baking?.NeedsColor ??
                           (info.Color.r != 1f || info.Color.g != 1f || info.Color.b != 1f);
            if (isTinted && !CanBakeColor(plan)) return false;

            var isAlphaDifferent = !stats.Valid || stats.AlphaError > AlphaTolerance;
            return !isAlphaDifferent || CanBakeAlpha(plan);
        }

        // 透け方を再現できない理由が、焼き込めないことにある場合に、その理由を添える
        void NoteBakeObstacles(TAOFallbackPlan plan)
        {
            var baking = plan.Baking;
            if (baking == null) return;

            if (_settings.BakeAlpha && baking.HasAlphaSettings && baking.AlphaObstacle.HasValue)
                plan.Note(baking.AlphaObstacle.Value);
            if (_settings.BakeColor && baking.NeedsColor && baking.ColorObstacle.HasValue)
                plan.Note(baking.ColorObstacle.Value);
        }

        // 不透明として扱ったときの、再現の誤差
        float RouteError(TAOFallbackPlan plan, Route route, TAOFallbackSurfaceStats stats)
        {
            var isCutout = route.Opaque == TAOFallbackKind.ToonStandard ? NeedsCutout(plan) : KeepsHoles(plan);
            if (!isCutout) return stats.Error(TAOFallbackCandidate.Opaque);
            return stats.Error(CanBakeAlpha(plan) ? TAOFallbackCandidate.CutoutBaked : TAOFallbackCandidate.CutoutRaw);
        }

        TAOFallbackKind ClassifyOpaque(TAOFallbackPlan plan, Route route)
        {
            if (route.KeepsShaderTag)
            {
                if (KeepsHoles(plan)) return route.Cutout;

                plan.Note(KeptTagNote(plan.Info));
                return TAOFallbackKind.Unchanged;
            }

            switch (route.Opaque)
            {
                case TAOFallbackKind.Unlit:
                    return KeepsHoles(plan) ? route.Cutout : TAOFallbackKind.Unlit;

                case TAOFallbackKind.Toon:
                    if (IsFrontCulled(plan)) return TAOFallbackKind.Hidden;
                    return KeepsHoles(plan) ? route.Cutout : TAOFallbackKind.Toon;

                default:
                    if (NeedsCutout(plan))
                    {
                        plan.Note("抜けている部分が多いため、カットアウトのある Toon Cutout にフォールバックします。",
                            "Falls back to Toon Cutout, which supports cutout, because a large part is cut away.");
                        return route.Cutout;
                    }

                    return _settings.Outline != TAOFallbackOutline.None && TryGetOutline(plan, true, out _)
                        ? TAOFallbackKind.ToonStandardOutline
                        : TAOFallbackKind.ToonStandard;
            }
        }

        /// <summary>
        /// 半透明のマテリアルが、面として見えるもの（透け感のある衣類など）か、重ねて表示するもの（顔の表情や眼鏡のレンズなど）か。
        /// </summary>
        bool IsSurface(TAOFallbackMaterialInfo info, TAOFallbackSurfaceStats stats)
        {
            if (info.Mode == TAOFallbackSourceMode.Overlay || info.Mode == TAOFallbackSourceMode.Refraction) return false;
            if (_avatar.IsFace(info.Material)) return false;
            if (!stats.Valid) return info.ZWrite && info.Color.a >= SolidSurfaceAlpha;

            // 体のボーンに付いているメッシュは、薄くても衣類として扱う
            if (stats.BodyFraction >= BodyFraction) return stats.VisibleAlpha >= BodySurfaceAlpha;
            return info.ZWrite && stats.VisibleAlpha >= SolidSurfaceAlpha;
        }

        bool IsFrontCulled(TAOFallbackPlan plan)
        {
            if (!plan.Info.CullKnown || plan.Info.Cull != CullMode.Front) return false;
            plan.Warn("裏面だけを描画するマテリアルは再現できないため、非表示にします。",
                "Hidden because a material that draws only back faces cannot be reproduced.");
            return true;
        }

        // アルファで抜ける部分があるか。不透明として扱う半透明のマテリアルも、アルファの低い部分は抜けて見える
        static bool HasAlphaHoles(TAOFallbackMaterialInfo info)
            => info.Mode == TAOFallbackSourceMode.Cutout || info.IsTranslucent;

        /// <summary>カットアウトのあるフォールバック先で、元と同じように抜けるか。</summary>
        bool KeepsHoles(TAOFallbackPlan plan)
        {
            if (!HasAlphaHoles(plan.Info)) return false;
            var stats = Measure(plan);
            if (!stats.Valid) return plan.Info.Mode == TAOFallbackSourceMode.Cutout;

            // 透明度を焼き込めば、色のアルファやアルファマスクで抜いている部分も、同じように抜ける
            if (CanBakeAlpha(plan)) return stats.Holes > 0.001f;
            return stats.CutoutHoles > 0.001f && stats.CutoutWrong <= CutoutWrong;
        }

        bool NeedsCutout(TAOFallbackPlan plan)
        {
            if (!HasAlphaHoles(plan.Info)) return false;
            var stats = Measure(plan);
            if (!stats.Valid || stats.BodyFraction >= BodyFraction) return false;

            if (CanBakeAlpha(plan)) return stats.Holes >= CutoutHoles;
            return stats.CutoutHoles >= CutoutHoles && stats.CutoutWrong <= CutoutWrong;
        }

        // ---- Toon Standard ----

        TAOFallbackToonStandardPlan PlanToonStandard(TAOFallbackPlan plan)
        {
            var info = plan.Info;
            var material = info.Material;
            var toonStandard = new TAOFallbackToonStandardPlan();

            if (info.CullKnown)
            {
                toonStandard.WriteCulling = true;
                toonStandard.Culling = (int)info.Cull;
            }

            toonStandard.Ramp = PlanRamp(plan, toonStandard);

            toonStandard.MinBrightness = !_settings.AutoMinBrightness ? _settings.MinBrightness
                : info.IsLilToon ? Mathf.Clamp(TAOMaterialProps.Float(material, "_LightMinLimit", 0.05f), 0f,
                    TAOOptimizeVRCShaderFallback.MaxMinBrightness)
                : 0f;
            toonStandard.LimitBrightness = !_settings.AutoLimitBrightness ? _settings.LimitBrightness
                : !info.IsLilToon || TAOMaterialProps.Float(material, "_LightMaxLimit", 1f) <= 1f;

            PlanEmission(plan, toonStandard);
            toonStandard.NormalMap = UsesNormalMap(info);
            PlanHueShift(plan, toonStandard);

            // 輪郭線のないマテリアルに手動で指定されたときは太さ 0 にして、シェーダーの既定の輪郭線を出さない
            if (plan.Kind == TAOFallbackKind.ToonStandardOutline)
            {
                toonStandard.Outline = true;
                toonStandard.OutlineThickness = TryGetOutline(plan, false, out var thickness) ? thickness : 0f;
            }

            return toonStandard;
        }

        TAOFallbackRampSpec PlanRamp(TAOFallbackPlan plan, TAOFallbackToonStandardPlan toonStandard)
        {
            var info = plan.Info;
            var material = info.Material;

            // lilToon は、フォールバック専用のプロパティとして _Ramp を宣言している
            if (!info.IsLilToon || !TAOMaterialProps.DeclaresTexture(material.shader, "_Ramp")) return null;

            // 影の無いマテリアルに、影を付けない
            if (material.HasProperty("_UseShadow") && !TAOMaterialProps.IsOn(material, "_UseShadow"))
                return TAOFallbackRampSpec.FromPreset(TAOFallbackShadow.Flat);
            if (_settings.Shadow != TAOFallbackShadow.FromMaterial) return TAOFallbackRampSpec.FromPreset(_settings.Shadow);
            if (!material.HasProperty("_ShadowColor")) return TAOFallbackRampSpec.FromPreset(DefaultShadow);

            var strength = TAOMaterialProps.Float(material, "_ShadowStrength", 1f);
            if (!TAOMaterialProps.IsOn(material, "_ShadowMaskType") &&
                TAOMaterialProps.Texture(material, "_ShadowStrengthMask") != null)
            {
                strength *= Measure(plan).Mask(TAOFallbackMask.ShadowStrength);
            }

            var spec = new TAOFallbackRampSpec
            {
                Generated = true,
                Strength = Mathf.Clamp01(strength),
                Color1 = TAOMaterialProps.ShaderColor(material, "_ShadowColor", new Color(0.82f, 0.76f, 0.85f, 1f)),
                Color2 = TAOMaterialProps.ShaderColor(material, "_Shadow2ndColor", new Color(0.68f, 0.66f, 0.79f, 1f)),
                Color3 = TAOMaterialProps.ShaderColor(material, "_Shadow3rdColor", new Color(0f, 0f, 0f, 0f)),
                Border1 = TAOMaterialProps.Float(material, "_ShadowBorder", 0.5f),
                Blur1 = TAOMaterialProps.Float(material, "_ShadowBlur", 0.1f),
                Border2 = TAOMaterialProps.Float(material, "_Shadow2ndBorder", 0.15f),
                Blur2 = TAOMaterialProps.Float(material, "_Shadow2ndBlur", 0.1f),
                Border3 = TAOMaterialProps.Float(material, "_Shadow3rdBorder", 0.25f),
                Blur3 = TAOMaterialProps.Float(material, "_Shadow3rdBlur", 0.1f),
                BorderColor = TAOMaterialProps.ShaderColor(material, "_ShadowBorderColor", new Color(1f, 0.1f, 0f, 1f)),
                BorderRange = TAOMaterialProps.Float(material, "_ShadowBorderRange", 0.08f),
                MainStrength = Mathf.Clamp01(TAOMaterialProps.Float(material, "_ShadowMainStrength")),
            };

            toonStandard.ShadowAlbedo = spec.ShadowAlbedo;
            return spec;
        }

        /// <summary>
        /// Toon Standard は、発光が無効でも Emission Color × Emission Map × Strength で発光する。
        /// 無効な発光では光らないようにし、有効な発光では、元の強さに合わせてフォールバック専用の Strength を設定する。
        /// </summary>
        void PlanEmission(TAOFallbackPlan plan, TAOFallbackToonStandardPlan toonStandard)
        {
            var info = plan.Info;
            var material = info.Material;

            if (!info.IsLilToon)
            {
                // 発光の有効・無効を読み取れたときだけ設定する
                if (material.HasProperty("_EmissionColor") && IsGenericEmissionOff(material))
                    toonStandard.WriteEmission = true;
                return;
            }

            toonStandard.WriteEmission = true;
            if (!TAOMaterialProps.IsOn(material, "_UseEmission"))
            {
                plan.BlackenEmission = CanBlackenEmission(info);
                return;
            }

            var blendMode = Mathf.RoundToInt(TAOMaterialProps.Float(material, "_EmissionBlendMode", 1f));
            var uvMode = Mathf.RoundToInt(TAOMaterialProps.Float(material, "_EmissionMap_UVMode"));
            var usesAudioLink = TAOMaterialProps.IsOn(material, "_UseAudioLink") &&
                                TAOMaterialProps.IsOn(material, "_AudioLink2Emission");
            var isAdditive = blendMode == 1 || blendMode == 2;
            var mask = TAOMaterialProps.Texture(material, "_EmissionBlendMask") != null
                ? Measure(plan).Mask(TAOFallbackMask.Emission)
                : 1f;

            if (!isAdditive || uvMode > 1 || usesAudioLink || mask < EmissionMask)
            {
                plan.Note("この発光の設定は Toon Standard で再現できないため、フォールバックしたときは発光しません。",
                    "This emission cannot be reproduced by Toon Standard, so it does not glow when falling back.");
                return;
            }

            var emissionColor = TAOMaterialProps.Color(material, "_EmissionColor", Color.white);
            var strength = TAOMaterialProps.Float(material, "_EmissionBlend", 1f) * Mathf.Clamp01(emissionColor.a) * mask;

            // lilToon の発光色は [HDR] なのでそのまま使われるが、Toon Standard の発光色はリニアに変換されて使われる。
            // 同じ色でも明るさが変わるので、その分を強さで補う
            var original = Luminance(TAOMaterialProps.ShaderColor(material, "_EmissionColor", Color.white));
            var fallback = Luminance(ToonStandardEmissionColor(emissionColor));
            if (fallback > 1e-5f) strength *= original / fallback;

            // 発光のテクスチャのアルファは、Toon Standard では使われないので、強さに含める
            var emissionMap = _surfaces.Pixels(TAOMaterialProps.Texture(material, "_EmissionMap"));
            if (emissionMap != null) strength *= emissionMap.GlowAlpha;

            // 点滅は、平均の明るさにする
            var blink = TAOMaterialProps.Vector(material, "_EmissionBlink", new Vector4(0f, 0f, Mathf.PI, 0f));
            strength *= 1f - Mathf.Clamp01(blink.x) * 0.5f;

            var mainStrength = Mathf.Clamp01(TAOMaterialProps.Float(material, "_EmissionMainStrength"));
            if (mainStrength > 0f)
            {
                var pixels = _surfaces.Pixels(info.MainTexture);
                var luminance = (pixels != null ? pixels.AverageLuminance : 1f) *
                                Luminance(TAOMaterialProps.ShaderColor(material, "_Color", Color.white));
                strength *= Mathf.Lerp(1f, Mathf.Clamp01(luminance), mainStrength);
            }

            toonStandard.EmissionStrength = Mathf.Clamp(strength, 0f, 2f);
            toonStandard.EmissionUV = uvMode;
        }

        /// <summary>
        /// 無効な発光の色を、黒にできるか。無効な発光の色は通常の表示に使われないので、黒にしても見た目は変わらない。
        /// 発光の設定がアニメーションで変化する場合は、通常の表示が変わるので黒にしない。
        /// </summary>
        bool CanBlackenEmission(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            if (!info.IsLilToon || !material.HasProperty("_UseEmission") || TAOMaterialProps.IsOn(material, "_UseEmission"))
                return false;
            if (IsAnimated(material, name => name == "_UseEmission" || name.StartsWith("_Emission"))) return false;
            return TAOMaterialProps.Color(material, "_EmissionColor", Color.black).maxColorComponent > 0f;
        }

        // Toon Standard が受け取る発光色
        static Color ToonStandardEmissionColor(Color stored)
        {
            var shader = Shader.Find(ToonStandardShaderName);
            var isHDR = shader != null && TAOMaterialProps.IsHDR(shader, "_EmissionColor");
            return isHDR ? stored : stored.linear;
        }

        // Standard と同じ慣習で発光を切り替えるシェーダーで、発光が無効か
        static bool IsGenericEmissionOff(Material material)
        {
            var keyword = material.shader.keywordSpace.FindKeyword("_EMISSION");
            if (keyword.isValid) return !material.IsKeywordEnabled(keyword);

            foreach (var name in new[] { "_UseEmission", "_EnableEmission", "_EmissionEnabled" })
                if (material.HasProperty(name)) return material.GetFloat(name) == 0f;
            return false;
        }

        static bool UsesNormalMap(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            if (TAOMaterialProps.Texture(material, "_BumpMap") == null) return false;
            if (info.IsLilToon) return TAOMaterialProps.IsOn(material, "_UseBumpMap") && info.MainTexIsIdentity;

            var keyword = material.shader.keywordSpace.FindKeyword("_NORMALMAP");
            return keyword.isValid && material.IsKeywordEnabled(keyword);
        }

        // 色相の変更だけは、Toon Standard の Hue Shift で近い色にできる
        void PlanHueShift(TAOFallbackPlan plan, TAOFallbackToonStandardPlan toonStandard)
        {
            var info = plan.Info;
            var material = info.Material;
            var isBaked = plan.Bake != null && plan.Bake.ToneCorrection;
            if (!info.IsLilToon || isBaked || !material.HasProperty("_MainTexHSVG")) return;

            var hsvg = material.GetVector("_MainTexHSVG");
            if (hsvg == DefaultHSVG) return;

            var hue = hsvg.x - Mathf.Floor(hsvg.x);
            var hasMask = TAOMaterialProps.Texture(material, "_MainColorAdjustMask") != null;
            if (hue > 0.001f && hue < 0.999f && !hasMask)
            {
                toonStandard.HueShift = true;
                toonStandard.HueShiftAngle = hue * Mathf.PI * 2f;
            }

            var hueOnly = Mathf.Approximately(hsvg.y, 1f) && Mathf.Approximately(hsvg.z, 1f) &&
                          Mathf.Approximately(hsvg.w, 1f);
            if (hasMask || !hueOnly)
            {
                plan.Note("色調補正のうち、色相以外は再現できません。「メインカラーを焼き込み」で再現できます。",
                    "Only the hue of the color adjustment can be reproduced. \"Bake Main Color\" reproduces the rest.");
            }
        }

        /// <summary>
        /// 輪郭線を Toon Standard Outline で再現できるか。
        /// 太さのマスクはフォールバック先に届かないので、マスクで輪郭線を消している部分が多いマテリアルは対象にしない。
        /// </summary>
        bool TryGetOutline(TAOFallbackPlan plan, bool automatic, out float thickness)
        {
            thickness = 0f;
            var info = plan.Info;
            var material = info.Material;
            if (!info.IsLilToon || !info.HasOutline) return false;

            var width = TAOMaterialProps.Float(material, "_OutlineWidth", 0.08f);
            if (width <= 0f) return false;

            var mask = TAOMaterialProps.Texture(material, "_OutlineWidthMask") != null
                ? Measure(plan).Mask(TAOFallbackMask.OutlineWidth)
                : 1f;

            if (automatic)
            {
                // 頂点カラーで太さを変える輪郭線は、再現できない
                if (Mathf.RoundToInt(TAOMaterialProps.Float(material, "_OutlineVertexR2Width")) != 0) return false;
                if (mask < OutlineMask) return false;

                var color = TAOMaterialProps.ShaderColor(material, "_OutlineColor", new Color(0.6f, 0.56f, 0.73f, 1f));
                if (_settings.Outline == TAOFallbackOutline.DarkOnly && Luminance(color) > OutlineLuminance) return false;
            }

            thickness = Mathf.Clamp(width * mask, 0f, 0.5f);
            return thickness > 0f;
        }

        // ---- Standard ----

        TAOFallbackStandardPlan PlanStandard(TAOFallbackPlan plan)
        {
            var info = plan.Info;
            plan.BlackenEmission = CanBlackenEmission(info);

            return new TAOFallbackStandardPlan
            {
                Mode = plan.Kind == TAOFallbackKind.StandardCutout ? 1
                    : plan.Kind == TAOFallbackKind.StandardFade ? 2
                    : plan.Kind == TAOFallbackKind.StandardTransparent ? 3
                    : 0,
                Matte = info.IsLilToon,
            };
        }

        // ---- メインカラーと透明度の焼き込み ----

        bool CanBakeColor(TAOFallbackPlan plan)
            => _settings.BakeColor && plan.Baking != null && !plan.Baking.ColorObstacle.HasValue;

        bool CanBakeAlpha(TAOFallbackPlan plan)
            => _settings.BakeAlpha && plan.Baking != null && !plan.Baking.AlphaObstacle.HasValue;

        TAOFallbackBakeCheck CheckBake(TAOFallbackMaterialInfo info)
        {
            if (!info.IsLilToon || info.IsLite) return null;

            var material = info.Material;
            var check = new TAOFallbackBakeCheck
            {
                ToneCorrection = TAOMaterialProps.Vector(material, "_MainTexHSVG", DefaultHSVG) != DefaultHSVG ||
                                 (TAOMaterialProps.Float(material, "_MainGradationStrength") > 0f &&
                                  TAOMaterialProps.Texture(material, "_MainGradationTex") != null),
                Layer2 = TAOMaterialProps.IsOn(material, "_UseMain2ndTex"),
                Layer3 = TAOMaterialProps.IsOn(material, "_UseMain3rdTex"),
                Tinted = info.Color.r != 1f || info.Color.g != 1f || info.Color.b != 1f,
                AlphaMaskMode = Mathf.RoundToInt(TAOMaterialProps.Float(material, "_AlphaMaskMode")),
            };

            check.HasAlphaSettings = info.Color.a < 1f || check.AlphaMaskMode != 0;
            check.CommonObstacle = FindCommonObstacle(info);
            check.ColorObstacle = check.CommonObstacle ?? FindColorObstacle(info, check.Layer2, check.Layer3, check.Tinted);
            check.AlphaObstacle = check.CommonObstacle ?? FindAlphaObstacle(info, check.AlphaMaskMode);
            return check;
        }

        void PlanBake(TAOFallbackPlan plan)
        {
            var info = plan.Info;
            var baking = plan.Baking;
            if (baking == null || !TAOFallbackKinds.UsesMainTexture(plan.Kind)) return;

            var bake = new TAOFallbackBakeSpec { Source = info.Material };

            if (_settings.BakeColor)
            {
                // Toon Standard と Standard 以外のフォールバック先は、マテリアルの色を使わない。
                // 2nd・3rd は色を掛けたあとに重ねるので、焼き込むときは色も焼き込む
                var bakeColor = baking.Tinted &&
                                (baking.Layer2 || baking.Layer3 || !TAOFallbackKinds.UsesMaterialColor(plan.Kind));
                if (baking.ToneCorrection || baking.Layer2 || baking.Layer3 || bakeColor)
                {
                    var obstacle = baking.CommonObstacle ?? FindColorObstacle(info, baking.Layer2, baking.Layer3, bakeColor);
                    if (obstacle.HasValue)
                    {
                        plan.Note(obstacle.Value);
                    }
                    else
                    {
                        bake.ToneCorrection = baking.ToneCorrection;
                        bake.Layer2 = baking.Layer2;
                        bake.Layer3 = baking.Layer3;
                        bake.BakeColor = bakeColor;
                    }
                }
            }

            if (_settings.BakeAlpha && NeedsAlphaBake(plan))
            {
                if (baking.AlphaObstacle.HasValue)
                {
                    plan.Note(baking.AlphaObstacle.Value);
                }
                else
                {
                    bake.BakeAlpha = true;
                    bake.KeepAlphaMask = baking.AlphaMaskMode == 1;
                }
            }

            if (bake.HasColorPart)
            {
                plan.Note("メインカラーを焼き込んだテクスチャに差し替えます。",
                    "The main texture is replaced with one that the main color is baked into.");
            }

            if (bake.BakeAlpha)
            {
                plan.Note("透明度を、メインテクスチャのアルファに焼き込みます。",
                    "The transparency is baked into the alpha of the main texture.");
                if (info.MainTexture is Texture2D main && TAOFallbackTextures.DoublesMemoryWithAlpha(main.format))
                {
                    plan.Note("メインテクスチャがアルファを持たない形式（DXT1）のため、アルファを持つ形式（DXT5）で生成します。このテクスチャのメモリは 2 倍になります。",
                        "The main texture is in a format without alpha (DXT1), so it is generated in a format with alpha (DXT5). The memory of this texture doubles.");
                }
            }

            if (bake.HasColorPart || bake.BakeAlpha) plan.Bake = bake;
        }

        /// <summary>
        /// フォールバック先がメインテクスチャのアルファを使い、そのアルファが元の表示の透け方・抜け方と違うか。
        /// </summary>
        bool NeedsAlphaBake(TAOFallbackPlan plan)
        {
            var info = plan.Info;
            var kind = plan.Kind;
            if (!TAOFallbackKinds.UsesTextureAlpha(kind) || !plan.Baking.HasAlphaSettings) return false;

            // 不透明のマテリアルは、アルファを使っていない
            if (!info.IsTranslucent && info.Mode != TAOFallbackSourceMode.Cutout) return false;

            var stats = Measure(plan);
            if (!stats.Valid) return true;

            var isCutout = kind == TAOFallbackKind.ToonCutout || kind == TAOFallbackKind.UnlitCutout ||
                           kind == TAOFallbackKind.StandardCutout;
            if (isCutout || !info.IsTranslucent) return stats.CutoutWrong + stats.CutoutExtra > CutoutTolerance;

            // Standard は色のアルファも使うので、焼き込みが要るのはアルファマスクがあるときだけ
            if (TAOFallbackKinds.IsStandard(kind) && plan.Baking.AlphaMaskMode == 0) return false;
            return stats.AlphaError > AlphaTolerance;
        }

        /// <summary>メインカラーも透明度も焼き込めない理由。焼き込める場合は null。</summary>
        TAOText? FindCommonObstacle(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return new TAOText("この環境では、テクスチャに焼き込めません。",
                    "Cannot bake into textures in this environment.");
            }

            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows)
            {
                return new TAOText("Windows 以外のビルドでは、テクスチャに焼き込みません。",
                    "Baking into textures is done only in Windows builds.");
            }

            if (info.MainTexture != null && !(info.MainTexture is Texture2D))
            {
                return new TAOText("メインテクスチャが Texture2D ではないため、焼き込めません。",
                    "Cannot bake because the main texture is not a Texture2D.");
            }

            if (!info.MainTexIsIdentity || TAOMaterialProps.Vector(material, "_MainTex_ScrollRotate", Vector4.zero) != Vector4.zero)
            {
                return new TAOText("メインテクスチャに Tiling・Offset・UV アニメーションが設定されているため、焼き込めません。",
                    "Cannot bake because the main texture uses tiling, offset or UV animation.");
            }

            if (IsAnimated(material, name => name == "_Color" || name.StartsWith("_Main") || name.StartsWith("_UseMain") ||
                                             name.StartsWith("_Color2nd") || name.StartsWith("_Color3rd")))
            {
                return new TAOText("メインカラーの設定がアニメーションで変化するため、焼き込めません。",
                    "Cannot bake because the main color settings are changed by animations.");
            }

            return null;
        }

        /// <summary>メインカラーを焼き込むと通常の表示が変わってしまう場合に、その理由を返す。焼き込める場合は null。</summary>
        static TAOText? FindColorObstacle(TAOFallbackMaterialInfo info, bool layer2, bool layer3, bool bakeColor)
        {
            var material = info.Material;

            if (Shader.Find(BakerShaderName) == null)
            {
                return new TAOText("lilToon の焼き込み用シェーダーが見つからないため、メインカラーを焼き込めません。",
                    "The main color cannot be baked because the bake shader of lilToon was not found.");
            }

            if (bakeColor && info.Color.maxColorComponent > 1f)
            {
                return new TAOText("メインカラーの色が HDR のため、焼き込めません。",
                    "Cannot bake because the main color is HDR.");
            }

            if ((layer2 && !CanBakeLayer(material, "2nd")) || (layer3 && !CanBakeLayer(material, "3rd")))
            {
                return new TAOText("メインカラー 2nd・3rd に、焼き込めない設定（UV0 以外・デカールの左右指定・アニメーションなど）があります。",
                    "Main Color 2nd / 3rd uses settings that cannot be baked (UV other than UV0, left/right decals, animation and so on).");
            }

            return null;
        }

        /// <summary>透明度を焼き込むと通常の表示が変わってしまう場合に、その理由を返す。焼き込める場合は null。</summary>
        TAOText? FindAlphaObstacle(TAOFallbackMaterialInfo info, int maskMode)
        {
            var material = info.Material;

            if (Shader.Find(AlphaBakerShaderName) == null)
            {
                return new TAOText("焼き込み用のシェーダーが見つからないため、透明度を焼き込めません。",
                    "The transparency cannot be baked because the bake shader was not found.");
            }

            // 2nd・3rd の透明度の設定は、色のアルファとアルファマスクの間で適用されるので、まとめて焼き込むと結果が変わる
            foreach (var layer in new[] { "2nd", "3rd" })
            {
                if (!TAOMaterialProps.IsOn(material, "_UseMain" + layer + "Tex") ||
                    !TAOMaterialProps.IsOn(material, "_Main" + layer + "TexAlphaMode")) continue;
                return new TAOText("メインカラー 2nd・3rd が透明度を変える設定のため、透明度を焼き込めません。",
                    "The transparency cannot be baked because Main Color 2nd / 3rd changes it.");
            }

            // 「置き換え」のアルファマスクは、通常の表示でメインテクスチャと色のアルファを使わない。
            // マスクの設定を残したまま焼き込むので、通常の表示は変わらない
            if (maskMode == 0 || maskMode == 1) return null;

            // それ以外のアルファマスクは、マスクを外して、焼き込んだアルファだけで同じ透け方にする。
            // マスクにテクスチャがあると、テクスチャの縁で、メインテクスチャのアルファとの重なり方が変わる。輪郭線もマスクを使う。
            // 通常の表示が変わらないのは、輪郭線が無く、マスクが一定の値で、アルファに掛けるだけの場合に限られる
            var isConstant = TAOMaterialProps.Texture(material, "_AlphaMask") == null &&
                             !IsAnimated(material, name => name.StartsWith("_AlphaMask"));
            var isExact = maskMode == 2 || info.MainTexture == null;
            if (info.HasOutline || !isConstant || !isExact)
            {
                return new TAOText("「置き換え」以外のアルファマスクは、焼き込むと通常の表示が変わるため、透明度を焼き込めません。",
                    "The transparency cannot be baked because baking an alpha mask other than Replace would change the normal appearance.");
            }

            return null;
        }

        // 焼き込んでも通常の表示が変わらないのは、UV0 に固定で重なり、時間・視点・光で変化しないレイヤーだけ
        static bool CanBakeLayer(Material material, string layer)
        {
            var prefix = "_Main" + layer;
            foreach (var name in new[]
                     {
                         prefix + "Tex_UVMode", prefix + "Tex_Cull", prefix + "TexAlphaMode", prefix + "TexIsLeftOnly",
                         prefix + "TexIsRightOnly", prefix + "TexShouldCopy", prefix + "TexShouldFlipMirror",
                         prefix + "TexShouldFlipCopy", prefix + "TexIsMSDF",
                     })
            {
                if (TAOMaterialProps.IsOn(material, name)) return false;
            }

            if (TAOMaterialProps.Float(material, prefix + "EnableLighting", 1f) != 1f) return false;
            if (TAOMaterialProps.Vector(material, prefix + "Tex_ScrollRotate", Vector4.zero) != Vector4.zero) return false;
            if (TAOMaterialProps.Vector(material, prefix + "DissolveParams", Vector4.zero).x != 0f) return false;
            if (TAOMaterialProps.Vector(material, prefix + "DistanceFade", Vector4.zero).z != 0f) return false;

            var decalAnimation = TAOMaterialProps.Vector(material, prefix + "TexDecalAnimation", new Vector4(1f, 1f, 1f, 30f));
            if (decalAnimation.x != 1f || decalAnimation.y != 1f || decalAnimation.z != 1f) return false;

            if (TAOMaterialProps.IsOn(material, "_UseAudioLink") && TAOMaterialProps.IsOn(material, "_AudioLink2Main" + layer))
                return false;

            return TAOMaterialProps.Color(material, "_Color" + layer, Color.white).maxColorComponent <= 1f;
        }

        // ---- 共通 ----

        TAOFallbackSurfaceStats Measure(TAOFallbackPlan plan)
            => plan.Stats ?? (plan.Stats = _surfaces.Measure(plan.Info));

        bool IsAnimated(Material material, Func<string, bool> matches) => _avatar.IsAnimated(material, matches);

        static float Luminance(Color linear) => 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
    }
}
