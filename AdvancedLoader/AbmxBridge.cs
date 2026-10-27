using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIChara;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKABMX.Core;
using KKABMX.GUI;
using UnityEngine;

namespace AdvancedLoader
{
    /// <summary>
    /// Kept apart from <see cref="AbmxBridge"/> so checking for ABMX never loads its types.
    /// </summary>
    internal static class AbmxPresence
    {
        public const string GUID = "KKABMX.Core";

        /// <summary>Texts of ABMX's own load toggles; the window handles bonemod by category instead.</summary>
        public static readonly string[] LoadToggleTexts = { "Face Bonemod", "Body Bonemod" };

        /// <summary>Installed in a version this plugin works with.</summary>
        public static bool Available => Dependencies.Abmx.IsOk;
    }

    /// <summary>
    /// Copies bonemod (ABMX) sliders by maker category. Every slider ABMX adds to the maker knows its category
    /// (InterfaceData.BoneControls), so a bone is taken from the card when its category is ticked and kept otherwise.
    /// Only call into this class when <see cref="AbmxPresence.Available"/> is true.
    /// </summary>
    internal static class AbmxBridge
    {
        private static readonly MethodInfo ReadModifiersMethod = AccessTools.Method(typeof(BoneController), "ReadModifiers");
        private static Dictionary<string, string> _boneCategories;

        /// <summary>Body bonemod of a card (accessory bones are left out).</summary>
        public static object ReadFromCard(ChaFile file)
        {
            var data = ExtendedSave.GetExtendedDataById(file, KKABMX_Core.ExtDataGUID);
            var modifiers = ReadModifiersMethod?.Invoke(null, new object[] { data }) as List<BoneModifier>;
            return (modifiers ?? new List<BoneModifier>()).Where(IsBodyBone).Select(x => x.Clone()).ToList();
        }

        /// <summary>Copy of the character's current body bonemod.</summary>
        public static object Snapshot(ChaControl chaCtrl)
        {
            var controller = chaCtrl.GetComponent<BoneController>();
            if (controller == null) return new List<BoneModifier>();
            return controller.GetAllModifiers().Where(IsBodyBone).Select(x => x.Clone()).ToList();
        }

        private static bool IsBodyBone(BoneModifier modifier) => modifier.BoneLocation < BoneLocation.Accessory;

        /// <summary>
        /// For every face bone (when Face is loaded) and body bone (when Body is loaded): take the card's value when the
        /// bone's category is ticked, otherwise the value from before the load.
        /// </summary>
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

        /// <summary>Bone name -> maker sub category ("Breast", "Nose"…) of the ABMX slider that edits it.</summary>
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

        /// <summary>Finger sliders are generated per finger bone rather than listed in BoneControls.</summary>
        public static string CategoryOf(string boneName)
        {
            if (GetBoneCategories().TryGetValue(boneName, out var category)) return category;
            return InterfaceData.FingerNamePrefixes.Any(boneName.StartsWith) ? InterfaceData.FingerCategory.SubCategoryName : null;
        }
    }
}
