using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIChara;
using HarmonyLib;
using Manager;
using MessagePack;

namespace AdvancedLoader
{
    /// <summary>
    /// Reads and writes a character's full accessory slot list: 20 vanilla slots followed by MoreAccessories slots.
    /// </summary>
    internal static class AccessoryData
    {
        public const int VanillaSlotCount = 20;

        // protected ChaFile.LoadFile(string path, int lang, bool noLoadPNG, bool noLoadStatus)
        private static readonly MethodInfo ChaFileLoadFile = AccessTools.Method(typeof(ChaFile), "LoadFile", new[] { typeof(string), typeof(int), typeof(bool), typeof(bool) });

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

        public static string GetName(ChaFileAccessory.PartsInfo part)
        {
            var info = Singleton<Character>.Instance.chaListCtrl.GetListInfo((ChaListDefine.CategoryNo)part.type, part.id);
            return info != null ? info.Name : $"Unknown accessory ({part.type}/{part.id})";
        }

        /// <summary>
        /// Deep copy of all accessory slots of a character.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> ReadCharacter(ChaFile file)
        {
            return ReadSlots(file.coordinate, file);
        }

        /// <summary>
        /// Deep copy of vanilla slots from the given coordinate and MoreAccessories slots of its owner.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> ReadSlots(ChaFileCoordinate coordinate, ChaFile owner)
        {
            var result = coordinate.accessory.parts.Select(Clone).ToList();
            var extra = MoreAccessoriesBridge.GetParts(owner);
            if (extra != null) result.AddRange(extra.Select(Clone));
            return result;
        }

        /// <summary>
        /// Load a coordinate (clothes) card and return a deep copy of its accessory slots, or null on failure.
        /// The coordinate is loaded into a temporary ChaFile so MoreAccessories and Sideloader know whose coordinate it is.
        /// </summary>
        /// <param name="inspect">Called with the loaded temporary file while its extended data is still available.</param>
        public static List<ChaFileAccessory.PartsInfo> ReadCoordinateCard(string fullPath, Action<ChaFileControl> inspect = null)
        {
            var prevLoadAdditional = MoreAccessoriesBridge.LoadAdditionalAccessories;
            MoreAccessoriesBridge.LoadAdditionalAccessories = true;
            Hooks.ReadingCard = true;

            var temp = new ChaFileControl();
            try
            {
                if (!temp.coordinate.LoadFile(fullPath)) return null;
                inspect?.Invoke(temp);
                return ReadCharacter(temp);
            }
            finally
            {
                Hooks.ReadingCard = false;
                MoreAccessoriesBridge.LoadAdditionalAccessories = prevLoadAdditional;
                MoreAccessoriesBridge.Forget(temp);
            }
        }

        /// <summary>
        /// Load a character card into a temporary ChaFile and return a deep copy of its accessory slots, or null on failure.
        /// </summary>
        /// <param name="inspect">Called with the loaded temporary file while its extended data is still available.</param>
        public static List<ChaFileAccessory.PartsInfo> ReadCard(string fullPath, byte sex, Action<ChaFileControl> inspect = null)
        {
            var character = Singleton<Character>.Instance;
            var prevIsMod = character.isMod;
            var prevLoadAdditional = MoreAccessoriesBridge.LoadAdditionalAccessories;
            MoreAccessoriesBridge.LoadAdditionalAccessories = true;

            var temp = new ChaFileControl();
            try
            {
                // Same read as LoadFileLimited. LoadCharaFile is avoided because other plugins
                // treat it as "this card is now the maker character".
                var path = temp.ConvertCharaFilePath(fullPath, sex);
                var loaded = (bool)ChaFileLoadFile.Invoke(temp, new object[] { path, (int)Singleton<GameSystem>.Instance.language, true, true });
                if (!loaded) return null;
                inspect?.Invoke(temp);
                return ReadCharacter(temp);
            }
            finally
            {
                MoreAccessoriesBridge.LoadAdditionalAccessories = prevLoadAdditional;
                MoreAccessoriesBridge.Forget(temp);
                // LoadCharaFile overwrites the "modded card" flag of the character being edited
                character.isMod = prevIsMod;
            }
        }

        /// <summary>
        /// Which character slots are taken, without copying the accessory data.
        /// </summary>
        public static List<bool> ReadOccupancy(ChaFileCoordinate coordinate, ChaFile owner)
        {
            var result = coordinate.accessory.parts.Select(x => !IsEmpty(x)).ToList();
            var extra = MoreAccessoriesBridge.GetParts(owner);
            if (extra != null) result.AddRange(extra.Select(x => !IsEmpty(x)));
            return result;
        }

        /// <summary>
        /// Target character slot for each card slot that gets loaded. Slots that cannot exist
        /// (21 and up without MoreAccessories) are left out.
        /// </summary>
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

        /// <summary>
        /// Card slots that a load takes: every non-empty one for "All", the ticked ones for "Selected".
        /// </summary>
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

        /// <summary>
        /// Final accessory slots for a load. "All" into the same slots replaces everything with the card's accessories;
        /// every other combination keeps the current accessories and writes the loaded ones into their target slots.
        /// </summary>
        /// <param name="targetToSource">Which card slot ended up in which character slot.</param>
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

        /// <summary>
        /// Write the full slot list into a character file (vanilla slots into the coordinate, the rest into MoreAccessories).
        /// </summary>
        public static void Write(ChaFile file, List<ChaFileAccessory.PartsInfo> slots)
        {
            WriteVanilla(file.coordinate, slots);
            if (MoreAccessoriesBridge.Available)
                MoreAccessoriesBridge.SetParts(file, slots.Skip(VanillaSlotCount).Select(Clone).ToList());
        }

        public static void WriteVanilla(ChaFileCoordinate coordinate, List<ChaFileAccessory.PartsInfo> slots)
        {
            var parts = coordinate.accessory.parts;
            for (var i = 0; i < parts.Length; i++)
                parts[i] = i < slots.Count ? Clone(slots[i]) : new ChaFileAccessory.PartsInfo();
        }
    }
}
