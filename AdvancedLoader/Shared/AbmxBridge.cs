using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKABMX.Core;
using KKABMX.GUI;
using KKAPI.Maker;
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

        public static bool OutfitBonemod => Available && AbmxBridge.LoadsOutfitBonemod;

        public static List<BoneDifference> Differences(ChaControl chaCtrl, CardData card) =>
            Available && chaCtrl != null && card?.Bonemod != null ? AbmxBridge.Differences(chaCtrl, card.Bonemod) : new List<BoneDifference>();
    }

    internal sealed class BoneDifference
    {
        public string Name;
        public bool Face;
        public string Current;
        public string Card;
    }

    // Only use when AbmxPresence.Available
    internal static class AbmxBridge
    {
        private static readonly MethodInfo ReadModifiersMethod = AccessTools.Method(typeof(BoneController), "ReadModifiers");
        private static readonly MethodInfo SaveModifiersMethod = AccessTools.Method(typeof(BoneController), "SaveModifiers");
        private static Dictionary<string, MakerCategory> _boneCategories;

        public const string DataId = KKABMX_Core.ExtDataGUID;

        private static readonly MethodInfo CoordinateLoadingMethod = AccessTools.Method(typeof(KKAPI.Chara.CharaCustomFunctionController), "OnCoordinateBeingLoadedInternal");

        public static readonly bool LoadsOutfitBonemod =
            AccessTools.Method(typeof(BoneController), "OnCoordinateBeingLoaded", new[] { typeof(ChaFileCoordinate), typeof(bool) })?.DeclaringType == typeof(BoneController);

        public static Action UseOutfitBonemod(bool load)
        {
            var registration = KKAPI.Chara.CharacterApi.RegisteredHandlers.FirstOrDefault(x => x.ControllerType == typeof(BoneController));
            if (registration == null) return null;
            var previous = registration.MaintainCoordinateState;
            registration.MaintainCoordinateState = !load;
            return () => registration.MaintainCoordinateState = previous;
        }

        public static void LoadOutfitBonemod(ChaControl chaCtrl, string path)
        {
            var controller = chaCtrl == null ? null : chaCtrl.GetComponent<BoneController>();
            if (controller == null || CoordinateLoadingMethod == null) return;
            var coordinate = new ChaFileCoordinate();
            coordinate.LoadFile(path);
            CoordinateLoadingMethod.Invoke(controller, new object[] { coordinate });
            Diag.Log($"Outfit bonemod loaded from {System.IO.Path.GetFileName(path)}");
        }

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

        private static HashSet<string> HeadBones(ChaControl chaCtrl) => chaCtrl.objHeadBone == null
            ? new HashSet<string>()
            : new HashSet<string>(chaCtrl.objHeadBone.GetComponentsInChildren<Transform>(true).Select(x => x.name));

        private static bool IsHead(string name, HashSet<string> headBones, out string category)
        {
            var makerCategory = CategoryOf(name);
            category = makerCategory?.SubCategoryName;
            return makerCategory != null ? makerCategory.CategoryName == MakerConstants.Face.All.CategoryName : headBones.Contains(name);
        }

        // Bones only the advanced window edits, chosen one by one in the Bonemod tab
        private static bool IsAdvancedOnly(bool isHead, string category) => isHead
            ? BodyFaceParts.FacePartForCategory(category) == FacePart.OtherBones
            : BodyFaceParts.BodyPartForCategory(category) == BodyPart.OtherBones;

        public static List<BoneDifference> Differences(ChaControl chaCtrl, object cardData)
        {
            var result = new List<BoneDifference>();
            var controller = chaCtrl == null ? null : chaCtrl.GetComponent<BoneController>();
            if (controller == null) return result;

            var card = cardData as List<BoneModifier> ?? new List<BoneModifier>();
            var current = controller.GetAllModifiers().Where(IsBodyBone).ToList();
            var headBones = HeadBones(chaCtrl);
            var names = new HashSet<string>(card.Select(x => x.BoneName));
            names.UnionWith(current.Select(x => x.BoneName));
            foreach (var name in names.OrderBy(x => x, StringComparer.Ordinal))
            {
                var isHead = IsHead(name, headBones, out var category);
                if (!IsAdvancedOnly(isHead, category)) continue;
                var now = current.FirstOrDefault(x => x.BoneName == name);
                var fromCard = card.FirstOrDefault(x => x.BoneName == name);
                if (Equal(now, fromCard)) continue;
                result.Add(new BoneDifference { Name = name, Face = isHead, Current = Short(now), Card = Short(fromCard) });
            }
            return result;
        }

        public static void Apply(ChaControl chaCtrl, object cardData, object snapshot, bool face, FacePart faceParts, bool body, BodyPart bodyParts,
            ICollection<string> selectedBones)
        {
            if (!face && !body && selectedBones.Count == 0) return;
            var controller = chaCtrl.GetComponent<BoneController>();
            if (controller == null) return;

            var card = cardData as List<BoneModifier> ?? new List<BoneModifier>();
            var before = snapshot as List<BoneModifier> ?? new List<BoneModifier>();
            var headBones = HeadBones(chaCtrl);
            var names = new HashSet<string>(card.Select(x => x.BoneName));
            names.UnionWith(before.Select(x => x.BoneName));
            names.UnionWith(controller.GetAllModifiers().Where(IsBodyBone).Select(x => x.BoneName));
            Diag.Log($"Bonemod apply: face={face} ({faceParts}) body={body} ({bodyParts}), {selectedBones.Count} selected bones; card {card.Count}, before {before.Count}, now {controller.GetAllModifiers().Count(IsBodyBone)} body bones");

            var result = new List<BoneModifier>();
            foreach (var name in names)
            {
                var current = controller.GetAllModifiers().FirstOrDefault(x => x.BoneName == name && IsBodyBone(x));
                var isHead = IsHead(name, headBones, out var category);
                bool fromCard;
                if (IsAdvancedOnly(isHead, category))
                {
                    fromCard = selectedBones.Contains(name);
                }
                else if (isHead ? !face : !body)
                {
                    if (current != null) result.Add(current.Clone());
                    continue;
                }
                else
                {
                    fromCard = isHead
                        ? (faceParts & BodyFaceParts.FacePartForCategory(category)) != 0
                        : (bodyParts & BodyFaceParts.BodyPartForCategory(category)) != 0;
                }

                var source = (fromCard ? card : before).FirstOrDefault(x => x.BoneName == name);
                if (!Same(source, current))
                    Diag.Log($"  {name} ({(isHead ? "head" : "body")}, {category ?? "no category"}, {current?.BoneLocation}): now {Describe(current)} -> {(fromCard ? "card" : "before")} {Describe(source)}");
                if (source != null && !source.IsEmpty()) result.Add(source.Clone());
            }

            // Reused modifiers keep the old body's baseline
            foreach (var modifier in controller.GetAllModifiers().Where(IsBodyBone).ToList()) controller.RemoveModifier(modifier);
            foreach (var modifier in result) controller.AddModifier(modifier);
            Diag.Log($"Bonemod rebuilt with {result.Count} body bones");
            controller.NeedsFullRefresh = true;
        }

        private static string Describe(BoneModifier modifier)
        {
            if (modifier == null || modifier.IsEmpty()) return "none";
            var data = modifier.CoordinateModifiers.FirstOrDefault();
            return data == null ? "empty" : $"scale {data.ScaleModifier} len {data.LengthModifier} pos {data.PositionModifier}";
        }

        private static bool Same(BoneModifier a, BoneModifier b) => Describe(a) == Describe(b);

        private static bool Equal(BoneModifier a, BoneModifier b)
        {
            var aEmpty = a == null || a.IsEmpty();
            var bEmpty = b == null || b.IsEmpty();
            if (aEmpty || bEmpty) return aEmpty == bEmpty;
            if (a.CoordinateModifiers.Length != b.CoordinateModifiers.Length) return false;
            for (var i = 0; i < a.CoordinateModifiers.Length; i++)
            {
                var x = a.CoordinateModifiers[i];
                var y = b.CoordinateModifiers[i];
                if (x.ScaleModifier != y.ScaleModifier || Mathf.Abs(x.LengthModifier - y.LengthModifier) > 0.0001f || x.PositionModifier != y.PositionModifier ||
                    x.RotationModifier != y.RotationModifier) return false;
            }
            return true;
        }

        private static string Short(BoneModifier modifier)
        {
            if (modifier == null || modifier.IsEmpty()) return "none";
            var data = modifier.CoordinateModifiers.FirstOrDefault();
            if (data == null) return "none";
            var parts = new List<string>();
            if (data.ScaleModifier != Vector3.one) parts.Add("scale " + Short(data.ScaleModifier));
            if (Mathf.Abs(data.LengthModifier - 1) > 0.0001f) parts.Add("length " + data.LengthModifier.ToString("0.##"));
            if (data.PositionModifier != Vector3.zero) parts.Add("position " + Short(data.PositionModifier));
            if (data.RotationModifier != Vector3.zero) parts.Add("rotation " + Short(data.RotationModifier));
            var text = parts.Count == 0 ? "default" : string.Join(", ", parts.ToArray());
            return modifier.CoordinateModifiers.Length > 1 ? text + " (per outfit)" : text;
        }

        private static string Short(Vector3 v) => $"{v.x:0.##} {v.y:0.##} {v.z:0.##}";

        public static string DescribeAll(ChaControl chaCtrl)
        {
            var controller = chaCtrl.GetComponent<BoneController>();
            if (controller == null) return "no controller";
            return string.Join("; ", controller.GetAllModifiers().Where(x => IsBodyBone(x) && !x.IsEmpty()).Select(x => $"{x.BoneName}={Describe(x)}").ToArray());
        }

        private static Dictionary<string, MakerCategory> GetBoneCategories()
        {
            if (_boneCategories != null) return _boneCategories;

            _boneCategories = new Dictionary<string, MakerCategory>();
            foreach (var meta in InterfaceData.BoneControls.Where(x => !x.IsSeparator && x.Category != null))
            {
                var category = meta.Category;
                if (!_boneCategories.ContainsKey(meta.BoneName)) _boneCategories[meta.BoneName] = category;
                if (!string.IsNullOrEmpty(meta.RightBoneName) && !_boneCategories.ContainsKey(meta.RightBoneName)) _boneCategories[meta.RightBoneName] = category;
            }
            return _boneCategories;
        }

        // Finger sliders are per finger bone, not in BoneControls
        public static MakerCategory CategoryOf(string boneName)
        {
            if (GetBoneCategories().TryGetValue(boneName, out var category)) return category;
            return InterfaceData.FingerNamePrefixes.Any(boneName.StartsWith) ? InterfaceData.FingerCategory : null;
        }
    }
}
