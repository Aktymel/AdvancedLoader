using System.Collections.Generic;
#if HS2
using AIChara;
#endif

namespace AdvancedLoader
{
    /// <summary>
    /// Accessory slot handling for a load started from the Advanced Load window.
    /// AdvancedLoader sets <see cref="Active"/> around the game's own load code and clears it afterwards,
    /// so loads from the game's original windows are never touched.
    /// </summary>
    internal sealed class AccessoryLoadContext
    {
        public ChaControl Target;
        public AccessoryMode Mode;
        public AccessoryPlacement Placement;
        public int Shift;
        public IEnumerable<int> SelectedSlots;
        public List<ChaFileAccessory.PartsInfo> CardSlots;

        /// <summary>Final slot list written to the character, available after the load for Material Editor handling.</summary>
        public List<ChaFileAccessory.PartsInfo> Result;

        /// <summary>Character slot -> card slot for every accessory taken from the card.</summary>
        public Dictionary<int, int> TargetToSource;
    }

    /// <summary>
    /// Body and face parts to load from the card, for a "Load character" started from the Advanced Load window.
    /// </summary>
    internal sealed class PartsLoadContext
    {
        public ChaFileControl Target;
        /// <summary>Set for "Clothes > Load", where the game loads into the character's current coordinate.</summary>
        public ChaFileCoordinate CoordinateTarget;
        public BodyPart BodyParts = BodyPart.All;
        public FacePart FaceParts = FacePart.All;
        public ClothesPart ClothesParts = ClothesPart.All;
    }
}
