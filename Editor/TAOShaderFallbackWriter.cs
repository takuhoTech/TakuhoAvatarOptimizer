using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TakuhoTech.TAO.Editor
{
    /// <summary>
    /// 複製したマテリアルに、フォールバックの設定を書き込む。
    /// 書き込むのは、VRCFallback タグと、現在のシェーダーが使わない（フォールバックしたときにだけ使われる）値。
    /// 現在のシェーダーが使う値を変えるのは、通常の表示が変わらないと分かっている場合だけ。
    /// </summary>
    internal static class TAOFallbackWriter
    {
        // Toon Standard の機能を切り替えるキーワード。フォールバックしたときの見た目を決めるので、すべて管理する
        static readonly string[] ToonStandardKeywords =
        {
            "USE_SPECULAR", "USE_MATCAP", "USE_DETAIL_MAPS", "USE_NORMAL_MAPS", "USE_OCCLUSION_MAP", "USE_AUDIOLINK",
            "USE_RIMLIGHT", "USE_HUE_SHIFT", "USE_COLOR_MASK",
        };

        static readonly PropertyInfo DisableDrawersProperty = typeof(EditorMaterialUtility).GetProperty(
            "disableApplyMaterialPropertyDrawers", BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>
        /// マテリアルをそのまま複製する。Unity は、マテリアルを作るときやシェーダーを変えるときに、
        /// プロパティの属性に応じてキーワードなどを書き換えることがあるので、その処理を止めて複製する。
        /// </summary>
        public static Material Clone(Material source)
        {
            Material clone;
            using (NoPropertyDrawers()) clone = new Material(source);

            // Material Variant は、親の値を引き継いだ通常のマテリアルにする
            clone.parent = null;
            clone.name = source.name;
            return clone;
        }

        public static IDisposable NoPropertyDrawers() => new PropertyDrawersScope();

        public static void Apply(Material material, TAOFallbackPlan plan, TAOFallbackTextures textures)
        {
            if (!plan.Changes) return;

            material.SetOverrideTag(TAOFallbackKinds.TagName, plan.Tag);

            if (plan.Bake != null) ApplyBake(material, plan.Bake, textures);

            if (plan.BlackenEmission && material.HasProperty("_EmissionColor"))
            {
                var color = material.GetColor("_EmissionColor");
                material.SetColor("_EmissionColor", new Color(0f, 0f, 0f, color.a));
            }

            var toonStandard = plan.ToonStandard;
            if (toonStandard?.Ramp != null)
            {
                var ramp = textures.Ramp(toonStandard.Ramp);
                if (ramp != null)
                {
                    material.SetTexture("_Ramp", ramp);
                    material.SetTextureScale("_Ramp", Vector2.one);
                    material.SetTextureOffset("_Ramp", Vector2.zero);
                }
            }

            using (var saved = new SavedProperties(material))
            {
                // 以前に使っていたシェーダーの値が残っていると、フォールバック先のシェーダーに使われてしまうので、取り除く
                if (plan.Kind != TAOFallbackKind.Custom) saved.RemoveUndeclared();
                if (toonStandard != null) WriteToonStandard(saved, toonStandard);
                if (plan.Standard != null) WriteStandard(saved, plan.Standard);
                saved.Apply();
            }
        }

        /// <summary>
        /// シェーダーが宣言していないテクスチャを取り除く。ビルドでは Unity が同じことを行うので、
        /// シーンプレビューでも、フォールバック先に届かないテクスチャが表示されないようにする。
        /// </summary>
        public static void RemoveUndeclaredTextures(Material material)
        {
            using (var saved = new SavedProperties(material))
            {
                saved.RemoveUndeclaredTextures();
                saved.Apply();
            }
        }

        // 焼き込んだ内容と同じ見た目になるように、焼き込んだ設定を無効にする
        static void ApplyBake(Material material, TAOFallbackBakeSpec bake, TAOFallbackTextures textures)
        {
            var baked = textures.Bake(bake);
            if (baked == null) return;

            material.SetTexture("_MainTex", baked);

            if (bake.ToneCorrection)
            {
                material.SetVector("_MainTexHSVG", TAOFallbackPlanner.DefaultHSVG);
                material.SetFloat("_MainGradationStrength", 0f);
                ClearTexture(material, "_MainGradationTex");
                ClearTexture(material, "_MainColorAdjustMask");
            }

            if (bake.Layer2) DisableLayer(material, "2nd");
            if (bake.Layer3) DisableLayer(material, "3rd");

            // 焼き込んだ色とアルファは、マテリアルの色から外す
            if (bake.BakeColor || bake.BakeAlpha)
            {
                var color = material.GetColor("_Color");
                material.SetColor("_Color", new Color(
                    bake.BakeColor ? 1f : color.r, bake.BakeColor ? 1f : color.g, bake.BakeColor ? 1f : color.b,
                    bake.BakeAlpha ? 1f : color.a));
            }

            // 「置き換え」のアルファマスクは、通常の表示でメインテクスチャと色のアルファを使わないので、設定を残す。
            // それ以外のアルファマスクは外して、焼き込んだアルファだけで同じ透け方にする
            if (bake.BakeAlpha && !bake.KeepAlphaMask && material.HasProperty("_AlphaMaskMode"))
            {
                material.SetFloat("_AlphaMaskMode", 0f);
                ClearTexture(material, "_AlphaMask");
            }
        }

        static void DisableLayer(Material material, string layer)
        {
            material.SetFloat("_UseMain" + layer + "Tex", 0f);
            ClearTexture(material, "_Main" + layer + "Tex");
            ClearTexture(material, "_Main" + layer + "BlendMask");
        }

        // 使われなくなったテクスチャの参照を外して、ビルドに含まれないようにする
        static void ClearTexture(Material material, string name)
        {
            if (material.HasProperty(name)) material.SetTexture(name, null);
        }

        static void WriteToonStandard(SavedProperties saved, TAOFallbackToonStandardPlan plan)
        {
            if (plan.WriteCulling) saved.SetFloat("_Culling", plan.Culling);
            saved.SetFloat("_ShadowBoost", plan.ShadowBoost);
            saved.SetFloat("_ShadowAlbedo", plan.ShadowAlbedo);
            saved.SetFloat("_MinBrightness", plan.MinBrightness);
            saved.SetFloat("_LimitBrightness", plan.LimitBrightness ? 1f : 0f);

            if (plan.WriteEmission)
            {
                saved.SetFloat("_EmissionStrength", plan.EmissionStrength);
                saved.SetFloat("_EmissionUV", plan.EmissionUV);
            }

            if (plan.HueShift) saved.SetFloat("_HueShift", plan.HueShiftAngle);

            if (plan.Outline)
            {
                saved.SetFloat("_OutlineThickness", plan.OutlineThickness);
                saved.SetFloat("_OutlineFromAlbedo", 0f);
            }

            foreach (var keyword in ToonStandardKeywords)
            {
                var enabled = keyword == "USE_NORMAL_MAPS" ? plan.NormalMap : keyword == "USE_HUE_SHIFT" && plan.HueShift;
                saved.SetKeyword(keyword, enabled);
            }
        }

        // VRChat は、Standard にフォールバックするときに、Standard と同じ名前の値を引き継ぐ。
        // 値やキーワードがどこまで引き継がれるかは分からないので、Unity の Standard の設定画面が書き込むものを、ひととおり書いておく
        static void WriteStandard(SavedProperties saved, TAOFallbackStandardPlan plan)
        {
            saved.SetFloat("_Mode", plan.Mode);
            saved.SetKeyword("_ALPHATEST_ON", plan.Mode == 1);
            saved.SetKeyword("_ALPHABLEND_ON", plan.Mode == 2);
            saved.SetKeyword("_ALPHAPREMULTIPLY_ON", plan.Mode == 3);
            if (!plan.Matte) return;

            // lilToon は Standard の光沢の値を持たないので、Standard の初期値（つやのある見た目）にならないようにする
            saved.SetFloat("_Glossiness", 0f);
            saved.SetFloat("_GlossMapScale", 0f);
            saved.SetFloat("_SpecularHighlights", 0f);
            saved.SetFloat("_GlossyReflections", 0f);
            saved.SetKeyword("_SPECULARHIGHLIGHTS_OFF", true);
            saved.SetKeyword("_GLOSSYREFLECTIONS_OFF", true);
        }

        sealed class PropertyDrawersScope : IDisposable
        {
            readonly bool _previous;

            public PropertyDrawersScope()
            {
                if (DisableDrawersProperty == null) return;
                _previous = (bool)DisableDrawersProperty.GetValue(null);
                DisableDrawersProperty.SetValue(null, true);
            }

            public void Dispose()
            {
                if (DisableDrawersProperty != null) DisableDrawersProperty.SetValue(null, _previous);
            }
        }

        /// <summary>
        /// マテリアルに保存されている値とキーワード。
        /// シェーダーが宣言していない値は Material の API では書けないので、シリアライズされた値を直接編集する。
        /// 宣言されていない float・色・キーワードは、ビルド後のマテリアルにも残り、フォールバック先のシェーダーに渡る。
        /// </summary>
        sealed class SavedProperties : IDisposable
        {
            readonly Shader _shader;
            readonly SerializedObject _serialized;
            readonly SerializedProperty _textures;
            readonly SerializedProperty _floats;
            readonly SerializedProperty _ints;
            readonly SerializedProperty _colors;
            readonly SerializedProperty _invalidKeywords;
            Dictionary<string, int> _floatIndices;

            public SavedProperties(Material material)
            {
                _shader = material.shader;
                _serialized = new SerializedObject(material);
                _textures = _serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                _floats = _serialized.FindProperty("m_SavedProperties.m_Floats");
                _ints = _serialized.FindProperty("m_SavedProperties.m_Ints");
                _colors = _serialized.FindProperty("m_SavedProperties.m_Colors");
                _invalidKeywords = _serialized.FindProperty("m_InvalidKeywords");
            }

            public void Apply() => _serialized.ApplyModifiedPropertiesWithoutUndo();

            public void Dispose() => _serialized.Dispose();

            bool IsDeclared(string name) => _shader.FindPropertyIndex(name) >= 0;

            /// <summary>現在のシェーダーが使う値は、通常の表示が変わるので書き換えない。</summary>
            public void SetFloat(string name, float value)
            {
                if (_floats == null || IsDeclared(name)) return;

                if (_floatIndices == null)
                {
                    _floatIndices = new Dictionary<string, int>();
                    for (var i = 0; i < _floats.arraySize; i++)
                        _floatIndices[_floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue] = i;
                }

                if (!_floatIndices.TryGetValue(name, out var index))
                {
                    index = _floats.arraySize;
                    _floats.InsertArrayElementAtIndex(index);
                    _floats.GetArrayElementAtIndex(index).FindPropertyRelative("first").stringValue = name;
                    _floatIndices[name] = index;
                }

                _floats.GetArrayElementAtIndex(index).FindPropertyRelative("second").floatValue = value;
            }

            /// <summary>現在のシェーダーが使うキーワードには触れない。</summary>
            public void SetKeyword(string keyword, bool enabled)
            {
                if (_invalidKeywords == null || _shader.keywordSpace.FindKeyword(keyword).isValid) return;

                var index = -1;
                for (var i = 0; i < _invalidKeywords.arraySize; i++)
                {
                    if (_invalidKeywords.GetArrayElementAtIndex(i).stringValue != keyword) continue;
                    index = i;
                    break;
                }

                if (enabled && index < 0)
                {
                    _invalidKeywords.InsertArrayElementAtIndex(_invalidKeywords.arraySize);
                    _invalidKeywords.GetArrayElementAtIndex(_invalidKeywords.arraySize - 1).stringValue = keyword;
                }
                else if (!enabled && index >= 0)
                {
                    _invalidKeywords.DeleteArrayElementAtIndex(index);
                }
            }

            /// <summary>以前のシェーダーが残した値とキーワードを取り除く。</summary>
            public void RemoveUndeclared()
            {
                RemoveUndeclared(_textures);
                RemoveUndeclared(_floats);
                RemoveUndeclared(_ints);
                RemoveUndeclared(_colors);
                if (_invalidKeywords != null) _invalidKeywords.ClearArray();
                _floatIndices = null;
            }

            public void RemoveUndeclaredTextures() => RemoveUndeclared(_textures);

            void RemoveUndeclared(SerializedProperty properties)
            {
                if (properties == null) return;
                for (var i = properties.arraySize - 1; i >= 0; i--)
                {
                    var name = properties.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                    if (!IsDeclared(name)) properties.DeleteArrayElementAtIndex(i);
                }
            }
        }
    }
}
