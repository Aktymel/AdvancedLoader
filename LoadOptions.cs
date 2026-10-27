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
        /// <summary>Keep all accessories of the current character.</summary>
        Keep,
        /// <summary>Take all accessory slots from the card.</summary>
        All,
        /// <summary>Take only the ticked slots from the card, into the same slot numbers.</summary>
        Selected
    }

    internal enum AccessoryPlacement
    {
        /// <summary>Card slot N goes to slot N.</summary>
        SameSlots,
        /// <summary>Card slot N goes to slot N + shift.</summary>
        Shift,
        /// <summary>Loaded accessories fill the character's empty slots in order.</summary>
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

    /// <summary>
    /// What the Advanced Load window should take from the selected card. One instance per mode.
    /// </summary>
    internal sealed class LoadOptions
    {
        public bool Face;
        public bool Body;
        public bool Hair;
        public bool Parameters;
        public bool Clothes;

        /// <summary>Parts of Body and Face taken from the card when those are ticked.</summary>
        public BodyPart BodyParts = BodyPart.All;
        public FacePart FaceParts = FacePart.All;
        /// <summary>Clothes pieces taken from the card when Clothes is ticked.</summary>
        public ClothesPart ClothesParts = ClothesPart.All;

        public AccessoryMode Accessories = AccessoryMode.Keep;
        public readonly HashSet<int> SelectedSlots = new HashSet<int>();
        public AccessoryPlacement Placement = AccessoryPlacement.SameSlots;
        public int Shift = 10;

        /// <summary>
        /// "All from card" into the same slots replaces every accessory, which the game's own load already does.
        /// Any other placement keeps the current accessories and adds the card's.
        /// </summary>
        public bool ReplacesAllAccessories => Accessories == AccessoryMode.All && Placement == AccessoryPlacement.SameSlots;

        public MaterialGroup MaterialGroups;
        /// <summary>Reset every Material Editor change of a group before copying the card's changes.</summary>
        public bool MaterialCleanCopy = true;
        /// <summary>Copy Material Editor data together with the accessory slots that are loaded or kept.</summary>
        public bool MaterialFollowAccessories = true;

        /// <summary>
        /// Values for other plugins' load toggles (by their text), used only for this window's loads.
        /// Missing means off, so nothing is loaded from the card that was not asked for.
        /// </summary>
        public readonly Dictionary<string, bool> PluginToggles = new Dictionary<string, bool>();
    }
}
