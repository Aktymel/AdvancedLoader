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

        // blocks: card groups loaded as a group; in free slots each goes whole into the first run of free slots it fits in
        public static Dictionary<int, int> MapTargets(AccessoryPlacement placement, int shift, IList<bool> occupied, IEnumerable<int> sourceSlots,
            IEnumerable<IList<int>> blocks = null)
        {
            var map = new Dictionary<int, int>();
            var used = new HashSet<int>();
            bool Free(int slot) => !(slot < occupied.Count && occupied[slot]) && !used.Contains(slot);
            void Place(int source, int target)
            {
                if (target < 0 || (target >= VanillaSlotCount && !MoreAccessoriesBridge.Available)) return;
                used.Add(target);
                map[source] = target;
            }

            var blockOf = new Dictionary<int, IList<int>>();
            if (placement == AccessoryPlacement.FreeSlots && blocks != null)
                foreach (var block in blocks)
                    foreach (var slot in block) blockOf[slot] = block;
            var placedBlocks = new HashSet<IList<int>>();

            var nextFree = 0;
            foreach (var source in sourceSlots)
            {
                if (blockOf.TryGetValue(source, out var group))
                {
                    if (!placedBlocks.Add(group)) continue;
                    var start = 0;
                    while (!Enumerable.Range(start, group.Count).All(Free)) start++;
                    for (var i = 0; i < group.Count; i++) Place(group[i], start + i);
                    continue;
                }

                int target;
                switch (placement)
                {
                    case AccessoryPlacement.Shift:
                        target = source + shift;
                        break;
                    case AccessoryPlacement.FreeSlots:
                        while (!Free(nextFree)) nextFree++;
                        target = nextFree;
                        break;
                    default:
                        target = source;
                        break;
                }
                Place(source, target);
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
            out Dictionary<int, int> targetToSource, string groups = null, IEnumerable<IList<int>> blocks = null)
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
            AccessoryGroups.Reserve(occupied, groups);
            foreach (var pair in MapTargets(placement, shift, occupied, SourceSlots(mode, card, selectedSlots), blocks))
            {
                while (result.Count <= pair.Value) result.Add(new ChaFileAccessory.PartsInfo());
                result[pair.Value] = pair.Key < card.Count ? Clone(card[pair.Key]) : new ChaFileAccessory.PartsInfo();
                targetToSource[pair.Value] = pair.Key;
            }
            return result;
        }

    }
}
