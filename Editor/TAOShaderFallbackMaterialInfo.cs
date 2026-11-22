using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    internal enum TAOFallbackFamily
    {
        /// <summary>Unity 内蔵のシェーダーと VRChat SDK のシェーダー。Safety が有効でも、同じ種類のシェーダーで表示される。</summary>
        BuiltIn,

        LilToon,

        /// <summary>そのほかのカスタムシェーダー。一般的なプロパティ名やタグから推測する。</summary>
        Generic,
    }

    /// <summary>元のマテリアルの描画モード。</summary>
    internal enum TAOFallbackSourceMode
    {
        Opaque,
        Cutout,
        Transparent,
        TransparentOnePass,
        TransparentTwoPass,
        Refraction,
        Fur,
        FurCutout,
        Gem,
        Overlay,
        FakeShadow,
        OutlineOnly,
        FurOnly,

        /// <summary>加算など、通常のアルファブレンドではない描画。</summary>
        Effect,
    }

    /// <summary>シェーダーが宣言しているプロパティだけを読む。宣言されていないプロパティを読むと、Unity がエラーを出すため。</summary>
    internal static class TAOMaterialProps
    {
        public static bool Has(Material material, string name) => material.HasProperty(name);

        /// <summary>
        /// シェーダーがテクスチャのプロパティとして宣言しているか。
        /// 宣言されていないテクスチャは、ビルドでマテリアルから取り除かれるので、フォールバック先には届かない。
        /// </summary>
        public static bool DeclaresTexture(Shader shader, string name)
        {
            var index = shader.FindPropertyIndex(name);
            return index >= 0 && shader.GetPropertyType(index) == ShaderPropertyType.Texture;
        }

        public static float Float(Material material, string name, float fallback = 0f)
            => material.HasProperty(name) ? material.GetFloat(name) : fallback;

        public static bool IsOn(Material material, string name) => Float(material, name) != 0f;

        public static Color Color(Material material, string name, Color fallback)
            => material.HasProperty(name) ? material.GetColor(name) : fallback;

        /// <summary>
        /// シェーダーが受け取る色。Unity は、[HDR] の付いた色をそのまま渡し、それ以外の色をリニアに変換して渡す。
        /// 同じ名前の色でも、フォールバック先のシェーダーで [HDR] の有無が違うと、受け取る色が変わる。
        /// </summary>
        public static Color ShaderColor(Material material, string name, Color fallback)
        {
            if (!material.HasProperty(name)) return fallback.linear;
            var color = material.GetColor(name);
            return IsHDR(material.shader, name) ? color : color.linear;
        }

        public static bool IsHDR(Shader shader, string name)
        {
            var index = shader.FindPropertyIndex(name);
            return index >= 0 && (shader.GetPropertyFlags(index) & ShaderPropertyFlags.HDR) != 0;
        }

        public static Vector4 Vector(Material material, string name, Vector4 fallback)
            => material.HasProperty(name) ? material.GetVector(name) : fallback;

        public static Texture Texture(Material material, string name)
            => material.HasProperty(name) ? material.GetTexture(name) : null;

        public static Vector2 Scale(Material material, string name)
            => material.HasProperty(name) ? material.GetTextureScale(name) : Vector2.one;

        public static Vector2 Offset(Material material, string name)
            => material.HasProperty(name) ? material.GetTextureOffset(name) : Vector2.zero;
    }

    /// <summary>フォールバック先を決めるために、マテリアルから読み取った情報。</summary>
    internal sealed class TAOFallbackMaterialInfo
    {
        public readonly Material Material;
        public TAOFallbackFamily Family;
        public TAOFallbackSourceMode Mode;
        public bool HasOutline;
        public bool IsLite;

        public CullMode Cull = CullMode.Back;

        /// <summary>カリングをマテリアルから読み取れたか。</summary>
        public bool CullKnown;

        public bool ZWrite = true;
        public float Cutoff = 0.5f;

        /// <summary>フォールバック先のシェーダーが使う _MainTex を、シェーダーが持っているか。</summary>
        public bool HasMainTextureProperty;

        public Texture MainTexture;
        public Vector2 MainScale = Vector2.one;
        public Vector2 MainOffset = Vector2.zero;
        public Color Color = Color.white;

        /// <summary>lilToon の「非表示」。</summary>
        public bool Invisible;

        /// <summary>乗算済みアルファで合成する（lilToon 以外）。Standard の Transparent に当たる。</summary>
        public bool Premultiplied;

        /// <summary>読み取れないアルファマスクが設定されている（lilToon 以外）。実際の不透明度は、測った値と違うことがある。</summary>
        public bool HasUnreadAlphaMask;

        /// <summary>一覧に表示するシェーダーの種類（「lilToon」「Poiyomi Toon」など）。</summary>
        public string ShaderName = "";

        /// <summary>lilToon のカスタムシェーダーの名前（「Uzumore」など）。カスタムシェーダーでなければ null。</summary>
        public string CustomShaderName;

        /// <summary>設定済みの VRCFallback。無ければ空文字。</summary>
        public string ExistingTag = "";

        /// <summary>設定済みの VRCFallback が、シェーダーではなくマテリアルに設定されているか。</summary>
        public bool ExistingTagOnMaterial;

        /// <summary>シェーダー自体に書かれている VRCFallback。無ければ空文字。</summary>
        public string ShaderTag = "";

        public TAOFallbackMaterialInfo(Material material) => Material = material;

        public bool IsLilToon => Family == TAOFallbackFamily.LilToon;

        /// <summary>「lilToon（Uzumore）」のような表記。</summary>
        public string ShaderLabel
            => CustomShaderName == null ? ShaderName
                : T($"{ShaderName}（{CustomShaderName}）", $"{ShaderName} ({CustomShaderName})");

        /// <summary>アルファブレンドで描画される。</summary>
        public bool IsTranslucent
            => Mode == TAOFallbackSourceMode.Transparent || Mode == TAOFallbackSourceMode.TransparentOnePass ||
               Mode == TAOFallbackSourceMode.TransparentTwoPass || Mode == TAOFallbackSourceMode.Refraction ||
               Mode == TAOFallbackSourceMode.Overlay;

        public bool IsCutout => Mode == TAOFallbackSourceMode.Cutout || Mode == TAOFallbackSourceMode.FurCutout;

        public bool DoubleSided => Cull == CullMode.Off;

        public bool MainTexIsIdentity => MainScale == Vector2.one && MainOffset == Vector2.zero;

        public string ModeLabel
        {
            get
            {
                switch (Mode)
                {
                    case TAOFallbackSourceMode.Opaque: return T("不透明", "Opaque");
                    case TAOFallbackSourceMode.Cutout: return T("カットアウト", "Cutout");
                    case TAOFallbackSourceMode.Transparent: return T("半透明", "Transparent");
                    case TAOFallbackSourceMode.TransparentOnePass: return T("半透明 1パス", "Transparent 1 Pass");
                    case TAOFallbackSourceMode.TransparentTwoPass: return T("半透明 2パス", "Transparent 2 Pass");
                    case TAOFallbackSourceMode.Refraction: return T("屈折", "Refraction");
                    case TAOFallbackSourceMode.Fur: return T("ファー", "Fur");
                    case TAOFallbackSourceMode.FurCutout: return T("ファー カットアウト", "Fur Cutout");
                    case TAOFallbackSourceMode.Gem: return T("宝石", "Gem");
                    case TAOFallbackSourceMode.Overlay: return T("オーバーレイ", "Overlay");
                    case TAOFallbackSourceMode.FakeShadow: return "FakeShadow";
                    case TAOFallbackSourceMode.OutlineOnly: return T("輪郭線のみ", "Outline Only");
                    case TAOFallbackSourceMode.FurOnly: return T("ファーのみ", "Fur Only");
                    default: return T("エフェクト", "Effect");
                }
            }
        }
    }

    internal static class TAOFallbackMaterialAnalyzer
    {
        // シェーダー自体に書かれている VRCFallback。マテリアルに設定されたものと区別するために使う
        static readonly Dictionary<Shader, string> ShaderLevelTags = new Dictionary<Shader, string>();

        public static TAOFallbackMaterialInfo Analyze(Material material)
        {
            var info = new TAOFallbackMaterialInfo(material);
            var shader = material.shader;

            info.ExistingTag = material.GetTag(TAOFallbackKinds.TagName, false, "");
            info.ShaderTag = ShaderLevelTag(shader);
            info.ExistingTagOnMaterial = info.ExistingTag != info.ShaderTag;

            info.HasMainTextureProperty = material.HasProperty("_MainTex");
            if (info.HasMainTextureProperty)
            {
                info.MainTexture = material.GetTexture("_MainTex");
                info.MainScale = material.GetTextureScale("_MainTex");
                info.MainOffset = material.GetTextureOffset("_MainTex");
            }

            info.Color = TAOMaterialProps.Color(material, "_Color", Color.white);
            info.Cutoff = TAOMaterialProps.Float(material, "_Cutoff", 0.5f);

            if (IsBuiltIn(shader))
            {
                info.Family = TAOFallbackFamily.BuiltIn;
                ReadGeneric(info);
            }
            else if (IsLilToon(shader))
            {
                info.Family = TAOFallbackFamily.LilToon;
                ReadLilToon(info);
            }
            else
            {
                info.Family = TAOFallbackFamily.Generic;
                ReadGeneric(info);
            }

            TAOFallbackShaderNames.Read(info);
            return info;
        }

        /// <summary>
        /// Unity 内蔵のシェーダーは、Safety が有効でも VRChat の内蔵シェーダーで同じように表示される。
        /// VRChat SDK のシェーダーは、シェーダー自体にフォールバックが定義されている。
        /// </summary>
        static bool IsBuiltIn(Shader shader)
        {
            if (shader.name.StartsWith("VRChat/")) return true;
            if (!EditorUtility.IsPersistent(shader)) return false;
            var path = AssetDatabase.GetAssetPath(shader);
            return !(path.StartsWith("Assets/") || path.StartsWith("Packages/"));
        }

        /// <summary>lilToon と、lilToon のカスタムシェーダー（.lilcontainer）。</summary>
        static bool IsLilToon(Shader shader)
        {
            if (shader.name.Contains("lilToon")) return true;
            if (shader.FindPropertyIndex("_lilToonVersion") >= 0) return true;
            return AssetDatabase.GetAssetPath(shader).Contains(".lilcontainer");
        }

        static string ShaderLevelTag(Shader shader)
        {
            if (ShaderLevelTags.TryGetValue(shader, out var tag)) return tag;

            var probe = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            tag = probe.GetTag(TAOFallbackKinds.TagName, false, "");
            Object.DestroyImmediate(probe);
            ShaderLevelTags[shader] = tag;
            return tag;
        }

        // lilToon は描画モードごとにシェーダーが分かれているので、lilToon 自身と同じく、シェーダー名の最後の部分で判定する
        static void ReadLilToon(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            var name = material.shader.name;
            var last = name.Substring(name.LastIndexOf('/') + 1);
            var isMulti = last.Contains("Multi");

            info.IsLite = last.Contains("Lite") || name.Contains("/Lite/");
            info.HasOutline = last.Contains("Outline") || name.Contains("OutlineOnly") ||
                              (isMulti && TAOMaterialProps.IsOn(material, "_UseOutline"));
            info.Mode = LilToonMode(material, name, last, isMulti);

            info.Cull = ToCullMode(TAOMaterialProps.Float(material, "_Cull", (float)CullMode.Back));
            info.CullKnown = material.HasProperty("_Cull");
            info.ZWrite = TAOMaterialProps.Float(material, "_ZWrite", 1f) != 0f;
            info.Invisible = TAOMaterialProps.IsOn(material, "_Invisible");
        }

        static TAOFallbackSourceMode LilToonMode(Material material, string name, string last, bool isMulti)
        {
            if (name.Contains("FakeShadow")) return TAOFallbackSourceMode.FakeShadow;
            if (name.Contains("OutlineOnly")) return TAOFallbackSourceMode.OutlineOnly;
            if (name.Contains("FurOnly")) return TAOFallbackSourceMode.FurOnly;

            // lilToonMulti は、描画モードをプロパティで持つ
            var multiMode = isMulti ? (int)TAOMaterialProps.Float(material, "_TransparentMode") : -1;

            if (last.Contains("Gem") || multiMode == 6) return TAOFallbackSourceMode.Gem;
            if (last.Contains("Refraction") || multiMode == 3) return TAOFallbackSourceMode.Refraction;
            if (last.Contains("Fur") || multiMode == 4 || multiMode == 5)
            {
                return last.Contains("Cutout") || multiMode == 5
                    ? TAOFallbackSourceMode.FurCutout
                    : TAOFallbackSourceMode.Fur;
            }

            if (isMulti)
            {
                return multiMode == 1 ? TAOFallbackSourceMode.Cutout
                    : multiMode == 2 ? TAOFallbackSourceMode.Transparent
                    : TAOFallbackSourceMode.Opaque;
            }

            if (last.Contains("Overlay")) return TAOFallbackSourceMode.Overlay;
            if (last.Contains("Transparent"))
            {
                return last.Contains("TwoPass") ? TAOFallbackSourceMode.TransparentTwoPass
                    : last.Contains("OnePass") ? TAOFallbackSourceMode.TransparentOnePass
                    : TAOFallbackSourceMode.Transparent;
            }

            return last.Contains("Cutout") ? TAOFallbackSourceMode.Cutout : TAOFallbackSourceMode.Opaque;
        }

        // lilToon 以外は、Standard と同じ慣習（RenderType、キュー、_Mode、ブレンドのプロパティ）から推測する
        static void ReadGeneric(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            var renderType = material.GetTag("RenderType", true, "");
            var queue = material.renderQueue;
            var standardMode = TAOMaterialProps.Float(material, "_Mode", -1f);

            ReadGenericCull(info);

            var blended = renderType == "Transparent" || queue > (int)RenderQueue.GeometryLast || standardMode >= 2f ||
                          material.IsKeywordEnabled("_ALPHABLEND_ON") ||
                          material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON");
            if (blended)
            {
                info.ZWrite = TAOMaterialProps.Float(material, "_ZWrite") != 0f;
                info.Mode = IsPlainAlphaBlend(info) ? TAOFallbackSourceMode.Transparent : TAOFallbackSourceMode.Effect;
                info.Premultiplied = material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
                                     (material.HasProperty("_SrcBlend") &&
                                      (int)material.GetFloat("_SrcBlend") == (int)BlendMode.One);

                // Poiyomi のアルファマスク。マスクの読み方が複雑なので、不透明度には反映しない
                info.HasUnreadAlphaMask = TAOMaterialProps.IsOn(material, "_MainAlphaMaskMode") &&
                                          TAOMaterialProps.Texture(material, "_AlphaMask") != null;
                return;
            }

            info.ZWrite = TAOMaterialProps.Float(material, "_ZWrite", 1f) != 0f;
            var cutout = renderType == "TransparentCutout" || queue >= (int)RenderQueue.AlphaTest ||
                         standardMode == 1f || material.IsKeywordEnabled("_ALPHATEST_ON");
            info.Mode = cutout ? TAOFallbackSourceMode.Cutout : TAOFallbackSourceMode.Opaque;
        }

        /// <summary>
        /// テクスチャと色のアルファで透ける、通常のアルファブレンドか。
        /// 加算などのエフェクトは、フォールバック先のシェーダーでは再現できない。
        /// </summary>
        static bool IsPlainAlphaBlend(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            if (!info.HasMainTextureProperty && !material.HasProperty("_Color")) return false;

            if (material.HasProperty("_DstBlend"))
                return (int)material.GetFloat("_DstBlend") == (int)BlendMode.OneMinusSrcAlpha;

            var name = material.shader.name.ToLowerInvariant();
            return !(name.Contains("addit") || name.Contains("_add") || name.Contains("particle") ||
                     name.Contains("effect"));
        }

        static void ReadGenericCull(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            foreach (var name in new[] { "_Cull", "_CullMode", "_Culling" })
            {
                if (!material.HasProperty(name)) continue;
                info.Cull = ToCullMode(material.GetFloat(name));
                info.CullKnown = true;
                return;
            }

            if (!material.HasProperty("_DoubleSided")) return;
            info.Cull = material.GetFloat("_DoubleSided") != 0f ? CullMode.Off : CullMode.Back;
            info.CullKnown = true;
        }

        static CullMode ToCullMode(float value)
        {
            var mode = Mathf.RoundToInt(value);
            return mode == 0 ? CullMode.Off : mode == 1 ? CullMode.Front : CullMode.Back;
        }
    }

    /// <summary>
    /// 一覧に表示するシェーダーの種類を求める。
    /// lilToon のカスタムシェーダー（.lilcontainer）は、「lilToon（Uzumore）」のように、カスタムシェーダーの名前を添える。
    /// </summary>
    internal static class TAOFallbackShaderNames
    {
        const string CustomDataFile = "lilCustomShaderDatas.lilblock";

        static readonly Regex CustomNamePattern = new Regex("ShaderName\\s+\"([^\"]*)\"");
        static readonly Regex GuidPattern = new Regex("^[0-9a-fA-F]{32}$");

        // シェーダーごとの結果（シェーダーの種類と、カスタムシェーダーの名前）
        static readonly Dictionary<Shader, KeyValuePair<string, string>> Names =
            new Dictionary<Shader, KeyValuePair<string, string>>();

        // フォルダーごとの、カスタムシェーダーの設定ファイルの ShaderName。設定ファイルが無ければ null
        static readonly Dictionary<string, string> FolderNames = new Dictionary<string, string>();

        public static void Read(TAOFallbackMaterialInfo info)
        {
            var shader = info.Material.shader;
            if (!Names.TryGetValue(shader, out var names))
            {
                names = Resolve(shader, info);
                Names[shader] = names;
            }

            info.ShaderName = names.Key;
            info.CustomShaderName = names.Value;
        }

        static KeyValuePair<string, string> Resolve(Shader shader, TAOFallbackMaterialInfo info)
        {
            var name = shader.name;
            if (info.IsLilToon)
            {
                var kind = info.IsLite ? "lilToon Lite"
                    : name.Substring(name.LastIndexOf('/') + 1).Contains("Multi") ? "lilToon Multi"
                    : "lilToon";
                return new KeyValuePair<string, string>(kind, LilToonCustomName(shader));
            }

            // Unity 内蔵のシェーダーは、「Unlit/Texture」のように、名前の最後の部分だけでは分からないものが多い
            var isBuiltInName = info.Family == TAOFallbackFamily.BuiltIn && !name.StartsWith("VRChat/");
            return new KeyValuePair<string, string>(isBuiltInName ? name : LastSegment(name), null);
        }

        static string LastSegment(string name)
        {
            var segments = name.Split('/');
            var index = segments.Length - 1;

            // Poiyomi のロックしたシェーダーは、「Hidden/Locked/.poiyomi/Poiyomi Toon/（GUID）」という名前になる
            if (index > 0 && GuidPattern.IsMatch(segments[index])) index--;
            return segments[index].Length > 0 ? segments[index] : name;
        }

        static string LilToonCustomName(Shader shader)
        {
            var path = AssetDatabase.GetAssetPath(shader);
            if (!string.IsNullOrEmpty(path))
            {
                var folderName = NameOfFolder(Path.GetDirectoryName(FileUtil.GetPhysicalPath(path)));
                if (folderName != null) return folderName.Length > 0 ? LastSegment(folderName) : null;
            }

            return CustomNameFromShaderName(shader.name);
        }

        // カスタムシェーダーは、シェーダーと同じフォルダーの設定ファイルに ShaderName "Sigmal00/Uzumore" のように名前を持つ
        static string NameOfFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            if (FolderNames.TryGetValue(folder, out var name)) return name;

            name = null;
            try
            {
                var file = Path.Combine(folder, CustomDataFile);
                if (File.Exists(file))
                {
                    var match = CustomNamePattern.Match(File.ReadAllText(file));
                    name = match.Success ? match.Groups[1].Value : "";
                }
            }
            catch (System.Exception)
            {
                // 読めない場合は、シェーダーの名前から求める
            }

            FolderNames[folder] = name;
            return name;
        }

        // 「Sigmal00/Uzumore/lilToon」「Hidden/Sigmal00/Uzumore/OpaqueOutline」のような名前から、カスタムシェーダーの名前を取り出す
        static string CustomNameFromShaderName(string name)
        {
            var segments = new List<string>(name.Split('/'));
            if (segments.Count > 0 && segments[0] == "Hidden") segments.RemoveAt(0);

            // lilToon 自身のシェーダーは、「lilToon」「Hidden/lilToonCutout」「_lil/lilToonMulti」のような名前
            if (segments.Count < 2 || segments[0] == "_lil") return null;

            segments.RemoveAt(segments.Count - 1);
            if (segments.Count > 1 && segments[segments.Count - 1] == "Lite") segments.RemoveAt(segments.Count - 1);
            return segments[segments.Count - 1];
        }
    }
}
