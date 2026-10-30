using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedLoader
{
    internal enum LoadMode
    {
        Character,
        Coordinate
    }

    internal enum AccessoryMode
    {
        Keep,
        All,
        Selected
    }

    internal enum AccessoryPlacement
    {
        SameSlots,
        Shift,
        FreeSlots
    }

    [Flags]
    internal enum MaterialGroup
    {
        None = 0,
        Body = 1,
        Head = 2,
        Eyes = 4,
        Hair = 8,
        Clothes = 16
    }

    internal sealed class LoadOptions
    {
        public bool Face;
        public bool Body;
        public bool Hair;
        public bool Parameters;
        public bool Clothes;

        public BodyPart BodyParts = BodyPart.All;
        public FacePart FaceParts = FacePart.All;
        public ClothesPart ClothesParts = ClothesPart.All;

        public AccessoryMode Accessories = AccessoryMode.Keep;
        public readonly HashSet<int> SelectedSlots = new HashSet<int>();
        public AccessoryPlacement Placement = AccessoryPlacement.SameSlots;
        public int Shift = 10;

#if KKS || KK
        public OutfitMode OutfitSelection = OutfitMode.All;
        public readonly HashSet<int> SelectedOutfits = new HashSet<int>();
        public OutfitPlacement OutfitPlacement = OutfitPlacement.SameSlots;
        public int OutfitShift = 4;
#endif

        public bool ReplacesAllAccessories => Accessories == AccessoryMode.All && Placement == AccessoryPlacement.SameSlots;

        public MaterialGroup MaterialGroups;
        public bool MaterialCleanCopy = true;
        public bool MaterialFollowAccessories = true;

        // Missing = off
        public readonly Dictionary<string, bool> OtherPlugins = new Dictionary<string, bool>();

        public static LoadOptions Default(LoadMode mode) => mode == LoadMode.Character
            ? new LoadOptions { Face = true, Body = true, Hair = true, Clothes = true, Accessories = AccessoryMode.All }
            : new LoadOptions { Clothes = true, Accessories = AccessoryMode.All };

        // "Name=value;" pairs. Selected slots and outfits belong to one card, so they are not saved
        public string Save()
        {
            var values = new Dictionary<string, object>
            {
                { "Face", Face }, { "Body", Body }, { "Hair", Hair }, { "Parameters", Parameters }, { "Clothes", Clothes },
                { "FaceParts", (int)FaceParts }, { "BodyParts", (int)BodyParts }, { "ClothesParts", (int)ClothesParts },
                { "Accessories", (int)Accessories }, { "Placement", (int)Placement }, { "Shift", Shift },
#if KKS || KK
                { "OutfitSelection", (int)OutfitSelection }, { "OutfitPlacement", (int)OutfitPlacement }, { "OutfitShift", OutfitShift },
#endif
                { "MaterialGroups", (int)MaterialGroups }, { "MaterialCleanCopy", MaterialCleanCopy }, { "MaterialFollowAccessories", MaterialFollowAccessories },
                { "Plugins", string.Join("|", OtherPlugins.Where(x => x.Value).Select(x => x.Key).ToArray()) }
            };
            return string.Join(";", values.Select(x => x.Key + "=" + x.Value).ToArray());
        }

        public void Load(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var values = new Dictionary<string, string>();
            foreach (var pair in text.Split(';'))
            {
                var i = pair.IndexOf('=');
                if (i > 0) values[pair.Substring(0, i)] = pair.Substring(i + 1);
            }

            Face = Read(values, "Face", Face);
            Body = Read(values, "Body", Body);
            Hair = Read(values, "Hair", Hair);
            Parameters = Read(values, "Parameters", Parameters);
            Clothes = Read(values, "Clothes", Clothes);
            FaceParts = (FacePart)Read(values, "FaceParts", (int)FaceParts);
            BodyParts = (BodyPart)Read(values, "BodyParts", (int)BodyParts);
            ClothesParts = (ClothesPart)Read(values, "ClothesParts", (int)ClothesParts);
            Accessories = (AccessoryMode)Read(values, "Accessories", (int)Accessories);
            Placement = (AccessoryPlacement)Read(values, "Placement", (int)Placement);
            Shift = Read(values, "Shift", Shift);
#if KKS || KK
            OutfitSelection = (OutfitMode)Read(values, "OutfitSelection", (int)OutfitSelection);
            OutfitPlacement = (OutfitPlacement)Read(values, "OutfitPlacement", (int)OutfitPlacement);
            OutfitShift = Read(values, "OutfitShift", OutfitShift);
#endif
            MaterialGroups = (MaterialGroup)Read(values, "MaterialGroups", (int)MaterialGroups);
            MaterialCleanCopy = Read(values, "MaterialCleanCopy", MaterialCleanCopy);
            MaterialFollowAccessories = Read(values, "MaterialFollowAccessories", MaterialFollowAccessories);
            if (values.TryGetValue("Plugins", out var plugins))
            {
                OtherPlugins.Clear();
                foreach (var id in plugins.Split('|'))
                    if (id.Length > 0) OtherPlugins[id] = true;
            }
        }

        private static bool Read(Dictionary<string, string> values, string name, bool fallback) =>
            values.TryGetValue(name, out var text) && bool.TryParse(text, out var value) ? value : fallback;

        private static int Read(Dictionary<string, string> values, string name, int fallback) =>
            values.TryGetValue(name, out var text) && int.TryParse(text, out var value) ? value : fallback;
    }
}
