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
        private static Dictionary<string, string> _boneCategories;

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
