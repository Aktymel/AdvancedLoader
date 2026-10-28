using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Manager;

namespace AdvancedLoader
{
    internal static partial class AccessoryData
    {
        // protected ChaFile.LoadFile(string path, bool noLoadPNG, bool noLoadStatus)
        private static readonly MethodInfo ChaFileLoadFile = AccessTools.Method(typeof(ChaFile), "LoadFile", new[] { typeof(string), typeof(bool), typeof(bool) });

        /// <summary>The coordinate a clothes card is read into.</summary>
        public static ChaFileCoordinate CardCoordinate(ChaFileControl temp) => temp.coordinate[0];

        public static string GetName(ChaFileAccessory.PartsInfo part)
        {
#if KK
            var lists = Singleton<Character>.Instance.chaListCtrl;
#else
            var lists = Character.chaListCtrl;
#endif
            var info = lists.GetListInfo((ChaListDefine.CategoryNo)part.type, part.id);
            return info != null ? info.Name : $"Unknown accessory ({part.type}/{part.id})";
        }

        /// <summary>
        /// Deep copy of all accessory slots of an outfit. In Koikatsu Sunshine MoreAccessories keeps slots 21+ in the
        /// same array, so the owner is not needed.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> ReadSlots(ChaFileCoordinate coordinate, ChaFile owner = null)
        {
            return coordinate.accessory.parts.Select(Clone).ToList();
        }

        public static List<bool> ReadOccupancy(ChaFileCoordinate coordinate, ChaFile owner)
        {
            return coordinate.accessory.parts.Select(x => !IsEmpty(x)).ToList();
        }

        /// <summary>
        /// Write all slots into an outfit. Without MoreAccessories only the first 20 fit; empty slots past 20 are dropped.
        /// Call <see cref="MoreAccessoriesBridge.Sync"/> afterwards for the outfit that is worn.
        /// </summary>
        public static void Write(ChaFileCoordinate coordinate, List<ChaFileAccessory.PartsInfo> slots)
        {
            var count = MoreAccessoriesBridge.Available ? Math.Max(slots.Count, VanillaSlotCount) : VanillaSlotCount;
            while (count > VanillaSlotCount && (count > slots.Count || IsEmpty(slots[count - 1]))) count--;
            coordinate.accessory.parts = Enumerable.Range(0, count)
                .Select(i => i < slots.Count ? Clone(slots[i]) : new ChaFileAccessory.PartsInfo())
                .ToArray();
        }

        /// <summary>
        /// Load a coordinate (clothes) card and return a deep copy of its accessory slots, or null on failure.
        /// </summary>
        /// <param name="inspect">Called with the loaded temporary file while its extended data is still available.</param>
        public static List<ChaFileAccessory.PartsInfo> ReadCoordinateCard(string fullPath, Action<ChaFileControl> inspect = null)
        {
            Hooks.ReadingCard = true;
            var temp = new ChaFileControl();
            try
            {
                if (!CardCoordinate(temp).LoadFile(fullPath)) return null;
                inspect?.Invoke(temp);
                return ReadSlots(CardCoordinate(temp));
            }
            finally
            {
                Hooks.ReadingCard = false;
            }
        }

        /// <summary>
        /// Load a character card into a temporary ChaFile and return a deep copy of the accessory slots of its first outfit,
        /// or null on failure. The other outfits are read through <paramref name="inspect"/>.
        /// </summary>
        public static List<ChaFileAccessory.PartsInfo> ReadCard(string fullPath, byte sex, Action<ChaFileControl> inspect = null)
        {
#if KKS
            var prevIsMod = Character.isMod;
#endif
            var temp = new ChaFileControl();
            try
            {
                // Same read as LoadFileLimited. LoadCharaFile is avoided because other plugins
                // treat it as "this card is now the maker character".
                var path = temp.ConvertCharaFilePath(fullPath, sex);
                var loaded = (bool)ChaFileLoadFile.Invoke(temp, new object[] { path, true, true });
                if (!loaded) return null;
                inspect?.Invoke(temp);
                return ReadSlots(temp.coordinate[0]);
            }
            finally
            {
#if KKS
                // Loading a card overwrites the "modded card" flag of the character being edited
                Character.isMod = prevIsMod;
#endif
            }
        }
    }
}
