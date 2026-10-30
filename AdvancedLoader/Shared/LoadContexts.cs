using System.Collections.Generic;
using ExtensibleSaveFormat;
#if HS2 || AI
using AIChara;
#endif

namespace AdvancedLoader
{
    internal sealed class AccessoryLoadContext
    {
        public ChaControl Target;
        public AccessoryMode Mode;
        public AccessoryPlacement Placement;
        public int Shift;
        public IEnumerable<int> SelectedSlots;
        public List<ChaFileAccessory.PartsInfo> CardSlots;

        public List<ChaFileAccessory.PartsInfo> Result;

        public Dictionary<int, int> TargetToSource;
    }

    internal sealed class PartsLoadContext
    {
        public ChaFileControl Target;
        public ChaFileCoordinate CoordinateTarget;
        public BodyPart BodyParts = BodyPart.All;
        public FacePart FaceParts = FacePart.All;
        public ClothesPart ClothesParts = ClothesPart.All;
        public Dictionary<string, PluginData> KeptPluginData;
    }

    // Studio's Change loads the whole card; this turns it into the maker's limited load
    internal sealed class LimitedLoadContext
    {
        public ChaFileControl Target;
        public ChaControl Character;
        public byte Sex;
        public bool Face;
        public bool Body;
        public bool Hair;
        public bool Parameter;
        public bool Coordinate;
    }
}
