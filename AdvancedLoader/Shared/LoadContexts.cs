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
}
