using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
#if HS2 || AI
using AIChara;
#elif KKS || KK
#endif

namespace AdvancedLoader
{
    internal static partial class AccessoryData
    {
        public const int VanillaSlotCount = 20;

        public static bool IsEmpty(ChaFileAccessory.PartsInfo part)
        {
            // 120 is the PartsInfo default, 350 is ChaListDefine.CategoryNo.ao_none
            return part == null || part.type == 120 || part.type == (int)ChaListDefine.CategoryNo.ao_none;
        }

        public static ChaFileAccessory.PartsInfo Clone(ChaFileAccessory.PartsInfo part)
        {
            if (part == null) return new ChaFileAccessory.PartsInfo();
            var copy = MessagePackSerializer.Deserialize<ChaFileAccessory.PartsInfo>(MessagePackSerializer.Serialize(part));
            copy.partsOfHead = part.partsOfHead;
            return copy;
        }

        public static Dictionary<int, int> MapTargets(AccessoryPlacement placement, int shift, IList<bool> occupied, IEnumerable<int> sourceSlots)
        {
            var map = new Dictionary<int, int>();
            var used = new HashSet<int>();
            var nextFree = 0;
            foreach (var source in sourceSlots)
            {
                int target;
                switch (placement)
                {
                    case AccessoryPlacement.Shift:
                        target = source + shift;
                        break;
                    case AccessoryPlacement.FreeSlots:
                        while ((nextFree < occupied.Count && occupied[nextFree]) || used.Contains(nextFree)) nextFree++;
                        target = nextFree;
                        break;
                    default:
                        target = source;
                        break;
                }
                if (target < 0 || (target >= VanillaSlotCount && !MoreAccessoriesBridge.Available)) continue;
                used.Add(target);
                map[source] = target;
            }
            return map;
        }

        public static IEnumerable<int> SourceSlots(AccessoryMode mode, List<ChaFileAccessory.PartsInfo> card, IEnumerable<int> selectedSlots)
        {
            switch (mode)
            {
                case AccessoryMode.All:
                    return Enumerable.Range(0, card.Count).Where(i => !IsEmpty(card[i]));
                case AccessoryMode.Selected:
                    return selectedSlots.OrderBy(x => x);
                default:
                    return Enumerable.Empty<int>();
            }
        }

        public static List<ChaFileAccessory.PartsInfo> Combine(AccessoryMode mode, AccessoryPlacement placement, int shift,
            List<ChaFileAccessory.PartsInfo> current, List<ChaFileAccessory.PartsInfo> card, IEnumerable<int> selectedSlots,
            out Dictionary<int, int> targetToSource)
        {
            targetToSource = new Dictionary<int, int>();
            if (mode == AccessoryMode.Keep) return current.Select(Clone).ToList();

            if (mode == AccessoryMode.All && placement == AccessoryPlacement.SameSlots)
            {
                for (var i = 0; i < card.Count; i++) targetToSource[i] = i;
                return card.Select(Clone).ToList();
            }

            var result = current.Select(Clone).ToList();
            var occupied = current.Select(x => !IsEmpty(x)).ToList();
            foreach (var pair in MapTargets(placement, shift, occupied, SourceSlots(mode, card, selectedSlots)))
            {
                while (result.Count <= pair.Value) result.Add(new ChaFileAccessory.PartsInfo());
                result[pair.Value] = pair.Key < card.Count ? Clone(card[pair.Key]) : new ChaFileAccessory.PartsInfo();
                targetToSource[pair.Value] = pair.Key;
            }
            return result;
        }

    }
}
