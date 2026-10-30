using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKABMX.Core;
using KKABMX.GUI;
using UnityEngine;
#if HS2 || AI
using AIChara;
#elif KKS || KK
#endif

namespace AdvancedLoader
{
    // Separate so the check never loads ABMX types
    internal static class AbmxPresence
    {
        public const string GUID = "KKABMX.Core";

        public static readonly string[] LoadToggleTexts = { "Face Bonemod", "Body Bonemod" };

        public static bool Available => Dependencies.Abmx.IsOk;
    }

    // Only use when AbmxPresence.Available
    internal static class AbmxBridge
    {
        private static readonly MethodInfo ReadModifiersMethod = AccessTools.Method(typeof(BoneController), "ReadModifiers");
        private static readonly MethodInfo SaveModifiersMethod = AccessTools.Method(typeof(BoneController), "SaveModifiers");
        private static Dictionary<string, string> _boneCategories;

        public const string DataId = KKABMX_Core.ExtDataGUID;

        // Card data for ABMX with the character's own body and face bones: ABMX would put the card's on the bones for a few frames,
        // and joints the game doesn't recalculate keep that scale. Ticked parts are taken from the card afterwards (Apply)
        public static PluginData KeepBodyBones(ChaControl chaCtrl, PluginData cardData)
        {
            var controller = chaCtrl == null ? null : chaCtrl.GetComponent<BoneController>();
            if (controller == null || ReadModifiersMethod == null || SaveModifiersMethod == null) return cardData;
            var card = ReadModifiersMethod.Invoke(null, new object[] { cardData }) as List<BoneModifier> ?? new List<BoneModifier>();
            var merged = controller.GetAllModifiers().Where(IsBodyBone).Select(x => x.Clone())
                .Concat(card.Where(x => !IsBodyBone(x)).Select(x => x.Clone()))
                .ToList();
            Diag.Log($"Bonemod for the load: {merged.Count(IsBodyBone)} body bones of the character, {merged.Count(x => !IsBodyBone(x))} accessory bones of the card");
            return SaveModifiersMethod.Invoke(null, new object[] { merged, false }) as PluginData;
        }

        public static object ReadFromCard(ChaFile file)
        {
            var data = ExtendedSave.GetExtendedDataById(file, KKABMX_Core.ExtDataGUID);
            var modifiers = ReadModifiersMethod?.Invoke(null, new object[] { data }) as List<BoneModifier>;
            return (modifiers ?? new List<BoneModifier>()).Where(IsBodyBone).Select(x => x.Clone()).ToList();
        }

        public static object Snapshot(ChaControl chaCtrl)
        {
            var controller = chaCtrl.GetComponent<BoneController>();
            if (controller == null) return new List<BoneModifier>();
            return controller.GetAllModifiers().Where(IsBodyBone).Select(x => x.Clone()).ToList();
        }

        private static bool IsBodyBone(BoneModifier modifier) => modifier.BoneLocation < BoneLocation.Accessory;

        public static void Apply(ChaControl chaCtrl, object cardData, object snapshot, bool face, FacePart faceParts, bool body, BodyPart bodyParts)
        {
            if (!face && !body) return;
            var controller = chaCtrl.GetComponent<BoneController>();
            if (controller == null) return;

            var card = cardData as List<BoneModifier> ?? new List<BoneModifier>();
            var before = snapshot as List<BoneModifier> ?? new List<BoneModifier>();
            var headBones = chaCtrl.objHeadBone == null
                ? new HashSet<string>()
                : new HashSet<string>(chaCtrl.objHeadBone.GetComponentsInChildren<Transform>(true).Select(x => x.name));
            var names = new HashSet<string>(card.Select(x => x.BoneName));
            names.UnionWith(before.Select(x => x.BoneName));
            names.UnionWith(controller.GetAllModifiers().Where(IsBodyBone).Select(x => x.BoneName));
            Diag.Log($"Bonemod apply: face={face} ({faceParts}) body={body} ({bodyParts}); card {card.Count}, before {before.Count}, now {controller.GetAllModifiers().Count(IsBodyBone)} body bones");

            foreach (var name in names)
            {
                var isHead = headBones.Contains(name);
                if (isHead ? !face : !body) continue;

                var category = CategoryOf(name);
                var fromCard = isHead
                    ? (faceParts & BodyFaceParts.FacePartForCategory(category)) != 0
                    : (bodyParts & BodyFaceParts.BodyPartForCategory(category)) != 0;

                var source = (fromCard ? card : before).FirstOrDefault(x => x.BoneName == name);
                var current = controller.GetAllModifiers().FirstOrDefault(x => x.BoneName == name && IsBodyBone(x));
                if (!Same(source, current))
                    Diag.Log($"  {name} ({(isHead ? "head" : "body")}, {category ?? "no category"}, {current?.BoneLocation}): now {Describe(current)} -> {(fromCard ? "card" : "before")} {Describe(source)}");

                if (source == null || source.IsEmpty())
                {
                    if (current != null) controller.RemoveModifier(current);
                }
                else if (current != null)
                {
                    current.CoordinateModifiers = source.Clone().CoordinateModifiers;
                }
                else
                {
                    controller.AddModifier(new BoneModifier(name, BoneLocation.BodyTop, source.Clone().CoordinateModifiers));
                }
            }

            controller.NeedsFullRefresh = true;
        }

        private static string Describe(BoneModifier modifier)
        {
            if (modifier == null || modifier.IsEmpty()) return "none";
            var data = modifier.CoordinateModifiers.FirstOrDefault();
            return data == null ? "empty" : $"scale {data.ScaleModifier} len {data.LengthModifier} pos {data.PositionModifier}";
        }

        private static bool Same(BoneModifier a, BoneModifier b) => Describe(a) == Describe(b);

        // For the diagnostic log: body bonemod as it is now
        public static string DescribeAll(ChaControl chaCtrl)
        {
            var controller = chaCtrl.GetComponent<BoneController>();
            if (controller == null) return "no controller";
            return string.Join("; ", controller.GetAllModifiers().Where(x => IsBodyBone(x) && !x.IsEmpty()).Select(x => $"{x.BoneName}={Describe(x)}").ToArray());
        }

        private static Dictionary<string, string> GetBoneCategories()
        {
            if (_boneCategories != null) return _boneCategories;

            _boneCategories = new Dictionary<string, string>();
            foreach (var meta in InterfaceData.BoneControls.Where(x => !x.IsSeparator && x.Category != null))
            {
                var category = meta.Category.SubCategoryName;
                if (!_boneCategories.ContainsKey(meta.BoneName)) _boneCategories[meta.BoneName] = category;
                if (!string.IsNullOrEmpty(meta.RightBoneName) && !_boneCategories.ContainsKey(meta.RightBoneName)) _boneCategories[meta.RightBoneName] = category;
            }
            return _boneCategories;
        }

        // Finger sliders are per finger bone, not in BoneControls
        public static string CategoryOf(string boneName)
        {
            if (GetBoneCategories().TryGetValue(boneName, out var category)) return category;
            return InterfaceData.FingerNamePrefixes.Any(boneName.StartsWith) ? InterfaceData.FingerCategory.SubCategoryName : null;
        }
    }
}
