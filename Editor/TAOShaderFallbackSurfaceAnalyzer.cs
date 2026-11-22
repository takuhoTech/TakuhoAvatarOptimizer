using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// 縮小して読み取ったテクスチャ。値は、シェーダーが受け取るのと同じリニアの値。
    /// 圧縮されたテクスチャや読み取り不可のテクスチャも、GPU 経由で読み取る。
    /// </summary>
    internal sealed class TAOFallbackPixels
    {
        public const int Size = 256;

        readonly Color32[] _data;
        float _averageLuminance = -1f;
        float _glowAlpha = -1f;

        TAOFallbackPixels(Color32[] data) => _data = data;

        public Color32 Sample(float u, float v)
        {
            var x = (int)((u - Mathf.Floor(u)) * Size);
            var y = (int)((v - Mathf.Floor(v)) * Size);
            if (x >= Size) x = Size - 1;
            if (y >= Size) y = Size - 1;
            return _data[y * Size + x];
        }

        public static float Luminance(Color32 color)
            => (0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b) / 255f;

        /// <summary>アルファで重みを付けた、テクスチャ全体の平均の明るさ。</summary>
        public float AverageLuminance
        {
            get
            {
                if (_averageLuminance >= 0f) return _averageLuminance;

                double sum = 0, weight = 0;
                foreach (var color in _data)
                {
                    sum += Luminance(color) * color.a;
                    weight += color.a;
                }

                _averageLuminance = weight > 0 ? (float)(sum / weight) : 1f;
                return _averageLuminance;
            }
        }

        /// <summary>
        /// 明るい部分ほど重みを付けた、アルファの平均。
        /// 発光のテクスチャのアルファは、lilToon では発光の強さに掛かるが、Toon Standard では使われない。
        /// </summary>
        public float GlowAlpha
        {
            get
            {
                if (_glowAlpha >= 0f) return _glowAlpha;

                double sum = 0, weight = 0;
                foreach (var color in _data)
                {
                    var luminance = Luminance(color);
                    sum += luminance * color.a / 255f;
                    weight += luminance;
                }

                _glowAlpha = weight > 0 ? (float)(sum / weight) : 1f;
                return _glowAlpha;
            }
        }

        /// <summary>読み取れないテクスチャ（2D 以外、GPU が使えない環境）は null。</summary>
        public static TAOFallbackPixels Read(Texture texture)
        {
            if (texture == null || texture.dimension != TextureDimension.Tex2D) return null;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;

            var active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            var readable = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                return new TAOFallbackPixels(readable.GetPixels32());
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readable);
            }
        }
    }

    /// <summary>マテリアルが使われている面での平均を測るマスク。</summary>
    internal enum TAOFallbackMask
    {
        OutlineWidth,
        ShadowStrength,
        Emission,
    }

    /// <summary>
    /// 半透明のマテリアルを、どう表示するかの候補。候補ごとに、元の表示との差（再現の誤差）を測る。
    /// 「焼き込み」は、メインカラーと透明度をメインテクスチャに焼き込んだ場合、「そのまま」は、焼き込まない場合。
    /// </summary>
    internal enum TAOFallbackCandidate
    {
        /// <summary>不透明で表示する。</summary>
        Opaque,

        /// <summary>表示しない。</summary>
        Hidden,

        /// <summary>カットアウトで表示する（焼き込み）。</summary>
        CutoutBaked,

        /// <summary>カットアウトで表示する（そのまま）。</summary>
        CutoutRaw,

        /// <summary>Particle（乗算合成）で表示する（焼き込み）。</summary>
        ParticleBaked,

        /// <summary>Particle（乗算合成）で表示する（そのまま）。</summary>
        ParticleRaw,

        /// <summary>アルファブレンドで表示する（そのまま）。焼き込んだ場合は、元の表示と同じ合成になる。</summary>
        BlendRaw,
    }

    /// <summary>マテリアルが使われている面を、メッシュの UV とテクスチャから測った結果。値はどれも面積の割合。</summary>
    internal sealed class TAOFallbackSurfaceStats
    {
        public const int MaskCount = 3;
        public const int CandidateCount = 7;

        /// <summary>再現の誤差を測るときに想定する背景の数（暗い・中間・明るい）。</summary>
        public const int BackdropCount = 3;

        readonly float[] _masks = { 1f, 1f, 1f };
        readonly float[] _errors = new float[CandidateCount * BackdropCount];

        /// <summary>メッシュから測れたか。使われる場所が分からないマテリアルは false。</summary>
        public bool Valid;

        /// <summary>元の表示で、抜けて見えない部分の割合。</summary>
        public float Holes;

        /// <summary>元の表示で見えている部分の、平均の不透明度。</summary>
        public float VisibleAlpha;

        /// <summary>Cutout にフォールバックしたときに、元の表示と同じく抜ける部分の割合。</summary>
        public float CutoutHoles;

        /// <summary>Cutout にフォールバックしたときに、元の表示では見えているのに抜けてしまう部分の割合。</summary>
        public float CutoutWrong;

        /// <summary>Cutout にフォールバックしたときに、元の表示では抜けているのに表示されてしまう部分の割合。</summary>
        public float CutoutExtra;

        /// <summary>メインテクスチャのアルファの平均。フォールバック先のシェーダーは、色のアルファやアルファマスクを使わない。</summary>
        public float TextureAlpha;

        /// <summary>
        /// メインテクスチャのアルファと、元の表示の不透明度との差の平均。
        /// 0 なら、メインテクスチャのアルファだけで透け方が決まっている。
        /// </summary>
        public float AlphaError;

        /// <summary>見えている部分の、平均の明るさ（リニア）。</summary>
        public float Luminance;

        /// <summary>頂点カラーが白から離れている度合い。Particle は頂点カラーを掛けるので、色と不透明度が変わる。</summary>
        public float VertexTint;

        /// <summary>体（頭と首を除く Humanoid ボーン）に付いている部分の割合。</summary>
        public float BodyFraction;

        /// <summary>再現の誤差を測ったか。半透明のマテリアルだけ測る。</summary>
        public bool HasErrors;

        /// <summary>元の表示で、面全体を平均した不透明度。</summary>
        public float Coverage => (1f - Holes) * VisibleAlpha;

        /// <summary>マスクのテクスチャの平均。テクスチャが無ければ 1。</summary>
        public float Mask(TAOFallbackMask mask) => _masks[(int)mask];

        public void SetMask(int index, float value) => _masks[index] = value;

        /// <summary>
        /// 候補で表示したときの、元の表示との差。0 は同じ、1 は白と黒ほどの差。見た目の明るさ（ガンマ）で比べる。
        /// 何が後ろにあるかは分からないので、背景が暗い場合・中間の場合・明るい場合の平均を取る。
        /// onSkin は、顔に重ねて表示するものなど、後ろが肌だと分かっている場合で、明るい背景を重く見る。
        /// </summary>
        public float Error(TAOFallbackCandidate candidate, bool onSkin = false)
        {
            var offset = (int)candidate * BackdropCount;
            return onSkin
                ? _errors[offset + 1] * 0.25f + _errors[offset + 2] * 0.75f
                : (_errors[offset] + _errors[offset + 1] + _errors[offset + 2]) / BackdropCount;
        }

        public void SetError(int index, float value) => _errors[index] = value;
    }

    /// <summary>
    /// マテリアルが使われているメッシュの UV でテクスチャを読み、実際に見えている部分の不透明度や抜けの割合を測る。
    /// テクスチャ全体ではなく、メッシュが使っている範囲だけを、面積で重み付けして数える。
    /// </summary>
    internal sealed class TAOFallbackSurfaceAnalyzer
    {
        // 元の表示で、これより薄い部分は見えないものとして扱う
        const float InvisibleAlpha = 0.02f;

        static readonly Vector3[][] SamplePatterns = BuildSamplePatterns(5);

        readonly TAOFallbackAvatarContext _avatar;
        readonly Dictionary<Texture, TAOFallbackPixels> _pixels = new Dictionary<Texture, TAOFallbackPixels>();
        readonly Dictionary<Mesh, MeshData> _meshes = new Dictionary<Mesh, MeshData>();
        readonly Dictionary<Transform[], TAOFallbackRegion[]> _boneRegions = new Dictionary<Transform[], TAOFallbackRegion[]>();
        readonly Dictionary<Material, TAOFallbackSurfaceStats> _stats = new Dictionary<Material, TAOFallbackSurfaceStats>();

        public TAOFallbackSurfaceAnalyzer(TAOFallbackAvatarContext avatar) => _avatar = avatar;

        public TAOFallbackPixels Pixels(Texture texture)
        {
            if (texture == null) return null;
            if (_pixels.TryGetValue(texture, out var pixels)) return pixels;

            try
            {
                pixels = TAOFallbackPixels.Read(texture);
            }
            catch (Exception)
            {
                pixels = null;
            }

            _pixels[texture] = pixels;
            return pixels;
        }

        public TAOFallbackSurfaceStats Measure(TAOFallbackMaterialInfo info)
        {
            if (_stats.TryGetValue(info.Material, out var stats)) return stats;

            stats = new TAOFallbackSurfaceStats();
            try
            {
                var sampler = CreateSampler(info);
                foreach (var slot in _avatar.SlotsOf(info.Material)) SampleSlot(slot, sampler);
                sampler.WriteTo(stats);
            }
            catch (Exception)
            {
                // 読み取れないメッシュがあった場合は、測れなかったものとして扱う
                stats = new TAOFallbackSurfaceStats();
            }

            _stats[info.Material] = stats;
            return stats;
        }

        Sampler CreateSampler(TAOFallbackMaterialInfo info)
        {
            var material = info.Material;
            var color = TAOMaterialProps.ShaderColor(material, "_Color", Color.white);
            var sampler = new Sampler
            {
                Main = Pixels(info.MainTexture),
                MainScale = info.MainScale,
                MainOffset = info.MainOffset,
                ColorAlpha = Mathf.Clamp01(info.Color.a),
                ColorRed = Mathf.Clamp01(color.r),
                ColorGreen = Mathf.Clamp01(color.g),
                ColorBlue = Mathf.Clamp01(color.b),
                CutoutThreshold = info.Cutoff,

                // 不透明はアルファを使わない。カットアウトは閾値以上がすべて見え、半透明は閾値以上がアルファの濃さで見える。
                // lilToon は、半透明でも閾値（Cutoff）より薄い部分を描かない（屈折を除く）。
                // ほかのシェーダーの閾値は、カットアウトのときだけ使われる
                UsesAlpha = info.IsTranslucent || info.IsCutout,
                IsBlended = info.IsTranslucent,
                MeasuresErrors = info.IsTranslucent,
                VisibleThreshold = !info.IsTranslucent ? info.Cutoff
                    : info.IsLilToon && info.Mode != TAOFallbackSourceMode.Refraction ? Mathf.Max(info.Cutoff, InvisibleAlpha)
                    : InvisibleAlpha,
            };

            if (!info.IsLilToon) return sampler;

            sampler.MaskMode = Mathf.RoundToInt(TAOMaterialProps.Float(material, "_AlphaMaskMode"));
            if (sampler.MaskMode != 0)
            {
                sampler.Mask = Pixels(TAOMaterialProps.Texture(material, "_AlphaMask"));
                sampler.MaskScale = TAOMaterialProps.Scale(material, "_AlphaMask");
                sampler.MaskOffset = TAOMaterialProps.Offset(material, "_AlphaMask");
                sampler.MaskMultiply = TAOMaterialProps.Float(material, "_AlphaMaskScale", 1f);
                sampler.MaskAdd = TAOMaterialProps.Float(material, "_AlphaMaskValue");
            }

            if (info.HasOutline)
                sampler.AddProbe(TAOFallbackMask.OutlineWidth, Pixels(TAOMaterialProps.Texture(material, "_OutlineWidthMask")));

            if (TAOMaterialProps.IsOn(material, "_UseShadow") && !TAOMaterialProps.IsOn(material, "_ShadowMaskType"))
            {
                sampler.AddProbe(TAOFallbackMask.ShadowStrength,
                    Pixels(TAOMaterialProps.Texture(material, "_ShadowStrengthMask")));
            }

            if (TAOMaterialProps.IsOn(material, "_UseEmission"))
            {
                // 発光のマスクは、色とアルファの両方が発光の強さに掛かる
                sampler.AddProbe(TAOFallbackMask.Emission,
                    Pixels(TAOMaterialProps.Texture(material, "_EmissionBlendMask")),
                    TAOMaterialProps.Scale(material, "_EmissionBlendMask"),
                    TAOMaterialProps.Offset(material, "_EmissionBlendMask"), true);
            }

            return sampler;
        }

        void SampleSlot(TAOFallbackSlot slot, Sampler sampler)
        {
            var mesh = GetMesh(slot.Mesh);
            var triangles = mesh.TrianglesOf(slot.Mesh, slot.SubMesh);
            if (triangles == null) return;

            var vertices = mesh.Vertices;
            var uv = mesh.UV;
            var hasUV = uv.Length == vertices.Length;

            // スキンメッシュは、頂点が最も強く従うボーンで部位を決める
            TAOFallbackRegion[] boneRegions = null;
            BoneWeight[] weights = null;
            if (slot.Bones != null)
            {
                weights = mesh.WeightsOf(slot.Mesh);
                if (weights.Length == vertices.Length) boneRegions = RegionsOf(slot.Bones);
            }

            var anchorRegion = _avatar.RegionOf(slot.Anchor);
            var texels = TAOFallbackPixels.Size * TAOFallbackPixels.Size *
                         Mathf.Abs(sampler.MainScale.x * sampler.MainScale.y);

            // Particle は頂点カラーを掛けるので、再現の誤差を測るときは頂点カラーも読む
            var colors = sampler.MeasuresErrors ? mesh.ColorsOf(slot.Mesh) : null;
            var tint = Color.white;

            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i0 = triangles[i], i1 = triangles[i + 1], i2 = triangles[i + 2];
                var area = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]).magnitude * 0.5f;
                if (!(area > 0f)) continue;

                var uv0 = hasUV ? uv[i0] : Vector2.zero;
                var uv1 = hasUV ? uv[i1] : Vector2.zero;
                var uv2 = hasUV ? uv[i2] : Vector2.zero;

                var region = anchorRegion;
                if (boneRegions != null)
                {
                    var bone = weights[i0].boneIndex0;
                    if (bone >= 0 && bone < boneRegions.Length) region = boneRegions[bone];
                }

                // テクスチャ上で大きい三角形ほど、多くの点で読む
                var uvArea = Mathf.Abs((uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv2.x - uv0.x) * (uv1.y - uv0.y)) * 0.5f * texels;
                var pattern = SamplePatterns[uvArea < 2f ? 0 : uvArea < 8f ? 1 : uvArea < 32f ? 2 : uvArea < 128f ? 3 : 4];
                var weight = area / pattern.Length;

                Color color0 = default, color1 = default, color2 = default;
                if (colors != null)
                {
                    color0 = colors[i0];
                    color1 = colors[i1];
                    color2 = colors[i2];
                }

                foreach (var point in pattern)
                {
                    if (colors != null) tint = color0 * point.x + color1 * point.y + color2 * point.z;
                    sampler.Add(
                        uv0.x * point.x + uv1.x * point.y + uv2.x * point.z,
                        uv0.y * point.x + uv1.y * point.y + uv2.y * point.z,
                        weight, region, tint);
                }
            }
        }

        MeshData GetMesh(Mesh mesh)
        {
            if (_meshes.TryGetValue(mesh, out var data)) return data;
            data = new MeshData { Vertices = mesh.vertices, UV = mesh.uv };
            _meshes[mesh] = data;
            return data;
        }

        TAOFallbackRegion[] RegionsOf(Transform[] bones)
        {
            if (_boneRegions.TryGetValue(bones, out var regions)) return regions;
            regions = new TAOFallbackRegion[bones.Length];
            for (var i = 0; i < bones.Length; i++) regions[i] = _avatar.RegionOf(bones[i]);
            _boneRegions[bones] = regions;
            return regions;
        }

        // 三角形を 4 つの同じ面積の三角形に分ける操作を繰り返し、それぞれの重心（重心座標）を読む点にする
        static Vector3[][] BuildSamplePatterns(int levels)
        {
            var patterns = new Vector3[levels][];
            var triangles = new List<Vector3[]> { new[] { Vector3.right, Vector3.up, Vector3.forward } };

            for (var level = 0; level < levels; level++)
            {
                patterns[level] = new Vector3[triangles.Count];
                for (var i = 0; i < triangles.Count; i++)
                    patterns[level][i] = (triangles[i][0] + triangles[i][1] + triangles[i][2]) / 3f;

                var divided = new List<Vector3[]>(triangles.Count * 4);
                foreach (var triangle in triangles)
                {
                    Vector3 a = triangle[0], b = triangle[1], c = triangle[2];
                    Vector3 ab = (a + b) * 0.5f, bc = (b + c) * 0.5f, ca = (c + a) * 0.5f;
                    divided.Add(new[] { a, ab, ca });
                    divided.Add(new[] { ab, b, bc });
                    divided.Add(new[] { ca, bc, c });
                    divided.Add(new[] { ab, bc, ca });
                }

                triangles = divided;
            }

            return patterns;
        }

        sealed class MeshData
        {
            public Vector3[] Vertices;
            public Vector2[] UV;
            BoneWeight[] _weights;
            Color32[] _colors;
            bool _hasReadColors;
            readonly Dictionary<int, int[]> _triangles = new Dictionary<int, int[]>();

            public int[] TrianglesOf(Mesh mesh, int subMesh)
            {
                if (_triangles.TryGetValue(subMesh, out var triangles)) return triangles;
                triangles = mesh.GetTopology(subMesh) == MeshTopology.Triangles ? mesh.GetTriangles(subMesh) : null;
                _triangles[subMesh] = triangles;
                return triangles;
            }

            public BoneWeight[] WeightsOf(Mesh mesh) => _weights ?? (_weights = mesh.boneWeights);

            /// <summary>頂点カラー。頂点カラーが無いメッシュと、すべて白のメッシュは null。</summary>
            public Color32[] ColorsOf(Mesh mesh)
            {
                if (_hasReadColors) return _colors;
                _hasReadColors = true;

                var colors = mesh.colors32;
                if (colors.Length != Vertices.Length) return null;

                foreach (var color in colors)
                {
                    if (color.r == 255 && color.g == 255 && color.b == 255 && color.a == 255) continue;
                    _colors = colors;
                    break;
                }

                return _colors;
            }
        }

        sealed class Probe
        {
            public TAOFallbackPixels Pixels;
            public Vector2 Scale;
            public Vector2 Offset;

            /// <summary>赤チャンネルではなく、色の明るさとアルファの積を読む。</summary>
            public bool ColorAndAlpha;

            public double Sum;
        }

        /// <summary>1 つのマテリアルについて、面積で重み付けした集計を行う。</summary>
        sealed class Sampler
        {
            // 再現の誤差を測るときの、背景の明るさ（リニア）。暗い・中間・明るい背景で、別々に測る
            static readonly float[] Backdrops = { 0.04f, 0.25f, 0.7f };
            static readonly float[] ChannelWeights = { 0.2126f, 0.7152f, 0.0722f };

            public TAOFallbackPixels Main;
            public Vector2 MainScale = Vector2.one;
            public Vector2 MainOffset;
            public float ColorAlpha = 1f;

            // マテリアルの色（リニア）
            public float ColorRed = 1f, ColorGreen = 1f, ColorBlue = 1f;

            public bool UsesAlpha;
            public bool IsBlended;
            public bool MeasuresErrors;
            public float VisibleThreshold;
            public float CutoutThreshold;

            // lilToon のアルファマスク（0: なし, 1: 置き換え, 2: 乗算, 3: 加算, 4: 減算）
            public int MaskMode;
            public TAOFallbackPixels Mask;
            public Vector2 MaskScale = Vector2.one;
            public Vector2 MaskOffset;
            public float MaskMultiply = 1f;
            public float MaskAdd;

            readonly Probe[] _probes = new Probe[TAOFallbackSurfaceStats.MaskCount];
            readonly double[] _errors =
                new double[TAOFallbackSurfaceStats.CandidateCount * TAOFallbackSurfaceStats.BackdropCount];
            readonly float[] _texture = new float[3];
            readonly float[] _tint = new float[3];
            double _total, _visible, _alpha, _cutoutHoles, _cutoutWrong, _cutoutExtra, _textureAlpha, _alphaError;
            double _luminance, _vertexTint, _body;

            public void AddProbe(TAOFallbackMask mask, TAOFallbackPixels pixels)
                => AddProbe(mask, pixels, Vector2.one, Vector2.zero, false);

            public void AddProbe(TAOFallbackMask mask, TAOFallbackPixels pixels, Vector2 scale, Vector2 offset,
                bool colorAndAlpha)
            {
                if (pixels == null) return;
                _probes[(int)mask] = new Probe
                {
                    Pixels = pixels, Scale = scale, Offset = offset, ColorAndAlpha = colorAndAlpha,
                };
            }

            public void Add(float u, float v, float weight, TAOFallbackRegion region, Color tint)
            {
                u = u * MainScale.x + MainOffset.x;
                v = v * MainScale.y + MainOffset.y;

                var textureAlpha = 1f;
                _texture[0] = _texture[1] = _texture[2] = 1f;
                if (Main != null)
                {
                    var color = Main.Sample(u, v);
                    textureAlpha = color.a / 255f;
                    _texture[0] = color.r / 255f;
                    _texture[1] = color.g / 255f;
                    _texture[2] = color.b / 255f;
                }

                var alpha = textureAlpha * ColorAlpha;
                if (MaskMode != 0)
                {
                    var mask = Mask != null
                        ? Mask.Sample(u * MaskScale.x + MaskOffset.x, v * MaskScale.y + MaskOffset.y).r / 255f
                        : 1f;
                    mask = Mathf.Clamp01(mask * MaskMultiply + MaskAdd);
                    alpha = MaskMode == 1 ? mask
                        : MaskMode == 2 ? alpha * mask
                        : MaskMode == 3 ? Mathf.Clamp01(alpha + mask)
                        : Mathf.Clamp01(alpha - mask);
                }

                // フォールバック先の Cutout は、メインテクスチャのアルファだけで抜く
                var visible = !UsesAlpha || alpha >= VisibleThreshold;
                var cutoutVisible = textureAlpha >= CutoutThreshold;

                // 元の表示の不透明度
                var opacity = !visible ? 0f : IsBlended ? alpha : 1f;

                _total += weight;
                if (visible)
                {
                    _visible += weight;
                    _alpha += weight * opacity;
                    _luminance += weight * (ChannelWeights[0] * _texture[0] * ColorRed +
                                            ChannelWeights[1] * _texture[1] * ColorGreen +
                                            ChannelWeights[2] * _texture[2] * ColorBlue);
                    if (!cutoutVisible) _cutoutWrong += weight;
                }
                else if (cutoutVisible)
                {
                    _cutoutExtra += weight;
                }
                else
                {
                    _cutoutHoles += weight;
                }

                _textureAlpha += weight * textureAlpha;
                _alphaError += weight * Mathf.Abs(textureAlpha - opacity);
                if (region == TAOFallbackRegion.Body) _body += weight;

                if (MeasuresErrors) AddErrors(weight, textureAlpha, opacity, cutoutVisible, tint);

                foreach (var probe in _probes)
                {
                    if (probe == null) continue;
                    var sample = probe.Pixels.Sample(u * probe.Scale.x + probe.Offset.x, v * probe.Scale.y + probe.Offset.y);
                    probe.Sum += weight * (probe.ColorAndAlpha
                        ? TAOFallbackPixels.Luminance(sample) * sample.a / 255f
                        : sample.r / 255f);
                }
            }

            /// <summary>
            /// 候補ごとに、背景に重ねた結果と元の表示との差を足す。
            /// 元の表示は、背景に、マテリアルの色を不透明度で重ねたもの。合成はリニアで行い、差は見た目の明るさ（ガンマ 2）で測る。
            /// </summary>
            void AddErrors(float weight, float textureAlpha, float opacity, bool cutoutVisible, Color tint)
            {
                _tint[0] = tint.r;
                _tint[1] = tint.g;
                _tint[2] = tint.b;
                _vertexTint += weight * (1f - Mathf.Min(Mathf.Min(tint.r, tint.g), Mathf.Min(tint.b, tint.a)));

                // Particle は、テクスチャの色と頂点カラーを掛けた色で、下の色を暗くする（乗算合成）
                var particleBakedAlpha = opacity * tint.a;
                var particleRawAlpha = textureAlpha * tint.a;

                for (var channel = 0; channel < 3; channel++)
                {
                    var texture = _texture[channel];
                    var original = texture * (channel == 0 ? ColorRed : channel == 1 ? ColorGreen : ColorBlue);
                    var originalRoot = Root(original);
                    var textureRoot = Root(texture);
                    var scale = weight * ChannelWeights[channel];

                    for (var i = 0; i < Backdrops.Length; i++)
                    {
                        var backdrop = Backdrops[i];
                        var backdropRoot = Root(backdrop);
                        var shown = Root(backdrop + opacity * (original - backdrop));

                        AddError(TAOFallbackCandidate.Opaque, i, scale, originalRoot - shown);
                        AddError(TAOFallbackCandidate.Hidden, i, scale, backdropRoot - shown);
                        AddError(TAOFallbackCandidate.CutoutBaked, i, scale, (opacity > 0f ? originalRoot : backdropRoot) - shown);
                        AddError(TAOFallbackCandidate.CutoutRaw, i, scale, (cutoutVisible ? textureRoot : backdropRoot) - shown);
                        AddError(TAOFallbackCandidate.ParticleBaked, i, scale,
                            Root(backdrop * (1f - particleBakedAlpha + particleBakedAlpha * original * _tint[channel])) - shown);
                        AddError(TAOFallbackCandidate.ParticleRaw, i, scale,
                            Root(backdrop * (1f - particleRawAlpha + particleRawAlpha * texture * _tint[channel])) - shown);
                        AddError(TAOFallbackCandidate.BlendRaw, i, scale,
                            Root(backdrop + textureAlpha * (texture - backdrop)) - shown);
                    }
                }
            }

            void AddError(TAOFallbackCandidate candidate, int backdrop, float scale, float difference)
                => _errors[(int)candidate * TAOFallbackSurfaceStats.BackdropCount + backdrop] += scale * Math.Abs(difference);

            static float Root(float value) => value > 0f ? (float)Math.Sqrt(value) : 0f;

            public void WriteTo(TAOFallbackSurfaceStats stats)
            {
                if (!(_total > 0)) return;

                stats.Valid = true;
                stats.Holes = (float)(1.0 - _visible / _total);
                stats.VisibleAlpha = _visible > 0 ? (float)(_alpha / _visible) : 0f;
                stats.CutoutHoles = (float)(_cutoutHoles / _total);
                stats.CutoutWrong = (float)(_cutoutWrong / _total);
                stats.CutoutExtra = (float)(_cutoutExtra / _total);
                stats.TextureAlpha = (float)(_textureAlpha / _total);
                stats.AlphaError = (float)(_alphaError / _total);
                stats.Luminance = _visible > 0 ? (float)(_luminance / _visible) : 0f;
                stats.BodyFraction = (float)(_body / _total);

                if (MeasuresErrors)
                {
                    stats.HasErrors = true;
                    stats.VertexTint = (float)(_vertexTint / _total);
                    for (var i = 0; i < _errors.Length; i++) stats.SetError(i, (float)(_errors[i] / _total));
                }

                for (var i = 0; i < _probes.Length; i++)
                    if (_probes[i] != null) stats.SetMask(i, (float)(_probes[i].Sum / _total));
            }
        }
    }
}
