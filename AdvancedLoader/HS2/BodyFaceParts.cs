using System;
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HarmonyLib;

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
        Skin = 1 << 6,
        Tan = 1 << 7,
        Nipples = 1 << 8,
        Underhair = 1 << 9,
        Nails = 1 << 10,
        Paint = 1 << 11,
        // ABMX bones outside maker categories
        OtherBones = 1 << 12,
        All = (1 << 13) - 1
    }

    [Flags]
    internal enum FacePart
    {
        None = 0,
        FaceType = 1 << 0,
        Overall = 1 << 1,
        Chin = 1 << 2,
        Cheek = 1 << 3,
        EyebrowShape = 1 << 4,
        EyeShape = 1 << 5,
        Nose = 1 << 6,
        Mouth = 1 << 7,
        Ear = 1 << 8,
        Mole = 1 << 9,
        Eyes = 1 << 10,
        EyeDetails = 1 << 11,
        Highlights = 1 << 12,
        Eyebrows = 1 << 13,
        Eyelashes = 1 << 14,
        Eyeshadow = 1 << 15,
        Blush = 1 << 16,
        Lipstick = 1 << 17,
        Paint = 1 << 18,
        Beard = 1 << 19,
        // ABMX bones outside maker categories
        OtherBones = 1 << 20,
        All = (1 << 21) - 1
    }

    // Bit index = ChaFileDefine.ClothesKind
    [Flags]
    internal enum ClothesPart
    {
        None = 0,
        Top = 1 << 0,
        Bottom = 1 << 1,
        InnerTop = 1 << 2,
        InnerBottom = 1 << 3,
        Gloves = 1 << 4,
        Pantyhose = 1 << 5,
        Socks = 1 << 6,
        Shoes = 1 << 7,
        All = (1 << 8) - 1
    }

    // Unticked pieces get the previous piece back before Reload
    internal static class ClothesParts
    {
        public const int Count = 8;

        private static readonly string[] Names = { "Top", "Bottom", "Inner top", "Inner bottom", "Gloves", "Pantyhose", "Socks", "Shoes" };

        public static IEnumerable<KeyValuePair<ClothesPart, string>> PartNames =>
            Enumerable.Range(0, Count).Select(i => new KeyValuePair<ClothesPart, string>((ClothesPart)(1 << i), Names[i]));

        public static string Describe(ClothesPart parts) =>
            string.Join(", ", Enumerable.Range(0, Count).Where(i => (parts & (ClothesPart)(1 << i)) != 0).Select(i => Names[i]).ToArray());

        public static IEnumerable<int> UntickedSlots(ClothesPart ticked) => Enumerable.Range(0, Count).Where(i => (ticked & (ClothesPart)(1 << i)) == 0);

        public static ChaFileClothes.PartsInfo[] Copy(ChaFileClothes clothes)
        {
            return clothes.parts.Select(x => MessagePack.MessagePackSerializer.Deserialize<ChaFileClothes.PartsInfo>(MessagePack.MessagePackSerializer.Serialize(x))).ToArray();
        }

        public static void RestoreUnticked(ChaFileClothes loaded, ChaFileClothes.PartsInfo[] before, ClothesPart ticked)
        {
            foreach (var slot in UntickedSlots(ticked))
                if (slot < loaded.parts.Length && slot < before.Length) loaded.parts[slot] = before[slot];
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

        // Shape indices are the ones each maker menu (CvsB_Shape*) edits in ChaFileBody.shapeValueBody
        private static readonly Part<BodyPart>[] BodyParts =
        {
            new Part<BodyPart> { Flag = BodyPart.Overall, Name = "Overall", MakerCategories = new[] { "All" }, ShapeIndices = new[] { 0, 9 } },
            new Part<BodyPart> { Flag = BodyPart.Breast, Name = "Breast", MakerCategories = new[] { "Breast" }, ShapeIndices = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 32 },
                Properties = new[] { "bustSoftness", "bustWeight", "areolaSize" } },
            new Part<BodyPart> { Flag = BodyPart.Upper, Name = "Upper body", MakerCategories = new[] { "Upper" }, ShapeIndices = Range(10, 17) },
            new Part<BodyPart> { Flag = BodyPart.Lower, Name = "Lower body", MakerCategories = new[] { "Lower" }, ShapeIndices = Range(18, 24) },
            new Part<BodyPart> { Flag = BodyPart.Arm, Name = "Arms", MakerCategories = new[] { "Arm" }, ShapeIndices = Range(29, 31) },
            new Part<BodyPart> { Flag = BodyPart.Leg, Name = "Legs", MakerCategories = new[] { "Leg" }, ShapeIndices = Range(25, 28) },
            new Part<BodyPart> { Flag = BodyPart.Skin, Name = "Skin type", MakerCategories = new[] { "SkinType" },
                Properties = new[] { "skinId", "detailId", "detailPower", "skinColor", "skinGlossPower", "skinMetallicPower" } },
            new Part<BodyPart> { Flag = BodyPart.Tan, Name = "Suntan", MakerCategories = new[] { "Sunburn" }, Properties = new[] { "sunburnId", "sunburnColor" } },
            new Part<BodyPart> { Flag = BodyPart.Nipples, Name = "Nipples", MakerCategories = new[] { "Nip" }, Properties = new[] { "nipId", "nipColor", "nipGlossPower" } },
            new Part<BodyPart> { Flag = BodyPart.Underhair, Name = "Pubic hair", MakerCategories = new[] { "Underhair" }, Properties = new[] { "underhairId", "underhairColor" } },
            new Part<BodyPart> { Flag = BodyPart.Nails, Name = "Nails", MakerCategories = new[] { "Nails" }, Properties = new[] { "nailColor", "nailGlossPower" } },
            new Part<BodyPart> { Flag = BodyPart.Paint, Name = "Paint", MakerCategories = new[] { "Paint01", "Paint02" }, Properties = new[] { "paintInfo" } },
            new Part<BodyPart> { Flag = BodyPart.OtherBones, Name = "Other bonemod (advanced window)", BonesOnly = true }
        };

        // Shape indices are the ones each maker menu (CvsF_Shape*) edits in ChaFileFace.shapeValueFace
        private static readonly Part<FacePart>[] FaceParts =
        {
            new Part<FacePart> { Flag = FacePart.FaceType, Name = "Face type", MakerCategories = new[] { "FaceType" }, Properties = new[] { "headId", "skinId", "detailId", "detailPower" } },
            new Part<FacePart> { Flag = FacePart.Overall, Name = "Overall", MakerCategories = new[] { "All" }, ShapeIndices = Range(0, 4) },
            new Part<FacePart> { Flag = FacePart.Chin, Name = "Jaw", MakerCategories = new[] { "Chin" }, ShapeIndices = Range(5, 12) },
            new Part<FacePart> { Flag = FacePart.Cheek, Name = "Cheeks", MakerCategories = new[] { "Cheek" }, ShapeIndices = Range(13, 18) },
            new Part<FacePart> { Flag = FacePart.EyebrowShape, Name = "Eyebrow shape", MakerCategories = new[] { "Eyebrow" }, Properties = new[] { "eyebrowLayout", "eyebrowTilt" } },
            new Part<FacePart> { Flag = FacePart.EyeShape, Name = "Eye shape", MakerCategories = new[] { "Eyes" }, ShapeIndices = Range(19, 31) },
            new Part<FacePart> { Flag = FacePart.Nose, Name = "Nose", MakerCategories = new[] { "Nose" }, ShapeIndices = Range(32, 46) },
            new Part<FacePart> { Flag = FacePart.Mouth, Name = "Mouth", MakerCategories = new[] { "Mouth" }, ShapeIndices = Range(47, 53) },
            new Part<FacePart> { Flag = FacePart.Ear, Name = "Ears", MakerCategories = new[] { "Ear" }, ShapeIndices = Range(54, 58) },
            new Part<FacePart> { Flag = FacePart.Mole, Name = "Mole", MakerCategories = new[] { "Mole" }, Properties = new[] { "moleId", "moleColor", "moleLayout" } },
            new Part<FacePart> { Flag = FacePart.Eyes, Name = "Eyes (iris, pupil)", MakerCategories = new[] { "EyeL", "EyeR" }, Properties = new[] { "pupil", "pupilSameSetting" } },
            new Part<FacePart> { Flag = FacePart.EyeDetails, Name = "Eyes (other)", MakerCategories = new[] { "Etc" }, Properties = new[] { "pupilY", "whiteShadowScale" } },
            new Part<FacePart> { Flag = FacePart.Highlights, Name = "Eye highlights", MakerCategories = new[] { "HL" }, Properties = new[] { "hlId", "hlColor", "hlLayout", "hlTilt" } },
            new Part<FacePart> { Flag = FacePart.Eyebrows, Name = "Eyebrows", Properties = new[] { "eyebrowId", "eyebrowColor" } },
            new Part<FacePart> { Flag = FacePart.Eyelashes, Name = "Eyelashes", MakerCategories = new[] { "Eyelashes" }, Properties = new[] { "eyelashesId", "eyelashesColor" } },
            new Part<FacePart> { Flag = FacePart.Eyeshadow, Name = "Eyeshadow", MakerCategories = new[] { "Eyeshadow" },
                Properties = new[] { "makeup.eyeshadowId", "makeup.eyeshadowColor", "makeup.eyeshadowGloss" } },
            new Part<FacePart> { Flag = FacePart.Blush, Name = "Blush", Properties = new[] { "makeup.cheekId", "makeup.cheekColor", "makeup.cheekGloss" } },
            new Part<FacePart> { Flag = FacePart.Lipstick, Name = "Lipstick", MakerCategories = new[] { "Lip" }, Properties = new[] { "makeup.lipId", "makeup.lipColor", "makeup.lipGloss" } },
            new Part<FacePart> { Flag = FacePart.Paint, Name = "Face paint", MakerCategories = new[] { "Paint01", "Paint02" }, Properties = new[] { "makeup.paintInfo" } },
            new Part<FacePart> { Flag = FacePart.Beard, Name = "Beard", Properties = new[] { "beardId", "beardColor" } },
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
                    // e.g. makeup.lipColor: copy the value inside the makeup objects, not the whole makeup
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
