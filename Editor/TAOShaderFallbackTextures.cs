using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// フォールバック用のテクスチャ（Shadow Ramp と、メインカラーや透明度を焼き込んだメインテクスチャ）を作る。
    /// 同じ内容のテクスチャは 1 枚にまとめる。
    /// </summary>
    internal sealed class TAOFallbackTextures : IDisposable
    {
        const int RampWidth = 256;
        const int RampHeight = 4;

        // メインテクスチャが無いマテリアルの色だけを焼き込むときのサイズ
        const int ColorOnlySize = 4;

        // シーンプレビューでは、圧縮しない RenderTexture を使うので、サイズを抑える
        const int PreviewMaxSize = 1024;

        // VRChat SDK の Toon Standard の Shadow Ramp。Resources から読めないときに使う
        static readonly Dictionary<TAOFallbackShadow, string> PresetGuids = new Dictionary<TAOFallbackShadow, string>
        {
            { TAOFallbackShadow.Flat, "8ed41581528c4fa4fa11970aca4edb8d" },
            { TAOFallbackShadow.Realistic, "348500adef1d2da428abc7b720b8b699" },
            { TAOFallbackShadow.RealisticSoft, "636cf1b5dfca6f54b94ca3d2ff8216c9" },
            { TAOFallbackShadow.RealisticVerySoft, "5f304bf7a07313d43b8562d9eabce646" },
            { TAOFallbackShadow.Toon2Band, "dfafc89321615114fb6dbecdba0c8214" },
            { TAOFallbackShadow.Toon3Band, "5d1b50be612cf1248b6e101f8d1c5b53" },
            { TAOFallbackShadow.Toon4Band, "0d6a7a9ec31ab7448a777f0e2daef4af" },
        };

        readonly bool _preview;
        readonly Dictionary<string, Texture> _ramps = new Dictionary<string, Texture>();
        readonly Dictionary<string, Texture> _bakes = new Dictionary<string, Texture>();
        readonly List<Object> _created = new List<Object>();

        /// <param name="preview">
        /// シーンプレビュー用。作ったテクスチャは Dispose で破棄する。
        /// ビルドでは、作ったテクスチャはアバターの一部として保存されるので、破棄しない。
        /// </param>
        public TAOFallbackTextures(bool preview) => _preview = preview;

        public void Dispose()
        {
            if (!_preview) return;

            foreach (var created in _created)
            {
                if (created == null) continue;
                if (created is RenderTexture renderTexture) renderTexture.Release();
                Object.DestroyImmediate(created);
            }

            _created.Clear();
            _ramps.Clear();
            _bakes.Clear();
        }

        // ---- Shadow Ramp ----

        public Texture Ramp(TAOFallbackRampSpec spec)
        {
            var key = spec.Key;
            if (_ramps.TryGetValue(key, out var ramp)) return ramp;

            ramp = spec.Generated ? GenerateRamp(spec) : LoadPreset(spec.Preset);

            // プリセットが見つからない場合は、影の無い Ramp にする
            if (ramp == null) ramp = GenerateRamp(new TAOFallbackRampSpec { Generated = true, Strength = 0f });

            _ramps[key] = ramp;
            return ramp;
        }

        // プリセットの名前は、VRChat SDK の Shadow Ramp のテクスチャの名前（ShadowRampRealisticVerySoft など）と同じ
        static Texture LoadPreset(TAOFallbackShadow preset)
        {
            var texture = Resources.Load<Texture2D>("VRChat/ShadowRamp" + preset);
            if (texture != null) return texture;

            if (!PresetGuids.TryGetValue(preset, out var guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Toon Standard は、Ramp の横方向を光の当たり具合で読む
        Texture GenerateRamp(TAOFallbackRampSpec spec)
        {
            var texture = new Texture2D(RampWidth, RampHeight, TextureFormat.RGBA32, false, false)
            {
                name = "TAO Fallback Shadow Ramp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            var pixels = new Color32[RampWidth * RampHeight];
            for (var x = 0; x < RampWidth; x++)
            {
                Color32 color = spec.Evaluate((x + 0.5f) / RampWidth).gamma;
                color.a = 255;
                for (var y = 0; y < RampHeight; y++) pixels[y * RampWidth + x] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _created.Add(texture);
            return texture;
        }

        // ---- メインカラーと透明度の焼き込み ----

        /// <summary>メインテクスチャがこの形式のとき、透明度を焼き込むと、アルファを持つ形式になってメモリが 2 倍になる。</summary>
        public static bool DoublesMemoryWithAlpha(TextureFormat format)
            => format == TextureFormat.DXT1 || format == TextureFormat.DXT1Crunched;

        /// <summary>焼き込めなかった場合は null。</summary>
        public Texture Bake(TAOFallbackBakeSpec spec)
        {
            var key = BakeKey(spec);
            if (_bakes.TryGetValue(key, out var baked)) return baked;

            try
            {
                baked = BakeMain(spec);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TAO] Failed to bake the main texture of {spec.Source.name}: {exception.Message}", spec.Source);
                baked = null;
            }

            _bakes[key] = baked;
            return baked;
        }

        Texture BakeMain(TAOFallbackBakeSpec spec)
        {
            var bakers = CreateBakers(spec);
            if (bakers == null) return null;

            var source = spec.Source;
            var main = TAOMaterialProps.Texture(source, "_MainTex") as Texture2D;
            GetSize(spec, main, out var width, out var height);
            var isSRGB = main == null || GraphicsFormatUtility.IsSRGBFormat(main.graphicsFormat);
            var readWrite = isSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            var useMipmaps = main != null ? main.mipmapCount > 1 : width > ColorOnlySize;

            var active = RenderTexture.active;
            try
            {
                var input = main != null ? (Texture)main : Texture2D.whiteTexture;
                if (_preview)
                {
                    var scale = Mathf.Min(1f, (float)PreviewMaxSize / Mathf.Max(width, height));
                    var preview = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                        Mathf.Max(1, Mathf.RoundToInt(height * scale)), 0, RenderTextureFormat.ARGB32, readWrite)
                    {
                        name = source.name + " (TAO Baked Preview)",
                        useMipMap = useMipmaps,
                        autoGenerateMips = useMipmaps,
                    };
                    CopySampling(main, preview);
                    Render(input, preview, bakers, main);
                    _created.Add(preview);
                    return preview;
                }

                var mipCount = !useMipmaps ? 1 : main != null ? main.mipmapCount : 0;
                var texture = mipCount > 0
                    ? new Texture2D(width, height, TextureFormat.RGBA32, mipCount, !isSRGB)
                    : new Texture2D(width, height, TextureFormat.RGBA32, true, !isSRGB);
                try
                {
                    texture.name = (main != null ? main.name : source.name) + " (TAO Baked)";

                    // ミップマップは、縮小して作るのではなく、階層ごとに焼き込む。
                    // 小さく描画すると、元のテクスチャの同じ階層のミップマップが読まれるので、
                    // 遠くから見たときも、元のテクスチャに lilToon が色を重ねた結果と同じになる
                    for (var mip = 0; mip < texture.mipmapCount; mip++)
                        BakeMip(texture, mip, input, bakers, main, readWrite);
                    texture.Apply(false, false);
                    CopySampling(main, texture);
                    Compress(texture, main, useMipmaps, spec.BakeAlpha);
                }
                catch
                {
                    Object.DestroyImmediate(texture);
                    throw;
                }

                _created.Add(texture);
                return texture;
            }
            finally
            {
                RenderTexture.active = active;
                foreach (var baker in bakers) Object.DestroyImmediate(baker);
            }
        }

        static void BakeMip(Texture2D texture, int mip, Texture input, List<Material> bakers, Texture2D main,
            RenderTextureReadWrite readWrite)
        {
            var width = Mathf.Max(1, texture.width >> mip);
            var height = Mathf.Max(1, texture.height >> mip);
            var area = new Rect(0, 0, width, height);

            var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWrite);
            Texture2D level = null;
            try
            {
                Render(input, target, bakers, main);
                RenderTexture.active = target;
                if (mip == 0)
                {
                    texture.ReadPixels(area, 0, 0, false);
                    return;
                }

                level = new Texture2D(width, height, TextureFormat.RGBA32, false, readWrite == RenderTextureReadWrite.Linear);
                level.ReadPixels(area, 0, 0, false);
                texture.SetPixels32(level.GetPixels32(), mip);
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                if (level != null) Object.DestroyImmediate(level);
            }
        }

        // 焼き込みを順に適用する。メインカラーと透明度の両方を焼き込むときは、メインカラーを焼き込んだ結果に、透明度を焼き込む
        static void Render(Texture input, RenderTexture target, List<Material> bakers, Texture2D main)
        {
            if (bakers.Count == 1)
            {
                Graphics.Blit(input, target, bakers[0]);
                return;
            }

            var readWrite = target.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            var intermediate = RenderTexture.GetTemporary(target.width, target.height, 0, RenderTextureFormat.ARGB32,
                readWrite);
            var wrapU = intermediate.wrapModeU;
            var wrapV = intermediate.wrapModeV;
            var filter = intermediate.filterMode;
            try
            {
                // lilToon は、アルファマスクをメインテクスチャのサンプラーで読む。
                // 透明度の焼き込みは途中の結果をメインテクスチャとして読むので、元のメインテクスチャと同じ設定で読ませる
                if (main != null)
                {
                    intermediate.wrapModeU = main.wrapModeU;
                    intermediate.wrapModeV = main.wrapModeV;
                    intermediate.filterMode = main.filterMode;
                }

                Graphics.Blit(input, intermediate, bakers[0]);
                Graphics.Blit(intermediate, target, bakers[1]);
            }
            finally
            {
                // 一時的な RenderTexture は使い回されるので、設定を元に戻す
                intermediate.wrapModeU = wrapU;
                intermediate.wrapModeV = wrapV;
                intermediate.filterMode = filter;
                RenderTexture.ReleaseTemporary(intermediate);
            }
        }

        // 通常の表示が変わらないように、元のメインテクスチャと同じ大きさで焼き込む
        static void GetSize(TAOFallbackBakeSpec spec, Texture2D main, out int width, out int height)
        {
            if (main != null)
            {
                width = main.width;
                height = main.height;
                return;
            }

            // メインテクスチャが無いときは、焼き込むテクスチャのうち最も大きいものに合わせる
            width = ColorOnlySize;
            height = ColorOnlySize;
            var names = new[]
            {
                spec.Layer2 ? "_Main2ndTex" : null, spec.Layer3 ? "_Main3rdTex" : null,
                spec.BakeAlpha && TAOMaterialProps.IsOn(spec.Source, "_AlphaMaskMode") ? "_AlphaMask" : null,
            };
            foreach (var name in names)
            {
                if (name == null) continue;
                var texture = TAOMaterialProps.Texture(spec.Source, name);
                if (texture == null) continue;
                width = Mathf.Max(width, texture.width);
                height = Mathf.Max(height, texture.height);
            }
        }

        static void CopySampling(Texture2D from, Texture to)
        {
            if (from == null)
            {
                to.wrapMode = TextureWrapMode.Repeat;
                to.filterMode = FilterMode.Bilinear;
                return;
            }

            to.wrapModeU = from.wrapModeU;
            to.wrapModeV = from.wrapModeV;
            to.filterMode = from.filterMode;
            to.anisoLevel = from.anisoLevel;
            to.mipMapBias = from.mipMapBias;
        }

        // 元のメインテクスチャと同じ形式で圧縮して、テクスチャメモリが増えないようにする。
        // 同じ形式なら、焼き込んでいない部分は、圧縮し直しても元とほぼ同じになる。
        // 透明度を焼き込むときは、アルファを持つ形式にする
        static void Compress(Texture2D texture, Texture2D main, bool useMipmaps, bool needsAlpha)
        {
            var format = TextureFormat.RGBA32;
            if (texture.width % 4 == 0 && texture.height % 4 == 0 && texture.width > ColorOnlySize)
            {
                if (main == null)
                {
                    format = TextureFormat.BC7;
                }
                else
                {
                    switch (main.format)
                    {
                        case TextureFormat.DXT1:
                        case TextureFormat.DXT1Crunched:
                            // DXT5 は、色の持ち方が DXT1 と同じで、アルファが加わる
                            format = needsAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1;
                            break;
                        case TextureFormat.DXT5:
                        case TextureFormat.DXT5Crunched:
                            format = TextureFormat.DXT5;
                            break;
                        case TextureFormat.RGBA32:
                        case TextureFormat.ARGB32:
                        case TextureFormat.BGRA32:
                        case TextureFormat.RGB24:
                            format = TextureFormat.RGBA32;
                            break;
                        default:
                            format = TextureFormat.BC7;
                            break;
                    }
                }
            }

            if (format != TextureFormat.RGBA32)
                EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);

            // ミップマップのあるテクスチャは、ミップストリーミングを有効にする
            if (useMipmaps) EnableMipStreaming(texture, main);

            // 設定をすべて書き込んでから、元のテクスチャと同じく読み取り不可にする
            texture.Apply(false, true);
        }

        static void EnableMipStreaming(Texture2D texture, Texture2D main)
        {
            using (var serialized = new SerializedObject(texture))
            {
                var streaming = serialized.FindProperty("m_StreamingMipmaps");
                if (streaming == null) return;
                streaming.boolValue = true;

                var priority = serialized.FindProperty("m_StreamingMipmapsPriority");
                if (priority != null && main != null) priority.intValue = main.streamingMipmapsPriority;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // 焼き込みに使うマテリアル。メインカラー、透明度の順に適用する。シェーダーが見つからなければ null
        static List<Material> CreateBakers(TAOFallbackBakeSpec spec)
        {
            var colorShader = spec.HasColorPart ? Shader.Find(TAOFallbackPlanner.BakerShaderName) : null;
            var alphaShader = spec.BakeAlpha ? Shader.Find(TAOFallbackPlanner.AlphaBakerShaderName) : null;
            if ((spec.HasColorPart && colorShader == null) || (spec.BakeAlpha && alphaShader == null)) return null;

            var bakers = new List<Material>();
            if (colorShader != null) bakers.Add(CreateBaker(colorShader, spec));
            if (alphaShader != null) bakers.Add(CreateAlphaBaker(alphaShader, spec.Source));
            return bakers.Count > 0 ? bakers : null;
        }

        // lilToon と同じ順で、メインテクスチャのアルファに、色のアルファとアルファマスクを適用する
        static Material CreateAlphaBaker(Shader shader, Material source)
        {
            var baker = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            baker.SetFloat("_TAOColorAlpha", TAOMaterialProps.Color(source, "_Color", Color.white).a);

            var mode = Mathf.RoundToInt(TAOMaterialProps.Float(source, "_AlphaMaskMode"));
            baker.SetFloat("_TAOMaskMode", mode);
            if (mode == 0) return baker;

            baker.SetTexture("_TAOAlphaMask", TAOMaterialProps.Texture(source, "_AlphaMask"));
            baker.SetTextureScale("_TAOAlphaMask", TAOMaterialProps.Scale(source, "_AlphaMask"));
            baker.SetTextureOffset("_TAOAlphaMask", TAOMaterialProps.Offset(source, "_AlphaMask"));
            baker.SetFloat("_TAOMaskScale", TAOMaterialProps.Float(source, "_AlphaMaskScale", 1f));
            baker.SetFloat("_TAOMaskValue", TAOMaterialProps.Float(source, "_AlphaMaskValue"));
            return baker;
        }

        // lilToon が「焼き込み」で使うシェーダーに、lilToon と同じ値を設定する。アルファは変えない
        static Material CreateBaker(Shader shader, TAOFallbackBakeSpec spec)
        {
            var source = spec.Source;
            var baker = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            var color = TAOMaterialProps.Color(source, "_Color", Color.white);
            baker.SetColor("_Color", spec.BakeColor ? new Color(color.r, color.g, color.b, 1f) : Color.white);
            baker.SetVector("_MainTexHSVG", TAOMaterialProps.Vector(source, "_MainTexHSVG", TAOFallbackPlanner.DefaultHSVG));
            baker.SetFloat("_MainGradationStrength", TAOMaterialProps.Float(source, "_MainGradationStrength"));
            baker.SetTexture("_MainGradationTex", TAOMaterialProps.Texture(source, "_MainGradationTex"));
            baker.SetTexture("_MainColorAdjustMask", TAOMaterialProps.Texture(source, "_MainColorAdjustMask"));

            if (spec.Layer2) CopyLayer(source, baker, "2nd");
            if (spec.Layer3) CopyLayer(source, baker, "3rd");
            return baker;
        }

        static void CopyLayer(Material source, Material baker, string layer)
        {
            var prefix = "_Main" + layer;
            var texture = prefix + "Tex";
            var mask = prefix + "BlendMask";

            baker.SetFloat("_UseMain" + layer + "Tex", 1f);
            baker.SetColor("_Color" + layer, TAOMaterialProps.Color(source, "_Color" + layer, Color.white));
            baker.SetFloat(texture + "Angle", TAOMaterialProps.Float(source, texture + "Angle"));
            baker.SetVector(texture + "DecalAnimation",
                TAOMaterialProps.Vector(source, texture + "DecalAnimation", new Vector4(1f, 1f, 1f, 30f)));
            baker.SetVector(texture + "DecalSubParam",
                TAOMaterialProps.Vector(source, texture + "DecalSubParam", new Vector4(1f, 1f, 0f, 1f)));
            baker.SetFloat(texture + "IsDecal", TAOMaterialProps.Float(source, texture + "IsDecal"));
            baker.SetFloat(texture + "BlendMode", TAOMaterialProps.Float(source, texture + "BlendMode"));

            baker.SetTexture(texture, TAOMaterialProps.Texture(source, texture));
            baker.SetTextureScale(texture, TAOMaterialProps.Scale(source, texture));
            baker.SetTextureOffset(texture, TAOMaterialProps.Offset(source, texture));
            baker.SetTexture(mask, TAOMaterialProps.Texture(source, mask));
        }

        // 焼き込みの結果を決める値をすべて並べたキー
        static string BakeKey(TAOFallbackBakeSpec spec)
        {
            var source = spec.Source;
            var key = new StringBuilder();

            void AddTexture(string name)
            {
                var texture = TAOMaterialProps.Texture(source, name);
                key.Append(texture != null ? texture.GetInstanceID() : 0).Append('|');
            }

            void AddVector(Vector4 value)
            {
                for (var i = 0; i < 4; i++)
                    key.Append(value[i].ToString("R", CultureInfo.InvariantCulture)).Append(',');
                key.Append('|');
            }

            AddTexture("_MainTex");
            key.Append(spec.HasColorPart ? 'C' : '-').Append('|');
            AddVector(TAOMaterialProps.Vector(source, "_MainTexHSVG", TAOFallbackPlanner.DefaultHSVG));
            AddVector(new Vector4(TAOMaterialProps.Float(source, "_MainGradationStrength"), 0f, 0f, 0f));
            AddTexture("_MainGradationTex");
            AddTexture("_MainColorAdjustMask");
            AddVector(spec.BakeColor ? (Vector4)TAOMaterialProps.Color(source, "_Color", Color.white) : Vector4.one);

            if (spec.BakeAlpha)
            {
                AddTexture("_AlphaMask");
                AddVector(new Vector4(TAOMaterialProps.Color(source, "_Color", Color.white).a,
                    TAOMaterialProps.Float(source, "_AlphaMaskMode"),
                    TAOMaterialProps.Float(source, "_AlphaMaskScale", 1f),
                    TAOMaterialProps.Float(source, "_AlphaMaskValue")));

                var maskScale = TAOMaterialProps.Scale(source, "_AlphaMask");
                var maskOffset = TAOMaterialProps.Offset(source, "_AlphaMask");
                AddVector(new Vector4(maskScale.x, maskScale.y, maskOffset.x, maskOffset.y));
            }
            else
            {
                key.Append("-|");
            }

            foreach (var layer in new[] { spec.Layer2 ? "2nd" : null, spec.Layer3 ? "3rd" : null })
            {
                if (layer == null)
                {
                    key.Append("-|");
                    continue;
                }

                var texture = "_Main" + layer + "Tex";
                AddTexture(texture);
                AddTexture("_Main" + layer + "BlendMask");
                AddVector(TAOMaterialProps.Color(source, "_Color" + layer, Color.white));
                AddVector(TAOMaterialProps.Vector(source, texture + "DecalAnimation", Vector4.zero));
                AddVector(TAOMaterialProps.Vector(source, texture + "DecalSubParam", Vector4.zero));

                var scale = TAOMaterialProps.Scale(source, texture);
                var offset = TAOMaterialProps.Offset(source, texture);
                AddVector(new Vector4(scale.x, scale.y, offset.x, offset.y));
                AddVector(new Vector4(TAOMaterialProps.Float(source, texture + "Angle"),
                    TAOMaterialProps.Float(source, texture + "IsDecal"),
                    TAOMaterialProps.Float(source, texture + "BlendMode"), 0f));
            }

            return key.ToString();
        }
    }
}
