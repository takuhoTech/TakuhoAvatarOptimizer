using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// コンポーネントの設定の写し。ビルドでは、AAO に検出されないように早い段階でコンポーネントを削除するので、
    /// 設定はこの写しで持ち運ぶ。
    /// </summary>
    internal sealed class TAOShaderFallbackSettings
    {
        public TAOFallbackShader DefaultFallback;
        public TAOFallbackShadow Shadow;
        public TAOFallbackOutline Outline;
        public bool BakeColor;
        public bool BakeAlpha;
        public bool AutoMinBrightness;
        public float MinBrightness;
        public bool AutoLimitBrightness;
        public bool LimitBrightness;
        public bool OverwriteExisting;

        readonly Dictionary<Material, TAOFallbackMaterialOverride> _overrides =
            new Dictionary<Material, TAOFallbackMaterialOverride>();

        /// <summary>設定の内容を表す文字列。設定が変わったかどうかを調べるために使う。</summary>
        public string Signature { get; private set; } = "";

        public static TAOShaderFallbackSettings From(TAOOptimizeVRCShaderFallback component)
        {
            var settings = new TAOShaderFallbackSettings
            {
                DefaultFallback = component.defaultFallback,
                Shadow = component.shadow,
                Outline = component.outline,
                BakeColor = component.bakeColor,
                BakeAlpha = component.bakeAlpha,
                AutoMinBrightness = component.autoMinBrightness,
                MinBrightness = Mathf.Clamp(component.minBrightness, 0f, TAOOptimizeVRCShaderFallback.MaxMinBrightness),
                AutoLimitBrightness = component.autoLimitBrightness,
                LimitBrightness = component.limitBrightness,
                OverwriteExisting = component.overwriteExisting,
            };

            var signature = new StringBuilder();
            signature.Append((int)settings.DefaultFallback).Append(',').Append((int)settings.Shadow).Append(',')
                .Append((int)settings.Outline).Append(',')
                .Append(settings.BakeColor ? 1 : 0).Append(settings.BakeAlpha ? 1 : 0)
                .Append(settings.AutoMinBrightness ? 1 : 0).Append(settings.AutoLimitBrightness ? 1 : 0)
                .Append(settings.LimitBrightness ? 1 : 0).Append(settings.OverwriteExisting ? 1 : 0).Append(',')
                .Append(settings.MinBrightness.ToString("R", CultureInfo.InvariantCulture));

            if (component.materialOverrides != null)
            {
                foreach (var entry in component.materialOverrides)
                {
                    if (entry == null || entry.material == null || entry.target == TAOFallbackTarget.Auto) continue;
                    settings._overrides[entry.material] = new TAOFallbackMaterialOverride
                    {
                        material = entry.material,
                        target = entry.target,
                        customTag = entry.customTag ?? "",
                    };
                    signature.Append(';').Append(entry.material.GetInstanceID()).Append(':').Append((int)entry.target)
                        .Append(':').Append(entry.customTag);
                }
            }

            settings.Signature = signature.ToString();
            return settings;
        }

        /// <summary>
        /// マテリアルごとの指定。ほかのツールがビルド中に複製したマテリアルは、複製元（original）で探す。
        /// 指定が無ければ null。
        /// </summary>
        public TAOFallbackMaterialOverride FindOverride(Material material, Material original)
        {
            if (_overrides.Count == 0) return null;
            if (material != null && _overrides.TryGetValue(material, out var found)) return found;
            if (original != null && _overrides.TryGetValue(original, out found)) return found;
            return null;
        }
    }
}
