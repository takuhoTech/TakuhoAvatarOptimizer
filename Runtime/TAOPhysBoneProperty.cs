using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRC.Dynamics;

namespace TakuhoTech.TAO
{
    public enum TAOPhysBonePropertyKind
    {
        Value,
        Curve,
        Vector3Curve,
        Permission,
        Colliders,
    }

    /// <summary>PhysBone の設定項目。値のほかに、一緒に扱うカーブやフィルタの名前を持つ。</summary>
    public sealed class TAOPhysBoneProperty
    {
        public string Section { get; }
        public string Label { get; }

        /// <summary>VRCPhysBoneBase と TAOPhysBoneSettings で共通のフィールド名。</summary>
        public string Name { get; }

        public TAOPhysBonePropertyKind Kind { get; }

        /// <summary>値と一緒にコピーされるフィールド（カーブ・フィルタ）。</summary>
        public string[] RelatedNames { get; }

        public TAOPhysBoneProperty(string section, string label, string name, TAOPhysBonePropertyKind kind,
            params string[] relatedNames)
        {
            Section = section;
            Label = label;
            Name = name;
            Kind = kind;
            RelatedNames = relatedNames;
        }

        public IEnumerable<string> AllNames => new[] { Name }.Concat(RelatedNames);
    }

    public readonly struct TAOPhysBoneSection
    {
        public string Name { get; }
        public string DocTag { get; }

        public TAOPhysBoneSection(string name, string docTag)
        {
            Name = name;
            DocTag = docTag;
        }
    }

    /// <summary>AAO Merge PhysBone と同じ項目構成の、PhysBone の設定項目の一覧。</summary>
    public static class TAOPhysBoneProperties
    {
        /// <summary>セクションに属さない項目（Version）のセクション名。</summary>
        public const string NoSection = "";

        public static readonly TAOPhysBoneSection[] Sections =
        {
            new TAOPhysBoneSection("Transform", "transforms"),
            new TAOPhysBoneSection("Forces", "forces"),
            new TAOPhysBoneSection("Limits", "limits"),
            new TAOPhysBoneSection("Collision", "collision"),
            new TAOPhysBoneSection("Stretch & Squish", "stretch--squish"),
            new TAOPhysBoneSection("Grab & Pose", "grab--pose"),
            new TAOPhysBoneSection("Options", "options"),
        };

        public static readonly TAOPhysBoneProperty[] All =
        {
            new TAOPhysBoneProperty(NoSection, "Version", "version", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Transform", "Ignore Other Phys Bones", "ignoreOtherPhysBones",
                TAOPhysBonePropertyKind.Value),

            new TAOPhysBoneProperty("Forces", "Integration Type", "integrationType", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Forces", "Pull", "pull", TAOPhysBonePropertyKind.Curve, "pullCurve"),
            new TAOPhysBoneProperty("Forces", "Spring (Momentum)", "spring", TAOPhysBonePropertyKind.Curve,
                "springCurve"),
            new TAOPhysBoneProperty("Forces", "Stiffness", "stiffness", TAOPhysBonePropertyKind.Curve,
                "stiffnessCurve"),
            new TAOPhysBoneProperty("Forces", "Gravity", "gravity", TAOPhysBonePropertyKind.Curve, "gravityCurve"),
            new TAOPhysBoneProperty("Forces", "Gravity Falloff", "gravityFalloff", TAOPhysBonePropertyKind.Curve,
                "gravityFalloffCurve"),
            new TAOPhysBoneProperty("Forces", "Immobile Type", "immobileType", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Forces", "Immobile", "immobile", TAOPhysBonePropertyKind.Curve, "immobileCurve"),

            new TAOPhysBoneProperty("Limits", "Limit Type", "limitType", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Limits", "Max Angle X", "maxAngleX", TAOPhysBonePropertyKind.Curve,
                "maxAngleXCurve"),
            new TAOPhysBoneProperty("Limits", "Max Angle Z", "maxAngleZ", TAOPhysBonePropertyKind.Curve,
                "maxAngleZCurve"),
            new TAOPhysBoneProperty("Limits", "Limit Rotation", "limitRotation", TAOPhysBonePropertyKind.Vector3Curve,
                "limitRotationXCurve", "limitRotationYCurve", "limitRotationZCurve"),

            new TAOPhysBoneProperty("Collision", "Radius", "radius", TAOPhysBonePropertyKind.Curve, "radiusCurve"),
            new TAOPhysBoneProperty("Collision", "Allow Collision", "allowCollision",
                TAOPhysBonePropertyKind.Permission, "collisionFilter"),
            new TAOPhysBoneProperty("Collision", "Colliders", "colliders", TAOPhysBonePropertyKind.Colliders),

            new TAOPhysBoneProperty("Stretch & Squish", "Stretch Motion", "stretchMotion",
                TAOPhysBonePropertyKind.Curve, "stretchMotionCurve"),
            new TAOPhysBoneProperty("Stretch & Squish", "Max Stretch", "maxStretch", TAOPhysBonePropertyKind.Curve,
                "maxStretchCurve"),
            new TAOPhysBoneProperty("Stretch & Squish", "Max Squish", "maxSquish", TAOPhysBonePropertyKind.Curve,
                "maxSquishCurve"),

            new TAOPhysBoneProperty("Grab & Pose", "Allow Grabbing", "allowGrabbing",
                TAOPhysBonePropertyKind.Permission, "grabFilter"),
            new TAOPhysBoneProperty("Grab & Pose", "Allow Posing", "allowPosing", TAOPhysBonePropertyKind.Permission,
                "poseFilter"),
            new TAOPhysBoneProperty("Grab & Pose", "Grab Movement", "grabMovement", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Grab & Pose", "Snap To Hand", "snapToHand", TAOPhysBonePropertyKind.Value),

            new TAOPhysBoneProperty("Options", "Parameter", "parameter", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Options", "Is Animated", "isAnimated", TAOPhysBonePropertyKind.Value),
            new TAOPhysBoneProperty("Options", "Reset When Disabled", "resetWhenDisabled",
                TAOPhysBonePropertyKind.Value),
        };

        static readonly Dictionary<string, TAOPhysBoneProperty> ByName = All.ToDictionary(p => p.Name);

        static readonly Dictionary<string, bool> Availability = new Dictionary<string, bool>();

        public static TAOPhysBoneProperty Find(string name)
            => ByName.TryGetValue(name, out var property) ? property : null;

        /// <summary>SDK のバージョンによって存在しない項目は使えない。</summary>
        public static bool IsAvailable(TAOPhysBoneProperty property)
        {
            if (Availability.TryGetValue(property.Name, out var available)) return available;

            available = typeof(VRCPhysBoneBase).GetField(property.Name, BindingFlags.Public | BindingFlags.Instance)
                        != null;
            Availability[property.Name] = available;
            return available;
        }

        /// <summary>指定したセクションの、使える項目。</summary>
        public static List<TAOPhysBoneProperty> InSection(string section)
            => All.Where(p => p.Section == section && IsAvailable(p)).ToList();
    }
}
