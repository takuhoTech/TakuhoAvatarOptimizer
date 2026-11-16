using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.Dynamics;

namespace TakuhoTech.TAO
{
    /// <summary>
    /// PhysBone の設定。フィールド名は VRCPhysBoneBase と同一で、リフレクションで値をやり取りする。
    /// <see cref="manualProperties"/> に含まれる項目だけが、ビルド時に PhysBone へ書き込まれる。
    /// </summary>
    [Serializable]
    public class TAOPhysBoneSettings
    {
        /// <summary>手動で指定する項目の名前（TAOPhysBoneProperty.Name）。</summary>
        public List<string> manualProperties = new List<string>();

        public VRCPhysBoneBase.Version version;
        public bool ignoreOtherPhysBones;

        public VRCPhysBoneBase.IntegrationType integrationType;
        [Range(0f, 1f)] public float pull;
        public AnimationCurve pullCurve = new AnimationCurve();
        [Range(0f, 1f)] public float spring;
        public AnimationCurve springCurve = new AnimationCurve();
        [Range(0f, 1f)] public float stiffness;
        public AnimationCurve stiffnessCurve = new AnimationCurve();
        [Range(-1f, 1f)] public float gravity;
        public AnimationCurve gravityCurve = new AnimationCurve();
        [Range(0f, 1f)] public float gravityFalloff;
        public AnimationCurve gravityFalloffCurve = new AnimationCurve();
        public VRCPhysBoneBase.ImmobileType immobileType;
        [Range(0f, 1f)] public float immobile;
        public AnimationCurve immobileCurve = new AnimationCurve();

        public VRCPhysBoneBase.LimitType limitType;
        [Range(0f, 180f)] public float maxAngleX = 45f;
        public AnimationCurve maxAngleXCurve = new AnimationCurve();
        [Range(0f, 90f)] public float maxAngleZ = 45f;
        public AnimationCurve maxAngleZCurve = new AnimationCurve();
        public Vector3 limitRotation;
        public AnimationCurve limitRotationXCurve = new AnimationCurve();
        public AnimationCurve limitRotationYCurve = new AnimationCurve();
        public AnimationCurve limitRotationZCurve = new AnimationCurve();

        public float radius;
        public AnimationCurve radiusCurve = new AnimationCurve();
        public VRCPhysBoneBase.AdvancedBool allowCollision;
        public VRCPhysBoneBase.PermissionFilter collisionFilter;
        public List<VRCPhysBoneColliderBase> colliders = new List<VRCPhysBoneColliderBase>();

        [Range(0f, 1f)] public float stretchMotion;
        public AnimationCurve stretchMotionCurve = new AnimationCurve();
        public float maxStretch;
        public AnimationCurve maxStretchCurve = new AnimationCurve();
        [Range(0f, 1f)] public float maxSquish;
        public AnimationCurve maxSquishCurve = new AnimationCurve();

        public VRCPhysBoneBase.AdvancedBool allowGrabbing;
        public VRCPhysBoneBase.PermissionFilter grabFilter;
        public VRCPhysBoneBase.AdvancedBool allowPosing;
        public VRCPhysBoneBase.PermissionFilter poseFilter;
        [Range(0f, 1f)] public float grabMovement;
        public bool snapToHand;

        public string parameter;
        public bool isAnimated;
        public bool resetWhenDisabled;

        static readonly Dictionary<string, FieldInfo> SettingsFields = FieldsOf(typeof(TAOPhysBoneSettings));
        static readonly Dictionary<string, FieldInfo> PhysBoneFields = FieldsOf(typeof(VRCPhysBoneBase));

        public bool IsManual(string propertyName) => manualProperties.Contains(propertyName);

        /// <summary>
        /// 複数の PhysBone の設定をまとめた設定を返す。数値とカーブは平均、それ以外は最も多い値、コライダーは和集合。
        /// すべての項目を手動として含むので、そのまま <see cref="ApplyTo"/> に使える。
        /// </summary>
        /// <param name="physBones">まとめる PhysBone。</param>
        /// <param name="mainRotatingBoneCount">
        /// 結果を使う PhysBone の、回転するボーンの数。1 以上のとき、ボーン数の比でパラメータを補正してからまとめる。
        /// </param>
        public static TAOPhysBoneSettings Combine(IEnumerable<VRCPhysBoneBase> physBones, int mainRotatingBoneCount = 0)
        {
            var sources = physBones.Where(p => p != null).ToList();
            var combined = new TAOPhysBoneSettings();
            if (sources.Count == 0) return combined;

            var boneCountRatios = mainRotatingBoneCount > 0
                ? sources.Select(p => mainRotatingBoneCount / (float)TAOPhysBoneTopology.RotatingBoneCount(p)).ToList()
                : null;

            foreach (var property in TAOPhysBoneProperties.All)
            {
                if (!TAOPhysBoneProperties.IsAvailable(property)) continue;
                foreach (var name in property.AllNames) combined.CombineField(name, sources, boneCountRatios);
                combined.manualProperties.Add(property.Name);
            }

            return combined;
        }

        /// <summary>手動の項目の値を、PhysBone へ書き込む。</summary>
        public void ApplyTo(VRCPhysBoneBase physBone)
        {
            if (physBone == null) return;
            foreach (var propertyName in manualProperties)
            {
                var property = TAOPhysBoneProperties.Find(propertyName);
                if (property == null) continue;
                foreach (var name in property.AllNames) WriteField(name, physBone);
            }
        }

        /// <summary>
        /// ボーン数が減ると全体の曲がりは関節の数に比例して小さくなる。全体の曲がりを保つため、
        /// 関節ごとの復元力（Pull・Stiffness）は比に比例して弱め、関節ごとの角度制限（Max Angle）は比に反比例して広げる。
        /// </summary>
        static float ScaleByBoneCount(string name, float value, float ratio)
        {
            switch (name)
            {
                case "pull":
                case "stiffness":
                    return Mathf.Clamp01(value * ratio);
                case "maxAngleX":
                    return Mathf.Clamp(value / ratio, 0f, 180f);
                case "maxAngleZ":
                    return Mathf.Clamp(value / ratio, 0f, 90f);
                default:
                    return value;
            }
        }

        void CombineField(string name, List<VRCPhysBoneBase> physBones, List<float> boneCountRatios)
        {
            if (!SettingsFields.TryGetValue(name, out var settingsField)
                || !PhysBoneFields.TryGetValue(name, out var physBoneField)) return;

            try
            {
                var values = physBones.Select(p => physBoneField.GetValue(p)).ToList();
                if (boneCountRatios != null && values[0] is float)
                {
                    values = values
                        .Select((v, i) => (object)ScaleByBoneCount(name, (float)v, boneCountRatios[i]))
                        .ToList();
                }

                settingsField.SetValue(this, CombineValues(settingsField.FieldType, values));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TAO] PhysBoneの設定「{name}」をまとめられませんでした: {e.Message}");
            }
        }

        static object CombineValues(Type fieldType, List<object> values)
        {
            if (values.Count == 1) return CloneValue(values[0]);
            if (fieldType == typeof(AnimationCurve)) return AverageCurves(values.Cast<AnimationCurve>().ToList());
            if (values[0] is float) return values.Cast<float>().Average();
            if (values[0] is Vector3)
            {
                return new Vector3(
                    values.Average(v => ((Vector3)v).x),
                    values.Average(v => ((Vector3)v).y),
                    values.Average(v => ((Vector3)v).z));
            }

            if (values[0] is List<VRCPhysBoneColliderBase>)
            {
                return values.Cast<List<VRCPhysBoneColliderBase>>()
                    .Where(list => list != null)
                    .SelectMany(list => list)
                    .Where(collider => collider != null)
                    .Distinct()
                    .ToList();
            }

            return values.GroupBy(v => v).OrderByDescending(g => g.Count()).First().Key;
        }

        /// <summary>カーブなし（空）は係数 1 の一定値として扱い、同じ位置の値を平均する。</summary>
        static AnimationCurve AverageCurves(List<AnimationCurve> curves)
        {
            var averaged = new AnimationCurve();
            if (curves.All(c => c == null || c.length == 0)) return averaged;

            const int samples = 11;
            for (var i = 0; i < samples; i++)
            {
                var time = i / (samples - 1f);
                averaged.AddKey(time, curves.Average(c => c == null || c.length == 0 ? 1f : c.Evaluate(time)));
            }

            for (var i = 0; i < samples; i++) averaged.SmoothTangents(i, 0f);
            return averaged;
        }

        void WriteField(string name, VRCPhysBoneBase physBone)
        {
            if (!SettingsFields.TryGetValue(name, out var settingsField)
                || !PhysBoneFields.TryGetValue(name, out var physBoneField)) return;

            try
            {
                physBoneField.SetValue(physBone, CloneValue(settingsField.GetValue(this)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TAO] PhysBoneの設定「{name}」を上書きできませんでした: {e.Message}", physBone);
            }
        }

        /// <summary>カーブとリストは、元のオブジェクトと共有しないようにコピーする。</summary>
        static object CloneValue(object value)
        {
            if (value is AnimationCurve curve)
            {
                return new AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode,
                };
            }

            if (value is IList && value.GetType().IsGenericType)
                return Activator.CreateInstance(value.GetType(), value);

            return value;
        }

        static Dictionary<string, FieldInfo> FieldsOf(Type type)
        {
            var fields = new Dictionary<string, FieldInfo>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                fields[field.Name] = field;
            return fields;
        }
    }
}
