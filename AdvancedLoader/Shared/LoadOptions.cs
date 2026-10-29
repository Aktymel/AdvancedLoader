using System;
using System.Collections.Generic;

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
    }
}
