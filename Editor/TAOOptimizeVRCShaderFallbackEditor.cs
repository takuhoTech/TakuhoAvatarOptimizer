using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.runtime;
using UnityEditor;
using UnityEngine;
using static TakuhoTech.TAO.Editor.TAOLocalization;

namespace TakuhoTech.TAO.Editor
{
    [CustomEditor(typeof(TAOOptimizeVRCShaderFallback))]
    internal class TAOOptimizeVRCShaderFallbackEditor : UnityEditor.Editor
    {
        const float MinTargetWidth = 56f;
        const float MaxTargetWidth = 138f;
        const float NoteWidth = 18f;
        const float MinShaderWidth = 60f;
        const float MaxShaderShare = 0.55f;
        const string GroupStateKey = "TAO.ShaderFallback.Group.";

        static readonly string[] FallbackNames = { "Toon Standard", "Unlit", "Toon" };

        // マテリアルごとの指定で選べる項目
        static readonly TAOFallbackTarget[] Targets =
        {
            TAOFallbackTarget.Auto, TAOFallbackTarget.Keep, TAOFallbackTarget.ToonStandard,
            TAOFallbackTarget.ToonStandardOutline, TAOFallbackTarget.Toon, TAOFallbackTarget.ToonCutout,
            TAOFallbackTarget.Unlit, TAOFallbackTarget.UnlitCutout, TAOFallbackTarget.UnlitTransparent,
            TAOFallbackTarget.Standard, TAOFallbackTarget.StandardCutout, TAOFallbackTarget.StandardFade,
            TAOFallbackTarget.StandardTransparent, TAOFallbackTarget.Matcap, TAOFallbackTarget.Particle,
            TAOFallbackTarget.Hidden, TAOFallbackTarget.Custom,
        };

        SerializedProperty _defaultFallback;
        SerializedProperty _shadow;
        SerializedProperty _outline;
        SerializedProperty _bakeColor;
        SerializedProperty _bakeAlpha;
        SerializedProperty _autoMinBrightness;
        SerializedProperty _minBrightness;
        SerializedProperty _autoLimitBrightness;
        SerializedProperty _limitBrightness;
        SerializedProperty _overwriteExisting;
        SerializedProperty _materialOverrides;

        // 「Preview Result」の結果。マテリアルの使われ方とテクスチャの計測は、再検出するまで使い回す
        bool _showResult;
        bool _rescan;
        TAOFallbackAvatarContext _avatar;
        TAOFallbackSurfaceAnalyzer _surfaces;
        string _planSignature;
        int _materialCount;
        int _warningCount;
        float _targetWidth = MinTargetWidth;
        readonly Dictionary<TAOFallbackKind, List<TAOFallbackPlan>> _groups =
            new Dictionary<TAOFallbackKind, List<TAOFallbackPlan>>();

        // シェーダーの欄の幅。長い名前のシェーダーがほかのグループのマテリアルの欄を狭くしないように、グループごとに持つ
        readonly Dictionary<TAOFallbackKind, float> _shaderWidths = new Dictionary<TAOFallbackKind, float>();

        void OnEnable()
        {
            _defaultFallback = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.defaultFallback));
            _shadow = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.shadow));
            _outline = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.outline));
            _bakeColor = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.bakeColor));
            _bakeAlpha = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.bakeAlpha));
            _autoMinBrightness = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.autoMinBrightness));
            _minBrightness = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.minBrightness));
            _autoLimitBrightness = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.autoLimitBrightness));
            _limitBrightness = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.limitBrightness));
            _overwriteExisting = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.overwriteExisting));
            _materialOverrides = serializedObject.FindProperty(nameof(TAOOptimizeVRCShaderFallback.materialOverrides));
        }

        bool IsToonStandard => _defaultFallback.enumValueIndex == (int)TAOFallbackShader.ToonStandard;

        public override void OnInspectorGUI()
        {
            var component = (TAOOptimizeVRCShaderFallback)target;
            serializedObject.Update();

            DrawLanguagePopup();
            EditorGUILayout.HelpBox(T(
                "VRChat の Safety でカスタムシェーダーが無効化されたときの見た目を、元の見た目に近づけます。\n" +
                "ビルド時に複製したマテリアルに設定するため、元のマテリアルとテクスチャは変更しません。",
                "Makes the avatar look closer to the original when custom shaders are blocked by VRChat Safety.\n" +
                "The settings are written to materials cloned at build time, so the original materials and textures are not modified."),
                MessageType.Info);

            DrawSettings();
            DrawAdvancedSettings();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            var avatarRoot = RuntimeUtil.FindAvatarInParents(component.transform);
            if (avatarRoot == null)
            {
                EditorGUILayout.HelpBox(T("アバターの中に配置してください。", "Place this component inside an avatar."),
                    MessageType.Warning);
                return;
            }

            DrawPreviewButtons();
            if (_showResult) DrawResult(component, avatarRoot.gameObject);
            serializedObject.ApplyModifiedProperties();
        }

        // ---- 設定 ----

        void DrawSettings()
        {
            EnumPopup(_defaultFallback, new GUIContent(T("基本のフォールバック先", "Default Fallback"),
                T("不透明のマテリアルのフォールバック先です。半透明やカットアウトのマテリアルも、同じ系統のフォールバック先にします。\n" +
                  "Toon Standard: 光の影響あり。影・輪郭線・両面描画・マテリアルの色を再現できます\n" +
                  "Toon: 光の影響あり。メインテクスチャを表示します\n" +
                  "Unlit: 光の影響なし。メインテクスチャを表示します。半透明を Unlit Transparent でそのまま表せます",
                  "The fallback of opaque materials. Translucent and cutout materials also fall back to the same family.\n" +
                  "Toon Standard: lit. Can reproduce shadows, outlines, double-sided rendering and the material color\n" +
                  "Toon: lit. Displays the main texture\n" +
                  "Unlit: not lit. Displays the main texture. Translucent materials can be shown as they are with Unlit Transparent")),
                FallbackNames);

            if (IsToonStandard)
            {
                EnumPopup(_shadow, new GUIContent(T("影", "Shadow"),
                    T("Toon Standard の影（Shadow Ramp）です。影が無効のマテリアルには、影を付けません。\n" +
                      "マテリアルの影を再現: lilToon の影の設定（色・範囲・ぼかし・コントラスト）から、マテリアルごとに Shadow Ramp を生成します\n" +
                      "そのほか: lilToon（Lite を除く）のマテリアルに、Toon Standard のプリセットの影を使います。そのほかのマテリアルには影を付けません",
                      "The shadow (Shadow Ramp) of Toon Standard. Materials with shadows disabled get no shadow.\n" +
                      "From Material: generates a Shadow Ramp for each material from the shadow settings of lilToon (colors, borders, blur and contrast)\n" +
                      "Others: uses a preset shadow of Toon Standard for lilToon materials (except Lite). Other materials get no shadow")),
                    new[]
                    {
                        T("マテリアルの影を再現", "From Material"), "Flat", "Realistic", "Realistic Soft", "Realistic Very Soft",
                        "Toon 2 Band", "Toon 3 Band", "Toon 4 Band",
                    });
                EnumPopup(_outline, new GUIContent(T("輪郭線", "Outline"),
                    T("輪郭線のあるマテリアルを Toon Standard Outline にフォールバックさせ、元の太さを設定します。\n" +
                      "暗い色だけ: Toon Standard の輪郭線は光の影響を受けないので、暗い場所で光って見える明るい色の輪郭線は対象にしません\n" +
                      "すべて: 輪郭線の色にかかわらず対象にします",
                      "Materials with an outline fall back to Toon Standard Outline with the original thickness.\n" +
                      "Dark Only: the outline of Toon Standard is not affected by light, so bright outlines, which would glow in dark places, are excluded\n" +
                      "All: applies regardless of the outline color")),
                    new[] { T("なし", "None"), T("暗い色だけ", "Dark Only"), T("すべて", "All") });
                DrawMinBrightness();
                DrawLimitBrightness();
            }
            else if (_defaultFallback.enumValueIndex == (int)TAOFallbackShader.Unlit)
            {
                EditorGUILayout.HelpBox(T(
                    "Unlit は両面描画に対応していないため、両面描画のマテリアルの裏面は表示されません。",
                    "Unlit does not support double-sided rendering, so the back faces of double-sided materials are not displayed."),
                    MessageType.Info);
            }
        }

        void DrawAdvancedSettings()
        {
            // 開閉の状態は、ほかのコンポーネントと同じく SerializedProperty の isExpanded で保持する
            _materialOverrides.isExpanded = EditorGUILayout.Foldout(_materialOverrides.isExpanded,
                T("詳細設定", "Advanced Settings"), true);
            if (!_materialOverrides.isExpanded) return;

            EditorGUI.indentLevel++;

            Toggle(_bakeColor, T("メインカラーを焼き込み", "Bake Main Color"),
                T("lilToon のメインカラー（色・色調補正・2nd・3rd）を、メインテクスチャに焼き込みます。フォールバック先のシェーダーは、メインテクスチャしか使わないためです（Toon Standard は色も使います）。\n" +
                  "焼き込みが必要なマテリアルだけ、ビルド時に複製したマテリアルのメインテクスチャを、焼き込んだテクスチャに差し替えます（元のアセットは変更しません）。" +
                  "通常の表示は、テクスチャの再圧縮や補間の違いによるわずかな差を除いて変わりません",
                  "Bakes the main color of lilToon (color, color adjustment, 2nd and 3rd) into the main texture, because the fallback shaders only use the main texture (Toon Standard also uses the color).\n" +
                  "Only for materials that need it, the main texture of the material cloned at build time is replaced with the baked one (the original assets are not modified). " +
                  "The normal appearance does not change except for slight differences caused by recompression and filtering"));
            Toggle(_bakeAlpha, T("透明度を焼き込み", "Bake Transparency"),
                T("lilToon の色のアルファとアルファマスクを、メインテクスチャのアルファに焼き込みます。フォールバック先のシェーダーは、メインテクスチャのアルファだけで透かしたり抜いたりするためです。\n" +
                  "半透明やカットアウトのフォールバック先になるマテリアルのうち、メインテクスチャのアルファだけでは透け方が変わるものが対象です。通常の表示は、テクスチャの再圧縮や補間の違いによるわずかな差を除いて変わりません。\n" +
                  "オフのときは、色のアルファやアルファマスクで透かしているマテリアルが、透けない表示や非表示に分類されることがあります。\n" +
                  "メインテクスチャがアルファを持たない形式（DXT1）の場合は、アルファを持つ形式（DXT5）で生成するので、そのテクスチャのメモリが 2 倍になります",
                  "Bakes the alpha of the color and the alpha mask of lilToon into the alpha of the main texture, because the fallback shaders use only the alpha of the main texture for transparency and cutout.\n" +
                  "Applies to materials that fall back to a translucent or cutout shader and whose transparency would change with the alpha of the main texture alone. The normal appearance does not change except for slight differences caused by recompression and filtering.\n" +
                  "When off, materials that are transparent through the color alpha or the alpha mask may be classified as opaque or hidden.\n" +
                  "When the main texture is in a format without alpha (DXT1), it is generated in a format with alpha (DXT5), so the memory of that texture doubles"));
            Toggle(_overwriteExisting, T("設定済みのフォールバック先を上書き", "Overwrite Existing Fallback"),
                T("マテリアルに VRCFallback が設定済みでも、フォールバック先を決め直して上書きします。オフにすると、設定済みのマテリアルは変更しません。",
                  "Also decides the fallback for materials that already have a VRCFallback and overwrites it. When off, materials that already have a VRCFallback are left unchanged."));

            EditorGUI.indentLevel--;
        }

        void DrawMinBrightness()
        {
            TAOGUI.SplitFieldAndPopup(EditorGUILayout.GetControlRect(), out var fieldRect, out var popupRect);
            var label = new GUIContent(T("明るさの下限", "Min Brightness"),
                T("暗い場所でも保つ明るさ（Toon Standard の Min Brightness）です。自動: lilToon の「明るさの下限」から、マテリアルごとに決めます",
                  "The brightness kept even in dark places (Min Brightness of Toon Standard). Auto: decided for each material from the Light Min Limit of lilToon"));

            if (_autoMinBrightness.boolValue)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(fieldRect, label, T("マテリアルごと", "Per material"));
            }
            else
            {
                EditorGUI.Slider(fieldRect, _minBrightness, 0f, TAOOptimizeVRCShaderFallback.MaxMinBrightness, label);
            }

            var isAuto = TAOGUI.AutoPopup(popupRect, _autoMinBrightness.boolValue);
            if (isAuto != _autoMinBrightness.boolValue) _autoMinBrightness.boolValue = isAuto;
        }

        void DrawLimitBrightness()
        {
            TAOGUI.SplitFieldAndPopup(EditorGUILayout.GetControlRect(), out var fieldRect, out var popupRect);
            var label = new GUIContent(T("明るさの上限", "Limit Brightness"),
                T("強い光でも白飛びしないように、明るさを制限します（Toon Standard の Limit Brightness）。自動: lilToon の「明るさの上限」が 1 以下のマテリアルで制限します",
                  "Limits the brightness so that strong light does not blow out the colors (Limit Brightness of Toon Standard). Auto: limited for materials whose Light Max Limit of lilToon is 1 or less"));

            if (_autoLimitBrightness.boolValue)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(fieldRect, label, T("マテリアルごと", "Per material"));
            }
            else
            {
                EditorGUI.PropertyField(fieldRect, _limitBrightness, label);
            }

            var isAuto = TAOGUI.AutoPopup(popupRect, _autoLimitBrightness.boolValue);
            if (isAuto != _autoLimitBrightness.boolValue) _autoLimitBrightness.boolValue = isAuto;
        }

        static void EnumPopup(SerializedProperty property, GUIContent label, string[] names)
        {
            var rect = EditorGUILayout.GetControlRect();
            label = EditorGUI.BeginProperty(rect, label, property);
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.Popup(rect, label, property.enumValueIndex,
                names.Select(name => new GUIContent(name)).ToArray());
            if (EditorGUI.EndChangeCheck()) property.enumValueIndex = selected;
            EditorGUI.EndProperty();
        }

        // 項目名が長いので、チェックボックスを左に置いて、項目名が切れないようにする
        static void Toggle(SerializedProperty property, string label, string tooltip)
        {
            var rect = EditorGUILayout.GetControlRect();
            var content = EditorGUI.BeginProperty(rect, new GUIContent(label, tooltip), property);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.ToggleLeft(rect, content, property.boolValue);
            if (EditorGUI.EndChangeCheck()) property.boolValue = value;
            EditorGUI.EndProperty();
        }

        // ---- プレビュー ----

        // Preview Fallback を押しても表示されない理由。表示されるときは null
        static string PreviewUnavailableReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return T("再生中は、プレビューは表示されません。", "The preview is not shown in Play Mode.");

            if (PreviewSession.Current == null)
            {
                return T(
                    "NDMF のプレビューが無効になっているため、表示されません。メニューの Tools > NDM Framework > Enable Previews を有効にしてください。",
                    "Nothing is shown because the NDMF preview is disabled. Enable Tools > NDM Framework > Enable Previews in the menu.");
            }

            return null;
        }

        void DrawPreviewButtons()
        {
            var preview = TAOShaderFallbackPreviewFilter.Toggle.IsEnabled;
            var isPreviewing = GUILayout.Toggle(preview.Value, new GUIContent(T("フォールバックをプレビュー", "Preview Fallback"),
                T("Safety でシェーダーが無効化されたときの見た目を、シーン上に表示します（NDMF のプレビュー）。もう一度押すと元に戻ります",
                  "Shows in the scene how the avatar looks when shaders are blocked by Safety (NDMF preview). Press again to restore")),
                "Button");
            if (isPreviewing != preview.Value) preview.Value = isPreviewing;

            var unavailable = preview.Value ? PreviewUnavailableReason() : null;
            if (unavailable != null) EditorGUILayout.HelpBox(unavailable, MessageType.Warning);

            var showResult = GUILayout.Toggle(_showResult, new GUIContent(T("検出結果を表示", "Preview Result"),
                T("検出したマテリアルと、設定されるフォールバック先を表示します。EditorOnly のオブジェクトのマテリアルは含みません",
                  "Shows the detected materials and the fallback to be set. Materials of EditorOnly objects are not included")),
                "Button");
            if (showResult == _showResult) return;

            // 表示するたびに、アバターの現在の状態から検出する
            _showResult = showResult;
            _rescan = true;
        }

        void DrawResult(TAOOptimizeVRCShaderFallback component, GameObject avatarRoot)
        {
            // 計算は Layout イベントだけで行い、Layout と Repaint で描画する内容をそろえる
            if (Event.current.type == EventType.Layout) UpdatePlans(component, avatarRoot);
            if (_avatar == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                var summary = T($"検出したマテリアル: {_materialCount}", $"Detected materials: {_materialCount}");
                GUILayout.Label(summary, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

                if (_warningCount > 0)
                {
                    var warning = new GUIContent(
                        T($"見た目が大きく変わる: {_warningCount}", $"Looks very different: {_warningCount}"),
                        EditorGUIUtility.IconContent("console.warnicon.sml").image,
                        T("フォールバックしたときに、見た目が元から大きく変わるマテリアルの数です。一覧で同じアイコンの付いた行にカーソルを置くと、理由を確認できます。行の右のポップアップで、フォールバック先を指定できます",
                          "The number of materials that look very different from the original when falling back. Hover over the rows with the same icon in the list to see the reason. The fallback can be specified with the popup on the right of each row"));
                    GUILayout.Label(warning, EditorStyles.miniLabel, GUILayout.ExpandWidth(false),
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(T("再検出", "Rescan"),
                        T("アバターのマテリアルやメッシュを変更したあとに、検出をやり直します",
                          "Detects again after the materials or meshes of the avatar are changed")),
                        EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    // 次の Layout イベントで検出し直す
                    _planSignature = null;
                    _rescan = true;
                }
            }

            var targetLabels = Targets.Select(TargetLabel).ToArray();
            foreach (var kind in TAOFallbackKinds.DisplayOrder)
            {
                if (!_groups.TryGetValue(kind, out var plans)) continue;

                // 見出しを開閉するだけで、再計算はしない
                var key = GroupStateKey + kind;
                var isExpanded = SessionState.GetBool(key, kind != TAOFallbackKind.Unchanged);
                var expand = EditorGUILayout.Foldout(isExpanded, $"{TAOFallbackKinds.Label(kind)} ({plans.Count})", true);
                if (expand != isExpanded) SessionState.SetBool(key, expand);
                if (!expand) continue;

                _shaderWidths.TryGetValue(kind, out var shaderWidth);
                EditorGUI.indentLevel++;
                foreach (var plan in plans) DrawRow(plan, targetLabels, shaderWidth);
                EditorGUI.indentLevel--;
            }
        }

        // 設定が変わったときだけ、フォールバック先を決め直す
        void UpdatePlans(TAOOptimizeVRCShaderFallback component, GameObject avatarRoot)
        {
            var settings = TAOShaderFallbackSettings.From(component);
            if (_avatar == null || _rescan || settings.Signature != _planSignature)
            {
                if (_avatar == null || _rescan)
                {
                    _avatar = TAOFallbackAvatarContext.FromScene(avatarRoot);
                    _surfaces = new TAOFallbackSurfaceAnalyzer(_avatar);
                    _rescan = false;
                }

                var planner = new TAOFallbackPlanner(settings, _avatar, _surfaces);
                _groups.Clear();
                _materialCount = 0;
                _warningCount = 0;
                foreach (var material in _avatar.Materials)
                {
                    if (material == null) continue;

                    TAOFallbackPlan plan;
                    try
                    {
                        plan = planner.Plan(material);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, material);
                        continue;
                    }

                    if (!_groups.TryGetValue(plan.Kind, out var plans)) _groups[plan.Kind] = plans = new List<TAOFallbackPlan>();
                    plans.Add(plan);
                    _materialCount++;
                    if (plan.HasWarning) _warningCount++;
                }

                _planSignature = settings.Signature;
            }

            // 欄の幅は、いちばん長い表記に合わせる。表記は言語や指定で変わるので、毎回測る。
            // フォールバック先の指定は、ほとんどの行が「自動」なので、指定した名前が入る幅だけを取って、マテリアルの欄を広くする
            _shaderWidths.Clear();
            _targetWidth = MinTargetWidth;
            foreach (var group in _groups)
            {
                var shaderWidth = 0f;
                foreach (var plan in group.Value)
                {
                    shaderWidth = Mathf.Max(shaderWidth, EditorStyles.miniLabel.CalcSize(new GUIContent(ShaderText(plan))).x);
                    _targetWidth = Mathf.Max(_targetWidth, EditorStyles.popup.CalcSize(new GUIContent(TargetLabel(plan.Target))).x);
                }

                _shaderWidths[group.Key] = shaderWidth;
            }

            _targetWidth = Mathf.Min(_targetWidth, MaxTargetWidth);
        }

        // [マテリアル] [シェーダー｜描画モード] [フォールバック先の指定] [補足]
        void DrawRow(TAOFallbackPlan plan, string[] targetLabels, float groupShaderWidth)
        {
            var row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            var noteRect = new Rect(row.xMax - NoteWidth, row.y, NoteWidth, row.height);
            var targetRect = new Rect(noteRect.x - 2f - _targetWidth, row.y, _targetWidth, row.height);

            // シェーダーの欄は、マテリアルの欄と分け合う幅の半分ほどまでにして、マテリアルの名前が読めるようにする。
            // 入りきらない表記は、カーソルを置くと確認できる
            var shared = Mathf.Max(targetRect.x - row.x - 4f, 0f);
            var shaderWidth = Mathf.Min(groupShaderWidth + 2f, Mathf.Max(shared * MaxShaderShare, Mathf.Min(MinShaderWidth, shared)));
            var shaderRect = new Rect(targetRect.x - 2f - shaderWidth, row.y, shaderWidth, row.height);
            var materialRect = new Rect(row.x, row.y, Mathf.Max(shaderRect.x - 2f - row.x, 0f), row.height);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // 一覧は検出結果なので、ここではマテリアルを差し替えない
            EditorGUI.ObjectField(materialRect, plan.Material, typeof(Material), false);
            EditorGUI.LabelField(shaderRect, new GUIContent(ShaderText(plan), MeasuredText(plan)), EditorStyles.miniLabel);

            var selected = Mathf.Max(0, Array.IndexOf(Targets, plan.Target));
            var picked = TAOGUI.ColoredPopup(targetRect, selected, targetLabels, plan.Target != TAOFallbackTarget.Auto);
            if (picked != selected) SetOverride(plan.Material, Targets[picked]);

            // 見た目が大きく変わるマテリアルは警告のアイコン、そのほかの補足は情報のアイコン
            if (plan.HasWarning || plan.Notes.Count > 0)
            {
                var icon = EditorGUIUtility.IconContent(plan.HasWarning ? "console.warnicon.sml" : "console.infoicon.sml");
                GUI.Label(noteRect, new GUIContent(icon.image, plan.NoteText));
            }

            if (plan.Target == TAOFallbackTarget.Custom) DrawCustomTag(plan.Material);
            EditorGUI.indentLevel = indent;
        }

        // 「lilToon（Uzumore）｜半透明 2パス」のような、シェーダーの種類と元の描画モード
        static string ShaderText(TAOFallbackPlan plan)
            => plan.Info.ShaderLabel + T("｜", " | ") + plan.Info.ModeLabel;

        // シェーダーの欄にカーソルを置いたときに表示する、シェーダーの名前と、フォールバック先を決めるために測った値
        static string MeasuredText(TAOFallbackPlan plan)
        {
            var info = plan.Info;
            var lines = new List<string> { T("シェーダー: ", "Shader: ") + info.Material.shader.name };

            var stats = plan.Stats;
            if (stats == null || !stats.Valid || !(info.IsTranslucent || info.IsCutout)) return string.Join("\n", lines);

            if (info.IsTranslucent)
            {
                lines.Add(T($"不透明度: {Percent(stats.VisibleAlpha)}（見えている部分の平均）",
                    $"Opacity: {Percent(stats.VisibleAlpha)} (average of the visible part)"));
            }

            lines.Add(T($"抜けている部分: {Percent(stats.Holes)}", $"Cut away: {Percent(stats.Holes)}"));
            lines.Add(T($"メインテクスチャのアルファ: {Percent(stats.TextureAlpha)}（平均）",
                $"Alpha of the main texture: {Percent(stats.TextureAlpha)} (average)"));
            lines.Add(T($"体に付いている部分: {Percent(stats.BodyFraction)}",
                $"Attached to the body: {Percent(stats.BodyFraction)}"));

            if (!stats.HasErrors) return string.Join("\n", lines);

            var onSkin = plan.ComparedOnSkin;
            string translucent;
            if (plan.BlendCandidate != TAOFallbackKind.Unchanged)
            {
                // アルファブレンドのフォールバック先は、メインテクスチャだけで元の色と透け方になるなら、元の表示と同じ合成になる
                translucent = TAOFallbackKinds.Label(plan.BlendCandidate) + " " +
                              (plan.BlendIsFaithful ? "0.00" : Error(stats, TAOFallbackCandidate.BlendRaw, onSkin));
            }
            else
            {
                // 焼き込めるかどうかで結果が変わる候補は、両方を表示する
                var particle = Error(stats, TAOFallbackCandidate.ParticleBaked, onSkin);
                var particleRaw = Error(stats, TAOFallbackCandidate.ParticleRaw, onSkin);
                if (particle != particleRaw)
                    particle += T($"（焼き込まない場合 {particleRaw}）", $" ({particleRaw} without baking)");
                translucent = "Particle " + particle;
            }

            lines.Add(onSkin
                ? T("元の表示との差（0 に近いほど元の表示に近い。顔に重ねるものは、後ろが肌の場合で比べます）:",
                    "Difference from the original (closer to 0 is closer to the original; overlays on the face are compared over skin):")
                : T("元の表示との差（0 に近いほど元の表示に近い）:", "Difference from the original (closer to 0 is closer to the original):"));
            lines.Add(T($"　不透明 {Error(stats, TAOFallbackCandidate.Opaque, onSkin)}　{translucent}　非表示 {Error(stats, TAOFallbackCandidate.Hidden, onSkin)}",
                $"  Opaque {Error(stats, TAOFallbackCandidate.Opaque, onSkin)}  {translucent}  Hidden {Error(stats, TAOFallbackCandidate.Hidden, onSkin)}"));
            if (stats.VertexTint > 0.02f && plan.BlendCandidate == TAOFallbackKind.Unchanged)
            {
                lines.Add(T("メッシュに白以外の頂点カラーがあります。Particle は頂点カラーを掛けて表示します。",
                    "The mesh has vertex colors other than white. Particle multiplies them."));
            }

            return string.Join("\n", lines);
        }

        static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

        static string Error(TAOFallbackSurfaceStats stats, TAOFallbackCandidate candidate, bool onSkin)
            => stats.Error(candidate, onSkin).ToString("0.00");

        void DrawCustomTag(Material material)
        {
            var index = FindOverride(material);
            if (index < 0) return;

            var tag = _materialOverrides.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(TAOFallbackMaterialOverride.customTag));
            var row = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            row.xMin += 16f;
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 96f;
            EditorGUI.DelayedTextField(row, tag, new GUIContent("VRCFallback",
                T("設定する VRCFallback タグ（例: VertexLit、UnlitCutoutDoubleSided）",
                  "The VRCFallback tag to set (for example VertexLit or UnlitCutoutDoubleSided)")));
            EditorGUIUtility.labelWidth = labelWidth;
        }

        static string TargetLabel(TAOFallbackTarget target)
        {
            switch (target)
            {
                case TAOFallbackTarget.Auto: return T("自動", "Auto");
                case TAOFallbackTarget.Keep: return T("変更しない", "Keep");
                default: return TAOFallbackKinds.Label(TAOFallbackKinds.FromTarget(target));
            }
        }

        int FindOverride(Material material)
        {
            for (var i = 0; i < _materialOverrides.arraySize; i++)
            {
                var entry = _materialOverrides.GetArrayElementAtIndex(i)
                    .FindPropertyRelative(nameof(TAOFallbackMaterialOverride.material));
                if (entry.objectReferenceValue == material) return i;
            }

            return -1;
        }

        // 「自動」に戻したマテリアルは、一覧から取り除く
        void SetOverride(Material material, TAOFallbackTarget target)
        {
            var index = FindOverride(material);
            if (target == TAOFallbackTarget.Auto)
            {
                if (index >= 0) _materialOverrides.DeleteArrayElementAtIndex(index);
                return;
            }

            if (index < 0)
            {
                index = _materialOverrides.arraySize;
                _materialOverrides.InsertArrayElementAtIndex(index);
                var added = _materialOverrides.GetArrayElementAtIndex(index);
                added.FindPropertyRelative(nameof(TAOFallbackMaterialOverride.material)).objectReferenceValue = material;
                added.FindPropertyRelative(nameof(TAOFallbackMaterialOverride.customTag)).stringValue = "";
            }

            _materialOverrides.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(TAOFallbackMaterialOverride.target)).intValue = (int)target;
        }
    }
}
