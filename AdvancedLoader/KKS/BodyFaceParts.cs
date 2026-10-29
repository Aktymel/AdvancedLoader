using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MessagePack;

namespace AdvancedLoader
{
    [Flags]
    internal enum BodyPart
    {
        None = 0,
        Overall = 1 << 0,
        Breast = 1 << 1,
        Upper = 1 << 2,
        Lower = 1 << 3,
        Arm = 1 << 4,
        Leg = 1 << 5,
        Nail = 1 << 6,
        Underhair = 1 << 7,
        Tan = 1 << 8,
        Paint = 1 << 9,
        // ABMX bones outside maker categories
        OtherBones = 1 << 10,
        All = (1 << 11) - 1
    }

    [Flags]
    internal enum FacePart
    {
        None = 0,
        Overall = 1 << 0,
        Ear = 1 << 1,
        Chin = 1 << 2,
        Cheek = 1 << 3,
        Eyebrow = 1 << 4,
        Eyes = 1 << 5,
        Iris = 1 << 6,
        Nose = 1 << 7,
        Mouth = 1 << 8,
        Mole = 1 << 9,
        Makeup = 1 << 10,
        // ABMX bones outside maker categories
        OtherBones = 1 << 11,
        All = (1 << 12) - 1
    }

    // Bit index = ChaFileDefine.ClothesKind
    [Flags]
    internal enum ClothesPart
    {
        None = 0,
        Top = 1 << 0,
        Bottom = 1 << 1,
        Bra = 1 << 2,
        Shorts = 1 << 3,
        Gloves = 1 << 4,
        Pantyhose = 1 << 5,
        Socks = 1 << 6,
        IndoorShoes = 1 << 7,
        OutdoorShoes = 1 << 8,
        All = (1 << 9) - 1
    }

    // Unticked pieces get the previous piece back before Reload
    internal static class ClothesParts
    {
        public const int Count = 9;

        private static readonly string[] Names = { "Top", "Bottom", "Bra", "Shorts", "Gloves", "Pantyhose", "Socks", "Indoor shoes", "Outdoor shoes" };

        public static IEnumerable<KeyValuePair<ClothesPart, string>> PartNames =>
            Enumerable.Range(0, Count).Select(i => new KeyValuePair<ClothesPart, string>((ClothesPart)(1 << i), Names[i]));

        public static string Describe(ClothesPart parts) =>
            string.Join(", ", Enumerable.Range(0, Count).Where(i => (parts & (ClothesPart)(1 << i)) != 0).Select(i => Names[i]).ToArray());

        public static IEnumerable<int> UntickedSlots(ClothesPart ticked) => Enumerable.Range(0, Count).Where(i => (ticked & (ClothesPart)(1 << i)) == 0);

        public static ChaFileClothes Copy(ChaFileClothes clothes)
        {
            return MessagePackSerializer.Deserialize<ChaFileClothes>(MessagePackSerializer.Serialize(clothes));
        }

        public static void RestoreUnticked(ChaFileClothes loaded, ChaFileClothes before, ClothesPart ticked)
        {
            foreach (var slot in UntickedSlots(ticked))
            {
                if (slot < loaded.parts.Length && slot < before.parts.Length) loaded.parts[slot] = before.parts[slot];
                // The top's sub parts (jacket, shirt…) belong to the top
                if (slot == 0) loaded.subPartsId = before.subPartsId;
            }
        }
    }

    // The game loads Body/Face whole; unticked categories are put back before Reload
    internal static class BodyFaceParts
    {
        private sealed class Part<T>
        {
            public T Flag;
            public string Name;
            public string[] MakerCategories = new string[0];
            public int[] ShapeIndices;
            public string[] Properties;
            public bool BonesOnly;
        }

        // Shape indices are the ones each maker menu (CvsBodyAll, CvsBreast…) edits in ChaFileBody.shapeValueBody
        private static readonly Part<BodyPart>[] BodyParts =
        {
            new Part<BodyPart> { Flag = BodyPart.Overall, Name = "Overall", MakerCategories = new[] { "tglAll" }, ShapeIndices = new[] { 0, 1 },
                Properties = new[] { "skinId", "detailId", "detailPower", "skinMainColor", "skinSubColor", "skinGlossPower", "drawAddLine", "typeBone" } },
            new Part<BodyPart> { Flag = BodyPart.Breast, Name = "Breast", MakerCategories = new[] { "tglBreast" }, ShapeIndices = Range(4, 13),
                Properties = new[] { "bustSoftness", "bustWeight", "areolaSize", "nipId", "nipColor", "nipGlossPower" } },
            new Part<BodyPart> { Flag = BodyPart.Upper, Name = "Upper body", MakerCategories = new[] { "tglUpper" }, ShapeIndices = new[] { 2, 3, 14, 15, 16, 17, 18, 19 } },
            new Part<BodyPart> { Flag = BodyPart.Lower, Name = "Lower body", MakerCategories = new[] { "tglLower" }, ShapeIndices = Range(20, 27) },
            new Part<BodyPart> { Flag = BodyPart.Arm, Name = "Arms", MakerCategories = new[] { "tglArm" }, ShapeIndices = Range(37, 43) },
            new Part<BodyPart> { Flag = BodyPart.Leg, Name = "Legs", MakerCategories = new[] { "tglLeg" }, ShapeIndices = Range(28, 36) },
            new Part<BodyPart> { Flag = BodyPart.Nail, Name = "Nails", MakerCategories = new[] { "tglNail" }, Properties = new[] { "nailColor", "nailGlossPower" } },
            new Part<BodyPart> { Flag = BodyPart.Underhair, Name = "Pubic hair", MakerCategories = new[] { "tglUnderhair" }, Properties = new[] { "underhairId", "underhairColor" } },
            new Part<BodyPart> { Flag = BodyPart.Tan, Name = "Suntan", MakerCategories = new[] { "tglSunburn" }, Properties = new[] { "sunburnId", "sunburnColor" } },
            new Part<BodyPart> { Flag = BodyPart.Paint, Name = "Paint", MakerCategories = new[] { "tglPaint" }, Properties = new[] { "paintId", "paintColor", "paintLayoutId", "paintLayout" } },
            new Part<BodyPart> { Flag = BodyPart.OtherBones, Name = "Other bonemod (advanced window)", BonesOnly = true }
        };

        // Shape indices are the ones each maker menu (CvsFaceAll, CvsEye01…) edits in ChaFileFace.shapeValueFace
        private static readonly Part<FacePart>[] FaceParts =
        {
            new Part<FacePart> { Flag = FacePart.Overall, Name = "Overall", MakerCategories = new[] { "tglAll" }, ShapeIndices = Range(0, 5),
                Properties = new[] { "headId", "skinId", "detailId", "detailPower" } },
            new Part<FacePart> { Flag = FacePart.Ear, Name = "Ears", MakerCategories = new[] { "tglEar" }, ShapeIndices = Range(47, 51) },
            new Part<FacePart> { Flag = FacePart.Chin, Name = "Jaw", MakerCategories = new[] { "tglChin" }, ShapeIndices = Range(6, 13) },
            new Part<FacePart> { Flag = FacePart.Cheek, Name = "Cheeks", MakerCategories = new[] { "tglCheek" }, ShapeIndices = Range(14, 18), Properties = new[] { "cheekGlossPower" } },
            new Part<FacePart> { Flag = FacePart.Eyebrow, Name = "Eyebrows", MakerCategories = new[] { "tglEyebrow" }, ShapeIndices = Range(19, 23),
                Properties = new[] { "eyebrowId", "eyebrowColor", "foregroundEyebrow" } },
            new Part<FacePart> { Flag = FacePart.Eyes, Name = "Eyes", MakerCategories = new[] { "tglEye01" }, ShapeIndices = Range(24, 37),
                Properties = new[] { "eyelineUpId", "eyelineUpWeight", "eyelineDownId", "eyelineColor" } },
            new Part<FacePart> { Flag = FacePart.Iris, Name = "Iris", MakerCategories = new[] { "tglEye02" },
                Properties = new[] { "pupil", "pupilWidth", "pupilHeight", "pupilX", "pupilY", "whiteId", "whiteBaseColor", "whiteSubColor",
                    "hlUpId", "hlUpColor", "hlUpX", "hlUpY", "hlDownId", "hlDownColor", "hlDownX", "hlDownY", "foregroundEyes" } },
            new Part<FacePart> { Flag = FacePart.Nose, Name = "Nose", MakerCategories = new[] { "tglNose" }, ShapeIndices = Range(38, 40), Properties = new[] { "noseId" } },
            new Part<FacePart> { Flag = FacePart.Mouth, Name = "Mouth", MakerCategories = new[] { "tglMouth" }, ShapeIndices = Range(41, 46),
                Properties = new[] { "lipLineId", "lipLineColor", "lipGlossPower", "doubleTooth" } },
            new Part<FacePart> { Flag = FacePart.Mole, Name = "Mole", MakerCategories = new[] { "tglMole" }, Properties = new[] { "moleId", "moleColor", "moleLayout" } },
            new Part<FacePart> { Flag = FacePart.Makeup, Name = "Makeup", MakerCategories = new[] { "tglMakeup" },
                Properties = new[] { "baseMakeup.eyeshadowId", "baseMakeup.eyeshadowColor", "baseMakeup.cheekId", "baseMakeup.cheekColor",
                    "baseMakeup.lipId", "baseMakeup.lipColor", "baseMakeup.paintId", "baseMakeup.paintColor", "baseMakeup.paintLayout" } },
            new Part<FacePart> { Flag = FacePart.OtherBones, Name = "Other bonemod (advanced window)", BonesOnly = true }
        };

        public static IEnumerable<KeyValuePair<BodyPart, string>> BodyPartNames(bool withBones) =>
            BodyParts.Where(x => withBones || !x.BonesOnly).Select(x => new KeyValuePair<BodyPart, string>(x.Flag, x.Name));

        public static IEnumerable<KeyValuePair<FacePart, string>> FacePartNames(bool withBones) =>
            FaceParts.Where(x => withBones || !x.BonesOnly).Select(x => new KeyValuePair<FacePart, string>(x.Flag, x.Name));

        public static string Describe(BodyPart parts) => string.Join(", ", BodyParts.Where(x => (parts & x.Flag) != 0).Select(x => x.Name).ToArray());
        public static string Describe(FacePart parts) => string.Join(", ", FaceParts.Where(x => (parts & x.Flag) != 0).Select(x => x.Name).ToArray());

        public static BodyPart BodyPartForCategory(string subCategory) =>
            BodyParts.FirstOrDefault(x => x.MakerCategories.Contains(subCategory))?.Flag ?? BodyPart.OtherBones;

        public static FacePart FacePartForCategory(string subCategory) =>
            FaceParts.FirstOrDefault(x => x.MakerCategories.Contains(subCategory))?.Flag ?? FacePart.OtherBones;

        public static void RestoreUnticked(ChaFileBody loaded, ChaFileBody before, BodyPart ticked)
        {
            foreach (var part in BodyParts.Where(x => (ticked & x.Flag) == 0))
                Restore(loaded, before, loaded.shapeValueBody, before.shapeValueBody, part.ShapeIndices, part.Properties);
        }

        public static void RestoreUnticked(ChaFileFace loaded, ChaFileFace before, FacePart ticked)
        {
            foreach (var part in FaceParts.Where(x => (ticked & x.Flag) == 0))
                Restore(loaded, before, loaded.shapeValueFace, before.shapeValueFace, part.ShapeIndices, part.Properties);
        }

        private static void Restore(object loaded, object before, float[] loadedShapes, float[] beforeShapes, int[] shapeIndices, string[] properties)
        {
            if (shapeIndices != null && loadedShapes != null && beforeShapes != null)
            {
                foreach (var index in shapeIndices)
                    if (index < loadedShapes.Length && index < beforeShapes.Length) loadedShapes[index] = beforeShapes[index];
            }

            if (properties == null) return;
            foreach (var path in properties)
            {
                var target = loaded;
                var source = before;
                var name = path;
                var dot = path.IndexOf('.');
                if (dot > 0)
                {
                    // e.g. baseMakeup.lipColor: copy the value inside the makeup objects, not the whole makeup
                    var owner = AccessTools.Property(loaded.GetType(), path.Substring(0, dot));
                    target = owner?.GetValue(loaded, null);
                    source = owner?.GetValue(before, null);
                    name = path.Substring(dot + 1);
                    if (target == null || source == null)
                    {
                        AdvancedLoaderPlugin.Logger.LogWarning($"{loaded.GetType().Name}.{path} not found, this part is loaded from the card");
                        continue;
                    }
                }

                var property = AccessTools.Property(target.GetType(), name);
                if (property == null)
                {
                    AdvancedLoaderPlugin.Logger.LogWarning($"{loaded.GetType().Name}.{path} not found, this part is loaded from the card");
                    continue;
                }
                // Old object is unused now
                property.SetValue(target, property.GetValue(source, null), null);
            }
        }

        private static int[] Range(int first, int last) => Enumerable.Range(first, last - first + 1).ToArray();
    }
}
