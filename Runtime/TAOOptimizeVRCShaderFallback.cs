using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace TakuhoTech.TAO
{
    /// <summary>基本のフォールバック先。</summary>
    public enum TAOFallbackShader
    {
        ToonStandard = 0,
        Unlit = 1,
        Toon = 2,
    }

    /// <summary>Toon Standard にフォールバックしたときの影。</summary>
    public enum TAOFallbackShadow
    {
        /// <summary>元のマテリアルの影の設定から、Shadow Ramp を生成する。</summary>
        FromMaterial = 0,

        // 以下は Toon Standard の Shading のプリセット
        Flat = 1,
        Realistic = 2,
        RealisticSoft = 3,
        RealisticVerySoft = 4,
        Toon2Band = 5,
        Toon3Band = 6,
        Toon4Band = 7,
    }

    /// <summary>輪郭線のあるマテリアルを、Toon Standard Outline にフォールバックさせる条件。</summary>
    public enum TAOFallbackOutline
    {
        None = 0,

        /// <summary>暗い色の輪郭線だけ。Toon Standard の輪郭線は光の影響を受けないので、明るい色は暗い場所で光って見える。</summary>
        DarkOnly = 1,

        All = 2,
    }

    /// <summary>マテリアルごとに指定するフォールバック先。</summary>
    public enum TAOFallbackTarget
    {
        /// <summary>自動で決める。</summary>
        Auto = 0,

        /// <summary>このマテリアルには何もしない。</summary>
        Keep = 1,

        ToonStandard = 10,
        ToonStandardOutline = 11,
        Toon = 20,
        ToonCutout = 21,
        Unlit = 30,
        UnlitCutout = 31,
        UnlitTransparent = 32,
        Matcap = 40,
        Particle = 50,
        Hidden = 60,
        Standard = 70,
        StandardCutout = 71,
        StandardFade = 72,
        StandardTransparent = 73,

        /// <summary>VRCFallback タグを文字列で直接指定する。</summary>
        Custom = 100,
    }

    /// <summary>マテリアルごとのフォールバック先の指定。</summary>
    [Serializable]
    public class TAOFallbackMaterialOverride
    {
        public Material material;
        public TAOFallbackTarget target = TAOFallbackTarget.Auto;

        /// <summary>target が Custom のときに設定する VRCFallback タグ。</summary>
        public string customTag = "";
    }

    /// <summary>
    /// VRChat の Safety でカスタムシェーダーが無効化されたときの見た目が、元の見た目に近くなるように、
    /// マテリアルごとに VRCFallback を設定する。元のマテリアルとテクスチャのアセットは変更しない。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("TAO/TAO Optimize VRC Shader Fallback")]
    public class TAOOptimizeVRCShaderFallback : MonoBehaviour, IEditorOnly
    {
        /// <summary>Toon Standard の Min Brightness の上限。</summary>
        public const float MaxMinBrightness = 0.1f;

        public TAOFallbackShader defaultFallback = TAOFallbackShader.ToonStandard;

        /// <summary>Toon Standard の影。影が無効のマテリアルには、設定にかかわらず影を付けない。</summary>
        public TAOFallbackShadow shadow = TAOFallbackShadow.FromMaterial;

        public TAOFallbackOutline outline = TAOFallbackOutline.DarkOnly;

        /// <summary>
        /// メインカラー（色・色調補正・2nd・3rd）を、メインテクスチャに焼き込む。
        /// ビルド時に複製したマテリアルのメインテクスチャを、焼き込んだテクスチャに差し替える。
        /// </summary>
        public bool bakeColor = true;

        /// <summary>
        /// 透明度（色のアルファとアルファマスク）を、メインテクスチャのアルファに焼き込む。
        /// フォールバック先のシェーダーは、メインテクスチャのアルファしか使わないため。
        /// </summary>
        public bool bakeAlpha;

        /// <summary>明るさの下限（Toon Standard の Min Brightness）。自動なら、元のマテリアルの設定から決める。</summary>
        public bool autoMinBrightness = true;

        [Range(0f, MaxMinBrightness)] public float minBrightness;

        /// <summary>明るさの上限（Toon Standard の Limit Brightness）。自動なら、元のマテリアルの設定から決める。</summary>
        public bool autoLimitBrightness = true;

        public bool limitBrightness = true;

        /// <summary>マテリアルに設定済みの VRCFallback を決め直す。無効なら、設定済みのマテリアルには何もしない。</summary>
        public bool overwriteExisting = true;

        public List<TAOFallbackMaterialOverride> materialOverrides = new List<TAOFallbackMaterialOverride>();
    }
}
